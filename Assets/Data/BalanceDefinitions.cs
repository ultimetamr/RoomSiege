using System;
using System.Collections.Generic;

namespace RoomSiege.Data
{
    // A missing balancing decision is different from a numeric zero.
    // When specified is false, consumers must ignore value.
    [Serializable]
    public struct OptionalFloat
    {
        public bool specified;
        public float value;
    }

    [Serializable]
    public struct OptionalInt
    {
        public bool specified;
        public int value;
    }

    [Serializable]
    public sealed class BalanceCatalog
    {
        public int schemaVersion;
        public EconomyDefinition economy;
        public EnemyProgressionDefinition enemyProgression;
        public TowerDefinition[] towers;
        public EnemyDefinition[] enemies;
        public WaveDefinition[] waves;
        public DifficultyDefinition[] difficulties;

        public TowerDefinition GetTower(string id) => Find(towers, x => x.id == id, id);
        public EnemyDefinition GetEnemy(string id) => Find(enemies, x => x.id == id, id);
        public DifficultyDefinition GetDifficulty(string id) => Find(difficulties, x => x.id == id, id);

        private static T Find<T>(T[] entries, Predicate<T> predicate, string id) where T : class
        {
            var entry = entries == null ? null : Array.Find(entries, predicate);
            return entry ?? throw new KeyNotFoundException($"Unknown configuration id: {id}");
        }
    }

    [Serializable]
    public sealed class EnemyProgressionDefinition
    {
        public float spawnIntervalSeconds;
        public float hpIncreasePerWave;
        public float armorIncreasePerWave;
        public float speedIncreasePerWave;
        public int maximumWave;
        public int earlyWaveCount;
        public float earlyWaveHpMultiplier = 1f;
        public float earlyWaveSpeedMultiplier = 1f;
        public EnemyWaveOverride[] waveOverrides;
    }

    [Serializable]
    public sealed class EnemyWaveOverride
    {
        public int wave;
        public int statWave;
        public float hpMultiplier = 1f;
    }

    [Serializable]
    public sealed class EconomyDefinition
    {
        public float sellRefundFraction;
        public float interestRate;
    }

    [Serializable]
    public sealed class TowerDefinition
    {
        public string id;
        public string displayName;
        public int buildCost;
        public string damageMode;
        public string rangeBand;
        public OptionalFloat rangeMeters;
        public bool singleUse;
        public float minimumRangeMeters,impactDelaySeconds;
        public TowerLevelDefinition[] levels;
    }

    [Serializable]
    public sealed class TowerLevelDefinition
    {
        public int level;
        // Incremental upgrade cost; Lv1 uses the tower's buildCost instead.
        public int upgradeCost;
        public float effectMagnitude,effectDuration;
        public float damagePerHit;
        public float shotsPerSecond;
        public float damagePerSecond;
        public int pierceTargetCount;
        public float slowFraction;
        public OptionalFloat slowDurationSeconds;
        public float rangeMultiplier;
        public bool splashEnabled;
        public string splashSize;
        public OptionalFloat splashRadiusMeters;
        public OptionalFloat splashDamage;
    }

    [Serializable]
    public sealed class EnemyDefinition
    {
        public string id;
        public float hitPoints;
        public float moveSpeed;
        public int killGold;
        public float leakDamage;
        public string movementPattern;
        public bool hasShield;
        public OptionalFloat shieldHitPoints;
        public SummonDefinition summon;
    }

    [Serializable]
    public sealed class SummonDefinition
    {
        public bool enabled;
        public string enemyId;
        public bool periodic;
        public OptionalFloat initialDelaySeconds;
        public OptionalFloat intervalSeconds;
        public OptionalInt countPerSummon;
    }

    [Serializable]
    public sealed class WaveDefinition
    {
        public int number;
        public WaveGroupDefinition[] groups;
    }

    [Serializable]
    public sealed class WaveGroupDefinition
    {
        public string enemyId;
        public int count;
    }

    [Serializable]
    public sealed class DifficultyDefinition
    {
        public string id;
        public string displayName;
        public float hpMultiplier;
        public int initialGold;
        public float speedMultiplier;
        public float leakMultiplier;
    }
}
