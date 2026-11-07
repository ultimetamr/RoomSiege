using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Scan;
using UnityEngine;

namespace RoomSiege.Towers.Tests
{
    public sealed class TowerTests
    {
        private sealed class Wallet:ITowerWallet
        {public int Coins{get;private set;}public Wallet(int coins){Coins=coins;}public bool TrySpend(int amount){if(amount<0||Coins<amount)return false;Coins-=amount;return true;}public void Credit(int amount){Coins+=amount;}}
        private sealed class Target:ITowerTarget
        {
            public int TargetId{get;set;}public Vector3 Position{get;set;}public Vector3 PreviousPosition{get;set;}
            public float HitRadius=>.14f;public float Hp=10000;public float SlowAmount;public float Duration;
            public bool IsAlive=>Hp>0;public void Damage(float amount){Hp-=amount;}public void Slow(int source,float fraction,float duration){SlowAmount=Mathf.Max(SlowAmount,fraction);Duration=duration;}
        }
        private BalanceCatalog catalog;private Wallet wallet;private TowerPlacement placement;private GridCell cell;
        [SetUp]public void Setup()
        {
            catalog=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));wallet=new Wallet(1000);placement=new TowerPlacement(catalog,wallet);
            cell=new GridCell("test-floor",SurfaceKind.Floor,new Vector2Int(5,0),new Vector3(2,0,0));
            placement.SetGrid(Grid(cell));
        }
        private static GridModel Grid(params GridCell[] cells)=>new GridModel(ScanMode.Sandbox,Vector3.zero,cells,new SpawnPoint[0]);
        private TowerInstance Build(string id,int level=1)
        {Assert.That(placement.TryBuild(id,cell,out var tower,out var reason),Is.True,reason);if(level>1)Assert.That(placement.TryUpgrade(tower,level,out reason),Is.True,reason);return tower;}
        private static Target At(Vector3 pos,int id=1)=>new Target{TargetId=id,Position=pos,PreviousPosition=pos};
        private sealed class EnteringTarget:ITowerTarget,ITowerTargetReadiness
        {
            public bool CanBeTargeted{get;set;}
            public int TargetId=>999;public Vector3 Position{get;set;}public Vector3 PreviousPosition=>Position;
            public float HitRadius=>.12f;public bool IsAlive=>Hp>0;public float Hp=100;
            public void Damage(float amount){Hp-=amount;}public void Slow(int source,float fraction,float duration){}
        }
        [TestCase("MG")][TestCase("Frost")][TestCase("Laser")][TestCase("Trap")]
        public void EveryWeaponWaitsUntilEnemyHasFinishedItsEntrance(string id)
        {
            var tower=Build(id,3);var enemy=new EnteringTarget{Position=cell.Center+Vector3.up*.16f};
            var engine=new TowerCombat(tower,placement.Consume);
            engine.Step(.1f,Vector3.zero,new[]{enemy},enemy);
            Assert.AreEqual(100,enemy.Hp,"Even focus and piercing attacks must respect entrance protection");
            Assert.IsFalse(tower.Consumed,"Protected enemies cannot consume a trap");
            enemy.CanBeTargeted=true;engine.Step(.1f,Vector3.zero,new[]{enemy},enemy);
            Assert.Less(enemy.Hp,100,"Protection ends; normal combat resumes");
        }
        [Test]public void CellPickerSnapsEdgesChoosesNearestAndRespectsOpaqueBlockers()
        {
            var adjacent=new GridCell("test-floor",SurfaceKind.Floor,new Vector2Int(6,0),cell.Center+Vector3.right*.4f);
            var grid=Grid(cell,adjacent);
            Assert.IsTrue(TowerCellPicker.TryPick(grid,new Ray(cell.Center+new Vector3(-.30f,1,0),Vector3.down),out var snapped,out _));
            Assert.AreEqual(cell.Center,snapped.Center);
            Assert.IsFalse(TowerCellPicker.TryPick(grid,new Ray(cell.Center+new Vector3(-.34f,1,0),Vector3.down),out _,out _));
            Assert.IsTrue(TowerCellPicker.TryPick(grid,new Ray(cell.Center+new Vector3(.19f,1,0),Vector3.down),out snapped,out _));
            Assert.AreEqual(cell.Center,snapped.Center);
            Assert.IsTrue(TowerCellPicker.TryPick(grid,new Ray(cell.Center+new Vector3(.21f,1,0),Vector3.down),out snapped,out _));
            Assert.AreEqual(adjacent.Center,snapped.Center);
            Assert.IsFalse(TowerCellPicker.TryPick(grid,new Ray(cell.Center+Vector3.up,Vector3.down),out _,out _,d=>true));
            Assert.IsFalse(TowerCellPicker.TryPick(null,new Ray(Vector3.up,Vector3.down),out _,out _));
        }
        [Test]public void PerkChoicesAreCappedAndResetWithoutTouchingBaseline()
        {
            var perks=placement.Perks;
            Assert.IsFalse(perks.Select(-1));Assert.IsFalse(perks.Select(3));
            Assert.IsTrue(perks.Select(0));Assert.IsTrue(perks.Select(0));Assert.IsTrue(perks.Select(0));
            Assert.IsFalse(perks.Select(1));Assert.AreEqual(3,perks.ChoicesMade);
            Assert.AreEqual(Mathf.Pow(1.25f,3),perks.MgRate,.00001f);
            Assert.AreEqual(Mathf.Pow(.9f,3),perks.MgRange,.00001f);
            placement.ResetCampaign();Assert.AreEqual(0,perks.ChoicesMade);Assert.AreEqual(1,perks.MgRate);
            Assert.AreEqual(0,perks.ChainRadius);Assert.AreEqual(0,perks.TrapRefundFraction);
        }
        [Test]public void OverclockChangesActualFireRateAndRangeOfAlreadyDeployedMachineGun()
        {
            var tower=Build("MG");float baseline=tower.Range;var target=At(cell.Center+Vector3.up*.16f);
            placement.Perks.Select(0);Assert.AreEqual(baseline*.9f,tower.Range,.00001f);
            var combat=new TowerCombat(tower,placement.Consume);
            combat.Step(.001f,Vector3.zero,new[]{target},null);
            combat.Step(.68f,Vector3.zero,new[]{target},null);
            Assert.AreEqual(10000-2*tower.Stats.damagePerHit,target.Hp,.001f,"Second shot must use the boosted interval");
            Assert.AreEqual(1.2f,tower.Stats.shotsPerSecond,.00001f,"Shared baseline stays unchanged");
        }
        [Test]public void TrapRecoveryRefundsBaseCostOnceAndCannotBeSoldAfterConsumption()
        {
            placement.Perks.Select(2);var tower=Build("Trap",3);int before=wallet.Coins;
            placement.Consume(tower);Assert.AreEqual(before+9,wallet.Coins);
            placement.Consume(tower);Assert.AreEqual(before+9,wallet.Coins);
            Assert.IsFalse(placement.TrySell(tower,out _));Assert.AreEqual(before+9,wallet.Coins);
        }
        [Test]public void PerkTiersKeepColdChainAndRecoveryWithinDocumentedBounds()
        {
            var cold=new CampaignPerks();for(int i=0;i<3;i++)cold.Select(1);
            Assert.AreEqual(1.5f,cold.ChainRadius,.00001f);
            var recovery=new CampaignPerks();for(int i=0;i<3;i++)recovery.Select(2);
            Assert.AreEqual(.6f,recovery.TrapRefundFraction,.00001f);
        }
        [Test]public void PointerFocusImmediatelyOverridesNearestTargetAndReleasesOnDeath()
        {
            var near=At(Vector3.right,1);var chosen=At(Vector3.right*2,2);var focus=new GazeFocus();focus.Mark(chosen);
            Assert.AreSame(chosen,TowerCombat.Select(Vector3.zero,3,Vector3.zero,new[]{near,chosen},focus.Focused));
            chosen.Hp=0;focus.Step(null,.01f);Assert.IsNull(focus.Focused);
            Assert.AreSame(near,TowerCombat.Select(Vector3.zero,3,Vector3.zero,new[]{near,chosen},focus.Focused));
        }
        [Test]public void CampaignResetClearsOldTowersWithoutRefundAndReleasesCells()
        {
            var old=Build("MG",3);int before=wallet.Coins;int notifications=0;
            placement.Changed+=()=>notifications++;
            placement.ResetCampaign();
            Assert.IsTrue(old.Removed);Assert.AreEqual(0,placement.Towers.Count);
            Assert.AreEqual(before,wallet.Coins);Assert.AreEqual(1,notifications);
            Assert.IsTrue(placement.TryBuild("MG",cell,out var next,out _));
            Assert.Greater(next.Id,old.Id);Assert.AreEqual(before-50,wallet.Coins);
        }
        [TestCase("MG",50)][TestCase("Frost",40)][TestCase("Laser",80)][TestCase("Trap",30)]
        public void BuildChargesConfiguredCostOnceAndRejectsOccupiedCell(string id,int price)
        {Build(id);Assert.That(wallet.Coins,Is.EqualTo(1000-price));Assert.That(placement.TryBuild(id,cell,out _,out _),Is.False);Assert.That(wallet.Coins,Is.EqualTo(1000-price));}
        [Test]public void UnaffordableInvalidAndStaleCellsNeverCharge()
        {
            var poor=new Wallet(29);var p=new TowerPlacement(catalog,poor);p.SetGrid(Grid(cell));Assert.That(p.TryBuild("Trap",cell,out _,out _),Is.False);Assert.That(poor.Coins,Is.EqualTo(29));
            var fake=new GridCell("test-floor",SurfaceKind.Floor,cell.Index,cell.Center+Vector3.forward);
            Assert.That(placement.TryBuild("MG",fake,out _,out _),Is.False);
            Assert.That(placement.TryBuild("MG",new GridCell("unsafe",SurfaceKind.Floor,Vector2Int.zero,Vector3.zero),out _,out _),Is.False);
            Assert.That(placement.TryBuild("unknown",cell,out _,out _),Is.False);Assert.That(wallet.Coins,Is.EqualTo(1000));
        }
        [TestCase("MG",160,112)][TestCase("Frost",135,94)][TestCase("Laser",225,157)][TestCase("Trap",95,66)]
        public void LevelThreeSumsBothUpgradesSellFloorsAllInvestmentAndCannotSellTwice(string id,int cost,int refund)
        {
            var tower=Build(id);Assert.That(placement.TryUpgrade(tower,3,out _),Is.True);Assert.That(tower.Level,Is.EqualTo(3));Assert.That(tower.InvestedGold,Is.EqualTo(cost));Assert.That(wallet.Coins,Is.EqualTo(1000-cost));
            Assert.That(placement.TryUpgrade(tower,3,out _),Is.False);Assert.That(placement.TrySell(tower,out int actual),Is.True);Assert.That(actual,Is.EqualTo(refund));Assert.That(wallet.Coins,Is.EqualTo(1000-cost+refund));
            Assert.That(placement.TrySell(tower,out _),Is.False);Assert.That(placement.TryUpgrade(tower,2,out _),Is.False);
        }
        [Test]public void SequentialUpgradeDoesNotRechargeLv2()
        {var tower=Build("MG",2);Assert.That(wallet.Coins,Is.EqualTo(910));Assert.That(placement.UpgradePrice(tower,3),Is.EqualTo(70));placement.TryUpgrade(tower,3,out _);Assert.That(wallet.Coins,Is.EqualTo(840));}
        [Test]public void OccupiedCellResolvesPaidTowerForManagementWithoutPurchase()
        {
            var tower=Build("MG");int before=wallet.Coins;
            Assert.AreSame(tower,placement.FindActiveAtCell(cell));
            Assert.AreEqual(before,wallet.Coins);Assert.AreEqual(1,tower.Level);
            Assert.IsTrue(placement.TryUpgrade(tower,2,out _));Assert.AreEqual(before-40,wallet.Coins);
            placement.TrySell(tower,out _);Assert.IsNull(placement.FindActiveAtCell(cell));
        }
        [Test]public void FreezeStopsMovementThenRestoresIndependentFrostSlow()
        {
            var go=new GameObject("Freeze target");var target=go.AddComponent<TowerTestTarget>();
            var material=new Material(Shader.Find("Sprites/Default"));
            try
            {
                target.Initialize(Vector3.zero,Vector3.right*10,true,100,.5f,material);
                target.Slow(1,.55f,5);target.Slow(2,.55f,5);target.Slow(-100,1,2);
                target.Tick(1,null);Assert.AreEqual(Vector3.zero,target.Position);Assert.AreEqual(1,target.SlowFraction);
                target.Tick(1.1f,null);Assert.AreEqual(.65f,target.SlowFraction,.0001f);Assert.Greater(target.Position.x,0);
                for(int i=3;i<10;i++)target.Slow(i,.55f,3);
                Assert.AreEqual(.8f,target.SlowFraction,.0001f,"Ordinary stacking must retain the 80% cap");
            }
            finally{Object.DestroyImmediate(go);Object.DestroyImmediate(material);}
        }
        [Test]public void UpgradeInsufficientFundsLeavesStatsUnchanged()
        {var poor=new Wallet(80);var p=new TowerPlacement(catalog,poor);p.SetGrid(Grid(cell));p.TryBuild("MG",cell,out var tower,out _);Assert.That(p.TryUpgrade(tower,2,out _),Is.False);Assert.That(tower.Level,Is.EqualTo(1));Assert.That(poor.Coins,Is.EqualTo(30));}
        [Test]public void ScanSuspendsAndInvalidatedPurchaseRefundsOnce()
        {var tower=Build("MG",2);placement.SetGrid(null);Assert.That(placement.Suspended,Is.True);Assert.That(placement.TryUpgrade(tower,3,out _),Is.False);Assert.That(tower.Removed,Is.False);placement.SetGrid(Grid());Assert.That(tower.Removed,Is.True);Assert.That(wallet.Coins,Is.EqualTo(1000));placement.SetGrid(Grid());Assert.That(wallet.Coins,Is.EqualTo(1000));}
        [Test]public void EquivalentGridKeepsOccupiedCellAndPaidLevel()
        {var tower=Build("MG",2);placement.SetGrid(Grid(cell));Assert.That(tower.Removed,Is.False);Assert.That(tower.Level,Is.EqualTo(2));Assert.That(wallet.Coins,Is.EqualTo(910));}
        [Test]public void TargetSelectionUsesSafeZoneDistanceAndInRangeFocusOnly()
        {
            var a=At(new Vector3(1.6f,.16f,0),1);var b=At(new Vector3(2.8f,.16f,0),2);var outside=At(new Vector3(10,.16f,0),3);
            var all=new ITowerTarget[]{a,b,outside};var origin=cell.Center+Vector3.up*.16f;
            Assert.That(TowerCombat.Select(origin,2,Vector3.zero,all,null),Is.SameAs(a));Assert.That(TowerCombat.Select(origin,2,Vector3.zero,all,b),Is.SameAs(b));
            Assert.That(TowerCombat.Select(origin,2,Vector3.zero,all,outside),Is.SameAs(a));a.Hp=0;Assert.That(TowerCombat.Select(origin,2,Vector3.zero,all,null),Is.SameAs(b));
        }
        [Test]public void GazeNeedsContinuousHalfSecondAndClearsDeadFocus()
        {var focus=new GazeFocus();var a=At(Vector3.zero);var b=At(Vector3.one,2);focus.Step(a,.49f);Assert.That(focus.Focused,Is.Null);focus.Step(b,.1f);Assert.That(focus.Focused,Is.Null);focus.Step(null,.1f);focus.Step(a,.25f);Assert.That(focus.Focused,Is.Null);focus.Step(a,.25f);Assert.That(focus.Focused,Is.SameAs(a));a.Hp=0;focus.Step(null,.1f);Assert.That(focus.Focused,Is.Null);}
        [TestCase(30)][TestCase(60)][TestCase(90)]
        public void MgFireRateIsStableAcrossFrameRates(int fps)
        {var tower=Build("MG");var engine=new TowerCombat(tower,placement.Consume);var a=At(cell.Center+new Vector3(0,.16f,1));for(int i=0;i<fps*10;i++)engine.Step(1f/fps,Vector3.zero,new ITowerTarget[]{a},null);Assert.That(10000-a.Hp,Is.EqualTo(60).Within(5));}
        [Test]public void MgLv3UsesSmallSplashWithoutDoubleHittingPrimary()
        {var tower=Build("MG",3);var a=At(cell.Center+new Vector3(0,.16f,.8f));var b=At(a.Position+Vector3.right*.1f,2);var c=At(a.Position+Vector3.right*.5f,3);new TowerCombat(tower,placement.Consume).Step(.01f,Vector3.zero,new ITowerTarget[]{a,b,c},a);Assert.That(a.Hp,Is.EqualTo(9989));Assert.That(b.Hp,Is.EqualTo(9997));Assert.That(c.Hp,Is.EqualTo(10000));}
        [Test]public void FrostSlowsAllInRangeAndUsesUpgradeDps()
        {var tower=Build("Frost",3);var a=At(cell.Center+new Vector3(0,.16f,1.2f));var b=At(cell.Center+new Vector3(.5f,.16f,0),2);var c=At(cell.Center+Vector3.forward*2,3);new TowerCombat(tower,placement.Consume).Step(.5f,Vector3.zero,new ITowerTarget[]{a,b,c},null);Assert.That(a.SlowAmount,Is.EqualTo(.55f));Assert.That(b.SlowAmount,Is.EqualTo(.55f));Assert.That(a.Duration,Is.EqualTo(2));Assert.That(a.Hp,Is.EqualTo(9999.25f));Assert.That(c.SlowAmount,Is.Zero);}
        [TestCase(1,9,1)][TestCase(2,15,1)][TestCase(3,24,2)]
        public void LaserDamagesOnlyCollinearTargetsUpToPierceLimit(int level,float dps,int count)
        {var tower=Build("Laser",level);var a=At(cell.Center+new Vector3(0,.16f,.5f));var b=At(cell.Center+new Vector3(0,.16f,1),2);var c=At(cell.Center+new Vector3(.5f,.16f,.5f),3);new TowerCombat(tower,placement.Consume).Step(.25f,Vector3.zero,new ITowerTarget[]{a,b,c},a);Assert.That(a.Hp,Is.EqualTo(10000-dps*.25f));Assert.That(b.Hp,Is.EqualTo(count==2?10000-dps*.25f:10000));Assert.That(c.Hp,Is.EqualTo(10000));}
        [Test]public void TrapSweptCrossingTriggersOnceWithNoRefundAndAppliesSlow()
        {var tower=Build("Trap",3);int coins=wallet.Coins;var a=At(cell.Center+new Vector3(1,.16f,0));a.PreviousPosition=cell.Center+new Vector3(-1,.16f,0);var engine=new TowerCombat(tower,placement.Consume);engine.Step(.1f,Vector3.zero,new ITowerTarget[]{a},null);engine.Step(.1f,Vector3.zero,new ITowerTarget[]{a},null);Assert.That(a.Hp,Is.EqualTo(9400));Assert.That(a.SlowAmount,Is.EqualTo(.3f));Assert.That(tower.Consumed,Is.True);Assert.That(wallet.Coins,Is.EqualTo(coins));Assert.That(placement.TrySell(tower,out _),Is.False);}
        [TestCase(1,350)][TestCase(2,450)][TestCase(3,600)]
        public void TrapBurstsOneGroundTargetAndDoesNotKeepAttacking(int level,float damage)
        {
            var tower=Build("Trap",level);
            var first=At(cell.Center+Vector3.up*.16f);
            var next=At(first.Position+Vector3.right*.1f,2);
            var engine=new TowerCombat(tower,placement.Consume);
            var targets=new ITowerTarget[]{first,next};
            engine.Step(.1f,Vector3.zero,targets,null);
            engine.Step(1,Vector3.zero,targets,null);
            Assert.AreEqual(10000-damage,first.Hp);
            Assert.AreEqual(10000,next.Hp,"A trap is a single-target mine, not a persistent area weapon");
            Assert.IsTrue(tower.Consumed);
            Assert.AreEqual(level==3?.3f:0,first.SlowAmount);
        }
        [Test]public void TrapExplosionOutlivesConsumedTowerWithoutExtraDamageOrGold()
        {
            var visualRoot=new GameObject("Trap visual integration");
            var visualMaterial=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            try
            {
                var pool=visualRoot.AddComponent<TrapExplosionVisuals>();pool.Build(visualMaterial);
                var tower=Build("Trap");int coins=wallet.Coins;
                var viewObject=new GameObject("Consumed trap");viewObject.transform.SetParent(visualRoot.transform,false);
                var view=viewObject.AddComponent<TowerView>();view.Build(tower,visualMaterial);
                var enemy=At(cell.Center+Vector3.up*.16f);
                var combat=new TowerCombat(tower,placement.Consume);combat.Fired+=view.Fire;
                placement.Changed+=()=>{if(tower.Consumed&&viewObject!=null)Object.DestroyImmediate(viewObject);};
                combat.Step(.1f,Vector3.zero,new[]{enemy},null);
                Assert.IsTrue(tower.Consumed);Assert.IsTrue(view==null);
                Assert.AreEqual(1,pool.VisibleCount,"Explosion survives the consumed tower's removal");
                pool.Tick(.2f,false);
                Assert.AreEqual(1,pool.VisibleCount);
                Assert.AreEqual(9650,enemy.Hp,"Visual blast cannot add area damage");
                Assert.AreEqual(coins,wallet.Coins);
                pool.Tick(TrapExplosionVisuals.Duration,false);Assert.AreEqual(0,pool.VisibleCount);
            }
            finally{Object.DestroyImmediate(visualRoot);Object.DestroyImmediate(visualMaterial);}
        }
        [Test]public void TrapDoesNotTriggerOnDifferentSurfaceHeight()
        {var tower=Build("Trap");var a=At(cell.Center+Vector3.up);new TowerCombat(tower,placement.Consume).Step(.1f,Vector3.zero,new ITowerTarget[]{a},null);Assert.That(tower.Consumed,Is.False);}
        [Test]public void ZeroNanAndRemovedTowerDoNoDamage()
        {var tower=Build("MG");var a=At(cell.Center+new Vector3(0,.16f,1));var engine=new TowerCombat(tower,placement.Consume);engine.Step(0,Vector3.zero,new ITowerTarget[]{a},null);engine.Step(float.NaN,Vector3.zero,new ITowerTarget[]{a},null);placement.TrySell(tower,out _);engine.Step(1,Vector3.zero,new ITowerTarget[]{a},null);Assert.That(a.Hp,Is.EqualTo(10000));}
        [Test]public void SlowSourcesExpireIndependentlyAndMovementRecovers()
        {
            var go=new GameObject("Test slow");var target=go.AddComponent<TowerTestTarget>();
            var material=new Material(Shader.Find("Sprites/Default"));
            try{target.Initialize(Vector3.zero,Vector3.right*10,true,100,.5f,material);target.Slow(1,.55f,1);target.Slow(2,.3f,2);Assert.That(target.SlowFraction,Is.EqualTo(.65f).Within(.0001f));target.Tick(.5f,null);Assert.That(target.SlowFraction,Is.EqualTo(.65f).Within(.0001f));target.Tick(.6f,null);Assert.That(target.SlowFraction,Is.EqualTo(.3f).Within(.0001f));target.Tick(1,null);Assert.That(target.SlowFraction,Is.Zero);}
            finally{Object.DestroyImmediate(go);Object.DestroyImmediate(material);}
        }
        [Test]public void SharedGameManagerEconomyInitializesOnlyOnceAndRejectsInvalidTransactions()
        {
            var go=new GameObject("Wallet integration test");var manager=go.AddComponent<GameManager>();
            try
            {
                manager.InitializeEconomy(150);manager.InitializeEconomy(180);Assert.That(manager.Coins,Is.EqualTo(150));
                var shared=new GameTowerWallet(manager);Assert.That(shared.TrySpend(50),Is.True);Assert.That(manager.Coins,Is.EqualTo(100));
                Assert.That(shared.TrySpend(-1),Is.False);Assert.That(shared.TrySpend(101),Is.False);Assert.That(manager.Coins,Is.EqualTo(100));
                shared.Credit(35);Assert.That(shared.Coins,Is.EqualTo(135));Assert.Throws<System.ArgumentOutOfRangeException>(()=>shared.Credit(-1));
            }
            finally{Object.DestroyImmediate(go);}
        }
    }

    public sealed class TowerStrategyHintTests
    {
        [Test]
        public void StrategyHintsExplainUsefulTowerPairs()
        {
            StringAssert.Contains("Frost",TowerStrategyHints.For("MG"));
            StringAssert.Contains("Laser",TowerStrategyHints.For("Frost"));
            StringAssert.Contains("Shielded",TowerStrategyHints.For("Laser"));
            StringAssert.Contains("Runner",TowerStrategyHints.For("Trap"));
        }
    }
}
