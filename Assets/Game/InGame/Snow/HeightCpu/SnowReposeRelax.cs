using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// relax 가 넘길 수 없는 벽. 블레이드 다운으로 전진하는 동안 블레이드가 차지한 상자는
    /// 눈을 <b>받지 않는다</b>. 주기만 하고 받지 않는 셀이다.
    ///
    /// 이 한 줄이 없으면 앞에 쌓인 더미가 치운 차선 쪽으로 도로 새어나간다. 그리고 벽이
    /// <b>블레이드 폭만큼만</b>이라는 것이 둔덕의 원인이다 — 더미가 자라 안식각 원뿔이 끝단을
    /// 넘어서면 초과분은 벽이 없는 바깥으로 돌아나가 뒤로 흐른다.
    /// </summary>
    /// <summary>
    /// relax 가 넘을 수 없는 벽. 블레이드 다운으로 전진하는 동안 블레이드가 차지한 자리는
    /// 눈을 <b>받지 않는다</b>. 주기만 하고 받지 않는 셀이다.
    ///
    /// 이 한 줄이 없으면 앞에 쌓인 더미가 치운 차선 쪽으로 도로 새어나간다. 그리고 벽이
    /// <b>블레이드가 실제로 덮는 자리만큼만</b>이라는 것이 둔덕의 원인이다 — 더미가 자라
    /// 안식각 원뿔이 끝단을 넘어서면 초과분은 벽이 없는 바깥으로 돌아나가 뒤로 흐른다.
    /// 날개를 달면 그 "바깥"이 막히고, 그래서 반대쪽으로만 뱉는다.
    /// </summary>
    public struct SnowRelaxBarrier
    {
        public const int MaxSegments = 3;

        public bool Active;
        public int SegmentCount;
        public SnowObb S0, S1, S2;

        public bool Contains(float wx, float wz)
        {
            if (!Active) return false;
            if (SegmentCount > 0 && S0.Contains(wx, wz)) return true;
            if (SegmentCount > 1 && S1.Contains(wx, wz)) return true;
            if (SegmentCount > 2 && S2.Contains(wx, wz)) return true;
            return false;
        }

        /// <summary>배리어 전체를 감싸는 월드 사각형. relax 가 이웃마다 월드 변환을 돌지 않게 한다.</summary>
        public void WorldBounds(out float minX, out float minZ, out float maxX, out float maxZ)
        {
            minX = minZ = float.MaxValue;
            maxX = maxZ = float.MinValue;
            for (int i = 0; i < SegmentCount; i++)
            {
                SnowObb o = i == 0 ? S0 : (i == 1 ? S1 : S2);
                float ex = System.Math.Abs(o.RightX) * o.HalfWidthM + System.Math.Abs(o.ForwardX) * o.HalfDepthM;
                float ez = System.Math.Abs(o.RightZ) * o.HalfWidthM + System.Math.Abs(o.ForwardZ) * o.HalfDepthM;
                if (o.CenterX - ex < minX) minX = o.CenterX - ex;
                if (o.CenterZ - ez < minZ) minZ = o.CenterZ - ez;
                if (o.CenterX + ex > maxX) maxX = o.CenterX + ex;
                if (o.CenterZ + ez > maxZ) maxZ = o.CenterZ + ez;
            }
        }
    }

    /// <summary>
    /// <b>안식각 이완. 이 파일 하나가 더미도 능선도 둔덕도 만든다.</b> 나머지는 전부 배관이다.
    ///
    /// v7 은 더미의 모양을 알고 있었다 — 해석적 능선을 정의하고 부피의 단조성으로 높이를 풀어
    /// 매 프레임 다시 방출했다. 이쪽은 모른다. 아는 것은 이웃한 두 셀의 높이차가 안식각을 넘으면
    /// 초과분이 낮은 쪽으로 흐른다는 규칙 하나뿐이다.
    ///
    /// 보존의 전부는 <b>준 양의 합을 뺀다</b>는 것이다. 계산한 양이 아니라. 정수 나눗셈의 나머지는
    /// 자연히 출발 셀에 남고, 그래서 델타의 합이 정확히 0 이 된다. v7 의 누수는 정확히 이 자리에서,
    /// 예측한 양을 원장에서 차감하고 실제로는 다른 양을 필드에 놓으면서 생겼다.
    /// </summary>
    public static class SnowReposeRelax
    {

        /// <summary>
        /// 낙차 한계 · 응집력 · 분모는 전부 <see cref="SnowMaterialCpu"/> 에서 온다.
        /// 분모 8 이 임의의 숫자가 아니라 8이웃 명시적 확산의 안정 한계라는 것,
        /// 그리고 절반을 넘겼을 때 한계 순환에 빠졌다는 것은 그 구조체의 주석에 적어뒀다.
        /// </summary>
        private const int NeighbourCount = 8;

        private static readonly int[] NX = { 1, -1, 0, 0, 1, 1, -1, -1 };
        private static readonly int[] NZ = { 0, 0, 1, -1, 1, -1, 1, -1 };

        [System.ThreadStatic] private static int[] _excess;
        [System.ThreadStatic] private static int[] _nIndex;
        [System.ThreadStatic] private static List<int> _touched;

        /// <summary>
        /// 활성 청크의 셀을 한 번 이완한다. 움직인 총량(mm)을 돌려주고, 천장·바닥에서 잘려나간
        /// 양을 <paramref name="clampedMm"/> 로 보고한다 — 0 이 아니면 그만큼이 장부 없이 사라졌다.
        ///
        /// 두 패스다. 1패스가 델타를 채우고 2패스가 반영하므로 <b>청크 순회 순서가 결과를 바꾸지
        /// 못한다</b>. 델타를 건드린 셀은 비활성 청크에 있을 수도 있어서(경계를 넘어 흘러간 눈)
        /// 2패스는 청크가 아니라 건드린 셀 목록을 돈다. 그 셀들의 청크는 깨운다 — 그것이 dirty
        /// 꼬리가 자라는 경로다.
        /// </summary>
        public static long Iterate(SnowHeightFieldCpu field, IReadOnlyList<int> chunks,
                                   in SnowRelaxBarrier barrier, in SnowMaterialCpu material,
                                   out long clampedMm)
        {
            var geo = field.Geo;
            var height = field.HeightMm;
            var delta = field.DeltaMm;

            // <b>이완은 깊이가 아니라 표면 높이를 본다.</b> 바닥이 상수 0 이던 동안에는 두 표현이
            // 같았고, 경사가 들어오는 순간 갈린다 - 깊이로 비교하면 램프 위 눈이 아래쪽 셀보다
            // "높지 않다"고 판정되어 중력을 무시하고 평평하게 앉는다.
            var ground = field.Ground;
            ushort[] floor = ground?.FloorMm;
            byte[] snowable = ground?.Coverage;
            clampedMm = 0;

            // 배리어의 셀 AABB 를 한 번만 구한다. 이게 없으면 이웃 판정마다 월드 변환이 돌아서
            // 스텝당 수백만 번이 되고, 그것만으로 프레임 예산이 날아간다. 블레이드 상자는
            // 19 x 3 셀 남짓이라 대부분의 이웃은 정수 비교 네 번에 걸러진다.
            int barX0 = 1, barZ0 = 1, barX1 = 0, barZ1 = 0;     // 기본값은 빈 사각형
            if (barrier.Active && barrier.SegmentCount > 0)
            {
                barrier.WorldBounds(out float bMinX, out float bMinZ, out float bMaxX, out float bMaxZ);
                if (!geo.TryWorldRectToCellRect(bMinX, bMinZ, bMaxX, bMaxZ,
                                                out barX0, out barZ0, out barX1, out barZ1))
                {
                    barX0 = 1; barZ0 = 1; barX1 = 0; barZ1 = 0;
                }
            }

            int dropOrtho = material.MaxDropOrthoMm;
            int dropDiag = material.MaxDropDiagMm;
            int cohesion = material.CohesionMm;
            int denom = material.RelaxDenominator;

            if (_excess == null)
            {
                _excess = new int[NeighbourCount];
                _nIndex = new int[NeighbourCount];
                _touched = new List<int>(1 << 16);
            }
            var touched = _touched;
            touched.Clear();

            // ---- 1패스: 델타를 채운다 -------------------------------------------------
            for (int k = 0; k < chunks.Count; k++)
            {
                geo.ChunkCellBounds(chunks[k], out int bx0, out int bz0, out int bx1, out int bz1);

                for (int cz = bz0; cz <= bz1; cz++)
                for (int cx = bx0; cx <= bx1; cx++)
                {
                    int ci = geo.CellIndex(cx, cz);
                    int h = height[ci];
                    if (h == 0) continue;
                    int fH = floor == null ? 0 : floor[ci];

                    int n = 0;
                    for (int d = 0; d < 8; d++)
                    {
                        int nx = cx + NX[d];
                        int nz = cz + NZ[d];
                        if (nx < 0 || nx >= geo.ResX || nz < 0 || nz >= geo.ResZ) continue;

                        if (nx >= barX0 && nx <= barX1 && nz >= barZ0 && nz <= barZ1)
                        {
                            geo.CellCenterWorld(nx, nz, out float wx, out float wz);
                            if (barrier.Contains(wx, wz)) continue;      // 블레이드는 못 받는다
                        }

                        int ni = geo.CellIndex(nx, nz);

                        // 눈이 불가능한 셀은 <b>받지도 않는다</b>. 배리어와 같은 취급이다 — 흘려보내면
                        // 용량 0 에 걸려 잘리고, 그 잘린 양은 원장에서 설명되지 않는 손실이 된다.
                        if (snowable != null && snowable[ni] == 0) continue;

                        int drop = d < 4 ? dropOrtho : dropDiag;

                        // <b>지형 자체의 낙차는 눈을 흐르게 하지 않는다.</b> 램프 위에 균일하게 깔린
                        // 눈은 안식각으로 무너질 이유가 없다(스펙 §5) — 무너져야 하는 것은 지형보다
                        // 더 쌓인 몫뿐이다. 그래서 허용 낙차를 max(안식각, 지형 낙차) 로 두고, 그것을
                        // 정리하면 아래 한 줄이 된다. 바닥이 평평하면(floorDrop = 0) 이전 식과 동일하다.
                        int floorDrop = floor == null ? 0 : fH - floor[ni];
                        int e = h - height[ni] + (floorDrop < drop ? floorDrop : drop) - drop;

                        // 응집력을 넘기 전에는 한 밀리미터도 안 움직인다. 정지 마찰이다.
                        if (e <= cohesion) continue;

                        _excess[n] = e;
                        _nIndex[n] = ni;
                        n++;
                    }

                    if (n == 0) continue;

                    int given = 0;
                    for (int j = 0; j < n; j++)
                    {
                        int give = _excess[j] / denom;
                        if (give <= 0) continue;
                        if (given + give > h) give = h - given;   // 가진 것보다 많이 줄 수는 없다
                        if (give <= 0) break;

                        if (delta[_nIndex[j]] == 0) touched.Add(_nIndex[j]);
                        delta[_nIndex[j]] += give;
                        given += give;
                    }
                    if (given == 0) continue;

                    if (delta[ci] == 0) touched.Add(ci);
                    delta[ci] -= given;                 // 준 양의 합. 계산한 양이 아니라.
                }
            }

            // ---- 2패스: 반영한다 -------------------------------------------------------
            long moved = 0;
            for (int t = 0; t < touched.Count; t++)
            {
                int ci = touched[t];
                int d = delta[ci];
                if (d == 0) continue;
                int applied = field.ApplyDeltaAt(ci);
                if (applied > 0) moved += applied;

                // 델타의 합은 정확히 0 이므로, 반영량이 델타와 다르면 그 차이는 천장이나 바닥에서
                // 잘려나간 것이다. 잘린 양을 장부에 싣지 않으면 그것이 곧 누수다.
                clampedMm += d - applied;

                field.WakeChunk(geo.ChunkIndex(geo.ChunkOfCellX(ci % geo.ResX), geo.ChunkOfCellZ(ci / geo.ResX)));
            }

            // touched 에 같은 셀이 두 번 들어갈 수 없으므로(델타가 0 일 때만 추가) 여기서 델타는 전부 0 이다.
            return moved;
        }
    }
}
