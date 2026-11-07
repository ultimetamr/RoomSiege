using System.Collections.Generic;
using RoomSiege.Scan;
using UnityEngine;

namespace RoomSiege.Towers
{
    // One reverse breadth-first field shared by all enemies. Rebuilt only when
    // the room or a blocking tower changes, never once per enemy per frame.
    public sealed class TowerRouteMap
    {
        private readonly GridModel grid;
        private readonly RoomWalkMap map;
        private readonly List<Vector3> blockers=new List<Vector3>();
        private readonly bool[] open;
        private readonly int[] distance;
        private bool? entrancesReachable;
        private int openCount,reachableCount,coreSeedCount;
        private string readinessFailure;
        private const float TowerRadius=.20f;
        public bool Available=>map!=null;
        public TowerRouteMap(GridModel grid,IReadOnlyList<TowerInstance> towers,Vector3? extra=null)
        {
            this.grid=grid;map=grid?.WalkMap;
            if(map==null)return;
            foreach(var tower in towers)
                if(!tower.Removed&&!tower.Definition.singleUse&&Mathf.Abs(tower.Cell.Center.y-grid.SafeZoneCenter.y)<.35f)
                    blockers.Add(tower.Cell.Center);
            if(extra.HasValue)blockers.Add(extra.Value);
            open=new bool[map.Count];distance=new int[map.Count];
            var queue=new Queue<int>();
            for(int i=0;i<map.Count;i++)
            {
                open[i]=map.Open(i)&&ClearOfTowers(map.Point(i));distance[i]=-1;
                if(open[i])openCount++;
                if(open[i]&&Horizontal(map.Point(i),grid.SafeZoneCenter)<=GridModel.SafeRadius+.09f)
                {distance[i]=0;queue.Enqueue(i);coreSeedCount++;}
            }
            while(queue.Count>0)
            {
                int current=queue.Dequeue();
                reachableCount++;
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                {
                    if(dx==0&&dz==0)continue;
                    int next=map.Neighbor(current,dx,dz);
                    if(next<0||!open[next]||distance[next]>=0||!EdgeOpen(current,next))continue;
                    distance[next]=distance[current]+1;queue.Enqueue(next);
                }
            }
        }
        private bool ClearOfTowers(Vector3 point)
        {
            // Check the actual largest body here and each connecting segment
            // below. Padding every node by another half-cell rejects valid
            // two-deployment-cell (0.8m) passages between towers.
            foreach(var tower in blockers)
                if(Horizontal(point,tower)<TowerRadius+RoomWalkMap.EnemyClearance)return false;
            return true;
        }
        private bool EdgeOpen(int a,int b)
        {
            int dx=b%map.Width-a%map.Width,dz=b/map.Width-a/map.Width;
            if(dx!=0&&dz!=0&&(!open[map.Neighbor(a,dx,0)]||!open[map.Neighbor(a,0,dz)]))return false;
            return ClearTowerSegment(map.Point(a),map.Point(b));
        }
        private int ReachableNode(Vector3 point,float radius)
        {
            int best=-1;float nearest=radius*radius;
            int origin=map.Index(point);if(origin<0)return -1;
            int span=Mathf.CeilToInt(radius/RoomWalkMap.Step);
            for(int dx=-span;dx<=span;dx++)for(int dz=-span;dz<=span;dz++)
            {
                int node=map.Neighbor(origin,dx,dz);
                if(node<0||distance[node]<0)continue;
                float d=(map.Point(node)-point).sqrMagnitude;
                if(d>nearest||!SegmentClear(point,map.Point(node)))continue;
                nearest=d;best=node;
            }
            return best;
        }
        public bool SegmentClear(Vector3 a,Vector3 b)
        {
            if(map==null)return true;
            if(!map.SegmentOpen(a,b))return false;
            return ClearTowerSegment(a,b);
        }
        private bool ClearTowerSegment(Vector3 a,Vector3 b)
        {
            foreach(var tower in blockers)
                if(TowerCombat.SegmentDistanceXZ(tower,a,b)<TowerRadius+RoomWalkMap.EnemyClearance)return false;
            return true;
        }
        public bool CanReach(Vector3 point)=>map==null||ReachableNode(point,.65f)>=0;
        public bool AllEntrancesReachable()
        {
            if(map==null)return true;
            if(entrancesReachable.HasValue)return entrancesReachable.Value;
            readinessFailure="";
            if(coreSeedCount==0)
            {readinessFailure="核心周围没有可通行地板，请在开阔处点“固定在脚下”";return (entrancesReachable=false).Value;}
            foreach(var spawn in grid.SpawnPoints)
            {
                // Portals lie at the wall; materialize bodies inside the floor.
                Vector3 inward=spawn.Forward;
                Vector3 entry=spawn.Position+inward*.65f;
                if(!CanReach(entry))
                {readinessFailure="出怪口通路被房间边界或家具挡住，请向室内开阔地面重新标定";return (entrancesReachable=false).Value;}
                Vector3 side=Vector3.Cross(Vector3.up,inward);
                if(!CanReach(entry+side*.15f)||!CanReach(entry-side*.15f))
                {readinessFailure="出怪口两侧空间不足，请向室内开阔地面重新标定";return (entrancesReachable=false).Value;}
            }
            return (entrancesReachable=true).Value;
        }
        public string ReadinessFailure {get{AllEntrancesReachable();return readinessFailure??"";}}
        public string NavigationSummary=>$"nodes={map?.Count??0} open={openCount} coreSeeds={coreSeedCount} reachable={reachableCount} core={grid?.SafeZoneCenter} failure={ReadinessFailure}";
        public bool TrySpawn(Vector3 desired,out Vector3 point)
        {
            point=desired;if(map==null)return true;
            // Used only to settle a spawn on the nearest valid floor node.
            int best=-1;float nearest=1.5f*1.5f;
            for(int i=0;i<map.Count;i++)if(distance[i]>=0)
            {float d=(map.Point(i)-desired).sqrMagnitude;if(d<nearest){nearest=d;best=i;}}
            if(best<0)return false;point=map.Point(best);return true;
        }
        public Vector3 Move(Vector3 feet,float speed,float dt,float age,int id,bool runner)
        {
            if(map==null||speed<=0||dt<=0)return feet;
            int current=ReachableNode(feet,.65f);if(current<0)return feet;
            Vector3 target=map.Point(current);
            // Descend the field without cutting corners or stepping through a tower.
            if(distance[current]>0)
            {
                int next=current,best=distance[current];
                // Look ahead on the descending field. Choosing the nearest node
                // alone can pull an actor backwards halfway between two nodes.
                for(int dx=-3;dx<=3;dx++)for(int dz=-3;dz<=3;dz++)
                {
                    int candidate=map.Neighbor(current,dx,dz);
                    if(candidate<0||distance[candidate]<0||distance[candidate]>=best)continue;
                    if(Horizontal(feet,map.Point(candidate))>.65f)continue;
                    if(!SegmentClear(feet,map.Point(candidate)))continue;
                    next=candidate;best=distance[candidate];
                }
                target=map.Point(next);
            }
            Vector3 direction=target-feet;direction.y=0;
            if(direction.sqrMagnitude<.000001f)return feet;
            float step=Mathf.Min(speed*dt,direction.magnitude);direction.Normalize();
            if(runner)
            {
                Vector3 zig=(direction+Vector3.Cross(Vector3.up,direction)*Mathf.Sin(age*5.2f+id*1.7f)*.4f).normalized;
                Vector3 curved=feet+zig*step;if(SegmentClear(feet,curved))return curved;
            }
            Vector3 result=feet+direction*step;
            return SegmentClear(feet,result)?result:feet;
        }
        private static float Horizontal(Vector3 a,Vector3 b)=>GridBuilder.HorizontalDistance(a,b);
    }
}
