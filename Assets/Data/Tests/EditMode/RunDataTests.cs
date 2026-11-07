using System.IO;
using UnityEditor;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

namespace RoomSiege.Data.Tests
{
    // Editor-only convenience command. It does not enter Play Mode or load a scene.
    public static class RunDataTests
    {
        private static TestRunnerApi api;

        [MenuItem("RoomSiege/Run Data EditMode Tests")]
        public static void Run()
        {
            if (api != null) return;
            api = ScriptableObject.CreateInstance<TestRunnerApi>();
            api.RegisterCallbacks(new Results());
            api.Execute(new ExecutionSettings(new Filter {
                testMode = TestMode.EditMode,
                assemblyNames = new[] { "RoomSiege.Data.EditModeTests" }
            }));
        }

        private sealed class Results : ICallbacks
        {
            public void RunStarted(ITestAdaptor tests) { }
            public void TestStarted(ITestAdaptor test) { }
            public void TestFinished(ITestResultAdaptor result) { }

            public void RunFinished(ITestResultAdaptor result)
            {
                var directory = Path.Combine(Path.GetDirectoryName(Application.dataPath), "TestResults");
                Directory.CreateDirectory(directory);
                var path = Path.Combine(directory, "RoomSiege.Data.EditMode.xml");
                File.WriteAllText(path, result.ToXml().OuterXml);
                Debug.Log($"[RoomSiege.Data] EditMode: {result.PassCount} passed, {result.FailCount} failed, " +
                          $"{result.SkipCount} skipped. Results: {path}");
                EditorApplication.delayCall += () => {
                    if (api == null) return;
                    api.UnregisterCallbacks(this);
                    Object.DestroyImmediate(api);
                    api = null;
                };
            }
        }
    }
}
