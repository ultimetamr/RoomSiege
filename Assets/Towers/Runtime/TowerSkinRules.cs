using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Towers
{
    // Shared skin rules never mutate balance assets or amplify other support sources.
    public static class TowerSkinRules
    {
        public static bool ColdAlly(TowerInstance tower,IReadOnlyList<TowerInstance> allies)
        {
            if(allies==null)return false;
            foreach(var other in allies)
                if(other!=null&&!other.Removed&&(other.Definition.id=="Frost"||other.Definition.id=="Cryo")&&
                    (other.Cell.Center-tower.Cell.Center).sqrMagnitude<=1.2f*1.2f)return true;
            return false;
        }
        public static float GravityStrength(TowerInstance tower,IReadOnlyList<TowerInstance> allies)
        {
            string effect=tower.Skin?.effect;
            float bonus=effect=="gravityDeep"?.08f:effect=="gravityFrost"&&ColdAlly(tower,allies)?.1f:0;
            return Mathf.Min(.8f,tower.Stats.slowFraction+tower.ControlBonus+bonus+(tower.OverloadRemaining>0?tower.OverloadControlIncrease:0));
        }
        public static bool Controlled(ITowerTarget target)=>target is ICombatStatusTarget status&&status.ControlFraction>0;
        public static float MortarRadius(TowerInstance tower)=>tower.Stats.splashRadiusMeters.value*(tower.Perks?.WeaponBlast(tower.Definition.id)??1)*
            (tower.Skin?.effect=="wideShell"?1.12f:tower.Skin?.effect=="siegeShell"?1.1f:1);
    }
}
