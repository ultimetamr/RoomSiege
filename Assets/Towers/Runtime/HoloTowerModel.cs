using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Towers
{
    // Cosmetic geometry only. Placement, colliders and combat remain owned by TowerHost.
    public sealed class HoloTowerModel : MonoBehaviour
    {
        private readonly FacetedMesh[] meshes=new FacetedMesh[3];
        private readonly MeshRenderer[] surfaces=new MeshRenderer[3];
        private Material material;
        private MaterialPropertyBlock block;
        public int DisplayLevel { get; private set; }

        public void Build(string id,Material supplied)
        {
            material=new Material(Resources.Load<Shader>("RoomSiegeEnemySilhouette")??supplied.shader){name=id+" faceted hologram"};
            RoomSiegeStyle.ApplyWorldDetail(material);block=new MaterialPropertyBlock();
            for(int level=1;level<=3;level++)
            {
                var mesh=new FacetedMesh(id+" level "+level);meshes[level-1]=mesh;
                if(RoomSiege.Data.WeaponRoster.IsNew(id))ArsenalModelGeometry.Build(mesh,id,level);
                else if(id=="Frost")Frost(mesh,level);
                else if(id=="Laser")Laser(mesh,level);
                else if(id=="Tesla")Tesla(mesh,level);
                else if(id=="Mortar")Mortar(mesh,level);
                else Trap(mesh,level);
                mesh.Upload();surfaces[level-1]=mesh.Attach(transform,id+" Lv"+level,material);
            }
            SetAppearance(1,TowerVisuals.ColorFor(id));
        }
        private static void Body(FacetedMesh mesh,Vector3 center,Vector3 size,Quaternion rotation)
        {mesh.Box(center,size,rotation,new Color(.55f,.80f,.88f,1));}
        private static void Accent(FacetedMesh mesh,Vector3 center,Vector3 size,Quaternion rotation)
        {mesh.Box(center,size,rotation,new Color(1,1,1,0));}
        private static void Frost(FacetedMesh mesh,int level)
        {
            Body(mesh,new Vector3(0,-.085f,0),new Vector3(.12f,.035f,.12f),Quaternion.Euler(0,45,0));
            Body(mesh,new Vector3(0,-.015f,0),new Vector3(.038f,.16f,.038f),Quaternion.Euler(0,45,12));
            Accent(mesh,new Vector3(0,.018f,0),new Vector3(.012f,.125f,.012f),Quaternion.identity);
            int count=level==1?3:level==2?4:6;
            for(int i=0;i<count;i++)
            {
                float angle=i*Mathf.PI*2/count;
                var position=new Vector3(Mathf.Cos(angle)*.067f,-.025f,Mathf.Sin(angle)*.067f);
                Body(mesh,position,new Vector3(.021f,.095f+level*.012f,.026f),Quaternion.Euler(0,-angle*Mathf.Rad2Deg,16));
                Accent(mesh,position+Vector3.up*.015f,new Vector3(.008f,.055f,.030f),Quaternion.Euler(0,-angle*Mathf.Rad2Deg,16));
            }
        }
        private static void Laser(FacetedMesh mesh,int level)
        {
            Body(mesh,new Vector3(0,-.093f,0),new Vector3(.11f,.038f,.10f),Quaternion.identity);
            Body(mesh,new Vector3(0,-.035f,0),new Vector3(.04f,.09f,.045f),Quaternion.identity);
            for(int side=-1;side<=1;side+=2)
            {
                Body(mesh,new Vector3(side*.048f,.008f,.041f),new Vector3(.022f,.064f,.14f),Quaternion.identity);
                Accent(mesh,new Vector3(side*.048f,.041f,.041f),new Vector3(.009f,.006f,.12f),Quaternion.identity);
                if(level>=2)Body(mesh,new Vector3(side*.068f,-.018f,-.026f),new Vector3(.022f,.08f,.055f),Quaternion.Euler(0,0,side*18));
                if(level>=3)Accent(mesh,new Vector3(side*.067f,.018f,.081f),new Vector3(.009f,.066f,.037f),Quaternion.identity);
            }
            Body(mesh,new Vector3(0,0,.045f),new Vector3(.033f,.035f,.105f),Quaternion.Euler(0,0,45));
            Accent(mesh,new Vector3(0,0,.105f),new Vector3(.025f,.025f,.018f),Quaternion.Euler(0,0,45));
        }
        private static void TechBody(FacetedMesh mesh,Vector3 center,Vector3 size,Quaternion rotation)
        {mesh.Box(center,size,rotation,Color.white);mesh.OutlineBox(center,size,.0013f);}
        private static void Tesla(FacetedMesh mesh,int level)
        {
            TechBody(mesh,new Vector3(0,-.09f,0),new Vector3(.12f,.035f,.12f),Quaternion.identity);
            int coils=level+2;
            for(int i=0;i<coils;i++){float a=i*Mathf.PI*2/coils;var p=new Vector3(Mathf.Cos(a)*.06f,.02f,Mathf.Sin(a)*.06f);TechBody(mesh,p,new Vector3(.018f,.12f,.018f),Quaternion.Euler(0,-a*Mathf.Rad2Deg,12));}
            Accent(mesh,new Vector3(0,.1f,0),new Vector3(.035f,.035f,.035f),Quaternion.Euler(45,45,45));
        }
        private static void Mortar(FacetedMesh mesh,int level)
        {
            TechBody(mesh,new Vector3(0,-.085f,0),new Vector3(.14f,.04f,.14f),Quaternion.Euler(0,45,0));
            TechBody(mesh,new Vector3(0,.01f,0),new Vector3(.06f,.16f,.06f),Quaternion.Euler(0,45,level==1?18:level==2?12:8));
            Accent(mesh,new Vector3(0,.095f,.03f),new Vector3(.032f,.05f,.032f),Quaternion.Euler(0,45,0));
            if(level>=2)Accent(mesh,new Vector3(.06f,.025f,0),new Vector3(.025f,.055f,.025f),Quaternion.Euler(0,45,45));
        }
        private static void Trap(FacetedMesh mesh,int level)
        {
            Body(mesh,new Vector3(0,-.012f,0),new Vector3(.145f,.025f,.145f),Quaternion.Euler(0,45,0));
            Accent(mesh,new Vector3(0,.005f,0),new Vector3(.028f,.008f,.028f),Quaternion.Euler(0,45,0));
            for(int i=0;i<4;i++)
            {
                float angle=i*90;var rotation=Quaternion.Euler(0,angle,0);
                Body(mesh,rotation*new Vector3(0,.008f,.067f),new Vector3(.028f,.028f,.048f),rotation);
                Accent(mesh,rotation*new Vector3(0,.025f,.068f),new Vector3(.010f,.007f,.035f),rotation);
                if(level>=2)Body(mesh,rotation*new Vector3(.035f,.025f,.035f),new Vector3(.012f,.035f,.026f),rotation);
                if(level>=3)Accent(mesh,rotation*new Vector3(.035f,.044f,.035f),new Vector3(.014f,.005f,.025f),rotation);
            }
        }
        public void SetAppearance(int level,Color tint)
        {
            DisplayLevel=Mathf.Clamp(level,1,3);
            block.Clear();block.SetColor("_Color",RoomSiegeStyle.Alpha(tint,.58f));block.SetColor("_RimColor",tint);
            for(int i=0;i<3;i++){surfaces[i].enabled=i==DisplayLevel-1;surfaces[i].SetPropertyBlock(block);}
        }
        private void OnDestroy()
        {
            foreach(var mesh in meshes)mesh?.Dispose();
            if(material!=null){if(Application.isPlaying)Destroy(material);else DestroyImmediate(material);}
        }
    }
}
