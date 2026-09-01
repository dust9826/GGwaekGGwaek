using System;
using UnityEngine;
using UnityEngine.Serialization;

namespace PPack
{
    /// <summary>
    /// 실험 프리팹에만 붙이는 단계별 관성 조작 프로필. 일반 슬라이딩의 중력·카빙·그립은
    /// <see cref="PenguinLocomotion"/>에 남기고, 운반과 눈덩이 밀기의 속도 곡선만 덧씌운다.
    /// </summary>
    [DefaultExecutionOrder(100)]
    [DisallowMultipleComponent]
    public sealed class PenguinMomentumHandling : MonoBehaviour
    {
        [Serializable]
        private struct HandlingPoint
        {
            [Tooltip("입력을 시작했을 때의 목표 속도(m/s). 실제 속도를 순간 변경하지는 않는다.")]
            [Min(0f)] public float InitialTargetSpeedMps;
            [Tooltip("평지에서 추진 입력을 유지했을 때의 최고 목표 속도(m/s).")]
            [Min(0.1f)] public float MaximumSpeedMps;
            [Tooltip("초기 목표에서 최고 목표까지 가속 곡선이 진행되는 시간(초).")]
            [Min(0.05f)] public float AccelerationSeconds;
            [Tooltip("운반 상태에서 평지 최고속도부터 자연 감속해 멈추는 기준 시간(초).")]
            [Min(0.05f)] public float CoastStopSeconds;
            [Tooltip("운반 상태에서 평지 최고속도부터 S로 멈추는 기준 시간(초).")]
            [Min(0.05f)] public float BrakeStopSeconds;
            [Tooltip("같은 A/D 방향을 유지해 조향 권한을 모두 얻는 시간(초). 방향을 바꾸면 다시 시작한다.")]
            [Min(0.05f)] public float SteerResponseSeconds;

            public HandlingPoint(float initialTargetSpeedMps, float maximumSpeedMps,
                float accelerationSeconds, float coastStopSeconds, float brakeStopSeconds,
                float steerResponseSeconds)
            {
                InitialTargetSpeedMps = initialTargetSpeedMps;
                MaximumSpeedMps = maximumSpeedMps;
                AccelerationSeconds = accelerationSeconds;
                CoastStopSeconds = coastStopSeconds;
                BrakeStopSeconds = brakeStopSeconds;
                SteerResponseSeconds = steerResponseSeconds;
            }

            public static HandlingPoint Lerp(HandlingPoint from, HandlingPoint to, float value01)
            {
                float t = Mathf.Clamp01(value01);
                return new HandlingPoint(
                    Mathf.Lerp(from.InitialTargetSpeedMps, to.InitialTargetSpeedMps, t),
                    Mathf.Lerp(from.MaximumSpeedMps, to.MaximumSpeedMps, t),
                    Mathf.Lerp(from.AccelerationSeconds, to.AccelerationSeconds, t),
                    Mathf.Lerp(from.CoastStopSeconds, to.CoastStopSeconds, t),
                    Mathf.Lerp(from.BrakeStopSeconds, to.BrakeStopSeconds, t),
                    Mathf.Lerp(from.SteerResponseSeconds, to.SteerResponseSeconds, t));
            }
        }

        private const float BaseBodyMassKg = 30f;
        private const float BaseKickAccelerationMps2 = 6f;
        private const float KickPulseAverage01 = 0.6f * (2f / Mathf.PI);
        private const float CarryFrictionCoefficient = 0.05f;
        private const float CarryAirDragForceCoefficient = 0.0168f;
        private const float SnowRollingResistanceCoefficient = 0.015f;
        private const float AirDensityKgPerM3 = 1.225f;
        private const float SnowballDragCoefficient = 0.47f;
        private const float HeavyInitialSteerResponse01 = 0.55f;
        private const float TargetTrackingForceReserve = 1.2f;
        private const float SnowballSteerReferenceSpeedMps = 9f;
        private const float QuarterTurnRadians = Mathf.PI * 0.5f;
        private const float SeedImmediateSteerAuthorityAtReferenceSpeed01 = 0.646f;
        private const float SeedCommittedSteerAuthorityAtReferenceSpeed01 = 0.744f;
        private const float MaximumImmediateSteerAuthorityAtReferenceSpeed01 = 0f;
        private const float MaximumCommittedSteerAuthorityAtReferenceSpeed01 = 0.33f;

