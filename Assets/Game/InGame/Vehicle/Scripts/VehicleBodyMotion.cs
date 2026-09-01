using UnityEngine;
using UnityEngine.Serialization;

namespace PPack
{
    /// <summary>
    /// 차체 비주얼이 주행에 반응한다 — 가속에 피치, 드리프트에 롤, 충돌에 킥.
    ///
    /// <b>BodyPivot 에 붙고 그 로컬 회전·위치만 쓴다.</b> 스케일은 Feel(<c>MMF_Player</c>)이
    /// 자식 <c>Body</c> 에서 쓰므로 여기서 건드리지 않는다 — 채널 하나에 주인 하나다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleBodyMotion : MonoBehaviour
    {
        [SerializeField] private VehicleController _controller;
        [SerializeField] private Rigidbody _rigidbody;

        [Header("피치 — 가속·제동")]
        [SerializeField] private float _maxPitchDeg = 5f;

        /// <summary><c>VehicleController._accel</c> 과 같은 값이어야 한다.</summary>
        [SerializeField, Min(0.01f)] private float _accelReference = 20f;

        /// <summary><c>VehicleController._brakeDecel</c> 과 같은 값이어야 한다.</summary>
        [SerializeField, Min(0.01f)] private float _brakeReference = 30f;

        [Header("롤 — 드리프트")]
        [SerializeField] private float _maxRollDeg = 8f;

        /// <summary>실측된 드리프트 슬립각. <c>Vehicle/AGENTS.md</c> 참조.</summary>
        [SerializeField, Min(0.01f)] private float _slipReferenceDeg = 45f;

        [SerializeField, Min(0f)] private float _driftRollGain = 1.3f;

        // 아래 셋은 전부 "임펄스 크기"이지 변위가 아니다. 감쇠비 0.7 에서 실제 피크 변위는
        // 대략 scale × 0.42 다 (scale 0.25 → 피크 0.106 m 실측).
        [Header("반응 크기")]
        [Tooltip("충돌 킥의 임펄스 크기. 세기 1 의 정면 충돌 기준.")]
        [FormerlySerializedAs("_maxKickDistance")]
        [SerializeField, Min(0f)] private float _impactKickScale = 0.25f;

        [Tooltip("드리프트가 걸리는 순간 위로 튀는 임펄스 크기.")]
        [SerializeField, Min(0f)] private float _driftHopScale = 1f;

        [Tooltip("차체가 원점에서 벗어날 수 있는 절대 상한(m). 안전 레일이지 튜닝 값이 아니다.")]
        [SerializeField, Min(0f)] private float _maxBodyOffset = 0.6f;

        [Header("스프링")]
        [SerializeField, Min(0.01f)] private float _frequency = 9f;
        [SerializeField, Range(0f, 2f)] private float _damping = 0.7f;

        private Vector3 _previousVelocity;
        private float _targetPitchDeg;
        private float _targetRollDeg;
        private bool _wasDrifting;

        private float _pitchDeg;
        private float _pitchRate;
        private float _rollDeg;
        private float _rollRate;
        private Vector3 _kick;
        private Vector3 _kickRate;

        /// <summary>지금 적용 중인 값. 검증이 읽는다.</summary>
        public float PitchDeg => _pitchDeg;

        /// <summary>지금 적용 중인 값. 검증이 읽는다.</summary>
        public float RollDeg => _rollDeg;

        /// <summary>
        /// 차체를 들어 올릴 지속 오프셋(m). 눈 위 승차 높이를 눈 쪽이 밀어 넣는다.
        ///
        /// <b>임펄스가 아니다.</b> <see cref="_kick"/> 은 0 으로 돌아가는 스프링이라 지속값을 담을 수
        /// 없다. 스무딩은 넣는 쪽이 이미 끝내고 오므로 여기서는 더하기만 한다 — 두 번 감쇠시키면
        /// 눈에서 나오는 순간이 뭉개진다.
        /// </summary>
        public float RideOffsetY { get; set; }

        /// <summary>짐이 만드는 피치(도). 가속 피치에 더해진다. 접지가 아니면 다른 항과 같이 0 이 된다.</summary>
        public float LoadPitchDeg { get; set; }

        private void Reset()
        {
            _rigidbody = GetComponentInParent<Rigidbody>();
            _controller = GetComponentInParent<VehicleController>();
        }

        private void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponentInParent<Rigidbody>();
            if (_controller == null) _controller = GetComponentInParent<VehicleController>();
            _previousVelocity = _rigidbody != null ? _rigidbody.linearVelocity : Vector3.zero;
        }

