using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Visuals
{
    public static class WorldUiSampling
    {
        // Rasterize dynamic glyphs at 3x resolution, retaining their layout size in metres.
        public const float GlyphDensity = 3f;

        public static void Configure(Canvas canvas)
        {
            if (canvas == null || canvas.renderMode != RenderMode.WorldSpace) return;
            var scaler = canvas.GetComponent<CanvasScaler>();
            if (canvas.isRootCanvas && scaler == null) scaler = canvas.gameObject.AddComponent<CanvasScaler>();
            if (scaler != null) scaler.dynamicPixelsPerUnit = GlyphDensity;
            // Nested override-sorting canvases also serve as Text.canvas. Apply immediately,
            // including inactive pages, rather than waiting for the root scaler's render callback.
            canvas.scaleFactor = GlyphDensity;
        }
    }
}
