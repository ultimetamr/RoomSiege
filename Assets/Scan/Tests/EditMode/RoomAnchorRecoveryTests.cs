using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;

namespace RoomSiege.Scan.Tests
{
    public sealed class RoomAnchorRecoveryTests
    {
        private static Task Done(CancellationToken token)=>Task.CompletedTask;

        [Test]
        public async Task ZeroAnchorsReconnectThenRestoreSavedRoom()
        {
            int reads=0,restarts=0,ready=0;
            int anchors=await RoomAnchorRecovery.ReadAsync(
                t=>{ready++;return Done(t);},t=>Task.FromResult(++reads<3?0:29),n=>n==0,
                t=>{restarts++;return Done(t);},Done,CancellationToken.None);
            Assert.AreEqual(29,anchors);Assert.AreEqual(3,reads);
            Assert.AreEqual(1,restarts);Assert.AreEqual(4,ready);
        }

        [Test]
        public async Task HealthyRoomDoesNotRestartProvider()
        {
            int restarts=0;
            Assert.AreEqual(72,await RoomAnchorRecovery.ReadAsync(Done,t=>Task.FromResult(72),n=>n==0,
                t=>{restarts++;return Done(t);},Done,CancellationToken.None));
            Assert.Zero(restarts);
        }

        [Test]
        public void PersistentlyEmptyRoomFailsAfterOneReconnect()
        {
            int reads=0,restarts=0;
            var error=Assert.ThrowsAsync<InvalidOperationException>(async()=>await RoomAnchorRecovery.ReadAsync(
                Done,t=>{reads++;return Task.FromResult(0);},n=>n==0,
                t=>{restarts++;return Done(t);},Done,CancellationToken.None));
            StringAssert.Contains("仍未读到锚点",error.Message);
            Assert.AreEqual(4,reads);Assert.AreEqual(1,restarts);
        }

        [Test]
        public async Task ReadCannotStartBeforeTrackingReady()
        {
            var ready=new TaskCompletionSource<bool>();int reads=0;
            var task=RoomAnchorRecovery.ReadAsync(t=>ready.Task,
                t=>{reads++;return Task.FromResult(1);},n=>n==0,Done,Done,CancellationToken.None);
            Assert.Zero(reads);ready.SetResult(true);
            Assert.AreEqual(1,await task);Assert.AreEqual(1,reads);
        }

        [Test]
        public void PermissionFailureIsNotReportedAsEmptyRoom()
        {
            int restarts=0;
            Assert.ThrowsAsync<UnauthorizedAccessException>(async()=>await RoomAnchorRecovery.ReadAsync<int>(
                Done,t=>throw new UnauthorizedAccessException(),n=>n==0,
                t=>{restarts++;return Done(t);},Done,CancellationToken.None));
            Assert.Zero(restarts);
        }

        [Test]
        public void CancelledNativeReadCannotRestartOrPublish()
        {
            using var cancellation=new CancellationTokenSource();int restarts=0;
            Assert.CatchAsync<OperationCanceledException>(async()=>await RoomAnchorRecovery.ReadAsync(
                Done,t=>{cancellation.Cancel();return Task.FromResult(29);},n=>n==0,
                t=>{restarts++;return Done(t);},Done,cancellation.Token));
            Assert.Zero(restarts);
        }

        [Test]
        public async Task TrackingMustRemainStableAcrossFocusFluctuations()
        {
            double now=0;int polls=0;
            await RoomAnchorRecovery.WaitUntilStable(()=>polls!=2,()=>now,
                t=>{polls++;now+=.25;return Done(t);},CancellationToken.None);
            Assert.AreEqual(6,polls,"A focus loss must restart the .75 second stabilization window.");
        }

        [Test]
        public void SleepingHeadsetWaitIsCancellable()
        {
            using var cancellation=new CancellationTokenSource();
            Assert.CatchAsync<OperationCanceledException>(async()=>await RoomAnchorRecovery.WaitUntilStable(
                ()=>false,()=>0,t=>{cancellation.Cancel();return Done(t);},cancellation.Token));
        }
    }
}
