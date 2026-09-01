using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 활성 퀘스트 집의 기존 굴뚝 연기를 지붕과 같은 색으로 잠시 강조한다.
    /// 공유 머티리얼이나 퀘스트 판정은 바꾸지 않고, 끝날 때 원래 파티클 설정을 복원한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryHouseQuestSmoke : MonoBehaviour
    {
        private const float EmissionMultiplier = 1.35f;
        private const float SizeMultiplier = 1.1f;

        private ParticleSystem _smoke;
        private ParticleSystem.MinMaxGradient _originalStartColor;
        private ParticleSystem.MinMaxCurve _originalRateOverTime;
        private ParticleSystem.MinMaxCurve _originalStartSize;
        private Color _highlightColor;
        private ParticleSystem.Particle[] _livingParticles;
        private bool _configured;
        private bool _applied;

        public Color HighlightColor => _highlightColor;
        public bool IsApplied => _applied;
        public bool OwnsSmoke { get; private set; }

        public void Configure(ParticleSystem smoke, Color highlightColor, bool ownsSmoke = false)
        {
            if (smoke == null) return;

            _smoke = smoke;
            ParticleSystem.MainModule main = smoke.main;
            ParticleSystem.EmissionModule emission = smoke.emission;
            _originalStartColor = main.startColor;
            _originalStartSize = main.startSize;
            _originalRateOverTime = emission.rateOverTime;

            _highlightColor = highlightColor;
            _highlightColor.a = 0.92f;
            OwnsSmoke = ownsSmoke;
            _configured = true;

            Apply();
        }

        public void Restore()
        {
            if (!_applied || _smoke == null) return;

            ParticleSystem.MainModule main = _smoke.main;
            main.startColor = _originalStartColor;
            main.startSize = _originalStartSize;

            ParticleSystem.EmissionModule emission = _smoke.emission;
            emission.rateOverTime = _originalRateOverTime;

            RecolorLivingParticles(ResolveOriginalColor());
            _applied = false;
        }

        private void OnEnable()
        {
            if (_configured) Apply();
        }

        private void OnDisable() => Restore();

        private void OnDestroy() => Restore();

        private void Apply()
        {
            if (!_configured || _smoke == null || _applied) return;

            Color lightSmoke = Color.Lerp(_highlightColor, Color.white, 0.28f);
            lightSmoke.a = 0.78f;

            ParticleSystem.MainModule main = _smoke.main;
            main.startColor = new ParticleSystem.MinMaxGradient(_highlightColor, lightSmoke);
            main.startSize = Multiply(_originalStartSize, SizeMultiplier);

            ParticleSystem.EmissionModule emission = _smoke.emission;
            emission.rateOverTime = Multiply(_originalRateOverTime, EmissionMultiplier);

            RecolorLivingParticles(Color.Lerp(_highlightColor, lightSmoke, 0.45f));
            _applied = true;
        }

        private void RecolorLivingParticles(Color color)
        {
            if (_smoke == null) return;

            int capacity = Mathf.Max(1, _smoke.main.maxParticles);
            if (_livingParticles == null || _livingParticles.Length < capacity)
                _livingParticles = new ParticleSystem.Particle[capacity];

            int count = _smoke.GetParticles(_livingParticles);
            for (int index = 0; index < count; index++)
                _livingParticles[index].startColor = color;
            if (count > 0) _smoke.SetParticles(_livingParticles, count);
        }

        private Color ResolveOriginalColor()
        {
            return _originalStartColor.mode switch
            {
                ParticleSystemGradientMode.Color => _originalStartColor.color,
                ParticleSystemGradientMode.TwoColors =>
                    Color.Lerp(_originalStartColor.colorMin, _originalStartColor.colorMax, 0.5f),
                _ => Color.white
            };
        }

        private static ParticleSystem.MinMaxCurve Multiply(ParticleSystem.MinMaxCurve source, float multiplier)
        {
            switch (source.mode)
            {
                case ParticleSystemCurveMode.Constant:
                    return new ParticleSystem.MinMaxCurve(source.constant * multiplier);
                case ParticleSystemCurveMode.TwoConstants:
                    return new ParticleSystem.MinMaxCurve(
                        source.constantMin * multiplier,
                        source.constantMax * multiplier);
                default:
                    source.curveMultiplier *= multiplier;
                    return source;
            }
        }
    }
}
