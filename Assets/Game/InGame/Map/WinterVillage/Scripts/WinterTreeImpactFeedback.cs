using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class WinterTreeImpactFeedback : MonoBehaviour
    {
        [SerializeField] private MMF_Player _feedbacks;
        [SerializeField] private ParticleSystem _powderSnow;
        [SerializeField] private ParticleSystem _snowClumps;

        [Header("Snow Burst")]
        [SerializeField, Min(0)] private int _powderCountAtFullImpact = 30;
        [SerializeField, Min(0)] private int _clumpCountAtFullImpact = 8;
        [SerializeField, Range(0f, 0.5f)] private float _globalCooldown = 0.06f;

        private static WinterTreeImpactFeedback _instance;
        private MMF_CameraShake _cameraShake;
        private MMF_Sound _woodImpactSound;
        private float _nextPlayTime;

        public int PlayCount { get; private set; }
        public int LastSnowEmitCount { get; private set; }
        public float LastStrength { get; private set; }

        private void Awake()
        {
            _instance = this;
            if (_feedbacks == null)
            {
                _feedbacks = GetComponent<MMF_Player>();
            }

            if (_feedbacks != null)
            {
                _cameraShake = _feedbacks.GetFeedbackOfType<MMF_CameraShake>();
                _woodImpactSound = _feedbacks.GetFeedbackOfType<MMF_Sound>();
            }
        }

        private void OnDestroy()
        {
            if (_instance == this)
            {
                _instance = null;
            }
        }

        public static void TryPlay(
            Vector3 canopyPosition,
            Vector3 impactPosition,
            float canopyRadius,
            Vector3 impactDirection,
            float normalizedStrength)
        {
            if (_instance != null)
            {
                _instance.PlayImpact(
                    canopyPosition,
                    impactPosition,
                    canopyRadius,
                    impactDirection,
                    normalizedStrength);
            }
        }

        private void PlayImpact(
            Vector3 canopyPosition,
            Vector3 impactPosition,
            float canopyRadius,
            Vector3 impactDirection,
            float normalizedStrength)
        {
            if (Time.time < _nextPlayTime)
            {
                return;
            }

            _nextPlayTime = Time.time + _globalCooldown;
            float strength = Mathf.Clamp01(normalizedStrength);
            Vector3 horizontalImpact = Vector3.ProjectOnPlane(impactDirection, Vector3.up).normalized;
            Vector2 wind = WinterWindGustVFX.SharedWindDirection;
            Vector3 drift = new Vector3(wind.x, 0f, wind.y) * 0.38f + horizontalImpact * 0.22f;

            int powderCount = Mathf.RoundToInt(Mathf.Lerp(10f, _powderCountAtFullImpact, strength));
            int clumpCount = Mathf.RoundToInt(Mathf.Lerp(3f, _clumpCountAtFullImpact, strength));
            EmitSnow(_powderSnow, canopyPosition, canopyRadius, drift, powderCount, false);
            EmitSnow(_snowClumps, canopyPosition, canopyRadius * 0.82f, drift, clumpCount, true);

            if (_cameraShake != null)
            {
                float amplitude = Mathf.Lerp(0.012f, 0.04f, strength);
                _cameraShake.CameraShakeProperties = new MMCameraShakeProperties(
                    Mathf.Lerp(0.08f, 0.13f, strength),
                    amplitude,
                    Mathf.Lerp(18f, 26f, strength),
                    amplitude,
                    amplitude * 0.55f,
                    amplitude * 0.8f);
            }

            if (_woodImpactSound != null)
            {
                float volume = Mathf.Lerp(0.3f, 0.52f, strength);
                float centerPitch = Mathf.Lerp(1.01f, 0.96f, strength);
                _woodImpactSound.MinVolume = volume * 0.88f;
                _woodImpactSound.MaxVolume = volume;
                _woodImpactSound.MinPitch = centerPitch - 0.035f;
                _woodImpactSound.MaxPitch = centerPitch + 0.035f;
            }

            if (_feedbacks != null)
            {
                // Feel intensity is intentionally left at 1. Concrete shake and sound values carry impact strength.
                _feedbacks.PlayFeedbacks(impactPosition);
            }

            LastStrength = strength;
            LastSnowEmitCount = powderCount + clumpCount;
            PlayCount++;
        }

        private static void EmitSnow(
            ParticleSystem system,
            Vector3 canopyPosition,
            float canopyRadius,
            Vector3 drift,
            int count,
            bool clump)
        {
            if (system == null || count <= 0)
            {
                return;
            }

            if (!system.isPlaying)
            {
                system.Play(false);
            }

            ParticleSystem.EmitParams parameters = new ParticleSystem.EmitParams();
            for (int i = 0; i < count; i++)
            {
                Vector2 spread = Random.insideUnitCircle * canopyRadius;
                parameters.position = canopyPosition + new Vector3(spread.x, Random.Range(-0.12f, 0.2f), spread.y);
                float verticalSpeed = clump
                    ? Random.Range(-0.45f, -0.12f)
                    : Random.Range(-0.05f, 0.18f);
                parameters.velocity = drift
                    + new Vector3(Random.Range(-0.22f, 0.22f), verticalSpeed, Random.Range(-0.22f, 0.22f));
                parameters.startLifetime = clump ? Random.Range(1.25f, 2f) : Random.Range(0.8f, 1.45f);
                parameters.startSize = clump ? Random.Range(0.09f, 0.18f) : Random.Range(0.025f, 0.075f);
                parameters.startColor = clump
                    ? new Color(0.94f, 0.98f, 1f, Random.Range(0.85f, 1f))
                    : new Color(0.9f, 0.97f, 1f, Random.Range(0.48f, 0.82f));
                system.Emit(parameters, 1);
            }
        }
    }
}
