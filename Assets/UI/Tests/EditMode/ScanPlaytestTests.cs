using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Meta;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Waves;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RoomSiege.UI.Tests
{
    internal sealed class SnapshotRoomScanner:IRoomScanner
    {
        private readonly RoomSnapshot room;
        public bool Fail,Passthrough=true,CaptureRequested;public int Reads;
        public SnapshotRoomScanner(RoomSnapshot room){this.room=room;}
        public bool TryEnablePassthrough()=>Passthrough;
        public Task<RoomSnapshot> ScanAsync(Transform space,CancellationToken token,Action<string> progress,bool requestCapture=false)
        {
            token.ThrowIfCancellationRequested();Reads++;CaptureRequested|=requestCapture;progress("读取已保存房间");
            return Fail?Task.FromException<RoomSnapshot>(new InvalidOperationException("测试房间读取失败")):Task.FromResult(room);
        }
    }
    public sealed class ScanPlaytestTests
    {
        private sealed class Memory:IProgressionStorage
        {public string[] slots={"",""};public bool Fail;public string Read(int i)=>slots[i];public void Write(int i,string value){if(Fail)throw new System.IO.IOException("full");slots[i]=value;}}
        [Test]public void ExplicitDevelopmentGrantPersistsAllContentWithoutChangingWalletOrLoadout()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);store.CompleteChallenge("funding",5000);
            store.Buy("mg-epic",out _);store.Equip("MG","mg-epic",out _);store.BeginRun("old",1);store.WaveCleared("old",1);store.Settle("old",false);
            var before=store.Snapshot();var config=new PlaytestAccess{unlockAllContent=true,supplyStock=50};Assert.IsTrue(config.unlockAllContent);Assert.AreEqual(50,config.supplyStock);Assert.IsTrue(config.Apply(store));
            Assert.IsTrue(store.PlaytestUnlocked);Assert.IsTrue(catalog.weapons.All(store.OwnsWeapon));Assert.IsTrue(catalog.skins.All(s=>store.Owns(s.id)));Assert.IsTrue(catalog.supplies.All(s=>store.Count(s.id)==50));
            Assert.AreEqual(before.fragments,store.Fragments);Assert.AreEqual(before.run.reward,store.LastReward);Assert.AreEqual("mg-epic",store.Equipped("MG"));CollectionAssert.AreEqual(before.loadout,store.Loadout);CollectionAssert.AreEqual(before.supplies,store.Supplies);
            var reloaded=new ProgressionStore(catalog,memory);Assert.IsTrue(reloaded.PlaytestUnlocked);Assert.IsTrue(catalog.weapons.All(reloaded.OwnsWeapon));Assert.IsTrue(catalog.skins.All(s=>reloaded.Owns(s.id)));Assert.IsFalse(reloaded.Buy("Missile",out _));
            Assert.IsTrue(reloaded.SetLoadout(new[]{"Tesla","Mortar","Missile","Gravity"},out _));Assert.IsFalse(reloaded.SetLoadout(new[]{"Beacon","Prism","Cryo","Gravity"},out _));
        }
        [Test]public void TrialGrantIsIdempotentAndDoesNotRefillUsedSuppliesOrInflateRewards()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);store.UnlockAllForPlaytest();store.BeginRun("r",0);Assert.IsTrue(store.Consume("r","freeze"));
            int revision=store.Revision;Assert.IsTrue(store.UnlockAllForPlaytest());Assert.AreEqual(19,store.Count("freeze"));Assert.AreEqual(revision,store.Revision);Assert.AreEqual(0,store.Fragments);Assert.AreEqual(0,store.LastReward);
            var reload=new ProgressionStore(catalog,memory);Assert.IsTrue(reload.UnlockAllForPlaytest());Assert.AreEqual(19,reload.Count("freeze"));
        }
        [Test]public void TrialGrantPreservesHighStockAndRollsBackFullyWhenSaveFails()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);store.CompleteChallenge("test",100000);for(int i=0;i<25;i++)store.Buy("freeze",out _);
            memory.Fail=true;Assert.IsFalse(store.UnlockAllForPlaytest());Assert.IsFalse(store.PlaytestUnlocked);Assert.IsFalse(store.OwnsWeapon("Missile"));Assert.IsFalse(store.Owns("missile-legend"));
            memory.Fail=false;Assert.IsTrue(store.UnlockAllForPlaytest());Assert.AreEqual(25,store.Count("freeze"));Assert.AreEqual(20,store.Count("repair"));
            Assert.IsFalse(store.UnlockAllForPlaytest(100));Assert.IsFalse(store.UnlockAllForPlaytest(0));
        }
        [Test]public void DisabledTrialConfigDoesNotTouchNormalProgression()
        {
            var store=new ProgressionStore(ProgressionCatalog.Load(),new Memory());int revision=store.Revision;Assert.IsTrue(new PlaytestAccess().Apply(store));Assert.AreEqual(revision,store.Revision);Assert.IsFalse(store.PlaytestUnlocked);Assert.IsFalse(store.OwnsWeapon("Tesla"));
        }

        [Test]public void NewStockGiftTopsUpExistingTrialOnceWithoutChangingRewardsOrRefillingAfterUse()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);
            store.UnlockAllForPlaytest();store.BeginRun("r",0);store.Consume("r","freeze");
            int wallet=store.Fragments,reward=store.LastReward;var config=new PlaytestAccess{unlockAllContent=true,supplyStock=50};
            Assert.IsTrue(config.Apply(store));Assert.IsTrue(catalog.supplies.All(x=>store.Count(x.id)==50));
            Assert.AreEqual(wallet,store.Fragments);Assert.AreEqual(reward,store.LastReward);
            Assert.IsTrue(store.Consume("r","freeze"));int revision=store.Revision;
            var loaded=new ProgressionStore(catalog,memory);Assert.IsTrue(config.Apply(loaded));
            Assert.AreEqual(49,loaded.Count("freeze"));Assert.AreEqual(revision,loaded.Revision);
        }
        [Test]public void NewStockGiftRollsBackOnStorageFailureAndCanRetry()
        {
            var memory=new Memory();var store=new ProgressionStore(ProgressionCatalog.Load(),memory);store.UnlockAllForPlaytest();
            memory.Fail=true;Assert.IsFalse(new PlaytestAccess{unlockAllContent=true,supplyStock=50}.Apply(store));Assert.AreEqual(20,store.Count("freeze"));
            memory.Fail=false;Assert.IsTrue(new PlaytestAccess{unlockAllContent=true,supplyStock=50}.Apply(store));Assert.AreEqual(50,store.Count("freeze"));
        }
        private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static string runtimeFailure;
        private static Button Button(Transform root,string name)=>root.Find(name).GetComponent<Button>();
        [UnityTest]
        public IEnumerator StartAlwaysShowsEightStepsReadsSavedRoomAndCancelsWithoutLatePublication()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            yield return CheckScanAndTrial();
            UnityEditor.SessionState.SetString("RoomSiege.Scan8.TestFailure",runtimeFailure??"");
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            string failure=UnityEditor.SessionState.GetString("RoomSiege.Scan8.TestFailure","");Assert.IsEmpty(failure,failure);
        }
        private static IEnumerator CheckScanAndTrial()
        {
            runtimeFailure=null;
            string failure=null;float until=Time.realtimeSinceStartup+30;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            SnapshotRoomScanner native=null;var observed=new List<int>();ScanController scan=null;WorldPanelHost panels=null;Transform setup=null;Action changed=null;
            try
            {
                WaveController.Instance.ReturnToMenu();panels=WorldPanelHost.Instance;panels.Tutorial.Cancel();scan=ScanController.Instance;scan.UseSandbox();
                var center=scan.CurrentGrid.SafeZoneCenter;var room=new RoomSnapshot{FloorHeight=center.y};var floor=SandboxRoom.Rectangle("scan-eight",SurfaceKind.Floor,center,new Vector2(6,6));floor.HasAnchorPose=true;floor.AnchorPose=new Pose(center,Quaternion.identity);room.Surfaces.Add(floor);
                native=new SnapshotRoomScanner(room);typeof(ScanController).GetField("scanner",Hidden).SetValue(scan,native);
                typeof(ScanController).GetProperty("CurrentRoom").SetValue(scan,room);typeof(ScanController).GetMethod("Publish",Hidden).Invoke(scan,new object[]{GridBuilder.Build(room,center,ScanMode.RealRoom),"cached real room"});
                setup=panels.Panels[0].transform.Find("Room setup page");
                changed=()=>{if(scan.IsScanning&&scan.ScanStep>0&&!observed.Contains(scan.ScanStep))observed.Add(scan.ScanStep);};scan.ViewChanged+=changed;
                var p=ProgressionController.Instance;Assert.IsTrue(new PlaytestAccess{unlockAllContent=true,supplyStock=50}.Apply(p.Store));p.ConfigurationChanged();p.OpenShop();p.GetComponent<ProgressionPanelUi>().ShowWeapons();Canvas.ForceUpdateCanvases();
                foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())if(camera.name=="Armory preview camera")camera.Render();
                CampaignRuntimeTests.CaptureUi("scan8-playtest-unlocked");panels.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Button(panels.Panels[0].transform.Find("Main menu page"),"开始游戏").onClick.Invoke();panels.Tutorial.Cancel();Assert.IsTrue(scan.IsScanning);Assert.AreEqual(1,scan.ScanStep);Assert.IsNull(scan.CurrentGrid);
                typeof(WorldPanelHost).GetMethod("RefreshSetup",Hidden).Invoke(panels,null);Assert.IsFalse(Button(setup,"进入建造").interactable);
                Assert.IsTrue(setup.Find("Scan status").GetComponent<Text>().text.Contains("1/8"));CampaignRuntimeTests.CaptureUi("scan8-01-start");
            }
            catch(Exception error){failure=error.ToString();}
            bool capturedRoute=false;until=Time.realtimeSinceStartup+12;
            while(failure==null&&scan.IsScanning&&Time.realtimeSinceStartup<until)
            {
                if(scan.ScanStep==7&&!capturedRoute){capturedRoute=true;Canvas.ForceUpdateCanvases();CampaignRuntimeTests.CaptureUi("scan8-07-route");}
                yield return null;
            }
            if(failure==null)try
            {
                CollectionAssert.AreEqual(Enumerable.Range(1,8),observed);Assert.AreEqual(1,native.Reads);Assert.IsFalse(native.CaptureRequested);Assert.IsFalse(scan.IsScanning);Assert.AreEqual(8,scan.ScanStep);Assert.AreEqual(ScanMode.RealRoom,scan.CurrentGrid.Mode);Assert.IsTrue(scan.CurrentGrid.SpawnPoints.Count>0);Assert.IsFalse(scan.EntranceCalibrationPending);
                typeof(WorldPanelHost).GetMethod("RefreshSetup",Hidden).Invoke(panels,null);Assert.IsTrue(Button(setup,"进入建造").interactable);CampaignRuntimeTests.CaptureUi("scan8-08-complete");
                panels.RequestMenu();Button(panels.Panels[0].transform.Find("Main menu page"),"开始游戏").onClick.Invoke();Assert.AreEqual(1,scan.ScanStep);Assert.IsTrue(scan.IsScanning);scan.UseSandbox();Assert.IsFalse(scan.IsScanning);Assert.AreEqual(0,scan.ScanStep);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+3.2f;while(failure==null&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                Assert.AreEqual(ScanMode.Sandbox,scan.CurrentGrid.Mode);Assert.AreEqual(1,native.Reads,"Cancelled run must not query or overwrite sandbox");
                native.Fail=true;scan.BeginGameRoomScan();Assert.AreEqual(1,scan.ScanStep);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+5;while(failure==null&&scan.IsScanning&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try{Assert.IsFalse(scan.IsScanning);Assert.AreEqual(ScanMode.Sandbox,scan.CurrentGrid.Mode);Assert.IsTrue(scan.Status.Contains("测试房间读取失败"));Assert.IsFalse(native.CaptureRequested);}
            catch(Exception error){failure=error.ToString();}
            if(scan!=null&&changed!=null)scan.ViewChanged-=changed;if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            runtimeFailure=failure;
        }
    }
}
