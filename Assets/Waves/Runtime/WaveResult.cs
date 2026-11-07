namespace RoomSiege.Waves
{
    public enum CampaignOutcome { None, Victory, Failure, Interrupted }
    public enum WaveEndReason { Cleared, Failed, Interrupted }

    // Immutable settlement snapshot. Summoned enemies count toward kills and leaks too.
    public readonly struct WaveResult
    {
        public int WaveNumber { get; }
        public int Kills { get; }
        public int Leaks { get; }
        public int RemainingGold { get; }
        public float DamageDealt { get; }
        public float DurationSeconds { get; }
        public WaveEndReason Reason { get; }

        public WaveResult(int waveNumber, int kills, int leaks, int remainingGold, WaveEndReason reason)
            : this(waveNumber, kills, leaks, remainingGold, 0, 0, reason) { }

        public WaveResult(int waveNumber, int kills, int leaks, int remainingGold, float damageDealt, float durationSeconds, WaveEndReason reason)
        {
            WaveNumber = waveNumber;
            Kills = kills;
            Leaks = leaks;
            RemainingGold = remainingGold;
            DamageDealt = damageDealt;
            DurationSeconds = durationSeconds;
            Reason = reason;
        }
    }
}
