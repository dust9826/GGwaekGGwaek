using UnityEngine;

namespace PPack
{
    /// <summary>프로토타입과 이후 실제 적용이 공유할 수 있는 반지름 기반 단계 계산.</summary>
    public static class SnowballStageModel
    {
        public const float MinRadiusM = SnowBallCpu.SeedRadiusM;
        public const float MaxRadiusM = SnowBallCpu.MaxRadiusM;
        public const int StageCount = 4;
        public const float StageIntervalM = (MaxRadiusM - MinRadiusM) / StageCount;
        public const float DefaultStageDurationSeconds = 7f;
        public const float DefaultContinuousGrowthShare = 0.4f;
        public const int ReferenceFullSnowDepthMm = 300;
        public const float DefaultMaximumHandlingMassKg = 300f;
        public const float DefaultMinimumHandlingMassKg =
            DefaultMaximumHandlingMassKg * MinRadiusM * MinRadiusM * MinRadiusM /
            (MaxRadiusM * MaxRadiusM * MaxRadiusM);
        public const float EffectiveDensityKgPerM3 =
            DefaultMaximumHandlingMassKg /
            (4.18879032f * MaxRadiusM * MaxRadiusM * MaxRadiusM);

        public static float Stage1StartRadiusM => MinRadiusM + StageIntervalM;
        public static float Stage2StartRadiusM => MinRadiusM + StageIntervalM * 2f;
        public static float Stage3StartRadiusM => MinRadiusM + StageIntervalM * 3f;
        public static float Stage4StartRadiusM => MaxRadiusM;

        public static float GetDefaultReferenceSpeedMps(ESnowBallGrowthStage stage)
        {
            switch (stage)
            {
                case ESnowBallGrowthStage.Seed: return 3.2f;
                case ESnowBallGrowthStage.Stage1: return 3.65f;
                case ESnowBallGrowthStage.Stage2: return 4.1f;
                case ESnowBallGrowthStage.Stage3: return 4.55f;
                default: return 5f;
            }
        }

        /// <summary>
        /// 300 mm 처녀설을 단계 기준 속도로 설정 시간 동안 지날 때의 실제 제거량(mm·셀).
        /// 진행도에 따라 제거 반지름도 커지므로, 그 관계를 적분해 완전한 설면에서 설정 시간에
        /// 정확히 필요량을 채우는 고정 문턱을 만든다.
        /// </summary>
        public static long CalculateRequiredHarvestMm(ESnowBallGrowthStage stage,
            float referenceSeconds, float referenceSpeedMps, float continuousGrowthShare,
            int referenceDepthMm = ReferenceFullSnowDepthMm)
        {
            if (stage == ESnowBallGrowthStage.Stage4) return 0L;

            GetStageRange(stage, out float startRadiusM, out float endRadiusM);
            double radiusGrowthM = Mathf.Clamp01(continuousGrowthShare) *
                                   (endRadiusM - startRadiusM);
            double effectiveRadiusM = startRadiusM;
            if (radiusGrowthM > 0.000001d)
            {
                effectiveRadiusM = radiusGrowthM /
                    System.Math.Log((startRadiusM + radiusGrowthM) / startRadiusM);
            }

            double durationSeconds = Mathf.Max(0.1f, referenceSeconds);
            double speedMps = Mathf.Max(0.01f, referenceSpeedMps);
            double depthMm = Mathf.Max(0, referenceDepthMm);
            double requiredMm = durationSeconds * 2d * speedMps * depthMm *
                                effectiveRadiusM / SnowBallCpu.CellAreaM2;
            return System.Math.Max(1L, (long)System.Math.Ceiling(requiredMm));
        }

        public static float ClampRadius(float radiusM) => Mathf.Clamp(radiusM, MinRadiusM, MaxRadiusM);

        public static ESnowBallGrowthStage GetStage(float radiusM)
        {
            float radius = ClampRadius(radiusM);
            if (Mathf.Approximately(radius, MaxRadiusM))
                return ESnowBallGrowthStage.Stage4;
            if (radius < Stage1StartRadiusM) return ESnowBallGrowthStage.Seed;
            if (radius < Stage2StartRadiusM) return ESnowBallGrowthStage.Stage1;
            if (radius < Stage3StartRadiusM) return ESnowBallGrowthStage.Stage2;
            if (radius < Stage4StartRadiusM) return ESnowBallGrowthStage.Stage3;
            return ESnowBallGrowthStage.Stage4;
        }

        public static void GetStageRange(ESnowBallGrowthStage stage, out float startRadiusM,
            out float endRadiusM)
        {
            int stageIndex = Mathf.Clamp((int)stage, 0, StageCount);
            startRadiusM = MinRadiusM + StageIntervalM * stageIndex;
            endRadiusM = stageIndex == StageCount
                ? MaxRadiusM
                : MinRadiusM + StageIntervalM * (stageIndex + 1);
        }

        public static float GetStageProgress01(float radiusM)
        {
            float radius = ClampRadius(radiusM);
            ESnowBallGrowthStage stage = GetStage(radius);
            if (stage == ESnowBallGrowthStage.Stage4) return 1f;
            GetStageRange(stage, out float startRadiusM, out float endRadiusM);
            return Mathf.InverseLerp(startRadiusM, endRadiusM, radius);
        }

        /// <summary>단계별 비교용 눈덩이가 구간 한쪽에 치우치지 않도록 중앙 반지름을 반환한다.</summary>
        public static float GetStageRepresentativeRadius(ESnowBallGrowthStage stage)
        {
            GetStageRange(stage, out float startRadiusM, out float endRadiusM);
            return (startRadiusM + endRadiusM) * 0.5f;
        }

        /// <summary>
        /// 실제 눈의 양과 같은 부피 비율. 반지름 선형 진행도와 달리 큰 눈덩이에서 질량 차이를
        /// 충분히 드러내므로 조작용 질량과 하중 기반 튜닝의 공통 입력으로 쓴다.
        /// </summary>
        public static float GetVolumeProgress01(float radiusM)
        {
            float radius = ClampRadius(radiusM);
            float minimumVolume = MinRadiusM * MinRadiusM * MinRadiusM;
            float maximumVolume = MaxRadiusM * MaxRadiusM * MaxRadiusM;
            float volume = radius * radius * radius;
            return Mathf.InverseLerp(minimumVolume, maximumVolume, volume);
        }

        /// <summary>
        /// 최대 반지름이 지정 질량이 되는 균일 밀도 구체의 조작용 질량. 최소 반지름도 같은
        /// 밀도를 사용하므로 300 kg 상한에서는 약 0.5184 kg이고 단계 경계에서 값이 튀지 않는다.
        /// </summary>
        public static float GetEffectiveHandlingMassKg(float radiusM,
            float maximumMassKg = DefaultMaximumHandlingMassKg)
        {
            float radius = ClampRadius(radiusM);
            float maximum = Mathf.Max(0.01f, maximumMassKg);
            float radiusRatio = radius / MaxRadiusM;
            return maximum * radiusRatio * radiusRatio * radiusRatio;
        }

    }
}
