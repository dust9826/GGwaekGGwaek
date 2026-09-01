using System;
using System.Collections.Generic;

namespace PPack
{
    public enum EDeliveryRequestState
    {
        Active,
        Completed,
        Cancelled
    }

    public enum EDeliveryTruckState
    {
        Driving,
        SnowBlocked,
        YieldReversing,
        YieldWaiting,
        ResumeRoute,
        Completed,
        Cancelled
    }

    public sealed class DeliveryRequest
    {
        private readonly DeliveryFactory[] _stops;

        public DeliveryRequest(int id, IReadOnlyList<DeliveryFactory> stops,
                               DeliveryRoute route, float pointsPerMeter)
        {
            if (id < 0) throw new ArgumentOutOfRangeException(nameof(id));
            if (stops == null || stops.Count < 2) throw new ArgumentException("의뢰는 공장을 두 곳 이상 방문해야 한다", nameof(stops));
            Route = route ?? throw new ArgumentNullException(nameof(route));
            if (pointsPerMeter < 0f) throw new ArgumentOutOfRangeException(nameof(pointsPerMeter));

            Id = id;
            _stops = new DeliveryFactory[stops.Count];
            for (int index = 0; index < stops.Count; index++)
            {
                if (stops[index] == null) throw new ArgumentException("비어 있는 방문 공장이 있다", nameof(stops));
                _stops[index] = stops[index];
            }

            PlannedLength = route.Length;
            SuccessPoints = (int)MathF.Round(PlannedLength * pointsPerMeter, MidpointRounding.AwayFromZero);
            State = EDeliveryRequestState.Active;
        }

        public int Id { get; }
        public IReadOnlyList<DeliveryFactory> Stops => _stops;
        public DeliveryRoute Route { get; }
        public float PlannedLength { get; }
        public int SuccessPoints { get; }
        public float ContinuousSnowBlockedSeconds { get; private set; }
        public EDeliveryRequestState State { get; private set; }

        public bool TickSnowBlocked(float deltaSeconds, float cancelAfterSeconds)
        {
            if (State != EDeliveryRequestState.Active) return false;
            if (deltaSeconds < 0f) throw new ArgumentOutOfRangeException(nameof(deltaSeconds));
            if (cancelAfterSeconds <= 0f) throw new ArgumentOutOfRangeException(nameof(cancelAfterSeconds));

            ContinuousSnowBlockedSeconds += deltaSeconds;
            if (ContinuousSnowBlockedSeconds < cancelAfterSeconds) return false;
            State = EDeliveryRequestState.Cancelled;
            return true;
        }

        public void ClearSnowBlocked()
        {
            if (State == EDeliveryRequestState.Active) ContinuousSnowBlockedSeconds = 0f;
        }

        public bool Complete()
        {
            if (State != EDeliveryRequestState.Active) return false;
            State = EDeliveryRequestState.Completed;
            ContinuousSnowBlockedSeconds = 0f;
            return true;
        }
    }
}

