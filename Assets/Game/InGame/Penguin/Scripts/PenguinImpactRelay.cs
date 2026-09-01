using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 실제 충돌을 몸통 킥과 스쿼시 연출로 넘긴다.
    ///
    /// <b>2026-08-22 Phase 4: <c>VehicleImpactRelay</c>와 같은 방식이 됐다.</b> CC 시절엔
    /// 충돌 콜백 자체를 못 받아서 "의도한 속도와 실제로 난 속도의 차이"로 충돌을 추정해야
    /// 했다(부딪힌 것 vs 경사·마찰로 정상 감속한 것을 구분하기 어려운 방식이었다). 진짜
    /// <c>Rigidbody</c>는 <c>OnCollisionEnter</c>와 <c>collision.relativeVelocity</c>를 그대로
    /// 주므로, 접촉 법선 방향의 상대속도(닫히는 속도)만 재면 된다 — 훨씬 정직하다.
    ///
    /// <b>속도 감쇠 로직(옛 <c>ApplyImpactBrake</c>)은 통째로 없앴다.</b> CC는 부딪혀도 속도
    /// 장부가 안 줄어서 벽에 붙었다 떨어지면 저장된 속도로 튀어나가는 결함이 있었는데, 진짜
    /// 충돌은 물리 자체가 속도를 깎는다 — 결함이 애초에 성립하지 않으므로 고칠 코드도 없다.
    /// 이 컴포넌트는 속도를 손으로 고치지 않고 닫히는 속도를 연출 세기로만 쓴다.
    ///
    /// <b>쿨다운이 없다.</b> 옛 버전은 매 프레임 검사하는 방식이라 벽에 붙어 있는 동안 계속
    /// 터지지 않게 쿨다운이 필요했지만, <c>OnCollisionEnter</c>는 접촉이 새로 시작할 때만
    /// 한 번 불린다 — <c>VehicleImpactRelay</c>와 동일.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PenguinImpactRelay : ImpactReceiver
    {
        [SerializeField] private PenguinBodyMotion _bodyMotion;
        [SerializeField] private PenguinActions _actions;
        [SerializeField] private PenguinLocomotion _locomotion;
        [SerializeField] private PenguinSnowball _snowball;
        [SerializeField] private PenguinCarry _carry;
        [SerializeField] private PenguinAnimatorDriver _animatorDriver;

        [Tooltip("충돌 스쿼시를 소유한 MMF_Player. 한 트랜스폼의 스케일에는 주인을 하나만 둔다.")]
        [SerializeField] private MMF_Player _impactFeedback;

        [Header("문턱 (접촉 법선 방향 상대속도, m/s)")]
        [SerializeField, Min(0f)] private float _minImpactSpeed = 2.5f;
        [Tooltip("반응이 최대가 되는 속도. 평지 최고속(9.2)에 맞춰 두면 전력 슬라이딩 중 정면 충돌이 1 이 된다.")]
        [SerializeField, Min(0.1f)] private float _maxImpactSpeed = 9f;
        [SerializeField, Min(0f)] private float _damageImpactSpeed = 4f;
        [SerializeField, Min(0f)] private float _heavyImpactSpeed = 6f;
        [SerializeField, Min(0f)] private float _heavyLandingSpeed = 8f;

        [Header("반응")]
        [Tooltip("세게 부딪혔을 때의 스쿼시 배율. 1 이면 안 눌리고 낮을수록 세게 눌린다.")]
        [SerializeField, Range(0.5f, 1f)] private float _squashRemapAtFullImpact = 0.85f;

        [Header("큰 충격 회복")]
        [Tooltip("지상에서 충격을 받은 직후 같은 지면을 착지로 오인하지 않는 시간.")]
        [SerializeField, Min(0f)] private float _landingDetectionDelaySeconds = 0.12f;
        [Tooltip("물리 착지 자세에서 Dead 누운 자세로 이어지는 시간.")]
        [SerializeField, Min(0.05f)] private float _landingBlendSeconds = 0.28f;
        [SerializeField, Min(0f)] private float _deadPoseHoldSeconds = 2f;
        [SerializeField, Min(0.05f)] private float _getUpBlendSeconds = 0.45f;
        [SerializeField, Min(0.05f)] private float _groundProbeExtraM = 0.15f;

        private MMF_SquashAndStretch _squash;
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];
        private Rigidbody _body;
        private CapsuleCollider _capsule;
        private Animator _animator;
        private RigidbodyConstraints _normalConstraints;
        private EHeavyImpactPhase _heavyImpactPhase;
        private float _phaseElapsedSeconds;
        private bool _locomotionWasEnabled;
        private bool _snowballWasEnabled;
        private bool _animatorDriverWasEnabled;
        private bool _bodyMotionWasEnabled;
        private bool _actionsCouldAct;
        private Vector3 _landingPivotStartLocalPosition;
        private Quaternion _landingPivotStartLocalRotation;

        private static readonly int BaseLocomotionStateHash = Animator.StringToHash("Base Locomotion.Locomotion");
        private static readonly int SpeedHash = Animator.StringToHash("Speed");
        private static readonly int IsSlidingHash = Animator.StringToHash("IsSliding");
        private const int BaseLayerIndex = 0;
        private const int ActionsLayerIndex = 1;

        private enum EHeavyImpactPhase
        {
            None,
            Airborne,
            LandingBlend,
            HoldingDead,
            GettingUp
        }

        /// <summary>마지막으로 잡은 세기(0~1). 검증이 읽는다.</summary>
        public float LastImpactStrength01 { get; private set; }

        /// <summary>발동 횟수. 검증이 읽는다.</summary>
        public int ImpactCount { get; private set; }

        public bool IsHeavyImpactActive => _heavyImpactPhase != EHeavyImpactPhase.None;
        public bool HasLandedFromHeavyImpact => _heavyImpactPhase is EHeavyImpactPhase.LandingBlend
            or EHeavyImpactPhase.HoldingDead or EHeavyImpactPhase.GettingUp;
        public bool IsBlendingToDeadPose => _heavyImpactPhase == EHeavyImpactPhase.LandingBlend;

        /// <summary>운반물이 등에 안착한 순간, 실제 질량에서 만든 세기로 기존 Feel 스쿼시를 재생한다.</summary>
        public void PlayCarryLoad(float strength01)
        {
            PlayCarryScaleFeedback(strength01, 0.78f);
        }

        /// <summary>운반물을 내려놓은 순간 몸이 기준 크기보다 잠깐 크게 펴지는 Feel을 재생한다.</summary>
        public void PlayCarryRelease(float strength01)
        {
            PlayCarryScaleFeedback(strength01, 1.16f);
        }

        private void Reset()
        {
            _bodyMotion = GetComponentInChildren<PenguinBodyMotion>();
            _impactFeedback = GetComponentInChildren<MMF_Player>();
            _actions = GetComponent<PenguinActions>();
            _locomotion = GetComponent<PenguinLocomotion>();
            _snowball = GetComponent<PenguinSnowball>();
            _carry = GetComponent<PenguinCarry>();
            _animatorDriver = GetComponentInChildren<PenguinAnimatorDriver>(true);
        }

        private void Awake()
        {
            if (_impactFeedback != null) _squash = _impactFeedback.GetFeedbackOfType<MMF_SquashAndStretch>();
            if (_actions == null) _actions = GetComponent<PenguinActions>();
            if (_locomotion == null) _locomotion = GetComponent<PenguinLocomotion>();
            if (_snowball == null) _snowball = GetComponent<PenguinSnowball>();
            if (_carry == null) _carry = GetComponent<PenguinCarry>();
            if (_animatorDriver == null)
                _animatorDriver = GetComponentInChildren<PenguinAnimatorDriver>(true);
            _body = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _animator = GetComponentInChildren<Animator>(true);
        }

        private void FixedUpdate()
        {
            if (_heavyImpactPhase != EHeavyImpactPhase.Airborne) return;

            _phaseElapsedSeconds += Time.fixedDeltaTime;
            if (_phaseElapsedSeconds < _landingDetectionDelaySeconds) return;
            if (!TryFindGround(out RaycastHit ground)) return;

            LandFromHeavyImpact(ground.point.y);
        }

        private void Update()
        {
            switch (_heavyImpactPhase)
            {
                case EHeavyImpactPhase.LandingBlend:
                    _phaseElapsedSeconds += Time.deltaTime;
                    break;
                case EHeavyImpactPhase.HoldingDead:
                    _phaseElapsedSeconds += Time.deltaTime;
                    if (_phaseElapsedSeconds >= _deadPoseHoldSeconds) BeginGettingUp();
                    break;
                case EHeavyImpactPhase.GettingUp:
                    _phaseElapsedSeconds += Time.deltaTime;
                    break;
            }
        }

        private void LateUpdate()
        {
            if (_heavyImpactPhase == EHeavyImpactPhase.GettingUp)
            {
                float getUpT = Mathf.Clamp01(_phaseElapsedSeconds /
                                              Mathf.Max(0.01f, _getUpBlendSeconds));
                float getUpEased = getUpT * getUpT * (3f - 2f * getUpT);
                if (_actions != null) _actions.SetHeavyImpactPoseWeight(1f - getUpEased);
                if (getUpT >= 1f) FinishHeavyImpact();
                return;
            }

            if (_heavyImpactPhase != EHeavyImpactPhase.LandingBlend) return;

            float t = Mathf.Clamp01(_phaseElapsedSeconds / Mathf.Max(0.01f, _landingBlendSeconds));
            float eased = t * t * (3f - 2f * t);
            if (_bodyMotion != null)
            {
                Transform pivot = _bodyMotion.transform;
                pivot.localPosition = Vector3.Lerp(_landingPivotStartLocalPosition,
                    Vector3.zero, eased);
                pivot.localRotation = Quaternion.Slerp(_landingPivotStartLocalRotation,
                    Quaternion.identity, eased);
            }

            if (t >= 1f) BeginHoldingDead();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (IsHeavyImpactActive) return;
            if (collision.contactCount == 0) return;

            ContactPoint contact = collision.GetContact(0);

            float closingSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            float equivalentDeltaV = collision.impulse.magnitude / Mathf.Max(0.01f,
                GetComponent<Rigidbody>().mass);
            float effectiveSpeed = Mathf.Min(closingSpeed, equivalentDeltaV);
            bool landing = Mathf.Abs(contact.normal.y) > 0.7f;
            if (landing && effectiveSpeed < _heavyLandingSpeed) return;

            HandleImpact(effectiveSpeed, collision.impulse, contact.point, -contact.normal);
        }

        public void ReceiveExternalImpulse(Vector3 impulse, Vector3 point)
        {
            ReceiveImpact(new ImpactHit(EImpactCause.ExternalImpulse, impulse, point));
        }

        public override void ReceiveImpact(ImpactHit hit)
        {
            Rigidbody body = GetComponent<Rigidbody>();
            float effectiveSpeed = hit.Impulse.magnitude / Mathf.Max(0.01f, body.mass);
            Vector3 direction = hit.Impulse.sqrMagnitude > 0.0001f
                ? hit.Impulse.normalized : transform.forward;
            HandleImpact(effectiveSpeed, hit.Impulse, hit.Point, direction);
        }

        private void HandleImpact(float effectiveSpeed, Vector3 impulse, Vector3 point,
            Vector3 visualDirection)
        {
            if (IsHeavyImpactActive) return;
            if (effectiveSpeed < _minImpactSpeed) return;

            float strength01 = Mathf.InverseLerp(_minImpactSpeed, _maxImpactSpeed, effectiveSpeed);
            LastImpactStrength01 = strength01;
            ImpactCount++;

            if (effectiveSpeed < _heavyImpactSpeed && _bodyMotion != null)
                _bodyMotion.AddImpulse(strength01, visualDirection);
            if (effectiveSpeed >= _heavyImpactSpeed) BeginHeavyImpact();
            else if (effectiveSpeed >= _damageImpactSpeed && _actions != null) _actions.PlayDamage();

            // 세기를 PlayFeedbacks 의 intensity 인자로 넘기면 안 된다. 그 값은 RemapCurveOne 에
            // 곱해지는데(MMF_SquashAndStretch.cs:175) 배율이 1 미만이라 곱할수록 0 에 가까워져
            // 약한 충돌이 오히려 더 세게 눌린다. 배율 자체를 보간해야 비례한다.
            if (_squash != null) _squash.RemapCurveOne = Mathf.Lerp(1f, _squashRemapAtFullImpact, strength01);
            if (_impactFeedback != null) _impactFeedback.PlayFeedbacks();
        }

        private void PlayCarryScaleFeedback(float strength01, float remapAtFullLoad)
        {
            strength01 = Mathf.Clamp01(strength01);
            if (_squash != null)
                _squash.RemapCurveOne = Mathf.Lerp(1f, remapAtFullLoad, strength01);
            if (_impactFeedback != null) _impactFeedback.PlayFeedbacks();
        }

        private void BeginHeavyImpact()
        {
            if (_body == null) return;

            _normalConstraints = _body.constraints;
            _locomotionWasEnabled = _locomotion != null && _locomotion.enabled;
            _snowballWasEnabled = _snowball != null && _snowball.enabled;
            _animatorDriverWasEnabled = _animatorDriver != null && _animatorDriver.enabled;
            _bodyMotionWasEnabled = _bodyMotion != null && _bodyMotion.enabled;
            _actionsCouldAct = _actions != null && _actions.CanAct;

            if (_carry != null) _carry.ForceRelease();

            if (_snowball != null)
            {
                _snowball.Release();
                _snowball.enabled = false;
            }
            if (_locomotion != null) _locomotion.enabled = false;
            if (_animatorDriver != null) _animatorDriver.enabled = false;
            if (_bodyMotion != null) _bodyMotion.enabled = false;
            if (_actions != null) _actions.CanAct = false;

            if (_animator != null)
            {
                _animator.speed = 1f;
                _animator.SetFloat(SpeedHash, 0f);
                _animator.SetBool(IsSlidingHash, false);
            }

            _body.constraints = RigidbodyConstraints.None;
            _heavyImpactPhase = EHeavyImpactPhase.Airborne;
            _phaseElapsedSeconds = 0f;
        }

        private void LandFromHeavyImpact(float groundY)
        {
            Vector3 pivotWorldPosition = default;
            Quaternion pivotWorldRotation = Quaternion.identity;
            if (_bodyMotion != null)
            {
                pivotWorldPosition = _bodyMotion.transform.position;
                pivotWorldRotation = _bodyMotion.transform.rotation;
            }

            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f)
                forward = Vector3.ProjectOnPlane(transform.up, Vector3.up);
            if (forward.sqrMagnitude <= 0.0001f) forward = Vector3.forward;

            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.position = new Vector3(_body.position.x, groundY + 0.01f, _body.position.z);
            _body.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
            _body.constraints = RigidbodyConstraints.FreezeAll;

            if (_bodyMotion != null)
            {
                Transform pivot = _bodyMotion.transform;
                pivot.SetPositionAndRotation(pivotWorldPosition, pivotWorldRotation);
                _landingPivotStartLocalPosition = pivot.localPosition;
                _landingPivotStartLocalRotation = pivot.localRotation;
            }

            if (_actions != null) _actions.PlayHeavyImpact(_landingBlendSeconds);
            _heavyImpactPhase = EHeavyImpactPhase.LandingBlend;
            _phaseElapsedSeconds = 0f;
        }

        private void BeginHoldingDead()
        {
            if (_bodyMotion != null)
            {
                _bodyMotion.transform.localPosition = Vector3.zero;
                _bodyMotion.transform.localRotation = Quaternion.identity;
            }
            _heavyImpactPhase = EHeavyImpactPhase.HoldingDead;
            _phaseElapsedSeconds = 0f;
        }

        private void BeginGettingUp()
        {
            if (_animator != null)
            {
                _animator.speed = 1f;
                if (_animator.layerCount > BaseLayerIndex)
                    _animator.Play(BaseLocomotionStateHash, BaseLayerIndex, 0f);
            }

            _heavyImpactPhase = EHeavyImpactPhase.GettingUp;
            _phaseElapsedSeconds = 0f;
        }

        private void FinishHeavyImpact()
        {
            if (_body != null)
            {
                _body.linearVelocity = Vector3.zero;
                _body.angularVelocity = Vector3.zero;
                _body.constraints = _normalConstraints;
            }
            if (_actions != null) _actions.CanAct = _actionsCouldAct;
            if (_actions != null) _actions.ClearHeavyImpactPose();
            if (_locomotion != null) _locomotion.enabled = _locomotionWasEnabled;
            if (_snowball != null) _snowball.enabled = _snowballWasEnabled;
            if (_animatorDriver != null) _animatorDriver.enabled = _animatorDriverWasEnabled;
            if (_bodyMotion != null)
            {
                _bodyMotion.ResetPose();
                _bodyMotion.enabled = _bodyMotionWasEnabled;
            }

            _heavyImpactPhase = EHeavyImpactPhase.None;
            _phaseElapsedSeconds = 0f;
        }

        private bool TryFindGround(out RaycastHit ground)
        {
            ground = default;
            if (_capsule == null) return false;

            Bounds bounds = _capsule.bounds;
            int count = Physics.RaycastNonAlloc(bounds.center, Vector3.down, _groundHits,
                bounds.extents.y + _groundProbeExtraM, ~0, QueryTriggerInteraction.Ignore);
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < count; index++)
            {
                RaycastHit candidate = _groundHits[index];
                if (candidate.collider == null || candidate.collider.transform.IsChildOf(transform))
                    continue;
                if (candidate.rigidbody != null || candidate.normal.y < 0.5f) continue;
                if (candidate.distance >= nearestDistance) continue;

                nearestDistance = candidate.distance;
                ground = candidate;
            }

            return nearestDistance < float.PositiveInfinity;
        }
    }
}
