using System.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.UI.Tests
{
    public sealed class WorldPanelTests
    {
        private static Pose Head => new Pose(new Vector3(0,1.6f,0),Quaternion.identity);
        private static WorldPanelMotion Create()
        { var motion=new WorldPanelMotion(); motion.Step(Head,default,0,false); return motion; }

        [Test]
        public void InitialAnchorIsExactlyOnePointTwoMetresAndEightDegreesDown()
        {
            var motion=Create();
            Assert.That(Vector3.Distance(motion.Current.position,Head.position),Is.EqualTo(1.2f).Within(0.00001f));
            Assert.That(Vector3.Angle(motion.Current.position-Head.position,Vector3.forward),Is.EqualTo(8f).Within(0.001f));
            Assert.That(motion.Current.position.y,Is.LessThan(Head.position.y));
            Assert.That(WorldPanelMotion.Valid(motion.Current),Is.True);
        }
        [Test]
        public void HeadTranslationUsesExactlyPointTwelveLerpPerFrame()
        {
            var motion=Create(); var start=motion.Current;
            var head=new Pose(Head.position+Vector3.right*0.2f,Head.rotation);
            var result=motion.Step(head,start,1f/60,false);
            Assert.That(Vector3.Distance(result.position,start.position+Vector3.right*0.024f),Is.LessThan(0.00001f));
        }
        [Test]
        public void SmallHeadRotationNoiseDoesNotShakeThePanels()
        {
            var motion=Create(); var start=motion.Current;
            for(int i=0;i<120;i++)
            {
                var head=new Pose(Head.position,Quaternion.Euler(0,i%2==0?5:-5,0));
                motion.Step(head,motion.Current,1f/60,false);
            }
            Assert.That(Vector3.Distance(start.position,motion.Current.position),Is.LessThan(0.00001f));
            Assert.That(motion.Turning,Is.False);
        }
        [Test]
        public void NinetyDegreeTurnRecentresGraduallyWithoutTeleporting()
        {
            var motion=Create(); var head=new Pose(Head.position,Quaternion.Euler(0,90,0));
            var first=motion.Step(head,motion.Current,1f/60,false);
            Assert.That(motion.Turning,Is.True);
            Assert.That(Quaternion.Angle(first.rotation,WorldPanelMotion.DefaultHeading(head)),Is.GreaterThan(80f));
            float largestStep=0;
            for(int i=0;i<180;i++)
            {
                var previous=motion.Current;
                motion.Step(head,previous,1f/60,false);
                largestStep=Mathf.Max(largestStep,Vector3.Distance(previous.position,motion.Current.position));
            }
            Assert.That(largestStep,Is.LessThan(0.04f));
            Assert.That(Quaternion.Angle(motion.Current.rotation,WorldPanelMotion.DefaultHeading(head)),Is.LessThan(2.1f));
            Assert.That(Vector3.Distance(motion.Current.position,head.position+WorldPanelMotion.DefaultHeading(head)*Vector3.forward*1.2f),Is.LessThan(0.05f));
        }
        [TestCase(0.55f,30)] [TestCase(0.55f,60)] [TestCase(0.55f,90)]
        [TestCase(2.8f,30)] [TestCase(2.8f,60)] [TestCase(2.8f,90)]
        public void NearAndFarPanelsReturnWithinOneSecond(float distance,int fps)
        {
            var motion=Create();
            var external=new Pose(Head.position+WorldPanelMotion.DefaultHeading(Head)*Vector3.forward*distance,motion.Current.rotation);
            var previous=external;
            for(int i=0;i<fps;i++)
            {
                var next=motion.Step(Head,previous,1f/fps,false);
                Assert.That(Vector3.Distance(next.position,previous.position),Is.LessThan(0.15f));
                previous=next;
            }
            Assert.That(Vector3.Distance(motion.Current.position,Head.position),Is.EqualTo(1.2f).Within(0.0001f));
            Assert.That(motion.Returning,Is.False);
            Assert.That(motion.LastResetReason,Is.EqualTo(PanelResetReason.Distance));
        }
        [Test]
        public void NanPoseRecoversFromLastGoodPoseWithoutPropagatingOrJumping()
        {
            var motion=Create(); var last=motion.Current;
            var broken=new Pose(new Vector3(float.NaN,0,0),Quaternion.identity);
            var result=motion.Step(Head,broken,1f/60,false);
            Assert.That(WorldPanelMotion.Valid(result),Is.True);
            Assert.That(Vector3.Distance(last.position,result.position),Is.LessThan(0.00001f));
            Assert.That(motion.LastResetReason,Is.EqualTo(PanelResetReason.InvalidPose));
        }
        [Test]
        public void ZeroQuaternionAndInvalidHeadNeverPoisonTheRenderedPose()
        {
            var motion=Create();
            motion.Step(Head,new Pose(motion.Current.position,new Quaternion(0,0,0,0)),1f/60,false);
            Assert.That(WorldPanelMotion.Valid(motion.Current),Is.True);
            motion.Step(new Pose(Vector3.positiveInfinity,Quaternion.identity),motion.Current,1f/60,false);
            Assert.That(WorldPanelMotion.Valid(motion.Current),Is.True);
        }
        [Test]
        public void PersistentOcclusionDoesNotRestartRecoveryEveryFrame()
        {
            var motion=Create(); int count=motion.ResetCount;
            for(int i=0;i<120;i++) motion.Step(Head,motion.Current,1f/60,true);
            Assert.That(motion.ResetCount,Is.EqualTo(count+1));
            Assert.That(motion.Returning,Is.False);
            Assert.That(motion.LastResetReason,Is.EqualTo(PanelResetReason.Occlusion));
        }
        [Test]
        public void PauseAndResumeFadeMonotonicallyToThirtyPercentAndBack()
        {
            float alpha=1;
            for(int i=0;i<15;i++) {float next=WorldPanelMotion.Fade(alpha,true,1f/60); Assert.That(next,Is.LessThanOrEqualTo(alpha));alpha=next;}
            Assert.That(alpha,Is.EqualTo(0.3f).Within(0.00001f));
            for(int i=0;i<15;i++) {float next=WorldPanelMotion.Fade(alpha,false,1f/60); Assert.That(next,Is.GreaterThanOrEqualTo(alpha));alpha=next;}
            Assert.That(alpha,Is.EqualTo(1f).Within(0.00001f));
            Assert.That(WorldPanelMotion.Fade(float.NaN,true,1f/60),Is.EqualTo(0.3f));
        }
        [Test]
        public void InteractionTargetsAreSmoothedAndInvalidTargetsIgnored()
        {
            var motion=Create(); var start=motion.Current;
            motion.SetInteractionTarget(start.position+Vector3.right*0.5f);
            motion.Step(Head,start,1f/60,false);
            Assert.That(motion.Current.position.x,Is.EqualTo(0.06f).Within(0.00001f));
            motion.SetInteractionTarget(Vector3.positiveInfinity);
            motion.Step(Head,motion.Current,1f/60,false);
            Assert.That(WorldPanelMotion.Valid(motion.Current),Is.True);
        }

        [Test]
        public void RayHighlightRestoresSelectedSurfaceAndIgnoresDisabledHover()
        {
            var go=new GameObject("Highlight",typeof(RectTransform),typeof(CanvasRenderer),typeof(Image),typeof(Button));
            var events=new GameObject("Highlight events",typeof(UnityEngine.EventSystems.EventSystem));
            try
            {
                var button=go.GetComponent<Button>();button.targetGraphic=go.GetComponent<Image>();
                RoomSiege.Visuals.RoomSiegeStyle.StyleButton(button);
                var highlight=go.GetComponent<RoomSiege.Visuals.RayButtonHighlight>();
                if(go.GetComponent<Outline>()==null)typeof(RoomSiege.Visuals.RayButtonHighlight).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(highlight,null);
                var selected=new Color(.05f,.34f,.43f,.94f);highlight.SetIdleColor(selected);
                var data=new UnityEngine.EventSystems.PointerEventData(events.GetComponent<UnityEngine.EventSystems.EventSystem>()){pointerId=12};
                highlight.OnPointerEnter(data);Assert.AreNotEqual(selected,button.targetGraphic.color);
                highlight.OnPointerExit(data);Assert.AreEqual(selected,button.targetGraphic.color);
                button.interactable=false;highlight.OnPointerEnter(data);
                Assert.IsFalse(go.GetComponent<Outline>().enabled,"Unaffordable cards never suggest an actionable hover");
                button.interactable=true;highlight.OnPointerExit(data);Assert.AreEqual(selected,button.targetGraphic.color);
            }
            finally{Object.DestroyImmediate(go);Object.DestroyImmediate(events);}
        }
        [Test]
        public void BattleHudFitsAboveTheActionAndHidesForTutorialAndPlacement()
        {
            var cameraObject=new GameObject("HUD Test Head",typeof(Camera));
            var managerObject=new GameObject("HUD Test Manager");var hostObject=new GameObject("HUD Test Host");
            try
            {
                var game=managerObject.AddComponent<GameManager>();
                if(GameManager.Instance!=game)typeof(GameManager).GetMethod("Awake",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(game,null);
                var host=hostObject.AddComponent<WorldPanelHost>();host.Build(cameraObject.GetComponent<Camera>());
                game.ResetCampaign(150,GameState.BUILD);host.Tutorial.Cancel();host.RefreshTutorial();
                var top=host.Panels[3].transform;
                string[] names={"Progress ring","Progress label","HUD life","HUD gold"};
                float previousRight=float.NegativeInfinity;
                foreach(string name in names)
                {
                    var rect=(RectTransform)top.Find(name);
                    Assert.IsTrue(rect.gameObject.activeSelf,name);
                    float left=rect.anchoredPosition.x-rect.sizeDelta.x*.5f;
                    Assert.Greater(left,previousRight,name+" must not overlap preceding information");
                    previousRight=rect.anchoredPosition.x+rect.sizeDelta.x*.5f;
                    Assert.LessOrEqual(previousRight,385);
                }
                Assert.LessOrEqual(((RectTransform)top.Find("HUD glass")).sizeDelta.y,90,"Compact HUD leaves room visible");
                host.Tutorial.Begin();host.RefreshTutorial();
                Assert.IsTrue(top.Find("First run tutorial").gameObject.activeSelf);
                foreach(string name in names)Assert.IsFalse(top.Find(name).gameObject.activeSelf,"Tutorial replaces HUD");
                host.Tutorial.Cancel();host.RefreshTutorial();host.SetPlacementMode(true);
                Assert.AreEqual(0,host.Panels[3].GetComponent<CanvasGroup>().alpha);
                Assert.IsFalse(host.Panels[3].GetComponent<CanvasGroup>().blocksRaycasts);
            }
            finally{Object.DestroyImmediate(hostObject);Object.DestroyImmediate(cameraObject);Object.DestroyImmediate(managerObject);}
        }
        [Test]
        public void FourWorldCanvasesKeepTheirLayoutAndRecoverDisabledOrZeroScalePanels()
        {
            var cameraObject=new GameObject("UI Test Head",typeof(Camera));
            var hostObject=new GameObject("UI Test Host");
            try
            {
                cameraObject.transform.SetPositionAndRotation(Head.position,Head.rotation);
                var host=hostObject.AddComponent<WorldPanelHost>();host.Build(cameraObject.GetComponent<Camera>());
                Assert.That(host.Panels.Length,Is.EqualTo(4));
                Assert.That(host.transform.parent,Is.Null);
                Assert.That(host.Panels.All(p=>p.renderMode==RenderMode.WorldSpace),Is.True);
                Assert.That(host.GetComponentsInChildren<ProgressRingGraphic>(true).Length,Is.EqualTo(1));
                Assert.That(host.GetComponentsInChildren<Graphic>().All(g=>g.canvasRenderer!=null),Is.True);
                Assert.That(host.Panels[0].transform.Find("Main menu page").gameObject.activeSelf,Is.True);
                Assert.That(host.Panels[0].transform.Find("Build controls").gameObject.activeSelf,Is.False);
                Assert.That(host.Panels[1].GetComponentsInChildren<Graphic>().Length,Is.EqualTo(0));
                var panel=host.Panels[1]; Vector3 initial=panel.transform.localPosition;
                panel.enabled=false;panel.gameObject.SetActive(false);panel.transform.localScale=Vector3.zero;
                panel.transform.localPosition=Vector3.one*100;
                host.Follower.Tick(1f/60);
                Assert.That(panel.enabled && panel.gameObject.activeSelf,Is.True);
                Assert.That(panel.transform.localPosition,Is.EqualTo(initial));
                Assert.That(panel.transform.localScale,Is.EqualTo(Vector3.one*0.001f));
                Assert.That(host.GetComponentsInChildren<Graphic>().All(g=>g.material.shader.name=="RoomSiege/WorldPanel"),Is.True);
            }
            finally { Object.DestroyImmediate(hostObject); Object.DestroyImmediate(cameraObject); }
        }
        [Test]
        public void RealColliderOcclusionTriggersRecoveryButIgnoresTriggers()
        {
            var cameraObject=new GameObject("UI Test Head",typeof(Camera));
            var hostObject=new GameObject("UI Test Host");
            var obstruction=GameObject.CreatePrimitive(PrimitiveType.Cube);
            try
            {
                cameraObject.transform.SetPositionAndRotation(Head.position,Head.rotation);
                var host=hostObject.AddComponent<WorldPanelHost>();host.Build(cameraObject.GetComponent<Camera>());
                obstruction.transform.position=Vector3.Lerp(Head.position,host.transform.position,0.5f);
                obstruction.transform.localScale=Vector3.one*0.2f;
                var box=obstruction.GetComponent<BoxCollider>();box.isTrigger=true;
                Physics.SyncTransforms();host.Follower.Tick(1f/60);
                Assert.That(host.Follower.IsOccluded,Is.False);
                box.isTrigger=false;Physics.SyncTransforms();host.Follower.Tick(1f/60);
                Assert.That(host.Follower.IsOccluded,Is.True);
                Assert.That(host.Follower.Motion.LastResetReason,Is.EqualTo(PanelResetReason.Occlusion));
            }
            finally { Object.DestroyImmediate(obstruction);Object.DestroyImmediate(hostObject);Object.DestroyImmediate(cameraObject); }
        }
        [Test]
        public void ComponentFadeStillCompletesWhenTimeScaleIsZero()
        {
            var cameraObject=new GameObject("UI Test Head",typeof(Camera));
            var hostObject=new GameObject("UI Test Host");float timeScale=Time.timeScale;
            try
            {
                cameraObject.transform.SetPositionAndRotation(Head.position,Head.rotation);
                var host=hostObject.AddComponent<WorldPanelHost>();host.Build(cameraObject.GetComponent<Camera>());
                Time.timeScale=0;
                for(int i=0;i<15;i++)host.Follower.Tick(1f/60);
                Assert.That(host.Follower.Group.alpha,Is.EqualTo(0.3f).Within(0.00001f));
                Time.timeScale=timeScale;
                for(int i=0;i<15;i++)host.Follower.Tick(1f/60);
                Assert.That(host.Follower.Group.alpha,Is.EqualTo(1f).Within(0.00001f));
            }
            finally { Time.timeScale=timeScale;Object.DestroyImmediate(hostObject);Object.DestroyImmediate(cameraObject); }
        }
    }
}
