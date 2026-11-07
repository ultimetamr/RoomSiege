using System;
using System.Collections.Generic;
using System.Linq;
using RoomSiege.Waves;
using UnityEngine;

namespace RoomSiege.Meta
{
    public enum MetaRating { None, Copper, Silver, Gold }

    [Serializable]
    public sealed class MetaScoreEntry
    {
        public string difficulty;
        public int score;
        public string rating;
        public int wave;
        public int remainingGold;
        public float lives;
        public int leaks;
        public string outcome;
        public string timestamp;
    }

    [Serializable]
    internal sealed class MetaScoreList { public List<MetaScoreEntry> entries = new List<MetaScoreEntry>(); }

    public readonly struct MetaSettlement
    {
        public CampaignOutcome Outcome { get; }
        public MetaRating Rating { get; }
        public float Lives { get; }
        public int Leaks { get; }
        public int RemainingGold { get; }
        public float Damage { get; }
        public float DurationSeconds { get; }
        public float DPS => DurationSeconds > .001f ? Damage / DurationSeconds : 0;
        public int Score { get; }
        public string Difficulty { get; }

        public MetaSettlement(CampaignOutcome outcome, MetaRating rating, float lives, int leaks,
            int remainingGold, float damage, float durationSeconds, int score, string difficulty)
        {
            Outcome = outcome; Rating = rating; Lives = lives; Leaks = leaks;
            RemainingGold = remainingGold; Damage = damage; DurationSeconds = durationSeconds; Score = score;
            Difficulty = difficulty;
        }
    }

    public static class MetaPersistence
    {
        private const string ScoresKey = "RoomSiege.Meta.LocalScores";
        private const string GoldSkinKey = "RoomSiege.Meta.GoldSkinUnlocked";
        private const string GoldSkinEquippedKey = "RoomSiege.Meta.GoldSkinEquipped";
        private const string TowerPrefix = "RoomSiege.Meta.TowerUnlocked.";
        private const string EnemyPrefix = "RoomSiege.Meta.EnemyUnlocked.";

        public static string DifficultyId(int value) => value == 0 ? "Casual" : value == 2 ? "Hardcore" : "Standard";

        public static bool GoldSkinUnlocked => PlayerPrefs.GetInt(GoldSkinKey, 0) != 0;
        public static bool GoldSkinEquipped => GoldSkinUnlocked && PlayerPrefs.GetInt(GoldSkinEquippedKey, 1) != 0;
        public static bool IsTowerUnlocked(string id) => PlayerPrefs.GetInt(TowerPrefix + id, 0) != 0;
        public static bool IsEnemyUnlocked(string id) => PlayerPrefs.GetInt(EnemyPrefix + id, 0) != 0;
        public static bool UnlockTower(string id) => Unlock(TowerPrefix, id);
        public static bool UnlockEnemy(string id) => Unlock(EnemyPrefix, id);
        public static void UnlockGoldSkin() { PlayerPrefs.SetInt(GoldSkinKey, 1); PlayerPrefs.Save(); }
        public static void SetGoldSkinEquipped(bool enabled)
        {
            if (enabled && !GoldSkinUnlocked) return;
            PlayerPrefs.SetInt(GoldSkinEquippedKey, enabled ? 1 : 0); PlayerPrefs.Save();
        }

        private static bool Unlock(string prefix, string id)
        {
            if (string.IsNullOrWhiteSpace(id) || PlayerPrefs.GetInt(prefix + id, 0) != 0) return false;
            PlayerPrefs.SetInt(prefix + id, 1); PlayerPrefs.Save(); return true;
        }

        public static IReadOnlyList<MetaScoreEntry> GetScores(string difficulty)
        {
            var all = Load().entries.Where(x => x != null && x.difficulty == difficulty)
                .OrderByDescending(x => x.score).ThenByDescending(x => x.wave).Take(10).ToList();
            return all.AsReadOnly();
        }

        public static void AddScore(MetaScoreEntry entry)
        {
            if (entry == null || !KnownDifficulty(entry.difficulty)) return;
            var list = Load(); list.entries.RemoveAll(x => x == null);
            list.entries.Add(entry);
            list.entries = list.entries.Where(x => KnownDifficulty(x.difficulty))
                .GroupBy(x => x.difficulty).SelectMany(group => group.OrderByDescending(x => x.score)
                    .ThenByDescending(x => x.wave).Take(10)).ToList();
            PlayerPrefs.SetString(ScoresKey, JsonUtility.ToJson(list)); PlayerPrefs.Save();
        }

        private static MetaScoreList Load()
        {
            string json = PlayerPrefs.GetString(ScoresKey, string.Empty);
            if (string.IsNullOrWhiteSpace(json)) return new MetaScoreList();
            try
            {
                var data = JsonUtility.FromJson<MetaScoreList>(json) ?? new MetaScoreList();
                if (data.entries == null) data.entries = new List<MetaScoreEntry>();
                return data;
            }
            catch (ArgumentException) { return new MetaScoreList(); }
        }

        private static bool KnownDifficulty(string id) => id == "Casual" || id == "Standard" || id == "Hardcore";
    }
}
