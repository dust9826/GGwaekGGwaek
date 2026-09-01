using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class ThiefImpactReceiver : ImpactReceiver
    {
        [SerializeField] private ThiefActor _actor;
        [Tooltip("일반 물리 충돌로 넘어지는 최소 운동량(kg·m/s). 직접 공격에는 적용하지 않는다.")]
        [SerializeField, Min(0.01f)] private float _knockdownMomentumKgMps = 140f;

        public float KnockdownMomentumKgMps => _knockdownMomentumKgMps;

        private void Awake()
        {
            if (_actor == null) _actor = GetComponent<ThiefActor>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_actor == null || !_actor.HasBehaviorAuthority) return;
            if (!ImpactMomentum.TryCompute(collision, out float momentum, out ContactPoint contact)) return;
            ReceiveImpact(new ImpactHit(EImpactCause.PhysicalCollision,
                contact.normal * momentum, contact.point));
        }

        public override void ReceiveImpact(ImpactHit hit)
        {
            if (_actor == null || !_actor.HasBehaviorAuthority) return;
            if (hit.Cause != EImpactCause.DirectAttack &&
                hit.MomentumKgMps < _knockdownMomentumKgMps) return;
            _actor.TryBeginImpact(hit);
        }
    }
}
