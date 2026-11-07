using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RoomSiege.Meta;
using RoomSiege.Progression;
using RoomSiege.Waves;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UI;

namespace RoomSiege.UI.Tests
{
    public sealed class GameUiSystemTests
    {
        private static readonly BindingFlags Hidden=BindingFlags.Instance|BindingFlags.NonPublic;
        private static void Set(ProgressionPanelUi ui,string field,int value)=>typeof(ProgressionPanelUi).GetField(field,Hidden).SetValue(ui,value);
        private static void Capture(string name)
        {
            Canvas.ForceUpdateCanvases();
            foreach(var camera in UnityEngine.Object.FindObjectsOfType<Camera>())if(camera.name=="Armory preview camera")camera.Render();
            CampaignRuntimeTests.CaptureUi("game-ui-"+name);
        }
        private static Rect Bounds(RectTransform rect,Transform root)
        {
            var corners=new Vector3[4];rect.GetWorldCorners(corners);
            var a=root.InverseTransformPoint(corners[0]);var b=root.InverseTransformPoint(corners[2]);
            return Rect.MinMaxRect(a.x,a.y,b.x,b.y);
        }
        private static void Check(Transform root,string context)
        {
            Canvas.ForceUpdateCanvases();
            var buttons=root.GetComponentsInChildren<Button>().Where(b=>b.gameObject.activeInHierarchy&&b.targetGraphic!=null&&b.targetGraphic.raycastTarget).ToArray();
            for(int i=0;i<buttons.Length;i++)
            {
                var bound=Bounds((RectTransform)buttons[i].transform,root);
                Assert.GreaterOrEqual(bound.xMin,-425,context+" / "+buttons[i].name);Assert.LessOrEqual(bound.xMax,425,context+" / "+buttons[i].name);
                Assert.GreaterOrEqual(bound.yMin,-245,context+" / "+buttons[i].name);Assert.LessOrEqual(bound.yMax,245,context+" / "+buttons[i].name);
                for(int j=0;j<i;j++)
                {
                    if(buttons[i].transform.IsChildOf(buttons[j].transform)||buttons[j].transform.IsChildOf(buttons[i].transform))continue;
                    Assert.IsFalse(bound.Overlaps(Bounds((RectTransform)buttons[j].transform,root)),context+" / buttons overlap: "+buttons[i].name+" / "+buttons[j].name);
                }
            }
            foreach(var text in root.GetComponentsInChildren<Text>())
            {
                Assert.AreEqual(3f,text.pixelsPerUnit,context+" / font sampling: "+text.name);
                if(text.gameObject.activeInHierarchy&&!string.IsNullOrEmpty(text.text))
                    Assert.LessOrEqual(text.preferredHeight,text.rectTransform.rect.height+1,context+" / text clipped: "+text.text+" / "+text.preferredHeight+" > "+text.rectTransform.rect.height);
            }
        }
        [UnityTest]
        public IEnumerator EveryCatalogPageFitsAndSharesNavigationWithoutBlockingWorldInteractions()
        {
            EditorSceneManager.OpenScene("Assets/Scenes/RoomSiege_Start.unity");CampaignRuntimeTests.SavePrefs();
            for(int slot=0;slot<2;slot++)PlayerPrefs.DeleteKey("RoomSiege.Progression.v1."+slot);PlayerPrefs.Save();
            bool previous=LogAssert.ignoreFailingMessages;LogAssert.ignoreFailingMessages=true;
            yield return new EnterPlayMode();LogAssert.ignoreFailingMessages=true;
            float until=Time.realtimeSinceStartup+40;string failure=null;
            while((ProgressionController.Instance==null||!ProgressionController.Instance.IsReady||MetaController.Instance==null||!MetaController.Instance.IsReady)&&Time.realtimeSinceStartup<until)yield return null;
            try
            {
                var p=ProgressionController.Instance;Assert.IsTrue(p.IsReady);WaveController.Instance.ReturnToMenu();WorldPanelHost.Instance.Tutorial.Cancel();
                p.Store.CompleteChallenge("ui-funding",100000);Assert.IsTrue(new PlaytestAccess{unlockAllContent=true,supplyStock=50}.Apply(p.Store));Capture("01-menu");
                var ui=p.GetComponent<ProgressionPanelUi>();var main=WorldPanelHost.Instance.Panels[0].transform;var root=main.Find("Progression shop overlay");
                for(int weapon=0;weapon<p.Catalog.weapons.Length;weapon++)
                {
                    Set(ui,"weaponPage",weapon/6);Set(ui,"selectedWeapon",weapon);ui.ShowWeapons();Check(root,"weapon "+p.Catalog.weapons[weapon]);if(weapon==0)Capture("02-weapons");
                }
                foreach(var skin in p.Catalog.skins){ui.SelectSkin(skin.id);Check(root,skin.id);}
                ui.SelectSkin("mg-legend");Capture("03-skins");
                for(int page=0;page<3;page++){Set(ui,"loadoutPage",page);ui.ShowLoadout();Check(root,"loadout "+page);}Capture("04-loadout");
                for(int page=0;page<4;page++){Set(ui,"supplyPage",page);typeof(ProgressionPanelUi).GetMethod("ShowTab",Hidden).Invoke(ui,new object[]{2});Check(root,"supplies "+page);if(page==0)Capture("05-supplies");}
                for(int page=0;page<4;page++){Set(ui,"carryPage",page);typeof(ProgressionPanelUi).GetMethod("ShowTab",Hidden).Invoke(ui,new object[]{3});Check(root,"carry "+page);}Capture("06-carry");
                typeof(ProgressionPanelUi).GetMethod("ShowTab",Hidden).Invoke(ui,new object[]{4});Check(root,"challenges");Capture("07-challenges");
                MetaController.Instance.OpenCodex(false);Check(main.Find("Meta overlay"),"tower codex");Capture("08-codex");
                for(int i=0;i<18;i++){Check(main.Find("Meta overlay"),"tower codex "+i);main.Find("Meta overlay/Codex page/下一项").GetComponent<Button>().onClick.Invoke();}
                MetaController.Instance.OpenCodex(true);Check(main.Find("Meta overlay"),"enemy codex");Capture("09-enemies");
                for(int i=0;i<5;i++){Check(main.Find("Meta overlay"),"enemy codex "+i);main.Find("Meta overlay/Codex page/下一项").GetComponent<Button>().onClick.Invoke();}
                main.Find("Main menu page/声音与布局").GetComponent<Button>().onClick.Invoke();Check(main.Find("Audio settings"),"settings");Capture("10-settings");
                WorldPanelHost.Instance.ShowHelp();Check(main.Find("Help page"),"help");Capture("11-help");
                WorldPanelHost.Instance.Navigation.CloseCurrent();
                GameManager.Instance.SetState(GameState.SCAN);RoomSiege.Scan.ScanController.Instance.UseSandbox();WaveController.Instance.StartNewCampaign();WorldPanelHost.Instance.Tutorial.Cancel();
            }
            catch(Exception error){Capture("failed-layout");failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var ui=ProgressionController.Instance.GetComponent<ProgressionPanelUi>();var root=WorldPanelHost.Instance.Panels[0].transform.Find("Progression shop overlay");
                Assert.IsNull(WorldPanelHost.Instance.BuildControls.Find("出战 / 补给"));
                var p=ProgressionController.Instance;Assert.IsTrue(p.Store.SetSupplies(Array.Empty<string>(),out _));
                CampaignRuntimeTests.BuildOpeningTower("MG",new Vector3(1,0,1.4f));
                ui.ShowSupplies();Check(root,"build inventory");Assert.IsFalse(root.GetComponentsInChildren<Button>().Any(b=>b.interactable&&b.GetComponentInChildren<Text>()?.text=="使用"));
                WorldPanelHost.Instance.Navigation.CloseCurrent();
            }
            catch(Exception error){Capture("failed-build-inventory");failure=error.ToString();}
            yield return null;yield return null;
            if(failure==null)try
            {
                var ui=ProgressionController.Instance.GetComponent<ProgressionPanelUi>();var main=WorldPanelHost.Instance.Panels[0].transform;var root=main.Find("Progression shop overlay");
                WaveController.Instance.StartWaveFromUi();Assert.IsTrue(WaveController.Instance.IsWaveActive);
                main.Find("Battle controls/使用道具").GetComponent<Button>().onClick.Invoke();Assert.IsTrue(ui.Visible);
                for(int page=0;page<4;page++)
                {
                    Set(ui,"carryPage",page);ui.ShowSupplies();Check(root,"battle inventory "+page);
                    Assert.IsTrue(root.GetComponentsInChildren<Button>().Where(b=>b.name=="使用").All(b=>b.interactable));
                    Assert.IsFalse(root.GetComponentsInChildren<Button>().Any(b=>b.name.Contains("购买")||b.name=="四武器阵容"||b.name=="战术补给"||b.name=="道具库存"));
                }
                ui.ShowWeapons();Assert.IsFalse(root.GetComponentsInChildren<Button>().Any(b=>b.name.StartsWith("Weapon ")));
                Set(ui,"carryPage",0);ui.ShowSupplies();Capture("12-battle-inventory");
                ProgressionController.Instance.SetStatus("核心生命已满，本次不消耗道具");
                Assert.IsTrue(root.GetComponentsInChildren<Text>().Any(t=>t.text=="核心生命已满，本次不消耗道具"),"Effect rejection feedback must remain visible");
                var context=(Text)typeof(ProgressionPanelUi).GetField("context",Hidden).GetValue(ui);
                var firstItem=root.Find("Shop content/Inventory item "+ProgressionController.Instance.Catalog.supplies[0].id);
                Assert.IsFalse(Bounds(context.rectTransform,root).Overlaps(Bounds((RectTransform)firstItem,root)),"Inventory rows must not cover the instruction");
                Assert.IsTrue(root.GetComponentsInChildren<Graphic>(true).Where(g=>g.transform.parent!=null&&g.transform.parent.name=="Game UI chrome").All(g=>!g.raycastTarget));
            }
            catch(Exception error){Capture("failed-battle-layout");failure=error.ToString();}
            yield return new ExitPlayMode();CampaignRuntimeTests.RestorePrefs();LogAssert.ignoreFailingMessages=previous;Assert.IsNull(failure,failure);
        }
    }
}
