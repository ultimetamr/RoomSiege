using System;
using System.Collections.Generic;
using RoomSiege.Data;
using RoomSiege.Scan;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.Enemies
{
    public sealed class EnemyActor : MonoBehaviour,ITowerTarget,ITowerHitReceiver,ITowerFocusVisual,ITowerTargetReadiness,ICombatStatusTarget,IArsenalStatusTarget
    {
        public const float EntranceProtectionSeconds=.8f;
        private readonly SlowEffectStack slows=new SlowEffectStack();
        private readonly FrostControl frost=new FrostControl();
        private float burnRemaining,burnDps,disruptRemaining,decoyRemaining;
        private int burnSource;
        private float armorBreakRemaining,armorBreakFraction;
        public float ArmorBreakFraction=>armorBreakRemaining>0?armorBreakFraction:0;
        private TowerRouteMap decoyRoutes;
        public bool IsFrozen=>frost.Frozen;
        public int ChillStacks=>frost.Stacks;
        public float FreezeProtection=>frost.ProtectionRemaining;
        public float ShieldPoints=>Shield;
        public float ControlFraction=>SlowFraction;
        private EnemyController owner;
        private EnemyVisual visual;
        private EnemyStatusVisual statusVisual;
        private EnemyDefinition definition;
        private EnemyStats stats;
        private BoxCollider body;
        private float age,summonClock,height,radius;
        private bool summonFirstPending;
        private Vector3 previous;
        private bool alive,summoned;
        private int spawnFrame;
        public float AliveSeconds=>age;
        public bool CanBeTargeted=>IsAlive&&age>=EntranceProtectionSeconds&&Time.frameCount>spawnFrame;
        public int TargetId { get;private set; }
        public bool IsBoss=>EnemyId=="Boss";
        public string EnemyId=>definition!=null?definition.id:"";
        public Vector3 Position=>transform.position+Vector3.up*height*.5f;
        public Vector3 PreviousPosition=>previous;
        public float HitRadius=>radius;
        public bool IsAlive=>this!=null&&alive&&gameObject.activeInHierarchy;
        public float HitPoints { get;private set; }
        public float Shield { get;private set; }
        public float MaximumHitPoints=>stats.HitPoints;
        public float Armor=>stats.Armor;
        public float MaximumShield=>stats.HasShield&&stats.ShieldHitPoints.specified?stats.ShieldHitPoints.value:0;
        public float SlowFraction=>IsFrozen?1:slows.Fraction;
        public int SlowSourceCount=>slows.Count;
        public bool IsSummoned=>summoned;
        public float MoveSpeed=>stats.MoveSpeed;
        public event Action<TowerHit> HitReceived;

        internal void Prepare(EnemyController controller,Material silhouette,Material accent,Material statusMaterial)
        {
            owner=controller;body=gameObject.AddComponent<BoxCollider>();
            visual=gameObject.AddComponent<EnemyVisual>();visual.Build(silhouette,accent);
            statusVisual=gameObject.AddComponent<EnemyStatusVisual>();statusVisual.Build(this,statusMaterial);
        }
        internal void Spawn(int spawnId,EnemyDefinition config,EnemyStats resolved,Vector3 feet,bool isSummoned)
        {
            TargetId=spawnId;definition=config;stats=resolved;summoned=isSummoned;
            HitPoints=stats.HitPoints;Shield=stats.HasShield&&stats.ShieldHitPoints.specified?stats.ShieldHitPoints.value:0;
            slows.Clear();frost.Clear();burnRemaining=burnDps=disruptRemaining=decoyRemaining=0;decoyRoutes=null;age=summonClock=0;spawnFrame=Time.frameCount;
            armorBreakRemaining=armorBreakFraction=0;
            summonFirstPending=config.summon!=null&&config.summon.enabled&&config.summon.periodic;
            height=config.id=="Boss"?.72f:config.id=="Tank"?.52f:config.id=="Runner"?.28f:.34f;
            radius=config.id=="Boss"?GridModel.BossBodyRadius:config.id=="Tank"?.20f:config.id=="Runner"?.09f:.12f;
            transform.SetPositionAndRotation(feet,Quaternion.identity);previous=Position;
            body.center=Vector3.up*height*.5f;body.size=new Vector3(radius*2,height,radius*2);body.enabled=true;
            visual.Configure(config.id,height,radius,Shield>0);alive=true;gameObject.SetActive(true);
            statusVisual.ResetForSpawn(height,radius);
            TowerTargetRegistry.Register(this);
        }
        internal void Tick(float dt,GridModel grid,IReadOnlyList<TowerInstance> towers,TowerRouteMap routes=null)
        {
            if(!IsAlive || dt<=0 || grid==null)return;
            previous=Position;age+=dt;
            slows.Tick(dt);frost.Tick(dt);
            armorBreakRemaining=Mathf.Max(0,armorBreakRemaining-dt);if(armorBreakRemaining<=0)armorBreakFraction=0;
            disruptRemaining=Mathf.Max(0,disruptRemaining-dt);decoyRemaining=Mathf.Max(0,decoyRemaining-dt);
            if(burnRemaining>0){float burnDt=Mathf.Min(dt,burnRemaining);burnRemaining-=dt;ReceiveHit(new TowerHit(burnDps*burnDt,Position,TowerDamageKind.Frost,burnSource));if(!IsAlive)return;}
            Vector3 feet=transform.position;
            feet.y=grid.SafeZoneCenter.y;
            if(Reached(feet,grid.SafeZoneCenter)) { Leak();return; }
            float movementDt=CanBeTargeted?dt:0;
            var movementRoutes=decoyRemaining>0&&decoyRoutes!=null?decoyRoutes:routes;
            Vector3 next=movementRoutes!=null&&movementRoutes.Available?
                movementRoutes.Move(feet,stats.MoveSpeed*(1-SlowFraction),movementDt,age,TargetId,definition.movementPattern=="serpentine"):
                EnemySteering.Move(feet,grid.SafeZoneCenter,stats.MoveSpeed*(1-SlowFraction),movementDt,age,TargetId,radius,
                    definition.movementPattern=="serpentine",towers);
            next=owner.KeepEnemySeparation(this,feet,next);
            // Separation may stop an actor for a frame. Drive both heading and
            // the walk cycle from the final displacement, otherwise a blocked
            // enemy appears to accelerate in place while its feet do not move.
            Vector3 actualMotion=next-feet;
            if(actualMotion.sqrMagnitude>.000001f)
                transform.rotation=Quaternion.RotateTowards(transform.rotation,Quaternion.LookRotation(actualMotion),360*dt);
            transform.position=next;
            if(TowerCombat.SegmentDistanceXZ(grid.SafeZoneCenter,feet,next)<=GridModel.SafeRadius+radius){Leak();return;}
            var summon=definition.summon;
            if(summon!=null&&summon.enabled&&summon.periodic&&summon.intervalSeconds.specified&&summon.countPerSummon.specified)
            {
                if(disruptRemaining<=0&&!IsFrozen)summonClock+=dt;
                float interval=Mathf.Max(.1f,summon.intervalSeconds.value);
                float firstDelay=summon.initialDelaySeconds.specified?Mathf.Max(.1f,summon.initialDelaySeconds.value):interval;
                float due=summonFirstPending?firstDelay:interval;
                while(summonClock>=due&&IsAlive)
                {summonClock-=due;summonFirstPending=false;owner.EnqueueSummons(summon.enemyId,summon.countPerSummon.value,transform.position);due=interval;}
            }
            visual.Tick(dt,Shield>0,SlowFraction,new Vector2(actualMotion.x,actualMotion.z).magnitude);
        }
        private bool Reached(Vector3 feet,Vector3 safe)=>TowerCombat.SegmentDistanceXZ(safe,feet,feet)<=GridModel.SafeRadius+radius;
        public void Slow(int sourceId,float fraction,float duration)
        {if(!IsAlive)return;if(fraction>=.999f){TryFreeze(duration);return;}slows.Add(sourceId,fraction,duration);}
        public bool AddChill(int stacks)=>IsAlive&&CanBeTargeted&&frost.Chill(stacks,EnemyId=="Boss");
        public bool TryFreeze(float seconds)=>IsAlive&&frost.Freeze(seconds,EnemyId=="Boss");
        public void ApplyBurn(float dps,float duration,int source)
        {if(!IsAlive||dps<=0||duration<=0||float.IsNaN(dps)||float.IsInfinity(dps))return;burnDps=Mathf.Max(burnDps,dps);burnRemaining=Mathf.Max(burnRemaining,duration);burnSource=source;}
        public bool CanFreeze=>IsAlive&&CanBeTargeted&&frost.CanFreeze;
        public void ApplyArmorBreak(float fraction,float seconds)
        {
            if(!CanBeTargeted||float.IsNaN(fraction)||float.IsInfinity(fraction)||float.IsNaN(seconds)||float.IsInfinity(seconds)||seconds<=0)return;
            armorBreakFraction=Mathf.Max(armorBreakFraction,Mathf.Clamp01(fraction));armorBreakRemaining=Mathf.Max(armorBreakRemaining,seconds);
        }
        public void Disrupt(float shieldFraction,float seconds)
        {if(!CanBeTargeted)return;Shield*=1-Mathf.Clamp01(shieldFraction);disruptRemaining=Mathf.Max(disruptRemaining,seconds);}
        public bool SetDecoy(TowerRouteMap routes,float seconds)
        {if(!CanBeTargeted||EnemyId=="Boss"||routes==null||!routes.CanReach(transform.position))return false;decoyRoutes=routes;decoyRemaining=seconds;return true;}
        public void Damage(float amount)=>ReceiveHit(new TowerHit(amount,Position,TowerDamageKind.Projectile,0));
        public void ReceiveHit(TowerHit hit)
        {
            if(!IsAlive||hit.Amount<=0||float.IsNaN(hit.Amount)||float.IsInfinity(hit.Amount)||!owner.CanSimulate)return;
            // Enter visibly before tower fire can erase a newly pooled actor in its spawn frame.
            if(hit.SourceTowerId>0&&!CanBeTargeted)return;
            HitReceived?.Invoke(hit);
            // Apply once before shield absorption; shield overflow must not be mitigated twice.
            float remaining=DifficultyCalculator.DamageAfterArmor(hit.Amount,stats.Armor*(1-ArmorBreakFraction)*(1-hit.IgnoreArmorFraction));
            if(Shield>0)
            {
                float multiplier=hit.Kind==TowerDamageKind.Laser?owner.LaserShieldMultiplier:1;
                float absorbed=Mathf.Min(remaining,Shield/multiplier);
                Shield=Mathf.Max(0,Shield-absorbed*multiplier);remaining=Mathf.Max(0,remaining-absorbed);
            }
            float bodyDamage=Mathf.Min(HitPoints,remaining);
            HitPoints=Mathf.Max(0,HitPoints-remaining);
            owner.ResolveDamage(hit,bodyDamage);
            Vector3 point=hit.Point;
            if(float.IsNaN(point.x)||float.IsNaN(point.y)||float.IsNaN(point.z)||float.IsInfinity(point.x)||float.IsInfinity(point.y)||float.IsInfinity(point.z))point=Position;
            visual.Highlight(point,Shield>0);
            if(HitPoints<=0)
            {
                var killed=new EnemyKill(TargetId,definition.id,Position,stats.KillGold,summoned);
                owner.ShowDeath(visual.Grunt);
                Retire();owner.ResolveKill(this,killed);
            }
        }
        private void Leak()
        {if(!IsAlive)return;var leaked=new EnemyLeak(TargetId,definition.id,stats.LeakDamage);Retire();owner.ResolveLeak(this,leaked);}
        internal void Retire()
        {alive=false;body.enabled=false;TowerTargetRegistry.Unregister(this);visual.SetFocused(false);visual.Hide();statusVisual.Hide();}
        internal void Recycle()
        {Retire();slows.Clear();gameObject.SetActive(false);}
        public void SetFocused(bool focused){if(visual!=null)visual.SetFocused(focused&&IsAlive);if(statusVisual!=null)statusVisual.SetFocused(focused&&IsAlive);}
        private void OnDisable(){alive=false;TowerTargetRegistry.Unregister(this);}
        private void OnDestroy(){TowerTargetRegistry.Unregister(this);}
    }
}
