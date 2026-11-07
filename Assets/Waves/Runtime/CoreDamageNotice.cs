using System.Collections.Generic;
using RoomSiege.Enemies;

namespace RoomSiege.Waves
{
    // Keep one notice per burst, so simultaneous leaks do not stack labels.
    public sealed class CoreDamageNotice
    {
        private readonly List<string> kinds=new List<string>();
        public int Count {get;private set;}
        public float Damage {get;private set;}
        public float Remaining {get;private set;}
        public bool Visible=>Remaining>0;
        public string Summary=>Visible?$"{(kinds.Count>2?kinds[0]+"、"+kinds[1]+"等":string.Join("、",kinds))}漏过{Count}只 · -{Damage:0.##}生命":"";
        public void Add(EnemyLeak leak)
        {
            if(leak.LifeDamage<=0||float.IsNaN(leak.LifeDamage)||float.IsInfinity(leak.LifeDamage))return;
            if(!Visible)Clear();
            string name=EnemyReadability.Name(leak.EnemyId);if(!kinds.Contains(name))kinds.Add(name);
            Count++;Damage+=leak.LifeDamage;Remaining=4f;
        }
        public void Tick(float dt,bool paused)
        {
            if(paused||dt<=0||float.IsNaN(dt)||float.IsInfinity(dt))return;
            Remaining=UnityEngine.Mathf.Max(0,Remaining-dt);
        }
        public void Clear(){kinds.Clear();Count=0;Damage=Remaining=0;}
    }
}
