using UnityEngine;
using UnityEngine.Audio;

namespace RoomSiege.Audio
{
    internal static class SpatialAudioSourceFactory
    {
        private static AudioMixer mixer;
        private static AudioMixerGroup output;
        public static AudioSource Create(Transform parent, string name, float minimum = .45f, float maximum = 12f)
        {
            if (mixer == null)
            {
                mixer = Resources.Load<AudioMixer>("RoomSiegeSpatialMix");
                if (mixer == null) throw new System.InvalidOperationException("RoomSiege spatial mixer missing.");
                var groups = mixer.FindMatchingGroups("Master");
                if (groups.Length == 0) throw new System.InvalidOperationException("RoomSiege spatial output missing.");
                output = groups[0];
            }
            var go = new GameObject(name);
            go.SetActive(false);
            go.transform.SetParent(parent, false);
            var source = go.AddComponent<AudioSource>();
            source.playOnAwake = false;
            source.outputAudioMixerGroup = output;
            source.spatialBlend = 1;
            // SDK unity backend submits mono PCM and renders HRTF on the listener.
            // The native spatializer is a separate backend; enabling both mutes/doubles processing.
            source.spatialize = false;
            source.spatializePostEffects = false;
            source.dopplerLevel = 0;
            source.spread = 0;
            source.minDistance = minimum;
            source.maxDistance = maximum;
            source.rolloffMode = AudioRolloffMode.Custom;
            ConfigureDistance(source, false);
            var pico = go.AddComponent<PXR_Audio_Spatializer_AudioSource>();
            // OnAudioFilterRead runs before Unity volume/rolloff. Apply these to the
            // SDK source once, before the listener renders the binaural signal.
            pico.enablePicoAttenuation = true;
            pico.sourceAttenuationMode = PXR_Audio.Spatializer.SourceAttenuationMode.None;
            // SDK OnValidate otherwise overwrites Unity distances with its defaults in Editor.
            pico.SetMinAttenuationRange(minimum);
            pico.SetMaxAttenuationRange(maximum);
            pico.SetReflectionGainDB(-120);
            pico.SetDopplerStatus(false);
            go.AddComponent<SpatialSourceGain>().Initialize(source, pico, Camera.main != null ? Camera.main.transform : null);
            go.SetActive(true);
            return source;
        }

        internal static void ConfigureDistance(AudioSource source, bool uiFeedback)
        {
            var gain = source.GetComponent<SpatialSourceGain>();
            if (gain != null && gain.UiDistanceProfile == uiFeedback) return;
            float maximum = source.maxDistance;
            // Keep the accepted UI level; gameplay needs a broader near field in a room.
            var keys = uiFeedback ? new[] {
                new Keyframe(0, 1), new Keyframe(source.minDistance / maximum, 1),
                new Keyframe(2f / maximum, .38f), new Keyframe(6f / maximum, .1f), new Keyframe(1, 0)
            } : new[] {
                new Keyframe(0, 1), new Keyframe(1.5f / maximum, 1),
                new Keyframe(3f / maximum, .75f), new Keyframe(6f / maximum, .4f), new Keyframe(1, 0)
            };
            source.SetCustomCurve(AudioSourceCurveType.CustomRolloff, new AnimationCurve(keys));
            if (gain != null)
            {
                gain.UiDistanceProfile = uiFeedback;
                gain.RefreshRolloff();
            }
        }
    }
}
