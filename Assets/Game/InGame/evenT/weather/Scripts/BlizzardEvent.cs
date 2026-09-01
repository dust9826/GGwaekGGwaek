using System;
using UnityEngine;

namespace PPack
{
    public enum EBlizzardEventPhase
    {
        Idle,
        Warning,
        Active,
        Recovery,
        Cooldown
    }

    [DisallowMultipleComponent]
    public sealed class BlizzardEvent : MonoBehaviour
    {
        [Header("Lifecycle")]
        [SerializeField, Min(0f)] private float _warningDuration = 5f;
        [SerializeField, Min(0.01f)] private float _recoveryDuration = 3f;
        [SerializeField, Min(0f)] private float _cooldownDuration;
        [SerializeField, Min(0.01f)] private float _activeRampDuration = 2f;

        [Header("Moving Route")]
        [SerializeField] private SnowCpuStage _snowStage;
        [SerializeField, Min(0.1f)] private float _moveSpeedMps = 7f;
        [SerializeField, Min(0.5f)] private float _coreRadiusM = 14f;
        [SerializeField, Min(0f)] private float _edgeFeatherM = 5f;
        [SerializeField, Min(0.125f)] private float _candidateStepM = 2f;
        [SerializeField, Min(0.5f)] private float _minimumRegionSeparationM = 28f;

        [Header("Snow Accumulation")]
        [SerializeField, Min(0)] private int _snowfallAmountMm = 120;
        [SerializeField, Min(0)] private int _maximumSnowDepthMm = 300;
        [SerializeField] private int _boundarySeed = 1979;

        private EBlizzardEventPhase _phase = EBlizzardEventPhase.Idle;
        private BlizzardRoutePlan _route;
        private Vector2 _currentCenter;
        private float _phaseElapsed;
        private float _phaseDuration;
        private float _travelledM;
        private float _intensity;
        private byte[] _maximumExposureR8;
        private bool _running;

        public static event Action<BlizzardEvent> Registered;
        public static event Action<BlizzardEvent> Unregistered;

        public event Action<EBlizzardEventPhase> PhaseChanged;
        public event Action<float> IntensityChanged;

        public EBlizzardEventPhase Phase => _phase;
        public float Intensity => _intensity;
        public float PhaseProgress => _phaseDuration <= 0f ? 1f : Mathf.Clamp01(_phaseElapsed / _phaseDuration);
        public float AffectedRadiusM => _coreRadiusM + _edgeFeatherM;
        public Vector2 AffectedHalfExtents => Vector2.one * AffectedRadiusM;
        public BlizzardRoutePlan Route => _route;
        public bool IsVisibleOnMap => _phase is EBlizzardEventPhase.Warning
            or EBlizzardEventPhase.Active
            or EBlizzardEventPhase.Recovery;
        public Vector3 EventCenter => new Vector3(_currentCenter.x, transform.position.y, _currentCenter.y);

        private void Awake() => ResolveSnowStage();

        private void OnEnable()
        {
            ResolveSnowStage();
            Registered?.Invoke(this);
            SetIntensity(0f);
        }

        private void OnDisable()
        {
            _running = false;
            SetIntensity(0f);
            Unregistered?.Invoke(this);
        }

        private void Update()
        {
            if (!_running) return;

            float delta = Time.deltaTime;
            _phaseElapsed += delta;
            switch (_phase)
            {
                case EBlizzardEventPhase.Warning:
                    SetIntensity(Mathf.SmoothStep(0f, 0.18f, PhaseProgress));
                    if (_phaseElapsed >= _phaseDuration) BeginPhase(EBlizzardEventPhase.Active);
                    break;

                case EBlizzardEventPhase.Active:
                    UpdateActive(delta);
                    break;

                case EBlizzardEventPhase.Recovery:
                    SetIntensity(1f - Mathf.SmoothStep(0f, 1f, PhaseProgress));
                    if (_phaseElapsed >= _phaseDuration) BeginPhase(EBlizzardEventPhase.Cooldown);
                    break;

                case EBlizzardEventPhase.Cooldown:
                    if (_phaseElapsed >= _phaseDuration) Stop();
                    break;
            }
        }

