using System;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

namespace PPack
{
    /// <summary>플레이 시간을 하루로 환산해 해·달·하늘·안개·앰비언트·포스트를 굴리는 환경 연출의 권위.
    /// 하루 길이는 <see cref="TimeOfDayConfig.SecondsPerDay"/> 하나로 정해진다.
    ///
    /// <para><b>게임 규칙을 모른다.</b> 의뢰도 점수도 전역 타이머도 참조하지 않고 자기 경과 시간만
    /// 센다 — Map이 Cleanliness를 알면 안 되기 때문이다. 진행을 게임 페이즈에 묶고 싶으면 바깥에서
    /// <see cref="Begin"/>/<see cref="Pause"/>를 부른다.</para>
    ///
    /// <para><b>빌려 쓰는 것은 전부 되돌린다.</b> 스카이박스는 런타임 복제본을 만들어 칠하고, 씬
    /// Volume의 프로파일도 복제본으로 바꿔 칠한다. 에셋을 직접 만지면 플레이가 끝나도 값이 남아
    /// 맵의 라이팅 원본이 오염된다. 종료 시 스카이박스·해·안개·앰비언트·프로파일을 모두 복원한다.</para></summary>
    [DisallowMultipleComponent]
    public sealed class TimeOfDayDirector : MonoBehaviour
    {
        private static readonly int SkyTintId = Shader.PropertyToID("_SkyTint");
        private static readonly int GroundColorId = Shader.PropertyToID("_GroundColor");
        private static readonly int ExposureId = Shader.PropertyToID("_Exposure");
        private static readonly int AtmosphereThicknessId = Shader.PropertyToID("_AtmosphereThickness");
        private static readonly int SunDiskId = Shader.PropertyToID("_SunDisk");
        private static readonly int SunSizeId = Shader.PropertyToID("_SunSize");
        private static readonly int SunSizeConvergenceId = Shader.PropertyToID("_SunSizeConvergence");

        [SerializeField] private TimeOfDayConfig _config;

        [Tooltip("비우면 RenderSettings.sun, 그것도 없으면 씬의 첫 디렉셔널 라이트를 쓴다.")]
        [SerializeField] private Light _sun;

        [Tooltip("밤을 비추는 두 번째 디렉셔널 라이트. 비워 두면 달빛이 없다.")]
        [SerializeField] private Light _moon;

        [Tooltip("하늘에 뜨는 달 원반. 비워 두면 달빛만 있고 달은 안 보인다.")]
        [SerializeField] private MoonDisk _moonDisk;

        [Tooltip("밤하늘의 별. 비워 두면 별만 없고 나머지는 그대로 돌아간다.")]
        [SerializeField] private SkyDome _stars;

        [Tooltip("오로라 레이어. 밤마다 확률적으로 뜬다.")]
        [SerializeField] private SkyDome _aurora;

        [Tooltip("시간대별로 몰아줄 씬 Volume. 비워 두면 포스트 프로세싱은 건드리지 않는다.")]
        [SerializeField] private Volume _volume;

        [Tooltip("켜져 있으면 Start부터 바로 시간이 흐른다. 인트로·결과 화면에서 멈추고 싶으면 끄고 Begin()을 부른다.")]
        [SerializeField] private bool _autoStart = true;

        private Material _runtimeSkybox;
        private Material _originalSkybox;
        private VolumeProfile _runtimeProfile;
        private VolumeProfile _originalProfile;
        private Light _originalSun;
        private Color _originalFogColor;
        private float _originalFogDensity;
        private Color _originalAmbientSky;
        private Color _originalAmbientEquator;
        private Color _originalAmbientGround;
        private Color _originalAmbientLight;
        private bool _captured;

        public bool IsRunning { get; private set; }

        /// <summary>시작 이후 흐른 게임 내 시간(초). 하루가 넘어가도 계속 누적된다.</summary>
        public float ElapsedSeconds { get; private set; }

