using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoomSiege.Audio
{
    public sealed class UiAudioFeedback : MonoBehaviour, IPointerEnterHandler, IPointerDownHandler
    {
        private AudioDirector owner;
        private Button button;
        internal void Initialize(AudioDirector director, Button target) { owner = director; button = target; }
        public void OnPointerEnter(PointerEventData data) { if (owner != null) owner.UiHover(button); }
        public void OnPointerDown(PointerEventData data)
        {
            if (owner != null && button != null && !button.interactable) owner.UiUnavailable(button);
        }
    }
}
