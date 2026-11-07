#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEditor.XR.Management;
using UnityEditor.XR.Management.Metadata;
using UnityEditor.Events;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using UnityEngine.XR.Management;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.XR;
using Unity.XR.CoreUtils;
using UnityEngine.XR.Interaction.Toolkit;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.UI;
using RoomSiege;

[InitializeOnLoad]
public static class RoomSiegeBootstrap
{
    private const string ScenePath = "Assets/Scenes/RoomSiege_Start.unity";
    private const string RevisionKey = "RoomSiege.SkeletonRevision2";
    static RoomSiegeBootstrap() { EditorApplication.delayCall += Initialize; }

    private static void Initialize()
    {
        if (EditorApplication.isCompiling || EditorApplication.isUpdating)
        {
            EditorApplication.delayCall += Initialize;
            return;
        }
        if (SessionState.GetBool(RevisionKey, false)) return;
        EnsureProject();
        SessionState.SetBool(RevisionKey, true);
    }

    [MenuItem("RoomSiege/Configure Skeleton")]
    public static void EnsureProject()
    {
        ConfigurePlayer();
        ConfigureXR();
        var scene = EditorSceneManager.OpenScene(ScenePath);
        if (!scene.GetRootGameObjects().Any(root => root.GetComponentInChildren<RoomSiegeMenu>() != null))
            CreateScene();
        EditorBuildSettings.scenes = new[] { new EditorBuildSettingsScene(ScenePath, true) };
        AssetDatabase.SaveAssets();
        Debug.Log("[RoomSiege] Skeleton configured: Android / OpenXR / PICO.");
    }

    private static void ConfigurePlayer()
    {
        PlayerSettings.companyName = "RoomSiege";
        PlayerSettings.productName = "RoomSiege";
        PlayerSettings.bundleVersion = "0.1.0";
        PlayerSettings.SetApplicationIdentifier(BuildTargetGroup.Android, "com.roomsiege.mr");
        PlayerSettings.Android.minSdkVersion = AndroidSdkVersions.AndroidApiLevel29;
        PlayerSettings.Android.targetArchitectures = AndroidArchitecture.ARM64;
        PlayerSettings.SetScriptingBackend(BuildTargetGroup.Android, ScriptingImplementation.IL2CPP);
        PlayerSettings.SetUseDefaultGraphicsAPIs(BuildTarget.Android, false);
        PlayerSettings.SetGraphicsAPIs(BuildTarget.Android, new[] { UnityEngine.Rendering.GraphicsDeviceType.OpenGLES3 });
        RestoreAndroidQualityDefault();
        EditorUserBuildSettings.SwitchActiveBuildTarget(BuildTargetGroup.Android, BuildTarget.Android);
    }

    private static void RestoreAndroidQualityDefault()
    {
        // Use the same serialized map as Unity's Quality Settings inspector. A YAML-only
        // migration can be overwritten when the editor saves its loaded settings.
        var quality = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/QualitySettings.asset")[0];
        var settings = new SerializedObject(quality);
        var defaults = settings.FindProperty("m_PerPlatformDefaultQuality");
        for (int i = 0; i < defaults.arraySize; i++)
            if (defaults.GetArrayElementAtIndex(i).FindPropertyRelative("first").stringValue == "Android")
                return;
        int highFidelity = System.Array.IndexOf(QualitySettings.names, "High Fidelity");
        if (highFidelity < 0) return;
        int index = defaults.arraySize;
        defaults.InsertArrayElementAtIndex(index);
        var entry = defaults.GetArrayElementAtIndex(index);
        entry.FindPropertyRelative("first").stringValue = "Android";
        entry.FindPropertyRelative("second").intValue = highFidelity;
        settings.ApplyModifiedPropertiesWithoutUndo();
        EditorUtility.SetDirty(quality);
    }

