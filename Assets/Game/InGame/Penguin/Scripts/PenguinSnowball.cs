using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 펭귄의 눈덩이 — <b>뭉치기 · 붙기 · 밀기 · 터뜨리기.</b>
    ///
    /// <para><b>왜 `Player/PenguinSnowballControl` 을 옮겨 왔는가 (2026-08-21):</b> 그쪽은
    /// <see cref="PenguinProtoMotor"/>(속도 대입 Rigidbody) 위에 서 있고, 조작이 카메라 기준
    /// WASD + 마우스 시선으로 바뀌면서 그 스택이 <see cref="PenguinLocomotion"/>으로 교체됐다.
    /// 두 스택에 같은 기능을 두면 폴더 규칙이 금지하는 "기존 관례 옆의 두 번째 관례" 가 된다.</para>
    ///
    /// <para><b>2026-08-22 Phase 4: 펭귄–눈덩이 충돌을 진짜로 켰다.</b> CC 시절엔 CC가 공의
    /// 곡면을 타고 올라가는 문제(실측 접촉 106회, 밀기 0회) 때문에 충돌을 꺼 두고(
    /// <c>Physics.IgnoreCollision</c>) <c>PenguinLocomotion.BlockNormal</c>로 "안 밀리는 공
    /// 앞에서 제자리걸음"을 손으로 흉내 냈다. CC가 사라진 지금은 평범한 캡슐-구 충돌이라 그
    /// 문제 자체가 없다 — 두 메커니즘 다 삭제했고, 표면을 못 파고드는 것도 솔버가 공짜로
    /// 처리한다. <see cref="SubmitPush"/> 는 그대로 남는다 — 협동 밀기 합산·타이밍 미니게임처럼
    /// 순수 충돌이 아닌 게임플레이 규칙이라서다.</para>
    ///
    /// <para>붙은 펭귄은 공을 직접 움직이지 않는다. 각자 카메라 기준 의도 속도와 한 사람의 최대
    /// 힘을 <see cref="SnowBallCarrier.SubmitPush"/> 로 제출하고, 공이 같은 물리 스텝의 모든 힘을
    /// 합쳐 경사면 접선에 한 번 적용한다. 그래서 같은 방향은 더해지고 반대 방향은 상쇄된다.</para>
    ///
    /// <para>공을 선택한 뒤 Space를 누르면 간단한 탑승 연출이 된다. 펭귄은 공 꼭대기를 따라가지만 힘
    /// 계산은 옆에서 미는 펭귄과 같다 — 위치에 따른 지렛대나 체중은 더하지 않는다. 탑승 중엔
    /// <c>_body.isKinematic = true</c>로 바꿔 <see cref="LateUpdate"/>의 직접 위치 대입이 물리
    /// 시뮬레이션과 싸우지 않게 한다.</para>
    ///
    /// 조작: <b>E</b> 전방 눈덩이 지정/발밑 눈으로 새 눈덩이 만들기/지정 해제 · <b>WASD</b>
    /// 지정한 눈덩이 밀기·방향 변경 · <b>Space</b> 공 위 탑승 · <b>Q</b> 터뜨리기.
    ///
    /// <para>⚠ <b>우클릭 협동 밀치기 타이밍은 2026-09-01 에 껐다</b>(싱글·멀티 양쪽).
    /// 스위치는 <see cref="SnowBallCarrier.CoopPushEnabled"/> 하나이고 거기에 근거를 적어 뒀다.
    /// 여러 명이 미는 힘을 <b>합산</b>하는 것은 그대로 남는다 — 껐다는 것은 타이밍 미니게임뿐이다.</para>
    /// </summary>
    [RequireComponent(typeof(PenguinLocomotion))]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    [DefaultExecutionOrder(-100)]
    public sealed class PenguinSnowball : MonoBehaviour
    {
        [Header("접점")]
        [SerializeField] private PenguinInputReader _input;
        [Tooltip("비우면 씬에서 찾는다.")]
        [SerializeField] private SnowCpuStage _stage;

        [Header("붙기")]
        [Tooltip("이 거리 안의 눈덩이에 붙는다(m). 공 표면까지의 거리다 — 공이 커지면 자동으로 넓어진다.")]
        [SerializeField, Min(0.1f)] private float _reachM = 1.2f;
        [Tooltip("전방 좌우 이 각도 안에 있는 눈덩이만 선택 후보로 본다.")]
        [SerializeField, Range(5f, 90f)] private float _selectionHalfAngleDeg = 55f;

        [Header("밀기")]
        [Tooltip("표면이 이만큼 안에 들어오면 닿은 것으로 본다(m). 붙어 있어도 걸어서 떨어지면 밀리지 않는다.")]
        [SerializeField, Min(0.01f)] private float _contactSlackM = 0.22f;

        /// <summary>지금 붙어 있는 눈덩이. 없으면 null. HUD·검증이 읽는다.</summary>
        public SnowBallCarrier Held { get; private set; }

        /// <summary>
        /// <b>연출이 읽는 눈덩이.</b> 붙기는 권위 피어만 하므로 클라이언트의 <see cref="Held"/> 는
        /// 항상 null 이다 — 그대로 두면 남의 펭귄도 내 펭귄도 밀기 자세를 안 잡는다. 비권위 피어에서는
        /// 복제된 것(<c>PenguinNetAvatar.HeldForPresentation</c>)을 쓴다.
        /// </summary>
        public SnowBallCarrier HeldForPose => Held != null ? Held : _presentationHeld;

        private SnowBallCarrier _presentationHeld;
        private SnowBallCarrier _presentationCarried;

        /// <summary>
        /// <b>연출이 읽는, 지금 메고 있는 공.</b> 운반은 권위만 돌리므로 클라이언트의
        /// <see cref="PenguinCarry"/> 는 아무것도 모른다 — 복제된 것을 쓴다. 권위 피어에서는
        /// <c>PenguinCarry</c> 가 진실을 갖고 있으므로 이 값이 null 이어도 상관없다.
        /// </summary>
        public SnowBallCarrier CarriedForPose => _presentationCarried;

        /// <summary>
        /// 복제된 연출 상태를 앉힌다. <b>비권위 피어 전용</b> — 권위 피어는 <see cref="Step"/> 가
        /// 같은 값을 스스로 만든다.
        /// </summary>
        public void ApplyPresentation(SnowBallCarrier heldForPose, SnowBallCarrier carriedForPose,
            bool isPushing)
        {
            _presentationHeld = heldForPose;
            _presentationCarried = carriedForPose;
            IsPushing = isPushing;
        }

        /// <summary>
        /// 협동 밀기에서 <b>이 펭귄을 가리키는 신원</b>. 싱글은 자기 자신, 멀티는
        /// <see cref="PenguinNetAvatar"/> 다.
        ///
        /// <para><b>왜 필요한가.</b> <c>SnowBallCarrier.ParticipantBit</c> 는 넘겨받은 것이
        /// <c>NetworkBehaviour</c> 면 <c>InputAuthority.PlayerId</c> 비트를 주고, 아니면 자기 안의
        /// 로컬 슬롯을 준다. 이 컴포넌트는 <c>MonoBehaviour</c> 라 서버가 슬롯 비트로 참가자를
        /// 등록하는데 클라이언트의 HUD 는 아바타로 조회해 PlayerId 비트를 묻는다 — 두 비트가 달라서
        /// <b>클라이언트에게는 타이밍 창이 아예 열리지 않는다.</b> 신원을 하나로 맞춘다.</para>
        /// </summary>
        public Component CoopIdentity { set => _coopIdentity = value; }

        private Component _coopIdentity;

        private Component Participant => _coopIdentity != null ? _coopIdentity : this;

        /// <summary>
        /// 네트워크 진입점이 켠다. 켜지면 이 컴포넌트는 자기 키보드를 읽지 않는다 — 데디 서버에는
        /// 키보드가 없고, 각 피어가 자기 입력으로 남의 공을 만지면 안 된다.
        /// </summary>
        public bool NetworkDriven { set => _networkDriven = value; }

        private bool _networkDriven;

        /// <summary>마지막 뭉치기가 실패한 이유. 화면에 띄우려면 이것을 쓴다.</summary>
        public string LastFailure { get; private set; } = string.Empty;

        /// <summary>검증용 — 실제로 공에 힘을 준 횟수. 0 이면 밀기가 한 번도 안 일어났다.</summary>
        public static int DebugPushes;

        /// <summary>전방에 E로 선택할 수 있는 눈덩이가 있는가. 로컬 안내 HUD가 읽는다.</summary>
        public bool CanSelectNearbyBall => HeldForPose == null && _selectionCandidate != null;

        /// <summary>이번 물리 스텝에 선택한 눈덩이에 의도적인 힘을 제출했는가.</summary>
        public bool IsPushing { get; private set; }

        /// <summary>실험 조작에서 현재 공에 브레이크를 제출하고 있는가.</summary>
        public bool IsMomentumBraking { get; private set; }

        /// <summary>실험 조작의 눈덩이 누적 추진 단계.</summary>
        public float MomentumBuildUp01 => _momentumHandling != null ? _momentumBuildUp01 : 0f;

        /// <summary>실험 조작에서 이번 스텝에 요구한 눈덩이 속도(m/s).</summary>
        public float MomentumTargetSpeedMps { get; private set; }

        private PenguinLocomotion _locomotion;
        private PenguinControlState _controlState;
        private Rigidbody _body;
        private CapsuleCollider _capsule;
        private PenguinCameraOrbit _cameraOrbit;
        private Camera _viewCamera;
        private bool _mountedOnTop;
        private SnowBallCarrier _selectionCandidate;
        private PenguinMomentumHandling _momentumHandling;
        private float _momentumBuildUp01;
        private float _momentumSteerCommitment01;
        private float _momentumSteerSign;

        private bool CanUseSnowballInput => _controlState == null ||
            _controlState.Current is not (EPenguinControlState.CarryApproach or
                EPenguinControlState.Carrying);

        /// <summary>공 위에서 달리는 연출로 힘을 보태는 중인가.</summary>
        public bool IsMountedOnTop => _mountedOnTop;

        private void Awake()
        {
            _locomotion = GetComponent<PenguinLocomotion>();
            _controlState = GetComponent<PenguinControlState>();
            _body = GetComponent<Rigidbody>();
            _capsule = GetComponent<CapsuleCollider>();
            _momentumHandling = GetComponent<PenguinMomentumHandling>();
            _cameraOrbit = GetComponentInChildren<PenguinCameraOrbit>(true);
            _viewCamera = GetComponentInChildren<Camera>(true);
            if (_input == null) _input = GetComponent<PenguinInputReader>();

            // <b>멀티에서는 진입점이 만든다.</b> Awake 는 남의 아바타에서도 도는데 HUD 는 로컬
            // 플레이어에게만 있어야 하고, 클라이언트에서는 <see cref="Held"/> 가 항상 null 이라
            // 복제된 공을 읽는 다른 경로가 필요하다.
            if (SnowBallCarrier.CoopPushEnabled && !TryGetComponent(out PenguinNetAvatar _))
                SnowballCoopTimingHud.Create(this);
        }

        private void Update()
        {
            _selectionCandidate = CanUseSnowballInput && HeldForPose == null
                ? FindSelectableBall()
                : null;
        }

        /// <summary>검증용 즉시 동작. 앞에 공이 있으면 붙고, 없으면 새로 뭉친다.</summary>
        public void BeginPush()
        {
            if (!CanUseSnowballInput || Held != null) return;
            if (TryGrabNearby()) return;
            TryGather();
        }

        /// <summary>
        /// Q 한 번. <b>붙어 있는 공을 터뜨린다.</b>
        ///
        /// <para>크기가 아니라 사람이 정한다 — 무게는 "더 키우지 말라" 는 압력일 뿐이고, 어디서
        /// 그만둘지는 플레이어의 선택이어야 한다. 요청만 남기고 실제 터짐은 서버가 다음 틱에
        /// 처리한다(<see cref="SnowBallCarrier.ServerBurstRequested"/>).</para>
        /// </summary>
        public void Burst()
        {
            if (Held == null) return;
            Held.ServerBurstRequested = true;
            Release();
        }

        /// <summary>뗀다. 여기서부터는 물리다 — 경사면 굴러 내려간다.</summary>
        public void Release()
        {
            if (Held != null) Held.ReleaseTop(this);
            SetMountedOnTop(false);
            Held = null;
            if (_controlState != null && _controlState.IsSnowballState)
                _controlState.TryTransitionTo(EPenguinControlState.Normal);
            if (_cameraOrbit != null) _cameraOrbit.LookTarget = null;
            ResetMomentumState();
        }

        /// <summary>
        /// 탑승 상태를 바꾸는 유일한 자리. <b>탑승 중엔 <c>isKinematic = true</c>다</b> —
        /// <see cref="LateUpdate"/>가 매 프레임 <c>transform.position</c>을 직접 대입해 공
        /// 꼭대기에 앵커링하는데, 논-키네마틱 상태로 두면 그 대입이 물리 시뮬레이션(중력·충돌)과
        /// 매 프레임 싸운다. 키네마틱이면 그 대입이 정상적인 사용법이 된다.
        /// </summary>
        private void SetMountedOnTop(bool mounted)
        {
            _mountedOnTop = mounted;
            if (_body != null) _body.isKinematic = mounted;
        }

        /// <summary>
        /// <b>의도적인 밀기는 펭귄이 힘을 직접 적용하지 않고 공에 제출한다.</b> 협동 시 여러
        /// 펭귄의 힘을 한 물리 스텝에 합치기 위해서다(<see cref="SnowBallCarrier.SubmitPush"/>).
        ///
        /// <para><b>2026-08-22부터 펭귄–공 충돌 자체는 진짜다.</b> CC 시절엔 CC가 공의 곡면을
        /// 타고 올라가는 문제(실측 접촉 106회, 밀기 0회) 때문에 충돌을 꺼 두고 표면 간격을
        /// 손으로 재서 막아야 했다. 지금은 평범한 캡슐-구 충돌이라 그 문제가 없다 — 표면을 못
        /// 파고드는 것도, 안 밀리는 공 앞에서 막히는 것도 전부 솔버가 처리한다. 여기 남은 일은
        /// "붙어 있어도 닿을 때만 의도적으로 민다"는 게임플레이 판정뿐이다.</para>
        ///
        /// <para><b>2026-08-22 Phase 6: 접촉 간격도 XZ 평면 거리로 잰다.</b> 예전엔 3D
        /// 거리(<c>Vector3.magnitude</c>)로 쟀는데, 캡슐 중심은 지상 0.85m·작은 공은 자기
        /// 반지름 높이(0.2~0.3m)에 있어 그 높이차만으로 3D 거리가 부풀었다. <see
        /// cref="TickSideOrbit"/>가 정확히 XZ 반지름(공 반지름+몸 반지름)으로 궤도를 잡아 놓아도,
        /// 3D 거리로 문턱을 재면 항상 <see cref="_contactSlackM"/>을 넘겨 붙어 있어도 영원히
        /// 못 미는 상태가 됐다(실측, PlayMode에서 처음 발견) — 궤도와 문턱의 척도가 서로
        /// 달랐던 것이 원인이다. 궤도와 같은 척도(XZ)로 통일한다.</para>
        /// </summary>
        private void FixedUpdate()
        {
            if (_networkDriven || _input == null) return;

            // 싱글은 마커를 보는 쪽이 곧 본문을 도는 쪽이라 여기서 판정한다. 멀티에서는 같은 일을
            // <see cref="PenguinNetAvatar"/> 가 자기 화면에서 하고 결과만 보낸다.
            PenguinMoveInput local = _locomotion.ReadLocalInput();
            if (local.CoopShovePressed) local.CoopShoveSuccess = JudgeCoopTimingLocally();
            Step(Time.fixedDeltaTime, local);
        }

        /// <summary>지금 붙어 있는 공의 타이밍을 <b>내 화면의 위상</b>으로 판정한다.</summary>
        private bool JudgeCoopTimingLocally() =>
            Held != null
            && Held.TryGetCoopTiming(Participant, out float phase01, out _, out _)
            && SnowBallCarrier.IsCoopTimingSuccess(phase01);

        /// <summary>
        /// 눈덩이 조작 본문. <b>진입점이 둘이어도 이 본문은 하나다</b> — 싱글은
        /// <c>FixedUpdate</c> 가, 멀티는 <see cref="PenguinNetAvatar"/> 가 확정된 틱에서 부른다.
        /// </summary>
        public void Step(float dt, in PenguinMoveInput input)
        {
            if (_momentumHandling == null)
                _momentumHandling = GetComponent<PenguinMomentumHandling>();

            IsPushing = false;
            IsMomentumBraking = false;
            MomentumTargetSpeedMps = 0f;
            if (!CanUseSnowballInput)
            {
                if (Held != null) Release();
                return;
            }
            if (input.PickupPressed) return;
            if (Held == null && _controlState != null && _controlState.IsSnowballState)
                Release();
            if (input.CreateSnowballPressed)
            {
                if (Held != null) Release();
                else if (!TryGrabNearby()) TryGather();
            }

            if (input.BurstPressed) Burst();

            // <b>여기서 다시 판정하지 않는다.</b> 판정은 마커를 보는 쪽이 이미 했고
            // (<see cref="PenguinMoveInput.CoopShoveSuccess"/>), 이 본문은 서버에서도 도는데
            // 서버 위상은 클라이언트가 본 위상이 아니다.
            if (input.CoopShovePressed && Held != null &&
                Held.TryGetCoopTiming(Participant, out _, out bool submitted, out _) && !submitted)
            {
                Held.SubmitCoopTiming(Participant, input.CoopShoveSuccess);
            }

            if (Held == null) return;

            // 눈덩이 옆 밀기 상태에서는 Space 탑승을 우선 비활성화한다.

            var body = Held.GetComponent<Rigidbody>();
            if (body == null || body.isKinematic) return;

            float brake01 = _momentumHandling != null
                ? Mathf.Clamp01(-input.Move.y)
                : 0f;
            bool propelling = input.Move.y > 0.01f && brake01 <= 0.01f;
            UpdateMomentumState(dt, propelling, brake01, input.Move.x);

            if (_mountedOnTop)
            {
                if (_locomotion.DesiredVelocity.sqrMagnitude > 1e-6f)
                {
                    Held.SubmitPush(Participant, _locomotion.DesiredVelocity, SnowBallCarrier.PlayerPushForceN);
                    IsPushing = true;
                    DebugPushes++;
                }
                return;
            }

            TickSideOrbit(dt, input, body);

            Vector3 bodyCenter = _capsule != null
                ? transform.TransformPoint(_capsule.center)
                : transform.position;
            Vector3 towardBall = body.position - bodyCenter;
            towardBall.y = 0f; // 궤도와 같은 척도 — 높이차를 접촉 판정에 섞지 않는다.
            float dist = towardBall.magnitude;
            if (dist < 1e-4f) return;

            float gap = dist - Held.RadiusM - BodyRadiusM;

            // 걸어서 떨어졌다 - 붙어 있어도 닿지 않으면 밀리지 않는다.
            if (gap > _contactSlackM) return;

            if (_momentumHandling != null) Held.SubmitMomentumCoast();

            if (brake01 > 0.01f)
            {
                Held.SubmitBrake(Participant,
                    _momentumHandling.SnowballBrakeForceN * brake01);
                IsMomentumBraking = true;
                return;
            }

            float pushSpeed = Mathf.Max(0f, input.Move.y) * _locomotion.WalkSpeedMps;
            if (_momentumHandling != null && pushSpeed > 0f)
            {
                pushSpeed = _momentumHandling.SnowballTargetSpeedMps(
                    _locomotion.WalkSpeedMps, Held.GrowthProgress01, _momentumBuildUp01);
                MomentumTargetSpeedMps = pushSpeed;
            }
            Vector3 want = towardBall.sqrMagnitude > 1e-6f
                ? towardBall.normalized * pushSpeed
                : Vector3.zero;
            if (want.sqrMagnitude > 1e-6f)
            {
                if (_momentumHandling != null)
                    Held.SubmitMomentumPush(Participant, want,
                        SnowBallCarrier.PlayerPushForceN *
                        _momentumHandling.SnowballPropulsionMultiplier);
                else
                    Held.SubmitPush(Participant, want, SnowBallCarrier.PlayerPushForceN);
                IsPushing = true;
                DebugPushes++;
            }
        }

        private void LateUpdate()
        {
            if (!_mountedOnTop || Held == null) return;

            transform.position = Held.transform.position + Vector3.up * (Held.RadiusM + 0.03f);
        }

        /// <summary>
        /// A/D를 일반 횡이동 대신 공 중심을 기준으로 한 둘레 이동으로 바꾼다.
        ///
        /// <para>2026-08-22 Phase 4에서 <c>Update</c>가 아니라 <see cref="FixedUpdate"/>에서
        /// 호출하도록 옮겼다 — <c>MovePosition</c>은 물리 스텝 사이에 여러 번 부르면 마지막
        /// 호출만 적용되는데, <c>FixedUpdate</c>는 스텝당 정확히 한 번만 돌므로 이 문제 자체가
        /// 사라진다.</para>
        ///
        /// <para><b>2026-08-22 Phase 6: 3D 구면 계산을 걷어내고 순수 XZ 원 궤도로 단순화했다.</b>
        /// 예전 코드는 캡슐 <b>중심</b>(지상 0.85m)과 공 <b>중심</b>(지상 반지름 높이, 작은 공은
        /// 0.2~0.3m)의 수직 차이를 <c>centerDistance² − vertical²</c>로 구면 반지름에서 빼는
        /// 방식이었는데, 실측(2026-08-22, PlayMode 테스트로 처음 실행해 발견)으로는 이 수직차가
        /// <c>centerDistance</c>(≈0.64m)보다 쉽게 커져서 근호 안이 음수가 되고, 최솟값
        /// 0.01(반지름 0.1m)로 주저앉았다 — 펭귄이 공 위에 거의 겹치도록 빨려 들어가 밀기 방향이
        /// 어긋나고 <c>SubmitPush</c>가 통째로 안 나갔다. <b>이 코드는 CC 시절부터 그대로였고
        /// 캡슐 <c>center</c> 값도 CC와 동일해서, Phase 1~5의 Rigidbody 재작성과 무관한 사전
        /// 존재 버그다</b> — 이번에 테스트를 실제로 돌려서 처음 드러났다.</para>
        ///
        /// <para>애초에 3D 구면 기하가 필요 없다 — 둘레 돌기는 <b>지면 위 원 운동</b>이지 공을
        /// 감싸는 구면 위 이동이 아니다. 높이는 서로 독립이므로(공은 자기 반지름 높이에 얹혀
        /// 있고 펭귄은 발밑 지면에 서 있다) 수평 반지름을 <c>공 반지름 + 몸 반지름</c>으로 바로
        /// 정하면 충분하다.</para>
        /// </summary>
        private void TickSideOrbit(float dt, in PenguinMoveInput input, Rigidbody ballBody)
        {
            if (_capsule == null || _body == null || Held == null) return;

            Vector3 bodyCenter = transform.TransformPoint(_capsule.center);
            Vector3 radial = bodyCenter - Held.transform.position;
            radial.y = 0f;
            if (radial.sqrMagnitude < 1e-6f) radial = -transform.forward;

            float horizontalRadius = Held.RadiusM + BodyRadiusM + 0.02f;
            float steerAuthority = 1f;
            if (_momentumHandling != null)
            {
                Vector3 tangentVelocity = Vector3.ProjectOnPlane(ballBody.linearVelocity,
                    Held.SupportNormal);
                steerAuthority = _momentumHandling.SnowballSteerAuthority(
                    Held.GrowthProgress01, tangentVelocity.magnitude,
                    _momentumSteerCommitment01);
            }
            float angleDeg = -input.Move.x * steerAuthority * _locomotion.WalkSpeedMps /
                             horizontalRadius * Mathf.Rad2Deg * dt;
            Vector3 nextRadial = Quaternion.AngleAxis(angleDeg, Vector3.up) * radial.normalized;
            Vector3 targetCenter = Held.transform.position + nextRadial * horizontalRadius;

            Vector3 delta = targetCenter - bodyCenter;
            delta.y = 0f;
            _body.MovePosition(_body.position + delta);

            Vector3 face = Held.transform.position - transform.position;
            face.y = 0f;
            if (face.sqrMagnitude > 1e-6f)
                transform.rotation = Quaternion.LookRotation(face.normalized, Vector3.up);
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
            float target = propelling && brake01 <= 0.01f ? 1f : 0f;
            _momentumBuildUp01 = Mathf.MoveTowards(_momentumBuildUp01, target,
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

        /// <summary>펭귄 몸의 반지름. 공을 <b>몸 밖에</b> 두려면 이것이 필요하다.</summary>
        private float BodyRadiusM
        {
            get
            {
                if (_capsule == null) _capsule = GetComponent<CapsuleCollider>();
                if (_capsule == null) return 0.4f;

                Vector3 sc = transform.localScale;
                return _capsule.radius * Mathf.Max(Mathf.Abs(sc.x), Mathf.Abs(sc.z));
            }
        }

        private bool TryGrabNearby()
        {
            SnowBallCarrier best = FindSelectableBall();
            if (best == null) return false;
            if (!_controlState.TryTransitionTo(EPenguinControlState.SnowballSide)) return false;

            Held = best;
            SetMountedOnTop(false);
            if (_cameraOrbit != null) _cameraOrbit.LookTarget = best.transform;
            _selectionCandidate = null;
            LastFailure = string.Empty;
            return true;
        }

        private SnowBallCarrier FindSelectableBall()
        {
            SnowBallCarrier best = null;
            float bestDist = float.MaxValue;
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude <= 1e-6f) forward = Vector3.forward;
            forward.Normalize();
            float minDot = Mathf.Cos(_selectionHalfAngleDeg * Mathf.Deg2Rad);

            foreach (SnowBallCarrier ball in FindObjectsByType<SnowBallCarrier>(FindObjectsSortMode.None))
            {
                if (ball.gameObject.scene != gameObject.scene) continue;
                if (!IsVisible(ball)) continue;

                Vector3 d = ball.transform.position - transform.position;
                d.y = 0f;
                float centerDistance = d.magnitude;
                if (centerDistance <= 1e-4f || Vector3.Dot(forward, d / centerDistance) < minDot) continue;

                float surface = centerDistance - ball.RadiusM;
                if (surface > _reachM || surface >= bestDist) continue;

                bestDist = surface;
                best = ball;
            }

            return best;
        }

        private void TryGather()
        {
            if (_stage == null) _stage = FindAnyObjectByType<SnowCpuStage>();
            if (_stage == null) { LastFailure = "이 씬에 눈이 없다"; return; }

            SnowBallCarrier made = _stage.TryCreateBall(transform.position);

            if (made == null)
            {
                LastFailure = $"눈이 얕아 뭉칠 수 없다 (걷은 양 {_stage.LastGatheredMm} " +
                              $"< 필요 {SnowBallCpu.MinCreateMassMm})";
                return;
            }

            Held = made;
            SetMountedOnTop(false);
            _controlState.TryTransitionTo(EPenguinControlState.SnowballSide);
            if (_cameraOrbit != null) _cameraOrbit.LookTarget = made.transform;
            _selectionCandidate = null;
            LastFailure = string.Empty;
        }

        private void TryMountOnTop()
        {
            if (Held == null || _controlState.Current != EPenguinControlState.SnowballSide
                             || !Held.TryClaimTop(this)) return;
            if (!_controlState.TryTransitionTo(EPenguinControlState.SnowballTop))
            {
                Held.ReleaseTop(this);
                return;
            }

            SetMountedOnTop(true);
        }

        private bool IsVisible(SnowBallCarrier ball)
        {
            if (_viewCamera == null) _viewCamera = GetComponentInChildren<Camera>(true);
            if (_viewCamera == null) return true;

            Vector3 viewport = _viewCamera.WorldToViewportPoint(ball.transform.position);
            const float margin = 0.1f;
            return viewport.z > 0f && viewport.x >= -margin && viewport.x <= 1f + margin &&
                   viewport.y >= -margin && viewport.y <= 1f + margin;
        }
    }
}
