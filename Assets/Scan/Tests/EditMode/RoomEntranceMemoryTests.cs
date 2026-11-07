using NUnit.Framework;
using UnityEngine;

namespace RoomSiege.Scan.Tests
{
    public sealed class RoomEntranceMemoryTests
    {
        private static RoomSnapshot Room(Pose pose,string id="saved-floor")
        {
            var room=new RoomSnapshot {FloorHeight=pose.position.y};
            var floor=SandboxRoom.Rectangle(id,SurfaceKind.Floor,Vector3.zero,new Vector2(6,6));
            for(int i=0;i<floor.Polygon.Length;i++)
            {
                var local=floor.Polygon[i];
                var world=pose.position+pose.rotation*new Vector3(local.x,0,local.y);
                floor.Polygon[i]=new Vector2(world.x,world.z);
            }
            floor.Height=pose.position.y;floor.HasAnchorPose=true;floor.AnchorPose=pose;
            room.Surfaces.Add(floor);return room;
        }
        private static string Saved(RoomSnapshot room)=>RoomEntranceMemory.Encode(room,
            new SpawnPoint("manual",new Vector3(0,0,2.4f),Vector3.back,new Vector2(.8f,.8f),true));

        [Test]
        public void SavedEntranceFollowsRelocatedFloorAnchorInsteadOfOldWorldPosition()
        {
            string saved=Saved(Room(new Pose(Vector3.zero,Quaternion.identity)));
            Assert.IsNotEmpty(saved);
            var pose=new Pose(new Vector3(4,.4f,-3),Quaternion.Euler(0,90,0));
            Assert.IsTrue(RoomEntranceMemory.TryRestore(Room(pose),saved,pose.position,out var entrance));
            Assert.Less(Vector3.Distance(pose.position+pose.rotation*new Vector3(0,0,2.4f),entrance.Position),.0001f);
            Assert.Less(Vector3.Distance(pose.rotation*Vector3.back,entrance.Forward),.0001f);
            Assert.IsTrue(entrance.IsManual);
        }
        [Test]
        public void DifferentRoomCannotReuseSavedEntrance()
        {
            var room=Room(new Pose(Vector3.zero,Quaternion.identity));
            Assert.IsFalse(RoomEntranceMemory.TryRestore(Room(new Pose(Vector3.zero,Quaternion.identity),"different-floor"),Saved(room),Vector3.zero,out _));
        }
        [Test]
        public void MovedCoreOrFurnitureCanInvalidateSavedEntrance()
        {
            var room=Room(new Pose(Vector3.zero,Quaternion.identity));string saved=Saved(room);
            Assert.IsFalse(RoomEntranceMemory.TryRestore(room,saved,new Vector3(0,0,2),out _));
            room.Volumes.Add(new RoomVolume {Center=new Vector3(0,.5f,2.4f),Size=Vector3.one});
            Assert.IsFalse(RoomEntranceMemory.TryRestore(room,saved,Vector3.zero,out _));
        }
        [TestCase("")]
        [TestCase("not-json")]
        [TestCase("{}")]
        [TestCase("{\"version\":2,\"floorId\":\"saved-floor\"}")]
        public void MissingOrInvalidSavedDataIsIgnored(string json)
        {
            Assert.IsFalse(RoomEntranceMemory.TryRestore(Room(new Pose(Vector3.zero,Quaternion.identity)),json,Vector3.zero,out _));
        }
        [Test]
        public void UnanchoredSandboxNeverProducesSavedRealRoomCoordinates()
        {
            Assert.IsNull(Saved(SandboxRoom.Create(Vector3.zero)));
        }
    }
}
