using System;
using System.Collections.Generic;
using RoomSiege.Data;
using UnityEngine;

namespace RoomSiege.Towers
{
    public interface IArsenalStatusTarget
    {
        bool IsBoss {get;}
        void ApplyArmorBreak(float fraction,float seconds);
        void Disrupt(float shieldFraction,float seconds);
    }

    public static class TowerAuraSystem
    {
        // Resolve from the unmodified source radius before any tower fires. Order never changes the buff.
        public static void Resolve(IReadOnlyList<TowerInstance> towers,bool active)
        {
            foreach(var tower in towers){tower.AuraRateBonus=0;tower.AuraRangeBonus=0;}
            if(!active)return;
            foreach(var source in towers)
            {
                if(source.Removed||(source.Definition.id!="Beacon"&&source.Definition.id!="Prism"))continue;
                float radius=source.Range;
                foreach(var target in towers)
                {
                    if(target.Removed||target.Definition.singleUse||WeaponRoster.IsSupport(target.Definition.id))continue;
                    if((target.Cell.Center-source.Cell.Center).sqrMagnitude>radius*radius)continue;
                    string effect=source.Skin?.effect;
                    float bonus=effect=="beaconTempo"||effect=="prismBoost"?.04f:
                        effect=="beaconWinter"&&TowerSkinRules.ColdAlly(source,towers)?.06f:0;
                    float strength=(source.Stats.effectMagnitude+bonus)*source.OverloadOutputMultiplier*source.EffectMultiplier;
                    if(source.Definition.id=="Beacon")target.AuraRateBonus=Mathf.Max(target.AuraRateBonus,strength);
                    else target.AuraRangeBonus=Mathf.Max(target.AuraRangeBonus,strength);
                    if(source.Definition.id=="Beacon")target.AuraRangeBonus=Mathf.Max(target.AuraRangeBonus,.05f*source.MasteryRank);
                    else target.AuraRateBonus=Mathf.Max(target.AuraRateBonus,.05f*source.MasteryRank);
                    if(effect=="prismTempo")target.AuraRateBonus=Mathf.Max(target.AuraRateBonus,.06f);
                }
            }
        }
    }