        /// <summary>0=자정, 0.25=일출, 0.5=정오, 0.75=일몰.</summary>
        public float NormalizedTime { get; private set; }

        /// <summary>지난 날 수. 시작 시각과 무관하게 0에서 센다.</summary>
        public int DayIndex { get; private set; }

        public EDayPhase Phase { get; private set; }

        /// <summary>0=대낮, 1=한밤. 마을의 창문·가로등이 이 값을 읽어 밤에만 밝게 타오른다.
        ///
        /// <para><b>왜 static인가.</b> 소비자가 집마다·등마다 붙은 수십 개라 인스펙터로 일일이
        /// 연결하는 것은 비현실적이고, 맵 프리팩을 고쳐서 디렉터를 참조하게 만들면 WinterVillage가
        /// TimeOfDay 없이는 동작하지 않게 된다. 이 값은 <b>순전히 연출용</b>이라(권위 상태가 아니다)
        /// 전역 값으로 두어도 서버·클라이언트 동기화에 영향이 없다. 디렉터가 없는 씨에서는 1이 아닌
        /// <b>0</b>이 기본이어야 했으나, 그러면 하루 주기를 안 쓰는 씨에서 불이 전부 꺼진다.
        /// 그래서 1로 두고(=지금까지와 똑같은 동작), 디렉터가 살아 있는 동안만 덮어쓴다.</para></summary>
        public static float NightFactor01 { get; private set; } = 1f;

        /// <summary>하루가 넘어갈 때 새 <see cref="DayIndex"/>와 함께 발생한다.</summary>
        public event Action<int> DayAdvanced;

        public event Action<EDayPhase> PhaseChanged;

        public void Configure(TimeOfDayConfig config, Light sun = null, Light moon = null,
            SkyDome stars = null, SkyDome aurora = null, Volume volume = null, MoonDisk moonDisk = null)
        {
            _config = config;
            if (sun != null) _sun = sun;
            if (moon != null) _moon = moon;
            if (stars != null) _stars = stars;
            if (aurora != null) _aurora = aurora;
            if (volume != null) _volume = volume;
            if (moonDisk != null) _moonDisk = moonDisk;
            ResetToStart();
        }

        public void Begin()
        {
            IsRunning = true;
            Apply();
        }

        public void Pause() => IsRunning = false;

        /// <summary>시각을 config의 시작 시각으로 되돌린다. 날짜 수도 0으로 돌아간다.</summary>
        public void ResetToStart()
        {
            ElapsedSeconds = 0f;
            NormalizedTime = _config != null ? Mathf.Repeat(_config.StartTimeOfDay, 1f) : 0.3f;
            DayIndex = 0;
            Phase = _config != null ? _config.PhaseAt(NormalizedTime) : EDayPhase.Day;
            Apply();
        }

        /// <summary>시각을 직접 지정한다(디버그·컷신용). 하루 경계를 건너뛰어도 날짜는 세지 않는다.</summary>
        public void SetNormalizedTime(float normalizedTime)
        {
            NormalizedTime = Mathf.Repeat(normalizedTime, 1f);
            UpdatePhase();
            Apply();
        }

        private void Awake()
        {
            CaptureEnvironment();
            NormalizedTime = _config != null ? Mathf.Repeat(_config.StartTimeOfDay, 1f) : 0.3f;
            Phase = _config != null ? _config.PhaseAt(NormalizedTime) : EDayPhase.Day;
        }

        private void Start()
        {
            if (_autoStart) Begin();
            else Apply();
        }

        private void OnDestroy() => RestoreEnvironment();

        private void Update()
        {
            if (!IsRunning || _config == null) return;

            float delta = Time.deltaTime;
            ElapsedSeconds += delta;

            float previous = NormalizedTime;
            NormalizedTime = Mathf.Repeat(previous + (delta / Mathf.Max(1f, _config.SecondsPerDay)), 1f);
            if (NormalizedTime < previous)
            {
                DayIndex++;
                DayAdvanced?.Invoke(DayIndex);
            }

            UpdatePhase();
            Apply();
        }

