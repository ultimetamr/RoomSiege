using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Enemies
{
    /// <summary>Procedural silhouettes for every non-Grunt enemy. No primitive capsules are used.</summary>
    public sealed class EnemyTypeModel : MonoBehaviour
    {
        private FacetedMesh geometry;
        private MeshRenderer surface;
        private MaterialPropertyBlock block;
        private string type;
        private float targetHeight,targetRadius;
        private bool fitRestPose;
        private float clock,phase;
        public Renderer Renderer=>surface;
        public string Type=>type;
        public float WorldBottom=>surface!=null?surface.bounds.min.y:0;
        public float WorldTop=>surface!=null?surface.bounds.max.y:0;
        public float WorldHeight=>WorldTop-WorldBottom;
        public void Build(Material material)
        {
            block=new MaterialPropertyBlock();geometry=new FacetedMesh("Enemy type silhouette",true);
            surface=geometry.Attach(transform,"Enemy silhouette",material);surface.enabled=false;
        }
        public void Configure(string id,float height,float radius)
        {
            gameObject.SetActive(true);type=id;clock=phase=0;targetHeight=height;targetRadius=radius;fitRestPose=true;
            transform.localScale=Vector3.one;transform.localPosition=Vector3.zero;transform.localRotation=Quaternion.identity;
            Rebuild(false);surface.gameObject.SetActive(true);surface.enabled=true;
        }
        public void SetTint(Color color,float flash)
        {
            color.r*=1.35f;color.g*=1.35f;color.b*=1.35f;
            block.Clear();block.SetColor("_Color",color);block.SetColor("_RimColor",RoomSiegeStyle.Enemy);block.SetFloat("_HitFlash",flash);surface.SetPropertyBlock(block);
        }
        public void Tick(float dt,float travelled)
        {
            if(dt<=0)return;clock+=dt;phase+=Mathf.Max(0,travelled)*Mathf.PI*2/.24f;
            // Only re-pose models that visibly animate; idle types still pulse through the shared material.
            if(type=="Runner"||type=="Boss"||type=="Shielded")Rebuild(travelled>.00001f);
        }
        private void Rebuild(bool walking)
        {
            geometry.Clear();float swing=Mathf.Sin(phase)*18*(walking?1:0);float bob=Mathf.Sin(clock*2)*.002f;
            if(type=="Runner")
            {
                geometry.Box(new Vector3(0,.20f+bob,0),new Vector3(.10f,.16f,.075f),Quaternion.Euler(0,0,swing*.18f),Color.white);
                geometry.Box(new Vector3(0,.31f+bob,.015f),new Vector3(.072f,.07f,.065f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.315f+bob,.054f),new Vector3(.048f,.012f,.012f),Quaternion.identity,new Color(1,1,1,1));
                SupportLegs(.033f,.13f,.11f,.025f,swing);
                geometry.OutlineBox(new Vector3(0,.20f,0),new Vector3(.105f,.16f,.078f),.0015f);
            }
            else if(type=="Tank")
            {
                geometry.Box(new Vector3(0,.20f,0),new Vector3(.25f,.20f,.18f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.34f,.015f),new Vector3(.17f,.10f,.14f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.405f,.07f),new Vector3(.08f,.055f,.12f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.405f,.145f),new Vector3(.035f,.035f,.13f),Quaternion.identity,Color.white);
                for(int side=-1;side<=1;side+=2)geometry.Box(new Vector3(side*.14f,.15f,0),new Vector3(.035f,.15f,.19f),Quaternion.identity,Color.white);
                for(int side=-1;side<=1;side+=2)
                {
                    geometry.Box(new Vector3(side*.105f,.055f,0),new Vector3(.095f,.11f,.23f),Quaternion.identity,Color.white);
                    geometry.OutlineBox(new Vector3(side*.105f,.055f,0),new Vector3(.097f,.112f,.232f),.0015f);
                }
                geometry.OutlineBox(new Vector3(0,.20f,0),new Vector3(.255f,.205f,.185f),.002f);
            }
            else if(type=="Shielded")
            {
                geometry.Box(new Vector3(0,.20f,0),new Vector3(.13f,.21f,.10f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.35f,.01f),new Vector3(.09f,.075f,.08f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.20f,.085f),new Vector3(.22f,.20f,.018f),Quaternion.identity,Color.white);
                for(int side=-1;side<=1;side+=2)geometry.Box(new Vector3(side*.09f,.22f,0),new Vector3(.025f,.13f,.11f),Quaternion.identity,Color.white);
                geometry.OutlineBox(new Vector3(0,.20f,0),new Vector3(.135f,.215f,.105f),.0015f);
                SupportLegs(.044f,.095f,.08f,.03f,swing*.55f);
            }
            else if(type=="Boss")
            {
                geometry.Box(new Vector3(0,.27f+bob,0),new Vector3(.28f,.27f,.21f),Quaternion.Euler(0,0,swing*.08f),Color.white);
                geometry.Box(new Vector3(0,.46f+bob,.015f),new Vector3(.20f,.13f,.17f),Quaternion.identity,Color.white);
                geometry.Box(new Vector3(0,.53f+bob,.11f),new Vector3(.10f,.07f,.12f),Quaternion.identity,Color.white);
                for(int side=-1;side<=1;side+=2){geometry.Box(new Vector3(side*.155f,.30f,0),new Vector3(.05f,.22f,.20f),Quaternion.identity,Color.white);geometry.Box(new Vector3(side*.075f,.55f,.01f),new Vector3(.035f,.10f,.035f),Quaternion.Euler(0,0,side*22),Color.white);}
                geometry.Box(new Vector3(0,.47f,.105f),new Vector3(.12f,.018f,.018f),Quaternion.identity,new Color(1,1,1,1));
                geometry.OutlineBox(new Vector3(0,.27f,0),new Vector3(.285f,.275f,.215f),.0025f);
                SupportLegs(.083f,.14f,.13f,.05f,swing*.4f);
            }
            Decorate();
            geometry.Upload();
            NormalizeToEnvelope();
        }
        private void Mark(Vector3 center,Vector3 size,Quaternion rotation)
        {geometry.Box(center,size,rotation,new Color(1,.72f,.68f,0));}
        private void Decorate()
        {
            if(type=="Runner")
            {
                // Swept back antennae and a narrow visor distinguish the fast unit.
                for(int side=-1;side<=1;side+=2)
                {
                    geometry.Box(new Vector3(side*.061f,.205f,-.005f),new Vector3(.022f,.115f,.030f),Quaternion.Euler(-28,0,side*15),new Color(.7f,.8f,.9f,1));
                    Mark(new Vector3(side*.033f,.30f,-.018f),new Vector3(.009f,.073f,.012f),Quaternion.Euler(-30,0,side*12));
                    Mark(new Vector3(side*.035f,.205f,.04f),new Vector3(.008f,.068f,.007f),Quaternion.Euler(0,0,-side*20));
                }
            }
            else if(type=="Tank")
            {
                for(int side=-1;side<=1;side+=2)
                {
                    for(int i=0;i<5;i++)Mark(new Vector3(side*.151f,.057f,(i-2)*.042f),new Vector3(.006f,.035f,.016f),Quaternion.identity);
                    geometry.Box(new Vector3(side*.075f,.24f,.099f),new Vector3(.064f,.088f,.02f),Quaternion.Euler(0,0,side*12),new Color(.72f,.75f,.8f,1));
                }
                Mark(new Vector3(0,.346f,.090f),new Vector3(.11f,.012f,.008f),Quaternion.identity);
            }
            else if(type=="Shielded")
            {
                Mark(new Vector3(0,.20f,.097f),new Vector3(.009f,.16f,.006f),Quaternion.identity);
                for(int side=-1;side<=1;side+=2)
                    Mark(new Vector3(side*.052f,.23f,.097f),new Vector3(.105f,.009f,.006f),Quaternion.Euler(0,0,-side*27));
                Mark(new Vector3(0,.355f,.052f),new Vector3(.061f,.012f,.008f),Quaternion.identity);
            }
            else if(type=="Boss")
            {
                // Crown, shoulder armor and a luminous chest reactor read at room scale.
                for(int side=-1;side<=1;side+=2)
                {
                    geometry.Box(new Vector3(side*.162f,.40f,0),new Vector3(.10f,.08f,.17f),Quaternion.Euler(0,0,-side*17),new Color(.8f,.65f,.65f,1));
                    Mark(new Vector3(side*.13f,.395f,.09f),new Vector3(.060f,.012f,.012f),Quaternion.Euler(0,0,-side*18));
                    Mark(new Vector3(side*.075f,.55f,.03f),new Vector3(.012f,.08f,.012f),Quaternion.Euler(0,0,side*22));
                }
                geometry.Box(new Vector3(0,.28f,.115f),new Vector3(.11f,.10f,.025f),Quaternion.Euler(0,0,45),new Color(.8f,.65f,.65f,1));
                Mark(new Vector3(0,.28f,.131f),new Vector3(.052f,.052f,.008f),Quaternion.Euler(0,0,45));
                Mark(new Vector3(0,.50f,.105f),new Vector3(.12f,.012f,.012f),Quaternion.identity);
            }
        }
        private void NormalizeToEnvelope()
        {
            var bounds=geometry.Mesh.bounds;
            if(fitRestPose)
            {
                float horizontal=targetRadius*2/Mathf.Max(.001f,Mathf.Max(bounds.size.x,bounds.size.z));
                transform.localScale=new Vector3(horizontal,targetHeight/Mathf.Max(.001f,bounds.size.y),horizontal);
                fitRestPose=false;
            }
            // Keep one fixed scale throughout the walk cycle. Only correct the foot contact height.
            transform.localPosition=Vector3.up*(-bounds.min.y*transform.localScale.y);
        }
        private void SupportLegs(float spacing,float hipHeight,float length,float width,float swing)
        {
            for(int side=-1;side<=1;side+=2)
            {
                Quaternion rotation=Quaternion.Euler(swing*side,0,0);
                Vector3 hip=new Vector3(side*spacing,hipHeight,0);
                geometry.Box(hip+rotation*(Vector3.down*length*.5f),new Vector3(width,length,width*1.05f),rotation,Color.white);
                geometry.Box(hip+rotation*new Vector3(0,-length,.016f),new Vector3(width*1.3f,.024f,width*1.9f),rotation,Color.white);
            }
        }
        public void Hide(){surface.enabled=false;gameObject.SetActive(false);}
        private void OnDestroy(){geometry?.Dispose();}
    }
}
