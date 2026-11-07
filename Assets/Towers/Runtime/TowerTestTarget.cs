using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Towers
{
    // A bounded test double only: no pathfinding, wave spawning, leaks, or kill rewards.
    public sealed class TowerTestTarget : MonoBehaviour, ITowerTarget
    {
        private readonly SlowEffectStack slows=new SlowEffectStack();
        private Vector3 pathA,pathB;
        private float pathT;
        private bool moving;
        private float baseSpeed;
        private float hp;
        private float maxHp;
        private TextMesh label;
        private LineRenderer halo;
        // Negative local IDs keep test dummies distinct from positive enemy spawn IDs.
        private static int nextTargetId;
        private readonly int targetId = System.Threading.Interlocked.Decrement(ref nextTargetId);
        public int TargetId => targetId;
        public Vector3 Position => transform.position;
        public Vector3 PreviousPosition { get; private set; }
        public float HitRadius => .11f;
        public bool IsAlive => this!=null && hp>0 && gameObject.activeInHierarchy;
        public float Hp => hp;
        public float SlowFraction=>slows.Fraction;
        public int SlowSourceCount=>slows.Count;
        public void Initialize(Vector3 a,Vector3 b,bool move,float hitPoints,float speed,Material material)
        {
            slows.Clear();transform.position=a;PreviousPosition=a;pathA=a;pathB=b;moving=move;baseSpeed=speed;hp=maxHp=hitPoints;
            var collider=gameObject.AddComponent<SphereCollider>();collider.radius=.11f;
            for(int i=0;i<3;i++)
            {
                var ring=TowerVisuals.Ring(transform,"Test dummy wire",material,TowerVisuals.Red,.09f,.008f);
                ring.transform.localRotation=Quaternion.Euler(i==0?0:90,i==2?90:0,0);
            }
            halo=TowerVisuals.Ring(transform,"Focus fire halo",material,TowerVisuals.Red,.16f);
            halo.enabled=false;
            var text=new GameObject("Test target health");text.transform.SetParent(transform,false);text.transform.localPosition=Vector3.up*.19f;
            label=text.AddComponent<TextMesh>();label.font=Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");label.characterSize=.014f;label.fontSize=30;label.anchor=TextAnchor.MiddleCenter;
            label.color=RoomSiege.Visuals.RoomSiegeStyle.Text;
            label.GetComponent<MeshRenderer>().sharedMaterial=label.font.material; RefreshLabel();
        }
        public void Tick(float dt,Camera camera)
        {
            PreviousPosition=Position;
            slows.Tick(dt);
            if(IsAlive && moving)
            {
                pathT+=baseSpeed*(1-SlowFraction)*dt/Mathf.Max(.01f,Vector3.Distance(pathA,pathB));
                transform.position=Vector3.Lerp(pathA,pathB,Mathf.PingPong(pathT,1));
            }
            if(camera!=null && label!=null)label.transform.rotation=Quaternion.LookRotation(label.transform.position-camera.transform.position);
            if(halo!=null)halo.transform.localScale=Vector3.one*(1+.06f*Mathf.Sin(Time.time*5));
            RefreshLabel();
        }
        public void SetFocused(bool value){if(halo!=null)halo.enabled=value&&IsAlive;}
        public void Damage(float amount) { if(!IsAlive || amount<=0 || float.IsNaN(amount)||float.IsInfinity(amount))return;hp=Mathf.Max(0,hp-amount);RefreshLabel(); }
        public void Slow(int sourceId,float fraction,float duration)
        {if(IsAlive)slows.Add(sourceId,fraction,duration);}
        private void RefreshLabel()
        {if(label!=null)label.text=hp<=0?"测试桩 · 已击破":$"测试桩 {hp:0.#}/{maxHp:0.#}\n缓速 {SlowFraction:P0}";}
    }
}
