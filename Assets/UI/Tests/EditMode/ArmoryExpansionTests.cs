using System;
using System.IO;
using System.Linq;
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEditor.SceneManagement;
using UnityEngine.UI;
using RoomSiege.Progression;
using RoomSiege.Data;
using RoomSiege.Towers;
using RoomSiege.Scan;
using RoomSiege.Meta;
using RoomSiege.Waves;
namespace RoomSiege.UI.Tests
{
    public sealed class ArmoryExpansionTests
    {
        private sealed class Memory:IProgressionStorage
        {public string[] slots={"",""};public bool Fail;public string Read(int i)=>slots[i];public void Write(int i,string s){if(Fail)throw new IOException("write fail");slots[i]=s;}}
        private sealed class Target:ITowerTarget
        {public int TargetId{get;set;}public Vector3 Position{get;set;}public Vector3 PreviousPosition=>Position;public float HitRadius=>.1f;public float Hp=1000;public bool IsAlive=>Hp>0;public void Damage(float a){Hp-=a;}public void Slow(int id,float f,float d){}}
        private static BalanceCatalog Balance()=>BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));
        private static TowerInstance Tower(string id)=>TowerInstance.CreatePreview(Balance().GetTower(id),new GridCell("test",SurfaceKind.Floor,Vector2Int.zero,Vector3.zero));
        [Test]
        public void PurchaseWeaponsIsDurableAndLockedWeaponsCannotEnterLoadout()
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);
            Assert.IsTrue(store.OwnsWeapon("MG"));Assert.IsFalse(store.OwnsWeapon("Tesla"));
            Assert.IsFalse(store.SetLoadout(new[]{"Tesla","Frost","Laser","Trap"},out _));Assert.IsFalse(store.Buy("Tesla",out _));
            store.CompleteChallenge("test-funding",2000);Assert.IsTrue(store.Buy("Tesla",out _));Assert.AreEqual(1600,store.Fragments);
            Assert.IsFalse(store.Buy("Tesla",out _));Assert.AreEqual(1600,store.Fragments);
            Assert.IsTrue(store.Buy("Mortar",out _));Assert.AreEqual(1000,store.Fragments);
            Assert.IsTrue(store.SetLoadout(new[]{"Tesla","Mortar","Laser","Frost"},out _));
            var loaded=new ProgressionStore(catalog,memory);Assert.IsTrue(loaded.OwnsWeapon("Tesla"));Assert.IsTrue(loaded.OwnsWeapon("Mortar"));CollectionAssert.AreEqual(store.Loadout,loaded.Loadout);
        }
        [Test]
        public void WeaponPurchaseRollsBackOnWriteFailure()
        {var m=new Memory();var s=new ProgressionStore(ProgressionCatalog.Load(),m);s.CompleteChallenge("test",1000);m.Fail=true;Assert.IsFalse(s.Buy("Tesla",out _));Assert.AreEqual(1000,s.Fragments);Assert.IsFalse(s.OwnsWeapon("Tesla"));}
        [Test]
        public void OldSaveWithoutOwnedWeaponsPreservesSkinsCurrencyAndLoadout()
        {
            var c=ProgressionCatalog.Load();var m=new Memory();var s=new ProgressionStore(c,m);s.CompleteChallenge("test",2000);s.Buy("mg-elite",out _);s.Equip("MG","mg-elite",out _);
            // Re-envelope a genuine prior schema state, not a fresh-user reset.
            for(int i=0;i<2;i++)if(!string.IsNullOrEmpty(m.slots[i]))
            {
                var envelope=JsonUtility.FromJson<Envelope>(m.slots[i]);var state=JsonUtility.FromJson<ProgressionSave>(envelope.payload);state.ownedWeapons=null;
                envelope.payload=JsonUtility.ToJson(state).Replace("\"ownedWeapons\":[],","").Replace("\"ownedWeapons\":null,","");
                using(var sha=System.Security.Cryptography.SHA256.Create())envelope.sha256=BitConverter.ToString(sha.ComputeHash(System.Text.Encoding.UTF8.GetBytes(envelope.payload))).Replace("-","");
                m.slots[i]=JsonUtility.ToJson(envelope);
            }
            var loaded=new ProgressionStore(c,m);Assert.IsTrue(loaded.Writable);Assert.AreEqual(1750,loaded.Fragments);Assert.AreEqual("mg-elite",loaded.Equipped("MG"));Assert.IsFalse(loaded.OwnsWeapon("Tesla"));Assert.AreEqual(4,loaded.Loadout.Count);
        }
        [Serializable]private class Envelope{public string payload,sha256;}
        [Test]
        public void TeslaChainsOncePerIntervalWithDecayAndNoDuplicateHits()
        {
            var tower=Tower("Tesla");var combat=new TowerCombat(tower,_=>{});
            var a=new Target{TargetId=1,Position=new Vector3(0,.16f,1)};var b=new Target{TargetId=2,Position=new Vector3(.3f,.16f,1.3f)};var c=new Target{TargetId=3,Position=new Vector3(.6f,.16f,1.6f)};
            ITowerTarget[] targets={a,b,c};combat.Step(.01f,Vector3.zero,targets,null);Assert.AreEqual(993,a.Hp,.001f);Assert.AreEqual(995.1,b.Hp,.001f);Assert.AreEqual(996.57,c.Hp,.001f);
            for(int i=0;i<50;i++)combat.Step(.01f,Vector3.zero,targets,null);Assert.AreEqual(993,a.Hp,.001f);
            combat.Step(1,Vector3.zero,targets,null);Assert.Less(a.Hp,993);
        }
        [Test]
        public void TeslaDoesNotJumpBeyondConfiguredDistance()
        {var t=Tower("Tesla");var c=new TowerCombat(t,_=>{});var a=new Target{Position=new Vector3(0,.16f,1)};var b=new Target{Position=new Vector3(1.1f,.16f,1)};c.Step(.01f,Vector3.zero,new ITowerTarget[]{a,b},null);Assert.AreEqual(1000,b.Hp);}
        [Test]
        public void MortarDelaysAreaDamageAndCanMissMovingTargets()
        {
            var t=Tower("Mortar");var c=new TowerCombat(t,_=>{});int launched=0,fired=0;c.Launched+=(_,__,___,____)=>launched++;c.Fired+=(_,__,___)=>fired++;
            var a=new Target{Position=new Vector3(0,.16f,1.5f)};var b=new Target{Position=new Vector3(.3f,.16f,1.5f)};var far=new Target{Position=new Vector3(2,.16f,2)};
            ITowerTarget[] list={a,b,far};c.Step(.01f,Vector3.zero,list,null);Assert.AreEqual(1,launched);Assert.AreEqual(1000,a.Hp);c.Step(.3f,Vector3.zero,list,null);Assert.AreEqual(1000,a.Hp);
            a.Position=new Vector3(0,.16f,4);c.Step(.4f,Vector3.zero,list,null);Assert.AreEqual(1000,a.Hp);Assert.Less(b.Hp,1000);Assert.AreEqual(1000,far.Hp);Assert.AreEqual(1,fired);
        }
        [Test]
        public void MortarIgnoresTargetsInsideMinimumRangeAndSelectsOuterTarget()
        {var t=Tower("Mortar");var c=new TowerCombat(t,_=>{});var near=new Target{Position=new Vector3(0,.16f,.4f)};var outer=new Target{Position=new Vector3(0,.16f,2)};c.Step(.01f,Vector3.zero,new ITowerTarget[]{near,outer},near);Assert.AreSame(outer,c.Target);c.Step(.7f,Vector3.zero,new ITowerTarget[]{near,outer},null);Assert.AreEqual(1000,near.Hp);Assert.Less(outer.Hp,1000);}
        [Test]
        public void EverySupplyHasVisibleVectorGeometry()
        {
            foreach(var supply in ProgressionCatalog.Load().supplies)
            {
                var go=new GameObject("glyph",typeof(RectTransform));
                try
                {
                    var glyph=go.AddComponent<ArmoryGlyph>();glyph.Id=supply.id;glyph.rectTransform.sizeDelta=new Vector2(60,60);
                    using(var vertices=new UnityEngine.UI.VertexHelper())
                    {
                        typeof(ArmoryGlyph).GetMethod("OnPopulateMesh",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic,null,new[]{typeof(VertexHelper)},null).Invoke(glyph,new object[]{vertices});
                        Assert.GreaterOrEqual(vertices.currentVertCount,12,supply.id);Assert.IsNotNull(go.GetComponent<CanvasRenderer>());Assert.AreSame(Texture2D.whiteTexture,glyph.mainTexture);
                    }
                }
                finally{UnityEngine.Object.DestroyImmediate(go);}
            }
        }
        [TestCase("Tesla")][TestCase("Mortar")]
        public void NewWeaponsHaveThreeDistinctModelLevelsAndCodexArtwork(string id)
        {
            var go=new GameObject("model test");Material material=null;
            try{material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));var view=go.AddComponent<TowerView>();view.Build(Tower(id),material);var model=go.GetComponentInChildren<HoloTowerModel>();Assert.IsNotNull(model);Assert.AreEqual(1,model.DisplayLevel);model.SetAppearance(3,TowerVisuals.ColorFor(id));Assert.AreEqual(3,model.DisplayLevel);Assert.IsNotNull(Resources.Load<Texture2D>("Codex/Towers/CodexTower_"+id));}
            finally{UnityEngine.Object.DestroyImmediate(go);if(material!=null)UnityEngine.Object.DestroyImmediate(material);}
        }
    }
    public sealed class ArmoryRuntimeTests
    {
        private static Button Named(Transform root,string name)=>root.GetComponentsInChildren<Button>(true).First(x=>x.name==name);
        private static Button Caption(Transform root,string text)=>root.GetComponentsInChildren<Button>(true).First(x=>x.GetComponentInChildren<Text>(true)?.text==text);
        private static void Capture(string name){Canvas.ForceUpdateCanvases();foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())if(camera.name=="Armory preview camera")camera.Render();CampaignRuntimeTests.CaptureUi(name);}
        [UnityTest]
        public IEnumerator ArmoryPurchaseSelectFourBuildUpgradeSellAndReturnKeepsUnlocks()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int i=0;i<2;i++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+i);PlayerPrefs.Save();
            bool old=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;float until=Time.realtimeSinceStartup+30;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var progress=ProgressionController.Instance;Assert.IsTrue(progress.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();progress.OpenShop();
                var root=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");var ui=progress.GetComponent<ProgressionPanelUi>();
                Named(root,"Weapon Tesla").onClick.Invoke();Capture("armory-01-tesla-locked");Assert.IsFalse(Caption(root,"解锁 400  /  还差 400").interactable);
                progress.Store.CompleteChallenge("test-funding",5000);ui.ShowWeapons();Named(root,"Weapon Tesla").onClick.Invoke();Caption(root,"永久解锁 · 400 碎片").onClick.Invoke();Assert.IsTrue(progress.Store.OwnsWeapon("Tesla"));
                Named(root,"Weapon Mortar").onClick.Invoke();Caption(root,"永久解锁 · 600 碎片").onClick.Invoke();Assert.IsTrue(progress.Store.OwnsWeapon("Mortar"));Capture("armory-02-mortar-owned");
                ui.ShowLoadout();Named(root,"Loadout slot 0").onClick.Invoke();Caption(root,"特斯拉  替换").onClick.Invoke();Named(root,"Loadout slot 2").onClick.Invoke();Caption(root,"迫击炮  替换").onClick.Invoke();
                CollectionAssert.AreEqual(new[]{"Tesla","Frost","Mortar","Trap"},progress.Store.Loadout);Capture("armory-03-four-loadout");
                ui.SelectSkin("mg-legend");Capture("armory-04-legendary");Named(root,"战术补给").onClick.Invoke();Capture("armory-05-supply-cards");Named(root,"下一页").onClick.Invoke();Capture("armory-06-supply-cards-page2");
                Named(root,"道具库存").onClick.Invoke();Capture("armory-07-carry");Named(root,"收藏挑战").onClick.Invoke();Capture("armory-08-challenges");
                WorldPanelHost.Instance.Navigation.CloseCurrent();GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                GameManager.Instance.AddCoins(1000);var tower=CampaignRuntimeTests.BuildOpeningTower("Tesla",new Vector3(1,0,1.4f));Assert.IsTrue(progress.LoadoutLocked);Assert.IsTrue(TowerHost.Instance.Placement.TryUpgrade(tower,3,out _));Assert.AreEqual(3,tower.Level);
                var mortar=CampaignRuntimeTests.BuildOpeningTower("Mortar",new Vector3(1.8f,0,1.4f));Assert.IsNotNull(mortar);TowerHost.Instance.GetComponent<TowerPanelUi>().ShowUpgrade(mortar);Capture("armory-09-mortar-built");
                int coins=GameManager.Instance.Coins;Assert.IsTrue(TowerHost.Instance.Placement.TrySell(mortar,out int refund));Assert.AreEqual(63,refund);Assert.AreEqual(coins+refund,GameManager.Instance.Coins);
                WaveController.Instance.ReturnToMenu();Assert.IsTrue(progress.Store.OwnsWeapon("Tesla"));Assert.IsTrue(progress.Store.OwnsWeapon("Mortar"));Assert.AreEqual(4000,progress.Store.Fragments);
            }
            catch(Exception error){failure=error.ToString();}
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=old;
            Assert.IsNull(failure,failure);
        }
    }
}
