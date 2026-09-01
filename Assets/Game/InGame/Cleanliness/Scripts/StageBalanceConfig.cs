using UnityEngine;

namespace PPack
{
    /// <summary>스테이지 밸런스 노브 한 곳. 게임매니저·의뢰 디렉터가 매 틱 이 에셋을 읽으므로
    /// <b>플레이 중 값을 바꾸면 즉시 반영</b>된다. ScriptableObject라 플레이 모드에서 만진 값이
    /// 플레이 종료 후에도 남는다(컴포넌트·씬 편집은 원복된다) — 밸런스 반복의 핵심 이유다.
    ///
    /// <para>프리셋은 파일별로 나눈다(Balance_Test/Easy/Real). .asset은 YAML이라 Plastic이 병합하지
    /// 못하므로 프리셋 하나당 파일 하나로 둔다. SO 실시간 편집 persist는 에디터 한정이라, 확정값은
    /// 프리셋 에셋으로 굳힌다.</para></summary>
    [CreateAssetMenu(menuName = "PPack/Cleanliness/Stage Balance Config")]
    public sealed class StageBalanceConfig : ScriptableObject
    {
        [Header("전역 시간")]
        [Min(1f)] public float StartSeconds = 60f;
        [Tooltip("전역 시간 상한(초). 0이면 상한 없음.")]
        [Min(0f)] public float MaxSeconds = 0f;
        [Tooltip("클리어 시 더할 기본 시간(초). 실제 추가량 = 이 값 × 난이도 × 시간감쇠 스칼라.")]
        [Min(0f)] public float ClearTimeBonusBase = 12f;

        [Header("의뢰 스폰")]
        [Tooltip("게임 시작 후 첫 배치가 나오기까지의 대기(초). 정규 간격과 따로 둔다 — 시작하자마자 터지지도, 한 간격을 통째로 기다리지도 않게.")]
        [Min(0f)] public float FirstSpawnDelaySeconds = 5f;
        [Min(0.1f)] public float SpawnIntervalMin = 20f;
        [Min(0.1f)] public float SpawnIntervalMax = 40f;
        [Tooltip("한 스폰 이벤트에서 나올 의뢰 수의 범위(쉬운 쪽으로 편향해 뽑는다). 같은 프레임에 한꺼번에 나오지 않고 아래 간격만큼 흩어져 나온다.")]
        public Vector2Int BurstSize = new Vector2Int(1, 3);
        [Tooltip("한 배치 안에서 의뢰가 하나씩 나오는 간격(초). 동시에 터지면 UI도 SFX도 한 번처럼 뭉친다.")]
        [Min(0.1f)] public float BurstGapSecondsMin = 1f;
        [Min(0.1f)] public float BurstGapSecondsMax = 3f;
        [Tooltip("좌상단에 동시에 쌓일 수 있는 의뢰 최대 수.")]
        [Min(1)] public int MaxActiveRequests = 8;

        [Header("난이도")]
        [Tooltip("의뢰별 랜덤 난이도 지터 범위(min, max). 예: 0.9~1.1.")]
        public Vector2 DifficultyRatioRange = new Vector2(0.9f, 1.1f);
        [Tooltip("거리 정규화 기준(m). 난이도의 거리항 = 기지→집 거리 / 이 값.")]
        [Min(1f)] public float DistanceNormalizer = 60f;
        [Tooltip("전역 난이도 스칼라의 분당 증가량. 시간이 지날수록 어려운 의뢰·큰 보상.")]
        [Min(0f)] public float GlobalDifficultyRampPerMinute = 0.15f;
        [Min(1f)] public float GlobalDifficultyMax = 2.5f;

        [Header("보상")]
        [Min(0)] public int RewardBase = 10;
        [Min(0f)] public float RewardPerDifficulty = 10f;

        [Header("만료(TTL)")]
        [Tooltip("난이도 1 기준 TTL(초). 실제 TTL = 이 값 × 난이도 × 시간감쇠 스칼라. 어려울수록 길다.")]
        [Min(1f)] public float TtlBase = 45f;
        [Tooltip("시간이 지날수록 TTL을 줄이는 스칼라의 분당 감소량.")]
        [Min(0f)] public float TtlRampPerMinute = 0.05f;
        [Range(0.1f, 1f)] public float TtlScalarMin = 0.5f;

        [Header("클리어 보너스 시간 감쇠")]
        [Tooltip("시간이 지날수록 클리어당 추가시간을 줄이는 스칼라의 분당 감소량.")]
        [Min(0f)] public float ClearBonusRampPerMinute = 0.05f;
        [Range(0.1f, 1f)] public float ClearBonusScalarMin = 0.5f;
    }
}
