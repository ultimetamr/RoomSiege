using UnityEngine;
using UnityEngine.Rendering;
using RoomSiege.Visuals;

namespace RoomSiege.Enemies
{
    public sealed class EnemyVisual : MonoBehaviour
    {
        private Transform body,shield,impact;
        private Renderer bodyRenderer,shieldRenderer,impactRenderer;
        private EnemyTypeModel typeModel;
        private LineRenderer focus,slowHalo;
        private MaterialPropertyBlock properties;
        private Color tint;
        private float flash,clock;
        private bool focused;
        public GruntModel Grunt {get;private set;}
        public bool BodyReady
        {
            get
            {
                var renderer=Grunt!=null&&Grunt.gameObject.activeInHierarchy?Grunt.GetComponentInChildren<MeshRenderer>():bodyRenderer;
                if(renderer==null||!renderer.enabled||!renderer.gameObject.activeInHierarchy)return false;
                var filter=renderer.GetComponent<MeshFilter>();
                return filter!=null&&filter.sharedMesh!=null&&filter.sharedMesh.vertexCount>0;
            }
        }
        public void Build(Material silhouette,Material accent)
        {
            properties=new MaterialPropertyBlock();
            var silhouetteGo=new GameObject("Enemy silhouette");silhouetteGo.transform.SetParent(transform,false);
            body=silhouetteGo.transform;typeModel=silhouetteGo.AddComponent<EnemyTypeModel>();typeModel.Build(silhouette);bodyRenderer=typeModel.Renderer;
            var infantry=new GameObject("Grunt articulated model");infantry.transform.SetParent(transform,false);
            Grunt=infantry.AddComponent<GruntModel>();Grunt.Build(silhouette);infantry.SetActive(false);
            shield=Shape("Shield silhouette",PrimitiveType.Sphere,silhouette,out shieldRenderer);
            impact=Shape("Local hit highlight",PrimitiveType.Sphere,accent,out impactRenderer);
            var halo=new GameObject("Focus fire halo");halo.transform.SetParent(transform,false);
            focus=halo.AddComponent<LineRenderer>();focus.sharedMaterial=accent;focus.useWorldSpace=false;focus.loop=true;
            focus.positionCount=48;focus.startWidth=focus.endWidth=.009f;focus.startColor=focus.endColor=RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,.8f);
            focus.shadowCastingMode=ShadowCastingMode.Off;focus.receiveShadows=false;
            slowHalo=RoomSiege.Towers.TowerVisuals.Ring(transform,"Ice slow accent",accent,
                RoomSiegeStyle.Alpha(RoomSiegeStyle.Frost,.7f),.1f,.006f);
            slowHalo.enabled=false;
            impact.gameObject.SetActive(false);focus.enabled=false;
        }
        private Transform Shape(string label,PrimitiveType primitive,Material material,out Renderer renderer)
        {
            var go=GameObject.CreatePrimitive(primitive);go.name=label;go.transform.SetParent(transform,false);
            var collider=go.GetComponent<Collider>();collider.enabled=false;
            if(Application.isPlaying)Destroy(collider);else DestroyImmediate(collider);
            renderer=go.GetComponent<Renderer>();renderer.sharedMaterial=material;
            renderer.shadowCastingMode=ShadowCastingMode.Off;renderer.receiveShadows=false;return go.transform;
        }
        public void Configure(string id,float height,float radius,bool hasShield)
        {
            clock=flash=0;focused=false;body.gameObject.SetActive(true);shield.gameObject.SetActive(hasShield);impact.gameObject.SetActive(false);focus.enabled=false;
            slowHalo.enabled=false;
            tint=Color.Lerp(RoomSiegeStyle.Enemy,new Color(.60f,.22f,.25f),.40f);
            tint.a=id=="Boss"?.68f:id=="Tank"?.62f:.56f;
            bool isGrunt=id=="Grunt";
            body.gameObject.SetActive(!isGrunt);bodyRenderer.enabled=!isGrunt;Grunt.gameObject.SetActive(isGrunt);
            if(isGrunt)typeModel.Hide();else{typeModel.Configure(id,height,radius);typeModel.SetTint(tint,0);}
            Grunt.transform.localScale=new Vector3(radius/.12f,height/.34f,radius/.12f);
            Grunt.ResetPose();Grunt.SetTint(tint,0);
            shield.localPosition=Vector3.up*height*.5f;shield.localScale=new Vector3(radius*2.5f,height*1.08f,radius*2.5f);
            SetColor(bodyRenderer,tint);SetColor(shieldRenderer,RoomSiegeStyle.Alpha(RoomSiegeStyle.Enemy,.18f));
            for(int i=0;i<48;i++)
            {
                float a=i*Mathf.PI*2/48;
                focus.SetPosition(i,new Vector3(Mathf.Cos(a)*(radius+.06f),.025f,Mathf.Sin(a)*(radius+.06f)));
                slowHalo.SetPosition(i,new Vector3(Mathf.Cos(a)*(radius+.025f),.035f,Mathf.Sin(a)*(radius+.025f)));
            }
        }
        public void Highlight(Vector3 worldPoint,bool hasShield)
        {
            flash=.14f;impact.gameObject.SetActive(true);
            if(Grunt.gameObject.activeSelf)Grunt.Highlight();
            Vector3 local=transform.InverseTransformPoint(worldPoint);
            local.x=Mathf.Clamp(local.x,-.4f,.4f);local.y=Mathf.Clamp(local.y,0,1);local.z=Mathf.Clamp(local.z,-.4f,.4f);
            impact.localPosition=local;impact.localScale=Vector3.one*.065f;
            SetColor(impactRenderer,hasShield?new Color(1f,.72f,.72f,.95f):new Color(1f,.94f,.94f,.95f));
            shield.gameObject.SetActive(hasShield);
        }
        public void Tick(float dt,bool hasShield,float slowFraction=0,float travelled=0)
        {
            clock+=dt;flash=Mathf.Max(0,flash-dt);
            impact.gameObject.SetActive(flash>0);
            if(Grunt.gameObject.activeSelf){body.gameObject.SetActive(false);bodyRenderer.enabled=false;typeModel.Hide();}
            else {body.gameObject.SetActive(true);bodyRenderer.enabled=true;}
            SetColor(bodyRenderer,Color.Lerp(tint,new Color(1f,.72f,.72f,.58f),flash/.14f*.35f));
            if(Grunt.gameObject.activeSelf){Grunt.SetTint(tint,flash/.14f);Grunt.Tick(dt,travelled);}
            else {typeModel.SetTint(tint,flash/.14f);typeModel.Tick(dt,travelled);}
            shield.gameObject.SetActive(hasShield);focus.enabled=focused;
            slowHalo.enabled=slowFraction>0;
            focus.transform.localScale=Vector3.one*(1+.06f*Mathf.Sin(clock*6));
        }
        private void SetColor(Renderer renderer,Color color)
        {
            properties.Clear();properties.SetColor("_Color",color);
            properties.SetColor("_RimColor",renderer==bodyRenderer?RoomSiegeStyle.Enemy:color);
            properties.SetFloat("_HitFlash",renderer==bodyRenderer?flash/.14f:0);
            renderer.SetPropertyBlock(properties);
        }
        public void SetFocused(bool value){focused=value;focus.enabled=value;}
        public void Hide(){body.gameObject.SetActive(false);bodyRenderer.enabled=false;typeModel.Hide();Grunt.gameObject.SetActive(false);shield.gameObject.SetActive(false);impact.gameObject.SetActive(false);focus.enabled=false;slowHalo.enabled=false;}
    }
}