        [Header("운반 — 슬라이딩 조작 위에 적용")]
        [SerializeField] private HandlingPoint _carryLight = new(
            3.8f, 7.2f, 1.1f, 3f, 1f, 0.15f);
        [SerializeField] private HandlingPoint _carryHeavy = new(
            2.2f, 9f, 2.8f, 5f, 1.8f, 0.85f);

        [Header("눈덩이 — 실제 속도 조향 곡선")]
        [Tooltip("눈덩이가 정지해 있을 때의 조향 권한.")]
        [SerializeField, Range(0f, 1f)] private float _snowballSteerAtZeroMps = 1f;
        [Tooltip("눈덩이가 9 m/s일 때의 조향 권한. 9 m/s를 넘어도 같은 기울기로 감소해 0에 도달한다.")]
        [SerializeField, Range(0f, 1f)] private float _snowballSteerAtNineMps = 0.28f;

        [Header("눈덩이 — 최대 반지름 물리 기준")]
        [Tooltip("W를 놓았을 때 추진 누적만 0으로 돌아가는 시간. 눈덩이 속도는 직접 줄이지 않는다.")]
        [SerializeField, Min(0.05f)] private float _snowballDriveReleaseSeconds = 0.2f;
        [Tooltip("반지름 1.5m 눈덩이가 현재 최고 목표속도에서 평지 자연 감속으로 멈추는 기준 시간.")]
        [SerializeField, Min(0.1f)] private float _snowballMaximumCoastStopSeconds = 5.2f;
        [Tooltip("반지름 1.5m 눈덩이가 현재 최고 목표속도에서 S 제동으로 멈추는 기준 시간.")]
        [SerializeField, Min(0.1f)] private float _snowballMaximumBrakeStopSeconds = 1.9f;
        [Tooltip("반지름 1.5m 눈덩이가 현재 최고 목표속도에서 90도 방향을 바꾸는 데 필요한 최소 기준 시간.")]
        [SerializeField, Min(0.1f)] private float _snowballMaximumQuarterTurnSeconds = 3.5f;

        [Header("눈덩이 — 반지름 단계 경계 기준")]
        [SerializeField] private HandlingPoint _snowballSeed = new(
            2.4f, 3.2f, 0.65f, 2.2f, 0.7f, 0.35f);
        [FormerlySerializedAs("_snowballStage2")]
        [SerializeField] private HandlingPoint _snowballStage1Point = new(
            2.15f, 3.65f, 1.2f, 2.7f, 0.9f, 0.8f);
        [FormerlySerializedAs("_snowballStage3")]
        [SerializeField] private HandlingPoint _snowballStage2Point = new(
            1.8f, 4.1f, 2f, 3.4f, 1.2f, 1.4f);
        [FormerlySerializedAs("_snowballStage4")]
        [SerializeField] private HandlingPoint _snowballStage3Point = new(
            1.4f, 4.55f, 3f, 4.3f, 1.55f, 2.1f);
        [FormerlySerializedAs("_snowballMaximum")]
        [SerializeField] private HandlingPoint _snowballStage4Point = new(
            1.1f, 5f, 4.2f, 5.2f, 1.9f, 3f);

        private PenguinSnowball _snowball;
        private PenguinLocomotion _locomotion;
        private PenguinControlState _controlState;
        private Rigidbody _body;

        public float SlideBrakeForceN
        {
            get
            {
                HandlingPoint point = CurrentCarryPoint();
                float massKg = CurrentBodyMassKg();
                float desiredForceN = massKg * point.MaximumSpeedMps /
                                      Mathf.Max(0.05f, point.BrakeStopSeconds);
                return Mathf.Max(0f, desiredForceN - CarryPassiveResistanceN(
                    massKg, point.MaximumSpeedMps, CurrentGroundNormal()));
            }
        }

