using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Scan
{
    public static class GridBuilder
    {
        public const int MinimumCells = 8;
        public const float MinimumFloorArea = 6f;
        private const float Epsilon = 0.0001f;

        public static GridModel Build(RoomSnapshot room, Vector3 playerPosition, ScanMode mode)
        {
            if (room == null) throw new ArgumentNullException(nameof(room));
            var safe = new Vector3(playerPosition.x, room.FloorHeight, playerPosition.z);
            var spawns = SelectEntrances(room.Entrances, safe);
            var cells = new List<GridCell>();
            var occupied = new HashSet<Vector3Int>();
            foreach (var surface in room.Surfaces)
            {
                if (surface.Polygon == null || surface.Polygon.Length < 3) continue;
                var bounds = PolygonBounds(surface.Polygon);
                // World-aligned 0.4m lattice. A whole cell must fit within its support polygon.
                int minX = Mathf.CeilToInt(bounds.xMin / GridModel.CellSize);
                int maxX = Mathf.FloorToInt(bounds.xMax / GridModel.CellSize) - 1;
                int minZ = Mathf.CeilToInt(bounds.yMin / GridModel.CellSize);
                int maxZ = Mathf.FloorToInt(bounds.yMax / GridModel.CellSize) - 1;
                // Bound malformed/unreasonable native geometry instead of allocating an unbounded grid.
                if (maxX - minX > 250 || maxZ - minZ > 250) continue;
                for (int x = minX; x <= maxX; x++)
                for (int z = minZ; z <= maxZ; z++)
                {
                    var cellRect = new Rect(x * GridModel.CellSize, z * GridModel.CellSize,
                        GridModel.CellSize, GridModel.CellSize);
                    if (!FitsPolygon(cellRect, surface.Polygon)) continue;
                    if (spawns.Exists(s => OverlapsSpawn(cellRect, s))) continue;
                    if (room.Volumes.Exists(v => Blocks(cellRect, surface.Height, surface.Id, v))) continue;
                    var key = new Vector3Int(x, Mathf.RoundToInt(surface.Height * 100f), z);
                    if (!occupied.Add(key)) continue;
                    cells.Add(new GridCell(surface.Id, surface.Kind, new Vector2Int(x, z),
                        new Vector3(cellRect.center.x, surface.Height, cellRect.center.y)));
                }
            }
            var supported=new GridModel(mode,safe,cells,spawns,RoomWalkMap.Build(room));
            return WithSafeZone(supported,safe);
        }

        public static GridModel WithSafeZone(GridModel source,Vector3 position)
        {
            if(source==null)throw new ArgumentNullException(nameof(source));
            var safe=new Vector3(position.x,source.SafeZoneCenter.y,position.z);
            var cells=new List<GridCell>();
            var reserved=new List<GridCell>();
            var paths=new List<List<Vector3>>();
            if(source.WalkMap!=null)
                foreach(var spawn in source.SpawnPoints)
                    if(source.WalkMap.TryFindPath(spawn.Position+spawn.Forward*.65f,safe,GridModel.SafeRadius,out var path))paths.Add(path);
            foreach(var cell in source.SupportedCells)
            {
                float half=GridModel.CellSize*.5f;
                var rect=new Rect(cell.Center.x-half,cell.Center.z-half,GridModel.CellSize,GridModel.CellSize);
                bool route=false;
                if(cell.Surface==SurfaceKind.Floor&&Mathf.Abs(cell.Center.y-safe.y)<.35f)
                    foreach(var path in paths)
                    {
                        for(int i=0;i<path.Count;i++)
                        {
                            // Centreline nodes are only 0.2m apart. The extra
                            // half-step makes the cell exclusion cover the
                            // continuous swept Boss body between samples.
                            if(DistanceToRect(new Vector2(path[i].x,path[i].z),rect)>RoomWalkMap.EnemyClearance+RoomWalkMap.Step*.5f)continue;
                            route=true;break;
                        }
                        if(route)break;
                    }
                if(route){reserved.Add(cell);continue;}
                if(DistanceToRect(new Vector2(safe.x,safe.z),rect)>=GridModel.CoreBuildRadius-Epsilon)cells.Add(cell);
            }
            return source.WithSafeZone(safe,cells,reserved);
        }

        public static bool IsPlayable(RoomSnapshot room, GridModel model)
        {
            return string.IsNullOrEmpty(PlayableFailure(room,model));
        }

        public static bool TryFindNearbyCore(GridModel model,out Vector3 center)
        {
            center=model==null?Vector3.zero:model.SafeZoneCenter;
            if(model?.WalkMap==null)return false;
            // A seated player's feet can be inside the scanned desk footprint.
            // Recover locally, without silently moving the objective across the room.
            var map=model.WalkMap;float nearest=2f;int best=-1;
            var visited=new bool[map.Count];
            for(int i=0;i<map.Count;i++)
            {
                if(!map.Open(i)||visited[i])continue;
                var component=new List<int>{i};visited[i]=true;
                for(int cursor=0;cursor<component.Count;cursor++)
                    for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                    {
                        if(dx==0&&dz==0)continue;
                        int node=map.Neighbor(component[cursor],dx,dz);
                        if(!map.Open(node)||visited[node])continue;
                        if(dx!=0&&dz!=0&&(!map.Open(map.Neighbor(component[cursor],dx,0))||
                            !map.Open(map.Neighbor(component[cursor],0,dz))))continue;
                        visited[node]=true;component.Add(node);
                    }
                // A few isolated cells between furniture cannot host a battle route.
                if(component.Count<MinimumCells*4)continue;
                foreach(int node in component)
                {
                    float distance=HorizontalDistance(map.Point(node),model.SafeZoneCenter);
                    if(distance<=GridModel.SafeRadius+.09f)return true;
                    if(distance<nearest){nearest=distance;best=node;}
                }
            }
            if(best<0)return false;
            center=map.Point(best);return true;
        }

        public static bool TryCreateAutomaticEntrance(RoomSnapshot room,GridModel grid,
            Func<GridModel,string> routeFailure,out SpawnPoint entrance)
        {
            entrance=default;
            if(room==null||grid?.WalkMap==null||grid.PlaceableCells.Count<MinimumCells)return false;
            var map=grid.WalkMap;var distance=new int[map.Count];var queue=new Queue<int>();
            for(int i=0;i<map.Count;i++)
            {
                distance[i]=-1;
                if(map.Open(i)&&HorizontalDistance(map.Point(i),grid.SafeZoneCenter)<=GridModel.SafeRadius+.09f)
                {distance[i]=0;queue.Enqueue(i);}
            }
            // Select only from floor connected to the defended core, including diagonal clearance.
            while(queue.Count>0)
            {
                int current=queue.Dequeue();
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                {
                    if(dx==0&&dz==0)continue;
                    int next=map.Neighbor(current,dx,dz);
                    if(!map.Open(next)||distance[next]>=0)continue;
                    if(dx!=0&&dz!=0&&(!map.Open(map.Neighbor(current,dx,0))||!map.Open(map.Neighbor(current,0,dz))))continue;
                    distance[next]=distance[current]+1;queue.Enqueue(next);
                }
            }
            var candidates=new List<int>();
            for(int i=0;i<map.Count;i++)if(distance[i]>0)candidates.Add(i);
            candidates.Sort((a,b)=>
            {
                int comparison=distance[b].CompareTo(distance[a]);
                return comparison!=0?comparison:a.CompareTo(b);
            });
            var candidateRoom=new RoomSnapshot {FloorHeight=room.FloorHeight};
            candidateRoom.Surfaces.AddRange(room.Surfaces);candidateRoom.Volumes.AddRange(room.Volumes);
            foreach(int node in candidates)
            {
                var entry=map.Point(node);var forward=(grid.SafeZoneCenter-entry).normalized;
                var position=entry-forward*.65f;
                if(HorizontalDistance(position,grid.SafeZoneCenter)<1.8f)continue;
                if(!room.Surfaces.Exists(s=>s.Kind==SurfaceKind.Floor&&s.Polygon!=null&&Contains(s.Polygon,new Vector2(position.x,position.z))))continue;
                if(room.Volumes.Exists(v=>FurnitureContains(v,position+Vector3.up*.05f)))continue;
                var side=Vector3.Cross(Vector3.up,forward)*.2f;
                if(!map.SegmentOpen(entry-side,entry+side)||
                    !map.SegmentOpen(entry-side-forward*.05f,entry+side-forward*.05f))continue;
                var candidate=new SpawnPoint("0-room-entrance",position,forward,new Vector2(.8f,.8f),true);
                candidateRoom.Entrances.Clear();candidateRoom.Entrances.Add(candidate);
                var model=Build(candidateRoom,grid.SafeZoneCenter,grid.Mode);
                if(!string.IsNullOrEmpty(PlayableFailure(candidateRoom,model))||
                    !string.IsNullOrEmpty(routeFailure?.Invoke(model)))continue;
                entrance=candidate;return true;
            }
            return false;
        }

        public static bool TryCalibrateEntrance(RoomSnapshot room,GridModel grid,Ray ray,
            Func<GridModel,string> routeFailure,out SpawnPoint entrance,out Vector3? target,out string failure)
        {
            entrance=default;target=null;failure="房间尚未就绪";
            if(room==null||grid==null)return false;
            if(!Finite(ray.origin)||!Finite(ray.direction)||ray.direction.sqrMagnitude<.001f)
            {failure="射线尚未定位，请保持手柄 / 手部可见";return false;}
            ray=new Ray(ray.origin,ray.direction.normalized);
            var plane=new Plane(Vector3.up,Vector3.up*room.FloorHeight);
            if(!plane.Raycast(ray,out float distance)||distance>8f)
            {failure="请将射线向下指向 8m 内的房间地板";return false;}
            var point=ray.GetPoint(distance);target=point;
            if(!room.Surfaces.Exists(s=>s.Kind==SurfaceKind.Floor&&s.Polygon!=null&&Contains(s.Polygon,new Vector2(point.x,point.z))))
            {failure="落点超出已扫描地板，请移回房间内";return false;}
            float separation=HorizontalDistance(point,grid.SafeZoneCenter);
            if(separation<1.8f)
            {failure=$"距核心 {separation:F1}m，请选至少 1.8m 外的地板";return false;}
            if(room.Volumes.Exists(v=>FurnitureContains(v,point+Vector3.up*.05f)))
            {failure="落点被家具占用，请选旁边的开阔地板";return false;}
            var requested=new SpawnPoint("0-manual-entrance",point,grid.SafeZoneCenter-point,new Vector2(.8f,.8f),true);
            if(!TryFitManualEntrance(room,grid,requested,routeFailure,out entrance))
            {failure="入口到核心的通路受阻，请换一块开阔地板";return false;}
            target=entrance.Position;
            // Preview and confirmation use the same rules without modifying the live room.
            var candidate=new RoomSnapshot {FloorHeight=room.FloorHeight};
            candidate.Surfaces.AddRange(room.Surfaces);candidate.Volumes.AddRange(room.Volumes);
            candidate.Entrances.Add(entrance);candidate.Entrances.AddRange(room.Entrances.FindAll(s=>!s.IsManual));
            var model=Build(candidate,grid.SafeZoneCenter,grid.Mode);
            failure=PlayableFailure(candidate,model);
            if(string.IsNullOrEmpty(failure))failure=routeFailure?.Invoke(model)??"";
            return string.IsNullOrEmpty(failure);
        }

        private static bool Finite(Vector3 value)=>!float.IsNaN(value.x+value.y+value.z)&&
            !float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);

        public static bool TryFitManualEntrance(RoomSnapshot room,GridModel grid,SpawnPoint requested,
            Func<GridModel,string> routeFailure,out SpawnPoint fitted)
        {
            fitted=requested;
            if(room==null||grid?.WalkMap==null||!requested.IsManual)return false;
            var map=grid.WalkMap;
            bool Accept(SpawnPoint candidate)
            {
                if(HorizontalDistance(candidate.Position,grid.SafeZoneCenter)<1.8f)return false;
                if(!room.Surfaces.Exists(s=>s.Kind==SurfaceKind.Floor&&s.Polygon!=null&&
                    Contains(s.Polygon,new Vector2(candidate.Position.x,candidate.Position.z))))return false;
                if(room.Volumes.Exists(v=>FurnitureContains(v,candidate.Position+Vector3.up*.05f)))return false;
                var entry=candidate.Position+candidate.Forward*.65f;
                var side=Vector3.Cross(Vector3.up,candidate.Forward)*.20f;
                // Include both staggered spawn lanes and the short backwards offset.
                if(!map.SegmentOpen(entry-side,entry+side)||
                    !map.SegmentOpen(entry-side-candidate.Forward*.05f,entry+side-candidate.Forward*.05f))return false;
                var entrances=new List<SpawnPoint>{candidate};
                entrances.AddRange(room.Entrances.FindAll(s=>!s.IsManual));
                var test=new GridModel(grid.Mode,grid.SafeZoneCenter,new List<GridCell>(grid.PlaceableCells),
                    SelectEntrances(entrances,grid.SafeZoneCenter),map);
                return string.IsNullOrEmpty(routeFailure?.Invoke(test));
            }
            if(Accept(requested))return true;
            var desired=requested.Position+requested.Forward*.65f;
            var nearby=new List<int>();
            for(int i=0;i<map.Count;i++)
                if(map.Open(i)&&HorizontalDistance(map.Point(i),desired)<=.8f)nearby.Add(i);
            nearby.Sort((a,b)=>(map.Point(a)-desired).sqrMagnitude.CompareTo((map.Point(b)-desired).sqrMagnitude));
            foreach(int node in nearby)
            {
                var candidate=new SpawnPoint(requested.AnchorId,map.Point(node)-requested.Forward*.65f,
                    requested.Forward,requested.FootprintSize,true);
                if(!Accept(candidate))continue;
                fitted=candidate;return true;
            }
            return false;
        }

        public static string PlayableFailure(RoomSnapshot room,GridModel model)
        {
            if(room==null||model==null)return "房间数据为空";
            float floorArea=0f;
            foreach(var surface in room.Surfaces)
                if(surface.Kind==SurfaceKind.Floor&&surface.Polygon!=null)floorArea+=Area(surface.Polygon);
            if(floorArea<MinimumFloorArea)return $"地板面积不足：{floorArea:F1}m²（需要至少 {MinimumFloorArea:F0}m²）";
            if(model.PlaceableCells.Count<MinimumCells)return $"可放格不足：{model.PlaceableCells.Count} 格（需要至少 {MinimumCells} 格）";
            if(model.SpawnPoints.Count==0)return "未识别到门洞/过道出怪口，请使用“标定出怪口”";
            return string.Empty;
        }

        public static List<SpawnPoint> SelectEntrances(IEnumerable<SpawnPoint> candidates, Vector3 safe)
        {
            var result = new List<SpawnPoint>();
            var sorted = new List<SpawnPoint>(candidates);
            sorted.Sort((a, b) => string.CompareOrdinal(a.AnchorId, b.AnchorId));
            foreach (var candidate in sorted)
            {
                var rect = Footprint(candidate);
                if (DistanceToRect(new Vector2(safe.x, safe.z), PolygonBounds(rect)) < GridModel.SafeRadius) continue;
                if (result.Exists(s => HorizontalDistance(s.Position, candidate.Position) < 0.8f)) continue;
                result.Add(candidate);
                if (result.Count == 2) break;
            }
            return result;
        }

        public static bool OverlapsSpawn(Rect cell, SpawnPoint spawn) => Overlap(cell, Footprint(spawn));

        public static bool FurnitureContains(RoomVolume volume,Vector3 point)
        {
            var local=Quaternion.Inverse(volume.Rotation)*(point-volume.Center);
            return new Bounds(Vector3.zero,volume.Size+Vector3.up*.1f).Contains(local);
        }

        private static Vector2[] Footprint(SpawnPoint spawn)
        {
            var forward = new Vector2(spawn.Forward.x, spawn.Forward.z);
            var right = new Vector2(forward.y, -forward.x);
            var c = new Vector2(spawn.Position.x, spawn.Position.z);
            var a = right * spawn.FootprintSize.x * 0.5f;
            var b = forward * spawn.FootprintSize.y * 0.5f;
            return new[] { c-a-b, c+a-b, c+a+b, c-a+b };
        }

        private static bool Blocks(Rect cell, float height, string supportId, RoomVolume volume)
        {
            if (volume.Id == supportId) return false;
            var half = volume.Size * 0.5f;
            float minY = float.PositiveInfinity, maxY = float.NegativeInfinity;
            var points = new List<Vector2>();
            for (int x = -1; x <= 1; x += 2)
            for (int y = -1; y <= 1; y += 2)
            for (int z = -1; z <= 1; z += 2)
            {
                var p = volume.Center + volume.Rotation * Vector3.Scale(half, new Vector3(x,y,z));
                minY = Mathf.Min(minY,p.y); maxY = Mathf.Max(maxY,p.y);
                points.Add(new Vector2(p.x,p.z));
            }
            if (maxY <= height + 0.02f || minY > height + 0.5f) return false;
            // Conservative world AABB prevents deployment into furniture, including tilted boxes.
            var footprint=PolygonBounds(points.ToArray());
            // A 5mm margin also avoids accepting touching edges through float conversion noise.
            footprint.xMin-=0.005f; footprint.xMax+=0.005f;
            footprint.yMin-=0.005f; footprint.yMax+=0.005f;
            return cell.Overlaps(footprint);
        }

        public static bool FitsPolygon(Rect cell, Vector2[] polygon)
        {
            var corners = Corners(cell);
            foreach (var p in corners) if (!Contains(polygon, p)) return false;
            // Corner-only tests miss notches in concave floor outlines.
            for (int i = 0; i < polygon.Length; i++)
            {
                var a = polygon[i]; var b = polygon[(i + 1) % polygon.Length];
                foreach (var p in new[] { a, (a+b)*0.5f })
                    if (p.x > cell.xMin+Epsilon && p.x < cell.xMax-Epsilon &&
                        p.y > cell.yMin+Epsilon && p.y < cell.yMax-Epsilon) return false;
                for (int j = 0; j < 4; j++)
                    if (Crosses(a,b,corners[j],corners[(j+1)%4])) return false;
            }
            return true;
        }

        public static bool Contains(Vector2[] polygon, Vector2 point)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length-1; i < polygon.Length; j=i++)
            {
                var a = polygon[j]; var b = polygon[i];
                if (DistanceToSegment(point,a,b) < Epsilon) return true;
                if ((a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x-a.x)*(point.y-a.y)/(b.y-a.y)+a.x) inside = !inside;
            }
            return inside;
        }

        private static bool Overlap(Rect rect, Vector2[] polygon)
        {
            if (!rect.Overlaps(PolygonBounds(polygon))) return false;
            foreach (var p in Corners(rect)) if (Contains(polygon,p)) return true;
            foreach (var p in polygon) if (rect.Contains(p)) return true;
            var c = Corners(rect);
            for (int i=0;i<polygon.Length;i++)
            for (int j=0;j<4;j++)
                if (Crosses(polygon[i],polygon[(i+1)%polygon.Length],c[j],c[(j+1)%4])) return true;
            return false;
        }

        public static float DistanceToRect(Vector2 p, Rect r) =>
            Vector2.Distance(p, new Vector2(Mathf.Clamp(p.x,r.xMin,r.xMax),Mathf.Clamp(p.y,r.yMin,r.yMax)));
        private static float DistanceToSegment(Vector2 p, Vector2 a, Vector2 b)
        {
            var d=b-a;
            return Vector2.Distance(p,a+d*(d.sqrMagnitude < Epsilon*Epsilon ? 0f : Mathf.Clamp01(Vector2.Dot(p-a,d)/d.sqrMagnitude)));
        }
        private static float Cross(Vector2 a, Vector2 b) => a.x*b.y-a.y*b.x;
        private static bool Crosses(Vector2 a, Vector2 b, Vector2 c, Vector2 d) =>
            Cross(b-a,c-a)*Cross(b-a,d-a)<-Epsilon*Epsilon && Cross(d-c,a-c)*Cross(d-c,b-c)<-Epsilon*Epsilon;
        public static float HorizontalDistance(Vector3 a, Vector3 b) => Vector2.Distance(new Vector2(a.x,a.z),new Vector2(b.x,b.z));
        public static float Area(Vector2[] polygon)
        {
            float area=0f;
            for(int i=0;i<polygon.Length;i++) area+=Cross(polygon[i],polygon[(i+1)%polygon.Length]);
            return Mathf.Abs(area)*0.5f;
        }
        private static Vector2[] Corners(Rect r) => new[] {
            new Vector2(r.xMin,r.yMin),new Vector2(r.xMax,r.yMin),new Vector2(r.xMax,r.yMax),new Vector2(r.xMin,r.yMax) };
        private static Rect PolygonBounds(Vector2[] p)
        {
            float x0=float.PositiveInfinity,z0=float.PositiveInfinity,x1=float.NegativeInfinity,z1=float.NegativeInfinity;
            foreach(var v in p) { x0=Mathf.Min(x0,v.x); x1=Mathf.Max(x1,v.x); z0=Mathf.Min(z0,v.y); z1=Mathf.Max(z1,v.y); }
            return Rect.MinMaxRect(x0,z0,x1,z1);
        }
    }
}
