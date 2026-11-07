using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    [InitializeOnLoad]
    public static class RunUiTests
    {
        private static TestRunnerApi api;
        private const string RunningKey="RoomSiege.UI.Tests.Running";
        static RunUiTests()
        {
            if(SessionState.GetBool(RunningKey,false)) Register();
        }
        private static void Register()
        {
            api=ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Results());
        }
        [MenuItem("RoomSiege/UI/Run EditMode Tests")]
        public static void Run()=>RunAssemblies("RoomSiege.UI.EditModeTests","RoomSiege.Towers.EditModeTests","RoomSiege.Data.EditModeTests","RoomSiege.Scan.EditModeTests");
        [MenuItem("RoomSiege/UI/Run Room Route Tests")]
        public static void RunRoomRoutes()=>RunAssemblies("RoomSiege.Towers.EditModeTests","RoomSiege.Scan.EditModeTests");
        [MenuItem("RoomSiege/UI/Run Room Calibration Test")]
        public static void RunCalibration()=>RunFilter(new Filter {testMode=TestMode.EditMode,
            testNames=new[]{"RoomSiege.UI.Tests.CampaignRuntimeTests.RealFloorWithoutDetectedDoorCanCalibrateTrackedRayAndEnterBuild"}});
        [MenuItem("RoomSiege/UI/Run Room Startup Tests")]
        public static void RunStartup()=>RunFilter(new Filter {testMode=TestMode.EditMode,testNames=new[]{
            "RoomSiege.UI.Tests.CampaignRuntimeTests.RealRoomScanAutomaticallyPreparesEntranceAndStartsCampaign",
            "RoomSiege.UI.Tests.CampaignRuntimeTests.VisualPolishCapturesRealPagesAndPreservesPlacementAndManagementFlow",
            "RoomSiege.UI.Tests.CampaignRuntimeTests.FirstRunLessonUsesRealPlacementAndUpgradeWithoutBlockingConstruction",
            "RoomSiege.UI.Tests.CampaignRuntimeTests.FollowModePreservesPaidTowerAndGeometryThenFixedModeStopsMovement",
            "RoomSiege.UI.Tests.CampaignRuntimeTests.RealFloorWithoutDetectedDoorCanCalibrateTrackedRayAndEnterBuild"}});
        [MenuItem("RoomSiege/UI/Run Armory Tests")]
        public static void RunArmory()=>RunFilter(new Filter {testMode=TestMode.EditMode,testNames=new[]{"RoomSiege.UI.Tests.ArmoryExpansionTests","RoomSiege.UI.Tests.ArmoryRuntimeTests","RoomSiege.UI.Tests.ProgressionTests","RoomSiege.UI.Tests.ProgressionRuntimeTests"}});
        private static void RunAssemblies(params string[] assemblies)
            =>RunFilter(new Filter {testMode=TestMode.EditMode,assemblyNames=assemblies});
        private static void RunFilter(Filter filter)
        {
            if(SessionState.GetBool(RunningKey,false))return;
            SessionState.SetBool(RunningKey,true);
            Register();
            api.Execute(new ExecutionSettings(filter));
        }
        private sealed class Results:ICallbacks
        {
            public void RunStarted(ITestAdaptor tests){} public void TestStarted(ITestAdaptor test){} public void TestFinished(ITestResultAdaptor result){}
            public void RunFinished(ITestResultAdaptor result)
            {
                SessionState.SetBool(RunningKey,false);
                Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/RoomSiege.UI.EditMode.xml",result.ToXml().OuterXml);
                Debug.Log($"[RoomSiege.UI] EditMode: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.");
                EditorApplication.delayCall+=()=>{if(api!=null){api.UnregisterCallbacks(this);Object.DestroyImmediate(api);api=null;}};
            }
        }
    }
}