        public float SnowballBrakeForceN
        {
            get => SnowballBrakeForceNForRadius(ActiveSnowballRadiusOrMinimum());
        }

        public float SnowballDriveReleaseSeconds => _snowballDriveReleaseSeconds;

        public float BuildUpSeconds => TryGetActiveSnowballRadius(out float radiusM)
            ? SnowballPointForRadius(radiusM).AccelerationSeconds
            : CurrentCarryPoint().AccelerationSeconds;

        public float BuildDownSeconds => TryGetActiveSnowballRadius(out _)
            ? _snowballDriveReleaseSeconds
            : CurrentCarryPoint().CoastStopSeconds;

        public float BrakeResetSeconds => TryGetActiveSnowballRadius(out _)
            ? _snowballDriveReleaseSeconds
            : CurrentCarryPoint().BrakeStopSeconds;

        public float SteerCommitSeconds => TryGetActiveSnowballRadius(out float radiusM)
            ? SnowballPointForRadius(radiusM).SteerResponseSeconds
            : CurrentCarryPoint().SteerResponseSeconds;

        public float SteerReleaseSeconds => Mathf.Max(0.05f, SteerCommitSeconds * 0.35f);

        public float CarryPropulsionMultiplier
        {
            get
            {
                float cargoShare01 = CurrentCargoShare01();
                HandlingPoint point = CarryPoint(cargoShare01);
                float desiredAccelerationMps2 = RequiredAccelerationMps2(point);
                float mobility = Mathf.Lerp(0.65f, 0.4f, cargoShare01);
                float availableAverageAcceleration = BaseKickAccelerationMps2 *
                                                     KickPulseAverage01 * mobility;
                return desiredAccelerationMps2 /
                       Mathf.Max(0.01f, availableAverageAcceleration);
            }
        }

        public float SnowballPropulsionMultiplier
        {
            get
            {
                float radiusM = ActiveSnowballRadiusOrMinimum();
                return SnowballDriveForceN(radiusM) / SnowBallCarrier.PlayerPushForceN;
            }
        }

        private void Awake()
        {
            CacheComponents();
        }

        private void FixedUpdate()
        {
            CacheComponents();
            ApplyCarryCoastResistance(Time.fixedDeltaTime);
        }

        public float SlideTargetSpeedMps(float legacyTargetSpeedMps, float buildUp01)
        {
            return legacyTargetSpeedMps;
        }

        public float CarryTargetSpeedMps(float cargoShare01, float buildUp01)
        {
            return TargetSpeedMps(CarryPoint(cargoShare01), buildUp01);
        }

        public float CarryInitialTargetSpeedMps(float cargoShare01)
            => CarryPoint(cargoShare01).InitialTargetSpeedMps;

        public float CarryMaximumSpeedMps(float cargoShare01)
            => CarryPoint(cargoShare01).MaximumSpeedMps;

        public float CarryAccelerationSeconds(float cargoShare01)
            => CarryPoint(cargoShare01).AccelerationSeconds;

        public float CarryCoastStopSeconds(float cargoShare01)
            => CarryPoint(cargoShare01).CoastStopSeconds;

        public float CarryBrakeStopSeconds(float cargoShare01)
            => CarryPoint(cargoShare01).BrakeStopSeconds;

        public float SnowballTargetSpeedMps(float walkSpeedMps, float growth01, float buildUp01)
        {
            _ = walkSpeedMps;
            float radiusM = TryGetActiveSnowballRadius(out float activeRadiusM)
                ? activeRadiusM
                : Mathf.Lerp(SnowballStageModel.MinRadiusM,
                    SnowballStageModel.MaxRadiusM, Mathf.Clamp01(growth01));
            return TargetSpeedMps(SnowballPointForRadius(radiusM), buildUp01);
        }

        public float SnowballTargetSpeedMps(ESnowBallGrowthStage stage, float stageProgress01,
            float buildUp01)
        {
            return TargetSpeedMps(SnowballPoint(stage, stageProgress01), buildUp01);
        }

