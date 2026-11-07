using NUnit.Framework;

namespace RoomSiege.UI.Tests
{
    public sealed class FirstRunTutorialTests
    {
        private static TutorialObservation Build => new TutorialObservation { State=GameState.BUILD, RoomReady=true };

        [Test]
        public void OnlySuccessfulGameFactsAdvanceEachOfTheSixSteps()
        {
            var tutorial=new FirstRunTutorial();tutorial.Begin();
            Assert.AreEqual(TutorialStep.Room,tutorial.Step);
            tutorial.Observe(new TutorialObservation{State=GameState.SCAN,RoomReady=true});
            Assert.AreEqual(TutorialStep.Room,tutorial.Step,"A loaded room alone does not start the campaign");
            var facts=Build;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Choose,tutorial.Step);
            tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Choose,tutorial.Step,"Rejected purchase does not advance");
            facts.HasPendingTower=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Place,tutorial.Step);
            tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Place,tutorial.Step,"Rejected placement does not advance");
            facts.HasTowers=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Inspect,tutorial.Step);
            facts.HasManagedTower=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Upgrade,tutorial.Step);
            tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Upgrade,tutorial.Step,"Rejected upgrade does not advance");
            facts.HasUpgradedTower=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Launch,tutorial.Step);
            facts.State=GameState.WAVE;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Launch,tutorial.Step,"Wait for an actual wave");
            facts.WaveActive=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Completed,tutorial.Step);
        }

        [Test]
        public void CancelingSelectionAndSellingAllTowersReturnToAnActionableStep()
        {
            var tutorial=new FirstRunTutorial();tutorial.Begin();var facts=Build;
            facts.HasPendingTower=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Place,tutorial.Step);
            facts.HasPendingTower=false;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Choose,tutorial.Step);
            facts.HasTowers=facts.HasUpgradedTower=true;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Launch,tutorial.Step);
            facts.HasTowers=facts.HasUpgradedTower=false;tutorial.Observe(facts);Assert.AreEqual(TutorialStep.Choose,tutorial.Step);
        }

        [Test]
        public void InsufficientFundsCanDeferUpgradeAndReopeningManagementRemainsActionable()
        {
            var tutorial=new FirstRunTutorial();tutorial.Begin();var facts=Build;
            facts.HasTowers=facts.HasManagedTower=true;tutorial.Observe(facts);
            Assert.AreEqual(TutorialStep.Upgrade,tutorial.Step);StringAssert.Contains("金币不足",tutorial.Body);
            facts.HasManagedTower=false;tutorial.Observe(facts);StringAssert.Contains("再次点击已有塔",tutorial.Body);
            tutorial.DeferUpgrade();tutorial.Observe(facts);
            Assert.IsTrue(tutorial.UpgradeDeferred);Assert.AreEqual(TutorialStep.Launch,tutorial.Step);
            tutorial.Begin();Assert.IsFalse(tutorial.UpgradeDeferred);
        }

        [Test]
        public void RescanningRestartsRoomInstructionsWithoutCompletingLesson()
        {
            var tutorial=new FirstRunTutorial();tutorial.Begin();var facts=Build;
            facts.HasTowers=true;tutorial.Observe(facts);
            facts.State=GameState.SCAN;facts.RoomReady=false;tutorial.Observe(facts);
            Assert.AreEqual(TutorialStep.Room,tutorial.Step);StringAssert.Contains("模拟沙盘",tutorial.Body);
        }

        [TestCase(GameState.MENU)] [TestCase(GameState.RESULT)]
        public void InterruptedLessonDoesNotWriteACompletion(GameState state)
        {
            var tutorial=new FirstRunTutorial();int conclusions=0;tutorial.Concluded+=_=>conclusions++;
            tutorial.Begin();tutorial.Observe(new TutorialObservation{State=state});
            Assert.IsFalse(tutorial.Visible);Assert.AreEqual(0,conclusions);
            tutorial.Begin();Assert.IsTrue(tutorial.Active);
        }

        [Test]
        public void SkipAndCompletionAreDistinguishedAndOnlySavedOnce()
        {
            var tutorial=new FirstRunTutorial();int skipped=0,completed=0;
            tutorial.Concluded+=skip=>{if(skip)skipped++;else completed++;};
            tutorial.Begin();tutorial.Skip();tutorial.Skip();Assert.AreEqual(1,skipped);
            tutorial.Begin();var facts=new TutorialObservation{State=GameState.WAVE,WaveActive=true};
            tutorial.Observe(facts);tutorial.Observe(facts);Assert.AreEqual(1,completed);
            Assert.IsTrue(tutorial.Visible);Assert.IsFalse(tutorial.Active);
            tutorial.Skip();Assert.IsFalse(tutorial.Visible);Assert.AreEqual(1,skipped);
        }

        [Test]
        public void DeferringAnUnrelatedStepCannotAdvanceIt()
        {
            var tutorial=new FirstRunTutorial();tutorial.Begin();tutorial.DeferUpgrade();
            Assert.AreEqual(TutorialStep.Room,tutorial.Step);Assert.IsFalse(tutorial.UpgradeDeferred);
        }
    }
}