    private static void ConfigureXR()
    {
        const string loaderType = "UnityEngine.XR.OpenXR.OpenXRLoader";
        var general = XRGeneralSettingsPerBuildTarget.XRGeneralSettingsForBuildTarget(BuildTargetGroup.Android);
        if (general == null || general.Manager == null) throw new System.InvalidOperationException("Android XR settings missing.");
        if (general.Manager.activeLoaders.Count != 1 || general.Manager.activeLoaders[0].GetType().FullName != loaderType)
        {
            foreach (var loader in general.Manager.activeLoaders.ToArray())
                XRPackageMetadataStore.RemoveLoader(general.Manager, loader.GetType().FullName, BuildTargetGroup.Android);
            if (!XRPackageMetadataStore.AssignLoader(general.Manager, loaderType, BuildTargetGroup.Android))
                throw new System.InvalidOperationException("Could not assign OpenXR loader.");
            EditorUtility.SetDirty(general.Manager);
        }
        if (!general.InitManagerOnStart) { general.InitManagerOnStart = true; EditorUtility.SetDirty(general); }
#if ENABLE_PICO_OPENXR_SDK
        // The official SDK Portal enables this feature group for OpenXR.
        ByteDance.PICO.XR.Editor.PXR_Utils.EnableOpenXRFeature<ByteDance.PICO.OpenXR.PICOFeature>();
        ByteDance.PICO.XR.Editor.PXR_Utils.EnableOpenXRFeature<ByteDance.PICO.OpenXR.OpenXRExtensions>();
        ByteDance.PICO.XR.Editor.PXR_Utils.EnableOpenXRFeature<ByteDance.PICO.OpenXR.Interactions.PICO4ControllerProfile>();
        ByteDance.PICO.XR.Editor.PXR_Utils.EnableOpenXRFeature<ByteDance.PICO.OpenXR.Interactions.PICO4UltraControllerProfile>();
        ByteDance.PICO.XR.Editor.PXR_Utils.EnableOpenXRFeature<ByteDance.PICO.OpenXR.Interactions.PICONeo3ControllerProfile>();
        ByteDance.PICO.XR.Editor.PXR_Utils.EnableOpenXRFeature<ByteDance.PICO.OpenXR.Interactions.PICOG3ControllerProfile>();
#endif
    }

