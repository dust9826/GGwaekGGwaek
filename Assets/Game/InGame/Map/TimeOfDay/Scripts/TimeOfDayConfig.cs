using UnityEngine;

namespace PPack
{
    /// <summary>하루 길이와 시간대별 하늘·해·안개·앰비언트 값을 한 곳에 모은 노브.
    /// <see cref="TimeOfDayDirector"/>가 매 프레임 이 에셋을 읽으므로 <b>플레이 중 값을 바꾸면 즉시
    /// 반영</b>되고, ScriptableObject라 플레이 종료 후에도 남는다 — <see cref="StageBalanceConfig"/>와
    /// 같은 이유, 같은 방식이다.
    ///
    /// <para>시간은 0~1로 정규화한다. <c>0 = 자정</c>, <c>0.25 = 일출</c>, <c>0.5 = 정오</c>,
    /// <c>0.75 = 일몰</c>. 모든 그라디언트·커브의 가로축이 이 값이므로 커브를 만질 때 0~1 밖으로
    /// 나가지 않게 한다.</para>
    ///
    /// <para>프리셋은 파일별로 나눈다. <c>.asset</c>은 YAML이고 Plastic이 병합하지 못한다.</para></summary>
    [CreateAssetMenu(menuName = "PPack/Map/Time Of Day Config")]
    public sealed class TimeOfDayConfig : ScriptableObject
    {
        [Header("하루 길이")]
        [Tooltip("게임 플레이 시간 몇 초를 하루로 볼지. 300 = 5분에 하루.")]
        [Min(1f)] public float SecondsPerDay = 300f;

        [Tooltip("게임 시작 시각(0=자정, 0.25=일출, 0.5=정오, 0.75=일몰).")]
        [Range(0f, 1f)] public float StartTimeOfDay = 0.3f;

        [Header("해")]
        [Tooltip("정오에 해가 서 있는 방위(Y축). 맵의 그림자 방향을 정한다.")]
        [Range(0f, 360f)] public float SunYawDegrees = 30f;

        [Tooltip("정오의 최대 고도(도). 90이면 머리 바로 위를 지나가 그림자가 발밑으로 기어든다. " +
            "겨울 태양처럼 낮게 두면 하루 종일 그림자가 길고 방향이 크게 바뀐다.")]
        [Range(10f, 90f)] public float SunMaxElevation = 34f;

        [Tooltip("동→남→서로 흘러가는 방위 진폭(도). 0이면 정확히 한 평면을 따라 뜨고 져서 " +
            "그림자 방향이 하루 내내 거의 같다. 키우면 대각선으로 가로지른다.")]
        [Range(0f, 120f)] public float SunAzimuthSweep = 78f;

        [Tooltip("시간대별 태양광 색.")]
        public Gradient SunColor = new Gradient();

        [Tooltip("시간대별 태양광 세기. 0이 되면 디렉셔널 라이트를 꺼서 밤에 지면 밑에서 새는 빛을 막는다.")]
        public AnimationCurve SunIntensity = AnimationCurve.Linear(0f, 0f, 1f, 0f);

        [Header("달")]
        [Tooltip("달이 가장 높을 때의 방위(Y축). 해와 어긋나게 두면 둘이 같은 길을 되감지 않는다.")]
        [Range(0f, 360f)] public float MoonYawDegrees = 200f;

        [Tooltip("달의 최대 고도(도). 해보다 조금 높게 두면 밤 그림자가 덜 눕는다.")]
        [Range(10f, 90f)] public float MoonMaxElevation = 46f;

        [Range(0f, 120f)] public float MoonAzimuthSweep = 60f;

        [Tooltip("달빛 색. 차가운 파랑이 기본이다.")]
        public Gradient MoonColor = new Gradient();

        [Tooltip("달 그림자의 진하기. 달은 약한 보조광이라 1.0이면 그림자가 너무 검다.")]
        [SerializeField, Range(0f, 1f)] private float _moonShadowStrength = 0.35f;

        /// <summary>달 그림자 진하기. 0이면 그림자가 없다.</summary>
        public float MoonShadowStrength => _moonShadowStrength;

        [Tooltip("달빛 세기. 낮에는 0이어야 한다 — 해와 달이 동시에 그림자를 던지면 방향이 읽히지 않는다.")]
        public AnimationCurve MoonIntensity = AnimationCurve.Linear(0f, 0f, 1f, 0f);

        [Header("스카이박스 (Skybox/Procedural)")]
        [Tooltip("비우면 씬에 이미 설정된 스카이박스를 복제해 쓴다. 어느 쪽이든 원본 에셋은 건드리지 않는다.")]
        public Material SkyboxTemplate;

