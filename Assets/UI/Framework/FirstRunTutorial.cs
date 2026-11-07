using System;

namespace RoomSiege.UI
{
    public enum TutorialStep { None, Room, Choose, Place, Inspect, Upgrade, Launch, Completed }

    // Facts come from the live game, so rejected clicks never advance the lesson.
    public struct TutorialObservation
    {
        public GameState State;
        public bool RoomReady, HasPendingTower, HasTowers, HasManagedTower, HasUpgradedTower;
        public bool UpgradeAffordable, WaveActive;
    }

    public sealed class FirstRunTutorial
    {
        public const string PreferenceKey = "RoomSiege.Tutorial.Intro.v1";
        public TutorialStep Step { get; private set; }
        public bool UpgradeDeferred { get; private set; }
        public bool Active => Step != TutorialStep.None && Step != TutorialStep.Completed;
        public bool Visible => Step != TutorialStep.None;
        public TutorialObservation Latest { get; private set; }
        public event Action Changed;
        public event Action<bool> Concluded; // true = skipped, false = successfully started a wave

        public void Begin()
        {
            UpgradeDeferred = false;
            Latest = default;
            SetStep(TutorialStep.Room);
        }

        public void Observe(TutorialObservation value)
        {
            Latest = value;
            if (!Active) return;
            if (value.State == GameState.MENU || value.State == GameState.RESULT) { Cancel(); return; }
            if (value.State == GameState.WAVE && value.WaveActive)
            {
                SetStep(TutorialStep.Completed);
                Concluded?.Invoke(false);
                return;
            }
            if (value.State == GameState.SCAN || !value.RoomReady) { SetStep(TutorialStep.Room); return; }
            if (value.State != GameState.BUILD) return;
            if (!value.HasTowers)
                SetStep(value.HasPendingTower ? TutorialStep.Place : TutorialStep.Choose);
            else if (value.HasUpgradedTower || UpgradeDeferred)
                SetStep(TutorialStep.Launch);
            else if (value.HasManagedTower || Step == TutorialStep.Upgrade)
                SetStep(TutorialStep.Upgrade);
            else
                SetStep(TutorialStep.Inspect);
        }

        public void DeferUpgrade()
        {
            if (Step != TutorialStep.Upgrade) return;
            UpgradeDeferred = true;
            SetStep(TutorialStep.Launch);
        }

        public void Skip()
        {
            if (!Active) { Cancel(); return; }
            SetStep(TutorialStep.None);
            Concluded?.Invoke(true);
        }

        // Returning to the menu interrupts the lesson without marking it as learned.
        public void Cancel() { SetStep(TutorialStep.None); }
        private void SetStep(TutorialStep value)
        {
            if (Step == value) return;
            Step = value;
            Changed?.Invoke();
        }

        public string Title
        {
            get
            {
                switch (Step)
                {
                    case TutorialStep.Room: return "首次教学 1 / 6 · 准备房间";
                    case TutorialStep.Choose: return "首次教学 2 / 6 · 选择防御塔";
                    case TutorialStep.Place: return "首次教学 3 / 6 · 部署第一座塔";
                    case TutorialStep.Inspect: return "首次教学 4 / 6 · 管理已有塔";
                    case TutorialStep.Upgrade: return "首次教学 5 / 6 · 尝试升级";
                    case TutorialStep.Launch: return "首次教学 6 / 6 · 开始守卫";
                    case TutorialStep.Completed: return "教学完成 · 守住你的房间！";
                    default: return "";
                }
            }
        }

        public string Body
        {
            get
            {
                switch (Step)
                {
                    case TutorialStep.Room:
                        return Latest.RoomReady
                            ? "蓝格可建造，红环是入口，脚下圆圈是核心。\n选择难度与核心模式，再点“进入建造”。"
                            : "点击“扫描真实房间”，在系统内完成并保存后返回。\n也可点“使用模拟沙盘”直接体验；房间就绪才能建造。";
                    case TutorialStep.Choose:
                        return "← 点击左栏任意已出战塔卡；也可 Grip 抓图标后松开。\n初学建议机枪；选择免费，成功落格才扣金币。";
                    case TutorialStep.Place:
                        return "看向地面蓝格，用射线对准后扣扳机或捏合建造。\n红色格不能放；靠近敌人路线，保留一条通往核心的路。";
                    case TutorialStep.Inspect:
                        return "落格后建造栏已恢复；可选塔卡继续部署。\n用射线点击刚建好的塔，打开中间的升级管理栏。";
                    case TutorialStep.Upgrade:
                        if (!Latest.HasManagedTower) return "再次点击已有塔打开管理栏，再点“升 Lv2”。\n想保留金币可点“稍后升级”，教学不会强制消费。";
                        return Latest.UpgradeAffordable
                            ? "点击管理栏“升 Lv2”，成功后属性与外观会更新。\n只按显示价格扣钱；想保留金币可点“稍后升级”。"
                            : "当前金币不足，升级按钮已禁用，不会重复扣款。\n点“稍后升级”继续；击杀赚钱后可再点塔升级。";
                    case TutorialStep.Launch:
                        return "布防不限时。准备好后点主屏下方“开始第 1 波”。\n敌人从红环走向核心；漏怪扣生命，守住 10 波获胜。";
                    case TutorialStep.Completed:
                        return "击杀赚金币，波间有 15 秒补强；战斗中也能打开建造。\n暂停菜单可查看操作说明；失败后点“再来一局”重新布防。";
                    default: return "";
                }
            }
        }
    }
}
