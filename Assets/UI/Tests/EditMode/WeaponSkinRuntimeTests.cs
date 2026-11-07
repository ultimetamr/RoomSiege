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
using RoomSiege.Visuals;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;
using UnityEngine.Rendering.Universal;

namespace RoomSiege.UI.Tests
{
    public sealed class WeaponSkinRuntimeTests
    {
        private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static Button Button(Transform root,string label)=>root.GetComponentsInChildren<Button>().First(b=>b.GetComponentInChildren<Text>()?.text==label);
        private static void Capture(string name){Canvas.ForceUpdateCanvases();foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())if(camera.name=="Armory preview camera")camera.Render();CampaignRuntimeTests.CaptureUi(name);}
        private static EnemyActor Spawn(string id,Vector3 point)
        {
            var controller=EnemyController.Instance;Assert.IsTrue(controller.Enqueue(id));var enemy=(EnemyActor)typeof(EnemyController).GetMethod("SpawnNext",Hidden).Invoke(controller,null);
            enemy.transform.position=point;typeof(EnemyActor).GetField("age",Hidden).SetValue(enemy,1f);typeof(EnemyActor).GetField("spawnFrame",Hidden).SetValue(enemy,Time.frameCount-1);return enemy;
        }
        [Test]
        public void EverySkinArtworkResolvesAndMissingSkinFallsBackToWeapon()
        {
            var catalog=ProgressionCatalog.Load();
            foreach(var skin in catalog.skins)
            {
                var artwork=Resources.Load<Texture2D>("Codex/Skins/Skin_"+skin.id);
                Assert.IsNotNull(artwork,skin.id);
                Assert.AreSame(artwork,WeaponArtwork.Load(skin.weaponId,skin.id),skin.id);
                Assert.AreNotSame(WeaponArtwork.Load(skin.weaponId),artwork,skin.id);
            }
            Assert.AreSame(WeaponArtwork.Load("MG"),WeaponArtwork.Load("MG","missing-skin"));
        }
        private static void AssertStablePreview(Transform root)
        {
            var image=root.GetComponentsInChildren<RawImage>().First(x=>x.name=="Actual weapon preview");
            var target=image.texture as RenderTexture;Assert.IsNotNull(target);Assert.IsTrue(target.IsCreated());
            Assert.AreEqual(4,target.antiAliasing);Assert.IsTrue(target.useMipMap);Assert.AreEqual(FilterMode.Trilinear,target.filterMode);
            var camera=UnityEngine.Object.FindObjectsOfType<Camera>().Single(x=>x.name=="Armory preview camera");
            Assert.IsFalse(camera.GetUniversalAdditionalCameraData().allowXRRendering);Assert.Less(camera.depth,Camera.main.depth);
            Assert.AreEqual(StereoTargetEyeMask.None,camera.stereoTargetEye);Assert.AreSame(target,camera.targetTexture);
            Assert.AreEqual(1,camera.backgroundColor.a);Assert.AreEqual(1<<30,camera.cullingMask);
            Assert.AreEqual(0,Camera.main.cullingMask&(1<<30));
            foreach(var model in UnityEngine.Object.FindObjectsOfType<TowerView>().Where(x=>x.name.StartsWith("Preview ")))
            {
                Assert.IsFalse(model.enabled);
                foreach(var surface in model.GetComponentsInChildren<MeshRenderer>())Assert.AreEqual("RoomSiege/PreviewSurface",surface.sharedMaterial.shader.name);
            }
        }
        [Test]public void EveryArtworkUsesMipmapsAndTrilinearFiltering()
        {
            foreach(string path in UnityEditor.AssetDatabase.FindAssets("t:Texture2D",new[]{"Assets/Meta/Runtime/Resources/Codex"}).Select(UnityEditor.AssetDatabase.GUIDToAssetPath))
            {
                var importer=(UnityEditor.TextureImporter)UnityEditor.AssetImporter.GetAtPath(path);
                Assert.IsTrue(importer.mipmapEnabled,path);Assert.AreEqual(FilterMode.Trilinear,importer.filterMode,path);
            }
        }
        private static void AssertPortrait(Transform root,string name,string skinId)
        {
            var image=root.GetComponentsInChildren<RawImage>().First(x=>x.name==name);
            Assert.AreSame(WeaponArtwork.Load("MG",skinId),image.texture,name);
        }
        private static void AssertSettlementLayout(Transform overlay)
        {
            Canvas.ForceUpdateCanvases();
            var page=overlay.Find("Settlement page");
            var elements=page.Cast<Transform>().Where(x=>x.gameObject.activeInHierarchy).Select(x=>(RectTransform)x).ToList();
            elements.Add((RectTransform)overlay.Find("Gold skin toggle"));
            var bounds=new System.Collections.Generic.List<Rect>();
            foreach(var rect in elements)
            {
                var corners=new Vector3[4];rect.GetWorldCorners(corners);
                var min=overlay.InverseTransformPoint(corners[0]);var max=overlay.InverseTransformPoint(corners[2]);
                var bound=Rect.MinMaxRect(min.x,min.y,max.x,max.y);
                Assert.GreaterOrEqual(bound.yMin,-244,rect.name);Assert.LessOrEqual(bound.yMax,124,rect.name);
                foreach(var other in bounds)Assert.IsFalse(bound.Overlaps(other),rect.name+" overlaps "+other);
                bounds.Add(bound);
                var label=rect.GetComponent<Text>()??rect.GetComponentInChildren<Text>();
                if(label!=null&&!string.IsNullOrEmpty(label.text))Assert.LessOrEqual(label.preferredHeight,label.rectTransform.rect.height+1,label.name+": "+label.text);
            }
        }
        [UnityTest]
        public IEnumerator WornSkinRefreshesEveryPortraitAndResultControlsRemainSeparate()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int slot=0;slot<2;slot++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+slot);PlayerPrefs.Save();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;float until=Time.realtimeSinceStartup+40;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady||MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var p=ProgressionController.Instance;Assert.IsTrue(p.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();
                Assert.IsTrue(p.Store.CompleteChallenge("portrait-funding",100000));
                Assert.IsTrue(p.Store.Buy("mg-legend",out _));Assert.IsTrue(p.Store.Buy("mg-elite",out _));
                var ui=p.GetComponent<ProgressionPanelUi>();var root=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                ui.SelectSkin("mg-legend");AssertStablePreview(root);Button(root,"穿戴").onClick.Invoke();Assert.IsFalse(Button(root,"已穿戴").interactable);
                var reloaded=new ProgressionStore(p.Catalog,new PlayerPrefsProgressionStorage());Assert.AreEqual("mg-legend",reloaded.Equipped("MG"));
                ui.ShowLoadout();AssertPortrait(root,"Weapon artwork MG","mg-legend");Capture("skin-ui-fix-01-loadout");
                Button(root,"换肤").onClick.Invoke();Assert.IsNotNull(UnityEngine.Object.FindObjectsOfType<TowerView>().First(x=>x.name=="Preview mg-legend").Tower.Skin);
                Button(root,"恢复基础全息").onClick.Invoke();ui.ShowLoadout();AssertPortrait(root,"Weapon artwork MG",null);
                ui.SelectSkin("mg-elite");Button(root,"穿戴").onClick.Invoke();
                typeof(ProgressionPanelUi).GetField("selectedWeapon",Hidden).SetValue(ui,0);ui.ShowWeapons();
                AssertPortrait(root,"Weapon artwork MG","mg-elite");Assert.IsNotNull(UnityEngine.Object.FindObjectsOfType<TowerView>().First(x=>x.name=="Preview mg-elite").Tower.Skin);Capture("skin-ui-fix-02-weapons");
                WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                var p=ProgressionController.Instance;var panels=WorldPanelHost.Instance;var towerUi=TowerHost.Instance.GetComponent<TowerPanelUi>();
                towerUi.Refresh();AssertPortrait(panels.Panels[1].transform,"Weapon portrait","mg-elite");
                var ui=p.GetComponent<ProgressionPanelUi>();ui.SelectSkin("mg-legend");Button(panels.Panels[0].transform.Find("Progression shop overlay"),"穿戴").onClick.Invoke();
                towerUi.Refresh();WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;TowerHost.Instance.GetComponent<TowerPanelUi>().Refresh();AssertPortrait(panels.Panels[1].transform,"Weapon portrait","mg-legend");Capture("skin-ui-fix-03-build");
                var tower=CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));Assert.AreEqual("mg-legend",tower.Skin.id);
                var ui=ProgressionController.Instance.GetComponent<ProgressionPanelUi>();ui.SelectSkin("mg-elite");Assert.IsFalse(Button(panels.Panels[0].transform.Find("Progression shop overlay"),"穿戴").interactable);
                panels.Navigation.CloseCurrent();GameManager.Instance.ApplyLeakDamage(100);
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var panels=WorldPanelHost.Instance;Assert.AreEqual(GameState.RESULT,GameManager.Instance.State);
                var overlay=panels.Panels[0].transform.Find("Meta overlay");AssertSettlementLayout(overlay);
                Assert.IsFalse(panels.Panels[0].transform.Find("Result actions page").gameObject.activeInHierarchy);Capture("skin-ui-fix-04-defeat");
                MetaController.Instance.OpenCodex(false);Assert.AreSame(WeaponArtwork.Load("MG","mg-legend"),overlay.Find("Codex page/Codex model image").GetComponent<RawImage>().texture);
                MetaController.Instance.OpenSettlement();Button(overlay,"军械商店").onClick.Invoke();Assert.IsFalse(overlay.gameObject.activeInHierarchy);
                WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
                typeof(WaveController).GetMethod("FinishCampaign",Hidden).Invoke(WaveController.Instance,new object[]{CampaignOutcome.Victory,"胜利 · 十波全部守住"});
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                AssertSettlementLayout(WorldPanelHost.Instance.Panels[0].transform.Find("Meta overlay"));Capture("skin-ui-fix-05-victory");
            }
            catch(Exception error){failure=error.ToString();}
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=previous;Assert.IsNull(failure,failure);
        }
        [UnityTest]
        public IEnumerator SkinStorePurchaseEquipRevertAndAllFourteenLegendaryWorldModelsWork()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int slot=0;slot<2;slot++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+slot);PlayerPrefs.Save();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            string failure=null;float until=Time.realtimeSinceStartup+40;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var p=ProgressionController.Instance;Assert.IsTrue(p.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();p.Store.CompleteChallenge("skin-funding",100000);
                var ui=p.GetComponent<ProgressionPanelUi>();var root=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                ui.SelectSkin("missile-legend");Assert.IsFalse(Button(root,"先在武器研发解锁此武器").interactable);Capture("skins54-01-locked");
                foreach(string id in WeaponRoster.Ids.Skip(4))Assert.IsTrue(p.Store.Buy(id,out _));
                ui.SelectSkin("missile-legend");Button(root,"购买 · 2400 碎片").onClick.Invoke();Assert.IsTrue(p.Store.Owns("missile-legend"));
                Button(root,"穿戴").onClick.Invoke();Assert.AreEqual("missile-legend",p.Store.Equipped("Missile"));Assert.IsFalse(Button(root,"已穿戴").interactable);Capture("skins54-02-missile-legend");
                Button(root,"恢复基础全息").onClick.Invoke();Assert.AreEqual("",p.Store.Equipped("Missile"));Assert.IsTrue(p.Store.Owns("missile-legend"));Button(root,"穿戴").onClick.Invoke();
                foreach(string weapon in WeaponRoster.Ids.Skip(4))
                {
                    string skin=weapon.ToLowerInvariant()+"-legend";if(!p.Store.Owns(skin))Assert.IsTrue(p.Store.Buy(skin,out _));Assert.IsTrue(p.Store.Equip(weapon,skin,out _));
                    ui.SelectSkin(skin);Assert.AreEqual(3,root.GetComponentsInChildren<RawImage>().Count(image=>image.name.StartsWith("Skin artwork ")));Assert.IsTrue(root.GetComponentsInChildren<RawImage>().Where(image=>image.name.StartsWith("Skin artwork ")).All(image=>image.texture!=null));
                    if(weapon=="Gravity"||weapon=="Mortar")Capture("skins54-03-"+weapon);
                }
                ui.SelectSkin("tesla-elite");Button(root,"上一组").onClick.Invoke();Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(t=>t.text=="零度湮灭"));Button(root,"下一组").onClick.Invoke();Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(t=>t.text=="导电先锋"));
                WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            foreach(string weapon in WeaponRoster.Ids.Skip(4))
            {
                if(failure!=null)break;
                try
                {
                    var p=ProgressionController.Instance;WaveController.Instance.ReturnToMenu();Assert.IsTrue(p.Store.SetLoadout(new[]{weapon,"MG","Frost","Trap"},out _));p.ConfigurationChanged();
                    GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();GameManager.Instance.AddCoins(1000);
                }
                catch(Exception error){failure=error.ToString();}
                yield return null;yield return null;
                if(failure==null)try
                {
                    var tower=CampaignRuntimeTests.BuildOpeningTower(weapon,new Vector3(1,0,1.4f));Assert.AreEqual(weapon.ToLowerInvariant()+"-legend",tower.Skin.id);
                    var view=UnityEngine.Object.FindObjectsOfType<TowerView>().First(v=>v.Tower==tower);Assert.IsNotNull(view.transform.Find("Skin signature 传说"));Assert.AreEqual(1,view.GetComponentsInChildren<Collider>().Length);
                    Assert.IsTrue(TowerHost.Instance.Placement.TryUpgrade(tower,3,out _));Assert.AreEqual(3,tower.Level);Assert.AreEqual(weapon.ToLowerInvariant()+"-legend",tower.Skin.id);
                    Assert.IsTrue(TowerHost.Instance.Placement.TrySell(tower,out _));
                }
                catch(Exception error){failure=weapon+": "+error;}
            }
            if(failure==null)try
            {
                WaveController.Instance.ReturnToMenu();var p=ProgressionController.Instance;Assert.IsTrue(p.Store.SetLoadout(new[]{"Missile","Mortar","Sniper","Flame"},out _));p.ConfigurationChanged();
                GameManager.Instance.SetState(GameState.SCAN);ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();GameManager.Instance.AddCoins(1000);CampaignRuntimeTests.BuildOpeningTower("Missile",new Vector3(1,0,1.4f));TowerHost.Instance.enabled=false;
            }
            catch(Exception error){failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var p=ProgressionController.Instance;WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);GameManager.Instance.SetCurrentWave(10);EnemyController.Instance.enabled=false;EnemyController.Instance.ClearEnemies();WaveController.Instance.enabled=false;
                var point=ScanController.Instance.CurrentGrid.SafeZoneCenter+Vector3.forward*2.4f;var shield=Spawn("Shielded",point);var boss=Spawn("Boss",point+Vector3.right*.15f);var targets=new ITowerTarget[]{shield,boss};
                Func<string,TowerInstance> create=id=>{var t=TowerInstance.CreatePreview(TowerHost.Instance.Catalog.GetTower(id),new GridCell("skin-test",SurfaceKind.Floor,Vector2Int.zero,point-Vector3.forward*1.2f));t.Skin=p.Catalog.Skin(id.ToLowerInvariant()+"-legend");return t;};
                float shieldBefore=shield.Shield;var missile=new TowerCombat(create("Missile"),_=>{});missile.Step(.01f,Vector3.zero,targets,null);Assert.AreEqual(shieldBefore,shield.Shield);missile.Step(.66f,Vector3.zero,targets,null);
                Assert.Less(shield.Shield,shieldBefore*.8f);Assert.AreEqual(.15f,shield.ArmorBreakFraction);Assert.AreEqual(.075f,boss.ArmorBreakFraction);
                var mortar=new TowerCombat(create("Mortar"),_=>{});mortar.Step(.01f,Vector3.zero,targets,null);mortar.Step(.91f,Vector3.zero,targets,null);Assert.AreEqual(.2f,shield.ArmorBreakFraction);Assert.AreEqual(.1f,boss.ArmorBreakFraction);
                WaveController.Instance.ReturnToMenu();
            }
            catch(Exception error){failure=error.ToString();}
            if(EnemyController.Instance!=null)EnemyController.Instance.enabled=true;if(TowerHost.Instance!=null)TowerHost.Instance.enabled=true;if(WaveController.Instance!=null)WaveController.Instance.enabled=true;
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=previous;Assert.IsNull(failure,failure);
        }
    }
}
