using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Meta;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class AdaptiveReinforcementTests
    {
        private BalanceCatalog balance;
        private sealed class Target:ITowerTarget,ITowerHitReceiver,ICombatStatusTarget,IArsenalStatusTarget
        {
            public int TargetId {get;set;}
            public Vector3 Position {get;set;}
            public Vector3 PreviousPosition=>Position;
            public float HitRadius=>.1f;
            public float Hp=1000000,Shield=100,SlowAmount,SlowSeconds,Burn,BurnSeconds,Break,BreakSeconds,Disruption;public int Chills,Hits;
            public float ShieldPoints=>Shield;
            public float ControlFraction=>SlowAmount;
            public bool IsAlive=>Hp>0;
            public bool IsBoss=>false;
            public double Received;
            public void Damage(float amount){Received+=amount;Hp-=amount;Hits++;}
            public void ReceiveHit(TowerHit hit)=>Damage(hit.Amount);
            public void Slow(int source,float amount,float seconds){SlowAmount=amount;SlowSeconds=seconds;}
            public bool AddChill(int n){Chills+=n;return true;}
            public bool TryFreeze(float seconds)=>true;
            public void ApplyBurn(float dps,float seconds,int source){Burn=dps;BurnSeconds=seconds;}
            public void ApplyArmorBreak(float amount,float seconds){Break=amount;BreakSeconds=seconds;}
            public void Disrupt(float fraction,float seconds){Shield*=1-fraction;Disruption=seconds;}
        }
        [SetUp]public void Setup()=>balance=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));
        private static IEnumerable<string> Weapons()=>WeaponRoster.Ids;
        private static IEnumerable<string> Supplies()=>CampaignPerks.SupplyIds;
        private TowerInstance Tower(string id,CampaignPerks perks=null,Vector3 at=default)
        {
            var tower=TowerInstance.CreatePreview(balance.GetTower(id),new GridCell("perk",SurfaceKind.Floor,Vector2Int.zero,at));
            typeof(TowerInstance).GetProperty("Perks").SetValue(tower,perks??new CampaignPerks());return tower;
        }
        private static void Select(CampaignPerks perks,TowerInstance tower,int variant)
        {
            perks.BuildOffers(new[]{tower},null,17);int slot=perks.Offers.ToList().FindIndex(x=>x.Variant==variant);
            Assert.GreaterOrEqual(slot,0);Assert.IsTrue(perks.SelectOffer(slot,new[]{tower},null));
        }
        private static void SelectSupply(CampaignPerks perks,string id,int variant)
        {
            perks.BuildOffers(Array.Empty<TowerInstance>(),new[]{id},17);int slot=perks.Offers.ToList().FindIndex(x=>x.Variant==variant);
            Assert.IsTrue(perks.SelectOffer(slot,Array.Empty<TowerInstance>(),new[]{id}));
        }
        [TestCaseSource(nameof(Weapons))]
        public void EveryWeaponGetsThreeDistinctApplicableCardsAndResets(string id)
        {
            var perks=new CampaignPerks();var tower=Tower(id,perks);float range=tower.Range;
            perks.BuildOffers(new[]{tower},null,9);Assert.AreEqual(3,perks.Offers.Count);
            CollectionAssert.AreEquivalent(new[]{0,1,2},perks.Offers.Select(x=>x.Variant));
            foreach(var offer in perks.Offers){Assert.AreEqual(id,offer.TargetId);Assert.IsFalse(offer.IsSupply);Assert.IsTrue(perks.OfferDescription(offer).Contains("0 → 1阶"));}
            Select(perks,tower,2);Assert.AreEqual(range*1.15f,tower.Range,.0001f);
            perks.Reset();Assert.AreEqual(range,tower.Range,.0001f);Assert.AreEqual(0,perks.ChoicesMade);Assert.AreEqual(0,perks.Offers.Count);
        }
        [TestCaseSource(nameof(Weapons))]
        public void PowerCardChangesActualDamageControlOrAuraWithoutMutatingCatalog(string id)
        {
            string before=JsonUtility.ToJson(balance.GetTower(id));var perks=new CampaignPerks();var basic=Tower(id);var boosted=Tower(id,perks);Select(perks,boosted,0);
            if(id=="Beacon"||id=="Prism")
            {
                var a=Tower("MG");var b=Tower("MG");TowerAuraSystem.Resolve(new[]{basic,a},true);TowerAuraSystem.Resolve(new[]{boosted,b},true);
                Assert.Greater(id=="Beacon"?b.AuraRateBonus:b.AuraRangeBonus,id=="Beacon"?a.AuraRateBonus:a.AuraRangeBonus);
            }
            else
            {
                float z=id=="Mortar"?1f:.4f;var a=new Target{TargetId=1,Position=new Vector3(0,.16f,z)};var b=new Target{TargetId=2,Position=a.Position};
                var ca=new TowerCombat(basic,_=>{});var cb=new TowerCombat(boosted,_=>{});
                // A consumed trap is removed immediately in actual placement.
                bool firedTrap=false;
                for(int i=0;i<400;i++){ca.Step(.01f,Vector3.zero,new[]{a},null);cb.Step(.01f,Vector3.zero,new[]{b},null);if(id=="Trap"){firedTrap=true;break;}}
                if(id=="Frost"||id=="Gravity")Assert.Greater(b.SlowAmount,a.SlowAmount);
                else if(id=="Cryo")Assert.Greater(b.Chills,a.Chills);
                else if(id=="Sunder")Assert.Greater(b.Break,a.Break);
                else if(id=="Disruptor")Assert.Less(b.Shield,a.Shield);
                else Assert.Greater(b.Received,a.Received,id+(firedTrap?" explosion":" output"));
            }
            Assert.AreEqual(before,JsonUtility.ToJson(balance.GetTower(id)),"No baseline mutation");
        }
        [TestCase("MG")][TestCase("Sniper")][TestCase("Blade")][TestCase("Cryo")]
        public void MasteryActuallyIncreasesFiringCadence(string id)
        {
            var perks=new CampaignPerks();var a=Tower(id);var b=Tower(id,perks);Select(perks,b,1);
            var ta=new Target{TargetId=1,Position=new Vector3(0,.16f,.4f)};var tb=new Target{TargetId=2,Position=ta.Position};
            var ca=new TowerCombat(a,_=>{});var cb=new TowerCombat(b,_=>{});int ac=0,bc=0;ca.Fired+=(x,y,z)=>ac++;cb.Fired+=(x,y,z)=>bc++;
            for(int i=0;i<2000;i++){ca.Step(.01f,Vector3.zero,new[]{ta},null);cb.Step(.01f,Vector3.zero,new[]{tb},null);}
            Assert.Greater(bc,ac);
        }
        [TestCase("Laser")][TestCase("Tesla")][TestCase("Railgun")]
        public void MasteryActuallyAddsHitTargets(string id)
        {
            var perks=new CampaignPerks();var a=Tower(id);var b=Tower(id,perks);Select(perks,b,1);
            var ta=Enumerable.Range(0,8).Select(i=>new Target{TargetId=i+1,Position=new Vector3(0,.16f,.25f+i*.18f)}).ToArray();
            var tb=Enumerable.Range(0,8).Select(i=>new Target{TargetId=i+11,Position=ta[i].Position}).ToArray();
            new TowerCombat(a,_=>{}).Step(.01f,Vector3.zero,ta,null);new TowerCombat(b,_=>{}).Step(.01f,Vector3.zero,tb,null);
            Assert.AreEqual(ta.Count(x=>x.Hits>0)+1,tb.Count(x=>x.Hits>0));
        }
        [TestCase("Beacon")][TestCase("Prism")]
        public void MasteryAddsComplementaryAura(string id)
        {
            var perks=new CampaignPerks();var source=Tower(id,perks);var ally=Tower("MG");Select(perks,source,1);
            TowerAuraSystem.Resolve(new[]{source,ally},true);Assert.AreEqual(.05f,id=="Beacon"?ally.AuraRangeBonus:ally.AuraRateBonus,.001f);
        }
        [TestCase("Frost")][TestCase("Gravity")][TestCase("Flame")][TestCase("Sunder")][TestCase("Disruptor")]
        public void MasteryExtendsActualAppliedStatus(string id)
        {
            var perks=new CampaignPerks();var a=Tower(id);var b=Tower(id,perks);Select(perks,b,1);
            var ta=new Target{TargetId=1,Position=new Vector3(0,.16f,.4f)};var tb=new Target{TargetId=2,Position=ta.Position};
            new TowerCombat(a,_=>{}).Step(.01f,Vector3.zero,new[]{ta},null);new TowerCombat(b,_=>{}).Step(.01f,Vector3.zero,new[]{tb},null);
            float before=id=="Flame"?ta.BurnSeconds:id=="Sunder"?ta.BreakSeconds:id=="Disruptor"?ta.Disruption:ta.SlowSeconds;
            float after=id=="Flame"?tb.BurnSeconds:id=="Sunder"?tb.BreakSeconds:id=="Disruptor"?tb.Disruption:tb.SlowSeconds;
            Assert.Greater(before,0);Assert.AreEqual(before+1,after,.001f);
        }
        [TestCase("Mortar")][TestCase("Missile")]
        public void MasteryBlastHitsAnEnemyOutsideTheOriginalRadius(string id)
        {
            var perks=new CampaignPerks();var a=Tower(id);var b=Tower(id,perks);Select(perks,b,1);
            float radius=a.Stats.splashRadiusMeters.value;
            var ta=new[]{new Target{TargetId=1,Position=new Vector3(0,.16f,1)},new Target{TargetId=2,Position=new Vector3(radius*1.1f,.16f,1)}};
            var tb=ta.Select(x=>new Target{TargetId=x.TargetId+10,Position=x.Position}).ToArray();
            var ca=new TowerCombat(a,_=>{});var cb=new TowerCombat(b,_=>{});
            for(int i=0;i<80;i++){ca.Step(.01f,Vector3.zero,ta,null);cb.Step(.01f,Vector3.zero,tb,null);}
            Assert.AreEqual(0,ta[1].Hits);Assert.Greater(tb[1].Hits,0);
        }
        [Test]
        public void ScatterMasteryHitsAFormerlyExcludedSideTarget()
        {
            var perks=new CampaignPerks();var a=Tower("Scatter");var b=Tower("Scatter",perks);Select(perks,b,1);
            var ta=new[]{new Target{TargetId=1,Position=new Vector3(0,.16f,.4f)},new Target{TargetId=2,Position=new Vector3(Mathf.Sin(35*Mathf.Deg2Rad)*.5f,.16f,Mathf.Cos(35*Mathf.Deg2Rad)*.5f)}};
            var tb=ta.Select(x=>new Target{TargetId=x.TargetId+10,Position=x.Position}).ToArray();
            new TowerCombat(a,_=>{}).Step(.01f,Vector3.zero,ta,null);new TowerCombat(b,_=>{}).Step(.01f,Vector3.zero,tb,null);
            Assert.AreEqual(0,ta[1].Hits);Assert.Greater(tb[1].Hits,0);
        }
        [Test]
        public void TrapMasteryReturnsFifteenPercentOfBaseCostAtTrigger()
        {
            var wallet=new Wallet();var placement=new TowerPlacement(balance,wallet);var grid=GridBuilder.Build(SandboxRoom.Create(Vector3.zero),Vector3.zero,ScanMode.Sandbox);
            placement.SetGrid(grid);var cell=grid.PlaceableCells.First(x=>x.Surface==SurfaceKind.Floor);
            Assert.IsTrue(placement.TryBuild("Trap",cell,out var trap,out _));Select(placement.Perks,trap,1);
            int before=wallet.Coins;placement.Consume(trap);
            Assert.AreEqual(before+Mathf.FloorToInt(trap.Definition.buildCost*.15f),wallet.Coins);Assert.IsTrue(trap.Consumed);
        }
        private sealed class Wallet:ITowerWallet
        {
            public int Coins{get;private set;}=1000;
            public bool TrySpend(int amount){if(Coins<amount)return false;Coins-=amount;return true;}
            public void Credit(int amount)=>Coins+=amount;
        }
        [TestCaseSource(nameof(Supplies))]
        public void EverySupplyOffersPowerLifetimeAndCoverageOnlyWhenActive(string id)
        {
            var perks=new CampaignPerks();perks.BuildOffers(Array.Empty<TowerInstance>(),new[]{id},11);
            Assert.AreEqual(3,perks.Offers.Count);CollectionAssert.AreEquivalent(new[]{0,1,2},perks.Offers.Select(x=>x.Variant));
            foreach(var offer in perks.Offers){Assert.AreEqual(id,offer.TargetId);Assert.IsTrue(offer.IsSupply);Assert.IsNotEmpty(perks.OfferDescription(offer));}
            Assert.IsFalse(perks.SelectOffer(0,Array.Empty<TowerInstance>(),Array.Empty<string>()),"Expired/used supply must not be selectable");
            Assert.AreEqual(0,perks.ChoicesMade);SelectSupply(perks,id,1);Assert.AreEqual(15,perks.SupplyLifetime(id));
            Assert.AreEqual(0,perks.SupplyLifetime("not-active"));perks.Reset();Assert.AreEqual(0,perks.SupplyLifetime(id));
        }
        [Test]
        public void OffersIncludeBothCategoriesDeduplicateAndRejectSoldTargetsAndStaleClicks()
        {
            var perks=new CampaignPerks();var a=Tower("Sniper",perks);var b=Tower("Beacon",perks);
            perks.BuildOffers(new[]{a,a,b},new[]{"gravity","gravity","unknown"},5);
            Assert.AreEqual(3,perks.Offers.Count);Assert.AreEqual(3,perks.Offers.Select(x=>x.Key).Distinct().Count());
            Assert.IsTrue(perks.Offers.Any(x=>x.IsSupply));Assert.IsTrue(perks.Offers.Any(x=>!x.IsSupply));
            Assert.IsTrue(perks.Offers.All(x=>x.TargetId=="Sniper"||x.TargetId=="Beacon"||x.TargetId=="gravity"));
            var sold=perks.Offers.First(x=>!x.IsSupply);var targets=new[]{a,b}.Where(t=>t.Definition.id!=sold.TargetId).ToArray();
            Assert.IsFalse(perks.SelectOffer(perks.Offers.ToList().IndexOf(sold),targets,new[]{"gravity"}));
            Assert.IsTrue(perks.SelectOffer(0,new[]{a,b},new[]{"gravity"}));Assert.IsFalse(perks.SelectOffer(0,new[]{a,b},new[]{"gravity"}));
            Assert.AreEqual(1,perks.ChoicesMade);
        }
        [Test]
        public void SameClassExistingAndNewTowersShareRanksAndOnlyThreeChoicesAreAllowed()
        {
            var perks=new CampaignPerks();var a=Tower("Sniper",perks);Select(perks,a,0);Select(perks,a,0);Select(perks,a,0);
            var b=Tower("Sniper",perks);Assert.AreEqual(a.DamageMultiplier,b.DamageMultiplier);Assert.AreEqual(Mathf.Pow(1.25f,3),b.DamageMultiplier,.0001f);
            perks.BuildOffers(new[]{a},null,8);Assert.IsFalse(perks.SelectOffer(0,new[]{a},null));Assert.AreEqual(3,perks.ChoicesMade);
            perks.Reset();Assert.AreEqual(1,a.DamageMultiplier);Assert.AreEqual(1,b.DamageMultiplier);
        }
        [Test]
        public void LiveFieldsGrowAndExtendImmediatelyButExpiredFieldsNeverRevive()
        {
            var catalog=ProgressionCatalog.Load();var perks=new CampaignPerks();var item=catalog.Supply("gravity");
            string original=JsonUtility.ToJson(item);var field=new SupportField(item,Vector3.zero,-1,1,null,perks);
            field.Step(10,Array.Empty<RoomSiege.Enemies.EnemyActor>());SelectSupply(perks,"gravity",1);
            Assert.AreEqual(50,field.Remaining,.001f);SelectSupply(perks,"gravity",2);Assert.AreEqual(item.radius*1.15f,field.Radius,.001f);
            Assert.AreEqual(original,JsonUtility.ToJson(item));field.Step(100,Array.Empty<RoomSiege.Enemies.EnemyActor>());Assert.IsTrue(field.Finished);
            SelectSupply(perks,"gravity",1);Assert.IsTrue(field.Finished);Assert.AreEqual(0,field.Remaining);
        }
        [Test]
        public void ActiveRepairFieldUsesStrongerHealingAndFasterPulses()
        {
            var perks=new CampaignPerks();var item=ProgressionCatalog.Load().Supply("repair");SelectSupply(perks,"repair",0);SelectSupply(perks,"repair",2);
            var field=new SupportField(item,Vector3.zero,-1,1,null,perks);float healed=0;
            field.Step(20,Array.Empty<RoomSiege.Enemies.EnemyActor>(),null,h=>healed+=h);
            Assert.AreEqual(.5f*1.25f*5,healed,.001f);
        }
        [Test]
        public void EmptyFieldGetsOnlyCoreFallbackAndNoUndeployedWeapon()
        {
            var perks=new CampaignPerks();perks.BuildOffers(Array.Empty<TowerInstance>(),Array.Empty<string>(),19);
            Assert.AreEqual(3,perks.Offers.Count);Assert.IsTrue(perks.Offers.All(x=>x.IsCore));
            Assert.IsTrue(perks.SelectOffer(1,Array.Empty<TowerInstance>(),Array.Empty<string>()));Assert.AreEqual(.8f,perks.TacticalCooldown,.001f);
        }
    }
}
