using System;
using System.Collections;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine.TestTools;

namespace RoomSiege.Scan.Tests
{
    public sealed class RoomScanQueueTests
    {
        [UnityTest]
        public IEnumerator CaptureWaitsForCancelledReadToDrainThenRuns()
        {
            var queue=new RoomScanQueue();var read=new TaskCompletionSource<RoomSnapshot>();
            var first=queue.Run(()=>read.Task,CancellationToken.None);
            bool launched=false;var expected=new RoomSnapshot();
            var capture=queue.Run(()=>{launched=true;return Task.FromResult(expected);},CancellationToken.None);
            Assert.IsFalse(launched);
            read.SetCanceled();
            while(!capture.IsCompleted)yield return null;
            Assert.IsTrue(first.IsCanceled);Assert.IsTrue(launched);Assert.AreSame(expected,capture.Result);
        }

        [UnityTest]
        public IEnumerator CancelledQueuedCaptureNeverLaunchesAndFailureReleasesProvider()
        {
            var queue=new RoomScanQueue();var read=new TaskCompletionSource<RoomSnapshot>();
            var first=queue.Run(()=>read.Task,CancellationToken.None);
            var cancellation=new CancellationTokenSource();bool launched=false;
            var cancelled=queue.Run(()=>{launched=true;return Task.FromResult(new RoomSnapshot());},cancellation.Token);
            cancellation.Cancel();read.SetException(new InvalidOperationException("Native read failed"));
            while(!first.IsCompleted||!cancelled.IsCompleted)yield return null;
            Assert.IsTrue(first.IsFaulted);Assert.IsNotNull(first.Exception);Assert.IsTrue(cancelled.IsCanceled);Assert.IsFalse(launched);
            var next=queue.Run(()=>Task.FromResult(new RoomSnapshot()),CancellationToken.None);
            while(!next.IsCompleted)yield return null;
            Assert.IsTrue(next.IsCompletedSuccessfully);cancellation.Dispose();
        }
    }
}
