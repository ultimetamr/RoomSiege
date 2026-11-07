#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

// Unity compiles C# and assets once; the submission server packages this export with Gradle.
public static class RoomSiegeVibeLabBuild
{
    [MenuItem("RoomSiege/Export Vibe Lab Android Project")]
    public static void ExportAndroid()
    {
        const string output = "Builds/VibeLab/UnityExport";
        if (Directory.Exists(output))
            throw new InvalidOperationException($"Export destination already exists: {output}. Move it before exporting again.");

        var previousExport = EditorUserBuildSettings.exportAsGoogleAndroidProject;
        var previousDevelopment = EditorUserBuildSettings.development;
        var previousDebugging = EditorUserBuildSettings.allowDebugging;
        var previousProfiler = EditorUserBuildSettings.connectProfiler;
        try
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = true;
            EditorUserBuildSettings.development = false;
            EditorUserBuildSettings.allowDebugging = false;
            EditorUserBuildSettings.connectProfiler = false;
            Directory.CreateDirectory("Builds/VibeLab");
            var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions
            {
                scenes = new[] { "Assets/Scenes/RoomSiege_Start.unity" },
                target = BuildTarget.Android,
                locationPathName = output,
                options = BuildOptions.None
            });
            File.WriteAllText("Builds/VibeLab/export-result.txt",
                $"{report.summary.result}\nErrors: {report.summary.totalErrors}\nUnity: {Application.unityVersion}\n");
            if (report.summary.result != BuildResult.Succeeded)
                throw new InvalidOperationException($"Vibe Lab export failed: {report.summary.result} ({report.summary.totalErrors} errors).");
            Debug.Log($"[RoomSiege] Vibe Lab Release export succeeded: {output}");
        }
        finally
        {
            EditorUserBuildSettings.exportAsGoogleAndroidProject = previousExport;
            EditorUserBuildSettings.development = previousDevelopment;
            EditorUserBuildSettings.allowDebugging = previousDebugging;
            EditorUserBuildSettings.connectProfiler = previousProfiler;
        }
    }
}
#endif
