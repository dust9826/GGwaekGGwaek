using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// <b>레이마칭의 빈 공간 건너뛰기 구조.</b> 필드를 블록당 한 텍셀로 줄이되 <b>최대값</b>을 취하고
    /// (평균은 상한이 아니다), 그 결과를 다일레이트해서 각 텍셀이 <b>자기 주변 안전반경 안의 최대
    /// 높이</b>를 담게 한다.
    ///
    /// 왜 필요한가 — v7 이 균일 스텝으로 마칭했을 때 <b>57,600 광선 중 324개(0.56%)가 표면을 뚫고
    /// 지나갔다.</b> 스텝 예산을 코드 길이에 나누면 스텝이 요철보다 커지기 때문이고, 화면에 반점으로
    /// 나타난다. 상한이 있으면
    /// <code>advance = min(안전반경, (ray.y - coarseMax) / |rd.y|)</code>
    /// 로 전진할 수 있고, 건너뛴 구간 <b>전체</b>에 대해 상한이 성립하며 광선의 y 는 단조 감소하므로
    /// 표면을 지나쳐 뛰는 것이 <b>구조적으로 불가능</b>해진다 — 확률이 낮은 게 아니라 불가능하다.
    ///
    /// <b>상한을 깨는 것이 이 방식의 유일한 치명적 실수다.</b> 표면에 무엇을 더하든(디테일 노이즈,
    /// fillet, 로브) 그 최대 들림값을 상한에 포함시켜야 한다. v7 에서 fillet 이 평지를 2.5 cm 무조건
    /// 들어올리는 버그가 있었고, 그것이 상한을 깨자 표면에 구멍이 뚫렸다.
    ///
    /// 이 클래스는 권위 필드를 <b>읽기만</b> 한다. 렌더 타입을 참조하지 않으므로 코어 어셈블리에 있고,
    /// 헤드리스 서버에서는 아무도 부르지 않으면 그만이다.
    /// </summary>
    public sealed class SnowCoarseMaxCpu
    {
        /// <summary>한 coarse 텍셀이 덮는 셀 수. 8 이면 1 x 1 m.</summary>
        public const int BlockCells = 8;

        /// <summary>다일레이트 반경, coarse 텍셀 단위. 1 이면 안전반경이 블록 하나만큼 넓어진다.</summary>
        public const int DilateTexels = 1;

        private readonly SnowHeightFieldCpu _field;

        /// <summary>
        /// 구운 로브. 있으면 블록 최대값에 <b>그 블록의 실제 최대 lift</b> 를 더한다.
        ///
        /// 일괄 +r 을 쓰면 안 되는 이유가 정확히 하나 있다. 긁힌 바닥은 게이트 때문에 로브가 0 인데
        /// 상한만 +r 만큼 떠 있으면, 스치는 광선이 그 틈을 고정 스텝으로 기어내려가다 스텝 예산을
        /// 소진하고 discard 된다 - 치운 차선에 검은 줄무늬가 생긴다. v7 이 "블록마다 구운 텍셀을
        /// max 해서 상한이 blanket +r 이 아니라 ACTUAL lift 가 되게 한다" 고 적어둔 것이 이것이다.
        /// </summary>
        private readonly SnowSurfaceBakeCpu _lump;
        private readonly ushort[] _blockMax;      // 다일레이트 전
        private readonly ushort[] _dilated;       // 최종. 렌더러가 이걸 올린다

        public int ResX { get; }
        public int ResZ { get; }

        /// <summary>다일레이트된 상한. 텍셀당 밀리미터.</summary>
        public ushort[] MaxMm => _dilated;

        /// <summary>
        /// 한 텍셀의 상한이 유효한 월드 반경. 마칭이 한 번에 이만큼까지 전진해도 안전하다.
        /// 다일레이트 반경이 0 이면 블록 안에서만 유효하므로 그 절반이다.
        /// </summary>
        public float SafeRadiusM
            => (DilateTexels * BlockCells + BlockCells * 0.5f) * SnowFieldGeometry.CellSizeM;

        public SnowCoarseMaxCpu(SnowHeightFieldCpu field, SnowSurfaceBakeCpu lump = null)
        {
            _field = field;
            _lump = lump;
            var geo = field.Geo;
            ResX = (geo.ResX + BlockCells - 1) / BlockCells;
            ResZ = (geo.ResZ + BlockCells - 1) / BlockCells;
            _blockMax = new ushort[ResX * ResZ];
            _dilated = new ushort[ResX * ResZ];
            RebuildAll();
        }

        public void RebuildAll()
        {
            for (int tz = 0; tz < ResZ; tz++)
            for (int tx = 0; tx < ResX; tx++)
                _blockMax[tz * ResX + tx] = BlockMaxOf(tx, tz);

            for (int tz = 0; tz < ResZ; tz++)
            for (int tx = 0; tx < ResX; tx++)
                _dilated[tz * ResX + tx] = DilateAt(tx, tz);
        }

        /// <summary>
        /// 변한 청크만 다시 굽는다. 블록이 셀 8개이고 청크가 16개이므로 청크 하나가 정확히
        /// coarse 텍셀 2 x 2 개를 덮는다 — 나눠 떨어지므로 경계 처리가 없다.
        /// </summary>
        public void RebuildChunks(IReadOnlyList<int> chunks)
        {
            var geo = _field.Geo;
            for (int k = 0; k < chunks.Count; k++)
            {
                geo.ChunkCellBounds(chunks[k], out int cx0, out int cz0, out int cx1, out int cz1);
                int tx0 = cx0 / BlockCells, tz0 = cz0 / BlockCells;
                int tx1 = cx1 / BlockCells, tz1 = cz1 / BlockCells;
                for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                    _blockMax[tz * ResX + tx] = BlockMaxOf(tx, tz);
            }

            // 다일레이트는 이웃까지 번지므로 반경만큼 넓혀서 다시 계산한다.
            for (int k = 0; k < chunks.Count; k++)
            {
                geo.ChunkCellBounds(chunks[k], out int cx0, out int cz0, out int cx1, out int cz1);
                int tx0 = cx0 / BlockCells - DilateTexels, tz0 = cz0 / BlockCells - DilateTexels;
                int tx1 = cx1 / BlockCells + DilateTexels, tz1 = cz1 / BlockCells + DilateTexels;
                if (tx0 < 0) tx0 = 0;
                if (tz0 < 0) tz0 = 0;
                if (tx1 >= ResX) tx1 = ResX - 1;
                if (tz1 >= ResZ) tz1 = ResZ - 1;
                for (int tz = tz0; tz <= tz1; tz++)
                for (int tx = tx0; tx <= tx1; tx++)
                    _dilated[tz * ResX + tx] = DilateAt(tx, tz);
            }
        }

        private ushort BlockMaxOf(int tx, int tz)
        {
            var geo = _field.Geo;
            int cx0 = tx * BlockCells, cz0 = tz * BlockCells;
            int cx1 = cx0 + BlockCells - 1, cz1 = cz0 + BlockCells - 1;
            if (cx1 >= geo.ResX) cx1 = geo.ResX - 1;
            if (cz1 >= geo.ResZ) cz1 = geo.ResZ - 1;

            int m = 0;
            var h = _field.HeightMm;
            for (int cz = cz0; cz <= cz1; cz++)
            {
                int row = cz * geo.ResX;
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int v = h[row + cx];
                    if (v > m) m = v;
                }
            }

            // 이 블록의 실제 최대 lift 를 더한다. 로브가 없는 곳에서는 0 이 더해지므로
            // 상한이 정확히 표면과 같아지고, 스치는 광선이 기어내려갈 틈이 남지 않는다.
            if (_lump != null)
            {
                int u = SnowSurfaceBakeCpu.Upsample;
                int lx0 = cx0 * u, lz0 = cz0 * u;
                int lx1 = (cx1 + 1) * u - 1, lz1 = (cz1 + 1) * u - 1;
                if (lx1 >= _lump.ResX) lx1 = _lump.ResX - 1;
                if (lz1 >= _lump.ResZ) lz1 = _lump.ResZ - 1;

                int liftMax = 0;
                int filletMax = 128;                 // 128 이 0 이다. 그 아래는 표면을 내리므로 무시한다
                var lift = _lump.Lift;
                var fil = _lump.Fillet;
                for (int lz = lz0; lz <= lz1; lz++)
                {
                    int row = lz * _lump.ResX;
                    for (int lx = lx0; lx <= lx1; lx++)
                    {
                        int v = lift[row + lx];
                        if (v > liftMax) liftMax = v;
                        int f = fil[row + lx];
                        if (f > filletMax) filletMax = f;
                    }
                }
                m += (int)(liftMax / 255f * _lump.RadiusM * 1000f + 0.999f);

                // fillet 은 부호가 있다. 표면을 내리는 쪽은 상한에 영향이 없으므로 양수만 더한다.
                // 이 한 줄을 빼면 둥근 어깨가 상한 위로 삐져나오는 자리에 구멍이 뚫린다.
                m += (int)((filletMax - 128) / 127f * _lump.FilletRangeM * 1000f + 0.999f);
            }

            if (m > ushort.MaxValue) m = ushort.MaxValue;
            return (ushort)m;
        }

        private ushort DilateAt(int tx, int tz)
        {
            int x0 = tx - DilateTexels, x1 = tx + DilateTexels;
            int z0 = tz - DilateTexels, z1 = tz + DilateTexels;
            if (x0 < 0) x0 = 0;
            if (z0 < 0) z0 = 0;
            if (x1 >= ResX) x1 = ResX - 1;
            if (z1 >= ResZ) z1 = ResZ - 1;

            int m = 0;
            for (int z = z0; z <= z1; z++)
            {
                int row = z * ResX;
                for (int x = x0; x <= x1; x++)
                {
                    int v = _blockMax[row + x];
                    if (v > m) m = v;
                }
            }
            return (ushort)m;
        }
    }
}
