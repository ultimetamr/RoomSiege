using NUnit.Framework;
using RoomSiege.Meta;
using RoomSiege.Waves;

namespace RoomSiege.UI.Tests
{
    public sealed class WaveFlowTests
    {
        [Test]
        public void BossCueReplacesStartOnceAndDoesNotQueueBehindClear()
        {
            var cue=new WavePhaseNotice();
            Assert.IsFalse(cue.BossArrived());
            cue.BeginWave(10,10);Assert.AreEqual(WavePhaseCue.Started,cue.Kind);
            Assert.IsTrue(cue.BossArrived());Assert.AreEqual(WavePhaseCue.Boss,cue.Kind);
            cue.Tick(1,false);float remaining=cue.Remaining;
            Assert.IsFalse(cue.BossArrived());Assert.AreEqual(remaining,cue.Remaining);
            cue.Tick(100,true);Assert.IsTrue(cue.Visible);
            cue.Tick(10,false);Assert.IsFalse(cue.Visible);
            Assert.IsFalse(cue.BossArrived(),"Expired boss cues cannot replay in the same wave");
            cue.EndWave(new WaveResult(10,9,0,300,WaveEndReason.Cleared),10);
            Assert.IsFalse(cue.Visible);Assert.IsFalse(cue.BossArrived());
            cue.Reset();cue.BeginWave(1,10);Assert.IsTrue(cue.BossArrived());
        }

        [TestCase(0,"零漏怪")]
        [TestCase(3,"漏过 3 只")]
        public void ClearCueUsesActualLeaksAndNextWaveImmediatelyReplacesIt(int leaks,string expected)
        {
            var cue=new WavePhaseNotice();cue.BeginWave(1,10);
            cue.EndWave(new WaveResult(1,6-leaks,leaks,150,WaveEndReason.Cleared),10);
            Assert.AreEqual(WavePhaseCue.Cleared,cue.Kind);StringAssert.Contains(expected,cue.Text);
            Assert.IsFalse(cue.BossArrived());
            cue.BeginWave(2,10);Assert.AreEqual(WavePhaseCue.Started,cue.Kind);StringAssert.Contains("2 / 10",cue.Text);
            cue.Tick(float.NaN,false);Assert.AreEqual(2.5f,cue.Remaining);
            cue.Reset();Assert.IsFalse(cue.Visible);Assert.AreEqual("",cue.Text);
        }

        [TestCase(WaveEndReason.Failed)]
        [TestCase(WaveEndReason.Interrupted)]
        public void NonClearEndNeverClaimsTheWaveWasDefended(WaveEndReason reason)
        {
            var cue=new WavePhaseNotice();cue.BeginWave(1,10);
            cue.EndWave(new WaveResult(1,1,1,100,reason),10);Assert.IsFalse(cue.Visible);
        }

        [Test]
        public void FailureNamesTheRealLastLeakAndUnknownCauseRemainsUnattributed()
        {
            string actual=SettlementFeedback.Reason(CampaignOutcome.Failure,6,"生命归零","Shielded");
            StringAssert.Contains("第 6 波",actual);StringAssert.Contains("护盾兵",actual);
            string fallback=SettlementFeedback.Reason(CampaignOutcome.Failure,1,"生命归零","");
            StringAssert.DoesNotContain("最后漏过",fallback);
            Assert.AreEqual("返回主菜单",SettlementFeedback.Reason(CampaignOutcome.Interrupted,2,"返回主菜单","Boss"));
            StringAssert.DoesNotContain("漏过",SettlementFeedback.Reason(CampaignOutcome.Victory,10,"胜利","Boss"));
        }

        [TestCase("Runner","减速跑者")]
        [TestCase("Tank","升级输出塔")]
        [TestCase("Shielded","激光破盾")]
        [TestCase("Boss","召唤物")]
        public void RetryAdviceMatchesTheObservedEnemy(string enemy,string expected)
        {StringAssert.Contains(expected,SettlementFeedback.NextAttempt(CampaignOutcome.Failure,MetaRating.Copper,enemy));}

        [Test]
        public void VictoryAndInterruptedAdviceDoNotReuseFailureAdvice()
        {
            StringAssert.Contains("购买皮肤",SettlementFeedback.NextAttempt(CampaignOutcome.Victory,MetaRating.Gold,"Boss"));
            StringAssert.Contains("≥300G",SettlementFeedback.NextAttempt(CampaignOutcome.Victory,MetaRating.Silver,"Runner"));
            StringAssert.Contains("调整房间与难度",SettlementFeedback.NextAttempt(CampaignOutcome.Interrupted,MetaRating.None,"Boss"));
        }
    }
}