        /// <summary>현재 CPU 눈의 부족량으로 경로를 골라 날짜 시스템이 예약한 눈폭풍을 시작한다.</summary>
        /// <summary>
        /// <b>경로만 정하고 시작하지는 않는다.</b> 멀티에서 서버가 경로를 골라
        /// <see cref="BlizzardNetHub"/> 로 복제한 뒤, 자기도 <see cref="Trigger(BlizzardRoutePlan)"/> 로
        /// 시작하기 위해 필요하다. 계획 파라미터가 이 컴포넌트의 private 필드라 여기에 둔다.
        /// </summary>
        public bool TryPlanRoute(out BlizzardRoutePlan route)
        {
            route = default;
            ResolveSnowStage();
            if (_snowStage == null || _snowStage.Field == null || !_snowStage.HasSimulationAuthority)
                return false;

            return BlizzardRoutePlanner.TryPlan(
                _snowStage.Field, _snowStage.InitialDepthMm, AffectedRadiusM,
                _candidateStepM, _minimumRegionSeparationM, _boundarySeed, out route);
        }

        public bool Trigger()
        {
            if (_running || _phase != EBlizzardEventPhase.Idle) return false;
            ResolveSnowStage();
            if (_snowStage == null || _snowStage.Field == null || !_snowStage.HasSimulationAuthority)
            {
                Debug.LogWarning($"{nameof(BlizzardEvent)}: 권위 CPU 눈 필드가 준비되지 않아 시작하지 못했다.", this);
                return false;
            }

            if (!BlizzardRoutePlanner.TryPlan(
                    _snowStage.Field,
                    _snowStage.InitialDepthMm,
                    AffectedRadiusM,
                    _candidateStepM,
                    _minimumRegionSeparationM,
                    _boundarySeed,
                    out BlizzardRoutePlan route))
            {
                Debug.LogWarning($"{nameof(BlizzardEvent)}: 서로 떨어진 유효 눈 구역 두 곳을 찾지 못했다.", this);
                return false;
            }

            return Trigger(route);
        }

        /// <summary>서버가 고른 경로를 받아 같은 이동 연출을 시작한다. 눈 필드 쓰기는 권위 피어에서만 된다.</summary>
        public bool Trigger(BlizzardRoutePlan route)
        {
            if (_running || _phase != EBlizzardEventPhase.Idle) return false;
            ResolveSnowStage();
            if (_snowStage == null || _snowStage.Field == null) return false;
            if (route.Direction.sqrMagnitude <= 0.0001f || route.TravelDistance <= 0f) return false;

            _route = route;
            _currentCenter = route.Start;
            _travelledM = 0f;
            _maximumExposureR8 = new byte[_snowStage.Field.HeightMm.Length];
            _running = true;
            BeginPhase(EBlizzardEventPhase.Warning);
            return true;
        }

        public void Stop()
        {
            _running = false;
            _maximumExposureR8 = null;
            SetIntensity(0f);
            SetPhase(EBlizzardEventPhase.Idle, 0f);
        }

        public float CalculateAreaWeight(Vector3 worldPosition)
        {
            float distance = Vector2.Distance(
                new Vector2(worldPosition.x, worldPosition.z),
                _currentCenter);
            if (distance <= _coreRadiusM) return 1f;
            if (_edgeFeatherM <= 0f || distance >= AffectedRadiusM) return 0f;

            float normalized = 1f - (distance - _coreRadiusM) / _edgeFeatherM;
            return Mathf.SmoothStep(0f, 1f, normalized);
        }

