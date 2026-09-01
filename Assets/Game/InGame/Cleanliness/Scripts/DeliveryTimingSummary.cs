using System;
using System.Collections.Generic;

namespace PPack
{
    /// <summary>왕복 계측 샘플을 짧은 밸런스용 통계로 접는다. 로그 전체를 다시 읽지 않아도 된다.</summary>
    public sealed class DeliveryTimingSummary
    {
        public readonly struct Sample
        {
            public Sample(int requestId, int houseIndex, float distanceM, float difficulty, float ttlSeconds,
                          double centerToHouseSeconds, double houseToCenterSeconds,
                          double pathM, double movingSeconds)
            {
                RequestId = requestId;
                HouseIndex = houseIndex;
                DistanceM = distanceM;
                Difficulty = difficulty;
                TtlSeconds = ttlSeconds;
                CenterToHouseSeconds = centerToHouseSeconds;
                HouseToCenterSeconds = houseToCenterSeconds;
                PathM = pathM;
                MovingSeconds = movingSeconds;
            }

            public int RequestId { get; }
            public int HouseIndex { get; }
            public float DistanceM { get; }
            public float Difficulty { get; }
            public float TtlSeconds { get; }
            public double CenterToHouseSeconds { get; }
            public double HouseToCenterSeconds { get; }
            /// <summary>실제로 걸어간 거리. 직선거리와 달리 강을 돌아간 우회가 들어 있다.</summary>
            public double PathM { get; }

            /// <summary>실제로 움직인 시간. 서 있던 시간은 빠져 있다.</summary>
            public double MovingSeconds { get; }

            public double RoundTripSeconds => CenterToHouseSeconds + HouseToCenterSeconds;
            public double TtlMarginSeconds => TtlSeconds - CenterToHouseSeconds;

            /// <summary>정지 시간을 뺀 실제 이동 속도. 밸런스가 읽어야 하는 값은 이쪽이다.</summary>
            public double SpeedMps => MovingSeconds > 0.001d ? PathM / MovingSeconds : 0d;

            /// <summary>실제 경로 ÷ 직선 왕복. 강 건너 집이 여기서 튀어 오른다.</summary>
            public double DetourRatio => DistanceM > 0.01f ? PathM / (DistanceM * 2d) : 0d;

            public double IdleSeconds => RoundTripSeconds - MovingSeconds;
        }

        private readonly List<Sample> _samples = new List<Sample>();

        public int Count => _samples.Count;

        public IReadOnlyList<Sample> Samples => _samples;

        public void Add(int requestId, int houseIndex, float distanceM, float difficulty, float ttlSeconds,
                        double centerToHouseSeconds, double houseToCenterSeconds,
                        double pathM, double movingSeconds)
        {
            _samples.Add(new Sample(requestId, houseIndex, distanceM, difficulty, ttlSeconds,
                                    centerToHouseSeconds, houseToCenterSeconds, pathM, movingSeconds));
        }

        public string ToLogLine()
        {
            if (_samples.Count == 0) return "[DeliveryTimingSummary] samples=0";

            TimingStats outbound = Measure(sample => sample.CenterToHouseSeconds);
            TimingStats inbound = Measure(sample => sample.HouseToCenterSeconds);
            TimingStats roundTrip = Measure(sample => sample.RoundTripSeconds);
            TimingStats margin = Measure(sample => sample.TtlMarginSeconds);
            TimingStats distance = Measure(sample => sample.DistanceM);
            TimingStats speed = Measure(sample => sample.SpeedMps);
            TimingStats detour = Measure(sample => sample.DetourRatio);
            TimingStats idle = Measure(sample => sample.IdleSeconds);

            return $"[DeliveryTimingSummary] samples={_samples.Count} " +
                   $"distance={distance.Average:0.0}m " +
                   $"centerToHouse={outbound.Average:0.00}/{outbound.Median:0.00}/{outbound.Max:0.00}s " +
                   $"houseToCenter={inbound.Average:0.00}/{inbound.Median:0.00}/{inbound.Max:0.00}s " +
                   $"roundTrip={roundTrip.Average:0.00}/{roundTrip.Median:0.00}/{roundTrip.Max:0.00}s " +
                   $"ttlMargin={margin.Average:0.00}/{margin.Min:0.00}s " +
                   $"speed={speed.Average:0.00}/{speed.Median:0.00}/{speed.Min:0.00}m/s " +
                   $"detour={detour.Average:0.00}/{detour.Max:0.00}x " +
                   $"idle={idle.Average:0.00}/{idle.Max:0.00}s";
        }

        public string ToCompactLabel()
        {
            return _samples.Count == 0 ? "samples=0" : $"samples={_samples.Count} · {Measure(sample => sample.RoundTripSeconds).Average:0.0}s avg";
        }

        private TimingStats Measure(Func<Sample, double> selector)
        {
            double min = double.PositiveInfinity;
            double max = double.NegativeInfinity;
            double total = 0d;
            var values = new List<double>(_samples.Count);

            for (int index = 0; index < _samples.Count; index++)
            {
                double value = selector(_samples[index]);
                values.Add(value);
                total += value;
                if (value < min) min = value;
                if (value > max) max = value;
            }

            values.Sort();
            int middle = values.Count / 2;
            double median = values.Count % 2 == 0
                ? (values[middle - 1] + values[middle]) * 0.5d
                : values[middle];
            return new TimingStats(total / values.Count, median, min, max);
        }

        private readonly struct TimingStats
        {
            public TimingStats(double average, double median, double min, double max)
            {
                Average = average;
                Median = median;
                Min = min;
                Max = max;
            }

            public double Average { get; }
            public double Median { get; }
            public double Min { get; }
            public double Max { get; }
        }
    }
}
