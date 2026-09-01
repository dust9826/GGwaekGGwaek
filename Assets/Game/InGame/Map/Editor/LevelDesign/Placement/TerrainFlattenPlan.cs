using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal sealed class TerrainFlattenPlan
    {
        private const float FootprintPadding = 0.4f;
        private const float BlendDistance = 2.5f;

        private readonly float[,] _proposedHeights;

        private TerrainFlattenPlan(
            Terrain terrain,
            Vector3 placementPosition,
            Vector3 footprintCenter,
            Vector3 right,
            Vector3 forward,
            float halfWidth,
            float halfDepth,
            int xBase,
            int zBase,
            float[,] proposedHeights,
            float maximumAdjustment)
        {
            Terrain = terrain;
            PlacementPosition = placementPosition;
            FootprintCenter = footprintCenter;
            Right = right;
            Forward = forward;
            HalfWidth = halfWidth;
            HalfDepth = halfDepth;
            XBase = xBase;
            ZBase = zBase;
            _proposedHeights = proposedHeights;
            MaximumAdjustment = maximumAdjustment;
        }

        public Terrain Terrain { get; }
        public TerrainData TerrainData => Terrain.terrainData;
        public Vector3 PlacementPosition { get; }
        public Vector3 FootprintCenter { get; }
        public Vector3 Right { get; }
        public Vector3 Forward { get; }
        public float HalfWidth { get; }
        public float HalfDepth { get; }
        public float MaximumAdjustment { get; }
        public int XBase { get; }
        public int ZBase { get; }
        public int Width => _proposedHeights.GetLength(1);
        public int Height => _proposedHeights.GetLength(0);

        public static bool TryCreate(
            Terrain terrain,
            GameObject prefab,
            Vector3 hitPoint,
            out TerrainFlattenPlan plan)
        {
            Quaternion rotation = prefab == null ? Quaternion.identity : prefab.transform.rotation;
            return TryCreate(terrain, prefab, hitPoint, rotation, out plan);
        }

        public static bool TryCreate(
            Terrain terrain,
            GameObject prefab,
            Vector3 hitPoint,
            Quaternion rotation,
            out TerrainFlattenPlan plan)
        {
            plan = null;
            if (terrain == null || terrain.terrainData == null) return false;
            if (!PrefabFootprint.TryCreate(prefab, out PrefabFootprint footprint)) return false;

            Vector3 scale = prefab.transform.localScale;
            Vector3 right = Vector3.ProjectOnPlane(rotation * Vector3.right, Vector3.up).normalized;
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, Vector3.up).normalized;
            if (right.sqrMagnitude < 0.9f || forward.sqrMagnitude < 0.9f) return false;

            Bounds localBounds = footprint.LocalBounds;
            float floorOffset = (rotation * Vector3.Scale(
                new Vector3(0f, localBounds.min.y, 0f), scale)).y;
            Vector3 placementPosition = hitPoint - Vector3.up * floorOffset;
            Vector3 centerOffset = rotation * Vector3.Scale(
                new Vector3(localBounds.center.x, 0f, localBounds.center.z), scale);
            Vector3 footprintCenter = new Vector3(
                placementPosition.x + centerOffset.x,
                hitPoint.y,
                placementPosition.z + centerOffset.z);
            float halfWidth = localBounds.extents.x * Mathf.Abs(scale.x) + FootprintPadding;
            float halfDepth = localBounds.extents.z * Mathf.Abs(scale.z) + FootprintPadding;

            TerrainData data = terrain.terrainData;
            Vector3 terrainSize = data.size;
            Vector3 terrainPosition = terrain.transform.position;
            int resolution = data.heightmapResolution;

            Vector3[] outerCorners = GetCorners(footprintCenter, right, forward,
                halfWidth + BlendDistance, halfDepth + BlendDistance);
            Vector3[] coreCorners = GetCorners(footprintCenter, right, forward, halfWidth, halfDepth);
            foreach (Vector3 corner in coreCorners)
            {
                Vector3 local = terrain.transform.InverseTransformPoint(corner);
                if (local.x < 0f || local.z < 0f || local.x > terrainSize.x || local.z > terrainSize.z)
                    return false;
            }

            float minLocalX = float.PositiveInfinity;
            float maxLocalX = float.NegativeInfinity;
            float minLocalZ = float.PositiveInfinity;
            float maxLocalZ = float.NegativeInfinity;
            foreach (Vector3 corner in outerCorners)
            {
                Vector3 local = terrain.transform.InverseTransformPoint(corner);
                minLocalX = Mathf.Min(minLocalX, local.x);
                maxLocalX = Mathf.Max(maxLocalX, local.x);
                minLocalZ = Mathf.Min(minLocalZ, local.z);
                maxLocalZ = Mathf.Max(maxLocalZ, local.z);
            }

            int xBase = Mathf.Clamp(Mathf.FloorToInt(minLocalX / terrainSize.x * (resolution - 1)), 0, resolution - 1);
            int xMax = Mathf.Clamp(Mathf.CeilToInt(maxLocalX / terrainSize.x * (resolution - 1)), 0, resolution - 1);
            int zBase = Mathf.Clamp(Mathf.FloorToInt(minLocalZ / terrainSize.z * (resolution - 1)), 0, resolution - 1);
            int zMax = Mathf.Clamp(Mathf.CeilToInt(maxLocalZ / terrainSize.z * (resolution - 1)), 0, resolution - 1);
            int width = xMax - xBase + 1;
            int height = zMax - zBase + 1;
            if (width < 2 || height < 2) return false;

            float[,] original = data.GetHeights(xBase, zBase, width, height);
            float[,] proposed = new float[height, width];
            float targetHeight = Mathf.Clamp01((hitPoint.y - terrainPosition.y) / terrainSize.y);
            float maximumAdjustment = 0f;

            for (int z = 0; z < height; z++)
            {
                for (int x = 0; x < width; x++)
                {
                    int globalX = xBase + x;
                    int globalZ = zBase + z;
                    Vector3 localPoint = new Vector3(
                        globalX / (float)(resolution - 1) * terrainSize.x,
                        0f,
                        globalZ / (float)(resolution - 1) * terrainSize.z);
                    Vector3 worldPoint = terrain.transform.TransformPoint(localPoint);
                    Vector3 offset = worldPoint - footprintCenter;
                    float localX = Mathf.Abs(Vector3.Dot(offset, right));
                    float localZ = Mathf.Abs(Vector3.Dot(offset, forward));
                    float outsideX = Mathf.Max(0f, localX - halfWidth);
                    float outsideZ = Mathf.Max(0f, localZ - halfDepth);
                    float outsideDistance = Mathf.Sqrt(outsideX * outsideX + outsideZ * outsideZ);
                    float weight = 1f - Mathf.Clamp01(outsideDistance / BlendDistance);
                    weight = weight * weight * (3f - 2f * weight);

                    float originalHeight = original[z, x];
                    float nextHeight = Mathf.Lerp(originalHeight, targetHeight, weight);
                    proposed[z, x] = nextHeight;
                    maximumAdjustment = Mathf.Max(
                        maximumAdjustment,
                        Mathf.Abs(nextHeight - originalHeight) * terrainSize.y);
                }
            }

            plan = new TerrainFlattenPlan(
                terrain,
                placementPosition,
                footprintCenter,
                right,
                forward,
                halfWidth,
                halfDepth,
                xBase,
                zBase,
                proposed,
                maximumAdjustment);
            return true;
        }

        public void Apply(string undoLabel)
        {
            Undo.RegisterCompleteObjectUndo(TerrainData, undoLabel);
            TerrainData.SetHeightsDelayLOD(XBase, ZBase, _proposedHeights);
            TerrainData.SyncHeightmap();
            EditorUtility.SetDirty(TerrainData);
        }

        public Vector3 GetWorldPoint(int x, int z)
        {
            TerrainData data = TerrainData;
            int resolution = data.heightmapResolution;
            Vector3 local = new Vector3(
                (XBase + x) / (float)(resolution - 1) * data.size.x,
                _proposedHeights[z, x] * data.size.y,
                (ZBase + z) / (float)(resolution - 1) * data.size.z);
            return Terrain.transform.TransformPoint(local);
        }

        public Vector3[] GetFootprintCorners(float elevation = 0f)
        {
            Vector3 center = FootprintCenter + Vector3.up * elevation;
            return GetCorners(center, Right, Forward, HalfWidth, HalfDepth);
        }

        private static Vector3[] GetCorners(
            Vector3 center,
            Vector3 right,
            Vector3 forward,
            float halfWidth,
            float halfDepth)
        {
            return new[]
            {
                center - right * halfWidth - forward * halfDepth,
                center + right * halfWidth - forward * halfDepth,
                center + right * halfWidth + forward * halfDepth,
                center - right * halfWidth + forward * halfDepth
            };
        }
    }
}
