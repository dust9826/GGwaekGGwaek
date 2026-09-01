namespace PPack
{
    /// <summary>
    /// 굴리면 커지는 눈덩이의 <b>권위</b>. 순수 C# 이고 <c>UnityEngine</c> 을 참조하지 않는다 —
    /// 데디 서버(<c>-batchmode -nographics</c>)에서 그대로 돈다.
    ///
    /// <para><b>새 시뮬이 아니다.</b> 블레이드는 걷어서(<see cref="SnowBladeSweep.Cut"/>) 곧바로 앞에
    /// 붓는다(<see cref="SnowBladeSweep.Deposit"/>). 눈덩이는 <b>같은 걷기를 하고 붓지 않는다</b> —
    /// 그것이 차이의 전부다. 그래서 여기에는 걷은 양을 들고 있는 저울과 그 양에서 반지름을 내는
    /// 식만 있고, 컷·퇴적·안식각은 한 줄도 새로 쓰지 않았다.</para>
    ///
    /// <para><b>질량 단위는 필드와 같은 mm·셀 정수다.</b> 다만 생산 성장 가중치가 1보다 작으면
    /// 필드에서 걷은 양 중 그 비율만 공의 질량이 되고, 나머지는 압축·비산된 것으로 장부에서 제외한다.
    /// 가중치의 1 mm 미만 나머지는 누산해 매 스텝 반올림 편향이 쌓이지 않게 한다.</para>
    /// </summary>
    public sealed class SnowBallCpu
    {
        /// <summary>보이는 반지름의 상한. 초과 수확량은 압축 질량으로 계속 보관한다.</summary>
        public const float MaxRadiusM = 1.5f;

        /// <summary>성장 가중치의 정수 스케일. 네트워크와 CPU 계산에서 같은 반올림을 쓴다.</summary>
        public const int GrowthWeightScale = 1000;

        /// <summary>생산 프리팹이 수확량 중 실제 질량으로 가져오는 기본 비율(0.5).</summary>
        public const int DefaultGrowthWeightPermille = 500;

        /// <summary>
        /// 질량 0 인 공의 반지름. <b>0 이면 안 된다</b> — 접지면이 없으면 걷을 셀이 없고, 걷지 못하면
        /// 질량이 늘지 않아 영원히 0 이다. 눈덩이를 손으로 뭉쳐 시작한다는 뜻이기도 하다.
        /// </summary>
        public const float SeedRadiusM = 0.18f;

        /// <summary>한 스텝의 스윕 세그먼트 상한. 블레이드와 같은 값이라 빠른 굴림에서도 구멍이 안 난다.</summary>
        public const int MaxSegments = 8;

        /// <summary>셀 하나의 바닥 면적(m²). mm·셀 → m³ 환산의 유일한 상수다.</summary>
        public const float CellAreaM2 = SnowFieldGeometry.CellSizeM * SnowFieldGeometry.CellSizeM;

        private const float MmToM = 1e-3f;
        private const float FourThirdsPi = 4.18879032f;

        /// <summary>
        /// 지금 깎고 있는 눈. <b>바뀔 수 있다</b> — 공이 눈 상자에서 지면으로(또는 그 반대로) 굴러
        /// 넘어가면 <see cref="Rebind"/> 로 갈아 끼운다.
        ///
        /// <para>좌표계가 바뀌므로 호출자가 이전 중심을 버려야 한다 — 안 그러면 두 필드를 가로지르는
        /// 선을 훑는다.</para>
        /// </summary>
        private SnowHeightFieldCpu _field;

        private long _massMm;
        private readonly int _growthWeightPermille;
        private int _growthRemainderPermille;

        /// <param name="residueMm">지나간 셀에 남기는 눈. 실제 수확량과 보존량을 정한다.</param>
        /// <param name="growthWeightPermille">필드에서 걷은 양 중 공의 질량으로 가져올 비율. 나머지는
        /// 압축·비산된 것으로 보고 별도 장부에 남기지 않는다.</param>
        public SnowBallCpu(SnowHeightFieldCpu field, int residueMm,
            int growthWeightPermille = GrowthWeightScale)
        {
            _field = field;
            ResidueMm = residueMm;
            _growthWeightPermille = ClampGrowthWeight(growthWeightPermille);
        }

        /// <inheritdoc cref="_field"/>
        public SnowHeightFieldCpu Field => _field;

        /// <summary>깎을 필드를 갈아 끼운다. 질량은 그대로 들고 간다.</summary>
        public void Rebind(SnowHeightFieldCpu field) => _field = field;

        /// <summary>공이 실제로 취득한 눈(mm·셀). 필드 수확량에 성장 가중치를 적용한 값이다.</summary>
        public long MassMm => _massMm;

        /// <inheritdoc cref="SnowBallCpu(SnowHeightFieldCpu,int)"/>
        public int ResidueMm { get; set; }

        public int GrowthWeightPermille => _growthWeightPermille;

        /// <summary>보이는 크기가 1.5 m에 닿는 질량. 성장의 하드 상한이 아니다.</summary>
        public long VisibleMaxMassMm => MassMmForRadius(MaxRadiusM);

        public bool IsOverSizeThreshold => _massMm >= VisibleMaxMassMm;

        public double VolumeM3 => _massMm * (double)MmToM * CellAreaM2;

        /// <summary>질량을 구체로 환산한 제한 없는 반지름. 이동 감속과 초과량 표시가 읽는다.</summary>
        public float EquivalentRadiusM => RadiusFromMassMm(_massMm);

        /// <summary>화면과 접지 폭에 쓰는 반지름. 1.5 m를 넘지 않는다.</summary>
        public float RadiusM => System.Math.Min(MaxRadiusM, EquivalentRadiusM);

        public float DiameterM => RadiusM * 2f;

        /// <summary>
        /// 질량에서 반지름. <b>클라이언트도 부른다</b> — 복제되는 것은 질량 하나이고 크기는 그 파생물이라,
        /// 반지름을 따로 복제하면 두 값이 갈라진다.
        /// </summary>
        public static float RadiusFromMassMm(long massMm)
        {
            if (massMm <= 0) return SeedRadiusM;

            double v = massMm * (double)MmToM * CellAreaM2;
            float r = (float)System.Math.Cbrt(v / FourThirdsPi);
            return r < SeedRadiusM ? SeedRadiusM : r;
        }

        /// <summary>반지름에 필요한 질량. 테스트와 튜닝이 반대 방향을 물어볼 때 쓴다.</summary>
        public static long MassMmForRadius(float radiusM)
        {
            double v = FourThirdsPi * radiusM * radiusM * radiusM;
            return (long)(v / (MmToM * (double)CellAreaM2));
        }

        private static int ClampGrowthWeight(int growthWeightPermille)
        {
            if (growthWeightPermille < 1) return 1;
            return growthWeightPermille > GrowthWeightScale ? GrowthWeightScale : growthWeightPermille;
        }

        /// <summary>
        /// 씨앗 반지름의 공을 만들려면 최소한 이만큼의 눈이 필요하다. <b>보이는 공은 실제로 든 눈이
        /// 뒷받침해야 한다</b> — 0 으로 만들어 두면 화면에 눈이 없는 데서 눈덩이가 생긴다.
        /// </summary>
        public static long MinCreateMassMm => MassMmForRadius(SeedRadiusM);

        /// <summary>
        /// 제자리에서 눈을 <b>뭉친다</b>. 굴리기(<see cref="Harvest"/>)와 다른 점은 이동이 필요 없다는
        /// 것뿐이고, 걷는 연산은 같다 — 스윕의 두 자세를 같은 점으로 주면 그 자리의 사각 패치가 된다.
        ///
        /// <para>펭귄이 손으로 뭉치는 동작이 이것이다. 반지름을 인자로 받는 이유는 <b>뭉치는 손의 크기가
        /// 공의 크기와 다르기</b> 때문이다 — 처음 뭉칠 때는 공이 없으므로 공의 반지름을 쓸 수 없다.</para>
        /// </summary>
        /// <returns>실제로 걷은 양(mm·셀). 눈이 얕으면 0 이다.</returns>
        public long Gather(float centerX, float centerZ, float patchRadiusM, int residueMm)
        {
            if (patchRadiusM <= 0f) return 0;

            var pose = new SnowBladePose
            {
                CenterX = centerX,
                CenterZ = centerZ,
                ForwardX = 1f,
                ForwardZ = 0f,
            };
            var shape = new SnowBladeShape
            {
                HalfWidthM = patchRadiusM,
                HalfDepthM = patchRadiusM,
                Profile = SnowBladeProfileKind.Straight,
                WingLengthM = 0f,
            };

            long cut = SnowBladeSweep.Cut(_field, pose, pose, shape, 1, residueMm);
            AddWeightedCut(cut);
            return cut;
        }

        /// <summary>
        /// <paramref name="prevX"/>→<paramref name="nowX"/> 로 굴러간 자리의 눈을 걷어 공에 쌓는다.
        /// <b>움직이지 않으면 아무것도 걷지 않는다</b> — 제자리에서 도는 공이 자기 밑을 계속 파면
        /// 가만히 서서 커질 수 있다.
        /// </summary>
        /// <returns>실제로 걷은 양(mm·셀). 필드에서 빠진 양과 같다.</returns>
        public long Harvest(float prevX, float prevZ, float nowX, float nowZ)
            => HarvestInternal(prevX, prevZ, nowX, nowZ, 0f, false);

        /// <summary>
        /// 표시·콜라이더와 같은 <paramref name="harvestRadiusM"/>를 반폭으로 사용해 눈을 걷는다.
        /// 단계 성장처럼 내부 취득 질량과 실제 물리 반지름이 갈리는 경로가 사용한다.
        /// </summary>
        /// <returns>실제로 걷은 양(mm·셀). 필드에서 빠진 양과 같다.</returns>
        public long Harvest(float prevX, float prevZ, float nowX, float nowZ,
            float harvestRadiusM)
            => HarvestInternal(prevX, prevZ, nowX, nowZ, harvestRadiusM, true);

        private long HarvestInternal(float prevX, float prevZ, float nowX, float nowZ,
            float harvestRadiusM, bool overrideRadius)
        {
            float dx = nowX - prevX;
            float dz = nowZ - prevZ;
            float travel = (float)System.Math.Sqrt(dx * dx + dz * dz);
            if (travel < 1e-4f) return 0;

            float invTravel = 1f / travel;
            float fx = dx * invTravel;
            float fz = dz * invTravel;

            long total = 0;
            float done = 0f;

            // <b>한 번에 훑을 수 있는 거리에는 상한이 있다.</b> 세그먼트가 <see cref="MaxSegments"/> 개뿐이라
            // 그보다 긴 거리를 한 스윕으로 넘기면 세그먼트 사이가 접지면보다 넓게 벌어져 <b>걷지 않은
            // 줄무늬</b>가 남는다(실측: 씨앗 반지름 0.18 m 로 5 m 를 한 번에 넘기자 중간 셀이 300 mm 그대로
            // 남았다). 블레이드는 한 틱에 0.18 m 를 가므로 이 결함을 만나지 않지만, 경사를 굴러 내려가는
            // 공은 한 틱에 몇 미터를 간다. 그래서 거리를 나눠 훑는다.
            while (done < travel)
            {
                float r = overrideRadius
                    ? System.Math.Max(SeedRadiusM,
                        System.Math.Min(MaxRadiusM, harvestRadiusM))
                    : RadiusM;

                // <b>접지면은 정사각이 아니라 띠다.</b> 처음에는 `HalfDepth = r` 로 썼고, 그러면 공이
                // 한 스텝에 <b>자기 면적 전체</b>(2r × 2r)를 먹는다 — 실측으로 6 m 를 밀자 지름 2.41 m ·
                // 7,347 L 로 상한에 닿았고, 잔량을 150 → 250 으로 올려도 상한이 질량을 정하므로 결과가
                // 똑같았다. 굴러가는 공이 실제로 걷어가는 것은 <b>이동한 거리 × 공의 폭</b>이고, 그것이
                // 성장이 거리에 비례하게 만드는 전부다.
                //
                // 반길이를 셀 하나로 둔 것은 격자가 표현할 수 있는 가장 얕은 접지가 그것이기 때문이다.
                // 여기에 물리적인 접지 길이(2·√(2 r d)) 를 넣으려면 침하 깊이가 필요하고, 그 값은
                // 이 공이 아직 갖고 있지 않다.
                float contact = SnowFieldGeometry.CellSizeM;

                float span = contact * MaxSegments;
                float remaining = travel - done;
                float step = remaining < span ? remaining : span;

                var prev = new SnowBladePose
                {
                    CenterX = prevX + fx * done,
                    CenterZ = prevZ + fz * done,
                    ForwardX = fx,
                    ForwardZ = fz,
                };
                var now = new SnowBladePose
                {
                    CenterX = prevX + fx * (done + step),
                    CenterZ = prevZ + fz * (done + step),
                    ForwardX = fx,
                    ForwardZ = fz,
                };

                var shape = new SnowBladeShape
                {
                    HalfWidthM = r,
                    HalfDepthM = contact,
                    Profile = SnowBladeProfileKind.Straight,
                    WingLengthM = 0f,
                };

                // 세그먼트 간격이 접지 깊이(2·contact)를 넘지 않는다 — step ≤ 8·contact 이므로
                // 간격은 최대 1.14 contact 다. 이것이 줄무늬를 막는 조건이다.
                int segments = 1 + (int)(step / contact);
                if (segments > MaxSegments) segments = MaxSegments;

                long cut = SnowBladeSweep.Cut(_field, prev, now, shape, segments, ResidueMm);
                AddWeightedCut(cut);
                total += cut;
                done += step;
            }

            return total;
        }

        private void AddWeightedCut(long cutMm)
        {
            if (cutMm <= 0) return;
            long scaled = cutMm * _growthWeightPermille + _growthRemainderPermille;
            long gained = scaled / GrowthWeightScale;
            _growthRemainderPermille = (int)(scaled % GrowthWeightScale);
            _massMm += gained;
        }

        /// <summary>
        /// 들고 있던 눈을 그 자리에 붓는다. 손을 놓거나 공이 깨질 때 부른다.
        ///
        /// <para>퇴적 밴드는 자세 <b>앞</b>에만 생기므로(<see cref="SnowBladeSweep.Deposit"/>) 자세를
        /// 공 뒤로 물려서 밴드가 공을 덮게 한다. 그렇게 하지 않으면 눈이 공 앞에 한 뼘 떨어져 쌓인다.</para>
        /// </summary>
        /// <returns>놓지 못한 잔량. <b>0 이 아니면 그만큼이 공에 남아 있다</b> — 버리지 않는다.</returns>
        public long Release(float centerX, float centerZ, int capMm, int spreadRings)
        {
            if (_massMm <= 0) return 0;

            float r = RadiusM;
            var shape = new SnowBladeShape
            {
                HalfWidthM = r,
                HalfDepthM = r,
                Profile = SnowBladeProfileKind.Straight,
                WingLengthM = 0f,
            };

            // 밴드는 f ∈ [HalfDepth, HalfDepth + bandDepth] = [r, 3r] 이므로 2r 만큼 물리면 [-r, +r] 이 된다.
            var pose = new SnowBladePose
            {
                CenterX = centerX - r * 2f,
                CenterZ = centerZ,
                ForwardX = 1f,
                ForwardZ = 0f,
            };

            long unplaced = SnowBladeSweep.Deposit(_field, pose, shape, r * 2f, 0f, _massMm,
                                                   capMm, spreadRings);
            _massMm = unplaced;
            return unplaced;
        }
    }
}
