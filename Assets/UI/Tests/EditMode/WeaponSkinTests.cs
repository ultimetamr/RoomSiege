using System;
using System.Collections.Generic;
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
    public sealed class WeaponSkinTests
    {
        private sealed class Memory:IProgressionStorage
        {public string[] slots={"",""};public string Read(int i)=>slots[i];public void Write(int i,string value)=>slots[i]=value;}
        private sealed class Target:ITowerTarget,ITowerHitReceiver,ITowerTargetReadiness,ICombatStatusTarget,IArsenalStatusTarget
        {
            public int TargetId{get;set;}public Vector3 Position{get;set;}public Vector3 PreviousPosition=>Position;public float HitRadius=>.1f;
            public float Hp=10000,Shield,Burn,BurnSeconds,Break,BreakSeconds,Disruption,Control;public int Hits,Chills;public TowerHit Last;
            public bool IsAlive=>Hp>0;public bool CanBeTargeted{get;set;}=true;public bool IsBoss{get;set;}
            public readonly SlowEffectStack Slows=new SlowEffectStack();public readonly FrostControl Frost=new FrostControl();
            public void Damage(float amount)=>Hp-=amount;public void ReceiveHit(TowerHit hit){Hits++;Last=hit;Hp-=hit.Amount;}
            public void Slow(int id,float f,float seconds)=>Slows.Add(id,f,seconds);
            public float ShieldPoints=>Shield;public float ControlFraction=>Mathf.Max(Control,Slows.Fraction);
            public bool AddChill(int n){Chills+=n;return Frost.Chill(n,IsBoss);}public bool TryFreeze(float seconds)=>Frost.Freeze(seconds,IsBoss);
            public void ApplyBurn(float dps,float seconds,int source){Burn=dps;BurnSeconds=seconds;}
            public void ApplyArmorBreak(float f,float seconds){Break=Mathf.Max(Break,f);BreakSeconds=seconds;}
            public void Disrupt(float fraction,float seconds){Shield*=1-fraction;Disruption=seconds;}
        }
        private ProgressionCatalog catalog;private BalanceCatalog balance;
        [SetUp]public void Setup(){catalog=ProgressionCatalog.Load();balance=BalanceJson.Parse(File.ReadAllText("Assets/Data/Config/roomsiege.balance.json"));}
        private TowerInstance Tower(string id,string tier=null,Vector3 point=default)
        {
            var tower=TowerInstance.CreatePreview(balance.GetTower(id),new GridCell("skin",SurfaceKind.Floor,Vector2Int.zero,point));
            if(tier!=null)tower.Skin=catalog.Skin(id.ToLowerInvariant()+"-"+tier);return tower;
        }
        private static Target At(float z=.7f,float x=0,int id=1)=>new Target{TargetId=id,Position=new Vector3(x,.16f,z)};
        private static TowerCombat Combat(TowerInstance tower,params TowerInstance[] allies)=>new TowerCombat(tower,_=>{},()=>allies);
        private static void Step(TowerCombat combat,float dt,params Target[] targets)=>combat.Step(dt,Vector3.zero,targets,null);
        private static IEnumerable<string> NewSkins()=>ProgressionCatalog.Load().skins.Where(s=>!WeaponRoster.IsStarter(s.weaponId)).Select(s=>s.id);
        private static IEnumerable<string> AllSkins()=>ProgressionCatalog.Load().skins.Select(s=>s.id);

        [TestCaseSource(nameof(NewSkins))]
        public void EachNewSkinRequiresItsWeaponAndPersistsPurchaseEquipmentAndRevert(string id)
        {
            var skin=catalog.Skin(id);var memory=new Memory();var store=new ProgressionStore(catalog,memory);store.CompleteChallenge("funding",20000);
            Assert.IsFalse(store.Buy(id,out _));Assert.AreEqual(20000,store.Fragments);
            Assert.IsTrue(store.Buy(skin.weaponId,out _));int before=store.Fragments;
            Assert.IsTrue(store.Buy(id,out _));Assert.AreEqual(before-skin.price,store.Fragments);Assert.IsFalse(store.Buy(id,out _));
            Assert.IsTrue(store.Equip(skin.weaponId,id,out _));Assert.IsFalse(store.Equip("MG",id,out _));
            var reload=new ProgressionStore(catalog,memory);Assert.AreEqual(id,reload.Equipped(skin.weaponId));Assert.AreEqual(before-skin.price,reload.Fragments);
            Assert.IsTrue(reload.Equip(skin.weaponId,"",out _));Assert.IsTrue(new ProgressionStore(catalog,memory).Owns(id));Assert.AreEqual("",new ProgressionStore(catalog,memory).Equipped(skin.weaponId));
        }
        [TestCaseSource(nameof(AllSkins))]
        public void EverySkinHasActualPortraitRarityAttachmentsAndOneCollider(string id)
        {
            var skin=catalog.Skin(id);var tower=Tower(skin.weaponId);tower.Skin=skin;var go=new GameObject(id);var material=new Material(Resources.Load<Shader>("RoomSiegeHologram"));
            try
            {
                go.AddComponent<TowerView>().Build(tower,material);Assert.AreEqual(1,go.GetComponentsInChildren<Collider>().Length);
                Assert.IsNotNull(go.transform.Find("Skin signature "+skin.rarity));Assert.IsNotNull(go.transform.Find("Skin circuit "+skin.rarity));
                Assert.IsTrue(ColorUtility.TryParseHtmlString(skin.color,out var color));Assert.AreEqual(color,TowerVisuals.ColorFor(tower));
                var portrait=Resources.Load<Texture2D>("Codex/Skins/Skin_"+id);Assert.IsNotNull(portrait,id);Assert.AreEqual(512,portrait.width);Assert.AreEqual(512,portrait.height);
                if(skin.weaponId=="Tesla"||skin.weaponId=="Mortar")Assert.AreEqual(color,go.transform.Find("Arc strand 0").GetComponent<LineRenderer>().startColor);
            }
            finally{UnityEngine.Object.DestroyImmediate(go);UnityEngine.Object.DestroyImmediate(material);}
        }
        [TestCaseSource(nameof(NewSkins))]
        public void NewSkinEffectsRespectSpawnProtectionAndInvalidDelta(string id)
        {
            var skin=catalog.Skin(id);var tower=Tower(skin.weaponId);tower.Skin=skin;var target=At();target.CanBeTargeted=false;target.Shield=100;
            var engine=Combat(tower,Tower("Frost"));for(int i=0;i<100;i++)Step(engine,.1f,target);Step(engine,float.NaN,target);Step(engine,-1,target);
            Assert.AreEqual(10000,target.Hp);Assert.AreEqual(100,target.Shield);Assert.AreEqual(0,target.Chills);Assert.AreEqual(0,target.Burn);Assert.AreEqual(0,target.Break);Assert.AreEqual(0,target.Slows.Count);
        }
        [TestCase("Tesla")][TestCase("Cryo")][TestCase("Sunder")][TestCase("Disruptor")][TestCase("Scatter")][TestCase("Missile")][TestCase("Blade")]
        public void CadenceSkinsIncreaseAttackOrPulseFrequency(string id)
        {
            var basic=Combat(Tower(id));var skin=Combat(Tower(id,"elite"));var a=At();var b=At();a.Shield=b.Shield=100000;
            int basicPulses=0,skinPulses=0;basic.Fired+=(x,y,c)=>basicPulses++;skin.Fired+=(x,y,c)=>skinPulses++;
            basic.Launched+=(x,y,c,d)=>basicPulses++;skin.Launched+=(x,y,c,d)=>skinPulses++;
            for(int i=0;i<2100;i++){Step(basic,.01f,a);Step(skin,.01f,b);}
            Assert.Greater(skinPulses,basicPulses,id);
        }
        [TestCase("Sniper",1.1f)][TestCase("Flame",1.08f)][TestCase("Railgun",1.1f)]
        public void DamageSkinsScaleDamageWithoutMutatingBaseData(string id,float multiplier)
        {
            var a=At();var b=At();Step(Combat(Tower(id)),.1f,a);Step(Combat(Tower(id,"elite")),.1f,b);
            Assert.AreEqual(a.Last.Amount*multiplier,b.Last.Amount,.0001f);if(id=="Flame")Assert.AreEqual(a.Burn*multiplier,b.Burn,.0001f);
        }
        [TestCase("Beacon",1.12f)][TestCase("Prism",1.12f)][TestCase("Gravity",1.12f)][TestCase("Blade",1.18f)]
        public void ReachSkinsAffectActualCoverage(string id,float multiplier)
        {
            string tier=id=="Blade"?"epic":"elite";var source=Tower(id,tier);Assert.AreEqual(source.Definition.rangeMeters.value*multiplier,source.Range,.0001f);
            if(id=="Beacon"||id=="Prism")
            {
                var target=Tower("MG",null,new Vector3(source.Definition.rangeMeters.value*1.06f,0,0));
                TowerAuraSystem.Resolve(new[]{source,target},true);Assert.Greater(id=="Beacon"?target.AuraRateBonus:target.AuraRangeBonus,0);
            }
            else {var target=At(source.Definition.rangeMeters.value*1.06f);Step(Combat(source),.1f,target);Assert.Greater(id=="Gravity"?target.Slows.Fraction:target.Hits,0);}
        }
        [Test]public void TeslaRelayAddsOneDecayedHopAndWinterNeedsOneNearbyColdAlly()
        {
            var targets=new[]{At(.5f,0),At(.7f,0,2),At(.9f,0,3),At(1.1f,0,4),At(1.3f,0,5)};
            Step(Combat(Tower("Tesla","epic")),.01f,targets);Assert.AreEqual(4,targets.Count(t=>t.Hits==1));Assert.AreEqual(0,targets[4].Hits);
            Assert.AreEqual(balance.GetTower("Tesla").levels[0].damagePerHit*Mathf.Pow(.7f,3),targets[3].Last.Amount,.0001);
            var legendary=Tower("Tesla","legend");var first=At();var engine=Combat(legendary);Step(engine,.01f,first);Assert.AreEqual(0,first.Chills);
            first=At();Step(Combat(legendary,Tower("Frost"),Tower("Cryo")),.01f,first);Assert.AreEqual(1,first.Chills);Assert.IsTrue(legendary.FrostEnchanted);
        }
        [TestCase("elite",1.12f)][TestCase("legend",1.1f)]
        public void MortarBlastCoverageChangesAtDelayedImpact(string tier,float multiplier)
        {
            var tower=Tower("Mortar",tier);var target=At(1.3f);var edge=At(1.3f,tower.Stats.splashRadiusMeters.value*1.05f,2);var engine=Combat(tower);
            float radius=0;engine.Launched+=(a,b,c,d)=>radius=d;Step(engine,.01f,target,edge);Assert.AreEqual(tower.Stats.splashRadiusMeters.value*multiplier,radius,.001f);Assert.AreEqual(0,target.Hits);
            Step(engine,tower.ImpactDelay+.01f,target,edge);Assert.AreEqual(1,target.Hits);Assert.AreEqual(1,edge.Hits);if(tier=="legend"){Assert.AreEqual(.2f,edge.Break);Assert.AreEqual(2,edge.BreakSeconds);}
        }
        [Test]public void MortarEmberAndSiegeRespectBurnAndBossLimits()
        {
            var tower=Tower("Mortar","epic");var target=At(1.3f);var engine=Combat(tower);Step(engine,.01f,target);Step(engine,tower.ImpactDelay+.01f,target);
            Assert.AreEqual(tower.Stats.damagePerHit*.08f,target.Burn,.001);Assert.AreEqual(3,target.BurnSeconds);
            tower=Tower("Mortar","legend");target=At(1.3f);target.IsBoss=true;engine=Combat(tower);Step(engine,.01f,target);Step(engine,tower.ImpactDelay+.01f,target);Assert.AreEqual(.1f,target.Break);
        }
        [Test]public void SupportSkinsHaveRealNonRecursiveBonusesAndCannotStackDuplicateSources()
        {
            var target=Tower("MG");var source=Tower("Beacon","epic");TowerAuraSystem.Resolve(new[]{source,target},true);Assert.AreEqual(.16f,target.AuraRateBonus,.001);
            var legend=Tower("Beacon","legend");TowerAuraSystem.Resolve(new[]{legend,target},true);Assert.AreEqual(.12f,target.AuraRateBonus,.001);
            var frost=Tower("Frost");var prism=Tower("Prism","epic");TowerAuraSystem.Resolve(new[]{legend,source,Tower("Beacon","legend"),frost,prism,target},true);
            Assert.AreEqual(.18f,target.AuraRateBonus,.001);Assert.AreEqual(.14f,target.AuraRangeBonus,.001);Assert.AreEqual(0,source.AuraRangeBonus);
            var tempo=Tower("Prism","legend");TowerAuraSystem.Resolve(new[]{tempo,target},true);Assert.AreEqual(.06f,target.AuraRateBonus,.001);
            TowerAuraSystem.Resolve(new[]{tempo,source,target},true);Assert.AreEqual(.16f,target.AuraRateBonus,.001);
            TowerAuraSystem.Resolve(new[]{tempo,source,target},false);Assert.AreEqual(0,target.AuraRateBonus);Assert.AreEqual(0,target.AuraRangeBonus);
        }
        [Test]public void CryoEchoOnlyAddsOneChillEveryThirdEffectivePulseAndLegendSlowsBossLess()
        {
            var tower=Tower("Cryo","epic");var target=At();var engine=Combat(tower);Step(engine,.01f,target);Assert.AreEqual(1,target.Chills);Step(engine,1.51f,target);Assert.AreEqual(2,target.Chills);Step(engine,1.51f,target);Assert.AreEqual(4,target.Chills);
            var boss=At(.8f,0,2);boss.IsBoss=true;target=At();Step(Combat(Tower("Cryo","legend")),.01f,target,boss);Assert.AreEqual(.2f,target.Slows.Fraction);Assert.AreEqual(.1f,boss.Slows.Fraction);
        }
        [Test]public void SunderLingersAndLegendaryBossBreakIsHalved()
        {
            var target=At();Step(Combat(Tower("Sunder","epic")),.01f,target);Assert.AreEqual(balance.GetTower("Sunder").levels[0].effectDuration+1,target.BreakSeconds);
            target=At();var boss=At(.8f,0,2);boss.IsBoss=true;Step(Combat(Tower("Sunder","legend")),.01f,target,boss);Assert.AreEqual(.3f,target.Break,.001);Assert.AreEqual(.15f,boss.Break,.001);
        }
        [Test]public void DisruptorShredsShieldAndLegendExtendsDisruptionWithoutBossLock()
        {
            var target=At();target.Shield=100;Step(Combat(Tower("Disruptor","epic")),.01f,target);Assert.AreEqual(75,target.Shield,.001);Assert.AreEqual(0,target.Hits);
            target=At();var boss=At(.8f,0,2);boss.IsBoss=true;Step(Combat(Tower("Disruptor","legend")),.01f,target,boss);Assert.AreEqual(1.8f,target.Disruption,.001);Assert.AreEqual(.6f,boss.Disruption,.001);Assert.AreEqual(.2f,target.Slows.Fraction);Assert.AreEqual(.1f,boss.Slows.Fraction);
        }
        [Test]public void GravitySkinsResolveStrongestSourceInEitherOrderAndLoseColdSynergyAfterSale()
        {
            var epic=Tower("Gravity","epic");var legend=Tower("Gravity","legend");var cold=Tower("Cryo");var all=new[]{epic,legend,cold};var target=At();var boss=At(.8f,0,2);boss.IsBoss=true;
            var a=Combat(epic,all);var b=Combat(legend,all);Step(b,.1f,target,boss);Step(a,.1f,target,boss);Assert.AreEqual(.45f,target.Slows.Fraction,.001);Assert.AreEqual(.225f,boss.Slows.Fraction,.001);Assert.AreEqual(1,target.Slows.Count);
            Step(b,2.91f,target,boss);Assert.AreEqual(1,target.Chills);Assert.AreEqual(1,boss.Chills);Step(b,.01f,target,boss);Assert.AreEqual(1,target.Chills);
            typeof(TowerInstance).GetProperty("Removed").SetValue(cold,true);Step(a,.1f,target);Assert.AreEqual(.43f,target.Slows.Fraction,.001);
            typeof(TowerInstance).GetProperty("Removed").SetValue(epic,true);Step(b,.1f,target);Assert.AreEqual(.35f,target.Slows.Fraction,.001);
        }
        [Test]public void SniperPiercesArmorAndLegendaryBonusRequiresExistingControl()
        {
            var target=At();Step(Combat(Tower("Sniper","epic")),.01f,target);Assert.AreEqual(.75f,target.Last.IgnoreArmorFraction);
            var plain=At();var slowed=At();slowed.Control=.3f;Step(Combat(Tower("Sniper","legend")),.01f,plain);Step(Combat(Tower("Sniper","legend")),.01f,slowed);Assert.AreEqual(plain.Last.Amount*1.25f,slowed.Last.Amount,.001);
        }
        [Test]public void ScatterExpandsConeAndLegendChillsEachVictimOnceWithColdAlly()
        {
            var a=At(.5f);var edge=At(.57f,.4f,2);var behind=At(-.5f,0,3);Step(Combat(Tower("Scatter","epic")),.01f,a,edge,behind);Assert.AreEqual(1,edge.Hits);Assert.AreEqual(0,behind.Hits);
            a=At(.5f);Step(Combat(Tower("Scatter","legend")),.01f,a);Assert.AreEqual(0,a.Chills);
            a=At(.5f);edge=At(.6f,.1f,2);Step(Combat(Tower("Scatter","legend"),Tower("Cryo"),Tower("Frost")),.01f,a,edge);Assert.AreEqual(1,a.Chills);Assert.AreEqual(1,edge.Chills);
        }
        [Test]public void FlameBurnLingersAndLegendHasBoundedArmorBreak()
        {
            var target=At();Step(Combat(Tower("Flame","epic")),.1f,target);Assert.AreEqual(2.4f,target.BurnSeconds,.001);
            target=At();var boss=At(.8f,0,2);boss.IsBoss=true;Step(Combat(Tower("Flame","legend")),.1f,target,boss);Assert.AreEqual(.15f,target.Break);Assert.AreEqual(.075f,boss.Break);Assert.AreEqual(2,target.BreakSeconds);
        }
        [Test]public void MissileExpandedRadiusAndEmpApplyOnlyOnImpact()
        {
            var tower=Tower("Missile","epic");var target=At();var edge=At(.7f,tower.Stats.splashRadiusMeters.value*1.1f,2);var engine=Combat(tower);Step(engine,.01f,target,edge);Assert.AreEqual(0,edge.Hits);Step(engine,.66f,target,edge);Assert.AreEqual(1,edge.Hits);
            target=At();target.Shield=100;target.IsBoss=true;engine=Combat(Tower("Missile","legend"));Step(engine,.01f,target);Assert.AreEqual(100,target.Shield);Step(engine,.66f,target);Assert.AreEqual(80,target.Shield,.001);Assert.AreEqual(.075f,target.Break);Assert.AreEqual(1,target.Hits);
        }
        [Test]public void RailgunExtraPiercesAndLegendConservesDamageAndIgnoresArmor()
        {
            var targets=Enumerable.Range(0,6).Select(i=>At(.5f+i*.35f,0,i+1)).ToArray();Step(Combat(Tower("Railgun","epic")),.01f,targets);Assert.AreEqual(5,targets.Count(t=>t.Hits>0));Assert.AreEqual(0,targets[5].Hits);
            targets=Enumerable.Range(0,3).Select(i=>At(.5f+i*.35f,0,i+1)).ToArray();Step(Combat(Tower("Railgun","legend")),.01f,targets);Assert.AreEqual(targets[0].Last.Amount*.95f,targets[1].Last.Amount,.001);Assert.AreEqual(.25f,targets[2].Last.IgnoreArmorFraction);
        }
        [Test]public void BladeLegendBoostsControlledTargetsBeforeAddingItsOwnChill()
        {
            var target=At(.5f);var slowed=At(.5f,.1f,2);slowed.Control=.3f;Step(Combat(Tower("Blade","legend"),Tower("Cryo"),Tower("Frost")),.01f,target,slowed);
            Assert.AreEqual(target.Last.Amount*1.25f,slowed.Last.Amount,.001);Assert.AreEqual(1,target.Chills);Assert.AreEqual(1,slowed.Chills);
            target=At(.5f);Step(Combat(Tower("Blade","legend")),.01f,target);Assert.AreEqual(0,target.Chills);
        }
        [Test]public void WrongWeaponEffectIsRejectedRatherThanSilentlySellingAnInertSkin()
        {
            catalog.Skin("tesla-epic").weaponId="Mortar";Assert.Throws<ArgumentException>(()=>ProgressionCatalog.Parse(JsonUtility.ToJson(catalog)));
        }
    }
}
