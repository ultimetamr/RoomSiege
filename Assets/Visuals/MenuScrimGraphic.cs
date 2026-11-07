using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Visuals
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class MenuScrimGraphic : MaskableGraphic
    {
        protected override void OnPopulateMesh(VertexHelper mesh)
        {
            mesh.Clear();var rect=GetPixelAdjustedRect();
            float[] stops={0,.46f,.7f,1};float[] alpha={.98f,.91f,.2f,.08f};
            for(int i=0;i<stops.Length;i++)
            {
                float x=Mathf.Lerp(rect.xMin,rect.xMax,stops[i]);
                for(int y=0;y<2;y++)
                {
                    var vertex=UIVertex.simpleVert;vertex.position=new Vector3(x,y==0?rect.yMin:rect.yMax);
                    vertex.color=RoomSiegeStyle.Alpha(RoomSiegeStyle.Overlay,alpha[i]*color.a);vertex.uv0=Vector2.one*.5f;mesh.AddVert(vertex);
                }
                if(i>0){int start=(i-1)*2;mesh.AddTriangle(start,start+1,start+3);mesh.AddTriangle(start,start+3,start+2);}
            }
        }
    }
}
