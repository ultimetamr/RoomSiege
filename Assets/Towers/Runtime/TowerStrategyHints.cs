namespace RoomSiege.Towers
{
    public static class TowerStrategyHints
    {
        public static string For(string id)
        {
            if(RoomSiege.Data.WeaponRoster.IsNew(id))return RoomSiege.Data.WeaponRoster.Description(id);
            switch(id)
            {
                case "MG": return "组合建议：用 Frost 留住目标，再让 MG 持续收割";
                case "Frost": return "组合建议：减速区交给 MG 或 Laser 集中输出";
                case "Laser": return "组合建议：对准 Shielded，配合 Frost 延长束线命中";
                case "Trap": return "一次性地雷：地面路线上单体爆发\n拦截 Runner / 补漏，Lv3 减速 30% 持续 2s";
                case "Tesla": return "连锁电弧：配合冰墙减速聚怪，敌人分散时收益降低";
                case "Mortar": return "延迟范围炮击：配合减速预判落点，0.75m内无法开火";
                default: return "组合建议：让输出、减速和拦截覆盖同一条路线";
            }
        }
    }
}
