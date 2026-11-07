using System;
using System.Threading;
using System.Threading.Tasks;

namespace RoomSiege.Scan
{
    // Drain cancelled native queries before starting a capture on the same provider.
    public sealed class RoomScanQueue
    {
        private readonly SemaphoreSlim gate = new SemaphoreSlim(1, 1);

        public async Task<RoomSnapshot> Run(Func<Task<RoomSnapshot>> operation, CancellationToken token)
        {
            await gate.WaitAsync(token);
            try
            {
                token.ThrowIfCancellationRequested();
                return await operation();
            }
            finally { gate.Release(); }
        }
    }
}
