# Unity 6 Android build compatibility — 2026-11-06

Target: Unity 6000.6.8f1, ARM64 Android, OpenXR, PICO SDK 6.1.1.

## Dependency repairs

- The PICO SDK is now embedded at `Packages/com.bytedance.pico.xr`, copied from `D:/DENO/PICO-Unity-SDK-6.1.1-20260903/XR`. The external SDK source is untouched.
- In the embedded `Runtime/PICOFeature.cs`, the custom loader name now matches the actual shipped `Runtime/Android/LoaderForUnitySDK.aar`. OpenXR 1.18 previously searched for the nonexistent `LoaderForUnitySDK_1_1_0` asset and stopped the build.
- Added Unity's official Composition Layers 2.6.0 package, copied from this editor's bundled package archive. The SDK requires this dependency for its OpenXR 1.16+ passthrough feature.
- Updated the embedded enterprise virtual-display sample to retrieve its native composition layer ID with `CompositionLayerManager.TryGetLayerId`, following the installed Composition Layers sample. Unity 6.6 rejects the previous `GetInstanceID()` call.
- Resolved pre-existing merge conflict markers in the embedded `Runtime/CompositionLayers.meta`, retaining its first folder GUID. Neither candidate folder GUID was referenced by project scenes, prefabs, or assets.

## Branding and delivery

The existing `RoomSiegeBranding` prebuild hook applies custom legacy/adaptive Android icons and the custom splash image. It fails if required branding textures are missing.

Build evidence is recorded under `TestResults/Unity6DeviceBuild-2026-11-06/`. The manifest and PlayerSettings before this build work are preserved there.

Device delivery uses replacement installation to retain local data. No uninstall or automatic permission grant is used.

## Final packaging evidence

- Unity completed script compilation, shader compilation, IL2CPP, native compilation, and Android project generation. Its Gradle invocation then failed in JDK 17's Windows `PipeImpl` / `UnixDomainSockets.connect` with `Invalid argument: connect`.
- Standalone Gradle succeeded when `JAVA_TOOL_OPTIONS` supplied `-Djdk.net.unixdomain.tmpdir=D:/Would/DOME/my-pico-unity-app/Temp/JavaNet`. Setting that variable inside the editor or before launching the editor did not reach Unity's Java invocation, so those attempts are not counted as successful Unity builds.
- Final packaging used the same generated project at `Library/Bee/Android/Prj/IL2CPP/Gradle`, Java and Gradle bundled with this Unity editor, and `assembleDebug --no-daemon`. `Gradle-icon-final.log` records `BUILD SUCCESSFUL` (94 tasks).
- APK inspection found the adaptive icon correct but the legacy `app_icon.png` still held Unity's logo. The final APK overwrites that generated fallback with `RoomSiegeIcon.png`. `RoomSiegeAndroidIconExport` now enforces the same correction in future exports. Its editor compilation was separately verified successfully in `BrandingCompile.log`.
- The final APK's extracted legacy icon matches `RoomSiegeIcon.png` byte-for-byte (SHA-256 `37AFEEC0D74C03C00EA53BD30BFE5262F065CEB2CDE6926FCD3EE8F4CA040E4E`). Both legacy and adaptive foreground images were visually inspected.
- Final APK SHA-256: `8EB0370D4AEAFFD5A1B5CE63CFC69C8ADF3E4A14688EEE927D2291E7CBF9F0B3`.
- `aapt` confirms package `com.roomsiege.mr`, label `RoomSiege`, ARM64, and the custom adaptive icon reference. The PICO OpenXR native loader is included.
- Replacement installation on PICO hardware succeeded. Initial launch was blocked while `com.spacecanvas.prototype` remained the active full-space application; the user was asked to exit it and open RoomSiege.
- After the user opened RoomSiege, device inspection confirmed its foreground activity and running process (PID 15021). Startup logs confirm controller-ray initialization, music output, and purchase mode (4/18 weapons, 0/54 skins, no supplied consumables). Room acquisition returned zero anchors through eight retries and fell back to a 139-cell sandbox with passthrough enabled; real-room restoration is not counted as passing. Logs: `DeviceStartup-details.json`.
