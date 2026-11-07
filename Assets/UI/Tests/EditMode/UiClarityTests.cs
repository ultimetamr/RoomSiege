using System.IO;
using System.Reflection;
using NUnit.Framework;
using RoomSiege.Visuals;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.UI;

namespace RoomSiege.UI.Tests
{
    public sealed class UiClarityTests
    {
        [Test]
        public void HigherGlyphDensityPreservesPhysicalLayoutAndNestedPageSampling()
        {
            var root = new GameObject("World UI", typeof(RectTransform), typeof(Canvas));
            try
            {
                var canvas = root.GetComponent<Canvas>(); canvas.renderMode = RenderMode.WorldSpace;
                root.transform.localScale = Vector3.one * .001f;
                var page = new GameObject("Sorted page", typeof(RectTransform), typeof(Canvas));
                page.transform.SetParent(root.transform, false);
                var nested = page.GetComponent<Canvas>(); nested.overrideSorting = true;
                var node = new GameObject("Label", typeof(RectTransform), typeof(Text));
                node.transform.SetParent(page.transform, false);
                var text = node.GetComponent<Text>(); text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
                text.fontSize = 22; text.text = "RoomSiege 房间防线 123";
                text.rectTransform.sizeDelta = new Vector2(350, 60);
                Vector3 scale = root.transform.localScale; Vector2 size = text.rectTransform.sizeDelta;
                WorldUiSampling.Configure(canvas); WorldUiSampling.Configure(nested);
                Canvas.ForceUpdateCanvases();
                Assert.AreEqual(3f, canvas.GetComponent<CanvasScaler>().dynamicPixelsPerUnit);
                Assert.AreEqual(3f, text.pixelsPerUnit, "Sorted overlay must use the high-density font atlas too");
                var settings = text.GetGenerationSettings(size);
                Assert.AreEqual(3f, settings.scaleFactor); Assert.AreEqual(22, settings.fontSize);
                Assert.AreEqual(scale, root.transform.localScale); Assert.AreEqual(size, text.rectTransform.sizeDelta);
                Assert.LessOrEqual(text.preferredHeight, size.y);
                page.SetActive(false); page.SetActive(true); Canvas.ForceUpdateCanvases();
                Assert.AreEqual(3f, text.pixelsPerUnit, "Reopening a page must retain sampling density");
            }
            finally { Object.DestroyImmediate(root); }
        }

        [Test]
        public void RoundedPanelHasTransparentEdgeFringeWithinUnchangedHitRectangle()
        {
            var node = new GameObject("Rounded button", typeof(RectTransform), typeof(RoundedGlassImage));
            try
            {
                var image = node.GetComponent<RoundedGlassImage>(); image.rectTransform.sizeDelta = new Vector2(160, 48);
                image.color = new Color(.1f, .2f, .3f, .9f); image.raycastTarget = true;
                using (var vertices = new VertexHelper())
                {
                    typeof(RoundedGlassImage).GetMethod("OnPopulateMesh", BindingFlags.Instance | BindingFlags.NonPublic,
                        null, new[] { typeof(VertexHelper) }, null)
                        .Invoke(image, new object[] { vertices });
                    int transparent = 0, opaque = 0; var vertex = UIVertex.simpleVert;
                    for (int i = 0; i < vertices.currentVertCount; i++)
                    {
                        vertices.PopulateUIVertex(ref vertex, i);
                        if (vertex.color.a == 0) transparent++; else opaque++;
                        Assert.LessOrEqual(Mathf.Abs(vertex.position.x), 80.001f);
                        Assert.LessOrEqual(Mathf.Abs(vertex.position.y), 24.001f);
                    }
                    Assert.GreaterOrEqual(transparent, 48); Assert.Greater(opaque, transparent);
                    Assert.IsTrue(image.raycastTarget); Assert.AreSame(image, node.GetComponent<Image>());
                    Assert.AreEqual(new Vector2(160, 48), image.rectTransform.sizeDelta);
                }
            }
            finally { Object.DestroyImmediate(node); }
        }

        [Test]
        public void AndroidUsesTheExistingFourSamplePipeline()
        {
            StringAssert.Contains("Android: 2", File.ReadAllText("ProjectSettings/QualitySettings.asset"));
            var pipeline = AssetDatabase.LoadAssetAtPath<UniversalRenderPipelineAsset>("Assets/Settings/URP-HighFidelity.asset");
            Assert.IsNotNull(pipeline); Assert.AreEqual(4, pipeline.msaaSampleCount);
        }
    }
}
