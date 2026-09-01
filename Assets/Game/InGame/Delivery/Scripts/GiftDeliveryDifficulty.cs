using System;
using UnityEngine;

namespace PPack
{
    [Serializable]
    public struct GiftDeliveryDifficultySettings
    {
        public float StartRouteLengthM;
        public float RouteLengthPerOrderM;
        public float MaxRouteLengthM;

        public float StartTimeSlackMultiplier;
        public float TimeSlackDecayPerOrder;
        public float MinTimeSlackMultiplier;

        public float AssumedSpeedMps;
        public float MinTimeLimitSeconds;

        public int StartGiftCount;
        public int OrdersPerGiftCountStep;
        public int MaxGiftCount;

        public int StartRequiredValue;
        public int RequiredValuePerOrder;
        public int MaxRequiredValue;

        public static GiftDeliveryDifficultySettings Default => new GiftDeliveryDifficultySettings
        {
            StartRouteLengthM = 40f,
            RouteLengthPerOrderM = 8f,
            MaxRouteLengthM = 180f,

            StartTimeSlackMultiplier = 2.5f,
            TimeSlackDecayPerOrder = 0.06f,
            MinTimeSlackMultiplier = 1.25f,

            AssumedSpeedMps = 4f,
            MinTimeLimitSeconds = 20f,

            StartGiftCount = 1,
            OrdersPerGiftCountStep = 4,
            MaxGiftCount = 3,

            StartRequiredValue = 0,
            RequiredValuePerOrder = 5,
            MaxRequiredValue = 60
        };
    }

    public readonly struct GiftDeliveryTarget
    {
        public GiftDeliveryTarget(float targetRouteLengthM, float timeLimitSeconds,
                                  int requiredGiftCount, int requiredTotalValue)
        {
            TargetRouteLengthM = targetRouteLengthM;
            TimeLimitSeconds = timeLimitSeconds;
            RequiredGiftCount = requiredGiftCount;
            RequiredTotalValue = requiredTotalValue;
        }

        public float TargetRouteLengthM { get; }
        public float TimeLimitSeconds { get; }
        public int RequiredGiftCount { get; }
        public int RequiredTotalValue { get; }
    }

    public static class GiftDeliveryDifficulty
    {
        public static GiftDeliveryTarget Evaluate(int completedCount, in GiftDeliveryDifficultySettings s)
        {
            float targetLength = Mathf.Min(
                s.StartRouteLengthM + completedCount * s.RouteLengthPerOrderM,
                s.MaxRouteLengthM);

            float slack = Mathf.Max(
                s.StartTimeSlackMultiplier - completedCount * s.TimeSlackDecayPerOrder,
                s.MinTimeSlackMultiplier);

            float assumedSpeed = Mathf.Max(s.AssumedSpeedMps, 0.01f);
            float timeLimit = Mathf.Max(targetLength / assumedSpeed * slack, s.MinTimeLimitSeconds);

            int giftCount = Mathf.Clamp(
                s.StartGiftCount + completedCount / Mathf.Max(s.OrdersPerGiftCountStep, 1),
                s.StartGiftCount, s.MaxGiftCount);

            int requiredValue = Mathf.Clamp(
                s.StartRequiredValue + completedCount * s.RequiredValuePerOrder,
                s.StartRequiredValue, s.MaxRequiredValue);

            return new GiftDeliveryTarget(targetLength, timeLimit, giftCount, requiredValue);
        }
    }
}
