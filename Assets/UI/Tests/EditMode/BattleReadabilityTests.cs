using NUnit.Framework;
using RoomSiege.Data;
using RoomSiege.Enemies;
using RoomSiege.Waves;
using UnityEngine;

namespace RoomSiege.UI.Tests
{
    public sealed class BattleReadabilityTests
    {
        [TestCase(0,0,0)] [TestCase(6,0,3)] [TestCase(5,0,3)] [TestCase(5,1,2)]
        public void ForecastMatchesRotatingEntranceSplit(int count,int ordinal,int expected)
        {Assert.AreEqual(expected,WaveThreatPresentation.EntranceCount(count,ordinal,0,2));}

        [Test]
        public void PerEntranceForecastPreservesEveryGroupTotal()
        {
            for(int count=0;count<=20;count++)for(int ordinal=0;ordinal<5;ordinal++)
                Assert.AreEqual(count,WaveThreatPresentation.EntranceCount(count,ordinal,0,2)+WaveThreatPresentation.EntranceCount(count,ordinal,1,2));
            var wave=new WaveDefinition{groups=new[]{new WaveGroupDefinition{enemyId="Grunt",count=5},new WaveGroupDefinition{enemyId="Runner",count=3}}};
            Assert.AreEqual("步兵×3 · 跑者×1",WaveThreatPresentation.Forecast(wave,0,2));
            Assert.AreEqual("步兵×2 · 跑者×2",WaveThreatPresentation.Forecast(wave,1,2));
            StringAssert.Contains("Frost",WaveThreatPresentation.StrategyHint(wave));
        }

        [Test]
        public void OffscreenDirectionsRemainRelativeToHeadYaw()
        {
            Assert.AreEqual("↑ 前方",WaveThreatPresentation.Direction(Vector3.forward,Vector3.forward));
            Assert.AreEqual("→ 右侧",WaveThreatPresentation.Direction(Vector3.forward,Vector3.right));
            Assert.AreEqual("← 左侧",WaveThreatPresentation.Direction(Vector3.forward,Vector3.left));
            Assert.AreEqual("↓ 身后",WaveThreatPresentation.Direction(Vector3.forward,Vector3.back));
            Assert.AreEqual("↑ 前方",WaveThreatPresentation.Direction(Vector3.right,Vector3.right));
            Assert.AreEqual("↓ 身后",WaveThreatPresentation.Direction(Vector3.right,Vector3.left));
        }

        [Test]
        public void HealthAndStatusNeverMislabelFullHealthAsCritical()
        {
            Assert.AreEqual(1,EnemyReadability.Fraction(30,30));Assert.AreEqual(0,EnemyReadability.Fraction(float.NaN,30));
            Assert.AreEqual(0,EnemyReadability.Fraction(10,0));Assert.AreEqual(1,EnemyReadability.Fraction(100,30));
            Assert.AreEqual("",EnemyReadability.Status(1,0,0,false));
            Assert.AreEqual("护盾 · 减速 30%",EnemyReadability.Status(1,10,.3f,false));
            Assert.AreEqual("护盾 · 减速 40% · 2层",EnemyReadability.Status(1,10,.4f,false,2));
            Assert.AreEqual("护盾 · 减速 50% · 3层",EnemyReadability.Status(1,10,.5f,false,3));
            Assert.AreEqual("破盾 · 冰冻 · 残血",EnemyReadability.Status(.2f,0,1,true));
            Assert.AreEqual("",EnemyReadability.Status(0,0,0,false));
        }

        [Test]
        public void EnemyRolesMakeSpecialistsReadable()
        {
            Assert.AreEqual("基础推进",EnemyReadability.Role("Grunt"));
            Assert.AreEqual("高速蛇形",EnemyReadability.Role("Runner"));
            Assert.AreEqual("重装推进",EnemyReadability.Role("Tank"));
            Assert.AreEqual("护盾前排",EnemyReadability.Role("Shielded"));
            Assert.AreEqual("首领召唤",EnemyReadability.Role("Boss"));
        }

        [Test]
        public void LeakNoticeAggregatesFractionalDamagePausesAndResets()
        {
            var notice=new CoreDamageNotice();
            notice.Add(new EnemyLeak(1,"Grunt",.5f));notice.Add(new EnemyLeak(2,"Tank",3));
            Assert.AreEqual(2,notice.Count);Assert.AreEqual(3.5f,notice.Damage);
            StringAssert.Contains("步兵、重装",notice.Summary);StringAssert.Contains("-3.5生命",notice.Summary);
            notice.Tick(10,true);Assert.IsTrue(notice.Visible);
            notice.Tick(4,false);Assert.IsFalse(notice.Visible);
            notice.Add(new EnemyLeak(3,"Runner",1));Assert.AreEqual(1,notice.Count);Assert.AreEqual(1,notice.Damage);
            notice.Clear();Assert.AreEqual("",notice.Summary);Assert.IsFalse(notice.Visible);
            notice.Add(new EnemyLeak(4,"Boss",float.NaN));Assert.IsFalse(notice.Visible);
        }
    }
}
