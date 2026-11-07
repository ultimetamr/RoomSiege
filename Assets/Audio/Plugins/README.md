# SDK 6.1.1 managed spatial audio library alias

`ApiUnityImpl` in the installed PICO SDK 6.1.1 imports `PicoSpatializer`,
but that SDK ships its core C API as `yggdrasil` instead. These two files
are unchanged copies of that same SDK's core library under the expected name:

- Win64/PicoSpatializer.dll <- XR/SpatialAudio/Plugins/Win/x64/yggdrasil.dll
- Android/libPicoSpatializer.so <- XR/SpatialAudio/Plugins/Android/libs/arm64-v8a/libyggdrasil.so

No binary from a previous SDK is used. Export tables were checked for the
context, scene, source and binaural output calls used by RoomSiege.
The unused legacy Unity ambisonic decoder callback is absent from this core;
RoomSiege uses mono object sources and does not invoke that callback.

Windows import is Editor-only; Android import is ARM64-only.
When updating the SDK, recheck its managed API library name and these aliases.
