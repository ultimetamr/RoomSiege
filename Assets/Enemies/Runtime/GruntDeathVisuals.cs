using RoomSiege.Visuals;
using UnityEngine;
using UnityEngine.Rendering;

namespace RoomSiege.Enemies
{
    /// <summary>Bounded cosmetic pool, independent of actor lifetime, rewards and wave counts.</summary>
    public sealed class GruntDeathVisuals : MonoBehaviour
    {
        public const int Capacity=12;
        private sealed class Echo {public Mesh Mesh;public MeshRenderer Renderer;public Transform Root;public float Age;public Vector3 Origin;public Quaternion Rotation;}
        private readonly Echo[] echoes=new Echo[Capacity];
        private MaterialPropertyBlock block;
        private int cursor;
        public int VisibleCount {get{int count=0;foreach(var e in echoes)if(e!=null&&e.Renderer.enabled)count++;return count;}}
        public void Build(Material material)
        {
            block=new MaterialPropertyBlock();
            for(int i=0;i<Capacity;i++)
            {
                var go=new GameObject("Grunt dissolve "+i);go.transform.SetParent(transform,false);
                var mesh=new Mesh{name="Grunt death snapshot "+i};mesh.MarkDynamic();
                go.AddComponent<MeshFilter>().sharedMesh=mesh;var renderer=go.AddComponent<MeshRenderer>();
                renderer.sharedMaterial=material;renderer.enabled=false;renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;
                echoes[i]=new Echo{Mesh=mesh,Renderer=renderer,Root=go.transform};
            }
        }
        public void Emit(GruntModel model)
        {
            if(model==null||!model.gameObject.activeInHierarchy||echoes[0]==null)return;
            var e=echoes[cursor];cursor=(cursor+1)%Capacity;model.Capture(e.Mesh);
            e.Age=0;e.Origin=model.transform.position;e.Rotation=model.transform.rotation;
            e.Root.SetPositionAndRotation(e.Origin,e.Rotation);e.Root.localScale=model.transform.lossyScale;
            e.Renderer.enabled=true;Paint(e,0);
        }
        public void Tick(float dt,bool paused)
        {
            if(paused||dt<=0)return;
            foreach(var e in echoes)
            {
                if(e==null||!e.Renderer.enabled)continue;
                e.Age+=dt;float t=e.Age/.42f;
                if(t>=1){e.Renderer.enabled=false;continue;}
                e.Root.position=e.Origin+Vector3.up*(t*.045f);
                e.Root.rotation=e.Rotation*Quaternion.Euler(t*32,0,t*9);
                Paint(e,t);
            }
        }
        private void Paint(Echo e,float t)
        {
            block.Clear();block.SetColor("_Color",RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,(1-t)*.5f));
            block.SetColor("_RimColor",RoomSiegeStyle.Enemy);block.SetFloat("_HitFlash",(1-t)*.6f);
            block.SetFloat("_Dissolve",t);e.Renderer.SetPropertyBlock(block);
        }
        public void Clear(){foreach(var e in echoes)if(e!=null)e.Renderer.enabled=false;cursor=0;}
        private void OnDestroy(){foreach(var e in echoes)if(e!=null){if(Application.isPlaying)Destroy(e.Mesh);else DestroyImmediate(e.Mesh);}}
    }
}
