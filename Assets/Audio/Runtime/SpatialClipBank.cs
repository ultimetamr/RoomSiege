using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Audio
{
    // Local mono cues; HRTF performs stereo rendering. Integer-frequency loops join continuously.
    internal sealed class SpatialClipBank
    {
        private readonly Dictionary<string,AudioClip> arsenal=new Dictionary<string,AudioClip>();
        public AudioClip Arsenal(string id)=>arsenal.TryGetValue(id,out var clip)?clip:null;
        private readonly List<AudioClip> clips = new List<AudioClip>();
        public AudioClip Click, Hover, Confirm, Error, Step, Runner, Tank, BossStep, ShieldStep,
            Impact, MgShot, Frost, FrostLoop, Trap, Tesla, Mortar, Alarm, Horn, Laser, Boss, WaveStart, Victory, Defeat, Music;

        public SpatialClipBank()
        {
            Click = Make("Soft UI click", .075f, t => Decay(t, .075f) * .32f * Mathf.Sin(t * 2 * Mathf.PI * 880));
            Hover = Make("Soft UI hover", .045f, t => Decay(t, .045f) * .14f * Mathf.Sin(t * 2 * Mathf.PI * 1320));
            Confirm = Sweep("UI confirm", .12f, 520, 920, .30f);
            Error = Sweep("UI error", .16f, 220, 110, .26f);
            Step = ImpactTone("Grunt footstep", 110, .15f);
            Runner = ImpactTone("Runner footstep", 190, .09f);
            Tank = ImpactTone("Tank heavy footstep", 58, .25f);
            ShieldStep = ImpactTone("Shielded footstep", 90, .19f);
            BossStep = ImpactTone("Boss heavy footstep", 38, .3f);
            Impact = ImpactTone("Tower impact", 330, .12f);
            MgShot = Burst("MG muzzle", .075f, 190, 620, .30f);
            Frost = ImpactTone("Frost crystalline hit", 520, .16f);
            FrostLoop = Make("Frost field shimmer", 1.2f, t =>
                (.12f + .05f * Mathf.Sin(2 * Mathf.PI * 2.5f * t)) *
                (Mathf.Sin(2 * Mathf.PI * 360 * t) + .12f * Mathf.Sin(2 * Mathf.PI * 720 * t)));
            Tesla = Sweep("Tesla chain arc", .18f, 240, 980, .24f);
            Mortar = Make("Mortar impact", .42f, t => Decay(t,.42f)*(.5f*Mathf.Sin(2*Mathf.PI*(90-50*t)*t)+.25f*Mathf.Sin(2*Mathf.PI*310*t)));
            Trap = Make("Trap spatial explosion", .52f, t =>
            {
                float phase=2*Mathf.PI*(115*t-55*t*t);
                float body=Mathf.Sin(phase)*Mathf.Exp(-t*7);
                float crack=(Mathf.Sin(2*Mathf.PI*713*t)+.45f*Mathf.Sin(2*Mathf.PI*1193*t))*Mathf.Exp(-t*36);
                float debris=Mathf.Sin(2*Mathf.PI*235*t+2*Mathf.Sin(2*Mathf.PI*83*t))*Mathf.Exp(-t*13);
                return Decay(t,.52f)*(.65f*body+.18f*crack+.15f*debris);
            });
            Alarm = Make("Safe-zone alarm", .65f, t => Decay(t, .65f) * .55f *
                Mathf.Sin(2 * Mathf.PI * (420 * t + 25 * Mathf.Sin(2 * Mathf.PI * 3 * t))));
            Horn = Make("Wave horn", .85f, t =>
            {
                float local = t < .32f ? t : t - .32f;
                float length = t < .32f ? .32f : .53f;
                float p = 2 * Mathf.PI * (t < .32f ? 196 : 262) * local;
                return Decay(local, length) * .5f * (Mathf.Sin(p) + .25f * Mathf.Sin(2 * p));
            });
            Laser = Make("Directional laser hum", 1, t => .3f * Mathf.Sin(2 * Mathf.PI * 720 * t) + .08f * Mathf.Sin(2 * Mathf.PI * 1440 * t));
            WaveStart = Sweep("Wave start", .55f, 180, 420, .32f);
            Victory = Sweep("Victory", .9f, 330, 990, .34f);
            Defeat = Sweep("Defeat", .9f, 260, 90, .30f);
            Boss = Make("Boss low orbit", 2, t => (.7f + .2f * Mathf.Cos(2 * Mathf.PI * t)) *
                (.35f * Mathf.Sin(2 * Mathf.PI * 42 * t) + .2f * Mathf.Sin(2 * Mathf.PI * 84 * t)));
            string[] arsenalIds={"Beacon","Prism","Cryo","Sunder","Disruptor","Gravity","Sniper","Scatter","Flame","Missile","Railgun","Blade"};
            for(int i=0;i<arsenalIds.Length;i++)
            {
                int tone=i;float length=i==8?1f:.22f+(i%3)*.06f;
                arsenal[arsenalIds[i]]=Make(arsenalIds[i]+" spatial cue",length,t=>
                {
                    float envelope=tone==8?1:Decay(t,length);
                    float frequency=tone==6?160:tone==9?90:220+tone*29;
                    float phase=2*Mathf.PI*frequency*t;
                    return envelope*(.22f*Mathf.Sin(phase)+.07f*Mathf.Sin(phase*1.5f+Mathf.Sin(2*Mathf.PI*(8+tone)*t)));
                });
            }
            Music = Make("RoomSiege orbital theme", 19.2f, MusicSample);
        }

        private static readonly int[] Chords = { 57, 53, 60, 55 };
        private static readonly int[] ChordIntervals = { 0, 3, 7 };
        private static readonly int[] Arpeggio = { 0, 7, 12, 7, 3, 10, 15, 10 };
        private static float Note(int midi) => 440 * Mathf.Pow(2, (midi - 69) / 12f);
        private static float MusicSample(float t)
        {
            // Eight bars at 100 BPM: a restrained melodic bed audible on headset speakers.
            const float beat = .6f;
            int root = Chords[(int)(t / (beat * 8)) % Chords.Length];
            float local = t % (beat * 8);
            float padEnvelope = Mathf.Clamp01(local / .12f) * Mathf.Clamp01((beat * 8 - local) / .18f);
            float pad = 0;
            foreach (int interval in ChordIntervals)
                pad += Mathf.Sin(2 * Mathf.PI * Note(root + interval) * local) * .10f;
            float noteTime = t % (beat * .5f);
            int step = (int)(t / (beat * .5f)) % Arpeggio.Length;
            float melody = Mathf.Sin(2 * Mathf.PI * Note(root + 12 + Arpeggio[step]) * noteTime) *
                Mathf.Exp(-noteTime * 12) * Mathf.Clamp01(noteTime / .006f) * .28f;
            float bassTime = t % beat;
            float bass = Mathf.Sin(2 * Mathf.PI * Note(root - 12) * bassTime) *
                Mathf.Exp(-bassTime * 7) * Mathf.Clamp01(bassTime / .01f) * .10f;
            return pad * padEnvelope + melody + bass;
        }

        public AudioClip Footstep(string id) => id == "Runner" ? Runner : id == "Tank" ? Tank :
            id == "Shielded" ? ShieldStep : id == "Boss" ? BossStep : Step;

        private AudioClip ImpactTone(string name, float frequency, float duration)
        {
            return Make(name, duration, t => Decay(t, duration) * Mathf.Exp(-9 * t / duration) *
                (.7f * Mathf.Sin(2 * Mathf.PI * frequency * t) + .2f * Mathf.Sin(2 * Mathf.PI * frequency * 2.7f * t)));
        }

        private AudioClip Burst(string name, float duration, float start, float end, float gain)
        {
            return Make(name, duration, t =>
            {
                float p = 2 * Mathf.PI * Mathf.Lerp(start, end, t / duration) * t;
                return Decay(t, duration) * gain * (Mathf.Sin(p) + .32f * Mathf.Sin(p * 2.03f));
            });
        }

        private AudioClip Sweep(string name, float duration, float start, float end, float gain)
        {
            return Make(name, duration, t =>
            {
                float p = 2 * Mathf.PI * Mathf.Lerp(start, end, t / duration) * t;
                return Decay(t, duration) * gain * Mathf.Sin(p);
            });
        }

        private static float Decay(float t, float length) => Mathf.Clamp01(t / .008f) * Mathf.Clamp01((length - t) / .025f);

        private AudioClip Make(string name, float duration, System.Func<float, float> sample)
        {
            const int rate = 24000;
            var data = new float[Mathf.RoundToInt(rate * duration)];
            float peak = 0;
            for (int i = 0; i < data.Length; i++)
            {
                data[i] = sample((float)i / rate);
                peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            }
            // Establish one predictable asset level; source gain sets the mix afterwards.
            float gain = peak > 0 ? .7f / peak : 0;
            for (int i = 0; i < data.Length; i++) data[i] *= gain;
            var clip = AudioClip.Create(name, data.Length, 1, rate, false);
            clip.SetData(data, 0);
            clips.Add(clip);
            return clip;
        }

        public void Dispose() { foreach (var clip in clips) if (clip != null) Object.Destroy(clip); clips.Clear(); }
    }
}
