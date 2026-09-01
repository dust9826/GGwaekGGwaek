using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 펭귄의 <b>네트워크 진입점</b>. 변형 프리팹(`Resources/PF_PenguinNet`)에만 붙고 싱글 프리팹에는
    /// 없다.
    ///
    /// <para><b>본문은 여기 없다.</b> 걷기·점프·슬라이딩의 수치와 규칙은 전부
    /// <see cref="PenguinLocomotion.Step"/> 하나에 있고, 이 컴포넌트가 하는 일은 <c>dt</c> 와 입력을
    /// 골라 넘기는 것뿐이다. 같은 게임을 두 벌 만들지 않으려는 것이다.</para>
    ///
    /// <para><b>왜 <see cref="PenguinLocomotion"/> 자체를 <c>NetworkBehaviour</c> 로 만들지 않았나.</b>
    /// 그러면 <c>PF_Penguin</c> 에 <c>NetworkObject</c> 가 필요해지는데, 이 프로젝트에는 씬에 배치된
    /// <c>NetworkObject</c> 가 하나도 없다(전 씬 확인). 싱글 씬 셋이 쓰는 프리팹에 미검증 변경을
    /// 얹지 않는다.</para>
    ///
    /// <para><b>예측은 아직 켜지 않는다.</b> 서버만 로코모션을 돌린다 — 그러면 서버에서
    /// <c>Runner.IsForward</c> 가 항상 참이라 재시뮬레이션 위험이 통째로 사라진다(위상 누적,
    /// 에지 래치, 되감기지 않는 마찰 재질 스왑 등 일곱 가지). 대가는 자기 입력이 1 RTT 늦게
    /// 보이는 것이고, 손맛이 부족하면 이 가드를 풀고 그 일곱을 롤백 상태로 올리면 된다.
    /// 설계 근거는 `docs/specs/2026-08-24-multiplay-penguin-rebuild.md` §6-B(b).</para>
    /// </summary>
    [RequireComponent(typeof(NetworkRigidbody))]
    [DisallowMultipleComponent]
    public sealed class PenguinNetAvatar : NetworkBehaviour
    {
        private PenguinLocomotion _locomotion;
        private PenguinSnowball _snowball;
        private PenguinCarry _carry;
        private PenguinActions _actions;
        private bool _previousAttackHeld;
        private byte _renderedAttackCount;
        private PenguinInputReader _input;

        /// <summary>
        /// 이 펭귄이 붙어 있는 공. <b>클라이언트에게 알려야 하는 이유</b> — 붙기는 서버만 하므로
        /// 클라이언트의 <c>PenguinSnowball.Held</c> 는 영원히 null 이고, 그러면 로컬 HUD 가 어느 공의
        /// 타이밍을 그려야 하는지 알 수 없다. 자세도 효과도 아니고 <b>어느 공인지</b>만 보낸다.
        /// </summary>
        [Networked] private NetworkBehaviourId HeldBall { get; set; }

        /// <summary>
        /// <b>연출 상태.</b> 예측을 켜지 않았으므로 <c>PenguinLocomotion.Step</c> 은 서버에서만 돈다 —
        /// 그러면 클라이언트의 로코모션은 <c>Speed</c> 도 <c>IsGrounded</c> 도 <c>IsSliding</c> 도
        /// 영원히 기본값이라, 몸은 <c>NetworkRigidbody</c> 보정으로 움직이는데 애니메이터는 선 자세로
        /// 미끄러지고 몸통은 안 기울고 카메라의 속도 반응이 죽는다. <b>남의 펭귄만이 아니라 클라이언트에
        /// 앉은 자기 펭귄도</b> 그렇다.
        ///
        /// <para>그래서 <c>Step</c> 이 만든 결과를 보낸다. 자세가 아니라 <b>연출이 읽는 값</b>이고,
        /// 이동은 여전히 <c>NetworkRigidbody</c> 가 맡는다. 근거와 목록은
        /// <see cref="PenguinPresentation"/> 에 있다.</para>
        /// </summary>
        [Networked] private float NetSpeed { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private Vector3 NetMoveDirection { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private Vector3 NetGroundNormal { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private float NetLateralGripAccel { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private NetworkBool NetGrounded { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private NetworkBool NetSliding { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private NetworkBool NetSlidePose { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        [Networked] private byte NetJumpCount { get; set; }

        /// <inheritdoc cref="NetSpeed"/>
        /// <remarks>HUD 의 체력 바가 읽는 값. 남의 펭귄 것도 오지만 지금은 로컬 것만 그린다.</remarks>
        [Networked] private float NetStamina01 { get; set; }

        /// <inheritdoc cref="NetStamina01"/>
        [Networked] private NetworkBool NetStaminaExhausted { get; set; }

        /// <summary>
        /// 좌클릭 홀드가 서버에서 실제로 적용됐는가. 밀기 날개 자세가 읽는다. <b>로컬 입력을 읽지
        /// 않는 이유</b>는 남의 아바타에는 입력 리더가 꺼져 있기 때문이다 — 루트 <c>AGENTS.md</c> 의
        /// "연출은 복제된 상태로 그린다" 규약이 그것이다.
        /// </summary>
        [Networked] private NetworkBool NetPushing { get; set; }

        [Networked] private NetworkBool NetCarryApproaching { get; set; }

        [Networked] private NetworkBool NetCarrying { get; set; }

        /// <summary>
        /// 지금 <b>메고 있는</b> 공. <see cref="HeldBall"/> 과 같은 이유로 복제한다 —
        /// 운반은 서버만 돌리므로(<c>PenguinCarry.NetworkDriven</c>) 클라이언트의
        /// <c>PenguinCarry.IsCarrying</c> 은 <b>영원히 거짓</b>이고 <c>Cargo</c> 도 늘 null 이다.
        /// <see cref="NetCarrying"/> 은 "누가 메고 있다" 만 알려 주고 <b>어느 공인지는 모른다</b> —
        /// 성장 HUD 는 그 공을 알아야 그린다.
        /// </summary>
        [Networked] private NetworkBehaviourId CarriedBall { get; set; }

        /// <summary>공격 재생 횟수. <b>펄스가 아니라 계수기다</b> — 한 틱짜리 트리거를 복제하면
        /// 그 틱을 못 본 피어가 모션을 통째로 놓친다(점프의 <see cref="NetJumpCount"/> 와 같은 이유).
        /// 비권위 피어는 값이 늘어난 만큼 <see cref="PenguinActions.PlayAttackPose"/> 를 돌린다.</summary>
        [Networked] private byte NetAttackCount { get; set; }

        /// <summary>마지막으로 애니메이터에 흘린 점프 계수. 에지를 한 번만 쏘려고 둔다.</summary>
        private byte _lastSeenJumpCount;
        private bool _hasSeenJumpCount;

        /// <summary>복제된 <see cref="HeldBall"/> 를 이 피어의 공으로 푼다. 그리기 전용이다.</summary>
        public SnowBallCarrier HeldForPresentation
        {
            get
            {
                if (HeldBall == default || Runner == null) return null;
                return Runner.TryFindBehaviour(HeldBall, out SnowBallCarrier ball) ? ball : null;
            }
        }

        public bool CarryApproachingForPresentation => NetCarryApproaching;
        public bool CarryingForPresentation => NetCarrying;

        /// <summary>복제된 <see cref="CarriedBall"/> 을 이 피어의 공으로 푼다. 그리기 전용이다.</summary>
        public SnowBallCarrier CarriedForPresentation
        {
            get
            {
                if (CarriedBall == default || Runner == null) return null;
                return Runner.TryFindBehaviour(CarriedBall, out SnowBallCarrier ball) ? ball : null;
            }
        }

        public override void Spawned()
        {
            _locomotion = GetComponent<PenguinLocomotion>();
            _snowball = GetComponent<PenguinSnowball>();
            _carry = GetComponent<PenguinCarry>();
            _actions = GetComponent<PenguinActions>();
            _input = GetComponentInChildren<PenguinInputReader>(true);

            // ⚠ 스폰 자세를 물리 바디에 먼저 밀어 넣는다. 안 하면 원점으로 순간이동한다 —
            // RunnerSimulatePhysics 가 Physics.simulationMode 를 Script 로 바꾸면 스폰 직후에는
            // 아직 Simulate() 가 돌지 않아 PhysX 바디 자세가 트랜스폼과 동기화되기 전인데,
            // NetworkRigidbody.Spawned 는 '물리 바디' 자세를 네트워크 버퍼에 담기 때문이다
            // (SnowBallCarrier.cs:565-576 실측: (-2,1,8) 에 만들었는데 (0,6,0) 에서 떨어졌다).
            // 그래서 이 컴포넌트가 프리팹에서 NetworkRigidbody 보다 앞에 있어야 한다.
            if (TryGetComponent(out Rigidbody body))
            {
                body.position = transform.position;
                body.rotation = transform.rotation;
            }

            // 로코모션이 자기 클럭으로 또 돌지 않게 한다. 물리 애드온이 simulationMode 를 바꿔도
            // Unity 는 FixedUpdate 를 계속 부른다(SnowBallCarrier.cs:336-339).
            if (_locomotion != null) _locomotion.NetworkDriven = true;

            // 운반도 서버가 돌린다. 각 피어가 자기 키보드로 돌리면 선물이 자기 화면에서만 움직이고
            // 서버의 완료 판정은 그것을 보지 못한다.
            if (_carry != null) _carry.NetworkDriven = true;
            if (_actions != null) _actions.NetworkDriven = true;
            if (_snowball != null)
            {
                _snowball.NetworkDriven = true;

                // 협동 참가자 비트를 <b>이 아바타</b>로 통일한다. 근거는 PenguinSnowball.CoopIdentity.
                _snowball.CoopIdentity = this;
            }

            // ⚠ <b>서버가 이 아바타를 그 플레이어의 오브젝트로 등록한다.</b> 빠뜨리면 증상이
            // 네트워킹처럼 안 보인다 — <c>SnowCpuStage</c> 의 관심 반경 판정이
            // <c>TryGetPlayerObject</c> 로 각 플레이어의 위치를 찾고 못 찾은 플레이어는 건너뛴다.
            // 아무도 못 찾으면 <b>어떤 청크도 stale 로 표시되지 않아 클라이언트는 깎인 자리를 영원히
            // 못 본다</b>(폴더 AGENTS.md, 2026-08-20 실측). 실제로 2026-08-24 에 이것을 빠뜨려
            // "서버는 눈을 깎는데 화면에선 안 파인다" 로 다시 겪었다.
            //
            // 런처의 Spawn 반환값으로 하지 않는 이유도 거기 있다 — 스폰이 큐를 거치면 그 반환값이
            // null 이라 SetPlayerObject(null) 이 된다. 아바타 자신이 하면 큐를 거쳐도 한 번만 등록된다.
            if (Object.HasStateAuthority) Runner.SetPlayerObject(Object.InputAuthority, Object);

            // 늦게 합류한 피어가 그동안 쌓인 공격을 몰아 재생하지 않도록 현재 값에서 시작한다.
            _renderedAttackCount = NetAttackCount;

            // 증강 로드아웃은 씬(AugmentRig)에 있고 소비처는 프리팹에 있다. 빌더가 싱글에서 둘을
            // 물려 두지만 MultiPlaySceneBuilder 가 그 펭귄을 지우므로 멀티에서는 끊긴 채로 남는다 —
            // 그러면 WalkSpeed 증강이 아무에게도 안 걸린다.
            if (_locomotion != null)
                _locomotion.SetAugments(
                    FindFirstObjectByType<AugmentLoadout>(FindObjectsInactive.Include));

            if (HasInputAuthority)
            {
                SessionLauncher.LocalInputSource = ReadLocalNetInput;

                // 주문표의 화살표와 "들고 있는 상자" 판정은 로컬 아바타가 기준이다. 씬에는 플레이어가
                // 없으므로(서버가 스폰한다) 아바타가 스스로 자기를 넣는다.
                foreach (RequestHudPresenter hud in FindObjectsByType<RequestHudPresenter>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    hud.BindLocalPlayer(transform);

                PenguinCameraOrbit orbit = GetComponentInChildren<PenguinCameraOrbit>(true);
                foreach (RequestStageFlowPresenter flow in FindObjectsByType<RequestStageFlowPresenter>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    flow.BindLocalCameraOrbit(orbit);

                // 일시정지 메뉴도 같은 이유로 로컬 아바타를 받아야 한다. 안 주면 멀티에서 메뉴를 열어도
                // 입력과 카메라가 계속 돌고 커서가 잠긴 채라 버튼을 누를 수 없다(2026-08-31 실측).
                PenguinInputReader input = GetComponentInChildren<PenguinInputReader>(true);
                foreach (PauseMenuController pause in FindObjectsByType<PauseMenuController>(
                             FindObjectsInactive.Include, FindObjectsSortMode.None))
                    pause.BindLocalPlayer(input, orbit);

                // 눈덩이 성장 HUD 도 같은 이유로 끊긴다. 카메라까지 넘기는 것은 컨트롤러가 Bind() 에서
                // 그 카메라를 HUD 로 다시 넘기기 때문이다 — 여기서 안 주면 HUD 가 월드 좌표를 못 푼다.
                Camera localCamera = GetComponentInChildren<Camera>(true);
                foreach (SnowballGrowthPlayableSceneController growth in
                             FindObjectsByType<SnowballGrowthPlayableSceneController>(
                                 FindObjectsInactive.Include, FindObjectsSortMode.None))
                    growth.BindLocalPlayer(_snowball, localCamera);

                // 협동 타이밍 HUD 는 <b>로컬 플레이어에게만</b> 만든다. 4인이면 클라 한 대에 패널이
                // 넷 겹친다 - 카메라·오디오 리스너와 같은 부류다(DisableLocalOnly 참고).
                if (SnowBallCarrier.CoopPushEnabled) SnowballCoopTimingHud.Create(this);
            }
            else
            {
                DisableLocalOnly();
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            // 내 아바타가 사라지면 입력 공급도 멈춘다. 안 그러면 죽은 컴포넌트를 계속 읽는다.
            if (HasInputAuthority && SessionLauncher.LocalInputSource == ReadLocalNetInput)
                SessionLauncher.LocalInputSource = null;
        }

        public override void FixedUpdateNetwork()
        {
            // 예측을 켜지 않았으므로 서버만 돈다. 클라이언트는 NetworkRigidbody 의 보정을 받아 그린다.
            if (!Object.HasStateAuthority) return;
            if (_locomotion == null) return;

            // 입력이 없는 틱에도 게시한다 - 공을 놓은 사실이 그 틱에 막힐 이유가 없다.
            HeldBall = _snowball != null && _snowball.Held != null ? _snowball.Held.Id : default;

            if (!GetInput(out NetworkInputData net)) return;

            // <b>비트 둘을 합치지 않고 나눠 읽는다.</b> 하나는 "제출했다", 다른 하나는 "그 판정이
            // 성공이었다" 다. 서버는 위상을 다시 보지 않는다(PenguinMoveInput.CoopShoveSuccess).
            bool coopSuccess = net.Buttons.IsSet((int)EInputButton.CoopShoveSuccess);
            bool coopFailure = net.Buttons.IsSet((int)EInputButton.CoopShoveFailure);

            var input = new PenguinMoveInput
            {
                Move = net.Move,
                CameraYawDeg = net.CameraYawDeg,
                SprintHeld = net.Buttons.IsSet((int)EInputButton.Sprint),
                JumpPressed = net.Buttons.IsSet((int)EInputButton.Jump),
                PackSnowHeld = net.Buttons.IsSet((int)EInputButton.Action),
                CreateSnowballPressed = net.Buttons.IsSet((int)EInputButton.CreateSnowball),
                BurstPressed = net.Buttons.IsSet((int)EInputButton.Burst),
                PickupPressed = net.Buttons.IsSet((int)EInputButton.Pickup),
                CoopShovePressed = coopSuccess || coopFailure,
                CoopShoveSuccess = coopSuccess,
            };

            _locomotion.Step(Runner.DeltaTime, input);

            // 눈덩이도 서버가 돌린다. 각 피어가 자기 키보드로 돌리면 남의 공을 만지게 된다.
            if (_snowball != null)
            {
                // 팀원이 2026-08-25 에 눈덩이 조작을 한 본문으로 합쳤다(E 토글 · WASD 밀기).
                // 진입점도 하나면 된다.
                _snowball.Step(Runner.DeltaTime, input);
            }

            // 선물·눈덩이를 등에 메는 것도 서버 일이다. 화물의 자세는 NetworkRigidbody 가 그린다.
            if (_carry != null) _carry.Step(Runner.DeltaTime, input.PickupPressed);

            // 공격은 눌린 순간에만 의미가 있다. 래치를 그대로 보내면 재시뮬레이션에서 어긋나므로
            // 눌림 상태를 보내고 에지는 여기서 지난 틱과 비교해 만든다(점프와 같은 규약).
            if (_actions != null)
            {
                // 좌클릭 홀드는 이미 <see cref="EInputButton.Action"/> 으로 온다(눈덩이 뭉치기와 같은 키).
                // 새 비트를 만들지 않고 그것으로 에지를 만든다 — 와이어 포맷에 구멍을 늘리지 않는다.
                bool attackHeld = net.Buttons.IsSet((int)EInputButton.Action);
                bool attackPressed = attackHeld && !_previousAttackHeld;
                _previousAttackHeld = attackHeld;
                _actions.Step(Runner.DeltaTime, attackPressed);
            }

            PublishPresentation();
        }

        /// <summary>이번 틱이 만들어 낸 연출 상태를 복제 필드에 옮겨 담는다. 서버에서만 부른다.</summary>
        private void PublishPresentation()
        {
            PenguinPresentation p = _locomotion.CapturePresentation();
            NetSpeed = p.Speed;
            NetMoveDirection = p.HorizontalVelocityDirection;
            NetGroundNormal = p.GroundNormal;
            NetLateralGripAccel = p.LateralGripAccel;
            NetGrounded = p.Grounded;
            NetSliding = p.Sliding;
            NetSlidePose = p.SlidePose;
            NetJumpCount = p.JumpCount;
            NetStamina01 = p.Stamina01;
            NetStaminaExhausted = p.StaminaExhausted;
            NetPushing = _snowball != null && _snowball.IsPushing;
            NetCarryApproaching = _carry != null && _carry.IsApproaching;
            NetCarrying = _carry != null && _carry.IsCarrying;
            CarriedBall = _carry != null && _carry.IsCarrying && _carry.Cargo is SnowBallCarrier carried
                ? carried.Id
                : default;
            if (_actions != null) NetAttackCount = unchecked((byte)_actions.AttackCount);
        }

        /// <summary>
        /// 복제된 연출 상태를 그리기 직전에 앉힌다.
        ///
        /// <para><b>권위 피어에서는 하지 않는다.</b> <c>ApplyPresentation</c> 이 로코모션의 상태기
        /// 위치를 쓰는데, 서버에서 그것은 다음 <c>Step</c> 이 읽는 값이다 — 한 틱 늦은 복제본으로
        /// 덮으면 전이가 어긋난다. 서버는 <c>Step</c> 이 이미 같은 값을 채워 두었다.</para>
        /// </summary>
        public override void Render()
        {
            if (Object.HasStateAuthority) return;
            if (_locomotion == null) return;

            _locomotion.ApplyPresentation(new PenguinPresentation
            {
                Speed = NetSpeed,
                HorizontalVelocityDirection = NetMoveDirection,
                GroundNormal = NetGroundNormal,
                LateralGripAccel = NetLateralGripAccel,
                Grounded = NetGrounded,
                Sliding = NetSliding,
                SlidePose = NetSlidePose,
                JumpCount = NetJumpCount,
                Stamina01 = NetStamina01,
                StaminaExhausted = NetStaminaExhausted,
            });

            if (_snowball != null)
                _snowball.ApplyPresentation(HeldForPresentation, CarriedForPresentation, NetPushing);

            // 공격 모션. 계수기가 늘어난 만큼 재생한다 — 여러 틱을 한 번에 받아도 한 번만 돌리면
            // 충분하므로(같은 모션이다) 값만 맞추고 한 번 재생한다.
            if (_actions != null && NetAttackCount != _renderedAttackCount)
            {
                _renderedAttackCount = NetAttackCount;
                _actions.PlayAttackPose();
            }

            // 계수기가 바뀐 프레임에만 트리거를 쏜다. 처음 본 값은 기준점일 뿐이라 쏘지 않는다 —
            // 늦게 들어온 피어가 남의 점프 이력을 한꺼번에 재생하면 안 된다.
            if (!_hasSeenJumpCount)
            {
                _hasSeenJumpCount = true;
                _lastSeenJumpCount = NetJumpCount;
            }
            else if (_lastSeenJumpCount != NetJumpCount)
            {
                _lastSeenJumpCount = NetJumpCount;
                _locomotion.RaisePresentationJump();
            }
        }

        /// <summary>
        /// 로컬 플레이어가 <b>자기 화면의 마커</b>로 협동 타이밍을 판정해 릴레이에 남긴다.
        /// <c>SessionLauncher.OnInput</c> 이 그것을 버튼 비트로 실어 보낸다.
        ///
        /// <para><b>왜 입력 수집기에서 하지 않는가.</b> 그쪽은 <c>Core</c> 라 공도 HUD 도 모른다.
        /// 그리고 판정에 필요한 위상은 <see cref="HeldForPresentation"/> 으로 이 피어가 지금 그리고
        /// 있는 값이어야 한다 - 서버의 위상이 아니다.</para>
        ///
        /// <para>같은 누름이 여러 프레임에 걸쳐 보일 수 있다(리더의 래치는 다음 <c>FixedUpdate</c>
        /// 까지 참으로 남는다). 해롭지 않다 - 릴레이는 0.2초 펄스를 갱신할 뿐이고, 서버 쪽
        /// <c>SubmitCoopTiming</c> 은 이미 제출한 참가자를 무시한다. 펄스가 다음 창으로 새지도
        /// 않는다 - 창이 끝나고 다시 열리기까지 최소 0.35초(<c>CoopStrainDelayHardSeconds</c>)가
        /// 걸린다.</para>
        /// </summary>
        private void Update()
        {
            if (!HasInputAuthority || _input == null) return;
            if (!_input.CoopShovePressedThisFrame) return;

            SnowBallCarrier held = HeldForPresentation;
            if (held == null) return;
            if (!held.TryGetCoopTiming(this, out float phase01, out bool submitted, out _)) return;
            if (submitted) return;

            CoopShoveInputRelay.Queue(SnowBallCarrier.IsCoopTimingSuccess(phase01));
        }

        /// <summary>
        /// 로컬 플레이어의 입력을 <c>Core</c> 가 보낼 수 있는 모양으로 옮겨 담는다.
        ///
        /// <para>읽는 규칙 자체는 <see cref="PenguinLocomotion.ReadLocalInput"/> 하나에 있다 —
        /// 여기서 다시 읽으면 싱글과 멀티의 조작이 갈린다.</para>
        /// </summary>
        private NetworkInputData ReadLocalNetInput()
        {
            var data = new NetworkInputData();
            if (_locomotion == null) return data;

            PenguinMoveInput local = _locomotion.ReadLocalInput();
            data.Move = local.Move;
            data.CameraYawDeg = local.CameraYawDeg;
            data.Buttons.Set((int)EInputButton.Sprint, local.SprintHeld);
            data.Buttons.Set((int)EInputButton.Jump, local.JumpPressed);
            data.Buttons.Set((int)EInputButton.Action, local.PackSnowHeld);
            data.Buttons.Set((int)EInputButton.CreateSnowball, local.CreateSnowballPressed);
            data.Buttons.Set((int)EInputButton.Burst, local.BurstPressed);
            data.Buttons.Set((int)EInputButton.Pickup, local.PickupPressed);
            return data;
        }

        /// <summary>
        /// 남의 아바타에서 로컬 전용을 끈다.
        ///
        /// <para><b>안 끄면 증상이 네트워킹처럼 안 보인다.</b> 4인이면 클라 한 대에 카메라 4개와
        /// 오디오 리스너 4개가 스폰되고, <c>PenguinCameraOrbit.OnEnable</c> 이 각각
        /// <c>Cursor.lockState</c> 를 잡는다.</para>
        /// </summary>
        private void DisableLocalOnly()
        {
            // <b>타입으로 찾는다.</b> 직렬화 배열로 두면 프리팹에 배선이 하나 더 생기고, 누가
            // 자식을 추가했을 때 조용히 빠진다. 여기 나열된 넷이 "로컬 플레이어에게만 존재해야
            // 하는 것" 의 전부이고, 그 목록 자체가 이 메서드의 내용이다.
            foreach (PenguinCameraOrbit c in GetComponentsInChildren<PenguinCameraOrbit>(true)) c.enabled = false;
            foreach (PenguinInputReader c in GetComponentsInChildren<PenguinInputReader>(true)) c.enabled = false;
            foreach (Camera c in GetComponentsInChildren<Camera>(true)) c.enabled = false;
            foreach (AudioListener c in GetComponentsInChildren<AudioListener>(true)) c.enabled = false;
        }
    }
}
