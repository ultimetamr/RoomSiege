using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Scan;
using UnityEngine;

namespace RoomSiege.Towers.Tests
{
    public sealed class TowerRouteTests
    {
        [System.Serializable] private sealed class CapturedRoom
        {
            public Vector3 safeCenter;
            public float floorHeight;
            public DeploySurface[] surfaces;
            public RoomVolume[] volumes;
            public Vector3[] entrances,entranceForwards;
        }

        [Test]public void CapturedFurnitureBlockedCoreRecoversLocallyAndEntranceCanReachIt()
        {
            var captured=JsonUtility.FromJson<CapturedRoom>(File.ReadAllText("Assets/Towers/Tests/EditMode/Fixtures/BlockedCoreRoom.json"));
            var room=new RoomSnapshot {FloorHeight=captured.floorHeight};
            room.Surfaces.AddRange(captured.surfaces);room.Volumes.AddRange(captured.volumes);
            for(int i=0;i<captured.entrances.Length;i++)
                room.Entrances.Add(new SpawnPoint("manual-"+i,captured.entrances[i],captured.entranceForwards[i],new Vector2(.8f,.8f),true));
            var original=GridBuilder.Build(room,captured.safeCenter,ScanMode.RealRoom);
            var blocked=new TowerRouteMap(original,new TowerInstance[0]);
            Assert.IsFalse(blocked.AllEntrancesReachable());Assert.IsNotEmpty(blocked.ReadinessFailure);
            Assert.IsTrue(GridBuilder.TryFindNearbyCore(original,out var core));
            Assert.That(GridBuilder.HorizontalDistance(core,captured.safeCenter),Is.InRange(1.5f,2f));
            var recovered=GridBuilder.WithSafeZone(original,core);
            var routes=new TowerRouteMap(recovered,new TowerInstance[0]);
            Assert.IsTrue(routes.AllEntrancesReachable(),routes.NavigationSummary);
            var spawn=recovered.SpawnPoints[0];
            Assert.IsTrue(routes.TrySpawn(spawn.Position+spawn.Forward*.65f,out var feet));
            for(int i=0;i<3600&&GridBuilder.HorizontalDistance(feet,core)>.89f;i++)
            {
                var next=routes.Move(feet,.5f,1f/60,i/60f,1,false);
                Assert.IsTrue(routes.SegmentClear(feet,next));feet=next;
            }
            Assert.LessOrEqual(GridBuilder.HorizontalDistance(feet,core),.89f,"Actual movement must reach the recovered core; feet="+feet);
        }

        [Test]public void CapturedEntranceFitsNearbyFloorWithFullSpawnLanesAndActualRoute()
        {
            var captured=JsonUtility.FromJson<CapturedRoom>(File.ReadAllText("Assets/Towers/Tests/EditMode/Fixtures/BlockedEntranceRoom.json"));
            var room=new RoomSnapshot {FloorHeight=captured.floorHeight};
            room.Surfaces.AddRange(captured.surfaces);room.Volumes.AddRange(captured.volumes);
            var requested=new SpawnPoint("0-manual-entrance",captured.entrances[0],captured.entranceForwards[0],new Vector2(.8f,.8f),true);
            room.Entrances.Add(requested);
            var grid=GridBuilder.Build(room,captured.safeCenter,ScanMode.RealRoom);
            string Failure(GridModel candidate)=>new TowerRouteMap(candidate,new TowerInstance[0]).ReadinessFailure;
            Assert.IsNotEmpty(Failure(grid),"Captured entrance's inward spawn point is blocked");
            Assert.IsTrue(GridBuilder.TryFitManualEntrance(room,grid,requested,Failure,out var fitted));
            Assert.That(GridBuilder.HorizontalDistance(fitted.Position,requested.Position),Is.InRange(.01f,.801f));
            Assert.GreaterOrEqual(GridBuilder.HorizontalDistance(fitted.Position,grid.SafeZoneCenter),1.8f);
            room.Entrances.Clear();room.Entrances.Add(fitted);
            var model=GridBuilder.Build(room,grid.SafeZoneCenter,ScanMode.RealRoom);
            var routes=new TowerRouteMap(model,new TowerInstance[0]);
            Assert.IsTrue(routes.AllEntrancesReachable(),routes.NavigationSummary);
            Assert.IsTrue(GridBuilder.TryFitManualEntrance(room,model,fitted,Failure,out var stable));
            Assert.AreEqual(fitted.Position,stable.Position,"A valid saved entrance must not drift on each load");
            for(int lane=-1;lane<=1;lane++)
            {
                var entry=fitted.Position+fitted.Forward*.65f+Vector3.Cross(Vector3.up,fitted.Forward)*(lane*.192f)-fitted.Forward*.05f;
                Assert.IsTrue(routes.CanReach(entry));Assert.IsTrue(routes.TrySpawn(entry,out var feet));
                for(int i=0;i<4800&&GridBuilder.HorizontalDistance(feet,model.SafeZoneCenter)>.89f;i++)
                {var next=routes.Move(feet,.5f,1f/60,i/60f,lane+2,false);Assert.IsTrue(routes.SegmentClear(feet,next));feet=next;}
                Assert.LessOrEqual(GridBuilder.HorizontalDistance(feet,model.SafeZoneCenter),.89f,"Every staggered lane must reach the core");
            }
            var distant=new SpawnPoint("distant",new Vector3(20,room.FloorHeight,20),Vector3.back,new Vector2(.8f,.8f),true);
            Assert.IsFalse(GridBuilder.TryFitManualEntrance(room,model,distant,Failure,out _),"Never relocate an entrance across the room");
        }