        public Gradient SkyTint = new Gradient();
        public Gradient SkyGroundColor = new Gradient();
        public AnimationCurve SkyExposure = AnimationCurve.Linear(0f, 0.3f, 1f, 0.3f);
        public AnimationCurve SkyAtmosphereThickness = AnimationCurve.Linear(0f, 0.6f, 1f, 0.6f);

        [Tooltip("낮에 보이는 해 원반 크기.")]
        [Range(0f, 0.2f)] public float SunDiskSize = 0.04f;

        [Tooltip("밤에 보이는 달 원반 크기. 해보다 작아야 달로 읽힌다.")]
        [Range(0f, 0.2f)] public float MoonDiskSize = 0.022f;

        [Tooltip("원반 가장자리의 단단함. 높을수록 윤곽이 선명하다. 달은 해보다 높게 쓴다.")]
        [Min(1f)] public float SunDiskConvergence = 5f;
        [Min(1f)] public float MoonDiskConvergence = 14f;

        [Header("안개 · 앰비언트")]
        public Gradient FogColor = new Gradient();

        [Tooltip("시간대별 안개 농도. 밤과 새벽을 짙게 하면 거리감이 산다.")]
        public AnimationCurve FogDensity = AnimationCurve.Linear(0f, 0.0042f, 1f, 0.0042f);

        public Gradient AmbientSkyColor = new Gradient();
        public Gradient AmbientEquatorColor = new Gradient();
        public Gradient AmbientGroundColor = new Gradient();

        [Header("포스트 프로세싱 (Volume)")]
        [Tooltip("씬 Volume의 프로파일을 시간대별로 몰아준다. 원본 에셋은 건드리지 않고 런타임 복제본만 칠한다.")]
        public bool DriveVolume = true;

        [Tooltip("노출 보정(EV). 밤에 내리면 어둠이 깊어지고, 낮에 살짝 올리면 눈이 트인다.")]
        public AnimationCurve PostExposure = AnimationCurve.Linear(0f, -0.14f, 1f, -0.14f);

        [Tooltip("채도. 밤에는 색이 빠져야 달빛처럼 보인다.")]
        public AnimationCurve Saturation = AnimationCurve.Linear(0f, -9f, 1f, -9f);

        [Tooltip("대비.")]
        public AnimationCurve Contrast = AnimationCurve.Linear(0f, 17f, 1f, 17f);

        [Tooltip("비네트 세기. 밤에 조이면 시선이 가운데로 모인다.")]
        public AnimationCurve VignetteIntensity = AnimationCurve.Linear(0f, 0.2f, 1f, 0.2f);

        [Tooltip("컬러 필터. 화면 전체의 색조를 시간대별로 민다.")]
        public Gradient ColorFilter = new Gradient();

        [Header("오로라")]
        [Tooltip("밤마다 오로라가 뜨는 확률. 0이면 안 뜨고 1이면 매일 뜬다. 날짜별로 한 번만 뽑고, " +
            "같은 날은 언제 봐도 같은 결과다 — 프레임마다 주사위를 굴리면 깜빡거린다.")]
        [Range(0f, 1f)] public float AuroraChancePerNight = 0.45f;

        [Tooltip("오로라 세기. 가로축은 밤 정도(0=아직 밝음, 1=한밤중).")]
        public AnimationCurve AuroraIntensity = AnimationCurve.EaseInOut(0f, 0f, 1f, 1f);

        [Header("구간 경계")]
        [Tooltip("새벽이 시작되는 정규화 시각.")]
        [Range(0f, 1f)] public float DawnStart = 0.2f;
        [Range(0f, 1f)] public float DayStart = 0.3f;
        [Tooltip("노을은 해가 지기 전에 시작해야 한다. 해가 지평선 아래로 내려가면 절처적 하늘은 " +
            "따뜻한 산란을 만들 재료가 없어서 황갈색으로 탁해진다(측정해서 고친 값이다).")]
        [Range(0f, 1f)] public float DuskStart = 0.68f;
        [Range(0f, 1f)] public float NightStart = 0.80f;

        /// <summary>정규화 시각이 속한 구간. 경계 값이 순서를 벗어나도 예외 없이 판정되도록
        /// 뒤에서부터 검사한다.</summary>
        public EDayPhase PhaseAt(float normalizedTime)
        {
            float t = Mathf.Repeat(normalizedTime, 1f);
            if (t >= NightStart) return EDayPhase.Night;
            if (t >= DuskStart) return EDayPhase.Dusk;
            if (t >= DayStart) return EDayPhase.Day;
            if (t >= DawnStart) return EDayPhase.Dawn;
            return EDayPhase.Night;
        }

        private void Reset() => ApplyDefaultSky();

