using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomSiege.Visuals
{
    /// <summary>Small chamfered solids; one mesh per articulated silhouette, no cosmetic colliders.</summary>
    public sealed class FacetedMesh
    {
        private readonly List<Vector3> vertices=new List<Vector3>(1024);
        private readonly List<int> triangles=new List<int>(2048);
        private readonly List<Color> colors=new List<Color>(1024);
        private readonly List<Vector2> uvs=new List<Vector2>(1024);
        private static readonly Vector2[] outline={new Vector2(-.32f,-.5f),new Vector2(.32f,-.5f),
            new Vector2(.5f,-.32f),new Vector2(.5f,.32f),new Vector2(.32f,.5f),
            new Vector2(-.32f,.5f),new Vector2(-.5f,.32f),new Vector2(-.5f,-.32f)};
        public readonly Mesh Mesh;
        public FacetedMesh(string name,bool dynamic=false)
        {Mesh=new Mesh{name=name};if(dynamic)Mesh.MarkDynamic();}
        public void Clear(){vertices.Clear();triangles.Clear();colors.Clear();uvs.Clear();}
        public void Box(Vector3 center,Vector3 size,Quaternion rotation,Color color)
        {
            var matrix=Matrix4x4.TRS(center,rotation,size);
            // Clockwise top/bottom fans and independently shaded chamfered side faces.
            for(int side=0;side<8;side++)
            {
                int next=(side+1)%8;
                Vector2 a=outline[side],b=outline[next];
                Face(matrix,new Vector3(a.x,-.5f,a.y),new Vector3(a.x,.5f,a.y),
                    new Vector3(b.x,.5f,b.y),new Vector3(b.x,-.5f,b.y),color);
                Triangle(matrix,new Vector3(0,.5f,0),new Vector3(b.x,.5f,b.y),new Vector3(a.x,.5f,a.y),color);
                Triangle(matrix,new Vector3(0,-.5f,0),new Vector3(a.x,-.5f,a.y),new Vector3(b.x,-.5f,b.y),color);
            }
        }
        private void Face(Matrix4x4 m,Vector3 a,Vector3 b,Vector3 c,Vector3 d,Color color)
        {
            int first=vertices.Count;Add(m,a,color);Add(m,b,color);Add(m,c,color);Add(m,d,color);
            triangles.Add(first);triangles.Add(first+1);triangles.Add(first+2);
            triangles.Add(first);triangles.Add(first+2);triangles.Add(first+3);
        }
        public void OutlineBox(Vector3 center,Vector3 size,float width)
        {
            for(int i=0;i<8;i++)
            {
                Vector2 a=outline[i],b=outline[(i+1)%8];
                Vector3 low=center+Vector3.Scale(new Vector3(a.x,-.5f,a.y),size);
                Vector3 high=center+Vector3.Scale(new Vector3(a.x,.5f,a.y),size);
                Rod(low,high,width);Rod(low,center+Vector3.Scale(new Vector3(b.x,-.5f,b.y),size),width);
                Rod(high,center+Vector3.Scale(new Vector3(b.x,.5f,b.y),size),width);
            }
        }
        private void Rod(Vector3 a,Vector3 b,float width)
        {
            var m=Matrix4x4.TRS((a+b)*.5f,Quaternion.LookRotation(b-a),new Vector3(width,width,(b-a).magnitude));
            var color=new Color(1,1,1,0); // Explicit emissive edge marker, interpreted by the surface shader.
            Face(m,new Vector3(-.5f,-.5f,-.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,-.5f,-.5f),color);
            Face(m,new Vector3(.5f,-.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,-.5f,.5f),color);
            Face(m,new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,.5f,.5f),new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,-.5f,-.5f),color);
            Face(m,new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,.5f,-.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,-.5f,.5f),color);
            Face(m,new Vector3(-.5f,.5f,-.5f),new Vector3(-.5f,.5f,.5f),new Vector3(.5f,.5f,.5f),new Vector3(.5f,.5f,-.5f),color);
            Face(m,new Vector3(-.5f,-.5f,.5f),new Vector3(-.5f,-.5f,-.5f),new Vector3(.5f,-.5f,-.5f),new Vector3(.5f,-.5f,.5f),color);
        }
        private void Triangle(Matrix4x4 m,Vector3 a,Vector3 b,Vector3 c,Color color)
        {int first=vertices.Count;Add(m,a,color);Add(m,b,color);Add(m,c,color);triangles.Add(first);triangles.Add(first+1);triangles.Add(first+2);}
        private void Add(Matrix4x4 m,Vector3 p,Color color)
        {vertices.Add(m.MultiplyPoint3x4(p));colors.Add(color);uvs.Add(new Vector2(p.x+.5f,p.y+.5f));}
        public void Upload()
        {
            Mesh.Clear(false);Mesh.SetVertices(vertices);Mesh.SetColors(colors);Mesh.SetUVs(0,uvs);
            Mesh.SetTriangles(triangles,0);Mesh.RecalculateNormals();Mesh.RecalculateBounds();
        }
        public MeshRenderer Attach(Transform parent,string name,Material material)
        {
            var go=new GameObject(name);go.transform.SetParent(parent,false);
            go.AddComponent<MeshFilter>().sharedMesh=Mesh;
            var renderer=go.AddComponent<MeshRenderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return renderer;
        }
        public void Dispose(){if(Application.isPlaying)Object.Destroy(Mesh);else Object.DestroyImmediate(Mesh);}
    }
}