    // Twelve persistent weapons. No gameplay objects, coroutines or extra collision geometry per attack.
    public sealed class ArsenalCombat
    {
        private readonly TowerInstance tower;
        private readonly Action<Vector3,Vector3,bool> fired;
        private readonly Action<Vector3,Vector3,float,float> launched;
        private readonly Func<IReadOnlyList<TowerInstance>> allies;
        private readonly List<ITowerTarget> line=new List<ITowerTarget>();
        private float cooldown,flight,skinClock;
        private int pulses;
        private ITowerTarget missileTarget;
        private int missileTargetId;
        private Vector3 missilePoint;
        private float missileDamage,missileRadius;
        private Vector3 Origin=>tower.Cell.Center+Vector3.up*.16f;
        public ArsenalCombat(TowerInstance tower,Action<Vector3,Vector3,bool> fired,Action<Vector3,Vector3,float,float> launched,Func<IReadOnlyList<TowerInstance>> allies=null)
        {this.tower=tower;this.fired=fired;this.launched=launched;this.allies=allies;}
        public void Step(float dt,IReadOnlyList<ITowerTarget> targets,ITowerTarget target)
        {
            if(dt<=0||float.IsNaN(dt)||float.IsInfinity(dt)||tower.Removed)return;
            var stats=tower.Stats;string id=tower.Definition.id;string effect=tower.Skin?.effect;
            var companions=allies?.Invoke();
            bool coldAlly=TowerSkinRules.ColdAlly(tower,companions);
            tower.FrostEnchanted=coldAlly&&(effect=="scatterFrost"||effect=="bladeFrost"||effect=="gravityFrost");
            if(flight>0)
            {
                if(missileTarget!=null&&missileTarget.TargetId==missileTargetId&&missileTarget.IsAlive)missilePoint=missileTarget.Position;
                flight-=dt;
                if(flight<=0)
                {
                    foreach(var victim in targets)if(Valid(victim)&&Distance(victim.Position,missilePoint)<=missileRadius)
                    {
                        if(effect=="missileEmp"&&victim is IArsenalStatusTarget status)
                        {status.Disrupt(.2f,0);status.ApplyArmorBreak(status.IsBoss?.075f:.15f,2);}
                        Hit(victim,missileDamage,TowerDamageKind.Splash);
                    }
                    fired(Origin,missilePoint,false);missileTarget=null;
                }
            }
            if(id=="Beacon"||id=="Prism")return;
            if(id=="Gravity")
            {
                skinClock+=dt;bool any=false;bool chill=effect=="gravityFrost"&&skinClock>=3;
                foreach(var enemy in targets)if(InRange(enemy))
                {
                    float strength=TowerSkinRules.GravityStrength(tower,companions);
                    if(companions!=null)foreach(var other in companions)
                        if(!other.Removed&&other.Definition.id=="Gravity"&&Mathf.Abs(enemy.Position.y-other.Cell.Center.y)<=1.16f&&Distance(enemy.Position,other.Cell.Center)<=other.Range)
                            strength=Mathf.Max(strength,TowerSkinRules.GravityStrength(other,companions));
                    enemy.Slow(-21000,strength*(Boss(enemy)?.5f:1),dt+.15f+tower.MasteryRank);any=true;
                    if(chill&&enemy is ICombatStatusTarget cold)cold.AddChill(1);
                }
                if(chill&&any)skinClock=0;
                if(any)Pulse(dt,stats,Origin,false);return;
            }
            if(id=="Flame")
            {
                if(!Valid(target))return;
                foreach(var victim in targets)if(InCone(victim,target.Position,35))
                {
                    if(effect=="flameSunder"&&victim is IArsenalStatusTarget status)status.ApplyArmorBreak(status.IsBoss?.075f:.15f,2);
                    Hit(victim,stats.damagePerSecond*dt*tower.RateMultiplier,TowerDamageKind.Frost);
                    if(victim is ICombatStatusTarget burn&&victim.IsAlive)burn.ApplyBurn(stats.effectMagnitude*tower.DamageMultiplier*tower.OverloadOutputMultiplier,stats.effectDuration*(effect=="flameLinger"?2:1)+tower.MasteryRank,tower.Id);
                }
                fired(Origin,target.Position,true);return;
            }
            cooldown=Mathf.Max(0,cooldown-dt);
            if(cooldown>0)return;
            if(id=="Cryo"||id=="Sunder"||id=="Disruptor")
            {
                bool affected=false;int nextPulse=pulses+1;
                foreach(var enemy in targets)if(InRange(enemy))
                {
                    if(id=="Cryo"&&enemy is ICombatStatusTarget cold)
                    {
                        cold.AddChill(Mathf.RoundToInt(stats.effectMagnitude)+tower.PowerRank+(effect=="cryoEcho"&&nextPulse%3==0?1:0));
                        if(effect=="cryoSlow")enemy.Slow(tower.Id,Boss(enemy)?.1f:.2f,2);
                        affected=true;
                    }
                    else if(enemy is IArsenalStatusTarget status)
                    {
                        if(id=="Sunder")status.ApplyArmorBreak(Mathf.Min(.9f,stats.effectMagnitude+.1f*tower.PowerRank+(effect=="sunderDeep"?.1f:0))*(status.IsBoss?.5f:1),stats.effectDuration+tower.MasteryRank+(effect=="sunderLinger"?1:0));
                        else
                        {
                            status.Disrupt(Mathf.Min(.95f,stats.effectMagnitude+.1f*tower.PowerRank+(effect=="shieldRupture"?.1f:0)),
                                (status.IsBoss?.4f:stats.effectDuration)+tower.MasteryRank*(status.IsBoss?.5f:1)+(effect=="disruptSlow"?status.IsBoss?.2f:.6f:0));
                            if(effect=="disruptSlow")enemy.Slow(tower.Id,status.IsBoss?.1f:.2f,1.5f);
                        }
                        affected=true;
                    }
                }
                if(affected){pulses=nextPulse;fired(Origin,Origin,false);Reset(stats);}return;
            }
            if(!Valid(target))return;
            switch(id)
            {
                case "Sniper":Hit(target,stats.damagePerHit*(effect=="sniperExecute"&&TowerSkinRules.Controlled(target)?1.25f:1),TowerDamageKind.Projectile,effect=="sniperPierce"?.75f:.5f);fired(Origin,target.Position,false);break;
                case "Scatter":
                    foreach(var victim in targets)if(InCone(victim,target.Position,(effect=="scatterCone"?40:30)+10*tower.MasteryRank))
                    {
                        Hit(victim,stats.damagePerHit*Mathf.Lerp(1,.6f,Mathf.Clamp01(Distance(victim.Position,Origin)/tower.Range)),TowerDamageKind.Projectile);
                        if(effect=="scatterFrost"&&coldAlly&&victim.IsAlive&&victim is ICombatStatusTarget cold)cold.AddChill(1);
                        fired(Origin,victim.Position,false);
                    }break;
                case "Missile":
                    if(flight>0)return;
                    missileTarget=target;missileTargetId=target.TargetId;missilePoint=target.Position;missileDamage=stats.damagePerHit;missileRadius=stats.splashRadiusMeters.value*(tower.Perks?.WeaponBlast("Missile")??1)*(effect=="missileBlast"?1.2f:1);
                    flight=tower.ImpactDelay;launched(Origin,missilePoint,flight,missileRadius);break;
                case "Railgun":
                    var direction=Flat(target.Position-Origin).normalized;line.Clear();
                    foreach(var victim in targets)if(InRange(victim))
                    {var delta=Flat(victim.Position-Origin);float along=Vector3.Dot(delta,direction);if(along>=0&&(delta-direction*along).sqrMagnitude<=Mathf.Pow(victim.HitRadius+.045f,2))line.Add(victim);}
                    line.Sort((a,b)=>Distance(a.Position,Origin).CompareTo(Distance(b.Position,Origin)));
                    for(int i=0;i<Mathf.Min(line.Count,stats.pierceTargetCount+tower.MasteryRank+(effect=="railPierce"?2:0));i++)Hit(line[i],stats.damagePerHit*Mathf.Pow(effect=="railConserve"?.95f:.85f,i),TowerDamageKind.Projectile,effect=="railConserve"?.25f:0);
                    fired(Origin,Origin+direction*tower.Range,false);break;
                case "Blade":
                    foreach(var victim in targets)if(InRange(victim))
                    {
                        Hit(victim,stats.damagePerHit*(effect=="bladeFrost"&&TowerSkinRules.Controlled(victim)?1.25f:1),TowerDamageKind.Projectile);
                        if(effect=="bladeFrost"&&coldAlly&&victim.IsAlive&&victim is ICombatStatusTarget cold)cold.AddChill(1);
                    }
                    fired(Origin,Origin,false);break;
            }
            Reset(stats);
        }
        private void Reset(TowerLevelDefinition stats)=>cooldown=1f/Mathf.Max(.01f,stats.shotsPerSecond*tower.RateMultiplier);
        private void Pulse(float dt,TowerLevelDefinition stats,Vector3 point,bool continuous)
        {cooldown=Mathf.Max(0,cooldown-dt);if(cooldown<=0){fired(Origin,point,continuous);cooldown=1;}}
        private void Hit(ITowerTarget victim,float amount,TowerDamageKind kind,float ignoreArmor=0)
        {if(Valid(victim))TowerTargetRegistry.Hit(victim,new TowerHit(amount*tower.DamageMultiplier,victim.Position,kind,tower.Id,ignoreArmor));}
        private static bool Valid(ITowerTarget target)=>TowerTargetRegistry.CanTarget(target);
        private static bool Boss(ITowerTarget target)=>target is IArsenalStatusTarget status&&status.IsBoss;
        private static Vector3 Flat(Vector3 p){p.y=0;return p;}
        private static float Distance(Vector3 a,Vector3 b)=>Flat(a-b).magnitude;
        private bool InRange(ITowerTarget target)=>Valid(target)&&Mathf.Abs(target.Position.y-Origin.y)<=1&&Distance(target.Position,Origin)<=tower.Range;
        private bool InCone(ITowerTarget target,Vector3 toward,float degrees)
        {
            if(!InRange(target))return false;
            var delta=Flat(target.Position-Origin);var direction=Flat(toward-Origin);
            return delta.sqrMagnitude<.00001f||direction.sqrMagnitude<.00001f||Vector3.Dot(delta.normalized,direction.normalized)>=Mathf.Cos(degrees*Mathf.Deg2Rad);
        }
    }
}