        public float SnowballInitialTargetSpeedMps(ESnowBallGrowthStage stage,
            float stageProgress01) => SnowballPoint(stage, stageProgress01).InitialTargetSpeedMps;

        public float SnowballMaximumSpeedMps(ESnowBallGrowthStage stage,
            float stageProgress01) => SnowballPoint(stage, stageProgress01).MaximumSpeedMps;

        public float SnowballBuildUpSeconds(ESnowBallGrowthStage stage, float stageProgress01)
            => SnowballPoint(stage, stageProgress01).AccelerationSeconds;

        public float SnowballSteerResponseSeconds(ESnowBallGrowthStage stage,
            float stageProgress01) => SnowballPoint(stage, stageProgress01).SteerResponseSeconds;

        public float SnowballCoastStopSeconds(ESnowBallGrowthStage stage, float stageProgress01)
        {
            float radiusM = RadiusForStageProgress(stage, stageProgress01);
            float momentumNs = SnowballStageModel.GetEffectiveHandlingMassKg(radiusM) *
                               SnowballPoint(stage, stageProgress01).MaximumSpeedMps;
            return momentumNs / Mathf.Max(0.01f, SnowballCoastResistanceForceN(radiusM));
        }

        public float SnowballBrakeStopSeconds(ESnowBallGrowthStage stage, float stageProgress01)
        {
            float radiusM = RadiusForStageProgress(stage, stageProgress01);
            float momentumNs = SnowballStageModel.GetEffectiveHandlingMassKg(radiusM) *
                               SnowballPoint(stage, stageProgress01).MaximumSpeedMps;
            return momentumNs / Mathf.Max(0.01f, SnowballBrakeForceNForRadius(radiusM));
        }

        /// <summary>
        /// 평지에서 추진을 놓았을 때 사용할 총 저항 상한. 질량(r³)보다 느린 면적(r²) 비율로
        /// 증가하므로 큰 공일수록 같은 속도에서 더 오래 굴러간다.
        /// </summary>
        public float SnowballCoastResistanceForceN(float radiusM)
        {
            return MaximumRadiusReferenceForceN(_snowballMaximumCoastStopSeconds) *
                   SnowballAreaRatio01(radiusM);
        }

        /// <summary>S 입력의 능동 제동력 상한. 정지점을 넘지 않는 제한은 공 쪽에서 적용한다.</summary>
        public float SnowballBrakeForceNForRadius(float radiusM)
        {
            return MaximumRadiusReferenceForceN(_snowballMaximumBrakeStopSeconds) *
                   SnowballAreaRatio01(radiusM);
        }

        /// <summary>
        /// 진행 방향을 바꾸는 횡력 상한. 요구 속도를 즉시 맞추지 않고 이 힘까지만 적용해
        /// 질량과 속도가 큰 공의 방향 전환이 실제 운동량만큼 늦어진다.
        /// </summary>
        public float SnowballSteerForceN(float radiusM)
        {
            float maximumMomentumNs = SnowballStageModel.DefaultMaximumHandlingMassKg *
                                      _snowballStage4Point.MaximumSpeedMps;
            float maximumForceN = maximumMomentumNs * QuarterTurnRadians /
                                  Mathf.Max(0.1f, _snowballMaximumQuarterTurnSeconds);
            return maximumForceN * SnowballAreaRatio01(radiusM);
        }

        public float SnowballDriveMultiplier(ESnowBallGrowthStage stage, float stageProgress01)
        {
            return SnowballDriveForceN(RadiusForStageProgress(stage, stageProgress01)) /
                   SnowBallCarrier.PlayerPushForceN;
        }

        public float SlideSteerAuthority(float cargoShare01, float commitment01)
        {
            if (cargoShare01 <= 0.0001f) return 1f;

            HandlingPoint point = CarryPoint(cargoShare01);
            return SteerResponseAuthority(point, commitment01);
        }

        public float SnowballSteerAuthority(float growth01, float speedMps, float commitment01)
        {
            float radiusM = TryGetActiveSnowballRadius(out float activeRadiusM)
                ? activeRadiusM
                : Mathf.Lerp(SnowballStageModel.MinRadiusM,
                    SnowballStageModel.MaxRadiusM, Mathf.Clamp01(growth01));
            return SnowballSpeedSteerAuthority(speedMps) *
                   SnowballSteerResponseAuthority(radiusM, commitment01);
        }

