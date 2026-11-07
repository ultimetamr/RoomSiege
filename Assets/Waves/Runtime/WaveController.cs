using System;
using System.Collections;
using System.Collections.Generic;
using RoomSiege.Data;
using RoomSiege.Enemies;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.UI;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace RoomSiege.Waves
{
    // Runs after enemy movement and tower attacks. Settlement is deferred to LateUpdate
    // so the final lethal leak has fired Leaked even though RESULT was entered first.
    [DefaultExecutionOrder(11100)]
    public sealed class WaveController : MonoBehaviour
    {
        public const float PreparationSeconds = 15f;
        public static WaveController Instance { get; private set; }

        private readonly List<WaveResult> history = new List<WaveResult>();
        private IReadOnlyList<WaveResult> historyView;
        private GameManager manager;
        private EnemyController enemies;
        private TowerHost towers;
        private ScanController scanner;
        private WorldPanelHost panels;
        private BalanceCatalog catalog;
        private WaveHorn horn;
        private WavePanelUi panel;
        private int waveIndex;
        private int kills;
        private int leaks;
        private bool ready;
        private bool running;
        private bool waveActive;
        private bool applicationPaused;
        private bool hasGridMode;
        private ScanMode gridMode;
        private bool roomChanged;
        private bool failurePending;
        public bool AwaitingFirstWave { get; private set; }
        public bool HasDeployedTowers
        {
            get { if(towers==null||towers.Placement==null)return false;
                foreach(var tower in towers.Placement.Towers)if(!tower.Removed)return true;
                return false; }
        }
        private float preparationRemaining;
        private float waveDamage;
        private float waveDuration;
        private bool reinforcementPending;
        private bool warningPlayed;
        private float freezeCooldown;
        private WaveTacticalVisual tacticalVisual;
        public bool ReinforcementPending=>reinforcementPending;
        public float FreezeCooldown=>freezeCooldown;

        public bool IsReady => ready;
        public bool IsRunning => running;
        public bool IsWaveActive => waveActive;
        public bool IsPreparing => running && !waveActive && Outcome == CampaignOutcome.None;
        public CampaignOutcome Outcome { get; private set; }
        public string FinishReason { get; private set; } = "";
        public string LastLeakedEnemyId { get; private set; } = "";
        public int TotalWaves => catalog == null ? 0 : catalog.waves.Length;
        public int CurrentWave => manager == null ? 0 : manager.CurrentWave;
        public int Kills => kills;
        public int Leaks => leaks;
        public float PreparationRemaining => preparationRemaining;
        public IReadOnlyList<WaveResult> Results => historyView ?? (historyView = history.AsReadOnly());
        public WaveDefinition PreviewWave
        {
            get
            {
                int next = waveActive ? waveIndex + 1 : waveIndex;
                return catalog != null && next < TotalWaves ? catalog.waves[next] : null;
            }
        }
        public bool HasPlayableGrid => scanner != null && !scanner.IsScanning && scanner.CurrentGrid != null &&
            scanner.CurrentGrid.SpawnPoints.Count > 0&&towers!=null&&towers.Placement.Routes!=null&&towers.Placement.Routes.AllEntrancesReachable();
        public bool SimulationPaused => applicationPaused || Time.timeScale <= .0001f || (panels != null && panels.Follower != null && panels.Follower.IsPaused);
        public bool IsPaused => SimulationPaused ||
            (panels != null && panels.Follower != null && (panels.Follower.IsPaused || panels.Navigation.HasOpenPage));

        public float Progress
        {
            get
            {
                if (TotalWaves == 0) return 0;
                if (Outcome == CampaignOutcome.Victory) return 1;
                float part = 0;
                if (waveActive)
                {
                    int resolved = kills + leaks;
                    int total = resolved + enemies.ActiveCount + enemies.PendingCount;
                    if (total > 0) part = (float)resolved / total;
                }
                return Mathf.Clamp01((waveIndex + part) / TotalWaves);
            }
        }

        public event Action Changed;
        public event Action<int> WaveStarted;
        public event Action<WaveResult> WaveFinished;
        public event Action<CampaignOutcome> CampaignFinished;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            if (SceneManager.GetActiveScene().name == "RoomSiege_Start" && FindObjectOfType<WaveController>() == null)
                new GameObject("RoomSiege Waves").AddComponent<WaveController>();
        }

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        private IEnumerator Start()
        {
            while ((manager = GameManager.Instance) == null || (enemies = EnemyController.Instance) == null ||
                   !enemies.IsReady || (towers = TowerHost.Instance) == null || towers.Catalog == null ||
                   (scanner = ScanController.Instance) == null || (panels = WorldPanelHost.Instance) == null ||
                   panels.Panels == null || panels.Follower == null) yield return null;

            catalog = towers.Catalog;
            manager.InitializeEconomy(catalog.GetDifficulty(manager.Difficulty == 0 ? "Casual" :
                manager.Difficulty == 2 ? "Hardcore" : "Standard").initialGold);
            horn = gameObject.AddComponent<WaveHorn>();
            tacticalVisual=gameObject.AddComponent<WaveTacticalVisual>();
            gameObject.AddComponent<EntranceWarningVisual>();
            manager.StateChanged += StateChanged;
            manager.CampaignReset += ResetCampaign;
            enemies.Killed += Killed;
            enemies.Leaked += Leaked;
            enemies.DamageDealt += Damage;
            scanner.GridChanged += GridChanged;
            GridChanged(scanner.CurrentGrid);
            ready = true;
            panel = gameObject.AddComponent<WavePanelUi>();
            panel.Initialize(this, manager, enemies, panels);
            panels.NewCampaignRequested += StartNewCampaign;
            panels.RestartRequested += StartNewCampaign;
            panels.MenuRequested += ReturnToMenu;
            panels.StartWaveRequested += StartWaveFromUi;
            panels.TacticalRequested += UseEmergencyFreeze;
            panels.StartingGold = InitialGold;
            panels.CampaignReady = true;
            panels.RoomReady=()=>HasPlayableGrid;
            panels.RoomFailure=()=>towers?.Placement?.Routes?.ReadinessFailure??"";
            StateChanged(manager.State);
        }

        private int InitialGold(int difficulty) => catalog.GetDifficulty(difficulty == 0 ? "Casual" : difficulty == 2 ? "Hardcore" : "Standard").initialGold;

        public void StartNewCampaign()
        {
            if (!ready || !HasPlayableGrid) return;
            // An active battle has to be ended before its transient counters are reset.
            if (running && Outcome == CampaignOutcome.None) FinishCampaign(CampaignOutcome.Interrupted, "重新布防");
            panels.Navigation.CloseCurrent();
            Time.timeScale = 1f;
            panels.Follower.SetPaused(false);
            scanner.BattleLayoutLocked=true;
            manager.ResetCampaign(InitialGold(manager.Difficulty), GameState.BUILD);
        }

        public void ReturnToMenu()
        {
            if (!ready) return;
            if (running && Outcome == CampaignOutcome.None) FinishCampaign(CampaignOutcome.Interrupted, "返回主菜单");
            panels.Navigation.CloseCurrent();
            Time.timeScale = 1f;
            panels.Follower.SetPaused(false);
            scanner.BattleLayoutLocked=false;
            manager.ResetCampaign(InitialGold(manager.Difficulty), GameState.MENU);
        }

        private void ResetCampaign()
        {
            horn.Stop(); enemies.ClearEnemies(); history.Clear();
            waveIndex = kills = leaks = 0;
            waveDamage = waveDuration = preparationRemaining = 0;
            running = waveActive = roomChanged = failurePending = AwaitingFirstWave = false;
            reinforcementPending=warningPlayed=false;freezeCooldown=0;
            tacticalVisual.Clear();
            Outcome = CampaignOutcome.None;
            FinishReason = LastLeakedEnemyId = "";
            Changed?.Invoke();
        }

        public void StartWaveFromUi()
        {
            if (!ready) return;
            if (waveActive) { panels.SetConstructionOpen(false); return; }
            if(AwaitingFirstWave&&!HasDeployedTowers)
            {towers.SetStatus("请先部署至少 1 座塔，再开始第一波；建造准备不限时");return;}
            if (!IsPaused) StartWave();
        }

        private void StateChanged(GameState state)
        {
            if (!ready) return;
            if (state == GameState.RESULT)
            {
                // ResolveLeak raises Leaked AFTER the state transition. Do not settle here.
                failurePending = manager.IsFailed;
                return;
            }
            if (Outcome != CampaignOutcome.None) return;
            if (state == GameState.BUILD && !running)
            {
                running = true;
                waveIndex = 0;
                manager.SetCurrentWave(0);
                AwaitingFirstWave = true;
                BeginPreparation();
            }
            Changed?.Invoke();
        }

        private void BeginPreparation()
        {
            waveActive = false;
            preparationRemaining = PreparationSeconds;
            manager.SetState(GameState.BUILD);
            warningPlayed=false;
            towers.SetStatus(AwaitingFirstWave ? "布防准备不限时 · 选择塔部署完成后，点击开始第 1 波" :
                $"第 {waveIndex + 1}/{TotalWaves} 波前准备 15 秒 · 可购买、升级、卖出");
            Changed?.Invoke();
            if(!AwaitingFirstWave&&(waveIndex==3||waveIndex==6||waveIndex==9)&&towers.Placement.Perks.ChoicesMade<waveIndex/3)
            {
                reinforcementPending=true;
                var perks=towers.Placement.Perks;
                perks.BuildOffers(towers.Placement.Towers,perks.ActiveSupplyTargets?.Invoke(),UnityEngine.Random.Range(0,int.MaxValue));
                var descriptions=new string[3];var names=new string[3];
                for(int i=0;i<3;i++){names[i]=perks.OfferName(perks.Offers[i]);descriptions[i]=perks.OfferDescription(perks.Offers[i]);}
                panels.ShowReinforcements(waveIndex,names,descriptions,SelectReinforcement);
            }
        }

        public void SelectReinforcement(int choice)
        {
            if(!reinforcementPending||!IsPreparing||!enemies.IsDrained)return;
            var perks=towers.Placement.Perks;if(choice<0||choice>=perks.Offers.Count)return;
            var selected=perks.Offers[choice];string selectedName=perks.OfferName(selected);
            if(!perks.SelectOffer(choice,towers.Placement.Towers,perks.ActiveSupplyTargets?.Invoke()))return;
            if(selected.IsCore&&selected.Variant==0)manager.AddCoins(40);
            reinforcementPending=false;panels.CloseReinforcements();
            towers.SetStatus($"第 {waveIndex} 波已清场 · {selectedName}已生效\n第 {waveIndex+1} 波将在 {Mathf.CeilToInt(preparationRemaining)} 秒准备后开始，可提前开波");
            Debug.Log($"[RoomSiege.Waves] reinforcement choice={selected.Key} tier={perks.Rank(selected.TargetId,selected.Variant,selected.IsSupply)} afterWave={waveIndex}");
            Changed?.Invoke();
        }
        public void UseEmergencyFreeze()
        {
            WorldInputPointer.ConsumePagePress();
            if(!waveActive||IsPaused||freezeCooldown>0||scanner.CurrentGrid==null)return;
            Vector3 center=scanner.CurrentGrid.SafeZoneCenter;int affected=0;
            foreach(var enemy in enemies.ActiveEnemies)
                if(enemy.IsAlive&&GridBuilder.HorizontalDistance(enemy.transform.position,center)<=2f)
                {if(enemy.TryFreeze(towers.Placement.Perks.TacticalDuration))affected++;}
            if(affected==0){towers.SetStatus("核心周围 2m 内暂无敌人，冰冻未消耗");return;}
            freezeCooldown=30f*towers.Placement.Perks.TacticalCooldown;tacticalVisual.Pulse(center);horn.Freeze(center);
            towers.SetStatus($"应急冰冻命中 {affected} 个敌人 · 冻结 {towers.Placement.Perks.TacticalDuration:0.#} 秒 · 冷却 {freezeCooldown:0.#} 秒");
            Debug.Log("[RoomSiege.Waves] emergency_freeze affected="+affected);
        }

        private void Update()
        {
            if (!ready) return;
            horn.SetPaused(IsPaused);
            if(!IsPaused&&waveActive)freezeCooldown=Mathf.Max(0,freezeCooldown-Time.deltaTime);
            panels.SetTacticalAction(!waveActive?"应急冰冻 · 战斗中可用":freezeCooldown>0?$"应急冰冻 · 冷却 {Mathf.CeilToInt(freezeCooldown)}s": $"应急冰冻 · 核心周围2m / {towers.Placement.Perks.TacticalDuration:0.#}秒",waveActive&&!IsPaused&&freezeCooldown<=0);
            if (waveActive && !IsPaused && HasPlayableGrid) waveDuration += Time.deltaTime;
            if (!IsPreparing || AwaitingFirstWave || manager.State != GameState.BUILD || IsPaused || !HasPlayableGrid) return;
            if(reinforcementPending)return;
            preparationRemaining = Mathf.Max(0, preparationRemaining - Time.deltaTime);
            if(!warningPlayed&&preparationRemaining<=5f)
            {
                warningPlayed=true;
                foreach(var spawn in scanner.CurrentGrid.SpawnPoints)horn.Warn(spawn.Position);
            }
            if (preparationRemaining <= 0) StartWave();
        }

        private void StartWave()
        {
            if (!IsPreparing || reinforcementPending || !HasPlayableGrid || manager.IsFailed || waveIndex >= TotalWaves) return;
            if(AwaitingFirstWave&&!HasDeployedTowers)return;
            // Never retire live actors or pending spawns when leaving a reinforcement page.
            if(!enemies.IsDrained){towers.SetStatus("当前波敌人仍未清场，等待战斗结算");return;}
            var wave = catalog.waves[waveIndex];
            AwaitingFirstWave = false;
            // Clear BUILD-only test doubles so they never draw fire during a real wave.
            towers.ClearStubs();
            kills = leaks = 0;
            waveDamage = waveDuration = 0;
            waveActive = true;
            manager.SetCurrentWave(wave.number);
            int entranceCount = Mathf.Min(2, scanner.CurrentGrid.SpawnPoints.Count);
            int ordinal = 0;
            foreach (var group in wave.groups)
            {
                // Split each configured group evenly across up to two scanned entrances.
                for (int entrance = 0; entrance < entranceCount; entrance++)
                {
                    int count = group.count / entranceCount + (entrance < group.count % entranceCount ? 1 : 0);
                    if (count == 0) continue;
                    if (!enemies.Enqueue(group.enemyId, count, (entrance + ordinal) % entranceCount))
                    {
                        FinishCampaign(CampaignOutcome.Interrupted, "出怪请求未接受，波次已中止");
                        return;
                    }
                }
                ordinal++;
            }
            manager.SetState(GameState.WAVE);
            foreach(var spawn in scanner.CurrentGrid.SpawnPoints){horn.SetPosition(spawn.Position);horn.Play();}
            towers.SetStatus($"号角响起 · 第 {wave.number}/{TotalWaves} 波");
            WaveStarted?.Invoke(wave.number);
            Changed?.Invoke();
        }

        private void Killed(EnemyKill killed)
        {
            if (!waveActive || Outcome != CampaignOutcome.None) return;
            // EnemyController already credited configured gold exactly once.
            kills++;
            Changed?.Invoke();
        }

        private void Leaked(EnemyLeak leaked)
        {
            if (!waveActive || Outcome != CampaignOutcome.None) return;
            LastLeakedEnemyId = leaked.EnemyId;
            leaks++;
            Changed?.Invoke();
        }

        private void Damage(TowerHit hit, float amount)
        {
            if (waveActive && Outcome == CampaignOutcome.None && hit.SourceTowerId != 0) waveDamage += amount;
        }

        private void GridChanged(GridModel grid)
        {
            if (grid == null) return; // A temporary scan suspends the timer and enemy simulation.
            if (running && waveActive && hasGridMode && grid.Mode != gridMode) roomChanged = true;
            gridMode = grid.Mode;
            hasGridMode = true;
        }

        private void LateUpdate()
        {
            if (!ready || Outcome != CampaignOutcome.None) return;
            if (failurePending || manager.IsFailed || manager.Lives <= 0)
            {
                FinishCampaign(CampaignOutcome.Failure, "防线失守 · 生命归零");
                return;
            }
            if (manager.State == GameState.RESULT || (running && manager.State == GameState.MENU))
            {
                FinishCampaign(CampaignOutcome.Interrupted, "战役已中止");
                return;
            }
            if (roomChanged)
            {
                // EnemyController clears enemies on real/sandbox swaps. Never treat that as a clear.
                if (waveActive) RecordWave(WaveEndReason.Interrupted);
                roomChanged = false;
                enemies.ClearEnemies();
                BeginPreparation(); // Retry the interrupted wave, retaining the already settled wallet.
                return;
            }
            if (!running || !waveActive || manager.State != GameState.WAVE || IsPaused || !HasPlayableGrid) return;
            // Includes pending and living Boss summons, not merely the preset group counts.
            if (!enemies.IsDrained) return;
            RecordWave(WaveEndReason.Cleared);
            waveIndex++;
            if (waveIndex == TotalWaves) FinishCampaign(CampaignOutcome.Victory, "胜利 · 十波全部守住");
            else BeginPreparation();
        }

        private void RecordWave(WaveEndReason reason)
        {
            if (!waveActive) return;
            var result = new WaveResult(CurrentWave, kills, leaks, manager.Coins, waveDamage, waveDuration, reason);
            waveActive = false;
            history.Add(result);
            WaveFinished?.Invoke(result);
        }

        private void FinishCampaign(CampaignOutcome outcome, string status)
        {
            if (Outcome != CampaignOutcome.None) return;
            RecordWave(outcome == CampaignOutcome.Failure ? WaveEndReason.Failed : WaveEndReason.Interrupted);
            Outcome = outcome;
            FinishReason = status;
            reinforcementPending=false;
            running = false;
            failurePending = false;
            preparationRemaining = 0;
            horn.Stop();
            enemies.ClearEnemies();
            if (outcome == CampaignOutcome.Victory) manager.CompleteVictory();
            else manager.SetState(GameState.RESULT);
            towers.SetStatus(status);
            Changed?.Invoke();
            CampaignFinished?.Invoke(outcome);
        }

        private void OnApplicationPause(bool value) { applicationPaused = value; }

        private void OnDestroy()
        {
            if (manager != null) manager.StateChanged -= StateChanged;
            if (manager != null) manager.CampaignReset -= ResetCampaign;
            if (panels != null)
            {
                panels.NewCampaignRequested -= StartNewCampaign; panels.RestartRequested -= StartNewCampaign;
                panels.MenuRequested -= ReturnToMenu; panels.StartWaveRequested -= StartWaveFromUi;
                panels.TacticalRequested -= UseEmergencyFreeze;
                panels.CampaignReady = false; panels.StartingGold = null;panels.RoomReady=null;panels.RoomFailure=null;
            }
            if (enemies != null) { enemies.Killed -= Killed; enemies.Leaked -= Leaked; enemies.DamageDealt -= Damage; }
            if (scanner != null) scanner.GridChanged -= GridChanged;
            if (Instance == this) Instance = null;
        }
    }
}
