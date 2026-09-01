using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 청크 격자 위에 얹힌 쿼드트리. dirty 비트를 부모로 상향 전파해서,
    /// <b>흩어져 있는 잠 안 든 청크를 전수 스캔 없이 모은다.</b>
    ///
    /// 정직하게 — <see cref="QueryRect"/> 는 트리를 타지 않는다. "이 사각형 안의 청크를 전부
    /// 내놓아라" 라는 질의는 가지치기할 것이 없어서 트리 순회가 순수 오버헤드이고, 직접 사각형
    /// 루프가 더 빠르면서 더 짧다. 트리가 실제로 값을 하는 것은 <see cref="QueryDirty"/> 뿐이다:
    /// 청크가 4,096개(128 m 맵)이고 맵을 512 m 로 키우면 65,536개가 되는데, 그 중 조용한 가지를
    /// 루트에서 잘라낸다. v7 이 잰 relax 활동 창이 전체의 0.55~2.33% 였으므로 잘리는 쪽이
    /// 압도적으로 많다.
    /// </summary>
    public sealed class SnowChunkQuadtree
    {
        private readonly int _chunksX;
        private readonly int _chunksZ;
        private readonly int _side;
        private readonly int _depth;

        /// <summary>_dirty[0] 이 잎(패딩된 side x side), _dirty[_depth] 가 루트 1개.</summary>
        private readonly bool[][] _dirty;

        private int _dirtyCount;

        public SnowChunkQuadtree(SnowFieldGeometry geo)
        {
            _chunksX = geo.ChunksX;
            _chunksZ = geo.ChunksZ;
            _side = geo.QuadtreeSide;
            _depth = geo.QuadtreeDepth;

            _dirty = new bool[_depth + 1][];
            for (int level = 0; level <= _depth; level++)
            {
                int s = _side >> level;
                _dirty[level] = new bool[s * s];
            }
        }

        public int DirtyCount => _dirtyCount;

        public bool IsDirty(int chunkIndex)
        {
            int x = chunkIndex % _chunksX;
            int z = chunkIndex / _chunksX;
            return _dirty[0][z * _side + x];
        }

        /// <summary>잎에서 루트까지 켠다.</summary>
        public void MarkDirty(int chunkIndex)
        {
            int x = chunkIndex % _chunksX;
            int z = chunkIndex / _chunksX;
            if (!_dirty[0][z * _side + x]) _dirtyCount++;

            for (int level = 0; level <= _depth; level++)
            {
                int s = _side >> level;
                _dirty[level][z * s + x] = true;
                x >>= 1;
                z >>= 1;
            }
        }

        /// <summary>잎을 끄고, 형제 넷이 전부 깨끗할 때만 부모를 끈다. 하나라도 더러우면 조상은 그대로다.</summary>
        public void ClearDirty(int chunkIndex)
        {
            int x = chunkIndex % _chunksX;
            int z = chunkIndex / _chunksX;
            if (!_dirty[0][z * _side + x]) return;

            _dirty[0][z * _side + x] = false;
            _dirtyCount--;

            for (int level = 1; level <= _depth; level++)
            {
                int childSide = _side >> (level - 1);
                int px = x >> 1;
                int pz = z >> 1;

                bool any = false;
                for (int dz = 0; dz < 2 && !any; dz++)
                for (int dx = 0; dx < 2 && !any; dx++)
                {
                    int cxi = (px << 1) + dx;
                    int czi = (pz << 1) + dz;
                    if (cxi < childSide && czi < childSide && _dirty[level - 1][czi * childSide + cxi]) any = true;
                }
                if (any) break;                      // 부모가 더럽게 남으면 조상도 전부 더럽다

                int parentSide = _side >> level;
                _dirty[level][pz * parentSide + px] = false;
                x = px;
                z = pz;
            }
        }

        /// <summary>
        /// 더러운 청크 전부. <b>청크 인덱스 오름차순</b>으로 돌려준다 — DFS 는 모튼 순서라
        /// 정렬이 필요하고, 활성 집합이 수백 개라 정렬 비용은 무의미하다. 순서가 고정이어야
        /// 피어 간 결정론이 성립한다.
        /// </summary>
        public void QueryDirty(List<int> results)
        {
            results.Clear();
            if (_dirtyCount == 0) return;
            Descend(_depth, 0, 0, results);
            results.Sort();
        }

        private void Descend(int level, int x, int z, List<int> results)
        {
            int s = _side >> level;
            if (x >= s || z >= s) return;
            if (!_dirty[level][z * s + x]) return;

            if (level == 0)
            {
                if (x < _chunksX && z < _chunksZ) results.Add(z * _chunksX + x);
                return;
            }

            int cl = level - 1;
            Descend(cl, x << 1,       z << 1,       results);
            Descend(cl, (x << 1) + 1, z << 1,       results);
            Descend(cl, x << 1,       (z << 1) + 1, results);
            Descend(cl, (x << 1) + 1, (z << 1) + 1, results);
        }

        /// <summary>
        /// 셀 사각형(양 끝 포함)이 닿는 청크 전부, 오름차순. 트리를 타지 않는다 — 위 클래스 주석 참고.
        /// </summary>
        public void QueryRect(int cx0, int cz0, int cx1, int cz1, List<int> results)
        {
            results.Clear();
            if (cx1 < 0 || cz1 < 0) return;
            if (cx0 < 0) cx0 = 0;
            if (cz0 < 0) cz0 = 0;

            int chx0 = cx0 / SnowFieldGeometry.ChunkCells;
            int chz0 = cz0 / SnowFieldGeometry.ChunkCells;
            int chx1 = cx1 / SnowFieldGeometry.ChunkCells;
            int chz1 = cz1 / SnowFieldGeometry.ChunkCells;

            if (chx1 >= _chunksX) chx1 = _chunksX - 1;
            if (chz1 >= _chunksZ) chz1 = _chunksZ - 1;
            if (chx0 > chx1 || chz0 > chz1) return;

            for (int chz = chz0; chz <= chz1; chz++)
            for (int chx = chx0; chx <= chx1; chx++)
                results.Add(chz * _chunksX + chx);
        }
    }
}
