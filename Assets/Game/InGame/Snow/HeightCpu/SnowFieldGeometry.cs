namespace PPack
{
    /// <summary>
    /// 맵 · 셀 · 청크 좌표계 사이의 변환 전부. <b>상태가 없다</b> — 필드 하나에 대한 읽기 전용 기하이고,
    /// 그래서 전수 테스트가 가능하다. 이 계열에서 가장 흔한 버그가 좌표 변환 오프바이원인데,
    /// 상태가 없으면 대각선 전체를 왕복시켜 볼 수 있다.
    ///
    /// 셀 크기는 컴파일 상수이고 맵 크기는 생성자 인자다. 이 비대칭이 설계 결정이다:
    /// relax 는 반복당 1셀씩 전파하므로 셀 크기를 바꾸면 안식각 낙차 · 반복수 · 비용이 전부 따라
    /// 움직이지만, 맵 크기는 메모리와 텍스처만 바꾸고 <b>스텝당 비용은 건드리지 않는다</b>.
    /// 스텝당 비용은 활성 반경에만 의존한다.
    /// </summary>
    public sealed class SnowFieldGeometry
    {
        /// <summary>미터 단위 셀 한 변. v7 테스트 스테이지와 같은 값이라 v7 실측치가 그대로 전이된다.</summary>
        public const float CellSizeM = 0.125f;

        /// <summary>밀리미터 단위 셀 한 변. 안식각 낙차가 이 값에서 나온다.</summary>
        public const int CellSizeMm = 125;

        /// <summary>청크 한 변의 셀 수. 2 x 2 m.</summary>
        public const int ChunkCells = 16;

        /// <summary>
        /// PPack SnowField 의 델타 동기화 블록 한 변. 청크 변이 이 값의 배수여야 블록이 청크 안에
        /// 정확히 중첩된다 — 지금은 청크당 4개. 이것이 "리플리케이션 자리를 비워둔다"의 실질이다.
        /// </summary>
        public const int NetworkBlockCells = 8;

        public readonly int ResX;
        public readonly int ResZ;
        public readonly int ChunksX;
        public readonly int ChunksZ;
        public readonly int CellCount;
        public readonly int ChunkCount;

        /// <summary>청크 격자를 덮는 2의 거듭제곱 한 변. 격자 밖 노드는 영구 비활성이다.</summary>
        public readonly int QuadtreeSide;

        public readonly int QuadtreeDepth;

        public readonly float OriginXM;
        public readonly float OriginZM;

        /// <summary>
        /// 바닥 높이의 기준(월드 Y). <see cref="SnowGroundFieldCpu"/> 의 셀당 바닥 높이가 이 값 위의
        /// 밀리미터이므로, 여기가 <c>ushort</c> 창(0~65.5 m)이 앉는 자리다.
        ///
        /// <para><b>왜 필드에 Y 가 필요한가</b> — 눈 깊이만으로는 경사 위의 눈을 표현할 수 없다.
        /// 2026-08-24 까지 지면은 마처의 <c>_GroundY</c> 스칼라 하나(0)였고, 그래서 램프는 씬에
        /// 있어도 눈이 올라가지 못했다. 격자는 여전히 XZ 이고 <b>깊이를 월드 Y 방향으로 잰다</b> —
        /// 대가는 셀이 표면에서 1/cos θ 만큼 늘어나는 것이다(45°에서 12.5 → 17.7 cm).
        /// 45° 를 넘는 램프·지붕은 자기 회전을 가진 구역으로 떼야 하고, 그것은 아직 없다.</para>
        /// </summary>
        public readonly float OriginYM;

        public SnowFieldGeometry(float mapSizeXM, float mapSizeZM, float originXM, float originZM)
            : this(mapSizeXM, mapSizeZM, originXM, originZM, 0f) { }

        public SnowFieldGeometry(float mapSizeXM, float mapSizeZM, float originXM, float originZM,
                                 float originYM)
        {
            OriginXM = originXM;
            OriginZM = originZM;
            OriginYM = originYM;

            ResX = RoundUpToChunk(CeilCells(mapSizeXM));
            ResZ = RoundUpToChunk(CeilCells(mapSizeZM));
            ChunksX = ResX / ChunkCells;
            ChunksZ = ResZ / ChunkCells;
            CellCount = ResX * ResZ;
            ChunkCount = ChunksX * ChunksZ;

            int side = 1;
            int depth = 0;
            int need = ChunksX > ChunksZ ? ChunksX : ChunksZ;
            while (side < need)
            {
                side <<= 1;
                depth++;
            }
            QuadtreeSide = side;
            QuadtreeDepth = depth;
        }

        private static int CeilCells(float sizeM)
        {
            int n = (int)System.Math.Ceiling(sizeM / (double)CellSizeM - 1e-6);
            return n < ChunkCells ? ChunkCells : n;
        }

        private static int RoundUpToChunk(int cells)
        {
            int r = cells % ChunkCells;
            return r == 0 ? cells : cells + (ChunkCells - r);
        }

        public int CellIndex(int cx, int cz) => cz * ResX + cx;

        public int ChunkIndex(int chx, int chz) => chz * ChunksX + chx;

        public int ChunkOfCellX(int cx) => cx / ChunkCells;

        public int ChunkOfCellZ(int cz) => cz / ChunkCells;

        public bool InBounds(int cx, int cz) => cx >= 0 && cx < ResX && cz >= 0 && cz < ResZ;

        /// <summary>
        /// 월드 XZ 를 셀로. 필드 밖이면 false 를 돌려주되 cx/cz 에는 클램프하지 않은 값이 들어간다.
        /// 바닥 방향 나눗셈을 쓴다 — 음수 좌표에서 (int) 캐스트는 0 쪽으로 잘려서 원점 근처
        /// 한 줄이 두 번 계산된다.
        /// </summary>
        public bool TryWorldToCell(float wxM, float wzM, out int cx, out int cz)
        {
            double fx = (wxM - OriginXM) / (double)CellSizeM;
            double fz = (wzM - OriginZM) / (double)CellSizeM;
            cx = (int)System.Math.Floor(fx);
            cz = (int)System.Math.Floor(fz);
            return cx >= 0 && cx < ResX && cz >= 0 && cz < ResZ;
        }

        public void CellCenterWorld(int cx, int cz, out float wxM, out float wzM)
        {
            wxM = OriginXM + (cx + 0.5f) * CellSizeM;
            wzM = OriginZM + (cz + 0.5f) * CellSizeM;
        }

        /// <summary>청크가 덮는 셀 범위. 양 끝을 <b>포함</b>한다.</summary>
        public void ChunkCellBounds(int chunkIndex, out int cx0, out int cz0, out int cx1, out int cz1)
        {
            int chx = chunkIndex % ChunksX;
            int chz = chunkIndex / ChunksX;
            cx0 = chx * ChunkCells;
            cz0 = chz * ChunkCells;
            cx1 = cx0 + ChunkCells - 1;
            cz1 = cz0 + ChunkCells - 1;
        }

        /// <summary>월드 사각형을 필드에 클램프한 셀 사각형으로. 겹치는 부분이 전혀 없으면 false.</summary>
        public bool TryWorldRectToCellRect(float minXM, float minZM, float maxXM, float maxZM,
                                           out int cx0, out int cz0, out int cx1, out int cz1)
        {
            cx0 = (int)System.Math.Floor((minXM - OriginXM) / (double)CellSizeM);
            cz0 = (int)System.Math.Floor((minZM - OriginZM) / (double)CellSizeM);
            cx1 = (int)System.Math.Floor((maxXM - OriginXM) / (double)CellSizeM);
            cz1 = (int)System.Math.Floor((maxZM - OriginZM) / (double)CellSizeM);

            if (cx1 < 0 || cz1 < 0 || cx0 >= ResX || cz0 >= ResZ) return false;

            if (cx0 < 0) cx0 = 0;
            if (cz0 < 0) cz0 = 0;
            if (cx1 >= ResX) cx1 = ResX - 1;
            if (cz1 >= ResZ) cz1 = ResZ - 1;
            return true;
        }
    }
}
