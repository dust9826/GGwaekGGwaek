using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class TerrainRoadGrader
    {
        private const float DistanceEpsilon = 0.0001f;
        private const float MaximumSideSlopeDegrees = 32f;
        private const float MinimumShoulderSamples = 4f;

        public static bool GradeTerrain(
            Terrain terrain,
            TerrainRoadBaseline baseline,
            IReadOnlyList<TerrainRoadPath> paths,
            float maximumGradeDegrees,
            float shoulderWidth,
            string undoName,
            bool registerUndo = true)
        {
            return GradeTerrain(
                terrain,
                baseline,
                paths,
                maximumGradeDegrees,
                shoulderWidth,
                shoulderWidth,
                MaximumSideSlopeDegrees,
                1f,
                undoName,
                registerUndo);
        }

        public static bool GradeTerrain(
            Terrain terrain,
            TerrainRoadBaseline baseline,
            IReadOnlyList<TerrainRoadPath> paths,
            float maximumGradeDegrees,
            float minimumShoulderWidth,
            float maximumShoulderWidth,
            float maximumSideSlopeDegrees,
            float sideSlopeSafety,
            string undoName,
            bool registerUndo = true)
        {
            if (terrain == null || terrain.terrainData == null || baseline == null) return false;

            TerrainData data = terrain.terrainData;
            if (!baseline.Matches(data) || !baseline.TryCopyHeights(out float[,] sourceHeights))
            {
                Debug.LogError(
                    $"Road grading baseline does not match TerrainData '{data.name}'. Capture a new baseline first.",
                    terrain);
                return false;
            }

            if (registerUndo)
            {
                Undo.RegisterCompleteObjectUndo(
                    data,
                    string.IsNullOrEmpty(undoName) ? "Grade Terrain Road Network" : undoName);
            }

            int resolution = data.heightmapResolution;
            float[,] influence = new float[resolution, resolution];
            float[,] targetHeight = new float[resolution, resolution];
            Vector3 origin = terrain.GetPosition();
            Vector3 size = data.size;
            List<Vector3> sampled = new();
            List<Vector3> designed = new();

            if (paths != null)
            {
                for (int pathIndex = 0; pathIndex < paths.Count; pathIndex++)
                {
                    TerrainRoadPath path = paths[pathIndex];
                    if (path == null || path.Terrain != terrain) continue;
                    path.GetWorldCenterPoints(sampled);
                    if (sampled.Count < 2) continue;

                    BuildGradeLimitedCenterLine(sampled, maximumGradeDegrees, designed);
                    RasterizeGradingInfluence(
                        designed,
                        path.Width,
                        minimumShoulderWidth,
                        maximumShoulderWidth,
                        maximumSideSlopeDegrees,
                        sideSlopeSafety,
                        origin,
                        size,
                        resolution,
                        sourceHeights,
                        influence,
                        targetHeight);
                }
            }

            float[,] result = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            {
                for (int x = 0; x < resolution; x++)
                {
                    float weight = Mathf.Clamp01(influence[z, x]);
                    result[z, x] = Mathf.Lerp(sourceHeights[z, x], targetHeight[z, x], weight);
                }
            }

            data.SetHeightsDelayLOD(0, 0, result);
            data.SyncHeightmap();
            terrain.Flush();
            EditorUtility.SetDirty(data);
            return true;
        }

        internal static void BuildGradeLimitedCenterLine(
            IReadOnlyList<Vector3> source,
            float maximumGradeDegrees,
            List<Vector3> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (source == null) return;
            for (int i = 0; i < source.Count; i++) destination.Add(source[i]);
            if (destination.Count < 3) return;

            float[] heights = new float[destination.Count];
            for (int i = 0; i < destination.Count; i++) heights[i] = destination[i].y;

            for (int pass = 0; pass < 2; pass++)
            {
                float previous = heights[0];
                for (int i = 1; i < heights.Length - 1; i++)
                {
                    float current = heights[i];
                    heights[i] = (previous + current * 2f + heights[i + 1]) * 0.25f;
                    previous = current;
                }
            }

            float maximumSlope = Mathf.Tan(Mathf.Clamp(maximumGradeDegrees, 1f, 35f) * Mathf.Deg2Rad);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < heights.Length; i++)
                {
                    float distance = PlanarDistance(destination[i - 1], destination[i]);
                    float maximumDelta = Mathf.Max(DistanceEpsilon, distance * maximumSlope);
                    heights[i] = Mathf.Clamp(
                        heights[i],
                        heights[i - 1] - maximumDelta,
                        heights[i - 1] + maximumDelta);
                }

                for (int i = heights.Length - 2; i >= 0; i--)
                {
                    float distance = PlanarDistance(destination[i], destination[i + 1]);
                    float maximumDelta = Mathf.Max(DistanceEpsilon, distance * maximumSlope);
                    heights[i] = Mathf.Clamp(
                        heights[i],
                        heights[i + 1] - maximumDelta,
                        heights[i + 1] + maximumDelta);
                }
            }

            for (int i = 0; i < destination.Count; i++)
            {
                Vector3 point = destination[i];
                point.y = heights[i];
                destination[i] = point;
            }
        }

        private static void RasterizeGradingInfluence(
            IReadOnlyList<Vector3> points,
            float roadWidth,
            float minimumShoulderWidth,
            float maximumShoulderWidth,
            float maximumSideSlopeDegrees,
            float sideSlopeSafety,
            Vector3 origin,
            Vector3 terrainSize,
            int resolution,
            float[,] sourceHeights,
            float[,] influence,
            float[,] targetHeight)
        {
            float halfWidth = Mathf.Max(0.25f, roadWidth) * 0.5f;
            float heightSampleSize = Mathf.Max(
                terrainSize.x,
                terrainSize.z) / Mathf.Max(1, resolution - 1);
            float minimumShoulder = Mathf.Max(
                Mathf.Max(0.1f, minimumShoulderWidth),
                heightSampleSize * MinimumShoulderSamples);
            float maximumShoulder = Mathf.Max(minimumShoulder, maximumShoulderWidth);
            float maximumSideSlope = Mathf.Tan(
                Mathf.Clamp(maximumSideSlopeDegrees, 1f, 45f) * Mathf.Deg2Rad);
            float slopeSafety = Mathf.Clamp(sideSlopeSafety, 1f, 3f);

            for (int segmentIndex = 0; segmentIndex < points.Count - 1; segmentIndex++)
            {
                Vector3 start = points[segmentIndex];
                Vector3 end = points[segmentIndex + 1];
                float startTargetHeight = start.y - RoadSurfaceSampler.SurfaceOffset;
                float endTargetHeight = end.y - RoadSurfaceSampler.SurfaceOffset;
                float startBaselineHeight = SampleBaselineWorldHeight(
                    start,
                    sourceHeights,
                    origin,
                    terrainSize,
                    resolution);
                float endBaselineHeight = SampleBaselineWorldHeight(
                    end,
                    sourceHeights,
                    origin,
                    terrainSize,
                    resolution);
                float requiredShoulder = Mathf.Max(
                    Mathf.Abs(startTargetHeight - startBaselineHeight),
                    Mathf.Abs(endTargetHeight - endBaselineHeight)) * slopeSafety /
                    Mathf.Max(0.01f, maximumSideSlope);
                float shoulder = Mathf.Min(maximumShoulder, Mathf.Max(minimumShoulder, requiredShoulder));
                float radius = halfWidth + shoulder;
                int xMin = WorldToHeight(
                    Mathf.Min(start.x, end.x) - radius,
                    origin.x,
                    terrainSize.x,
                    resolution,
                    true);
                int xMax = WorldToHeight(
                    Mathf.Max(start.x, end.x) + radius,
                    origin.x,
                    terrainSize.x,
                    resolution,
                    false);
                int zMin = WorldToHeight(
                    Mathf.Min(start.z, end.z) - radius,
                    origin.z,
                    terrainSize.z,
                    resolution,
                    true);
                int zMax = WorldToHeight(
                    Mathf.Max(start.z, end.z) + radius,
                    origin.z,
                    terrainSize.z,
                    resolution,
                    false);

                Vector2 segmentStart = new(start.x, start.z);
                Vector2 segmentEnd = new(end.x, end.z);
                Vector2 delta = segmentEnd - segmentStart;
                float lengthSquared = delta.sqrMagnitude;
                for (int z = zMin; z <= zMax; z++)
                {
                    float worldZ = HeightToWorld(z, origin.z, terrainSize.z, resolution);
                    for (int x = xMin; x <= xMax; x++)
                    {
                        float worldX = HeightToWorld(x, origin.x, terrainSize.x, resolution);
                        Vector2 point = new(worldX, worldZ);
                        float t = lengthSquared <= DistanceEpsilon
                            ? 0f
                            : Mathf.Clamp01(Vector2.Dot(point - segmentStart, delta) / lengthSquared);
                        float distance = Vector2.Distance(point, segmentStart + delta * t);
                        if (distance > radius) continue;

                        float weight = distance <= halfWidth
                            ? 1f
                            : 1f - SmoothStep01((distance - halfWidth) / shoulder);
                        float worldHeight = Mathf.Lerp(start.y, end.y, t) - RoadSurfaceSampler.SurfaceOffset;
                        float normalizedHeight = terrainSize.y <= DistanceEpsilon
                            ? 0f
                            : Mathf.Clamp01((worldHeight - origin.y) / terrainSize.y);

                        if (weight > influence[z, x] + 0.001f)
                        {
                            influence[z, x] = weight;
                            targetHeight[z, x] = normalizedHeight;
                        }
                        else if (Mathf.Abs(weight - influence[z, x]) <= 0.001f && weight > 0f)
                        {
                            targetHeight[z, x] = (targetHeight[z, x] + normalizedHeight) * 0.5f;
                        }
                    }
                }
            }
        }

        private static int WorldToHeight(
            float world,
            float origin,
            float terrainSize,
            int resolution,
            bool floor)
        {
            float normalized = terrainSize <= DistanceEpsilon ? 0f : (world - origin) / terrainSize;
            float coordinate = normalized * Mathf.Max(1, resolution - 1);
            int result = floor ? Mathf.FloorToInt(coordinate) - 1 : Mathf.CeilToInt(coordinate) + 1;
            return Mathf.Clamp(result, 0, resolution - 1);
        }

        private static float HeightToWorld(int coordinate, float origin, float size, int resolution)
        {
            return origin + coordinate / (float)Mathf.Max(1, resolution - 1) * size;
        }

        private static float SampleBaselineWorldHeight(
            Vector3 worldPoint,
            float[,] sourceHeights,
            Vector3 origin,
            Vector3 terrainSize,
            int resolution)
        {
            float normalizedX = terrainSize.x <= DistanceEpsilon
                ? 0f
                : Mathf.Clamp01((worldPoint.x - origin.x) / terrainSize.x);
            float normalizedZ = terrainSize.z <= DistanceEpsilon
                ? 0f
                : Mathf.Clamp01((worldPoint.z - origin.z) / terrainSize.z);
            float sampleX = normalizedX * Mathf.Max(1, resolution - 1);
            float sampleZ = normalizedZ * Mathf.Max(1, resolution - 1);
            int x0 = Mathf.Clamp(Mathf.FloorToInt(sampleX), 0, resolution - 1);
            int z0 = Mathf.Clamp(Mathf.FloorToInt(sampleZ), 0, resolution - 1);
            int x1 = Mathf.Min(x0 + 1, resolution - 1);
            int z1 = Mathf.Min(z0 + 1, resolution - 1);
            float tx = sampleX - x0;
            float tz = sampleZ - z0;
            float low = Mathf.Lerp(sourceHeights[z0, x0], sourceHeights[z0, x1], tx);
            float high = Mathf.Lerp(sourceHeights[z1, x0], sourceHeights[z1, x1], tx);
            return origin.y + Mathf.Lerp(low, high, tz) * terrainSize.y;
        }

        private static float SmoothStep01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private static float PlanarDistance(Vector3 left, Vector3 right)
        {
            return Vector2.Distance(new Vector2(left.x, left.z), new Vector2(right.x, right.z));
        }
    }
}
