using UnityEngine;

namespace RoomSiege.Towers
{
    public interface ICombatStatusTarget
    {
        float ShieldPoints {get;}
        float ControlFraction {get;}
        bool AddChill(int stacks);
        bool TryFreeze(float seconds);
        void ApplyBurn(float damagePerSecond,float duration,int source);
    }
    // All freezes share a post-freeze immunity, including emergency freeze and supplies.
    public sealed class FrostControl
    {
        public int Stacks {get;private set;}
        public float FrozenRemaining {get;private set;}
        public float ProtectionRemaining {get;private set;}
        private float chillRemaining;
        public bool Frozen=>FrozenRemaining>0;
        public bool CanFreeze=>!Frozen&&ProtectionRemaining<=0;
        public bool Freeze(float duration,bool boss)
        {
            if(!CanFreeze||float.IsNaN(duration)||float.IsInfinity(duration)||duration<=0)return false;
            FrozenRemaining=boss?Mathf.Min(duration,.6f):duration;
            Stacks=0;chillRemaining=0;return true;
        }
        public bool Chill(int amount,bool boss)
        {
            if(!CanFreeze||amount<=0)return false;
            Stacks=Mathf.Min(5,Stacks+amount);chillRemaining=4;
            return Stacks>=5&&Freeze(boss?.35f:1f,boss);
        }
        public void Tick(float dt)
        {
            if(float.IsNaN(dt)||float.IsInfinity(dt)||dt<=0)return;
            float frozen=FrozenRemaining;
            FrozenRemaining=Mathf.Max(0,FrozenRemaining-dt);
            if(frozen>0&&FrozenRemaining<=0)ProtectionRemaining=Mathf.Max(0,8-(dt-frozen));
            else if(frozen<=0)ProtectionRemaining=Mathf.Max(0,ProtectionRemaining-dt);
            chillRemaining=Mathf.Max(0,chillRemaining-dt);
            if(chillRemaining<=0)Stacks=0;
        }
        public void Clear(){Stacks=0;FrozenRemaining=ProtectionRemaining=chillRemaining=0;}
    }
}
