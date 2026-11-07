using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Scan
{
    // Detached floor navigation, including the safe zone and portal footprints.
    // Deployment cells deliberately exclude those regions and cannot be used for navigation.
    public sealed class RoomWalkMap
    {
        public const float Step=.2f;
        public const float EnemyClearance=GridModel.BossBodyRadius+.02f;
        public readonly int Width,Depth;
        public readonly Vector3 Origin;
        private readonly bool[] nodes;
        public int Count=>nodes.Length;
        private RoomWalkMap(int width,int depth,Vector3 origin,bool[] nodes)
        {Width=width;Depth=depth;Origin=origin;this.nodes=nodes;}
        public Vector3 Point(int index)=>Origin+new Vector3(index%Width*Step,0,index/Width*Step);
        public bool Open(int index)=>index>=0&&index<Count&&nodes[index];
        public int Index(Vector3 point)
        {
            int x=Mathf.RoundToInt((point.x-Origin.x)/Step),z=Mathf.RoundToInt((point.z-Origin.z)/Step);
            return x<0||z<0||x>=Width||z>=Depth?-1:z*Width+x;
        }
        public int Neighbor(int index,int dx,int dz)
        {int x=index%Width+dx,z=index/Width+dz;return x<0||z<0||x>=Width||z>=Depth?-1:z*Width+x;}
        public bool SegmentOpen(Vector3 a,Vector3 b)
        {
            int current=Index(a),end=Index(b);
            if(!Open(current)||!Open(end))return false;
            Vector3 delta=b-a;
            int sx=delta.x>0?1:delta.x<0?-1:0,sz=delta.z>0?1:delta.z<0?-1:0;
            float tx=sx==0?float.PositiveInfinity:
                Mathf.Max(0,(Origin.x+(current%Width+sx*.5f)*Step-a.x)/delta.x);
            float tz=sz==0?float.PositiveInfinity:
                Mathf.Max(0,(Origin.z+(current/Width+sz*.5f)*Step-a.z)/delta.z);
            float dx=sx==0?float.PositiveInfinity:Step/Mathf.Abs(delta.x);
            float dz=sz==0?float.PositiveInfinity:Step/Mathf.Abs(delta.z);
            // Visit every crossed cell. Spaced samples can miss a clipped corner,
            // making long look-ahead pass while the next short movement is blocked.
            for(int count=0;current!=end&&count<Width+Depth+2;count++)
            {
                if(Mathf.Abs(tx-tz)<.000001f)
                {
                    if(!Open(Neighbor(current,sx,0))||!Open(Neighbor(current,0,sz)))return false;
                    current=Neighbor(current,sx,sz);tx+=dx;tz+=dz;
                }
                else if(tx<tz){current=Neighbor(current,sx,0);tx+=dx;}
                else{current=Neighbor(current,0,sz);tz+=dz;}
                if(!Open(current))return false;
            }
            return current==end;
        }

        // Returns a deterministic shortest grid route from an entrance body to
        // the defended core. GridBuilder uses this path to reserve Boss-width
        // deployment cells, so route cells never become tower placement cells.
        public bool TryFindPath(Vector3 start,Vector3 safe,float safeRadius,out List<Vector3> points)
        {
            points=new List<Vector3>();
            int origin=NearestOpen(start,1.0f);
            if(origin<0)return false;
            var parent=new int[Count];for(int i=0;i<parent.Length;i++)parent[i]=-1;
            var queue=new Queue<int>();var visited=new bool[Count];
            for(int i=0;i<Count;i++)
                if(Open(i)&&GridBuilder.HorizontalDistance(Point(i),safe)<=safeRadius+.09f)
                {visited[i]=true;queue.Enqueue(i);}
            if(queue.Count==0)return false;
            while(queue.Count>0)
            {
                int current=queue.Dequeue();
                if(current==origin)
                {
                    var cursor=origin;
                    while(cursor>=0){points.Add(Point(cursor));cursor=parent[cursor];}
                    return points.Count>0;
                }
                for(int dx=-1;dx<=1;dx++)for(int dz=-1;dz<=1;dz++)
                {
                    if(dx==0&&dz==0)continue;
                    int next=Neighbor(current,dx,dz);
                    if(next<0||!Open(next)||visited[next])continue;
                    if(dx!=0&&dz!=0&&(!Open(Neighbor(current,dx,0))||!Open(Neighbor(current,0,dz))))continue;
                    visited[next]=true;parent[next]=current;queue.Enqueue(next);
                }
            }
            return false;
        }
        private int NearestOpen(Vector3 point,float radius)
        {
            int origin=Index(point);if(origin<0)return -1;
            int span=Mathf.CeilToInt(radius/Step),best=-1;float nearest=radius*radius;
            for(int dx=-span;dx<=span;dx++)for(int dz=-span;dz<=span;dz++)
            {
                int node=Neighbor(origin,dx,dz);if(node<0||!Open(node))continue;
                float distance=(Point(node)-point).sqrMagnitude;
                if(distance<nearest){nearest=distance;best=node;}
            }
            return best;
        }
        public static RoomWalkMap Build(RoomSnapshot room)
        {
            Vector2 low=new Vector2(float.PositiveInfinity,float.PositiveInfinity),high=new Vector2(float.NegativeInfinity,float.NegativeInfinity);
            foreach(var surface in room.Surfaces)
                if(surface.Kind==SurfaceKind.Floor&&Mathf.Abs(surface.Height-room.FloorHeight)<.15f&&surface.Polygon!=null)
                    foreach(var p in surface.Polygon){low=Vector2.Min(low,p);high=Vector2.Max(high,p);}
            if(float.IsInfinity(low.x))return null;
            int width=Mathf.FloorToInt((high.x-low.x)/Step)+1,depth=Mathf.FloorToInt((high.y-low.y)/Step)+1;
            if(width<=0||depth<=0||width>250||depth>250)return null;
            var nodes=new bool[width*depth];
            var origin=new Vector3(low.x,room.FloorHeight,low.y);
            var obstacles=new List<Vector2[]>();
            foreach(var volume in room.Volumes)
            {
                var corners=new List<Vector2>();
                float minY=float.PositiveInfinity,maxY=float.NegativeInfinity;
                for(int x=-1;x<=1;x+=2)for(int y=-1;y<=1;y+=2)for(int z=-1;z<=1;z+=2)
                {
                    var p=volume.Center+volume.Rotation*Vector3.Scale(volume.Size*.5f,new Vector3(x,y,z));
                    minY=Mathf.Min(minY,p.y);maxY=Mathf.Max(maxY,p.y);
                    corners.Add(new Vector2(p.x,p.z));
                }
                // Native box axes need not be upright. Use world height and footprint,
                // so wall-mounted objects do not become phantom floor obstacles.
                if(maxY<=room.FloorHeight+.02f||minY>room.FloorHeight+.55f)continue;
                obstacles.Add(ConvexHull(corners));
            }
            for(int z=0;z<depth;z++)for(int x=0;x<width;x++)
            {
                var point=origin+new Vector3(x*Step,0,z*Step);bool valid=OnFloor(room,point);
                // Circumscribed square covers the full body, including diagonal edges.
                float extent=EnemyClearance+Step*.72f;
                for(int sx=-1;sx<=1&&valid;sx++)for(int sz=-1;sz<=1&&valid;sz++)
                    valid=OnFloor(room,point+new Vector3(sx*extent,0,sz*extent));
                if(valid)foreach(var polygon in obstacles)
                {
                    if(NearFootprint(polygon,new Vector2(point.x,point.z),extent)){valid=false;break;}
                }
                nodes[z*width+x]=valid;
            }
            return new RoomWalkMap(width,depth,origin,nodes);
        }
        private static bool NearFootprint(Vector2[] polygon,Vector2 point,float clearance)
        {
            if(GridBuilder.Contains(polygon,point))return true;
            for(int i=0;i<polygon.Length;i++)
            {
                var a=polygon[i];var d=polygon[(i+1)%polygon.Length]-a;
                var closest=a+d*(d.sqrMagnitude<.000001f?0:Mathf.Clamp01(Vector2.Dot(point-a,d)/d.sqrMagnitude));
                if((point-closest).sqrMagnitude<=clearance*clearance)return true;
            }
            return false;
        }
        private static Vector2[] ConvexHull(List<Vector2> points)
        {
            points.Sort((a,b)=>a.x==b.x?a.y.CompareTo(b.y):a.x.CompareTo(b.x));
            var hull=new List<Vector2>();
            float Turn(Vector2 a,Vector2 b,Vector2 c)=>(b.x-a.x)*(c.y-a.y)-(b.y-a.y)*(c.x-a.x);
            foreach(var p in points)
            {while(hull.Count>=2&&Turn(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);hull.Add(p);}
            int lower=hull.Count;
            for(int i=points.Count-2;i>=0;i--)
            {
                var p=points[i];
                while(hull.Count>lower&&Turn(hull[hull.Count-2],hull[hull.Count-1],p)<=0)hull.RemoveAt(hull.Count-1);
                hull.Add(p);
            }
            hull.RemoveAt(hull.Count-1);return hull.ToArray();
        }
        private static bool OnFloor(RoomSnapshot room,Vector3 p)
        {
            foreach(var surface in room.Surfaces)
                if(surface.Kind==SurfaceKind.Floor&&Mathf.Abs(surface.Height-room.FloorHeight)<.15f&&surface.Polygon!=null&&
                    GridBuilder.Contains(surface.Polygon,new Vector2(p.x,p.z)))return true;
            return false;
        }
    }
}
