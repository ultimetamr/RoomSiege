using NUnit.Framework;

namespace RoomSiege.UI.Tests
{
    public sealed class UiPressGateTests
    {
        [Test]
        public void ClosingPageConsumesHeldPressUntilPhysicalRelease()
        {
            var gate = new UiPressGate();
            Assert.IsTrue(gate.Update(true));
            gate.BlockUntilRelease();
            Assert.IsFalse(gate.Output);
            // A fresh/reset UI model must never see the original held press as another down.
            for (int i = 0; i < 120; i++) Assert.IsFalse(gate.Update(true));
            Assert.IsTrue(gate.IsHeld);
            Assert.IsFalse(gate.Update(false));
            Assert.IsFalse(gate.IsHeld);
            Assert.IsTrue(gate.Update(true));
        }

        [Test]
        public void PageTransitionConsumesBothHandsIndependently()
        {
            var left = new UiPressGate(); var right = new UiPressGate();
            left.Update(true); right.Update(true);
            left.BlockUntilRelease(); right.BlockUntilRelease();
            left.Update(false);
            Assert.IsFalse(right.Update(true));
            Assert.IsTrue(left.Update(true));
            right.Update(false);
            Assert.IsTrue(right.Update(true));
        }

        [Test]
        public void OrdinaryHoldRemainsAvailableForDragging()
        {
            var gate = new UiPressGate();
            Assert.IsTrue(gate.Update(true));
            Assert.IsTrue(gate.Update(true));
            Assert.IsFalse(gate.Update(false));
        }
    }
}
