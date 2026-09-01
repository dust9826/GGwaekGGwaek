using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class LanternFlicker : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Light _light;
        [SerializeField] private Renderer _emissiveRenderer;
        [SerializeField, ColorUsage(false, true)] private Color _emissionColor = new Color(1f, 0.66f, 0.18f, 1f);
        [SerializeField, Min(0f)] private float _emissionIntensity = 4.5f;
        [SerializeField, Min(0f)] private float _baseLightIntensity = 1.7f;
        [SerializeField, Range(0f, 0.25f)] private float _ambientVariation = 0.055f;
        [SerializeField, Min(0.01f)] private float _noiseSpeed = 1.35f;
        [SerializeField] private Vector2 _dipInterval = new Vector2(3.5f, 8f);
        [SerializeField] private Vector2 _dipDuration = new Vector2(0.08f, 0.2f);
        [SerializeField] private Vector2 _dipDepth = new Vector2(0.28f, 0.58f);

        [Tooltip("대낮에 남기는 밝기. 낮에도 완전히 끄지 않아야 등이 거기 있다는 게 읽힌다.")]
        [SerializeField, Range(0f, 1f)] private float _dayLevel = 0.12f;

        [Tooltip("한밤의 밝기 배율. 1보다 크면 밤에 더 세게 타오른다.")]
        [SerializeField, Min(0f)] private float _nightBoost = 1.6f;

        private MaterialPropertyBlock _propertyBlock;
        private float _seed;
        private float _baseRange;
        private float _nextDipTime;
        private float _dipStartTime;
        private float _dipEndTime;
        private float _activeDipDepth;

        public float CurrentFactor { get; private set; } = 1f;

        private void Awake()
        {
            _seed = Mathf.Abs(GetEntityId().GetHashCode() * 0.01731f);
            _baseRange = _light != null ? _light.range : 0f;
            ScheduleNextDip(Time.time);
        }

        private void OnEnable()
        {
            if (_light != null)
            {
                _baseRange = _light.range;
            }
            ScheduleNextDip(Time.time);
        }

        private void Update()
        {
            float now = Time.time;
            if (now >= _nextDipTime && now >= _dipEndTime)
            {
                _dipStartTime = now;
                _dipEndTime = now + Random.Range(_dipDuration.x, _dipDuration.y);
                _activeDipDepth = Random.Range(_dipDepth.x, _dipDepth.y);
                ScheduleNextDip(_dipEndTime);
            }

            float noise = Mathf.Clamp01(Mathf.PerlinNoise(_seed, now * _noiseSpeed));
            float ambientFactor = 1f + (noise - 0.5f) * 2f * _ambientVariation;
            float dipFactor = 1f;
            if (now < _dipEndTime)
            {
                float duration = Mathf.Max(0.01f, _dipEndTime - _dipStartTime);
                float progress = Mathf.Clamp01((now - _dipStartTime) / duration);
                float envelope = Mathf.Sin(progress * Mathf.PI);
                dipFactor -= envelope * _activeDipDepth;
            }

            CurrentFactor = Mathf.Clamp(ambientFactor * dipFactor, 0.15f, 1.12f);
            // 밤에만 제대로 타오른다. 낮에도 같은 세기로 켜져 있으면 불이 켜졌다는 게 읽히지 않는다.
            Apply(CurrentFactor * DayNightLevel());
        }

        private void OnDisable()
        {
            CurrentFactor = 1f;
            Apply(CurrentFactor);
        }

        private void Apply(float factor)
        {
            if (_propertyBlock == null)
            {
                _propertyBlock = new MaterialPropertyBlock();
            }

            if (_light != null)
            {
                _light.intensity = _baseLightIntensity * factor;
                _light.range = _baseRange * Mathf.Lerp(0.92f, 1f, factor);
            }

            if (_emissiveRenderer == null)
            {
                return;
            }

            _emissiveRenderer.GetPropertyBlock(_propertyBlock);
            _propertyBlock.SetColor(EmissionColorId, _emissionColor * (_emissionIntensity * factor));
            _emissiveRenderer.SetPropertyBlock(_propertyBlock);
        }

        private float DayNightLevel() =>
            Mathf.Lerp(_dayLevel, _nightBoost, Mathf.Clamp01(TimeOfDayDirector.NightFactor01));

        private void ScheduleNextDip(float fromTime)
        {
            float minimum = Mathf.Max(0.25f, Mathf.Min(_dipInterval.x, _dipInterval.y));
            float maximum = Mathf.Max(minimum, Mathf.Max(_dipInterval.x, _dipInterval.y));
            _nextDipTime = fromTime + Random.Range(minimum, maximum);
        }
    }
}
