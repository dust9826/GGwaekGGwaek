using System;
using System.Collections.Generic;

namespace PPack
{
    public static class DeliveryRoutePlanner
    {
        private readonly struct PreviousStep
        {
            public PreviousStep(DeliveryRoadNode node, DeliveryRoadSegment segment)
            {
                Node = node;
                Segment = segment;
            }

            public DeliveryRoadNode Node { get; }
            public DeliveryRoadSegment Segment { get; }
        }

        public static bool TryPlan(DeliveryRoadNetwork network,
                                   DeliveryRoadNode start,
                                   DeliveryRoadNode end,
                                   out DeliveryRoute route)
        {
            route = null;
            if (network == null || start == null || end == null || start == end) return false;

            var distances = new Dictionary<DeliveryRoadNode, float>();
            var previous = new Dictionary<DeliveryRoadNode, PreviousStep>();
            var unvisited = new HashSet<DeliveryRoadNode>();
            foreach (DeliveryRoadNode node in network.Nodes)
            {
                if (node == null) continue;
                distances[node] = node == start ? 0f : float.PositiveInfinity;
                unvisited.Add(node);
            }
            if (!distances.ContainsKey(start) || !distances.ContainsKey(end)) return false;

            while (unvisited.Count > 0)
            {
                DeliveryRoadNode current = null;
                float currentDistance = float.PositiveInfinity;
                foreach (DeliveryRoadNode candidate in unvisited)
                {
                    float distance = distances[candidate];
                    if (distance >= currentDistance) continue;
                    current = candidate;
                    currentDistance = distance;
                }

                if (current == null || float.IsPositiveInfinity(currentDistance)) break;
                unvisited.Remove(current);
                if (current == end) break;

                foreach (DeliveryRoadSegment segment in network.Segments)
                {
                    if (segment == null) continue;
                    DeliveryRoadNode neighbor = segment.Other(current);
                    if (neighbor == null || !unvisited.Contains(neighbor)) continue;

                    float candidateDistance = currentDistance + segment.Length;
                    if (candidateDistance >= distances[neighbor]) continue;
                    distances[neighbor] = candidateDistance;
                    previous[neighbor] = new PreviousStep(current, segment);
                }
            }

            if (!previous.ContainsKey(end)) return false;

            var reversed = new List<DeliveryRoadTraversal>();
            DeliveryRoadNode cursor = end;
            while (cursor != start)
            {
                PreviousStep step = previous[cursor];
                bool reverse = step.Segment.End == step.Node && step.Segment.Start == cursor;
                reversed.Add(new DeliveryRoadTraversal(step.Segment, reverse));
                cursor = step.Node;
            }
            reversed.Reverse();
            route = new DeliveryRoute(reversed);
            return true;
        }

        public static bool TryPlanStops(DeliveryRoadNetwork network,
                                        IReadOnlyList<DeliveryFactory> stops,
                                        out DeliveryRoute route)
        {
            route = null;
            if (stops == null || stops.Count < 2) return false;

            var traversals = new List<DeliveryRoadTraversal>();
            for (int index = 1; index < stops.Count; index++)
            {
                DeliveryFactory previous = stops[index - 1];
                DeliveryFactory next = stops[index];
                if (previous == null || next == null || previous.RoadNode == next.RoadNode) return false;
                if (!TryPlan(network, previous.RoadNode, next.RoadNode, out DeliveryRoute leg)) return false;
                traversals.AddRange(leg.Traversals);
            }

            route = new DeliveryRoute(traversals);
            return true;
        }
    }
}

