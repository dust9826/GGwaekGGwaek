using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 트럭의 종축 속도 계산. 순수 함수라 씬·시간·난수에 의존하지 않는다 — 헤드리스 서버에서
    /// 그대로 돌고 EditMode 로 검증한다.
    ///
    /// <b>순항 속도는 이 클래스가 정하지 않는다.</b> 여기서 나오는 것은 전부 "상한"이고, 실제
    /// 목표는 호출부가 <c>Mathf.Min(순항속도, 상한)</c> 으로 정한다. 그래서 직선에서는 언제나
    /// 정확히 <c>DeliveryTruck._speedMetersPerSecond</c> 로 복귀한다.
    /// </summary>
    public static class DeliveryTruckMotion
    {
        private const float StraightCurvature = 1e-4f;

        /// <summary>곡률에서 나오는 속도 상한. 횡가속도 상한을 넘지 않는 최대 속도다.</summary>
        public static float CornerSpeedLimit(float curvature, float maxLateralAccel)
        {
            if (curvature <= StraightCurvature || maxLateralAccel <= 0f) return float.PositiveInfinity;
            return Mathf.Sqrt(maxLateralAccel / curvature);
        }

        /// <summary>
        /// 노드에서 도로가 꺾이는 각도를 곡률로 환산한 속도 상한. 곡선 평가가 도로 경계를 넘지
        /// 않으므로(<see cref="DeliveryRoadCurve"/>는 도로 하나만 안다) 노드의 꺾임은 곡률로
        /// 안 잡힌다 — <paramref name="blendDistance"/> 만큼의 거리에 그 각도를 펴서 본다.
        /// </summary>
        public static float TurnSpeedLimit(float turnDegrees, float blendDistance, float maxLateralAccel)
        {
            if (turnDegrees <= 0.5f || blendDistance <= 0f) return float.PositiveInfinity;
            return CornerSpeedLimit(turnDegrees * Mathf.Deg2Rad / blendDistance, maxLateralAccel);
        }

        /// <summary>
        /// <paramref name="distance"/> 앞에서 <paramref name="limitAtPoint"/> 로 떨어뜨리려면
        /// 지금 낼 수 있는 최대 속도. 이것이 "코너 앞에서 미리 감속" 의 전부다.
        /// </summary>
        public static float ApproachSpeedLimit(float limitAtPoint, float distance, float brakeAccel)
        {
            if (float.IsPositiveInfinity(limitAtPoint)) return float.PositiveInfinity;
            float clampedDistance = Mathf.Max(0f, distance);
            return Mathf.Sqrt(limitAtPoint * limitAtPoint + 2f * brakeAccel * clampedDistance);
        }

        /// <summary>가속과 제동은 비대칭이다 — 트럭은 느리게 붙고 빠르게 선다.</summary>
        public static float StepSpeed(float currentSpeed, float targetSpeed,
                                      float accel, float brake, float deltaSeconds)
        {
            if (targetSpeed > currentSpeed) return Mathf.Min(targetSpeed, currentSpeed + accel * deltaSeconds);
            return Mathf.Max(targetSpeed, currentSpeed - brake * deltaSeconds);
        }

        public static float BrakingDistance(float speed, float brake)
            => brake <= 0f ? float.PositiveInfinity : speed * speed / (2f * brake);

        /// <summary>
        /// 방향 오차(도)를 다 풀 때까지 <paramref name="toleranceMeters"/> 만큼만 미끄러지도록
        /// 역산한 속도 상한. 오차가 클수록, 허용 거리가 짧을수록 더 세게 누른다.
        ///
        /// 요 각속도 제한(<paramref name="maxYawRateDegPerSecond"/>)은 방향을 얼마나 빨리
        /// 따라잡을 수 있는지의 상한이지, 그동안 트럭이 얼마나 미끄러져도 되는지는 말해주지
        /// 않는다 — 그 여백을 이 함수가 정한다. <c>toleranceMeters = HalfLength</c>(2m) 같은
        /// 큰 값을 쓰면 30° 안팎의 흔한 꺾임에서는 상한이 순항 속도보다 커져 아무 효과가 없다
        /// (2×120/30=8 > 5). 그래서 트럭 본체 크기가 아니라 훨씬 짧은 값(기본 0.5m)을 쓴다.
        /// </summary>
        public static float HeadingCatchUpSpeedLimit(float errorDeg, float toleranceMeters, float maxYawRateDegPerSecond)
        {
            if (errorDeg <= 0f) return float.PositiveInfinity;
            return toleranceMeters * maxYawRateDegPerSecond / errorDeg;
        }
    }
}