        private void UpdatePhase()
        {
            if (_config == null) return;
            EDayPhase phase = _config.PhaseAt(NormalizedTime);
            if (phase == Phase) return;
            Phase = phase;
            PhaseChanged?.Invoke(phase);
        }

        private void Apply()
        {
            if (_config == null) return;

            float t = NormalizedTime;
            float sunIntensity = Mathf.Max(0f, _config.SunIntensity.Evaluate(t));

            ApplySun(t, sunIntensity);
            ApplyMoon(t);
            KeepSunAsSkyOwner();
            ApplySkybox(t);
            ApplyMoonDisk(t);

            float night = NightFactor(t);
            NightFactor01 = night;
            ApplyStars(t, night);
            ApplyAurora(t, night);
            ApplyVolume(t);

            RenderSettings.fogColor = _config.FogColor.Evaluate(t);
            RenderSettings.fogDensity = Mathf.Max(0f, _config.FogDensity.Evaluate(t));

            Color ambientSky = _config.AmbientSkyColor.Evaluate(t);
            RenderSettings.ambientSkyColor = ambientSky;
            RenderSettings.ambientEquatorColor = _config.AmbientEquatorColor.Evaluate(t);
            RenderSettings.ambientGroundColor = _config.AmbientGroundColor.Evaluate(t);
            // Flat 앰비언트 모드에서는 ambientLight만 읽히므로 하늘색을 그대로 넘긴다.
            RenderSettings.ambientLight = ambientSky;
        }

        private void ApplySun(float t, float intensity)
        {
            Light sun = ResolveSun();
            if (sun == null) return;

            sun.transform.rotation = CelestialRotation(
                t, _config.SunYawDegrees, _config.SunMaxElevation, _config.SunAzimuthSweep);
            sun.color = _config.SunColor.Evaluate(t);
            sun.intensity = intensity;
            // 세기 0인 해를 켜 둔 채 지평선 아래로 돌리면 URP가 여전히 그림자를 갱신한다. 꺼서 막는다.
            sun.enabled = intensity > 0.001f;
        }

        /// <summary>달은 해의 반대편을 돈다(정규시각 +0.5). 그래서 해가 지면 뜨고, 해가 뜨면 진다 —
        /// 한 커브로 둘을 따로 맞추는 것보다 틀릴 여지가 없다.</summary>
        private void ApplyMoon(float t)
        {
            if (_moon == null) return;

            float moonT = Mathf.Repeat(t + 0.5f, 1f);
            _moon.transform.rotation = CelestialRotation(
                moonT, _config.MoonYawDegrees, _config.MoonMaxElevation, _config.MoonAzimuthSweep);
            _moon.color = _config.MoonColor.Evaluate(t);

            float intensity = Mathf.Max(0f, _config.MoonIntensity.Evaluate(t));
            _moon.intensity = intensity;
            _moon.enabled = intensity > 0.001f;

            // 달은 약한 보조광이다. 그림자를 전력(1.0)으로 드리우면 달빛을 올려도 그만큼
            // 그림자가 깊어져서 화면이 오히려 어두워진다 — 실제로 달을 0.62→0.78로 올리고도
            // 펭귄 주변 눈밭이 까맣게 남았다. 밤에는 형태만 읽히면 충분하다.
            _moon.shadowStrength = _config.MoonShadowStrength;
        }

