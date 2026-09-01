namespace PPack
{
    /// <summary>한 스텝의 입력. <c>dt</c> 는 인자다 — 시뮬은 <c>Time</c> 을 모른다.</summary>
    public struct SnowPlowStepInput
    {
        public SnowBladePose Prev;
        public SnowBladePose Now;
        public bool BladeDown;
        public float SignedSpeedMps;
        public float DtSeconds;
    }

    /// <summary>한 스텝의 결과. <see cref="ConservationErrorMm"/> 과 <see cref="UnplacedMm"/> 는 항상 0 이어야 한다.</summary>
    public struct SnowPlowStepStats
    {
        public int ActiveChunks;
        public int DroppedByCap;
        public int Segments;
        public int RelaxIterations;
        public long CellsVisited;
        public long CutMm;
        public long UnplacedMm;
        public long ClampedMm;
        public long RelaxMovedMm;
        public long TotalBeforeMm;
        public long TotalAfterMm;

        public long ConservationErrorMm => TotalAfterMm - TotalBeforeMm;
    }

    /// <summary>
    /// 네 단계를 묶는다. <b>요구된 세 거동이 전부 여기서 파생된다</b> — 전용 코드가 하나도 없다.
    ///
    /// <list type="bullet">
    /// <item><b>밀면 앞에 쌓인다</b> — CUT 이 매 스텝 처녀설을 걷어 DEPOSIT 이 앞에 붓고,
    /// 블레이드가 전진하면 다음 스텝의 CUT 박스가 그 자리를 다시 덮으므로 더미가 블레이드 앞을
    /// 타고 이동하면서 새 처녀설만큼 누적된다.</item>
    /// <item><b>넘치면 좌우로 퍼진다</b> — 더미가 안식각 원뿔을 넘으면 RELAX 가 옆으로 흘리는데,
    /// 벽이 블레이드 폭에서 끝나므로 끝단 바깥으로 돌아나가 뒤로 감긴다. 그것이 둔덕이다.</item>
    /// <item><b>후진하거나 블레이드를 들면 그 자리에 남는다</b> — CUT 도 벽도 걸리지 않는다.
    /// 그래서 dump 동사가 필요 없다.</item>
    /// </list>
    ///
    /// 아래 상수는 성능 노브가 아니라 <b>시뮬레이션 규칙</b>이다. 피어마다 다르면 필드가 갈라진다.
    /// </summary>
    public sealed class SnowPlowStepCpu
    {
        /// <summary>블레이드 중심에서 이 반경 안의 청크가 무조건 활성이다.</summary>
        public const float ActiveRadiusM = 10f;

        /// <summary>스텝당 시뮬할 청크의 상한. 빌드 상수다 — 프레임 시간에 따라 움직이면 안 된다.</summary>
        public const int ActiveChunkCap = 512;

        public const int MaxSegments = 8;

        private readonly SnowHeightFieldCpu _field;
        private readonly SnowChunkQuadtree _tree;
        private readonly SnowActiveSet _active = new SnowActiveSet(ActiveChunkCap);
        private SnowBladeShape _shape;
        private SnowMaterialCpu _material = SnowMaterialCpu.Default;

        public SnowPlowStepCpu(SnowHeightFieldCpu field)
            : this(field, SnowBladeShape.Default) { }

        public SnowPlowStepCpu(SnowHeightFieldCpu field, SnowBladeShape shape)
        {
            _field = field;
            _shape = shape;
            _tree = new SnowChunkQuadtree(field.Geo);
            field.AttachDirtyIndex(_tree);
        }

        public SnowChunkQuadtree Tree => _tree;

        /// <summary>
        /// 직전 스텝이 실제로 시뮬한 청크. 표현 데이터(coarse-max 상한, 텍스처 부분 업로드)를
        /// 다시 굽는 범위가 정확히 이것이다 — 렌더가 자기 힘으로 "무엇이 변했나"를 다시 알아낼
        /// 이유가 없다.
        /// </summary>
        public System.Collections.Generic.IReadOnlyList<int> ActiveChunks => _active.Chunks;

        /// <summary>이번 스텝에 실제로 무언가 움직인 청크. 표현 데이터를 다시 굽는 범위가 이것이다.</summary>
        public System.Collections.Generic.IReadOnlyList<int> ChangedChunks => _field.ChangedChunks;

        /// <summary>
        /// 눈의 물성. 런타임에 바꿔도 안전하다 — 매 스텝 여기서 낙차·응집력·분모를 다시 읽는다.
        /// 세터가 <see cref="SnowMaterialCpu.Resolved"/> 를 부르므로 안식각만 바꿔도 낙차가 따라온다.
        /// </summary>
        public SnowMaterialCpu Material
        {
            get => _material;
            set => _material = value.Resolved();
        }

        /// <summary>
        /// 블레이드 상자. 런타임에 바꿔도 안전하다 — 컷도 배리어도 퇴적 밴드도 매 스텝 이 값에서
        /// 다시 파생되고, 캐시된 사본이 없다.
        ///
        /// 폭에는 실질적인 하한이 있다. 0.125 m 셀에서 2.30 m 는 18.4 셀이고, 0.5 m 아래로 내리면
        /// 4 셀이 되어 배리어가 한두 셀만 막게 된다 — 그러면 더미가 벽을 돌아 뒤로 새기 시작한다.
        /// 위쪽으로는 활성 반경 10 m 가 둔덕까지 덮어야 하므로 폭 + 여유가 그 안에 들어와야 한다.
        /// </summary>
        public SnowBladeShape Shape
        {
            get => _shape;
            set => _shape = value;
        }

        public SnowPlowStepStats Step(in SnowPlowStepInput input)
        {
            var geo = _field.Geo;
            var stats = new SnowPlowStepStats { TotalBeforeMm = _field.TotalHeightMm };
            _field.BeginStep();

            // ---- 활성 집합. 블레이드 주변 사각형 U 아직 잠들지 않은 청크 -----------------
            if (!geo.TryWorldRectToCellRect(input.Now.CenterX - ActiveRadiusM, input.Now.CenterZ - ActiveRadiusM,
                                            input.Now.CenterX + ActiveRadiusM, input.Now.CenterZ + ActiveRadiusM,
                                            out int rx0, out int rz0, out int rx1, out int rz1))
            {
                rx0 = 1; rz0 = 1; rx1 = 0; rz1 = 0;      // 블레이드가 필드 밖이면 사각형은 비어 있다
            }

            _active.Build(_tree, rx0, rz0, rx1, rz1, ActiveChunkCap);
            stats.ActiveChunks = _active.Chunks.Count;
            stats.DroppedByCap = _active.DroppedByCap;
            stats.CellsVisited = (long)_active.Chunks.Count
                               * SnowFieldGeometry.ChunkCells * SnowFieldGeometry.ChunkCells;

            // ---- CUT 과 DEPOSIT. 전진하며 블레이드를 내렸을 때만 -------------------------
            bool ploughing = input.BladeDown && input.SignedSpeedMps > 0f;
            if (ploughing)
            {
                float dx = input.Now.CenterX - input.Prev.CenterX;
                float dz = input.Now.CenterZ - input.Prev.CenterZ;
                float travel = (float)System.Math.Sqrt(dx * dx + dz * dz);

                // 회전도 세어야 한다. 중심이 거의 안 움직여도 블레이드가 돌면 <b>끝단</b>은 크게
                // 움직이고, 그 사이 호를 안 훑으면 안 깎인 쐐기가 남는다 - 선회 구간에서만
                // 더미 면에 골이 생기는 이유가 이것이다. v7 이 "회전하는 블레이드의 중심은 호를
                // 그리는데 두 끝 자세를 잇는 직선 현은 모서리를 자른다" 고 적어둔 그것이다.
                float cross = input.Prev.ForwardX * input.Now.ForwardZ
                            - input.Prev.ForwardZ * input.Now.ForwardX;
                float dot = input.Prev.ForwardX * input.Now.ForwardX
                          + input.Prev.ForwardZ * input.Now.ForwardZ;
                float yawRad = (float)System.Math.Atan2(System.Math.Abs(cross), dot);
                float tipTravel = yawRad * (_shape.HalfWidthM + _shape.WingLengthM);

                float span = travel > tipTravel ? travel : tipTravel;

                // 블레이드 두께의 절반씩 겹치게 훑어야 빠른 주행에서 구멍이 안 난다.
                int segments = 1 + (int)(span / System.Math.Max(_shape.HalfDepthM, 1e-4f));
                if (segments > MaxSegments) segments = MaxSegments;
                stats.Segments = segments;

                _field.BeginCutPhase();
                stats.CutMm = SnowBladeSweep.Cut(_field, input.Prev, input.Now, _shape, segments,
                                                 _material.CutResidueMm);
                if (stats.CutMm > 0)
                    stats.UnplacedMm = SnowBladeSweep.Deposit(_field, input.Now, _shape,
                                                              _material.DepositBandDepthM,
                                                              _material.DepositSideMarginM, stats.CutMm,
                                                              _material.MaxPileHeightMm,
                                                              _material.DepositSpreadRings);
            }

            // ---- RELAX. 블레이드는 벽이다 ----------------------------------------------
            // 절삭 구간을 닫는다. 아래의 이완이 바꾸는 셀은 보내지 않는다 - 각 피어가 스스로 만든다.
            _field.EndCutPhase();

            var barrier = ploughing ? SnowBladeSweep.MakeBarrier(input.Now, _shape) : default;
            int iterations = _material.RelaxIterations;
            stats.RelaxIterations = iterations;
            for (int i = 0; i < iterations; i++)
            {
                stats.RelaxMovedMm += SnowReposeRelax.Iterate(_field, _active.Chunks, barrier, _material,
                                                              out long clamped);
                stats.ClampedMm += clamped;
            }

            // ---- 조용한 청크를 재운다 ---------------------------------------------------
            for (int i = 0; i < _active.Chunks.Count; i++) _field.RestChunk(_active.Chunks[i]);

            stats.TotalAfterMm = _field.TotalHeightMm;
            return stats;
        }

    }
}
