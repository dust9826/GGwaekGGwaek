using UnityEngine;

namespace PPack
{
    public enum EGiftDeliveryOrderState
    {
        Active,
        Completed,
        Failed
    }

    public enum EGiftDeliveryFailReason
    {
        None,
        TimeExpired,
        WrongHouse
    }

    public sealed class GiftDeliveryOrder
    {
        private static readonly Color[] QuestPalette =
        {
            new Color(1f, 0.05f, 0.85f),
            new Color(0.1f, 0.9f, 0.95f),
            new Color(0.95f, 0.9f, 0.15f),
            new Color(0.20f, 0.82f, 0.34f),
            new Color(1f, 0.48f, 0.10f),
            new Color(0.62f, 0.32f, 0.95f),
        };

        private static readonly EGiftBoxKind[] GiftKinds =
        {
            EGiftBoxKind.Red,
            EGiftBoxKind.Yellow,
            EGiftBoxKind.Green,
            EGiftBoxKind.Blue
        };

        public GiftDeliveryOrder(int id, int houseIndex, float routeLength,
                                 int requiredGiftCount, int requiredTotalValue, float timeLimitSeconds)
            : this(id, houseIndex, routeLength, requiredGiftCount, requiredTotalValue, timeLimitSeconds,
                QuestPalette[(uint)id % QuestPalette.Length], GiftKinds[(uint)id % GiftKinds.Length])
        {
        }

        public GiftDeliveryOrder(int id, int houseIndex, float routeLength,
                                 int requiredGiftCount, int requiredTotalValue, float timeLimitSeconds,
                                 Color questColor)
            : this(id, houseIndex, routeLength, requiredGiftCount, requiredTotalValue, timeLimitSeconds,
                questColor, GiftKinds[(uint)id % GiftKinds.Length])
        {
        }

        public GiftDeliveryOrder(int id, int houseIndex, float routeLength,
                                 int requiredGiftCount, int requiredTotalValue, float timeLimitSeconds,
                                 Color questColor, EGiftBoxKind giftKind)
        {
            Id = id;
            HouseIndex = houseIndex;
            RouteLength = routeLength;
            RequiredGiftCount = requiredGiftCount;
            RequiredTotalValue = requiredTotalValue;
            TimeLimitSeconds = timeLimitSeconds;
            RemainingSeconds = timeLimitSeconds;
            QuestColor = questColor;
            QuestColor = new Color(QuestColor.r, QuestColor.g, QuestColor.b, 1f);
            GiftKind = giftKind;
            State = EGiftDeliveryOrderState.Active;
            FailReason = EGiftDeliveryFailReason.None;
        }

        public int Id { get; }
        public int HouseIndex { get; }
        public float RouteLength { get; }
        public int RequiredGiftCount { get; }
        public int RequiredTotalValue { get; }
        public float TimeLimitSeconds { get; }
        public float RemainingSeconds { get; private set; }
        public Color QuestColor { get; }
        public EGiftBoxKind GiftKind { get; }
        public EGiftDeliveryOrderState State { get; private set; }
        public EGiftDeliveryFailReason FailReason { get; private set; }

        public void Tick(float delta)
        {
            if (State != EGiftDeliveryOrderState.Active) return;
            RemainingSeconds -= delta;
        }

        public bool TryComplete(int count, int totalValue)
        {
            if (State != EGiftDeliveryOrderState.Active) return false;
            if (count < RequiredGiftCount || totalValue < RequiredTotalValue) return false;
            State = EGiftDeliveryOrderState.Completed;
            return true;
        }

        public void Fail(EGiftDeliveryFailReason reason)
        {
            if (State != EGiftDeliveryOrderState.Active) return;
            State = EGiftDeliveryOrderState.Failed;
            FailReason = reason;
        }
    }
}
