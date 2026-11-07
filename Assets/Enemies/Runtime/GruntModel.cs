using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Enemies
{
    /// <summary>One renderer, articulated mesh; gait advances by travelled metres, not wall time.</summary>
    public sealed class GruntModel : MonoBehaviour
    {
        private FacetedMesh geometry;
        private MeshRenderer surface;
        private MaterialPropertyBlock block;
        private float phase,clock,hit;
        public float WalkPhase=>phase;
        public Mesh PoseMesh=>geometry.Mesh;
        public void Build(Material material)
        {block=new MaterialPropertyBlock();geometry=new FacetedMesh("Grunt articulated silhouette",true);surface=geometry.Attach(transform,"Grunt infantry",material);ResetPose();}
        public void ResetPose(){phase=clock=hit=0;surface.enabled=true;surface.gameObject.SetActive(true);Pose(0);}
        public void Highlight(){hit=1;}
        public void SetTint(Color color,float flash)
        {color.r*=1.35f;color.g*=1.35f;color.b*=1.35f;block.Clear();block.SetColor("_Color",color);block.SetColor("_RimColor",RoomSiegeStyle.Enemy);block.SetFloat("_HitFlash",flash);surface.SetPropertyBlock(block);}
        public void Tick(float dt,float distance)
        {
            if(dt<=0)return;clock+=dt;hit=Mathf.MoveTowards(hit,0,dt*7);
            phase+=Mathf.Max(0,distance)*Mathf.PI*2/.23f;
            Pose(distance>.00001f?1:0);
        }
        private void Pose(float walking)
        {
            geometry.Clear();float swing=Mathf.Sin(phase)*27*walking;
            float bob=Mathf.Abs(Mathf.Sin(phase))* .006f*walking+Mathf.Sin(clock*2)*.0015f;
            Quaternion flinch=Quaternion.Euler(-hit*9,0,hit*4);
            Body(new Vector3(0,.213f+bob,0),new Vector3(.125f,.105f,.07f),flinch,Color.white);
            Body(new Vector3(0,.30f+bob,.003f),new Vector3(.079f,.071f,.068f),flinch,Color.white);
            Body(new Vector3(0,.303f+bob,.039f),new Vector3(.057f,.012f,.009f),flinch,new Color(1.8f,1.1f,1.1f,1));
            Body(new Vector3(0,.151f+bob,0),new Vector3(.09f,.032f,.055f),Quaternion.identity,Color.white);
            for(int side=-1;side<=1;side+=2)
            {
                Quaternion leg=Quaternion.Euler(swing*side,0,0);
                Vector3 hip=new Vector3(side*.035f,.14f+bob,0);
                Body(hip+leg*new Vector3(0,-.056f,0),new Vector3(.035f,.105f,.038f),leg,Color.white);
                Body(hip+leg*new Vector3(0,-.116f,.015f),new Vector3(.044f,.03f,.072f),leg,Color.white);
                Quaternion arm=Quaternion.Euler(-swing*side*.8f-hit*10,0,side*7);
                Body(new Vector3(side*.084f,.245f+bob,0)+arm*new Vector3(0,-.042f,0),
                    new Vector3(.03f,.095f,.036f),arm,Color.white);
                Body(new Vector3(side*.071f,.251f+bob,0),new Vector3(.036f,.036f,.055f),flinch,Color.white);
            }
            Body(new Vector3(0,.214f+bob,.039f),new Vector3(.061f,.052f,.012f),flinch,new Color(.78f,.8f,.86f,1));
            Body(new Vector3(0,.214f+bob,.047f),new Vector3(.008f,.035f,.006f),flinch,new Color(1,1,1,0));
            Body(new Vector3(0,.224f+bob,-.047f),new Vector3(.075f,.073f,.027f),flinch,new Color(.65f,.7f,.78f,1));
            geometry.Upload();
        }
        private void Body(Vector3 c,Vector3 s,Quaternion r,Color color)=>geometry.Box(c,s,r,color);
        public void Capture(Mesh target){target.Clear();target.vertices=geometry.Mesh.vertices;target.triangles=geometry.Mesh.triangles;target.normals=geometry.Mesh.normals;target.colors=geometry.Mesh.colors;target.uv=geometry.Mesh.uv;target.RecalculateBounds();}
        private void OnDestroy(){geometry?.Dispose();}
    }
}
