using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using RoomSiege.Visuals;

namespace RoomSiege.Scan
{
    // Visual-only authored model. It never participates in placement, raycasts or pathfinding.
    public sealed class GuardCoreVisual : MonoBehaviour
    {
        private readonly List<Material> materials = new List<Material>();
        private Transform heart, orbit;
        private MeshRenderer[] heartRenderers;
        private MaterialPropertyBlock pulse;
        private Vector3 heartHome;
        private float animationTime, damageRemaining;
        public float AnimationTime => animationTime;

        public static GuardCoreVisual Create(Transform parent, Vector3 worldPosition)
        {
            var root = new GameObject("Guard core"); root.transform.SetParent(parent, false);
            root.transform.position = worldPosition;
            var view = root.AddComponent<GuardCoreVisual>();
            view.pulse = new MaterialPropertyBlock();
            var body = new GameObject("Authored guardian core"); body.transform.SetParent(root.transform, false);
            foreach (var part in new[] { "GuardianChassis", "GuardianHeart", "GuardianOrbit" })
            {
                var asset = Resources.Load<GameObject>("GuardianCore/" + part);
                if (asset == null) throw new System.InvalidOperationException("Guardian core mesh missing: " + part);
                var node = Instantiate(asset, body.transform, false); node.name = part;
                if (part == "GuardianHeart") view.heart = node.transform;
                if (part == "GuardianOrbit") view.orbit = node.transform;
                foreach (var renderer in node.GetComponentsInChildren<MeshRenderer>())
                {
                    var source = renderer.sharedMaterials; var assigned = new Material[source.Length];
                    for (int i = 0; i < source.Length; i++) assigned[i] = view.Surface(source[i] != null ? source[i].name : "Metal");
                    renderer.sharedMaterials = assigned; renderer.shadowCastingMode = ShadowCastingMode.Off;
                    renderer.receiveShadows = false; renderer.lightProbeUsage = LightProbeUsage.Off;
                    renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
                }
            }
            var renderers = body.GetComponentsInChildren<MeshRenderer>();
            var bounds = renderers[0].bounds; foreach (var renderer in renderers) bounds.Encapsulate(renderer.bounds);
            float scale = .295f / Mathf.Max(.001f, Mathf.Max(bounds.size.x, bounds.size.z));
            Vector3 center = root.transform.InverseTransformPoint(bounds.center);
            body.transform.localScale = Vector3.one * scale;
            body.transform.localPosition = new Vector3(-center.x, -(center.y - bounds.extents.y), -center.z) * scale;
            view.heartHome = view.heart.localPosition;
            view.heartRenderers = view.heart.GetComponentsInChildren<MeshRenderer>();
            return view;
        }

        private Material Surface(string group)
        {
            var shader = Resources.Load<Shader>("RoomSiegeCoreSurface");
            if (shader == null) throw new System.InvalidOperationException("Guardian core shader missing");
            var material = new Material(shader) { name = "Guardian " + group };
            bool glow = group.Contains("Glow"), crystal = group.Contains("Crystal"), gold = group.Contains("Gold"), trim = group.Contains("Trim");
            Color color = glow ? RoomSiegeStyle.Friendly : crystal ? new Color(.12f,.58f,.77f) : gold ? RoomSiegeStyle.Gold :
                trim ? new Color(.58f,.71f,.8f) : group.Contains("Ceramic") ? new Color(.19f,.28f,.36f) :
                group.Contains("Inset") ? new Color(.015f,.03f,.045f) : new Color(.075f,.12f,.18f);
            material.SetColor("_Color", color);
            material.SetColor("_Emission", glow ? RoomSiegeStyle.Friendly * .5f : crystal ? RoomSiegeStyle.Friendly * .22f : Color.black);
            material.SetFloat("_Metallic", crystal ? .3f : .75f); materials.Add(material); return material;
        }

        public void PulseDamage() { damageRemaining = .65f; }
        public void Step(float deltaTime)
        {
            if (heart == null || orbit == null || float.IsNaN(deltaTime) || float.IsInfinity(deltaTime)) return;
            float dt = Mathf.Clamp(deltaTime, 0, .05f); animationTime += dt;
            damageRemaining = Mathf.Max(0, damageRemaining - dt);
            heart.localPosition = heartHome + Vector3.up * (Mathf.Sin(animationTime * 1.7f) * .006f);
            heart.localRotation = Quaternion.Euler(0, animationTime * 16, 0);
            orbit.localRotation = Quaternion.Euler(0, -animationTime * 24, 0);
            Color energy = damageRemaining > 0 ? RoomSiegeStyle.Enemy : RoomSiegeStyle.Friendly;
            pulse.SetColor("_Emission", energy * (.27f + .08f * Mathf.Sin(animationTime * 2.3f)));
            foreach (var renderer in heartRenderers) renderer.SetPropertyBlock(pulse);
        }
        private void Update() { Step(Time.unscaledDeltaTime); }
        private void OnDestroy()
        {
            foreach (var material in materials)
                if (material != null) { if (Application.isPlaying) Destroy(material); else DestroyImmediate(material); }
            materials.Clear();
        }
    }
}
