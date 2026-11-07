using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Enemies;
using RoomSiege.Meta;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.Waves;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RoomSiege.UI.Tests
{
    public sealed class Arsenal12RuntimeTests
    {
        private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static Button Named(Transform root,string name)=>root.GetComponentsInChildren<Button>(true).First(x=>x.name==name);
        private static void Capture(string name){Canvas.ForceUpdateCanvases();foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())if(camera.name=="Armory preview camera")camera.Render();CampaignRuntimeTests.CaptureUi(name);}
        private static EnemyActor Spawn(string id,Vector3 point)
        {
            var controller=EnemyController.Instance;Assert.IsTrue(controller.Enqueue(id));var actor=(EnemyActor)typeof(EnemyController).GetMethod("SpawnNext",Hidden).Invoke(controller,null);
            actor.transform.position=point;typeof(EnemyActor).GetField("age",Hidden).SetValue(actor,1f);typeof(EnemyActor).GetField("spawnFrame",Hidden).SetValue(actor,Time.frameCount-1);return actor;
        }
        [UnityTest]
        public IEnumerator NewShopPagesEveryWeaponBuildsUpgradesSellsAndAffectsActualEnemies()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int i=0;i<2;i++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+i);PlayerPrefs.Save();
            bool old=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;float until=Time.realtimeSinceStartup+40;while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var p=ProgressionController.Instance;Assert.IsTrue(p.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();p.Store.CompleteChallenge("test",20000);
                foreach(string id in WeaponRoster.Ids.Skip(6))Assert.IsTrue(p.Store.Buy(id,out _));
                p.OpenShop();var ui=p.GetComponent<ProgressionPanelUi>();ui.ShowWeapons();var root=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                Named(root,"辅助武器").onClick.Invoke();Named(root,"Weapon Sunder").onClick.Invoke();Capture("arsenal12-01-support");
                Named(root,"下一页武器").onClick.Invoke();Assert.IsNotNull(Named(root,"Weapon Gravity"));Capture("arsenal12-02-support-page2");
                Named(root,"作战武器").onClick.Invoke();Named(root,"下一页武器").onClick.Invoke();Named(root,"Weapon Missile").onClick.Invoke();Capture("arsenal12-03-combat");
                ui.ShowLoadout();Named(root,"下一组武器").onClick.Invoke();Named(root,"下一组武器").onClick.Invoke();Capture("arsenal12-04-loadout");WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            foreach(string id in WeaponRoster.Ids.Skip(6))
            {
                if(failure!=null)break;
                try
                {
                    var p=ProgressionController.Instance;WaveController.Instance.ReturnToMenu();Assert.IsTrue(p.Store.SetLoadout(new[]{id,"MG","Frost","Trap"},out _));p.ConfigurationChanged();
                    GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();GameManager.Instance.AddCoins(1000);
                }
                catch(Exception error){failure=error.ToString();}
                yield return null;yield return null;
                if(failure==null)try
                {
                    int before=GameManager.Instance.Coins;var tower=CampaignRuntimeTests.BuildOpeningTower(id,new Vector3(1,0,1.4f));var placement=TowerHost.Instance.Placement;Assert.IsTrue(ProgressionController.Instance.LoadoutLocked);
                    Assert.IsTrue(placement.TryUpgrade(tower,3,out _));Assert.AreEqual(3,tower.Level);int invested=tower.Definition.buildCost+tower.Definition.levels[1].upgradeCost+tower.Definition.levels[2].upgradeCost;
                    Assert.AreEqual(before-invested,GameManager.Instance.Coins,id+" build + upgrade debit");TowerHost.Instance.GetComponent<TowerPanelUi>().ShowUpgrade(tower);
                    if(id=="Railgun")Capture("arsenal12-05-built-railgun");
                    Assert.IsTrue(placement.TrySell(tower,out _));Assert.AreEqual(before-invested+Mathf.FloorToInt(invested*.7f),GameManager.Instance.Coins,id+" sale");
                }
                catch(Exception error){failure=id+": "+error;}
            }
            if(failure==null)try
            {
                WaveController.Instance.ReturnToMenu();var p=ProgressionController.Instance;Assert.IsTrue(p.Store.SetLoadout(new[]{"Sniper","Sunder","Cryo","Disruptor"},out _));p.ConfigurationChanged();GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();GameManager.Instance.AddCoins(1000);CampaignRuntimeTests.BuildOpeningTower("Sniper",new Vector3(1,0,1.4f));TowerHost.Instance.enabled=false;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);GameManager.Instance.SetCurrentWave(10);EnemyController.Instance.enabled=false;EnemyController.Instance.ClearEnemies();WaveController.Instance.enabled=false;
                var point=ScanController.Instance.CurrentGrid.SafeZoneCenter+Vector3.forward*2.4f;var tank=Spawn("Tank",point);var boss=Spawn("Boss",point+Vector3.right*.2f);var shield=Spawn("Shielded",point+Vector3.left*.2f);var targets=new ITowerTarget[]{tank,boss,shield};
                Func<string,TowerInstance> tower=id=>TowerInstance.CreatePreview(TowerHost.Instance.Catalog.GetTower(id),new GridCell("test",SurfaceKind.Floor,Vector2Int.zero,point-Vector3.forward));
                new TowerCombat(tower("Sunder"),_=>{}).Step(.01f,Vector3.zero,targets,null);Assert.AreEqual(.2f,tank.ArmorBreakFraction);Assert.AreEqual(.1f,boss.ArmorBreakFraction);
                float hp=tank.HitPoints;new TowerCombat(tower("Sniper"),_=>{}).Step(.01f,Vector3.zero,targets,tank);Assert.AreEqual(DifficultyCalculator.DamageAfterArmor(22,tank.Armor*.8f*.5f),hp-tank.HitPoints,.01f);
                float beforeShield=shield.Shield;new TowerCombat(tower("Disruptor"),_=>{}).Step(.01f,Vector3.zero,targets,null);Assert.AreEqual(beforeShield*.85f,shield.Shield,.01f);
                new TowerCombat(tower("Gravity"),_=>{}).Step(.01f,Vector3.zero,targets,null);Assert.AreEqual(.35f,tank.SlowFraction,.001f);Assert.AreEqual(.175f,boss.SlowFraction,.001f);
                var cryo=new TowerCombat(tower("Cryo"),_=>{});for(int i=0;i<5;i++)cryo.Step(2,Vector3.zero,targets,null);Assert.IsTrue(tank.IsFrozen);Assert.IsTrue(boss.IsFrozen);
                WaveController.Instance.ReturnToMenu();foreach(string id in WeaponRoster.Ids.Skip(6))Assert.IsTrue(ProgressionController.Instance.Store.OwnsWeapon(id));
            }
            catch(Exception error){failure=error.ToString();}
            if(EnemyController.Instance!=null)EnemyController.Instance.enabled=true;if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;if(WaveController.Instance!=null)WaveController.Instance.enabled=true;
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=old;Assert.IsNull(failure,failure);
        }
    }
}
