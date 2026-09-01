using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈의 <b>생김새</b>를 셰이더 전역으로 밀어넣는 유일한 자리.
    ///
    /// <para><b>왜 전역인가.</b> 이 팔레트를 읽는 재질이 둘 이상이다 - 표면을 그리는 마처와
    /// 눈덩이를 그리는 메시. 둘이 정확히 같은 값을 봐야 하고, 안 그러면 눈덩이가 자기가 만들어진
    /// 눈과 다른 조명 아래 있는 것처럼 보인다. 같은 스무 개 setter 를 재질마다 복사하는 것이
    /// 바로 그 어긋남이 생기는 방식이다. `SnowCasualStyle.hlsl` 의 주석이 같은 말을 한다.</para>
    ///
    /// <para><b>값은 코드에 박혀 있고 노브는 하나다.</b> 출처는 <c>AnyTest/SnowGrainFakeV6</c> -
    /// 그 스파이크가 여러 판을 돌려 고정한 값이고, 여기서 스무 개를 인스펙터로 열면 그 고정이
    /// 풀린다. A/B 는 <see cref="SnowLookSettings.Casual"/> 하나로 충분하다 - 0 이면 호출자가
    /// 계산한 사실적 색을 비트 단위로 돌려준다.</para>
    ///
    /// <para><b>모양은 건드리지 않는다.</b> V6 의 <c>RoundM</c>(둥근 어깨)과 <c>LoadExaggeration</c>
    /// 은 여기서 0·1(중립)이다. 우리 마처는 그 일을 <b>구운 fillet 텍스처</b>로 하고, 그쪽이
    /// coarse-max 상한을 EXACT 하게 유지한다 - 셰이더에서 표면을 들어올리면 그 최대 들림값을
    /// <c>_CoarseMaxBiasM</c> 에 넣어야 하고, V6 는 바로 그것 때문에 표면에 구멍이 뚫린 적이 있다.</para>
    /// </summary>
    public struct SnowLookSettings
    {
        /// <summary>0 = 사실적(호출자의 색 그대로), 1 = 토이. 유일한 A/B 노브.</summary>
        public float Casual;

        public float Bands;
        public float BandSoftness;
        public float Wrap;

        public Color LitColor;
        public Color MidColor;
        public Color ShadowColor;

        public float AlbedoInfluence;
        public float AoInfluence;
        public float Exposure;

        public float RimStrength;
        public float RimPower;
        public Color RimColor;

        public float SparkleAmount;
        public float SparkleScaleM;
        public float SparkleRadius;
        public float SparkleThreshold;
        public float SparkleSpeed;
        public Color SparkleColor;

        /// <summary>
        /// V6 가 고정한 값. <b>파란-보라 그림자가 이 중 가장 큰 일을 한다</b> - 사실적 렌더러는
        /// 알베도 하나에 매끄러운 광량을 곱하고, 양식화된 렌더러는 작은 팔레트를 골라 그 사이를
        /// 계단으로 건넌다. 어두운 끝이 검정이 아니라 채도 있는 청자색인 것이 "장난감" 으로 읽히는
        /// 이유의 대부분이다.
        /// </summary>
        public static SnowLookSettings V6(float casual) => new SnowLookSettings
        {
            Casual = casual,

            Bands = 4f,
            BandSoftness = 0.45f,
            Wrap = 0.25f,

            LitColor = new Color(1.00f, 0.985f, 0.945f, 1f),
            MidColor = new Color(0.80f, 0.845f, 0.98f, 1f),
            ShadowColor = new Color(0.40f, 0.40f, 0.78f, 1f),

            AlbedoInfluence = 0.35f,
            AoInfluence = 0.45f,
            Exposure = 0.85f,

            // 젖은 느낌 금지. 여기 스페큘러 로브가 아예 없다 - 노이즈 있는 노멀 위의 고주파
            // 하이라이트가 눈을 젖은 플라스틱으로 만든 것이 V6 이전의 실패였다. 대신 넓고 어두운
            // 림과, 크고 드물고 천천히 반짝이는 스파클이 그 자리를 대신한다. 둘 다 실루엣·셀
            // 규모라서 글리터 필드로 변할 수가 없다.
            RimStrength = 0.22f,
            RimPower = 2.2f,
            RimColor = new Color(0.86f, 0.92f, 1.00f, 1f),

            SparkleAmount = 0.55f,
            SparkleScaleM = 0.24f,
            SparkleRadius = 0.16f,
            SparkleThreshold = 0.86f,
            SparkleSpeed = 0.35f,
            SparkleColor = Color.white,
        };
    }

    /// <summary><see cref="SnowLookSettings"/> 를 셰이더 전역으로 민다.</summary>
    public static class SnowLookStyle
    {
        private static readonly int CasualId = Shader.PropertyToID("_SnowCasual");
        private static readonly int BandsId = Shader.PropertyToID("_SnowBands");
        private static readonly int BandSoftId = Shader.PropertyToID("_SnowBandSoftness");
        private static readonly int WrapId = Shader.PropertyToID("_SnowWrap");
        private static readonly int LitId = Shader.PropertyToID("_SnowLitColor");
        private static readonly int MidId = Shader.PropertyToID("_SnowMidColor");
        private static readonly int ShadowId = Shader.PropertyToID("_SnowShadowColor");
        private static readonly int AlbedoInfId = Shader.PropertyToID("_SnowAlbedoInfluence");
        private static readonly int AoInfId = Shader.PropertyToID("_SnowAoInfluence");
        private static readonly int ExposureId = Shader.PropertyToID("_SnowExposure");
        private static readonly int RimStrengthId = Shader.PropertyToID("_SnowRimStrength");
        private static readonly int RimPowerId = Shader.PropertyToID("_SnowRimPower");
        private static readonly int RimColorId = Shader.PropertyToID("_SnowRimColor");
        private static readonly int SparkleAmountId = Shader.PropertyToID("_SnowSparkleAmount");
        private static readonly int SparkleScaleId = Shader.PropertyToID("_SnowSparkleScaleM");
        private static readonly int SparkleRadiusId = Shader.PropertyToID("_SnowSparkleRadius");
        private static readonly int SparkleThreshId = Shader.PropertyToID("_SnowSparkleThresh");
        private static readonly int SparkleSpeedId = Shader.PropertyToID("_SnowSparkleSpeed");
        private static readonly int SparkleColorId = Shader.PropertyToID("_SnowSparkleColor");
        private static readonly int TimeId = Shader.PropertyToID("_SnowTime");

        // 모양 계열은 중립으로 고정한다. 우리 마처는 구운 fillet 을 쓰므로 셰이더에서 표면을
        // 들어올리는 항이 하나도 없어야 coarse-max 상한이 EXACT 하다.
        private static readonly int RoundMId = Shader.PropertyToID("_SnowRoundM");
        private static readonly int RoundKId = Shader.PropertyToID("_SnowRoundK");
        private static readonly int BandWideId = Shader.PropertyToID("_SnowBandNormalWideM");
        private static readonly int LoadExagId = Shader.PropertyToID("_SnowLoadExaggeration");
        private static readonly int LoadLiftId = Shader.PropertyToID("_SnowLoadLiftMaxM");
        private static readonly int VirginId = Shader.PropertyToID("_SnowVirginDepthM");
        private static readonly int LumpSquashId = Shader.PropertyToID("_SnowLumpSquash");

        public static void Apply(in SnowLookSettings s)
        {
            Shader.SetGlobalFloat(CasualId, s.Casual);

            Shader.SetGlobalFloat(BandsId, s.Bands);
            Shader.SetGlobalFloat(BandSoftId, s.BandSoftness);
            Shader.SetGlobalFloat(WrapId, s.Wrap);

            Shader.SetGlobalColor(LitId, s.LitColor);
            Shader.SetGlobalColor(MidId, s.MidColor);
            Shader.SetGlobalColor(ShadowId, s.ShadowColor);

            Shader.SetGlobalFloat(AlbedoInfId, s.AlbedoInfluence);
            Shader.SetGlobalFloat(AoInfId, s.AoInfluence);
            Shader.SetGlobalFloat(ExposureId, s.Exposure);

            Shader.SetGlobalFloat(RimStrengthId, s.RimStrength);
            Shader.SetGlobalFloat(RimPowerId, s.RimPower);
            Shader.SetGlobalColor(RimColorId, s.RimColor);

            Shader.SetGlobalFloat(SparkleAmountId, s.SparkleAmount);
            Shader.SetGlobalFloat(SparkleScaleId, s.SparkleScaleM);
            Shader.SetGlobalFloat(SparkleRadiusId, s.SparkleRadius);
            Shader.SetGlobalFloat(SparkleThreshId, s.SparkleThreshold);
            Shader.SetGlobalFloat(SparkleSpeedId, s.SparkleSpeed);
            Shader.SetGlobalColor(SparkleColorId, s.SparkleColor);

            // 시간을 C# 에서 미는 이유: 이 사슬의 일부가 손으로 제출한 CommandBuffer 에서
            // 그려지고, 그쪽에서는 _Time 이 신뢰할 수 없다.
            Shader.SetGlobalFloat(TimeId, Application.isPlaying ? Time.time : Time.realtimeSinceStartup);

            Shader.SetGlobalFloat(RoundMId, 0f);
            Shader.SetGlobalFloat(RoundKId, 0.14f);
            Shader.SetGlobalFloat(BandWideId, 0f);
            Shader.SetGlobalFloat(LoadExagId, 1f);
            Shader.SetGlobalFloat(LoadLiftId, 0f);
            Shader.SetGlobalFloat(VirginId, 0.3f);
            Shader.SetGlobalFloat(LumpSquashId, 0f);
        }
    }
}
