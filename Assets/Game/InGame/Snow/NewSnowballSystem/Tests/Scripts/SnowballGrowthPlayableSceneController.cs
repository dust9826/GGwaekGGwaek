using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>실제 펭귄이 밀거나 운반하는 눈덩이에 성장 표현을 붙이고 HUD 대상을 따라간다.</summary>
    [DefaultExecutionOrder(1000)]
    [DisallowMultipleComponent]
    public sealed class SnowballGrowthPlayableSceneController : MonoBehaviour
    {
        [SerializeField] private SnowballGrowthArcHud _hud;
        [SerializeField] private PenguinSnowball _penguinSnowball;
        [SerializeField] private Camera _worldCamera;
        [Header("300 mm 처녀설 기준 단계별 성장 시간")]
        [FormerlySerializedAs("_stageDurationSeconds")]
        [SerializeField, Min(0.1f)] private float _seedToStage1ReferenceSeconds =
            SnowballStageModel.DefaultStageDurationSeconds;
        [SerializeField, Min(0.1f)] private float _stage1ToStage2ReferenceSeconds =
            SnowballStageModel.DefaultStageDurationSeconds;
        [SerializeField, Min(0.1f)] private float _stage2ToStage3ReferenceSeconds =
            SnowballStageModel.DefaultStageDurationSeconds;
        [SerializeField, Min(0.1f)] private float _stage3ToStage4ReferenceSeconds =
            SnowballStageModel.DefaultStageDurationSeconds;
        [Tooltip("승급 전까지 연속 성장으로 보여 줄 단계 반지름 증가분. 나머지는 승급 순간 실제 크기로 점프한다.")]
        [SerializeField, Range(0f, 1f)] private float _continuousGrowthShare =
            SnowballStageModel.DefaultContinuousGrowthShare;
        private SnowballGrowthPlayablePresentation _boundPresentation;
        private VisualElement _legacyGrowthHud;

        public void Configure(SnowballGrowthArcHud hud, PenguinSnowball penguinSnowball,
            Camera worldCamera)
        {
            _hud = hud;
            _penguinSnowball = penguinSnowball;
            _worldCamera = worldCamera;
        }

        /// <summary>로컬 아바타를 물린다. <c>_hud</c>는 빌더가 채운 같은 오브젝트의 것을 유지하고
        /// 펭귄과 카메라만 바꾼다. 카메라는 <see cref="Bind"/>가 HUD로 다시 전달한다.</summary>
        public void BindLocalPlayer(PenguinSnowball penguinSnowball, Camera worldCamera)
        {
            _penguinSnowball = penguinSnowball;
            _worldCamera = worldCamera;
        }

        private void Update()
        {
            SnowBallCarrier carrier = FindActiveSnowball();
            if (carrier == null)
            {
                Bind(null);
                return;
            }

            CancelLegacyCoopTiming(carrier, _penguinSnowball);

            SnowballGrowthPlayablePresentation presentation =
                carrier.GetComponent<SnowballGrowthPlayablePresentation>();
            if (presentation == null)
            {
                presentation = carrier.gameObject.AddComponent<SnowballGrowthPlayablePresentation>();
                presentation.Initialize(carrier);
            }
            SnowballGrowthStageTimer timer = carrier.GetComponent<SnowballGrowthStageTimer>();
            if (timer == null)
            {
                timer = carrier.gameObject.AddComponent<SnowballGrowthStageTimer>();
                timer.Initialize(carrier);
            }
            timer.ConfigureContinuousGrowthShare(_continuousGrowthShare);
            PenguinMomentumHandling handling = _penguinSnowball == null
                ? null
                : _penguinSnowball.GetComponent<PenguinMomentumHandling>();
            ConfigureStageReference(timer, handling, ESnowBallGrowthStage.Seed,
                _seedToStage1ReferenceSeconds);
            ConfigureStageReference(timer, handling, ESnowBallGrowthStage.Stage1,
                _stage1ToStage2ReferenceSeconds);
            ConfigureStageReference(timer, handling, ESnowBallGrowthStage.Stage2,
                _stage2ToStage3ReferenceSeconds);
            ConfigureStageReference(timer, handling, ESnowBallGrowthStage.Stage3,
                _stage3ToStage4ReferenceSeconds);
            SnowballGrowthFootprintSync footprint =
                carrier.GetComponent<SnowballGrowthFootprintSync>();
            if (footprint == null)
                footprint = carrier.gameObject.AddComponent<SnowballGrowthFootprintSync>();
            footprint.Configure(carrier, timer, FindSnowStage(carrier));
            presentation.ConfigureStageTimer(timer);
            Bind(presentation);
        }

        private static void ConfigureStageReference(SnowballGrowthStageTimer timer,
            PenguinMomentumHandling handling, ESnowBallGrowthStage stage,
            float referenceSeconds)
        {
            float referenceSpeedMps = handling == null
                ? SnowballStageModel.GetDefaultReferenceSpeedMps(stage)
                : handling.SnowballMaximumSpeedMps(stage, 0f);
            timer.ConfigureStageReference(stage, referenceSeconds, referenceSpeedMps);
        }

        private static SnowCpuStage FindSnowStage(Component carrier)
        {
            foreach (SnowCpuStage candidate in FindObjectsByType<SnowCpuStage>(
                         FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == carrier.gameObject.scene) return candidate;
            }

            return null;
        }

        /// <summary>
        /// 이 펭귄이 지금 밀거나 메고 있는 공. <b>권위는 자기 상태에서, 클라이언트는 복제된 것에서</b>
        /// 읽는다.
        ///
        /// <para>⚠ <b><see cref="PenguinControlState"/> 로 게이트하지 않는다</b>(2026-09-01 정정).
        /// 전이를 호출하는 셋(<c>PenguinSnowball</c> · <c>PenguinCarry</c> · <c>PenguinLocomotion</c>)이
        /// 전부 <c>NetworkDriven</c> 이라 클라이언트에서는 <c>Current</c> 가 <b>영원히 Normal</b> 이다.
        /// 그 게이트가 있는 동안 성장 HUD 는 <b>호스트에게만</b> 떴다 — 클라이언트에서는 이 함수가
        /// 언제나 null 을 돌려줬기 때문이다. 같은 이유로 <c>PenguinCarry.IsCarrying</c> 도 믿을 수
        /// 없어서 복제된 <c>CarriedForPose</c> 를 덧댄다.</para>
        /// </summary>
        private SnowBallCarrier FindActiveSnowball()
        {
            if (_penguinSnowball == null) return null;

            // 밀기 — HeldForPose 가 권위에서는 Held, 클라이언트에서는 복제된 공을 준다.
            SnowBallCarrier pushed = _penguinSnowball.HeldForPose;
            if (pushed != null) return pushed;

            // 운반 — 권위는 로컬 상태가 진실이다.
            PenguinCarry carry = _penguinSnowball.GetComponent<PenguinCarry>();
            if (carry != null && carry.IsCarrying && carry.Cargo is SnowBallCarrier carrier)
                return carrier;

            // 클라이언트는 그 상태를 못 가지므로 복제된 것을 쓴다.
            return _penguinSnowball.CarriedForPose;
        }

        private static void CancelLegacyCoopTiming(SnowBallCarrier carrier,
            Component participant)
        {
            if (carrier == null || participant == null) return;
            if (!carrier.TryGetCoopTiming(participant, out _, out _, out _)) return;

            // 질량 기반 관성 조작에서 낮은 초기 추진력을 기존 협동 미니게임의 "힘 부족"으로
            // 해석하지 않는다. 창이 그려지는 LateUpdate 전에 실패로 닫아 우클릭 타이밍 표시와
            // 보너스 Impulse를 모두 비활성화한다.
            carrier.SubmitCoopTiming(participant, false);
        }

        private void Bind(SnowballGrowthPlayablePresentation presentation)
        {
            if (presentation == _boundPresentation) return;
            _boundPresentation = presentation;
            if (_hud != null) _hud.Configure(_boundPresentation, _worldCamera);
        }

        private void LateUpdate()
        {
            if (_legacyGrowthHud == null)
            {
                SnowballCoopTimingHud legacyHud = FindFirstObjectByType<SnowballCoopTimingHud>();
                if (legacyHud != null)
                {
                    UIDocument document = legacyHud.GetComponent<UIDocument>();
                    if (document != null)
                        _legacyGrowthHud = document.rootVisualElement.Q<VisualElement>(
                            "snowball-growth-hud");
                }
            }

            if (_legacyGrowthHud != null) _legacyGrowthHud.style.display = DisplayStyle.None;
        }
    }
}
