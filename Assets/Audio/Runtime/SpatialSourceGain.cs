using UnityEngine;

namespace RoomSiege.Audio
{
    // The SDK's PCM submission precedes Unity's source volume and attenuation.
    // Keep those controls effective in the explicitly initialized HRTF backend.
    [DefaultExecutionOrder(11300)]
    public sealed class SpatialSourceGain : MonoBehaviour
    {
        private AudioSource source;
        private PXR_Audio_Spatializer_AudioSource spatial;
        private Transform listener;
        private AnimationCurve rolloff;
        private float lastGain = -1;
        internal bool? UiDistanceProfile { get; set; }

        internal void Initialize(AudioSource audioSource, PXR_Audio_Spatializer_AudioSource spatialSource, Transform head)
        {
            source = audioSource;
            spatial = spatialSource;
            listener = head;
            rolloff = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
            Synchronize();
        }

        private void LateUpdate() => Synchronize();

        internal void RefreshRolloff()
        {
            if (source != null) rolloff = source.GetCustomCurve(AudioSourceCurveType.CustomRolloff);
        }

        private void Synchronize()
        {
            if (source == null || spatial == null) return;
            if (listener == null && Camera.main != null) listener = Camera.main.transform;
            float distance = listener != null ? Vector3.Distance(source.transform.position, listener.position) : 0;
            float attenuation = rolloff != null ? Mathf.Clamp01(rolloff.Evaluate(distance / Mathf.Max(.001f, source.maxDistance))) : 1;
            float gain = source.mute || (AudioListener.pause && !source.ignoreListenerPause) ? 0 : source.volume * attenuation;
            if (Mathf.Abs(gain - lastGain) < .00001f) return;
            lastGain = gain;
            spatial.SetGainAmplitude(gain);
        }
    }
}
