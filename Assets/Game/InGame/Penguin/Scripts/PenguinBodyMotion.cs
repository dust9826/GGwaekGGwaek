using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 몸통 비주얼에 지면 경사·선회와 충돌 반응을 한 피벗에서 합성한다.
    ///
    /// <b><c>BodyPivot</c> 에 붙고 그 로컬 회전·위치만 쓴다.</b> 스케일은 Feel(<c>MMF_Player</c>)이
    /// 자식 <c>Mesh</c> 에서 쓰므로 여기서 건드리지 않는다 — <c>Vehicle/AGENTS.md</c> 가 확립한
    /// "채널 하나에 주인 하나" 규약이다. 우리가 <c>Mesh</c> 스케일을 덮으면
    /// <c>MMF_SquashAndStretch</c> 가 캡처한 <c>_initialScale</c> 과 어긋나 충돌마다 누적된다.
    ///
    /// <para>스프링 적분은 <see cref="VehicleBodyMotion"/> 과 같은 식이다. 검증된 것을 그대로
    /// 쓰는 편이 낫고, 두 캐릭터의 반응이 같은 문법을 갖는 것도 이득이다.</para>
    ///
    /// <para><b>가속 피치는 없다.</b> 활강 속도는 지면 경사·마찰·관성에서 나온다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PenguinBodyMotion : MonoBehaviour
    {
        [SerializeField] private PenguinLocomotion _locomotion;

        [Header("기울기 — 선회")]
        [Tooltip("기울기 절대 상한(도). 물리 계산이 이보다 크게 나와도 여기서 잘린다.")]
        [SerializeField, Range(0f, 45f)] private float _maxLeanDeg = 20f;

        [Tooltip("계산된 기울기에 곱하는 배율. 1 이면 물리 그대로, 크면 과장된다.")]
        [SerializeField, Min(0f)] private float _leanGain = 1f;

        [Header("지면")]
        [Tooltip("BodyPivot이 설면 법선을 따라 기울 수 있는 최대 각도(도). 물리 루트는 직립을 유지한다.")]
        [SerializeField, Range(0f, 60f)] private float _maxGroundTiltDeg = 30f;

        [Header("충돌 반응")]
        [SerializeField, Min(0f)] private float _impactKickScale = 0.25f;
        [SerializeField, Min(0f)] private float _maxBodyOffset = 0.4f;

        [Header("스프링")]
        [SerializeField, Min(0.01f)] private float _frequency = 9f;
        [SerializeField, Range(0f, 2f)] private float _damping = 0.7f;

        private float _targetLeanDeg;
        private float _leanDeg;
        private float _leanRate;
        private Vector3 _bodyOffset;
        private Vector3 _bodyOffsetVelocity;

        /// <summary>지금 적용 중인 기울기(도). 검증이 읽는다.</summary>
        public float LeanDeg => _leanDeg;

        /// <summary>지금 적용 중인 몸통 변위(m). 검증이 읽는다.</summary>
        public Vector3 BodyOffset => _bodyOffset;

        private void Reset()
        {
            _locomotion = GetComponentInParent<PenguinLocomotion>();
        }

        private void Awake()
        {
            if (_locomotion == null) _locomotion = GetComponentInParent<PenguinLocomotion>();
        }

        public void AddImpulse(float strength01, Vector3 worldDirection)
        {
            float scale = strength01 * _impactKickScale;
            if (scale <= 0f || worldDirection.sqrMagnitude < 0.0001f) return;

            Vector3 local = transform.parent == null
                ? worldDirection
                : transform.parent.InverseTransformDirection(worldDirection);
            _bodyOffsetVelocity += local.normalized * (scale * _frequency);
        }

        public void ResetPose()
        {
            _targetLeanDeg = 0f;
            _leanDeg = 0f;
            _leanRate = 0f;
            _bodyOffset = Vector3.zero;
            _bodyOffsetVelocity = Vector3.zero;
            transform.localPosition = Vector3.zero;
            transform.localRotation = Quaternion.identity;
        }

        private void LateUpdate()
        {
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            _targetLeanDeg = ComputeTargetLeanDeg();

            _leanDeg = Spring(_leanDeg, _targetLeanDeg, ref _leanRate, dt);
            _bodyOffset = Spring(_bodyOffset, Vector3.zero, ref _bodyOffsetVelocity, dt);
            _bodyOffset = Vector3.ClampMagnitude(_bodyOffset, _maxBodyOffset);
            Quaternion groundTilt = ComputeGroundTilt();
            // +Z 회전은 위가 왼쪽으로 가는 방향이다. 오른쪽으로 돌 때(양수 선회) 오른쪽으로
            // 기울어야 하므로 부호를 뒤집는다. 슬라이드 해제 뒤의 넘어짐은 BodyPivot 연출이
            // 아니라 루트 Rigidbody 회전이 직접 소유한다.
            Quaternion actionRotation = Quaternion.Euler(0f, 0f, -_leanDeg);
            transform.localRotation = groundTilt * actionRotation;
            transform.localPosition = _bodyOffset;
        }

        private Quaternion ComputeGroundTilt()
        {
            if (_locomotion == null || !_locomotion.UsesSlidingLocomotion || !_locomotion.IsGrounded)
                return Quaternion.identity;

            Vector3 localNormal = transform.parent == null
                ? _locomotion.GroundNormal
                : transform.parent.InverseTransformDirection(_locomotion.GroundNormal);
            Quaternion target = Quaternion.FromToRotation(Vector3.up, localNormal.normalized);
            return Quaternion.RotateTowards(Quaternion.identity, target, _maxGroundTiltDeg);
        }

        /// <summary>
        /// 기울기는 <b>실제로 적용된 그립력</b>에서 나온다 — 입력도, 운동학적 추정도 아니다.
        ///
        /// <code>
        /// lean = atan(LateralGripAccel / g)
        /// </code>
        ///
        /// <para><b>2026-08-22 Phase 5: <c>v·ω</c> 추정(원심가속도) 대신 <see
        /// cref="PenguinLocomotion.LateralGripAccel"/>을 읽는다.</b> 그 값은
        /// <c>TickSliding</c>이 실제로 낸 그립력을 가속도로 환산한 것이라, 그립이 낼 수 있는
        /// 최대치(<c>_lateralGripMu</c>)로 이미 잘려 있다 — <b>그립이 못 버텨 드리프트가 나는
        /// 순간 이 값도 같이 포화한다.</b> 옛 <c>v·ω</c>는 순수 운동학적 추정이라 그립 한계와
        /// 무관하게 계속 커질 수 있었다(핸들 상한 <c>_maxSteerDegPerSec</c>이 각속도만 제한할
        /// 뿐, 실제로 그 각속도를 유지할 그립이 있는지는 안 봤다). 이제 그림과 물리가 같은
        /// 값을 본다는 원칙이 더 정직해졌다.</para>
        ///
        /// <para><c>g</c> 는 실제 9.81 이다(<see cref="PenguinLocomotion.GravityMagnitude"/>).
        /// 저속 급선회에서는 물리적으로 더 크게 기우는 것이 맞지만 그림이 과해지므로
        /// <see cref="_maxLeanDeg"/> 로 자른다.</para>
        /// </summary>
        private float ComputeTargetLeanDeg()
        {
            if (_locomotion == null || !_locomotion.UsesSlidingLocomotion) return 0f;

            float g = _locomotion.GravityMagnitude;
            if (g <= 0.001f) return 0f;

            float deg = Mathf.Atan(_locomotion.LateralGripAccel / g) * Mathf.Rad2Deg * _leanGain;
            return Mathf.Clamp(deg, -_maxLeanDeg, _maxLeanDeg);
        }

        private float Spring(float current, float target, ref float rate, float dt)
        {
            float accel = (target - current) * (_frequency * _frequency)
                          - rate * (2f * _damping * _frequency);
            rate += accel * dt;
            return current + rate * dt;
        }

        private Vector3 Spring(Vector3 current, Vector3 target, ref Vector3 rate, float dt)
        {
            Vector3 accel = (target - current) * (_frequency * _frequency)
                            - rate * (2f * _damping * _frequency);
            rate += accel * dt;
            return current + rate * dt;
        }

    }
}