        /// <summary>밤–새벽–낮–노을–밤으로 한 바퀴 도는 기본값. 새 에셋이 회색 그라디언트로 태어나면
        /// 하늘이 통째로 죽으므로, 만들자마자 볼 만한 값이 들어 있어야 한다.</summary>
        [ContextMenu("기본 하늘 값 채우기")]
        public void ApplyDefaultSky()
        {
            // 필드 이니셔라이저만 놓으면 필드가 생기기 전에 만들어진 에셋은 0으로 역직렬화된다.
            // 원반 크기가 0이면 해도 달도 보이지 않는다 — 여기서 같이 되돌려야 하는 이유다.
            SunDiskSize = 0.04f;
            MoonDiskSize = 0.022f;
            SunDiskConvergence = 5f;
            MoonDiskConvergence = 14f;

            SunColor = MakeGradient(
                (0.22f, new Color(0.35f, 0.42f, 0.65f)),
                (0.27f, new Color(1f, 0.62f, 0.42f)),
                (0.4f, new Color(1f, 0.93f, 0.82f)),
                (0.6f, new Color(1f, 0.93f, 0.82f)),
                (0.75f, new Color(1f, 0.55f, 0.32f)),
                (0.85f, new Color(0.35f, 0.42f, 0.65f)));

            SunIntensity = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.20f, 0f), new Keyframe(0.30f, 1.0f),
                new Keyframe(0.5f, 1.35f), new Keyframe(0.68f, 1.0f), new Keyframe(0.78f, 0.35f),
                new Keyframe(0.84f, 0f), new Keyframe(1f, 0f));

            // 달은 해가 꾸는 세기의 1/4 정도다. 더 올리면 밤이 밤으로 안 보이고 낮이 하나 더 생긴다.
            MoonColor = MakeGradient(
                (0f, new Color(0.52f, 0.66f, 1f)),
                (0.5f, new Color(0.58f, 0.71f, 1f)),
                (1f, new Color(0.52f, 0.66f, 1f)));

            // 달은 해가 다 지기 전에 올라와야 한다. 예전 값은 해가 0.82에 죽는데 달이 0.86에야
            // 올라와서 둘 다 꺼진 구간이 생겼고, 거기서 화면이 툭 어두워졌다(측정: 낮 2.5 → 0.60).
            // 이제 0.68부터 차오르기 시작해 해가 지는 동안 이어받는다.
            MoonIntensity = new AnimationCurve(
                new Keyframe(0f, 0.78f), new Keyframe(0.20f, 0.74f), new Keyframe(0.34f, 0f),
                new Keyframe(0.62f, 0f), new Keyframe(0.72f, 0.38f), new Keyframe(0.82f, 0.74f),
                new Keyframe(1f, 0.78f));

            SkyTint = MakeGradient(
                (0f, new Color(0.14f, 0.17f, 0.27f)),
                (0.25f, new Color(0.60f, 0.44f, 0.42f)),
                (0.36f, new Color(0.60f, 0.70f, 0.95f)),
                (0.62f, new Color(0.60f, 0.70f, 0.95f)),
                (0.73f, new Color(0.66f, 0.44f, 0.38f)),
                (0.85f, new Color(0.14f, 0.17f, 0.27f)));

            SkyGroundColor = MakeGradient(
                (0f, new Color(0.028f, 0.045f, 0.082f)),
                (0.3f, new Color(0.35f, 0.38f, 0.42f)),
                (0.7f, new Color(0.35f, 0.38f, 0.42f)),
                (0.9f, new Color(0.028f, 0.045f, 0.082f)));

            SkyExposure = new AnimationCurve(
                new Keyframe(0f, 0.28f), new Keyframe(0.25f, 0.6f), new Keyframe(0.5f, 1.1f),
                new Keyframe(0.75f, 0.6f), new Keyframe(1f, 0.28f));

            // 낮에 두꺼우면 수평선이 초록으로 넘어간다 — 측정값: 1.05에서 녹색 초과 +6.2,
            // 0.55에서 −10.8. 낮은 얇게 가져가고, 노을·새벽에만 두껍게 해서 붉은기를 뽑는다.
            // 낮에 두꺼우면 수평선이 초록으로 넘어간다 — 측정값: 1.05에서 녹색 초과 +6.2,
            // 0.55에서 −10.8. 낮은 얇게 가져가고, 해가 아직 지평선 위에 있는 동안에만 두껍게 해
            // 붉은기를 뽑는다. 해가 진 뒤에 두꺼우면 황갈색 미댂물만 남는다.
            SkyAtmosphereThickness = new AnimationCurve(
                new Keyframe(0f, 0.7f), new Keyframe(0.25f, 1.2f), new Keyframe(0.5f, 0.55f),
                new Keyframe(0.73f, 1.15f), new Keyframe(0.82f, 0.8f), new Keyframe(1f, 0.7f));

