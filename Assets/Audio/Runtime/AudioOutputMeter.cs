using System.Threading;
using UnityEngine;

namespace RoomSiege.Audio
{
    // Attached after the SDK listener so this measures the final binaural PCM.
    public sealed class AudioOutputMeter : MonoBehaviour
    {
        private float peakRms;
        private int blocks;
        public float PeakRms => Volatile.Read(ref peakRms);
        public int Blocks => Volatile.Read(ref blocks);
        public void ResetPeak() => Volatile.Write(ref peakRms, 0);

        private void OnAudioFilterRead(float[] data, int channels)
        {
            double energy = 0;
            for (int i = 0; i < data.Length; i++) energy += data[i] * data[i];
            float rms = (float)System.Math.Sqrt(energy / data.Length);
            if (rms > peakRms) Volatile.Write(ref peakRms, rms);
            Interlocked.Increment(ref blocks);
        }
    }
}
