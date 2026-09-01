using System;
using System.Collections.Generic;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Splines;

namespace PPack
{
    internal static class RoadSurfaceSampler
    {
        internal const float SurfaceOffset = 0.035f;
        private const float MaximumRayDistance = 10000f;
        private const float SampleSpacing = 0.75f;

        public static bool TryGetMouseGround(Vector2 guiPosition, GameObject excludedRoot, out Vector3 point)
        {
            Ray ray = HandleUtility.GUIPointToWorldRay(guiPosition);
            RaycastHit[] hits = Physics.RaycastAll(
                ray,
                MaximumRayDistance,
                Physics.DefaultRaycastLayers,
                QueryTriggerInteraction.Ignore);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));

            for (int i = 0; i < hits.Length; i++)
            {
                Transform hitTransform = hits[i].collider.transform;
                if (excludedRoot != null && hitTransform.IsChildOf(excludedRoot.transform)) continue;
                if (hits[i].collider.GetComponentInParent<RoadPath>() != null) continue;
                point = hits[i].point;
                return true;
            }

            point = default;
            return false;
        }

        public static bool TryConformToGround(Vector3 position, out Vector3 conformed)
        {
            Terrain[] terrains = Terrain.activeTerrains;
            for (int i = 0; i < terrains.Length; i++)
            {
                Terrain terrain = terrains[i];
                if (terrain == null || terrain.terrainData == null) continue;

                Vector3 origin = terrain.GetPosition();
                Vector3 size = terrain.terrainData.size;
                float normalizedX = (position.x - origin.x) / size.x;
                float normalizedZ = (position.z - origin.z) / size.z;
                if (normalizedX < 0f || normalizedX > 1f || normalizedZ < 0f || normalizedZ > 1f) continue;

                float height = terrain.terrainData.GetInterpolatedHeight(normalizedX, normalizedZ) + origin.y;
                conformed = new Vector3(position.x, height + SurfaceOffset, position.z);
                return true;
            }

            Vector3 rayOrigin = position + Vector3.up * 1000f;
            if (Physics.Raycast(
                    rayOrigin,
                    Vector3.down,
                    out RaycastHit hit,
                    2000f,
                    Physics.DefaultRaycastLayers,
                    QueryTriggerInteraction.Ignore))
            {
                conformed = new Vector3(position.x, hit.point.y + SurfaceOffset, position.z);
                return true;
            }

            conformed = position + Vector3.up * SurfaceOffset;
            return false;
        }

        public static List<Vector3> BuildConformedCenterLine(IReadOnlyList<Vector3> controlPoints)
        {
            List<Vector3> result = new();
            if (controlPoints == null || controlPoints.Count < 2) return result;

            List<float3> knots = new(controlPoints.Count);
            for (int i = 0; i < controlPoints.Count; i++)
            {
                TryConformToGround(controlPoints[i], out Vector3 conformed);
                knots.Add(new float3(conformed.x, conformed.y, conformed.z));
            }

            Spline spline = new(knots, TangentMode.AutoSmooth);
            float length = Mathf.Max(0.01f, SplineUtility.CalculateLength(spline, Matrix4x4.identity));
            int steps = Mathf.Max(1, Mathf.CeilToInt(length / SampleSpacing));
            for (int step = 0; step <= steps; step++)
            {
                float3 point = SplineUtility.EvaluatePosition(spline, step / (float)steps);
                result.Add(new Vector3(point.x, point.y, point.z));
            }

            List<Vector3> gradeLimited = new(result.Count);
            TerrainRoadGrader.BuildGradeLimitedCenterLine(
                result,
                RoadBuilderPreferences.MaximumGrade,
                gradeLimited);
            result = gradeLimited;
            return result;
        }
    }
}
