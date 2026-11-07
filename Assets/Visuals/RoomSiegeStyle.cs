using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Visuals
{
    // Visual tokens only. No balance, input, camera or game state is owned here.
    public static class RoomSiegeStyle
    {
        public static readonly Color Friendly = new Color32(0x4F, 0xD8, 0xFF, 0xFF);
        public static readonly Color Enemy = new Color32(0xFF, 0x5A, 0x5A, 0xFF);
        public static readonly Color Gold = new Color32(0xFF, 0xD2, 0x7A, 0xFF);
        public static readonly Color Frost = new Color32(0x7A, 0xD7, 0xFF, 0xFF);
        public static readonly Color Text = new Color32(0xEE, 0xF1, 0xEE, 0xFF);
        public static readonly Color Glass = new Color(.027f, .052f, .075f, .78f);
        public static readonly Color Card = new Color(.049f, .085f, .12f, .88f);
        public static readonly Color Button = new Color(.075f, .124f, .167f, .96f);
        public static readonly Color Overlay = new Color(.018f, .035f, .055f, .97f);
        public static readonly Color Primary = new Color(.075f, .28f, .36f, .98f);
        public static readonly Color Selected = new Color(.065f, .195f, .255f, .98f);
        public static readonly Color Rule = new Color(.38f, .58f, .68f, .27f);
        public static readonly Color Muted = new Color32(0x9B, 0xB2, 0xC3, 0xFF);
        public const float CornerRadius = 10f;
        public const int CaptionSize = 18;
        public const int BodySize = 22;
        public const int TitleSize = 28;

        public static Color Alpha(Color value, float alpha)
        { value.a = alpha; return value; }

        public static int FontSize(int requested)
        { return requested <= 20 ? CaptionSize : requested <= 26 ? BodySize : TitleSize; }

        public static void StyleText(UnityEngine.UI.Text text, int requested)
        {
            text.fontSize = FontSize(requested);
            text.fontStyle = FontStyle.Normal;
            text.resizeTextForBestFit = false;
            text.color = Text;
        }

        public static Image AddRule(Transform parent, Material material, Vector2 position, Vector2 size, Color color)
        {
            var go = new GameObject("Section rule", typeof(RectTransform), typeof(Image));
            go.layer = 5; go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform; rect.anchoredPosition = position; rect.sizeDelta = size;
            var image = go.GetComponent<Image>(); image.material = material; image.color = color; image.raycastTarget = false;
            return image;
        }

        public static void StyleButton(UnityEngine.UI.Button button)
        {
            var colors = button.colors;
            colors.normalColor = Color.white;
            colors.highlightedColor = new Color(.80f, .96f, 1f, 1f);
            colors.pressedColor = new Color(.55f, .85f, 1f, .9f);
            colors.selectedColor = colors.highlightedColor;
            colors.disabledColor = new Color(.55f, .65f, .7f, .45f);
            colors.fadeDuration = .16f;
            button.colors = colors;
            var navigation = button.navigation; navigation.mode = Navigation.Mode.None; button.navigation = navigation;
            if (button.GetComponent<RayButtonHighlight>() == null)
                button.gameObject.AddComponent<RayButtonHighlight>();
        }

        public static void SetButtonSurface(Button button, Color color)
        {
            var highlight = button.GetComponent<RayButtonHighlight>();
            if (highlight != null) highlight.SetIdleColor(color);
            else if (button.targetGraphic != null) button.targetGraphic.color = color;
        }

        public static void PrimaryButton(Button button)
        {
            SetButtonSurface(button, Primary);
            var label = button.GetComponentInChildren<UnityEngine.UI.Text>();
            if (label != null) label.color = Text;
        }

        public static void NavigationButton(Button button, bool selected)
        {
            SetButtonSurface(button, selected ? Selected : Color.clear);
            var label = button.GetComponentInChildren<UnityEngine.UI.Text>();
            if (label != null) label.color = selected ? Friendly : Muted;
        }

        public static void ApplyWorldDetail(Material material)
        {
            if (material != null && material.HasProperty("_DetailTex"))
                material.SetTexture("_DetailTex", Resources.Load<Texture2D>("RoomSiegeHoloDetail2K"));
        }
    }
}
