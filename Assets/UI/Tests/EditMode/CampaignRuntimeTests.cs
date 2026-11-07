using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RoomSiege.Enemies;
using RoomSiege.Meta;
using RoomSiege.Towers;
using RoomSiege.Waves;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.SceneManagement;
using UnityEditor.SceneManagement;
using UnityEditor;
using UnityEngine.UI;
using UnityEngine.EventSystems;

namespace RoomSiege.UI.Tests
{
    public sealed class CampaignRuntimeTests
    {
        private static readonly string[] SavedPrefs=new[]{"GoldSkinUnlocked","GoldSkinEquipped","TowerUnlocked.MG","TowerUnlocked.Frost","TowerUnlocked.Laser","TowerUnlocked.Trap","TowerUnlocked.Tesla","TowerUnlocked.Mortar",
            "EnemyUnlocked.Grunt","EnemyUnlocked.Runner","EnemyUnlocked.Tank","EnemyUnlocked.Shielded","EnemyUnlocked.Boss"}.Concat(RoomSiege.Data.WeaponRoster.Ids.Skip(6).Select(id=>"TowerUnlocked."+id)).ToArray();
        private static readonly List<string> RuntimeErrors=new List<string>();
        private static void CaptureRuntimeError(string message,string stack,LogType type)
        {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&!message.Contains("ContextNotCreated"))RuntimeErrors.Add(message+"\n"+stack);}
        private static int protectedSpawnCount;
        private static string protectedSpawnFailure;
        private static void CheckProtectedSpawn(EnemyActor actor)
        {
            protectedSpawnCount++;
            if(!actor.GetComponent<EnemyVisual>().BodyReady)protectedSpawnFailure="Body missing at spawn";
            float hp=actor.HitPoints;
            actor.ReceiveHit(new TowerHit(100000,actor.Position,TowerDamageKind.Projectile,1));
            if(!actor.IsAlive||actor.HitPoints!=hp)protectedSpawnFailure="Killed before first rendered frame";
        }

        internal static TowerInstance BuildOpeningTower(string id,Vector3 offset)
        {
            var placement=TowerHost.Instance.Placement;
            Vector3 desired=placement.Grid.SafeZoneCenter+offset;
            var cells=new List<RoomSiege.Scan.GridCell>();
            foreach(var cell in placement.Grid.PlaceableCells)
                if(cell.Surface==RoomSiege.Scan.SurfaceKind.Floor&&(cell.Center-desired).sqrMagnitude<.37f)cells.Add(cell);
            cells.Sort((a,b)=>(a.Center-desired).sqrMagnitude.CompareTo((b.Center-desired).sqrMagnitude));
            foreach(var cell in cells)
                if(placement.TryBuild(id,cell,out var tower,out _))return tower;
            Assert.Fail($"Cannot build {id} near {offset} with {GameManager.Instance.Coins}G");return null;
        }

