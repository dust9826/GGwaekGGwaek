using System;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 펭귄 이동 — <b>Rigidbody 재작성 (2026-08-22).</b> <c>CharacterController</c> 를
    /// 완전히 걷어내고 접촉 기반 <c>Rigidbody</c>로 바꾼다. 자세한 배경과 계수 유도는
    /// <c>AGENTS.md</c>의 "Rigidbody 재작성" 절 참고.
    ///
    /// <b>2026-08-25 조작 개편:</b> Shift는 일반 이동의 무제한 달리기다. Shift+Space도 일반
    /// 점프와 같은 높이로 뛰며, 착지한 첫 물리 스텝에 지속 슬라이딩으로 전환한다. 슬라이딩 중
    /// Shift는 좌우 발을 번갈아 밀어 <c>AddForce</c> 추진을 만들고, W는 자세를 낮춰 항력과
    /// 그립을 줄인다. 실험용 <see cref="PenguinMomentumHandling"/>이 붙은 프리팹에서는 S가
    /// 지면 접선 속도를 거스르는 브레이크가 되고, 지속 추진과 같은 방향 조향이 서서히 힘을 얻는다.
    ///
    /// 슬라이딩은 손으로 짠 스칼라 장부가 아니라 <b>비등방 마찰</b>(날 방향은
    /// 미끄럽고 옆 방향은 잡힌다) 하나로 선회 감속·선회반경·드리프트를 전부 창발시킨다 —
    /// <see cref="TickSliding"/> 참고. <c>BlockNormal</c>·<c>ApplyImpactBrake</c>는 CC가 다른
    /// 물리와 격리돼 있던 시절 "충돌을 흉내 내던" 신호였는데, 이제 진짜 <c>Rigidbody</c>끼리
    /// 부딪히므로 둘 다 삭제했다 — 솔버가 공짜로 처리한다.
    ///
    /// <b>중력은 이제 우리가 소유하지 않는다.</b> 예전 <c>_gravity = -20</c>("중력이 2배인
    /// 세계")은 펭귄이 <c>BlockNormal</c>/<c>IgnoreCollision</c>으로 다른 물리와 완전히 격리돼
    /// 있던 시절의 결정이었다. 이제 눈덩이(<see cref="SnowBallCarrier"/>)와 실제로 부딪히므로,
    /// <c>Rigidbody.useGravity</c>를 켜 프로젝트의 나머지 전부와 같은 <c>Physics.gravity</c>
    /// (−9.81)를 쓴다. <see cref="GravityMagnitude"/>는 이제 이 값을 그대로 반환한다.
    ///
    /// <b>수직 속도를 더 이상 손으로 적분하지 않는다.</b> CC 시절엔 <c>_verticalVelocity</c>를
    /// 매 프레임 갱신해야 했지만, 진짜 <c>Rigidbody</c>는 <c>useGravity</c> 하나로 낙하를
    /// 대신한다. 점프는 그 위에 <c>linearVelocity.y</c>를 <b>대입</b>하는 한 줄뿐이다(더하지
    /// 않는다 — 낙하 중 점프해도 항상 같은 높이가 나오게 하려면 기존 수직 속도를 지워야 한다).
    ///
    /// <b>걷기는 목표속도-현재속도 오차를 가속도로 미는 PD 제어다.</b>
    /// <c>ForceMode.Acceleration</c>을 써서 질량과 무관하게 동작하고, <see cref="_walkAccel"/>로
    /// 상한을 걸어 순간 스냅이 아니라 짧은 시간에 걸쳐 크리스프하게 멈추도록 한다.
    ///
    /// 루트 Rigidbody는 X/Z 회전을 잠가 항상 조작 가능한 자세를 유지한다. 이동 방향 전환은
    /// 월드 Y축 요 토크가 맡으므로 카빙과 충돌의 선형 관성은 보존된다.
    /// </summary>
    [RequireComponent(typeof(PenguinControlState))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PenguinLocomotion : MonoBehaviour
    {
        private const float BareHopLandingGapM = 0.025f;

        [Header("접점")]
        [SerializeField] private PenguinInputReader _input;
        [Tooltip("이동 기준이 되는 카메라(또는 카메라 피벗). 보통 PenguinCameraOrbit 의 트랜스폼.")]
        [SerializeField] private Transform _cameraPivot;
        [Tooltip("현행 CPU 눈. SinglePlay와 Snow_BallPush_Test 계열은 이 참조를 쓴다.")]
        [SerializeField] private SnowCpuStage _snowCpuStage;
        [Tooltip("구형 눈을 쓰는 테스트 씬용 호환 참조. CPU 눈이 있으면 그쪽을 우선한다.")]
        [SerializeField] private SnowStage _snowStage;
        [Tooltip("슬라이딩 중 눈 깊이를 재는 지점들. 비어 있으면 이 트랜스폼 위치 하나로 잰다. Phase 3에서 쓰인다.")]
        [SerializeField] private Transform[] _snowSamplePoints;

        [Header("걷기")]
        [SerializeField] private float _walkSpeed = 3.5f;
        [Tooltip("Shift를 누른 일반 이동 속도(m/s). 체력이 남아 있는 동안만 나온다.")]
        [SerializeField, Min(0.1f)] private float _sprintSpeed = 5.5f;
        [Tooltip("걷기 중 목표 방향을 따라갈 수 있는 최대 요 속도(도/초). 실제 회전은 토크로 만든다.")]
        [SerializeField] private float _rotationDegPerSec = 540f;
        [Tooltip("목표속도로 따라붙는 최대 가속도(m/s²). 크게 잡을수록 스냅에 가까운 크리스프한 정지가 나온다.")]
        [SerializeField, Min(0.1f)] private float _walkAccel = 40f;

        [Header("체력")]
        [Tooltip("Shift 달리기가 소모하고 쉬면 차는 체력. 점프는 이것을 쓰지 않는다.")]
        [SerializeField] private PenguinStaminaTuning _staminaTuning =
            new PenguinStaminaTuning(sprintSeconds: 6f, refillSeconds: 4.5f,
                refillDelaySeconds: 0.6f, exhaustExit01: 0.3f);

        [Header("외부 속도 효과")]
        [Tooltip("부스터 같은 외부 효과가 적용하는 이동 속도 배율. 런타임 전용이며 프리팹 기본값은 항상 1이다.")]
        [SerializeField, Min(1f)] private float _speedBoostMultiplier = 1f;

        [Tooltip("판이 얻은 증강. 비어 있으면 효과가 없고 기존 동작 그대로다.")]
        [SerializeField] private AugmentLoadout _augments;

        [Header("점프")]
        [SerializeField] private float _jumpHeight = 1.0f;
        [Tooltip("점프 뒤 다시 점프할 수 있을 때까지의 시간(초). 착지 즉시 재점프하는 연타를 막는다. 슬라이딩 점프도 같은 타이머를 쓴다.")]
        [SerializeField, Min(0f)] private float _jumpCooldownSeconds = 0.35f;

        [Header("맨바닥 작은 점프")]
        [SerializeField, Min(0f)] private float _bareHopHeight = 0.18f;
        [SerializeField, Min(0f)] private float _bareHopVelocityDeltaMps = 1.1f;
        [SerializeField, Min(0f)] private float _bareHopMaxSpeedMps = 1.1f;
        [SerializeField, Min(0f)] private float _snowSupportToleranceM = 0.12f;

        [Header("접지")]
        [Tooltip("캡슐 바닥에서 이만큼 더 아래까지 지면을 찾는다(m). CC의 stepOffset/slopeLimit 대응물은 없다 — 실측 후 필요하면 Phase 2에서 추가한다.")]
        [SerializeField, Min(0.05f)] private float _groundProbeExtra = 0.15f;
        [Tooltip("접지 판정에 쓸 레이어. 지형만 남기면 눈덩이 같은 동적 물체에 반응하지 않는다.")]
        [SerializeField] private LayerMask _groundLayers = ~0;
        [Tooltip("이 각도(도)보다 가파른 지면에서는 걷기 힘을 걸지 않는다. CC의 slopeLimit=45 를 그대로 계승.")]
        [SerializeField, Range(1f, 89f)] private float _maxWalkableSlopeDeg = 45f;

        [Header("눈")]
        [Tooltip("이 깊이(cm) 이상이면 그 지점은 눈으로 덮인 것으로 본다.")]
        [SerializeField, Range(1, 60)] private int _snowThresholdCm = 2;
        [Tooltip("덮인 비율이 목표까지 따라가는 속도(초당). 경계를 넘나들 때 툭 끊기지 않게 한다.")]
        [SerializeField] private float _coverageLerpPerSecond = 4f;

        [Header("슬라이딩 — 비등방 마찰")]
        [Tooltip("날 방향(전진) 마찰 계수, 눈 위. 실측/문헌값 μ≈0.05(눈 위 매끄러운 물체). g 와 무관한 무차원 값이라 중력을 9.81 로 바꿔도 그대로 쓴다.")]
        [SerializeField, Min(0f)] private float _snowFrictionMu = 0.05f;
        [Tooltip("날 방향(전진) 마찰 계수, 맨바닥. μ≈0.5(바위·콘크리트). 이것도 무차원이라 그대로.")]
        [SerializeField, Min(0f)] private float _bareFrictionMu = 0.5f;
        [Tooltip("옆 방향(그립) 마찰 계수. 물리 문헌값이 아니라 튜닝 상수다 — v²/(μg) 선회반경이 옛 _turnBySpeed 곡선의 무릎(v=4.5 에서 150°/s)과 맞도록 역산했다. g=9.81 기준 1.2.")]
        [SerializeField, Min(0f)] private float _lateralGripMu = 1.2f;
        [Tooltip("공기 저항 계수(N/(m/s)²). 힘 도메인이라 질량과 무관 — F = k·v². 기존 가속도 계수(0.00056, 1/m)에 질량 30kg 을 곱한 값과 같다.")]
        [SerializeField, Min(0f)] private float _airDragForceN = 0.0168f;
        [Tooltip("슬라이딩 중 W로 몸을 웅크렸을 때 항력에 곱하는 값. 직접 추진력은 만들지 않는다.")]
        [SerializeField, Range(0.1f, 1f)] private float _tuckDragMultiplier = 0.72f;
        [Tooltip("슬라이딩 중 W로 몸을 웅크렸을 때 최대 그립과 목표 슬립각에 곱하는 값.")]
        [SerializeField, Range(0.1f, 1f)] private float _tuckGripMultiplier = 0.82f;
        [Tooltip("A/D를 끝까지 눌렀을 때 이동 방향과 차체 사이에 만드는 최대 슬립각(도). 핸들이 차체를 무한히 돌리지 않고, 이 각도 안에서만 그립을 꺼내 쓴다.")]
        [SerializeField, Range(0f, 45f)] private float _maxSlideSlipDeg = 30f;
        [Tooltip("목표 슬립각이 입력을 따라가는 시간(초). 짧을수록 즉각적이고 길수록 드리프트 진입이 묵직하다.")]
        [SerializeField, Min(0.01f)] private float _slideSlipResponseSeconds = 0.12f;
        [Tooltip("저속에서 차체가 목표 슬립각을 따라가는 최대 요 속도(도/초).")]
        [SerializeField, Min(1f)] private float _slideYawRateLowSpeed = 240f;
        [Tooltip("고속에서 차체가 목표 슬립각을 따라가는 최대 요 속도(도/초). 고속 빙글 회전을 막기 위해 저속보다 작다.")]
        [SerializeField, Min(1f)] private float _slideYawRateHighSpeed = 75f;
        [Tooltip("이 속도(m/s)에서 고속 요 속도를 완전히 적용한다.")]
        [SerializeField, Min(0.1f)] private float _slideYawHighSpeedMps = 9f;
        [Tooltip("슬라이딩 중에만 붙이는 마찰 0 물리 재질. 마찰을 비등방 모델이 전부 소유해야 하므로 PhysX 몫은 0이어야 한다. 걷기 중엔 null(기본 재질)로 돌아가 slope 위에서 자연스레 선다 — 채널 하나에 주인 하나.")]
        [SerializeField] private PhysicsMaterial _slidingMaterial;

        [Header("슬라이딩 — 교대 발 밀기")]
        [Tooltip("Shift를 누르는 동안 초당 발을 내딛는 횟수. 왼발과 오른발이 번갈아 한 번씩 민다.")]
        [SerializeField, Min(0.1f)] private float _slideKickStepsPerSecond = 3f;
        [Tooltip("발 하나가 미는 구간의 최대 힘(N). 한 프레임 임펄스가 아니라 AddForce로 분산한다.")]
        [SerializeField, Min(0f)] private float _slideKickForceN = 180f;
        [Tooltip("가벼운 물건을 운반할 때 기본 발차기 가속도에 곱하는 값. 일반 슬라이딩보다 느려야 하므로 1보다 작게 둔다.")]
        [SerializeField, Range(0f, 1f)] private float _carryKickLightMultiplier = 0.65f;
        [Tooltip("운반물이 전체 질량 대부분을 차지할 때 기본 발차기 가속도에 곱하는 하한. 매우 무거워도 눈 마찰을 이길 수 있어야 한다.")]
        [SerializeField, Range(0f, 1f)] private float _carryKickHeavyMultiplier = 0.4f;
        [Tooltip("운반 출발 보조가 가장 강하게 유지되는 지면 접선 속도(m/s).")]
        [SerializeField, Min(0f)] private float _carryLaunchAssistFullMps = 0.5f;
        [Tooltip("이 속도(m/s)부터 운반 출발·조향 보조를 끄고 기존 슬라이딩 값만 사용한다.")]
        [SerializeField, Min(0.1f)] private float _carryLaunchAssistEndMps = 2f;
        [Tooltip("가벼운 운반물을 메고 출발할 때 기본 발차기 가속도에 곱하는 값. 순항 구간에서는 기존 운반 배율로 복귀한다.")]
        [SerializeField, Range(0f, 1f)] private float _carryLaunchLightMultiplier = 0.85f;
        [Tooltip("매우 무거운 운반물을 메고 출발할 때 기본 발차기 가속도에 곱하는 하한. 순항 구간에서는 기존 운반 배율로 복귀한다.")]
        [SerializeField, Range(0f, 1f)] private float _carryLaunchHeavyMultiplier = 0.65f;
        [Tooltip("운반 저속 구간에서 A/D가 차체 전방으로부터 먼저 가리킬 수 있는 최대 각도(도). 속도가 붙으면 기존 슬립각으로 복귀한다.")]
        [SerializeField, Range(0f, 90f)] private float _carryLaunchTurnLeadDeg = 60f;
        [Tooltip("운반 저속 방향 전환의 최대 요 속도(도/초).")]
        [SerializeField, Min(1f)] private float _carryLaunchYawRateDegPerSec = 210f;
        [Tooltip("한 발 주기에서 실제로 힘을 싣는 비율. 나머지는 다음 스트로크를 위한 회수 구간이다.")]
        [SerializeField, Range(0.1f, 0.9f)] private float _slideKickPowerFraction = 0.6f;
        [Tooltip("평지에서 Shift를 계속 누른 일반 슬라이딩의 목표 순항 속도(m/s).")]
        [SerializeField, Min(0.1f)] private float _slideFlatTargetSpeedMps = 7.5f;
        [Tooltip("아주 가벼운 물건을 운반할 때의 평지 목표 순항 속도(m/s).")]
        [SerializeField, Min(0.1f)] private float _carryLightFlatTargetSpeedMps = 6.5f;
        [Tooltip("운반물이 전체 질량 대부분을 차지할 때의 평지 목표 순항 속도(m/s).")]
        [SerializeField, Min(0.1f)] private float _carryHeavyFlatTargetSpeedMps = 3.5f;
        [Tooltip("운반 중에는 날이 아니라 발로 하중을 지탱하므로 지면 종류와 무관하게 쓰는 전진 마찰 계수.")]
        [SerializeField, Min(0f)] private float _carryForwardFrictionMu = 0.05f;

        [Header("슬라이딩 전환")]
        [Tooltip("슬라이딩을 놓은 뒤 보행 가속도가 완전히 돌아오는 시간(초). 고속 관성을 즉시 걷기 속도로 잘라내지 않는다.")]
        [SerializeField, Min(0f)] private float _slideExitControlSeconds = 0.25f;
        [Tooltip("이 접선 속도(m/s) 아래가 일정 시간 유지되면 슬라이딩을 종료한다.")]
        [SerializeField, Min(0f)] private float _slideStopSpeed = 1f;
        [Tooltip("저속이 이 시간(초) 이상 유지돼야 슬라이딩을 종료한다.")]
        [SerializeField, Min(0f)] private float _slideStopDelay = 0.2f;

        [Header("회전")]
        [Tooltip("목표 요 속도를 실제 각속도가 따라가는 시간(초).")]
        [SerializeField, Min(0.01f)] private float _yawResponseSeconds = 0.12f;
        [Tooltip("방향 전환 모터가 낼 수 있는 최대 요 각가속도(rad/s²).")]
        [SerializeField, Min(0f)] private float _yawMaxAngularAcceleration = 70f;

        [Header("연출용 정규화")]
        [Tooltip("x = 속도(m/s), y = 연출 세기 0~1. 카메라·차체 반응이 전부 이 하나를 읽는다.")]
        [SerializeField] private AnimationCurve _speedToIntensity = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(9.2f, 0.75f), new Keyframe(22f, 1f));

        /// <summary>눈덩이 옆에서 미는 속도와 둘레 이동 속도의 기준.</summary>
        public float WalkSpeedMps => _walkSpeed * SpeedMultiplier;

        /// <summary>현재 외부 부스터가 적용한 이동 속도 배율.</summary>
        public float SpeedBoostMultiplier => _speedBoostMultiplier;

        /// <summary>걷기·달리기 속도에 곱하는 값. 부스트 패드와 증강은 <b>서로 다른 주인</b>이라 한
        /// 필드를 나눠 쓰지 않는다 — <see cref="PenguinBoostReceiver"/> 가 만료 때
        /// <see cref="SetSpeedBoostMultiplier"/>(1f) 로 되돌리므로 증강 값이 지워지고,
        /// 그쪽은 [1,3] 클램프라 감속 패널티를 담지도 못한다.
        ///
        /// <para><b>슬라이딩에는 걸지 않는다.</b> <c>SlideKickForceN</c> 과
        /// <c>SlideTargetSpeedMps</c> 는 계속 <see cref="_speedBoostMultiplier"/> 만 쓴다 —
        /// 증강 카드가 약속하는 것이 "walk speed" 라서다. 활강까지 올리고 싶으면 그것은
        /// 별도 스탯이지 이 스탯의 확장이 아니다.</para></summary>
        private float SpeedMultiplier =>
            _speedBoostMultiplier * (_augments != null ? _augments.GetMultiplier(EAugmentStat.WalkSpeed) : 1f);

        /// <summary>증강 로드아웃을 꽂는다. 씬에서는 빌더가 인스펙터 값을 채우고,
        /// <b>멀티에서는 펭귄이 런타임 스폰이라 그 씬 참조가 없으므로</b>
        /// <see cref="PenguinNetAvatar"/> 가 스폰 직후 이것을 부른다.</summary>
        public void SetAugments(AugmentLoadout augments) => _augments = augments;

        /// <summary>슬라이딩과 운반 충돌 프록시가 함께 쓰는 무마찰 물리 재질.</summary>
        public PhysicsMaterial SlidingMaterial => _slidingMaterial;

        /// <summary>
        /// 아이템 등 외부 시스템이 이동 속도를 임시로 올릴 때 쓰는 단일 진입점이다.
        /// 감속 용도로 오용하지 않도록 1~3배 범위로 제한한다.
        /// </summary>
        public void SetSpeedBoostMultiplier(float multiplier)
        {
            _speedBoostMultiplier = Mathf.Clamp(multiplier, 1f, 3f);
        }

        /// <summary>
        /// 연출이 읽는 단일 속도 소스(0~1). <b>슬라이딩 중 실제 수평 속력만 읽는다</b> —
        /// 차체 전방 성분만 쓰면 옆으로 흐르는 드리프트 순간 카메라가 느려지는 것처럼 보인다.
        /// 걷기 중엔 예전부터 항상 0이다(최고 3.5 m/s라 "극적인 속도"라는 개념이 없다).
        /// </summary>
        public float CurrentSpeed01 => UsesSlidingLocomotion
            ? Mathf.Clamp01(_speedToIntensity.Evaluate(Speed))
            : 0f;

        /// <summary>현재 수평 이동 속력(m/s). 애니메이터의 Speed 파라미터가 이걸 읽는다.</summary>
        public float Speed { get; private set; }

        /// <summary>현재 수평 이동 방향. 정지 중이면 <see cref="Vector3.zero"/>다.</summary>
        public Vector3 HorizontalVelocityDirection { get; private set; }

        /// <summary>발밑 사거리 안에 지면이 있는가(레이캐스트).</summary>
        public bool IsGrounded { get; private set; }

        /// <summary>지상 슬라이딩 중인가.</summary>
        public bool IsSliding => _hasPresentation
            ? _presentation.Sliding
            : _controlState != null && _controlState.Current == EPenguinControlState.Sliding;

        /// <summary>운반은 별도 상태지만 슬라이딩과 같은 마찰·조향·발차기 물리를 쓴다.</summary>
        public bool UsesSlidingLocomotion => IsSliding || (!_hasPresentation &&
            _controlState != null && _controlState.Current == EPenguinControlState.Carrying);

        /// <summary>
        /// 슬라이딩 애니메이션 자세를 써야 하는가. Shift+Space를 짧게 눌렀다면 착지 예약이
        /// 자세를 유지하고, 공중에서 Shift를 누른 뒤에는 실제 슬라이딩 상태가 이 값을 켠다.
        /// </summary>
        public bool IsSlidePose => _hasPresentation
            ? _presentation.SlidePose
            : UsesSlidingLocomotion || (_slideQueuedOnLanding && _queuedJumpLeftGround);

        /// <summary>슬라이딩 중 W를 눌러 항력을 줄이고 그립을 희생하는 중인가.</summary>
        public bool IsTucking => UsesSlidingLocomotion && _lastInput.Move.y > 0.1f;

        /// <summary>마지막으로 측정한 지면 법선. 미접지 시 위쪽이다.</summary>
        public Vector3 GroundNormal { get; private set; } = Vector3.up;

        /// <summary>마지막 접지 레이가 맞힌 실제 지지면의 월드 위치.</summary>
        public Vector3 GroundContactPoint { get; private set; }

        /// <summary>
        /// 입력이 요구하는 수평 속도(m/s). <b>실제로 나고 있는 속도가 아니다.</b>
        ///
        /// <para>눈덩이를 밀 때 공의 속도 상한으로 쓴다 — 실제 속도를 상한으로 쓰면 교착이 생긴다:
        /// 무거운 공에 막힌 펭귄은 속도가 0 이고, 그러면 상한도 0 이라 공이 영원히 못 움직인다.
        /// 의도 속도는 막혀도 줄지 않는다.</para>
        /// </summary>
        public Vector3 DesiredVelocity { get; private set; }

        /// <summary>점프가 실제로 발동한 순간(트리거 애니메이션용).</summary>
        public event Action Jumped;

        /// <summary>
        /// 지금 미끄러지는 속력(m/s, 날 방향 성분, 부호 있음). 뒤로 밀려 내려갈 때는 음수다.
        /// 슬라이딩이 아니면 0이다.
        /// </summary>
        public float SlideSpeed => _slideSpeedSigned;

        /// <summary>
        /// 이번 프레임에 실제로 적용된 선회각속도(도/초, 부호 있음). 슬라이딩이 아니면 0이다.
        /// </summary>
        public float TurnRateDegPerSec { get; private set; }

        /// <summary>현재 차체가 이동 방향에서 벗어나도록 요구받는 목표 슬립각(도, 오른쪽 양수).</summary>
        public float TargetSlipAngleDeg => _slideSlipDeg;

        /// <summary>실제 이동 방향에서 차체 전방까지의 슬립각(도, 오른쪽 양수).</summary>
        public float SlipAngleDeg { get; private set; }

        /// <summary>
        /// 이번 스텝에 <b>실제로 적용된</b> 그립력에서 나온 횡가속도(m/s², 부호 있음. 양수 =
        /// 오른쪽). 슬라이딩이 아니거나 공중이면 0이다.
        ///
        /// <para><see cref="PenguinBodyMotion"/>의 기울기가 이 값을 읽는다. <c>v·ω</c> 추정치
        /// 대신 이걸 쓰는 이유 — <c>TickSliding</c>의 그립력은 <c>_lateralGripMu</c>가 낼 수
        /// 있는 최대치로 잘리므로(<see cref="_lateralGripMu"/>), <b>그립이 못 버텨 드리프트가
        /// 나는 순간 이 값도 같이 포화한다.</b> <c>v·ω</c>는 순수 운동학적 추정이라 그립 한계와
        /// 무관하게 계속 커질 수 있어, 드리프트 중에 몸이 물리적으로 낼 수 없는 각도까지 눕는
        /// 그림이 나올 뻔했다.</para>
        /// </summary>
        public float LateralGripAccel { get; private set; }

        /// <summary>현재 발 밀기 주체. -1은 쉬는 중, 0은 왼발, 1은 오른발이다.</summary>
        public int ActiveSlideKickFoot { get; private set; } = -1;

        /// <summary>현재 발 스트로크의 진행도(0~1). 표현은 이 값만 읽고 물리를 만들지 않는다.</summary>
        public float SlideKickStroke01 { get; private set; }

        /// <summary>이번 물리 스텝에 실제로 적용된 발 힘의 정규화 값(0~1).</summary>
        public float SlideKickPower01 { get; private set; }

        /// <summary>실험 조작의 누적 추진 단계. 프로필이 없으면 0이다.</summary>
        public float MomentumBuildUp01 => _momentumHandling != null ? _momentumBuildUp01 : 0f;

        /// <summary>현재 방향으로 쌓인 조향 권한. 프로필이 없으면 0이다.</summary>
        public float MomentumSteerCommitment01 => _momentumHandling != null
            ? _momentumSteerCommitment01
            : 0f;

        /// <summary>이번 스텝의 실험 평지 추진 목표 속도(m/s).</summary>
        public float MomentumTargetSpeedMps { get; private set; }

        /// <summary>슬라이딩 또는 운반 중 S 브레이크가 눌렸는가.</summary>
        public bool IsMomentumBraking { get; private set; }

        /// <summary>
        /// 이 컴포넌트가 쓰는 중력 크기(m/s²). <b>이제 <c>Physics.gravity</c> 그대로다</b> —
        /// 더 이상 우리가 소유한 별도의 g가 아니다. 눈덩이(<see cref="SnowBallCarrier"/>)도 이미
        /// <c>Physics.gravity</c>를 쓰고 있었으므로, 이렇게 맞추는 것이 격리를 없애는 전제와
        /// 맞다 — 자세한 이유는 AGENTS.md 참고.
        /// </summary>
        public float GravityMagnitude => Physics.gravity.magnitude;

        private PenguinControlState _controlState;
        private Rigidbody _rigidbody;
        private CapsuleCollider _capsule;
        private float _baseBodyMassKg;
        private float _snowCoverage01;
        private float _slideSpeedSigned;
        private bool _hasSlidingMaterial;
        private float _slideExitControlRemaining;
        private float _slideSlipDeg;
        private float _slideSlipVelocity;
        private float _slideStopElapsed;
        private bool _slideQueuedOnLanding;
        private bool _queuedJumpLeftGround;
        private float _queuedJumpElapsed;
        private bool _bareHopActive;
        private float _slideKickStep;
        private PenguinMomentumHandling _momentumHandling;
        private float _momentumBuildUp01;
        private float _momentumSteerCommitment01;
        private float _momentumSteerSign;

        private void Awake()
        {
            _controlState = GetComponent<PenguinControlState>();
            _rigidbody = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _momentumHandling = GetComponent<PenguinMomentumHandling>();
            _baseBodyMassKg = Mathf.Max(0.01f, _rigidbody.mass);
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotationX
                                     | RigidbodyConstraints.FreezeRotationZ;
            _rigidbody.maxDepenetrationVelocity = 3f;
            _rigidbody.maxAngularVelocity = 7f;
            _rigidbody.useGravity = true;
        }

        private void Start()
        {
            if (_snowCpuStage != null) return;

            foreach (SnowCpuStage candidate in FindObjectsByType<SnowCpuStage>())
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                _snowCpuStage = candidate;
                return;
            }
        }

        /// <summary>네트워크 진입점이 켠다. 켜지면 이 컴포넌트는 자기 클럭으로 돌지 않는다.</summary>
        public bool NetworkDriven { set => _networkDriven = value; }

        private bool _networkDriven;
        private PenguinMoveInput _lastInput;

        /// <summary>점프가 발동한 횟수. 연출 복제가 에지를 잡는 데 쓴다.</summary>
        private byte _jumpCount;

        private PenguinStaminaState _stamina = PenguinStaminaState.Full;

        /// <summary>이번 스텝에 달려도 되는가. <see cref="Step"/> 첫머리에서 정해진다.</summary>
        private bool _sprintAllowed = true;

        /// <summary><b>지난 스텝에 실제로 달렸는가.</b> 입력이 아니라 결과를 되먹이는 이유는,
        /// Shift 를 쥐고 눈덩이에 올라타 있거나 슬라이딩 중일 때는 달리는 것이 아니라서다 —
        /// 입력으로 깎으면 아무 이득 없이 체력만 준다. 한 스텝 지연은 눈에 보이지 않는다.</summary>
        private bool _sprintedLastStep;

        private float _jumpCooldownRemaining;

        /// <summary>0~1. HUD 의 체력 바가 읽는다. 비권위 피어에서는
        /// <see cref="ApplyPresentation"/> 이 채운 복제값이다.</summary>
        public float Stamina01 => _stamina.Value01;

        /// <summary>다 써서 잠긴 상태인가. 바의 색이 이것으로 갈린다.</summary>
        public bool StaminaExhausted => _stamina.Exhausted;

        /// <summary>
        /// 런너가 없는 판(싱글플레이·단독 테스트)의 진입점.
        ///
        /// <para>런너가 있으면 여기서 돌지 않는다 — 물리 애드온이 <c>simulationMode</c> 를
        /// <c>Script</c> 로 바꿔도 Unity 는 <c>FixedUpdate</c> 를 계속 부르므로, 가드가 없으면
        /// 같은 힘을 두 시계로 쌓는다.</para>
        /// </summary>
        private void FixedUpdate()
        {
            if (_networkDriven || _input == null) return;
            Step(Time.fixedDeltaTime, ReadLocalInput());
        }

        /// <summary>
        /// 로컬 입력 컴포넌트와 카메라에서 한 스텝 분량을 읽는다. <b>멀티도 이것을 쓴다</b> —
        /// 네트워크 진입점이 이 값을 <c>NetworkInputData</c> 로 옮겨 담는다. 읽는 규칙이 두 곳에
        /// 살면 싱글과 멀티의 조작이 조용히 갈린다.
        /// </summary>
        public PenguinMoveInput ReadLocalInput() => new PenguinMoveInput
        {
            Move = _input.MoveInput,
            CameraYawDeg = _cameraPivot != null ? _cameraPivot.eulerAngles.y : transform.eulerAngles.y,
            SprintHeld = _input.SprintHeld,
            JumpPressed = _input.JumpPressedThisFrame,
            PackSnowHeld = _input.PackSnowHeld,
            CreateSnowballPressed = _input.CreateSnowballPressedThisFrame,
            BurstPressed = _input.BurstPressedThisFrame,
            PickupPressed = _input.PickupPressedThisFrame,
            CoopShovePressed = _input.CoopShovePressedThisFrame,
        };

        /// <summary>
        /// 이번 스텝이 만들어 낸 연출 상태를 꺼낸다. <b>권위 피어만 의미가 있다</b> —
        /// 다른 곳에서는 <see cref="Step"/> 이 돌지 않아 전부 기본값이다.
        /// </summary>
        public PenguinPresentation CapturePresentation() => new PenguinPresentation
        {
            Stamina01 = _stamina.Value01,
            StaminaExhausted = _stamina.Exhausted,
            Speed = Speed,
            HorizontalVelocityDirection = HorizontalVelocityDirection,
            GroundNormal = GroundNormal,
            LateralGripAccel = LateralGripAccel,
            Grounded = IsGrounded,
            Sliding = IsSliding,
            SlidePose = IsSlidePose,
            JumpCount = _jumpCount,
        };

        /// <summary>
        /// 남이 계산한 연출 상태를 이 컴포넌트에 앉힌다. <b>비권위 피어 전용이다.</b>
        ///
        /// <para><c>_controlState</c> 를 건드리지 않고 <see cref="_presentation"/> 우회로를 쓰는 이유:
        /// 그것은 <b>전환 규칙을 가진 시뮬레이션 상태</b>라, 한 틱 늦은 복제본을 밀어 넣으면 전환이
        /// 어긋난다. 연출이 필요한 것은 결과(<c>IsSliding</c>·<c>IsSlidePose</c>)뿐이다.</para>
        ///
        /// <para><b>권위 피어에서 부르면 안 된다.</b> 그러면 자기가 계산한 상태 위에 복제본을 덮어
        /// 쓰게 된다.</para>
        /// </summary>
        public void ApplyPresentation(in PenguinPresentation presentation)
        {
            _presentation = presentation;
            _hasPresentation = true;

            Speed = presentation.Speed;
            HorizontalVelocityDirection = presentation.HorizontalVelocityDirection;
            GroundNormal = presentation.GroundNormal;
            LateralGripAccel = presentation.LateralGripAccel;
            IsGrounded = presentation.Grounded;
            _stamina = PenguinStaminaState.Replicated(presentation.Stamina01, presentation.StaminaExhausted);
        }

        /// <summary>
        /// 복제된 점프를 애니메이터로 흘린다. <see cref="Jumped"/> 는 이 클래스만 발동할 수 있고,
        /// 언제 쏠지(계수기가 바뀐 순간)는 네트워크 진입점이 판단한다.
        /// </summary>
        public void RaisePresentationJump() => Jumped?.Invoke();

        private PenguinPresentation _presentation;
        private bool _hasPresentation;

        /// <summary>
        /// 이동 본문. <b>진입점이 둘이어도 이 본문은 하나다</b> — 싱글은 <c>FixedUpdate</c> 가,
        /// 멀티는 <see cref="PenguinNetAvatar"/> 가 확정된 틱에서 부른다.
        /// </summary>
        public void Step(float dt, in PenguinMoveInput input)
        {
            _lastInput = input;
            SampleGround(out Vector3 groundNormal, out float slopeAngleDeg, out bool grounded,
                out Vector3 groundContactPoint, out float groundGapM);
            if (_bareHopActive)
            {
                bool landed = grounded
                              && groundGapM <= BareHopLandingGapM
                              && _rigidbody.linearVelocity.y <= 0f;
                if (landed)
                    _bareHopActive = false;
                else
                    grounded = false;
            }
            IsGrounded = grounded;
            GroundNormal = grounded ? groundNormal : Vector3.up;
            if (grounded) GroundContactPoint = groundContactPoint;
            UpdateSnowCoverage(dt);

            const float movementControl01 = 1f;

            if (_controlState.Current == EPenguinControlState.SnowballTop)
            {
                ExitSlide();
                CancelQueuedSlide();
                TurnRateDegPerSec = 0f;
                _slideSpeedSigned = 0f;
                LateralGripAccel = 0f;

                // 실제 이동은 PenguinSnowball의 꼭대기 앵커(LateUpdate)가 맡는다. 여기서는
                // 애니메이션이 읽을 Speed/DesiredVelocity 만 갱신한다.
                Vector3 mountedDir = CameraRelativeDirection(input.Move, input.CameraYawDeg);
                DesiredVelocity = mountedDir * WalkSpeedMps;
                Speed = Mathf.Clamp01(input.Move.magnitude) * WalkSpeedMps;
                UpdateHorizontalVelocityDirection();
                return;
            }

            if (_controlState.Current == EPenguinControlState.SnowballSide)
            {
                ExitSlide();
                CancelQueuedSlide();
                TurnRateDegPerSec = 0f;
                _slideSpeedSigned = 0f;
                LateralGripAccel = 0f;

                // A/D는 공 둘레 이동에 쓰므로 힘 의도에서 제외한다. W/S의 크기만 유지한다.
                Vector3 fwd = _cameraPivot != null ? FlattenForward(_cameraPivot) : FlattenForward(transform);
                DesiredVelocity = fwd * (input.Move.y * WalkSpeedMps);
                Speed = Mathf.Clamp01(input.Move.magnitude) * WalkSpeedMps;
                UpdateHorizontalVelocityDirection();

                // 수평 이동은 PenguinSnowball.TickSideOrbit 이 소유한다. 수직은 중력이 알아서
                // 처리하므로 여기서 손댈 것이 없다.
                return;
            }

            if (_controlState.Current == EPenguinControlState.CarryApproach)
            {
                ExitSlide();
                CancelQueuedSlide();
                TurnRateDegPerSec = 0f;
                _slideSpeedSigned = 0f;
                LateralGripAccel = 0f;
                DesiredVelocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, Vector3.up);
                Speed = DesiredVelocity.magnitude;
                UpdateHorizontalVelocityDirection();
                return;
            }

            if (_controlState.Current == EPenguinControlState.Carrying)
            {
                SetSlidingMaterial(true);
                CancelQueuedSlide();

                if (input.JumpPressed && grounded)
                {
                    if (IsSnowCovered(GroundContactPoint))
                        Jump(_jumpHeight);
                    else
                        BareHop(input, groundNormal);
                    return;
                }

                TickSliding(dt, input, grounded, groundNormal, slopeAngleDeg, movementControl01);
                return;
            }

            // 일반 Space로 먼저 점프했더라도 공중에서 Shift를 누르는 즉시 슬라이딩 상태로
            // 들어간다. 수직 속도는 건드리지 않으므로 점프 정점은 그대로고, 공중에서는
            // TickSliding의 관성·조향만 쓰다가 착지하면 같은 상태로 지면 마찰·카빙이 이어진다.
            if (!grounded && !IsSliding && input.SprintHeld)
                EnterSliding();

            if (_slideQueuedOnLanding)
            {
                _queuedJumpElapsed += dt;
                if (!grounded) _queuedJumpLeftGround = true;
                if (grounded && (_queuedJumpLeftGround || _queuedJumpElapsed >= 0.18f))
                {
                    EnterSliding();
                    TickSliding(dt, input, true, groundNormal, slopeAngleDeg, movementControl01);
                    TickSlideStop(dt, true, groundNormal);
                    return;
                }
            }

            if (_controlState.Current == EPenguinControlState.Sliding)
            {
                if (input.JumpPressed && grounded)
                {
                    if (IsSnowCovered(GroundContactPoint))
                        JumpFromSlide();
                    else
                        BareHop(input, groundNormal);
                    return;
                }

                TickSliding(dt, input, grounded, groundNormal, slopeAngleDeg, movementControl01);
                TickSlideStop(dt, grounded, groundNormal);
                return;
            }

            SetSlidingMaterial(false);
            TurnRateDegPerSec = 0f;
            SlipAngleDeg = 0f;
            _slideSpeedSigned = 0f;
            LateralGripAccel = 0f;

            Vector3 moveDir = CameraRelativeDirection(input.Move, input.CameraYawDeg);
            float moveSpeed = (input.SprintHeld ? _sprintSpeed : _walkSpeed) * SpeedMultiplier;
            Vector3 desiredHorizontal = moveDir * moveSpeed;

            // CC의 slopeLimit(45°) 대응. 가파른 지면에서는 걷기 힘을 걸지 않는다 — 오르지 못하고
            // 미끄러지거나 멈춘다.
            if (grounded && slopeAngleDeg > _maxWalkableSlopeDeg)
                desiredHorizontal = Vector3.zero;

            // 안 밀리는 눈덩이 앞에서 막히는 것은 이제 코드가 아니라 솔버가 처리한다 — 진짜
            // Rigidbody 충돌이라 파고들 수 없다(Phase 4).
            DesiredVelocity = desiredHorizontal * movementControl01;

            float walkControl01 = 1f;
            if (_slideExitControlRemaining > 0f && _slideExitControlSeconds > 0f)
            {
                // 슬라이드 점프의 공중 관성을 보행 모터가 깎지 않는다. 착지한 뒤에만 조작권을
                // 천천히 되돌리면 비행 중 속도는 보존되고 착지 전환도 갑자기 꺾이지 않는다.
                if (grounded)
                    _slideExitControlRemaining = Mathf.Max(0f,
                        _slideExitControlRemaining - dt);
                walkControl01 = 1f - _slideExitControlRemaining / _slideExitControlSeconds;
            }

            Vector3 currentHorizontal = _rigidbody.linearVelocity;
            currentHorizontal.y = 0f;
            Vector3 facingDir = _slideExitControlRemaining > 0f && currentHorizontal.sqrMagnitude > 0.0001f
                ? currentHorizontal.normalized
                : moveDir;

            ApplyWalkForce(dt, desiredHorizontal, walkControl01 * movementControl01);
            ApplyYawFacing(facingDir, _rotationDegPerSec, movementControl01);
            if (ApplyJump(grounded, input) && input.SprintHeld)
                QueueSlideOnLanding();

            Vector3 horizontalVel = _rigidbody.linearVelocity;
            horizontalVel.y = 0f;
            Speed = horizontalVel.magnitude;
            HorizontalVelocityDirection = Speed > 0.001f
                ? horizontalVel / Speed
                : Vector3.zero;
        }

        private void QueueSlideOnLanding()
        {
            _slideQueuedOnLanding = true;
            _queuedJumpLeftGround = false;
            _queuedJumpElapsed = 0f;
        }

        private void CancelQueuedSlide()
        {
            _slideQueuedOnLanding = false;
            _queuedJumpLeftGround = false;
            _queuedJumpElapsed = 0f;
        }

        private void JumpFromSlide()
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = Mathf.Sqrt(2f * GravityMagnitude * _jumpHeight);
            ExitSlide();
            SetLinearVelocity(velocity);
            MarkJumped();
        }

        /// <summary>
        /// 맨바닥에서 슬라이딩·운반 상태를 유지한 채 뛰는 작은 점프. Space만 누르면 수평 관성을
        /// 그대로 두고, WASD가 있으면 차체 기준 네 방향으로 같은 크기의 속도 변화를 더한다.
        /// </summary>
        private void BareHop(in PenguinMoveInput input, Vector3 groundNormal)
        {
            Vector3 forward = ProjectForwardOnGround(groundNormal);
            Vector3 right = Vector3.Cross(groundNormal, forward).normalized;
            Vector3 direction = forward * input.Move.y + right * input.Move.x;
            direction.y = 0f;
            float inputMagnitude = Mathf.Clamp01(input.Move.magnitude);
            if (direction.sqrMagnitude > 0.0001f)
                direction = direction.normalized * inputMagnitude;

            Vector3 velocity = _rigidbody.linearVelocity;
            Vector3 horizontal = HorizontalVelocity(velocity);
            if (direction.sqrMagnitude > 0.0001f)
            {
                Vector3 moved = horizontal + direction * _bareHopVelocityDeltaMps;
                float speedLimit = Mathf.Max(horizontal.magnitude, _bareHopMaxSpeedMps);
                horizontal = Vector3.ClampMagnitude(moved, speedLimit);
            }

            velocity.x = horizontal.x;
            velocity.z = horizontal.z;
            velocity.y = Mathf.Sqrt(2f * GravityMagnitude * _bareHopHeight);
            SetLinearVelocity(velocity);
            _bareHopActive = true;
            MarkJumped();
        }

        /// <summary>점프 4갈래가 모두 지나는 자리. 계수기를 올리고 <b>쿨타임을 찍는다</b> —
        /// 슬라이딩 점프도 여기를 지나므로 슬라이딩으로 우회해 연타할 수 없다.</summary>
        private void MarkJumped()
        {
            _jumpCount++;
            _jumpCooldownRemaining = _jumpCooldownSeconds;
            Jumped?.Invoke();
        }

        private bool CanJump => _jumpCooldownRemaining <= 0f;

        private void Jump(float heightM)
        {
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = Mathf.Sqrt(2f * GravityMagnitude * heightM);
            SetLinearVelocity(velocity);
            MarkJumped();
        }

        /// <summary>
        /// 슬라이딩 진입·이탈의 부수 효과. <b>진입 속도는 보존한다</b> — 걷던 흐름이 끊기지
        /// 않아야 드리프트 진입으로 읽힌다. <b>마찰 재질을 스왑한다</b> —
        /// 슬라이딩 중엔 <see cref="_slidingMaterial"/>(마찰 0)을 붙여 비등방 마찰 모델이
        /// 마찰을 전부 소유하게 하고, 걷기로 돌아오면 <c>null</c>(기본 재질)로 되돌려 PhysX의
        /// 평범한 마찰로 슬로프 위에 자연스레 서게 한다 — 채널 하나에 주인 하나.
        /// </summary>
        private void SetSlidingMaterial(bool enabled)
        {
            if (enabled == _hasSlidingMaterial) return;

            if (enabled)
            {
                _slideExitControlRemaining = 0f;
                _slideSlipDeg = 0f;
                _slideSlipVelocity = 0f;
                if (_capsule != null) _capsule.sharedMaterial = _slidingMaterial;
            }
            else
            {
                _slideExitControlRemaining = _slideExitControlSeconds;
                _slideSlipDeg = 0f;
                _slideSlipVelocity = 0f;
                SlipAngleDeg = 0f;
                if (_capsule != null) _capsule.sharedMaterial = null;
            }

            _hasSlidingMaterial = enabled;
        }

        private void EnterSliding()
        {
            if (!_controlState.TryTransitionTo(EPenguinControlState.Sliding)) return;

            SetSlidingMaterial(true);
            _slideStopElapsed = 0f;
            CancelQueuedSlide();
            ResetSlideKick();
        }

        private void ExitSlide()
        {
            SetSlidingMaterial(false);
            if (_controlState.Current == EPenguinControlState.Sliding)
                _controlState.TryTransitionTo(EPenguinControlState.Normal);
            _slideStopElapsed = 0f;
            ResetSlideKick();
            ResetMomentumState();
        }

        private void TickSlideStop(float dt, bool grounded, Vector3 groundNormal)
        {
            if (!grounded)
            {
                _slideStopElapsed = 0f;
                return;
            }

            if (SurfaceSpeed(_rigidbody.linearVelocity, groundNormal) <= _slideStopSpeed)
                _slideStopElapsed += dt;
            else
                _slideStopElapsed = 0f;

            if (_slideStopElapsed >= _slideStopDelay) ExitSlide();
        }

        /// <summary>
        /// 비등방 마찰 슬라이딩. <b>날 방향(전진)은 미끄럽고 옆 방향(그립)은 잡힌다</b> — 이
        /// 하나에서 선회 감속·선회반경(v²/(μ·g))·드리프트가 전부 창발한다. 경사 가속은 코드가
        /// 없다 — <c>Rigidbody.useGravity</c>가 진짜 중력으로 알아서 처리한다.
        /// </summary>
        private void TickSliding(float dt, in PenguinMoveInput input, bool grounded, Vector3 groundNormal, float slopeAngleDeg,
            float movementControl01)
        {
            if (_momentumHandling == null)
                _momentumHandling = GetComponent<PenguinMomentumHandling>();

            Vector3 surfaceVel = grounded
                ? Vector3.ProjectOnPlane(_rigidbody.linearVelocity, groundNormal)
                : HorizontalVelocity(_rigidbody.linearVelocity);

            float brake01 = _momentumHandling != null
                ? Mathf.Clamp01(-input.Move.y)
                : 0f;
            IsMomentumBraking = brake01 > 0.01f;
            UpdateMomentumState(dt, input.SprintHeld && !IsMomentumBraking,
                brake01, input.Move.x);

            // 핸들은 차체를 계속 돌리는 각속도가 아니라, 실제 이동 방향에 대한 목표 슬립각이다.
            // 따라서 A/D를 오래 눌러도 차체가 빙글 돌지 않는다. 차체가 비스듬해지면 아래의
            // 횡그립이 그 옆속도를 지우며 이동 방향을 끌고 와 실제 선회가 생긴다.
            float steer = movementControl01 > 0f && Mathf.Abs(input.Move.x) > 0.01f
                ? input.Move.x * movementControl01
                : 0f;
            if (_momentumHandling != null)
            {
                float totalMassKg = Mathf.Max(_baseBodyMassKg, _rigidbody.mass);
                float cargoShare01 = IsCarrying ? CargoShare01(totalMassKg) : 0f;
                steer *= _momentumHandling.SlideSteerAuthority(cargoShare01,
                    _momentumSteerCommitment01);
            }
            float tuckGrip = IsTucking ? _tuckGripMultiplier : 1f;
            float targetSlipDeg = steer * _maxSlideSlipDeg * tuckGrip;
            _slideSlipDeg = Mathf.SmoothDamp(_slideSlipDeg, targetSlipDeg,
                ref _slideSlipVelocity, _slideSlipResponseSeconds,
                Mathf.Infinity, dt);

            if (surfaceVel.sqrMagnitude > 0.01f)
            {
                Vector3 velocityDir = surfaceVel.normalized;
                Vector3 turnAxis = grounded ? groundNormal : Vector3.up;
                Vector3 targetSurfaceForward = Quaternion.AngleAxis(_slideSlipDeg, turnAxis) * velocityDir;
                Vector3 targetForward = HorizontalVelocity(targetSurfaceForward);
                if (targetForward.sqrMagnitude <= 0.0001f) targetForward = FlattenForward(transform);
                float carryLaunchAssist01 = CarryLaunchAssist01(surfaceVel.magnitude);
                if (carryLaunchAssist01 > 0f && Mathf.Abs(steer) > 0.01f)
                {
                    Vector3 bodyForward = grounded
                        ? ProjectForwardOnGround(groundNormal)
                        : FlattenForward(transform);
                    Vector3 launchTurnForward = Quaternion.AngleAxis(
                        steer * _carryLaunchTurnLeadDeg, turnAxis) * bodyForward;
                    Vector3 horizontalLaunchTurnForward = HorizontalVelocity(launchTurnForward);
                    if (horizontalLaunchTurnForward.sqrMagnitude > 0.0001f)
                    {
                        targetForward = Vector3.Slerp(targetForward.normalized,
                            horizontalLaunchTurnForward.normalized, carryLaunchAssist01);
                    }
                }
                float speed01 = Mathf.InverseLerp(0f, _slideYawHighSpeedMps, surfaceVel.magnitude);
                float yawRate = Mathf.Lerp(_slideYawRateLowSpeed, _slideYawRateHighSpeed, speed01);
                yawRate = Mathf.Lerp(yawRate, _carryLaunchYawRateDegPerSec,
                    carryLaunchAssist01);
                ApplyYawFacing(targetForward, yawRate, movementControl01);
                Vector3 surfaceForward = ProjectForwardOnGround(groundNormal);
                SlipAngleDeg = Vector3.SignedAngle(velocityDir, surfaceForward, turnAxis);
            }
            else
            {
                // 정지 중에는 기준으로 삼을 이동 방향이 없다. 이때 A/D까지 무시하면 입력이
                // 고장 난 것처럼 보이므로, 기존 저속 요 속도로 차체만 제자리 회전시킨다.
                // 추진력은 더하지 않아 위치는 그대로고, 속도가 생기면 위의 슬립각 조향으로
                // 자연스럽게 전환된다.
                float yawRate = IsCarrying
                    ? _carryLaunchYawRateDegPerSec
                    : _slideYawRateLowSpeed;
                ApplyYawRate(steer * yawRate * Mathf.Deg2Rad,
                    movementControl01);

                SlipAngleDeg = 0f;
            }

            Vector3 fwd = grounded ? ProjectForwardOnGround(groundNormal) : FlattenForward(transform);
            float forwardSpeed = Vector3.Dot(surfaceVel, fwd);
            Vector3 lateralVel = surfaceVel - fwd * forwardSpeed;

            _slideSpeedSigned = forwardSpeed;
            DesiredVelocity = fwd * forwardSpeed;
            Speed = surfaceVel.magnitude;
            HorizontalVelocityDirection = Speed > 0.001f
                ? surfaceVel / Speed
                : Vector3.zero;

            if (!grounded)
            {
                // 공중에서는 마찰도 그립도 없다 — 관성을 그대로 보존한다. 중력은 이미 물리가 준다.
                LateralGripAccel = 0f;
                ResetSlideKick();
                return;
            }

            TickSlideKick(dt, input, fwd, forwardSpeed, surfaceVel.magnitude);

            float g = GravityMagnitude;
            float normalLoad = Mathf.Cos(slopeAngleDeg * Mathf.Deg2Rad); // 급한 비탈일수록 수직항력이 준다.
            float normalForceN = _rigidbody.mass * g * Mathf.Max(0f, normalLoad);

            // 날 방향 마찰 — 눈/맨바닥을 덮인 비율로 보간. 언제나 운동을 거스른다.
            float coverage = UpdateSnowCoverage(dt);
            float muForward = _controlState != null &&
                              _controlState.Current == EPenguinControlState.Carrying
                ? _carryForwardFrictionMu
                : Mathf.Lerp(_bareFrictionMu, _snowFrictionMu, coverage);
            float forwardFrictionN = muForward * normalForceN;

            if (Mathf.Abs(forwardSpeed) > 0.001f)
            {
                float oppose = Mathf.Min(forwardFrictionN, Mathf.Abs(forwardSpeed) * _rigidbody.mass / dt);
                _rigidbody.AddForce(fwd * (-Mathf.Sign(forwardSpeed) * oppose), ForceMode.Force);
            }

            // 항력 — v² 에 비례하는 힘 도메인 계수라 질량과 무관하게 그대로 곱한다.
            float tuckDrag = IsTucking ? _tuckDragMultiplier : 1f;
            float dragN = _airDragForceN * forwardSpeed * forwardSpeed * tuckDrag;
            if (dragN > 0f && Mathf.Abs(forwardSpeed) > 0.001f)
                _rigidbody.AddForce(fwd * (-Mathf.Sign(forwardSpeed) * dragN), ForceMode.Force);

            if (IsMomentumBraking && surfaceVel.sqrMagnitude > 1e-6f)
            {
                Vector3 brakeDirection = -surfaceVel.normalized;
                float passiveOppositionN = forwardFrictionN + dragN;
                float stoppingForceN = surfaceVel.magnitude * _rigidbody.mass / dt;
                float brakeForceN = Mathf.Min(_momentumHandling.SlideBrakeForceN * brake01,
                    Mathf.Max(0f, stoppingForceN - passiveOppositionN));
                if (brakeForceN > 0f)
                    _rigidbody.AddForce(brakeDirection * brakeForceN, ForceMode.Force);
            }

            // 그립 — 옆 속도를 지운다. 이번 스텝에 완전히 지우는 데 필요한 힘을, 낼 수 있는
            // 최대 그립력으로 자른다(과잉교정으로 반대 방향 진동이 생기지 않게).
            if (lateralVel.sqrMagnitude > 1e-6f)
            {
                float maxGripForce = _lateralGripMu * normalForceN * tuckGrip;
                Vector3 desiredCancelForce = -lateralVel * _rigidbody.mass / dt;
                Vector3 gripForce = Vector3.ClampMagnitude(desiredCancelForce, maxGripForce);
                _rigidbody.AddForce(gripForce, ForceMode.Force);

                // 실제로 적용된 그립력을 가속도로 환산해 연출(PenguinBodyMotion)에 노출한다.
                // 오른쪽(transform.right) 성분의 부호를 그대로 쓴다.
                Vector3 surfaceRight = Vector3.Cross(groundNormal, fwd).normalized;
                LateralGripAccel = Vector3.Dot(gripForce, surfaceRight) / _rigidbody.mass;
            }
            else
            {
                LateralGripAccel = 0f;
            }
        }

        private void TickSlideKick(float dt, in PenguinMoveInput input, Vector3 surfaceForward,
            float forwardSpeed, float surfaceSpeedMps)
        {
            if (!input.SprintHeld || IsMomentumBraking || _slideKickForceN <= 0f)
            {
                ResetSlideKick();
                return;
            }

            _slideKickStep += _slideKickStepsPerSecond * dt;
            int stepIndex = Mathf.FloorToInt(_slideKickStep);
            float stroke01 = _slideKickStep - stepIndex;

            ActiveSlideKickFoot = stepIndex & 1;
            SlideKickStroke01 = stroke01;

            if (stroke01 >= _slideKickPowerFraction)
            {
                SlideKickPower01 = 0f;
                return;
            }

            float powerPhase01 = stroke01 / Mathf.Max(0.01f, _slideKickPowerFraction);
            float strokePower01 = Mathf.Sin(powerPhase01 * Mathf.PI);
            float kickForceN = SlideKickForceN(surfaceSpeedMps);
            float targetSpeedMps = SlideTargetSpeedMps();
            float pulseAverage01 = _slideKickPowerFraction * (2f / Mathf.PI);
            float averageFullKickN = kickForceN * pulseAverage01;
            float tuckDrag = IsTucking ? _tuckDragMultiplier : 1f;
            float flatResistanceN = _snowFrictionMu * _rigidbody.mass * GravityMagnitude
                                    + _airDragForceN * targetSpeedMps * targetSpeedMps * tuckDrag;
            float sustainPower01 = Mathf.Clamp01(flatResistanceN /
                                                  Mathf.Max(0.01f, averageFullKickN));
            float speed01 = Mathf.Max(0f, forwardSpeed) / targetSpeedMps;
            float bite01 = Mathf.Clamp01(1f - (1f - sustainPower01) * speed01 * speed01);
            SlideKickPower01 = strokePower01 * bite01;
            if (SlideKickPower01 <= 0f) return;

            _rigidbody.AddForce(surfaceForward * (kickForceN * SlideKickPower01),
                ForceMode.Force);
        }

        private float SlideKickForceN(float surfaceSpeedMps)
        {
            float totalMassKg = Mathf.Max(_baseBodyMassKg, _rigidbody.mass);
            float baseKickAcceleration = _slideKickForceN / _baseBodyMassKg;
            float experimentMultiplier = IsCarrying && _momentumHandling != null
                ? _momentumHandling.CarryPropulsionMultiplier
                : 1f;
            return totalMassKg * baseKickAcceleration * CarryMobilityMultiplier(surfaceSpeedMps) *
                   _speedBoostMultiplier * experimentMultiplier;
        }

        private float CarryMobilityMultiplier(float surfaceSpeedMps)
        {
            if (!IsCarrying)
                return 1f;

            float totalMassKg = Mathf.Max(_baseBodyMassKg, _rigidbody.mass);
            float cargoShare01 = CargoShare01(totalMassKg);
            float cruiseMultiplier = Mathf.Lerp(_carryKickLightMultiplier,
                _carryKickHeavyMultiplier, cargoShare01);
            float launchMultiplier = Mathf.Lerp(_carryLaunchLightMultiplier,
                _carryLaunchHeavyMultiplier, cargoShare01);
            return Mathf.Lerp(cruiseMultiplier, launchMultiplier,
                CarryLaunchAssist01(surfaceSpeedMps));
        }

        private bool IsCarrying => _controlState != null &&
                                   _controlState.Current == EPenguinControlState.Carrying;

        private float CarryLaunchAssist01(float surfaceSpeedMps)
        {
            if (!IsCarrying) return 0f;

            // 출발 구간만 돕고 순항 난이도는 바꾸지 않도록 속도 하나로 추진과 조향을 함께 감쇠한다.
            float speed01 = Mathf.InverseLerp(_carryLaunchAssistFullMps,
                Mathf.Max(_carryLaunchAssistFullMps + 0.01f, _carryLaunchAssistEndMps),
                surfaceSpeedMps);
            return 1f - Mathf.SmoothStep(0f, 1f, speed01);
        }

        private float SlideTargetSpeedMps()
        {
            if (_controlState == null || _controlState.Current != EPenguinControlState.Carrying)
            {
                float legacyTarget = _slideFlatTargetSpeedMps * _speedBoostMultiplier;
                MomentumTargetSpeedMps = _momentumHandling != null
                    ? _momentumHandling.SlideTargetSpeedMps(legacyTarget, _momentumBuildUp01)
                    : legacyTarget;
                return MomentumTargetSpeedMps;
            }

            float totalMassKg = Mathf.Max(_baseBodyMassKg, _rigidbody.mass);
            float cargoShare01 = CargoShare01(totalMassKg);
            MomentumTargetSpeedMps = _momentumHandling != null
                ? _momentumHandling.CarryTargetSpeedMps(cargoShare01, _momentumBuildUp01) *
                  _speedBoostMultiplier
                : Mathf.Lerp(_carryLightFlatTargetSpeedMps,
                      _carryHeavyFlatTargetSpeedMps, cargoShare01) * _speedBoostMultiplier;
            return MomentumTargetSpeedMps;
        }

        private float CargoShare01(float totalMassKg)
            => Mathf.Clamp01((totalMassKg - _baseBodyMassKg) / totalMassKg);

        private void ResetSlideKick()
        {
            _slideKickStep = 0f;
            ActiveSlideKickFoot = -1;
            SlideKickStroke01 = 0f;
            SlideKickPower01 = 0f;
        }

        private void UpdateMomentumState(float dt, bool propelling, float brake01, float steer)
        {
            if (_momentumHandling == null)
            {
                ResetMomentumState();
                return;
            }

            float buildSeconds = brake01 > 0.01f
                ? _momentumHandling.BrakeResetSeconds
                : propelling
                    ? _momentumHandling.BuildUpSeconds
                    : _momentumHandling.BuildDownSeconds;
            float buildTarget = propelling && brake01 <= 0.01f ? 1f : 0f;
            if (!propelling) MomentumTargetSpeedMps = 0f;
            _momentumBuildUp01 = Mathf.MoveTowards(_momentumBuildUp01, buildTarget,
                dt / Mathf.Max(0.01f, buildSeconds));

            float sign = Mathf.Abs(steer) > 0.01f ? Mathf.Sign(steer) : 0f;
            if (sign == 0f)
            {
                _momentumSteerCommitment01 = Mathf.MoveTowards(
                    _momentumSteerCommitment01, 0f,
                    dt / Mathf.Max(0.01f, _momentumHandling.SteerReleaseSeconds));
                if (_momentumSteerCommitment01 <= 0f) _momentumSteerSign = 0f;
                return;
            }

            if (_momentumSteerSign != 0f && sign != _momentumSteerSign)
                _momentumSteerCommitment01 = 0f;
            _momentumSteerSign = sign;
            _momentumSteerCommitment01 = Mathf.MoveTowards(
                _momentumSteerCommitment01, 1f,
                dt / Mathf.Max(0.01f, _momentumHandling.SteerCommitSeconds));
        }

        private void ResetMomentumState()
        {
            _momentumBuildUp01 = 0f;
            _momentumSteerCommitment01 = 0f;
            _momentumSteerSign = 0f;
            MomentumTargetSpeedMps = 0f;
            IsMomentumBraking = false;
        }

        /// <summary>
        /// 목표속도-현재속도 오차를 가속도로 민다(PD 제어, P항만). <c>ForceMode.Acceleration</c>은
        /// 질량을 안 곱하므로 무게와 무관하게 같은 반응성을 낸다.
        /// </summary>
        private void ApplyWalkForce(float dt, Vector3 desiredHorizontal, float control01)
        {
            Vector3 currentHorizontal = _rigidbody.linearVelocity;
            currentHorizontal.y = 0f;

            Vector3 velocityError = desiredHorizontal - currentHorizontal;
            Vector3 accel = velocityError / dt;
            accel = Vector3.ClampMagnitude(accel, _walkAccel * Mathf.Clamp01(control01));

            _rigidbody.AddForce(accel, ForceMode.Acceleration);
        }

        private void ApplyYawFacing(Vector3 moveDir, float maxYawRateDegPerSec, float authority01)
        {
            if (authority01 <= 0f) return;
            if (moveDir.sqrMagnitude <= 0.0001f)
            {
                ApplyYawRate(0f, authority01);
                return;
            }

            float errorRad = Vector3.SignedAngle(FlattenForward(transform),
                HorizontalVelocity(moveDir).normalized, Vector3.up) * Mathf.Deg2Rad;
            float maxYawRate = maxYawRateDegPerSec * Mathf.Deg2Rad;
            float targetYawRate = Mathf.Clamp(errorRad / _yawResponseSeconds,
                -maxYawRate, maxYawRate);
            ApplyYawRate(targetYawRate, authority01);
        }

        private void ApplyYawRate(float targetYawRateRad, float authority01)
        {
            float currentYawRate = Vector3.Dot(_rigidbody.angularVelocity, Vector3.up);
            float acceleration = (targetYawRateRad - currentYawRate) / _yawResponseSeconds;
            acceleration = Mathf.Clamp(acceleration, -_yawMaxAngularAcceleration,
                _yawMaxAngularAcceleration);
            _rigidbody.AddTorque(Vector3.up * (acceleration * Mathf.Clamp01(authority01)),
                ForceMode.Acceleration);
            TurnRateDegPerSec = currentYawRate * Mathf.Rad2Deg;
        }

        /// <summary>
        /// <c>v = √(2gh)</c> — 높이에서 속도를 계산해 대입한다(더하지 않는다). 그래야 낙하 중에
        /// 뛰어도, g가 나중에 바뀌어도 항상 같은 높이가 나온다.
        /// </summary>
        private bool ApplyJump(bool grounded, in PenguinMoveInput input)
        {
            if (!input.JumpPressed || !grounded || !CanJump) return false;

            float jumpSpeed = Mathf.Sqrt(2f * GravityMagnitude * _jumpHeight);
            Vector3 velocity = _rigidbody.linearVelocity;
            velocity.y = jumpSpeed;
            SetLinearVelocity(velocity);
            MarkJumped();
            return true;
        }

        private void SetLinearVelocity(Vector3 velocity)
        {
            _rigidbody.linearVelocity = velocity;
        }

        private static Vector3 CameraRelativeDirection(Vector2 move, float cameraYawDeg)
        {
            if (move.sqrMagnitude <= 0.0001f) return Vector3.zero;

            // <b>카메라 트랜스폼이 아니라 요를 받는다.</b> 데디 서버에는 원격 플레이어의 카메라가
            // 없고, 클라가 완성된 방향 벡터를 보내면 아래의 크기 클램프를 서버가 강제할 수 없다.
            Quaternion yaw = Quaternion.Euler(0f, cameraYawDeg, 0f);
            Vector3 fwd = yaw * Vector3.forward;
            Vector3 right = yaw * Vector3.right;
            Vector3 dir = fwd * move.y + right * move.x;
            return dir.sqrMagnitude > 1f ? dir.normalized : dir;
        }

        /// <summary>
        /// 발밑 지면의 법선과 경사각을 잰다. 지면이 사거리 밖이면 <c>Vector3.up</c>과 0°다.
        ///
        /// <para><c>CharacterController</c> 시절과 같은 이유로 레이를 쓴다 — 접촉 콜백은 벽에도
        /// 걸려 수직에 가까운 법선을 주고, 착지 직후 한 프레임 값이 비므로 아래로 한 번 쏘는
        /// 편이 결정적이다. <c>Rigidbody</c>의 <c>OnCollisionStay</c> 접촉 법선으로 바꾸는 것도
        /// 가능하지만, 예전에 <c>isGrounded</c> 플래그가 비탈에서 660프레임에 199회 뒤집혔다는
        /// 실측이 있어 그 플래그류 자체를 신뢰하지 않는 접근을 그대로 가져간다.</para>
        /// </summary>
        private void SampleGround(out Vector3 normal, out float slopeAngleDeg, out bool grounded,
                                  out Vector3 contactPoint, out float groundGapM)
        {
            normal = Vector3.up;
            slopeAngleDeg = 0f;
            grounded = false;
            contactPoint = transform.position;
            groundGapM = float.PositiveInfinity;

            // bounds는 캡슐이 옆으로 누워도 월드 축 기준 크기를 다시 계산한다. 기존의
            // transform.position + height/2 방식은 직립만 가정해, 캡슐이 구르면
            // 실제 바닥보다 높은 곳에서 짧은 레이를 쏘고 접지를 영원히 놓쳤다.
            Bounds bounds = _capsule.bounds;
            Vector3 origin = bounds.center;
            float maxDistance = bounds.extents.y + _groundProbeExtra;

            if (!Physics.Raycast(origin, Vector3.down, out RaycastHit hit, maxDistance, _groundLayers,
                    QueryTriggerInteraction.Ignore))
                return;

            normal = hit.normal;
            slopeAngleDeg = Vector3.Angle(normal, Vector3.up);
            grounded = true;
            contactPoint = hit.point;
            groundGapM = Mathf.Max(0f, bounds.min.y - hit.point.y);
        }

        private float UpdateSnowCoverage(float dt)
        {
            float targetCoverage = SampleSnowCoverage();
            _snowCoverage01 = Mathf.MoveTowards(_snowCoverage01, targetCoverage,
                _coverageLerpPerSecond * dt);
            return _snowCoverage01;
        }

        private float SampleSnowCoverage()
        {
            if (_snowSamplePoints == null || _snowSamplePoints.Length == 0)
                return IsSnowCovered(IsGrounded ? GroundContactPoint : transform.position) ? 1f : 0f;

            int covered = 0;
            foreach (var point in _snowSamplePoints)
            {
                if (point == null) continue;
                Vector3 sample = point.position;
                if (IsGrounded) sample.y = GroundContactPoint.y;
                if (IsSnowCovered(sample)) covered++;
            }

            return (float)covered / _snowSamplePoints.Length;
        }

        private bool IsSnowCovered(Vector3 worldPosition)
        {
            if (_snowCpuStage != null && _snowCpuStage.Field != null)
                return _snowCpuStage.TryDepthAtSupport(worldPosition, _snowSupportToleranceM,
                           out float depthM)
                       && depthM >= _snowThresholdCm * 0.01f;

            return _snowStage != null &&
                   _snowStage.DepthCmAtWorld(worldPosition) >= _snowThresholdCm;
        }

        private static Vector3 FlattenForward(Transform t)
        {
            Vector3 f = t.forward;
            f.y = 0f;
            return f.sqrMagnitude > 0.0001f ? f.normalized : Vector3.forward;
        }

        private Vector3 ProjectForwardOnGround(Vector3 groundNormal)
        {
            Vector3 forward = Vector3.ProjectOnPlane(FlattenForward(transform), groundNormal);
            if (forward.sqrMagnitude > 0.0001f) return forward.normalized;

            Vector3 velocity = Vector3.ProjectOnPlane(_rigidbody.linearVelocity, groundNormal);
            return velocity.sqrMagnitude > 0.0001f ? velocity.normalized : Vector3.forward;
        }

        private static Vector3 HorizontalVelocity(Vector3 velocity)
        {
            velocity.y = 0f;
            return velocity;
        }

        private static float SurfaceSpeed(Vector3 velocity, Vector3 groundNormal)
            => Vector3.ProjectOnPlane(velocity, groundNormal).magnitude;

        private void UpdateHorizontalVelocityDirection()
        {
            Vector3 horizontal = _rigidbody.linearVelocity;
            horizontal.y = 0f;
            HorizontalVelocityDirection = horizontal.sqrMagnitude > 0.000001f
                ? horizontal.normalized
                : Vector3.zero;
        }

        private static Vector3 FlattenRight(Transform t)
        {
            Vector3 r = t.right;
            r.y = 0f;
            return r.sqrMagnitude > 0.0001f ? r.normalized : Vector3.right;
        }
    }
}
