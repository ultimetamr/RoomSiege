using System;
using UnityEngine;

namespace RoomSiege.Waves
{
    // Waves owns timing; Audio/ owns the source, clip and spatialization.
    public sealed class WaveHorn : MonoBehaviour
    {
        public static event Action<Vector3> PlaybackRequested;
        public static event Action StopRequested;
        public static event Action<Vector3> WarningRequested,FreezeRequested;
        private Vector3 position;
        private bool paused;
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetEvents() { PlaybackRequested = null; StopRequested = null;WarningRequested=FreezeRequested=null; }
        public void SetPosition(Vector3 value) => position = value;
        public void Play() { if (!paused) PlaybackRequested?.Invoke(position); }
        public void SetPaused(bool value) => paused = value;
        public void Stop() => StopRequested?.Invoke();
        public void Warn(Vector3 value){if(!paused)WarningRequested?.Invoke(value);}
        public void Freeze(Vector3 value){if(!paused)FreezeRequested?.Invoke(value);}
    }
}
