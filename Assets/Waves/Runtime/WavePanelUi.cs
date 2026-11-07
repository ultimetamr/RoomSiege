using System.Text;
using RoomSiege.Data;
using RoomSiege.Enemies;
using RoomSiege.UI;
using RoomSiege.Visuals;
using UnityEngine;
using UnityEngine.UI;

namespace RoomSiege.Waves
{
    // Adapts the existing right/top canvases; their head following and fade stay in UI/.
    public sealed class WavePanelUi : MonoBehaviour
    {
        private WaveController waves;
        private GameManager manager;
        private EnemyController enemies;
        private Text preview, life, gold, statistics, hint, progressLabel, hudLife, hudGold;
        private ProgressRingGraphic ring;
        private WorldPanelHost panels;
        private int displayedSeconds = -1;
        private bool displayedPaused;
        private bool displayedGrid;
        private float displayedProgress = -1;
        private bool displayedTowers;
        private readonly CoreDamageNotice damageNotice=new CoreDamageNotice();
        public WavePhaseNotice PhaseNotice { get; } = new WavePhaseNotice();
        private CoreHitVisual coreHit;
        private string normalHint;
        private float previousLives,lastLifeLoss;

        public void Initialize(WaveController controller, GameManager gameManager,
            EnemyController enemyController, WorldPanelHost panels)
        {
            waves = controller;
            this.panels = panels;
            manager = gameManager;
            previousLives=manager.Lives;
            enemies = enemyController;
            var right = panels.Panels[2].transform;
            var top = panels.Panels[3].transform;
            hudLife=top.Find("HUD life").GetComponent<Text>();hudGold=top.Find("HUD gold").GetComponent<Text>();
            preview = right.Find("Info label 0").GetComponent<Text>();
            life = right.Find("Info label 1").GetComponent<Text>();
            gold = right.Find("Info label 2").GetComponent<Text>();
            hint = right.Find("Info hint").GetComponent<Text>();
            Layout(right.Find("Info row 0"), new Vector2(0, 60), new Vector2(272, 144));
            Layout(preview.transform, new Vector2(0, 47), new Vector2(252, 164));
            right.Find("Info row 0").GetComponent<Image>().color=Color.clear;
            preview.alignment=TextAnchor.UpperLeft;
            Layout(right.Find("Info row 1"), new Vector2(-68, -30), new Vector2(128, 68));
            Layout(life.transform, new Vector2(-68, -30), new Vector2(124, 64));
            Layout(right.Find("Info row 2"), new Vector2(68, -30), new Vector2(128, 68));
            Layout(gold.transform, new Vector2(68, -30), new Vector2(124, 64));
            Layout(hint.transform, new Vector2(0, -162), new Vector2(252, 58));
            // Life and money live in the single top HUD; keep legacy nodes for module references.
            for(int i=1;i<=2;i++)
            { panels.RegisterContent(right.Find("Info row "+i).gameObject);panels.RegisterContent(right.Find("Info label "+i).gameObject); }
            SetFont(preview, 19);
            SetFont(life, 22);
            SetFont(gold, 22);
            gold.color = RoomSiegeStyle.Gold;
            SetFont(hint, 15);

            var statsObject = new GameObject("Wave settlement", typeof(RectTransform), typeof(Text));
            statsObject.layer = 5;
            statsObject.transform.SetParent(right, false);
            Layout(statsObject.transform, new Vector2(0, -87), new Vector2(252, 88));
            statistics = statsObject.GetComponent<Text>();
            statistics.font = preview.font;
            statistics.material = preview.material;
            statistics.color = preview.color;
            statistics.alignment = TextAnchor.MiddleLeft;
            statistics.raycastTarget = false;
            SetFont(statistics, 17);
            panels.RegisterContent(statsObject, GameState.BUILD, GameState.WAVE);
            var rule=RoomSiegeStyle.AddRule(right,preview.material,new Vector2(0,-38),new Vector2(252,1),RoomSiegeStyle.Rule);
            panels.RegisterContent(rule.gameObject,GameState.BUILD,GameState.WAVE);

            ring = top.GetComponentInChildren<ProgressRingGraphic>(true);
            var label = top.Find("Progress label");
            progressLabel = label != null ? label.GetComponent<Text>() : null;
            waves.Changed += Refresh;
            waves.WaveStarted += WaveStarted;
            waves.WaveFinished += WaveFinished;
            waves.CampaignFinished += CampaignFinished;
            enemies.Spawned += Spawned;
            manager.CoinsChanged += CoinsChanged;
            manager.LivesChanged += LivesChanged;
            enemies.CountsChanged += Refresh;
            enemies.Leaked+=Leaked;
            manager.CampaignReset+=ResetNotice;
            coreHit=gameObject.AddComponent<CoreHitVisual>();
            Refresh();
        }

        private static void Layout(Transform node, Vector2 position, Vector2 size)
        {
            if (node == null) return;
            var rect = node.GetComponent<RectTransform>();
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
        }

