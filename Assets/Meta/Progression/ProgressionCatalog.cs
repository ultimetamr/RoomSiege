using System;
using RoomSiege.Data;
using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Progression
{
    [Serializable] public sealed class WeaponUnlockDefinition
    {
        public string id,name,role,description,color;public int price;
    }
    [Serializable] public sealed class SkinDefinition
    {
        public string id, weaponId, name, rarity, description, color, effect;
        public int price;
        public float rate=1, damage=1, range=1;
    }
    [Serializable] public sealed class SupplyDefinition
    {
        public string id, name, description, target, category, color;
        public int price;
        public float radius, duration, magnitude, bossDuration;
        public float pulseInterval,armSeconds,triggerRadius,statusSeconds;
    }
    [Serializable] public sealed class RewardDefinition
    {
        public int perWave=10, victory=150, flawless=50, firstVictory=300;
        public float[] difficultyMultipliers={1,1.25f,1.5f};
    }
    [Serializable] public sealed class ProgressionCatalog
    {
        public int version=1;
        public string[] weapons={"MG","Frost","Laser","Trap"};
        public WeaponUnlockDefinition[] weaponUnlocks;
        public SkinDefinition[] skins;
        public SupplyDefinition[] supplies;
        public RewardDefinition rewards=new RewardDefinition();
        public static ProgressionCatalog Load()
        {
            var asset=Resources.Load<TextAsset>("RoomSiegeProgression");
            if(asset==null)throw new InvalidOperationException("缺少 RoomSiegeProgression 配置");
            return Parse(asset.text);
        }
        public static ProgressionCatalog Parse(string json)
        {
            var value=JsonUtility.FromJson<ProgressionCatalog>(json);
            if(value==null||value.version!=1||value.weapons==null||value.weapons.Length<4||value.skins==null||value.supplies==null||value.rewards==null)
                throw new ArgumentException("局外配置不完整");
            var effects=new Dictionary<string,string>{{"rate","MG"},{"pierce","MG"},{"frostAmmo","MG"},{"lingering","Frost"},{"pulse","Frost"},{"winter","Frost"},{"focus","Laser"},{"refract","Laser"},{"arc","Laser"},{"blast","Trap"},{"burn","Trap"},{"shatter","Trap"}};
            effects.Add("conduction","Tesla");
            effects.Add("relay","Tesla");
            effects.Add("cryoCircuit","Tesla");
            effects.Add("wideShell","Mortar");
            effects.Add("emberShell","Mortar");
            effects.Add("siegeShell","Mortar");
            effects.Add("beaconReach","Beacon");
            effects.Add("beaconTempo","Beacon");
            effects.Add("beaconWinter","Beacon");
            effects.Add("prismReach","Prism");
            effects.Add("prismBoost","Prism");
            effects.Add("prismTempo","Prism");
            effects.Add("cryoTempo","Cryo");
            effects.Add("cryoEcho","Cryo");
            effects.Add("cryoSlow","Cryo");
            effects.Add("sunderTempo","Sunder");
            effects.Add("sunderLinger","Sunder");
            effects.Add("sunderDeep","Sunder");
            effects.Add("disruptTempo","Disruptor");
            effects.Add("shieldRupture","Disruptor");
            effects.Add("disruptSlow","Disruptor");
            effects.Add("gravityReach","Gravity");
            effects.Add("gravityDeep","Gravity");
            effects.Add("gravityFrost","Gravity");
            effects.Add("sniperPower","Sniper");
            effects.Add("sniperPierce","Sniper");
            effects.Add("sniperExecute","Sniper");
            effects.Add("scatterTempo","Scatter");
            effects.Add("scatterCone","Scatter");
            effects.Add("scatterFrost","Scatter");
            effects.Add("flamePower","Flame");
            effects.Add("flameLinger","Flame");
            effects.Add("flameSunder","Flame");
            effects.Add("missileTempo","Missile");
            effects.Add("missileBlast","Missile");
            effects.Add("missileEmp","Missile");
            effects.Add("railPower","Railgun");
            effects.Add("railPierce","Railgun");
            effects.Add("railConserve","Railgun");
            effects.Add("bladeTempo","Blade");
            effects.Add("bladeReach","Blade");
            effects.Add("bladeFrost","Blade");
            var targets=new Dictionary<string,string>{{"freeze","core"},{"repair","core"},{"overload","tower"},{"emp","ground"},{"decoy","ground"},{"strike","ground"},
                {"firefield","ground"},{"gravity","ground"},{"mine","ground"},{"thunder","ground"},{"corrosion","ground"},{"frostsigil","ground"}};
            var ids=new HashSet<string>();var weaponIds=new HashSet<string>(value.weapons);
            if(weaponIds.Count!=value.weapons.Length||weaponIds.Contains(null)||weaponIds.Contains(""))throw new ArgumentException("武器 ID 重复");
            var supported=new HashSet<string>(WeaponRoster.Ids);
            if(!supported.SetEquals(weaponIds))throw new ArgumentException("武器列表与战斗实现不匹配");
            if(value.weaponUnlocks==null||value.weaponUnlocks.Length!=WeaponRoster.Ids.Length-4)throw new ArgumentException("武器解锁配置缺失");
            foreach(var unlock in value.weaponUnlocks)
                if(unlock==null||(!WeaponRoster.Contains(unlock.id)||WeaponRoster.IsStarter(unlock.id))||!ids.Add(unlock.id)||unlock.price<=0||string.IsNullOrEmpty(unlock.description)||!ColorUtility.TryParseHtmlString(unlock.color,out _))throw new ArgumentException("武器商品无效");
            foreach(var skin in value.skins)
                if(skin==null||string.IsNullOrEmpty(skin.id)||!ids.Add(skin.id)||!weaponIds.Contains(skin.weaponId)||skin.price<=0||!effects.TryGetValue(skin.effect??"",out var weapon)||weapon!=skin.weaponId||
                    (skin.rarity!="精英"&&skin.rarity!="史诗"&&skin.rarity!="传说")||
                    !Finite(skin.rate)||skin.rate<=0||skin.rate>2||!Finite(skin.damage)||skin.damage<=0||skin.damage>2||!Finite(skin.range)||skin.range<=0||skin.range>2||
                    !ColorUtility.TryParseHtmlString(skin.color,out _))throw new ArgumentException("皮肤配置无效");
            foreach(var item in value.supplies)
                if(item==null||string.IsNullOrEmpty(item.id)||!ids.Add(item.id)||item.price<=0||!targets.TryGetValue(item.id,out var target)||item.target!=target||!Finite(item.radius)||item.radius<0||
                    !Finite(item.duration)||item.duration<30||!Finite(item.magnitude)||item.magnitude<=0||!Finite(item.bossDuration)||item.bossDuration<0||
                    (item.category!="skill"&&item.category!="trap"&&item.category!="formation")||!ColorUtility.TryParseHtmlString(item.color,out _)||
                    !Finite(item.pulseInterval)||item.pulseInterval<0||!Finite(item.armSeconds)||item.armSeconds<0||!Finite(item.triggerRadius)||item.triggerRadius<0||
                    !Finite(item.statusSeconds)||item.statusSeconds<0||
                    (item.category=="formation"&&(item.radius<=0||item.duration<=0))||
                    ((item.id=="thunder"||item.id=="frostsigil"||item.id=="freeze"||item.id=="repair"||item.id=="strike")&&(item.pulseInterval<=0||item.pulseInterval>item.duration))||
                    (item.id=="mine"&&(item.triggerRadius<=0||item.triggerRadius>item.radius||item.armSeconds>=item.duration))||
                    ((item.id=="freeze"||item.id=="repair")&&item.statusSeconds<=0)||
                    (item.id=="strike"&&item.armSeconds>=item.duration)||
                    (item.id=="overload"&&item.magnitude<1)||
                    ((item.id=="emp"||item.id=="corrosion")&&item.magnitude>1)||
                    ((item.id=="gravity"||item.id=="decoy"||item.id=="freeze")&&item.magnitude>.8f)||(item.id=="corrosion"&&(item.magnitude>1||item.statusSeconds<=0)))
                    throw new ArgumentException("补给配置无效");
            var r=value.rewards;
            if(r.perWave<0||r.victory<0||r.flawless<0||r.firstVictory<0||r.difficultyMultipliers==null||r.difficultyMultipliers.Length!=3)
                throw new ArgumentException("奖励配置无效");
            foreach(float multiplier in r.difficultyMultipliers)if(!Finite(multiplier)||multiplier<=0||multiplier>10)throw new ArgumentException("难度奖励倍率无效");
            return value;
        }
        private static bool Finite(float v)=>!float.IsNaN(v)&&!float.IsInfinity(v);
        public SkinDefinition Skin(string id)=>Array.Find(skins,x=>x.id==id);
        public SupplyDefinition Supply(string id)=>Array.Find(supplies,x=>x.id==id);
        public WeaponUnlockDefinition Weapon(string id)=>Array.Find(weaponUnlocks,x=>x.id==id);
        public bool HasWeapon(string id)=>Array.IndexOf(weapons,id)>=0;
        public int Reward(int amount,int difficulty)=>Mathf.FloorToInt(amount*rewards.difficultyMultipliers[Mathf.Clamp(difficulty,0,2)]);
        public static string WeaponName(string id)=>WeaponRoster.Name(id);
    }
}
