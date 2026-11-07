using UnityEngine;

namespace RoomSiege.Scan
{
    public static class SandboxRoom
    {
        public static RoomSnapshot Create(Vector3 center)
        {
            var room = new RoomSnapshot { FloorHeight = center.y };
            room.Surfaces.Add(Rectangle("sandbox-floor",SurfaceKind.Floor,center,new Vector2(6f,6f)));
            AddFurniture(room,"sandbox-table",SurfaceKind.Table,center+new Vector3(-1.8f,0.36f,1.2f),new Vector3(1.2f,0.72f,0.8f),true);
            AddFurniture(room,"sandbox-cabinet",SurfaceKind.LowCabinet,center+new Vector3(2.4f,0.325f,1.2f),new Vector3(0.8f,0.65f,1.6f),true);
            AddFurniture(room,"sandbox-sofa",SurfaceKind.Floor,center+new Vector3(-1.4f,0.4f,-2.4f),new Vector3(2f,0.8f,0.8f),false);
            room.Entrances.Add(new SpawnPoint("sandbox-door",center+new Vector3(0f,0f,2.8f),Vector3.back,new Vector2(1f,0.8f)));
            return room;
        }
        public static DeploySurface Rectangle(string id,SurfaceKind kind,Vector3 center,Vector2 size)
        {
            return new DeploySurface { Id=id,Kind=kind,Height=center.y,Polygon=new[] {
                new Vector2(center.x-size.x/2,center.z-size.y/2),new Vector2(center.x+size.x/2,center.z-size.y/2),
                new Vector2(center.x+size.x/2,center.z+size.y/2),new Vector2(center.x-size.x/2,center.z+size.y/2) } };
        }
        private static void AddFurniture(RoomSnapshot room,string id,SurfaceKind kind,Vector3 center,Vector3 size,bool deploy)
        {
            room.Volumes.Add(new RoomVolume { Id=id,Center=center,Size=size,SupportsDeployment=deploy });
            if(deploy) room.Surfaces.Add(Rectangle(id,kind,center+Vector3.up*size.y/2,new Vector2(size.x,size.z)));
        }
    }
}
