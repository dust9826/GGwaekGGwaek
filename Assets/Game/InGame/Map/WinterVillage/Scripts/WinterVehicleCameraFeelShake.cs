using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEngine;

namespace PPack
{
    [DefaultExecutionOrder(10000)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(VehicleCamera))]
    public sealed class WinterVehicleCameraFeelShake : MonoBehaviour
    {
        [SerializeField, Range(0f, 20f)] private float _rotationMultiplier = 8f;

        private float _startedAt;
        private float _duration;
        private float _frequency;
        private Vector3 _amplitude;
        private float _seed;
        private bool _useUnscaledTime;

        public int ShakeCount { get; private set; }
        public bool IsShaking => ElapsedTime < _duration;
        public Vector3 CurrentOffset { get; private set; }
        public float PeakOffsetMagnitude { get; private set; }

        private float CurrentTime => _useUnscaledTime ? Time.unscaledTime : Time.time;
        private float ElapsedTime => CurrentTime - _startedAt;

        private void OnEnable()
        {
            MMCameraShakeEvent.Register(OnCameraShake);
        }

        private void OnDisable()
        {
            MMCameraShakeEvent.Unregister(OnCameraShake);
            CurrentOffset = Vector3.zero;
        }

        private void OnCameraShake(
            float duration,
            float amplitude,
            float frequency,
            float amplitudeX,
            float amplitudeY,
            float amplitudeZ,
            bool infinite,
            MMChannelData channelData,
            bool useUnscaledTime)
        {
            _useUnscaledTime = useUnscaledTime;
            _startedAt = CurrentTime;
            _duration = infinite ? 10f : Mathf.Max(0.01f, duration);
            _frequency = Mathf.Max(1f, frequency);
            _amplitude = (amplitudeX != 0f || amplitudeY != 0f || amplitudeZ != 0f)
                ? new Vector3(amplitudeX, amplitudeY, amplitudeZ)
                : Vector3.one * amplitude;
            _seed = Random.Range(0f, 100f);
            PeakOffsetMagnitude = 0f;
            ShakeCount++;
        }

        private void LateUpdate()
        {
            float elapsed = ElapsedTime;
            if (elapsed >= _duration)
            {
                CurrentOffset = Vector3.zero;
                return;
            }

            float fade = 1f - Mathf.Clamp01(elapsed / _duration);
            fade *= fade;
            float sampleTime = CurrentTime * _frequency;
            Vector3 noise = new Vector3(
                Mathf.PerlinNoise(_seed, sampleTime) * 2f - 1f,
                Mathf.PerlinNoise(_seed + 17.3f, sampleTime) * 2f - 1f,
                Mathf.PerlinNoise(_seed + 41.7f, sampleTime) * 2f - 1f);

            CurrentOffset = Vector3.Scale(noise, _amplitude) * fade;
            PeakOffsetMagnitude = Mathf.Max(PeakOffsetMagnitude, CurrentOffset.magnitude);
            transform.position += transform.TransformVector(CurrentOffset);

            float roll = noise.x * _amplitude.x * _rotationMultiplier * fade;
            float pitch = noise.y * _amplitude.y * _rotationMultiplier * 0.55f * fade;
            transform.rotation *= Quaternion.Euler(pitch, 0f, roll);
        }
    }
}