        /// <summary>천체 하나의 회전. 고도는 사인으로 오르내리고 방위는 동→남→서로 흐른다.
        ///
        /// <para>예전에는 X축만 <c>t*360-90</c>으로 돌렸다. 그러면 정오에 고도가 정확히 90도가 되어
        /// <b>머리 바로 위</b>를 지나가고, 방위가 하루 내내 고정이라 그림자가 길이만 변할 뿐 방향이
        /// 거의 그대로다. 고도 상한과 방위 진폭을 따로 두면 낮게 비스듬히 가로지르는 겨울 태양이 되고,
        /// 그림자가 길이와 방향을 함께 바꾼다.</para>
        ///
        /// <para><c>t=0.25</c>에서 고도 0(일출), <c>0.5</c>에서 최대, <c>0.75</c>에서 다시 0(일몰).</para></summary>
        private static Quaternion CelestialRotation(float t, float yaw, float maxElevation, float azimuthSweep)
        {
            float angle = (t - 0.25f) * Mathf.PI * 2f;
            float elevation = maxElevation * Mathf.Sin(angle);
            float azimuth = yaw - (azimuthSweep * Mathf.Cos(angle));
            return Quaternion.Euler(elevation, azimuth, 0f);
        }

        /// <summary>하늘 원반의 주인은 <b>언제나 해</b>다.
        ///
        /// <para>예전에는 밤에 <see cref="RenderSettings.sun"/>을 달로 넘겨 하늘에 달을 그렸다. 그게
        /// 화면이 툭 어두워졌다 다시 밝아지는 원인이었다 — 절차적 스카이박스는 주인의 <b>방향</b>으로
        /// 하늘 밝기를 계산하는데, 지평선 아래 해에서 하늘 높이 뜬 달로 주인이 바뀌는 순간 하늘이
        /// 통째로 다시 계산돼 <b>밤인데 도로 밝아진다</b>. 측정값: 교대 시 +16.4% 반등, 해로 고정하면
        /// −3.9%/−5.6%로 단조 감소.</para>
        ///
        /// <para>그래서 달은 <see cref="MoonDisk"/>가 직접 그린다. 하늘 밝기는 해 하나가 결정하므로
        /// 불연속이 생길 자리가 없다.</para></summary>
        private void KeepSunAsSkyOwner() => RenderSettings.sun = ResolveSun();

        private void ApplySkybox(float t)
        {
            Material sky = ResolveSkybox();
            if (sky == null) return;

            if (sky.HasProperty(SkyTintId)) sky.SetColor(SkyTintId, _config.SkyTint.Evaluate(t));
            if (sky.HasProperty(GroundColorId)) sky.SetColor(GroundColorId, _config.SkyGroundColor.Evaluate(t));
            if (sky.HasProperty(ExposureId)) sky.SetFloat(ExposureId, _config.SkyExposure.Evaluate(t));
            if (sky.HasProperty(AtmosphereThicknessId))
                sky.SetFloat(AtmosphereThicknessId, _config.SkyAtmosphereThickness.Evaluate(t));

            if (!sky.HasProperty(SunSizeId)) return;
            sky.SetFloat(SunSizeId, _config.SunDiskSize);
            sky.SetFloat(SunSizeConvergenceId, _config.SunDiskConvergence);
        }

        /// <summary>달 원반을 달빛 방향에 맞춰 띄운다. 세기는 달빛을 그대로 따라가므로 달이 안 뜬
        /// 낮에는 저절로 꺼진다.</summary>
        private void ApplyMoonDisk(float t)
        {
            if (_moonDisk == null) return;

            float peak = 0f;
            for (int index = 0; index < _config.MoonIntensity.length; index++)
                peak = Mathf.Max(peak, _config.MoonIntensity[index].value);
            if (peak <= 0.0001f) peak = 1f;

            float intensity = Mathf.Max(0f, _config.MoonIntensity.Evaluate(t));
            Vector3 forward = _moon != null
                ? _moon.transform.forward
                : CelestialRotation(Mathf.Repeat(t + 0.5f, 1f), _config.MoonYawDegrees,
                    _config.MoonMaxElevation, _config.MoonAzimuthSweep) * Vector3.forward;
            _moonDisk.SetMoon(forward, intensity / peak);
        }