        private static void SetFont(Text text, int size)
        {
            RoomSiegeStyle.StyleText(text, size);
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
        }

        private void LateUpdate()
        {
            if (waves == null) return;
            int seconds = Mathf.CeilToInt(waves.PreparationRemaining);
            if (seconds != displayedSeconds || displayedPaused != waves.IsPaused ||
                displayedGrid != waves.HasPlayableGrid || displayedTowers!=waves.HasDeployedTowers || Mathf.Abs(waves.Progress - displayedProgress) > .005f)
                Refresh();
            damageNotice.Tick(Time.deltaTime,waves.IsPaused);
            PhaseNotice.Tick(Time.deltaTime,waves.IsPaused);
            ApplyNotice();
            ApplyProgress();
        }

        private void CoinsChanged(int value) { if (gold != null) gold.text = "金币\n" + value + "G";if(hudGold!=null)hudGold.text="金币\n"+value+" G"; }
        private void LivesChanged(float value)
        {
            lastLifeLoss=Mathf.Max(0,previousLives-value);previousLives=value;
            if(life!=null)life.text="生命\n"+value.ToString("0.##");if(hudLife!=null)hudLife.text="核心生命\n"+value.ToString("0.##");
        }
        private void Leaked(EnemyLeak leak)
        {
            damageNotice.Add(new EnemyLeak(leak.SpawnId,leak.EnemyId,lastLifeLoss));coreHit.Pulse();ApplyNotice();
        }
        private void ResetNotice(){damageNotice.Clear();PhaseNotice.Reset();coreHit.Clear();ApplyNotice();ApplyProgress();}
        private void WaveStarted(int number){PhaseNotice.BeginWave(number,waves.TotalWaves);ApplyProgress();}
        private void WaveFinished(WaveResult result){PhaseNotice.EndWave(result,waves.TotalWaves);ApplyProgress();}
        private void CampaignFinished(CampaignOutcome outcome){PhaseNotice.Reset();ApplyProgress();}
        private void Spawned(EnemyActor actor)
        {
            if(actor!=null&&actor.EnemyId=="Boss"&&waves.IsWaveActive&&PhaseNotice.BossArrived())
            {ApplyProgress();Debug.Log("[RoomSiege.Waves] Boss arrival cue; wave="+waves.CurrentWave);}
        }
        private void ApplyNotice()
        {
            if(hint==null||life==null)return;
            hint.text=damageNotice.Visible?damageNotice.Summary:normalHint;
            hint.color=damageNotice.Visible?RoomSiegeStyle.Enemy:RoomSiegeStyle.Text;
            life.color=damageNotice.Visible?RoomSiegeStyle.Enemy:
                manager!=null&&manager.Lives<=manager.InitialLives*.3f?RoomSiegeStyle.Gold:RoomSiegeStyle.Text;
            if(hudLife!=null)hudLife.color=life.color;
        }

