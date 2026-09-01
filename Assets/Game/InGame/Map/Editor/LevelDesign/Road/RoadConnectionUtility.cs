using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    internal readonly struct RoadSnapResult
    {
        public RoadSnapResult(
            Vector3 point,
            TerrainRoadPath targetRoad,
            Vector3 tangent,
            bool isEndpoint)
        {
            Point = point;
            TargetRoad = targetRoad;
            Tangent = tangent;
            IsEndpoint = isEndpoint;
        }

        public Vector3 Point { get; }
        public TerrainRoadPath TargetRoad { get; }
        public Vector3 Tangent { get; }
        public bool IsEndpoint { get; }
        public float TargetWidth => TargetRoad == null ? 0f : TargetRoad.Width;
    }

    internal static class RoadConnectionUtility
    {
        public const float DefaultSnapDistance = 1.8f;

        public static bool TrySnapToRoad(
            Vector3 candidate,
            TerrainRoadPath ignoredRoad,
            out Vector3 snappedPoint,
            out TerrainRoadPath targetRoad)
        {
            if (TrySnapToRoad(candidate, ignoredRoad, out RoadSnapResult result))
            {
                snappedPoint = result.Point;
                targetRoad = result.TargetRoad;
                return true;
            }

            snappedPoint = candidate;
            targetRoad = null;
            return false;
        }

        public static bool TrySnapToRoad(
            Vector3 candidate,
            TerrainRoadPath ignoredRoad,
            out RoadSnapResult result)
        {
            return TryFindNearestRoad(candidate, ignoredRoad, DefaultSnapDistance, out result);
        }

        public static bool TryFindNearestRoad(
            Vector3 candidate,
            TerrainRoadPath ignoredRoad,
            float maximumSurfaceDistance,
            out RoadSnapResult result)
        {
            result = default;
            maximumSurfaceDistance = Mathf.Max(0f, maximumSurfaceDistance);
            float bestSurfaceDistance = float.PositiveInfinity;
            float bestCenterDistance = float.PositiveInfinity;
            TerrainRoadPath[] roads = Object.FindObjectsByType<TerrainRoadPath>();
            List<Vector3> sampledPoints = new();

            for (int roadIndex = 0; roadIndex < roads.Length; roadIndex++)
            {
                TerrainRoadPath road = roads[roadIndex];
                if (road == null || road == ignoredRoad) continue;
                road.GetWorldCenterPoints(sampledPoints);
                if (sampledPoints.Count < 2) continue;

                for (int segment = 0; segment < sampledPoints.Count - 1; segment++)
                {
                    Vector3 start = sampledPoints[segment];
                    Vector3 end = sampledPoints[segment + 1];
                    Vector3 closest = ClosestPointOnSegmentXZ(candidate, start, end);
                    float distance = Vector2.Distance(
                        new Vector2(candidate.x, candidate.z),
                        new Vector2(closest.x, closest.z));
                    float surfaceDistance = Mathf.Max(0f, distance - road.Width * 0.5f);
                    if (surfaceDistance > maximumSurfaceDistance) continue;
                    if (surfaceDistance > bestSurfaceDistance + 0.001f) continue;
                    if (Mathf.Abs(surfaceDistance - bestSurfaceDistance) <= 0.001f &&
                        distance >= bestCenterDistance) continue;

                    Vector3 tangent = Vector3.ProjectOnPlane(end - start, Vector3.up).normalized;
                    if (tangent.sqrMagnitude < 0.0001f) continue;

                    float endpointTolerance = Mathf.Max(0.35f, road.Width * 0.1f);
                    bool isEndpoint = Vector2.Distance(
                                          new Vector2(closest.x, closest.z),
                                          new Vector2(sampledPoints[0].x, sampledPoints[0].z)) <= endpointTolerance ||
                                      Vector2.Distance(
                                          new Vector2(closest.x, closest.z),
                                          new Vector2(sampledPoints[^1].x, sampledPoints[^1].z)) <= endpointTolerance;
                    bestSurfaceDistance = surfaceDistance;
                    bestCenterDistance = distance;
                    result = new RoadSnapResult(closest, road, tangent, isEndpoint);
                }
            }

            return result.TargetRoad != null;
        }

        public static Vector3 GetRoadEdgePoint(Vector3 candidate, RoadSnapResult result)
        {
            if (result.TargetRoad == null) return result.Point;

            Vector3 fromCenter = Vector3.ProjectOnPlane(candidate - result.Point, Vector3.up);
            if (fromCenter.sqrMagnitude < 0.0001f)
                fromCenter = Vector3.Cross(Vector3.up, result.Tangent);
            if (fromCenter.sqrMagnitude < 0.0001f) fromCenter = Vector3.right;

            return result.Point + fromCenter.normalized * result.TargetWidth * 0.5f;
        }

        internal static Vector3 ClosestPointOnSegmentXZ(Vector3 point, Vector3 start, Vector3 end)
        {
            Vector2 a = new(start.x, start.z);
            Vector2 b = new(end.x, end.z);
            Vector2 p = new(point.x, point.z);
            Vector2 delta = b - a;
            float lengthSquared = delta.sqrMagnitude;
            float t = lengthSquared < 0.0001f ? 0f : Mathf.Clamp01(Vector2.Dot(p - a, delta) / lengthSquared);
            return Vector3.Lerp(start, end, t);
        }
    }
}
