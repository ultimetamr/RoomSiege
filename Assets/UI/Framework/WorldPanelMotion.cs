using UnityEngine;

namespace RoomSiege.UI
{
    public enum PanelResetReason { None, Uninitialized, InvalidPose, InvalidHead, Distance, Occlusion, Requested }

    // State and geometry only: no camera parenting, scene queries, UI, timeScale or gameplay mutation.
    public sealed class WorldPanelMotion
    {
        public const float AnchorDistance = 1.2f;
        public const float DownwardPitch = 8f;
        public const float PositionLerp = 0.12f;
        public const float TurnThreshold = 25f;
        public const float MinimumDistance = 0.8f;
        public const float MaximumDistance = 2.5f;
        public const float ReturnDuration = 1f;
        public Pose Current { get; private set; }
        public bool Initialized { get; private set; }
        public bool Returning { get; private set; }
        public bool Turning { get; private set; }
        public PanelResetReason LastResetReason { get; private set; }
        public int ResetCount { get; private set; }
        private Pose lastHead = new Pose(new Vector3(0, 1.6f, 0), Quaternion.identity);
        private Quaternion heading = Quaternion.identity;
        private Pose returnStart;
        private float returnTime;
        private bool wasOccluded;
        private bool requested;
        private bool interacting;
        private Vector3 interactionTarget;

        public void RequestReset() { requested = true; interacting = false; }
        public void SetInteractionTarget(Vector3 worldPosition)
        {
            if (!Valid(worldPosition) || Returning) return;
            interacting = true;
            interactionTarget = worldPosition;
        }
        public void EndInteraction() { interacting = false; }

        public Pose Step(Pose head, Pose observed, float deltaTime, bool occluded)
        {
            float dt = Finite(deltaTime) ? Mathf.Clamp(deltaTime, 0f, 1f) : 0f;
            bool invalidHead = !Valid(head);
            if (invalidHead) head = lastHead;
            else { head.rotation = head.rotation.normalized; lastHead = head; }
            Quaternion desiredHeading = DefaultHeading(head);
            var anchor = new Pose(head.position + desiredHeading * Vector3.forward * AnchorDistance, desiredHeading);
            if (!Initialized)
            {
                Current = anchor; heading = desiredHeading; Initialized = true;
                LastResetReason = PanelResetReason.Uninitialized; ResetCount++;
                wasOccluded = occluded;
                return Current; // Placed before first render, so initialization never animates from world origin.
            }

            bool invalidPose = !Valid(observed);
            if (invalidPose) observed = Current; // Restore the last valid rendered pose, not NaN -> Lerp.
            if (!Valid(observed)) observed = anchor;
            var reason = invalidPose ? PanelResetReason.InvalidPose :
                invalidHead ? PanelResetReason.InvalidHead :
                requested ? PanelResetReason.Requested :
                occluded && !wasOccluded ? PanelResetReason.Occlusion : PanelResetReason.None;
            wasOccluded = occluded;
            requested = false;
            float distance = Vector3.Distance(head.position, observed.position);
            if (reason == PanelResetReason.None && !Returning &&
                (distance < MinimumDistance || distance > MaximumDistance)) reason = PanelResetReason.Distance;

            if (reason != PanelResetReason.None && (!Returning || invalidPose))
            {
                Returning = true; Turning = false; interacting = false;
                returnStart = observed; returnTime = 0f; heading = desiredHeading;
                LastResetReason = reason; ResetCount++;
            }

            if (Returning)
            {
                // A persistent occluder never restarts the timer. Depth-independent UI stays readable meanwhile.
                heading = Quaternion.RotateTowards(heading, desiredHeading, 60f * dt);
                returnTime = Mathf.Min(ReturnDuration, returnTime + dt);
                float t = Mathf.SmoothStep(0f, 1f, returnTime / ReturnDuration);
                Vector3 target = head.position + heading * Vector3.forward * AnchorDistance;
                Current = new Pose(Vector3.Lerp(returnStart.position, target, t),
                    Quaternion.Slerp(returnStart.rotation, heading, t));
                if (returnTime >= ReturnDuration - 0.00001f) Returning = false;
            }
            else
            {
                float angle = Quaternion.Angle(heading, desiredHeading);
                if (angle > TurnThreshold) Turning = true;
                if (Turning)
                {
                    heading = Quaternion.RotateTowards(heading, desiredHeading, 60f * dt);
                    if (Quaternion.Angle(heading, desiredHeading) <= 2f) Turning = false;
                }
                var target = interacting ? interactionTarget : head.position + heading * Vector3.forward * AnchorDistance;
                Current = new Pose(Vector3.Lerp(observed.position, target, PositionLerp),
                    Quaternion.Slerp(observed.rotation, heading, PositionLerp));
            }
            return Current;
        }

        public static Quaternion DefaultHeading(Pose head)
        {
            Vector3 forward = head.rotation * Vector3.forward;
            Vector3 up = Mathf.Abs(Vector3.Dot(forward, Vector3.up)) < 0.99f ? Vector3.up : head.rotation * Vector3.up;
            return Quaternion.LookRotation(forward, up) * Quaternion.Euler(DownwardPitch, 0, 0);
        }
        public static float Fade(float current, bool paused, float unscaledDeltaTime)
        {
            float target = paused ? 0.3f : 1f;
            if (!Finite(current)) current = target;
            float dt = Finite(unscaledDeltaTime) ? Mathf.Max(0f, unscaledDeltaTime) : 0f;
            return Mathf.MoveTowards(current, target, 0.7f / 0.25f * dt);
        }
        public static bool Finite(float value) => !float.IsNaN(value) && !float.IsInfinity(value);
        public static bool Valid(Vector3 p) => Finite(p.x) && Finite(p.y) && Finite(p.z) &&
            Mathf.Abs(p.x) < 1000000f && Mathf.Abs(p.y) < 1000000f && Mathf.Abs(p.z) < 1000000f;
        public static bool Valid(Pose pose)
        {
            var q = pose.rotation;
            float norm = q.x*q.x + q.y*q.y + q.z*q.z + q.w*q.w;
            return Valid(pose.position) && Finite(norm) && norm > 0.0001f;
        }
    }
}
