using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.XR.OpenXR;
using UnityEngine.XR.OpenXR.Features.Interactions;

namespace RoomSiege.Towers.Editor
{
    [InitializeOnLoad]
    public static class TowerBuildTools
    {
        private const string Source="Assets/Data/Config/roomsiege.balance.json";
        private const string Runtime="Assets/Towers/Runtime/Resources/RoomSiegeTowerBalance.json";
        static TowerBuildTools(){EditorApplication.delayCall+=Prepare;EditorApplication.update+=PollCommand;}
        [MenuItem("RoomSiege/Towers/Prepare Towers")]
        public static void Prepare()
        {
            if(EditorApplication.isCompiling||EditorApplication.isUpdating){EditorApplication.delayCall+=Prepare;return;}
            Directory.CreateDirectory(Path.GetDirectoryName(Runtime));
            var text=File.ReadAllText(Source);
            if(!File.Exists(Runtime)||File.ReadAllText(Runtime)!=text){File.WriteAllText(Runtime,text);AssetDatabase.ImportAsset(Runtime);}
            var settings=OpenXRSettings.GetSettingsForBuildTargetGroup(BuildTargetGroup.Android);
            var eye=settings!=null?settings.GetFeature<EyeGazeInteraction>():null;
            if(eye!=null&&!eye.enabled){eye.enabled=true;EditorUtility.SetDirty(eye);AssetDatabase.SaveAssets();}
            if(settings!=null)
            {
                var hand=settings.GetFeature<HandInteractionProfile>();
                if(hand!=null&&!hand.enabled){hand.enabled=true;EditorUtility.SetDirty(hand);}
                var simple=settings.GetFeature<KHRSimpleControllerProfile>();
                // PICO 6.1.1 accepts its controller profiles and EXT hand interaction, not KHR Simple.
                if(simple!=null&&simple.enabled){simple.enabled=false;EditorUtility.SetDirty(simple);}
            }
#if ENABLE_PICO_OPENXR_SDK
            var config=ByteDance.PICO.OpenXR.PXR_OpenXRProjectSetting.GetProjectConfig();
            if(!config.isEyeTracking){config.isEyeTracking=true;EditorUtility.SetDirty(config);AssetDatabase.SaveAssets();}
            if(!config.isHandTracking){config.isHandTracking=true;EditorUtility.SetDirty(config);}
            config.handTrackingSupportType=ByteDance.PICO.XR.HandTrackingSupport.ControllersAndHands;
            EditorUtility.SetDirty(config);
#endif
            AssetDatabase.SaveAssets();
        }
        [MenuItem("RoomSiege/Towers/Build Towers APK")]
        public static void Build()
        {
            Prepare();Directory.CreateDirectory("Builds");
            var options=BuildOptions.Development;
            if(System.Array.IndexOf(System.Environment.GetCommandLineArgs(),"-roomsiege-clean-build")>=0)
            {
                // Re-import variants after an interrupted or resource-limited shader compilation.
                AssetDatabase.ImportAsset("Packages/com.unity.render-pipelines.universal/Shaders/PostProcessing/LensFlareDataDriven.shader",ImportAssetOptions.ForceUpdate);
                options|=BuildOptions.CleanBuildCache|BuildOptions.StrictMode;
            }
            var report=BuildPipeline.BuildPlayer(new BuildPlayerOptions{scenes=new[]{"Assets/Scenes/RoomSiege_Start.unity"},target=BuildTarget.Android,locationPathName="Builds/RoomSiege-Towers.apk",options=options});
            File.WriteAllText("Builds/towers-build-result.txt",$"{report.summary.result}\nErrors: {report.summary.totalErrors}\nSize: {report.summary.totalSize}\n");
            Debug.Log($"[RoomSiege.Towers] Android build: {report.summary.result}, errors={report.summary.totalErrors}");
        }
        // Optional local editor command file for reproducible builds in an already open editor.
        private static void PollCommand()
        {
            const string path="Temp/RoomSiegeTowerCommand.txt";
            if(EditorApplication.isCompiling||EditorApplication.isUpdating||EditorApplication.isPlaying||!File.Exists(path))return;
            string command=File.ReadAllText(path).Trim();File.Delete(path);
            EditorApplication.delayCall+=()=>{if(command=="build")Build();else if(command=="tests")EditorApplication.ExecuteMenuItem("RoomSiege/Towers/Run EditMode Tests");else if(command=="armorytests")EditorApplication.ExecuteMenuItem("RoomSiege/UI/Run Armory Tests");else if(command=="uitests")EditorApplication.ExecuteMenuItem("RoomSiege/UI/Run EditMode Tests");else if(command=="roomtests")EditorApplication.ExecuteMenuItem("RoomSiege/UI/Run Room Route Tests");else if(command=="calibrationtest")EditorApplication.ExecuteMenuItem("RoomSiege/UI/Run Room Calibration Test");else if(command=="startuptests")EditorApplication.ExecuteMenuItem("RoomSiege/UI/Run Room Startup Tests");};
        }
    }
}
