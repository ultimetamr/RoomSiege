using System;
using System.Collections.Generic;
using System.Linq;
using RoomSiege.Data;
using UnityEngine;

namespace RoomSiege.Towers
{
    public sealed class ReinforcementOffer
    {
        public string TargetId {get;}
        public bool IsSupply {get;}
        public int Variant {get;}
        public bool IsCore=>TargetId=="core";
        public string Key=>(IsSupply?"supply:":"weapon:")+TargetId+":"+Variant;
        public ReinforcementOffer(string targetId,bool supply,int variant){TargetId=targetId;IsSupply=supply;Variant=variant;}
    }
    // Only this run owns these ranks. Existing towers/fields read them live; catalogs are immutable.
    public sealed class CampaignPerks
    {
        private readonly int[] tiers=new int[3];
        private readonly Dictionary<string,int[]> weaponRanks=new Dictionary<string,int[]>();
        private readonly Dictionary<string,int[]> supplyRanks=new Dictionary<string,int[]>();
        private readonly List<ReinforcementOffer> offers=new List<ReinforcementOffer>();
        public IReadOnlyList<ReinforcementOffer> Offers=>offers.AsReadOnly();
        public Func<IEnumerable<string>> ActiveSupplyTargets {get;set;}
        public static readonly string[] SupplyIds={"freeze","emp","overload","decoy","repair","strike","firefield","gravity","mine","thunder","corrosion","frostsigil"};
        private static readonly string[] SupplyNames={"急冻脉冲","电磁干扰","超载电池","诱饵信标","紧急维修","轨道打击","烈焰焚烧阵","重力迟滞阵","感应爆破雷","雷暴裁决阵","蚀甲破防印","寒霜封锁阵"};
        public int ChoicesMade {get;private set;}
        public int Rank(string id,int variant,bool supply=false)=>variant>=0&&variant<3&&(supply?supplyRanks:weaponRanks).TryGetValue(id,out var values)?values[variant]:0;
        public float WeaponDamage(string id)=>WeaponRoster.IsSupport(id)?1:Mathf.Pow(1.25f,Rank(id,0));
        public float WeaponRange(string id)=>Mathf.Pow(1.15f,Rank(id,2));
        public float WeaponRate(string id)=>id=="MG"||id=="Sniper"||id=="Blade"||id=="Cryo"?Mathf.Pow(1.2f,Rank(id,1)):1;
        public float WeaponStrength(string id)=>Mathf.Pow(1.25f,Rank(id,0));
        public float WeaponControl(string id)=>.05f*Rank(id,0);
        public float WeaponLinger(string id)=>Rank(id,1);
        public int WeaponExtraTargets(string id)=>Rank(id,1);
        public float WeaponBlast(string id)=>Mathf.Pow(1.2f,Rank(id,1));
        public float SupplyPower(string id)=>Mathf.Pow(1.25f,Rank(id,0,true));
        public float SupplyRange(string id)=>Mathf.Pow(1.15f,Rank(id,2,true));
        public float SupplyLifetime(string id)=>15*Rank(id,1,true);
        public float TacticalCooldown=>Mathf.Pow(.8f,Rank("core",1));
        public float TacticalDuration=>2+.5f*Rank("core",2);
        public void BuildOffers(IEnumerable<TowerInstance> deployed,IEnumerable<string> activeSupplies,int seed)
        {
            offers.Clear();var pool=new List<ReinforcementOffer>();
            foreach(string id in WeaponTargets(deployed))for(int variant=0;variant<3;variant++)pool.Add(new ReinforcementOffer(id,false,variant));
            foreach(string id in SupplyTargets(activeSupplies))for(int variant=0;variant<3;variant++)pool.Add(new ReinforcementOffer(id,true,variant));
            if(pool.Count==0){for(int v=0;v<3;v++)offers.Add(new ReinforcementOffer("core",false,v));return;}
            var random=new System.Random(seed);
            for(int i=pool.Count-1;i>0;i--){int other=random.Next(i+1);var swap=pool[i];pool[i]=pool[other];pool[other]=swap;}
            // Include both deployed weapons and live supplies when both categories exist.
            AddFirst(pool,x=>!x.IsSupply);AddFirst(pool,x=>x.IsSupply);
            while(offers.Count<3)
            {
                var candidate=pool.FirstOrDefault(x=>!offers.Any(o=>o.Key==x.Key)&&!offers.Any(o=>o.TargetId==x.TargetId&&o.IsSupply==x.IsSupply))??
                    pool.FirstOrDefault(x=>!offers.Any(o=>o.Key==x.Key));
                if(candidate==null)break;offers.Add(candidate);
            }
        }
        private void AddFirst(List<ReinforcementOffer> pool,Func<ReinforcementOffer,bool> predicate)
        {var first=pool.FirstOrDefault(predicate);if(first!=null)offers.Add(first);}
        private static IEnumerable<string> WeaponTargets(IEnumerable<TowerInstance> towers)=>(towers??Array.Empty<TowerInstance>())
            .Where(t=>t!=null&&!t.Removed&&!t.Consumed&&WeaponRoster.Contains(t.Definition.id)).Select(t=>t.Definition.id).Distinct();
        private static IEnumerable<string> SupplyTargets(IEnumerable<string> ids)=>(ids??Array.Empty<string>()).Where(id=>Array.IndexOf(SupplyIds,id)>=0).Distinct();
        public bool IsAvailable(ReinforcementOffer offer,IEnumerable<TowerInstance> deployed,IEnumerable<string> activeSupplies)
        {
            if(offer==null)return false;
            return offer.IsCore?!WeaponTargets(deployed).Any()&&!SupplyTargets(activeSupplies).Any():
                offer.IsSupply?SupplyTargets(activeSupplies).Contains(offer.TargetId):WeaponTargets(deployed).Contains(offer.TargetId);
        }
        public bool SelectOffer(int slot,IEnumerable<TowerInstance> deployed,IEnumerable<string> activeSupplies)
        {
            if(slot<0||slot>=offers.Count||ChoicesMade>=3||!IsAvailable(offers[slot],deployed,activeSupplies))return false;
            var choice=offers[slot];var ranks=choice.IsSupply?supplyRanks:weaponRanks;
            if(!ranks.TryGetValue(choice.TargetId,out var values)){values=new int[3];ranks.Add(choice.TargetId,values);}
            values[choice.Variant]++;ChoicesMade++;offers.Clear();return true;
        }
        public static string TargetName(ReinforcementOffer offer)=>offer.IsCore?"核心支援":offer.IsSupply?SupplyNames[Array.IndexOf(SupplyIds,offer.TargetId)]:WeaponRoster.Name(offer.TargetId);
        public string OfferName(ReinforcementOffer offer)=>TargetName(offer)+" · "+(offer.IsCore?new[]{"应急拨款","战术冷却","冰冻延长"}[offer.Variant]:offer.Variant==0?"效能强化":offer.Variant==1?offer.IsSupply?"持续支援":"专属进阶":"覆盖扩展");
        public string OfferDescription(ReinforcementOffer offer)
        {
            string id=offer.TargetId;int v=offer.Variant,current=Rank(id,v,offer.IsSupply),next=current+1;string effect;
            if(offer.IsCore)effect=v==0?"立即获得 40 金币":v==1?"应急冰冻冷却再 ×0.80":"应急冰冻时长 +0.5s";
            else if(offer.IsSupply)
            {
                if(v==1)effect="持续 / 守候时间 +15s\n现有效果立即延长";
                else if(v==2)effect=id=="repair"?"后续维修间隔再 ×0.80":id=="overload"?"超载额外射程 +10百分点":"作用半径再 ×1.15";
                else switch(id)
                {
                    case "freeze":effect="减速 +5百分点\n每次短冻 +0.5s（Boss封顶）";break;
                    case "emp":effect="削盾 +3百分点（上限99%）\n减速 +5百分点";break;
                    case "decoy":effect="减速 +5百分点（上限80%）";break;
                    case "gravity":effect="Boss减速 +5百分点\n离阵减速多保留 1s";break;
                    case "corrosion":effect="破甲 +5百分点（上限90%）";break;
                    case "frostsigil":effect="每次寒霜额外 +1层";break;
                    case "overload":effect="输出倍率 +0.25\n控制额外 +5百分点";break;
                    case "repair":effect="维修量再 ×1.25";break;
                    default:effect="伤害再 ×1.25";break;
                }
            }
            else if(v==2)effect="射程 / 作用半径再 ×1.15";
            else if(v==1)switch(id)
            {
                case "Laser":case "Tesla":case "Railgun":effect="额外命中 +1目标";break;
                case "Mortar":case "Missile":effect="爆炸半径再 ×1.20";break;
                case "Trap":effect="触发返还造价 +15百分点\n累计最多60%，不含升级";break;
                case "Scatter":effect="扇形半角 +10°";break;
                case "Flame":effect="离场余焰 +1s";break;
                case "Frost":case "Gravity":effect="离场减速 +1s";break;
                case "Beacon":effect="附近作战塔另获射程 +5%";break;
                case "Prism":effect="附近作战塔另获攻速 +5%";break;
                case "Sunder":effect="破甲持续时间 +1s";break;
                case "Disruptor":effect="召唤干扰 +1s（Boss减半）";break;
                default:effect="攻击 / 脉冲频率再 ×1.20";break;
            }
            else switch(id)
            {
                case "Frost":case "Gravity":effect="减速 +5百分点（上限80%）";break;
                case "Beacon":case "Prism":effect="光环增益再 ×1.25";break;
                case "Cryo":effect="每次寒霜额外 +1层";break;
                case "Sunder":effect="破甲 +10百分点（上限90%）";break;
                case "Disruptor":effect="削盾 +10百分点（上限95%）";break;
                default:effect="伤害再 ×1.25";break;
            }
            return $"{(offer.IsSupply?"持续道具":offer.IsCore?"核心":"在场武器")} · {current} → {next}阶\n{effect}\n"+(offer.IsCore?"本局有效":"现有与后续同类均生效");
        }
        // Compatibility for earlier run-rule tests and cold-chain/recovery hooks.
        public int Tier(int choice)=>choice>=0&&choice<3?tiers[choice]:0;
        public float MgRate=>Mathf.Pow(1.25f,tiers[0]);
        public float MgRange=>Mathf.Pow(.90f,tiers[0]);
        public float ChainRadius=>tiers[1]>0?1f+.25f*(tiers[1]-1):0;
        public float TrapRefundFraction=>Mathf.Min(.6f,(tiers[2]>0?.30f+.15f*(tiers[2]-1):0)+.15f*Rank("Trap",1));
        public static readonly string[] Names={"机枪超频","寒冰连锁","陷阱回收"};
        public string Description(int choice)=>choice==0?"MG 射速 ×1.25 / 射程 ×0.90":choice==1?"减速敌人死亡扩散寒冰":"陷阱触发回收造价";
        public bool Select(int choice){if(choice<0||choice>=3||ChoicesMade>=3)return false;tiers[choice]++;ChoicesMade++;return true;}
        public void Reset(){Array.Clear(tiers,0,tiers.Length);weaponRanks.Clear();supplyRanks.Clear();offers.Clear();ChoicesMade=0;}
    }
}
