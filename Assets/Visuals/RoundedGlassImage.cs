using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Visuals
{
    // Image inheritance preserves existing button/drag GetComponent<Image>() contracts.
    // The fill, sheen and rim are vector triangles; no panel sprite or camera copy is used.
    [AddComponentMenu("RoomSiege/Visuals/Rounded Glass Image")]
    public sealed class RoundedGlassImage : Image
    {
        private const int StepsPerCorner = 12;
        private const int Points = (StepsPerCorner + 1) * 4;
        private const float RimWidth = .8f;
        private const float EdgeFeather = 1f;

        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            Rect rect = GetPixelAdjustedRect();
            if (rect.width <= 0 || rect.height <= 0) return;
            float radius = Mathf.Min(RoomSiegeStyle.CornerRadius, Mathf.Min(rect.width, rect.height) * .5f);
            float feather = Mathf.Min(EdgeFeather, Mathf.Min(rect.width, rect.height) * .25f);
            float inset = Mathf.Min(RimWidth + feather, radius * .5f);
            var rimRect = new Rect(rect.xMin + feather, rect.yMin + feather,
                rect.width - feather * 2, rect.height - feather * 2);
            var innerRect = new Rect(rect.xMin + inset, rect.yMin + inset,
                rect.width - inset * 2, rect.height - inset * 2);
            Color fill = color;
            AddVertex(mesh, rect.center, fill);
            for (int i = 0; i < Points; i++)
            {
                Vector2 position = Contour(innerRect, Mathf.Max(0, radius - inset), i);
                AddVertex(mesh, position, fill);
            }
            Color rim = RoomSiegeStyle.Alpha(RoomSiegeStyle.Rule, fill.a * .32f);
            for (int i = 0; i < Points; i++) AddVertex(mesh, Contour(rimRect, Mathf.Max(0, radius - feather), i), rim);
            Color transparent = rim; transparent.a = 0;
            for (int i = 0; i < Points; i++) AddVertex(mesh, Contour(rect, radius, i), transparent);
            for (int i = 0; i < Points; i++)
            {
                int next = (i + 1) % Points;
                int a = 1 + i, b = 1 + next, c = 1 + Points + i, d = 1 + Points + next;
                mesh.AddTriangle(0, a, b);
                mesh.AddTriangle(a, c, d);
                mesh.AddTriangle(a, d, b);
                int e = 1 + Points * 2 + i, f = 1 + Points * 2 + next;
                mesh.AddTriangle(c, e, f);
                mesh.AddTriangle(c, f, d);
            }
        }

        private static Vector2 Contour(Rect rect, float radius, int index)
        {
            int corner = index / (StepsPerCorner + 1);
            int step = index % (StepsPerCorner + 1);
            float angle = (corner * 90f + step * 90f / StepsPerCorner) * Mathf.Deg2Rad;
            Vector2 center = new Vector2(corner == 0 || corner == 3 ? rect.xMax - radius : rect.xMin + radius,
                corner < 2 ? rect.yMax - radius : rect.yMin + radius);
            return center + new Vector2(Mathf.Cos(angle), Mathf.Sin(angle)) * radius;
        }

        private static void AddVertex(VertexHelper mesh, Vector2 position, Color tint)
        {
            var vertex = UIVertex.simpleVert;
            vertex.position = new Vector3(position.x, position.y, 0);
            vertex.color = tint;
            vertex.uv0 = new Vector4(.5f, .5f, 0, 0);
            mesh.AddVert(vertex);
        }
    }
}
