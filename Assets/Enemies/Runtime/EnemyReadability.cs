using UnityEngine;
using RoomSiege.Visuals;

namespace RoomSiege.Enemies
{
    public static class EnemyReadability
    {
        public static string Name(string id)
        {
            switch(id)
            {
                case "Grunt": return "步兵";
                case "Runner": return "跑者";
                case "Tank": return "重装";
                case "Shielded": return "护盾兵";
                case "Boss": return "首领";
                default: return "敌人";
            }
        }

        public static string Role(string id)
        {
            switch(id)
            {
                case "Runner": return "高速蛇形";
                case "Tank": return "重装推进";
                case "Shielded": return "护盾前排";
                case "Boss": return "首领召唤";
                default: return "基础推进";
            }
        }

        public static Color RoleColor(string id)
        {
            switch(id)
            {
                case "Runner": return RoomSiegeStyle.Gold;
                case "Tank": return new Color(1f,.72f,.42f,1f);
                case "Shielded": return RoomSiegeStyle.Frost;
                case "Boss": return RoomSiegeStyle.Enemy;
                default: return RoomSiegeStyle.Text;
            }
        }

        public static float Fraction(float current,float maximum)
        {
            if(float.IsNaN(current)||float.IsInfinity(current)||float.IsNaN(maximum)||float.IsInfinity(maximum)||maximum<=0)return 0;
            return Mathf.Clamp01(current/maximum);
        }

        public static string Status(float healthFraction,float shield,float slow,bool shieldBroken,int slowSources=1)
        {
            string result=shieldBroken?"破盾":shield>0?"护盾":"";
            if(slow>0)result+=(result.Length>0?" · ":"")+(slow>=.999f?"冰冻":"减速 "+Mathf.RoundToInt(slow*100)+"%");
            if(slow>0&&slow<.999f&&slowSources>1)result+=" · "+slowSources+"层";
            if(healthFraction>0&&healthFraction<=.3f)result+=(result.Length>0?" · ":"")+"残血";
            return result;
        }
    }
}
