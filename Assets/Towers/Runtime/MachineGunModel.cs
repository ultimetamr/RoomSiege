using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Towers
{
    /// <summary>Visual-only MG assembly. The combat system owns aim, cadence and damage.</summary>
    public sealed class MachineGunModel : MonoBehaviour
    {
        private readonly FacetedMesh[] meshes=new FacetedMesh[6];
        private readonly MeshRenderer[] renderers=new MeshRenderer[6];
        private MaterialPropertyBlock block;
        private Transform recoil;
        private Material material;
        private Color tint;
        private float kick,flash,clock;
        public float Recoil=>kick;
        public Vector3 MuzzlePosition=>recoil!=null?recoil.TransformPoint(new Vector3(0,0,.191f)):transform.position;
        public int DisplayLevel {get;private set;}
        public void Build(Transform foundation,Material supplied)
        {
            block=new MaterialPropertyBlock();
            // Use a surface shader for the body, keep existing hologram rings and shot lines.
            var shader=Resources.Load<Shader>("RoomSiegeEnemySilhouette");
            material=new Material(shader!=null?shader:supplied.shader){name="MG faceted hologram"};
            RoomSiegeStyle.ApplyWorldDetail(material);
            var carriage=new GameObject("Recoil carriage");carriage.transform.SetParent(transform,false);recoil=carriage.transform;
            for(int i=0;i<meshes.Length;i++)meshes[i]=new FacetedMesh("MG geometry "+i);
            Box(0,new Vector3(0,.055f,0),new Vector3(.16f,.09f,.14f));
            Box(0,new Vector3(0,.105f,0),new Vector3(.055f,.08f,.06f));
            Box(1,Vector3.zero,new Vector3(.105f,.068f,.125f));
            Box(1,new Vector3(0,.043f,-.025f),new Vector3(.065f,.025f,.055f));
            Box(1,new Vector3(-.067f,-.005f,-.015f),new Vector3(.033f,.055f,.07f));
            Box(2,new Vector3(0,0,.105f),new Vector3(.027f,.027f,.15f));
            Box(2,new Vector3(0,0,.165f),new Vector3(.047f,.045f,.028f));
            Box(3,new Vector3(.067f,-.005f,-.015f),new Vector3(.03f,.05f,.07f));
            Box(3,new Vector3(0,.053f,.02f),new Vector3(.025f,.018f,.06f));
            for(int side=-1;side<=1;side+=2)
                Box(4,new Vector3(side*.039f,0,.105f),new Vector3(.012f,.025f,.12f));
            Box(4,new Vector3(0,-.028f,.15f),new Vector3(.07f,.014f,.035f));
            Box(5,new Vector3(0,0,.191f),new Vector3(.034f,.034f,.018f));
            for(int i=0;i<meshes.Length;i++)
            {
                meshes[i].Upload();renderers[i]=meshes[i].Attach(i==0?foundation:recoil,"MG "+new[]{"pedestal","receiver","barrel","Lv2 targeting","Lv3 cooling rails","muzzle flash"}[i],material);
            }
            SetAppearance(1,TowerVisuals.Blue);
        }
        private void Box(int i,Vector3 center,Vector3 size)
        {meshes[i].Box(center,size,Quaternion.identity,Color.white);meshes[i].OutlineBox(center,size,i==5?.0015f:.0012f);}
        public void SetAppearance(int level,Color color)
        {DisplayLevel=Mathf.Clamp(level,1,3);tint=color;renderers[3].enabled=DisplayLevel>=2;renderers[4].enabled=DisplayLevel>=3;Paint();}
        public void Fire(){kick=.014f;flash=.065f;Paint();}
        public void Tick(float dt)
        {
            if(dt<=0)return;clock+=dt;kick=Mathf.MoveTowards(kick,0,dt*.09f);flash=Mathf.Max(0,flash-dt);
            recoil.localPosition=Vector3.back*kick;Paint();
        }
        private void Paint()
        {
            if(renderers[0]==null)return;
            block.Clear();block.SetColor("_Color",RoomSiegeStyle.Alpha(tint*(1+.10f*Mathf.Sin(clock*2)),.62f));
            block.SetColor("_RimColor",tint);block.SetFloat("_HitFlash",0);
            for(int i=0;i<5;i++)renderers[i].SetPropertyBlock(block);
            renderers[5].enabled=flash>0;
            block.SetColor("_Color",new Color(.72f,.94f,1,.9f));block.SetColor("_RimColor",Color.white);
            renderers[5].SetPropertyBlock(block);
        }
        private void OnDestroy()
        {foreach(var mesh in meshes)mesh?.Dispose();if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}}
    }
}