        private void Refresh()
        {
            if (waves == null || preview == null) return;
            displayedSeconds = Mathf.CeilToInt(waves.PreparationRemaining);
            displayedPaused = waves.IsPaused;
            displayedGrid = waves.HasPlayableGrid;
            displayedProgress = waves.Progress;
            displayedTowers=waves.HasDeployedTowers;
            CoinsChanged(manager.Coins);
            if(life!=null)life.text="生命\n"+manager.Lives.ToString("0.##");if(hudLife!=null)hudLife.text="核心生命\n"+manager.Lives.ToString("0.##");

            string phase = waves.Outcome == CampaignOutcome.Victory ? "胜利 · 十波完成" :
                waves.Outcome == CampaignOutcome.Failure ? "失败 · 防线失守" :
                waves.Outcome == CampaignOutcome.Interrupted ? "战役已中止" :
                waves.IsWaveActive ? $"第 {waves.CurrentWave}/{waves.TotalWaves} 波 · 号角已响" :
                waves.AwaitingFirstWave ? "布防准备 · 不限时" : waves.IsPreparing ? $"准备期 · {displayedSeconds} 秒" : "请选择房间与难度";
            var text = new StringBuilder(phase);
            if (waves.Outcome == CampaignOutcome.None)
            {
                WaveDefinition next = waves.PreviewWave;
                if (next == null) text.Append("\n最终波 · 无下一波");
                else
                {
                    text.Append("\n下一波 W").Append(next.number);
                    foreach (var group in next.groups)
                        text.Append('\n').Append(EnemyName(group.enemyId)).Append(" × ").Append(group.count);
                    text.Append('\n').Append(WaveThreatPresentation.StrategyHint(next));
                }
            }
            else text.Append("\n战役结算");
            preview.text = text.ToString();

            if (waves.IsWaveActive)
            {
                statistics.text = $"本波 W{waves.CurrentWave}\n击杀 {waves.Kills} · 漏怪 {waves.Leaks}\n" +
                    $"场上 {enemies.ActiveCount} · 待出 {enemies.PendingCount}";
            }
            else if (waves.Results.Count > 0)
            {
                var last = waves.Results[waves.Results.Count - 1];
                string result = last.Reason == WaveEndReason.Cleared ? "完成" :
                    last.Reason == WaveEndReason.Failed ? "失败" : "中断";
                statistics.text = $"W{last.WaveNumber} {result}\n击杀 {last.Kills} · 漏怪 {last.Leaks}\n结算剩余 {last.RemainingGold}G";
                if (waves.Outcome != CampaignOutcome.None)
                {
                    int kills = 0, leaks = 0;
                    foreach (var wave in waves.Results) { kills += wave.Kills; leaks += wave.Leaks; }
                    statistics.text += $"\n总计击杀 {kills} · 漏怪 {leaks}";
                }
            }
            else statistics.text = "防守目标\n守住核心，完成 10 波入侵";

            normalHint = waves.Outcome == CampaignOutcome.Failure ? "防线失守 · 失败" :
                waves.Outcome == CampaignOutcome.Victory ? "RoomSiege · 胜利" :
                waves.Outcome == CampaignOutcome.Interrupted ? "波次中止" :
                displayedPaused ? "已暂停 · 倒计时与出怪冻结" :
                !displayedGrid ? "等待有效房间 · 计时冻结" :
                waves.AwaitingFirstWave ? (displayedTowers?"准备就绪后手动开始":"先部署至少 1 座塔") : waves.IsPreparing ? "可买 / 升 / 卖 · 到时自动开波" :
                waves.IsWaveActive ? (enemies.PendingCount==0?"出怪完毕 · 清理剩余敌人":"击杀自动入账 · 清场后结算") : "从主菜单开始游戏";
            ApplyNotice();

            panels.SetWaveAction(waves.IsWaveActive ? "返回战场" : waves.AwaitingFirstWave ? "开始第 1 波" : $"立即开始第 {waves.CurrentWave + 1} 波",
                waves.Outcome == CampaignOutcome.None && waves.HasPlayableGrid && (!waves.AwaitingFirstWave||displayedTowers));

            if (ring != null)
            {
                ring.progress = displayedProgress;
                ring.SetVerticesDirty();
            }
            ApplyProgress();
        }

        private void ApplyProgress()
        {
            if(progressLabel==null||waves==null)return;
            bool cue=PhaseNotice.Visible&&waves.Outcome==CampaignOutcome.None;
            bool countdown=waves.IsPreparing&&!waves.AwaitingFirstWave&&waves.PreparationRemaining<=5;
            progressLabel.color=cue&&PhaseNotice.Kind==WavePhaseCue.Boss?RoomSiegeStyle.Enemy:
                cue&&PhaseNotice.Kind==WavePhaseCue.Cleared||countdown?RoomSiegeStyle.Gold:RoomSiegeStyle.Text;
            if(ring!=null)ring.color=cue&&PhaseNotice.Kind==WavePhaseCue.Boss?RoomSiegeStyle.Enemy:
                cue&&PhaseNotice.Kind==WavePhaseCue.Cleared||countdown?RoomSiegeStyle.Gold:RoomSiegeStyle.Friendly;
            progressLabel.text=cue?PhaseNotice.Text:
                waves.Outcome == CampaignOutcome.Victory ? "战役胜利\n10 / 10" :
                    waves.Outcome == CampaignOutcome.Failure ? $"防线失守\n{waves.CurrentWave} / {waves.TotalWaves}" :
                    waves.IsWaveActive ? $"波次进行中\n{waves.CurrentWave} / {waves.TotalWaves}" :
                    waves.AwaitingFirstWave ? "布防准备\n0 / 10" : waves.IsPreparing ?
                        $"准备 · {Mathf.CeilToInt(waves.PreparationRemaining)} 秒\n下一波 {waves.CurrentWave+1} / {waves.TotalWaves}\n可立即开波" :
                    $"战役进度\n{waves.CurrentWave} / {waves.TotalWaves}";
        }

        private static string EnemyName(string id)
        {
            switch (id)
            {
                case "Grunt": return "Grunt 步兵";
                case "Runner": return "Runner 跑者";
                case "Tank": return "Tank 重装";
                case "Shielded": return "Shielded 护盾";
                case "Boss": return "Boss 首领";
                default: return id;
            }
        }

        private void OnDestroy()
        {
            if (waves != null) waves.Changed -= Refresh;
            if(waves!=null){waves.WaveStarted-=WaveStarted;waves.WaveFinished-=WaveFinished;waves.CampaignFinished-=CampaignFinished;}
            if(enemies!=null)enemies.Spawned-=Spawned;
            if (manager != null) { manager.CoinsChanged -= CoinsChanged; manager.LivesChanged -= LivesChanged; }
            if (enemies != null) enemies.CountsChanged -= Refresh;
            if(enemies!=null)enemies.Leaked-=Leaked;
            if(manager!=null)manager.CampaignReset-=ResetNotice;
        }
    }
}
