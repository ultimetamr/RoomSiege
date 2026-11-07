using System;
using UnityEngine;

namespace RoomSiege.Scan
{
    public static class RoomEntranceMemory
    {
        public const string PreferenceKey="RoomSiege.Scan.Entrance.v1";
        [Serializable]
        private sealed class SavedEntrance
        {
            public int version=1;
            public string floorId;
            public Vector3 localPosition,localForward;
        }

        public static string Encode(RoomSnapshot room,SpawnPoint entrance)
        {
            if(room==null||!entrance.IsManual)return null;
            foreach(var floor in room.Surfaces)
            {
                if(floor.Kind!=SurfaceKind.Floor||!floor.HasAnchorPose||!Valid(floor.AnchorPose)||
                    string.IsNullOrEmpty(floor.Id)||!GridBuilder.Contains(floor.Polygon,new Vector2(entrance.Position.x,entrance.Position.z)))continue;
                var inverse=Quaternion.Inverse(floor.AnchorPose.rotation);
                return JsonUtility.ToJson(new SavedEntrance {floorId=floor.Id,
                    localPosition=inverse*(entrance.Position-floor.AnchorPose.position),localForward=inverse*entrance.Forward});
            }
            return null;
        }

        public static bool TryRestore(RoomSnapshot room,string json,Vector3 core,out SpawnPoint entrance)
        {
            entrance=default;
            if(room==null||string.IsNullOrEmpty(json)||!Finite(core))return false;
            SavedEntrance saved;
            try{saved=JsonUtility.FromJson<SavedEntrance>(json);}
            catch(ArgumentException){return false;}
            if(saved==null||saved.version!=1||string.IsNullOrEmpty(saved.floorId)||
                !Finite(saved.localPosition)||!Finite(saved.localForward))return false;
            foreach(var floor in room.Surfaces)
            {
                if(floor.Kind!=SurfaceKind.Floor||floor.Id!=saved.floorId||!floor.HasAnchorPose||!Valid(floor.AnchorPose))continue;
                // A floor anchor can relocate after recentering. Never reuse yesterday's world coordinates.
                var point=floor.AnchorPose.position+floor.AnchorPose.rotation*saved.localPosition;
                point.y=floor.Height;
                var forward=Vector3.ProjectOnPlane(floor.AnchorPose.rotation*saved.localForward,Vector3.up);
                if(!Finite(point)||!Finite(forward)||forward.sqrMagnitude<.01f||
                    !GridBuilder.Contains(floor.Polygon,new Vector2(point.x,point.z))||
                    GridBuilder.HorizontalDistance(point,core)<1.8f)return false;
                foreach(var volume in room.Volumes)
                    if(GridBuilder.FurnitureContains(volume,point+Vector3.up*.05f))return false;
                entrance=new SpawnPoint("0-manual-entrance",point,forward,new Vector2(.8f,.8f),true);
                return true;
            }
            return false;
        }
        private static bool Finite(Vector3 value)=>!float.IsNaN(value.x+value.y+value.z)&&
            !float.IsInfinity(value.x)&&!float.IsInfinity(value.y)&&!float.IsInfinity(value.z);
        private static bool Valid(Pose pose)
        {
            float norm=pose.rotation.x*pose.rotation.x+pose.rotation.y*pose.rotation.y+
                pose.rotation.z*pose.rotation.z+pose.rotation.w*pose.rotation.w;
            return Finite(pose.position)&&!float.IsNaN(norm)&&norm>.9f&&norm<1.1f;
        }
    }
}
