using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Rigidbody 기반 아케이드 차량 이동. WheelCollider 대신 직접 속도 제어를 쓴 이유는 튜닝
    /// 속도 — 물리 관절을 맞추지 않고도 "캐주얼한 감각"을 값 몇 개로 바로 조정할 수 있다.
    ///
    /// <b>속도를 덮어쓰지 않고 정면/횡으로 분해해 각각 다른 마찰을 먹인 뒤 되돌려쓴다.</b>
    /// 횡 성분을 버리지 않고 감쇠시키는 것이 이 클래스의 전부다. 거기서 드리프트(몸이 돌아도
    /// 속도가 안 따라온다)와 벽 튕김(충돌이 준 횡속도가 살아남는다)과 소품 밀기가 함께 나온다.
    /// 이전 버전은 같은 자리에서 <c>linearVelocity</c> 를 통째로 덮어써 횡속도를 0 으로 만들었고,
    /// 그래서 그립이 무한대라 안 미끄러지고 벽에 부딪혀도 안 튕겼다.
    ///
    /// 조향 각속도와 그립은 속도의 함수다. 속도 0 에서도 각속도가 살아 있으므로 제자리 회전이
    /// 분기 없이 나온다 — 레이싱에는 없는 예외지만 바닥을 덮어야 하는 청소 도구라 없으면
    /// 구석을 못 닦는다.
    ///
    /// <b>드리프트는 상시가 아니라 홀드 키다.</b> 평소 그립은 조향이 만드는 횡속도보다 높아
    /// 슬립이 안 쌓이고 그냥 커브가 돈다. 하나의 그립 곡선이 저속 정밀 조작과 고속 드리프트를
    /// 동시에 정하면 둘 중 하나는 반드시 포기해야 하는데, 청소는 저속 끝에서 일어나고 재미는
    /// 고속 끝에서 일어나므로 어느 쪽도 버릴 수 없었다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class VehicleController : MonoBehaviour
    {
        [SerializeField] private VehicleInput _input;
        [SerializeField] private Rigidbody _rigidbody;

        [Header("접지 판정")]
        [SerializeField] private float _groundCheckUp = 0.5f;
        [SerializeField] private float _groundCheckDistance = 0.8f;
        [SerializeField] private LayerMask _groundLayers = ~0;

        [Header("속도 (m/s, m/s²)")]
        [Tooltip("Shift를 놓았을 때의 최고 속도. 주행 곡선은 전부 이 값을 기준으로 정규화한다.")]
        [SerializeField, Min(0.1f)] private float _baseMaxSpeed = 12f;
        [Tooltip("Shift를 눌렀을 때의 최고 속도.")]
        [SerializeField, Min(0.1f)] private float _boostMaxSpeed = 16f;
        [Tooltip("후진이 느려야 전진이 빨라 보인다.")]
        [SerializeField, Min(0.1f)] private float _reverseMaxSpeed = 4f;
        [Tooltip("0 → 기본 최고속 0.6 초.")]
        [SerializeField, Min(0f)] private float _accel = 20f;
        [Tooltip("반대 입력. 코스팅보다 확실히 빨라야 브레이크로 읽힌다.")]
        [SerializeField, Min(0f)] private float _brakeDecel = 30f;
        [Tooltip("무입력. 손 떼면 1 초간 미끄러져 멈춘다.")]
        [SerializeField, Min(0f)] private float _coastDecel = 12f;

        [Header("조향 (도/초) — 속도 0 → 기본 최고속")]
        [Tooltip("속도 0 에서의 각속도. 이 값이 곧 제자리 회전 속도다.")]
        [SerializeField, Min(0f)] private float _turnRateAtRest = 120f;
        [Tooltip("기본 최고속에서의 각속도. 낮을수록 무겁고 차 같다. 슬립이 0 이라 선회 반경이 " +
                 "v/ω 로 정확히 나온다 — 12 m/s 에서 50°/s 면 13.8 m.")]
        [SerializeField, Min(0f)] private float _turnRateAtTopSpeed = 50f;
        [Tooltip("공중에서 조향이 약해지는 비율.")]
        [SerializeField, Range(0f, 1f)] private float _airSteerFactor = 0.35f;

        [Header("횡 그립 (m/s²) — 속도 0 → 기본 최고속")]
        [Tooltip("초당 죽이는 횡속도. 낮을수록 크게 미끄러진다. 튜닝 1순위.")]
        [SerializeField, Min(0f)] private float _gripAtRest = 40f;
        [Tooltip("조향이 만드는 횡속도(forwardSpeed·ω)보다 확실히 **높아야** 안 미끄러진다 — " +
                 "12 m/s·50°/s 면 그 값이 약 10.5 다. 여유를 두고 22.")]
        [SerializeField, Min(0f)] private float _gripAtTopSpeed = 22f;

        [Header("드리프트 (Space / 패드 RB 홀드)")]
        [Tooltip("끄면 드리프트 키가 통째로 죽고 평소 그립만 남는다.")]
        [SerializeField] private bool _driftEnabled = true;
        [Tooltip("드리프트 중 최고속 그립. 평소 값 대신 이쪽으로 보간한다.")]
        [SerializeField, Min(0f)] private float _driftGripAtTopSpeed = 5f;
        [Tooltip("드리프트 중 최고속 각속도. 평소 값보다 높아야 '더 꺾인다'로 읽힌다.")]
        [SerializeField, Min(0f)] private float _driftTurnRateAtTopSpeed = 85f;
        [Tooltip("이 속도 미만에서는 키를 눌러도 안 걸린다. 제자리 회전과 섞이지 않게 하는 문턱.")]
        [SerializeField, Range(0f, 1f)] private float _driftMinSpeed01 = 0.3f;

        /// <summary>이 모델이 소유하는 요. 충돌이 몸을 돌려도 여기는 안 바뀐다 — 아케이드에서는
        /// 스치기만 해도 차가 팽이처럼 도는 쪽이 더 나쁘다.</summary>
        private float _yaw;

        public bool IsGrounded { get; private set; }

        /// <summary>0 ~ 1 로 정규화한 평면 속력. <b>부스트 최고속이 1 이다.</b>
        ///
        /// 카메라·노즐·진공이 전부 이 값을 구독하는 연출용 단일 소스다. 총 속력이므로 드리프트
        /// 중에는 정면 속도보다 크다 — 몸이 실제로 내는 속력을 따라가야 하는 쪽이라 그게 맞다.
        ///
        /// 이전 버전은 <c>InverseLerp(기본최고속, 부스트최고속, speed)</c> 라 <b>Shift 를 누르기
        /// 전에는 항상 0</b> 이었다. 구독자 셋의 속도 연출이 통째로 죽어 있었다는 뜻이고, 그래서
        /// 원뿔 길이·각도와 노즐 자유각은 사실상 저속 끝값 하나로만 돌았다. 이제 주행 내내
        /// 살아나므로 그 값들은 다시 봐야 한다.</summary>
        public float CurrentSpeed01 { get; private set; }

        /// <summary>진행 방향과 정면이 벌어진 각도(도). 드리프트 연출은 이걸 읽는다.</summary>
        public float SlipAngle { get; private set; }

        /// <summary>드리프트 키가 실제로 먹은 스텝인지. 키를 눌러도 문턱 속도 미만이면 false.</summary>
        public bool IsDrifting { get; private set; }

        /// <summary>
        /// 지면이 정면 최고속에 먹이는 배율. 1 이 평지다. 지면 쪽 컴포넌트가 매 스텝 써넣는다 —
        /// 지금 쓰는 것은 <c>InGame/Snow</c> 의 <c>SnowVehicleDrag</c> 이고, 바퀴가 눈에 잠긴
        /// 비율(0~4/4)로 낮춘다.
        ///
        /// <b>여기가 지면이 차를 늦추는 유일한 접점이다.</b> 지형이 늘어도 이 값 하나를 나눠 쓴다 —
        /// 소비자마다 최고속을 따로 만지면 어느 쪽이 이겼는지 알 수 없게 된다.
        /// </summary>
        public float GroundSpeedFactor { get; set; } = 1f;

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

            // 요는 솔버가 돌리고 X·Z 만 얼린다. 몸이 서 있어야 한다는 전제는 그대로지만,
            // Y 까지 얼리면 회전을 MoveRotation 으로 쓸 수밖에 없고 그러면 보간이 죽는다 —
            // Drive() 의 각속도 주석 참조. 프리팹 설정에 맡기면 잘못 세팅됐을 때 조용히
            // 깨지므로 여기서 못 박는다.
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
            _yaw = transform.eulerAngles.y;

            // 벽에 박혔을 때 차체가 떠는 것을 막는다. 이 모델은 횡속도를 버리지 않고 보존하는데,
            // 벽에 닿으면 PhysX 가 겹침을 푸느라 넣어주는 속도까지 같이 보존돼 튕겨나갔다 다시
            // 박히기를 반복한다. 기본값은 아주 커서 한 번에 크게 밀어낸다.
            _rigidbody.maxDepenetrationVelocity = 3f;
        }

        /// <summary>
        /// 차량을 새 위치로 되돌리고 이전 주행의 선형·각속도를 모두 버린다. 트랜스폼만 옮기면
        /// 내부 목표 각도 <see cref="_yaw"/>가 이전 방향을 계속 가리켜 다음 물리 스텝에 차가
        /// 원래 방향으로 돌아가므로, 리스폰 회전과 함께 갱신해야 한다.
        /// </summary>
        public void RespawnAt(Vector3 position, Quaternion rotation)
        {
            _yaw = rotation.eulerAngles.y;
            _rigidbody.position = position;
            _rigidbody.rotation = rotation;
            _rigidbody.linearVelocity = Vector3.zero;
            _rigidbody.angularVelocity = Vector3.zero;
            CurrentSpeed01 = 0f;
            SlipAngle = 0f;
            IsDrifting = false;
        }

        private void FixedUpdate()
        {
            if (_input == null) return;

            CheckGrounded();
            Drive();
        }

        private void CheckGrounded()
        {
            Vector3 origin = transform.position + Vector3.up * _groundCheckUp;
            IsGrounded = Physics.Raycast(origin, Vector3.down, _groundCheckUp + _groundCheckDistance, _groundLayers);
        }

        private void Drive()
        {
            Vector2 move = _input.Move;
            float dt = Time.fixedDeltaTime;

            Vector3 velocity = _rigidbody.linearVelocity;
            float vertical = velocity.y;                       // 중력·충돌의 수직 성분은 물리에 맡긴다
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);

            CurrentSpeed01 = Mathf.Clamp01(planar.magnitude / _boostMaxSpeed);

            // 주행 모델이 보는 속도는 총 속력이 아니라 **정면 성분**이다. 둘은 다른 값이고,
            // 하나로 합쳐두면 드리프트가 스스로 커진다.
            //
            // planar 에는 횡 성분이 섞여 있어 미끄러지는 동안 실제 전진 속도보다 크다. 그 부푼
            // 값으로 그립과 각속도를 보간하면 둘 다 낮은 쪽으로 끌려가, 한 번 밀리기 시작하면
            // 더 안 잡히고 더 안 도는 양의 피드백이 된다 — "미끄럽다"와 "도는 반응이 늦다"가
            // 같은 원인에서 나온다.
            //
            // 기준이 부스트 최고속이 아니라 **기본 최고속**인 것도 의도다. 부스트는 곡선을 더
            // 밀지 않고 속도만 올린다 — 선회 반경이 v/ω 라 각속도가 그대로여도 저절로 넓어진다.
            Vector3 heading = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            float drive01 = Mathf.Clamp01(Mathf.Abs(Vector3.Dot(planar, heading)) / _baseMaxSpeed);

            // 문턱은 drive01 이 아니라 CurrentSpeed01 로 잰다. drive01 은 드리프트가 깊어질수록
            // 떨어지므로, 그걸로 게이트를 걸면 드리프트 도중에 스스로 풀렸다 걸렸다 한다.
            IsDrifting = _driftEnabled && _input.DriftHeld && CurrentSpeed01 >= _driftMinSpeed01;

            // 조향. 목표 각은 모델이 적분해서 소유하고(_yaw), 실제 회전은 솔버가 만든다.
            //
            // MoveRotation 으로 쓰면 안 된다 — 비키네마틱 바디에서 그것은 직접 대입이라 유니티가
            // 텔레포트로 취급하고 **보간 버퍼를 건너뛴다.** 위치는 우리가 linearVelocity 만 쓰고
            // 적분은 솔버가 하므로 매끄러운데 방향만 50Hz 로 스냅해서, 회전 중에만 지터가 보였다.
            // 실측으로 갈랐다: MoveRotation 은 transform.rotation 과 rb.rotation 의 차가 항상
            // 0.0000°, 같은 조건에서 각속도로 돌리면 최대 0.88° 로 벌어진다(스텝 폭 1.14° 안).
            //
            // 그래서 선형 쪽과 같은 모양으로 맞춘다 — 모델이 목표를 정하고, 솔버가 움직이고,
            // 유니티가 보간한다. 각속도는 현재 각과 목표의 차를 한 스텝에 메우는 교정 명령이라
            // 덜 돈 만큼 다음 스텝이 더 돈다. 읽은 값에 더하는 것이 아니므로 오차가 누적되지
            // 않고, 충돌 토크가 Y 를 돌려도 같은 방식으로 되돌아온다 — 절대각으로 스냅할 때
            // 나던 벽 떨림이 여기서는 생기지 않는다. 횡속도를 다루는 원칙과 같다.
            float turnRateAtTop = IsDrifting ? _driftTurnRateAtTopSpeed : _turnRateAtTopSpeed;
            float turnRate = Mathf.Lerp(_turnRateAtRest, turnRateAtTop, drive01);
            _yaw += move.x * turnRate * (IsGrounded ? 1f : _airSteerFactor) * dt;

            float yawError = Mathf.DeltaAngle(_rigidbody.rotation.eulerAngles.y, _yaw);
            _rigidbody.angularVelocity = new Vector3(0f, yawError * Mathf.Deg2Rad / dt, 0f);

            Vector3 forward = Quaternion.Euler(0f, _yaw, 0f) * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;          // 완전히 수직으로 뒤집힌 프레임
            forward.Normalize();

            // 정면 성분은 스로틀이, 횡 성분은 그립이 다룬다. 이 두 줄이 설계 전부다.
            float forwardSpeed = Vector3.Dot(planar, forward);
            Vector3 lateral = planar - forward * forwardSpeed;

            // 지면이 차를 늦추는 유일한 접점. 눈(`InGame/Snow`)이 바퀴가 잠긴 비율로 이 값을 낮춘다.
            // 정면 최고속만 건드린다 — 횡그립은 손대지 않는다. 그립 곡선 하나가 저속 정밀 조작과
            // 고속 드리프트를 동시에 정하려다 이미 한 번 실패했고, 거기에 세 번째 소비자를 얹지 않는다.
            float maxSpeed = (_input.AccelerateHeld ? _boostMaxSpeed : _baseMaxSpeed) * GroundSpeedFactor;
            float targetSpeed = move.y >= 0f ? move.y * maxSpeed : move.y * _reverseMaxSpeed;
            bool noThrottle = Mathf.Abs(move.y) < 0.01f;
            bool braking = !noThrottle && forwardSpeed * move.y < 0f;
            float rate = noThrottle ? _coastDecel : (braking ? _brakeDecel : _accel);
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, targetSpeed, rate * dt);

            // 드리프트에 따로 천장을 두지 않는다. 아래의 횡속도 클램프가 이미 천장이다 —
            // 횡이 최고속을 못 넘고 스로틀이 정면을 최고속으로 되돌리므로 슬립각은 45° 에서 멎는다.
            float gripAtTop = IsDrifting ? _driftGripAtTopSpeed : _gripAtTopSpeed;
            float grip = Mathf.Lerp(_gripAtRest, gripAtTop, drive01);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero, grip * dt);

            // 물리가 넣어준 속도는 우리 최고속을 넘을 수 있다 — 충돌 해소가 대표적이고, 그대로
            // 되돌려쓰면 차가 튕겨 날아간다. 다만 **총 속력**을 자르면 안 된다: 드리프트 중에는
            // √(정면² + 횡²) 이 최고속을 넘는 것이 정상이라, 총량을 자르면 횡 성분까지 같이 깎여
            // 드리프트가 눌린다(실측으로 슬립각 30.0° → 22.1°). 스파이크가 실리는 쪽은 횡이므로
            // 횡만 자른다.
            float lateralSpeed = lateral.magnitude;
            if (lateralSpeed > maxSpeed) lateral *= maxSpeed / lateralSpeed;

            Vector3 result = forward * forwardSpeed + lateral;
            _rigidbody.linearVelocity = new Vector3(result.x, vertical, result.z);

            SlipAngle = result.sqrMagnitude < 0.01f
                ? 0f
                : Vector3.Angle(result, forwardSpeed >= 0f ? forward : -forward);
        }
    }
}