            // 밤과 새벽이 짙다. 낮에 맑아지면서 맵이 한 번 널어졌다가 밤에 다시 닫힌다.
            FogDensity = new AnimationCurve(
                new Keyframe(0f, 0.0062f), new Keyframe(0.26f, 0.0075f), new Keyframe(0.5f, 0.0028f),
                new Keyframe(0.76f, 0.0068f), new Keyframe(1f, 0.0062f));

            FogColor = MakeGradient(
                (0f, new Color(0.09f, 0.12f, 0.2f)),
                (0.26f, new Color(0.55f, 0.45f, 0.45f)),
                (0.4f, new Color(0.72f, 0.8f, 0.9f)),
                (0.65f, new Color(0.72f, 0.8f, 0.9f)),
                (0.78f, new Color(0.6f, 0.42f, 0.38f)),
                (0.9f, new Color(0.09f, 0.12f, 0.2f)));

            // 밤 값이 높으면 눈밭이 스스로 빛나서 가깝게 느껴진다 — 밝은 지면과 어두운 하늘이
            // 맞닿으면 깊이가 죽는다(측정: 밤 하늘 0.225 대 눈 0.643, 2.9배). 달빛은 그대로 두고
            // 앤비언트만 낮춰 형태는 보이되 밤은 밤답게 만든다.
            AmbientSkyColor = MakeGradient(
                (0f, new Color(0.13f, 0.16f, 0.27f)),
                (0.3f, new Color(0.55f, 0.63f, 0.78f)),
                (0.7f, new Color(0.55f, 0.63f, 0.78f)),
                (0.88f, new Color(0.13f, 0.16f, 0.27f)));

            AmbientEquatorColor = MakeGradient(
                (0f, new Color(0.12f, 0.13f, 0.16f)),
                (0.3f, new Color(0.42f, 0.44f, 0.46f)),
                (0.7f, new Color(0.42f, 0.44f, 0.46f)),
                (0.88f, new Color(0.12f, 0.13f, 0.16f)));

            // 앰비언트가 낮→밤으로 떨어지는 구간을 완만하게. 예전엔 0.7에서 0.88 사이에 뚝 떨어져
            // 해·달 공백과 겹치면서 낙차가 배로 느껴졌다.
            PostExposure = new AnimationCurve(
                new Keyframe(0f, -0.55f), new Keyframe(0.26f, -0.1f), new Keyframe(0.5f, 0.1f),
                new Keyframe(0.74f, -0.1f), new Keyframe(0.88f, -0.55f), new Keyframe(1f, -0.55f));

            Saturation = new AnimationCurve(
                new Keyframe(0f, -26f), new Keyframe(0.28f, -4f), new Keyframe(0.5f, -6f),
                new Keyframe(0.74f, -2f), new Keyframe(0.88f, -26f), new Keyframe(1f, -26f));

            Contrast = new AnimationCurve(
                new Keyframe(0f, 10f), new Keyframe(0.3f, 17f), new Keyframe(0.7f, 17f),
                new Keyframe(0.9f, 10f), new Keyframe(1f, 10f));

            VignetteIntensity = new AnimationCurve(
                new Keyframe(0f, 0.34f), new Keyframe(0.3f, 0.16f), new Keyframe(0.7f, 0.16f),
                new Keyframe(0.9f, 0.34f), new Keyframe(1f, 0.34f));

            ColorFilter = MakeGradient(
                (0f, new Color(0.78f, 0.84f, 1f)),
                (0.27f, new Color(1f, 0.92f, 0.86f)),
                (0.5f, new Color(0.98f, 0.98f, 1f)),
                (0.75f, new Color(1f, 0.9f, 0.84f)),
                (0.9f, new Color(0.78f, 0.84f, 1f)));

            AuroraIntensity = new AnimationCurve(
                new Keyframe(0f, 0f), new Keyframe(0.35f, 0.25f), new Keyframe(1f, 1f));

            AmbientGroundColor = MakeGradient(
                (0f, new Color(0.06f, 0.06f, 0.06f)),
                (0.3f, new Color(0.2f, 0.2f, 0.2f)),
                (0.7f, new Color(0.2f, 0.2f, 0.2f)),
                (0.88f, new Color(0.06f, 0.06f, 0.06f)));
        }

        private static Gradient MakeGradient(params (float time, Color color)[] keys)
        {
            var colorKeys = new GradientColorKey[keys.Length];
            for (int index = 0; index < keys.Length; index++)
                colorKeys[index] = new GradientColorKey(keys[index].color, keys[index].time);

            var gradient = new Gradient();
            gradient.SetKeys(colorKeys, new[] { new GradientAlphaKey(1f, 0f) });
            return gradient;
        }
    }
}
