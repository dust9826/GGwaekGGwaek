using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 실제로 제거한 눈의 양을 단계별 고정 문턱에 누적하고, 그 진행도로 눈덩이 반지름을 제어한다.
    /// 설정 시간은 300 mm 처녀설을 기준 속도로 지날 때의 필요량을 계산하는 기준일 뿐 타이머가 아니다.
    /// </summary>
    // SnowCpuStage(기본 0)가 이번 틱의 수확량을 기록한 뒤, SnowBallCarrier(100)가
    // 추진력을 계산하기 전에 테스트용 제어 반지름과 질량을 확정한다.
    [DefaultExecutionOrder(50)]
    [DisallowMultipleComponent]
    public sealed class SnowballGrowthStageTimer : MonoBehaviour
    {
        private const float MinimumDurationSeconds = 0.1f;
        private const long MinimumRequirementMm = 1L;

        private SnowBallCarrier _carrier;
        private long _controlledMassMm;
        private long _stageHarvestMm;
        private long _pendingHarvestMm;
        private float _controlledRadiusM;
        private float _stageStartRadiusM;
        private float _continuousGrowthShare = SnowballStageModel.DefaultContinuousGrowthShare;
        private readonly float[] _referenceDurationsSeconds =
        {
            SnowballStageModel.DefaultStageDurationSeconds,
            SnowballStageModel.DefaultStageDurationSeconds,
            SnowballStageModel.DefaultStageDurationSeconds,
            SnowballStageModel.DefaultStageDurationSeconds,
        };
        private readonly float[] _referenceSpeedsMps =
        {
            3.2f, 3.65f, 4.1f, 4.55f,
        };
        private readonly long[] _requiredHarvestMm = new long[SnowballStageModel.StageCount];
        private ESnowBallGrowthStage _stage;
        private bool _initialized;

        public ESnowBallGrowthStage Stage => _stage;
        public long AccumulatedHarvestMm => _stageHarvestMm;
        public long RequiredHarvestMm => _stage == ESnowBallGrowthStage.Stage4
            ? 0L
            : _requiredHarvestMm[(int)_stage];
        public float ControlledRadiusM => _initialized
            ? _controlledRadiusM
            : SnowballStageModel.MinRadiusM;
        public float StageProgress01
        {
            get
            {
                if (!_initialized || _carrier == null) return 0f;
                if (_stage == ESnowBallGrowthStage.Stage4) return 1f;
                return Mathf.Clamp01((float)_stageHarvestMm /
                                     System.Math.Max(MinimumRequirementMm, RequiredHarvestMm));
            }
        }

        public void Initialize(SnowBallCarrier carrier)
        {
            if (_initialized || carrier == null) return;

            _carrier = carrier;
            _controlledMassMm = carrier.MassMm;
            _controlledRadiusM = carrier.RadiusM;
            _stageStartRadiusM = carrier.RadiusM;
            _stage = SnowballStageModel.GetStage(_stageStartRadiusM);
            RecalculateRequirements();
            _initialized = true;
        }

        public void ConfigureDuration(float stageDurationSeconds)
        {
            float duration = Mathf.Max(MinimumDurationSeconds, stageDurationSeconds);
            for (int i = 0; i < _referenceDurationsSeconds.Length; i++)
                _referenceDurationsSeconds[i] = duration;
            RecalculateRequirements();
        }

        public void ConfigureStageReference(ESnowBallGrowthStage stage,
            float referenceSeconds, float referenceSpeedMps)
        {
            int index = (int)stage;
            if (index < 0 || index >= SnowballStageModel.StageCount) return;

            _referenceDurationsSeconds[index] = Mathf.Max(MinimumDurationSeconds,
                referenceSeconds);
            _referenceSpeedsMps[index] = Mathf.Max(0.01f, referenceSpeedMps);
            RecalculateRequirement(index);
        }

        public void ConfigureContinuousGrowthShare(float continuousGrowthShare)
        {
            _continuousGrowthShare = Mathf.Clamp01(continuousGrowthShare);
            RecalculateRequirements();
        }

        public void RecordHarvestedSnow(long harvestedMm)
        {
            if (!_initialized || harvestedMm <= 0L) return;
            _pendingHarvestMm += harvestedMm;
        }

        /// <summary>
        /// 단계 진행을 굴리고 그 결과를 공의 질량으로 확정한다.
        ///
        /// <para><b>권위에서만 돈다 (2026-09-01).</b> 이 컴포넌트는 공의 <b>질량 주인</b>인데
        /// (<c>SnowBallCarrier.ServerApplyMass</c> → 스케일 · <c>Rigidbody.mass</c>) 게이트가
        /// 없어서 클라이언트에서도 돌았다. 그런데 수확을 먹여 주는
        /// <see cref="SnowballGrowthFootprintSync"/> 는 <c>HasSimulationAuthority</c> 로 막혀 있어
        /// <b>클라이언트의 타이머는 수확을 한 톨도 못 받는다</b> — <see cref="_controlledMassMm"/>
        /// 가 초기 씨앗 값에 얼어붙은 채 매 스텝 공을 그 크기로 되돌리고,
        /// <c>SnowBallCarrier.Render</c> 가 매 프레임 복제된 실제 크기로 되돌렸다. 공이
        /// <b>씨앗과 실제 크기를 프레임마다 왕복</b>하는 것이 그것이다(사용자 실측 2026-09-01,
        /// "크기가 매우 빠르게 커졌다 작아졌다").</para>
        ///
        /// <para>먹이는 쪽이 이미 권위 전용이므로 먹는 쪽도 권위 전용이어야 짝이 맞는다. 비권위
        /// 피어의 크기는 복제된 질량 하나에서만 나온다.</para>
        ///
        /// <para>⚠ <b>남은 문제 하나는 이 게이트로 안 풀린다.</b> 이 파이프라인은 실행 순서
        /// 0(<c>SnowCpuStage</c>) → 25(<see cref="SnowballGrowthFootprintSync"/>) →
        /// 50(여기) → 100(<c>SnowBallCarrier</c>) 으로 설계됐는데, 네트워크 전환 때 0 과 100 만
        /// <c>FixedUpdateNetwork</c> 로 옮겨지고 <b>가운데 둘은 <c>FixedUpdate</c> 에 남았다.</b>
        /// 그래서 멀티에서는 권위에서도 질량을 0 과 50 이 서로 다른 시계로 쓴다. 싱글은 넷이 같은
        /// <c>FixedUpdate</c> 라 순서가 지켜져 멀쩡하다.</para>
        /// </summary>
        private void FixedUpdate()
        {
            if (!_initialized || _carrier == null) return;
            if (!_carrier.IsAuthority) return;

            long remainingHarvestMm = _pendingHarvestMm;
            _pendingHarvestMm = 0L;

            while (_stage != ESnowBallGrowthStage.Stage4 && remainingHarvestMm > 0L)
            {
                long requiredMm = System.Math.Max(MinimumRequirementMm, RequiredHarvestMm);
                long missingMm = System.Math.Max(0L, requiredMm - _stageHarvestMm);
                long consumedMm = System.Math.Min(remainingHarvestMm, missingMm);
                _stageHarvestMm += consumedMm;
                remainingHarvestMm -= consumedMm;
                if (_stageHarvestMm < requiredMm) break;

                SnowballStageModel.GetStageRange(_stage, out _, out float stageEndRadiusM);
                _stage = (ESnowBallGrowthStage)((int)_stage + 1);
                _stageStartRadiusM = stageEndRadiusM;
                _stageHarvestMm = 0L;
            }

            ApplyProgressRadius();
        }

        private void ApplyProgressRadius()
        {
            float desiredRadiusM;
            if (_stage == ESnowBallGrowthStage.Stage4)
            {
                desiredRadiusM = SnowballStageModel.MaxRadiusM;
            }
            else
            {
                SnowballStageModel.GetStageRange(_stage, out _, out float stageEndRadiusM);
                float continuousEndRadiusM = Mathf.Lerp(_stageStartRadiusM, stageEndRadiusM,
                    _continuousGrowthShare);
                desiredRadiusM = Mathf.Lerp(_stageStartRadiusM, continuousEndRadiusM,
                    StageProgress01);
            }

            _controlledMassMm = SnowBallCpu.MassMmForRadius(desiredRadiusM);
            if (SnowBallCpu.RadiusFromMassMm(_controlledMassMm) + 0.000001f < desiredRadiusM)
                _controlledMassMm++;
            ApplyControlledMassPreservingContact();
        }

        private void RecalculateRequirements()
        {
            for (int i = 0; i < _requiredHarvestMm.Length; i++)
                RecalculateRequirement(i);
        }

        private void RecalculateRequirement(int index)
        {
            _requiredHarvestMm[index] = SnowballStageModel.CalculateRequiredHarvestMm(
                (ESnowBallGrowthStage)index, _referenceDurationsSeconds[index],
                _referenceSpeedsMps[index], _continuousGrowthShare);
        }

        private void ApplyControlledMassPreservingContact()
        {
            Vector3 supportNormal = _carrier.HasSupport
                ? _carrier.SupportNormal
                : Vector3.up;
            if (supportNormal.sqrMagnitude < 0.0001f) supportNormal = Vector3.up;
            supportNormal.Normalize();

            _carrier.ServerApplyMass(_controlledMassMm);

            float nextRadiusM = _carrier.RadiusM;
            float radiusIncreaseM = nextRadiusM - _controlledRadiusM;
            _controlledRadiusM = nextRadiusM;
            if (radiusIncreaseM <= 0.000001f) return;

            Rigidbody body = _carrier.GetComponent<Rigidbody>();
            if (body != null && !body.isKinematic)
                body.position += supportNormal * radiusIncreaseM;
            else
                _carrier.transform.position += supportNormal * radiusIncreaseM;
        }

    }
}