        // Render actual runtime UI at native resolution for offline visual review.
        internal static void CaptureUi(string name)
        {
            var host=WorldPanelHost.Instance;var go=new GameObject("UI review camera");
            var camera=go.AddComponent<Camera>();var rt=new RenderTexture(1920,1080,24);
            var pixels=new Texture2D(1920,1080,TextureFormat.RGB24,false);var previous=RenderTexture.active;
            try
            {
                camera.stereoTargetEye=StereoTargetEyeMask.None;camera.fieldOfView=50;camera.nearClipPlane=.01f;
                camera.clearFlags=CameraClearFlags.SolidColor;camera.backgroundColor=new Color(.035f,.055f,.075f);
                camera.transform.SetPositionAndRotation(host.transform.position-host.transform.forward*1.35f,host.transform.rotation);
                camera.targetTexture=rt;camera.Render();RenderTexture.active=rt;
                pixels.ReadPixels(new Rect(0,0,1920,1080),0,0);pixels.Apply();
                System.IO.Directory.CreateDirectory("TestResults/VisualPolish");
                System.IO.File.WriteAllBytes("TestResults/VisualPolish/"+name+".png",pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active=previous;camera.targetTexture=null;rt.Release();
                UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(rt);UnityEngine.Object.DestroyImmediate(pixels);
            }
        }

        [UnityTest]
        public IEnumerator VisualPolishCapturesRealPagesAndPreservesPlacementAndManagementFlow()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady||WaveController.Instance==null||!WaveController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try{WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();}
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.IsFalse(RoomSiege.Scan.ScanController.Instance.transform.Find("Scan room geometry").gameObject.activeSelf);
                var main=WorldPanelHost.Instance.Panels[0].transform;
                var art=main.Find("Main Battlefield background/RoomSiege duel key art").GetComponent<RawImage>();
                Assert.IsNotNull(art.texture);Assert.IsFalse(art.raycastTarget);Assert.IsTrue(art.gameObject.activeInHierarchy);
                Assert.IsNull(main.Find("Main menu page/Defender portrait"));
                Assert.AreEqual("开始游戏",main.Find("Main menu page/开始游戏").GetComponentInChildren<Text>().text);
                Assert.IsNull(main.Find("Main menu page/模拟沙盘开局"));
                CaptureUi("01-menu");
                GameManager.Instance.SetState(GameState.SCAN);RoomSiege.Scan.ScanController.Instance.UseSandbox();
                Assert.IsFalse(art.gameObject.activeInHierarchy,"Battle artwork must not cover the real room during setup or play");
                WorldPanelHost.Instance.Tutorial.Cancel();CaptureUi("02-room-setup");
                WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            TowerInstance tower=null;
            if(failure==null)try
            {
                Assert.IsTrue(RoomSiege.Scan.ScanController.Instance.transform.Find("Scan room geometry").gameObject.activeSelf);
                // The former left-side screenshot fixture now lies on the
                // reserved Boss route; use the open construction flank.
                CaptureUi("03-construction");tower=BuildOpeningTower("MG",new Vector3(1,0,1.4f));
                TowerHost.Instance.GetComponent<TowerPanelUi>().ShowUpgrade(tower);CaptureUi("04-tower-management");
                TowerHost.Instance.GetComponent<TowerPanelUi>().HideUpgrade();Assert.IsTrue(TowerHost.Instance.Choose("Frost"));
                CaptureUi("05-placement");Assert.IsTrue(WorldPanelHost.Instance.PlacementMode);
                TowerHost.Instance.Cancel();Assert.IsFalse(WorldPanelHost.Instance.PlacementMode);
                WaveController.Instance.StartWaveFromUi();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try{CaptureUi("06-battle");GameManager.Instance.ApplyLeakDamage(100);}
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.IsFalse(RoomSiege.Scan.ScanController.Instance.transform.Find("Scan room geometry").gameObject.activeSelf);
                CaptureUi("07-settlement");MetaController.Instance.GetComponent<MetaPanelUi>().ShowCodex(false);
                var portrait=MetaController.Instance.GetComponent<MetaPanelUi>().GetComponentInChildren<RawImage>();
                // Meta content is parented under the shared panels, not the controller.
                portrait=WorldPanelHost.Instance.GetComponentInChildren<RawImage>();
                Assert.IsNotNull(portrait);Assert.AreEqual("RoomSiege/WorldPanel",portrait.material.shader.name,"Codex imagery renders with the foreground UI");
                CaptureUi("08-codex");
                var audio=RoomSiege.Audio.AudioDirector.Instance.GetComponent<RoomSiege.Audio.AudioSettingsPanel>();
                typeof(RoomSiege.Audio.AudioSettingsPanel).GetMethod("Open",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(audio,null);
                CaptureUi("09-settings");
            }
            catch(Exception error){failure=error.ToString();}
            if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator RealFloorWithoutDetectedDoorCanCalibrateTrackedRayAndEnterBuild()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((WaveController.Instance==null||!WaveController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var scan=RoomSiege.Scan.ScanController.Instance;var panels=WorldPanelHost.Instance;
                WaveController.Instance.ReturnToMenu();scan.UseSandbox();GameManager.Instance.SetState(GameState.SCAN);panels.Tutorial.Cancel();
                var center=scan.CurrentGrid.SafeZoneCenter;
                var room=new RoomSiege.Scan.RoomSnapshot {FloorHeight=center.y};
                room.Surfaces.Add(RoomSiege.Scan.SandboxRoom.Rectangle("test-real-floor",RoomSiege.Scan.SurfaceKind.Floor,center,new Vector2(6,6)));
                room.Surfaces[0].HasAnchorPose=true;room.Surfaces[0].AnchorPose=new Pose(center,Quaternion.identity);
                typeof(RoomSiege.Scan.ScanController).GetProperty("CurrentRoom").SetValue(scan,room);
                typeof(RoomSiege.Scan.ScanController).GetProperty("EntranceCalibrationPending").SetValue(scan,true);
                var grid=RoomSiege.Scan.GridBuilder.Build(room,center,RoomSiege.Scan.ScanMode.RealRoom);
                typeof(RoomSiege.Scan.ScanController).GetMethod("Publish",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scan,new object[]{grid,"真实地板已就绪，请先标定出怪口"});
                var refresh=typeof(WorldPanelHost).GetMethod("RefreshSetup",BindingFlags.Instance|BindingFlags.NonPublic);
                refresh.Invoke(panels,null);
                var setup=panels.Panels[0].transform.Find("Room setup page");
                var enter=setup.Find("进入建造").GetComponent<Button>();
                Assert.IsFalse(enter.interactable,"Do not silently start on a real floor without an entrance");
                Assert.IsFalse(scan.IsScanning);Assert.IsFalse(scan.CaptureRequested);
                Assert.AreEqual("调整出怪口（可选）",setup.Find("标定出怪口").GetComponentInChildren<Text>().text);
                scan.BeginCalibration();Assert.IsTrue(scan.CalibrationArmed);
                var calibrationUpdate=typeof(WorldPanelHost).GetMethod("UpdateCalibrationInput",BindingFlags.Instance|BindingFlags.NonPublic);
                calibrationUpdate.Invoke(panels,null);
                Assert.IsTrue(panels.CalibrationMode);
                foreach(var panel in panels.Panels)
                {
                    Assert.AreEqual(0,panel.GetComponent<CanvasGroup>().alpha,"Room controls must leave the floor visible during calibration");
                    Assert.IsFalse(panel.GetComponent<CanvasGroup>().blocksRaycasts,"Hidden room controls cannot swallow floor confirmation");
                }
                Assert.IsTrue(panels.transform.Find("Calibration HUD").gameObject.activeSelf);
                Assert.IsTrue(panels.transform.Find("Calibration HUD/取消标定").GetComponent<Button>().interactable);
                scan.CancelCalibration();calibrationUpdate.Invoke(panels,null);
                Assert.IsFalse(panels.CalibrationMode);Assert.IsFalse(panels.transform.Find("Calibration HUD").gameObject.activeSelf);
                foreach(var panel in panels.Panels)Assert.IsTrue(panel.GetComponent<CanvasGroup>().blocksRaycasts);
                scan.BeginCalibration();calibrationUpdate.Invoke(panels,null);
                scan.CalibrateEntrance(new Ray(center+Vector3.up,Vector3.down));
                Assert.IsTrue(scan.CalibrationArmed,"Reject positions inside the safe zone without ending calibration");
                Assert.AreEqual(0,scan.CurrentGrid.SpawnPoints.Count);
                var target=center+Vector3.forward*2.4f;
                string entranceBefore=PlayerPrefs.GetString(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey,"");
                room.Volumes.Add(new RoomSiege.Scan.RoomVolume {Id="route barrier",Center=center+Vector3.forward*1.2f,
                    Size=new Vector3(6,1,.3f),Rotation=Quaternion.identity});
                scan.CalibrateEntrance(new Ray(target+Vector3.up,Vector3.down));
                Assert.IsTrue(scan.CalibrationArmed);Assert.IsTrue(scan.EntranceCalibrationPending);
                StringAssert.Contains("通路",scan.Status);
                Assert.IsEmpty(room.Entrances,"Failed confirmation must not publish an invalid entrance");
                Assert.AreEqual(entranceBefore,PlayerPrefs.GetString(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey,""),"A blocked entrance must not replace the saved entrance");
                refresh.Invoke(panels,null);Assert.IsFalse(enter.interactable);
                room.Volumes.Clear();
                scan.CalibrateEntrance(new Ray(target+Vector3.up,Vector3.down));
                Assert.IsFalse(scan.CalibrationArmed);Assert.IsFalse(scan.EntranceCalibrationPending);
                calibrationUpdate.Invoke(panels,null);Assert.IsFalse(panels.CalibrationMode);
                foreach(var panel in panels.Panels)Assert.IsTrue(panel.GetComponent<CanvasGroup>().blocksRaycasts);
                var savedEntrance=PlayerPrefs.GetString(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey,"");
                Assert.IsNotEmpty(savedEntrance);
                Assert.IsTrue(RoomSiege.Scan.RoomEntranceMemory.TryRestore(room,savedEntrance,center,out var restoredEntrance));
                Assert.Less(Vector3.Distance(restoredEntrance.Position,target),.001f);
                Assert.AreEqual(RoomSiege.Scan.ScanMode.RealRoom,scan.CurrentGrid.Mode);
                Assert.AreEqual(1,scan.CurrentGrid.SpawnPoints.Count);Assert.IsTrue(scan.CurrentGrid.SpawnPoints[0].IsManual);
                Assert.Less(Vector3.Distance(target,scan.CurrentGrid.SpawnPoints[0].Position),.001f,"Use the supplied controller/hand ray instead of head gaze");
                refresh.Invoke(panels,null);Assert.IsTrue(enter.interactable);
                CaptureUi("02-real-room-calibrated");
                enter.onClick.Invoke();Assert.AreEqual(GameState.BUILD,GameManager.Instance.State);
                Assert.IsTrue(scan.BattleLayoutLocked);
            }
            catch(Exception error){failure=error.ToString();}
            if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator RealRoomScanAutomaticallyPreparesEntranceAndStartsCampaign()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((WaveController.Instance==null||!WaveController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var scan=RoomSiege.Scan.ScanController.Instance;var panels=WorldPanelHost.Instance;
                WaveController.Instance.ReturnToMenu();scan.UseSandbox();
                var menu=panels.Panels[0].transform.Find("Main menu page");
                Assert.AreEqual("开始游戏",menu.Find("开始游戏").GetComponentInChildren<Text>().text);
                Assert.IsNull(menu.Find("模拟沙盘开局"));
                var center=scan.CurrentGrid.SafeZoneCenter;
                var room=new RoomSiege.Scan.RoomSnapshot {FloorHeight=center.y};
                var floor=RoomSiege.Scan.SandboxRoom.Rectangle("automatic-floor",RoomSiege.Scan.SurfaceKind.Floor,center,new Vector2(6,6));
                floor.HasAnchorPose=true;floor.AnchorPose=new Pose(center,Quaternion.identity);room.Surfaces.Add(floor);
                typeof(RoomSiege.Scan.ScanController).GetProperty("CurrentRoom").SetValue(scan,room);
                var grid=RoomSiege.Scan.GridBuilder.Build(room,center,RoomSiege.Scan.ScanMode.RealRoom);
                typeof(RoomSiege.Scan.ScanController).GetMethod("Publish",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(scan,new object[]{grid,"Room scan completed"});
                typeof(RoomSiege.Scan.ScanController).GetField("scanner",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(scan,new SnapshotRoomScanner(room));
                menu.Find("开始游戏").GetComponent<Button>().onClick.Invoke();panels.Tutorial.Cancel();
                Assert.IsTrue(scan.IsScanning);Assert.AreEqual(1,scan.ScanStep);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+12;
            while(failure==null&&RoomSiege.Scan.ScanController.Instance.IsScanning&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var scan=RoomSiege.Scan.ScanController.Instance;var panels=WorldPanelHost.Instance;
                var room=scan.CurrentRoom;var center=scan.CurrentGrid.SafeZoneCenter;
                Assert.AreEqual(GameState.SCAN,GameManager.Instance.State);
                Assert.IsFalse(scan.IsScanning);Assert.AreEqual(8,scan.ScanStep);
                Assert.AreEqual(RoomSiege.Scan.ScanMode.RealRoom,scan.CurrentGrid.Mode);
                Assert.AreEqual(1,scan.CurrentGrid.SpawnPoints.Count,"No manual calibration after a complete room scan");
                Assert.IsFalse(scan.CalibrationArmed);Assert.IsFalse(scan.EntranceCalibrationPending);
                var entrance=scan.CurrentGrid.SpawnPoints[0];
                var saved=PlayerPrefs.GetString(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey,"");
                Assert.IsTrue(RoomSiege.Scan.RoomEntranceMemory.TryRestore(room,saved,center,out var restored));
                Assert.Less(Vector3.Distance(entrance.Position,restored.Position),.001f);
                scan.UseRealRoom();Assert.AreEqual(entrance.Position,scan.CurrentGrid.SpawnPoints[0].Position,"An already valid room must remain stable");
                typeof(WorldPanelHost).GetMethod("RefreshSetup",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(panels,null);
                var enter=panels.Panels[0].transform.Find("Room setup page/进入建造").GetComponent<Button>();
                Assert.IsTrue(enter.interactable);enter.onClick.Invoke();
                Assert.AreEqual(GameState.BUILD,GameManager.Instance.State);Assert.IsTrue(scan.BattleLayoutLocked);
            }
            catch(Exception error){failure=error.ToString();}
            if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator CasualFirstThreeWavesClearWithAffordableGunsAndMixedOpening()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady||WaveController.Instance==null||!WaveController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            System.IO.Directory.CreateDirectory("TestResults");
            System.IO.File.WriteAllText("TestResults/RoomSiege-CasualOpening-Combat.txt","Real tower combat in Sandbox; Casual 180G; timeScale=3; no injected damage/currency, freeze or perks.\n");
            foreach(bool mixed in new[]{false,true})
            {
                if(failure==null)try
                {
                    var game=GameManager.Instance;var waves=WaveController.Instance;
                    waves.ReturnToMenu();game.SetState(GameState.SCAN);game.SelectDifficulty(0);
                    RoomSiege.Scan.ScanController.Instance.UseSandbox();waves.StartNewCampaign();
                    WorldPanelHost.Instance.Tutorial.Cancel();
                    Assert.AreEqual(180,game.Coins);
                    var first=BuildOpeningTower("MG",new Vector3(1,0,.6f));
                    BuildOpeningTower("MG",new Vector3(1,0,1.4f));
                    BuildOpeningTower(mixed?"Frost":"MG",new Vector3(1,0,2.2f));
                    if(mixed)Assert.IsTrue(TowerHost.Instance.Placement.TryUpgrade(first,2,out _));
                    Assert.AreEqual(mixed?0:30,game.Coins);
                    Time.timeScale=3;
                }
                catch(Exception error){failure=error.ToString();}
                yield return null;yield return null;
                for(int number=1;number<=3&&failure==null;number++)
                {
                    try
                    {
                        if(!mixed&&number>=2)BuildOpeningTower("MG",new Vector3(1.8f,0,number==2?1.4f:2.2f));
                        if(mixed&&number==3)BuildOpeningTower("MG",new Vector3(1.8f,0,1.4f));
                        WaveController.Instance.StartWaveFromUi();
                        Assert.IsTrue(WaveController.Instance.IsWaveActive);
                        Assert.AreEqual(number,GameManager.Instance.CurrentWave);
                    }
                    catch(Exception error){failure=error.ToString();}
                    until=Time.realtimeSinceStartup+20;
                    while(failure==null&&WaveController.Instance.IsWaveActive&&Time.realtimeSinceStartup<until)yield return null;
                    if(failure==null)try
                    {
                        var waves=WaveController.Instance;
                        Assert.IsFalse(waves.IsWaveActive,"Opening must finish through ordinary combat");
                        var result=waves.Results[number-1];
                        string detail=$"{(mixed?"2MG+Frost, one MG Lv2":"3MG, add MG after W1/W2")} W{number}: kills={result.Kills}, leaks={result.Leaks}, gold={result.RemainingGold}, duration={result.DurationSeconds:F2}s";
                        System.IO.File.AppendAllText("TestResults/RoomSiege-CasualOpening-Combat.txt",detail+"\n");
                        Assert.AreEqual(0,result.Leaks,detail);
                        Assert.AreEqual(number==1?6:number==2?8:9,result.Kills,detail);
                        Assert.AreEqual(GameManager.Instance.InitialLives,GameManager.Instance.Lives);
                        Assert.AreEqual(0,TowerHost.Instance.Placement.Perks.ChoicesMade);
                    }
                    catch(Exception error){failure=error.ToString();}
                }
            }
            Time.timeScale=1;
            if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator WavesFourAndSevenRenderPooledEnemiesBeforeAcceptingTowerDamage()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                GameManager.Instance.SetState(GameState.SCAN);WaveController.Instance.StartNewCampaign();
                WorldPanelHost.Instance.Tutorial.Cancel();TowerHost.Instance.enabled=false;
                Time.timeScale=3;
            }
            catch(Exception error){failure=error.ToString();}
            foreach(int number in new[]{4,7})
            {
                protectedSpawnCount=0;protectedSpawnFailure=null;
                if(failure==null)try
                {
                    var waves=WaveController.Instance;
                    typeof(WaveController).GetField("waveIndex",BindingFlags.Instance|BindingFlags.NonPublic).SetValue(waves,number-1);
                    typeof(WaveController).GetProperty("AwaitingFirstWave").SetValue(waves,false);
                    typeof(WaveController).GetMethod("BeginPreparation",BindingFlags.Instance|BindingFlags.NonPublic).Invoke(waves,null);
                    waves.SelectReinforcement(0);
                    EnemyController.Instance.Spawned+=CheckProtectedSpawn;
                }
                catch(Exception error){failure=error.ToString();}
                // Closing a page deliberately waits for release before gameplay resumes.
                yield return null;yield return null;
                if(failure==null)try
                {
                    WaveController.Instance.StartWaveFromUi();
                    Assert.IsTrue(WaveController.Instance.IsWaveActive,"Checkpoint page must release input before the wave starts");
                }
                catch(Exception error){failure=error.ToString();}
                until=Time.realtimeSinceStartup+20;
                while(failure==null&&WaveController.Instance.IsWaveActive&&Time.realtimeSinceStartup<until)
                {
                    yield return null;
                    try
                    {
                        Assert.IsNull(protectedSpawnFailure,protectedSpawnFailure);
                        foreach(var actor in EnemyController.Instance.ActiveEnemies)
                        {
                            if(!actor.IsAlive)continue;
                            Assert.IsTrue(actor.GetComponent<EnemyVisual>().BodyReady,"Pooled body must remain visible");
                            if(actor.CanBeTargeted)
                            {
                                Assert.GreaterOrEqual(actor.AliveSeconds,EnemyActor.EntranceProtectionSeconds);
                                Assert.AreEqual((number-1)*10f,actor.Armor,"Pooled enemies resolve armor for their current wave");
                                float armorFactor=100f/(100f+actor.Armor);
                                foreach(var kind in new[]{TowerDamageKind.Projectile,TowerDamageKind.Laser,TowerDamageKind.Frost,TowerDamageKind.Splash,TowerDamageKind.Trap})
                                {
                                    float before=actor.HitPoints;
                                    actor.ReceiveHit(new TowerHit(1,actor.Position,kind,1));
                                    Assert.AreEqual(before-armorFactor,actor.HitPoints,.001f,"Every weapon must respect armor exactly once");
                                }
                                float volley=TowerHost.Instance.Catalog.GetTower("MG").levels[2].damagePerHit*3;
                                actor.ReceiveHit(new TowerHit(volley,actor.Position,TowerDamageKind.Projectile,1));
                                Assert.IsTrue(actor.IsAlive,"A full-health W4/W7 enemy must survive a three-MG Lv3 volley plus five small test hits");
                                actor.ReceiveHit(new TowerHit(100000,actor.Position,TowerDamageKind.Projectile,1));
                            }
                            else
                            {
                                float hp=actor.HitPoints;
                                actor.ReceiveHit(new TowerHit(100000,actor.Position,TowerDamageKind.Laser,1));
                                Assert.AreEqual(hp,actor.HitPoints,"All weapon damage waits during entrance");
                            }
                        }
                    }
                    catch(Exception error){failure=error.ToString();}
                }
                if(EnemyController.Instance!=null)EnemyController.Instance.Spawned-=CheckProtectedSpawn;
                if(failure==null)try
                {
                    Assert.IsFalse(WaveController.Instance.IsWaveActive,"Wave must drain normally");
                    Assert.IsNull(protectedSpawnFailure,protectedSpawnFailure);
                    Assert.AreEqual(number==4?9:14,protectedSpawnCount);
                    var results=WaveController.Instance.Results;
                    Assert.AreEqual(0,results[results.Count-1].Leaks);
                    Assert.AreEqual(protectedSpawnCount,results[results.Count-1].Kills);
                }
                catch(Exception error){failure=error.ToString();}
            }
            if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;
            Time.timeScale=1;
            if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator TacticalFreezeColdChainAndCheckpointChoicesResetWithCampaign()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var game=GameManager.Instance;var waves=WaveController.Instance;var host=TowerHost.Instance;
                game.SetState(GameState.SCAN);waves.StartNewCampaign();
                Assert.IsTrue(RoomSiege.Scan.ScanController.Instance.BattleLayoutLocked);
                var grid=host.Placement.Grid;RoomSiege.Scan.ScanController.Instance.UseSandbox();
                Assert.AreSame(grid,host.Placement.Grid,"Cannot replace the room during a campaign");
                Assert.IsTrue(host.Placement.TryBuild("MG",grid.PlaceableCells[0],out _,out _));
                waves.StartWaveFromUi();waves.UseEmergencyFreeze();
                Assert.AreEqual(0,waves.FreezeCooldown,"No targets must not consume the command");
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+5;
            while(failure==null&&EnemyController.Instance.ActiveCount<2&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var enemies=EnemyController.Instance;var host=TowerHost.Instance;var waves=WaveController.Instance;
                var living=new List<EnemyActor>();foreach(var enemy in enemies.ActiveEnemies)if(enemy.IsAlive)living.Add(enemy);
                Assert.GreaterOrEqual(living.Count,2);var core=host.Placement.Grid.SafeZoneCenter;
                living[0].transform.position=core+Vector3.forward*1.65f;
                living[1].transform.position=core+Vector3.forward*1.65f+Vector3.right*.25f;
                host.Placement.Perks.Select(1);living[0].Slow(99,.3f,2);
                Assert.IsTrue(living[0].GetComponent<EnemyVisual>().Grunt.gameObject.activeInHierarchy,"Grunt uses articulated model in actual waves");
                var gun=host.GetComponentInChildren<MachineGunModel>();Assert.IsNotNull(gun,"Paid MG uses the new model");
                int activeBefore=enemies.ActiveCount;
                int before=GameManager.Instance.Coins;living[0].Damage(10000);
                Assert.AreEqual(before+6,GameManager.Instance.Coins);
                Assert.AreEqual(activeBefore-1,enemies.ActiveCount,"Death visuals cannot delay logical kill settlement");
                Assert.Greater(enemies.DeathVisuals.VisibleCount,0,"Real kill captures the current pose before actor recycling");
                Assert.AreEqual(.3f,living[1].SlowFraction,.0001f,"Death of a slowed enemy propagates cold chain");
                waves.UseEmergencyFreeze();Assert.AreEqual(30,waves.FreezeCooldown,.0001f);
                Assert.AreEqual(1,living[1].SlowFraction,.0001f);
                waves.UseEmergencyFreeze();Assert.AreEqual(30,waves.FreezeCooldown,.0001f,"Double press cannot retrigger");
                WorldPanelHost.Instance.ShowHelp();Assert.IsTrue(waves.IsPaused);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var waves=WaveController.Instance;var panels=WorldPanelHost.Instance;
                Assert.AreEqual(30,waves.FreezeCooldown,.0001f,"Pause freezes cooldown");
                panels.RequestRestart();Assert.AreEqual(0,waves.FreezeCooldown);Assert.IsFalse(waves.ReinforcementPending);
                Assert.AreEqual(0,TowerHost.Instance.Placement.Perks.ChoicesMade);
                Assert.AreEqual(0,EnemyController.Instance.DeathVisuals.VisibleCount,"Restart clears detached death models");
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            for(int checkpoint=3;checkpoint<=9;checkpoint+=3)
            {
                if(failure==null)try
                {
                    var waves=WaveController.Instance;
                    typeof(WaveController).GetField("waveIndex",BindingFlags.NonPublic|BindingFlags.Instance).SetValue(waves,checkpoint);
                    typeof(WaveController).GetProperty("AwaitingFirstWave").SetValue(waves,false);
                    typeof(WaveController).GetMethod("BeginPreparation",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(waves,null);
                    Assert.IsTrue(waves.ReinforcementPending);Assert.IsTrue(waves.IsPaused);
                    Assert.IsTrue(WorldPanelHost.Instance.Panels[0].transform.Find("Reinforcement page").gameObject.activeInHierarchy);
                    waves.StartWaveFromUi();Assert.IsFalse(waves.IsWaveActive);
                }
                catch(Exception error){failure=error.ToString();}
                yield return null;yield return null;
                if(failure==null)try
                {
                    var waves=WaveController.Instance;
                    Assert.AreEqual(15,waves.PreparationRemaining,.001f,"Choice must pause preparation");
                    Assert.AreEqual(3,TowerHost.Instance.Placement.Perks.Offers.Count);
                    waves.SelectReinforcement(0);waves.SelectReinforcement(0);
                    Assert.AreEqual(checkpoint/3,TowerHost.Instance.Placement.Perks.ChoicesMade,"Exactly one reward at each checkpoint");
                    Assert.IsFalse(waves.ReinforcementPending);
                }
                catch(Exception error){failure=error.ToString();}
                yield return null;yield return null;
            }
            if(failure==null)try
            {
                WorldPanelHost.Instance.RequestMenu();
                Assert.IsFalse(RoomSiege.Scan.ScanController.Instance.BattleLayoutLocked);
                Assert.AreEqual(0,TowerHost.Instance.Placement.Perks.ChoicesMade);
            }
            catch(Exception error){failure=error.ToString();}
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator PlacementClearsBattlefieldRestoresShopAndEveryShopInputHonorsFunds()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");
            SavePrefs();bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();
            LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();
            Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            string failure=null;
            try
            {
                Assert.IsTrue(MetaController.Instance!=null&&MetaController.Instance.IsReady);
                var game=GameManager.Instance;var waves=WaveController.Instance;var host=TowerHost.Instance;var panels=WorldPanelHost.Instance;
                game.SetState(GameState.SCAN);game.SelectDifficulty(1);waves.StartNewCampaign();
                var scan=RoomSiege.Scan.ScanController.Instance;var originalGrid=scan.CurrentGrid;
                Vector3 originalHead=Camera.main.transform.position;
                Camera.main.transform.position+=Vector3.right*2;
                typeof(RoomSiege.Scan.ScanController).GetMethod("Update",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(scan,null);
                Assert.AreSame(originalGrid,scan.CurrentGrid,"Walking must not move the core or rebuild paid deployment cells");
                Camera.main.transform.position=originalHead;
                waves.StartWaveFromUi();Assert.IsFalse(waves.IsWaveActive,"An unprepared first wave should not start silently");
                Assert.IsTrue(host.Choose("MG"));Assert.AreEqual(150,game.Coins,"Selection must be free");
                AssertPlacementPresentation(panels,true);
                Assert.IsFalse(host.Tap(new Ray(new Vector3(100,1,100),Vector3.down)),"Invalid floor must not finish placement");
                AssertPlacementPresentation(panels,true);Assert.AreEqual(150,game.Coins);
                panels.transform.Find("Placement HUD/取消放置").GetComponent<Button>().onClick.Invoke();
                AssertPlacementPresentation(panels,false);Assert.IsNull(host.PendingTowerId);Assert.AreEqual(150,game.Coins);
                var mgProxy=panels.Panels[1].GetComponentInChildren<TowerGrabProxy>();
                var grab=mgProxy.GetComponent<UnityEngine.XR.Interaction.Toolkit.Interactables.XRGrabInteractable>();
                grab.selectEntered.Invoke(new UnityEngine.XR.Interaction.Toolkit.SelectEnterEventArgs());
                Assert.IsTrue(mgProxy.Held);AssertPlacementPresentation(panels,true);
                foreach(var line in mgProxy.GetComponentsInChildren<LineRenderer>())Assert.IsFalse(line.enabled,"Grip icon must not remain in the way");
                grab.selectExited.Invoke(new UnityEngine.XR.Interaction.Toolkit.SelectExitEventArgs());
                Assert.IsFalse(mgProxy.Held);Assert.IsFalse(mgProxy.GetComponent<Collider>().enabled,"Hidden shop cannot intercept placement rays");
                host.Cancel();AssertPlacementPresentation(panels,false);
                for(int i=0;i<3;i++)
                {
                    Assert.IsTrue(host.Choose("MG"));AssertPlacementPresentation(panels,true);
                    var cell=host.Placement.Grid.PlaceableCells[i];
                    Assert.IsTrue(host.Tap(new Ray(cell.Center+Vector3.up*.4f,Vector3.down)),"Placement "+i);
                    Assert.AreEqual(150-(i+1)*50,game.Coins);
                    Assert.IsFalse(panels.Navigation.HasOpenPage,"Placement must not open a modal");
                    Assert.IsFalse(host.GetComponent<TowerPanelUi>().ManagementVisible,"No automatic upgrade popup");
                    Assert.IsTrue(panels.Panels[1].transform.Find("Tower slot 0").gameObject.activeInHierarchy);
                    Assert.IsNull(host.PendingTowerId,"One completed placement restores the shop");
                    AssertPlacementPresentation(panels,false);
                    if(i==0)
                    {
                        var paid=host.Placement.Towers[0];int balance=game.Coins;
                        Assert.IsTrue(host.Choose("Frost"));Physics.SyncTransforms();
                        // The edge of a 40cm occupied cell lies outside the
                        // tower's 28cm collider, but must still select upgrades.
                        Assert.IsTrue(host.Tap(new Ray(paid.Cell.Center+new Vector3(.18f,.5f,0),Vector3.down)));
                        Assert.AreSame(paid,host.ManagedTower);Assert.IsNull(host.PendingTowerId);
                        Assert.AreEqual(balance,game.Coins);Assert.AreEqual(1,host.Placement.Towers.Count);
                        host.Cancel();
                    }
                }
                Assert.IsNull(host.PendingTowerId,"No repeat placement when funds run out");
                foreach(string id in new[]{"MG","Frost","Laser","Trap"})Assert.IsFalse(host.Choose(id));
                foreach(var drag in panels.Panels[1].GetComponentsInChildren<TowerShopDrag>())
                {
                    Assert.IsFalse(drag.GetComponent<Button>().interactable);
                    var data=new PointerEventData(EventSystem.current);
                    drag.OnPointerDown(data);drag.OnPointerClick(data);
                    drag.OnBeginDrag(data);drag.OnEndDrag(data);
                }
                foreach(var proxy in panels.Panels[1].GetComponentsInChildren<TowerGrabProxy>())Assert.IsFalse(proxy.GetComponent<Collider>().enabled);
                Assert.AreEqual(0,game.Coins);Assert.AreEqual(3,host.Placement.Towers.Count);Assert.IsNull(host.PendingTowerId);
                StringAssert.Contains("金币不足",host.Status);
                AssertPlacementPresentation(panels,false);
                var selected=host.Placement.Towers[0];Physics.SyncTransforms();
                Assert.IsTrue(host.Tap(new Ray(selected.Cell.Center+Vector3.up*.4f,Vector3.down)));
                var management=panels.BuildControls.Find("Tower management");
                Assert.IsTrue(management.gameObject.activeInHierarchy);Assert.IsFalse(panels.Navigation.HasOpenPage);
                Assert.IsTrue(panels.Panels[1].transform.Find("Tower slot 0").gameObject.activeInHierarchy);
                Assert.IsFalse(management.Find("Upgrade Lv2").GetComponent<Button>().interactable);
                Assert.IsFalse(management.Find("Upgrade Lv3").GetComponent<Button>().interactable);
                game.AddCoins(40);Assert.IsTrue(management.Find("Upgrade Lv2").GetComponent<Button>().interactable);
                host.Upgrade(2);Assert.AreEqual(2,selected.Level);Assert.AreEqual(0,game.Coins);
                host.Upgrade(3);Assert.AreEqual(2,selected.Level);Assert.AreEqual(0,game.Coins);
                game.AddCoins(70);Assert.IsTrue(management.Find("Upgrade Lv3").GetComponent<Button>().interactable);
                host.Upgrade(3);Assert.AreEqual(3,selected.Level);Assert.AreEqual(0,game.Coins);
                host.Sell();Assert.AreEqual(112,game.Coins);Assert.IsFalse(management.gameObject.activeSelf);
                Assert.IsTrue(panels.Panels[1].transform.Find("Tower slot 2").GetComponent<Button>().interactable,"Income must unlock purchases immediately");
                panels.ShowHelp();Assert.IsTrue(waves.IsPaused);
                Assert.IsFalse(panels.BuildControls.gameObject.activeInHierarchy);
                var help=panels.Panels[0].transform.Find("Help page");
                for(int i=0;i<RoomSiegeGuide.Titles.Length-1;i++)help.Find("下一页").GetComponent<Button>().onClick.Invoke();
                StringAssert.Contains("重新开始",help.Find("Help title").GetComponent<Text>().text);
                help.Find("下一页").GetComponent<Button>().onClick.Invoke();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)
            {
                try
                {
                    Assert.IsFalse(WorldPanelHost.Instance.Navigation.HasOpenPage);
                    Assert.IsTrue(WorldPanelHost.Instance.BuildControls.gameObject.activeInHierarchy);
                    Assert.AreEqual(GameState.BUILD,GameManager.Instance.State);
                    Assert.AreEqual(112,GameManager.Instance.Coins);
                    var host=TowerHost.Instance;var panels=WorldPanelHost.Instance;
                    Assert.IsTrue(host.Choose("MG"));AssertPlacementPresentation(panels,true);
                    Assert.IsTrue(GameManager.Instance.TrySpendCoins(100));
                    Assert.IsNull(host.PendingTowerId);AssertPlacementPresentation(panels,false);
                    GameManager.Instance.AddCoins(100);
                    Assert.IsTrue(host.Choose("MG"));panels.RequestRestart();
                    Assert.IsNull(host.PendingTowerId);AssertPlacementPresentation(panels,false);
                }
                catch(Exception error){failure=error.ToString();}
            }
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }
        [UnityTest]
        public IEnumerator FailureRestartVictoryRestartAndReturnMenuResetWholeCampaign()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");
            // Existing PICO audio backend warning is tracked separately from flow correctness.
            bool previous=LogAssert.ignoreFailingMessages;
            // SessionState survives the Enter/ExitPlayMode domain reloads; iterator dictionaries do not.
            SavePrefs();
            LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();
            LogAssert.ignoreFailingMessages=true;
            string failure=null;
            float until=Time.realtimeSinceStartup+20;
            while((WaveController.Instance==null||!WaveController.Instance.IsReady||MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)
                yield return null;
            try
            {
                Assert.IsTrue(WaveController.Instance!=null&&WaveController.Instance.IsReady,"Waves did not initialize");
                Assert.IsTrue(MetaController.Instance!=null&&MetaController.Instance.IsReady,"Meta did not initialize");
                var game=GameManager.Instance;var waves=WaveController.Instance;var towers=TowerHost.Instance;
                var panels=WorldPanelHost.Instance;var enemies=EnemyController.Instance;
                game.SetState(GameState.SCAN);game.SelectDifficulty(1);waves.StartNewCampaign();
                Assert.AreEqual(GameState.BUILD,game.State);Assert.AreEqual(150,game.Coins);
                Assert.IsTrue(waves.AwaitingFirstWave);Assert.IsFalse(waves.IsWaveActive);
                Assert.IsTrue(towers.Placement.TryBuild("MG",towers.Placement.Grid.PlaceableCells[0],out _,out _));
                Assert.AreEqual(100,game.Coins);
                waves.StartWaveFromUi();Assert.AreEqual(GameState.WAVE,game.State);
                game.ApplyLeakDamage(100);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)
            {
                try
                {
                    Assert.AreEqual(CampaignOutcome.Failure,WaveController.Instance.Outcome);
                    Assert.IsTrue(MetaController.Instance.HasSettlement);
                    WorldPanelHost.Instance.RequestRestart();
                }
                catch(Exception error){failure=error.ToString();}
            }
            yield return null;yield return null;
            if(failure==null)
            {
                try
                {
                    var game=GameManager.Instance;var waves=WaveController.Instance;
                    AssertFreshBuild(150);
                    typeof(WaveController).GetMethod("FinishCampaign",BindingFlags.Instance|BindingFlags.NonPublic)
                        .Invoke(waves,new object[]{CampaignOutcome.Victory,"Flow test victory"});
                    Assert.IsTrue(game.IsVictorious);Assert.IsTrue(MetaController.Instance.HasSettlement);
                    WorldPanelHost.Instance.RequestRestart();
                }
                catch(Exception error){failure=error.ToString();}
            }
            yield return null;yield return null;
            if(failure==null)
            {
                try
                {
                    AssertFreshBuild(150);
                    WorldPanelHost.Instance.RequestMenu();
                }
                catch(Exception error){failure=error.ToString();}
            }
            yield return null;yield return null;
            if(failure==null)
            {
                try
                {
                    var game=GameManager.Instance;
                    Assert.AreEqual(GameState.MENU,game.State);Assert.IsFalse(MetaController.Instance.HasSettlement);
                    Assert.IsTrue(WorldPanelHost.Instance.Panels[0].transform.Find("Main menu page").gameObject.activeSelf);
                    game.SetState(GameState.SCAN);game.SelectDifficulty(0);WaveController.Instance.StartNewCampaign();
                    AssertFreshBuild(180);
                    game.SetState(GameState.WAVE);
                    var pause=WorldPanelHost.Instance.Panels[0].transform.Find("Pause menu page").gameObject;
                    WorldPanelHost.Instance.Navigation.Show(pause);
                    Assert.IsTrue(WaveController.Instance.IsPaused);Assert.IsFalse(EnemyController.Instance.CanSimulate);
                }
                catch(Exception error){failure=error.ToString();}
            }
            yield return new ExitPlayMode();
            RestorePrefs();
            LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator FollowModePreservesPaidTowerAndGeometryThenFixedModeStopsMovement()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            string failure=null;
            try
            {
                var scan=RoomSiege.Scan.ScanController.Instance;var panels=WorldPanelHost.Instance;
                var host=TowerHost.Instance;var game=GameManager.Instance;
                game.SetState(GameState.SCAN);WaveController.Instance.StartNewCampaign();
                var grid=scan.CurrentGrid;var room=scan.CurrentRoom;var originalCore=grid.SafeZoneCenter;
                Assert.IsFalse(scan.CoreFollowsPlayer);
                var cell=default(RoomSiege.Scan.GridCell);bool found=false;
                foreach(var candidate in grid.PlaceableCells)
                {
                    var offset=candidate.Center-originalCore;
                    if(candidate.Surface!=RoomSiege.Scan.SurfaceKind.Floor||offset.x<1.25f||offset.x>1.9f||Mathf.Abs(offset.z)>.65f)continue;
                    if(host.Placement.TryBuild("MG",candidate,out _,out _)){cell=candidate;found=true;break;}
                }
                Assert.IsTrue(found,"Need a tower on reachable open floor");
                var tower=host.Placement.Towers[0];Assert.IsTrue(host.Placement.TryUpgrade(tower,2,out _));
                int coins=game.Coins;var geometry=scan.transform.Find("Scan room geometry");
                var portal=geometry.Find("Spawn "+grid.SpawnPoints[0].AnchorId);
                scan.SetCoreFollowsPlayer(true);
                var mesh=geometry.Find("Virtual grid overlay/Placeable cells").GetComponent<MeshFilter>().sharedMesh;
                var vertices=mesh.vertices;
                var visualization=scan.GetComponent<RoomSiege.Scan.ScanVisualization>();
                var advance=typeof(RoomSiege.Scan.ScanVisualization).GetMethod("UpdateCoreVisual",BindingFlags.Instance|BindingFlags.NonPublic);
                Assert.IsTrue(scan.TryFollowPlayer(cell.Center+Vector3.up*1.6f),scan.Status);
                Assert.AreEqual(cell.Center,scan.CurrentGrid.SafeZoneCenter);
                Assert.IsFalse(tower.Removed);Assert.AreEqual(2,tower.Level);Assert.AreEqual(coins,game.Coins);
                Assert.AreEqual(cell.Center,tower.Cell.Center);Assert.AreSame(room,scan.CurrentRoom);
                Assert.AreSame(grid.WalkMap,scan.CurrentGrid.WalkMap);Assert.AreSame(grid.SpawnPoints,scan.CurrentGrid.SpawnPoints);
                Assert.AreSame(geometry,scan.transform.Find("Scan room geometry"));
                Assert.AreSame(portal,geometry.Find("Spawn "+grid.SpawnPoints[0].AnchorId));
                var ring=geometry.Find("Moving core marker/Safe zone 0.8m").GetComponent<LineRenderer>();
                Assert.AreEqual(originalCore,visualization.VisualCoreCenter,"Publishing a target must not teleport the displayed circle");
                advance.Invoke(visualization,new object[]{1f/90});
                float afterOneFrame=Vector3.Distance(visualization.VisualCoreCenter,cell.Center);
                Assert.Less(afterOneFrame,Vector3.Distance(originalCore,cell.Center));
                Assert.Greater(afterOneFrame,.01f);
                for(int i=0;i<90;i++)advance.Invoke(visualization,new object[]{1f/90});
                var ringPoint=ring.transform.TransformPoint(ring.GetPosition(0));
                Assert.AreEqual(cell.Center.x+.8f,ringPoint.x,.001f);Assert.AreEqual(cell.Center.z,ringPoint.z,.001f);
                Assert.AreSame(mesh,geometry.Find("Virtual grid overlay/Placeable cells").GetComponent<MeshFilter>().sharedMesh);
                if(grid.ReservedRouteCells.Select(c=>c.Index).SequenceEqual(scan.CurrentGrid.ReservedRouteCells.Select(c=>c.Index)))
                    CollectionAssert.AreEqual(vertices,mesh.vertices,"Unchanged route masks do not upload the deployment mesh");
                Assert.IsFalse(host.Placement.TryCanonical(cell,out _));
                var followed=scan.CurrentGrid;
                Assert.IsFalse(scan.TryFollowPlayer(originalCore+Vector3.right*100));Assert.AreSame(followed,scan.CurrentGrid);
                Assert.IsFalse(scan.TryFollowPlayer(grid.SpawnPoints[0].Position));Assert.AreSame(followed,scan.CurrentGrid);
                Assert.IsTrue(scan.TryFollowPlayer(originalCore));Assert.IsTrue(host.Placement.TryCanonical(cell,out _));
                Assert.IsTrue(scan.TryFollowPlayer(originalCore+Vector3.right*.04f),"Small steps under 10cm should also move the actual objective");
                Assert.AreEqual(originalCore.x+.04f,host.Placement.Grid.SafeZoneCenter.x,.001f);
                Assert.AreSame(scan.CurrentGrid,host.Placement.Grid);
                panels.ShowHelp();var paused=scan.CurrentGrid;
                Assert.IsFalse(scan.TryFollowPlayer(cell.Center));Assert.AreSame(paused,scan.CurrentGrid);
                panels.Navigation.CloseCurrent();scan.SetCoreFollowsPlayer(false);var fixedGrid=scan.CurrentGrid;
                Assert.IsFalse(scan.TryFollowPlayer(cell.Center));Assert.AreSame(fixedGrid,scan.CurrentGrid);
                Assert.AreEqual(coins,game.Coins);Assert.IsFalse(tower.Removed);
                panels.RequestMenu();var originalHead=Camera.main.transform.position;
                Camera.main.transform.position=originalHead+Vector3.right*8;
                scan.UseSandbox();
                Assert.AreEqual(Camera.main.transform.position.x,scan.CurrentGrid.SafeZoneCenter.x,.001f,"New room must not inherit the previous fixed core");
                Camera.main.transform.position=originalHead;
            }
            catch(Exception error){failure=error.ToString();}
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator FirstRunLessonUsesRealPlacementAndUpgradeWithoutBlockingConstruction()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            PlayerPrefs.DeleteKey(FirstRunTutorial.PreferenceKey);
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                Assert.IsTrue(MetaController.Instance!=null&&MetaController.Instance.IsReady);
                var panels=WorldPanelHost.Instance;var game=GameManager.Instance;
                var menu=panels.Panels[0].transform.Find("Main menu page");
                menu.Find("开始游戏").GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(TutorialStep.Room,panels.Tutorial.Step);
                var banner=panels.Panels[3].transform.Find("First run tutorial");
                Assert.IsTrue(banner.gameObject.activeInHierarchy);Assert.IsFalse(panels.Navigation.HasOpenPage);
                banner.Find("跳过教学").GetComponent<Button>().onClick.Invoke();
                Assert.AreEqual(2,PlayerPrefs.GetInt(FirstRunTutorial.PreferenceKey));
                panels.RequestMenu();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;
                panels.Panels[0].transform.Find("Main menu page/开始游戏").GetComponent<Button>().onClick.Invoke();
                RoomSiege.Scan.ScanController.Instance.UseSandbox();
                Assert.IsFalse(panels.Tutorial.Visible,"Skip must persist across new campaigns");
                panels.StartTutorial();Assert.IsTrue(panels.Tutorial.Active,"Manual relearning overrides saved preference");
                GameManager.Instance.SelectDifficulty(1);WaveController.Instance.StartNewCampaign();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;var towers=TowerHost.Instance;
                Assert.AreEqual(TutorialStep.Choose,panels.Tutorial.Step);
                Assert.IsTrue(towers.Choose("MG"));
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var towers=TowerHost.Instance;
                Assert.AreEqual(TutorialStep.Place,WorldPanelHost.Instance.Tutorial.Step);
                Assert.IsFalse(towers.Tap(new Ray(new Vector3(100,10,100),Vector3.down)));
                Assert.AreEqual(TutorialStep.Place,WorldPanelHost.Instance.Tutorial.Step);
                var cell=towers.Placement.Grid.PlaceableCells[0];
                Assert.IsTrue(towers.Tap(new Ray(cell.Center+Vector3.up*.4f,Vector3.down)));
                Assert.AreEqual(100,GameManager.Instance.Coins);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;var towers=TowerHost.Instance;
                Assert.AreEqual(TutorialStep.Inspect,panels.Tutorial.Step);
                Assert.IsTrue(panels.Panels[1].transform.Find("Tower slot 0").gameObject.activeInHierarchy);
                Assert.IsFalse(panels.Navigation.HasOpenPage);
                Physics.SyncTransforms();var tower=towers.Placement.Towers[0];
                Assert.IsTrue(towers.Tap(new Ray(tower.Cell.Center+Vector3.up*.4f,Vector3.down)));
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;
                Assert.AreEqual(TutorialStep.Upgrade,panels.Tutorial.Step);
                panels.ShowHelp();Assert.IsTrue(WaveController.Instance.IsPaused);
                panels.RefreshTutorial();
                Assert.IsFalse(panels.Panels[3].transform.Find("First run tutorial").gameObject.activeSelf,"Function pages hide the lesson too");
                panels.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.IsTrue(WorldPanelHost.Instance.Panels[3].transform.Find("First run tutorial").gameObject.activeSelf);
                TowerHost.Instance.Upgrade(2);Assert.AreEqual(60,GameManager.Instance.Coins);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.AreEqual(TutorialStep.Launch,WorldPanelHost.Instance.Tutorial.Step);
                WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;
                Assert.AreEqual(TutorialStep.Completed,panels.Tutorial.Step);
                Assert.AreEqual(1,PlayerPrefs.GetInt(FirstRunTutorial.PreferenceKey));
                panels.Tutorial.Skip();panels.RefreshTutorial();
                Assert.IsTrue(panels.Panels[3].transform.Find("Progress ring").gameObject.activeSelf);
                Assert.AreEqual(1,PlayerPrefs.GetInt(FirstRunTutorial.PreferenceKey),"Dismissing completion does not save Skip");
                panels.RequestMenu();
            }
            catch(Exception error){failure=error.ToString();}
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator ScanButtonCanInterruptBackgroundReadAndTrapKillsHardcoreWaveTenGrunt()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((WaveController.Instance==null||!WaveController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            TowerInstance trap=null;int before=0;
            try
            {
                var scan=RoomSiege.Scan.ScanController.Instance;var panels=WorldPanelHost.Instance;
                WaveController.Instance.ReturnToMenu();GameManager.Instance.SetState(GameState.SCAN);
                var button=panels.Panels[0].transform.Find("Room setup page/扫描真实房间").GetComponent<Button>();
                typeof(RoomSiege.Scan.ScanController).GetProperty("IsScanning").SetValue(scan,true);
                typeof(WorldPanelHost).GetMethod("RefreshSetup",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(panels,null);
                Assert.IsTrue(button.interactable,"A background query must not block an explicit capture");
                typeof(RoomSiege.Scan.ScanController).GetProperty("CaptureRequested").SetValue(scan,true);
                typeof(WorldPanelHost).GetMethod("RefreshSetup",BindingFlags.NonPublic|BindingFlags.Instance).Invoke(panels,null);
                Assert.IsFalse(button.interactable,"Do not launch two system captures");
                scan.UseSandbox();GameManager.Instance.SelectDifficulty(2);WaveController.Instance.StartNewCampaign();
                panels.Tutorial.Cancel();TowerHost.Instance.enabled=false;
                Assert.IsTrue(TowerHost.Instance.Placement.TryBuild("Trap",scan.CurrentGrid.PlaceableCells[0],out trap,out _));
                before=GameManager.Instance.Coins;GameManager.Instance.SetCurrentWave(10);GameManager.Instance.SetState(GameState.WAVE);
                Assert.IsTrue(EnemyController.Instance.Enqueue("Grunt"));
            }
            catch(Exception error){failure=error.ToString();}
            EnemyActor actor=null;until=Time.realtimeSinceStartup+5;
            while(failure==null&&Time.realtimeSinceStartup<until)
            {
                yield return null;
                foreach(var candidate in EnemyController.Instance.ActiveEnemies)if(candidate.IsAlive){actor=candidate;break;}
                if(actor!=null&&actor.CanBeTargeted)break;
            }
            if(failure==null)try
            {
                Assert.IsNotNull(actor);Assert.IsTrue(actor.CanBeTargeted);
                Assert.AreEqual(169.32f,actor.HitPoints,.001f);Assert.AreEqual(90,actor.Armor);
                actor.transform.position=trap.Cell.Center;
                new TowerCombat(trap,TowerHost.Instance.Placement.Consume).Step(.1f,Vector3.zero,new ITowerTarget[]{actor},null);
                Assert.IsFalse(actor.IsAlive,"Lv1 mine kills the real full-health armored W10 Grunt");
                Assert.IsTrue(trap.Consumed);Assert.AreEqual(before+6,GameManager.Instance.Coins,"One ordinary kill reward");
            }
            catch(Exception error){failure=error.ToString();}
            if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;
            if(WaveController.Instance!=null)WaveController.Instance.ReturnToMenu();
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator BattleFeedbackTracksShieldSlowLeakAndCampaignReset()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            while((MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var game=GameManager.Instance;var towers=TowerHost.Instance;var waves=WaveController.Instance;
                game.SetState(GameState.SCAN);game.SelectDifficulty(1);waves.StartNewCampaign();
                Assert.IsTrue(towers.Placement.TryBuild("MG",towers.Placement.Grid.PlaceableCells[0],out _,out _));
                waves.StartWaveFromUi();towers.enabled=false;
                EnemyController.Instance.ClearEnemies();
                game.SetCurrentWave(7);
                Assert.IsTrue(EnemyController.Instance.Enqueue("Shielded"));
            }
            catch(Exception error){failure=error.ToString();}
            EnemyActor actor=null;
            until=Time.realtimeSinceStartup+5;
            while(failure==null&&actor==null&&Time.realtimeSinceStartup<until)
            {
                yield return null;
                foreach(var candidate in EnemyController.Instance.ActiveEnemies)if(candidate.IsAlive&&candidate.EnemyId=="Shielded"){actor=candidate;break;}
            }
            if(failure==null)try
            {
                Assert.IsNotNull(actor);
                var status=actor.GetComponent<EnemyStatusVisual>();
                status.Refresh(0);Assert.AreEqual(1,status.HealthFraction);Assert.AreEqual(1,status.ShieldFraction);
                float bodyHp=actor.HitPoints;
                Assert.AreEqual(60,actor.Armor);
                actor.ReceiveHit(new TowerHit((actor.Shield/EnemyController.Instance.LaserShieldMultiplier+2)*(1+actor.Armor/100),actor.Position,TowerDamageKind.Laser,0));
                status.Refresh(0);
                Assert.AreEqual(0,actor.Shield,.0001f);Assert.AreEqual(bodyHp-2,actor.HitPoints,.0001f,"Shield overflow reaches HP without duplicate armor mitigation");
                actor.ReceiveHit(new TowerHit(16,actor.Position,TowerDamageKind.Projectile,0));
                Assert.AreEqual(bodyHp-12,actor.HitPoints,.001f,"Damage after shield break still uses the same armor");
                Assert.IsTrue(status.ShieldBreakVisible);StringAssert.Contains("破盾",status.StatusCaption);
                actor.Slow(912,1,2);status.Refresh(0);StringAssert.Contains("冰冻",status.StatusCaption);
                WorldPanelHost.Instance.ShowHelp();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.IsFalse(actor.transform.Find("Enemy compact status").gameObject.activeSelf,"Function page hides enemy labels");
                WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.IsTrue(actor.transform.Find("Enemy compact status").gameObject.activeSelf);
                actor.transform.position=RoomSiege.Scan.ScanController.Instance.CurrentGrid.SafeZoneCenter;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                Assert.AreEqual(8,GameManager.Instance.Lives,.0001f);
                var panels=WorldPanelHost.Instance;
                string message=panels.Panels[2].transform.Find("Info hint").GetComponent<Text>().text;
                StringAssert.Contains("护盾兵",message);StringAssert.Contains("-2生命",message);
                Assert.IsTrue(WaveController.Instance.GetComponent<CoreHitVisual>().Visible);
                Assert.IsFalse(actor.transform.Find("Enemy compact status").gameObject.activeSelf);
                panels.RequestRestart();TowerHost.Instance.enabled=true;
                Assert.IsFalse(WaveController.Instance.GetComponent<CoreHitVisual>().Visible);
                Assert.IsFalse(panels.Panels[2].transform.Find("Info hint").GetComponent<Text>().text.Contains("漏过"));
            }
            catch(Exception error){failure=error.ToString();}
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        [UnityTest]
        public IEnumerator WaveCuesActualBossFailureAudioAndRestartStayInSync()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");SavePrefs();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            RuntimeErrors.Clear();Application.logMessageReceived+=CaptureRuntimeError;
            float until=Time.realtimeSinceStartup+20;string failure=null;
            var audioReady=typeof(RoomSiege.Audio.AudioDirector).GetField("ready",BindingFlags.Instance|BindingFlags.NonPublic);
            while((MetaController.Instance==null||!MetaController.Instance.IsReady||RoomSiege.Audio.AudioDirector.Instance==null||
                !(bool)audioReady.GetValue(RoomSiege.Audio.AudioDirector.Instance))&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var game=GameManager.Instance;var towers=TowerHost.Instance;var waves=WaveController.Instance;
                Assert.IsTrue((bool)audioReady.GetValue(RoomSiege.Audio.AudioDirector.Instance));
                game.SetState(GameState.SCAN);game.SelectDifficulty(1);waves.StartNewCampaign();
                WorldPanelHost.Instance.Tutorial.Cancel();
                Assert.IsTrue(towers.Placement.TryBuild("MG",towers.Placement.Grid.PlaceableCells[0],out _,out _));
                waves.StartWaveFromUi();towers.enabled=false;
                Assert.AreEqual(WavePhaseCue.Started,waves.GetComponent<WavePanelUi>().PhaseNotice.Kind);
                Assert.AreEqual(1,PlayingCueCount("Wave horn"),"One start horn per sandbox entrance");
                Assert.AreEqual(0,PlayingCueCount("Wave start"),"Do not double the wave horn");
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+10;
            while(failure==null&&WaveController.Instance.IsWaveActive&&Time.realtimeSinceStartup<until)
            {
                foreach(var actor in EnemyController.Instance.ActiveEnemies)if(actor.IsAlive)actor.Damage(10000);
                yield return null;
            }
            if(failure==null)try
            {
                var waves=WaveController.Instance;var panels=WorldPanelHost.Instance;
                Assert.IsTrue(waves.IsPreparing);Assert.AreEqual(0,waves.Results[0].Leaks);
                Assert.AreEqual(WavePhaseCue.Cleared,waves.GetComponent<WavePanelUi>().PhaseNotice.Kind);
                StringAssert.Contains("零漏怪",panels.Panels[3].transform.Find("Progress label").GetComponent<Text>().text);
                Assert.Greater(waves.PreparationRemaining,14,"No added victory/clear delay");
                Assert.AreEqual(1,PlayingCueCount("UI confirm"));
                waves.StartWaveFromUi();Assert.AreEqual(2,waves.CurrentWave);Assert.IsTrue(waves.IsWaveActive);
                Assert.AreEqual(WavePhaseCue.Started,waves.GetComponent<WavePanelUi>().PhaseNotice.Kind);
                EnemyController.Instance.ClearEnemies();Assert.IsTrue(EnemyController.Instance.Enqueue("Boss",2));
            }
            catch(Exception error){failure=error.ToString();}
            EnemyActor boss=null;until=Time.realtimeSinceStartup+5;
            while(failure==null&&boss==null&&Time.realtimeSinceStartup<until)
            {
                yield return null;
                foreach(var actor in EnemyController.Instance.ActiveEnemies)if(actor.IsAlive&&actor.EnemyId=="Boss"){boss=actor;break;}
            }
            if(failure==null)try
            {
                Assert.IsNotNull(boss);
                var waves=WaveController.Instance;
                Assert.AreEqual(WavePhaseCue.Boss,waves.GetComponent<WavePanelUi>().PhaseNotice.Kind);
                StringAssert.Contains("首领已入场",WorldPanelHost.Instance.Panels[3].transform.Find("Progress label").GetComponent<Text>().text);
                Assert.AreEqual(1,PlayingCueCount("Wave start"));
                GameManager.Instance.ApplyLeakDamage(6);boss.transform.position=RoomSiege.Scan.ScanController.Instance.CurrentGrid.SafeZoneCenter;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var waves=WaveController.Instance;var panels=WorldPanelHost.Instance;
                Assert.AreEqual(CampaignOutcome.Failure,waves.Outcome);Assert.AreEqual("Boss",waves.LastLeakedEnemyId);
                Assert.IsTrue(panels.Navigation.HasOpenPage);Assert.IsFalse(waves.GetComponent<WavePanelUi>().PhaseNotice.Visible);
                var result=panels.Panels[0].transform.Find("Meta overlay/Settlement page");
                StringAssert.Contains("防线失守",result.Find("Settlement headline").GetComponent<Text>().text);
                StringAssert.Contains("首领",result.Find("Settlement reason").GetComponent<Text>().text);
                StringAssert.Contains("同房间",result.Find("Settlement next attempt").GetComponent<Text>().text);
                Assert.AreEqual(1,PlayingCueCount("Defeat"),"Opening the result page cannot suppress its cue");
                Assert.AreEqual(0,PlayingCueCount("Wave start"),"Old battlefield cues must stop on result");
                panels.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;
                var fallback=panels.Panels[0].transform.Find("Result actions page");
                Assert.IsTrue(fallback.gameObject.activeInHierarchy);
                StringAssert.Contains("首领",fallback.Find("Result hint").GetComponent<Text>().text);
                fallback.Find("再来一局").GetComponent<Button>().onClick.Invoke();TowerHost.Instance.enabled=true;
                AssertFreshBuild(150);Assert.AreEqual("",WaveController.Instance.LastLeakedEnemyId);Assert.AreEqual("",WaveController.Instance.FinishReason);
                Assert.AreEqual(0,PlayingCueCount("Defeat"));
                typeof(WaveController).GetMethod("FinishCampaign",BindingFlags.Instance|BindingFlags.NonPublic)
                    .Invoke(WaveController.Instance,new object[]{CampaignOutcome.Victory,"胜利 · 十波全部守住"});
                Assert.IsTrue(panels.Navigation.HasOpenPage);
                Assert.AreEqual(1,PlayingCueCount("Victory"));
                StringAssert.Contains("房间守住",panels.Panels[0].transform.Find("Meta overlay/Settlement page/Settlement headline").GetComponent<Text>().text);
                panels.RequestRestart();AssertFreshBuild(150);Assert.AreEqual(0,PlayingCueCount("Victory"));
            }
            catch(Exception error){failure=error.ToString();}
            Application.logMessageReceived-=CaptureRuntimeError;
            if(RuntimeErrors.Count>0)failure+="\nUnexpected runtime errors: "+string.Join("\n",RuntimeErrors);
            yield return new ExitPlayMode();RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }

        private static int PlayingCueCount(string name)
        {
            int count=0;
            foreach(var source in RoomSiege.Audio.AudioDirector.Instance.GetComponentsInChildren<AudioSource>())
                if(source.isPlaying&&source.clip!=null&&source.clip.name==name)
                {Assert.AreEqual(1,source.spatialBlend);count++;}
            return count;
        }

        internal static void SavePrefs()
        {
            for(int slot=0;slot<2;slot++)
            {
                string key="RoomSiege.Progression.v1."+slot;
                SessionState.SetBool("RoomSiege.Test.Progression.Exists."+slot,PlayerPrefs.HasKey(key));SessionState.SetString("RoomSiege.Test.Progression."+slot,PlayerPrefs.GetString(key,""));
            }
            SessionState.SetInt("RoomSiege.Test.Tutorial",PlayerPrefs.HasKey(FirstRunTutorial.PreferenceKey)?PlayerPrefs.GetInt(FirstRunTutorial.PreferenceKey):-1);
            foreach(var key in SavedPrefs)SessionState.SetInt("RoomSiege.Test.Backup."+key,
                PlayerPrefs.HasKey("RoomSiege.Meta."+key)?PlayerPrefs.GetInt("RoomSiege.Meta."+key):-1);
            SessionState.SetBool("RoomSiege.Test.ScoresExist",PlayerPrefs.HasKey("RoomSiege.Meta.LocalScores"));
            SessionState.SetString("RoomSiege.Test.Scores",PlayerPrefs.GetString("RoomSiege.Meta.LocalScores",""));
            SessionState.SetBool("RoomSiege.Test.EntranceExists",PlayerPrefs.HasKey(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey));
            SessionState.SetString("RoomSiege.Test.Entrance",PlayerPrefs.GetString(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey,""));
        }
        internal static void RestorePrefs()
        {
            int tutorial=SessionState.GetInt("RoomSiege.Test.Tutorial",-1);
            if(tutorial<0)PlayerPrefs.DeleteKey(FirstRunTutorial.PreferenceKey);else PlayerPrefs.SetInt(FirstRunTutorial.PreferenceKey,tutorial);
            foreach(var key in SavedPrefs)
            {
                int value=SessionState.GetInt("RoomSiege.Test.Backup."+key,-1);
                if(value<0)PlayerPrefs.DeleteKey("RoomSiege.Meta."+key);else PlayerPrefs.SetInt("RoomSiege.Meta."+key,value);
            }
            if(!SessionState.GetBool("RoomSiege.Test.ScoresExist",false))PlayerPrefs.DeleteKey("RoomSiege.Meta.LocalScores");
            else PlayerPrefs.SetString("RoomSiege.Meta.LocalScores",SessionState.GetString("RoomSiege.Test.Scores",""));
            if(!SessionState.GetBool("RoomSiege.Test.EntranceExists",false))PlayerPrefs.DeleteKey(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey);
            else PlayerPrefs.SetString(RoomSiege.Scan.RoomEntranceMemory.PreferenceKey,SessionState.GetString("RoomSiege.Test.Entrance",""));
            for(int slot=0;slot<2;slot++)
            {
                string key="RoomSiege.Progression.v1."+slot;
                if(SessionState.GetBool("RoomSiege.Test.Progression.Exists."+slot,false))PlayerPrefs.SetString(key,SessionState.GetString("RoomSiege.Test.Progression."+slot,""));else PlayerPrefs.DeleteKey(key);
            }
            PlayerPrefs.Save();
        }

        private static void AssertFreshBuild(int gold)
        {
            var game=GameManager.Instance;var waves=WaveController.Instance;
            Assert.AreEqual(GameState.BUILD,game.State);Assert.AreEqual(gold,game.Coins);
            Assert.AreEqual(game.InitialLives,game.Lives);Assert.AreEqual(0,game.CurrentWave);
            Assert.IsFalse(game.IsFailed);Assert.IsFalse(game.IsVictorious);
            Assert.AreEqual(CampaignOutcome.None,waves.Outcome);Assert.AreEqual(0,waves.Results.Count);
            Assert.IsTrue(waves.AwaitingFirstWave);Assert.IsFalse(MetaController.Instance.HasSettlement);
            Assert.AreEqual(0,TowerHost.Instance.Placement.Towers.Count);
            Assert.AreEqual(0,EnemyController.Instance.ActiveCount);Assert.AreEqual(0,EnemyController.Instance.PendingCount);
        }
        private static void AssertPlacementPresentation(WorldPanelHost panels,bool active)
        {
            Assert.AreEqual(active,panels.PlacementMode);
            Assert.AreEqual(active,panels.transform.Find("Placement HUD").gameObject.activeInHierarchy);
            foreach(var panel in panels.Panels)
            {
                Assert.IsTrue(panel.gameObject.activeInHierarchy,"Do not disable follower roots or ongoing drag owners");
                var group=panel.GetComponent<CanvasGroup>();Assert.AreEqual(active?0:1,group.alpha);
                Assert.AreEqual(!active,group.blocksRaycasts);Assert.AreEqual(!active,group.interactable);
            }
        }
    }
}
