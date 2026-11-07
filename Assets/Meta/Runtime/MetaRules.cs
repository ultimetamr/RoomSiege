using System;
using RoomSiege.Waves;

namespace RoomSiege.Meta
{
    public static class MetaRules
    {
        // The same baseline on each difficulty; rankings remain separate.
        public static MetaRating Rate(CampaignOutcome outcome, float lives, float initialLives, int remainingGold)
        {
            if (outcome == CampaignOutcome.Victory && initialLives > 0 && lives >= initialLives && remainingGold >= 300)
                return MetaRating.Gold;
            if (outcome == CampaignOutcome.Victory && lives > 0) return MetaRating.Silver;
            return outcome == CampaignOutcome.Failure ? MetaRating.Copper : MetaRating.None;
        }

        public static int Score(CampaignOutcome outcome, int completedWaves, int kills, int leaks, int gold, float lives)
        {
            if (outcome != CampaignOutcome.Victory && outcome != CampaignOutcome.Failure) return 0;
            double value = (outcome == CampaignOutcome.Victory ? 10000 : 0) +
                (double)completedWaves * 1000 + (double)kills * 10 + Math.Max(0, gold) +
                Math.Floor(Math.Max(0, lives) * 100d) - (double)leaks * 25;
            return (int)Math.Max(0d, Math.Min(int.MaxValue, value));
        }

        public static string RatingName(MetaRating rating) => rating == MetaRating.Gold ? "金" :
            rating == MetaRating.Silver ? "银" : rating == MetaRating.Copper ? "铜" : "未评级";
        public static string DifficultyName(string id) => id == "Casual" ? "休闲" : id == "Hardcore" ? "硬核" : "标准";
    }
}
