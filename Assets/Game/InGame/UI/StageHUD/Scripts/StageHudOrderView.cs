using UnityEngine;

namespace PPack
{
    /// <summary>주문 도메인과 무관하게 StageHUD가 한 장의 주문표를 그리는 데 필요한 값.</summary>
    public readonly struct StageHudOrderView
    {
        public StageHudOrderView(int id, Color giftColor, float remainingSeconds,
                                 float distanceMeters, float directionDegrees, bool showNavigation)
        {
            Id = id;
            GiftColor = giftColor;
            RemainingSeconds = remainingSeconds;
            DistanceMeters = distanceMeters;
            DirectionDegrees = directionDegrees;
            ShowNavigation = showNavigation;
        }

        public int Id { get; }
        public Color GiftColor { get; }
        public float RemainingSeconds { get; }
        public float DistanceMeters { get; }
        public float DirectionDegrees { get; }
        public bool ShowNavigation { get; }

        public static StageHudOrderView FromWorldTarget(int id, Color giftColor, float remainingSeconds,
            Transform origin, Vector3 targetPosition, float fallbackDistance, bool showNavigation)
        {
            if (origin == null)
                return new StageHudOrderView(id, giftColor, remainingSeconds,
                    fallbackDistance, 0f, showNavigation);

            Vector3 delta = targetPosition - origin.position;
            delta.y = 0f;
            Vector3 forward = origin.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;

            float direction = delta.sqrMagnitude < 0.001f
                ? 0f
                : Vector3.SignedAngle(forward.normalized, delta.normalized, Vector3.up);
            return new StageHudOrderView(id, giftColor, remainingSeconds,
                delta.magnitude, direction, showNavigation);
        }
    }
}
