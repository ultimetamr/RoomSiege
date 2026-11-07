using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.Controls;
using UnityEngine.InputSystem.XR;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.Inputs.Readers;
using UnityEngine.XR.Interaction.Toolkit.Interactors;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.UI
{
    // One pointer per hand. Pose and buttons always come from the SAME tracked OpenXR device.
    // The standard XRI module still owns hover, click, drag and grab event dispatch.
    [DefaultExecutionOrder(-20000)]
    public sealed class WorldInputPointer : MonoBehaviour
    {
        public XRRayInteractor Interactor { get; private set; }
        public bool Tracked { get; private set; }
        public bool PressedThisFrame { get; private set; }
        public int LastPressFrame { get; private set; } = -1;
        private bool left, pressed, gripped;
        private static readonly HashSet<WorldInputPointer> pointers = new HashSet<WorldInputPointer>();
        private readonly UiPressGate uiGate = new UiPressGate();
        private bool deliveredPress;

        public static bool AnyPressHeld
        {
            get
            {
                foreach (var pointer in pointers)
                    if (pointer != null && pointer.isActiveAndEnabled && pointer.uiGate.IsHeld) return true;
                return Mouse.current?.leftButton.isPressed == true;
            }
        }

        public static void ConsumePagePress()
        {
            foreach (var pointer in pointers)
            {
                if (pointer == null) continue;
                pointer.uiGate.BlockUntilRelease();
                pointer.PressedThisFrame = false;
                if (pointer.press != null) SetReader(pointer.press, false, pointer.deliveredPress);
                pointer.deliveredPress = false;
            }
        }
        private int deviceId = -1;
        private string lastHit;
        private float nextHealth;
        private XRInputButtonReader press, grip;
        private LineRenderer line;
        private Material material;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name != "RoomSiege_Start") return;
            foreach (var ray in FindObjectsOfType<XRRayInteractor>())
                if (ray.GetComponent<WorldInputPointer>() == null) ray.gameObject.AddComponent<WorldInputPointer>();
        }

        private void Awake()
        {
            pointers.Add(this);
            Interactor = GetComponent<XRRayInteractor>();
            left = name.Contains("Left");
            var driver = GetComponent<UnityEngine.InputSystem.XR.TrackedPoseDriver>();
            if (driver != null) driver.enabled = false;
            Interactor.enabled = false;
            press = new XRInputButtonReader("RoomSiege UI press", inputSourceMode: XRInputButtonReader.InputSourceMode.ManualValue);
            grip = new XRInputButtonReader("RoomSiege grab", inputSourceMode: XRInputButtonReader.InputSourceMode.ManualValue);
            Interactor.uiPressInput = press;
            Interactor.selectInput = grip;
            Interactor.enableUIInteraction = true;
            Interactor.maxRaycastDistance = 10f;
            Interactor.lineType = XRRayInteractor.LineType.StraightLine;
            material = new Material(Resources.Load<Shader>("RoomSiegeScanOverlay"));
            material.SetColor("_Color", Color.white);
            var visual = new GameObject("Visible MR pointer");
            visual.transform.SetParent(transform, false);
            line = visual.AddComponent<LineRenderer>();
            line.sharedMaterial = material;
            line.positionCount = 2;
            line.startWidth = .0025f; line.endWidth = .001f;
            line.useWorldSpace = true; line.enabled = false;
        }

        private void Update()
        {
            var device = FindDevice();
            Tracked = device != null;
            bool down = false, grab = false;
            if (Tracked)
            {
                var position = Control<Vector3Control>(device, "pointerPosition") ?? Control<Vector3Control>(device, "devicePosition");
                var rotation = Control<QuaternionControl>(device, "pointerRotation") ?? Control<QuaternionControl>(device, "deviceRotation");
                var p = position.ReadValue(); var q = rotation.ReadValue();
                Tracked = WorldPanelMotion.Valid(new Pose(p, q));
                if (Tracked) transform.SetLocalPositionAndRotation(p, q);
                bool hand = device is UnityEngine.XR.OpenXR.Features.Interactions.HandInteractionProfile.HandInteraction;
                float value = hand ? Value(device, "pointerActivateValue") : Value(device, "trigger");
                down = hand ? Value(device, "pointerActivated") > .5f
                    : Value(device, "triggerPressed") > .5f || value > (pressed ? .55f : .75f) || Value(device, "select") > .5f;
                // Do not OR continuous pinch strength into the native activation button: a
                // partially relaxed hand can stay above the analog release threshold for seconds.
                grab = hand ? Value(device, "graspFirm") > .5f || Value(device, "graspValue") > .75f
                    : Value(device, "gripPressed") > .5f || Value(device, "grip") > .75f;
                if (deviceId != device.deviceId)
                {
                    deviceId = device.deviceId;
                    Debug.Log($"[RoomSiege.Input] {name} device={device.name} layout={device.layout} id={deviceId} aim={position.name}/{rotation.name} hand={hand}");
                }
            }
            down &= Tracked; grab &= Tracked;
            bool uiDown = uiGate.Update(down);
            PressedThisFrame = uiDown && !deliveredPress;
            if(PressedThisFrame)LastPressFrame=Time.frameCount;
            SetReader(press, uiDown, deliveredPress); SetReader(grip, grab, gripped);
            deliveredPress = uiDown;
            if (down != pressed) Debug.Log($"[RoomSiege.Input] {name} press={down} tracked={Tracked} hit={lastHit ?? "none"} activate={(device!=null?Value(device,"pointerActivateValue"):0)} pinch={(device!=null?Value(device,"pinchValue"):0)}");
            pressed = down; gripped = grab;
            if (Interactor.enabled != Tracked) Interactor.enabled = Tracked;
            if (Time.unscaledTime >= nextHealth)
            {
                nextHealth = Time.unscaledTime + 5f;
                Debug.Log($"[RoomSiege.Input] {name} tracked={Tracked} devices={InputSystem.devices.Count} id={deviceId} uiModel={Interactor.TryGetUIModel(out _)} eventSystem={UnityEngine.EventSystems.EventSystem.current?.currentInputModule?.GetType().Name}");
                foreach(var input in InputSystem.devices)
                    if(input is XRController) Debug.Log($"[RoomSiege.Input] device={input.name} layout={input.layout} usages={string.Join(",",input.usages)} tracked={Value(input,"isTracked")} aimTracked={Value(input,"pointer/isTracked")} trigger={Value(input,"trigger")} pinch={Value(input,"pinchValue")}");
            }
        }

        private UnityEngine.InputSystem.InputDevice FindDevice()
        {
            UnityEngine.InputSystem.InputDevice candidate = null;
            foreach (var device in InputSystem.devices)
            {
                if (!(device is XRController) || !device.added) continue;
                bool matches = false;
                foreach (var usage in device.usages) if (usage == (left ? "LeftHand" : "RightHand")) matches = true;
                if (!matches) continue;
                var aimTracked = device.TryGetChildControl<ButtonControl>("pointer/isTracked");
                var tracked = aimTracked ?? device.TryGetChildControl<ButtonControl>("isTracked");
                if ((tracked == null || !tracked.isPressed) && Value(device,"isTracked") < .5f) continue;
                if (Control<Vector3Control>(device, "pointerPosition") == null && Control<Vector3Control>(device, "devicePosition") == null) continue;
                if (Control<QuaternionControl>(device, "pointerRotation") == null && Control<QuaternionControl>(device, "deviceRotation") == null) continue;
                if (device.deviceId == deviceId) return device;
                candidate = device;
            }
            return candidate;
        }
        private static T Control<T>(UnityEngine.InputSystem.InputDevice device, string path) where T : InputControl => device.TryGetChildControl<T>(path);
        private static float Value(UnityEngine.InputSystem.InputDevice device, string path) => Control<AxisControl>(device, path)?.ReadValue() ?? 0f;
        private static void SetReader(XRInputButtonReader reader, bool value, bool previous)
        {
            reader.manualPerformed = value; reader.manualValue = value ? 1f : 0f;
            if (value && !previous) reader.manualFramePerformed = Time.frameCount;
            if (!value && previous) reader.manualFrameCompleted = Time.frameCount;
        }
        private void LateUpdate()
        {
            line.enabled = Tracked;
            if (!Tracked) return;
            Vector3 end = transform.position + transform.forward * 3f;
            bool hit = Interactor.TryGetCurrentUIRaycastResult(out var result);
            if (hit) end = result.worldPosition;
            else if (Physics.Raycast(transform.position, transform.forward, out var physicsHit, 3f)) end = physicsHit.point;
            string target = hit ? result.gameObject.name : null;
            if (target != lastHit) { lastHit = target; Debug.Log($"[RoomSiege.Input] {name} hover={target ?? "none"}"); }
            line.SetPosition(0, transform.position); line.SetPosition(1, end);
            line.startColor = line.endColor = hit ? new Color(1f,.82f,.48f,.85f) : new Color(.31f,.85f,1f,.65f);
        }
        private void OnApplicationFocus(bool focus)
        {
            if (!focus) { uiGate.Update(false); uiGate.BlockUntilRelease(); deliveredPress = false; pressed = gripped = false; if (press != null) SetReader(press, false, true); if (grip != null) SetReader(grip, false, true); }
        }
        private void OnDestroy() { pointers.Remove(this); if (material != null) Destroy(material); }
    }
}
