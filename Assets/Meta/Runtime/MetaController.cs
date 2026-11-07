using System;
using System.Collections;
using System.Collections.Generic;
using RoomSiege.Data;
using RoomSiege.Enemies;
using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Waves;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomSiege.Meta
{
    [DefaultExecutionOrder(11300)]
    public sealed class MetaController : MonoBehaviour
    {
        public static MetaController Instance { get; private set; }
        private GameManager manager;
        private TowerHost towers;
        private EnemyController enemies;
        private WaveController waves;
        private WorldPanelHost panels;
        private MetaPanelUi panel;
        private bool ready, recorded;
        private float totalDamage, totalDuration;
        private int totalLeaks, totalKills;
        private int completedWaves;
        private MetaSettlement settlement;

        public bool IsReady => ready;
        public MetaSettlement Settlement => settlement;
        public bool HasSettlement => recorded;
        public bool GoldSkinUnlocked => MetaPersistence.GoldSkinUnlocked;
        public bool GoldSkinEquipped => TowerVisuals.GoldSkinEnabled;
        public string DifficultyId => MetaPersistence.DifficultyId(manager == null ? 1 : manager.Difficulty);
        public BalanceCatalog Catalog => towers == null ? null : towers.Catalog;
        public event Action Changed;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name == "RoomSiege_Start" && FindObjectOfType<MetaController>() == null)
                new GameObject("RoomSiege Meta").AddComponent<MetaController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private IEnumerator Start()
        {
            while ((manager = GameManager.Instance) == null || (towers = TowerHost.Instance) == null || towers.Catalog == null ||
                   (enemies = EnemyController.Instance) == null || !enemies.IsReady || (waves = WaveController.Instance) == null ||
                   !waves.IsReady || (panels = WorldPanelHost.Instance) == null || panels.Panels == null) yield return null;

            // Archived medal records remain, while cosmetics now use the paid shop inventory.
            TowerVisuals.SetGoldSkin(false);
            towers.Placement.Changed += TowersChanged;
            enemies.Spawned += EnemySeen;
            waves.WaveFinished += WaveFinished;
            waves.CampaignFinished += CampaignFinished;
            manager.CampaignReset += ResetCampaign;
            ready = true;
            panel = gameObject.AddComponent<MetaPanelUi>();
            panel.Initialize(this, panels);
            TowersChanged();
            foreach (var actor in enemies.ActiveEnemies) if (actor != null && actor.IsAlive) EnemySeen(actor);
            foreach (var result in waves.Results) WaveFinished(result);
            if (waves.Outcome != CampaignOutcome.None) CampaignFinished(waves.Outcome);
            Changed?.Invoke();
        }

        private void TowersChanged()
        {
            bool changed = false;
            // Includes sold/consumed towers: once built, their discovery remains recorded.
            foreach (var tower in towers.Placement.Towers)
                if (tower != null) changed |= MetaPersistence.UnlockTower(tower.Definition.id);
            if (changed) Changed?.Invoke();
        }

        private void ResetCampaign()
        {
            recorded = false; settlement = default;
            totalDamage = totalDuration = 0;
            totalLeaks = totalKills = completedWaves = 0;
            Changed?.Invoke();
        }

        private void EnemySeen(EnemyActor actor)
        {
            if (actor == null || string.IsNullOrWhiteSpace(actor.EnemyId)) return;
            if (MetaPersistence.UnlockEnemy(actor.EnemyId)) Changed?.Invoke();
        }

        private void WaveFinished(WaveResult result)
        {
            totalDamage += result.DamageDealt;
            totalDuration += result.DurationSeconds;
            totalLeaks += result.Leaks;
            totalKills += result.Kills;
            if (result.Reason == WaveEndReason.Cleared) completedWaves++;
            Changed?.Invoke();
        }

        private void CampaignFinished(CampaignOutcome outcome)
        {
            if (recorded || outcome == CampaignOutcome.None) return;
            ProgressionController.Instance?.CloseRun(outcome);
            recorded = true;
            int remainingGold = manager.Coins;
            MetaRating rating = MetaRules.Rate(outcome, manager.Lives, manager.InitialLives, remainingGold);
            int score = MetaRules.Score(outcome, completedWaves, totalKills, totalLeaks, remainingGold, manager.Lives);
            string difficulty = DifficultyId;
            settlement = new MetaSettlement(outcome, rating, manager.Lives, totalLeaks, remainingGold,
                totalDamage, totalDuration, score, difficulty);
            if (outcome == CampaignOutcome.Victory || outcome == CampaignOutcome.Failure)
                MetaPersistence.AddScore(new MetaScoreEntry
            {
                difficulty = difficulty, score = score, rating = rating.ToString(), wave = manager.CurrentWave,
                remainingGold = remainingGold, lives = manager.Lives, leaks = totalLeaks,
                outcome = outcome.ToString(),
                timestamp = DateTime.UtcNow.ToString("O")
            });
            if (rating == MetaRating.Gold)
            {
                MetaPersistence.UnlockGoldSkin();
            }
            Changed?.Invoke();
            panel?.ShowSettlement();
        }

        public void OpenCodex(bool enemiesPage) => panel?.ShowCodex(enemiesPage);
        public void OpenSettlement() { if (recorded) panel?.ShowSettlement(); }

        public void OpenSkinShop()
        {
            ProgressionController.Instance?.GetComponent<ProgressionPanelUi>()?.ShowSkins();
        }

        private void OnDestroy()
        {
            if (towers != null && towers.Placement != null) towers.Placement.Changed -= TowersChanged;
            if (enemies != null) enemies.Spawned -= EnemySeen;
            if (waves != null) { waves.WaveFinished -= WaveFinished; waves.CampaignFinished -= CampaignFinished; }
            if (manager != null) manager.CampaignReset -= ResetCampaign;
            if (Instance == this) Instance = null;
        }
    }
}
