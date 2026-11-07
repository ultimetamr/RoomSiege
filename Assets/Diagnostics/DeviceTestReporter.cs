using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using RoomSiege.Enemies;
using RoomSiege.Scan;
using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Waves;
using UnityEngine;

namespace RoomSiege.Diagnostics
{
    // Passive instrumentation: observes existing events without changing the match.
    [DefaultExecutionOrder(12000)]
    public sealed class DeviceTestReporter : MonoBehaviour
    {
        [Serializable] private sealed class Issue
        {
            public string severity, message, stack, firstUtc, lastUtc;
            public int count;
        }
        [Serializable] private sealed class WaveRow
        {
            public int wave, kills, leaks, gold;
            public float damage, seconds;
            public string reason;
        }
        [Serializable] private sealed class Report
        {
            public string session, startedUtc, updatedUtc, status, outcome = "None";
            public string version, unity, model, os, graphics, difficulty, configSha256;
            public int wave, gold, kills, leaks, activeEnemies, queuedEnemies, peakEnemies;
            public int towerCount, placeableCells, entrances, logOverflow;
            public int campaign;
            public float lives, elapsedSeconds;
            public long sampledFrames, framesOver33ms, framesOver50ms;
            public double frameSeconds;
            public float maxFrameMs;
            public string scanMode, scanStatus;
            public bool coreFollowsPlayer;
            public Vector3 safeZoneCenter;
            public List<WaveRow> waves = new List<WaveRow>();
            public List<Issue> issues = new List<Issue>();
        }
        [Serializable] private sealed class EventRow
        {
            public string utc, kind, detail;
            public float seconds;
            public int wave, gold;
            public float lives;
        }
        [Serializable] private sealed class GridSnapshot
        {
            public string mode;
            public Vector3 safeCenter;
            public List<Vector3> cells = new List<Vector3>();
            public List<Vector3> entrances = new List<Vector3>();
            public List<Vector3> entranceForwards = new List<Vector3>();
            public List<DeploySurface> surfaces;
            public List<RoomVolume> volumes;
            public float floorHeight;
            public string navigation;
        }
        private struct LogRow { public string message, stack; public LogType type; }
        private static DeviceTestReporter instance;
        private readonly object logLock = new object();
        private readonly Queue<LogRow> logs = new Queue<LogRow>();
        private readonly Dictionary<string, string> towerStates = new Dictionary<string, string>();
        private readonly Dictionary<int,float> enemyBirths=new Dictionary<int,float>();
        private readonly Report report = new Report();
        private GameManager manager;
        private TowerHost towers;
        private EnemyController enemies;
        private WaveController waves;
        private ScanController scanner;
        private string folder, lastTowerStatus, lastScanStatus;
        private float nextSave, nextHealth, startedAt;
        private bool paused, storageFailed, readinessReported, hookedPlacement, campaignEnded;
        private int overflow;
        private GridModel reportedGrid;
        private float nextCoreLog;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.BeforeSceneLoad)]
        private static void Bootstrap()
        {
            if (instance == null) new GameObject("RoomSiege Device Test Reporter").AddComponent<DeviceTestReporter>();
        }
        private void Awake()
        {
            if (instance != null) { Destroy(gameObject); return; }
            instance = this;
            DontDestroyOnLoad(gameObject);
            startedAt = Time.realtimeSinceStartup;
            report.session = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss") + "-" + Guid.NewGuid().ToString("N").Substring(0, 8);
            report.startedUtc = Utc();
            report.status = "Running";
            report.version = Application.version;
            report.unity = Application.unityVersion;
            report.model = SystemInfo.deviceModel;
            report.os = SystemInfo.operatingSystem;
            report.graphics = SystemInfo.graphicsDeviceName;
            folder = Path.Combine(Application.persistentDataPath, "TestReports", report.session);
            Application.logMessageReceivedThreaded += CaptureLog;
            try
            {
                Directory.CreateDirectory(folder);
                var config = Resources.Load<TextAsset>("RoomSiegeTowerBalance");
                if (config != null)
                {
                    File.WriteAllText(Path.Combine(folder, "balance.json"), config.text, new UTF8Encoding(false));
                    using (var sha = System.Security.Cryptography.SHA256.Create())
                        report.configSha256 = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(config.text))).Replace("-", "").ToLowerInvariant();
                }
                File.WriteAllText(Path.Combine(Application.persistentDataPath, "TestReports", "latest-session.txt"), report.session);
                Event("session_start", "Local-only device test recording enabled");
                Save();
                Debug.Log("[RoomSiege.TestReport] Recording: " + folder);
            }
            catch (Exception error) { StorageError(error); }
        }
        private static string Utc() => DateTime.UtcNow.ToString("O");
        private void CaptureLog(string message, string stack, LogType type)
        {
            if (storageFailed || message.StartsWith("[RoomSiege.TestReport]", StringComparison.Ordinal)) return;
            if (type == LogType.Log && !message.StartsWith("[RoomSiege", StringComparison.Ordinal)) return;
            lock (logLock)
            {
                if (logs.Count >= 512) { overflow++; return; }
                logs.Enqueue(new LogRow { message = message, stack = stack, type = type });
            }
        }
        private void Attach()
        {
            if (manager == null && GameManager.Instance != null)
            {
                manager = GameManager.Instance;
                manager.StateChanged += StateChanged;
                manager.CoinsChanged += CoinsChanged;
                manager.LivesChanged += LivesChanged;
                manager.CampaignReset += CampaignReset;
                Event("manager_ready", manager.State.ToString());
            }
            if (towers == null) towers = TowerHost.Instance;
            if (!hookedPlacement && towers != null && towers.Placement != null)
            {
                towers.Placement.Changed += TowersChanged;
                hookedPlacement = true;
                TowersChanged();
            }
            if (enemies == null && EnemyController.Instance != null)
            {
                enemies = EnemyController.Instance;
                enemies.Killed += Killed;
                enemies.Leaked += Leaked;
                enemies.Spawned += Spawned;
            }
            if (waves == null && WaveController.Instance != null)
            {
                waves = WaveController.Instance;
                waves.WaveStarted += WaveStarted;
                waves.WaveFinished += WaveFinished;
                waves.CampaignFinished += CampaignFinished;
            }
            if (scanner == null && ScanController.Instance != null)
            {
                scanner = ScanController.Instance;
                scanner.GridChanged += GridChanged;
                GridChanged(scanner.CurrentGrid);
            }
        }
        private void Update()
        {
            Attach();
            DrainLogs();
            if (!paused && !campaignEnded && manager != null && manager.State == GameState.WAVE && waves != null && !waves.IsPaused)
            {
                float dt = Time.unscaledDeltaTime;
                report.sampledFrames++;
                report.frameSeconds += dt;
                if (dt > .033333f) report.framesOver33ms++;
                if (dt > .05f) report.framesOver50ms++;
                report.maxFrameMs = Mathf.Max(report.maxFrameMs, dt * 1000);
            }
            if (Time.realtimeSinceStartup >= nextHealth)
            {
                nextHealth = Time.realtimeSinceStartup + .5f;
                Health();
            }
            if (Time.realtimeSinceStartup >= nextSave)
            {
                nextSave = Time.realtimeSinceStartup + 5;
                Save();
            }
        }
        private void DrainLogs()
        {
            while (true)
            {
                LogRow row;
                lock (logLock)
                {
                    if (logs.Count == 0) break;
                    row = logs.Dequeue();
                }
                if (row.type != LogType.Log) AddIssue(row.type.ToString(), row.message, row.stack);
                Event("log_" + row.type, row.message + (string.IsNullOrEmpty(row.stack) ? "" : "\n" + row.stack));
            }
            lock (logLock) report.logOverflow = overflow;
        }
        private void Health()
        {
            if (!readinessReported && Time.realtimeSinceStartup - startedAt >= 30)
            {
                readinessReported = true;
                if (manager == null || towers == null || towers.Catalog == null || enemies == null || !enemies.IsReady || waves == null || !waves.IsReady)
                    AddIssue("Observation", "30 秒后部分模块仍未就绪", "检查 events.jsonl 中各模块启动及异常日志；尚不能据此确定原因。");
            }
            if (towers != null && towers.Status != lastTowerStatus)
            { lastTowerStatus = towers.Status; Event("tower_status", lastTowerStatus); }
            if (scanner != null && scanner.Status != lastScanStatus)
            { lastScanStatus = scanner.Status; Event("scan_status", lastScanStatus); }
            if (enemies != null)
            {
                report.peakEnemies = Mathf.Max(report.peakEnemies, enemies.ActiveCount);
                if (enemies.ActiveCount > EnemyController.MaximumActive)
                    AddIssue("Error", "同屏敌人超过 20", enemies.ActiveCount.ToString());
            }
            var camera = Camera.main;
            if (camera != null && (!Valid(camera.transform.position) || !Valid(camera.transform.rotation)))
                AddIssue("Error", "相机位姿包含 NaN 或 Infinity", "定时采样发现无效位姿");
            var panels = WorldPanelHost.Instance;
            if (panels != null && panels.Follower != null && !Valid(panels.Follower.transform.position))
                AddIssue("Error", "面板位姿包含 NaN 或 Infinity", "定时采样发现无效位姿");
        }
        private static bool Valid(Vector3 v) => Finite(v.x) && Finite(v.y) && Finite(v.z);
        private static bool Valid(Quaternion v) => Finite(v.x) && Finite(v.y) && Finite(v.z) && Finite(v.w);
        private static bool Finite(float v) => !float.IsNaN(v) && !float.IsInfinity(v);
        private void AddIssue(string severity, string message, string stack)
        {
            var issue = report.issues.Find(x => x.severity == severity && x.message == message && x.stack == stack);
            if (issue == null)
            {
                if (report.issues.Count >= 100) return; // Full raw records remain in events.jsonl.
                issue = new Issue { severity = severity, message = message, stack = stack, firstUtc = Utc() };
                report.issues.Add(issue);
            }
            issue.lastUtc = Utc();
            issue.count++;
        }
        private void StateChanged(GameState value) { Event("state", value.ToString()); Save(); }
        private void CoinsChanged(int value) => Event("gold", value.ToString());
        private void LivesChanged(float value) => Event("lives", value.ToString(CultureInfo.InvariantCulture));
        private void Spawned(EnemyActor actor)
        {
            enemyBirths[actor.TargetId]=Time.realtimeSinceStartup;
            bool bodyReady=actor.GetComponent<EnemyVisual>().BodyReady;
            Event("spawn",$"id={actor.TargetId} type={actor.EnemyId} position={actor.transform.position:F3} bodyReady={bodyReady} hp={actor.MaximumHitPoints:F2} shield={actor.MaximumShield:F2} armor={actor.Armor:F1} protection={EnemyActor.EntranceProtectionSeconds:F1}s");
            if(!bodyReady)AddIssue("Error","敌人出生时模型未就绪",actor.EnemyId);
        }
        private void Killed(EnemyKill value)
        {
            report.kills++;
            string lifetime=enemyBirths.TryGetValue(value.SpawnId,out float born)?(Time.realtimeSinceStartup-born).ToString("F3",CultureInfo.InvariantCulture):"unknown";
            enemyBirths.Remove(value.SpawnId);
            Event("kill", value.EnemyId + " gold=" + value.Gold + " summoned=" + value.IsSummoned+" aliveSeconds="+lifetime);
        }
        private void Leaked(EnemyLeak value) { enemyBirths.Remove(value.SpawnId); report.leaks++; Event("leak", value.EnemyId + " damage=" + value.LifeDamage.ToString(CultureInfo.InvariantCulture)); }
        private void WaveStarted(int value) { enemyBirths.Clear(); Event("wave_start", value.ToString()); Save(); }
        private void WaveFinished(WaveResult value)
        {
            report.waves.Add(new WaveRow { wave = value.WaveNumber, kills = value.Kills, leaks = value.Leaks,
                gold = value.RemainingGold, damage = value.DamageDealt, seconds = value.DurationSeconds, reason = value.Reason.ToString() });
            Event("wave_end", JsonUtility.ToJson(report.waves[report.waves.Count - 1]));
            Save();
        }
        private void CampaignFinished(CampaignOutcome value)
        {
            report.outcome = value.ToString();
            report.status = "CampaignEnded";
            campaignEnded = true;
            Event("campaign_end", value.ToString());
            DrainLogs();
            Save();
            if (!storageFailed)
            {
                try
                {
                    WriteSnapshot($"campaign-{report.campaign:D3}.json",JsonUtility.ToJson(report,true));
                    WriteSnapshot($"campaign-{report.campaign:D3}.md",Markdown());
                }
                catch(Exception error){StorageError(error);}
            }
        }
        private void CampaignReset()
        {
            if(manager.State==GameState.BUILD)report.campaign++;
            campaignEnded=false;
            report.status=manager.State==GameState.MENU?"Menu":"Running";
            report.outcome="None";
            report.kills=report.leaks=report.peakEnemies=0;
            report.waves.Clear();towerStates.Clear();enemyBirths.Clear();
            report.sampledFrames=report.framesOver33ms=report.framesOver50ms=0;
            report.frameSeconds=report.maxFrameMs=0;
            Event("campaign_reset","campaign="+report.campaign+" phase="+manager.State);
            Save();
        }
        private void TowersChanged()
        {
            if (towers == null || towers.Placement == null) return;
            foreach (var tower in towers.Placement.Towers)
            {
                string key = tower.Id.ToString();
                string value = tower.Definition.id + " Lv" + tower.Level + " invested=" + tower.InvestedGold +
                    " removed=" + tower.Removed + " consumed=" + tower.Consumed + " cell=" + tower.Cell.Center;
                if (!towerStates.TryGetValue(key, out string previous) || previous != value)
                { towerStates[key] = value; Event("tower_change", "id=" + key + " " + value); }
            }
        }
        private void GridChanged(GridModel value)
        {
            if (value == null) {reportedGrid=null;Event("grid", "Scanning / unavailable"); return; }
            report.scanMode = value.Mode.ToString();
            report.placeableCells = value.PlaceableCells.Count;
            report.entrances = value.SpawnPoints.Count;
            report.safeZoneCenter=value.SafeZoneCenter;
            if(reportedGrid!=null&&ReferenceEquals(reportedGrid.SupportedCells,value.SupportedCells))
            {
                // Movement must not serialize every deployment cell and rewrite
                // two report files at the follow-update rate.
                reportedGrid=value;
                if(Time.realtimeSinceStartup>=nextCoreLog)
                {nextCoreLog=Time.realtimeSinceStartup+1;Event("core_move",value.SafeZoneCenter.ToString("F2"));}
                return;
            }
            reportedGrid=value;
            var grid = new GridSnapshot { mode = value.Mode.ToString(), safeCenter = value.SafeZoneCenter };
            foreach (var cell in value.PlaceableCells) grid.cells.Add(cell.Center);
            foreach (var spawn in value.SpawnPoints){grid.entrances.Add(spawn.Position);grid.entranceForwards.Add(spawn.Forward);}
            if(scanner?.CurrentRoom!=null)
            {
                grid.surfaces=scanner.CurrentRoom.Surfaces;grid.volumes=scanner.CurrentRoom.Volumes;
                grid.floorHeight=scanner.CurrentRoom.FloorHeight;
                // Subscribers can receive the grid before TowerHost adopts it.
                // Diagnose the published geometry rather than the previous route map.
                IReadOnlyList<TowerInstance> activeTowers=towers?.Placement?.Towers;
                grid.navigation=new TowerRouteMap(value,activeTowers??Array.Empty<TowerInstance>()).NavigationSummary;
                WriteSnapshot("room-layout.json",JsonUtility.ToJson(grid,true));
                Debug.Log("[RoomSiege.RoomReadiness] "+grid.navigation);
            }
            Event("grid", JsonUtility.ToJson(grid));
            Save();
        }
        private void Event(string kind, string detail)
        {
            if (storageFailed) return;
            try
            {
                var row = new EventRow { utc = Utc(), seconds = Time.realtimeSinceStartup - startedAt,
                    kind = kind, detail = detail, wave = manager == null ? 0 : manager.CurrentWave,
                    gold = manager == null ? 0 : manager.Coins, lives = manager == null ? 0 : manager.Lives };
                File.AppendAllText(Path.Combine(folder, "events.jsonl"), JsonUtility.ToJson(row) + "\n", new UTF8Encoding(false));
            }
            catch (Exception error) { StorageError(error); }
        }
        private void Save()
        {
            if (storageFailed) return;
            report.updatedUtc = Utc();
            report.elapsedSeconds = Time.realtimeSinceStartup - startedAt;
            if (manager != null)
            {
                report.wave = manager.CurrentWave; report.gold = manager.Coins; report.lives = manager.Lives;
                report.difficulty = manager.Difficulty == 0 ? "Casual" : manager.Difficulty == 2 ? "Hardcore" : "Standard";
            }
            if (enemies != null) { report.activeEnemies = enemies.ActiveCount; report.queuedEnemies = enemies.PendingCount; }
            report.towerCount = 0;
            if (towers != null && towers.Placement != null)
                foreach (var tower in towers.Placement.Towers) if (!tower.Removed) report.towerCount++;
            if (scanner != null)
            {
                report.scanStatus=scanner.Status;report.coreFollowsPlayer=scanner.CoreFollowsPlayer;
                if(scanner.CurrentGrid!=null)report.safeZoneCenter=scanner.CurrentGrid.SafeZoneCenter;
            }
            try
            {
                // Replace via rename so an interrupted write keeps the previous checkpoint intact.
                WriteSnapshot("report.json", JsonUtility.ToJson(report, true));
                WriteSnapshot("report.md", Markdown());
            }
            catch (Exception error) { StorageError(error); }
        }
        private void WriteSnapshot(string name, string content)
        {
            string path = Path.Combine(folder, name), temp = path + ".tmp", previous = path + ".previous";
            File.WriteAllText(temp, content, new UTF8Encoding(false));
            if (File.Exists(previous)) File.Delete(previous);
            if (File.Exists(path)) File.Move(path, previous);
            File.Move(temp, path);
        }
        private static string Cell(string text) => (text ?? "").Replace("|", "\\|").Replace("\r", "").Replace("\n", "<br>");
        private string Markdown()
        {
            var b = new StringBuilder("# RoomSiege 真机测试报告\n\n");
            b.AppendLine("- 会话：" + report.session);
            b.AppendLine("- 本次启动内的战役编号："+report.campaign);
            b.AppendLine("- UTC 开始 / 更新：" + report.startedUtc + " / " + report.updatedUtc);
            b.AppendLine("- 状态 / 结局：" + report.status + " / " + report.outcome);
            b.AppendLine("- 设备 / 系统：" + report.model + " / " + report.os);
            b.AppendLine("- 版本 / Unity：" + report.version + " / " + report.unity);
            b.AppendLine("- 配置 SHA256：" + report.configSha256);
            b.AppendLine("- 难度 / 波次：" + report.difficulty + " / " + report.wave);
            b.AppendLine("- 生命 / 金币 / 击杀 / 漏怪：" + report.lives + " / " + report.gold + " / " + report.kills + " / " + report.leaks);
            b.AppendLine("- 当前塔 / 敌人峰值：" + report.towerCount + " / " + report.peakEnemies);
            b.AppendLine("- 扫描模式 / 可放格 / 入口：" + report.scanMode + " / " + report.placeableCells + " / " + report.entrances);
            b.AppendLine("- 守护核心："+(report.coreFollowsPlayer?"跟随玩家":"固定位置")+" / "+report.safeZoneCenter.ToString("F2"));
            b.AppendLine("\n## 每波结果\n\n| 波次 | 原因 | 击杀 | 漏怪 | 金币 | 有效伤害 | 战斗秒数 |\n|---|---|---|---|---|---|---|");
            foreach (var row in report.waves) b.AppendLine($"| {row.wave} | {row.reason} | {row.kills} | {row.leaks} | {row.gold} | {row.damage:F1} | {row.seconds:F1} |");
            b.AppendLine("\n## 自动记录的问题与警告\n");
            if (report.issues.Count == 0) b.AppendLine("当前未记录到自动检测项；不代表视觉、交互、空间音频或游戏平衡已经通过验收。");
            foreach (var issue in report.issues)
                b.AppendLine("\n### " + Cell(issue.severity) + " ×" + issue.count + "\n\n" + Cell(issue.message) + "\n\n首次 " + issue.firstUtc + "；末次 " + issue.lastUtc + "\n\n" + Cell(issue.stack));
            b.AppendLine("\n## 帧耗时采样\n");
            b.AppendLine("仅统计未暂停且战役未结束的 WAVE 帧，含同步报告写入开销；此项为 Unity CPU 帧间隔，不能替代头显合成器/GPU 帧率分析。");
            b.AppendLine($"\n采样 {report.sampledFrames} 帧；平均 {(report.sampledFrames > 0 ? report.frameSeconds * 1000 / report.sampledFrames : 0):F2}ms；最大 {report.maxFrameMs:F2}ms；超过 33.3ms：{report.framesOver33ms}；超过 50ms：{report.framesOver50ms}。");
            b.AppendLine("\n## 试玩者反馈 / 待人工确认\n\n待试玩结束后补充：操作步骤、预期与实际表现、发生波次。未自动检测视觉闪烁、遮挡、声音方向或难度是否合理。警告需结合原始日志判因，胜负或漏怪本身不视为程序缺陷。");
            b.AppendLine("\n## 原始记录\n\nevents.jsonl：状态、经济、建造/升级/卖出、扫描格、击杀/漏怪、异常堆栈。balance.json：本局配置快照。每 5 秒及关键事件保存；突然断电/原生崩溃可能丢失最后数秒，需结合 Android 日志。未上传任何网络服务。");
            if (report.logOverflow > 0) b.AppendLine("\n日志队列溢出，未收录条数：" + report.logOverflow);
            return b.ToString();
        }
        private void OnApplicationPause(bool value)
        {
            paused = value; Event("application_pause", value.ToString()); DrainLogs(); Save();
        }
        private void OnApplicationFocus(bool value) => Event("application_focus", value.ToString());
        private void OnApplicationQuit()
        {
            if (!campaignEnded) report.status = "AppExitedBeforeCampaignEnd";
            Event("application_quit", report.status); DrainLogs(); Save();
        }
        private void StorageError(Exception error)
        {
            storageFailed = true;
            Debug.LogError("[RoomSiege.TestReport] Local report write failed: " + error);
        }
        private void OnDestroy()
        {
            if (instance != this) return;
            Application.logMessageReceivedThreaded -= CaptureLog;
            if (manager != null) { manager.StateChanged -= StateChanged; manager.CoinsChanged -= CoinsChanged; manager.LivesChanged -= LivesChanged; manager.CampaignReset -= CampaignReset; }
            if (hookedPlacement && towers != null && towers.Placement != null) towers.Placement.Changed -= TowersChanged;
            if (enemies != null) { enemies.Killed -= Killed; enemies.Leaked -= Leaked; enemies.Spawned -= Spawned; }
            if (waves != null) { waves.WaveStarted -= WaveStarted; waves.WaveFinished -= WaveFinished; waves.CampaignFinished -= CampaignFinished; }
            if (scanner != null) scanner.GridChanged -= GridChanged;
            instance = null;
        }
    }
}
