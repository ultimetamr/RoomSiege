using System;
using System.IO;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Progression;
using RoomSiege.Scan;
using RoomSiege.Towers;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class Arsenal12Tests
    {
        private sealed class Memory:IProgressionStorage
        {public string[] slots={"",""};public string Read(int i)=>slots[i];public void Write(int i,string text)=>slots[i]=text;}
        private sealed class Target:ITowerTarget,ITowerHitReceiver,ITowerTargetReadiness,ICombatStatusTarget,IArsenalStatusTarget
        {
            public int TargetId{get;set;}public Vector3 Position{get;set;}public Vector3 PreviousPosition=>Position;public float HitRadius=>.1f;
            public float Hp=10000,Shield=200,Armor=100,Break,Disruption,Burn;public int Hits;public TowerHit Last;
            public bool IsAlive=>Hp>0;public bool CanBeTargeted{get;set;}=true;public bool IsBoss{get;set;}
            public readonly SlowEffectStack Slows=new SlowEffectStack();public readonly FrostControl Frost=new FrostControl();
            public void Damage(float a){Hp-=a;}public void Slow(int id,float f,float seconds)=>Slows.Add(id,f,seconds);
            public float ShieldPoints=>Shield;public float ControlFraction=>Slows.Fraction;
            public bool AddChill(int n)=>Frost.Chill(n,IsBoss);public bool TryFreeze(float seconds)=>Frost.Freeze(seconds,IsBoss);
            public void ApplyBurn(float dps,float seconds,int source){Burn=Mathf.Max(Burn,dps);}
            public void ApplyArmorBreak(float f,float seconds){Break=Mathf.Max(Break,f);}
            public void Disrupt(float fraction,float seconds){Shield*=1-fraction;Disruption=seconds;}
            public void ReceiveHit(TowerHit hit){Hits++;Last=hit;Hp-=DifficultyCalculator.DamageAfterArmor(hit.Amount,Armor*(1-hit.IgnoreArmorFraction));}
        }
        private static BalanceCatalog Balance()=>BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));
        private static TowerInstance Tower(string id,int level=1,Vector3 point=default)
        {var tower=TowerInstance.CreatePreview(Balance().GetTower(id),new GridCell("test",SurfaceKind.Floor,Vector2Int.zero,point));typeof(TowerInstance).GetProperty("Level").SetValue(tower,level);return tower;}
        private static Target Enemy(float x=0,float z=1,int id=1)=>new Target{Position=new Vector3(x,.17f,z),TargetId=id};
        private static void Step(TowerCombat combat,float dt,params Target[] targets)=>combat.Step(dt,Vector3.zero,targets,null);
        [Test]
        public void EighteenWeaponsShareCatalogAndRetainOriginalBalance()
        {
            var balance=Balance();var catalog=ProgressionCatalog.Load();CollectionAssert.AreEquivalent(WeaponRoster.Ids,balance.towers.Select(x=>x.id));CollectionAssert.AreEquivalent(WeaponRoster.Ids,catalog.weapons);
            Assert.AreEqual(14,catalog.weaponUnlocks.Length);Assert.AreEqual(12,balance.towers.Count(x=>x.damageMode=="arsenal"));Assert.AreEqual(6,balance.towers.Count(x=>WeaponRoster.IsNew(x.id)&&WeaponRoster.IsSupport(x.id)));
            Assert.AreEqual(50,balance.GetTower("MG").buildCost);Assert.AreEqual(5,balance.GetTower("MG").levels[0].damagePerHit);Assert.AreEqual(12,catalog.supplies.Length);
        }
        [TestCase("Beacon")][TestCase("Prism")][TestCase("Cryo")][TestCase("Sunder")][TestCase("Disruptor")][TestCase("Gravity")]
        [TestCase("Sniper")][TestCase("Scatter")][TestCase("Flame")][TestCase("Missile")][TestCase("Railgun")][TestCase("Blade")]
        public void UnlockPurchaseAndFourSlotLoadoutSurviveReload(string id)
        {
            var catalog=ProgressionCatalog.Load();var memory=new Memory();var store=new ProgressionStore(catalog,memory);
            Assert.IsFalse(store.OwnsWeapon(id));Assert.IsFalse(store.Buy(id,out _));store.CompleteChallenge("test",10000);
            Assert.IsTrue(store.Buy(id,out _));int after=10000-catalog.Weapon(id).price;Assert.AreEqual(after,store.Fragments);Assert.IsFalse(store.Buy(id,out _));
            Assert.IsTrue(store.SetLoadout(new[]{id,"MG","Frost","Trap"},out _));var loaded=new ProgressionStore(catalog,memory);Assert.IsTrue(loaded.OwnsWeapon(id));Assert.AreEqual(after,loaded.Fragments);Assert.AreEqual(id,loaded.Loadout[0]);
        }
        [Test]
        public void FourSupportWeaponsCannotCreateAnUnwinnableLoadout()
        {
            var store=new ProgressionStore(ProgressionCatalog.Load(),new Memory());store.CompleteChallenge("test",10000);foreach(string id in new[]{"Beacon","Prism","Cryo"})store.Buy(id,out _);
            Assert.IsFalse(store.SetLoadout(new[]{"Frost","Beacon","Prism","Cryo"},out var reason));StringAssert.Contains("作战",reason);CollectionAssert.AreEqual(new[]{"MG","Frost","Laser","Trap"},store.Loadout);
        }
        [Test]
        public void AurasUseStrongestSourceAndDisappearWhenSoldWithoutRecursiveRangeGrowth()
        {
            var mg=Tower("MG");var laser=Tower("Laser",1,Vector3.right*.4f);var beacon=Tower("Beacon",1);var stronger=Tower("Beacon",3);var prism=Tower("Prism",3);var frost=Tower("Frost");var trap=Tower("Trap");
            var all=new[]{mg,laser,beacon,stronger,prism,frost,trap};TowerAuraSystem.Resolve(all,true);Assert.AreEqual(1.24f,mg.RateMultiplier,.001f);Assert.AreEqual(2.4f,mg.Range,.001f);Assert.AreEqual(0,beacon.AuraRangeBonus);Assert.AreEqual(0,frost.AuraRateBonus);Assert.AreEqual(0,trap.AuraRateBonus);
            TowerAuraSystem.Resolve(all,true);Assert.AreEqual(2.4f,mg.Range,.001f);
            typeof(TowerInstance).GetProperty("Removed").SetValue(stronger,true);TowerAuraSystem.Resolve(all,true);Assert.AreEqual(1.12f,mg.RateMultiplier,.001f);
            TowerAuraSystem.Resolve(all,false);Assert.AreEqual(1,mg.RateMultiplier);Assert.AreEqual(2,mg.Range);
        }
        [Test]
        public void BeaconActuallyIncreasesGunCadenceAndLaserContinuousDamage()
        {
            var a=Tower("MG");var b=Tower("MG");b.AuraRateBonus=.24f;var normal=Enemy();var buffed=Enemy();var ca=new TowerCombat(a,_=>{});var cb=new TowerCombat(b,_=>{});
            for(int i=0;i<400;i++){Step(ca,.01f,normal);Step(cb,.01f,buffed);}Assert.Greater(buffed.Hits,normal.Hits);
            var laser=Tower("Laser");laser.AuraRateBonus=.24f;var victim=Enemy();Step(new TowerCombat(laser,_=>{}),.1f,victim);Assert.AreEqual(laser.Stats.damagePerSecond*.1f*1.24f,victim.Last.Amount,.001f);
        }
        [Test]
        public void SniperPenetratesHalfArmorAndHonorsCadence()
        {
            var c=new TowerCombat(Tower("Sniper"),_=>{});var target=Enemy();Step(c,.01f,target);Assert.AreEqual(.5f,target.Last.IgnoreArmorFraction);Assert.AreEqual(DifficultyCalculator.DamageAfterArmor(22,50),10000-target.Hp,.001f);Step(c,.2f,target);Assert.AreEqual(1,target.Hits);
        }
        [Test]
        public void ScatterHitsOnlyForwardConeWithDistanceFalloff()
        {
            var c=new TowerCombat(Tower("Scatter"),_=>{});var near=Enemy(0,.5f);var far=Enemy(0,1.2f,2);var side=Enemy(1,0,3);Step(c,.01f,near,far,side);Assert.AreEqual(1,near.Hits);Assert.AreEqual(1,far.Hits);Assert.AreEqual(0,side.Hits);Assert.Greater(10000-near.Hp,10000-far.Hp);
        }
        [Test]
        public void FlameDamagesForwardTargetsAndLeavesBurnWithoutHittingBehind()
        {
            var c=new TowerCombat(Tower("Flame"),_=>{});var front=Enemy();var behind=Enemy(0,-1,2);Step(c,.1f,front,behind);Assert.Greater(front.Burn,0);Assert.Greater(front.Hits,0);Assert.AreEqual(0,behind.Burn);Assert.AreEqual(0,behind.Hits);
        }
        [Test]
        public void MissileFollowsMovingTargetAndDamagesOnlyAtDelayedImpact()
        {
            var c=new TowerCombat(Tower("Missile"),_=>{});var moving=Enemy();var old=Enemy(.1f,1,2);Step(c,.01f,moving,old);Assert.AreEqual(0,moving.Hits);moving.Position+=Vector3.right;Step(c,.64f,moving,old);Assert.AreEqual(0,moving.Hits);Step(c,.02f,moving,old);Assert.AreEqual(1,moving.Hits);Assert.AreEqual(0,old.Hits);
        }
        [Test]
        public void RailgunPiercesConfiguredCountWithDecayAndRejectsOffAxis()
        {
            var c=new TowerCombat(Tower("Railgun"),_=>{});var a=Enemy(0,1);var b=Enemy(0,1.4f,2);var d=Enemy(0,1.8f,3);var e=Enemy(0,2.2f,4);var side=Enemy(.5f,1.5f,5);Step(c,.01f,a,b,d,e,side);
            Assert.AreEqual(1,a.Hits);Assert.AreEqual(1,b.Hits);Assert.AreEqual(1,d.Hits);Assert.AreEqual(0,e.Hits);Assert.AreEqual(0,side.Hits);Assert.AreEqual(18*.85f,b.Last.Amount,.001f);
        }
        [Test]
        public void BladeCoversEveryDirectionInsideShortRadius()
        {
            var c=new TowerCombat(Tower("Blade"),_=>{});var a=Enemy(0,.7f);var b=Enemy(0,-.7f,2);var d=Enemy(.7f,0,3);var outside=Enemy(0,1.1f,4);Step(c,.01f,a,b,d,outside);Assert.AreEqual(1,a.Hits);Assert.AreEqual(1,b.Hits);Assert.AreEqual(1,d.Hits);Assert.AreEqual(0,outside.Hits);
        }
        [Test]
        public void CryoUsesSharedFreezeProtectionAndBossShortening()
        {
            var c=new TowerCombat(Tower("Cryo",3),_=>{});var a=Enemy();var boss=Enemy(.1f,1,2);boss.IsBoss=true;for(int i=0;i<3;i++)Step(c,1,a,boss);Assert.IsTrue(a.Frost.Frozen);Assert.IsTrue(boss.Frost.Frozen);Assert.Less(boss.Frost.FrozenRemaining,a.Frost.FrozenRemaining);
            a.Frost.Tick(1.01f);Step(c,1,a);Assert.IsFalse(a.Frost.Frozen);Assert.Greater(a.Frost.ProtectionRemaining,0);
        }
        [Test]
        public void SunderAndDisruptorApplyDifferentDebuffsWithBossLimits()
        {
            var a=Enemy();var boss=Enemy(.1f,1,2);boss.IsBoss=true;Step(new TowerCombat(Tower("Sunder"),_=>{}),.01f,a,boss);Assert.AreEqual(.2f,a.Break);Assert.AreEqual(.1f,boss.Break);Assert.AreEqual(0,a.Hits);
            Step(new TowerCombat(Tower("Disruptor"),_=>{}),.01f,a,boss);Assert.AreEqual(170,a.Shield,.001f);Assert.AreEqual(1.2f,a.Disruption);Assert.AreEqual(.4f,boss.Disruption);
        }
        [Test]
        public void GravitySharesSourceAndCannotStackItselfOrStopBoss()
        {
            var a=Enemy();var boss=Enemy(.1f,1,2);boss.IsBoss=true;Step(new TowerCombat(Tower("Gravity"),_=>{}),.1f,a,boss);Step(new TowerCombat(Tower("Gravity"),_=>{}),.1f,a,boss);Assert.AreEqual(.35f,a.Slows.Fraction,.001f);Assert.AreEqual(1,a.Slows.Count);Assert.AreEqual(.175f,boss.Slows.Fraction,.001f);Assert.AreEqual(0,a.Hits);
        }
        [Test]
        public void MixedGravityLevelsKeepStrongestInEitherOrderAndLoseItAfterSale()
        {
            var weak=Tower("Gravity");var strong=Tower("Gravity",3);var allies=new[]{weak,strong};var target=Enemy();var first=new TowerCombat(strong,_=>{},()=>allies);var last=new TowerCombat(weak,_=>{},()=>allies);
            Step(first,.1f,target);Step(last,.1f,target);Assert.AreEqual(.55f,target.Slows.Fraction,.001f);Assert.AreEqual(1,target.Slows.Count);
            typeof(TowerInstance).GetProperty("Removed").SetValue(strong,true);Step(last,.1f,target);Assert.AreEqual(.35f,target.Slows.Fraction,.001f);
        }
        [TestCase("Beacon")][TestCase("Prism")][TestCase("Cryo")][TestCase("Sunder")][TestCase("Disruptor")][TestCase("Gravity")]
        [TestCase("Sniper")][TestCase("Scatter")][TestCase("Flame")][TestCase("Missile")][TestCase("Railgun")][TestCase("Blade")]
        public void EntranceProtectionAndInvalidDeltaPreventEveryNewEffect(string id)
        {var target=Enemy();target.CanBeTargeted=false;var c=new TowerCombat(Tower(id),_=>{});Step(c,1,target);Step(c,float.NaN,target);Step(c,-1,target);Assert.AreEqual(10000,target.Hp);Assert.AreEqual(200,target.Shield);Assert.AreEqual(0,target.Break);Assert.AreEqual(0,target.Slows.Count);Assert.AreEqual(0,target.Frost.Stacks);Assert.AreEqual(0,target.Burn);}
        [TestCase("Beacon")][TestCase("Prism")][TestCase("Cryo")][TestCase("Sunder")][TestCase("Disruptor")][TestCase("Gravity")]
        [TestCase("Sniper")][TestCase("Scatter")][TestCase("Flame")][TestCase("Missile")][TestCase("Railgun")][TestCase("Blade")]
        public void EachWeaponHasThreeModelLevelsAndActualPortrait(string id)
        {
            var go=new GameObject(id);var material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            try{var view=go.AddComponent<TowerView>();view.Build(Tower(id),material);var model=go.GetComponentInChildren<HoloTowerModel>();Assert.IsNotNull(model);model.SetAppearance(3,WeaponRoster.Tint(id));Assert.AreEqual(3,model.DisplayLevel);Assert.IsNotNull(Resources.Load<Texture2D>("Codex/Towers/CodexTower_"+id));Assert.AreEqual(1,go.GetComponentsInChildren<Collider>().Length);}
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
        }
    }
}
