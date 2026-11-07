using System;
using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Data
{
    public static class BalanceJson
    {
        public static BalanceCatalog Parse(string json)
        {
            if (string.IsNullOrWhiteSpace(json)) throw new FormatException("Balance JSON is empty.");
            BalanceCatalog catalog;
            try { catalog = JsonUtility.FromJson<BalanceCatalog>(json); }
            catch (ArgumentException error) { throw new FormatException("Invalid balance JSON.", error); }
            Validate(catalog);
            return catalog;
        }

        public static void Validate(BalanceCatalog catalog)
        {
            Need(catalog != null, "Catalog is missing.");
            Need(catalog.schemaVersion == 1, "Unsupported schemaVersion; expected 1.");
            Need(catalog.economy != null, "Economy is missing.");
            Fraction(catalog.economy.sellRefundFraction, "economy.sellRefundFraction");
            Number(catalog.economy.interestRate, "economy.interestRate");
            Need(catalog.enemyProgression != null, "enemyProgression is missing.");
            Number(catalog.enemyProgression.spawnIntervalSeconds, "enemyProgression.spawnIntervalSeconds", true);
            Number(catalog.enemyProgression.hpIncreasePerWave, "enemyProgression.hpIncreasePerWave");
            Number(catalog.enemyProgression.armorIncreasePerWave, "enemyProgression.armorIncreasePerWave");
            Number(catalog.enemyProgression.speedIncreasePerWave, "enemyProgression.speedIncreasePerWave");
            Need(catalog.enemyProgression.maximumWave >= 1, "enemyProgression.maximumWave must be positive.");
            Need(catalog.enemyProgression.earlyWaveCount >= 0 && catalog.enemyProgression.earlyWaveCount <= catalog.enemyProgression.maximumWave,
                "enemyProgression.earlyWaveCount must be within the campaign.");
            Number(catalog.enemyProgression.earlyWaveHpMultiplier, "enemyProgression.earlyWaveHpMultiplier", true);
            Number(catalog.enemyProgression.earlyWaveSpeedMultiplier, "enemyProgression.earlyWaveSpeedMultiplier", true);
            var overriddenWaves = new HashSet<int>();
            if (catalog.enemyProgression.waveOverrides != null)
                foreach (var item in catalog.enemyProgression.waveOverrides)
                {
                    Need(item != null && item.wave >= 1 && item.wave <= catalog.enemyProgression.maximumWave &&
                         item.statWave >= 1 && item.statWave <= catalog.enemyProgression.maximumWave,
                         "enemyProgression.waveOverrides must reference campaign waves.");
                    Need(overriddenWaves.Add(item.wave), "Duplicate wave override.");
                    Number(item.hpMultiplier, "waveOverride.hpMultiplier", true);
                }
            Unique(catalog.towers, x => x.id, "towers");
            var enemyIds = Unique(catalog.enemies, x => x.id, "enemies");
            Unique(catalog.difficulties, x => x.id, "difficulties");

            foreach (var tower in catalog.towers)
            {
                var path = "tower " + tower.id;
                Need(!string.IsNullOrWhiteSpace(tower.displayName), path + " displayName is missing.");
                Need(tower.buildCost > 0, path + " buildCost must be positive.");
                Need(tower.damageMode == "projectile" || tower.damageMode == "slowArea" ||
                     tower.damageMode == "beam" || tower.damageMode == "trap" || tower.damageMode == "chain" || tower.damageMode == "mortar" || tower.damageMode == "arsenal", path + " damageMode is invalid.");
                Need(tower.rangeBand == "near" || tower.rangeBand == "unspecified", path + " rangeBand is invalid.");
                Optional(tower.rangeMeters, path + " rangeMeters", true);
                Number(tower.minimumRangeMeters,path+" minimumRangeMeters");
                Number(tower.impactDelaySeconds,path+" impactDelaySeconds");
                Need(tower.minimumRangeMeters < tower.rangeMeters.value,path+" minimum range exceeds range");
                if(tower.damageMode=="mortar")Need(tower.impactDelaySeconds>0,path+" mortar requires flight time");
                Need(tower.levels != null && tower.levels.Length == 3, path + " must define three levels.");
                for (var i = 0; i < tower.levels.Length; i++)
                {
                    var level = tower.levels[i];
                    Need(level != null && level.level == i + 1, path + " levels must be ordered Lv1/Lv2/Lv3.");
                    Need(level.upgradeCost >= 0 && (i != 0 || level.upgradeCost == 0), path + " upgradeCost is invalid.");
                    Number(level.effectMagnitude,path+" effectMagnitude");Number(level.effectDuration,path+" effectDuration");
                    if(tower.damageMode=="arsenal")
                    {
                        Need(WeaponRoster.IsNew(tower.id),path+" unknown arsenal weapon");
                        Need(tower.id=="Beacon"||tower.id=="Prism"||tower.id=="Gravity"||level.shotsPerSecond>0,path+" cadence missing");
                        if(tower.id=="Beacon"||tower.id=="Prism"||tower.id=="Sunder"||tower.id=="Disruptor")Need(level.effectMagnitude>0&&level.effectMagnitude<=.5f,path+" effect fraction invalid");
                        if(tower.id=="Cryo")Need(level.effectMagnitude>=1&&level.effectMagnitude<=5,path+" chill invalid");
                        if(tower.id=="Missile")Need(tower.impactDelaySeconds>0&&level.splashRadiusMeters.specified&&level.splashRadiusMeters.value>0,path+" missile blast missing");
                        if(tower.id=="Railgun")Need(level.pierceTargetCount>=1&&level.pierceTargetCount<=8,path+" rail penetration invalid");
                        if(tower.id=="Gravity")Need(level.slowFraction>0&&level.slowFraction<=.8f,path+" gravity slow invalid");
                        if(tower.id=="Flame"||tower.id=="Sunder"||tower.id=="Disruptor")Need(level.effectDuration>0,path+" status duration missing");
                    }
                    Number(level.damagePerHit, path + " damagePerHit");
                    if(tower.damageMode=="chain"||tower.damageMode=="mortar")
                    {
                        Need(level.damagePerHit>0&&level.shotsPerSecond>0&&level.splashRadiusMeters.specified&&level.splashRadiusMeters.value>0,path+" new weapon stats missing");
                        if(tower.damageMode=="chain")Need(level.pierceTargetCount>=1&&level.pierceTargetCount<=8,path+" chain count invalid");
                    }
                    Number(level.shotsPerSecond, path + " shotsPerSecond");
                    Number(level.damagePerSecond, path + " damagePerSecond");
                    Need(level.pierceTargetCount >= 0, path + " pierceTargetCount must be nonnegative.");
                    Fraction(level.slowFraction, path + " slowFraction");
                    Number(level.rangeMultiplier, path + " rangeMultiplier", true);
                    Optional(level.slowDurationSeconds, path + " slowDurationSeconds", true);
                    Optional(level.splashRadiusMeters, path + " splashRadiusMeters", true);
                    Optional(level.splashDamage, path + " splashDamage");
                    Need(!level.splashEnabled || !string.IsNullOrWhiteSpace(level.splashSize), path + " splashSize is missing.");
                }
            }

            foreach (var enemy in catalog.enemies)
            {
                var path = "enemy " + enemy.id;
                Number(enemy.hitPoints, path + " hitPoints", true);
                Number(enemy.moveSpeed, path + " moveSpeed", true);
                Number(enemy.leakDamage, path + " leakDamage");
                Need(enemy.killGold >= 0, path + " killGold must be nonnegative.");
                Need(enemy.movementPattern == "straight" || enemy.movementPattern == "serpentine", path + " movementPattern is invalid.");
                Optional(enemy.shieldHitPoints, path + " shieldHitPoints", true);
                Need(enemy.hasShield || !enemy.shieldHitPoints.specified, path + " shield HP requires hasShield.");
                if (enemy.summon == null || !enemy.summon.enabled) continue;
                Need(enemy.summon.enemyId != null && enemyIds.Contains(enemy.summon.enemyId), path + " summon enemyId is unknown.");
                Optional(enemy.summon.initialDelaySeconds, path + " summon initialDelaySeconds", true);
                Optional(enemy.summon.intervalSeconds, path + " summon intervalSeconds", true);
                Need(!enemy.summon.countPerSummon.specified || enemy.summon.countPerSummon.value > 0,
                    path + " summon count must be positive when specified.");
            }

            Need(catalog.waves != null && catalog.waves.Length > 0, "waves are missing.");
            for (var i = 0; i < catalog.waves.Length; i++)
            {
                var wave = catalog.waves[i];
                Need(wave != null && wave.number == i + 1, "Wave numbers must be consecutive, starting at 1.");
                Unique(wave.groups, x => x.enemyId, "wave " + wave.number + " groups");
                foreach (var group in wave.groups)
                {
                    Need(enemyIds.Contains(group.enemyId), "Wave references unknown enemy: " + group.enemyId);
                    Need(group.count > 0, "Wave enemy count must be positive.");
                }
            }

            foreach (var difficulty in catalog.difficulties)
            {
                Number(difficulty.hpMultiplier, "difficulty.hpMultiplier", true);
                Number(difficulty.speedMultiplier, "difficulty.speedMultiplier", true);
                Number(difficulty.leakMultiplier, "difficulty.leakMultiplier");
                Need(difficulty.initialGold >= 0, "difficulty.initialGold must be nonnegative.");
                Need(!string.IsNullOrWhiteSpace(difficulty.displayName), "difficulty.displayName is missing.");
            }
        }

        private static HashSet<string> Unique<T>(T[] entries, Func<T, string> id, string path) where T : class
        {
            Need(entries != null && entries.Length > 0, path + " are missing.");
            var ids = new HashSet<string>(StringComparer.Ordinal);
            foreach (var entry in entries)
            {
                Need(entry != null, path + " contains a null entry.");
                var key = id(entry);
                Need(!string.IsNullOrWhiteSpace(key), path + " contains an empty id.");
                Need(ids.Add(key), path + " contains duplicate id: " + key);
            }
            return ids;
        }

        private static void Fraction(float value, string path)
        {
            Number(value, path);
            Need(value <= 1f, path + " must be between 0 and 1.");
        }

        private static void Optional(OptionalFloat value, string path, bool positive = false)
        {
            if (value.specified) Number(value.value, path, positive);
        }

        private static void Number(float value, string path, bool positive = false)
        {
            Need(!float.IsNaN(value) && !float.IsInfinity(value) && (positive ? value > 0f : value >= 0f),
                path + (positive ? " must be finite and positive." : " must be finite and nonnegative."));
        }

        private static void Need(bool valid, string message)
        {
            if (!valid) throw new FormatException(message);
        }
    }
}
