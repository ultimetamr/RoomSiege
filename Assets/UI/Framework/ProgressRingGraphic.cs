using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.UI
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class ProgressRingGraphic : MaskableGraphic
    {
        [Range(0,1)] public float progress = 0.35f;
        [Range(0.01f,0.4f)] public float thickness = 0.1f;
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();
            const int segments = 96;
            float outer = Mathf.Min(rectTransform.rect.width, rectTransform.rect.height) * 0.5f;
            float inner = outer * (1f-thickness);
            Vector2 center = rectTransform.rect.center;
            for (int i=0; i<segments; i++)
            {
                Color tint = i < segments*Mathf.Clamp01(progress) ? color : new Color(color.r,color.g,color.b,color.a*0.15f);
                float a = Mathf.PI*0.5f-i*Mathf.PI*2f/segments;
                float b = Mathf.PI*0.5f-(i+1)*Mathf.PI*2f/segments;
                int start = mesh.currentVertCount;
                mesh.AddVert(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*outer,tint,Vector2.zero);
                mesh.AddVert(center+new Vector2(Mathf.Cos(a),Mathf.Sin(a))*inner,tint,Vector2.zero);
                mesh.AddVert(center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*inner,tint,Vector2.zero);
                mesh.AddVert(center+new Vector2(Mathf.Cos(b),Mathf.Sin(b))*outer,tint,Vector2.zero);
                mesh.AddTriangle(start,start+1,start+2); mesh.AddTriangle(start,start+2,start+3);
            }
        }
    }
}