    private static void CreateScene()
    {
        var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
        var manager = new GameObject("GameManager").AddComponent<GameManager>();
        var rig = new GameObject("CameraRig");
        var origin = rig.AddComponent<XROrigin>();
        var offset = new GameObject("Camera Offset");
        offset.transform.SetParent(rig.transform, false);
        var cameraObject = new GameObject("Main Camera", typeof(Camera), typeof(AudioListener));
        cameraObject.transform.SetParent(offset.transform, false);
        cameraObject.tag = "MainCamera";
        var camera = cameraObject.GetComponent<Camera>();
        camera.clearFlags = CameraClearFlags.SolidColor;
        camera.backgroundColor = new Color(0.04f, 0.06f, 0.10f, 1f);
        camera.nearClipPlane = 0.05f;
        origin.Camera = camera;
        origin.CameraFloorOffsetObject = offset;
        origin.RequestedTrackingOriginMode = XROrigin.TrackingOriginMode.Device;
        origin.CameraYOffset = 1.6f;
        offset.transform.localPosition = Vector3.up * 1.6f;
        AddTracking(cameraObject, "<XRHMD>/centerEyePosition", "<XRHMD>/centerEyeRotation");
        new GameObject("XR Interaction Manager", typeof(XRInteractionManager));
        CreateController(offset.transform, "Left");
        CreateController(offset.transform, "Right");

        var canvasObject = new GameObject("RoomSiegeWorldSpacePanel", typeof(RectTransform));
        canvasObject.transform.position = new Vector3(0f, 1.6f, 2f);
        canvasObject.transform.localScale = Vector3.one * 0.0025f;
        canvasObject.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 500f);
        var canvas = canvasObject.AddComponent<Canvas>();
        canvas.renderMode = RenderMode.WorldSpace;
        canvas.worldCamera = camera;
        canvasObject.AddComponent<CanvasScaler>();
        canvasObject.AddComponent<GraphicRaycaster>();
        canvasObject.AddComponent<TrackedDeviceGraphicRaycaster>();
        var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
        panel.transform.SetParent(canvasObject.transform, false);
        panel.GetComponent<RectTransform>().sizeDelta = new Vector2(700f, 500f);
        panel.GetComponent<Image>().color = new Color(0.08f, 0.12f, 0.18f, 0.94f);
        CreateText(panel.transform, "Title", "RoomSiege", new Vector2(0f, 130f), new Vector2(640f, 90f), 64);
        var start = CreateButton(panel.transform, "开始", new Vector2(-130f, -35f), manager.EnterBuildPlaceholder);
        CreateButton(panel.transform, "设置", new Vector2(130f, -35f), manager.EnterSettingsPlaceholder);
        var status = CreateText(panel.transform, "State", "状态：MENU", new Vector2(0f, -160f), new Vector2(650f, 100f), 26);
        canvasObject.AddComponent<RoomSiegeMenu>().Configure(manager, status, start);
        var events = new GameObject("EventSystem", typeof(EventSystem), typeof(XRUIInputModule));
        events.GetComponent<EventSystem>().firstSelectedGameObject = start.gameObject;
        EditorSceneManager.SaveScene(scene, ScenePath);
    }

    private static void AddTracking(GameObject go, string position, string rotation)
    {
        var driver = go.AddComponent<TrackedPoseDriver>();
        driver.positionInput = new InputActionProperty(new InputAction("Position", InputActionType.Value, position, expectedControlType: "Vector3"));
        driver.rotationInput = new InputActionProperty(new InputAction("Rotation", InputActionType.Value, rotation, expectedControlType: "Quaternion"));
        driver.ignoreTrackingState = true;
    }

    private static void CreateController(Transform parent, string hand)
    {
        var go = new GameObject(hand + " Controller Ray");
        go.transform.SetParent(parent, false);
        var device = "<XRController>{" + hand + "Hand}";
        AddTracking(go, device + "/devicePosition", device + "/deviceRotation");
        var ray = go.AddComponent<XRRayInteractor>();
        ray.enableUIInteraction = true;
        ray.maxRaycastDistance = 10f;
        var press = new XRInputButtonReader("UI Press", inputSourceMode: XRInputButtonReader.InputSourceMode.InputAction);
        press.inputActionPerformed.AddBinding(device + "/triggerPressed");
        press.inputActionValue.AddBinding(device + "/trigger");
        ray.uiPressInput = press;
    }

    private static Text CreateText(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize)
    {
        var go = new GameObject(name, typeof(RectTransform), typeof(Text));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = size;
        rect.anchoredPosition = position;
        var text = go.GetComponent<Text>();
        text.text = value;
        text.font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
        text.fontSize = fontSize;
        text.alignment = TextAnchor.MiddleCenter;
        text.color = Color.white;
        text.raycastTarget = false;
        return text;
    }

    private static Button CreateButton(Transform parent, string label, Vector2 position, UnityEngine.Events.UnityAction action)
    {
        var go = new GameObject(label, typeof(RectTransform), typeof(Image), typeof(Button));
        go.transform.SetParent(parent, false);
        var rect = go.GetComponent<RectTransform>();
        rect.sizeDelta = new Vector2(220f, 90f);
        rect.anchoredPosition = position;
        go.GetComponent<Image>().color = new Color(0.16f, 0.42f, 0.68f, 1f);
        var button = go.GetComponent<Button>();
        button.targetGraphic = go.GetComponent<Image>();
        UnityEventTools.AddPersistentListener(button.onClick, action);
        CreateText(go.transform, "Label", label, Vector2.zero, new Vector2(210f, 80f), 34);
        return button;
    }

    [MenuItem("RoomSiege/Build Android APK")]
    public static void BuildAndroid()
    {
        System.IO.Directory.CreateDirectory("Builds");
        var report = BuildPipeline.BuildPlayer(new BuildPlayerOptions {
            scenes = new[] { ScenePath }, target = BuildTarget.Android,
            locationPathName = "Builds/RoomSiege.apk", options = BuildOptions.Development
        });
        System.IO.File.WriteAllText("Builds/build-result.txt", $"{report.summary.result}\nErrors: {report.summary.totalErrors}\nSize: {report.summary.totalSize}\n");
        Debug.Log($"[RoomSiege] Android build: {report.summary.result}");
    }
}
#endif

