using System;
using System.Threading;
using System.Threading.Tasks;

namespace RoomSiege.Scan
{
    // Reopen the provider once: repeated queries cannot repair a stale native
    // room-data mapping left behind by a permission dialog/session suspension.
    public static class RoomAnchorRecovery
    {
        public static async Task<T> ReadAsync<T>(Func<CancellationToken,Task> ready,
            Func<CancellationToken,Task<T>> query,Func<T,bool> empty,
            Func<CancellationToken,Task> restart,Func<CancellationToken,Task> delay,
            CancellationToken token)
        {
            for(int attempt=0;attempt<4;attempt++)
            {
                token.ThrowIfCancellationRequested();
                await ready(token);
                token.ThrowIfCancellationRequested();
                var result=await query(token);
                token.ThrowIfCancellationRequested();
                if(!empty(result))return result;
                if(attempt==1)
                {
                    await ready(token);
                    token.ThrowIfCancellationRequested();
                    await restart(token);
                }
                if(attempt<3)await delay(token);
            }
            throw new InvalidOperationException("重试后仍未读到锚点；请确认系统已保存当前房间，再点扫描真实房间");
        }

        public static async Task WaitUntilStable(Func<bool> ready,Func<double> now,
            Func<CancellationToken,Task> delay,CancellationToken token,double stableSeconds=.75)
        {
            double since=-1;
            while(true)
            {
                token.ThrowIfCancellationRequested();
                double current=now();
                if(!ready())since=-1;
                else
                {
                    if(since<0)since=current;
                    if(current-since>=stableSeconds)return;
                }
                await delay(token);
            }
        }
    }
}
