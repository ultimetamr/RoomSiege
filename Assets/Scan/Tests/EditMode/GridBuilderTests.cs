using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace RoomSiege.Scan.Tests
{
    public sealed class GridBuilderTests
    {
        [TestCase(false)] [TestCase(true)]
        public void BoxRotationPreservesAnchorAndTrackingWhenNativeLocalRotationIsUnavailable(bool validLocal)
        {
            var origin=new GameObject("box rotation origin");
            try
            {
                origin.transform.rotation=Quaternion.Euler(0,35,0);
                var anchor=Quaternion.Euler(0,65,0);
                var local=validLocal?Quaternion.Euler(0,20,0):new Quaternion();
                var actual=PicoRoomScanner.BoxWorldRotation(origin.transform,anchor,local);
                var expected=origin.transform.rotation*anchor*(validLocal?local:Quaternion.identity);
                Assert.Less(Quaternion.Angle(expected,actual),.01f);
            }
            finally{Object.DestroyImmediate(origin);}
        }

        [Test]public void NearbyCoreKeepsValidPositionAndRejectsDistantOrMissingFloor()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(6,6)));
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.IsTrue(GridBuilder.TryFindNearbyCore(grid,out var center));Assert.AreEqual(Vector3.zero,center);
            var distant=GridBuilder.WithSafeZone(grid,new Vector3(10,0,10));
            Assert.IsFalse(GridBuilder.TryFindNearbyCore(distant,out _));
            room.Volumes.Add(new RoomVolume {Id="solid",Center=Vector3.zero,Size=new Vector3(6,2,6),Rotation=Quaternion.identity});
            Assert.IsFalse(GridBuilder.TryFindNearbyCore(GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom),out _));
            Assert.IsFalse(GridBuilder.TryFindNearbyCore(null,out _));
        }

        [Test]public void CalibrationFurnitureOccupancyUsesLocatedBoxOrientation()
        {
            var volume=new RoomVolume {Center=new Vector3(1,.5f,2),Size=new Vector3(2,1,.3f),Rotation=Quaternion.Euler(0,90,0)};
            Assert.IsTrue(GridBuilder.FurnitureContains(volume,new Vector3(1,.05f,2.8f)));
            Assert.IsFalse(GridBuilder.FurnitureContains(volume,new Vector3(1.8f,.05f,2)));
        }

        [Test]
        public void SandboxHasOneEntranceAndUsableFloorTableAndCabinet()
        {
            var room=SandboxRoom.Create(Vector3.zero);
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.Sandbox);
            Assert.That(GridBuilder.IsPlayable(room,grid),Is.True);
            Assert.That(grid.SpawnPoints.Count,Is.EqualTo(1));
            Assert.That(grid.PlaceableCells.Count,Is.GreaterThanOrEqualTo(8));
            Assert.That(grid.PlaceableCells.Any(c=>c.Surface==SurfaceKind.Floor),Is.True);
            Assert.That(grid.PlaceableCells.Any(c=>c.Surface==SurfaceKind.Table),Is.True);
            Assert.That(grid.PlaceableCells.Any(c=>c.Surface==SurfaceKind.LowCabinet),Is.True);
        }

        [TestCase(0f,0f)]
        [TestCase(1.2f,-0.8f)]
        [TestCase(-0.75f,1.1f)]
        public void EveryCellAvoidsCoreBodyAndEntrancesWithoutExcludingTheWholeRing(float x,float z)
        {
            var room=SandboxRoom.Create(Vector3.zero);
            var grid=GridBuilder.Build(room,new Vector3(x,1.7f,z),ScanMode.Sandbox);
            Assert.That(grid.SafeZoneCenter,Is.EqualTo(new Vector3(x,0f,z)));
            foreach(var cell in grid.PlaceableCells)
            {
                var rect=CellRect(cell);
                Assert.That(GridBuilder.DistanceToRect(new Vector2(x,z),rect),Is.GreaterThanOrEqualTo(GridModel.CoreBuildRadius-.0001f));
                foreach(var spawn in grid.SpawnPoints) Assert.That(GridBuilder.OverlapsSpawn(rect,spawn),Is.False);
                Assert.That(GridBuilder.FitsPolygon(rect,room.Surfaces.Single(s=>s.Id==cell.SurfaceId).Polygon),Is.True);
            }
        }

        [Test]
        public void CentreOnlySafetyTestWouldIncorrectlyAllowThisCell()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,new Vector3(.2f,0,.2f),new Vector2(.4f,.4f)));
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.That(Vector2.Distance(Vector2.zero,new Vector2(.2f,.2f)),Is.GreaterThan(GridModel.CoreBuildRadius));
            Assert.That(grid.PlaceableCells,Is.Empty);
        }

        [Test]
        public void FurnitureFootprintsBlockFloorButPermitSupportedTop()
        {
            var room=SandboxRoom.Create(Vector3.zero);
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.Sandbox);
            foreach(var cell in grid.PlaceableCells.Where(c=>c.Surface==SurfaceKind.Floor))
            foreach(var volume in room.Volumes)
            {
                var footprint=new Rect(volume.Center.x-volume.Size.x/2,volume.Center.z-volume.Size.z/2,volume.Size.x,volume.Size.z);
                Assert.That(CellRect(cell).Overlaps(footprint),Is.False,$"cell {cell.Center} intersects {volume.Id}");
            }
            Assert.That(grid.PlaceableCells.Where(c=>c.Surface==SurfaceKind.Table).All(c=>Mathf.Abs(c.Center.y-0.72f)<0.0001f),Is.True);
        }

        [Test]
        public void EntranceSelectionLimitsToTwoAndDeduplicatesDoorAndOpening()
        {
            var room=SandboxRoom.Create(Vector3.zero);
            room.Entrances.Add(new SpawnPoint("a",new Vector3(2.8f,0,0),Vector3.left,Vector2.one));
            room.Entrances.Add(new SpawnPoint("b",new Vector3(2.7f,0,0),Vector3.left,Vector2.one));
            room.Entrances.Add(new SpawnPoint("c",new Vector3(-2.8f,0,0),Vector3.right,Vector2.one));
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.That(grid.SpawnPoints.Count,Is.EqualTo(2));
            Assert.That(grid.SpawnPoints.Select(s=>s.AnchorId),Is.EqualTo(new[] {"a","c"}));
        }

        [Test]
        public void EntranceOverlappingSafeZoneIsRejected()
        {
            var spawns=GridBuilder.SelectEntrances(new[] {new SpawnPoint("unsafe",Vector3.zero,Vector3.forward,Vector2.one)},Vector3.zero);
            Assert.That(spawns,Is.Empty);
        }

        [Test]
        public void WholeCellRejectsConcaveNotchEvenWhenCornersAreInside()
        {
            var polygon=new[] { new Vector2(0,0),new Vector2(2,0),new Vector2(2,2),new Vector2(1.1f,2),
                new Vector2(1.1f,0.9f),new Vector2(0.9f,0.9f),new Vector2(0.9f,2),new Vector2(0,2) };
            Assert.That(GridBuilder.FitsPolygon(new Rect(0.8f,0.8f,0.4f,0.4f),polygon),Is.False);
        }

        [Test]
        public void RoomTooSmallOrNoPortalFailsPlayability()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(1f,1f)));
            Assert.That(GridBuilder.IsPlayable(room,GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom)),Is.False);
            room=SandboxRoom.Create(Vector3.zero); room.Entrances.Clear();
            Assert.That(GridBuilder.IsPlayable(room,GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom)),Is.False);
        }

        [Test]
        public void RealRoomFixtureProducesActualWorldCoordinatesAndNeverChangesMode()
        {
            var center=new Vector3(10f,2f,-4f);
            var room=new RoomSnapshot {FloorHeight=2f};
            room.Surfaces.Add(SandboxRoom.Rectangle("floor-anchor-guid",SurfaceKind.Floor,center,new Vector2(6f,6f)));
            room.Entrances.Add(new SpawnPoint("door-anchor-guid",center+new Vector3(0,0,2.8f),Vector3.back,new Vector2(1,0.8f)));
            var model=GridBuilder.Build(room,center+Vector3.up*1.6f,ScanMode.RealRoom);
            Assert.That(model.Mode,Is.EqualTo(ScanMode.RealRoom));
            Assert.That(model.SafeZoneCenter,Is.EqualTo(center));
            Assert.That(model.SpawnPoints[0].Position.y,Is.EqualTo(2f));
            Assert.That(GridBuilder.IsPlayable(room,model),Is.True);
            foreach(var cell in model.PlaceableCells)
            {
                Assert.That(cell.Center.x,Is.InRange(7f,13f));
                Assert.That(cell.Center.z,Is.InRange(-7f,-1f));
                Assert.That(cell.Center.y,Is.EqualTo(2f));
            }
        }

        [Test]
        public void GridSnapshotsRemainStableWhenInputsChange()
        {
            var room=SandboxRoom.Create(Vector3.zero);
            var model=GridBuilder.Build(room,Vector3.zero,ScanMode.Sandbox);
            var saved=model.PlaceableCells.Select(c=>c.Center).ToArray();
            room.Surfaces.Clear(); room.Entrances.Clear();
            CollectionAssert.AreEqual(saved,model.PlaceableCells.Select(c=>c.Center));
            Assert.That(model.SpawnPoints.Count,Is.EqualTo(1));
        }

        [Test]
        public void DuplicateSupportPolygonsDoNotDuplicateGridCoordinates()
        {
            var room=SandboxRoom.Create(Vector3.zero);
            room.Surfaces.Add(room.Surfaces[0]);
            var model=GridBuilder.Build(room,Vector3.zero,ScanMode.Sandbox);
            Assert.That(model.PlaceableCells.Select(c=>c.Center).Distinct().Count(),Is.EqualTo(model.PlaceableCells.Count));
        }

        [Test]
        public void RotatedEntranceFootprintExcludesCrossingCells()
        {
            var entrance=new SpawnPoint("diagonal",new Vector3(2,0,2),new Vector3(1,0,1),new Vector2(1.2f,0.8f));
            Assert.That(GridBuilder.OverlapsSpawn(new Rect(1.8f,1.8f,0.4f,0.4f),entrance),Is.True);
            Assert.That(GridBuilder.OverlapsSpawn(new Rect(-2,-2,0.4f,0.4f),entrance),Is.False);
        }

        private static Rect CellRect(GridCell cell) => new Rect(cell.Center.x-0.2f,cell.Center.z-0.2f,0.4f,0.4f);
        [Test]public void CoreRingAndOldClearanceBandPermitConstructionOutsideCoreBody()
        {
            var grid=GridBuilder.Build(SandboxRoom.Create(Vector3.zero),Vector3.zero,ScanMode.Sandbox);
            Assert.IsTrue(grid.PlaceableCells.Any(c=>c.Surface==SurfaceKind.Floor&&c.Center.magnitude<GridModel.SafeRadius),
                "The 0.8m leak ring must not prohibit construction");
            Assert.IsTrue(grid.PlaceableCells.Any(c=>c.Surface==SurfaceKind.Floor&&c.Center.magnitude>.8f&&c.Center.magnitude<1.05f),
                "The previous 0.25m margin must also be available");
            foreach(var cell in grid.PlaceableCells)
                Assert.GreaterOrEqual(GridBuilder.DistanceToRect(Vector2.zero,CellRect(cell)),GridModel.CoreBuildRadius-.0001f);
        }

        [Test]public void BossRouteCellsAreReservedAndNeverAppearAsPlaceableFloor()
        {
            var room=SandboxRoom.Create(Vector3.zero);
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.Sandbox);
            Assert.IsNotEmpty(grid.ReservedRouteCells);
            foreach(var cell in grid.ReservedRouteCells)
            {
                Assert.IsTrue(grid.SupportedCells.Any(c=>c.SurfaceId==cell.SurfaceId&&c.Index==cell.Index));
                Assert.IsFalse(grid.PlaceableCells.Any(c=>c.SurfaceId==cell.SurfaceId&&c.Index==cell.Index));
            }
            var spawn=grid.SpawnPoints[0];
            Assert.IsTrue(grid.WalkMap.TryFindPath(spawn.Position+spawn.Forward*.65f,grid.SafeZoneCenter,GridModel.SafeRadius,out var path));
            foreach(var point in path)
            foreach(var cell in grid.PlaceableCells.Where(c=>c.Surface==SurfaceKind.Floor))
                Assert.Greater(GridBuilder.DistanceToRect(new Vector2(point.x,point.z),CellRect(cell)),RoomWalkMap.EnemyClearance);
        }

        [Test]public void MovingSafeZoneReusesRoomAndPortalsWhileRecomputingOnlyPurchaseExclusion()
        {
            var original=GridBuilder.Build(SandboxRoom.Create(Vector3.zero),Vector3.zero,ScanMode.Sandbox);
            var moved=GridBuilder.WithSafeZone(original,new Vector3(.8f,1.7f,-.8f));
            Assert.AreEqual(new Vector3(.8f,0,-.8f),moved.SafeZoneCenter);
            Assert.AreSame(original.SupportedCells,moved.SupportedCells);
            Assert.AreSame(original.SpawnPoints,moved.SpawnPoints);
            Assert.AreSame(original.WalkMap,moved.WalkMap);
            foreach(var cell in moved.PlaceableCells)
                Assert.GreaterOrEqual(GridBuilder.DistanceToRect(new Vector2(.8f,-.8f),CellRect(cell)),GridModel.CoreBuildRadius-.0001f);
            var restored=GridBuilder.WithSafeZone(moved,Vector3.zero);
            CollectionAssert.AreEqual(original.PlaceableCells,restored.PlaceableCells);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NativeFloorPolygonUsesLocatedPoseAndTrackingOrigin(bool xyPlane)
        {
            var origin=new GameObject("Tracking origin fixture");
            try
            {
                origin.transform.SetPositionAndRotation(new Vector3(3,.4f,-2),Quaternion.Euler(0,35,0));
                var room=new RoomSnapshot();
                var vertices=new[]{new Vector2(-2,-2),new Vector2(2,-2),new Vector2(2,2),new Vector2(-2,2)};
                var rotation=xyPlane?Quaternion.Euler(90,0,0):Quaternion.identity;
                Assert.IsTrue(PicoRoomScanner.TryAddFloor(room,"native-floor",origin.transform,new Vector3(0,-.2f,0),rotation,vertices));
                Assert.AreEqual(1,room.Surfaces.Count);Assert.AreEqual(.2f,room.Surfaces[0].Height,.0001f);
                Assert.AreEqual(16,GridBuilder.Area(room.Surfaces[0].Polygon),.001f);
                var expected=origin.transform.TransformPoint(new Vector3(-2,-.2f,-2));
                Assert.AreEqual(expected.x,room.Surfaces[0].Polygon[0].x,.0001f);
                Assert.AreEqual(expected.z,room.Surfaces[0].Polygon[0].y,.0001f);
            }
            finally{Object.DestroyImmediate(origin);}
        }

        [TestCase(false)]
        [TestCase(true)]
        public void SmallRoomCaptureTiltProducesLevelPlayableGrid(bool xyPlane)
        {
            var room=new RoomSnapshot();
            var points=new[]{new Vector2(-3,-3),new Vector2(3,-3),new Vector2(3,3),new Vector2(-3,3)};
            var rotation=Quaternion.Euler(xyPlane?88.5f:1.5f,0,0);
            Assert.IsTrue(PicoRoomScanner.TryAddFloor(room,"slightly-tilted",null,new Vector3(0,.1f,0),rotation,points));
            Assert.AreEqual(.1f,room.Surfaces[0].Height,.0001f);
            room.FloorHeight=room.Surfaces[0].Height;
            room.Entrances.Add(new SpawnPoint("manual",new Vector3(0,.1f,2.6f),Vector3.back,Vector2.one,true));
            var grid=GridBuilder.Build(room,new Vector3(0,1.7f,0),ScanMode.RealRoom);
            Assert.IsTrue(GridBuilder.IsPlayable(room,grid));
            Assert.IsTrue(grid.PlaceableCells.All(cell=>Mathf.Abs(cell.Center.y-.1f)<.0001f));
            Assert.AreEqual(.1f,grid.SafeZoneCenter.y,.0001f);
        }

        [TestCase(6.08f)]
        [TestCase(108.31f)]
        public void CapturedWideFloorNearSlopeLimitIsNotRejectedByAbsoluteHeightSpan(float yaw)
        {
            var room=new RoomSnapshot();
            var position=new Vector3(-.161f,-1.22f,-.316f);
            var rotation=Quaternion.Euler(272.92f,yaw,253.30f);
            var points=new[]{new Vector2(-5,-4),new Vector2(5,-4),new Vector2(5,4),new Vector2(-5,4)};
            var heights=points.Select(p=>(position+rotation*new Vector3(p.x,p.y,0)).y).ToArray();
            Assert.Greater(heights.Max()-heights.Min(),.25f,"Reproduce the former room-width-dependent rejection");
            Assert.IsTrue(PicoRoomScanner.TryAddFloor(room,"captured-wide-floor",null,position,rotation,points));
            room.FloorHeight=room.Surfaces.Single().Height;
            Assert.AreEqual(position.y,room.FloorHeight,.001f);
            var grid=GridBuilder.Build(room,position+Vector3.up*1.6f,ScanMode.RealRoom);
            Assert.Greater(grid.PlaceableCells.Count,GridBuilder.MinimumCells);
            Assert.IsNotNull(grid.WalkMap);
            foreach(var cell in grid.PlaceableCells)Assert.AreEqual(room.FloorHeight,cell.Center.y);
        }

        [Test]public void AutomaticEntranceCannotUseDisconnectedFloorOrIgnoreRouteRejection()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("core-floor",SurfaceKind.Floor,Vector3.zero,new Vector2(2,2)));
            room.Surfaces.Add(SandboxRoom.Rectangle("distant-floor",SurfaceKind.Floor,Vector3.right*5,new Vector2(4,4)));
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.IsFalse(GridBuilder.TryCreateAutomaticEntrance(room,grid,null,out _),"Distant disconnected floor must not provide an entrance");
            room.Surfaces.Clear();room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(6,6)));
            grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.IsFalse(GridBuilder.TryCreateAutomaticEntrance(room,grid,_=>"blocked",out _));
            Assert.IsTrue(GridBuilder.TryCreateAutomaticEntrance(room,grid,null,out var first));
            Assert.IsTrue(GridBuilder.TryCreateAutomaticEntrance(room,grid,null,out var second));
            Assert.AreEqual(first.Position,second.Position,"Same scan produces a stable entrance");
        }

        [Test]public void CalibrationPreviewChecksActualRouteWithoutChangingExistingEntrance()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(8,8)));
            var old=new SpawnPoint("old",new Vector3(-3,0,0),Vector3.right,Vector2.one,true);
            room.Entrances.Add(old);
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            var ray=new Ray(new Vector3(0,1.6f,2.8f),Vector3.down);
            Assert.IsFalse(GridBuilder.TryCalibrateEntrance(room,grid,ray,_=>"通路受阻",out _,out var target,out var failure));
            Assert.IsTrue(target.HasValue);StringAssert.Contains("通路",failure);
            Assert.AreEqual(old.Position,room.Entrances.Single().Position);
            Assert.IsTrue(GridBuilder.TryCalibrateEntrance(room,grid,ray,_=>"",out var entrance,out target,out failure),failure);
            Assert.AreEqual(target.Value,entrance.Position);
            Assert.GreaterOrEqual(GridBuilder.HorizontalDistance(entrance.Position,grid.SafeZoneCenter),1.8f);
            Assert.AreEqual(old.Position,room.Entrances.Single().Position,"Preview never replaces a saved entrance");
        }

        [TestCase(0,1f,"1.8m")]
        [TestCase(0,6f,"地板")]
        public void CalibrationExplainsInvalidFloorTarget(float x,float z,string reason)
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(8,8)));
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.IsFalse(GridBuilder.TryCalibrateEntrance(room,grid,new Ray(new Vector3(x,1.6f,z),Vector3.down),
                _=>"",out _,out var target,out var failure));
            Assert.IsTrue(target.HasValue);StringAssert.Contains(reason,failure);Assert.IsEmpty(room.Entrances);
        }

        [Test]public void CalibrationExplainsFurnitureAndUpwardRayInsteadOfSilentlyIgnoringPress()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(8,8)));
            room.Volumes.Add(new RoomVolume {Center=new Vector3(0,.4f,2.8f),Size=Vector3.one});
            var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
            Assert.IsFalse(GridBuilder.TryCalibrateEntrance(room,grid,new Ray(new Vector3(0,1.6f,2.8f),Vector3.down),
                _=>"",out _,out _,out var failure));StringAssert.Contains("家具",failure);
            Assert.IsFalse(GridBuilder.TryCalibrateEntrance(room,grid,new Ray(Vector3.up,Vector3.up),
                _=>"",out _,out var target,out failure));Assert.IsNull(target);StringAssert.Contains("向下",failure);
        }

        [Test]public void UninitializedTiltedAndNonFiniteNativeFloorDataAreRejected()
        {
            var room=new RoomSnapshot();var points=new[]{new Vector2(-2,-2),new Vector2(2,-2),new Vector2(2,2),new Vector2(-2,2)};
            Assert.IsFalse(PicoRoomScanner.TryAddFloor(room,"missing-pose",null,Vector3.zero,new Quaternion(),points));
            Assert.IsFalse(PicoRoomScanner.TryAddFloor(room,"tilted",null,Vector3.zero,Quaternion.Euler(45,0,0),points));
            Assert.IsFalse(PicoRoomScanner.TryAddFloor(room,"steep",null,Vector3.zero,Quaternion.Euler(5,0,0),points));
            points[0]=new Vector2(float.NaN,0);
            Assert.IsFalse(PicoRoomScanner.TryAddFloor(room,"bad-data",null,Vector3.zero,Quaternion.Euler(90,0,0),points));
            Assert.AreEqual(0,room.Surfaces.Count);
        }
    }
}