        /// <summary>충돌이 준 한 방.</summary>
        /// <param name="strength01">0~1 로 정규화된 세기.</param>
        /// <param name="worldDirection">차체가 밀려야 하는 월드 방향.</param>
        public void AddImpulse(float strength01, Vector3 worldDirection)
        {
            AddScaledImpulse(strength01 * _impactKickScale, worldDirection);
        }

        /// <summary>스프링에 임펄스를 직접 넣는다. 충돌·드리프트가 서로 다른 크기를 쓰므로
        /// 크기를 정하는 것은 부르는 쪽이다.</summary>
        private void AddScaledImpulse(float scale, Vector3 worldDirection)
        {
            if (scale <= 0f || worldDirection.sqrMagnitude < 0.0001f) return;

            Vector3 local = transform.parent == null
                ? worldDirection
                : transform.parent.InverseTransformDirection(worldDirection);

            _kickRate += local.normalized * (scale * _frequency);
        }

        private void FixedUpdate()
        {
            if (_rigidbody == null) return;

            Vector3 velocity = _rigidbody.linearVelocity;
            Transform root = _rigidbody.transform;
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);

            float forwardSpeed = Vector3.Dot(planar, root.forward);
            float previousForwardSpeed = Vector3.Dot(_previousVelocity, root.forward);
            float lateralSpeed = Vector3.Dot(planar, root.right);

            // 충돌 해소가 넣은 속도는 모델의 rate 셋과 무관하게 한 스텝에 크게 튄다. 자르지
            // 않으면 벽에 닿는 순간 피치가 통째로 꺾여, VehicleImpactRelay 의 킥과 같은 사건에
            // 두 번 반응한다.
            float accel = (forwardSpeed - previousForwardSpeed) / Time.fixedDeltaTime;
            accel = Mathf.Clamp(accel, -_brakeReference, _accelReference);
            float accel01 = accel >= 0f ? accel / _accelReference : accel / _brakeReference;

            // +X 회전은 코가 내려가는 방향이다. 가속하면 앞이 들려야 하므로 부호를 뒤집는다.
            _targetPitchDeg = -_maxPitchDeg * accel01 + LoadPitchDeg;

            // VehicleController.SlipAngle 은 Vector3.Angle 이라 부호가 없다. 어느 쪽으로
            // 미끄러지는지를 모르면 롤 방향을 정할 수 없으므로 여기서 직접 구한다.
            float signedSlipDeg = Mathf.Atan2(lateralSpeed, Mathf.Abs(forwardSpeed)) * Mathf.Rad2Deg;
            float slip01 = Mathf.Clamp(signedSlipDeg / _slipReferenceDeg, -1f, 1f);
            float gain = _controller != null && _controller.IsDrifting ? _driftRollGain : 1f;

            // 바깥으로 기운다. +Z 회전은 위가 왼쪽으로 가는 방향이므로, 오른쪽으로 미끄러질 때
            // (slip01 > 0) 음수여야 오른쪽으로 기운다.
            _targetRollDeg = -_maxRollDeg * slip01 * gain;

            bool grounded = _controller == null || _controller.IsGrounded;
            if (!grounded)
            {
                _targetPitchDeg = 0f;
                _targetRollDeg = 0f;
            }

            // 드리프트가 걸리는 순간 한 번 위로 튄다. Feel 로 하지 않는 것은 이 채널
            // (BodyPivot.localPosition)을 이 스크립트가 소유하기 때문이다 — MMF_Position 을
            // 얹으면 주인이 둘이 된다.
            bool drifting = _controller != null && _controller.IsDrifting;
            if (drifting && !_wasDrifting) AddScaledImpulse(_driftHopScale, Vector3.up);
            _wasDrifting = drifting;

            _previousVelocity = velocity;
        }

        private void LateUpdate()
        {
            // 물리는 50Hz 인데 화면은 훨씬 빠르다(이 프로젝트 실측 451fps). 물리 값을 그대로
            // 쓰면 한 값을 아홉 프레임 붙잡고 있다가 튄다 — 스프링은 프레임률로 적분한다.
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            _pitchDeg = Spring(_pitchDeg, _targetPitchDeg, ref _pitchRate, dt);
            _rollDeg = Spring(_rollDeg, _targetRollDeg, ref _rollRate, dt);
            _kick = Spring(_kick, Vector3.zero, ref _kickRate, dt);
            _kick = Vector3.ClampMagnitude(_kick, _maxBodyOffset);

            transform.localRotation = Quaternion.Euler(_pitchDeg, 0f, _rollDeg);
            transform.localPosition = _kick + new Vector3(0f, Mathf.Clamp(RideOffsetY, 0f, _maxBodyOffset), 0f);
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
