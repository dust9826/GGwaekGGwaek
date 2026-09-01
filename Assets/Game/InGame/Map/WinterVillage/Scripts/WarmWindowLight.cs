using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class WarmWindowLight : MonoBehaviour
    {
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");

        [SerializeField] private Renderer[] _houseRenderers;
        [SerializeField] private Light[] _windowLights;
        [SerializeField, ColorUsage(false, true)] private Color _emissionColor = new Color(1f, 0.60f, 0.20f, 1f);
        [SerializeField, Min(0f)] private float _emissionIntensity = 3.2f;
        [SerializeField, Min(0f)] private float _baseLightIntensity = 0.75f;
        [SerializeField, Range(0f, 0.08f)] private float _variation = 0.018f;
        [SerializeField, Min(0.001f)] private float _variationSpeed = 0.075f;

        [Tooltip("대낮에 남기는 밝기. 0이 아니어야 낮에도 창이 죽지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _dayLevel = 0.10f;

        [Tooltip("한밤의 밝기 배율. 1보다 크면 밤에 창이 더 따뜻하게 타오른다.")]
        [SerializeField, Min(0f)] private float _nightBoost = 1.7f;

        private MaterialPropertyBlock _propertyBlock;
        private float _seed;

        public float CurrentFactor { get; private set; } = 1f;

        private void Awake()
        {
            _seed = Mathf.Abs(GetEntityId().GetHashCode() * 0.01173f);
        }

        private void Update()
        {
            float noise = Mathf.Clamp01(Mathf.PerlinNoise(_seed, Time.time * _variationSpeed));
            CurrentFactor = 1f + (noise - 0.5f) * 2f * _variation;
            // 낮에는 거의 꺼지고 밤에 제대로 타오른다. 이게 없으면 정오에도 창이 똑같이 빛나서
            // 밤이 밤으로 읽히지 않는다.
            Apply(CurrentFactor * Mathf.Lerp(_dayLevel, _nightBoost,
                Mathf.Clamp01(TimeOfDayDirector.NightFactor01)));
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

            if (_houseRenderers != null)
            {
                foreach (Renderer houseRenderer in _houseRenderers)
                {
                    if (houseRenderer == null)
                    {
                        continue;
                    }

                    houseRenderer.GetPropertyBlock(_propertyBlock);
                    _propertyBlock.SetColor(EmissionColorId, _emissionColor * (_emissionIntensity * factor));
                    houseRenderer.SetPropertyBlock(_propertyBlock);
                }
            }

            if (_windowLights == null)
            {
                return;
            }

            foreach (Light windowLight in _windowLights)
            {
                if (windowLight != null)
                {
                    windowLight.intensity = _baseLightIntensity * factor;
                }
            }
        }
    }
}
