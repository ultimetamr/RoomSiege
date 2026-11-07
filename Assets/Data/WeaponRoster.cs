using System;
using UnityEngine;

namespace RoomSiege.Data
{
    public static class WeaponRoster
    {
        public static readonly string[] Ids={"MG","Frost","Laser","Trap","Tesla","Mortar","Beacon","Prism","Cryo","Sunder","Disruptor","Gravity","Sniper","Scatter","Flame","Missile","Railgun","Blade"};
        public static bool Contains(string id)=>Array.IndexOf(Ids,id)>=0;
        public static bool IsNew(string id)=>Array.IndexOf(Ids,id)>=6;
        public static bool IsStarter(string id)=>id=="MG"||id=="Frost"||id=="Laser"||id=="Trap";
        public static bool IsSupport(string id)=>id=="Frost"||id=="Beacon"||id=="Prism"||id=="Cryo"||id=="Sunder"||id=="Disruptor"||id=="Gravity";
        public static string Name(string id)
        {
            switch(id){case "MG":return "机枪";case "Frost":return "冰墙";case "Laser":return "激光";case "Trap":return "陷阱";case "Tesla":return "特斯拉";case "Mortar":return "迫击炮";
                case "Beacon":return "战术信标";case "Prism":return "棱镜中继";case "Cryo":return "寒霜发生器";case "Sunder":return "蚀甲投射器";case "Disruptor":return "电磁压制器";case "Gravity":return "引力井";
                case "Sniper":return "磁轨狙击";case "Scatter":return "蜂巢霰弹";case "Flame":return "熔流喷焰";case "Missile":return "追踪导弹";case "Railgun":return "贯星轨道炮";case "Blade":return "环刃哨卫";default:return id;}
        }
        public static string Role(string id)
        {
            switch(id){case "MG":return "作战 / 近程持续";case "Frost":return "辅助 / 范围减速";case "Laser":return "作战 / 持续破盾";case "Trap":return "作战 / 一次爆破";case "Tesla":return "作战 / 连锁清群";case "Mortar":return "作战 / 预判炮击";
                case "Beacon":return "辅助 / 攻速增幅";case "Prism":return "辅助 / 射程增幅";case "Cryo":return "辅助 / 寒霜冻结";case "Sunder":return "辅助 / 持续破甲";case "Disruptor":return "辅助 / 削盾干扰";case "Gravity":return "辅助 / 重力迟滞";
                case "Sniper":return "作战 / 远程穿甲";case "Scatter":return "作战 / 扇形清群";case "Flame":return "作战 / 扇形灼烧";case "Missile":return "作战 / 追踪爆破";case "Railgun":return "作战 / 直线贯穿";case "Blade":return "作战 / 近距环斩";default:return "防御武器";}
        }
        public static Color Tint(string id)
        {
            string hex;
            switch(id){case "Frost":case "Cryo":hex="#7AD7FF";break;case "Tesla":hex="#B5A1FF";break;case "Mortar":hex="#FFD27A";break;
                case "Beacon":hex="#72FFD1";break;case "Prism":hex="#A4E8FF";break;case "Sunder":hex="#B8F578";break;case "Disruptor":hex="#BAA5FF";break;case "Gravity":hex="#A78BFA";break;
                case "Sniper":hex="#96EBFF";break;case "Scatter":hex="#FFD79A";break;case "Flame":hex="#FFAA6A";break;case "Missile":hex="#FFBC87";break;case "Railgun":hex="#96BAFF";break;case "Blade":hex="#80FFE1";break;default:hex="#4FD8FF";break;}
            ColorUtility.TryParseHtmlString(hex,out var tint);return tint;
        }
        public static string Description(string id)
        {
            switch(id){case "Beacon":return "提升周围作战塔攻速12/18/24%。不影响辅助塔和一次性陷阱；同类取最高，不叠乘。";
                case "Prism":return "提升周围作战塔射程10/15/20%。同类取最高；不会放大辅助光环或改变寻路。";
                case "Cryo":return "周期为范围敌人积累寒霜，满5层短暂冻结。Boss冻结缩短，冻结后有8秒保护。自身无伤害。";
                case "Sunder":return "周期标记范围敌人，破甲20/30/40%，持续2.3秒。Boss减半；同类取最高，自身无伤害。";
                case "Disruptor":return "周期削减当前护盾15/20/25%，并干扰召唤1.2秒；Boss干扰0.4秒。对无盾普通敌人无伤害。";
                case "Gravity":return "范围减速35/45/55%，Boss减半。同类引力井不叠层；不拉扯、不堵路，无直接伤害。";
                case "Sniper":return "远程低射速点杀，忽略50%护甲。适合重甲和首领，面对密集小怪需要清群塔支援。";
                case "Scatter":return "朝目标发射60°扇形霰弹，命中范围内全部敌人；距离越远伤害越低，适合近距拦截。";
                case "Flame":return "70°短程喷焰持续灼烧，离开后余焰持续1.2秒。同类燃烧取最高，不重复叠伤。";
                case "Missile":return "导弹飞行0.65秒后追踪原目标爆炸，范围伤害无距离衰减；目标死亡则在最后位置爆炸。";
                case "Railgun":return "周期直线贯穿3/4/5个目标，每次贯穿伤害保留85%。沿通路侧向布置，利用直线排列。";
                case "Blade":return "近距周期360°环斩，命中范围内全部敌人。射程短，无减速，需要控制塔配合。";
                default:return Role(id);}
        }
        public static string LevelDescription(TowerDefinition tower,TowerLevelDefinition level)
        {
            switch(tower.id){case "Beacon":return $"攻速 +{level.effectMagnitude:P0}，同类取最高";case "Prism":return $"射程 +{level.effectMagnitude:P0}，同类取最高";
                case "Cryo":return $"每{1/level.shotsPerSecond:0.##}s 寒霜+{level.effectMagnitude:0}层";case "Sunder":return $"破甲{level.effectMagnitude:P0} / {level.effectDuration:0.#}s，Boss减半";
                case "Disruptor":return $"每{1/level.shotsPerSecond:0.#}s 削盾{level.effectMagnitude:P0} / 干扰1.2s";case "Gravity":return $"范围减速{level.slowFraction:P0}，Boss减半";
                case "Flame":return $"喷焰{level.damagePerSecond:0.#}伤/s + 余焰{level.effectMagnitude:0.#}伤/s";
                default:return $"{level.damagePerHit:0.#}伤 × {level.shotsPerSecond:0.##}次/s"+(tower.id=="Railgun"?$" · 穿{level.pierceTargetCount}":tower.id=="Missile"?$" · 爆炸{level.splashRadiusMeters.value:0.##}m":"");}
        }
    }
}
