using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using NUnit.Framework;
using RoomSiege.Meta;
using RoomSiege.Progression;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    internal static class PaidSupplyFixture
    {
        public static void Stock(ProgressionStore store)
        {
            if(store.Achievements.Contains("fixture-stock"))return;
            var catalog=ProgressionCatalog.Load();Assert.IsTrue(store.CompleteChallenge("fixture-stock",catalog.supplies.Sum(x=>x.price)));
            foreach(var item in catalog.supplies)Assert.IsTrue(store.Buy(item.id,out _),item.id);
        }
    }
    public sealed class PurchasedSupplyTests
    {
        private sealed class Memory:IProgressionStorage
        {public string[] slots={"",""};public bool fail;public string Read(int i)=>slots[i];public void Write(int i,string value){if(fail)throw new IOException("full");slots[i]=value;}}
        [Serializable]private sealed class Envelope {public string payload,sha256;}
        private static void SaveLegacy(Memory memory,ProgressionSave data)
        {
            data.purchaseLedgerReady=false;data.purchasedWeapons.Clear();data.purchasedSkins.Clear();data.purchasedInventory.Clear();
            string payload=JsonUtility.ToJson(data);string hash;
            using(var sha=SHA256.Create())hash=string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(payload)).Select(x=>x.ToString("X2")));
            string value=JsonUtility.ToJson(new Envelope{payload=payload,sha256=hash});memory.slots[0]=memory.slots[1]=value;
        }
        [Test]public void ReleaseStartsWithoutTrialStockOrPremiumEntitlements()
        {
            var c=ProgressionCatalog.Load();var memory=new Memory();var s=new ProgressionStore(c,memory);
            Assert.IsFalse(PlaytestAccess.Load().unlockAllContent);Assert.IsTrue(PlaytestAccess.Load().Apply(s));
            foreach(var item in c.supplies)Assert.AreEqual(0,s.Count(item.id),item.id);
            foreach(var item in c.weaponUnlocks)Assert.IsFalse(s.OwnsWeapon(item.id),item.id);
            foreach(var skin in c.skins)Assert.IsFalse(s.Owns(skin.id),skin.id);
            Assert.IsTrue(new[]{"MG","Frost","Laser","Trap"}.All(s.OwnsWeapon));
            Assert.IsFalse(s.Buy("freeze",out _));Assert.IsFalse(s.Equip("MG","mg-epic",out _));
            Assert.IsTrue(s.BeginRun("r",0));Assert.IsFalse(s.Consume("r","freeze"));
            s=new ProgressionStore(c,memory);Assert.AreEqual(0,s.Count("freeze"));
        }
        [Test]public void LegacyTrialIsArchivedRevokedAndDoesNotReturnAfterReload()
        {
            var c=ProgressionCatalog.Load();var m=new Memory();var s=new ProgressionStore(c,m);s.CompleteChallenge("earned",1200);
            s.UnlockAllForPlaytest(50);s.GrantSupplyTestStock(50);s.SetLoadout(new[]{"Tesla","Mortar","Missile","Gravity"},out _);s.Equip("Tesla","tesla-epic",out _);
            SaveLegacy(m,s.Snapshot());s=new ProgressionStore(c,m);Assert.IsTrue(s.Writable,s.Error);Assert.IsTrue(PlaytestAccess.Load().Apply(s),s.Error);
            Assert.AreEqual(1200,s.Fragments);Assert.Contains("earned",s.Achievements.ToArray());Assert.IsFalse(s.PlaytestUnlocked);
            Assert.IsTrue(c.weaponUnlocks.All(x=>!s.OwnsWeapon(x.id)));Assert.IsTrue(c.skins.All(x=>!s.Owns(x.id)));Assert.IsTrue(c.supplies.All(x=>s.Count(x.id)==0));
            CollectionAssert.AreEqual(new[]{"MG","Frost","Laser","Trap"},s.Loadout);Assert.AreEqual("",s.Equipped("Tesla"));
            Assert.IsNotEmpty(s.Snapshot().retiredTrialSnapshot);int revision=s.Revision;
            s=new ProgressionStore(c,m);Assert.IsTrue(PlaytestAccess.Load().Apply(s));Assert.AreEqual(revision,s.Revision);Assert.IsFalse(s.OwnsWeapon("Tesla"));
        }
        [Test]public void RecordedPurchasesAndUnspentPaidStockSurviveTrialRetirement()
        {
            var c=ProgressionCatalog.Load();var m=new Memory();var s=new ProgressionStore(c,m);s.CompleteChallenge("earned",10000);
            Assert.IsTrue(s.Buy("Tesla",out _));Assert.IsTrue(s.Buy("tesla-epic",out _));s.Equip("Tesla","tesla-epic",out _);s.Buy("freeze",out _);
            s.UnlockAllForPlaytest(50);s.BeginRun("r",0);for(int i=0;i<49;i++)Assert.IsTrue(s.Consume("r","freeze"));
            int wallet=s.Fragments;Assert.IsTrue(s.RetirePlaytestAccess());Assert.IsTrue(s.OwnsWeapon("Tesla"));Assert.IsTrue(s.Owns("tesla-epic"));Assert.AreEqual("tesla-epic",s.Equipped("Tesla"));
            Assert.AreEqual(1,s.Count("freeze"));Assert.AreEqual(0,s.Count("mine"));Assert.AreEqual(wallet,s.Fragments);
            Assert.IsTrue(s.Consume("r","freeze"));Assert.IsFalse(s.Consume("r","freeze"));s=new ProgressionStore(c,m);Assert.AreEqual(0,s.Count("freeze"));
        }
        [Test]public void FailedRetirementKeepsDurableSnapshotBlocksTrialUsageAndCanRetry()
        {
            var c=ProgressionCatalog.Load();var m=new Memory();var s=new ProgressionStore(c,m);s.UnlockAllForPlaytest(50);var before=m.slots.ToArray();
            m.fail=true;Assert.IsFalse(s.RetirePlaytestAccess());CollectionAssert.AreEqual(before,m.slots);
            Assert.IsFalse(s.OwnsWeapon("Tesla"));Assert.IsFalse(s.Owns("mg-epic"));Assert.AreEqual(0,s.Count("freeze"));Assert.IsFalse(s.BeginRun("r",0));
            m.fail=false;Assert.IsTrue(s.RetirePlaytestAccess());Assert.IsFalse(s.PlaytestUnlocked);Assert.IsTrue(s.BeginRun("r",0));
        }
        [Test]public void NormalLegacyPurchasesRemainOwnedAfterLedgerMigration()
        {
            var c=ProgressionCatalog.Load();var m=new Memory();var s=new ProgressionStore(c,m);s.CompleteChallenge("earned",5000);s.Buy("Tesla",out _);s.Buy("freeze",out _);
            SaveLegacy(m,s.Snapshot());s=new ProgressionStore(c,m);Assert.IsTrue(s.Writable,s.Error);Assert.IsTrue(PlaytestAccess.Load().Apply(s),s.Error);Assert.IsTrue(s.OwnsWeapon("Tesla"));Assert.AreEqual(1,s.Count("freeze"));
        }
        [Test]public void NewModelReviewRefillsEarlierTrialOnceAndPurchaseBuildRevokesIt()
        {
            var c=ProgressionCatalog.Load();var m=new Memory();var s=new ProgressionStore(c,m);
            s.CompleteChallenge("earned",10000);Assert.IsTrue(s.Buy("freeze",out _));
            var old=new PlaytestAccess{unlockAllContent=true,supplyStock=50};Assert.IsTrue(old.Apply(s));
            s.BeginRun("r",0);for(int i=0;i<10;i++)Assert.IsTrue(s.Consume("r","freeze"));Assert.AreEqual(40,s.Count("freeze"));
            var review=new PlaytestAccess{unlockAllContent=true,supplyStock=50,stockGrantId="playtest-supplies-2026-11-05-model-review"};
            int wallet=s.Fragments;Assert.IsTrue(review.Apply(s));Assert.IsTrue(c.weapons.All(s.OwnsWeapon));Assert.IsTrue(c.skins.All(x=>s.Owns(x.id)));
            foreach(var item in c.supplies)Assert.AreEqual(50,s.Count(item.id));Assert.AreEqual(wallet,s.Fragments);
            Assert.IsTrue(s.Consume("r","freeze"));s=new ProgressionStore(c,m);int revision=s.Revision;Assert.IsTrue(review.Apply(s));Assert.AreEqual(49,s.Count("freeze"));Assert.AreEqual(revision,s.Revision);
            Assert.IsTrue(new PlaytestAccess().Apply(s));Assert.AreEqual(1,s.Count("freeze"));Assert.IsTrue(c.weaponUnlocks.All(x=>!s.OwnsWeapon(x.id)));
            Assert.IsFalse(s.Achievements.Any(x=>x.StartsWith("playtest-supplies-")));Assert.AreEqual(wallet,s.Fragments);
        }
        [Test]public void SustainedDamageIsReducedWhileDurationsAndBurstRoleRemain()
        {
            var c=ProgressionCatalog.Load();Assert.AreEqual(240,c.Supply("strike").magnitude);Assert.AreEqual(18,c.Supply("firefield").magnitude);
            Assert.AreEqual(65,c.Supply("thunder").magnitude);Assert.AreEqual(600,c.Supply("mine").magnitude);Assert.AreEqual(1.35f,c.Supply("overload").magnitude,.001f);
            foreach(var item in c.supplies)Assert.GreaterOrEqual(item.duration,30,item.id);
            Assert.Less(c.Supply("strike").magnitude/c.Supply("strike").pulseInterval,100);Assert.Less(c.Supply("thunder").magnitude,100);
        }
        [TestCase("freeze")][TestCase("emp")][TestCase("overload")][TestCase("decoy")][TestCase("repair")][TestCase("strike")]
        [TestCase("firefield")][TestCase("gravity")][TestCase("mine")][TestCase("thunder")][TestCase("corrosion")][TestCase("frostsigil")]
        public void EachSupportUsesAnAuthoredGroundedMeshAndMatchingPortrait(string id)
        {
            var parent=new GameObject("Support model test");
            try
            {
                var go=SupportDeviceModel.Create(parent.transform,ProgressionCatalog.Load().Supply(id));Assert.IsNotNull(go);
                var renderers=go.GetComponentsInChildren<MeshRenderer>();Assert.IsNotEmpty(renderers);var bounds=renderers[0].bounds;foreach(var r in renderers)bounds.Encapsulate(r.bounds);
                Assert.AreEqual(0,bounds.min.y,.003f);Assert.That(bounds.size.x,Is.InRange(.1f,.4f));Assert.That(bounds.size.z,Is.InRange(.1f,.4f));Assert.That(bounds.size.y,Is.InRange(.10f,.40f));
                Assert.IsEmpty(go.GetComponentsInChildren<Collider>());Assert.Greater(renderers.Sum(x=>x.GetComponent<MeshFilter>().sharedMesh.triangles.Length/3),3000);
                foreach(var r in renderers)foreach(var material in r.sharedMaterials){Assert.IsNotNull(material.shader);Assert.IsTrue(material.shader.isSupported);}
                Assert.IsNotNull(Resources.Load<Texture2D>("SupportDevices/Portraits/"+id));
            }
            finally{UnityEngine.Object.DestroyImmediate(parent);}
        }
    }
}
