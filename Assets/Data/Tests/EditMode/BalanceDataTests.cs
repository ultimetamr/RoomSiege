using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace RoomSiege.Data.Tests
{
    public sealed class BalanceDataTests
    {
        private string json;
        private BalanceCatalog catalog;

        [OneTimeSetUp]
        public void ReadDeliveredJson()
        {
            json = File.ReadAllText(Path.Combine(Application.dataPath, "Data/Config/roomsiege.balance.json"));
        }

        [SetUp]
        public void ParseFreshCatalog() => catalog = BalanceJson.Parse(json);

        [Test]
        public void DeliveredJsonParsesAndRoundTrips()
        {
            var copy = BalanceJson.Parse(JsonUtility.ToJson(catalog));
            Assert.That(copy.schemaVersion, Is.EqualTo(1));
            Assert.That(copy.towers.Length, Is.EqualTo(18));
            Assert.That(copy.enemies.Length, Is.EqualTo(5));
            Assert.That(copy.waves.Length, Is.EqualTo(10));
            Assert.That(copy.difficulties.Length, Is.EqualTo(3));
            Assert.That(copy.economy.sellRefundFraction, Is.EqualTo(0.7f));
            Assert.That(copy.economy.interestRate, Is.Zero);
            Assert.That(copy.GetTower("Frost").displayName, Is.EqualTo("Frost冰墙"));
            Assert.That(copy.enemyProgression.spawnIntervalSeconds, Is.EqualTo(1f));
            Assert.That(copy.enemyProgression.earlyWaveCount, Is.EqualTo(3));
            Assert.That(copy.enemyProgression.earlyWaveHpMultiplier, Is.EqualTo(.6f));
            Assert.That(copy.enemyProgression.earlyWaveSpeedMultiplier, Is.EqualTo(.7f));
        }

        [TestCase("MG", 50, 40, 70)]
        [TestCase("Frost", 40, 35, 60)]
        [TestCase("Laser", 80, 55, 90)]
        [TestCase("Trap", 30, 20, 45)]
        public void BuildAndIncrementalUpgradeCostsMatch(string id, int build, int lv2, int lv3)
        {
            var tower = catalog.GetTower(id);
            Assert.That(tower.buildCost, Is.EqualTo(build));
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, tower.levels.Select(x => x.level));
            CollectionAssert.AreEqual(new[] { 0, lv2, lv3 }, tower.levels.Select(x => x.upgradeCost));
        }

        [Test]
        public void TowerCombatValuesMatchAllThreeLevels()
        {
            var mg = catalog.GetTower("MG");
            CollectionAssert.AreEqual(new[] { 5f, 8f, 11f }, mg.levels.Select(x => x.damagePerHit));
            CollectionAssert.AreEqual(new[] { 1.2f, 1.4f, 1.6f }, mg.levels.Select(x => x.shotsPerSecond));
            Assert.That(mg.rangeBand, Is.EqualTo("near"));
            Assert.That(mg.levels.Take(2).Any(x => x.splashEnabled), Is.False);
            Assert.That(mg.levels[2].splashEnabled, Is.True);
            Assert.That(mg.levels[2].splashSize, Is.EqualTo("small"));

            var frost = catalog.GetTower("Frost");
            CollectionAssert.AreEqual(new[] { 0.3f, 0.45f, 0.55f }, frost.levels.Select(x => x.slowFraction));
            CollectionAssert.AreEqual(new[] { 1f, 1.3f, 1.3f }, frost.levels.Select(x => x.rangeMultiplier));
            Assert.That(frost.levels.All(x => x.slowDurationSeconds.specified && x.slowDurationSeconds.value == 2f), Is.True);
            CollectionAssert.AreEqual(new[] { 0f, 0f, 1.5f }, frost.levels.Select(x => x.damagePerSecond));

            var laser = catalog.GetTower("Laser");
            CollectionAssert.AreEqual(new[] { 9f, 15f, 24f }, laser.levels.Select(x => x.damagePerSecond));
            CollectionAssert.AreEqual(new[] { 1, 1, 2 }, laser.levels.Select(x => x.pierceTargetCount));

            var trap = catalog.GetTower("Trap");
            Assert.That(trap.singleUse, Is.True);
            CollectionAssert.AreEqual(new[] { 350f, 450f, 600f }, trap.levels.Select(x => x.damagePerHit));
            CollectionAssert.AreEqual(new[] { 0f, 0f, 0.3f }, trap.levels.Select(x => x.slowFraction));
        }

        [TestCase("Grunt", 24f, 0.5f, 6, 1f)]
        [TestCase("Runner", 15f, 1f, 8, 1f)]
        [TestCase("Tank", 120f, 0.3f, 25, 3f)]
        [TestCase("Shielded", 36f, 0.45f, 12, 2f)]
        [TestCase("Boss", 600f, 0.25f, 150, 5f)]
        public void BaseEnemyStatsMatch(string id, float hp, float speed, int gold, float leak)
        {
            var enemy = catalog.GetEnemy(id);
            Assert.That(enemy.hitPoints, Is.EqualTo(hp));
            Assert.That(enemy.moveSpeed, Is.EqualTo(speed));
            Assert.That(enemy.killGold, Is.EqualTo(gold));
            Assert.That(enemy.leakDamage, Is.EqualTo(leak));
        }

        [Test]
        public void EnemyTraitsAndChosenDefaultsAreExplicit()
        {
            Assert.That(catalog.GetEnemy("Runner").movementPattern, Is.EqualTo("serpentine"));
            var shield = catalog.GetEnemy("Shielded");
            Assert.That(shield.hasShield, Is.True);
            Assert.That(shield.shieldHitPoints.specified, Is.True);
            Assert.That(shield.shieldHitPoints.value, Is.EqualTo(18f));
            var summon = catalog.GetEnemy("Boss").summon;
            Assert.That(summon.enabled && summon.periodic, Is.True);
            Assert.That(summon.enemyId, Is.EqualTo("Grunt"));
            Assert.That(summon.intervalSeconds.specified && summon.countPerSummon.specified, Is.True);
            Assert.That(summon.initialDelaySeconds.specified, Is.True);
            Assert.That(summon.initialDelaySeconds.value, Is.EqualTo(5f));
            Assert.That(summon.intervalSeconds.value, Is.EqualTo(7.5f));
            Assert.That(summon.countPerSummon.value, Is.EqualTo(2));
            Assert.That(catalog.GetTower("MG").rangeMeters.value, Is.EqualTo(2f));
            Assert.That(catalog.GetTower("MG").levels[2].splashRadiusMeters.value, Is.EqualTo(0.3f));
            Assert.That(catalog.GetTower("MG").levels[2].splashDamage.value, Is.EqualTo(3f));
            Assert.That(catalog.GetTower("Frost").rangeMeters.value, Is.EqualTo(1f));
            Assert.That(catalog.GetTower("Laser").rangeMeters.value, Is.EqualTo(4f));
            Assert.That(catalog.GetTower("Trap").rangeMeters.value, Is.EqualTo(0.5f));
            Assert.That(catalog.GetTower("Trap").levels[2].slowDurationSeconds.value, Is.EqualTo(2f));
        }

        [Test]
        public void TenWavesMatchTheSpecifiedCompositions()
        {
            var actual = catalog.waves.Select(w => string.Join("+", w.groups.Select(g => g.enemyId + "x" + g.count)));
            CollectionAssert.AreEqual(new[] {
                "Gruntx6", "Gruntx8", "Gruntx6+Runnerx3", "Runnerx5+Gruntx4", "Tankx1+Gruntx6",
                "Shieldedx4+Runnerx4", "Gruntx10+Runnerx4", "Tankx2+Shieldedx3",
                "Runnerx8+Shieldedx4+Gruntx6", "Bossx1+Gruntx8"
            }, actual);
        }

        [TestCase("Casual", 1f, 180, 0.9f, 0.5f)]
        [TestCase("Standard", 1.3f, 150, 1f, 1f)]
        [TestCase("Hardcore", 1.7f, 120, 1.15f, 1.5f)]
        public void DifficultiesMatch(string id, float hp, int initialGold, float speed, float leak)
        {
            var difficulty = catalog.GetDifficulty(id);
            Assert.That(difficulty.hpMultiplier, Is.EqualTo(hp));
            Assert.That(difficulty.initialGold, Is.EqualTo(initialGold));
            Assert.That(difficulty.speedMultiplier, Is.EqualTo(speed));
            Assert.That(difficulty.leakMultiplier, Is.EqualTo(leak));
        }

        [TestCase("Grunt", "Casual", 24f, 0.45f, 6, 0.5f)]
        [TestCase("Runner", "Casual", 15f, 0.9f, 8, 0.5f)]
        [TestCase("Tank", "Casual", 120f, 0.27f, 25, 1.5f)]
        [TestCase("Shielded", "Casual", 36f, 0.405f, 12, 1f)]
        [TestCase("Boss", "Casual", 600f, 0.225f, 150, 2.5f)]
        [TestCase("Grunt", "Standard", 31.2f, 0.5f, 6, 1f)]
        [TestCase("Runner", "Standard", 19.5f, 1f, 8, 1f)]
        [TestCase("Tank", "Standard", 156f, 0.3f, 25, 3f)]
        [TestCase("Shielded", "Standard", 46.8f, 0.45f, 12, 2f)]
        [TestCase("Boss", "Standard", 780f, 0.25f, 150, 5f)]
        [TestCase("Grunt", "Hardcore", 40.8f, 0.575f, 6, 1.5f)]
        [TestCase("Runner", "Hardcore", 25.5f, 1.15f, 8, 1.5f)]
        [TestCase("Tank", "Hardcore", 204f, 0.345f, 25, 4.5f)]
        [TestCase("Shielded", "Hardcore", 61.2f, 0.5175f, 12, 3f)]
        [TestCase("Boss", "Hardcore", 1020f, 0.2875f, 150, 7.5f)]
        public void AppliesEachDifficultyToEveryEnemy(string enemyId, string difficultyId,
            float hp, float speed, int gold, float leak)
        {
            var stats = DifficultyCalculator.Apply(catalog.GetEnemy(enemyId), catalog.GetDifficulty(difficultyId));
            Assert.That(stats.Id, Is.EqualTo(enemyId));
            Assert.That(stats.HitPoints, Is.EqualTo(hp).Within(0.0001f));
            Assert.That(stats.MoveSpeed, Is.EqualTo(speed).Within(0.0001f));
            Assert.That(stats.KillGold, Is.EqualTo(gold));
            Assert.That(stats.LeakDamage, Is.EqualTo(leak).Within(0.0001f));
        }

        [TestCase("Casual", 3012d, 57d)]
        [TestCase("Standard", 3915.6d, 114d)]
        [TestCase("Hardcore", 5120.4d, 171d)]
        public void CampaignTotalsExcludeShieldAndUnscheduledSummons(string difficultyId, double hp, double leak)
        {
            var totals = DifficultyCalculator.CalculateCampaignTotals(catalog, catalog.GetDifficulty(difficultyId));
            Assert.That(totals.EnemyCount, Is.EqualTo(93));
            Assert.That(totals.KillGold, Is.EqualTo(873));
            Assert.That(totals.HitPoints, Is.EqualTo(hp).Within(0.001d));
            Assert.That(totals.LeakDamage, Is.EqualTo(leak).Within(0.001d));
        }

        [Test]
        public void ScalingIsRepeatableAndDoesNotMutateInput()
        {
            var original = JsonUtility.ToJson(catalog);
            var enemy = catalog.GetEnemy("Shielded");
            var difficulty = catalog.GetDifficulty("Standard");
            var first = DifficultyCalculator.Apply(enemy, difficulty);
            var second = DifficultyCalculator.Apply(enemy, difficulty);
            Assert.That(first.ShieldHitPoints.specified && first.HasShield, Is.True);
            Assert.That(first.ShieldHitPoints.value, Is.EqualTo(23.4f).Within(0.0001f));
            Assert.That(second.HitPoints, Is.EqualTo(first.HitPoints));
            Assert.That(JsonUtility.ToJson(catalog), Is.EqualTo(original));
        }

        [Test]
        public void WaveProgressionRaisesEnemyStatsWithoutMutatingBaseConfig()
        {
            var baseEnemy=catalog.GetEnemy("Grunt");
            var standard=catalog.GetDifficulty("Standard");
            var wave1=DifficultyCalculator.ApplyAtWave(baseEnemy,standard,1,catalog.enemyProgression);
            var wave10=DifficultyCalculator.ApplyAtWave(baseEnemy,standard,10,catalog.enemyProgression);
            Assert.That(wave1.HitPoints,Is.EqualTo(31.2f*1.35f*.6f).Within(.0001f));
            Assert.That(wave10.HitPoints,Is.EqualTo(31.2f*4.15f).Within(.0001f));
            Assert.That(wave10.MoveSpeed,Is.EqualTo(.5f*1.135f).Within(.0001f));
            Assert.That(baseEnemy.hitPoints,Is.EqualTo(24f));
        }

        [Test]
        public void UnspecifiedShieldRemainsExplicitlyUnspecified()
        {
            var enemy = catalog.GetEnemy("Shielded");
            enemy.shieldHitPoints = default;
            var stats = DifficultyCalculator.Apply(enemy, catalog.GetDifficulty("Hardcore"));
            Assert.That(stats.HasShield, Is.True);
            Assert.That(stats.ShieldHitPoints.specified, Is.False);
        }

        [TestCase("Casual")][TestCase("Standard")][TestCase("Hardcore")]
        public void EveryWaveRaisesHealthShieldAndArmorFromBaseWithoutCompounding(string difficultyId)
        {
            var original=JsonUtility.ToJson(catalog);
            var difficulty=catalog.GetDifficulty(difficultyId);
            foreach(var enemy in catalog.enemies)
            {
                float previousEffectiveHp=0;
                for(int wave=1;wave<=10;wave++)
                {
                    var stats=DifficultyCalculator.ApplyAtWave(enemy,difficulty,wave,catalog.enemyProgression);
                    int statWave=wave==1?2:wave;
                    float openingFactor=wave==2?1.1f:1f;
                    float expectedHp=enemy.hitPoints*difficulty.hpMultiplier*(1+(statWave-1)*.35f)*openingFactor;
                    if(wave<=3)expectedHp*=.6f;
                    Assert.That(stats.HitPoints,Is.EqualTo(expectedHp).Within(.001f));
                    Assert.That(stats.Armor,Is.EqualTo((statWave-1)*10f));
                    if(enemy.hasShield&&enemy.shieldHitPoints.specified)
                        Assert.That(stats.ShieldHitPoints.value,Is.EqualTo(enemy.shieldHitPoints.value*difficulty.hpMultiplier*(1+(statWave-1)*.35f)*(wave<=3?.6f:1f)*openingFactor).Within(.001f));
                    float effectiveHp=stats.HitPoints/DifficultyCalculator.DamageAfterArmor(1,stats.Armor);
                    Assert.Greater(effectiveHp,previousEffectiveHp);
                    previousEffectiveHp=effectiveHp;
                    Assert.AreEqual(enemy.killGold,stats.KillGold);
                }
                var capped=DifficultyCalculator.ApplyAtWave(enemy,difficulty,99,catalog.enemyProgression);
                Assert.That(capped.Armor,Is.EqualTo(90));
            }
            Assert.AreEqual(original,JsonUtility.ToJson(catalog));
        }

        [TestCase("Casual")][TestCase("Standard")][TestCase("Hardcore")]
        public void EarlyWaveReliefOnlyChangesHealthShieldAndSpeedInFirstThreeWaves(string difficultyId)
        {
            var prior=BalanceJson.Parse(json);
            prior.enemyProgression.earlyWaveCount=0;
            foreach(var enemy in catalog.enemies)
            for(int wave=1;wave<=10;wave++)
            {
                var before=DifficultyCalculator.ApplyAtWave(prior.GetEnemy(enemy.id),prior.GetDifficulty(difficultyId),wave,prior.enemyProgression);
                var after=DifficultyCalculator.ApplyAtWave(enemy,catalog.GetDifficulty(difficultyId),wave,catalog.enemyProgression);
                float factor=wave<=3?.6f:1f;
                Assert.That(after.HitPoints,Is.EqualTo(before.HitPoints*factor).Within(.001f),$"{enemy.id} W{wave}");
                Assert.AreEqual(before.HasShield,after.HasShield);
                Assert.AreEqual(before.ShieldHitPoints.specified,after.ShieldHitPoints.specified);
                if(after.HasShield&&after.ShieldHitPoints.specified)
                    Assert.That(after.ShieldHitPoints.value,Is.EqualTo(before.ShieldHitPoints.value*factor).Within(.001f));
                Assert.AreEqual(before.Armor,after.Armor);
                Assert.That(after.MoveSpeed,Is.EqualTo(before.MoveSpeed*(wave<=3?.7f:1f)).Within(.0001f));
                Assert.AreEqual(before.KillGold,after.KillGold);
                Assert.AreEqual(before.LeakDamage,after.LeakDamage);
                if(wave>=4)
                {
                    Assert.AreEqual(before.HitPoints,after.HitPoints,"Later waves must match the previous build exactly");
                    Assert.AreEqual(before.MoveSpeed,after.MoveSpeed);
                    Assert.AreEqual(before.ShieldHitPoints.value,after.ShieldHitPoints.value);
                }
            }
        }

        [TestCase("Casual")][TestCase("Standard")][TestCase("Hardcore")]
        public void FirstWaveMatchesPriorSecondAndSecondGainsTenPercentHealthOnly(string difficultyId)
        {
            var prior=BalanceJson.Parse(json);
            prior.enemyProgression.waveOverrides=null;
            foreach(var enemy in catalog.enemies)
            for(int wave=1;wave<=10;wave++)
            {
                var before=DifficultyCalculator.ApplyAtWave(prior.GetEnemy(enemy.id),prior.GetDifficulty(difficultyId),wave==1?2:wave,prior.enemyProgression);
                var after=DifficultyCalculator.ApplyAtWave(enemy,catalog.GetDifficulty(difficultyId),wave,catalog.enemyProgression);
                float hpFactor=wave==2?1.1f:1f;
                Assert.That(after.HitPoints,Is.EqualTo(before.HitPoints*hpFactor).Within(.001f));
                Assert.That(after.ShieldHitPoints.value,Is.EqualTo(before.ShieldHitPoints.value*hpFactor).Within(.001f));
                Assert.That(after.MoveSpeed,Is.EqualTo(before.MoveSpeed).Within(.0001f));
                Assert.AreEqual(before.Armor,after.Armor);
                Assert.AreEqual(before.KillGold,after.KillGold);
                Assert.AreEqual(before.LeakDamage,after.LeakDamage);
            }
        }

        [TestCase("Casual")][TestCase("Standard")][TestCase("Hardcore")]
        public void EveryTrapLevelKillsFullHealthWaveTenGruntAfterArmor(string difficultyId)
        {
            var grunt=DifficultyCalculator.ApplyAtWave(catalog.GetEnemy("Grunt"),catalog.GetDifficulty(difficultyId),10,catalog.enemyProgression);
            foreach(var level in catalog.GetTower("Trap").levels)
                Assert.GreaterOrEqual(DifficultyCalculator.DamageAfterArmor(level.damagePerHit,grunt.Armor),grunt.HitPoints);
        }

        [Test]
        public void WaveOverrideRejectsDuplicatesOutOfRangeAndInvalidHealth()
        {
            catalog.enemyProgression.waveOverrides[1].wave=1;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
            catalog=BalanceJson.Parse(json);
            catalog.enemyProgression.waveOverrides[0].statWave=11;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.ApplyAtWave(catalog.GetEnemy("Grunt"),catalog.GetDifficulty("Casual"),1,catalog.enemyProgression));
            catalog=BalanceJson.Parse(json);
            catalog.enemyProgression.waveOverrides[0].hpMultiplier=float.NaN;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
        }

        [TestCase(0f)][TestCase(-1f)][TestCase(float.NaN)][TestCase(float.PositiveInfinity)]
        public void EarlyWaveReliefRejectsInvalidMultipliers(float invalid)
        {
            catalog.enemyProgression.earlyWaveHpMultiplier=invalid;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.ApplyAtWave(catalog.GetEnemy("Grunt"),catalog.GetDifficulty("Standard"),1,catalog.enemyProgression));
        }

        [TestCase(0f)][TestCase(-1f)][TestCase(float.NaN)][TestCase(float.PositiveInfinity)]
        public void EarlyWaveReliefRejectsInvalidSpeedMultipliers(float invalid)
        {
            catalog.enemyProgression.earlyWaveSpeedMultiplier=invalid;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.ApplyAtWave(catalog.GetEnemy("Grunt"),catalog.GetDifficulty("Standard"),1,catalog.enemyProgression));
        }

        [TestCase(-1)][TestCase(11)]
        public void EarlyWaveReliefRejectsOutOfCampaignRanges(int count)
        {
            catalog.enemyProgression.earlyWaveCount=count;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.ApplyAtWave(catalog.GetEnemy("Grunt"),catalog.GetDifficulty("Standard"),1,catalog.enemyProgression));
        }

        [TestCase(0f,100f)][TestCase(30f,76.92308f)][TestCase(60f,62.5f)][TestCase(90f,52.63158f)]
        public void ArmorMitigatesProportionallyAcrossHitSizesAndFrameRates(float armor,float expected)
        {
            Assert.That(DifficultyCalculator.DamageAfterArmor(100,armor),Is.EqualTo(expected).Within(.0001f));
            foreach(int fps in new[]{30,60,90})
            {
                float sum=0;
                for(int frame=0;frame<fps;frame++)sum+=DifficultyCalculator.DamageAfterArmor(100f/fps,armor);
                Assert.That(sum,Is.EqualTo(expected).Within(.001f));
            }
            Assert.Greater(DifficultyCalculator.DamageAfterArmor(.001f,armor),0);
        }

        [TestCase(-1f)][TestCase(float.NaN)][TestCase(float.PositiveInfinity)]
        public void ArmorRejectsInvalidInputsAndConfiguration(float invalid)
        {
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.DamageAfterArmor(1,invalid));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.DamageAfterArmor(invalid,1));
            catalog.enemyProgression.armorIncreasePerWave=invalid;
            Assert.Throws<FormatException>(()=>BalanceJson.Validate(catalog));
            Assert.Throws<ArgumentOutOfRangeException>(()=>DifficultyCalculator.ApplyAtWave(catalog.GetEnemy("Grunt"),catalog.GetDifficulty("Standard"),4,catalog.enemyProgression));
        }

        [TestCase(0f)]
        [TestCase(-1f)]
        [TestCase(float.NaN)]
        [TestCase(float.PositiveInfinity)]
        public void ScalingRejectsInvalidHpMultipliers(float multiplier)
        {
            var difficulty = catalog.GetDifficulty("Standard");
            difficulty.hpMultiplier = multiplier;
            Assert.Throws<ArgumentOutOfRangeException>(() => DifficultyCalculator.Apply(catalog.GetEnemy("Grunt"), difficulty));
        }

        [Test]
        public void ScalingRejectsNullAndNumericOverflow()
        {
            var enemy = catalog.GetEnemy("Grunt");
            var difficulty = catalog.GetDifficulty("Standard");
            Assert.Throws<ArgumentNullException>(() => DifficultyCalculator.Apply(null, difficulty));
            Assert.Throws<ArgumentNullException>(() => DifficultyCalculator.Apply(enemy, null));
            difficulty.hpMultiplier = float.MaxValue;
            Assert.Throws<ArgumentOutOfRangeException>(() => DifficultyCalculator.Apply(enemy, difficulty));
        }

        [TestCase("")]
        [TestCase("null")]
        [TestCase("{ broken json")]
        [TestCase("{}")]
        public void ParserRejectsEmptyMalformedOrIncompleteDocuments(string invalidJson)
        {
            Assert.Throws<FormatException>(() => BalanceJson.Parse(invalidJson));
        }

        [Test]
        public void ParserRejectsUnknownEnemyReferences()
        {
            catalog.waves[0].groups[0].enemyId = "MissingEnemy";
            Assert.Throws<FormatException>(() => BalanceJson.Parse(JsonUtility.ToJson(catalog)));
        }

        [Test]
        public void ParserRejectsDuplicateIdsNegativeCountsAndMissingLevels()
        {
            catalog.enemies[1].id = "Grunt";
            Assert.Throws<FormatException>(() => BalanceJson.Validate(catalog));
            catalog = BalanceJson.Parse(json);
            catalog.waves[0].groups[0].count = -1;
            Assert.Throws<FormatException>(() => BalanceJson.Validate(catalog));
            catalog = BalanceJson.Parse(json);
            catalog.towers[0].levels = new TowerLevelDefinition[0];
            Assert.Throws<FormatException>(() => BalanceJson.Validate(catalog));
        }
    }
}
