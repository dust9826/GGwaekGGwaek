using UnityEngine;

namespace PPack
{
    /// <summary>한 의뢰의 난이도·보상·TTL·클리어 추가시간을 계산한 결과.</summary>
    public readonly struct RequestBalanceResult
    {
        public RequestBalanceResult(float difficulty, int reward, float ttlSeconds, float timeBonusSeconds)
        {
            Difficulty = difficulty;
            Reward = reward;
            TtlSeconds = ttlSeconds;
            TimeBonusSeconds = timeBonusSeconds;
        }

        public float Difficulty { get; }
        public int Reward { get; }
        public float TtlSeconds { get; }
        public float TimeBonusSeconds { get; }
    }

    /// <summary>의뢰 밸런스의 순수 계산. RNG·씬·GPU와 무관해 EditMode에서 전부 검증된다 —
    /// 랜덤(지터·집·종류·버스트)은 <see cref="RequestDirector"/>가 넣고, 여기는 결정론적이다.
    ///
    /// <para>난이도 = (거리/정규화) × 종류가중치 × 지터 × 전역스칼라. 보상·TTL·추가시간은 난이도에
    /// 비례하고, 시간이 지날수록 전역스칼라는 오르되 TTL·추가시간 스칼라는 내려간다.</para></summary>
    public static class RequestBalance
    {
        /// <summary>시간이 지날수록 오르는 전역 난이도 스칼라. 1에서 시작해 상한까지.</summary>
        public static float GlobalDifficultyScalar(StageBalanceConfig config, float elapsedSeconds)
        {
            float minutes = Mathf.Max(elapsedSeconds, 0f) / 60f;
            float scalar = 1f + config.GlobalDifficultyRampPerMinute * minutes;
            return Mathf.Clamp(scalar, 1f, Mathf.Max(config.GlobalDifficultyMax, 1f));
        }

        /// <summary>시간이 지날수록 TTL을 줄이는 스칼라. 1에서 하한까지.</summary>
        public static float TtlScalar(StageBalanceConfig config, float elapsedSeconds)
        {
            float minutes = Mathf.Max(elapsedSeconds, 0f) / 60f;
            float scalar = 1f - config.TtlRampPerMinute * minutes;
            return Mathf.Clamp(scalar, config.TtlScalarMin, 1f);
        }

        /// <summary>시간이 지날수록 클리어 추가시간을 줄이는 스칼라. 1에서 하한까지.</summary>
        public static float ClearBonusScalar(StageBalanceConfig config, float elapsedSeconds)
        {
            float minutes = Mathf.Max(elapsedSeconds, 0f) / 60f;
            float scalar = 1f - config.ClearBonusRampPerMinute * minutes;
            return Mathf.Clamp(scalar, config.ClearBonusScalarMin, 1f);
        }

        /// <summary>의뢰 하나의 밸런스를 계산한다.</summary>
        /// <param name="kindWeight">상자 종류의 난이도 가중치(<see cref="GiftBoxCatalog"/>).</param>
        /// <param name="distanceM">기지→집 경로 길이(m).</param>
        /// <param name="difficultyRatio">의뢰별 랜덤 지터(예: 0.9~1.1).</param>
        /// <param name="elapsedSeconds">게임 경과 시간(전역 스칼라 계산용).</param>
        public static RequestBalanceResult Evaluate(StageBalanceConfig config, float kindWeight,
                                                    float distanceM, float difficultyRatio, float elapsedSeconds)
        {
            float global = GlobalDifficultyScalar(config, elapsedSeconds);
            float distanceTerm = Mathf.Max(distanceM, 0f) / Mathf.Max(config.DistanceNormalizer, 1f);
            float difficulty = Mathf.Max(distanceTerm * kindWeight * difficultyRatio * global, 0f);

            int reward = config.RewardBase + Mathf.RoundToInt(config.RewardPerDifficulty * difficulty);
            float ttl = config.TtlBase * difficulty * TtlScalar(config, elapsedSeconds);
            float timeBonus = config.ClearTimeBonusBase * difficulty * ClearBonusScalar(config, elapsedSeconds);

            return new RequestBalanceResult(difficulty, reward, ttl, timeBonus);
        }
    }
}
