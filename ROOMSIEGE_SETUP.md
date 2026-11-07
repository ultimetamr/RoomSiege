# RoomSiege skeleton

Project: D:\Would\DOME\my-pico-unity-app
Editor: D:\Would\Unity\2022.3.62f3c1\Editor\Unity.exe
Unity version: 2022.3.62f3c1
PICO Unity SDK: 6.1.1, local UPM package at D:\DENO\PICO-Unity-SDK-6.1.1-20260903\XR

## Scope

Only project skeleton and world-space startup UI. No towers, enemies, wave spawning, scanning, rating, persistence, or gameplay implemented. Requested directories exist under Assets. GameManager has coins, lives, wave, difficulty, and MENU / SCAN / BUILD / WAVE / RESULT states.

Startup scene: Assets/Scenes/RoomSiege_Start.unity
CameraRig uses XROrigin and Input System TrackedPoseDriver. Left/right controller rays can target world-space UI through XRUIInputModule. The Start button calls GameManager.EnterBuildPlaceholder; Settings remains a menu placeholder. B is an Editor/emulator shortcut invoking the same Start button callback.

## Configuration

Android package: com.roomsiege.mr
Minimum API: 29; ARM64; IL2CPP; OpenGLES3
XR loader: UnityEngine.XR.OpenXR.OpenXRLoader
PICO features: PICOFeature, OpenXRExtensions, PICO4/PICO4Ultra/Neo3/G3 controller profiles. Feature API names were checked against the SDK Portal source. Android XR initializes on startup.

## Available Editor commands

- RoomSiege > Configure Skeleton: configure the Android and OpenXR settings; create/upgrade the generated skeleton scene if missing its menu component.
- RoomSiege > Build Android APK: produce Builds/RoomSiege.apk.
- Play the startup scene and click 开始 to show BUILD; click 设置 to return to MENU. Keyboard B invokes 开始.

## Verification on 2026-10-26

- Unity script compilation succeeded.
- Editor Play Mode rendered RoomSiege and both Chinese buttons.
- Clicking 开始 in Editor changed visible state to BUILD.
- Android build succeeded, 0 errors; APK size 48,579,775 bytes.
- APK installed successfully on PICO Emulator 0.13.0 / emulator-5554.
- Device ABI list includes arm64-v8a and x86_64.
- Emulator launch/render acceptance passed. RoomSiege title, Chinese Start/Settings buttons, and BUILD placeholder are visible. Unity runtime log confirms the tracked UI button event changed GameManager to BUILD at 13:59:52 on 2026-10-26. User approved Continue Anyway at the controller prompt; the prompt was already dismissed when automation resumed.
- SDK contains optional native-plugin 16KB alignment warnings during build; no build failure occurred.

The PICO Emulator documentation lists VST/passthrough as unsupported. This skeleton uses a solid background and does not implement room scanning.

## Skill archive note

D:\DENO\PICO-Unity-SDK-6.1.1-20260903.zip is the SDK package. It contains no SKILL.md, so no Unity AI Skill was registered from that archive. SDK integration and agent Skill installation are separate operations.

## Runtime evidence

Verified PNG: C:\Users\Administrator\Documents\Codex\2026-10-26\wo\outputs\RoomSiege-BUILD.png
Target: emulator-5554 (PICO Emulator 0.13.0). Standard screenshot capture produced a readable, undistorted image of the emulator view. This emulator's screencap does not support --spatial; standard pico-cli capture screenshot was used after that unsupported option failed.
The emulator emits repeated libEGL 'called unimplemented OpenGL ES API' messages. The application remains running and renders the accepted UI. No gameplay or further platform work was added after acceptance.

