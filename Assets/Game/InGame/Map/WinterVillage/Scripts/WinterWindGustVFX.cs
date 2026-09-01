using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class WinterWindGustVFX : MonoBehaviour
    {
        [Header("Particle Layers")]
        [SerializeField] private ParticleSystem _streaks;
        [SerializeField] private ParticleSystem _windFlakes;
        [SerializeField] private ParticleSystem _powderWisps;

        [Header("Wind Timing")]
        [SerializeField] private Vector2 _horizontalDirection = new Vector2(0.92f, 0.38f);
        [SerializeField, Range(0f, 14f)] private float _directionVariation = 7f;
        [SerializeField] private Vector2 _waitRange = new Vector2(4.2f, 7.4f);
        [SerializeField] private Vector2 _durationRange = new Vector2(2.7f, 4.1f);
        [SerializeField] private Vector2 _stormWaitRange = new Vector2(0.08f, 0.24f);
        [SerializeField] private Vector2 _stormDurationRange = new Vector2(5.2f, 7.4f);

        [Header("Gust Density")]
        [SerializeField] private Vector2 _streakEmissionRange = new Vector2(78f, 118f);
        [SerializeField] private Vector2 _flakeEmissionRange = new Vector2(42f, 68f);
        [SerializeField] private Vector2 _powderEmissionRange = new Vector2(8f, 15f);

        [Header("Layer Velocity")]
        [SerializeField] private Vector2 _speedRange = new Vector2(6.4f, 9.2f);
        [SerializeField] private float _streakFallSpeed = -0.72f;
        [SerializeField] private float _flakeFallSpeed = -1.25f;
        [SerializeField] private float _powderFallSpeed = -0.22f;

        [Header("Blizzard Response")]
        [SerializeField, Min(1f)] private float _stormEmissionMultiplier = 4.8f;
        [SerializeField, Min(1f)] private float _stormSpeedMultiplier = 1.9f;
        [SerializeField, Range(0f, 0.9f)] private float _stormStrengthFloor = 0.52f;

        private uint _randomState;
        private float _stateTime;
        private float _stateDuration;
        private float _peakStreakEmission;
        private float _peakFlakeEmission;
        private float _peakPowderEmission;
        private float _gustSpeed;
        private float _pulsePhase;
        private float _weatherIntensity;
        private bool _gusting;
        private Vector2 _activeDirection;

        public static float SharedGustStrength { get; private set; }
        public static Vector2 SharedWindDirection { get; private set; } = new Vector2(0.92f, 0.38f).normalized;
        public bool IsGusting => _gusting;
        public int GustCount { get; private set; }

        private void Awake()
        {
            if (_streaks == null)
            {
                _streaks = GetComponent<ParticleSystem>();
            }

            if (_windFlakes == null)
            {
                Transform child = transform.Find("WindFlakes");
                if (child != null) _windFlakes = child.GetComponent<ParticleSystem>();
            }

            if (_powderWisps == null)
            {
                Transform child = transform.Find("PowderWisps");
                if (child != null) _powderWisps = child.GetComponent<ParticleSystem>();
            }

            _horizontalDirection = _horizontalDirection.sqrMagnitude > 0.001f
                ? _horizontalDirection.normalized
                : Vector2.right;
            _activeDirection = _horizontalDirection;
            SharedWindDirection = _activeDirection;
            _randomState = unchecked((uint)GetEntityId().GetHashCode()) | 1u;
        }

        private void OnEnable()
        {
            SharedWindDirection = _activeDirection;
            SharedGustStrength = 0f;
            SetLayerEmissions(0f, 0f, 0f);
            _gusting = false;
            _stateTime = 0f;
            _stateDuration = 1.4f;

            PlayIfNeeded(_streaks);
            PlayIfNeeded(_windFlakes);
            PlayIfNeeded(_powderWisps);
        }

        private void Update()
        {
            if (_streaks == null)
            {
                return;
            }

            if (_weatherIntensity <= 0.001f)
            {
                EnterCalmState(false);
                return;
            }

            _stateTime += Time.deltaTime;
            if (_stateTime >= _stateDuration)
            {
                if (_gusting)
                {
                    BeginWait();
                }
                else
                {
                    BeginGust();
                }
            }

            if (!_gusting)
            {
                float stormCarry = Mathf.SmoothStep(0f, 1f, _weatherIntensity) * 0.18f;
                SharedGustStrength = stormCarry;
                SetLayerEmissions(
                    _streakEmissionRange.x * stormCarry,
                    _flakeEmissionRange.x * stormCarry,
                    _powderEmissionRange.x * stormCarry);
                return;
            }

            float progress = Mathf.Clamp01(_stateTime / Mathf.Max(_stateDuration, 0.01f));
            float envelope = Mathf.Sin(progress * Mathf.PI);
            envelope = Mathf.SmoothStep(0f, 1f, envelope);
            float pulse = Mathf.Lerp(0.9f, 1f, Mathf.Sin(progress * Mathf.PI * 4f + _pulsePhase) * 0.5f + 0.5f);
            envelope = Mathf.Clamp01(envelope * pulse);

            float stormFloor = _stormStrengthFloor * Mathf.SmoothStep(0f, 1f, _weatherIntensity);
            envelope = Mathf.Max(envelope, stormFloor);
            float emissionScale = Mathf.Lerp(1f, _stormEmissionMultiplier, _weatherIntensity);
            SharedGustStrength = Mathf.Clamp01(envelope);
            SetLayerEmissions(
                _peakStreakEmission * Mathf.Pow(envelope, 1.25f) * emissionScale,
                _peakFlakeEmission * envelope * emissionScale,
                _peakPowderEmission * Mathf.Pow(envelope, 1.55f) * emissionScale);
        }

        private void OnDisable()
        {
            SharedGustStrength = 0f;
            SetLayerEmissions(0f, 0f, 0f);
        }

        public void SetWeatherIntensity(float intensity)
        {
            float previous = _weatherIntensity;
            _weatherIntensity = Mathf.Clamp01(intensity);

            if (_weatherIntensity <= 0.001f)
            {
                EnterCalmState(true);
                return;
            }

            if (!_gusting && previous <= 0.001f)
            {
                BeginGust();
                return;
            }

            if (_gusting)
            {
                ApplyLayerVelocities();
            }
        }

        private void EnterCalmState(bool clearParticles)
        {
            _gusting = false;
            _stateTime = 0f;
            _stateDuration = float.PositiveInfinity;
            SharedGustStrength = 0f;
            SetLayerEmissions(0f, 0f, 0f);

            if (!clearParticles)
            {
                return;
            }

            ClearParticles(_streaks);
            ClearParticles(_windFlakes);
            ClearParticles(_powderWisps);
        }


        private void BeginWait()
        {
            _gusting = false;
            _stateTime = 0f;
            _stateDuration = Mathf.Lerp(
                RandomRange(_waitRange),
                RandomRange(_stormWaitRange),
                _weatherIntensity);
            SharedGustStrength = 0f;
            SetLayerEmissions(0f, 0f, 0f);
        }

        private void BeginGust()
        {
            _gusting = true;
            _stateTime = 0f;
            _stateDuration = Mathf.Lerp(
                RandomRange(_durationRange),
                RandomRange(_stormDurationRange),
                _weatherIntensity);
            _peakStreakEmission = RandomRange(_streakEmissionRange);
            _peakFlakeEmission = RandomRange(_flakeEmissionRange);
            _peakPowderEmission = RandomRange(_powderEmissionRange);
            _gustSpeed = RandomRange(_speedRange);
            _pulsePhase = RandomRange(new Vector2(0f, Mathf.PI * 2f));

            float variation = RandomRange(new Vector2(-_directionVariation, _directionVariation));
            Vector3 variedDirection = Quaternion.Euler(0f, variation, 0f)
                * new Vector3(_horizontalDirection.x, 0f, _horizontalDirection.y);
            _activeDirection = new Vector2(variedDirection.x, variedDirection.z).normalized;
            SharedWindDirection = _activeDirection;
            GustCount++;

            ApplyLayerVelocities();
        }

        private void ApplyLayerVelocities()
        {
            float speedScale = Mathf.Lerp(1f, _stormSpeedMultiplier, _weatherIntensity);
            SetVelocity(_streaks, _gustSpeed * speedScale, _streakFallSpeed);
            SetVelocity(_windFlakes, _gustSpeed * 0.62f * speedScale, _flakeFallSpeed);
            SetVelocity(_powderWisps, _gustSpeed * 0.43f * speedScale, _powderFallSpeed);
        }


        private void SetVelocity(ParticleSystem system, float speed, float fallSpeed)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.VelocityOverLifetimeModule velocity = system.velocityOverLifetime;
            velocity.enabled = true;
            velocity.space = ParticleSystemSimulationSpace.World;
            velocity.x = _activeDirection.x * speed;
            velocity.y = fallSpeed;
            velocity.z = _activeDirection.y * speed;
        }

        private void SetLayerEmissions(float streakRate, float flakeRate, float powderRate)
        {
            SetEmission(_streaks, streakRate);
            SetEmission(_windFlakes, flakeRate);
            SetEmission(_powderWisps, powderRate);
        }

        private static void SetEmission(ParticleSystem system, float rate)
        {
            if (system == null)
            {
                return;
            }

            ParticleSystem.EmissionModule emission = system.emission;
            emission.enabled = true;
            emission.rateOverTime = Mathf.Max(0f, rate);
        }

        private static void PlayIfNeeded(ParticleSystem system)
        {
            if (system != null && !system.isPlaying)
            {
                system.Play();
            }
        }

        private static void ClearParticles(ParticleSystem system)
        {
            if (system != null)
            {
                system.Clear(true);
            }
        }

        private float RandomRange(Vector2 range)
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            float value = (_randomState & 0x00FFFFFFu) / 16777215f;
            return Mathf.Lerp(range.x, range.y, value);
        }
    }
}
