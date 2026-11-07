using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.UI
{
    public sealed class PanelDragHandle : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler, IScrollHandler
    {
        public WorldPanelFollower Follower { get; set; }
        private float rayDistance;
        private Vector3 offset;
        private bool dragging;
        public void OnBeginDrag(PointerEventData data)
        {
            if (Follower == null || !TryRay(data, out var ray)) return;
            Vector3 hit = data.pointerPressRaycast.worldPosition;
            if (!WorldPanelMotion.Valid(hit) || (hit-ray.origin).sqrMagnitude < 0.001f) hit = transform.position;
            rayDistance = Mathf.Clamp(Vector3.Distance(ray.origin, hit), 0.1f, 5f);
            offset = Follower.transform.position - ray.GetPoint(rayDistance);
            dragging = true;
            OnDrag(data);
        }
        public void OnDrag(PointerEventData data)
        {
            if (dragging && Follower != null && TryRay(data, out var ray))
                Follower.Manipulate(ray.GetPoint(rayDistance) + offset);
        }
        public void OnEndDrag(PointerEventData data)
        { dragging = false; if (Follower != null) Follower.EndManipulation(); }
        public void OnScroll(PointerEventData data)
        {
            if (Follower == null || Follower.Head == null) return;
            float current = Vector3.Distance(Follower.Head.transform.position, Follower.transform.position);
            Follower.MoveDistance(current - data.scrollDelta.y * 0.15f);
        }
        private bool TryRay(PointerEventData data, out Ray ray)
        {
            if (data is TrackedDeviceEventData tracked && tracked.rayPoints != null && tracked.rayPoints.Count >= 2)
            {
                Vector3 origin = tracked.rayPoints[0];
                Vector3 direction = tracked.rayPoints[1] - origin;
                if (WorldPanelMotion.Valid(origin) && direction.sqrMagnitude > 0.0001f)
                { ray = new Ray(origin, direction.normalized); return true; }
            }
            if (Follower != null && Follower.Head != null)
            { ray = Follower.Head.ScreenPointToRay(data.position); return true; }
            ray = default; return false;
        }
        private void OnDisable() { dragging = false; if (Follower != null) Follower.EndManipulation(); }
    }
}
