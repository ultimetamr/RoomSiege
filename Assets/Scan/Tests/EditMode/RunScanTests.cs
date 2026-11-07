using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace RoomSiege.Scan.Tests
{
    public static class RunScanTests
    {
        private static TestRunnerApi api;
        [MenuItem("RoomSiege/Scan/Run EditMode Tests")]
        public static void Run()
        {
            if(api!=null) return;
            api=ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Results());
            api.Execute(new ExecutionSettings(new Filter { testMode=TestMode.EditMode,assemblyNames=new[] {"RoomSiege.Scan.EditModeTests"} }));
        }
        private sealed class Results:ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }
            public void RunFinished(ITestResultAdaptor result)
            {
                var dir=Path.Combine(Path.GetDirectoryName(Application.dataPath),"TestResults"); Directory.CreateDirectory(dir);
                File.WriteAllText(Path.Combine(dir,"RoomSiege.Scan.EditMode.xml"),result.ToXml().OuterXml);
                Debug.Log($"[RoomSiege.Scan] EditMode: {result.PassCount} passed, {result.FailCount} failed, {result.SkipCount} skipped.");
                EditorApplication.delayCall+=()=>{if(api!=null) {api.UnregisterCallbacks(this); Object.DestroyImmediate(api); api=null;} };
            }
        }
    }
}
