using System;
using System.Collections.Generic;
using RoomSiege.Enemies;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.Meta
{
    // One purchased field owns its combat clock; preparation and menus do not spend it.
    public sealed class SupportField
    {
        private readonly SupplyDefinition item;
        private readonly Vector3 point;
        private readonly int source;
        private readonly float damageScale;
        private readonly TowerRouteMap decoy;
        private readonly CampaignPerks perks;
        private int PowerRank=>perks?.Rank(item.id,0,true)??0;
        private float Power=>perks?.SupplyPower(item.id)??1;
        private float PulseInterval=>item.pulseInterval*(item.id=="repair"?Mathf.Pow(.8f,perks?.Rank(item.id,2,true)??0):1);
        public float Duration=>item.duration+(perks?.SupplyLifetime(item.id)??0);
        public float Radius=>item.radius*(perks?.SupplyRange(item.id)??1);
        private readonly HashSet<int> disrupted=new HashSet<int>();
        private float elapsed,nextPulse;
        private bool detonated,expired;
        public float Elapsed=>elapsed;
        public float Remaining=>Finished?0:Mathf.Max(0,Duration-elapsed);
        public bool Finished=>expired||detonated;
        public bool Armed=>elapsed>=item.armSeconds;
        public static float DamageMultiplier(int wave)=>1+.18f*(Mathf.Clamp(wave,1,10)-1);
        public static bool Supports(string id)=>id=="firefield"||id=="gravity"||id=="mine"||id=="thunder"||id=="frostsigil"||
            id=="freeze"||id=="emp"||id=="decoy"||id=="corrosion"||id=="strike"||id=="repair";
        public SupportField(SupplyDefinition definition,Vector3 position,int sourceId,int wave=1,TowerRouteMap decoyRoutes=null,CampaignPerks runPerks=null)
        {
            item=definition;point=position;source=sourceId;damageScale=DamageMultiplier(wave);decoy=decoyRoutes;perks=runPerks;
            nextPulse=item.id=="freeze"?0:item.id=="strike"?item.armSeconds:PulseInterval;
        }
        public void Step(float dt,IReadOnlyList<EnemyActor> targets,Action<Vector3> pulse=null,Action<float> heal=null)
        {
            if(Finished||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            float activeDt=Mathf.Min(dt,Remaining);elapsed=Mathf.Min(Duration,elapsed+dt);expired=elapsed>=Duration;
            if(item.id=="mine")
            {
                if(!Armed)return;
                foreach(var actor in targets)
                    if(Valid(actor)&&TowerCombat.SegmentDistanceXZ(point,actor.PreviousPosition,actor.Position)<=item.triggerRadius*(perks?.SupplyRange(item.id)??1))
                    {
                        detonated=true;
                        foreach(var victim in targets)if(InRange(victim))Hit(victim,item.magnitude);
                        pulse?.Invoke(point);return;
                    }
                return;
            }
            foreach(var actor in targets)if(InRange(actor))
            {
                bool boss=actor.EnemyId=="Boss";
                switch(item.id)
                {
                    case "firefield":actor.ApplyBurn(item.magnitude*damageScale*Power,activeDt+1,source);break;
                    // Shared IDs prevent overlapping copies from adding unintended slow stacks.
                    case "gravity":actor.Slow(-12002,boss?Mathf.Min(.8f,item.magnitude*.5f+.05f*PowerRank):item.magnitude,activeDt+.15f+PowerRank);break;
                    case "freeze":actor.Slow(-12003,Mathf.Min(.8f,(boss?.4f:item.magnitude)+.05f*PowerRank),activeDt+.15f);break;
                    case "emp":
                        actor.Disrupt(disrupted.Add(actor.TargetId)?Mathf.Min(.99f,item.magnitude+.03f*PowerRank):0,Mathf.Min(boss?item.bossDuration:item.statusSeconds,Remaining+activeDt));
                        actor.Slow(-12004,Mathf.Min(.8f,(boss?.3f:.5f)+.05f*PowerRank),activeDt+.15f);break;
                    case "decoy":actor.SetDecoy(decoy,Remaining+activeDt);actor.Slow(-12005,Mathf.Min(.8f,(boss?.35f:item.magnitude)+.05f*PowerRank),activeDt+.15f);break;
                    case "corrosion":actor.ApplyArmorBreak(Mathf.Min(.9f,(boss?item.bossDuration:item.magnitude)+.05f*PowerRank),item.statusSeconds);break;
                    case "frostsigil":actor.Slow(-12006,boss?.35f:.6f,activeDt+.15f);break;
                }
            }
            while(PulseInterval>0&&nextPulse<=elapsed+.00001f&&nextPulse<=Duration+.00001f)
            {
                nextPulse+=PulseInterval;
                if(item.id=="repair"){heal?.Invoke(item.statusSeconds*Power);pulse?.Invoke(point);}
                else if(item.id=="freeze"||item.id=="frostsigil")
                {
                    bool hit=false;foreach(var actor in targets)if(InRange(actor))
                        hit|=item.id=="freeze"?actor.TryFreeze(actor.IsBoss?item.bossDuration:item.statusSeconds+.5f*PowerRank):actor.AddChill(Mathf.RoundToInt(item.magnitude)+PowerRank);
                    if(hit)pulse?.Invoke(point);
                }
                else if(item.id=="strike")
                {
                    foreach(var actor in targets)if(InRange(actor))Hit(actor,item.magnitude);
                    pulse?.Invoke(point);
                }
                else if(item.id=="thunder")
                {
                    EnemyActor target=null;
                    foreach(var actor in targets)if(InRange(actor)&&(target==null||actor.HitPoints>target.HitPoints))target=actor;
                    if(target!=null){Vector3 hitPoint=target.Position;Hit(target,item.magnitude);pulse?.Invoke(hitPoint);}
                }
            }
        }
        private static bool Valid(EnemyActor actor)=>actor!=null&&actor.CanBeTargeted;
        private bool InRange(EnemyActor actor)=>Valid(actor)&&GridBuilder.HorizontalDistance(actor.Position,point)<=Radius;
        private void Hit(EnemyActor actor,float damage)=>TowerTargetRegistry.Hit(actor,new TowerHit(damage*damageScale*Power,actor.Position,TowerDamageKind.Trap,source,.6f));
    }
}