        /// <summary>별은 해가 죽은 만큼 산다. 따로 커브를 두지 않는 이유는, 둘이 어긋나면 해가 떠
        /// 있는데 별이 보이는 모순이 곧바로 화면에 나오기 때문이다.</summary>
        private float NightFactor(float t)
        {
            float peak = 0f;
            for (int index = 0; index < _config.SunIntensity.length; index++)
                peak = Mathf.Max(peak, _config.SunIntensity[index].value);
            if (peak <= 0.0001f) peak = 1f;

            float daylight = Mathf.Clamp01(Mathf.Max(0f, _config.SunIntensity.Evaluate(t)) / peak);
            return 1f - Mathf.Clamp01(daylight * 3f);
        }

        private void ApplyStars(float t, float night)
        {
            if (_stars == null) return;
            _stars.SetVisibility(night);
            _stars.SetDayProgress(t);
        }

        /// <summary>오로라는 <b>날짜별로 한 번</b> 뽑는다. 프레임마다 주사위를 굴리면 깜빡이고,
        /// <see cref="UnityEngine.Random"/>을 쓰면 같은 날인데도 도메인 리로드마다 결과가 달라진다.
        /// 그래서 날짜를 해시해 결정한다 — 같은 날은 언제 봐도 같은 하늘이다.</summary>
        private void ApplyAurora(float t, float night)
        {
            if (_aurora == null) return;

            bool tonight = AuroraTonight(DayIndex, _config.AuroraChancePerNight);
            float strength = tonight ? Mathf.Clamp01(_config.AuroraIntensity.Evaluate(night)) : 0f;
            _aurora.SetVisibility(strength * night);
            _aurora.SetDayProgress(t);
        }

        /// <summary>날짜 인덱스를 0~1로 해시한 값이 확률보다 작으면 그날 밤 오로라가 뜬다.</summary>
        public static bool AuroraTonight(int dayIndex, float chancePerNight)
        {
            if (chancePerNight <= 0f) return false;
            if (chancePerNight >= 1f) return true;

            unchecked
            {
                uint hash = (uint)(dayIndex * 73856093) ^ 0x9E3779B9u;
                hash ^= hash >> 15;
                hash *= 0x2545F491u;
                hash ^= hash >> 13;
                return (hash % 10000u) / 10000f < chancePerNight;
            }
        }

        /// <summary>씬 Volume을 시간대별로 몰아준다. <b>프로파일 에셋을 직접 만지지 않는다</b> —
        /// 그 프로파일은 맵이 소유하고 다른 씬도 쓰므로, 스카이박스와 같은 이유로 런타임 복제본에만
        /// 칠하고 종료 시 되돌린다.</summary>
        private void ApplyVolume(float t)
        {
            if (_volume == null || !_config.DriveVolume) return;

            VolumeProfile profile = ResolveVolumeProfile();
            if (profile == null) return;

            if (profile.TryGet(out ColorAdjustments color))
            {
                color.postExposure.overrideState = true;
                color.postExposure.value = _config.PostExposure.Evaluate(t);
                color.saturation.overrideState = true;
                color.saturation.value = _config.Saturation.Evaluate(t);
                color.contrast.overrideState = true;
                color.contrast.value = _config.Contrast.Evaluate(t);
                color.colorFilter.overrideState = true;
                color.colorFilter.value = _config.ColorFilter.Evaluate(t);
            }

            if (profile.TryGet(out Vignette vignette))
            {
                vignette.intensity.overrideState = true;
                vignette.intensity.value = _config.VignetteIntensity.Evaluate(t);
            }
        }

        private VolumeProfile ResolveVolumeProfile()
        {
            if (_runtimeProfile != null) return _runtimeProfile;
            if (_volume.sharedProfile == null) return null;

            _originalProfile = _volume.sharedProfile;
            _runtimeProfile = Instantiate(_originalProfile);
            _runtimeProfile.name = $"{_originalProfile.name} (TimeOfDay Runtime)";
            _volume.sharedProfile = _runtimeProfile;
            return _runtimeProfile;
        }

