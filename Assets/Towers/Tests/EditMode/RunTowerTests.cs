using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace RoomSiege.Towers.Tests
{
    public static class RunTowerTests
    {
        private static TestRunnerApi api;
        [MenuItem("RoomSiege/Towers/Run EditMode Tests")]
        public static void Run()
        {
            if(api!=null)return;
            api=ScriptableObject.CreateInstance<TestRunnerApi>();api.RegisterCallbacks(new Results());
            api.Execute(new ExecutionSettings(new Filter{testMode=TestMode.EditMode,assemblyNames=new[]{"RoomSiege.Towers.EditModeTests","RoomSiege.Data.EditModeTests","RoomSiege.Scan.EditModeTests","RoomSiege.UI.EditModeTests"}}));
        }
        private sealed class Results:ICallbacks
        {
            public void RunStarted(ITestAdaptor tests){}public void TestStarted(ITestAdaptor test){}public void TestFinished(ITestResultAdaptor result){}
            public void RunFinished(ITestResultAdaptor result)
            {
                Directory.CreateDirectory("TestResults");File.WriteAllText("TestResults/RoomSiege.Towers.EditMode.xml",result.ToXml().OuterXml);
                Debug.Log($"[RoomSiege.Towers] EditMode: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.");
                EditorApplication.delayCall+=()=>{if(api!=null){api.UnregisterCallbacks(this);Object.DestroyImmediate(api);api=null;}};
            }
        }
    }
}
