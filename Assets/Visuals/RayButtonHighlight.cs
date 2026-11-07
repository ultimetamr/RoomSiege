using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace RoomSiege.Visuals
{
    [RequireComponent(typeof(Button))]
    public sealed class RayButtonHighlight : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler,
        IPointerDownHandler, IPointerUpHandler
    {
        private readonly HashSet<int> hovering = new HashSet<int>();
        private readonly HashSet<int> pressing = new HashSet<int>();
        private Button button;
        private Graphic graphic;
        private Outline outline;
        private Color idle;

        private void Awake()
        {
            button = GetComponent<Button>();
            graphic = button.targetGraphic;
            if (graphic == null) return;
            idle = graphic.color;
            outline = graphic.gameObject.AddComponent<Outline>();
            outline.effectDistance = new Vector2(1.3f, -1.3f);
            outline.useGraphicAlpha = false;
            outline.enabled = false;
        }

        public void OnPointerEnter(PointerEventData data) { hovering.Add(data.pointerId); Refresh(); }
        public void OnPointerExit(PointerEventData data) { hovering.Remove(data.pointerId); pressing.Remove(data.pointerId); Refresh(); }
        public void OnPointerDown(PointerEventData data) { pressing.Add(data.pointerId); Refresh(); }
        public void OnPointerUp(PointerEventData data) { pressing.Remove(data.pointerId); Refresh(); }
        private void LateUpdate() { Refresh(); }
        public void SetIdleColor(Color value) { idle = value; Refresh(); }

        private void Refresh()
        {
            if (graphic == null) return;
            bool active = button.IsInteractable() && hovering.Count > 0;
            bool down = active && pressing.Count > 0;
            graphic.color = active ? Color.Lerp(idle, down ? RoomSiegeStyle.Gold : RoomSiegeStyle.Friendly, down ? .28f : .17f) : button.IsInteractable() ? idle : Color.Lerp(idle, new Color(.06f,.065f,.065f,idle.a*.55f),.75f);
            outline.effectColor = down ? RoomSiegeStyle.Gold : RoomSiegeStyle.Friendly;
            outline.enabled = active;
        }

        private void OnDisable()
        {
            hovering.Clear(); pressing.Clear();
            if (graphic != null) graphic.color = idle;
            if (outline != null) outline.enabled = false;
        }
    }
}