        private void UpdateActive(float delta)
        {
            float rampDuration = Mathf.Min(_activeRampDuration, _phaseDuration);
            float ramp = Mathf.Clamp01(_phaseElapsed / Mathf.Max(0.01f, rampDuration));
            SetIntensity(Mathf.SmoothStep(0.18f, 1f, ramp));

            Vector2 previous = _currentCenter;
            _travelledM = Mathf.Min(_route.TravelDistance, _travelledM + _moveSpeedMps * delta);
            _currentCenter = _route.Start + _route.Direction * _travelledM;
            transform.position = new Vector3(_currentCenter.x, transform.position.y, _currentCenter.y);

            _snowStage?.ApplyBlizzardSweep(
                previous,
                _currentCenter,
                _coreRadiusM,
                _edgeFeatherM,
                _snowfallAmountMm,
                _maximumSnowDepthMm,
                _boundarySeed,
                _maximumExposureR8);

            if (_travelledM >= _route.TravelDistance) BeginPhase(EBlizzardEventPhase.Recovery);
        }

        private void BeginPhase(EBlizzardEventPhase next)
        {
            float duration = next switch
            {
                EBlizzardEventPhase.Warning => _warningDuration,
                EBlizzardEventPhase.Active => _route.TravelDistance / Mathf.Max(0.1f, _moveSpeedMps),
                EBlizzardEventPhase.Recovery => _recoveryDuration,
                EBlizzardEventPhase.Cooldown => _cooldownDuration,
                _ => 0f
            };

            SetPhase(next, duration);
            if (next == EBlizzardEventPhase.Active)
            {
                _snowStage?.ApplyBlizzardSweep(
                    _currentCenter,
                    _currentCenter,
                    _coreRadiusM,
                    _edgeFeatherM,
                    _snowfallAmountMm,
                    _maximumSnowDepthMm,
                    _boundarySeed,
                    _maximumExposureR8);
            }
            else if (next == EBlizzardEventPhase.Cooldown)
            {
                SetIntensity(0f);
                if (_cooldownDuration <= 0f) Stop();
            }
        }

        private void SetPhase(EBlizzardEventPhase next, float duration)
        {
            _phase = next;
            _phaseElapsed = 0f;
            _phaseDuration = Mathf.Max(0f, duration);
            PhaseChanged?.Invoke(_phase);
        }

        private void SetIntensity(float value)
        {
            value = Mathf.Clamp01(value);
            if (Mathf.Approximately(_intensity, value)) return;
            _intensity = value;
            IntensityChanged?.Invoke(_intensity);
        }

        private void ResolveSnowStage()
        {
            if (_snowStage == null) _snowStage = FindAnyObjectByType<SnowCpuStage>();
        }

        private void OnValidate()
        {
            _moveSpeedMps = Mathf.Max(0.1f, _moveSpeedMps);
            _coreRadiusM = Mathf.Max(0.5f, _coreRadiusM);
            _edgeFeatherM = Mathf.Max(0f, _edgeFeatherM);
            _candidateStepM = Mathf.Max(SnowFieldGeometry.CellSizeM, _candidateStepM);
            _minimumRegionSeparationM = Mathf.Max(_coreRadiusM * 2f, _minimumRegionSeparationM);
            _snowfallAmountMm = Mathf.Max(0, _snowfallAmountMm);
            _maximumSnowDepthMm = Mathf.Max(0, _maximumSnowDepthMm);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 center = EventCenter;
            Gizmos.color = new Color(0.35f, 0.85f, 1f, 0.55f);
            Gizmos.DrawWireSphere(center, AffectedRadiusM);

            if (_route.Direction.sqrMagnitude <= 0.0001f) return;
            Vector3 end = new Vector3(
                _route.Start.x + _route.Direction.x * _route.TravelDistance,
                transform.position.y,
                _route.Start.y + _route.Direction.y * _route.TravelDistance);
            Gizmos.DrawLine(new Vector3(_route.Start.x, transform.position.y, _route.Start.y), end);
        }
    }
}
