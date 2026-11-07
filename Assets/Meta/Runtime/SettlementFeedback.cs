using RoomSiege.Enemies;
using RoomSiege.Waves;

namespace RoomSiege.Meta
{
    public static class SettlementFeedback
    {
        public const string RestartHint = "再来一局：同房间、同难度，生命与金币重置";
        public static string Headline(CampaignOutcome outcome) => outcome == CampaignOutcome.Victory ? "胜利 · 房间守住了" :
            outcome == CampaignOutcome.Failure ? "失败 · 防线失守" : "本局已中止";

        public static string Reason(CampaignOutcome outcome, int wave, string finishReason, string lastLeakedEnemy)
        {
            if(outcome==CampaignOutcome.Victory)return "最后一波与场上敌人已全部处理";
            if(outcome==CampaignOutcome.Failure)return $"第 {wave} 波 · 核心生命耗尽"+
                (string.IsNullOrEmpty(lastLeakedEnemy)?"":"\n最后漏过："+EnemyReadability.Name(lastLeakedEnemy));
            return string.IsNullOrEmpty(finishReason)?"本局未完成，不计入本地排行":finishReason;
        }

        public static string NextAttempt(CampaignOutcome outcome, MetaRating rating, string lastLeakedEnemy)
        {
            if(outcome==CampaignOutcome.Interrupted)return "可重新布防，或回主菜单调整房间与难度";
            if(outcome==CampaignOutcome.Victory)return rating==MetaRating.Gold?"金评级达成 · 荣誉已记录，结算碎片可用于购买皮肤":
                "挑战金评级：胜利、满血、剩余 ≥300G";
            switch(lastLeakedEnemy)
            {
                case "Runner": return "下局建议：冰墙减速跑者，再用输出塔拦截";
                case "Tank": return "下局建议：升级输出塔，给重装留足受击路程";
                case "Shielded": return "下局建议：用激光破盾，搭配冰墙延长输出时间";
                case "Boss": return "下局建议：集火首领，并保留火力清理召唤物";
                default: return "下局建议：入口布防配合减速，优先升级主力塔";
            }
        }
    }
}
