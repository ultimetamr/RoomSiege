using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
#if ENABLE_PICO_OPENXR_SDK
using ByteDance.PICO.OpenXR;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features;
#endif

namespace RoomSiege.Scan.Editor
{
    [InitializeOnLoad]
    public static class ScanBuildTools
    {
        static ScanBuildTools() { EditorApplication.delayCall+=Configure; }

        [MenuItem("RoomSiege/Scan/Configure MR Features")]
        public static void Configure()
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling) return;
#if ENABLE_PICO_OPENXR_SDK
            var settings=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            if(settings==null) { Debug.LogError("[RoomSiege.Scan] Android OpenXR settings missing."); return; }
            Enable<PassthroughFeature>(settings);
            Enable<PICOSceneCapture>(settings);
            Enable<PICOSpatialAnchor>(settings);
            var camera=Camera.main;
            if(camera!=null && (camera.clearFlags!=CameraClearFlags.SolidColor || camera.backgroundColor.a!=0f || camera.allowHDR))
            {
                camera.clearFlags=CameraClearFlags.SolidColor;
                var clear=camera.backgroundColor; clear.a=0f; camera.backgroundColor=clear;
                camera.allowHDR=false;
                EditorUtility.SetDirty(camera);
                EditorSceneManager.MarkSceneDirty(camera.gameObject.scene);
                EditorSceneManager.SaveScene(camera.gameObject.scene);
            }
            AssetDatabase.SaveAssets();
            Debug.Log("[RoomSiege.Scan] OpenXR Passthrough / Scene Capture / Spatial Anchor enabled.");
#else
            Debug.LogError("[RoomSiege.Scan] ENABLE_PICO_OPENXR_SDK is required for real-room builds.");
#endif
        }

#if ENABLE_PICO_OPENXR_SDK
        private static void Enable<T>(OpenXRSettings settings) where T:OpenXRFeature
        {
            var feature=settings.GetFeature<T>();
            if(feature==null) throw new System.InvalidOperationException("Missing OpenXR feature "+typeof(T).Name);
            if(feature.enabled) return;
            feature.enabled=true; EditorUtility.SetDirty(feature); EditorUtility.SetDirty(settings);
        }
#endif

        [MenuItem("RoomSiege/Scan/Build Scan APK")]
        public static void Build()
        {
            Configure();
            Directory.CreateDirectory("Builds");
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions {
                scenes=new[] {"Assets/Scenes/RoomSiege_Start.unity"},
                target=BuildTarget.Android,
                locationPathName="Builds/RoomSiege-Scan.apk",
                options=BuildOptions.Development
            });
            File.WriteAllText("Builds/scan-build-result.txt",$"{report.summary.result}\nErrors: {report.summary.totalErrors}\nSize: {report.summary.totalSize}\n");
            Debug.Log($"[RoomSiege.Scan] Android build: {report.summary.result}, errors={report.summary.totalErrors}");
        }
    }
}