        public float SnowballSteerAuthority(ESnowBallGrowthStage stage, float stageProgress01,
            float speedMps, float commitment01)
        {
            float radiusM = RadiusForStageProgress(stage, stageProgress01);
            return SnowballSpeedSteerAuthority(speedMps) *
                   SnowballSteerResponseAuthority(radiusM, commitment01);
        }

        public float SnowballSpeedSteerAuthority(float speedMps)
        {
            float speed01 = Mathf.Max(0f, speedMps) / SnowballSteerReferenceSpeedMps;
            return Mathf.Max(0f, Mathf.LerpUnclamped(
                _snowballSteerAtZeroMps, _snowballSteerAtNineMps, speed01));
        }

        private void ApplyCarryCoastResistance(float dt)
        {
            if (_body == null || _locomotion == null || _controlState == null ||
                _controlState.Current != EPenguinControlState.Carrying ||
                _locomotion.IsMomentumBraking || _locomotion.MomentumTargetSpeedMps > 0.01f ||
                _body.isKinematic || dt <= 0f) return;

            Vector3 normal = CurrentGroundNormal();
            Vector3 surfaceVelocity = Vector3.ProjectOnPlane(_body.linearVelocity, normal);
            float speedMps = surfaceVelocity.magnitude;
            if (speedMps <= 0.0001f) return;

            HandlingPoint point = CurrentCarryPoint();
            float targetResistanceN = _body.mass * point.MaximumSpeedMps /
                                      Mathf.Max(0.05f, point.CoastStopSeconds);
            float passiveResistanceN = CarryPassiveResistanceN(
                _body.mass, speedMps, normal);
            float supplementalForceN = Mathf.Min(
                speedMps * _body.mass / dt,
                Mathf.Max(0f, targetResistanceN - passiveResistanceN));
            if (supplementalForceN > 0f)
                _body.AddForce(-surfaceVelocity.normalized * supplementalForceN,
                    ForceMode.Force);
        }

        private HandlingPoint CurrentCarryPoint() => CarryPoint(CurrentCargoShare01());

        private HandlingPoint CarryPoint(float cargoShare01)
            => HandlingPoint.Lerp(_carryLight, _carryHeavy, Smooth(cargoShare01));

        private HandlingPoint SnowballPointForRadius(float radiusM)
        {
            ESnowBallGrowthStage stage = SnowballStageModel.GetStage(radiusM);
            return SnowballPoint(stage, SnowballStageModel.GetStageProgress01(radiusM));
        }

        private HandlingPoint SnowballPoint(ESnowBallGrowthStage stage, float stageProgress01)
        {
            HandlingPoint start;
            HandlingPoint end;
            switch (stage)
            {
                case ESnowBallGrowthStage.Seed:
                    start = _snowballSeed;
                    end = _snowballStage1Point;
                    break;
                case ESnowBallGrowthStage.Stage1:
                    start = _snowballStage1Point;
                    end = _snowballStage2Point;
                    break;
                case ESnowBallGrowthStage.Stage2:
                    start = _snowballStage2Point;
                    end = _snowballStage3Point;
                    break;
                case ESnowBallGrowthStage.Stage3:
                    start = _snowballStage3Point;
                    end = _snowballStage4Point;
                    break;
                default:
                    start = _snowballStage4Point;
                    end = _snowballStage4Point;
                    break;
            }

            return HandlingPoint.Lerp(start, end, Mathf.Clamp01(stageProgress01));
        }

        private float SnowballDriveForceN(float radiusM)
        {
            HandlingPoint point = SnowballPointForRadius(radiusM);
            float massKg = SnowballStageModel.GetEffectiveHandlingMassKg(radiusM);
            float accelerationMps2 = RequiredAccelerationMps2(point);
            return massKg * accelerationMps2 + SnowballPassiveResistanceN(
                radiusM, massKg, point.MaximumSpeedMps);
        }

