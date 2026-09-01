using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class TerrainRoadPainter
    {
        private const float MinimumFeather = 0.02f;
        private const float WeightEpsilon = 0.00001f;
        private const float RoadOpacity = 0.995f;
        private const float BorderOpacity = 0.97f;

        public static bool TryFindTerrain(IReadOnlyList<Vector3> worldPoints, out Terrain terrain)
        {
            terrain = null;
            if (worldPoints == null || worldPoints.Count == 0) return false;

            Terrain[] terrains = Terrain.activeTerrains;
            int bestContainedPoints = 0;
            for (int terrainIndex = 0; terrainIndex < terrains.Length; terrainIndex++)
            {
                Terrain candidate = terrains[terrainIndex];
                if (candidate == null || candidate.terrainData == null) continue;

                int containedPoints = 0;
                Vector3 origin = candidate.GetPosition();
                Vector3 size = candidate.terrainData.size;
                for (int pointIndex = 0; pointIndex < worldPoints.Count; pointIndex++)
                {
                    Vector3 point = worldPoints[pointIndex];
                    if (point.x >= origin.x && point.x <= origin.x + size.x
                        && point.z >= origin.z && point.z <= origin.z + size.z)
                    {
                        containedPoints++;
                    }
                }

                if (containedPoints <= bestContainedPoints) continue;
                bestContainedPoints = containedPoints;
                terrain = candidate;
            }

            return terrain != null;
        }

        public static bool PaintRoad(
            Terrain terrain,
            TerrainLayer roadLayer,
            TerrainLayer borderLayer,
            IReadOnlyList<Vector3> worldPoints,
            float width,
            float edgeFeather,
            float borderWidth,
            float borderFeather,
            string undoName,
            bool registerUndo = true)
        {
            if (!CanPaint(terrain, roadLayer, borderLayer, worldPoints)) return false;

            TerrainData data = terrain.terrainData;
            if (registerUndo)
            {
                Undo.RegisterCompleteObjectUndo(
                    data,
                    string.IsNullOrEmpty(undoName) ? "Paint Terrain Road" : undoName);
            }

            int borderLayerIndex = GetOrAddLayer(data, borderLayer);
            int roadLayerIndex = GetOrAddLayer(data, roadLayer);
            int alphaWidth = data.alphamapWidth;
            int alphaHeight = data.alphamapHeight;
            float[,] coreMask = new float[alphaHeight, alphaWidth];
            float[,] outerMask = new float[alphaHeight, alphaWidth];
            RasterizeStroke(
                terrain,
                worldPoints,
                width,
                edgeFeather,
                borderWidth,
                borderFeather,
                coreMask,
                outerMask);
            ApplyMasks(data, roadLayerIndex, borderLayerIndex, coreMask, outerMask, false);
            FinishTerrainChange(terrain);
            return true;
        }

        public static bool RebuildTerrainRoads(
            Terrain terrain,
            TerrainLayer roadLayer,
            TerrainLayer borderLayer,
            IReadOnlyList<TerrainRoadPath> paths,
            string undoName,
            bool registerUndo = true)
        {
            if (terrain == null || terrain.terrainData == null || roadLayer == null || borderLayer == null)
                return false;

            TerrainData data = terrain.terrainData;
            if (registerUndo)
            {
                Undo.RegisterCompleteObjectUndo(
                    data,
                    string.IsNullOrEmpty(undoName) ? "Rebuild Terrain Roads" : undoName);
            }

            int borderLayerIndex = GetOrAddLayer(data, borderLayer);
            int roadLayerIndex = GetOrAddLayer(data, roadLayer);
            int alphaWidth = data.alphamapWidth;
            int alphaHeight = data.alphamapHeight;
            float[,] coreMask = new float[alphaHeight, alphaWidth];
            float[,] outerMask = new float[alphaHeight, alphaWidth];
            List<Vector3> worldPoints = new();

            if (paths != null)
            {
                for (int i = 0; i < paths.Count; i++)
                {
                    TerrainRoadPath path = paths[i];
                    if (path == null || path.Terrain != terrain) continue;
                    path.GetWorldCenterPoints(worldPoints);
                    if (worldPoints.Count < 2) continue;
                    RasterizeStroke(
                        terrain,
                        worldPoints,
                        path.Width,
                        path.EdgeFeather,
                        path.BorderWidth,
                        path.BorderFeather,
                        coreMask,
                        outerMask);
                }
            }

            ApplyMasks(data, roadLayerIndex, borderLayerIndex, coreMask, outerMask, true);
            FinishTerrainChange(terrain);
            return true;
        }

        internal static float CalculateBrushStrength(float distance, float width, float edgeFeather)
        {
            float halfWidth = Mathf.Max(0.25f, width) * 0.5f;
            float feather = Mathf.Max(MinimumFeather, edgeFeather);
            if (distance <= halfWidth) return 1f;
            if (distance >= halfWidth + feather) return 0f;
            float t = Mathf.InverseLerp(halfWidth, halfWidth + feather, distance);
            return 1f - t * t * (3f - 2f * t);
        }

        private static void RasterizeStroke(
            Terrain terrain,
            IReadOnlyList<Vector3> worldPoints,
            float width,
            float edgeFeather,
            float borderWidth,
            float borderFeather,
            float[,] coreMask,
            float[,] outerMask)
        {
            TerrainData data = terrain.terrainData;
            Vector3 origin = terrain.GetPosition();
            Vector3 size = data.size;
            int alphaWidth = data.alphamapWidth;
            int alphaHeight = data.alphamapHeight;
            float safeWidth = Mathf.Max(0.25f, width);
            float safeBorder = Mathf.Max(0.1f, borderWidth);
            float coreFeather = Mathf.Min(
                Mathf.Max(MinimumFeather, edgeFeather),
                safeBorder * 0.3f);
            float safeBorderFeather = Mathf.Max(MinimumFeather, borderFeather);
            float outerWidth = safeWidth + safeBorder * 2f;
            float radius = outerWidth * 0.5f + safeBorderFeather;

            for (int segmentIndex = 0; segmentIndex < worldPoints.Count - 1; segmentIndex++)
            {
                Vector3 start = worldPoints[segmentIndex];
                Vector3 end = worldPoints[segmentIndex + 1];
                int xMin = WorldToAlpha(
                    Mathf.Min(start.x, end.x) - radius,
                    origin.x,
                    size.x,
                    alphaWidth,
                    true);
                int xMax = WorldToAlpha(
                    Mathf.Max(start.x, end.x) + radius,
                    origin.x,
                    size.x,
                    alphaWidth,
                    false);
                int zMin = WorldToAlpha(
                    Mathf.Min(start.z, end.z) - radius,
                    origin.z,
                    size.z,
                    alphaHeight,
                    true);
                int zMax = WorldToAlpha(
                    Mathf.Max(start.z, end.z) + radius,
                    origin.z,
                    size.z,
                    alphaHeight,
                    false);

                Vector2 segmentStart = new(start.x, start.z);
                Vector2 segmentEnd = new(end.x, end.z);
                Vector2 delta = segmentEnd - segmentStart;
                float lengthSquared = delta.sqrMagnitude;
                for (int z = zMin; z <= zMax; z++)
                {
                    float worldZ = AlphaToWorld(z, origin.z, size.z, alphaHeight);
                    for (int x = xMin; x <= xMax; x++)
                    {
                        float worldX = AlphaToWorld(x, origin.x, size.x, alphaWidth);
                        Vector2 point = new(worldX, worldZ);
                        float t = lengthSquared <= WeightEpsilon
                            ? 0f
                            : Mathf.Clamp01(Vector2.Dot(point - segmentStart, delta) / lengthSquared);
                        float distance = Vector2.Distance(point, segmentStart + delta * t);
                        if (distance > radius) continue;

                        coreMask[z, x] = Mathf.Max(
                            coreMask[z, x],
                            CalculateBrushStrength(distance, safeWidth, coreFeather));
                        outerMask[z, x] = Mathf.Max(
                            outerMask[z, x],
                            CalculateBrushStrength(distance, outerWidth, safeBorderFeather));
                    }
                }
            }
        }

        private static void ApplyMasks(
            TerrainData data,
            int roadLayerIndex,
            int borderLayerIndex,
            float[,] coreMask,
            float[,] outerMask,
            bool replaceExistingRoadNetwork)
        {
            int width = data.alphamapWidth;
            int height = data.alphamapHeight;
            float[,,] alpha = data.GetAlphamaps(0, 0, width, height);
            int layerCount = alpha.GetLength(2);

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    float requestedCore = coreMask[z, x] * RoadOpacity;
                    float requestedBorder = outerMask[z, x] * BorderOpacity;
                    float core = replaceExistingRoadNetwork
                        ? requestedCore
                        : Mathf.Max(alpha[z, x, roadLayerIndex], requestedCore);
                    float borderCandidate = replaceExistingRoadNetwork
                        ? requestedBorder
                        : Mathf.Max(alpha[z, x, borderLayerIndex], requestedBorder);
                    float border = Mathf.Min(borderCandidate, (1f - core) * BorderOpacity);
                    SetRoadAndBorderWeights(
                        alpha,
                        z,
                        x,
                        roadLayerIndex,
                        borderLayerIndex,
                        core,
                        border,
                        layerCount);
                }
            }

            data.SetAlphamaps(0, 0, alpha);
        }

        private static void SetRoadAndBorderWeights(
            float[,,] alpha,
            int z,
            int x,
            int roadLayerIndex,
            int borderLayerIndex,
            float roadWeight,
            float borderWeight,
            int layerCount)
        {
            float core = Mathf.Clamp01(roadWeight);
            float border = Mathf.Clamp(borderWeight, 0f, 1f - core);
            float remaining = 1f - core - border;
            float otherSum = 0f;
            for (int layer = 0; layer < layerCount; layer++)
            {
                if (layer != roadLayerIndex && layer != borderLayerIndex)
                    otherSum += alpha[z, x, layer];
            }

            if (otherSum > WeightEpsilon)
            {
                float scale = remaining / otherSum;
                for (int layer = 0; layer < layerCount; layer++)
                {
                    if (layer != roadLayerIndex && layer != borderLayerIndex)
                        alpha[z, x, layer] *= scale;
                }
            }
            else
            {
                for (int layer = 0; layer < layerCount; layer++)
                {
                    if (layer != roadLayerIndex && layer != borderLayerIndex)
                        alpha[z, x, layer] = 0f;
                }

                int baseLayer = FindBaseLayer(layerCount, roadLayerIndex, borderLayerIndex);
                if (baseLayer >= 0) alpha[z, x, baseLayer] = remaining;
            }

            alpha[z, x, roadLayerIndex] = core;
            alpha[z, x, borderLayerIndex] = border;
        }

        private static int FindBaseLayer(int layerCount, int roadLayerIndex, int borderLayerIndex)
        {
            for (int i = 0; i < layerCount; i++)
            {
                if (i != roadLayerIndex && i != borderLayerIndex) return i;
            }
            return -1;
        }

        private static bool CanPaint(
            Terrain terrain,
            TerrainLayer roadLayer,
            TerrainLayer borderLayer,
            IReadOnlyList<Vector3> worldPoints)
        {
            return terrain != null
                   && terrain.terrainData != null
                   && roadLayer != null
                   && borderLayer != null
                   && worldPoints != null
                   && worldPoints.Count >= 2;
        }

        private static int GetOrAddLayer(TerrainData data, TerrainLayer layer)
        {
            TerrainLayer[] layers = data.terrainLayers;
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == layer) return i;
            }

            Array.Resize(ref layers, layers.Length + 1);
            layers[^1] = layer;
            data.terrainLayers = layers;
            return layers.Length - 1;
        }

        private static int WorldToAlpha(
            float world,
            float origin,
            float terrainSize,
            int resolution,
            bool floor)
        {
            float normalized = terrainSize <= WeightEpsilon ? 0f : (world - origin) / terrainSize;
            float coordinate = normalized * Mathf.Max(1, resolution - 1);
            int result = floor ? Mathf.FloorToInt(coordinate) - 1 : Mathf.CeilToInt(coordinate) + 1;
            return Mathf.Clamp(result, 0, resolution - 1);
        }

        private static float AlphaToWorld(int coordinate, float origin, float size, int resolution)
        {
            return origin + coordinate / (float)Mathf.Max(1, resolution - 1) * size;
        }

        private static void FinishTerrainChange(Terrain terrain)
        {
            terrain.Flush();
            EditorUtility.SetDirty(terrain.terrainData);
        }
    }
}
