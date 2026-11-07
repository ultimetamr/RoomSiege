using System;
using System.Text;
using RoomSiege.Audio;
using RoomSiege.Data;
using RoomSiege.Towers;
using RoomSiege.UI;
using RoomSiege.Visuals;
using RoomSiege.Waves;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace RoomSiege.Meta
{
    // Uses the existing main panel's scale, following and fade. All content remains inside it.
    public sealed class MetaPanelUi : MonoBehaviour
    {
        private enum PageKind { Settlement, Codex, Rankings }
        private MetaController meta;
        private WorldPanelNavigation navigation;
        private WorldPanelHost panels;
        private GameObject overlay, entryButton, settlementPage, codexPage, rankingsPage;
        private Text settlementText, codexTitle, codexText, pageLabel, sampleStatus, skinLabel;
        private RawImage codexImage;
        private Text settlementTitle, settlementReason, settlementReward, settlementNext;
        private Text rankText, rankDifficulty, rankPage;
        private Button replay, skin;
        private Button settlementTab,towerTab,enemyTab,rankingTab;
        private RectTransform originalTitle;
        private Vector2 titlePosition, titleSize;
        private PageKind selectedPage;
        private bool enemyPage;
        private int index, difficultyIndex = 1, rankOffset;

        public void Initialize(MetaController controller, WorldPanelHost panels)
        {
            meta = controller;
            this.panels = panels;
            navigation = panels.Navigation;
            var main = panels.Panels[0].transform;
            var sample = main.Find("Title").GetComponent<Text>();
            panels.LibraryRequested += OpenLibrary;

            overlay = new GameObject("Meta overlay", typeof(RectTransform), typeof(Canvas),
                typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            overlay.layer = 5;
            overlay.transform.SetParent(main, false);
            overlay.transform.localPosition = new Vector3(0, 0, -12);
            overlay.transform.localScale = Vector3.one;
            overlay.GetComponent<RectTransform>().sizeDelta = new Vector2(848, 488);
            var canvas = overlay.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = panels.Panels[0].worldCamera;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 100;
            WorldUiSampling.Configure(canvas);
            MakeImage(overlay.transform, "Meta background", Vector2.zero, new Vector2(848, 488),
                RoomSiegeStyle.Overlay, sample, true);
            GameUiChrome.Frame(overlay.transform,sample.material);
            var heading=MakeText(overlay.transform, "Meta title", "战役档案", new Vector2(-226,207), new Vector2(310,38), 28, sample);heading.alignment=TextAnchor.MiddleLeft;
            RoomSiegeStyle.AddRule(overlay.transform,sample.material,new Vector2(0,125),new Vector2(730,1),RoomSiegeStyle.Rule);
            MakeButton(overlay.transform, "关闭", new Vector2(344,207), new Vector2(96,40), sample, Hide);
            settlementTab=MakeButton(overlay.transform, "战果", new Vector2(-270, 150), new Vector2(160, 40), sample, ShowSettlement);
            towerTab=MakeButton(overlay.transform, "塔图鉴", new Vector2(-90, 150), new Vector2(160, 40), sample, () => ShowCodex(false));
            enemyTab=MakeButton(overlay.transform, "敌种图鉴", new Vector2(90, 150), new Vector2(160, 40), sample, () => ShowCodex(true));
            rankingTab=MakeButton(overlay.transform, "本地排行", new Vector2(270, 150), new Vector2(160, 40), sample, ShowRankings);

            GameUiChrome.Surface(overlay.transform,"Archive content surface",new Vector2(0,-38),new Vector2(776,328),RoomSiegeStyle.Card,sample.material);
            settlementPage = MakePage("Settlement page");
            settlementPage.GetComponent<RectTransform>().anchoredPosition=Vector2.zero;
            settlementTitle = MakeText(settlementPage.transform,"Settlement headline","",new Vector2(0,108),new Vector2(730,32),30,sample);
            settlementReason = MakeText(settlementPage.transform,"Settlement reason","",new Vector2(0,66),new Vector2(730,40),18,sample);
            settlementText = MakeText(settlementPage.transform, "Settlement details", "", new Vector2(0,9),new Vector2(730,64),18,sample);
            settlementReward = MakeText(settlementPage.transform,"Settlement reward","",new Vector2(0,-43),new Vector2(730,24),16,sample);
            settlementReward.fontSize=16;settlementReward.color=RoomSiegeStyle.Gold;
            settlementNext = MakeText(settlementPage.transform,"Settlement next attempt","",new Vector2(0,-85),new Vector2(730,44),18,sample);
            var retry=MakeButton(settlementPage.transform,"再来一局",new Vector2(-185,-136),new Vector2(330,36),sample,panels.RequestRestart);
            RoomSiegeStyle.PrimaryButton(retry);
            MakeButton(settlementPage.transform,"返回主菜单",new Vector2(185,-136),new Vector2(330,36),sample,panels.RequestMenu);

            MakeButton(settlementPage.transform,"军械商店",new Vector2(0,-182),new Vector2(430,28),sample,()=>ProgressionController.Instance?.OpenShop());

            codexPage = MakePage("Codex page");
            codexTitle = MakeText(codexPage.transform, "Codex title", "", new Vector2(0, 112), new Vector2(720, 38), 23, sample);
            codexTitle.alignment=TextAnchor.MiddleLeft;
            codexText = MakeText(codexPage.transform, "Codex details", "", new Vector2(-136, 4), new Vector2(430, 178), 17, sample);
            codexText.alignment = TextAnchor.UpperLeft;
            codexImage = MakeRawImage(codexPage.transform,"Codex model image",new Vector2(250,4),new Vector2(218,218));
            codexImage.material=sample.material;
            MakeButton(codexPage.transform, "上一项", new Vector2(-260, -112), new Vector2(130, 38), sample, () => Move(-1));
            MakeButton(codexPage.transform, "下一项", new Vector2(-110, -112), new Vector2(130, 38), sample, () => Move(1));
            replay = MakeButton(codexPage.transform, "回放声音", new Vector2(70, -112), new Vector2(175, 38), sample, Replay);
            pageLabel = MakeText(codexPage.transform, "Codex index", "", new Vector2(260, -112), new Vector2(120, 38), 18, sample);
            sampleStatus = MakeText(codexPage.transform, "Sample status", "", new Vector2(0, -153), new Vector2(735, 30), 16, sample);

            rankingsPage = MakePage("Local rankings page");
            MakeButton(rankingsPage.transform, "休闲", new Vector2(-205, 110), new Vector2(170, 36), sample, () => SelectDifficulty(0));
            MakeButton(rankingsPage.transform, "标准", new Vector2(0, 110), new Vector2(170, 36), sample, () => SelectDifficulty(1));
            MakeButton(rankingsPage.transform, "硬核", new Vector2(205, 110), new Vector2(170, 36), sample, () => SelectDifficulty(2));
            rankDifficulty = MakeText(rankingsPage.transform, "Ranking difficulty", "", new Vector2(0, 68), new Vector2(700, 32), 20, sample);
            rankText = MakeText(rankingsPage.transform, "Ranking entries", "", new Vector2(0, -15), new Vector2(750, 140), 20, sample);
            rankText.alignment = TextAnchor.UpperLeft;
            MakeButton(rankingsPage.transform, "上一页", new Vector2(-220, -116), new Vector2(140, 38), sample,
                () => { rankOffset = 0; RefreshRankings(); });
            MakeButton(rankingsPage.transform, "下一页", new Vector2(0, -116), new Vector2(140, 38), sample,
                () => { rankOffset = MetaPersistence.GetScores(MetaPersistence.DifficultyId(difficultyIndex)).Count > 5 ? 5 : 0; RefreshRankings(); });
            rankPage = MakeText(rankingsPage.transform, "Ranking page", "", new Vector2(225, -116), new Vector2(190, 38), 18, sample);

            skin = MakeButton(overlay.transform, "", new Vector2(0, -223), new Vector2(730, 24), sample, meta.OpenSkinShop);
            skin.gameObject.name="Gold skin toggle";
            skinLabel = skin.GetComponentInChildren<Text>();
            skinLabel.fontSize=16;
            skinLabel.color = RoomSiegeStyle.Gold;
            settlementTitle.color=RoomSiegeStyle.Gold;settlementReason.color=RoomSiegeStyle.Muted;settlementNext.color=RoomSiegeStyle.Friendly;
            meta.Changed += Refresh;
            navigation.Register(overlay);
            Refresh();
        }

        public void ShowSettlement() { Show(PageKind.Settlement); }
        private void OpenLibrary() { ShowCodex(false); }
        public void ShowCodex(bool enemiesPage)
        {
            enemyPage = enemiesPage; index = 0;
            Show(PageKind.Codex);
        }
        private void ShowRankings()
        {
            difficultyIndex = meta.DifficultyId == "Casual" ? 0 : meta.DifficultyId == "Hardcore" ? 2 : 1;
            rankOffset = 0; Show(PageKind.Rankings);
        }
        private void Show(PageKind page)
        {
            if (overlay == null) return;
            selectedPage = page;
            MarkTab(settlementTab,page==PageKind.Settlement);MarkTab(towerTab,page==PageKind.Codex&&!enemyPage);
            MarkTab(enemyTab,page==PageKind.Codex&&enemyPage);MarkTab(rankingTab,page==PageKind.Rankings);
            settlementPage.SetActive(page == PageKind.Settlement);
            codexPage.SetActive(page == PageKind.Codex);
            rankingsPage.SetActive(page == PageKind.Rankings);
            navigation.Show(overlay);
            Refresh();
        }
        private static void MarkTab(Button tab,bool selected)
        {
            RoomSiegeStyle.NavigationButton(tab,selected);
        }
        private void Hide() { if (navigation != null) navigation.Close(overlay); }
        private void Move(int amount)
        {
            int count = enemyPage ? meta.Catalog.enemies.Length : meta.Catalog.towers.Length;
            index = (index + amount + count) % count;
            RefreshCodex();
        }
        private void SelectDifficulty(int difficulty) { difficultyIndex = difficulty; rankOffset = 0; RefreshRankings(); }

        private void Replay()
        {
            string id = enemyPage ? meta.Catalog.enemies[index].id : meta.Catalog.towers[index].id;
            bool unlocked = enemyPage ? MetaPersistence.IsEnemyUnlocked(id) : MetaPersistence.IsTowerUnlocked(id);
            if (!unlocked) return;
            bool played = AudioDirector.Instance != null && AudioDirector.Instance.PlayCodexSample(id, enemyPage);
            sampleStatus.text = played ? "正在回放本地空间音频样本" : "空间音频尚未就绪，暂无法回放";
        }

        private void Refresh()
        {
            if (meta == null || meta.Catalog == null || skin == null) return;
            skin.interactable = ProgressionController.Instance != null && ProgressionController.Instance.IsReady;
            skinLabel.text = "前往皮肤商店 · 使用核心碎片购买并穿戴";
            if (overlay == null || !overlay.activeSelf) return;
            if (selectedPage == PageKind.Settlement) RefreshSettlement();
            else if (selectedPage == PageKind.Codex) RefreshCodex();
            else RefreshRankings();
        }

        private void RefreshSettlement()
        {
            var manager=GameManager.Instance;
            settlementPage.transform.Find("再来一局").gameObject.SetActive(manager!=null&&manager.State==GameState.RESULT);
            settlementPage.transform.Find("返回主菜单").gameObject.SetActive(manager!=null&&manager.State==GameState.RESULT);
            if (!meta.HasSettlement)
            {
                settlementTitle.text="尚无本局战果";settlementTitle.color=RoomSiegeStyle.Text;
                settlementReason.text="胜利或失败后自动显示结算页";settlementReason.color=RoomSiegeStyle.Text;
                settlementText.text = "金：胜利、满血、剩余 ≥300G\n银：其余胜利    铜：失败";
                settlementReward.text="";
                settlementNext.text="本地保存 · 无需联网或登录";
                return;
            }
            var value = meta.Settlement;
            var waves=WaveController.Instance;
            string last=waves!=null?waves.LastLeakedEnemyId:"";
            string headline=SettlementFeedback.Headline(value.Outcome);
            string reason=SettlementFeedback.Reason(value.Outcome,manager!=null?manager.CurrentWave:0,waves!=null?waves.FinishReason:"",last);
            settlementTitle.text=headline;
            settlementTitle.color=value.Outcome==CampaignOutcome.Victory?RoomSiegeStyle.Gold:RoomSiegeStyle.Enemy;
            settlementReason.text=reason;settlementReason.color=RoomSiegeStyle.Text;
            settlementText.text = $"{MetaRules.DifficultyName(value.Difficulty)} · {MetaRules.RatingName(value.Rating)}评级 · 分数 {value.Score}    剩余 {value.RemainingGold}G\n" +
                $"生命剩余 {value.Lives:0.##}    漏怪 {value.Leaks}    平均 DPS {value.DPS:0.##}\n" +
                $"本体有效伤害 {value.Damage:0.##}    战斗 {value.DurationSeconds:0.##} 秒";
            var progress=ProgressionController.Instance;
            settlementReward.text=value.Outcome==CampaignOutcome.Interrupted?"中止记录不进入排行":progress!=null?$"核心碎片 +{progress.Store.LastReward:N0}  ·  总余额 {progress.Store.Fragments:N0}  ·  成绩已保存":"成绩已保存到本地排行";
            settlementNext.text=SettlementFeedback.NextAttempt(value.Outcome,value.Rating,last)+"\n"+SettlementFeedback.RestartHint;
            panels.SetResultSummary(headline+"\n"+reason+"\n"+SettlementFeedback.RestartHint);
        }

        private void RefreshCodex()
        {
            string imageType=enemyPage?"Enemies/CodexEnemy_":"Towers/CodexTower_";
            if (enemyPage)
            {
                var entry = meta.Catalog.enemies[index];
                bool unlocked = MetaPersistence.IsEnemyUnlocked(entry.id);
                codexTitle.text = $"敌种图鉴 · {entry.id} · {(unlocked ? "已解锁" : "未发现")}";
                if (!unlocked) codexText.text = "该敌种首次在战役中出现后解锁资料与声音样本。";
                else
                {
                    var difficulty = meta.Catalog.GetDifficulty(meta.DifficultyId);
                    int wave=Mathf.Max(1,GameManager.Instance.CurrentWave);
                    var stats = DifficultyCalculator.ApplyAtWave(entry, difficulty, wave, meta.Catalog.enemyProgression);
                    string shield = stats.HasShield && stats.ShieldHitPoints.specified ? $" + 护盾 {stats.ShieldHitPoints.value:0.##}" : "";
                    string baseShield = entry.hasShield && entry.shieldHitPoints.specified ? $" + 护盾 {entry.shieldHitPoints.value:0.##}" : "";
                    codexText.text = $"基础 HP {entry.hitPoints:0.##}{baseShield}\n" +
                        $"{difficulty.displayName} W{wave} HP {stats.HitPoints:0.##}{shield}    速度 {stats.MoveSpeed:0.##}m/s\n护甲 {stats.Armor:0} · 减伤 {100*(1-DifficultyCalculator.DamageAfterArmor(1,stats.Armor)):0}%\n" +
                        $"击杀 {entry.killGold}G    漏血 {stats.LeakDamage:0.##}\n{Weakness(entry)}\n{EnemyBehavior(entry)}";
                }
                replay.interactable = unlocked;
                SetCodexImage(imageType+entry.id,unlocked);
            }
            else
            {
                var entry = meta.Catalog.towers[index];
                bool unlocked = MetaPersistence.IsTowerUnlocked(entry.id);
                codexTitle.text = $"塔图鉴 · {entry.displayName} · {(unlocked ? "已解锁" : "未部署")}";
                if (!unlocked) codexText.text = "首次成功建造该塔后解锁图鉴和声音样本。\n图鉴锁定不影响从建造栏购买。";
                else
                {
                    var text = new StringBuilder($"造价 {entry.buildCost}G · 基础范围 {entry.rangeMeters.value:0.##}m · 无独立 HP\n");
                    foreach (var level in entry.levels) text.AppendLine(LevelDescription(entry, level));
                    text.AppendLine(TowerLimitation(entry.id));
                    text.Append("卖出返还累计投入 70% · 无利息");
                    codexText.text = text.ToString();
                }
                replay.interactable = unlocked;
                SetCodexImage(imageType+entry.id,unlocked,entry.id);
            }
            sampleStatus.text = "回放使用本地音频样本，不生成塔或敌人";
            pageLabel.text = $"{index + 1} / {(enemyPage ? meta.Catalog.enemies.Length : meta.Catalog.towers.Length)}";
        }

        private void RefreshRankings()
        {
            string difficulty = MetaPersistence.DifficultyId(difficultyIndex);
            var rows = MetaPersistence.GetScores(difficulty);
            if (rankOffset >= rows.Count) rankOffset = 0;
            rankDifficulty.text = $"{MetaRules.DifficultyName(difficulty)} · 最高分 {(rows.Count == 0 ? "—" : rows[0].score.ToString())} · 本机前 10";
            if (rows.Count == 0) rankText.text = "本难度尚无本地成绩。\n完成一局后记录；无需登录或联网。";
            else
            {
                var text = new StringBuilder();
                for (int i = rankOffset; i < Math.Min(rows.Count, rankOffset + 5); i++)
                {
                    var row = rows[i];
                    string outcome = row.outcome == "Victory" ? "胜利" : "失败";
                    string rating = row.rating == "Gold" ? "金" : row.rating == "Silver" ? "银" : "铜";
                    text.AppendLine($"{i + 1}. {row.score}分 · {outcome}/{rating} · W{row.wave} · HP {row.lives:0.##} · {row.remainingGold}G");
                }
                rankText.text = text.ToString();
            }
            rankPage.text = $"第 {rankOffset / 5 + 1} / {(rows.Count > 5 ? 2 : 1)} 页";
        }

        private static string LevelDescription(TowerDefinition tower, TowerLevelDefinition level)
        {
            string upgrade = level.level == 1 ? "" : $" · 升级 {level.upgradeCost}G";
            string damage;
            if(tower.damageMode=="arsenal")damage=RoomSiege.Data.WeaponRoster.LevelDescription(tower,level);
            else if (tower.damageMode == "projectile") damage = $"{level.damagePerHit:0.##}伤 × {level.shotsPerSecond:0.##}发/s" +
                (level.splashEnabled ? $" + 溅射 {level.splashDamage.value:0.##}/{level.splashRadiusMeters.value:0.##}m" : "");
            else if (tower.damageMode == "slowArea") damage = $"减速 {level.slowFraction:P0}/{level.slowDurationSeconds.value:0.##}s · 范围 ×{level.rangeMultiplier:0.##}" +
                (level.damagePerSecond > 0 ? $" + {level.damagePerSecond:0.##}伤/s" : "");
            else if (tower.damageMode == "beam") damage = $"持续 {level.damagePerSecond:0.##}伤/s · 穿 {level.pierceTargetCount} 目标";
            else if(tower.damageMode=="chain")damage=$"{level.damagePerHit:0.#}伤 · 连锁{level.pierceTargetCount}目标 · 每跳70%";
            else if(tower.damageMode=="mortar")damage=$"{level.damagePerHit:0.#}伤 · 爆炸{level.splashRadiusMeters.value:0.#}m · 延迟{tower.impactDelaySeconds:0.#}s";
            else damage = $"一次性 {level.damagePerHit:0.##}伤" + (level.slowFraction > 0 ? $" + 减速 {level.slowFraction:P0}/{level.slowDurationSeconds.value:0.##}s" : "");
            return $"Lv{level.level}：{damage}{upgrade}";
        }
        private static string TowerLimitation(string id) => RoomSiege.Data.WeaponRoster.IsNew(id)?RoomSiege.Data.WeaponRoster.Description(id): id=="Tesla"?"局限：敌人分散时电弧无法跳跃":id=="Mortar"?"局限：落点固定，近处无法开炮，需要减速配合":id == "Frost" ? "局限：低伤害，需要输出塔配合" :
            id == "Trap" ? "地面单体补漏；触发即消耗，不阻挡敌人" : id == "Laser" ? "局限：沿束线穿透，依赖目标排列" : "局限：近距离，前两级单体输出";
        private static string Weakness(EnemyDefinition entry) => entry.hasShield ? "弱点：激光对护盾有伤害加成" :
            entry.id == "Runner" ? "应对建议：冰墙减速，陷阱拦截" : entry.id == "Tank" ? "应对建议：持续输出、集中火力" :
            entry.id == "Boss" ? "应对建议：集火首领并清理召唤物" : "应对建议：群体拦截、溅射";
        private static string EnemyBehavior(EnemyDefinition entry)
        {
            if (entry.id == "Runner") return "行为：蛇形前进并绕塔";
            if (entry.id == "Boss" && entry.summon != null) return $"行为：每 {entry.summon.intervalSeconds.value:0.##}s 召唤 {entry.summon.countPerSummon.value} 个 {entry.summon.enemyId}";
            return entry.hasShield ? "行为：护盾优先承伤" : "行为：直线向安全区移动";
        }
        private void SetCodexImage(string resource,bool unlocked,string weaponId=null)
        {
            if(codexImage==null)return;
            codexImage.texture=weaponId==null?Resources.Load<Texture2D>("Codex/"+resource):WeaponArtwork.Load(weaponId,ProgressionController.Instance?.Store.Equipped(weaponId));
            codexImage.color=unlocked?Color.white:new Color(.35f,.42f,.48f,.72f);
            codexImage.gameObject.SetActive(codexImage.texture!=null);
        }

        private GameObject MakePage(string name)
        {
            var page = Rect(overlay.transform, name, new Vector2(0, -20), new Vector2(780, 310));
            return page;
        }
        private static Image MakeImage(Transform parent, string name, Vector2 position, Vector2 size, Color color, Text sample, bool raycast)
        {
            var image = Rect(parent, name, position, size).AddComponent<RoundedGlassImage>();
            image.material = sample.material; image.color = color; image.raycastTarget = raycast; return image;
        }
        private static RawImage MakeRawImage(Transform parent,string name,Vector2 position,Vector2 size)
        {
            var go=Rect(parent,name,position,size);var image=go.AddComponent<RawImage>();image.color=Color.white;image.raycastTarget=false;return image;
        }
        private static Button MakeButton(Transform parent, string label, Vector2 position, Vector2 size, Text sample, UnityAction action)
        {
            var image = MakeImage(parent, label, position, size, RoomSiegeStyle.Button, sample, true);
            var button = image.gameObject.AddComponent<Button>(); button.targetGraphic = image;
            RoomSiegeStyle.StyleButton(button);
            button.onClick.AddListener(() => { TowerHost.Instance?.SuppressTap(); action(); });
            MakeText(image.transform, "Label", label, Vector2.zero, size - Vector2.one * 4, 18, sample);
            return button;
        }
        private static Text MakeText(Transform parent, string name, string value, Vector2 position, Vector2 size, int fontSize, Text sample)
        {
            var text = Rect(parent, name, position, size).AddComponent<Text>();
            text.font = sample.font; text.material = sample.material;
            text.text = value; RoomSiegeStyle.StyleText(text, fontSize); text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false; text.horizontalOverflow = HorizontalWrapMode.Wrap;
            return text;
        }
        private static GameObject Rect(Transform parent, string name, Vector2 position, Vector2 size)
        {
            var go = new GameObject(name, typeof(RectTransform)); go.layer = 5; go.transform.SetParent(parent, false);
            var rect = go.GetComponent<RectTransform>(); rect.anchoredPosition = position; rect.sizeDelta = size; return go;
        }
        private void OnDestroy()
        {
            if (navigation != null) navigation.Close(overlay);
            if (panels != null) panels.LibraryRequested -= OpenLibrary;
            if (meta != null) meta.Changed -= Refresh;
            if (entryButton != null) Destroy(entryButton);
            if (overlay != null) Destroy(overlay);
            if (originalTitle != null) { originalTitle.anchoredPosition = titlePosition; originalTitle.sizeDelta = titleSize; }
        }
    }
}