        private float MaximumRadiusReferenceForceN(float stopSeconds)
        {
            return SnowballStageModel.DefaultMaximumHandlingMassKg *
                   _snowballStage4Point.MaximumSpeedMps / Mathf.Max(0.1f, stopSeconds);
        }

        private static float SnowballAreaRatio01(float radiusM)
        {
            float radius01 = Mathf.Clamp(radiusM, SnowballStageModel.MinRadiusM,
                SnowballStageModel.MaxRadiusM) / SnowballStageModel.MaxRadiusM;
            return radius01 * radius01;
        }

        private static float TargetSpeedMps(HandlingPoint point, float buildUp01)
        {
            return Mathf.Lerp(point.InitialTargetSpeedMps, point.MaximumSpeedMps,
                Smooth(buildUp01));
        }

        private static float RequiredAccelerationMps2(HandlingPoint point)
        {
            float seconds = Mathf.Max(0.05f, point.AccelerationSeconds);
            float fromRest = point.MaximumSpeedMps / seconds;
            float smoothStepPeak = 1.5f *
                (point.MaximumSpeedMps - point.InitialTargetSpeedMps) / seconds;
            return Mathf.Max(fromRest, smoothStepPeak) * TargetTrackingForceReserve;
        }

        private static float SteerResponseAuthority(HandlingPoint point, float commitment01)
        {
            float responseLoad01 = Mathf.InverseLerp(0.08f, 1f,
                point.SteerResponseSeconds);
            float initialAuthority = Mathf.Lerp(1f, HeavyInitialSteerResponse01,
                responseLoad01);
            float responseAuthority = Mathf.Lerp(initialAuthority, 1f,
                Smooth(commitment01));
            return Mathf.Clamp01(responseAuthority);
        }

        private float SnowballSteerResponseAuthority(float radiusM, float commitment01)
        {
            float radius = SnowballStageModel.ClampRadius(radiusM);
            float growth01 = Mathf.InverseLerp(SnowballStageModel.MinRadiusM,
                SnowballStageModel.MaxRadiusM, radius);
            HandlingPoint point = SnowballPointForRadius(radius);
            float referenceSpeedAuthority = SnowballSpeedSteerAuthority(point.MaximumSpeedMps);
            if (referenceSpeedAuthority <= 0.0001f) return 0f;

            float immediateAuthorityAtReferenceSpeed = Mathf.Lerp(
                SeedImmediateSteerAuthorityAtReferenceSpeed01,
                MaximumImmediateSteerAuthorityAtReferenceSpeed01,
                growth01);
            float committedAuthorityAtReferenceSpeed = Mathf.Lerp(
                SeedCommittedSteerAuthorityAtReferenceSpeed01,
                MaximumCommittedSteerAuthorityAtReferenceSpeed01,
                growth01);

            float initialAuthority = immediateAuthorityAtReferenceSpeed /
                                     referenceSpeedAuthority;
            float committedAuthority = committedAuthorityAtReferenceSpeed /
                                       referenceSpeedAuthority;
            return Mathf.Clamp01(Mathf.Lerp(initialAuthority, committedAuthority,
                Smooth(commitment01)));
        }

        private bool TryGetActiveSnowballRadius(out float radiusM)
        {
            if (_snowball == null) _snowball = GetComponent<PenguinSnowball>();
            SnowBallCarrier carrier = _snowball != null ? _snowball.HeldForPose : null;
            if (carrier == null)
            {
                radiusM = SnowballStageModel.MinRadiusM;
                return false;
            }

            radiusM = carrier.RadiusM;
            return true;
        }

        private float ActiveSnowballRadiusOrMinimum()
            => TryGetActiveSnowballRadius(out float radiusM)
                ? radiusM
                : SnowballStageModel.MinRadiusM;

        private float CurrentCargoShare01()
        {
            float totalMassKg = CurrentBodyMassKg();
            return Mathf.Clamp01((totalMassKg - BaseBodyMassKg) /
                                 Mathf.Max(0.01f, totalMassKg));
        }

