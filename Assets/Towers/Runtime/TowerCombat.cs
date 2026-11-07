using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Towers
{
    // Shared by real enemies and test targets; UI and movement consume the
    // same resolved value. Repeated aura ticks refresh one source, not a layer.
    public sealed class SlowEffectStack
    {
        private struct Effect {public float Fraction,Remaining;}
        private readonly Dictionary<int,Effect> effects=new Dictionary<int,Effect>();
        private readonly List<int> keys=new List<int>();
        public int Count=>effects.Count;
        public float Fraction {get;private set;}
        public void Add(int source,float fraction,float duration)
        {
            if(float.IsNaN(fraction)||float.IsInfinity(fraction)||float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0)return;
            if(fraction<=0)effects.Remove(source);
            else effects[source]=new Effect{Fraction=Mathf.Clamp01(fraction),Remaining=duration};
            Resolve();
        }
        public void Tick(float dt)
        {
            if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
            keys.Clear();keys.AddRange(effects.Keys);
            foreach(int key in keys)
            {
                var effect=effects[key];effect.Remaining-=dt;
                if(effect.Remaining<=0)effects.Remove(key);else effects[key]=effect;
            }
            Resolve();
        }
        public void Clear(){effects.Clear();keys.Clear();Fraction=0;}
        private void Resolve()
        {
            float strongest=0;foreach(var effect in effects.Values)strongest=Mathf.Max(strongest,effect.Fraction);
            Fraction=strongest>=.999f?1f:Mathf.Min(.8f,strongest+Mathf.Max(0,effects.Count-1)*.1f);
        }
    }

    public interface ITowerTarget
    {
        int TargetId { get; }
        Vector3 Position { get; }
        Vector3 PreviousPosition { get; }
        float HitRadius { get; }
        bool IsAlive { get; }
        void Damage(float amount);
        void Slow(int sourceId, float fraction, float duration);
    }

    public sealed class GazeFocus
    {
        public const float DwellSeconds = 0.5f;
        private ITowerTarget candidate;
        private float dwell;
        public ITowerTarget Focused { get; private set; }
        public float Progress => Mathf.Clamp01(dwell / DwellSeconds);
        public void Step(ITowerTarget target, float dt)
        {
            if (Focused != null && !Focused.IsAlive) Focused = null;
            if (target == null || !target.IsAlive || !Valid(dt)) { candidate = null; dwell = 0; return; }
            if (!ReferenceEquals(candidate, target)) { candidate = target; dwell = 0; }
            dwell += dt;
            if (dwell + 0.00001f >= DwellSeconds) Focused = target;
        }
        public void Clear() { Focused = candidate = null; dwell = 0; }
        public void Mark(ITowerTarget target)
        {if(target==null||!target.IsAlive)return;Focused=candidate=target;dwell=DwellSeconds;}
        private static bool Valid(float f) => !float.IsNaN(f) && !float.IsInfinity(f) && f >= 0;
    }

    public sealed class TowerCombat
    {
        private readonly TowerInstance tower;
        private readonly ArsenalCombat arsenal;
        private readonly Action<TowerInstance> consume;
        private float cooldown,skinClock,focusClock,arcCooldown;
        private int shots;
        private float shellRemaining,shellDamage,shellRadius;
        private Vector3 shellPoint;
        public event Action<Vector3,Vector3,float,float> Launched;
        private ITowerTarget previousTarget;
        private readonly Func<IReadOnlyList<TowerInstance>> allies;
        private readonly List<ITowerTarget> secondary=new List<ITowerTarget>();
        private readonly List<ITowerTarget> hits = new List<ITowerTarget>();
        public ITowerTarget Target { get; private set; }
        public event Action<Vector3, Vector3, bool> Fired;
        public TowerCombat(TowerInstance tower, Action<TowerInstance> consume,Func<IReadOnlyList<TowerInstance>> allies=null) { this.tower = tower; this.consume = consume;this.allies=allies;arsenal=new ArsenalCombat(tower,(a,b,c)=>Fired?.Invoke(a,b,c),(a,b,c,d)=>Launched?.Invoke(a,b,c,d),allies); }
        private Vector3 Origin => tower.Cell.Center + Vector3.up * (tower.Definition.singleUse ? 0.03f : 0.16f);
        public void Step(float dt, Vector3 safeCenter, IReadOnlyList<ITowerTarget> targets, ITowerTarget focused,
            IReadOnlyCollection<ITowerTarget> reserved = null)
        {
            if (dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt) || tower.Removed) { Target = null; return; }
            if(shellRemaining>0)
            {
                shellRemaining-=dt;
                if(shellRemaining<=0)
                {
                    foreach(var victim in targets)if(TowerTargetRegistry.CanTarget(victim)&&Vector3.Distance(victim.Position,shellPoint)<=shellRadius)
                    {
                        if(tower.Skin?.effect=="siegeShell"&&victim is IArsenalStatusTarget armor)armor.ApplyArmorBreak(armor.IsBoss?.1f:.2f,2);
                        TowerTargetRegistry.Hit(victim,new TowerHit(shellDamage*Mathf.Lerp(1,.35f,Vector3.Distance(victim.Position,shellPoint)/shellRadius),victim.Position,TowerDamageKind.Splash,tower.Id));
                        if(tower.Skin?.effect=="emberShell"&&victim.IsAlive&&victim is ICombatStatusTarget burn)burn.ApplyBurn(shellDamage*.08f,3,tower.Id);
                    }
                    Fired?.Invoke(Origin,shellPoint,false);
                }
            }
            Target = Select(Origin, tower.Range, safeCenter, targets, focused, reserved);
            if(tower.Definition.damageMode=="mortar"&&Target!=null&&Vector3.Distance(Origin,Target.Position)<tower.MinimumRange)
            {
                Target=null;float best=float.PositiveInfinity;
                foreach(var other in targets)if(InRange(other,Origin,tower.Range)&&Vector3.Distance(Origin,other.Position)>=tower.MinimumRange)
                {float score=(other.Position-safeCenter).sqrMagnitude;if(score<best){best=score;Target=other;}}
            }
            if(tower.Definition.damageMode=="arsenal"){arsenal.Step(dt,targets,Target);return;}
            var stats = tower.Stats;
            arcCooldown=Mathf.Max(0,arcCooldown-dt);skinClock+=dt;
            if(!ReferenceEquals(previousTarget,Target))focusClock=0;else if(Target!=null)focusClock+=dt;previousTarget=Target;
            string effect=tower.Skin?.effect??"";
            tower.FrostEnchanted=tower.Definition.id=="Tesla"?effect=="cryoCircuit"&&TowerSkinRules.ColdAlly(tower,allies?.Invoke()):HasFrostAlly();
            switch (tower.Definition.damageMode)
            {
                case "projectile":
                    cooldown = Mathf.Max(-dt, cooldown - dt);
                    if (Target == null) { cooldown = 0; break; }
                    while (cooldown <= 0 && Target.IsAlive)
                    {
                        var impact = Target.Position;
                        Hit(Target, stats.damagePerHit, TowerDamageKind.Projectile);
                        shots++;
                        if(tower.FrostEnchanted&&Target is ICombatStatusTarget chilled)chilled.AddChill(1);
                        if(effect=="pierce"&&shots%5==0)Pierce(Target,impact,targets,stats.damagePerHit*.6f);
                        Fired?.Invoke(Origin, impact, false);
                        if (stats.splashEnabled && stats.splashRadiusMeters.specified && stats.splashDamage.specified)
                            foreach (var other in targets)
                                if (!ReferenceEquals(other, Target) && TowerTargetRegistry.CanTarget(other) && Vector3.Distance(other.Position, impact) <= stats.splashRadiusMeters.value)
                                    Hit(other, stats.splashDamage.value, TowerDamageKind.Splash);
                        cooldown += 1f / Mathf.Max(0.01f, stats.shotsPerSecond*tower.RateMultiplier*(tower.Definition.id=="MG"&&tower.Perks!=null?tower.Perks.MgRate:1));
                    }
                    break;
                case "slowArea":
                    ITowerTarget frostTarget = null;
                    foreach (var other in targets)
                        if (InFrostArea(other, Origin, tower.Range))
                        {
                            frostTarget ??= other;
                            other.Slow(tower.Id, Mathf.Min(.8f,stats.slowFraction+tower.ControlBonus+(tower.OverloadRemaining>0?tower.OverloadControlIncrease:0)), stats.slowDurationSeconds.value+tower.MasteryRank+(effect=="lingering"?.5f:0));
                            if(effect=="pulse"&&skinClock>=8&&other is ICombatStatusTarget cold)cold.AddChill(2);
                            if (stats.damagePerSecond > 0) Hit(other, stats.damagePerSecond * dt*tower.OverloadOutputMultiplier, TowerDamageKind.Frost);
                        }
                    if(effect=="pulse"&&frostTarget!=null&&skinClock>=8)skinClock=0;
                    if (frostTarget != null) Fired?.Invoke(Origin, frostTarget.Position, true);
                    break;
                case "beam":
                    if (Target == null) break;
                    Vector3 direction = (Target.Position - Origin).normalized;
                    hits.Clear();
                    foreach (var other in targets)
                    {
                        if (!InRange(other, Origin, tower.Range)) continue;
                        Vector3 offset = other.Position - Origin;
                        float along = Vector3.Dot(offset, direction);
                        if (along >= 0 && (offset - direction * along).sqrMagnitude <= other.HitRadius * other.HitRadius) hits.Add(other);
                    }
                    hits.Sort((a,b) => Vector3.Dot(a.Position-Origin,direction).CompareTo(Vector3.Dot(b.Position-Origin,direction)));
                    int count = Mathf.Min(stats.pierceTargetCount+tower.MasteryRank, hits.Count);
                    for (int i = 0; i < count; i++)
                    {
                        var victim=hits[i];float shieldBefore=(victim as ICombatStatusTarget)?.ShieldPoints??0;
                        float focus=effect=="focus"?1+Mathf.Min(.12f,focusClock*.03f):1;
                        Hit(victim,stats.damagePerSecond*dt*focus*(1+tower.AuraRateBonus)*tower.OverloadOutputMultiplier,TowerDamageKind.Laser);
                        if(effect=="arc"&&arcCooldown<=0&&shieldBefore>0&&((victim as ICombatStatusTarget)?.ShieldPoints??0)<=0)
                        {arcCooldown=2;Arc(victim,targets,stats.damagePerSecond*.5f);}
                    }
                    if(effect=="refract"&&count>0)
                        foreach(var other in targets)if(!hits.Contains(other)&&TowerTargetRegistry.CanTarget(other)&&Vector3.Distance(other.Position,Target.Position)<=.8f)
                        {Hit(other,stats.damagePerSecond*dt*.3f*(1+tower.AuraRateBonus)*tower.OverloadOutputMultiplier,TowerDamageKind.Laser);Fired?.Invoke(Target.Position,other.Position,true);break;}
                    if (count > 0) Fired?.Invoke(Origin, hits[count - 1].Position, true);
                    break;
                case "chain":
                    cooldown=Mathf.Max(0,cooldown-dt);
                    if(Target==null||cooldown>0) break;
                    secondary.Clear();secondary.Add(Target);var chainHits=secondary;
                    float chainDamage=stats.damagePerHit;
                    for(int hop=1;hop<Mathf.Max(1,stats.pierceTargetCount+tower.MasteryRank+(effect=="relay"?1:0));hop++)
                    {
                        ITowerTarget next=null;float best=float.MaxValue;var from=chainHits[chainHits.Count-1].Position;
                        foreach(var other in targets)if(!chainHits.Contains(other)&&TowerTargetRegistry.CanTarget(other))
                        {float d=Vector3.Distance(from,other.Position);if(d<=stats.splashRadiusMeters.value&&d<best){best=d;next=other;}}
                        if(next==null)break;chainHits.Add(next);
                    }
                    Vector3 arcFrom=Origin;
                    foreach(var victim in chainHits)
                    {
                        Vector3 arcTo=victim.Position;Hit(victim,chainDamage,TowerDamageKind.Projectile);
                        if(tower.FrostEnchanted&&victim.IsAlive&&victim is ICombatStatusTarget cold)cold.AddChill(1);
                        Fired?.Invoke(arcFrom,arcTo,false);arcFrom=arcTo;chainDamage*=.7f;
                    }
                    cooldown += 1f/Mathf.Max(.01f,stats.shotsPerSecond*tower.RateMultiplier); break;
                case "mortar":
                    cooldown=Mathf.Max(0,cooldown-dt);
                    if(Target==null||shellRemaining>0||cooldown>0)break;
                    shellPoint=Target.Position;shellDamage=stats.damagePerHit*tower.DamageMultiplier;shellRadius=TowerSkinRules.MortarRadius(tower);
                    shellRemaining=tower.ImpactDelay;Launched?.Invoke(Origin,shellPoint,shellRemaining,shellRadius);
                    cooldown=1f/Mathf.Max(.01f,stats.shotsPerSecond*tower.RateMultiplier);
                    break;
                case "trap":
                    foreach (var other in targets)
                    {
                        if (!TowerTargetRegistry.CanTarget(other) || Mathf.Abs(other.Position.y - Origin.y) > 0.35f) continue;
                        if (SegmentDistanceXZ(Origin, other.PreviousPosition, other.Position) > tower.Range) continue;
                        Target = other;
                        bool slowed=other is ICombatStatusTarget controlled&&controlled.ControlFraction>0;
                        if (stats.slowFraction > 0) other.Slow(tower.Id,stats.slowFraction,stats.slowDurationSeconds.value);
                        Hit(other, stats.damagePerHit, TowerDamageKind.Trap);
                        if(effect=="blast"||effect=="burn"||effect=="shatter")
                        {
                            secondary.Clear();secondary.Add(other);
                            foreach(var victim in targets)if(!ReferenceEquals(victim,other)&&TowerTargetRegistry.CanTarget(victim)&&Vector3.Distance(victim.Position,other.Position)<=.9f)secondary.Add(victim);
                            foreach(var victim in secondary)
                            {
                                if(effect=="blast"&&!ReferenceEquals(victim,other))Hit(victim,stats.damagePerHit*Mathf.Max(.35f,1-Vector3.Distance(victim.Position,other.Position)/.9f),TowerDamageKind.Trap);
                                if(victim is ICombatStatusTarget status)
                                {if(effect=="burn")status.ApplyBurn(stats.damagePerHit*tower.DamageMultiplier*.06f,3,tower.Id);if(effect=="shatter"&&slowed)status.AddChill(5);}
                            }
                        }
                        Fired?.Invoke(Origin, other.Position, false);
                        consume(tower); return;
                    }
                    break;
            }
        }

        private void Hit(ITowerTarget target, float amount, TowerDamageKind kind)
        {
            Vector3 direction = (target.Position - Origin).normalized;
            var point = target.Position - direction * target.HitRadius;
            TowerTargetRegistry.Hit(target, new TowerHit(amount*tower.DamageMultiplier, point, kind, tower.Id));
        }

        private bool HasFrostAlly()
        {
            if(tower.Definition.id!="MG"||allies==null)return false;
            foreach(var other in allies())
                if(other!=null&&!other.Removed&&other.Definition.id=="Frost"&&
                    (tower.Skin?.effect=="frostAmmo"||other.Skin?.effect=="winter")&&
                    Vector3.Distance(other.Cell.Center,tower.Cell.Center)<=1.2f)return true;
            return false;
        }
        private void Pierce(ITowerTarget primary,Vector3 impact,IReadOnlyList<ITowerTarget> targets,float amount)
        {
            Vector3 direction=(impact-Origin).normalized;ITowerTarget best=null;float nearest=float.MaxValue;
            foreach(var other in targets)
            {
                if(ReferenceEquals(other,primary)||!InRange(other,Origin,tower.Range))continue;
                Vector3 offset=other.Position-Origin;float along=Vector3.Dot(offset,direction);
                if(along<=Vector3.Distance(impact,Origin)||along>=nearest||(offset-direction*along).sqrMagnitude>other.HitRadius*other.HitRadius)continue;
                best=other;nearest=along;
            }
            if(best!=null){Hit(best,amount,TowerDamageKind.Projectile);Fired?.Invoke(impact,best.Position,false);}
        }
        private void Arc(ITowerTarget primary,IReadOnlyList<ITowerTarget> targets,float amount)
        {
            int count=0;
            foreach(var other in targets)
                if(!ReferenceEquals(other,primary)&&TowerTargetRegistry.CanTarget(other)&&Vector3.Distance(other.Position,primary.Position)<=1)
                {Hit(other,amount,TowerDamageKind.Laser);Fired?.Invoke(primary.Position,other.Position,false);if(++count>=2)break;}
        }

        public static ITowerTarget Select(Vector3 origin, float range, Vector3 safeCenter, IReadOnlyList<ITowerTarget> targets, ITowerTarget focused,
            IReadOnlyCollection<ITowerTarget> reserved = null)
        {
            if (focused != null && focused.IsAlive && InRange(focused, origin, range)) return focused;
            var best = SelectPass(origin, range, safeCenter, targets, reserved, false);
            return best ?? SelectPass(origin, range, safeCenter, targets, reserved, true);
        }

        private static ITowerTarget SelectPass(Vector3 origin, float range, Vector3 safeCenter,
            IReadOnlyList<ITowerTarget> targets, IReadOnlyCollection<ITowerTarget> reserved, bool includeReserved)
        {
            ITowerTarget best = null;
            float distance = float.PositiveInfinity;
            foreach (var other in targets)
            {
                if (!InRange(other, origin, range)) continue;
                if (!includeReserved && IsReserved(reserved, other)) continue;
                var offset = other.Position - safeCenter; offset.y = 0;
                float score = offset.sqrMagnitude;
                if (score < distance || (score == distance && best != null && other.TargetId < best.TargetId))
                { best = other; distance = score; }
            }
            return best;
        }

        private static bool IsReserved(IReadOnlyCollection<ITowerTarget> reserved, ITowerTarget target)
        {
            if (reserved == null) return false;
            foreach (var item in reserved) if (ReferenceEquals(item, target)) return true;
            return false;
        }
        private static bool InRange(ITowerTarget other, Vector3 origin, float range) =>
            TowerTargetRegistry.CanTarget(other) && (other.Position - origin).sqrMagnitude <= range * range;
        private static bool InFrostArea(ITowerTarget other,Vector3 origin,float radius)
        {
            if(!TowerTargetRegistry.CanTarget(other))return false;
            Vector3 delta=other.Position-origin;
            // The displayed Frost footprint is a horizontal circle. Elevated
            // table/cabinet placements must not silently shrink it into a sphere.
            return Mathf.Abs(delta.y)<=1f&&delta.x*delta.x+delta.z*delta.z<=radius*radius;
        }
        public static float SegmentDistanceXZ(Vector3 p, Vector3 a, Vector3 b)
        {
            p.y = a.y = b.y = 0;
            Vector3 delta = b - a;
            float t = delta.sqrMagnitude > 0.000001f ? Mathf.Clamp01(Vector3.Dot(p - a, delta) / delta.sqrMagnitude) : 0;
            return Vector3.Distance(p, a + delta * t);
        }
    }
}
