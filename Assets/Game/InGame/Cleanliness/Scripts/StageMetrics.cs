using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>종료 순간에 굳힌 판정 재료. 종료 뒤 바뀌는 배달·제설 상태가 결과에 섞이지 않도록
    /// 값만 복사해 둔다 — Cleanliness/AGENTS.md "종료의 정체" 참고.</summary>
    public readonly struct StageMetrics
    {
        public readonly int DeliveriesCompleted;
        public readonly int DeliveriesCancelled;
        public readonly int TotalPoints;
        public readonly float SnowClearedPercent01;

        public StageMetrics(int deliveriesCompleted, int deliveriesCancelled, int totalPoints,
            float snowClearedPercent01)
        {
            DeliveriesCompleted = deliveriesCompleted;
            DeliveriesCancelled = deliveriesCancelled;
            TotalPoints = totalPoints;
            SnowClearedPercent01 = snowClearedPercent01;
        }

        /// <summary><see cref="DeliveryDirector"/>가 아니라 그 안의 요청 목록과 총점만 받는다 —
        /// Cleanliness는 Delivery 상태를 읽기만 하고(경계 규칙), 이 형태라야 도로망 없이도
        /// EditMode에서 직접 검증할 수 있다.</summary>
        public static StageMetrics Capture(IReadOnlyList<DeliveryRequest> requests, int totalPoints,
            SnowField field, long initialTotalDepthCm)
        {
            int completed = 0;
            int cancelled = 0;

            if (requests != null)
            {
                for (int index = 0; index < requests.Count; index++)
                {
                    if (requests[index].State == EDeliveryRequestState.Completed) completed++;
                    else if (requests[index].State == EDeliveryRequestState.Cancelled) cancelled++;
                }
            }

            return Capture(completed, cancelled, totalPoints, field, initialTotalDepthCm);
        }

        public static StageMetrics Capture(int completed, int cancelled, int totalPoints,
            SnowField field, long initialTotalDepthCm)
        {
            long currentTotalDepthCm = field != null ? field.TotalDepthCm : initialTotalDepthCm;
            return Capture(completed, cancelled, totalPoints, currentTotalDepthCm, initialTotalDepthCm);
        }

        public static StageMetrics Capture(int completed, int cancelled, int totalPoints,
            long currentSnowAmount, long initialSnowAmount)
        {
            float snowClearedPercent01 = initialSnowAmount > 0
                ? Mathf.Clamp01(1f - (float)currentSnowAmount / initialSnowAmount)
                : 0f;

            return new StageMetrics(completed, cancelled, totalPoints, snowClearedPercent01);
        }

    }
}
