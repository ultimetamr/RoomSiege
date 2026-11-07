using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoomSiege.UI
{
    // Actual UI dispatch evidence for the local device test report.
    public sealed class WorldUiEventProbe : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerClickHandler
    {
        private void Awake()
        {
            var button=GetComponent<Button>();
            if(button!=null)button.onClick.AddListener(()=>Debug.Log("[RoomSiege.Input] button_invoked="+name));
        }
        public void OnPointerDown(PointerEventData data)=>Debug.Log($"[RoomSiege.Input] ui_down={name} pointer={data.pointerId}");
        public void OnPointerUp(PointerEventData data)=>Debug.Log($"[RoomSiege.Input] ui_up={name} pointer={data.pointerId} eligible={data.eligibleForClick} dragging={data.dragging} hit={data.pointerCurrentRaycast.gameObject?.name}");
        public void OnPointerClick(PointerEventData data)=>Debug.Log($"[RoomSiege.Input] ui_click={name} pointer={data.pointerId}");
    }
}
