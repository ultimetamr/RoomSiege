using System.IO;
using UnityEditor;
using UnityEngine;

namespace RoomSiege.UI.Editor
{
    public static class WorldPanelBuildTools
    {
        [MenuItem("RoomSiege/UI/Build UI APK")]
        public static void Build()
        {
            Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[]{"Assets/Scenes/RoomSiege_Start.unity"},target=BuildTarget.Android,
                locationPathName="Builds/RoomSiege-UI.apk",options=BuildOptions.Development
            });
            File.WriteAllText("Builds/ui-build-result.txt",$"{report.summary.result}\nErrors: {report.summary.totalErrors}\nSize: {report.summary.totalSize}\n");
            Debug.Log($"[RoomSiege.UI] Android build: {report.summary.result}, errors={report.summary.totalErrors}");
        }
    }
}
