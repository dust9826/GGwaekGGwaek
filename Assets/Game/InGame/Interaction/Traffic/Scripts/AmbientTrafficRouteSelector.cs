using System;
using System.Collections.Generic;

namespace PPack
{
    public static class AmbientTrafficRouteSelector
    {
        private readonly struct Previous
        {
            public Previous(TrafficNode node, TrafficLane lane)
            {
                Node = node;
                Lane = lane;
            }

            public TrafficNode Node { get; }
            public TrafficLane Lane { get; }
        }

        public static bool TryPlan(TrafficLaneNetwork network, TrafficNode origin,
            TrafficNode destination, out TrafficRoute route)
        {
            route = null;
            if (network == null || origin == null || destination == null || origin == destination)
                return false;

            var distances = new Dictionary<TrafficNode, float>();
            var previous = new Dictionary<TrafficNode, Previous>();
            var unvisited = new HashSet<TrafficNode>();
            foreach (TrafficNode node in network.Nodes)
            {
                distances[node] = node == origin ? 0f : float.PositiveInfinity;
                unvisited.Add(node);
            }

            while (unvisited.Count > 0)
            {
                TrafficNode current = null;
                float best = float.PositiveInfinity;
                foreach (TrafficNode candidate in unvisited)
                {
                    float distance = distances[candidate];
                    if (distance >= best) continue;
                    current = candidate;
                    best = distance;
                }

                if (current == null || float.IsPositiveInfinity(best)) break;
                unvisited.Remove(current);
                if (current == destination) break;

                foreach (TrafficLane lane in current.Outgoing)
                {
                    if (!unvisited.Contains(lane.To)) continue;
                    float candidate = best + lane.Length;
                    if (candidate >= distances[lane.To]) continue;
                    distances[lane.To] = candidate;
                    previous[lane.To] = new Previous(current, lane);
                }
            }

            if (!previous.ContainsKey(destination)) return false;
            var reversed = new List<TrafficLane>();
            TrafficNode cursor = destination;
            while (cursor != origin)
            {
                Previous step = previous[cursor];
                reversed.Add(step.Lane);
                cursor = step.Node;
            }
            reversed.Reverse();
            route = new TrafficRoute(reversed);
            return true;
        }

        public static bool TryChoosePortalRoute(TrafficLaneNetwork network, TrafficNode origin,
            Random random, out TrafficRoute route)
        {
            route = null;
            if (network == null || origin == null || random == null) return false;

            var candidates = new List<TrafficNode>();
            foreach (TrafficNode portal in network.Portals)
                if (portal != origin) candidates.Add(portal);
            Shuffle(candidates, random);
            foreach (TrafficNode destination in candidates)
                if (TryPlan(network, origin, destination, out route)) return true;
            return false;
        }

        private static void Shuffle<T>(IList<T> values, Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (values[index], values[other]) = (values[other], values[index]);
            }
        }
    }
}
