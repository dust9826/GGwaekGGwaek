using UnityEngine;

namespace PPack
{
    public readonly struct BlizzardRoutePlan
    {
        public BlizzardRoutePlan(Vector2 start, Vector2 secondRegion, Vector2 direction, float travelDistance)
        {
            Start = start;
            SecondRegion = secondRegion;
            Direction = direction;
            TravelDistance = travelDistance;
        }

        public Vector2 Start { get; }
        public Vector2 SecondRegion { get; }
        public Vector2 Direction { get; }
        public float TravelDistance { get; }
    }

    /// <summary>초기 적설량과 현재 적설량의 차이가 큰 두 구역을 잇고 맵 밖까지 뻗는 경로를 고른다.</summary>
    public static class BlizzardRoutePlanner
    {
        public static bool TryPlan(SnowHeightFieldCpu field, int initialDepthMm, float affectedRadiusM,
            float candidateStepM, float minimumRegionSeparationM, int seed, out BlizzardRoutePlan plan)
        {
            plan = default;
            if (field == null || field.HeightMm.Length == 0) return false;

            SnowFieldGeometry geo = field.Geo;
            int resX = geo.ResX;
            int resZ = geo.ResZ;
            int stride = resX + 1;
            var summedDeficit = new long[stride * (resZ + 1)];

            for (int z = 0; z < resZ; z++)
            {
                long row = 0L;
                int fieldRow = z * resX;
                int integralRow = (z + 1) * stride;
                int previousIntegralRow = z * stride;
                for (int x = 0; x < resX; x++)
                {
                    int cell = fieldRow + x;
                    int baseline = InitialDepthAt(field, cell, initialDepthMm);
                    // 구역 사용량은 Σ(게임 시작 시 눈 - 현재 눈)이다. 같은 구역 안에 옮겨 쌓은
                    // 눈은 음수로 더해져 제거량을 상쇄해야 하므로 셀별로 0에 고정하지 않는다.
                    row += baseline - field.GetAt(cell);
                    summedDeficit[integralRow + x + 1] = summedDeficit[previousIntegralRow + x + 1] + row;
                }
            }

            int radiusCells = Mathf.Max(1, Mathf.CeilToInt(affectedRadiusM / SnowFieldGeometry.CellSizeM));
            int stepCells = Mathf.Max(1, Mathf.RoundToInt(candidateStepM / SnowFieldGeometry.CellSizeM));
            int minX = radiusCells;
            int minZ = radiusCells;
            int maxX = resX - radiusCells - 1;
            int maxZ = resZ - radiusCells - 1;
            if (minX > maxX || minZ > maxZ) return false;

            Candidate first = default;
            bool hasFirst = false;
            for (int z = minZ; z <= maxZ; z += stepCells)
            {
                for (int x = minX; x <= maxX; x += stepCells)
                {
                    int cell = geo.CellIndex(x, z);
                    if (InitialDepthAt(field, cell, initialDepthMm) <= 0) continue;

                    long score = SumRect(summedDeficit, stride,
                        x - radiusCells, z - radiusCells, x + radiusCells, z + radiusCells);
                    var candidate = new Candidate(x, z, score, TieBreak(x, z, seed));
                    if (!hasFirst || candidate.IsBetterThan(first))
                    {
                        first = candidate;
                        hasFirst = true;
                    }
                }
            }

            if (!hasFirst) return false;

            float separationCells = Mathf.Max(minimumRegionSeparationM,
                affectedRadiusM * 2f) / SnowFieldGeometry.CellSizeM;
            float separationCellsSq = separationCells * separationCells;
            Candidate second = default;
            bool hasSecond = false;
            for (int z = minZ; z <= maxZ; z += stepCells)
            {
                for (int x = minX; x <= maxX; x += stepCells)
                {
                    float dx = x - first.X;
                    float dz = z - first.Z;
                    if (dx * dx + dz * dz < separationCellsSq) continue;

                    int cell = geo.CellIndex(x, z);
                    if (InitialDepthAt(field, cell, initialDepthMm) <= 0) continue;

                    long score = SumRect(summedDeficit, stride,
                        x - radiusCells, z - radiusCells, x + radiusCells, z + radiusCells);
                    var candidate = new Candidate(x, z, score, TieBreak(x, z, seed ^ 0x5F3759DF));
                    if (!hasSecond || candidate.IsBetterThan(second))
                    {
                        second = candidate;
                        hasSecond = true;
                    }
                }
            }

            if (!hasSecond) return false;

            Vector2 start = CellCenter(geo, first.X, first.Z);
            Vector2 secondRegion = CellCenter(geo, second.X, second.Z);
            Vector2 direction = (secondRegion - start).normalized;
            if (direction.sqrMagnitude <= 0.0001f) return false;

            float travelDistance = DistanceToExpandedExit(geo, start, direction, affectedRadiusM);
            if (travelDistance <= 0f) return false;

            plan = new BlizzardRoutePlan(start, secondRegion, direction, travelDistance);
            return true;
        }

        private static int InitialDepthAt(SnowHeightFieldCpu field, int cell, int initialDepthMm)
            => field.Ground != null ? field.Ground.InitialDepthAt(cell, initialDepthMm) : initialDepthMm;

        private static long SumRect(long[] integral, int stride, int x0, int z0, int x1, int z1)
        {
            int left = x0;
            int top = z0;
            int right = x1 + 1;
            int bottom = z1 + 1;
            return integral[bottom * stride + right]
                   - integral[top * stride + right]
                   - integral[bottom * stride + left]
                   + integral[top * stride + left];
        }

        private static Vector2 CellCenter(SnowFieldGeometry geo, int x, int z)
            => new Vector2(
                geo.OriginXM + (x + 0.5f) * SnowFieldGeometry.CellSizeM,
                geo.OriginZM + (z + 0.5f) * SnowFieldGeometry.CellSizeM);

        private static float DistanceToExpandedExit(SnowFieldGeometry geo, Vector2 start,
            Vector2 direction, float radiusM)
        {
            float minX = geo.OriginXM - radiusM;
            float minZ = geo.OriginZM - radiusM;
            float maxX = geo.OriginXM + geo.ResX * SnowFieldGeometry.CellSizeM + radiusM;
            float maxZ = geo.OriginZM + geo.ResZ * SnowFieldGeometry.CellSizeM + radiusM;
            float tx = direction.x > 0f
                ? (maxX - start.x) / direction.x
                : direction.x < 0f ? (minX - start.x) / direction.x : float.PositiveInfinity;
            float tz = direction.y > 0f
                ? (maxZ - start.y) / direction.y
                : direction.y < 0f ? (minZ - start.y) / direction.y : float.PositiveInfinity;
            return Mathf.Min(tx, tz);
        }

        private static uint TieBreak(int x, int z, int seed)
        {
            uint value = unchecked((uint)(x * 73856093 ^ z * 19349663 ^ seed * 83492791));
            value ^= value >> 16;
            value *= 0x7FEB352Du;
            value ^= value >> 15;
            value *= 0x846CA68Bu;
            return value ^ (value >> 16);
        }

        private readonly struct Candidate
        {
            public Candidate(int x, int z, long score, uint tieBreak)
            {
                X = x;
                Z = z;
                Score = score;
                TieBreakValue = tieBreak;
            }

            public int X { get; }
            public int Z { get; }
            private long Score { get; }
            private uint TieBreakValue { get; }

            public bool IsBetterThan(Candidate other)
                => Score > other.Score || Score == other.Score && TieBreakValue > other.TieBreakValue;
        }
    }
}
