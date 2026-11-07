using System;
using UnityEngine;

namespace RoomSiege.Progression
{
    [Serializable] public sealed class PlaytestAccess
    {
        public bool unlockAllContent;
        public int supplyStock=50;
        public string stockGrantId=ProgressionStore.SupplyTestGrantId;
        public static PlaytestAccess Load()
        {
            var asset=Resources.Load<TextAsset>("RoomSiegePlaytest");
            return asset==null?new PlaytestAccess():JsonUtility.FromJson<PlaytestAccess>(asset.text);
        }
        public bool Apply(ProgressionStore store)=>unlockAllContent?
            store.UnlockAllForPlaytest(supplyStock)&&store.GrantSupplyTestStock(supplyStock,stockGrantId):store.RetirePlaytestAccess();
    }
}
