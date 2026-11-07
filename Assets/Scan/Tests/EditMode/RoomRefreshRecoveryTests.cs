using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using UnityEngine;

namespace RoomSiege.Scan.Tests
{
    public sealed class RoomRefreshRecoveryTests
    {
        private sealed class UnavailableRoom:IRoomScanner
        {
            public bool TryEnablePassthrough()=>true;
            public Task<RoomSnapshot> ScanAsync(Transform space,CancellationToken token,Action<string> progress,bool requestCapture=false)
                =>Task.FromException<RoomSnapshot>(new InvalidOperationException("room provider temporarily empty"));
        }

        [Test]
        public void FailedRefreshKeepsLocatedRoomAndDoesNotCancelFutureReads()
        {
            var root=new GameObject("inactive scan recovery test");root.SetActive(false);
            var camera=new GameObject("scan recovery camera");
            var cancellation=new CancellationTokenSource();
            try
            {
                var scan=root.AddComponent<ScanController>();
                var room=SandboxRoom.Create(Vector3.zero);
                var grid=GridBuilder.Build(room,Vector3.zero,ScanMode.RealRoom);
                var flags=BindingFlags.Instance|BindingFlags.NonPublic;
                typeof(ScanController).GetField("head",flags).SetValue(scan,camera.AddComponent<Camera>());
                typeof(ScanController).GetField("scanner",flags).SetValue(scan,new UnavailableRoom());
                typeof(ScanController).GetField("cancellation",flags).SetValue(scan,cancellation);
                typeof(ScanController).GetProperty("CurrentRoom").SetValue(scan,room);
                typeof(ScanController).GetProperty("CurrentGrid").SetValue(scan,grid);
                var refresh=typeof(ScanController).GetMethod("RefreshLocatedRoom",flags);
                refresh.Invoke(scan,null);
                Assert.AreSame(room,scan.CurrentRoom);Assert.AreSame(grid,scan.CurrentGrid);
                Assert.IsFalse(cancellation.IsCancellationRequested);
                Assert.IsFalse((bool)typeof(ScanController).GetField("refreshing",flags).GetValue(scan));
                refresh.Invoke(scan,null);
                Assert.AreSame(grid,scan.CurrentGrid);Assert.IsFalse(cancellation.IsCancellationRequested);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(camera);cancellation.Dispose();
            }
        }
    }
}
