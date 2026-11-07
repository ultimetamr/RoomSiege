using System.Collections.Generic;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.Enemies
{
    public static class EnemySteering
    {
        // Soft crowd spacing: slide past the neighbor rather than freezing an
        // entire queue. IDs choose opposite sides for a deterministic pair.
        public static Vector3 AvoidNeighbor(Vector3 current,Vector3 desired,Vector3 neighbor,float clearance,int selfId,int neighborId)
        {
            Vector3 motion=desired-current;motion.y=0;
            float step=motion.magnitude;
            if(step<.000001f)return current;
            Vector3 offset=current-neighbor;offset.y=0;
            Vector3 after=desired-neighbor;after.y=0;
            if(after.magnitude>=clearance||Vector3.Dot(motion,offset)>0)return desired;
            Vector3 forward=motion/step;
            Vector3 side=Vector3.Cross(Vector3.up,forward)*(selfId<neighborId?1:-1);
            Vector3 away=offset.sqrMagnitude>.000001f?offset.normalized:side;
            Vector3 slide=forward+side*1.4f+away*.8f;
            Vector3 result=current+slide.normalized*step;result.y=current.y;
            return result;
        }
        public static Vector3 Move(Vector3 feet,Vector3 safe,float speed,float dt,float age,
            int laneId,float radius,bool runner,IReadOnlyList<TowerInstance> towers)
        {
            Vector3 delta=safe-feet;delta.y=0;
            if(delta.sqrMagnitude<.000001f || speed<=0 || dt<=0)return feet;
            Vector3 forward=delta.normalized;
            Vector3 side=Vector3.Cross(Vector3.up,forward);
            // Oscillating direction preserves configured metres/second rather than adding free speed.
            Vector3 direction=runner?(forward+side*Mathf.Sin(age*5.2f+laneId*1.7f)*.65f).normalized:forward;
            // Every non-single-use tower occupies its deployment cell. Runners
            // keep their serpentine bias, while all other enemies also steer
            // around the same-height tower footprint.
            if(towers!=null)
            {
                foreach(var tower in towers)
                {
                    if(tower.Removed || tower.Definition.singleUse || !SameHeight(feet,tower))continue;
                    Vector3 away=feet-tower.Cell.Center;away.y=0;
                    float distance=away.magnitude;
                    float clearance=.20f+radius;
                    if(distance>clearance+.6f)continue;
                    Vector3 normal=distance>.0001f?away/distance:side;
                    float approach=Vector3.Dot(direction,-normal);
                    if(approach<=0 && distance>clearance+.08f)continue;
                    Vector3 tangent=Vector3.Cross(Vector3.up,normal);
                    float progress=Vector3.Dot(tangent,forward);
                    if(Mathf.Abs(progress)<.1f)tangent*=laneId%2==0?1:-1;
                    else if(progress<0)tangent=-tangent;
                    float weight=1-Mathf.Clamp01((distance-clearance)/.6f);
                    direction=(direction*(1-weight)+tangent*(.8f+weight)+normal*weight*.5f).normalized;
                }
            }
            float step=Mathf.Min(speed*dt,delta.magnitude);
            Vector3 next=feet+direction*step;next.y=safe.y;
            if(towers!=null)
            {
                // Reject a whole swept step through a tower, including large delta-time frames.
                foreach(var tower in towers)
                {
                    if(tower.Removed || tower.Definition.singleUse || !SameHeight(feet,tower))continue;
                    float clearance=.20f+radius;
                    if(TowerCombat.SegmentDistanceXZ(tower.Cell.Center,feet,next)>=clearance)continue;
                    Vector3 away=feet-tower.Cell.Center;away.y=0;
                    if(away.sqrMagnitude<.00001f)away=side;
                    Vector3 normal=away.normalized;
                    Vector3 tangent=Vector3.Cross(Vector3.up,normal);
                    float progress=Vector3.Dot(tangent,forward);
                    if(Mathf.Abs(progress)<.1f)tangent*=laneId%2==0?1:-1;
                    else if(progress<0)tangent=-tangent;
                    next=feet+tangent*step;
                    Vector3 edge=next-tower.Cell.Center;edge.y=0;
                    if(edge.magnitude<clearance)next=tower.Cell.Center+edge.normalized*clearance;
                    next.y=safe.y;
                }
            }
            return next;
        }
        private static bool SameHeight(Vector3 feet,TowerInstance tower)=>Mathf.Abs(tower.Cell.Center.y-feet.y)<.35f;
    }
}
