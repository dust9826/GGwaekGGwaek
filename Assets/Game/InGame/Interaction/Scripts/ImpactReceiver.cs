using UnityEngine;

namespace PPack
{
    public enum EImpactCause
    {
        PhysicalCollision,
        DirectAttack,
        ExternalImpulse,
    }

    public readonly struct ImpactHit
    {
        public ImpactHit(EImpactCause cause, Vector3 impulse, Vector3 point)
        {
            Cause = cause;
            Impulse = impulse;
            Point = point;
        }

        public EImpactCause Cause { get; }
        public Vector3 Impulse { get; }
        public Vector3 Point { get; }
        public float MomentumKgMps => Impulse.magnitude;
        public Vector3 Direction => Impulse.sqrMagnitude > 0.0001f ? Impulse.normalized : Vector3.forward;
    }

    /// <summary>공격·차량처럼 충돌 콜백 밖에서 전달되는 충격의 공용 진입점.</summary>
    public abstract class ImpactReceiver : MonoBehaviour
    {
        public abstract void ReceiveImpact(ImpactHit hit);
    }
}
