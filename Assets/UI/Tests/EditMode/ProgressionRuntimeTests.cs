using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RoomSiege.Enemies;
using RoomSiege.Meta;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.Waves;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RoomSiege.UI.Tests
{
    public sealed class ProgressionRuntimeTests
    {
        private static readonly List<string> errors=new List<string>();
        private static void Error(string message,string stack,LogType type)
        {if((type==LogType.Error||type==LogType.Exception||type==LogType.Assert)&&!message.Contains("ContextNotCreated"))errors.Add(message+"\n"+stack);}
        private static Button FindButton(Transform root,string text)=>root.GetComponentsInChildren<Button>(true).First(x=>x.GetComponentInChildren<Text>(true)?.text==text);
        private static EnemyActor ReadyEnemy()=>EnemyController.Instance.ActiveEnemies.FirstOrDefault(x=>x.CanBeTargeted);
        private static Vector3 GroundNear(EnemyActor actor)
        {
            var grid=ScanController.Instance.CurrentGrid;
            return grid.SupportedCells.Where(x=>x.Surface==SurfaceKind.Floor).OrderBy(x=>(x.Center-actor.transform.position).sqrMagnitude).First().Center;
        }
        private static void RenderShop(string name)
        {
            foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())if(camera.name=="Armory preview camera")camera.Render();
            CampaignRuntimeTests.CaptureUi(name);
        }
        [UnityTest]
        public IEnumerator ShopLoadoutAllSuppliesRewardsAndRestartUseRealRuntime()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int slot=0;slot<2;slot++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+slot);PlayerPrefs.Save();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            errors.Clear();Application.logMessageReceived+=Error;string failure=null;
            float until=Time.realtimeSinceStartup+30;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady||MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;Assert.IsNotNull(progress);Assert.IsTrue(progress.IsReady);PaidSupplyFixture.Stock(progress.Store);
                WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();progress.OpenShop();progress.GetComponent<ProgressionPanelUi>().ShowSkins();
                var ui=progress.GetComponent<ProgressionPanelUi>();var overlay=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                Assert.IsTrue(ui.Visible);Assert.IsFalse(overlay.parent.Find("Main menu page").gameObject.activeSelf);
                Assert.IsFalse(FindButton(overlay,"购买 · 250 碎片").interactable);RenderShop("progression-01-shop-locked");
                Assert.IsTrue(progress.Store.CompleteChallenge("test-funding",5000));
                // Let the UI observe the wallet revision before pressing the now affordable button.
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+.35f;while(Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;var overlay=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                var buy=FindButton(overlay,"购买 · 250 碎片");Assert.IsTrue(buy.interactable);buy.onClick.Invoke();Assert.AreEqual(4750,progress.Store.Fragments);
                Assert.IsTrue(progress.Store.Owns("mg-elite"));FindButton(overlay,"穿戴").onClick.Invoke();Assert.AreEqual("mg-elite",progress.Store.Equipped("MG"));
                RenderShop("progression-02-shop-owned");
                for(int i=0;i<4;i++)
                {
                    string[] ids={"mg-legend","frost-legend","laser-legend","trap-legend"};
                    progress.GetComponent<ProgressionPanelUi>().SelectSkin(ids[i]);RenderShop("progression-03-legend-"+i);
                }
                FindButton(overlay,"战术补给").onClick.Invoke();RenderShop("progression-07-supply-store");
                FindButton(overlay,"下一页").onClick.Invoke();RenderShop("progression-07-supply-store-page2");
                FindButton(overlay,"道具库存").onClick.Invoke();RenderShop("progression-08-carry-config");
                FindButton(overlay,"收藏挑战").onClick.Invoke();RenderShop("progression-09-challenges");
                progress.OpenLoadout();RenderShop("progression-04-loadout");
                Assert.IsTrue(progress.Store.SetSupplies(Array.Empty<string>(),out _));progress.ConfigurationChanged();
                WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var game=GameManager.Instance;game.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();game.SelectDifficulty(0);
                WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                Assert.IsFalse(string.IsNullOrEmpty(ProgressionController.Instance.Store.RunId));Assert.IsTrue(ProgressionController.Instance.CanConfigure);
                var tower=CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));Assert.AreEqual("mg-elite",tower.Skin.id);
                Assert.IsTrue(ProgressionController.Instance.LoadoutLocked);Assert.IsFalse(ProgressionController.Instance.CanConfigure);
                WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+7;
            while(failure==null&&ReadyEnemy()==null&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;var actor=ReadyEnemy();Assert.IsNotNull(actor);
                Assert.IsFalse(progress.TryUseSupply("repair"));Assert.AreEqual(1,progress.Store.Count("repair"));
                actor.transform.position=ScanController.Instance.CurrentGrid.SafeZoneCenter+Vector3.forward*1.5f;
                FindButton(WorldPanelHost.Instance.Panels[0].transform.Find("Battle controls"),"使用道具").onClick.Invoke();
                Assert.IsTrue(WaveController.Instance.IsPaused);RenderShop("progression-05-carry");
                Assert.IsTrue(progress.TryUseSupply("freeze"));Assert.IsTrue(actor.IsFrozen);Assert.AreEqual(0,progress.Store.Count("freeze"));
                Assert.Greater(progress.SupplyCooldown,2.9f);Assert.IsFalse(progress.TryUseSupply("overload"));Assert.AreEqual(1,progress.Store.Count("overload"));
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+4;
            while(failure==null&&ProgressionController.Instance.SupplyCooldown>0&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;Assert.IsTrue(progress.TryUseSupply("overload"));
                TowerHost.Instance.GetComponent<TowerPanelUi>().Refresh();Assert.IsTrue(WorldPanelHost.Instance.PlacementMode,"Targeted supply must hide large UI during WAVE too");
                Assert.IsFalse(TowerHost.Instance.Tap(new Ray(new Vector3(40,5,40),Vector3.up)));Assert.AreEqual(1,progress.Store.Count("overload"));
                var tower=TowerHost.Instance.Placement.Towers.First(x=>!x.Removed);Physics.SyncTransforms();
                Assert.IsTrue(TowerHost.Instance.Tap(new Ray(tower.Cell.Center+Vector3.up*.6f,Vector3.down)));
                Assert.AreEqual(0,progress.Store.Count("overload"));Assert.Greater(tower.OverloadRemaining,44.9f);Assert.AreEqual(progress.Catalog.Supply("overload").magnitude,tower.OverloadOutputMultiplier,.001f);Assert.AreEqual(.2f,tower.OverloadRangeBonus);Assert.IsFalse(TowerHost.Instance.ExternalPlacementMode);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+4;
            while(failure==null&&ProgressionController.Instance.SupplyCooldown>0&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var game=GameManager.Instance;var progress=ProgressionController.Instance;game.ApplyLeakDamage(1);float before=game.Lives;
                Assert.IsTrue(progress.TryUseSupply("repair"));Assert.Greater(game.Lives,before);Assert.IsTrue(game.TookDamage);
                Assert.IsTrue(progress.Store.Snapshot().run.damaged);Assert.AreEqual(3,progress.Store.UsedItems);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+12;
            while(failure==null&&WaveController.Instance.IsWaveActive&&Time.realtimeSinceStartup<until)
            {
                foreach(var actor in EnemyController.Instance.ActiveEnemies.ToArray())if(actor.CanBeTargeted)actor.Damage(1000000);
                yield return null;
            }
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;Assert.AreEqual(1,progress.Store.Snapshot().run.completedWaves);Assert.AreEqual(4760,progress.Store.Fragments);
                GameManager.Instance.ApplyLeakDamage(100);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;Assert.IsTrue(progress.Store.RunSettled);Assert.IsTrue(MetaController.Instance.HasSettlement);
                Assert.IsTrue(progress.RewardSummary.Contains("+10"));Assert.IsTrue(progress.CanConfigure,"Settled runs may configure the next loadout");RenderShop("progression-06-settlement");
                FindButton(WorldPanelHost.Instance.Panels[0].transform.Find("Meta overlay"),"军械商店").onClick.Invoke();Assert.IsTrue(progress.GetComponent<ProgressionPanelUi>().Visible);
                WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try{WorldPanelHost.Instance.RequestRestart();}
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var game=GameManager.Instance;Assert.AreEqual(GameState.BUILD,game.State);Assert.AreEqual(180,game.Coins);Assert.AreEqual(game.InitialLives,game.Lives);
                Assert.AreEqual(4760,ProgressionController.Instance.Store.Fragments);Assert.IsFalse(ProgressionController.Instance.LoadoutLocked);
                WaveController.Instance.ReturnToMenu();
                Assert.IsTrue(ProgressionController.Instance.Store.SetSupplies(new[]{"emp","decoy","strike"},out _));
                game.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));TowerHost.Instance.enabled=false;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try{WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);}
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+7;
            while(failure==null&&ReadyEnemy()==null&&Time.realtimeSinceStartup<until)yield return null;
            EnemyActor affected=null;
            if(failure==null)try
            {
                affected=ReadyEnemy();Assert.IsNotNull(affected);var progress=ProgressionController.Instance;
                Assert.IsTrue(progress.TryUseSupply("emp"));Assert.IsFalse(TowerHost.Instance.Tap(new Ray(new Vector3(40,5,40),Vector3.down)));Assert.AreEqual(1,progress.Store.Count("emp"));
                progress.CancelSupply();Assert.AreEqual(1,progress.Store.Count("emp"));Assert.IsTrue(progress.TryUseSupply("emp"));
                Vector3 point=GroundNear(affected);Assert.IsTrue(TowerHost.Instance.Tap(new Ray(point+Vector3.up*2,Vector3.down)));
                Assert.Greater((float)typeof(EnemyActor).GetField("disruptRemaining",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(affected),2.9f);Assert.AreEqual(0,progress.Store.Count("emp"));
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+4;
            while(failure==null&&ProgressionController.Instance.SupplyCooldown>0&&Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                affected=ReadyEnemy();Assert.IsNotNull(affected);Assert.IsTrue(ProgressionController.Instance.TryUseSupply("decoy"));var point=GroundNear(affected);
                Assert.IsTrue(TowerHost.Instance.Tap(new Ray(point+Vector3.up*2,Vector3.down)));
                Assert.Greater((float)typeof(EnemyActor).GetField("decoyRemaining",BindingFlags.Instance|BindingFlags.NonPublic).GetValue(affected),4.9f);
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+4;
            while(failure==null&&ProgressionController.Instance.SupplyCooldown>0&&Time.realtimeSinceStartup<until)yield return null;
            float hp=0;
            if(failure==null)try
            {
                affected=ReadyEnemy();Assert.IsNotNull(affected);hp=affected.HitPoints;Assert.IsTrue(ProgressionController.Instance.TryUseSupply("strike"));
                var point=GroundNear(affected);Assert.IsTrue(TowerHost.Instance.Tap(new Ray(point+Vector3.up*2,Vector3.down)));Assert.AreEqual(hp,affected.HitPoints,"Strike has a warning delay");
            }
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+1.1f;while(Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                Assert.IsTrue(!affected.IsAlive||affected.HitPoints<hp,"Delayed strike must damage the real enemy");
                Assert.AreEqual(0,ProgressionController.Instance.Store.Count("strike"));Assert.AreEqual(3,ProgressionController.Instance.Store.UsedItems);
                Assert.IsFalse(TowerHost.Instance.ExternalPlacementMode);WaveController.Instance.ReturnToMenu();
            }
            catch(Exception error){failure=error.ToString();}
            if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;
            Application.logMessageReceived-=Error;if(errors.Count>0)failure+="\nRuntime errors: "+string.Join("\n",errors);
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=previous;
            Assert.IsNull(failure,failure);
        }
    }
}
