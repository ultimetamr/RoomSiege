# RoomSiege Unity 6 compatibility — 2026-11-06

## Target

- Project: `D:\Would\DOME\my-pico-unity-app`
- Editor: Unity 6.6 `6000.6.8f1` (`237b6593792b`)
- PICO package remains the existing local SDK at `D:/DENO/PICO-Unity-SDK-6.1.1-20260903/XR`.
- Scope: editor/package compatibility. No gameplay rebalance, APK build or device deployment.

## Fixes

The original Console showed 281 errors. Old Input System and Version Control editor packages used non-generic TreeView APIs rejected by this editor. Package pins were aligned with this installed editor's official package catalog (`Editor/Data/Resources/PackageManager/Editor/manifest.json`):

- Input System: 1.14.0 → 1.20.1
- XR Interaction Toolkit: 3.3.0 → 3.6.1
- XR Management: 4.5.4 → 4.7.0
- OpenXR: 1.14.2 → 1.18.0
- XR Core Utils: 2.5.2 → 2.6.0
- Universal RP: 14.0.12 → 17.6.0
- Unity UI: 1.0.0 → 2.6.0
- Test Framework: 1.1.33 → 1.8.0
- Version Control: 2.7.1 → 2.13.6
- Visual Scripting: 1.9.4 → 1.9.12
- Timeline: 1.7.7 → 6.6.0

The obsolete direct TextMeshPro package pin was removed; Unity UI supplies the integrated text package. The removed `com.unity.modules.vr` module was dropped; OpenXR and `com.unity.modules.xr` remain. Package Manager regenerated `packages-lock.json`.

`TowerTestTarget.TargetId` used `GetInstanceID()`, which is rejected by this editor. Test dummies now receive stable negative IDs from an atomic counter, separate from positive enemy spawn IDs. No external SDK source or PackageCache files were patched.

The editor upgrade also reset `QualitySettings.m_PerPlatformDefaultQuality` to an empty map. Editing YAML alone did not persist through the next editor save. `RoomSiegeBootstrap.RestoreAndroidQualityDefault` now uses Unity's serialized settings interface (the same `first`/`second` map used by the official Quality Settings inspector) to restore a missing Android default to the existing High Fidelity level. Existing explicit Android choices are preserved. The existing URP asset and 4× MSAA remain.

## Validation

- Main project script compilation: 0 C# errors. Existing obsolete-API warnings are recorded in `TestResults/Unity6Compatibility-2026-11-06/MainCompile.txt`.
- Main scene was run in the Unity 6 editor: modern RoomSiege menu rendered; Console showed 0 errors.
- Final startup after cleanup: `EditorReady.log` records successful load in 9.856 seconds. The editor opened `RoomSiege_Start` on the Android target. A final manual Play smoke check rendered the modern menu and showed Console 0 errors / 0 warnings. The earlier shared-memory startup failure was no longer blocking this run.
- Initial targeted editor regression: 114 passed / 1 failed out of 115. The failing test identified the lost Android default quality setting; it was restored rather than weakening the test.
- Final targeted regression: `ClarityPersistent.xml` records 3 passed / 0 failed, including the previously failing Android default quality test. Combined with the initial 114 passing tests, all 115 selected checks have passing evidence across the two runs; a final complete 115-test rerun was not obtained.
- Additional automated runs did not produce complete test results: a `-nographics` run crashed in Unity native line rendering (`GfxDevice::DrawSharedGeometryJobs`), and a Direct3D 11 batch run crashed during resource loading. Neither invocation is counted as passing evidence.
- Subsequent editor startup diagnostics identified a host resource failure: `Logs/Editor-UDS.log` reports failure to allocate the 1,073,741,824-byte shared-memory pool used by the asset database. Other Unity projects were open concurrently. This startup failure occurs before project scripts load. Other projects were left untouched; the user was asked to save and close unused editors before the final startup check.
- Android builds, PICO device interactions, MR scanning on hardware, and device rendering/audio have not been revalidated after the package upgrade.

## Files and backup

Backups and test evidence are in `TestResults/Unity6Compatibility-2026-11-06/`. The `Before/` subdirectory preserves the original manifest, lock file, version file and target script, plus the post-upgrade quality settings before the Android restoration.

The secondary QA checkout's four task-modified files (manifest, lock file, editor version and test target script) were restored from their backups after checking they still matched this task's copies.

The temporary editor test launcher and its request file were removed. `Before/RoomSiegeBootstrap.cs` preserves the bootstrap before the persistent quality-default fix.

Use Unity `6000.6.8f1` for this migrated checkout. Reopening it with Unity 2022 would require restoring a compatible package set and assets; the new dependency pins are intended for Unity 6.
