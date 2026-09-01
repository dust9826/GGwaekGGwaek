using Fusion;
using Fusion.Addons.Physics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.VFX;
using UnityEngine.Serialization;

namespace PPack
{
    public enum ESnowBallGrowthStage
    {
        Seed = 0,
        Stage1 = 1,
        Stage2 = 2,
        Stage3 = 3,
        Stage4 = 4,
    }

    /// <summary>
    /// 씬에 놓인 눈덩이. 눈을 걷고 성장 가중치만큼 질량을 취득하는 계산은 <see cref="SnowCpuStage"/> 가 서버에서
    /// 굴리고(<see cref="SnowBallCpu"/>), 이 컴포넌트는 그 결과를 복제해 크기·질량에 반영한다.
    /// 운동 쪽에서는 여러 펭귄의 힘을 모아 서버 Rigidbody에 한 번 적용한다.
    ///
    /// <para><b>운동은 서버의 Unity 물리다.</b> 경사가 필드에 없기 때문이다 — 격자는 눈 깊이
    /// (<see cref="SnowHeightFieldCpu.HeightMm"/>) 하나뿐이고 지면은 마처의 <c>_GroundY</c> 스칼라
    /// 하나다. 그래서 "경사에서 놓치면 굴러 내려간다" 를 필드에서 만들 수는 없고, 맵 콜라이더 위에서
    /// <c>Rigidbody</c> 로 굴리면 공짜로 얻는다. 대가는 공의 운동이 정수 결정론 밖이라는 것이고,
    /// 이 프로젝트는 이미 <b>서버 권위 + 결과 복제</b>이므로(원인 복제는 실측으로 폐기됐다 —
    /// <c>AGENTS.md</c> 전파 절) 구조가 어긋나지 않는다.</para>
    ///
    /// <para>⚠ <b>공은 지면 위에 서고 눈 표면 위에 서지 않는다.</b> 눈은 레이마칭된 필드라 콜라이더가
    /// 없다. 얕은 눈에서는 안 보이지만, 깊은 더미 위로 공을 올릴 일이 생기면 그때 필요한 것은
    /// 콜라이더가 아니라 접지 높이 질의(<see cref="SnowCpuStage.HeightAtM"/>)다.</para>
    /// </summary>
    [RequireComponent(typeof(NetworkRigidbody))]
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(100)]
    public sealed class SnowBallCarrier : NetworkBehaviour
    {
        /// <summary>다져진 눈의 밀도(kg/m³). 무게가 커질수록 밀기 힘든 것이 이 값에서 나온다.</summary>
        public const float SnowDensityKgPerM3 = 400f;

        /// <summary>
        /// 펭귄 한 명이 눈덩이에 실을 수 있는 최대 힘(N). 싱글·멀티 조작이 이 값 하나를 공유한다.
        /// </summary>
        public const float PlayerPushForceN = 7800f;

        /// <summary>
        /// 함께 밀기(우클릭 타이밍 미니게임)를 켤 것인가. <b>2026-09-01 에 껐다</b> — 사용자 요청으로
        /// 싱글·멀티 양쪽에서 뺐다.
        ///
        /// <para><b>지우지 않고 끈 이유.</b> 되살릴 수 있게 두려는 것이고, 끄는 자리를 하나로 두면
        /// 되살릴 자리도 하나다. 이 값 하나가 판정(<see cref="TickCoopTiming"/>)과 HUD 생성
        /// 두 곳(<c>PenguinSnowball</c> · <c>PenguinNetAvatar</c>)을 함께 막는다.</para>
        ///
        /// <para>덤으로 <b>성장 HUD 가 둘로 보이던 것</b>도 같이 사라진다 —
        /// <c>SnowballCoopTimingHud</c> 의 uxml 이 자기 <c>snowball-growth-hud</c> 요소를 들고 있고,
        /// 신규 <c>SnowballGrowthArcHud</c> 와 겹쳐 보였다. 협동 HUD 가 아예 안 생기면 겹칠 것이 없다.</para>
        ///
        /// <para>되살릴 때 같이 볼 것: 입력 비트 <c>CoopShoveSuccess</c>/<c>CoopShoveFailure</c> 와
        /// <c>SnowBallCoopFeedback</c> 는 그대로 남겨 뒀다 — 이 값이 참이 되면 다시 돈다.</para>
        /// </summary>
        public static readonly bool CoopPushEnabled = false;

        public const float CoopTimingSuccessMin01 = 0.42f;
        public const float CoopTimingSuccessMax01 = 0.62f;

        private const float CoopStrainSpeedMps = 1.25f;
        private const float CoopEasyAccelerationMps2 = 3f;
        private const float CoopHardAccelerationMps2 = 0.35f;
        private const float CoopStrainDelayEasySeconds = 1.1f;
        private const float CoopStrainDelayHardSeconds = 0.35f;
        private const float CoopTimingDurationEasySeconds = 2.2f;
        private const float CoopTimingDurationHardSeconds = 1.2f;
        private const float CoopRetryCooldownEasySeconds = 4.5f;
        // 하한이 0 인 것은 의도다. 무거워질수록 함께밀기는 진폭이 아니라 <b>빈도</b>로 어려워진다 —
        // 난이도 1 에서는 실패해도 버티는 시간(0.35 s)만 지나면 바로 다시 온다.
        private const float CoopRetryCooldownHardSeconds = 0f;
        // 성공 한 번은 한 사람이 1초 동안 미는 힘을 순간적으로 모은 값이다. 0.35초치는 큰 공에서
        // 속도 변화가 거의 읽히지 않아 타이밍을 맞춘 보상이 느껴지지 않았다.
        private const float CoopBoostImpulsePerPlayerNs = PlayerPushForceN;
        private const float CoopBoostCoastSeconds = 0.45f;

        [Header("성장")]
        [Tooltip("지나간 셀에 남기는 눈(mm). 성장 속도의 유일한 노브다. 0 이면 한 번 지나간 자리가 " +
                 "맨땅이 된다.")]
        [SerializeField, Min(0)] private int _residueMm = 0;

        [Tooltip("필드에서 걷은 눈 중 공의 실제 질량으로 가져올 비율. 반지름과 Rigidbody 무게는 " +
                 "가중 적용 후 질량에서 함께 계산한다.")]
        [SerializeField, Range(0.1f, 1f)] private float _growthWeight = 0.5f;

        [Tooltip("이 눈덩이의 현재 성장 상태. 런타임에 표시 반지름에서 자동으로 갱신한다.")]
        [SerializeField] private ESnowBallGrowthStage _currentGrowthStage =
            ESnowBallGrowthStage.Seed;

        [Tooltip("이 환산 반지름까지는 질량 외의 추가 이동 감속을 적용하지 않는다.")]
        [SerializeField, Min(SnowBallCpu.SeedRadiusM)] private float _mobilitySlowdownStartRadiusM = 1.2f;

        [Tooltip("환산 반지름이 1.5 m일 때 유지할 목표 속도 비율.")]
        [SerializeField, Range(0.01f, 0.99f)] private float _mobilityAtVisibleMaximum01 = 0.45f;

        [Tooltip("초과 질량이 매우 커졌을 때 점진적으로 접근할 목표 속도 비율. 0보다 커야 완전히 막히지 않는다.")]
        [SerializeField, Range(0.01f, 0.5f)] private float _minimumMobility01 = 0.05f;

        [Header("표시")]
        [Tooltip("이 오브젝트의 메시가 지름 1 m 인 단위 구인가. 아니라면 그 비율을 넣는다.")]
        [SerializeField, Min(0.01f)] private float _meshDiameterM = 1f;

        [Tooltip("터질 때 한 번 재생할 이펙트. 비우면 Resources 의 VFX_SnowBurst 를 쓴다.\n\n" +
                 "공이 사라지므로 이 컴포넌트에 붙여 두면 같이 죽는다 - 재생은 떼어낸 일회용 " +
                 "오브젝트에서 한다.")]
        [SerializeField] private VisualEffectAsset _burstVfx;

        [Header("정지")]
        [Tooltip("아무도 밀지 않을 때 잔여 관성을 정리할 최대 지면 경사. 이보다 가파르면 중력으로 " +
                 "굴러가게 둔다.")]
        [SerializeField, Range(0f, 15f)] private float _idleStopMaxSlopeDeg = 3f;

        [Tooltip("구르는 회전의 상한(rad/s). ω = v/r 이라 반지름이 작을수록 발산한다 — 씨앗(0.18 m)이 " +
                 "걷기 속도 3.5 m/s 로 미끄러짐 없이 구르면 19.4 rad/s, 초당 3.1 바퀴다. " +
                 "낮추면 그만큼 굴러가는 대신 미끄러져 보인다.")]
        [SerializeField, Min(0.5f)] private float _maxAngularVelocityRadPerSec = 8f;

        /// <summary>
        /// 복제되는 질량(mm·셀). <b>직접 읽지 말고 <see cref="MassMm"/> 을 쓴다</b> — 이 속성은
        /// <c>Spawned</c> 전에 접근하면 예외를 던진다.
        /// </summary>
        [Networked] private int NetMassMm { get; set; }

        /// <summary>
        /// 이 공 위에서 굴리는 네트워크 펭귄. 한 공에 한 명만 허용하며 서버가 선점자를 정한다.
        /// </summary>
        [Networked] public NetworkBehaviourId TopRider { get; private set; }

        [Networked] private NetworkBool NetCoopTimingActive { get; set; }
        [Networked] private int NetCoopParticipantMask { get; set; }
        [Networked] private int NetCoopSuccessMask { get; set; }
        [Networked] private float NetCoopTimingPhase01 { get; set; }

        // 연출이 읽는 신호. 성공 이벤트를 따로 쏘지 않고 이 둘의 에지를 각 피어가 스스로 본다 -
        // 질량과 같은 방식이다(SnowBallCoopFeedback).
        [Networked] private int NetCoopBoostCount { get; set; }
        [Networked] private float NetCoopBoostDifficulty01 { get; set; }

        /// <summary>
        /// 바깥(운반)이 이 공을 들고 있는가. <b>서버만 쓰고 모든 피어가 읽는다</b> —
        /// <see cref="MirrorRemoteMotion"/> 이 이 값으로 자기 사본의 물리 설정을 맞춘다.
        /// </summary>
        [Networked] private NetworkBool NetCarried { get; set; }

        /// <summary>
        /// 표시·크기가 읽는 질량. 네트워크 값의 사본이다.
        ///
        /// <para><b>왜 사본이 필요한가:</b> 이 컴포넌트는 <b>런너 없는 씬에서도 돈다</b>(싱글플레이,
        /// 펭귄 테스트). <c>[Networked]</c> 는 <c>Spawned</c> 전에 <b>쓰기도</b> 예외이므로 그때는 저장할
        /// 곳이 없다. 두 곳에 상태를 두는 대가는 명시적으로 갚는다 — <b>복제 값이 사본을 먹이는 방향은
        /// 한쪽뿐</b>이고(<see cref="Render"/>), 사본이 복제 값을 되먹이는 경로는 없다.</para>
        /// </summary>
        public int MassMm => _massMm;

        /// <summary>서버가 이번 틱에 눈을 놓아야 하는가. 복제하지 않는다 — 서버에서만 읽고 지운다.</summary>
        public bool ServerReleaseRequested { get; set; }

        /// <summary>
        /// 서버가 이번 틱에 이 공을 <b>터뜨려야</b> 하는가. 놓기와 같은 모양이고 복제하지 않는다.
        ///
        /// <para><b>터짐은 크기가 아니라 플레이어가 정한다 (2026-08-21).</b> 전에는 반지름이 문턱을
        /// 넘는 순간 스테이지가 스스로 터뜨렸다. 그러면 공을 더 키우고 싶어도 키울 수가 없고,
        /// "무거워서 못 밀겠다" 와 "터뜨린다" 가 같은 순간에 묶여 선택이 사라진다. 지금은 무게가
        /// 압력을 만들고, 터뜨리는 것은 사람이 누른다.</para>
        /// </summary>
        public bool ServerBurstRequested { get; set; }

        /// <summary>
        /// 이 공이 <b>터져서</b> 사라지는 중인가. 스테이지가 없애기 직전에 표시한다.
        ///
        /// <para><b>왜 필요한가:</b> "사라짐 = 터짐" 은 게임 안에서는 맞지만 <b>씬을 닫을 때는
        /// 틀리다.</b> 표시가 없으면 Play 를 끝내는 순간 <c>OnDestroy</c> 가 이펙트를 만들고,
        /// 유니티가 <i>"Some objects were not cleaned up when closing the scene. (Did you spawn new
        /// GameObjects from OnDestroy?)"</i> 로 에러를 낸다(실측).</para>
        /// </summary>
        public void ServerMarkBursting() => _bursting = true;

        /// <summary>
        /// 이 공의 운동을 <b>바깥이 가져갔다</b>고 알린다. 지금 유일한 호출자는
        /// <see cref="PenguinCarry"/> 의 운반이다. 두 가지를 한다 — 평지 정지 보정이 이 동안
        /// 속도를 지우지 않게 하고, <b>권위이면 그 사실을 복제한다</b>.
        ///
        /// <para><b>왜 복제해야 하는가 (2026-09-01).</b> 운반의 물리 설정
        /// (<c>isKinematic</c> · <c>useGravity</c> · <c>detectCollisions</c>)은
        /// <see cref="PenguinCarry"/> 가 거는데 그쪽은 서버만 돈다(<c>NetworkDriven</c>). 그래서
        /// 클라이언트의 사본은 운반 중에도 동적이었다 — 중력으로 떨어지고, 메고 있는 펭귄의 캡슐과
        /// 충돌하고, 그 사이 <c>NetworkRigidbody</c> 가 매 틱 서버 자세로 되돌렸다. 한 트랜스폼에
        /// 쓰는 주체가 넷이라 <b>F 로 옮길 때 떨림이 제일 심했다.</b></para>
        ///
        /// <para>⚠ <b>던지기에 쓰지 마라.</b> 2026-09-01 전까지 이 훅의 문서는 던지기를 가리켰지만
        /// 호출자는 0건이었다. 던진 공은 <b>동적으로</b> 날아가야 하므로, kinematic 을 거는 지금
        /// 구현과 맞지 않는다. 던지기가 생기면 그때는 별도의 신호를 만든다.</para>
        /// </summary>
        public void BeginExternalMotion() => SetExternalMotion(true);

        /// <summary>운반이 끝나 일반 carrier 정지 규칙으로 되돌린다.</summary>
        public void EndExternalMotion() => SetExternalMotion(false);

        /// <summary>복제는 권위만 쓴다. 런너가 없는 판(싱글)에서는 사본만 바뀐다.</summary>
        private void SetExternalMotion(bool active)
        {
            _externalMotion = active;
            if (Object != null && Object.IsValid && Object.HasStateAuthority) NetCarried = active;
        }

        private bool _bursting;

        /// <summary>
        /// <b>권위(와 런너 없는 싱글)만 쓰고 읽는다.</b> 유일한 독자인
        /// <see cref="StopIdleDriftOnFlatGround"/> 가 <c>!IsAuthority</c> 에서 먼저 빠지므로,
        /// 프록시에서 이 값을 세워도 읽는 곳이 없다. 프록시 쪽 사본은
        /// <see cref="_mirroredMotion"/> 하나로 충분하다.
        /// </summary>
        private bool _externalMotion;

        /// <summary>
        /// 프록시가 지금 흉내 내고 있는 운동 상태. 전이할 때만 물리 설정을 만진다.
        /// <c>Unset</c> 은 아직 한 번도 반영하지 않았다는 뜻이라 첫 프레임이 반드시 적용된다.
        /// </summary>
        private enum EProxyMotion { Unset, Remote, Carried }

        private EProxyMotion _mirroredMotion = EProxyMotion.Unset;

        private Component _localTopRider;

        public int ResidueMm => _residueMm;

        public int GrowthWeightPermille => Mathf.Clamp(
            Mathf.RoundToInt(_growthWeight * SnowBallCpu.GrowthWeightScale), 1,
            SnowBallCpu.GrowthWeightScale);

        /// <summary>구르는 회전의 상한(rad/s). 검증이 읽는다.</summary>
        public float MaxAngularVelocityRadPerSec => _maxAngularVelocityRadPerSec;

        /// <summary>화면과 접지 폭에 쓰는 반지름. 질량이 늘어도 1.5 m를 넘지 않는다.</summary>
        public float RadiusM => Mathf.Min(SnowBallCpu.MaxRadiusM,
            SnowBallCpu.RadiusFromMassMm(_massMm));

        /// <summary>초과 압축 질량까지 반영한 환산 반지름.</summary>
        public float EquivalentRadiusM => SnowBallCpu.RadiusFromMassMm(_massMm);

        public long VisibleMaxMassMm => SnowBallCpu.MassMmForRadius(SnowBallCpu.MaxRadiusM);

        public long MassMmForRadius(float radiusM) => SnowBallCpu.MassMmForRadius(radiusM);

        public bool IsOverSizeThreshold => _massMm >= VisibleMaxMassMm;

        public float GrowthProgress01 => Mathf.InverseLerp(SnowBallCpu.SeedRadiusM,
            SnowBallCpu.MaxRadiusM, RadiusM);

        public float Mobility01
        {
            get
            {
                float startRadiusM = Mathf.Min(_mobilitySlowdownStartRadiusM,
                    SnowBallCpu.MaxRadiusM - 0.001f);
                if (EquivalentRadiusM <= startRadiusM) return 1f;

                float minimum = Mathf.Clamp(_minimumMobility01, 0.001f, 0.98f);
                float atVisibleMaximum = Mathf.Clamp(_mobilityAtVisibleMaximum01,
                    minimum + 0.001f, 0.99f);
                float normalizedAtMaximum = (atVisibleMaximum - minimum) / (1f - minimum);
                float ratioAtMaximum = Mathf.Sqrt(1f / normalizedAtMaximum - 1f);
                float curveScaleM = (SnowBallCpu.MaxRadiusM - startRadiusM) / ratioAtMaximum;
                float x = (EquivalentRadiusM - startRadiusM) / curveScaleM;
                return minimum + (1f - minimum) / (1f + x * x);
            }
        }

        public ESnowBallGrowthStage GrowthStage => _currentGrowthStage;

        /// <summary>HUD가 표시하는 현재 눈덩이 지름(m). 보이는 크기 상한을 따른다.</summary>
        public float DiameterM => RadiusM * 2f;

        /// <summary>현재 상태에서 다음 단계까지 반지름이 진행한 비율.</summary>
        public float GrowthStageProgress01 => _currentGrowthStage == ESnowBallGrowthStage.Stage4
            ? 1f
            : SnowballStageModel.GetStageProgress01(RadiusM);

        /// <summary>
        /// Seed와 Stage 1~3에서는 다음 단계가 시작되는 지름, Stage 4에서는 최대 지름이다.
        /// </summary>
        public float NextGrowthTargetDiameterM
        {
            get
            {
                SnowballStageModel.GetStageRange(_currentGrowthStage, out _,
                    out float targetRadiusM);
                return targetRadiusM * 2f;
            }
        }

        /// <summary>다음 단계(마지막 단계에서는 보이는 상한)까지 남은 지름(m).</summary>
        public float RemainingDiameterToNextGrowthTargetM =>
            Mathf.Max(0f, NextGrowthTargetDiameterM - DiameterM);

        /// <summary>Stage 4 진입점인 보이는 크기 상한에 도달했는지 여부.</summary>
        public bool IsVisibleGrowthComplete => IsOverSizeThreshold;

        private void UpdateGrowthStage()
        {
            _currentGrowthStage = IsOverSizeThreshold
                ? ESnowBallGrowthStage.Stage4
                : SnowballStageModel.GetStage(RadiusM);
        }

        /// <summary>
        /// 공 위 탑승권을 요청한다. 네트워크 오브젝트끼리는 복제되는 ID로, 런너 없는 단독 모드에서는
        /// 로컬 컴포넌트 참조로 관리한다. 이미 다른 펭귄이 있으면 false다.
        /// </summary>
        public bool TryClaimTop(Component rider)
        {
            if (rider == null) return false;

            if (TryGetNetworkRiderId(rider, out NetworkBehaviourId riderId))
            {
                if (TopRider != default && TopRider != riderId) return false;
                TopRider = riderId;
                return true;
            }

            if (_localTopRider != null && _localTopRider != rider) return false;
            _localTopRider = rider;
            return true;
        }

        /// <summary>자신이 가진 공 위 탑승권만 반납한다.</summary>
        public void ReleaseTop(Component rider)
        {
            if (rider == null) return;

            if (TryGetNetworkRiderId(rider, out NetworkBehaviourId riderId))
            {
                if (TopRider == riderId) TopRider = default;
                return;
            }

            if (_localTopRider == rider) _localTopRider = null;
        }

        private bool TryGetNetworkRiderId(Component rider, out NetworkBehaviourId riderId)
        {
            riderId = default;
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return false;
            if (rider is not NetworkBehaviour networkRider) return false;
            if (networkRider.Object == null || !networkRider.Object.IsValid) return false;

            riderId = networkRider.Id;
            return true;
        }

        private int _massMm;
        private Rigidbody _body;

        // 운반이 끝났을 때 되돌릴 프리팹 기본값. 프록시에는 PenguinCarry 의 캐시가 없으므로
        // 공이 스스로 들고 있어야 한다(MirrorRemoteMotion). isKinematic 은 캐시하지 않는다 —
        // 프록시는 운반이든 아니든 항상 kinematic 이라 되돌릴 값이 없다.
        private bool _defaultUseGravity;
        private bool _defaultDetectCollisions;
        private float _appliedRadiusM = -1f;
        private int _appliedMassMm = -1;
        private Vector3 _pendingPushN;
        private float _pendingTargetSpeedMps;
        private int _pendingParticipantMask;
        private float _pendingBrakeForceN;
        private bool _pendingMomentumMode;
        private Vector3 _supportNormal = Vector3.up;
        private bool _hasSupport;
        private readonly RaycastHit[] _supportHits = new RaycastHit[16];
        private readonly Component[] _localParticipants = new Component[31];
        private bool _coopTimingActive;
        private int _coopParticipantMask;
        private int _coopSuccessMask;
        private float _coopTimingPhase01;
        private float _coopStrainSeconds;
        private float _coopCooldownSeconds;
        private float _coopBoostCoastSeconds;
        private float _coopDifficulty01;
        private int _armingParticipantMask;
        private Vector3 _coopPushDirection;

        /// <summary>
        /// 전원 성공으로 실제 Impulse가 적용된 횟수. <b>연출과 검증이 읽는다</b> — 연출은 이 값의
        /// 증가를 보고 재생하므로, 서버가 이벤트를 따로 보내지 않는다.
        /// </summary>
        public int CoopBoostCount { get; private set; }

        /// <summary>
        /// 마지막 전원 성공의 난이도(0~1). <b>연출 세기가 이것을 탄다.</b> 임펄스만으로는
        /// "얼마나 어려운 성공이었는지" 를 연출이 알 방법이 없다.
        /// </summary>
        public float LastCoopBoostDifficulty01 { get; private set; }

        /// <summary>공 아래 맵 콜라이더의 법선. 밀기 방향을 경사 접선에 놓을 때 쓴다.</summary>
        public Vector3 SupportNormal => _supportNormal;

        /// <summary>공이 현재 맵 콜라이더에 접지했는가. 공중 이동을 눈 수확 경로로 오인하지 않게 한다.</summary>
        public bool HasSupport => _body != null && !_body.isKinematic && _hasSupport;

        /// <summary>
        /// 권위(<see cref="SnowCpuStage"/>)만 부른다. 질량이 곧 크기이므로 여기서 같이 반영한다.
        /// 스폰돼 있으면 복제 값에도 싣는다 — 안 돼 있으면(단독 모드) 사본만으로 충분하다.
        /// </summary>
        public void ServerApplyMass(long massMm)
        {
            _massMm = (int)System.Math.Max(0L, System.Math.Min(massMm, int.MaxValue));
            if (Object != null && Object.IsValid && Object.HasStateAuthority) NetMassMm = _massMm;
            ApplySize();
        }

        /// <summary>런너가 없는 씬(단독 모드)에서도 크기가 서야 한다. 여기서는 사본만 있다.</summary>
        private void Awake()
        {
            // 눈덩이 메시는 코드로 만든 아이코스피어다 - 유니티 기본 구는 극에서 삼각형이 한 점으로
            // 모이고, 이 셰이더는 정점에서 표면을 밀어내므로 그 자리에만 변위가 뭉친다.
            // 지름 1 이라서 스케일 계산과 SphereCollider 는 그대로다.
            // 데디케이티드 서버는 GPU 가 없고 표현 계층을 만들 이유도 없다 - 루트 AGENTS 의
            // "표현은 별도 계층" 이 여기서는 메시 생성 한 번을 아끼는 것으로 나타난다.
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                var filter = GetComponent<MeshFilter>();
                if (filter != null) filter.sharedMesh = SnowBallMesh.Shared;
            }

            _body = GetComponent<Rigidbody>();
            if (_body != null)
            {
                _defaultUseGravity = _body.useGravity;
                _defaultDetectCollisions = _body.detectCollisions;
            }

            ApplySize();
        }

        /// <summary>
        /// 펭귄 한 명의 힘을 이번 물리 스텝에 보탠다. 위치는 받지 않는다 — 옆에서 밀든 위에서
        /// 굴리든 같은 한 사람의 힘이고, 모든 참여자의 벡터를 합친 뒤 공 중심에 한 번만 적용한다.
        /// </summary>
        public void SubmitPush(Component pusher, Vector3 desiredVelocity, float maxForceN)
            => SubmitPushInternal(pusher, desiredVelocity, maxForceN, false);

        /// <summary>
        /// 관성 조작 실험의 추진 요청. 기존 성장 감속을 적용하지 않고, 목표 속도를 넘었을 때도
        /// 역방향 힘을 만들지 않는다. 목표는 추진 상한일 뿐 실제 속도 제한이 아니다.
        /// </summary>
        public void SubmitMomentumPush(Component pusher, Vector3 desiredVelocity, float maxForceN)
            => SubmitPushInternal(pusher, desiredVelocity, maxForceN, true);

        private void SubmitPushInternal(Component pusher, Vector3 desiredVelocity, float maxForceN,
            bool momentumMode)
        {
            if (maxForceN <= 0f || desiredVelocity.sqrMagnitude < 1e-6f) return;

            _supportNormal = SampleSupportNormal();
            if (!_hasSupport) return;

            Vector3 direction = Vector3.ProjectOnPlane(desiredVelocity, _supportNormal);
            if (direction.sqrMagnitude < 1e-6f) return;

            _pendingPushN += direction.normalized * maxForceN;
            _pendingTargetSpeedMps = Mathf.Max(_pendingTargetSpeedMps, desiredVelocity.magnitude);
            _pendingParticipantMask |= ParticipantBit(pusher, true);
            _pendingMomentumMode |= momentumMode;
        }

        /// <summary>
        /// 참가자 추적이 필요 없는 물리 검증용 경로. 실제 플레이어는 반드시 자신을 함께 제출한다.
        /// </summary>
        public void SubmitPush(Vector3 desiredVelocity, float maxForceN) =>
            SubmitPush(null, desiredVelocity, maxForceN);

        /// <summary>
        /// 관성 실험에서 W/S를 놓아도 평지 정지 보정이 속도를 즉시 지우지 않게 한다.
        /// Rigidbody 감쇠와 충돌, 경사 중력은 계속 작동한다.
        /// </summary>
        public void SubmitMomentumCoast() => _pendingMomentumMode = true;

        /// <summary>
        /// 이번 스텝의 실제 지면 접선 속도를 거스르는 제동력을 제출한다. 방향을 인자로 받지
        /// 않으므로 뒤로 구르거나 옆으로 흐를 때도 현재 운동을 멈추며 반대 방향 추진이 되지 않는다.
        /// </summary>
        public void SubmitBrake(Component pusher, float maxForceN)
        {
            if (maxForceN <= 0f) return;

            _supportNormal = SampleSupportNormal();
            if (!_hasSupport) return;

            _pendingBrakeForceN += maxForceN;
            _pendingMomentumMode = true;
        }

        /// <summary>이 펭귄에게 협동 타이밍 HUD를 보여줄 상태인지 조회한다.</summary>
        public bool TryGetCoopTiming(Component pusher, out float phase01, out bool submitted,
            out int participantCount)
        {
            phase01 = 0f;
            submitted = false;
            participantCount = 0;

            int bit = ParticipantBit(pusher, false);
            if (bit == 0) return false;

            bool networked = Object != null && Object.IsValid;
            bool active = networked ? NetCoopTimingActive : _coopTimingActive;
            int participants = networked ? NetCoopParticipantMask : _coopParticipantMask;
            if (!active || (participants & bit) == 0) return false;

            int successes = networked ? NetCoopSuccessMask : _coopSuccessMask;
            phase01 = networked ? NetCoopTimingPhase01 : _coopTimingPhase01;
            submitted = (successes & bit) != 0;
            participantCount = CountBits(participants);
            return true;
        }

        /// <summary>
        /// 각 클라이언트가 자기 화면의 타이밍으로 판정한 결과를 서버에 제출한다. 서버는 시간을 다시
        /// 판정하지 않고 참가자 전원의 성공 여부만 모은다.
        /// </summary>
        public void SubmitCoopTiming(Component pusher, bool success)
        {
            if (!_coopTimingActive) return;

            int bit = ParticipantBit(pusher, false);
            if (bit == 0 || (_coopParticipantMask & bit) == 0 || (_coopSuccessMask & bit) != 0) return;

            if (!success)
            {
                FinishCoopTiming(false);
                return;
            }

            _coopSuccessMask |= bit;
            MirrorCoopNetworkState();
            if ((_coopSuccessMask & _coopParticipantMask) == _coopParticipantMask)
            {
                FinishCoopTiming(true);
            }
        }

        public static bool IsCoopTimingSuccess(float phase01) =>
            phase01 >= CoopTimingSuccessMin01 && phase01 <= CoopTimingSuccessMax01;

        /// <summary>
        /// <b>런너가 없는 판(싱글플레이·펭귄 테스트)의 진입점.</b> 스폰돼 있으면 여기서 돌지 않는다 —
        /// 그때는 <see cref="FixedUpdateNetwork"/> 가 Fusion 의 틱으로 돌린다.
        ///
        /// <para>둘을 나누는 이유: 물리 애드온이 <c>Physics.simulationMode</c> 를 <c>Script</c> 로
        /// 바꾸면 Unity 는 <c>FixedUpdate</c> 에서 적분을 하지 않는다. 그런데 <c>FixedUpdate</c> 자체는
        /// 계속 호출되므로, 여기서 힘을 계산하면 <b>Fusion 틱과 다른 시계로 쌓아 다른 시계로 쓰는</b>
        /// 상태가 된다 — 스텝마다 받는 힘의 개수가 달라진다.</para>
        /// </summary>
        private void FixedUpdate()
        {
            if (Object != null && Object.IsValid) return;
            StepPhysics(Time.fixedDeltaTime);
        }

        /// <summary>
        /// 네트워크 판의 진입점. <b>모든 피어가 돈다</b> — <see cref="NetworkRigidbody"/> 가 프록시까지
        /// 시뮬레이션 대상으로 만들고 어긋나면 서버 상태로 되돌린다.
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            // <b>비권위 피어의 질량·크기는 여기서 맞춘다 — Render 가 아니다 (2026-09-01 실측).</b>
            //
            // ApplySize 는 localScale 을 바꾸고 그러면 SphereCollider 반경이 같이 바뀐다. 그것을
            // Render 에서 하면 콜라이더가 <b>물리 스텝 밖에서</b> 커지고, 뒤이은 Simulate 가 접촉을
            // 다시 풀면서 공을 옆으로 밀어낸다. 권위는 틱 안(ServerApplyMass)에서 바꾸므로 이 일이
            // 없다 — 그 비대칭이 계측에 그대로 찍혔다: 프록시 반전 62건 중 54건이 질량이 바뀐
            // 프레임(24) 또는 바로 다음 프레임(30)이었고, 권위는 질량이 6157번 바뀌는 동안 반전이
            // 0건이었다.
            //
            // 재시뮬에서 여러 번 불려도 안전하다. NetMassMm 은 틱마다 롤백되는 복제 상태이고
            // 대입은 멱등이라, 마지막 패스의 값이 그대로 남는다.
            if (!IsAuthority)
            {
                _massMm = NetMassMm;
                ApplySize();
            }

            StepPhysics(Runner.DeltaTime);
        }

        /// <summary>
        /// 한 스텝. <paramref name="dt"/> 는 호출한 시계의 간격이다 — 네트워크 판은
        /// <c>Runner.DeltaTime</c>, 단독 판은 <c>Time.fixedDeltaTime</c>.
        /// </summary>
        private void StepPhysics(float dt)
        {
            if (_body == null) _body = GetComponent<Rigidbody>();
            _supportNormal = SampleSupportNormal();

            Vector3 available = _pendingPushN;
            float targetSpeed = _pendingTargetSpeedMps;
            int participants = _pendingParticipantMask;
            float brakeForceN = _pendingBrakeForceN;
            bool momentumMode = _pendingMomentumMode;
            _pendingPushN = Vector3.zero;
            _pendingTargetSpeedMps = 0f;
            _pendingParticipantMask = 0;
            _pendingBrakeForceN = 0f;
            _pendingMomentumMode = false;

            _coopCooldownSeconds = Mathf.Max(0f, _coopCooldownSeconds - dt);
            _coopBoostCoastSeconds = Mathf.Max(0f, _coopBoostCoastSeconds - dt);

            if (_body == null || _body.isKinematic) return;

            Vector3 tangentVelocity = Vector3.ProjectOnPlane(_body.linearVelocity, _supportNormal);
            if (brakeForceN > 0f && tangentVelocity.sqrMagnitude > 1e-6f)
            {
                float stoppingForceN = tangentVelocity.magnitude * _body.mass / dt;
                _body.AddForce(-tangentVelocity.normalized *
                               Mathf.Min(brakeForceN, stoppingForceN), ForceMode.Force);
            }

            if (!momentumMode) targetSpeed *= Mobility01;
            if (available.sqrMagnitude < 1e-6f)
            {
                TickCoopTiming(participants, false, Vector3.zero, 0f, dt);
                if (!momentumMode) StopIdleDriftOnFlatGround();
                return;
            }

            Vector3 direction = available.normalized;
            float along = Vector3.Dot(tangentVelocity, direction);

            // 성장할수록 낮아지는 목표 속도를 넘었으면 같은 힘 한도 안에서 서서히 감속한다.
            // 속도를 즉시 잘라 붙이지 않으므로 큰 공의 실제 질량만큼 제동도 오래 걸린다.
            float needed = (targetSpeed - along) * _body.mass / dt;
            float accelerationMps2 = available.magnitude / Mathf.Max(0.01f, _body.mass);
            float difficulty01 = 1f - Mathf.InverseLerp(CoopHardAccelerationMps2,
                CoopEasyAccelerationMps2, accelerationMps2);
            bool straining = participants != 0 && along <= CoopStrainSpeedMps &&
                              needed > available.magnitude;
            TickCoopTiming(participants, straining, direction, difficulty01, dt);
            if (needed <= 0f)
            {
                if (momentumMode) return;
                _body.AddForce(-direction * Mathf.Min(available.magnitude, -needed), ForceMode.Force);
                return;
            }

            _body.AddForce(direction * Mathf.Min(available.magnitude, needed), ForceMode.Force);
        }

        /// <summary>
        /// 판정을 돌려도 되는 쪽인가. <b>런너가 없으면 참이다</b> — 싱글플레이와 펭귄 테스트는
        /// 지금 동작 그대로여야 하고, <see cref="FixedUpdate"/> 가 이미 같은 기준으로 갈린다.
        ///
        /// <para><b>공의 상태를 쓰는 바깥 컴포넌트도 이것을 봐야 한다 (2026-09-01 에 공개했다).</b>
        /// <c>SnowballGrowthStageTimer</c> 가 두 번째 호출자다 — 그쪽은 질량을 확정하는데
        /// 게이트가 없어서, 클라이언트에서 <see cref="ServerApplyMass"/> 를 매 스텝 부르고
        /// <see cref="Render"/> 가 매 프레임 복제 값으로 되돌렸다. 공이 <b>씨앗 크기와 실제
        /// 크기를 프레임마다 왕복</b>했다(실측). 같은 판정을 세 곳이 각자 적는 것을 늘리지 않으려고
        /// 여기 하나만 공개한다 — <c>SnowCpuStage.OwnsBallState</c> 는 특정 공이 아니라 스테이지
        /// 자신을 묻는 것이라 별개다.</para>
        /// </summary>
        public bool IsAuthority =>
            Object == null || !Object.IsValid || Object.HasStateAuthority;

        /// <summary>
        /// 복제된 운동 상태를 프록시의 물리 설정으로 옮긴다. <b>비권위 피어 전용.</b>
        ///
        /// <para>권위와 싱글은 <see cref="PenguinCarry"/> 가 직접 걸고 되돌리므로 여기서 손대지
        /// 않는다. 한 Rigidbody 에 쓰는 주체를 둘로 두면 그것이 또 하나의 싸움이 된다.
        /// 되돌릴 값은 <see cref="Awake"/> 가 캐시한 프리팹 기본값이다 — 프록시에는
        /// <c>PenguinCarry</c> 가 잡는 순간 떠 두는 캐시가 없다.</para>
        ///
        /// <para><b>프록시는 공의 물리를 아예 돌리지 않는다 (2026-09-01 실측으로 확정).</b>
        /// 전에는 밀리는 동안에만(<c>NetPushed</c>) 멈추고 나머지는 각 피어가 함께 굴렸다. 계측이
        /// 그 게이트가 <b>사실상 발동하지 않는다</b>는 것을 보였다 — 프록시 11615 프레임 중
        /// kinematic 은 <b>15 프레임</b>뿐이었다. 관성 조작에서는 미는 입력이 끊긴 뒤에도 공이
        /// 한참 굴러가는데, 그 관성은 <b>프록시가 받은 적 없는 힘의 결과</b>라 로컬 시뮬이
        /// 재현할 수 없다. 같은 실측에서 프록시는 움직인 프레임의 <b>5.5%</b>에서 진행 방향이
        /// 뒤집혔고(중앙 6.9 mm · 최대 17.7 cm) 권위는 <b>0%</b>였다. 그 되돌림이 떨림이다.</para>
        ///
        /// <para>대가는 밀기 결과가 왕복 시간만큼 늦게 보이는 것이다. 그것을 피하려던 것이 원래
        /// 결정이었지만, 로컬 시뮬은 늦지 않은 대신 <b>틀린</b> 운동을 그리고 있었다. 17 cm 되돌림과
        /// 한 틱 지연 중에서는 후자가 낫다. 예측이 필요해지면 그때는 입력 자체를 예측해야 하고,
        /// 그것은 <c>NetworkRigidbody</c> 설정이 아니라 별도 스펙이다.</para>
        ///
        /// <para>운반과 그 밖은 <b>원하는 상태가 다르다</b> — 운반은 등에 얹혀 있으므로 콜라이더까지
        /// 끄고, 그 밖에는 켜 둔다(펭귄이 굴러가는 공을 통과하면 안 된다). kinematic 바디도
        /// 충돌은 받으므로 미는 감촉 자체는 남는다.</para>
        ///
        /// <para><b>왜 <see cref="Render"/> 에서 부르는가.</b> 처음에는 <see cref="StepPhysics"/>
        /// 첫 줄이었는데 그것은 <see cref="FixedUpdateNetwork"/> 경로다. Fusion 은 새 스냅샷마다
        /// 확정 틱부터 <b>다시 시뮬레이션</b>하고 프록시도 그 재시뮬을 돈다. 그런데
        /// <see cref="_mirroredMotion"/> 은 롤백되지 않는 평범한 C# 필드라, 상태가 막 바뀐 구간에서
        /// 재시뮬 패스마다 kinematic 을 껐다 켰다 하게 된다. <c>Render</c> 는 프레임당 한 번,
        /// 재시뮬 밖에서 돌고 이 파일에는 이미 그 관례가 있다(질량·협동 부스트 사본이 같은 블록).
        /// 대가는 전환이 한 틱 늦는 것뿐이다.</para>
        ///
        /// <para><c>interpolation</c> 은 여기서 건드리지 않는다. 프리팹에서 <c>None</c> 으로 꺼 뒀다 —
        /// 보간은 <c>NetworkRigidbody</c> 하나가 맡는다(<c>MultiPlaySceneBuilder</c> 가 네트워크
        /// 선물에 적어 둔 것과 같은 규약).</para>
        /// </summary>
        private void MirrorRemoteMotion()
        {
            if (_body == null) return;
            if (IsAuthority) return;

            EProxyMotion want = NetCarried ? EProxyMotion.Carried : EProxyMotion.Remote;

            if (want == _mirroredMotion) return;
            _mirroredMotion = want;
            _externalMotion = true;

            // 속도를 먼저 지운다 — kinematic 이 된 뒤의 대입은 유니티가 경고로 막는다.
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.useGravity = want == EProxyMotion.Carried ? false : _defaultUseGravity;
            _body.detectCollisions =
                want == EProxyMotion.Carried ? false : _defaultDetectCollisions;
            _body.isKinematic = true;
        }

        private void StopIdleDriftOnFlatGround()
        {
            // <b>권위만 돈다</b> (2026-09-01). 클라이언트에서는 PenguinSnowball.NetworkDriven 이 참이라
            // SubmitPush 가 아예 안 불리고, 그래서 _pendingPushN 이 늘 0 이라 매 틱 여기로 떨어졌다.
            // 서버는 밀고 있는데 클라 사본은 자기 수평 속도와 회전을 매 틱 0 으로 죽이고,
            // NetworkRigidbody 가 되돌리고, 다음 틱에 또 죽인다 — 그것이 밀기 지터였다.
            // 덤으로 angularVelocity 가 0 이라 클라에서는 공이 구르지 않아 SnowBallRollAudio 도
            // 조용했다. "입력이 없다" 와 "멈춰야 한다" 는 권위에서만 같은 말이다.
            if (!IsAuthority) return;
            if (_externalMotion) return;
            if (_coopBoostCoastSeconds > 0f) return;
            if (!_hasSupport) return;

            Vector3 gravity = Physics.gravity;
            Vector3 up = gravity.sqrMagnitude > 1e-6f ? -gravity.normalized : Vector3.up;
            if (Vector3.Angle(_supportNormal, up) > _idleStopMaxSlopeDeg) return;

            // 평지에서 입력이 끊겼으면 이전 밀기의 수평 관성과 구름 회전을 남기지 않는다.
            // 지면 법선 방향 속도는 보존해 접촉 해결과 낙하를 방해하지 않는다.
            _body.linearVelocity = Vector3.Project(_body.linearVelocity, _supportNormal);
            _body.angularVelocity = Vector3.zero;
        }

        private void TickCoopTiming(int currentParticipants, bool straining, Vector3 pushDirection,
            float difficulty01, float dt)
        {
            if (!CoopPushEnabled) return;
            if (_coopTimingActive)
            {
                if ((currentParticipants & _coopParticipantMask) != _coopParticipantMask)
                {
                    FinishCoopTiming(false);
                    return;
                }

                float duration = Mathf.Lerp(CoopTimingDurationEasySeconds,
                    CoopTimingDurationHardSeconds, _coopDifficulty01);
                _coopTimingPhase01 += dt / duration;
                if (_coopTimingPhase01 >= 1f)
                {
                    FinishCoopTiming(false);
                    return;
                }

                MirrorCoopNetworkState();
                return;
            }

            if (_coopCooldownSeconds > 0f || !straining)
            {
                _coopStrainSeconds = 0f;
                _armingParticipantMask = 0;
                return;
            }

            if (_armingParticipantMask != currentParticipants)
            {
                _armingParticipantMask = currentParticipants;
                _coopStrainSeconds = 0f;
            }

            _coopStrainSeconds += dt;
            float strainDelay = Mathf.Lerp(CoopStrainDelayEasySeconds,
                CoopStrainDelayHardSeconds, difficulty01);
            if (_coopStrainSeconds < strainDelay) return;

            _coopTimingActive = true;
            _coopParticipantMask = currentParticipants;
            _coopSuccessMask = 0;
            _coopTimingPhase01 = 0f;
            _coopDifficulty01 = difficulty01;
            _coopPushDirection = pushDirection;
            _coopStrainSeconds = 0f;
            MirrorCoopNetworkState();
        }

        private void FinishCoopTiming(bool allSucceeded)
        {
            if (allSucceeded && _body != null && !_body.isKinematic && _coopPushDirection.sqrMagnitude > 1e-6f)
            {
                int playerCount = CountBits(_coopParticipantMask);
                Vector3 direction = Vector3.ProjectOnPlane(_coopPushDirection, _supportNormal).normalized;
                _body.AddForce(direction * (CoopBoostImpulsePerPlayerNs * playerCount), ForceMode.Impulse);
                _coopBoostCoastSeconds = CoopBoostCoastSeconds;
                LastCoopBoostDifficulty01 = _coopDifficulty01;
                CoopBoostCount++;
            }

            _coopTimingActive = false;
            _coopParticipantMask = 0;
            _coopSuccessMask = 0;
            _coopTimingPhase01 = 0f;
            _coopPushDirection = Vector3.zero;
            _coopCooldownSeconds = Mathf.Lerp(CoopRetryCooldownEasySeconds,
                CoopRetryCooldownHardSeconds, _coopDifficulty01);
            _armingParticipantMask = 0;
            MirrorCoopNetworkState();
        }

        private void MirrorCoopNetworkState()
        {
            if (Object == null || !Object.IsValid || !Object.HasStateAuthority) return;
            NetCoopTimingActive = _coopTimingActive;
            NetCoopParticipantMask = _coopParticipantMask;
            NetCoopSuccessMask = _coopSuccessMask;
            NetCoopTimingPhase01 = _coopTimingPhase01;
            NetCoopBoostCount = CoopBoostCount;
            NetCoopBoostDifficulty01 = LastCoopBoostDifficulty01;
        }

        private int ParticipantBit(Component pusher, bool createLocal)
        {
            if (pusher == null) return 0;

            if (pusher is NetworkBehaviour networkPusher && networkPusher.Object != null &&
                networkPusher.Object.IsValid && networkPusher.Object.InputAuthority.IsRealPlayer)
            {
                int playerId = networkPusher.Object.InputAuthority.PlayerId;
                return playerId >= 0 && playerId < 31 ? 1 << playerId : 0;
            }

            int free = -1;
            for (int i = 0; i < _localParticipants.Length; i++)
            {
                if (_localParticipants[i] == pusher) return 1 << i;
                if (_localParticipants[i] == null && free < 0) free = i;
            }

            if (!createLocal || free < 0) return 0;
            _localParticipants[free] = pusher;
            return 1 << free;
        }

        private static int CountBits(int mask)
        {
            int count = 0;
            while (mask != 0)
            {
                mask &= mask - 1;
                count++;
            }
            return count;
        }

        private Vector3 SampleSupportNormal()
        {
            if (_body == null) return Vector3.up;

            Vector3 gravity = Physics.gravity;
            Vector3 down = gravity.sqrMagnitude > 1e-6f ? gravity.normalized : Vector3.down;
            float distance = RadiusM * 2f + 0.5f;
            // Fusion 다피어는 피어마다 별도 PhysicsScene을 쓴다. 전역 Physics.Raycast는 기본 씬만
            // 보므로 네트워크 공이 지면 위에 있어도 공중으로 판정된다.
            PhysicsScene physicsScene = gameObject.scene.GetPhysicsScene();
            int hitCount = physicsScene.Raycast(_body.worldCenterOfMass, down, _supportHits, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            Vector3 normal = Vector3.up;
            _hasSupport = false;
            for (int i = 0; i < hitCount; i++)
            {
                RaycastHit hit = _supportHits[i];
                Rigidbody hitBody = hit.rigidbody;
                if (hitBody == _body || hit.distance >= nearest) continue;
                if (hit.collider is CharacterController) continue;
                if (hitBody != null && !hitBody.isKinematic) continue;

                nearest = hit.distance;
                normal = hit.normal;
                _hasSupport = true;
            }

            return normal.sqrMagnitude > 1e-6f ? normal.normalized : Vector3.up;
        }

        public override void Spawned()
        {
            _body = GetComponent<Rigidbody>();

            // ⚠ 스폰 자세를 물리 바디에 먼저 밀어 넣는다. 안 하면 공이 원점으로 순간이동한다(실측:
            // (-2, 1, 8) 에 만들었는데 (0, 6, 0) 에서 떨어졌다).
            //
            // RunnerSimulatePhysics 가 Physics.simulationMode 를 Script 로 바꾸면 스폰 직후에는 아직
            // Simulate() 가 돌지 않아 PhysX 바디 자세가 트랜스폼과 동기화되기 전이다(그때
            // _body.position 은 프리팹 기본값인 원점이다). 그런데 NetworkRigidbody.Spawned 는 상태
            // 권위에서 CopyToBuffer() 로 '물리 바디' 자세를 네트워크 버퍼에 담는다 — 트랜스폼이
            // 아니다. 그래서 원점이 복제되고 다음 틱에 엔진으로 되돌아온다.
            //
            // 프리팹의 컴포넌트 순서가 SnowBallCarrier -> NetworkRigidbody 라 이 대입이 먼저 돈다.
            // 순서를 바꾸면 이 수정이 조용히 무력해진다.
            _body.position = transform.position;
            _body.rotation = transform.rotation;

            // 공의 물리는 권위에서만 돈다. NetworkRigidbody 가 Runner.SetIsSimulated(Object, true)
            // 로 프록시까지 시뮬레이션 대상으로 만들지만, MirrorRemoteMotion 이 프록시의 바디를
            // kinematic 으로 잡아 그 시뮬이 자세를 만들지 못하게 한다 — 프록시는 복제된 자세를
            // 그리기만 한다.
            //
            // 2026-09-01 까지는 각 피어가 함께 굴렸다. 근거는 "서버만 돌리면 밀기가 왕복 시간만큼
            // 늦게 보인다" 였는데, 계측이 그 로컬 시뮬은 늦지 않은 대신 틀렸음을 보였다 — 프록시는
            // 미는 힘을 받은 적이 없어 관성을 재현할 수 없고, 움직인 프레임의 5.5%에서 진행 방향이
            // 뒤집혔다(권위는 0%). 자세한 것은 MirrorRemoteMotion 문서.
            //
            // 트랜스폼 복제도 NetworkRigidbody 가 한다(NetworkTRSP 상속). 그래서 프리팹에서
            // NetworkTransform 을 뺐다 — 둘 다 두면 같은 트랜스폼을 두 컴포넌트가 복제하며 싸운다.

            _massMm = NetMassMm;
            ApplySize();
        }

        /// <summary>
        /// <b>복제 값이 사본을 먹인다 — 이 방향뿐이다.</b> 권위가 아닌 피어는 스스로 질량을 만들지 않고
        /// 받은 값을 그대로 표시한다.
        /// </summary>
        public override void Render()
        {
            if (Object != null && Object.IsValid && !Object.HasStateAuthority)
            {
                // 질량은 여기서 받지 않는다 — 콜라이더 크기를 바꾸므로 물리 스텝 안이어야 한다
                // (FixedUpdateNetwork 참고). 여기 남는 것은 연출 전용 사본뿐이다.
                CoopBoostCount = NetCoopBoostCount;
                LastCoopBoostDifficulty01 = NetCoopBoostDifficulty01;
                MirrorRemoteMotion();
            }
            ApplySize();
        }

        /// <summary>
        /// 터짐 연출. <b>복제된 질량에서 스스로 판정한다</b> — 서버가 이벤트를 따로 보내지 않는다.
        ///
        /// <para>루트 규약이 "연출은 실제로 적용된 복제 상태에서 끌어낸다" 이고, 여기서는 그것이
        /// 공짜다: 질량은 이미 <c>[Networked]</c> 이고 터질 크기는 프리팹에 있으므로 각 피어가
        /// 같은 결론에 도달한다. 이벤트를 보내면 바이트가 늘고 유실될 자리가 하나 늘어난다.</para>
        ///
        /// <para>왜 <c>Despawned</c> 인가: 터짐은 이 오브젝트가 사라지는 <b>유일한</b> 경로다.
        /// 놓기(<c>Release</c>)는 공을 비우기만 하고 없애지 않는다. 그래서 "사라졌고 컸다" 가
        /// 곧 "터졌다" 다. 런너가 내려가는 중이면(씬 교체·종료) 재생하지 않는다 - 그때는 크기와
        /// 무관하게 사라지기 때문이다.</para>
        /// </summary>
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            // 런너가 내려가는 중이면 크기와 무관하게 사라지는 것이라 연출하지 않는다.
            if (runner == null || !runner.IsRunning) return;

            // 서버는 표시를 갖고 있다. 클라이언트는 못 받으므로(복제하지 않는다) 사라짐 자체를
            // 근거로 쓴다 - 놓기는 공을 없애지 않으니 그것으로 충분하다.
            if (Object != null && Object.HasStateAuthority && !_bursting) return;

            PlayBurst();
        }

        /// <summary>
        /// 런너가 없는 판(싱글플레이)에서는 <see cref="Despawned"/> 가 오지 않는다.
        ///
        /// <para>여기서는 <see cref="ServerMarkBursting"/> 표시가 있을 때만 재생한다 — 씬을 닫는
        /// 것과 터지는 것을 구별할 방법이 그것뿐이다.</para>
        /// </summary>
        private void OnDestroy()
        {
            if (Object != null && Object.IsValid) return;   // 네트워크 경로가 이미 처리했다
            if (!_bursting) return;                          // 씬 정리로 사라지는 중이다
            PlayBurst();
        }

        /// <summary>
        /// <b>사라짐이 곧 터짐이다 — 크기를 보지 않는다.</b> 놓기(<c>Release</c>)는 공을 비우기만 하고
        /// 없애지 않으므로, 이 오브젝트가 사라지는 경로는 터짐 하나뿐이다. 전에는 반지름이 문턱의
        /// 0.9 배를 넘는지 봤는데, 터짐이 플레이어 발동이 된 지금은 그 검사가 <b>사실을 추측으로
        /// 바꾸는 일</b>이다.
        /// </summary>
        private void PlayBurst()
        {
            // 데디케이티드 서버는 GPU 가 없다 - 표현 계층을 만들 이유도 없다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

            VisualEffectAsset asset = _burstVfx != null
                ? _burstVfx
                : Resources.Load<VisualEffectAsset>("VFX_SnowBurst");
            if (asset == null) return;

            var go = new GameObject("SnowBurstVfx");
            go.transform.position = transform.position;
            var ve = go.AddComponent<VisualEffect>();
            ve.visualEffectAsset = asset;
            ve.Play();

            // 한 방이라 스스로 끝난다. 수명 상한(1.2 s)에 여유를 얹어 치운다.
            Destroy(go, 2.5f);
        }


        /// <summary>
        /// 질량에서 크기·무게를 만든다. 스케일을 건드리므로 <see cref="SphereCollider"/> 반경도 같이
        /// 따라온다 — 콜라이더를 따로 키우면 두 곳이 갈라진다.
        /// </summary>
        private void ApplySize()
        {
            UpdateGrowthStage();
            float r = RadiusM;
            if (Mathf.Abs(r - _appliedRadiusM) < 1e-4f && _appliedMassMm == _massMm) return;
            _appliedRadiusM = r;
            _appliedMassMm = _massMm;

            transform.localScale = Vector3.one * (r * 2f / _meshDiameterM);

            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_body != null)
            {
                float volumeM3 = _massMm * 0.001f * SnowBallCpu.CellAreaM2;
                _body.mass = Mathf.Max(1f, volumeM3 * SnowDensityKgPerM3);

                // 구름 회전은 ω = v/r 이라 작은 공에서 발산한다. 상한은 크기와 무관한 상수여야
                // "초당 몇 바퀴로 보이는가" 가 일정해진다 - 반지름에 비례시키면 그대로 v/r 이 된다.
                _body.maxAngularVelocity = _maxAngularVelocityRadPerSec;
            }
        }
    }
}