        [Test]public void RotatedWallMountedBoxDoesNotBlockGroundButSameBoxAtFloorDoes()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(6,6)));
            room.Volumes.Add(new RoomVolume {Id="rotated",Center=new Vector3(0,1.5f,0),
                Rotation=Quaternion.Euler(90,30,0),Size=new Vector3(.8f,2f,.4f)});
            var overhead=RoomWalkMap.Build(room);
            Assert.IsTrue(overhead.SegmentOpen(Vector3.back,Vector3.forward),"World height, not the box's local Y axis, determines floor obstruction");
            room.Volumes[0].Center=new Vector3(0,.3f,0);
            var grounded=RoomWalkMap.Build(room);
            Assert.IsFalse(grounded.SegmentOpen(Vector3.back,Vector3.forward),"Grounded rotated furniture remains solid");
        }

        [Test]public void TinyDiagonalCornerCrossingCannotSkipBlockedSideCell()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(6,6)));
            room.Volumes.Add(new RoomVolume {Id="obstacle",Center=new Vector3(0,.25f,0),Size=new Vector3(.2f,.5f,.2f)});
            var map=RoomWalkMap.Build(room);
            var a=new Vector3(.501f,0,.499f);var b=new Vector3(.499f,0,.501f);
            Assert.IsTrue(map.Open(map.Index(a)));Assert.IsTrue(map.Open(map.Index(b)));
            Assert.IsFalse(map.SegmentOpen(a,b),"Even a sub-sample corner crossing checks both side cells");
            Assert.IsFalse(map.SegmentOpen(b,a));
        }

        [Test]public void RescannedRotatedFurnitureRoomHasUsableEntranceAndActualMovement()
        {
            var captured=JsonUtility.FromJson<CapturedRoom>(File.ReadAllText("Assets/Towers/Tests/EditMode/Fixtures/RotatedFurnitureRoom.json"));
            var room=new RoomSnapshot {FloorHeight=captured.floorHeight};
            room.Surfaces.AddRange(captured.surfaces);room.Volumes.AddRange(captured.volumes);
            var requested=new SpawnPoint("0-manual-entrance",captured.entrances[0],captured.entranceForwards[0],new Vector2(.8f,.8f),true);
            room.Entrances.Add(requested);
            var grid=GridBuilder.Build(room,captured.safeCenter,ScanMode.RealRoom);
            Assert.IsTrue(GridBuilder.TryFindNearbyCore(grid,out var usableCore));
            grid=GridBuilder.WithSafeZone(grid,usableCore);
            Assert.That(GridBuilder.HorizontalDistance(usableCore,captured.safeCenter),Is.InRange(1.8f,2f));
            string Failure(GridModel candidate)=>new TowerRouteMap(candidate,new TowerInstance[0]).ReadinessFailure;
            Assert.IsTrue(GridBuilder.TryFitManualEntrance(room,grid,requested,Failure,out var fitted),new TowerRouteMap(grid,new TowerInstance[0]).NavigationSummary);
            room.Entrances.Clear();room.Entrances.Add(fitted);
            var model=GridBuilder.Build(room,usableCore,ScanMode.RealRoom);
            var routes=new TowerRouteMap(model,new TowerInstance[0]);
            Assert.IsTrue(routes.AllEntrancesReachable(),routes.NavigationSummary);
            Assert.IsTrue(routes.TrySpawn(fitted.Position+fitted.Forward*.65f,out var feet));
            for(int i=0;i<7200&&GridBuilder.HorizontalDistance(feet,model.SafeZoneCenter)>.89f;i++)
            {var next=routes.Move(feet,.5f,1f/60,i/60f,1,false);Assert.IsTrue(routes.SegmentClear(feet,next));feet=next;}
            Assert.LessOrEqual(GridBuilder.HorizontalDistance(feet,model.SafeZoneCenter),.89f);
        }

        [TestCase("BlockedCoreRoom")]
        [TestCase("BlockedEntranceRoom")]
        [TestCase("RotatedFurnitureRoom")]
        public void CapturedRoomWithoutDoorAutomaticallyPreparesReachableEntrance(string fixture)
        {
            var captured=JsonUtility.FromJson<CapturedRoom>(File.ReadAllText("Assets/Towers/Tests/EditMode/Fixtures/"+fixture+".json"));
            var room=new RoomSnapshot {FloorHeight=captured.floorHeight};
            room.Surfaces.AddRange(captured.surfaces);room.Volumes.AddRange(captured.volumes);
            var grid=GridBuilder.Build(room,captured.safeCenter,ScanMode.RealRoom);
            if(GridBuilder.TryFindNearbyCore(grid,out var core))grid=GridBuilder.WithSafeZone(grid,core);
            string Failure(GridModel candidate)=>new TowerRouteMap(candidate,new TowerInstance[0]).ReadinessFailure;
            Assert.IsTrue(GridBuilder.TryCreateAutomaticEntrance(room,grid,Failure,out var entrance),fixture);
            Assert.IsEmpty(room.Entrances,"Choosing an entrance must not mutate the source room");
            Assert.IsTrue(GridBuilder.TryCreateAutomaticEntrance(room,grid,null,out var beforeTowersReady),"Startup must work before the Towers callback is attached");
            room.Entrances.Add(beforeTowersReady);
            Assert.IsTrue(new TowerRouteMap(GridBuilder.Build(room,grid.SafeZoneCenter,ScanMode.RealRoom),new TowerInstance[0]).AllEntrancesReachable());
            room.Entrances.Clear();room.Entrances.Add(entrance);
            var model=GridBuilder.Build(room,grid.SafeZoneCenter,ScanMode.RealRoom);
            Assert.IsTrue(GridBuilder.IsPlayable(room,model));
            var routes=new TowerRouteMap(model,new TowerInstance[0]);Assert.IsTrue(routes.AllEntrancesReachable(),routes.NavigationSummary);
            for(int lane=-1;lane<=1;lane++)
            {
                var feet=entrance.Position+entrance.Forward*.65f+Vector3.Cross(Vector3.up,entrance.Forward)*(lane*.192f)-entrance.Forward*.05f;
                Assert.IsTrue(routes.CanReach(feet));
                for(int i=0;i<7200&&GridBuilder.HorizontalDistance(feet,model.SafeZoneCenter)>.89f;i++)
                {var next=routes.Move(feet,.5f,1f/60,i/60f,lane+2,false);Assert.IsTrue(routes.SegmentClear(feet,next));feet=next;}
                Assert.LessOrEqual(GridBuilder.HorizontalDistance(feet,model.SafeZoneCenter),.89f,fixture+" lane "+lane);
            }
        }

        private sealed class Wallet:ITowerWallet
        {public int Coins{get;private set;}=10000;public bool TrySpend(int cost){if(cost>Coins)return false;Coins-=cost;return true;}public void Credit(int value){Coins+=value;}}
        private TowerPlacement placement;
        private Wallet wallet;
        private GridModel grid;
        [SetUp]public void Setup()
        {
            var room=new RoomSnapshot();
            room.Surfaces.Add(SandboxRoom.Rectangle("floor",SurfaceKind.Floor,Vector3.zero,new Vector2(6,6)));
            room.Entrances.Add(new SpawnPoint("door",new Vector3(0,0,2.8f),Vector3.back,new Vector2(1,.8f)));
            grid=GridBuilder.Build(room,new Vector3(0,0,-1.8f),ScanMode.Sandbox);
            wallet=new Wallet();placement=new TowerPlacement(BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json")),wallet);
            placement.SetGrid(grid);
            Assert.IsTrue(placement.Routes.AllEntrancesReachable(),"Open room baseline");
        }
        [Test]public void OrdinaryEnemyRoutesAroundTowerAndReachesCoreWithoutCrossingItsFootprint()
        {
            var cell=grid.PlaceableCells.OrderBy(c=>(c.Center-new Vector3(0,0,.6f)).sqrMagnitude).First();
            Assert.IsTrue(placement.TryBuild("MG",cell,out var tower,out var reason),reason);
            Assert.IsTrue(placement.Routes.TrySpawn(new Vector3(0,0,2.15f),out var feet));
            bool detoured=false;
            for(int i=0;i<3600&&GridBuilder.HorizontalDistance(feet,grid.SafeZoneCenter)>1.08f;i++)
            {
                Vector3 next=placement.Routes.Move(feet,.5f,1f/60,i/60f,1,false);
                Assert.GreaterOrEqual(TowerCombat.SegmentDistanceXZ(tower.Cell.Center,feet,next),.50f-.001f);
                Assert.LessOrEqual(Vector3.Distance(feet,next),.5f/60+.0001f);
                detoured|=Mathf.Abs(next.x)>.4f;feet=next;
            }
            Assert.IsTrue(detoured);Assert.LessOrEqual(GridBuilder.HorizontalDistance(feet,grid.SafeZoneCenter),1.08f,"Must not get stuck");
        }
        [TestCase(1,false)]
        [TestCase(2,true)]
        public void BossPassageRequiresTwoEmptyDeploymentColumns(int emptyColumns,bool expected)
        {
            Assert.AreEqual(2,Mathf.CeilToInt((GridModel.BossBodyRadius*2)/GridModel.CellSize));
            var definition=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json")).GetTower("MG");
            var wall=new List<TowerInstance>();
            foreach(var cell in grid.SupportedCells.Where(c=>Mathf.Abs(c.Center.z-.6f)<.01f))
            {
                bool gap=emptyColumns==2?Mathf.Abs(cell.Center.x)<.4f:Mathf.Abs(cell.Center.x-.2f)<.01f;
                if(!gap)wall.Add(TowerInstance.CreatePreview(definition,cell));
            }
            var routes=new TowerRouteMap(grid,wall);
            Assert.AreEqual(expected,routes.AllEntrancesReachable(),routes.NavigationSummary);
            if(!expected)return;
            Assert.IsTrue(routes.TrySpawn(new Vector3(0,0,2.15f),out var feet));
            for(int i=0;i<3600&&GridBuilder.HorizontalDistance(feet,grid.SafeZoneCenter)>1.08f;i++)
            {
                var next=routes.Move(feet,.25f,1f/60,i/60f,1,false);
                foreach(var tower in wall)
                    Assert.GreaterOrEqual(TowerCombat.SegmentDistanceXZ(tower.Cell.Center,feet,next),.5f-.0001f);
                feet=next;
            }
            Assert.LessOrEqual(GridBuilder.HorizontalDistance(feet,grid.SafeZoneCenter),1.08f,"Boss must actually traverse the two-cell opening");
        }

        [Test]public void TowerInsideObjectiveRingIsAllowedWithoutBlockingBossRoute()
        {
            var inside=grid.PlaceableCells.Where(c=>c.Surface==SurfaceKind.Floor&&
                GridBuilder.HorizontalDistance(c.Center,grid.SafeZoneCenter)<GridModel.SafeRadius).ToArray();
            Assert.IsNotEmpty(inside);
            bool built=false;string failure="";
            foreach(var cell in inside)
                if(placement.TryBuild("MG",cell,out _,out failure)){built=true;break;}
            Assert.IsTrue(built,failure);Assert.AreEqual(9950,wallet.Coins);
            Assert.IsTrue(placement.Routes.AllEntrancesReachable());
        }
        [Test]public void FinalBlockingTowerIsRejectedWithoutChargeButTrapIsAllowedAndSellingReopensRoute()
        {
            // Exercise the secondary route guard with a legacy/custom snapshot
            // that has no reserved purchase mask. Normal scan snapshots already
            // reject purchases on ReservedRouteCells before this guard runs.
            var legacy=new GridModel(grid.Mode,grid.SafeZoneCenter,grid.SupportedCells.ToList(),grid.SpawnPoints.ToList(),grid.WalkMap);
            placement.SetGrid(legacy);
            var row=legacy.PlaceableCells.Where(c=>c.Surface==SurfaceKind.Floor&&Mathf.Abs(c.Center.z-.6f)<.01f).OrderBy(c=>c.Center.x).ToArray();
            var built=new List<TowerInstance>();GridCell rejected=default;bool blocked=false;
            foreach(var cell in row)
            {
                int before=wallet.Coins;
                if(placement.TryBuild("MG",cell,out var tower,out var reason)){built.Add(tower);continue;}
                StringAssert.Contains("通路",reason);Assert.AreEqual(before,wallet.Coins);rejected=cell;blocked=true;break;
            }
            Assert.IsTrue(blocked,"A wall across the room must not be accepted");
            Assert.IsTrue(placement.Routes.AllEntrancesReachable());
            Assert.IsTrue(placement.TryBuild("Trap",rejected,out var trap,out _));
            Assert.IsTrue(placement.TrySell(trap,out _));
            foreach(var tower in built)placement.TrySell(tower,out _);
            Assert.IsTrue(placement.TryBuild("MG",rejected,out _,out _));
        }
        [Test]public void ReservedBossRouteRejectsAllPurchasesWithoutCharge()
        {
            Assert.IsNotEmpty(grid.ReservedRouteCells);
            foreach(string id in new[]{"MG","Frost","Laser","Trap"})
            {
                int coins=wallet.Coins;
                Assert.IsFalse(placement.TryBuild(id,grid.ReservedRouteCells[0],out _,out var reason));
                StringAssert.Contains("Boss",reason);Assert.AreEqual(coins,wallet.Coins);
            }
        }
        [Test]public void ActiveEnemyCannotBeEnclosedOrHaveTowerBuiltOnTop()
        {
            var cell=grid.PlaceableCells.OrderBy(c=>(c.Center-new Vector3(0,0,.6f)).sqrMagnitude).First();
            placement.ActiveEnemyPositions=()=>new[]{cell.Center};int before=wallet.Coins;
            Assert.IsFalse(placement.TryBuild("MG",cell,out _,out _));Assert.AreEqual(before,wallet.Coins);
            Assert.IsTrue(placement.TryBuild("Trap",cell,out _,out _));
        }
        [Test]public void TableTowerDoesNotBlockGroundRoutes()
        {
            var table=new GridCell("table",SurfaceKind.Table,new Vector2Int(0,1),new Vector3(0,.72f,.6f));
            var cells=grid.PlaceableCells.ToList();cells.Add(table);
            placement.SetGrid(new GridModel(grid.Mode,grid.SafeZoneCenter,cells,grid.SpawnPoints.ToList(),grid.WalkMap));
            Assert.IsTrue(placement.TryBuild("Laser",table,out _,out _));
            Assert.IsTrue(placement.Routes.SegmentClear(new Vector3(0,0,1),new Vector3(0,0,.2f)));
        }
        [Test]public void FurnitureAndFloorBoundaryCannotBeCrossedEvenInLongFrame()
        {
            var room=SandboxRoom.Create(Vector3.zero);var model=GridBuilder.Build(room,Vector3.zero,ScanMode.Sandbox);
            var routes=new TowerRouteMap(model,new TowerInstance[0]);Assert.IsTrue(routes.AllEntrancesReachable());
            Assert.IsFalse(routes.SegmentClear(new Vector3(-1.8f,0,2.2f),new Vector3(-1.8f,0,.2f)));
            Assert.IsFalse(routes.SegmentClear(Vector3.zero,new Vector3(4,0,0)));
            Assert.IsTrue(routes.TrySpawn(new Vector3(0,0,2.15f),out var start));
            Vector3 next=routes.Move(start,1,5,0,1,false);Assert.IsTrue(routes.SegmentClear(start,next));
        }

        [Test]public void MovingSafetyCircleOverUpgradedTowerPreservesPurchaseAndReopensCellsWhenItLeaves()
        {
            var cell=grid.PlaceableCells.OrderBy(c=>(c.Center-new Vector3(.2f,0,.6f)).sqrMagnitude).First();
            Assert.IsTrue(placement.TryBuild("MG",cell,out var tower,out var reason),reason);
            Assert.IsTrue(placement.TryUpgrade(tower,3,out reason),reason);
            int coins=wallet.Coins,invested=tower.InvestedGold,id=tower.Id;
            var moved=GridBuilder.WithSafeZone(grid,cell.Center);
            Assert.IsTrue(placement.CanMoveCore(moved));
            placement.SetGrid(moved);
            Assert.IsFalse(tower.Removed);Assert.AreEqual(id,tower.Id);
            Assert.AreEqual(3,tower.Level);Assert.AreEqual(invested,tower.InvestedGold);
            Assert.AreEqual(cell.Center,tower.Cell.Center);Assert.AreEqual(coins,wallet.Coins,"No refund from walking");
            Assert.IsFalse(placement.TryCanonical(cell,out _));
            var other=moved.PlaceableCells.First(c=>c.Index!=cell.Index&&Vector3.Distance(c.Center,cell.Center)<.7f);
            Assert.IsTrue(placement.TryCanonical(other,out _),"Nearby cells inside the objective ring remain usable");
            Assert.IsFalse(placement.TryBuild("Trap",cell,out _,out _),"The core body itself remains excluded");Assert.AreEqual(coins,wallet.Coins);
            var restored=GridBuilder.WithSafeZone(moved,grid.SafeZoneCenter);placement.SetGrid(restored);
            Assert.IsTrue(placement.TryCanonical(cell,out _));Assert.AreSame(tower,placement.Towers.Single());
            Assert.IsFalse(placement.TryBuild("MG",cell,out _,out _),"Original tower still occupies this cell");
            Assert.IsTrue(placement.TrySell(tower,out int refund));Assert.AreEqual(112,refund);
            Assert.AreEqual(coins+112,wallet.Coins);
        }

        [Test]public void CoreMoveCannotStrandAnExistingEnemy()
        {
            placement.ActiveEnemyPositions=()=>new[]{new Vector3(100,0,100)};
            Assert.IsFalse(placement.CanMoveCore(GridBuilder.WithSafeZone(grid,Vector3.zero)));
            Assert.AreSame(grid,placement.Grid);Assert.AreEqual(10000,wallet.Coins);
        }

        [Test]public void FollowReusesValidatedRouteWithoutRefreshingPaidTowerViews()
        {
            var candidate=GridBuilder.WithSafeZone(grid,grid.SafeZoneCenter+Vector3.right*.06f);
            Assert.IsTrue(placement.CanMoveCore(candidate));
            var field=typeof(TowerPlacement).GetField("checkedCoreRoutes",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            var checkedRoutes=(TowerRouteMap)field.GetValue(placement);
            int notifications=0;placement.Changed+=()=>notifications++;
            placement.SetGrid(candidate);
            Assert.AreSame(checkedRoutes,placement.Routes,"Follow must adopt the already checked distance field, not build another one");
            Assert.AreSame(candidate,placement.Grid);
            Assert.AreEqual(0,notifications,"Moving the core must not refresh every tower and shop view");
            Assert.IsTrue(placement.Routes.AllEntrancesReachable());
            var cell=candidate.PlaceableCells.First(c=>c.Surface==SurfaceKind.Floor);
            Assert.IsTrue(placement.TryBuild("Trap",cell,out _,out var reason),reason);
            Assert.AreEqual(1,notifications,"Actual purchases must still refresh views");
        }
    }
}
