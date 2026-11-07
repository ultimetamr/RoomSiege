using UnityEngine;

namespace RoomSiege.UI
{
    [DefaultExecutionOrder(10000)]
    public sealed class WorldPanelFollower : MonoBehaviour
    {
        public Camera Head { get; private set; }
        public WorldPanelMotion Motion { get; } = new WorldPanelMotion();
        public bool Paused { get; private set; }
        public bool IsPaused => Paused || applicationPaused || Time.timeScale <= 0.0001f;
        public CanvasGroup Group { get; private set; }
        public bool IsOccluded { get; private set; }
        private bool applicationPaused;
        private Canvas[] panels;
        private Vector3[] localPositions;
        private Vector2[] panelSizes;
        private readonly RaycastHit[] hits = new RaycastHit[32];
        private readonly Vector3[] corners = new Vector3[4];
        private Transform trackingRoot;
        private int loggedResets;

        public void Initialize(Camera camera, CanvasGroup group, Canvas[] canvases)
        {
            Head = camera; Group = group; panels = canvases;
            trackingRoot = camera != null ? camera.transform.root : null;
            if (panels != null)
            {
                localPositions = new Vector3[panels.Length]; panelSizes = new Vector2[panels.Length];
                for(int i=0;i<panels.Length;i++)
                {
                    localPositions[i] = panels[i].transform.localPosition;
                    panelSizes[i] = ((RectTransform)panels[i].transform).sizeDelta;
                }
            }
            Tick(0f);
        }
        public void SetPaused(bool value) { Paused = value; }
        public void ResetToDefault() => Motion.RequestReset();
        public void EndManipulation() => Motion.EndInteraction();
        public void Manipulate(Vector3 position) => Motion.SetInteractionTarget(position);
        public void MoveDistance(float metres)
        {
            if (Head == null) return;
            Vector3 direction = (transform.position - Head.transform.position).normalized;
            if (direction.sqrMagnitude < 0.5f) direction = WorldPanelMotion.DefaultHeading(new Pose(Head.transform.position, Head.transform.rotation)) * Vector3.forward;
            Motion.SetInteractionTarget(Head.transform.position + direction * Mathf.Clamp(metres, 0.45f, 3f));
        }

        private void LateUpdate() => Tick(Time.unscaledDeltaTime);
        public void Tick(float unscaledDeltaTime)
        {
            if (Head == null) Head = Camera.main;
            if (Head == null) return;
            if (panels != null)
                for(int i=0;i<panels.Length;i++)
                {
                    var panel=panels[i]; if(panel==null) continue;
                    // The cluster is the manipulation target; individual panels keep a valid fixed layout.
                    panel.transform.localPosition=localPositions[i];
                    panel.transform.localRotation=Quaternion.identity;
                    panel.transform.localScale=Vector3.one*0.001f;
                    ((RectTransform)panel.transform).sizeDelta=panelSizes[i];
                }
            // No child-of-head relationship: writes occur once in LateUpdate, after tracked pose updates.
            IsOccluded = WorldPanelMotion.Valid(new Pose(transform.position, transform.rotation)) && CheckOcclusion();
            int before = Motion.ResetCount;
            var pose = Motion.Step(new Pose(Head.transform.position, Head.transform.rotation),
                new Pose(transform.position, transform.rotation), unscaledDeltaTime, IsOccluded);
            transform.SetPositionAndRotation(pose.position, pose.rotation);
            transform.localScale = Vector3.one;
            if (panels != null)
                foreach (var panel in panels)
                {
                    if (panel == null) continue;
                    if (!panel.gameObject.activeSelf) panel.gameObject.SetActive(true);
                    panel.enabled = true;
                    panel.renderMode = RenderMode.WorldSpace;
                    panel.worldCamera = Head;
                }
            if (Group != null)
            {
                Group.enabled = true;
                Group.alpha = WorldPanelMotion.Fade(Group.alpha, IsPaused, unscaledDeltaTime);
            }
            if (Motion.ResetCount != before && Motion.ResetCount != loggedResets)
            {
                loggedResets = Motion.ResetCount;
                Debug.Log($"[RoomSiege.UI] Reset {Motion.LastResetReason}; from={Vector3.Distance(Head.transform.position,pose.position):F2}m");
            }
        }

        private bool CheckOcclusion()
        {
            if (panels == null) return false;
            foreach (var panel in panels)
            {
                if (panel == null) continue;
                var rect = (RectTransform)panel.transform;
                if (Blocked(rect.position)) return true;
                rect.GetWorldCorners(corners);
                // Sample the visible area, slightly inside its corners. No Renderer.isVisible/culling races.
                for (int i=0; i<4; i++)
                    if (Blocked(Vector3.Lerp(rect.position, corners[i], 0.85f))) return true;
            }
            return false;
        }
        private bool Blocked(Vector3 target)
        {
            Vector3 origin = Head.transform.position;
            Vector3 delta = target - origin;
            float length = delta.magnitude;
            if (!WorldPanelMotion.Valid(delta) || length < 0.06f) return false;
            int count = Physics.RaycastNonAlloc(origin, delta / length, hits, length - 0.02f,
                Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore);
            for (int i=0; i<count; i++)
            {
                var hit = hits[i].transform;
                if (hit.IsChildOf(transform) || (trackingRoot != null && hit.IsChildOf(trackingRoot))) continue;
                return true;
            }
            return count == hits.Length; // Treat a full buffer conservatively.
        }
        private void OnApplicationPause(bool paused) { applicationPaused = paused; }
    }
}
