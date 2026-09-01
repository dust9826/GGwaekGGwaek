using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public readonly struct TrafficNodeSpec
    {
        public TrafficNodeSpec(string id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        public string Id { get; }
        public Vector3 Position { get; }
    }

    public readonly struct TrafficRoadSpec
    {
        public TrafficRoadSpec(string id, string startId, string endId,
            params Vector3[] controls)
        {
            Id = id;
            StartId = startId;
            EndId = endId;
            Controls = controls ?? Array.Empty<Vector3>();
        }

        public string Id { get; }
        public string StartId { get; }
        public string EndId { get; }
        public Vector3[] Controls { get; }
    }

    public readonly struct TrafficLanePose
    {
        public TrafficLanePose(Vector3 position, Vector3 forward)
        {
            Position = position;
            Forward = forward;
        }

        public Vector3 Position { get; }
        public Vector3 Forward { get; }
    }

    public sealed class TrafficNode
    {
        private readonly List<TrafficLane> _outgoing = new();

        internal TrafficNode(string id, Vector3 position)
        {
            Id = id;
            Position = position;
        }

        public string Id { get; }
        public Vector3 Position { get; }
        public IReadOnlyList<TrafficLane> Outgoing => _outgoing;
        public bool IsPortal => _outgoing.Count == 1;
        public bool IsJunction => _outgoing.Count >= 3;

        internal void AddOutgoing(TrafficLane lane) => _outgoing.Add(lane);
    }

    public sealed class TrafficLane
    {
        private readonly Vector3[] _points;
        private readonly float[] _distances;

        internal TrafficLane(string id, TrafficNode from, TrafficNode to,
            IReadOnlyList<Vector3> points, float speedLimitMps)
        {
            Id = id;
            From = from;
            To = to;
            SpeedLimitMps = speedLimitMps;
            _points = new Vector3[points.Count];
            _distances = new float[points.Count];
            for (int index = 0; index < points.Count; index++)
            {
                _points[index] = points[index];
                if (index > 0)
                    _distances[index] = _distances[index - 1]
                                        + Vector3.Distance(_points[index - 1], _points[index]);
            }
        }

        public string Id { get; }
        public TrafficNode From { get; }
        public TrafficNode To { get; }
        public float SpeedLimitMps { get; }
        public float Length => _distances[^1];
        public IReadOnlyList<Vector3> Points => _points;

        public TrafficLanePose Evaluate(float distance)
        {
            float clamped = Mathf.Clamp(distance, 0f, Length);
            int upper = FindUpperPoint(clamped);
            int lower = Mathf.Max(0, upper - 1);
            float span = _distances[upper] - _distances[lower];
            float t = span <= 0.0001f ? 0f : (clamped - _distances[lower]) / span;
            Vector3 position = Vector3.Lerp(_points[lower], _points[upper], t);
            Vector3 forward = (_points[upper] - _points[lower]).normalized;
            return new TrafficLanePose(position, forward);
        }

        private int FindUpperPoint(float distance)
        {
            if (distance >= Length) return _points.Length - 1;
            int low = 1;
            int high = _points.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (_distances[middle] < distance) low = middle + 1;
                else high = middle;
            }
            return low;
        }
    }

    public sealed class TrafficRoute
    {
        private readonly TrafficLane[] _lanes;

        internal TrafficRoute(IReadOnlyList<TrafficLane> lanes)
        {
            _lanes = new TrafficLane[lanes.Count];
            for (int index = 0; index < lanes.Count; index++) _lanes[index] = lanes[index];
        }

        public IReadOnlyList<TrafficLane> Lanes => _lanes;
        public TrafficNode Origin => _lanes[0].From;
        public TrafficNode Destination => _lanes[^1].To;
    }

    public sealed class TrafficLaneNetwork
    {
        private const float DefaultLaneOffsetM = 1.1f;
        private const float DefaultSpeedLimitMps = 7f;
        private const float SampleSpacingM = 0.5f;

        private readonly List<TrafficNode> _nodes = new();
        private readonly List<TrafficLane> _lanes = new();
        private readonly List<TrafficNode> _portals = new();
        private readonly Dictionary<string, TrafficNode> _nodesById =
            new(StringComparer.Ordinal);

        public TrafficLaneNetwork(IReadOnlyList<TrafficNodeSpec> nodes,
            IReadOnlyList<TrafficRoadSpec> roads, float laneOffsetM = DefaultLaneOffsetM,
            float speedLimitMps = DefaultSpeedLimitMps)
        {
            if (nodes == null || roads == null) throw new ArgumentNullException();
            foreach (TrafficNodeSpec spec in nodes)
            {
                var node = new TrafficNode(spec.Id, spec.Position);
                _nodes.Add(node);
                _nodesById.Add(spec.Id, node);
            }

            foreach (TrafficRoadSpec road in roads)
            {
                TrafficNode start = _nodesById[road.StartId];
                TrafficNode end = _nodesById[road.EndId];
                List<Vector3> center = SampleCenterline(start.Position, end.Position, road.Controls);
                AddLane(road.Id + ":Forward", start, end,
                    Offset(center, laneOffsetM), speedLimitMps);
                center.Reverse();
                AddLane(road.Id + ":Reverse", end, start,
                    Offset(center, laneOffsetM), speedLimitMps);
            }

            foreach (TrafficNode node in _nodes)
                if (node.IsPortal) _portals.Add(node);
        }

        public IReadOnlyList<TrafficNode> Nodes => _nodes;
        public IReadOnlyList<TrafficLane> Lanes => _lanes;
        public IReadOnlyList<TrafficNode> Portals => _portals;

        public TrafficNode FindNode(string id)
            => id != null && _nodesById.TryGetValue(id, out TrafficNode node) ? node : null;

        public static TrafficLaneNetwork CreateWinterVillage()
        {
            var nodes = new[]
            {
                new TrafficNodeSpec("Central", new Vector3(-4f, 0f, -5f)),
                new TrafficNodeSpec("South", new Vector3(22f, 0f, -22f)),
                new TrafficNodeSpec("East", new Vector3(35f, 0f, 14f)),
                new TrafficNodeSpec("NorthWest", new Vector3(-36f, 0f, 29f)),
                new TrafficNodeSpec("NorthEast", new Vector3(35f, 0f, 36f)),
                new TrafficNodeSpec("DiagNW", new Vector3(-50f, 0f, 42f)),
                new TrafficNodeSpec("DiagSE", new Vector3(50f, 0f, -45f)),
                new TrafficNodeSpec("CrossSW", new Vector3(-52f, 0f, -43f)),
                new TrafficNodeSpec("SouthGate", new Vector3(7f, 0f, -48f)),
                new TrafficNodeSpec("NorthGate", new Vector3(35f, 0f, 50f)),
                new TrafficNodeSpec("H06", new Vector3(10.94f, 0f, 2.28f)),
                new TrafficNodeSpec("H07", new Vector3(25.03f, 0f, -13.6f)),
                new TrafficNodeSpec("H08", new Vector3(28.78f, 0f, -3.23f)),
                new TrafficNodeSpec("H09", new Vector3(38.64f, 0f, -35.66f)),
                new TrafficNodeSpec("H10", new Vector3(-32.5f, 0f, -33.5f)),
                new TrafficNodeSpec("H11", new Vector3(-29.67f, 0f, -30.67f))
            };

            var roads = new[]
            {
                new TrafficRoadSpec("DiagonalNW", "DiagNW", "NorthWest"),
                new TrafficRoadSpec("DiagonalNorthWest", "NorthWest", "Central",
                    new Vector3(-24f, 0f, 8f)),
                new TrafficRoadSpec("DiagonalCentral", "Central", "South"),
                new TrafficRoadSpec("DiagonalSouth", "South", "H09"),
                new TrafficRoadSpec("DiagonalSE", "H09", "DiagSE"),
                new TrafficRoadSpec("CrossWest", "CrossSW", "H10",
                    new Vector3(-39f, 0f, -40f)),
                new TrafficRoadSpec("CrossH10", "H10", "H11"),
                new TrafficRoadSpec("CrossCentralWest", "H11", "Central"),
                new TrafficRoadSpec("CrossCentralEast", "Central", "H06"),
                new TrafficRoadSpec("CrossEast", "H06", "East"),
                new TrafficRoadSpec("SouthGateRoad", "SouthGate", "South"),
                new TrafficRoadSpec("SouthH07", "South", "H07"),
                new TrafficRoadSpec("H07H08", "H07", "H08"),
                new TrafficRoadSpec("H08East", "H08", "East"),
                new TrafficRoadSpec("EastNorthEast", "East", "NorthEast"),
                new TrafficRoadSpec("NorthGateRoad", "NorthEast", "NorthGate")
            };
            return new TrafficLaneNetwork(nodes, roads);
        }

        private void AddLane(string id, TrafficNode from, TrafficNode to,
            IReadOnlyList<Vector3> points, float speedLimitMps)
        {
            var lane = new TrafficLane(id, from, to, points, speedLimitMps);
            _lanes.Add(lane);
            from.AddOutgoing(lane);
        }

        private static List<Vector3> SampleCenterline(Vector3 start, Vector3 end,
            IReadOnlyList<Vector3> controls)
        {
            float estimate = Vector3.Distance(start, end);
            Vector3 previous = start;
            for (int index = 0; index < controls.Count; index++)
            {
                estimate += Vector3.Distance(previous, controls[index]);
                previous = controls[index];
            }
            estimate += controls.Count > 0 ? Vector3.Distance(previous, end) : 0f;
            int steps = Mathf.Max(2, Mathf.CeilToInt(estimate / SampleSpacingM));
            var points = new List<Vector3>(steps + 1);
            for (int index = 0; index <= steps; index++)
            {
                float t = index / (float)steps;
                points.Add(controls.Count switch
                {
                    0 => Vector3.Lerp(start, end, t),
                    1 => Quadratic(start, controls[0], end, t),
                    _ => Cubic(start, controls[0], controls[1], end, t)
                });
            }
            return points;
        }

        private static List<Vector3> Offset(IReadOnlyList<Vector3> center, float offset)
        {
            var points = new List<Vector3>(center.Count);
            for (int index = 0; index < center.Count; index++)
            {
                Vector3 before = center[Mathf.Max(0, index - 1)];
                Vector3 after = center[Mathf.Min(center.Count - 1, index + 1)];
                Vector3 forward = (after - before).normalized;
                Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;
                points.Add(center[index] + right * offset);
            }
            return points;
        }

        private static Vector3 Quadratic(Vector3 a, Vector3 b, Vector3 c, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * b + t * t * c;
        }

        private static Vector3 Cubic(Vector3 a, Vector3 b, Vector3 c, Vector3 d, float t)
        {
            float u = 1f - t;
            return u * u * u * a + 3f * u * u * t * b
                   + 3f * u * t * t * c + t * t * t * d;
        }
    }
}
