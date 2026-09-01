namespace PPack
{
    /// <summary>
    /// <b>눈의 물성 전부.</b> 지금까지 <see cref="SnowReposeRelax"/> 와 <see cref="SnowPlowStepCpu"/> 에
    /// 상수로 박혀 있던 값들을 한 곳에 모은 것이고, variant 를 만들 수 있게 하는 것이 목적이다.
    ///
    /// 두 축이 이 구조체의 실질이다:
    /// <list type="bullet">
    /// <item><b>퍼지는 정도</b> = <see cref="ReposeAngleDeg"/>. 낮으면 물처럼 흘러 넓게 퍼지고,
    /// 높으면 제자리에 서서 탑이 된다.</item>
    /// <item><b>뭉치는 정도</b> = <see cref="CohesionMm"/>. 이 낙차를 넘기 전에는 <b>아예 흐르지 않는다</b> —
    /// 정지 마찰이다. 젖은 눈이 삽 위에 덩어리로 붙어 있는 것이 이것이고, 0 이면 마른 가루눈처럼
    /// 안식각에 닿는 즉시 무너진다.</item>
    /// </list>
    ///
    /// 셋째 축인 <b>흐르는 속도</b>(<see cref="RelaxDenominator"/>)는 물성처럼 보이지만
    /// <b>물성이 아니다.</b> 8이웃 명시적 확산의 안정 한계가 <c>λ·8 ≤ 1</c> 이라 8 미만은 수치적으로
    /// 발산한다 — 초과분의 절반을 넘겼을 때 한계 순환에 빠진 것이 그 증거다. 8 이 하한이고,
    /// 키우면 더 끈적하게 <em>보이지만</em> 실제로는 그냥 수렴이 느려지는 것이다. 끈적함은
    /// <see cref="CohesionMm"/> 으로 표현하는 것이 맞다.
    /// </summary>
    [System.Serializable]
    public struct SnowMaterialCpu
    {
        /// <summary>안식각. 이 기울기를 넘으면 무너진다. 마른 가루눈 35~40, 젖은 눈 50~60, 다져진 눈 60~70.</summary>
        public float ReposeAngleDeg;

        /// <summary>
        /// 응집력. 이웃과의 낙차가 안식각 + 이 값을 넘기 전에는 한 밀리미터도 안 움직인다.
        /// 클수록 뭉쳐서 가파른 벽을 세우고, 무너질 때 덩어리째 무너진다.
        /// </summary>
        public int CohesionMm;

        /// <summary>이웃 하나에 넘기는 몫은 초과분의 1/이 값. <b>8 미만은 발산한다</b> — 위 요약 참고.</summary>
        public int RelaxDenominator;

        /// <summary>스텝당 이완 반복. 늘리면 같은 스텝 안에서 더 멀리 전파된다. 비용이 여기 비례한다.</summary>
        public int RelaxIterations;

        /// <summary>컷이 남기는 잔설. 0 이면 바닥까지 긁는다.</summary>
        public int CutResidueMm;

        /// <summary>블레이드 앞 퇴적 밴드의 깊이. 좁으면 스파이크가 되어 이완 반복을 잡아먹는다.</summary>
        public float DepositBandDepthM;

        /// <summary>퇴적 밴드가 블레이드 폭 밖으로 나가는 여유.</summary>
        public float DepositSideMarginM;

        /// <summary>
        /// <b>더미가 도달할 수 있는 최대 높이.</b> 0 이면 상한 없음.
        ///
        /// 상한 그 자체보다 중요한 것은 그것이 <b>어떻게 지켜지느냐</b>다. 퇴적이 남은 여유 높이에
        /// 비례해서 붓기 때문에, 상한에 가까워진 셀은 거의 안 받고 낮은 셀이 대신 받는다.
        /// 그 결과 더미가 위로 솟는 대신 <b>옆으로 퍼진다</b> - 실제 블레이드 앞에서 눈이 하는 일이다.
        ///
        /// 밴드 전체가 상한에 닿으면 밴드를 <see cref="DepositSpreadRings"/> 만큼 넓혀가며 마저
        /// 놓고, 그래도 못 놓으면 상한을 무시한다. <b>질량은 어떤 경우에도 사라지지 않는다.</b>
        /// </summary>
        public int MaxPileHeightMm;

        /// <summary>상한에 걸렸을 때 밴드를 넓히는 횟수. 한 번에 셀 두 칸씩 넓어진다.</summary>
        public int DepositSpreadRings;

        /// <summary>눈 깊이 1 m 당 차량 최고속 배수 = 1 / (1 + 이 값 x 깊이). 저항이다.</summary>
        public float DragPerMetre;

        // ---- 파생값. 생성 시 한 번 계산해서 relax 의 내부 루프가 tan 을 부르지 않게 한다 ----

        public int MaxDropOrthoMm;
        public int MaxDropDiagMm;

        /// <summary>안식각과 셀 크기에서 낙차 두 개를 다시 계산한다. 필드를 바꿨으면 반드시 부른다.</summary>
        public SnowMaterialCpu Resolved()
        {
            var m = this;
            if (m.RelaxDenominator < 8) m.RelaxDenominator = 8;      // 안정 한계. 아래로는 발산한다
            if (m.RelaxIterations < 1) m.RelaxIterations = 1;
            if (m.CohesionMm < 0) m.CohesionMm = 0;
            if (m.MaxPileHeightMm < 0) m.MaxPileHeightMm = 0;
            if (m.DepositSpreadRings < 0) m.DepositSpreadRings = 0;
            if (m.ReposeAngleDeg < 1f) m.ReposeAngleDeg = 1f;
            if (m.ReposeAngleDeg > 85f) m.ReposeAngleDeg = 85f;

            double t = System.Math.Tan(m.ReposeAngleDeg * System.Math.PI / 180.0);
            m.MaxDropOrthoMm = (int)(t * SnowFieldGeometry.CellSizeMm);
            m.MaxDropDiagMm = (int)(t * SnowFieldGeometry.CellSizeMm * System.Math.Sqrt(2.0));
            return m;
        }

        /// <summary>
        /// 지금까지 쓰던 값 그대로. 안식각 55°(직교 178 mm · 대각 252 mm), 응집력 0.
        /// <b>이 프리셋으로는 기존 거동이 한 비트도 바뀌지 않는다.</b>
        /// </summary>
        public static SnowMaterialCpu Default => new SnowMaterialCpu
        {
            ReposeAngleDeg = 55f,
            CohesionMm = 0,
            RelaxDenominator = 8,
            RelaxIterations = 4,
            CutResidueMm = 0,
            DepositBandDepthM = 1.0f,
            DepositSideMarginM = 0.10f,
            DragPerMetre = 3f,
            MaxPileHeightMm = 1600,
            DepositSpreadRings = 6
        }.Resolved();

        /// <summary>마른 가루눈. 낮은 안식각에 응집력 0 — 넓게 퍼지고 둔덕이 낮고 완만하다.</summary>
        public static SnowMaterialCpu Powder => new SnowMaterialCpu
        {
            ReposeAngleDeg = 38f,
            CohesionMm = 0,
            RelaxDenominator = 8,
            RelaxIterations = 5,
            CutResidueMm = 0,
            DepositBandDepthM = 1.2f,
            DepositSideMarginM = 0.10f,
            DragPerMetre = 1.8f,
            MaxPileHeightMm = 1200,
            DepositSpreadRings = 8
        }.Resolved();

        /// <summary>젖은 눈. 높은 안식각에 응집력 — 가파르게 서고 덩어리로 무너지며 무겁다.</summary>
        public static SnowMaterialCpu Wet => new SnowMaterialCpu
        {
            ReposeAngleDeg = 60f,
            CohesionMm = 22,
            RelaxDenominator = 8,
            RelaxIterations = 3,
            CutResidueMm = 0,
            DepositBandDepthM = 0.8f,
            DepositSideMarginM = 0.08f,
            DragPerMetre = 4.5f,
            MaxPileHeightMm = 2200,
            DepositSpreadRings = 5
        }.Resolved();

        /// <summary>다져진 눈. 거의 안 흐른다 — 깎은 자리가 계단으로 남고 벽이 서 있다.</summary>
        public static SnowMaterialCpu Packed => new SnowMaterialCpu
        {
            ReposeAngleDeg = 68f,
            CohesionMm = 55,
            RelaxDenominator = 10,
            RelaxIterations = 2,
            CutResidueMm = 0,
            DepositBandDepthM = 0.7f,
            DepositSideMarginM = 0.06f,
            DragPerMetre = 6f,
            MaxPileHeightMm = 2800,
            DepositSpreadRings = 4
        }.Resolved();

        public static SnowMaterialCpu FromPreset(SnowMaterialPreset p)
        {
            switch (p)
            {
                case SnowMaterialPreset.Powder: return Powder;
                case SnowMaterialPreset.Wet: return Wet;
                case SnowMaterialPreset.Packed: return Packed;
                default: return Default;
            }
        }
    }

    public enum SnowMaterialPreset
    {
        Default = 0,
        Powder = 1,
        Wet = 2,
        Packed = 3,
        Custom = 4
    }
}