        private Light ResolveSun()
        {
            if (_sun != null) return _sun;

            _sun = RenderSettings.sun;
            if (_sun != null) return _sun;

            Light[] lights = FindObjectsByType<Light>(FindObjectsSortMode.None);
            foreach (Light light in lights)
            {
                if (light.type != LightType.Directional) continue;
                if (_moon != null && light == _moon) continue;
                _sun = light;
                break;
            }
            return _sun;
        }

        private Material ResolveSkybox()
        {
            if (_runtimeSkybox != null) return _runtimeSkybox;

            Material template = _config != null && _config.SkyboxTemplate != null
                ? _config.SkyboxTemplate
                : RenderSettings.skybox;
            if (template == null) return null;

            _runtimeSkybox = new Material(template) { name = $"{template.name} (TimeOfDay Runtime)" };
            EnableSkyDisk(_runtimeSkybox);
            RenderSettings.skybox = _runtimeSkybox;
            return _runtimeSkybox;
        }

        /// <summary>맵이 넘겨준 스카이박스는 원반이 꺼져 있을 수 있다(블루아워 예술사진이 보통 그렇다).
        /// 해를 보여주는 게 이 시스템의 요점이니 복제본에서만 켜 둔다.</summary>
        private static void EnableSkyDisk(Material sky)
        {
            if (!sky.HasProperty(SunDiskId)) return;
            sky.SetFloat(SunDiskId, 2f); // 0=None, 1=Simple, 2=High Quality
            sky.DisableKeyword("_SUNDISK_NONE");
            sky.DisableKeyword("_SUNDISK_SIMPLE");
            sky.EnableKeyword("_SUNDISK_HIGH_QUALITY");
        }

        private void CaptureEnvironment()
        {
            if (_captured) return;
            _captured = true;
            _originalSkybox = RenderSettings.skybox;
            _originalSun = RenderSettings.sun;
            _originalFogColor = RenderSettings.fogColor;
            _originalFogDensity = RenderSettings.fogDensity;
            _originalAmbientSky = RenderSettings.ambientSkyColor;
            _originalAmbientEquator = RenderSettings.ambientEquatorColor;
            _originalAmbientGround = RenderSettings.ambientGroundColor;
            _originalAmbientLight = RenderSettings.ambientLight;
        }

        private void RestoreEnvironment()
        {
            if (!_captured) return;
            _captured = false;

            // 디렉터가 사라지면 마을 불은 "항상 켜짐"으로 돌아간다. 안 되돌리면 마지막 프레임의
            // 밤 계수가 그대로 남아 플레이를 끈낸 뒤에도 창문이 꺼져 있다.
            NightFactor01 = 1f;

            RenderSettings.skybox = _originalSkybox;
            RenderSettings.sun = _originalSun;
            RenderSettings.fogColor = _originalFogColor;
            RenderSettings.fogDensity = _originalFogDensity;
            RenderSettings.ambientSkyColor = _originalAmbientSky;
            RenderSettings.ambientEquatorColor = _originalAmbientEquator;
            RenderSettings.ambientGroundColor = _originalAmbientGround;
            RenderSettings.ambientLight = _originalAmbientLight;

            if (_volume != null && _originalProfile != null)
            {
                _volume.sharedProfile = _originalProfile;
                _originalProfile = null;
            }
            if (_runtimeProfile != null)
            {
                if (Application.isPlaying) Destroy(_runtimeProfile);
                else DestroyImmediate(_runtimeProfile);
                _runtimeProfile = null;
            }

            if (_runtimeSkybox == null) return;
            if (Application.isPlaying) Destroy(_runtimeSkybox);
            else DestroyImmediate(_runtimeSkybox);
            _runtimeSkybox = null;
        }
    }
}
