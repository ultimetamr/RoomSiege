using UnityEngine;

namespace RoomSiege.Waves
{
    public enum WavePhaseCue { None, Started, Cleared, Boss }

    // One replaceable cue in the existing progress ring; no queue of stale announcements.
    public sealed class WavePhaseNotice
    {
        public WavePhaseCue Kind { get; private set; }
        public string Text { get; private set; } = "";
        public float Remaining { get; private set; }
        public bool Visible => Kind != WavePhaseCue.None && Remaining > 0;
        private bool bossAnnounced, active;

        public void BeginWave(int number, int total)
        {
            active = true; bossAnnounced = false;
            Show(WavePhaseCue.Started, $"第 {number} / {total} 波\n入侵开始\n守住核心", 2.5f);
        }

        public bool BossArrived()
        {
            if (!active || bossAnnounced) return false;
            bossAnnounced = true;
            Show(WavePhaseCue.Boss, "首领已入场\n集火首领\n留意召唤物", 4f);
            return true;
        }

        public void EndWave(WaveResult result, int total)
        {
            active = false;
            if (result.Reason != WaveEndReason.Cleared || result.WaveNumber >= total) { Clear(); return; }
            Show(WavePhaseCue.Cleared, $"第 {result.WaveNumber} 波守住\n" +
                (result.Leaks == 0 ? "零漏怪" : $"漏过 {result.Leaks} 只") + "\n准备下一波", 3f);
        }

        public void Tick(float dt, bool paused)
        {
            if (paused || dt <= 0 || float.IsNaN(dt) || float.IsInfinity(dt)) return;
            Remaining = Mathf.Max(0, Remaining - dt);
            if (Remaining <= 0) Clear();
        }

        public void Reset() { active = bossAnnounced = false; Clear(); }
        private void Clear() { Kind = WavePhaseCue.None; Text = ""; Remaining = 0; }
        private void Show(WavePhaseCue kind, string text, float seconds)
        { Kind = kind; Text = text; Remaining = seconds; }
    }
}
