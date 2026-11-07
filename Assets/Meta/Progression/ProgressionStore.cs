using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using UnityEngine;

namespace RoomSiege.Progression
{
    public interface IProgressionStorage { string Read(int slot); void Write(int slot,string json); }
    public sealed class PlayerPrefsProgressionStorage:IProgressionStorage
    {
        private const string Key="RoomSiege.Progression.v1.";
        public string Read(int slot)=>PlayerPrefs.GetString(Key+slot,"");
        public void Write(int slot,string json){PlayerPrefs.SetString(Key+slot,json);PlayerPrefs.Save();}
    }
    [Serializable] public sealed class InventoryItem { public string id;public int count; }
    [Serializable] public sealed class EquippedSkin { public string weaponId,skinId; }
    [Serializable] public sealed class ProgressionRun
    {
        public string id;
        public int difficulty,completedWaves,reward,usedItems;
        public bool damaged,settled,won;
    }
    [Serializable] public sealed class ProgressionSave
    {
        public int version=1,revision,fragments;
        public bool starterGranted,hasRun;
        public List<string> ownedWeapons=new List<string>();
        public List<string> ownedSkins=new List<string>();
        public List<InventoryItem> inventory=new List<InventoryItem>();
        public List<EquippedSkin> equippedSkins=new List<EquippedSkin>();
        public List<string> loadout=new List<string>{"MG","Frost","Laser","Trap"};
        public List<string> supplies=new List<string>();
        public List<string> achievements=new List<string>();
        public bool purchaseLedgerReady;
        public List<string> purchasedWeapons=new List<string>();
        public List<string> purchasedSkins=new List<string>();
        public List<InventoryItem> purchasedInventory=new List<InventoryItem>();
        public string retiredTrialSnapshot="";
        public ProgressionRun run;
        public string lastRunId="";
        public int lastReward;
    }
    [Serializable] internal sealed class SaveEnvelope { public string payload,sha256; }
    public sealed class ProgressionStore
    {
        private readonly ProgressionCatalog catalog;
        private readonly IProgressionStorage storage;
        private ProgressionSave data;
        private bool trialRetirementPending;
        public bool Writable {get;private set;}=true;
        public string Error {get;private set;}="";
        public int Fragments=>data.fragments;
        public int Revision=>data.revision;
        public int LastReward=>data.run!=null?data.run.reward:data.lastReward;
        public string RunId=>data.run?.id??"";
        public bool RunSettled=>data.run==null||data.run.settled;
        public int UsedItems=>data.run?.usedItems??0;
        public IReadOnlyList<string> Loadout=>data.loadout.AsReadOnly();
        public IReadOnlyList<string> Supplies=>data.supplies.AsReadOnly();
        public IReadOnlyList<string> Achievements=>data.achievements.AsReadOnly();
        public event Action Changed;
        public ProgressionStore(ProgressionCatalog catalog,IProgressionStorage storage)
        {
            this.catalog=catalog??throw new ArgumentNullException(nameof(catalog));this.storage=storage??throw new ArgumentNullException(nameof(storage));
            string first=storage.Read(0),second=storage.Read(1);
            var a=Decode(first);var b=Decode(second);
            data=a==null?b:b==null?a:a.revision>=b.revision?a:b;
            if(data==null)
            {
                data=new ProgressionSave();
                if(!string.IsNullOrEmpty(first)||!string.IsNullOrEmpty(second)){Writable=false;Error="局外存档损坏，已保护原始数据，暂不允许交易";}
            }
            if(Writable&&(!data.starterGranted||!data.purchaseLedgerReady))
            {
                var next=Clone();next.starterGranted=true;
                // New accounts buy consumables. Legacy trial ownership has no purchase
                // provenance; archive it when retiring the grant rather than guess.
                if(!next.purchaseLedgerReady)
                {
                    next.purchaseLedgerReady=true;
                    next.purchasedWeapons=PlaytestUnlocked?new List<string>():next.ownedWeapons.ToList();
                    next.purchasedSkins=PlaytestUnlocked?new List<string>():next.ownedSkins.ToList();
                    next.purchasedInventory=PlaytestUnlocked?new List<InventoryItem>():next.inventory.Select(x=>new InventoryItem{id=x.id,count=x.count}).ToList();
                }
                Commit(next);
            }
        }
        public ProgressionSave Snapshot()=>Clone();
        public const string PlaytestGrantId="playtest-all-content-2026-11";
        public bool PlaytestUnlocked=>!trialRetirementPending&&data.achievements.Contains(PlaytestGrantId);
        public bool UnlockAllForPlaytest(int supplyStock=20)
        {
            if(!Writable||supplyStock<1||supplyStock>99)return false;
            if(PlaytestUnlocked)return true;
            var next=Clone();
            foreach(var weapon in catalog.weaponUnlocks)if(!next.ownedWeapons.Contains(weapon.id))next.ownedWeapons.Add(weapon.id);
            foreach(var skin in catalog.skins)if(!next.ownedSkins.Contains(skin.id))next.ownedSkins.Add(skin.id);
            foreach(var item in catalog.supplies)SetCount(next,item.id,Mathf.Max(Count(item.id),supplyStock));
            next.achievements.Add(PlaytestGrantId);
            // One durable grant; no wallet inflation, reward credit or repeated stock refill.
            return Commit(next);
        }
        public const string SupplyTestGrantId="playtest-supplies-2026-11-04-50";
        public bool RetirePlaytestAccess()
        {
            if(!Writable)return false;
            if(!data.achievements.Contains(PlaytestGrantId)&&!data.achievements.Any(x=>x.StartsWith("playtest-supplies-",StringComparison.Ordinal)))return true;
            trialRetirementPending=true;
            var next=Clone();
            if(string.IsNullOrEmpty(next.retiredTrialSnapshot))next.retiredTrialSnapshot=JsonUtility.ToJson(data);
            next.ownedWeapons=next.purchasedWeapons.ToList();next.ownedSkins=next.purchasedSkins.ToList();
            next.inventory=next.purchasedInventory.Where(x=>x.count>0).Select(x=>new InventoryItem{id=x.id,count=x.count}).ToList();
            next.equippedSkins.RemoveAll(x=>!next.ownedSkins.Contains(x.skinId));
            next.loadout=next.loadout.Where(id=>catalog.Weapon(id)==null||next.ownedWeapons.Contains(id))
                .Concat(new[]{"MG","Frost","Laser","Trap"}).Distinct().Take(4).ToList();
            next.supplies.Clear();next.achievements.Remove(PlaytestGrantId);next.achievements.RemoveAll(x=>x.StartsWith("playtest-supplies-",StringComparison.Ordinal));
            bool saved=Commit(next);if(saved)trialRetirementPending=false;return saved;
        }
        public bool GrantSupplyTestStock(int stock,string grantId=SupplyTestGrantId)
        {
            if(!Writable||!PlaytestUnlocked||stock<1||stock>99||string.IsNullOrEmpty(grantId)||!grantId.StartsWith("playtest-supplies-",StringComparison.Ordinal))return false;
            if(data.achievements.Contains(grantId))return true;
            var next=Clone();
            foreach(var item in catalog.supplies)SetCount(next,item.id,Mathf.Max(Count(item.id),stock));
            next.achievements.Add(grantId);
            return Commit(next);
        }
        public int Count(string id)=>(trialRetirementPending?data.purchasedInventory:data.inventory).Find(x=>x.id==id)?.count??0;
        public bool OwnsWeapon(string id)=>catalog.HasWeapon(id)&&(catalog.Weapon(id)==null||(trialRetirementPending?data.purchasedWeapons:data.ownedWeapons).Contains(id));
        public bool Owns(string id)=>(trialRetirementPending?data.purchasedSkins:data.ownedSkins).Contains(id);
        public string Equipped(string weapon){string id=data.equippedSkins.Find(x=>x.weaponId==weapon)?.skinId??"";return Owns(id)?id:"";}
        public SkinDefinition EquippedDefinition(string weapon)=>catalog.Skin(Equipped(weapon));
        public bool Buy(string id,out string reason)
        {
            reason="";if(!Writable){reason=Error;return false;}
            var skin=catalog.Skin(id);var item=catalog.Supply(id);var weapon=catalog.Weapon(id);
            if(skin==null&&item==null&&weapon==null){reason="商品不存在";return false;}
            if(weapon!=null&&OwnsWeapon(id)){reason="该武器已永久解锁";return false;}
            if(skin!=null&&!OwnsWeapon(skin.weaponId)){reason="请先解锁对应武器";return false;}
            if(skin!=null&&Owns(id)){reason="已拥有，无需重复购买";return false;}
            if(item!=null&&Count(id)>=99){reason="库存已达上限 99";return false;}
            int price=weapon!=null?weapon.price:skin!=null?skin.price:item.price;
            if(Fragments<price){reason=$"碎片不足，还差 {price-Fragments}";return false;}
            var next=Clone();next.fragments-=price;
            if(weapon!=null){next.ownedWeapons.Add(id);next.purchasedWeapons.Add(id);}
            else if(skin!=null){next.ownedSkins.Add(id);next.purchasedSkins.Add(id);}
            else
            {
                SetCount(next,id,Count(id)+1);
                var paid=next.purchasedInventory.Find(x=>x.id==id);
                if(paid==null)next.purchasedInventory.Add(new InventoryItem{id=id,count=1});else paid.count++;
            }
            if(!Commit(next)){reason=Error;return false;}
            reason=weapon!=null?"武器已永久解锁，请选择替换一个出战槽":skin!=null?"已购买，可在出战配置装备":"补给已入库";return true;
        }
        public bool Equip(string weapon,string skinId,out string reason)
        {
            reason="";var skin=catalog.Skin(skinId);
            if(!OwnsWeapon(weapon)||!string.IsNullOrEmpty(skinId)&&(skin==null||skin.weaponId!=weapon||!Owns(skinId)))
            {reason="皮肤未拥有或武器不匹配";return false;}
            var next=Clone();next.equippedSkins.RemoveAll(x=>x.weaponId==weapon);
            if(!string.IsNullOrEmpty(skinId))next.equippedSkins.Add(new EquippedSkin{weaponId=weapon,skinId=skinId});
            if(!Commit(next)){reason=Error;return false;}reason=string.IsNullOrEmpty(skinId)?"已恢复基础全息":"皮肤已穿戴";return true;
        }
        public bool SetLoadout(IEnumerable<string> ids,out string reason)
        {
            var list=ids?.ToList();reason="";
            if(list==null||list.Count!=4||list.Distinct().Count()!=4||list.Any(x=>!OwnsWeapon(x)))
            {reason="请选择 4 种不同的已解锁武器";return false;}
            if(list.All(RoomSiege.Data.WeaponRoster.IsSupport)){reason="阵容至少需要 1 种作战武器，辅助武器无法独立清场";return false;}
            var next=Clone();next.loadout=list;
            if(!Commit(next)){reason=Error;return false;}reason="四武器阵容已保存";return true;
        }
        // Legacy save compatibility only. Runtime items are available directly from inventory.
        public bool SetSupplies(IEnumerable<string> ids,out string reason)
        {
            var list=ids?.ToList();reason="";
            if(list==null||list.Count>3||list.Any(x=>catalog.Supply(x)==null)||list.GroupBy(x=>x).Any(g=>g.Count()>2||g.Count()>Count(g.Key)))
            {reason="最多携带 3 件，同类最多 2 件且不能超过库存";return false;}
            var next=Clone();next.supplies=list;
            if(!Commit(next)){reason=Error;return false;}reason="辅助道具已保存，未使用不消耗";return true;
        }
        public bool BeginRun(string id,int difficulty)
        {
            if(string.IsNullOrEmpty(id)||difficulty<0||difficulty>2||id==data.lastRunId)return false;
            if(data.run!=null&&!data.run.settled)return false;
            var next=Clone();next.hasRun=true;next.run=new ProgressionRun{id=id,difficulty=difficulty};next.lastRunId=id;next.lastReward=0;
            return Commit(next);
        }
        public bool WaveCleared(string id,int wave)
        {
            if(data.run==null||data.run.id!=id||data.run.settled||wave!=data.run.completedWaves+1||wave>10)return false;
            var next=Clone();next.run.completedWaves=wave;Grant(next,catalog.Reward(catalog.rewards.perWave,next.run.difficulty));
            return Commit(next);
        }
        public bool RecordDamage(string id)
        {
            if(data.run==null||data.run.id!=id||data.run.settled||data.run.damaged)return false;
            var next=Clone();next.run.damaged=true;return Commit(next);
        }
        public bool Settle(string id,bool victory)
        {
            if(data.run==null||data.run.id!=id||data.run.settled||victory&&data.run.completedWaves!=10)return false;
            var next=Clone();var run=next.run;run.settled=true;run.won=victory;
            if(victory)
            {
                Grant(next,catalog.Reward(catalog.rewards.victory,run.difficulty));
                if(!run.damaged)Grant(next,catalog.Reward(catalog.rewards.flawless,run.difficulty));
                Achievement(next,"first-victory-"+run.difficulty,catalog.rewards.firstVictory);
                if(!run.damaged)Achievement(next,"flawless",100);
                if(run.usedItems==0)Achievement(next,"no-supplies",100);
            }
            next.lastReward=run.reward;return Commit(next);
        }
        // The command must validate its target/effect before this durable inventory transaction.
        public bool Consume(string runId,string itemId)
        {
            if(data.run==null||data.run.id!=runId||data.run.settled||catalog.Supply(itemId)==null||Count(itemId)<=0)return false;
            var next=Clone();var paid=next.purchasedInventory.Find(x=>x.id==itemId);
            // Consume trial stock first so revoking a trial preserves unspent purchases.
            if(paid!=null&&Count(itemId)<=paid.count)paid.count=Mathf.Max(0,paid.count-1);
            SetCount(next,itemId,Count(itemId)-1);next.supplies.Remove(itemId);next.run.usedItems++;
            return Commit(next);
        }
        public bool CompleteChallenge(string id,int reward)
        {
            if(string.IsNullOrEmpty(id)||reward<0||data.achievements.Contains(id))return false;
            var next=Clone();Achievement(next,id,reward);return Commit(next);
        }
        private static void Achievement(ProgressionSave value,string id,int reward)
        {if(value.achievements.Contains(id))return;value.achievements.Add(id);Grant(value,reward);}
        private static void Grant(ProgressionSave value,int amount)
        {value.fragments=checked(value.fragments+amount);if(value.run!=null)value.run.reward=checked(value.run.reward+amount);}
        private static void SetCount(ProgressionSave value,string id,int count)
        {var entry=value.inventory.Find(x=>x.id==id);if(entry==null){entry=new InventoryItem{id=id};value.inventory.Add(entry);}entry.count=count;}
        private ProgressionSave Clone()
        {
            var value=JsonUtility.FromJson<ProgressionSave>(JsonUtility.ToJson(data));
            // Unity serializes inline null classes as empty objects; keep an explicit presence bit.
            if(!value.hasRun)value.run=null;
            return value;
        }
        private bool Commit(ProgressionSave next)
        {
            if(!Writable)return false;
            if(trialRetirementPending&&(next.achievements.Contains(PlaytestGrantId)||next.achievements.Any(x=>x.StartsWith("playtest-supplies-",StringComparison.Ordinal))))return false;
            try
            {
                next.revision=checked(data.revision+1);string payload=JsonUtility.ToJson(next);
                storage.Write(next.revision%2,JsonUtility.ToJson(new SaveEnvelope{payload=payload,sha256=Hash(payload)}));
                data=next;Error="";
            }
            catch(Exception error){Error="局外存档写入失败，操作未生效："+error.Message;return false;}
            // UI observers must never turn an already persisted purchase into a reported failure.
            if(Changed!=null)foreach(Action observer in Changed.GetInvocationList())
                try{observer();}catch(Exception error){Debug.LogWarning("[RoomSiege.Progression] observer: "+error.Message);}
            return true;
        }
        private ProgressionSave Decode(string json)
        {
            if(string.IsNullOrEmpty(json))return null;
            try
            {
                var envelope=JsonUtility.FromJson<SaveEnvelope>(json);
                if(envelope==null||string.IsNullOrEmpty(envelope.payload)||Hash(envelope.payload)!=envelope.sha256)return null;
                var value=JsonUtility.FromJson<ProgressionSave>(envelope.payload);
                if(value!=null&&!value.hasRun)value.run=null;
                if(value==null||value.version!=1||value.revision<0||value.fragments<0||value.inventory==null||value.ownedSkins==null||value.equippedSkins==null||value.loadout==null||value.supplies==null||value.achievements==null)return null;
                // Earlier v1 saves predate permanent weapon unlocks. Empty means only the four free weapons.
                if(value.ownedWeapons==null)value.ownedWeapons=new List<string>();
                if(value.purchasedWeapons==null)value.purchasedWeapons=new List<string>();
                if(value.purchasedSkins==null)value.purchasedSkins=new List<string>();
                if(value.purchasedInventory==null)value.purchasedInventory=new List<InventoryItem>();
                if(value.purchaseLedgerReady&&(value.purchasedWeapons.Any(x=>!value.ownedWeapons.Contains(x))||value.purchasedWeapons.Distinct().Count()!=value.purchasedWeapons.Count||
                    value.purchasedSkins.Any(x=>!value.ownedSkins.Contains(x))||value.purchasedSkins.Distinct().Count()!=value.purchasedSkins.Count||
                    value.purchasedInventory.Any(x=>x==null||catalog.Supply(x.id)==null||x.count<0||x.count>99||x.count>(value.inventory.Find(i=>i.id==x.id)?.count??0))||
                    value.purchasedInventory.Select(x=>x.id).Distinct().Count()!=value.purchasedInventory.Count))return null;
                if(value.ownedWeapons.Any(x=>catalog.Weapon(x)==null)||value.ownedWeapons.Distinct().Count()!=value.ownedWeapons.Count)return null;
                if(value.loadout.Any(x=>catalog.Weapon(x)!=null&&!value.ownedWeapons.Contains(x)))return null;
                if(value.loadout.Count!=4||value.loadout.Distinct().Count()!=4||value.loadout.Any(x=>!catalog.HasWeapon(x))||value.supplies.Count>3)return null;
                if(value.inventory.Any(x=>x==null||catalog.Supply(x.id)==null||x.count<0||x.count>99)||value.inventory.Select(x=>x.id).Distinct().Count()!=value.inventory.Count)return null;
                if(value.ownedSkins.Any(x=>catalog.Skin(x)==null)||value.equippedSkins.Any(x=>x==null||!value.ownedSkins.Contains(x.skinId)||catalog.Skin(x.skinId)?.weaponId!=x.weaponId))return null;
                if(value.supplies.Any(x=>catalog.Supply(x)==null)||value.supplies.GroupBy(x=>x).Any(g=>g.Count()>2||g.Count()>(value.inventory.Find(x=>x.id==g.Key)?.count??0)))return null;
                if(value.ownedSkins.Distinct().Count()!=value.ownedSkins.Count||value.equippedSkins.Select(x=>x.weaponId).Distinct().Count()!=value.equippedSkins.Count||value.achievements.Any(string.IsNullOrEmpty)||value.achievements.Distinct().Count()!=value.achievements.Count)return null;
                if(value.run!=null&&(string.IsNullOrEmpty(value.run.id)||value.run.difficulty<0||value.run.difficulty>2||value.run.completedWaves<0||value.run.completedWaves>10||value.run.reward<0||value.run.usedItems<0))return null;
                return value;
            }
            catch(Exception){return null;}
        }
        private static string Hash(string value)
        {using(var sha=SHA256.Create())return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(value))).Replace("-","");}
    }
}
