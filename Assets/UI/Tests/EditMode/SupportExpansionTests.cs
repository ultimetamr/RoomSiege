using System;
using System.Collections;
using System.IO;
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
    public sealed class SupportExpansionTests
    {
        private sealed class Memory:IProgressionStorage
        {public string[] Slots={"",""};public string Read(int i)=>Slots[i];public void Write(int i,string value)=>Slots[i]=value;}
        [Test]
        public void NewSuppliesUsePurchasedStockWithoutCarryAndPersistIndependently()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);
            store.CompleteChallenge("test-funding",2000);
            foreach(string id in new[]{"firefield","gravity","mine","thunder","corrosion","frostsigil"})
            {
                Assert.IsTrue(store.Buy(id,out _));Assert.IsTrue(store.Buy(id,out _));Assert.AreEqual(2,store.Count(id));
                Assert.IsTrue(store.SetSupplies(Array.Empty<string>(),out _));Assert.IsTrue(store.BeginRun(id,0));
                Assert.IsTrue(store.Consume(id,id));Assert.AreEqual(1,store.Count(id));
                store=new ProgressionStore(catalog,memory);Assert.AreEqual(1,store.Count(id));
                Assert.IsTrue(store.Consume(id,id));Assert.AreEqual(0,store.Count(id));Assert.IsFalse(store.Consume(id,id));store.Settle(id,false);
                store=new ProgressionStore(catalog,memory);Assert.AreEqual(0,store.Count(id));
            }
        }
        [Test]
        public void MoreThanThreeSuppliesReloadAndUnusedStockSurvivesThreeRuns()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);
            Assert.IsTrue(store.UnlockAllForPlaytest(50));
            for(int run=0;run<3;run++)
            {
                string id="inventory-regression-"+run;Assert.IsTrue(store.BeginRun(id,1));
                foreach(var item in catalog.supplies)
                {
                    Assert.IsTrue(store.Consume(id,item.id));
                    store=new ProgressionStore(catalog,memory);Assert.IsTrue(store.Writable,store.Error);Assert.AreEqual(id,store.RunId);
                    Assert.AreEqual(49-run,store.Count(item.id));
                }
                Assert.AreEqual(12,store.UsedItems,"Direct stock has no three-item limit");Assert.IsTrue(store.Settle(id,false));
                store=new ProgressionStore(catalog,memory);Assert.IsTrue(store.Writable);
                foreach(var item in catalog.supplies)Assert.AreEqual(49-run,store.Count(item.id));
            }
        }
        [TestCase("pulseInterval",0,"thunder")][TestCase("triggerRadius",2,"mine")]
        [TestCase("duration",29,"freeze")][TestCase("statusSeconds",0,"freeze")]
        [TestCase("magnitude",1.1f,"gravity")][TestCase("statusSeconds",0,"corrosion")]
        public void InvalidEffectParametersCannotLoad(string field,float value,string id)
        {
            var catalog=ProgressionCatalog.Load();typeof(SupplyDefinition).GetField(field).SetValue(catalog.Supply(id),value);
            Assert.Throws<ArgumentException>(()=>ProgressionCatalog.Parse(JsonUtility.ToJson(catalog)));
        }
        [Test]
        public void PriorInventoryIsPreservedWithoutRepeatedStarterGifts()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);store.CompleteChallenge("test",1000);store.Buy("freeze",out _);
            var loaded=new ProgressionStore(catalog,memory);Assert.AreEqual(960,loaded.Fragments);Assert.AreEqual(1,loaded.Count("freeze"));Assert.AreEqual(0,loaded.Count("mine"));
            Assert.IsFalse(loaded.SetSupplies(new[]{"mine","gravity","firefield"},out _));Assert.IsFalse(loaded.SetSupplies(new[]{"mine","gravity","firefield","thunder"},out _));
        }
        [Test]
        public void EverySupplyHasAtLeastThirtySecondsAndRepairPulsesReachThirty()
        {
            var catalog=ProgressionCatalog.Load();foreach(var item in catalog.supplies)Assert.GreaterOrEqual(item.duration,30,item.id);
            float repaired=0;var repair=new SupportField(catalog.Supply("repair"),Vector3.zero,-1);
            repair.Step(30,Array.Empty<EnemyActor>(),null,value=>repaired+=value);
            Assert.AreEqual(3,repaired,.001f);Assert.IsTrue(repair.Finished);
            Assert.AreEqual(1,SupportField.DamageMultiplier(0));Assert.AreEqual(2.62f,SupportField.DamageMultiplier(10),.001f);
            Assert.AreEqual(2.62f,SupportField.DamageMultiplier(100),.001f);
        }
        [TestCase("firefield")][TestCase("gravity")][TestCase("mine")][TestCase("thunder")][TestCase("frostsigil")]
        public void EmptyPreplacedFieldExpiresAndInvalidDeltaCannotAdvanceIt(string id)
        {
            var item=ProgressionCatalog.Load().Supply(id);var field=new SupportField(item,Vector3.zero,-14000);
            field.Step(float.NaN,Array.Empty<EnemyActor>());field.Step(-1,Array.Empty<EnemyActor>());Assert.AreEqual(0,field.Elapsed);
            field.Step(100,Array.Empty<EnemyActor>());Assert.IsTrue(field.Finished);Assert.AreEqual(0,field.Remaining);
        }
    }
    public sealed class SupportRuntimeTests
    {
        private static readonly BindingFlags Hidden=BindingFlags.NonPublic|BindingFlags.Instance;
        private static Button Button(Transform root,string name)=>root.GetComponentsInChildren<Button>(true).First(x=>x.name==name);
        private static void Capture(string name){Canvas.ForceUpdateCanvases();CampaignRuntimeTests.CaptureUi(name);}
        private static EnemyActor Spawn(string id,Vector3 point)
        {
            var enemies=EnemyController.Instance;Assert.IsTrue(enemies.Enqueue(id));
            var actor=(EnemyActor)typeof(EnemyController).GetMethod("SpawnNext",Hidden).Invoke(enemies,null);Assert.IsNotNull(actor);
            actor.transform.position=point;typeof(EnemyActor).GetField("previous",Hidden).SetValue(actor,actor.Position);
            typeof(EnemyActor).GetField("age",Hidden).SetValue(actor,1f);typeof(EnemyActor).GetField("spawnFrame",Hidden).SetValue(actor,Time.frameCount-1);return actor;
        }
        private static void Tick(EnemyActor actor,float dt)=>typeof(EnemyActor).GetMethod("Tick",Hidden).Invoke(actor,new object[]{dt,ScanController.Instance.CurrentGrid,TowerHost.Instance.Placement.Towers,null});
        [UnityTest]
        public IEnumerator ThirdWaveChoiceResumesFourthWaveWithoutRetiringLiveActors()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            bool old=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;float until=Time.realtimeSinceStartup+30;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                WaveController.Instance.ReturnToMenu();Assert.IsTrue(ProgressionController.Instance.Store.UnlockAllForPlaytest(50));GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();
                WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));TowerHost.Instance.enabled=false;EnemyController.Instance.enabled=false;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            for(int wave=1;wave<=3&&failure==null;wave++)
            {
                try
                {
                    var waves=WaveController.Instance;waves.StartWaveFromUi();Assert.IsTrue(waves.IsWaveActive);Assert.AreEqual(wave,waves.CurrentWave);
                    var enemies=EnemyController.Instance;
                    if(wave==3)
                    {
                        var p=ProgressionController.Instance;
                        var cell=ScanController.Instance.CurrentGrid.SupportedCells.Where(x=>x.Surface==SurfaceKind.Floor&&Vector3.Distance(x.Center,ScanController.Instance.CurrentGrid.SafeZoneCenter)>2&&TowerHost.Instance.Placement.Routes.CanReach(x.Center)).OrderBy(x=>(x.Center-(ScanController.Instance.CurrentGrid.SafeZoneCenter+Vector3.forward*2.4f)).sqrMagnitude).First();
                        Assert.IsTrue(p.TryUseSupply("gravity"),p.Status+" state="+GameManager.Instance.State+" stock="+p.Store.Count("gravity")+" paused="+waves.SimulationPaused);Assert.IsTrue(TowerHost.Instance.Tap(new Ray(cell.Center+Vector3.up*2,Vector3.down)),p.Status);
                    }
                    while(enemies.PendingCount>0)
                    {
                        var actor=(EnemyActor)typeof(EnemyController).GetMethod("SpawnNext",Hidden).Invoke(enemies,null);
                        Assert.IsNotNull(actor);typeof(EnemyActor).GetField("age",Hidden).SetValue(actor,1f);
                        typeof(EnemyActor).GetField("spawnFrame",Hidden).SetValue(actor,Time.frameCount-1);actor.Damage(1000000);typeof(EnemyController).GetMethod("LateUpdate",Hidden).Invoke(enemies,null);
                    }
                    Assert.IsTrue(enemies.IsDrained);
                }
                catch(Exception error){failure=error.ToString();}
                yield return null;yield return null;
            }
            if(failure==null)try
            {
                var waves=WaveController.Instance;Assert.IsTrue(waves.ReinforcementPending);Assert.IsTrue(waves.IsPaused);
                var perks=TowerHost.Instance.Placement.Perks;
                Assert.IsTrue(ProgressionController.Instance.ActiveSupplyIds().Contains("gravity"));
                Assert.AreEqual(3,perks.Offers.Count);Assert.IsTrue(perks.Offers.Any(x=>x.TargetId=="MG"));
                var supplyChoice=perks.Offers.Select((offer,index)=>new{offer,index}).First(x=>x.offer.IsSupply);
                Assert.AreEqual("gravity",supplyChoice.offer.TargetId);
                Capture("24-adaptive-reinforcement");
                // Check every catalog card in the actual panel font and width.
                var card=WorldPanelHost.Instance.Panels[0].transform.Find("Reinforcement page/强化 0").GetComponentInChildren<UnityEngine.UI.Text>();
                string oldText=card.text;
                foreach(string id in RoomSiege.Data.WeaponRoster.Ids.Concat(CampaignPerks.SupplyIds))
                    for(int variant=0;variant<3;variant++)
                    {
                        var offer=new ReinforcementOffer(id,CampaignPerks.SupplyIds.Contains(id),variant);
                        card.text=perks.OfferName(offer).Replace(" · ","\n")+"\n"+perks.OfferDescription(offer);
                        Assert.LessOrEqual(card.preferredHeight,card.rectTransform.rect.height+.1f,id+" card must fit vertically");
                    }
                card.text=oldText;
                Assert.AreEqual(15,waves.PreparationRemaining,.001f);waves.SelectReinforcement(supplyChoice.index);Assert.IsFalse(waves.ReinforcementPending);
                Assert.AreEqual(1,perks.Rank("gravity",supplyChoice.offer.Variant,true));
                Assert.IsTrue(ProgressionController.Instance.ActiveSupplyIds().Contains("gravity"),"Selection must preserve active field");
                Assert.AreEqual(1,TowerHost.Instance.Placement.Perks.ChoicesMade);Assert.IsTrue(TowerHost.Instance.Status.Contains("第 4 波"));
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var waves=WaveController.Instance;typeof(WaveController).GetField("preparationRemaining",Hidden).SetValue(waves,0f);
                typeof(WaveController).GetMethod("Update",Hidden).Invoke(waves,null);
                Assert.IsTrue(waves.IsWaveActive);Assert.AreEqual(4,waves.CurrentWave);Assert.Greater(EnemyController.Instance.PendingCount,0);
                var actor=(EnemyActor)typeof(EnemyController).GetMethod("SpawnNext",Hidden).Invoke(EnemyController.Instance,null);
                Assert.IsNotNull(actor);Assert.IsTrue(actor.IsAlive);int pending=EnemyController.Instance.PendingCount;
                waves.SelectReinforcement(1);Assert.IsTrue(actor.IsAlive,"Stale reinforcement click cannot clear a live actor");
                Assert.AreEqual(pending,EnemyController.Instance.PendingCount);Assert.AreEqual(1,TowerHost.Instance.Placement.Perks.ChoicesMade);
            }
            catch(Exception error){failure=error.ToString();}
            if(EnemyController.Instance!=null)EnemyController.Instance.enabled=true;if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=old;Assert.IsNull(failure,failure);
        }
        [UnityTest]
        public IEnumerator ConfirmedArmorBreakAndFieldPauseFollowInventoryAndMenuLifecycle()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int i=0;i<2;i++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+i);PlayerPrefs.Save();
            bool old=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;SupportField field=null;float elapsed=0;float until=Time.realtimeSinceStartup+30;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var p=ProgressionController.Instance;Assert.IsTrue(p.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();
                PaidSupplyFixture.Stock(p.Store);Assert.IsTrue(p.Store.SetSupplies(new[]{"gravity","corrosion","thunder"},out _));p.ConfigurationChanged();
                GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));TowerHost.Instance.enabled=false;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var p=ProgressionController.Instance;WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);
                EnemyController.Instance.enabled=false;EnemyController.Instance.ClearEnemies();WaveController.Instance.enabled=false;
                var point=ScanController.Instance.CurrentGrid.SupportedCells.Where(x=>x.Surface==SurfaceKind.Floor&&Vector3.Distance(x.Center,ScanController.Instance.CurrentGrid.SafeZoneCenter)>2).OrderBy(x=>(x.Center-(ScanController.Instance.CurrentGrid.SafeZoneCenter+Vector3.forward*2.4f)).sqrMagnitude).First().Center;
                var ray=new Ray(point+Vector3.up*2,Vector3.down);Assert.IsTrue(p.TryUseSupply("corrosion"));Assert.IsFalse(TowerHost.Instance.Tap(new Ray(new Vector3(40,5,40),Vector3.up)));Assert.AreEqual(1,p.Store.Count("corrosion"));p.CancelSupply();
                var tank=Spawn("Tank",point);var boss=Spawn("Boss",point+Vector3.right*.25f);
                Assert.IsTrue(p.TryUseSupply("corrosion"),"Select armor break");Assert.IsTrue(TowerHost.Instance.Tap(ray),"Confirm armor break on reachable floor");Assert.AreEqual(0,p.Store.Count("corrosion"));Assert.AreEqual(.75f,tank.ArmorBreakFraction);Assert.AreEqual(.5f,boss.ArmorBreakFraction);
                typeof(ProgressionController).GetField("cooldown",Hidden).SetValue(p,0f);
                Assert.IsTrue(p.TryUseSupply("gravity"));Assert.IsTrue(TowerHost.Instance.Tap(ray));Assert.AreEqual(0,p.Store.Count("gravity"));
                foreach(var marker in (IEnumerable)typeof(ProgressionController).GetField("markers",Hidden).GetValue(p))
                    if(marker.GetType().GetField("field").GetValue(marker) is SupportField value)field=value;
                Assert.IsNotNull(field);p.OpenLoadout();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try{Assert.IsTrue(WaveController.Instance.IsPaused);elapsed=field.Elapsed;}
            catch(Exception error){failure=error.ToString();}
            until=Time.realtimeSinceStartup+.25f;while(Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try{Assert.AreEqual(elapsed,field.Elapsed);WorldPanelHost.Instance.Navigation.CloseCurrent();}
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            until=Time.realtimeSinceStartup+.25f;while(Time.realtimeSinceStartup<until)yield return null;
            if(failure==null)try
            {
                var p=ProgressionController.Instance;Assert.Greater(field.Elapsed,elapsed);
                elapsed=field.Elapsed;GameManager.Instance.SetState(GameState.BUILD);
                typeof(ProgressionController).GetMethod("Update",Hidden).Invoke(p,null);
                Assert.AreEqual(elapsed,field.Elapsed,"Preparation must preserve the field clock");Assert.Greater(p.ActiveSupportCount,0);
                GameManager.Instance.SetState(GameState.WAVE);typeof(ProgressionController).GetMethod("TickMarkers",Hidden).Invoke(p,new object[]{1f});
                Assert.Greater(field.Elapsed,elapsed,"Field resumes in the next wave");
                for(int run=0;run<3;run++)
                {
                    WaveController.Instance.ReturnToMenu();Assert.AreEqual(0,p.ActiveSupportCount);
                    Assert.AreEqual(0,p.Store.Count("gravity"));Assert.AreEqual(1,p.Store.Count("thunder"));
                    GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                    Assert.AreEqual(0,p.Store.Count("gravity"));Assert.AreEqual(1,p.Store.Count("thunder"),"Unused stock survives every new run");
                }
                WaveController.Instance.ReturnToMenu();Assert.AreEqual(0,ProgressionController.Instance.ActiveSupportCount);
                Assert.AreEqual(0,ProgressionController.Instance.Store.Count("gravity"));Assert.AreEqual(1,ProgressionController.Instance.Store.Count("thunder"));
            }
            catch(Exception error){failure=error.ToString();}
            if(EnemyController.Instance!=null)EnemyController.Instance.enabled=true;if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;if(WaveController.Instance!=null)WaveController.Instance.enabled=true;
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=old;Assert.IsNull(failure,failure);
        }
        [UnityTest]
        public IEnumerator ShopCarryPlacementAndSixEffectsUseActualEnemies()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int i=0;i<2;i++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+i);PlayerPrefs.Save();
            bool old=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;float until=Time.realtimeSinceStartup+30;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var progress=ProgressionController.Instance;Assert.IsTrue(progress.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();
                PaidSupplyFixture.Stock(progress.Store);progress.Store.CompleteChallenge("test-funding",5000);progress.OpenShop();var ui=progress.GetComponent<ProgressionPanelUi>();ui.ShowSupplies();
                var root=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                Button(root,"下一页").onClick.Invoke();Button(root,"下一页").onClick.Invoke();Assert.IsNotNull(Button(root,"Supply product mine"));Capture("support-01-fire-gravity-mine");
                Button(root,"下一页").onClick.Invoke();Assert.IsNotNull(Button(root,"Supply product frostsigil"));Capture("support-02-thunder-corrosion-frost");
                Button(root,"阵法").onClick.Invoke();Capture("support-03-formation-filter");Assert.AreEqual(3,root.GetComponentsInChildren<Button>().Count(x=>x.name.StartsWith("Supply product ")));
                Button(root,"道具库存").onClick.Invoke();Button(root,"下一组道具").onClick.Invoke();Button(root,"下一组道具").onClick.Invoke();Button(root,"下一组道具").onClick.Invoke();Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(x=>x.text=="寒霜封锁阵"));Capture("support-04-carry-page3");
                Assert.IsTrue(progress.Store.SetSupplies(new[]{"firefield","gravity","mine"},out _));progress.ConfigurationChanged();WorldPanelHost.Instance.Navigation.CloseCurrent();
                GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));TowerHost.Instance.enabled=false;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var progress=ProgressionController.Instance;
                WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);GameManager.Instance.SetCurrentWave(10);
                EnemyController.Instance.enabled=false;EnemyController.Instance.ClearEnemies();WaveController.Instance.enabled=false;progress.enabled=false;
                var point=ScanController.Instance.CurrentGrid.SupportedCells.Where(x=>x.Surface==SurfaceKind.Floor&&Vector3.Distance(x.Center,ScanController.Instance.CurrentGrid.SafeZoneCenter)>2).OrderBy(x=>(x.Center-(ScanController.Instance.CurrentGrid.SafeZoneCenter+Vector3.forward*2.4f)).sqrMagnitude).First().Center;
                var a=Spawn("Tank",point);var b=Spawn("Tank",point+Vector3.right*.25f);var boss=Spawn("Boss",point+Vector3.left*.25f);
                Assert.IsTrue(progress.TryUseSupply("firefield"));Assert.IsFalse(TowerHost.Instance.Tap(new Ray(new Vector3(40,5,40),Vector3.up)));Assert.AreEqual(1,progress.Store.Count("firefield"));progress.CancelSupply();Assert.AreEqual(1,progress.Store.Count("firefield"));
                Assert.IsTrue(progress.TryUseSupply("firefield"));Assert.IsTrue(TowerHost.Instance.Tap(new Ray(point+Vector3.up*2,Vector3.down)));Assert.AreEqual(0,progress.Store.Count("firefield"));Assert.IsFalse(progress.TryUseSupply("mine"));
                var catalog=progress.Catalog;var targets=new[]{a,b,boss};
                foreach(var target in targets)typeof(EnemyActor).GetProperty("HitPoints").SetValue(target,100000f);
                typeof(EnemyActor).GetProperty("HitPoints").SetValue(boss,200000f);float hp=a.HitPoints;
                var fire=new SupportField(catalog.Supply("firefield"),point,-14001,10);fire.Step(.1f,targets);fire.Step(.1f,targets);Tick(a,.1f);
                Assert.AreEqual(DifficultyCalculator.DamageAfterArmor(catalog.Supply("firefield").magnitude*SupportField.DamageMultiplier(10)*.1f,a.Armor),hp-a.HitPoints,.01f,"Overlapping fire must not double its burn DPS");
                var gravity=new SupportField(catalog.Supply("gravity"),point,-14002);gravity.Step(.1f,targets);new SupportField(catalog.Supply("gravity"),point,-14003).Step(.1f,targets);
                Assert.AreEqual(.8f,a.SlowFraction,.001f);Assert.AreEqual(1,a.SlowSourceCount);Assert.AreEqual(.4f,boss.SlowFraction,.001f);
                var mine=new SupportField(catalog.Supply("mine"),point,-14004);int blasts=0;hp=b.HitPoints;mine.Step(.74f,targets,_=>blasts++);Assert.AreEqual(hp,b.HitPoints);mine.Step(.02f,targets,_=>blasts++);Assert.AreEqual(1,blasts);Assert.Less(b.HitPoints,hp);hp=b.HitPoints;mine.Step(1,targets,_=>blasts++);Assert.AreEqual(hp,b.HitPoints);Assert.AreEqual(1,blasts);
                var thunder=new SupportField(catalog.Supply("thunder"),point,-14005);float bossHp=boss.HitPoints;hp=b.HitPoints;int bolts=0;
                thunder.Step(.99f,targets,_=>bolts++);Assert.AreEqual(bossHp,boss.HitPoints);thunder.Step(.01f,targets,_=>bolts++);Assert.AreEqual(1,bolts);Assert.Less(boss.HitPoints,bossHp);Assert.AreEqual(hp,b.HitPoints);thunder.Step(10,targets,_=>bolts++);Assert.AreEqual(11,bolts);
                var frost=new SupportField(catalog.Supply("frostsigil"),point,-14006);frost.Step(1,targets);Assert.AreEqual(3,a.ChillStacks);frost.Step(1,targets);Assert.IsTrue(a.IsFrozen);Assert.IsTrue(boss.IsFrozen);
                Tick(a,1.01f);frost.Step(1,targets);Assert.IsFalse(a.IsFrozen);Assert.AreEqual(0,a.ChillStacks);Assert.Greater(a.FreezeProtection,0);
                a.ApplyArmorBreak(.5f,6);a.ApplyArmorBreak(.25f,1);Assert.AreEqual(.5f,a.ArmorBreakFraction);hp=a.HitPoints;a.Damage(10);Assert.AreEqual(DifficultyCalculator.DamageAfterArmor(10,a.Armor*.5f),hp-a.HitPoints,.01f);
                a.Slow(-14999,.8f,10);Tick(a,6.01f);Assert.AreEqual(0,a.ArmorBreakFraction);
                var shielded=Spawn("Shielded",point);typeof(EnemyActor).GetProperty("Shield").SetValue(shielded,100f);
                var emp=new SupportField(catalog.Supply("emp"),point,-14007);emp.Step(.1f,new[]{shielded});Assert.AreEqual(10,shielded.Shield,.001f);
                emp.Step(.1f,new[]{shielded});Assert.AreEqual(10,shielded.Shield,.001f,"A field strips each shield once");
                var lateEnemy=Spawn("Tank",point);var armor=new SupportField(catalog.Supply("corrosion"),point,-14008);
                armor.Step(1,Array.Empty<EnemyActor>());armor.Step(1,new[]{lateEnemy});Assert.AreEqual(.75f,lateEnemy.ArmorBreakFraction);
                var grunt=Spawn("Grunt",point);var lateMine=new SupportField(catalog.Supply("mine"),point,-14009,10);
                lateMine.Step(.8f,new[]{grunt});Assert.IsFalse(grunt.IsAlive,"Wave-scaled explosive must eliminate a tenth-wave Grunt");
                typeof(ProgressionController).GetMethod("TickMarkers",Hidden).Invoke(progress,new object[]{.1f});Capture("support-05-field-in-world");
                WaveController.Instance.ReturnToMenu();Assert.AreEqual(0,progress.ActiveSupportCount);Assert.AreEqual(0,progress.Store.Count("firefield"));Assert.AreEqual(1,progress.Store.Count("mine"));
            }
            catch(Exception error){failure=error.ToString();}
            if(ProgressionController.Instance!=null)ProgressionController.Instance.enabled=true;
            if(EnemyController.Instance!=null)EnemyController.Instance.enabled=true;if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;if(WaveController.Instance!=null)WaveController.Instance.enabled=true;
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=old;Assert.IsNull(failure,failure);
        }
    }
}
