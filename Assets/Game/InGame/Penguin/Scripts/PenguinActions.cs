using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈덩이를 잡지 않은 상태의 좌클릭 공격을 소유한다. 눈덩이가 지정돼 있으면 공격하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PenguinInputReader))]
    [RequireComponent(typeof(PenguinControlState))]
    public sealed class PenguinActions : MonoBehaviour
    {
        [Header("접점")]
        [SerializeField] private PenguinInputReader _input;
        [SerializeField] private Animator _animator;

        [Header("공격")]
        [SerializeField, Min(0f)] private float _attackWarmupSeconds = 0.12f;
        [SerializeField, Min(0.1f)] private float _attackCooldownSeconds = 0.55f;
        [SerializeField, Min(0.1f)] private float _attackReachM = 0.85f;
        [SerializeField, Min(0.05f)] private float _attackRadiusM = 0.5f;
        [SerializeField, Min(0f)] private float _attackImpulseNs = 150f;
        [SerializeField, Min(0f)] private float _giftAttackMaxDeltaVMps = 3f;
        [SerializeField] private LayerMask _attackLayers = ~0;

        private static readonly int AttackHash = Animator.StringToHash("Attack");
        private static readonly int DamageHash = Animator.StringToHash("Damage");
        private static readonly int DeadHash = Animator.StringToHash("Dead");
        private static readonly int ActionsEmptyStateHash = Animator.StringToHash("Adelie Actions.Empty");
        private static readonly int ActionsDeadPoseStateHash = Animator.StringToHash("Adelie Actions.DeadPose");
        private const int ActionsLayerIndex = 1;

        private readonly Collider[] _attackHits = new Collider[24];
        private readonly HashSet<Rigidbody> _hitBodies = new();
        private float _attackWarmupRemaining;
        private float _attackCooldownRemaining;
        private bool _attackPending;
        private PenguinControlState _controlState;

        public int AttackCount { get; private set; }
        public int AttackHitCount { get; private set; }
        public int DamageCount { get; private set; }
        public int HeavyImpactCount { get; private set; }
        public bool CanAct { get; set; } = true;

        private void Reset()
        {
            _input = GetComponent<PenguinInputReader>();
            _animator = GetComponentInChildren<Animator>(true);
        }

        private void Awake()
        {
            if (_input == null) _input = GetComponent<PenguinInputReader>();
            _controlState = GetComponent<PenguinControlState>();
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
        }

        /// <summary>멀티에서는 서버가 <see cref="Step"/> 을 부른다. 켜지면 로컬 <see cref="FixedUpdate"/>
        /// 는 아무것도 하지 않는다 — 규칙이 두 곳에서 돌면 싱글과 멀티의 조작이 조용히 갈린다.</summary>
        public bool NetworkDriven { get; set; }

        private void FixedUpdate()
        {
            if (NetworkDriven) return;
            Step(Time.fixedDeltaTime, _input != null && _input.PrimaryActionPressedThisFrame);
        }

        /// <summary>한 스텝. 싱글은 <see cref="FixedUpdate"/> 가, 멀티는 서버의
        /// <see cref="PenguinNetAvatar"/> 가 부른다 — <c>PenguinLocomotion.Step</c> 과 같은 모양이다.</summary>
        public void Step(float deltaSeconds, bool attackPressed)
        {
            _attackCooldownRemaining = Mathf.Max(0f, _attackCooldownRemaining - deltaSeconds);

            if (_attackPending)
            {
                _attackWarmupRemaining -= deltaSeconds;
                if (_attackWarmupRemaining <= 0f)
                {
                    _attackPending = false;
                    ApplyAttackHit();
                }
            }

            if (!CanAct) return;
            if (!attackPressed || _attackCooldownRemaining > 0f) return;
            if (_controlState != null && (_controlState.IsSnowballState ||
                _controlState.Current is EPenguinControlState.CarryApproach
                    or EPenguinControlState.Carrying)) return;

            _attackCooldownRemaining = _attackCooldownSeconds;
            _attackWarmupRemaining = _attackWarmupSeconds;
            _attackPending = true;
            _hitBodies.Clear();
            PlayAttackPose();
            AttackCount++;
        }

        /// <summary>공격 모션만 재생한다. <b>판정은 하지 않는다</b> — 타격은 서버가 소유하고
        /// 비권위 피어는 이것만 부른다(<see cref="PenguinNetAvatar.Render"/>).
        ///
        /// <para>클라이언트가 판정까지 하면 같은 타격이 피어 수만큼 들어간다. 루트 AGENTS 의
        /// "원인을 복제하고 결과는 각자 그린다" 에서, 여기서 복제되는 원인은 <b>공격했다는 사실</b>
        /// 이고 각 피어가 그리는 결과는 <b>모션</b>이다.</para></summary>
        public void PlayAttackPose()
        {
            if (_animator == null) return;
            ActivateActionsLayer();
            _animator.SetTrigger(AttackHash);
        }

        public void PlayDamage()
        {
            if (!CanAct) return;
            _attackPending = false;
            if (_animator != null)
            {
                ActivateActionsLayer();
                _animator.SetTrigger(DamageHash);
            }
            DamageCount++;
        }

        public void PlayHeavyImpact(float blendSeconds)
        {
            _attackPending = false;
            if (_animator != null && _animator.layerCount > ActionsLayerIndex)
            {
                // DeadPose는 Dead_Adelie의 86% 지점에 멈춘 전용 상태다. 물리가 이미 쓰러짐을
                // 만들었으므로 클립 처음을 재생하지 않고 현재 자세에서 누운 자세로 블렌딩한다.
                _animator.ResetTrigger(DeadHash);
                ActivateActionsLayer();
                _animator.CrossFadeInFixedTime(ActionsDeadPoseStateHash,
                    Mathf.Max(0f, blendSeconds), ActionsLayerIndex);
            }
            HeavyImpactCount++;
        }

        public void SetHeavyImpactPoseWeight(float weight01)
        {
            if (_animator == null || _animator.layerCount <= ActionsLayerIndex) return;
            _animator.SetLayerWeight(ActionsLayerIndex, Mathf.Clamp01(weight01));
        }

        public void ClearHeavyImpactPose()
        {
            if (_animator == null || _animator.layerCount <= ActionsLayerIndex) return;

            // 가중치 0에서 Empty를 즉시 샘플링해 DeadPose 전환을 확실히 폐기한다. 다음 일반
            // 액션은 ActivateActionsLayer가 다시 1로 올리므로 복구 후 누운 자세가 남지 않는다.
            _animator.SetLayerWeight(ActionsLayerIndex, 0f);
            _animator.Play(ActionsEmptyStateHash, ActionsLayerIndex, 0f);
            _animator.Update(0f);
        }

        private void ActivateActionsLayer()
        {
            if (_animator.layerCount <= ActionsLayerIndex) return;
            _animator.SetLayerWeight(ActionsLayerIndex, 1f);
        }

        private void ApplyAttackHit()
        {
            if (!CanAct) return;

            Vector3 center = transform.position + Vector3.up * 0.85f + transform.forward * _attackReachM;
            int count = Physics.OverlapSphereNonAlloc(center, _attackRadiusM, _attackHits,
                _attackLayers, QueryTriggerInteraction.Ignore);
            Vector3 impulseDirection = (transform.forward + Vector3.up * 0.15f).normalized;

            for (int i = 0; i < count; i++)
            {
                Collider hit = _attackHits[i];
                if (hit == null || hit.transform.IsChildOf(transform)) continue;

                Rigidbody body = hit.attachedRigidbody;
                if (body == null || !_hitBodies.Add(body)) continue;

                float impulseNs = _attackImpulseNs;
                if (body.GetComponentInParent<Gift>() != null)
                    impulseNs = Mathf.Min(impulseNs, body.mass * _giftAttackMaxDeltaVMps);
                Vector3 impulse = impulseDirection * impulseNs;
                Vector3 point = hit.ClosestPoint(center);
                body.AddForceAtPosition(impulse, point, ForceMode.Impulse);
                ImpactReceiver receiver = body.GetComponentInParent<ImpactReceiver>();
                if (receiver != null)
                    receiver.ReceiveImpact(new ImpactHit(EImpactCause.DirectAttack, impulse, point));
                AttackHitCount++;
            }
        }
    }
}
