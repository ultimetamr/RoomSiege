using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class ProgressionTests
    {
        private sealed class Memory:IProgressionStorage
        {
            public readonly string[] Slots={"",""};public bool Fail;
            public string Read(int slot)=>Slots[slot];
            public void Write(int slot,string value){if(Fail)throw new IOException("simulated full disk");Slots[slot]=value;}
        }
        private ProgressionCatalog catalog;private Memory storage;private ProgressionStore store;
        [SetUp]public void Setup(){catalog=ProgressionCatalog.Load();storage=new Memory();store=new ProgressionStore(catalog,storage);PaidSupplyFixture.Stock(store);}
        private void Money(int amount=10000)=>Assert.IsTrue(store.CompleteChallenge("test-funding",amount));
        private void Win(string id,int difficulty=0,bool damaged=false)
        {
            Assert.IsTrue(store.BeginRun(id,difficulty));if(damaged)Assert.IsTrue(store.RecordDamage(id));
            for(int wave=1;wave<=10;wave++)Assert.IsTrue(store.WaveCleared(id,wave));
            Assert.IsTrue(store.Settle(id,true));
        }
        [Test]public void CatalogContainsThreeSkinsForEveryWeaponAndTwelveSupplies()
        {
            Assert.AreEqual(54,catalog.skins.Length);Assert.AreEqual(12,catalog.supplies.Length);
            foreach(string id in WeaponRoster.Ids)CollectionAssert.AreEquivalent(new[]{"精英","史诗","传说"},catalog.skins.Where(x=>x.weaponId==id).Select(x=>x.rarity));
            Assert.AreEqual(14,catalog.weaponUnlocks.Length);
            Assert.Throws<ArgumentException>(()=>ProgressionCatalog.Parse(JsonUtility.ToJson(catalog).Replace("frostAmmo","typo")));
            Assert.Throws<ArgumentException>(()=>ProgressionCatalog.Parse(JsonUtility.ToJson(catalog).Replace("\"target\":\"ground\"","\"target\":\"typo\"")));
        }
        [TestCase(0,10)][TestCase(1,12)][TestCase(2,15)]
        public void DifficultyRewardFloorsEachPayment(int difficulty,int expected)=>Assert.AreEqual(expected,catalog.Reward(10,difficulty));
        [Test]public void FreshAccountsNeverReceiveFreeConsumablesAcrossReloads()
        {
            var memory=new Memory();var fresh=new ProgressionStore(catalog,memory);
            Assert.AreEqual(0,fresh.Fragments);Assert.AreEqual(0,fresh.Supplies.Count);
            foreach(var item in catalog.supplies)Assert.AreEqual(0,fresh.Count(item.id));
            fresh=new ProgressionStore(catalog,memory);foreach(var item in catalog.supplies)Assert.AreEqual(0,fresh.Count(item.id));
        }
        [Test]public void PurchaseInsufficientAndDuplicateNeverSpend()
        {
            Assert.IsFalse(store.Buy("mg-elite",out _));Assert.AreEqual(0,store.Fragments);
            Money();Assert.IsTrue(store.Buy("mg-elite",out _));Assert.AreEqual(9750,store.Fragments);
            Assert.IsFalse(store.Buy("mg-elite",out _));Assert.AreEqual(9750,store.Fragments);
            Assert.IsFalse(store.Buy("unknown",out _));Assert.AreEqual(9750,store.Fragments);
        }
        [Test]public void PurchaseAndEquipmentSurviveReloadWithoutAffectingOtherWeapons()
        {
            Money();Assert.IsTrue(store.Buy("mg-epic",out _));Assert.IsFalse(store.Equip("Laser","mg-epic",out _));
            Assert.IsTrue(store.Equip("MG","mg-epic",out _));Assert.IsTrue(store.Buy("freeze",out _));
            var loaded=new ProgressionStore(catalog,storage);Assert.AreEqual(9060,loaded.Fragments);Assert.AreEqual(2,loaded.Count("freeze"));
            Assert.AreEqual("mg-epic",loaded.Equipped("MG"));Assert.AreEqual("",loaded.Equipped("Laser"));
            Assert.IsTrue(loaded.Equip("MG","",out _));Assert.IsTrue(loaded.Owns("mg-epic"));
        }
        [Test]public void FailedWriteRollsBackWalletInventoryAndRun()
        {
            Money();storage.Fail=true;Assert.IsFalse(store.Buy("freeze",out _));Assert.AreEqual(10000,store.Fragments);Assert.AreEqual(1,store.Count("freeze"));
            Assert.IsFalse(store.BeginRun("failed",0));Assert.AreEqual("",store.RunId);
            var loaded=new ProgressionStore(catalog,storage);Assert.AreEqual(10000,loaded.Fragments);
        }
        [Test]public void ObserverExceptionDoesNotTurnDurablePurchaseIntoFailure()
        {
            Money();store.Changed+=()=>throw new Exception("simulated observer");
            Assert.IsTrue(store.Buy("mg-elite",out _));Assert.AreEqual(9750,store.Fragments);
            Assert.IsTrue(new ProgressionStore(catalog,storage).Owns("mg-elite"));
        }
        [Test]public void CorruptedNewestSlotRecoversPreviousCompleteTransaction()
        {
            Money();int before=store.Fragments;Assert.IsTrue(store.Buy("freeze",out _));storage.Slots[store.Revision%2]="broken";
            var loaded=new ProgressionStore(catalog,storage);Assert.IsTrue(loaded.Writable);Assert.AreEqual(before,loaded.Fragments);Assert.AreEqual(1,loaded.Count("freeze"));
        }
        [Test]public void BothCorruptedSlotsAreProtectedFromOverwrite()
        {
            storage.Slots[0]="bad0";storage.Slots[1]="bad1";var loaded=new ProgressionStore(catalog,storage);
            Assert.IsFalse(loaded.Writable);Assert.IsFalse(loaded.CompleteChallenge("gift",1000));Assert.IsFalse(loaded.Buy("freeze",out _));
            CollectionAssert.AreEqual(new[]{"bad0","bad1"},storage.Slots);
        }
        [Test]public void LoadoutRequiresExactlyFourDistinctRegisteredWeapons()
        {
            Assert.IsFalse(store.SetLoadout(new[]{"MG","MG","Laser","Trap"},out _));
            Assert.IsFalse(store.SetLoadout(new[]{"MG","Laser","Trap"},out _));
            Assert.IsFalse(store.SetLoadout(new[]{"MG","Unknown","Laser","Trap"},out _));
            Assert.IsTrue(store.SetLoadout(new[]{"Trap","MG","Laser","Frost"},out _));
            CollectionAssert.AreEqual(new[]{"Trap","MG","Laser","Frost"},new ProgressionStore(catalog,storage).Loadout);
        }
        [Test]public void CarryLimitsCountAndStockAndDoesNotConsumeOnSelection()
        {
            Assert.IsFalse(store.SetSupplies(new[]{"freeze","emp","overload","strike"},out _));
            Assert.IsFalse(store.SetSupplies(new[]{"freeze","freeze"},out _));Money();store.Buy("freeze",out _);store.Buy("freeze",out _);
            Assert.IsFalse(store.SetSupplies(new[]{"freeze","freeze","freeze"},out _));
            Assert.IsTrue(store.SetSupplies(new[]{"freeze","freeze","strike"},out _));Assert.AreEqual(3,store.Count("freeze"));
        }
        [Test]public void PurchasedInventoryCanBeUsedWithoutCarryAndPersistsEachConsumption()
        {
            Money();int initial=store.Count("strike");Assert.IsTrue(store.Buy("strike",out _));
            Assert.IsTrue(store.SetSupplies(Array.Empty<string>(),out _));
            Assert.IsFalse(store.Consume("r","strike"));Assert.IsTrue(store.BeginRun("r",0));
            Assert.IsFalse(store.Consume("wrong-run","strike"));Assert.IsFalse(store.Consume("r","unknown"));
            Assert.IsTrue(store.Consume("r","strike"));Assert.AreEqual(initial,store.Count("strike"));Assert.AreEqual(1,store.UsedItems);
            Assert.IsTrue(store.Consume("r","strike"));Assert.AreEqual(0,store.Count("strike"));Assert.AreEqual(2,store.UsedItems);
            Assert.IsFalse(store.Consume("r","strike"));
            var loaded=new ProgressionStore(catalog,storage);Assert.AreEqual(0,loaded.Count("strike"));Assert.IsEmpty(loaded.Supplies);
            loaded.Settle("r",false);Assert.IsFalse(loaded.Consume("r","emp"));
        }
        [Test]public void DuplicateOutOfOrderAndPostSettlementWavePaymentsAreRejected()
        {
            store.BeginRun("r",1);Assert.IsFalse(store.WaveCleared("r",2));Assert.IsFalse(store.WaveCleared("other",1));
            Assert.IsTrue(store.WaveCleared("r",1));Assert.IsFalse(store.WaveCleared("r",1));Assert.AreEqual(12,store.Fragments);
            Assert.IsFalse(store.Settle("r",true));Assert.IsTrue(store.Settle("r",false));Assert.IsFalse(store.WaveCleared("r",2));Assert.AreEqual(12,store.Fragments);
        }
        [Test]public void VictoryRewardsAndChallengesPayOnceAndFirstWinIsPerDifficulty()
        {
            Win("first");Assert.AreEqual(800,store.Fragments);Assert.IsFalse(store.Settle("first",true));Assert.AreEqual(800,store.Fragments);
            Win("repeat");Assert.AreEqual(1100,store.Fragments);
            Win("standard",1);Assert.AreEqual(1769,store.Fragments); // 120 wave +187 victory +62 flawless +300 first.
            Assert.IsFalse(store.BeginRun("standard",1));
        }
        [Test]public void InterruptedRunKeepsEarnedWavesAndCannotStartUnsettledSecondRun()
        {
            store.BeginRun("r",2);store.WaveCleared("r",1);store.WaveCleared("r",2);Assert.IsFalse(store.BeginRun("new",0));
            var loaded=new ProgressionStore(catalog,storage);Assert.IsTrue(loaded.Settle("r",false));Assert.AreEqual(30,loaded.Fragments);Assert.IsTrue(loaded.BeginRun("new",0));
        }
        [Test]public void DamageRecordPreventsFlawlessRewardEvenIfHealed()
        {
            Win("hurt",0,true);Assert.AreEqual(650,store.Fragments);Assert.IsFalse(store.Achievements.Contains("flawless"));
            Assert.IsTrue(store.Achievements.Contains("no-supplies"));
        }
        [Test]public void SharedFreezeStacksDecayAndProtectionCannotBeBypassed()
        {
            var frost=new FrostControl();for(int i=0;i<4;i++)Assert.IsFalse(frost.Chill(1,false));Assert.IsTrue(frost.Chill(1,false));
            Assert.IsTrue(frost.Frozen);Assert.IsFalse(frost.Freeze(2,false));frost.Tick(1);Assert.IsFalse(frost.Frozen);Assert.AreEqual(8,frost.ProtectionRemaining,.001);
            Assert.IsFalse(frost.Chill(5,false));Assert.IsFalse(frost.Freeze(2,false));frost.Tick(8);Assert.IsTrue(frost.Freeze(2,false));
            frost.Clear();Assert.AreEqual(0,frost.ProtectionRemaining);frost.Chill(4,false);frost.Tick(4);Assert.AreEqual(0,frost.Stacks);
        }
        [Test]public void BossFreezeIsShortAndLargeTickStillConsumesProtectionTime()
        {
            var frost=new FrostControl();Assert.IsTrue(frost.Chill(5,true));Assert.AreEqual(.35f,frost.FrozenRemaining,.001);
            frost.Tick(.85f);Assert.AreEqual(7.5f,frost.ProtectionRemaining,.001);frost.Tick(8);Assert.IsTrue(frost.Freeze(99,true));Assert.AreEqual(.6f,frost.FrozenRemaining,.001);
            frost.Tick(float.NaN);Assert.AreEqual(.6f,frost.FrozenRemaining,.001);frost.Clear();Assert.IsTrue(frost.CanFreeze);
        }
        private sealed class Wallet:ITowerWallet
        {public int Coins=>10000;public bool TrySpend(int value)=>true;public void Credit(int value){}}
        private sealed class Target:ITowerTarget,ICombatStatusTarget,ITowerHitReceiver
        {
            public int TargetId{get;set;}public Vector3 Position{get;set;}public Vector3 PreviousPosition=>Position;
            public float HitRadius=>.1f;public float Hp=1000000,Shield,SlowAmount,Burn,Duration;public int Chills,Hits;
            public bool IsAlive=>Hp>0;public float ShieldPoints=>Shield;public float ControlFraction=>SlowAmount;
            public void Damage(float amount){Hp-=amount;Hits++;}public void ReceiveHit(TowerHit hit){float absorb=Mathf.Min(Shield,hit.Amount);Shield-=absorb;Damage(hit.Amount-absorb);}
            public void Slow(int source,float value,float duration){SlowAmount=value;Duration=duration;}
            public bool AddChill(int amount){Chills+=amount;return true;}public bool TryFreeze(float seconds)=>true;
            public void ApplyBurn(float dps,float duration,int source){Burn=dps;Duration=duration;}
        }
        private TowerInstance Tower(string id,string skin)
        {
            var balance=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));
            var instance=TowerInstance.CreatePreview(balance.GetTower(id),new GridCell("skin-test",SurfaceKind.Floor,Vector2Int.zero,new Vector3(2,0,0)));
            instance.Skin=catalog.Skin(skin);return instance;
        }
        private Target At(TowerInstance tower,float x=0,float z=.35f)=>new Target{Position=tower.Cell.Center+new Vector3(x,tower.Definition.singleUse?.03f:.16f,z)};
        [Test]public void EliteMachineGunActuallyFiresFasterWithoutChangingBaseStats()
        {
            var tower=Tower("MG","mg-elite");var target=At(tower);var engine=new TowerCombat(tower,_=>{});
            engine.Step(.001f,Vector3.zero,new[]{target},null);engine.Step(.8f,Vector3.zero,new[]{target},null);
            Assert.AreEqual(2,target.Hits);Assert.AreEqual(1.2f,tower.Stats.shotsPerSecond,.001f);
        }
        [Test]public void LegendaryMachineGunRequiresNearbyFrostAndMultipleFrostsDoNotMultiplyChill()
        {
            var tower=Tower("MG","mg-legend");var frost=Tower("Frost",null);var target=At(tower);
            var allies=new List<TowerInstance>{tower};var engine=new TowerCombat(tower,_=>{},()=>allies);
            engine.Step(.001f,Vector3.zero,new[]{target},null);Assert.IsFalse(tower.FrostEnchanted);Assert.AreEqual(0,target.Chills);
            allies.Add(frost);allies.Add(Tower("Frost",null));engine.Step(1,Vector3.zero,new[]{target},null);
            Assert.IsTrue(tower.FrostEnchanted);Assert.AreEqual(target.Hits-1,target.Chills);
        }
        [Test]public void LegendaryFrostEnchantsBasicMachineGunAndRangeBonusIsSingle()
        {
            var mg=Tower("MG",null);var frost=Tower("Frost","frost-legend");var target=At(mg);
            var engine=new TowerCombat(mg,_=>{},()=>new[]{mg,frost});engine.Step(.01f,Vector3.zero,new[]{target},null);
            Assert.AreEqual(1,target.Chills);Assert.AreEqual(frost.Definition.rangeMeters.value*1.1f,frost.Range,.001);
        }
        [Test]public void FrostEliteLingersAndEpicPulsesAtEightSeconds()
        {
            var elite=Tower("Frost","frost-elite");var target=At(elite);new TowerCombat(elite,_=>{}).Step(.1f,Vector3.zero,new[]{target},null);
            Assert.AreEqual(elite.Stats.slowDurationSeconds.value+.5f,target.Duration,.001);
            var epic=Tower("Frost","frost-epic");target=At(epic);var engine=new TowerCombat(epic,_=>{});
            engine.Step(7.9f,Vector3.zero,new[]{target},null);Assert.AreEqual(0,target.Chills);engine.Step(.1f,Vector3.zero,new[]{target},null);Assert.AreEqual(2,target.Chills);
        }
        [Test]public void FocusLaserIncreasesDamageOnlyWhileRetainingTarget()
        {
            var tower=Tower("Laser","laser-elite");var target=At(tower);var engine=new TowerCombat(tower,_=>{});
            engine.Step(.1f,Vector3.zero,new[]{target},null);float hp=target.Hp;
            engine.Step(4,Vector3.zero,new[]{target},null);Assert.AreEqual(tower.Stats.damagePerSecond*4*1.12f,hp-target.Hp,.1);
            var next=At(tower);engine.Step(.1f,Vector3.zero,new[]{next},null);Assert.AreEqual(tower.Stats.damagePerSecond*.1f,1000000-next.Hp,.1);
        }
        [Test]public void ArcLaserTriggersOnlyOnShieldBreakAndHonorsCooldown()
        {
            var tower=Tower("Laser","laser-legend");var target=At(tower);target.Shield=.001f;var second=At(tower,.2f,.4f);
            var engine=new TowerCombat(tower,_=>{});engine.Step(.1f,Vector3.zero,new[]{target,second},target);
            Assert.Greater(second.Hits,0);int hits=second.Hits;target.Shield=.001f;
            engine.Step(.1f,Vector3.zero,new[]{target,second},target);Assert.AreEqual(hits,second.Hits);
        }
        [TestCase("trap-elite")][TestCase("trap-epic")][TestCase("trap-legend")]
        public void TrapSkinsHaveDistinctAreaEffectsAndConsumeOnce(string skin)
        {
            var tower=Tower("Trap",skin);var target=At(tower,0,0);target.SlowAmount=.3f;var second=At(tower,.6f,0);int consumed=0;
            var engine=new TowerCombat(tower,_=>consumed++);engine.Step(.1f,Vector3.zero,new[]{target,second},null);Assert.AreEqual(1,consumed);
            if(skin=="trap-elite"){Assert.AreEqual(1,target.Hits);Assert.AreEqual(1,second.Hits);}
            if(skin=="trap-epic"){Assert.Greater(target.Burn,0);Assert.Greater(second.Burn,0);}
            if(skin=="trap-legend"){Assert.AreEqual(5,target.Chills);Assert.AreEqual(5,second.Chills);}
        }
        [Test]public void ShatterTrapCannotCreateItsOwnSlowedRequirement()
        {
            var tower=Tower("Trap","trap-legend");var target=At(tower,0,0);new TowerCombat(tower,_=>{}).Step(.1f,Vector3.zero,new[]{target},null);
            Assert.AreEqual(0,target.Chills);
        }
        [Test]public void EpicMachineGunPiercesExactlyEveryFifthShot()
        {
            var tower=Tower("MG","mg-epic");var first=At(tower,0,.2f);var next=At(tower,0,.55f);var engine=new TowerCombat(tower,_=>{});
            engine.Step(.001f,Vector3.zero,new[]{first,next},first);engine.Step(3.34f,Vector3.zero,new[]{first,next},first);
            Assert.AreEqual(5,first.Hits);Assert.AreEqual(1,next.Hits);Assert.AreEqual(tower.Stats.damagePerHit*.92f*.6f,1000000-next.Hp,.1f);
        }
        [Test]public void EpicLaserRefractsToOnlyOneOffAxisEnemy()
        {
            var tower=Tower("Laser","laser-epic");var first=At(tower,0,.2f);var a=At(tower,.2f,.4f);var b=At(tower,-.2f,.4f);
            new TowerCombat(tower,_=>{}).Step(.2f,Vector3.zero,new[]{first,a,b},first);
            Assert.AreEqual(1,first.Hits);Assert.AreEqual(1,a.Hits);Assert.AreEqual(0,b.Hits);
            Assert.AreEqual(tower.Stats.damagePerSecond*.2f*.9f*.3f,1000000-a.Hp,.1f);
        }
        [Test]public void OverloadChangesFrostStrengthWithoutRaisingEightyPercentCap()
        {
            var tower=Tower("Frost",null);tower.OverloadRemaining=8;var target=At(tower);
            new TowerCombat(tower,_=>{}).Step(.1f,Vector3.zero,new[]{target},null);
            Assert.AreEqual(Mathf.Min(.8f,tower.Stats.slowFraction+.1f),target.SlowAmount,.001);
        }
        [Test]public void EquippedSkinChangesWorldModelColorAndRarityGeometry()
        {
            var tower=Tower("MG","mg-epic");var go=new GameObject("skin-model-test");var material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            try
            {
                var view=go.AddComponent<TowerView>();view.Build(tower,material);Assert.IsNotNull(go.transform.Find("Skin circuit 史诗"));
                Assert.IsTrue(ColorUtility.TryParseHtmlString(tower.Skin.color,out var expected));Assert.AreEqual(expected,TowerVisuals.ColorFor(tower));
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
