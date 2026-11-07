using System;
using System.Collections;
using System.Collections.Generic;
using RoomSiege.Data;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomSiege.Enemies
{
    [DefaultExecutionOrder(10900)]
    public sealed class EnemyController : MonoBehaviour
    {
        private sealed class SpawnRequest
        {
            public string EnemyId;
            public int Remaining;
            public int Ordinal;
            public int Entrance;
            public bool Summoned;
            public Vector3 Origin;
        }
        public const int MaximumActive = 20;
        public static EnemyController Instance { get;private set; }
        [SerializeField,Min(1)] private float laserShieldMultiplier=2;
        private readonly Queue<SpawnRequest> queue=new Queue<SpawnRequest>();
        private readonly Stack<EnemyActor> pool=new Stack<EnemyActor>();
        private readonly List<EnemyActor> active=new List<EnemyActor>();
        private readonly HashSet<EnemyActor> returning=new HashSet<EnemyActor>();
        private readonly List<EnemyActor> recycleSnapshot=new List<EnemyActor>();
        private GameManager manager;
        private ScanController scanner;
        private TowerHost towers;
        private BalanceCatalog catalog;
        private GridModel grid;
        private Material silhouette,accent,statusMaterial;
        public GruntDeathVisuals DeathVisuals {get;private set;}
        private int nextSpawnId;
        private int pending;
        private float spawnDelay;
        private bool ready;
        private ScanMode lastMode;
        private bool hasMode;
        public bool IsReady=>ready;
        public int ActiveCount=>active.Count-returning.Count;
        public int PendingCount=>pending;
        public bool IsDrained=>pending==0&&ActiveCount==0;
        public float LaserShieldMultiplier=>Mathf.Max(1,laserShieldMultiplier);
        public float SpawnIntervalSeconds=>catalog!=null?catalog.enemyProgression.spawnIntervalSeconds:1f;
        public IReadOnlyList<EnemyActor> ActiveEnemies=>active.AsReadOnly();
        public bool CanSimulate=>ready&&grid!=null&&manager!=null&&!manager.IsFailed&&manager.State==GameState.WAVE&&Time.timeScale>0&&
            (WorldPanelHost.Instance==null||WorldPanelHost.Instance.Follower==null||
             (!WorldPanelHost.Instance.Follower.IsPaused&&!WorldPanelHost.Instance.Navigation.HasOpenPage));
        public event Action<EnemyKill> Killed;
        public event Action<EnemyActor> Spawned;
        public event Action<TowerHit, float> DamageDealt;
        public event Action<EnemyLeak> Leaked;
        public event Action CountsChanged;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if(SceneManager.GetActiveScene().name=="RoomSiege_Start"&&FindObjectOfType<EnemyController>()==null)
                new GameObject("RoomSiege Enemies").AddComponent<EnemyController>();
        }
        private void Awake()
        {if(Instance!=null&&Instance!=this){Destroy(gameObject);return;}Instance=this;}
        private IEnumerator Start()
        {
            while((towers=TowerHost.Instance)==null||towers.Catalog==null||towers.Placement==null||
                (manager=GameManager.Instance)==null||(scanner=ScanController.Instance)==null)yield return null;
            catalog=towers.Catalog;
            var shader=Resources.Load<Shader>("RoomSiegeEnemySilhouette");
            if(shader==null){Debug.LogError("[RoomSiege.Enemies] Enemy silhouette shader missing.");yield break;}
            silhouette=new Material(shader){name="Low saturation enemy silhouettes"};
            accent=new Material(shader){name="Enemy impact highlight"};
            accent.SetColor("_Color",Color.white);
            accent.SetColor("_RimColor",Color.white);
            RoomSiege.Visuals.RoomSiegeStyle.ApplyWorldDetail(silhouette);
            RoomSiege.Visuals.RoomSiegeStyle.ApplyWorldDetail(accent);
            statusMaterial=new Material(Resources.Load<Shader>("RoomSiegeHologram")){name="Shared enemy status lines"};
            DeathVisuals=gameObject.AddComponent<GruntDeathVisuals>();DeathVisuals.Build(silhouette);
            for(int i=0;i<MaximumActive;i++)
            {
                var go=new GameObject("Pooled enemy "+i);go.SetActive(false);go.transform.SetParent(transform,false);
                var actor=go.AddComponent<EnemyActor>();actor.Prepare(this,silhouette,accent,statusMaterial);pool.Push(actor);
            }
            manager.StateChanged+=StateChanged;manager.CampaignReset+=ClearEnemies;scanner.GridChanged+=GridChanged;
            towers.Placement.ActiveEnemyPositions=LivingFeet;
            ready=true;GridChanged(scanner.CurrentGrid);
            gameObject.AddComponent<EnemyLifeDisplay>();
        }
        /// <summary>Queue a group at a scanned entrance. Does not start a wave or grant test currency.</summary>
        public bool Enqueue(string enemyId,int count=1,int entranceIndex=0)
        {
            if(!ready||manager.IsFailed||count<=0||entranceIndex<0||count>int.MaxValue-pending)return false;
            if(!Known(enemyId))return false;
            if(grid!=null&&entranceIndex>=grid.SpawnPoints.Count)return false;
            queue.Enqueue(new SpawnRequest{EnemyId=enemyId,Remaining=count,Entrance=entranceIndex});
            pending+=count;CountsChanged?.Invoke();return true;
        }
        internal void EnqueueSummons(string enemyId,int count,Vector3 origin)
        {
            if(!CanSimulate||count<=0||count>int.MaxValue-pending||!Known(enemyId))return;
            queue.Enqueue(new SpawnRequest{EnemyId=enemyId,Remaining=count,Summoned=true,Origin=origin});
            pending+=count;CountsChanged?.Invoke();
        }
        private bool Known(string id)
        {foreach(var config in catalog.enemies)if(config.id==id)return true;return false;}
        private DifficultyDefinition Difficulty()=>catalog.GetDifficulty(manager.Difficulty==0?"Casual":manager.Difficulty==2?"Hardcore":"Standard");
        private IEnumerable<Vector3> LivingFeet()
        {foreach(var enemy in active)if(enemy.IsAlive)yield return enemy.transform.position;}
        private void GridChanged(GridModel updated)
        {
            if(updated!=null)
            {
                if(hasMode&&lastMode!=updated.Mode)ClearEnemies();
                lastMode=updated.Mode;hasMode=true;
            }
            grid=updated;
        }
        private void StateChanged(GameState state)
        {
            if(state==GameState.RESULT||state==GameState.MENU)ClearEnemies();
        }
        private void Update()
        {
            var panels=WorldPanelHost.Instance;
            bool visualPause=Time.timeScale<=0||panels!=null&&(panels.Follower!=null&&panels.Follower.IsPaused||panels.Navigation.HasOpenPage);
            if(DeathVisuals!=null)DeathVisuals.Tick(Time.deltaTime,visualPause);
            if(!CanSimulate)return;
            float dt=Time.deltaTime;
            // Snapshot iteration: synchronous death/leak callbacks retire actors, recycling is deferred.
            int count=active.Count;
            for(int i=0;i<count&&CanSimulate;i++)
                if(active[i].IsAlive)active[i].Tick(dt,grid,towers.Placement.Towers,towers.Placement.Routes);
            if(!CanSimulate)return;
            spawnDelay=Mathf.Max(0,spawnDelay-dt);
            if(pending>0&&pool.Count>0&&spawnDelay<=0&&grid.SpawnPoints.Count>0)
            {var spawned=SpawnNext();if(spawned!=null)spawnDelay=SpawnIntervalSeconds;}
        }
        private EnemyActor SpawnNext()
        {
            var request=queue.Peek();
            var config=catalog.GetEnemy(request.EnemyId);
            var stats=DifficultyCalculator.ApplyAtWave(config,Difficulty(),Mathf.Max(1,manager.CurrentWave),catalog.enemyProgression);
            var entrance=grid.SpawnPoints[request.Entrance%grid.SpawnPoints.Count];
            Vector3 origin=request.Summoned?request.Origin:entrance.Position;
            Vector3 forward=grid.SafeZoneCenter-origin;forward.y=0;
            if(!request.Summoned)forward=entrance.Forward;
            if(forward.sqrMagnitude<.0001f)forward=entrance.Forward;
            forward.Normalize();Vector3 side=Vector3.Cross(Vector3.up,forward);
            if(!request.Summoned&&towers.Placement.Routes!=null&&towers.Placement.Routes.Available)origin+=forward*.65f;
            float width=request.Summoned?.55f:Mathf.Max(.1f,Mathf.Min(entrance.FootprintSize.x,.8f));
            // Narrow staggered lanes form a group without changing an individual Grunt's straight path.
            float lane=((request.Ordinal%3)-1)*width*.24f;
            origin+=side*lane-forward*(request.Ordinal%2)*.05f;origin.y=grid.SafeZoneCenter.y;
            Vector3 fromSafe=origin-grid.SafeZoneCenter;fromSafe.y=0;
            if(fromSafe.magnitude<GridModel.SafeRadius+.4f)
            {
                Vector3 outward=fromSafe.sqrMagnitude>.0001f?fromSafe.normalized:-forward;
                origin=grid.SafeZoneCenter+outward*(GridModel.SafeRadius+.4f);
            }
            if(towers.Placement.Routes!=null&&!towers.Placement.Routes.TrySpawn(origin,out origin))return null;
            var actor=pool.Pop();active.Add(actor);
            actor.Spawn(++nextSpawnId,config,stats,origin,request.Summoned);
            Spawned?.Invoke(actor);
            request.Ordinal++;request.Remaining--;pending--;
            if(request.Remaining==0)queue.Dequeue();CountsChanged?.Invoke();
            return actor;
        }

        internal Vector3 KeepEnemySeparation(EnemyActor self, Vector3 current, Vector3 desired)
        {
            Vector3 motion=desired-current; motion.y=0;
            float maxStep=motion.magnitude;
            if(maxStep<.000001f)return current;
            foreach(var other in active)
            {
                if(other==null||other==self||!other.IsAlive)continue;
                Vector3 delta=desired-other.transform.position;delta.y=0;
                float required=self.HitRadius+other.HitRadius+.06f;
                if(delta.sqrMagnitude>=required*required)continue;
                var separated=EnemySteering.AvoidNeighbor(current,desired,other.transform.position,required,self.TargetId,other.TargetId);
                var routes=towers?.Placement?.Routes;
                // Crowd avoidance must obey the same room/tower boundaries.
                if(routes==null||routes.SegmentClear(current,separated))desired=separated;
                else
                {
                    // Try the opposite side in a narrow passage. A blocked
                    // actor waits without advancing its walk animation.
                    var opposite=EnemySteering.AvoidNeighbor(current,desired,other.transform.position,required,other.TargetId,self.TargetId);
                    desired=routes.SegmentClear(current,opposite)?opposite:current;
                }
                motion=desired-current;motion.y=0;
            }
            return current+Vector3.ClampMagnitude(desired-current,maxStep);
        }
        internal void ResolveKill(EnemyActor actor,EnemyKill killed)
        {
            if(!returning.Add(actor))return;
            if(actor.SlowFraction>0&&towers.Placement.Perks.ChainRadius>0)
                foreach(var other in active)
                    if(other.IsAlive&&GridBuilder.HorizontalDistance(other.Position,killed.Position)<=towers.Placement.Perks.ChainRadius)
                        other.Slow(-200,.30f,2f);
            // Consumers observe an already-settled wallet; never credit again in a Killed subscriber.
            manager.AddCoins(killed.Gold);CountsChanged?.Invoke();Killed?.Invoke(killed);
        }
        internal void ShowDeath(GruntModel model){if(DeathVisuals!=null)DeathVisuals.Emit(model);}
        internal void ResolveDamage(TowerHit hit, float bodyDamage)
        {
            if (bodyDamage <= 0 || float.IsNaN(bodyDamage) || float.IsInfinity(bodyDamage)) return;
            DamageDealt?.Invoke(hit, bodyDamage);
        }
        internal void ResolveLeak(EnemyActor actor,EnemyLeak leaked)
        {
            if(!returning.Add(actor))return;
            manager.ApplyLeakDamage(leaked.LifeDamage);CountsChanged?.Invoke();Leaked?.Invoke(leaked);
        }
        /// <summary>Clear active and queued actors without kill gold or leak damage.</summary>
        public void ClearEnemies()
        {
            if(DeathVisuals!=null)DeathVisuals.Clear();
            queue.Clear();pending=0;spawnDelay=0;
            foreach(var actor in active)if(actor!=null){actor.Retire();returning.Add(actor);}
            CountsChanged?.Invoke();
        }
        private void LateUpdate()
        {
            if(returning.Count==0)return;
            recycleSnapshot.Clear();recycleSnapshot.AddRange(returning);returning.Clear();
            foreach(var actor in recycleSnapshot)
            {if(actor==null)continue;active.Remove(actor);actor.Recycle();pool.Push(actor);}
            recycleSnapshot.Clear();CountsChanged?.Invoke();
        }
        private void OnDestroy()
        {
            if(manager!=null)manager.StateChanged-=StateChanged;
            if(manager!=null)manager.CampaignReset-=ClearEnemies;
            if(scanner!=null)scanner.GridChanged-=GridChanged;
            if(towers!=null&&towers.Placement!=null)towers.Placement.ActiveEnemyPositions=null;
            foreach(var actor in active)if(actor!=null)actor.Retire();
            if(silhouette!=null)Destroy(silhouette);if(accent!=null)Destroy(accent);
            if(statusMaterial!=null)Destroy(statusMaterial);
            if(Instance==this)Instance=null;
        }
    }
}