        private float CurrentBodyMassKg()
        {
            if (_body == null) _body = GetComponent<Rigidbody>();
            return _body != null ? Mathf.Max(BaseBodyMassKg, _body.mass) : BaseBodyMassKg;
        }

        private Vector3 CurrentGroundNormal()
        {
            if (_locomotion == null) _locomotion = GetComponent<PenguinLocomotion>();
            Vector3 normal = _locomotion != null ? _locomotion.GroundNormal : Vector3.up;
            return normal.sqrMagnitude > 0.0001f ? normal.normalized : Vector3.up;
        }

        private static float CarryPassiveResistanceN(float massKg, float speedMps,
            Vector3 groundNormal)
        {
            float normalAcceleration = Mathf.Abs(Vector3.Dot(
                Physics.gravity, groundNormal.normalized));
            return CarryFrictionCoefficient * massKg * normalAcceleration +
                   CarryAirDragForceCoefficient * speedMps * speedMps;
        }

        private static float SnowballPassiveResistanceN(float radiusM, float massKg,
            float speedMps)
        {
            float rollingForceN = SnowRollingResistanceCoefficient * massKg *
                                  Physics.gravity.magnitude;
            float areaM2 = Mathf.PI * radiusM * radiusM;
            float dragForceN = 0.5f * AirDensityKgPerM3 * SnowballDragCoefficient *
                               areaM2 * speedMps * speedMps;
            return rollingForceN + dragForceN;
        }

        private static float RadiusForStageProgress(ESnowBallGrowthStage stage,
            float stageProgress01)
        {
            SnowballStageModel.GetStageRange(stage, out float startRadiusM,
                out float endRadiusM);
            return Mathf.Lerp(startRadiusM, endRadiusM, Mathf.Clamp01(stageProgress01));
        }

        private void CacheComponents()
        {
            if (_snowball == null) _snowball = GetComponent<PenguinSnowball>();
            if (_locomotion == null) _locomotion = GetComponent<PenguinLocomotion>();
            if (_controlState == null) _controlState = GetComponent<PenguinControlState>();
            if (_body == null) _body = GetComponent<Rigidbody>();
        }

        private static float Smooth(float value01)
        {
            float t = Mathf.Clamp01(value01);
            return t * t * (3f - 2f * t);
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            Validate(ref _carryLight);
            Validate(ref _carryHeavy);
            Validate(ref _snowballSeed);
            Validate(ref _snowballStage1Point);
            Validate(ref _snowballStage2Point);
            Validate(ref _snowballStage3Point);
            Validate(ref _snowballStage4Point);
            _snowballSteerAtZeroMps = Mathf.Clamp01(_snowballSteerAtZeroMps);
            _snowballSteerAtNineMps = Mathf.Clamp(
                _snowballSteerAtNineMps, 0f, _snowballSteerAtZeroMps);
            _snowballDriveReleaseSeconds = Mathf.Max(0.05f, _snowballDriveReleaseSeconds);
            _snowballMaximumCoastStopSeconds = Mathf.Max(
                0.1f, _snowballMaximumCoastStopSeconds);
            _snowballMaximumBrakeStopSeconds = Mathf.Max(
                0.1f, _snowballMaximumBrakeStopSeconds);
            _snowballMaximumQuarterTurnSeconds = Mathf.Max(
                0.1f, _snowballMaximumQuarterTurnSeconds);
        }

        private static void Validate(ref HandlingPoint point)
        {
            point.InitialTargetSpeedMps = Mathf.Max(0f, point.InitialTargetSpeedMps);
            point.MaximumSpeedMps = Mathf.Max(0.1f,
                Mathf.Max(point.InitialTargetSpeedMps, point.MaximumSpeedMps));
            point.AccelerationSeconds = Mathf.Max(0.05f, point.AccelerationSeconds);
            point.CoastStopSeconds = Mathf.Max(0.05f, point.CoastStopSeconds);
            point.BrakeStopSeconds = Mathf.Max(0.05f, point.BrakeStopSeconds);
            point.SteerResponseSeconds = Mathf.Max(0.05f, point.SteerResponseSeconds);
        }
#endif
    }
}
