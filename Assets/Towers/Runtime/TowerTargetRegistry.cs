using System.Collections.Generic;
using UnityEngine;

namespace RoomSiege.Towers
{
    public enum TowerDamageKind { Projectile, Splash, Frost, Laser, Trap }

    public readonly struct TowerHit
    {
        public float Amount { get; }
        public Vector3 Point { get; }
        public TowerDamageKind Kind { get; }
        public int SourceTowerId { get; }
        public float IgnoreArmorFraction {get;}
        public TowerHit(float amount, Vector3 point, TowerDamageKind kind, int sourceTowerId,float ignoreArmorFraction=0)
        { Amount = amount; Point = point; Kind = kind; SourceTowerId = sourceTowerId;IgnoreArmorFraction=float.IsNaN(ignoreArmorFraction)?0:Mathf.Clamp01(ignoreArmorFraction); }
    }

    public interface ITowerHitReceiver { void ReceiveHit(TowerHit hit); }
    public interface ITowerFocusVisual { void SetFocused(bool focused); }
    public interface ITowerTargetReadiness { bool CanBeTargeted { get; } }

    // Runtime enemies register here without creating a Towers -> Enemies assembly dependency.
    public static class TowerTargetRegistry
    {
        private static readonly List<ITowerTarget> registered = new List<ITowerTarget>();
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset() => registered.Clear();
        public static void Register(ITowerTarget target)
        { if (target != null && !registered.Contains(target)) registered.Add(target); }
        public static void Unregister(ITowerTarget target) => registered.Remove(target);
        public static void AppendAliveTo(List<ITowerTarget> snapshot)
        {
            foreach (var target in registered)
                if (CanTarget(target))
                    snapshot.Add(target);
        }
        public static ITowerTarget FromCollider(Collider collider)
        {
            if (collider == null) return null;
            foreach (var component in collider.GetComponentsInParent<MonoBehaviour>())
                if (component is ITowerTarget target && target.IsAlive) return target;
            return null;
        }
        public static void Hit(ITowerTarget target, TowerHit hit)
        {
            if (target == null || !target.IsAlive) return;
            if (hit.SourceTowerId > 0 && !CanTarget(target)) return;
            if (target is ITowerHitReceiver receiver) receiver.ReceiveHit(hit);
            else target.Damage(hit.Amount);
        }
        public static bool CanTarget(ITowerTarget target) => target != null &&
            !(target is Object unityObject && unityObject == null) && target.IsAlive &&
            (!(target is ITowerTargetReadiness readiness) || readiness.CanBeTargeted);
    }
}
