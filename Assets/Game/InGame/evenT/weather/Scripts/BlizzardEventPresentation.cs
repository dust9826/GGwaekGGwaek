using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class BlizzardEventPresentation : MonoBehaviour
    {
        [SerializeField] private BlizzardEvent _event;
        [SerializeField] private WinterSnowfallFollow _snowfall;
        [SerializeField] private WinterWindGustVFX _wind;
        [SerializeField, Min(1f)] private float _snowEmissionMultiplier = 7.5f;
        [SerializeField, Min(1f)] private float _groundMistEmissionMultiplier = 12f;
        [SerializeField, Min(1f)] private float _snowVelocityMultiplier = 1.9f;
        [SerializeField, Min(1f)] private float _snowSimulationSpeedMultiplier = 1.3f;
        [SerializeField, Min(1f)] private float _snowMaxParticleMultiplier = 3f;
        [SerializeField] private Color _stormFogColor = new Color(0.68f, 0.73f, 0.81f, 1f);
        [SerializeField, Min(1f)] private float _fogDensityMultiplier = 4.5f;
        [SerializeField, Range(0.15f, 1f)] private float _linearFogEndMultiplier = 0.32f;

        [Header("Local Affected Area")]
        [SerializeField] private Transform _localObserver;
        [SerializeField, Min(0.05f)] private float _areaTransitionSeconds = 0.75f;

        [Header("Blizzard Wind Audio")]
        [SerializeField] private AudioSource _windAudio;
        [SerializeField] private AudioClip _windLoop;
        [SerializeField, Range(0f, 1f)] private float _windMaxVolume = 0.55f;
        [SerializeField, Min(0.05f)] private float _windFadeInSeconds = 2.2f;
        [SerializeField, Min(0.05f)] private float _windFadeOutSeconds = 3.2f;

        private ParticleSystem[] _snowSystems;
        private float[] _baseEmissionMultipliers;
        private float[] _baseVelocityMultipliers;
        private float[] _baseSimulationSpeeds;
        private int[] _baseMaxParticles;

        private bool _baseFogEnabled;
        private Color _baseFogColor;
        private FogMode _baseFogMode;
        private float _baseFogDensity;
        private float _baseFogStart;
        private float _baseFogEnd;
        private bool _fogCaptured;
        private float _eventIntensity;
        private float _localAreaBlend;
        private float _currentIntensity;
        private float _targetWindVolume;

        public float LocalPresentationIntensity => _currentIntensity;

        private void Awake()
        {
            ResolveReferences();
            CacheSnowfall();
            ConfigureWindAudio();
        }

        private void OnEnable()
        {
            ResolveReferences();
            CacheSnowfall();
            CaptureFog();
            ConfigureWindAudio();

            if (_event == null)
            {
                Debug.LogError($"{nameof(BlizzardEventPresentation)} requires a {nameof(BlizzardEvent)}.", this);
                enabled = false;
                return;
            }

            _event.IntensityChanged += SetEventIntensity;
            _localAreaBlend = CalculateLocalAreaWeight();
            SetEventIntensity(_event.Intensity);
        }

        private void OnDisable()
        {
            if (_event != null)
            {
                _event.IntensityChanged -= SetEventIntensity;
            }

            _eventIntensity = 0f;
            _localAreaBlend = 0f;
            RestorePresentation();
            StopWindAudioImmediately();
        }

        private void Update()
        {
            ResolveLocalObserver();
            float targetAreaBlend = CalculateLocalAreaWeight();
            _localAreaBlend = Mathf.MoveTowards(
                _localAreaBlend,
                targetAreaBlend,
                Time.deltaTime / Mathf.Max(0.05f, _areaTransitionSeconds));

            float localIntensity = _eventIntensity * _localAreaBlend;
            if (!Mathf.Approximately(localIntensity, _currentIntensity))
            {
                ApplyIntensity(localIntensity);
            }

            UpdateWindAudio();

            if (_currentIntensity <= 0.001f)
            {
                return;
            }

            // A blizzard never holds perfectly still. This restrained variation keeps the
            // whiteout alive without producing distracting brightness flashes.
            float fogPulse = Mathf.Lerp(0.94f, 1.06f, Mathf.PerlinNoise(Time.time * 0.16f, 0.37f));
            ApplyFog(_currentIntensity, fogPulse);
        }

        private void ResolveReferences()
        {
            if (_event == null) _event = GetComponent<BlizzardEvent>();
            if (_wind == null) _wind = FindAnyObjectByType<WinterWindGustVFX>();
            ResolveLocalObserver();

            if (_snowfall != null)
            {
                return;
            }

            // Both the snowfall and wind prefabs follow the camera. FindAnyObjectByType can
            // therefore pick the wind prefab and leave the actual snowfall unscaled.
            WinterSnowfallFollow[] followers = FindObjectsByType<WinterSnowfallFollow>(FindObjectsSortMode.None);
            foreach (WinterSnowfallFollow follower in followers)
            {
                if (follower != null && follower.GetComponent<WinterWindGustVFX>() == null)
                {
                    _snowfall = follower;
                    break;
                }
            }
        }

        private void ResolveLocalObserver()
        {
            if (_localObserver != null)
            {
                return;
            }

            AudioListener listener = FindAnyObjectByType<AudioListener>();
            if (listener != null && listener.isActiveAndEnabled)
            {
                _localObserver = listener.transform;
                return;
            }

            Camera mainCamera = Camera.main;
            if (mainCamera != null)
            {
                _localObserver = mainCamera.transform;
            }
        }

        private float CalculateLocalAreaWeight()
        {
            if (_event == null || _localObserver == null)
            {
                return 0f;
            }

            return _event.CalculateAreaWeight(_localObserver.position);
        }

        private void SetEventIntensity(float intensity)
        {
            _eventIntensity = Mathf.Clamp01(intensity);
            ApplyIntensity(_eventIntensity * _localAreaBlend);
        }

        private void CacheSnowfall()
        {
            if (_snowfall == null || _snowSystems != null)
            {
                return;
            }

            _snowSystems = _snowfall.GetComponentsInChildren<ParticleSystem>(true);
            _baseEmissionMultipliers = new float[_snowSystems.Length];
            _baseVelocityMultipliers = new float[_snowSystems.Length];
            _baseSimulationSpeeds = new float[_snowSystems.Length];
            _baseMaxParticles = new int[_snowSystems.Length];
            for (int index = 0; index < _snowSystems.Length; index++)
            {
                ParticleSystem system = _snowSystems[index];
                _baseEmissionMultipliers[index] = system.emission.rateOverTimeMultiplier;
                _baseVelocityMultipliers[index] = system.velocityOverLifetime.speedModifierMultiplier;
                _baseSimulationSpeeds[index] = system.main.simulationSpeed;
                _baseMaxParticles[index] = system.main.maxParticles;
            }
        }

        private void CaptureFog()
        {
            if (_fogCaptured)
            {
                return;
            }

            _baseFogEnabled = RenderSettings.fog;
            _baseFogColor = RenderSettings.fogColor;
            _baseFogMode = RenderSettings.fogMode;
            _baseFogDensity = RenderSettings.fogDensity;
            _baseFogStart = RenderSettings.fogStartDistance;
            _baseFogEnd = RenderSettings.fogEndDistance;
            _fogCaptured = true;
        }

        private void ApplyIntensity(float intensity)
        {
            intensity = Mathf.Clamp01(intensity);
            _currentIntensity = intensity;

            if (_snowSystems == null)
            {
                ResolveReferences();
                CacheSnowfall();
            }

            if (_snowSystems != null)
            {
                for (int index = 0; index < _snowSystems.Length; index++)
                {
                    ParticleSystem system = _snowSystems[index];
                    if (system == null) continue;

                    float targetEmission = system.name.Contains("Mist")
                        ? _groundMistEmissionMultiplier
                        : _snowEmissionMultiplier;
                    float emissionScale = Mathf.Lerp(1f, targetEmission, intensity);
                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.rateOverTimeMultiplier = _baseEmissionMultipliers[index] * emissionScale;

                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    velocity.speedModifierMultiplier = _baseVelocityMultipliers[index]
                        * Mathf.Lerp(1f, _snowVelocityMultiplier, intensity);

                    ParticleSystem.MainModule main = system.main;
                    main.simulationSpeed = _baseSimulationSpeeds[index]
                        * Mathf.Lerp(1f, _snowSimulationSpeedMultiplier, intensity);
                    main.maxParticles = Mathf.CeilToInt(_baseMaxParticles[index]
                        * Mathf.Lerp(1f, _snowMaxParticleMultiplier, intensity));
                }
            }

            if (_wind == null)
            {
                ResolveReferences();
            }

            if (_wind != null)
            {
                _wind.SetWeatherIntensity(intensity);
            }

            SetWindAudioIntensity(intensity);

            ApplyFog(intensity, 1f);
        }

        private void ConfigureWindAudio()
        {
            if (_windAudio == null)
            {
                _windAudio = GetComponent<AudioSource>();
            }

            if (_windAudio == null)
            {
                return;
            }

            _windAudio.playOnAwake = false;
            _windAudio.loop = true;
            _windAudio.spatialBlend = 0f;
            _windAudio.dopplerLevel = 0f;
            _windAudio.clip = _windLoop;
            _windAudio.volume = 0f;
        }

        private void SetWindAudioIntensity(float intensity)
        {
            float easedIntensity = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(intensity));
            _targetWindVolume = easedIntensity * _windMaxVolume;

            if (_windAudio != null
                && _windAudio.clip != null
                && _targetWindVolume > 0.001f
                && !_windAudio.isPlaying)
            {
                _windAudio.Play();
            }
        }

        private void UpdateWindAudio()
        {
            if (_windAudio == null)
            {
                return;
            }

            float fadeSeconds = _targetWindVolume > _windAudio.volume
                ? _windFadeInSeconds
                : _windFadeOutSeconds;
            float fadeSpeed = _windMaxVolume / Mathf.Max(0.05f, fadeSeconds);
            _windAudio.volume = Mathf.MoveTowards(
                _windAudio.volume,
                _targetWindVolume,
                fadeSpeed * Time.deltaTime);

            if (_targetWindVolume <= 0.001f
                && _windAudio.volume <= 0.001f
                && _windAudio.isPlaying)
            {
                _windAudio.Stop();
                _windAudio.volume = 0f;
            }
        }

        private void StopWindAudioImmediately()
        {
            _targetWindVolume = 0f;
            if (_windAudio == null)
            {
                return;
            }

            _windAudio.Stop();
            _windAudio.volume = 0f;
        }

        private void ApplyFog(float intensity, float pulse)
        {
            if (!_fogCaptured)
            {
                return;
            }

            RenderSettings.fog = _baseFogEnabled || intensity > 0.001f;
            RenderSettings.fogColor = Color.Lerp(_baseFogColor, _stormFogColor, intensity);

            if (_baseFogMode == FogMode.Linear)
            {
                float targetEnd = Mathf.Max(_baseFogStart + 18f, _baseFogEnd * _linearFogEndMultiplier * pulse);
                RenderSettings.fogStartDistance = Mathf.Lerp(_baseFogStart, 0f, intensity);
                RenderSettings.fogEndDistance = Mathf.Lerp(_baseFogEnd, targetEnd, intensity);
            }
            else
            {
                float targetDensity = Mathf.Max(_baseFogDensity * _fogDensityMultiplier, 0.042f) * pulse;
                RenderSettings.fogDensity = Mathf.Lerp(_baseFogDensity, targetDensity, intensity);
            }
        }

        private void RestorePresentation()
        {
            if (_snowSystems != null)
            {
                for (int index = 0; index < _snowSystems.Length; index++)
                {
                    ParticleSystem system = _snowSystems[index];
                    if (system == null) continue;

                    ParticleSystem.EmissionModule emission = system.emission;
                    emission.rateOverTimeMultiplier = _baseEmissionMultipliers[index];

                    ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
                    velocity.speedModifierMultiplier = _baseVelocityMultipliers[index];

                    ParticleSystem.MainModule main = system.main;
                    main.simulationSpeed = _baseSimulationSpeeds[index];
                    main.maxParticles = _baseMaxParticles[index];
                }
            }

            if (_wind != null)
            {
                _wind.SetWeatherIntensity(0f);
            }

            _targetWindVolume = 0f;

            if (!_fogCaptured)
            {
                return;
            }

            RenderSettings.fog = _baseFogEnabled;
            RenderSettings.fogColor = _baseFogColor;
            RenderSettings.fogMode = _baseFogMode;
            RenderSettings.fogDensity = _baseFogDensity;
            RenderSettings.fogStartDistance = _baseFogStart;
            RenderSettings.fogEndDistance = _baseFogEnd;
            _currentIntensity = 0f;
        }
    }
}
