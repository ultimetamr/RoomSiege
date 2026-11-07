using UnityEngine;

namespace RoomSiege.Enemies
{
    public readonly struct EnemyKill
    {
        public int SpawnId { get; }
        public string EnemyId { get; }
        public Vector3 Position { get; }
        public int Gold { get; }
        public bool IsSummoned { get; }
        public EnemyKill(int spawnId,string id,Vector3 position,int gold,bool summoned)
        { SpawnId=spawnId;EnemyId=id;Position=position;Gold=gold;IsSummoned=summoned; }
    }
    public readonly struct EnemyLeak
    {
        public int SpawnId { get; }
        public string EnemyId { get; }
        public float LifeDamage { get; }
        public EnemyLeak(int spawnId,string enemyId,float damage)
        { SpawnId=spawnId;EnemyId=enemyId;LifeDamage=damage; }
    }
}
