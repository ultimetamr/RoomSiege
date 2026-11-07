using System;

namespace RoomSiege.Data
{
    public readonly struct EnemyStats
    {
        public string Id { get; }
        public float HitPoints { get; }
        public float MoveSpeed { get; }
        public int KillGold { get; }
        public float LeakDamage { get; }
        public bool HasShield { get; }
        public OptionalFloat ShieldHitPoints { get; }
        public float Armor { get; }

        internal EnemyStats(EnemyDefinition enemy, float hp, float speed, float leak, OptionalFloat shield, float armor = 0)
        {
            Id = enemy.id;
            HitPoints = hp;
            MoveSpeed = speed;
            KillGold = enemy.killGold;
            LeakDamage = leak;
            HasShield = enemy.hasShield;
            ShieldHitPoints = shield;
            Armor = armor;
        }
    }

    public readonly struct CampaignTotals
    {
        public int EnemyCount { get; }
        public int KillGold { get; }
        public double HitPoints { get; }
        public double LeakDamage { get; }

        internal CampaignTotals(int count, int gold, double hp, double leak)
        {
            EnemyCount = count;
            KillGold = gold;
            HitPoints = hp;
            LeakDamage = leak;
        }
    }

    public static class DifficultyCalculator
    {
        // Resolve once on spawn from immutable base definitions. Summons use
        // their own base stats and the current wave, never the parent's scaled HP.
        public static EnemyStats ApplyAtWave(EnemyDefinition enemy,DifficultyDefinition difficulty,int wave,EnemyProgressionDefinition progression)
        {
            if(progression==null)throw new ArgumentNullException(nameof(progression));
            if(wave<1||progression.maximumWave<1)throw new ArgumentOutOfRangeException(nameof(wave));
            NonNegative(progression.hpIncreasePerWave,nameof(progression.hpIncreasePerWave));
            NonNegative(progression.armorIncreasePerWave,nameof(progression.armorIncreasePerWave));
            NonNegative(progression.speedIncreasePerWave,nameof(progression.speedIncreasePerWave));
            if(progression.earlyWaveCount<0||progression.earlyWaveCount>progression.maximumWave)
                throw new ArgumentOutOfRangeException(nameof(progression.earlyWaveCount));
            Positive(progression.earlyWaveHpMultiplier,nameof(progression.earlyWaveHpMultiplier));
            Positive(progression.earlyWaveSpeedMultiplier,nameof(progression.earlyWaveSpeedMultiplier));
            var baseline=Apply(enemy,difficulty);
            int statWave=Math.Min(wave,progression.maximumWave);
            float overrideHp=1f;
            if(progression.waveOverrides!=null)
                foreach(var item in progression.waveOverrides)
                {
                    if(item==null||item.wave<1||item.wave>progression.maximumWave||item.statWave<1||item.statWave>progression.maximumWave)
                        throw new ArgumentOutOfRangeException(nameof(progression.waveOverrides));
                    Positive(item.hpMultiplier,nameof(item.hpMultiplier));
                    if(item.wave==wave){statWave=item.statWave;overrideHp=item.hpMultiplier;}
                }
            int completed=statWave-1;
            float hpFactor=1+completed*progression.hpIncreasePerWave;
            // Early-wave relief is local to these waves; later stats still resolve from base.
            if(wave<=progression.earlyWaveCount)hpFactor*=progression.earlyWaveHpMultiplier;
            hpFactor*=overrideHp;
            float speedFactor=1+completed*progression.speedIncreasePerWave;
            if(wave<=progression.earlyWaveCount)speedFactor*=progression.earlyWaveSpeedMultiplier;
            float hp=baseline.HitPoints*hpFactor,speed=baseline.MoveSpeed*speedFactor;
            float armor=completed*progression.armorIncreasePerWave;
            NonNegative(armor,nameof(armor));
            Positive(hp,nameof(hp));Positive(speed,nameof(speed));
            var shield=baseline.ShieldHitPoints;
            if(baseline.HasShield&&shield.specified){shield.value*=hpFactor;Positive(shield.value,nameof(shield));}
            return new EnemyStats(enemy,hp,speed,baseline.LeakDamage,shield,armor);
        }

        // Proportional mitigation works equally for single hits and per-frame beams.
        // Never subtract flat armor per hit: that would make continuous weapons harmless.
        public static float DamageAfterArmor(float amount, float armor)
        {
            NonNegative(amount,nameof(amount));
            NonNegative(armor,nameof(armor));
            return amount * (100f / (100f + armor));
        }

        // Pure transformation: no config mutation, rounding, Unity lifecycle, or global state.
        // HP scales both body HP and any configured shield HP; leak damage keeps fractions.
        public static EnemyStats Apply(EnemyDefinition enemy, DifficultyDefinition difficulty)
        {
            if (enemy == null) throw new ArgumentNullException(nameof(enemy));
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            Positive(difficulty.hpMultiplier, nameof(difficulty.hpMultiplier));
            Positive(difficulty.speedMultiplier, nameof(difficulty.speedMultiplier));
            NonNegative(difficulty.leakMultiplier, nameof(difficulty.leakMultiplier));
            Positive(enemy.hitPoints, nameof(enemy.hitPoints));
            Positive(enemy.moveSpeed, nameof(enemy.moveSpeed));
            NonNegative(enemy.leakDamage, nameof(enemy.leakDamage));
            var hp = enemy.hitPoints * difficulty.hpMultiplier;
            var speed = enemy.moveSpeed * difficulty.speedMultiplier;
            var leak = enemy.leakDamage * difficulty.leakMultiplier;
            Positive(hp, nameof(hp));
            Positive(speed, nameof(speed));
            NonNegative(leak, nameof(leak));
            var shield = enemy.shieldHitPoints;
            if (enemy.hasShield && shield.specified)
            {
                Positive(shield.value, nameof(enemy.shieldHitPoints));
                shield.value *= difficulty.hpMultiplier;
                Positive(shield.value, nameof(shield));
            }
            return new EnemyStats(enemy, hp, speed, leak, shield);
        }

        // Only explicitly listed wave enemies count. Summons and shield HP are excluded.
        public static CampaignTotals CalculateCampaignTotals(BalanceCatalog catalog, DifficultyDefinition difficulty)
        {
            if (catalog == null) throw new ArgumentNullException(nameof(catalog));
            if (difficulty == null) throw new ArgumentNullException(nameof(difficulty));
            int count = 0, gold = 0;
            double hp = 0, leak = 0;
            foreach (var wave in catalog.waves)
            foreach (var group in wave.groups)
            {
                var stats = Apply(catalog.GetEnemy(group.enemyId), difficulty);
                checked { count += group.count; gold += stats.KillGold * group.count; }
                hp += (double)stats.HitPoints * group.count;
                leak += (double)stats.LeakDamage * group.count;
            }
            return new CampaignTotals(count, gold, hp, leak);
        }

        private static void Positive(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f)
                throw new ArgumentOutOfRangeException(name, "Must be finite and positive.");
        }

        private static void NonNegative(float value, string name)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value < 0f)
                throw new ArgumentOutOfRangeException(name, "Must be finite and nonnegative.");
        }
    }
}
