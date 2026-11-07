using System.Collections;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomSiege.Scan;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace RoomSiege.UI.Tests
{
    public sealed class GuardianBrandTests
    {
        [Test]
        public void AuthoredCoreStaysGroundedInsideExistingBuildClearanceAndHasNoColliders()
        {
            var parent = new GameObject("Core test anchor");
            try
            {
                Vector3 anchor = new Vector3(2, .7f, -3);
                var core = GuardCoreVisual.Create(parent.transform, anchor);
                var renderers = core.GetComponentsInChildren<MeshRenderer>();
                Assert.IsNotEmpty(renderers); Assert.IsEmpty(core.GetComponentsInChildren<Collider>());
                Assert.Greater(renderers.Sum(r => r.GetComponent<MeshFilter>().sharedMesh.triangles.Length / 3), 5000);
                var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
                Assert.AreEqual(anchor.y, bounds.min.y, .002f);
                Assert.Less(bounds.size.y, .5f); Assert.Greater(bounds.size.y, .3f);
                Assert.LessOrEqual(bounds.extents.x, GridModel.CoreBuildRadius);
                Assert.LessOrEqual(bounds.extents.z, GridModel.CoreBuildRadius);
                var chassis = core.transform.Find("Authored guardian core/GuardianChassis");
                Vector3 chassisHome = chassis.position;
                for (int frame = 0; frame < 600; frame++) core.Step(1f / 90);
                Assert.AreEqual(anchor, core.transform.position); Assert.AreEqual(chassisHome, chassis.position);
                Assert.Greater(Quaternion.Angle(Quaternion.identity,core.transform.Find("Authored guardian core/GuardianHeart").localRotation), 1);
                Assert.Greater(core.AnimationTime, 6);
                core.PulseDamage(); core.Step(float.NaN); core.Step(1f / 90);
                Assert.IsFalse(float.IsNaN(core.transform.position.x)); Assert.IsFalse(float.IsInfinity(core.transform.position.x));
                foreach (var renderer in renderers) foreach (var material in renderer.sharedMaterials)
                { Assert.IsNotNull(material.shader); Assert.IsTrue(material.shader.isSupported); }
            }
            finally { Object.DestroyImmediate(parent); }
        }

        [Test]
        public void PlayerAndLauncherBrandingReplaceDefaultUnityAssets()
        {
            var icon = AssetDatabase.LoadAssetAtPath<Texture2D>("Assets/Visuals/Branding/RoomSiegeIcon.png");
            Assert.IsNotNull(icon); Assert.GreaterOrEqual(icon.width, 1024);
            Assert.IsFalse(PlayerSettings.SplashScreen.showUnityLogo);
            Assert.IsTrue(PlayerSettings.SplashScreen.show);
            Assert.AreEqual(1, PlayerSettings.SplashScreen.logos.Length);
            Assert.AreSame(icon, PlayerSettings.SplashScreen.logos[0].logo.texture);
            Assert.AreSame(icon, PlayerSettings.virtualRealitySplashScreen);
            foreach (var kind in PlayerSettings.GetSupportedIconKinds(UnityEditor.Build.NamedBuildTarget.Android))
                foreach (var entry in PlayerSettings.GetPlatformIcons(UnityEditor.Build.NamedBuildTarget.Android, kind))
                    for (int layer = 0; layer < entry.minLayerCount; layer++) Assert.IsNotNull(entry.GetTexture(layer));
            Assert.IsNotNull(Resources.Load<Texture2D>("Branding/RoomSiegeEmblem"));
        }

        [Test]
        public void RenderActualCoreForVisualReview()
        {
            var root = new GameObject("Core render review"); var cameraNode = new GameObject("Core camera");
            var texture = new RenderTexture(1024, 1024, 24) { antiAliasing = 4 };
            var camera = cameraNode.AddComponent<Camera>(); var previous = RenderTexture.active;
            Texture2D pixels = null;
            try
            {
                var core = GuardCoreVisual.Create(root.transform, Vector3.zero);
                foreach (var child in root.GetComponentsInChildren<Transform>()) child.gameObject.layer = 30;
                camera.cullingMask = 1 << 30; camera.stereoTargetEye = StereoTargetEyeMask.None;
                camera.clearFlags = CameraClearFlags.SolidColor; camera.backgroundColor = new Color(.018f,.035f,.055f);
                camera.orthographic = true; camera.orthographicSize = .29f; camera.nearClipPlane = .01f; camera.farClipPlane = 5;
                camera.transform.position = new Vector3(.58f, .50f, -.7f); camera.transform.LookAt(new Vector3(0,.20f,0));
                camera.targetTexture = texture; texture.Create(); camera.Render(); RenderTexture.active = texture;
                pixels = new Texture2D(1024, 1024, TextureFormat.RGB24, false);
                pixels.ReadPixels(new Rect(0,0,1024,1024),0,0); pixels.Apply();
                Directory.CreateDirectory("TestResults/GuardianBrand");
                File.WriteAllBytes("TestResults/GuardianBrand/GuardianCore.png",pixels.EncodeToPNG());
            }
            finally
            {
                RenderTexture.active = previous; camera.targetTexture = null; texture.Release();
                Object.DestroyImmediate(texture); if(pixels!=null)Object.DestroyImmediate(pixels);
                Object.DestroyImmediate(cameraNode); Object.DestroyImmediate(root);
            }
        }
    }
}
