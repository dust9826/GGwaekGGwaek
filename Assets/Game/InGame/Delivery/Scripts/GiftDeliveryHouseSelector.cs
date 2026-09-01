using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public static class GiftDeliveryHouseSelector
    {
        public static bool TrySelectRandom(DeliveryRoadNetwork network,
                                           IReadOnlyList<DeliveryHouse> houses,
                                           IReadOnlyList<DeliveryRoadNode> startNodes,
                                           IReadOnlyList<int> excludedHouseIndices,
                                           System.Random random,
                                           out int houseIndex, out float routeLength, out DeliveryRoute route)
        {
            houseIndex = -1;
            routeLength = 0f;
            route = null;
            if (random == null) throw new ArgumentNullException(nameof(random));

            var candidateIndices = new List<int>();
            var candidateLengths = new List<float>();
            var candidateRoutes = new List<DeliveryRoute>();

            for (int index = 0; index < houses.Count; index++)
            {
                if (excludedHouseIndices != null && Contains(excludedHouseIndices, index)) continue;

                DeliveryHouse house = houses[index];
                if (house == null || house.RoadNode == null) continue;
                if (!TryShortestFromParticipants(network, startNodes, house.RoadNode,
                                                 out float length, out DeliveryRoute candidateRoute))
                    continue;

                candidateIndices.Add(index);
                candidateLengths.Add(length);
                candidateRoutes.Add(candidateRoute);
            }

            if (candidateIndices.Count == 0) return false;

            int selected = random.Next(candidateIndices.Count);
            houseIndex = candidateIndices[selected];
            routeLength = candidateLengths[selected];
            route = candidateRoutes[selected];
            return true;
        }

        public static bool TrySelect(DeliveryRoadNetwork network,
                                     IReadOnlyList<DeliveryHouse> houses,
                                     IReadOnlyList<DeliveryRoadNode> startNodes,
                                     float targetLengthM,
                                     IReadOnlyList<int> excludedHouseIndices,
                                     out int houseIndex, out float routeLength, out DeliveryRoute route)
        {
            if (TryFind(network, houses, startNodes, targetLengthM, excludedHouseIndices,
                        out houseIndex, out routeLength, out route))
                return true;

            return TryFind(network, houses, startNodes, targetLengthM, null,
                           out houseIndex, out routeLength, out route);
        }

        private static bool TryFind(DeliveryRoadNetwork network,
                                    IReadOnlyList<DeliveryHouse> houses,
                                    IReadOnlyList<DeliveryRoadNode> startNodes,
                                    float targetLengthM,
                                    IReadOnlyList<int> excludedHouseIndices,
                                    out int houseIndex, out float routeLength, out DeliveryRoute route)
        {
            houseIndex = -1;
            routeLength = 0f;
            route = null;
            float bestDifference = float.PositiveInfinity;

            for (int index = 0; index < houses.Count; index++)
            {
                if (excludedHouseIndices != null && Contains(excludedHouseIndices, index)) continue;

                DeliveryHouse house = houses[index];
                if (house == null || house.RoadNode == null) continue;

                if (!TryShortestFromParticipants(network, startNodes, house.RoadNode,
                                                 out float length, out DeliveryRoute shortestRoute))
                    continue;

                float difference = Mathf.Abs(length - targetLengthM);
                if (difference >= bestDifference) continue;

                bestDifference = difference;
                houseIndex = index;
                routeLength = length;
                route = shortestRoute;
            }

            return houseIndex >= 0;
        }

        private static bool TryShortestFromParticipants(DeliveryRoadNetwork network,
                                                         IReadOnlyList<DeliveryRoadNode> startNodes,
                                                         DeliveryRoadNode targetNode,
                                                         out float length, out DeliveryRoute route)
        {
            length = float.PositiveInfinity;
            route = null;

            for (int index = 0; index < startNodes.Count; index++)
            {
                DeliveryRoadNode startNode = startNodes[index];
                if (startNode == null || startNode == targetNode) continue;
                if (!DeliveryRoutePlanner.TryPlan(network, startNode, targetNode, out DeliveryRoute candidate))
                    continue;
                if (candidate.Length >= length) continue;
                length = candidate.Length;
                route = candidate;
            }

            return route != null;
        }

        private static bool Contains(IReadOnlyList<int> list, int value)
        {
            for (int index = 0; index < list.Count; index++)
                if (list[index] == value) return true;
            return false;
        }
    }
}
