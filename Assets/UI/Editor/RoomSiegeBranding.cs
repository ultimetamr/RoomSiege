using System;
using System.Linq;
using System.IO;
using UnityEditor;
using UnityEditor.Android;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace RoomSiege.UI.Editor
{
    // Unity 6 can leave the generated legacy app_icon.png at its default even
    // when every adaptive slot is populated. Enforce the fallback after export.
    public sealed class RoomSiegeAndroidIconExport : IPostGenerateGradleAndroidProject
    {
        public int callbackOrder => int.MaxValue;

        public void OnPostGenerateGradleAndroidProject(string path)
        {
            var launcherRes = Path.GetFullPath(Path.Combine(path, "../launcher/src/main/res"));
            Directory.CreateDirectory(Path.Combine(launcherRes, "mipmap-mdpi"));
            var source = Path.GetFullPath(RoomSiegeBranding.IconPath);
            if (!File.Exists(source)) throw new BuildFailedException("RoomSiege launcher icon missing");
            var icons = Directory.GetFiles(launcherRes, "app_icon.png", SearchOption.AllDirectories);
            if (icons.Length == 0)
                icons = new[] { Path.Combine(launcherRes, "mipmap-mdpi/app_icon.png") };
            foreach (var icon in icons) File.Copy(source, icon, true);
            Debug.Log("[RoomSiege.Branding] Enforced custom legacy Android icon in exported resources.");
        }
    }

    [InitializeOnLoad]
    public sealed class RoomSiegeBranding : IPreprocessBuildWithReport
    {
        public const string IconPath = "Assets/Visuals/Branding/RoomSiegeIcon.png";
        public const string AdaptivePath = "Assets/Visuals/Branding/RoomSiegeAdaptiveIcon.png";
        private const string BackgroundPath = "Assets/Visuals/Branding/AdaptiveBackground.asset";
        public int callbackOrder => -100;
        static RoomSiegeBranding() { EditorApplication.delayCall += PrepareWhenReady; }
        private static void PrepareWhenReady()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            { EditorApplication.delayCall += PrepareWhenReady; return; }
            if (AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath) != null) Prepare();
        }
        public void OnPreprocessBuild(BuildReport report) { Prepare(); }

        [MenuItem("RoomSiege/Branding/Apply Game Icon And Splash")]
        public static void Prepare()
        {
            Import(IconPath, true); Import(AdaptivePath, false);
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>(IconPath);
            var adaptive = AssetDatabase.LoadAssetAtPath<Texture2D>(AdaptivePath);
            var logo = AssetDatabase.LoadAssetAtPath<Sprite>(IconPath);
            if (icon == null || adaptive == null || logo == null) throw new InvalidOperationException("RoomSiege branding assets missing");
            var background = AssetDatabase.LoadAssetAtPath<Texture2D>(BackgroundPath);
            if (background == null)
            {
                background = new Texture2D(2, 2, TextureFormat.RGBA32, false) { name = "RoomSiege midnight background" };
                background.SetPixels(Enumerable.Repeat(new Color(.012f,.043f,.086f,1), 4).ToArray()); background.Apply();
                AssetDatabase.CreateAsset(background, BackgroundPath);
            }
            PlayerSettings.SetIconsForTargetGroup(BuildTargetGroup.Unknown,
                Enumerable.Repeat(icon, PlayerSettings.GetIconSizesForTargetGroup(BuildTargetGroup.Unknown).Length).ToArray());
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(NamedBuildTarget.Android))
            {
                var icons = PlayerSettings.GetPlatformIcons(NamedBuildTarget.Android, kind);
                foreach (var entry in icons)
                {
                    // Adaptive slots have background/foreground layers. Foreground includes
                    // safe padding so the emblem survives round/squircle launcher masks.
                    entry.SetTextures(entry.minLayerCount > 1 ? new[] { background, adaptive } : new[] { icon });
                }
                PlayerSettings.SetPlatformIcons(NamedBuildTarget.Android, kind, icons);
            }
            PlayerSettings.SplashScreen.show = true;
            PlayerSettings.SplashScreen.showUnityLogo = false;
            PlayerSettings.SplashScreen.backgroundColor = new Color(.012f,.043f,.086f,1);
            PlayerSettings.SplashScreen.overlayOpacity = 1;
            PlayerSettings.SplashScreen.animationMode = PlayerSettings.SplashScreen.AnimationMode.Static;
            PlayerSettings.SplashScreen.logos = new[] { PlayerSettings.SplashScreenLogo.Create(2f, logo) };
            PlayerSettings.virtualRealitySplashScreen = icon;
            AssetDatabase.SaveAssets();
            Debug.Log("[RoomSiege.Branding] Custom launcher icons, VR splash and game logo configured.");
        }

        private static void Import(string path, bool sprite)
        {
            var importer = AssetImporter.GetAtPath(path) as TextureImporter;
            if (importer == null) throw new InvalidOperationException("Missing branding texture: " + path);
            var type = sprite ? TextureImporterType.Sprite : TextureImporterType.Default;
            bool change = importer.textureType != type || importer.mipmapEnabled ||
                importer.textureCompression != TextureImporterCompression.Uncompressed || importer.wrapMode != TextureWrapMode.Clamp || importer.maxTextureSize != 2048;
            if (!change) return;
            importer.textureType = type; importer.spriteImportMode = SpriteImportMode.Single;
            importer.mipmapEnabled = false; importer.textureCompression = TextureImporterCompression.Uncompressed;
            importer.wrapMode = TextureWrapMode.Clamp; importer.filterMode = FilterMode.Bilinear; importer.maxTextureSize = 2048;
            importer.SaveAndReimport();
        }
    }
}
