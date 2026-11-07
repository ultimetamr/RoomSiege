using RoomSiege.Visuals;
using UnityEngine;

namespace RoomSiege.Towers
{
    // The mine is removed on impact; its bounded cosmetic pool outlives the tower view.
    public sealed class TrapExplosionVisuals : MonoBehaviour
    {
        public const int Capacity=6;
        public const float Duration=.7f;
        private sealed class Burst
        {
            public Transform Root,Shards;
            public LineRenderer Shock,Echo,Flash;
            public MeshRenderer Sparks;
            public float Age,Scale;
        }
        private readonly Burst[] bursts=new Burst[Capacity];
        private MaterialPropertyBlock tint;
        private FacetedMesh sparkMesh;
        private int cursor;
        public int VisibleCount {get{int count=0;foreach(var burst in bursts)if(burst!=null&&burst.Root.gameObject.activeSelf)count++;return count;}}

        public void Build(Material material)
        {
            if(bursts[0]!=null)return;
            tint=new MaterialPropertyBlock();
            sparkMesh=new FacetedMesh("Trap explosion sparks");
            for(int i=0;i<12;i++)
            {
                float angle=i*Mathf.PI*2/12;
                var direction=new Vector3(Mathf.Cos(angle),.25f+(i%3)*.15f,Mathf.Sin(angle)).normalized;
                sparkMesh.Box(direction*(.18f+(i%3)*.04f),new Vector3(.016f,.016f,.085f),
                    Quaternion.LookRotation(direction),i%3==0?RoomSiegeStyle.Gold:RoomSiegeStyle.Friendly);
            }
            sparkMesh.Upload();
            for(int i=0;i<Capacity;i++)
            {
                var go=new GameObject("Trap explosion "+i);go.transform.SetParent(transform,false);
                var burst=new Burst{Root=go.transform};
                burst.Shock=TowerVisuals.Ring(go.transform,"Expanding shock ring",material,RoomSiegeStyle.Friendly,1,.025f);
                burst.Echo=TowerVisuals.Ring(go.transform,"Delayed echo ring",material,RoomSiegeStyle.Gold,1,.014f);
                burst.Flash=TowerVisuals.Ring(go.transform,"Ignition flash",material,Color.white,1,.045f);
                burst.Flash.transform.localRotation=Quaternion.Euler(65,0,0);
                var shardObject=new GameObject("Burst sparks");shardObject.transform.SetParent(go.transform,false);
                shardObject.AddComponent<MeshFilter>().sharedMesh=sparkMesh.Mesh;
                burst.Sparks=shardObject.AddComponent<MeshRenderer>();burst.Sparks.sharedMaterial=material;
                burst.Sparks.shadowCastingMode=UnityEngine.Rendering.ShadowCastingMode.Off;burst.Sparks.receiveShadows=false;
                burst.Shards=shardObject.transform;
                bursts[i]=burst;go.SetActive(false);
            }
        }

        public void Emit(Vector3 groundPosition,int level)
        {
            if(bursts[0]==null||!Finite(groundPosition))return;
            var burst=bursts[cursor];cursor=(cursor+1)%Capacity;
            burst.Age=0;burst.Scale=1+.1f*(Mathf.Clamp(level,1,3)-1);
            burst.Root.SetPositionAndRotation(groundPosition+Vector3.up*.035f,Quaternion.identity);
            burst.Root.gameObject.SetActive(true);Paint(burst);
        }

        public void Tick(float dt,bool paused)
        {
            if(paused||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            foreach(var burst in bursts)
            {
                if(burst==null||!burst.Root.gameObject.activeSelf)continue;
                burst.Age+=dt;
                if(burst.Age>=Duration){burst.Root.gameObject.SetActive(false);continue;}
                Paint(burst);
            }
        }

        private void Paint(Burst burst)
        {
            float t=burst.Age/Duration;
            float expansion=1-Mathf.Pow(1-t,3);
            float radius=Mathf.Lerp(.08f,.65f,expansion)*burst.Scale;
            burst.Shock.transform.localScale=Vector3.one*radius;
            PaintLine(burst.Shock,RoomSiegeStyle.Friendly,Mathf.Pow(1-t,1.2f),.026f*(1-t)+.008f);
            float echo=Mathf.Clamp01((t-.12f)/.88f);
            burst.Echo.enabled=t>=.12f;
            burst.Echo.transform.localScale=Vector3.one*Mathf.Lerp(.06f,.5f,1-Mathf.Pow(1-echo,3))*burst.Scale;
            PaintLine(burst.Echo,RoomSiegeStyle.Gold,(1-echo)*.8f,.018f);
            float flash=Mathf.Clamp01(1-burst.Age/.16f);
            burst.Flash.enabled=flash>0;
            burst.Flash.transform.localScale=Vector3.one*(.08f+(1-flash)*.13f)*burst.Scale;
            PaintLine(burst.Flash,Color.Lerp(RoomSiegeStyle.Gold,Color.white,flash),flash,.04f);
            burst.Shards.localScale=Vector3.one*Mathf.Lerp(.18f,2.2f,expansion)*burst.Scale;
            burst.Shards.localPosition=Vector3.up*(Mathf.Sin(t*Mathf.PI)*.09f);
            tint.Clear();tint.SetColor("_Color",new Color(1,1,1,(1-t)*.95f));
            burst.Sparks.SetPropertyBlock(tint);
        }

        private static void PaintLine(LineRenderer line,Color color,float alpha,float width)
        {color.a=alpha;line.startColor=line.endColor=color;line.startWidth=line.endWidth=width;}
        private static bool Finite(Vector3 value)=>!float.IsNaN(value.x)&&!float.IsNaN(value.y)&&!float.IsNaN(value.z)&&
            !float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
        public void Clear(){foreach(var burst in bursts)if(burst!=null)burst.Root.gameObject.SetActive(false);cursor=0;}
        private void OnDestroy(){if(sparkMesh!=null)sparkMesh.Dispose();}
    }
}
