using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 실제로 눈이 제거될 때만 부드러운 마찰음을 재생한다.
    /// 판정은 <see cref="SnowPlowBlade"/> 가 소유하고 이 컴포넌트는 <c>SnowCleared</c> 이벤트를
    /// 표현할 뿐이다. 예전 공급자는 <c>SnowVehiclePad</c> 였는데 날이 그것을 런타임에 끈다(cs:261).
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource), typeof(AudioLowPassFilter))]
    public sealed class SnowPushAudio : MonoBehaviour
    {
        [SerializeField] private SnowPlowBlade _blade;
        [SerializeField] private AudioSource _source;

        [Header("편안한 청감")]
        [SerializeField, Range(0f, 1f)] private float _maxVolume = 0.24f;
        [SerializeField, Min(0.01f)] private float _attackSeconds = 0.09f;
        [SerializeField, Min(0.01f)] private float _releaseSeconds = 0.22f;
        [SerializeField, Min(0.02f)] private float _eventHoldSeconds = 0.14f;
        [SerializeField, Range(0.5f, 1.5f)] private float _minPitch = 0.96f;
        [SerializeField, Range(0.5f, 1.5f)] private float _maxPitch = 1.02f;
        [SerializeField, Min(1f)] private float _referenceRemovedCm = 2500f;

        private float _lastPushTime = float.NegativeInfinity;
        private float _lastIntensity;
        private float _volumeVelocity;

        public bool IsPushing => Time.time <= _lastPushTime + _eventHoldSeconds;
        public float CurrentVolume => _source != null ? _source.volume : 0f;

        private void Awake()
        {
            _blade ??= GetComponentInParent<SnowPlowBlade>();
            _source ??= GetComponent<AudioSource>();
            _source.volume = 0f;
        }

        private void OnEnable()
        {
            if (Application.isBatchMode)
            {
                enabled = false;
                return;
            }

            _blade ??= GetComponentInParent<SnowPlowBlade>();
            _source ??= GetComponent<AudioSource>();
            if (_blade == null || _source == null || _source.clip == null)
            {
                Debug.LogError($"{nameof(SnowPushAudio)}: {nameof(SnowPlowBlade)}, AudioSource or clip is missing.", this);
                enabled = false;
                return;
            }

            _blade.SnowCleared += OnSnowCleared;
        }

        private void OnDisable()
        {
            if (_blade != null)
            {
                _blade.SnowCleared -= OnSnowCleared;
            }
            if (_source != null)
            {
                _source.Stop();
                _source.volume = 0f;
            }
        }

        private void Update()
        {
            bool pushing = IsPushing;
            float targetVolume = pushing
                ? _maxVolume * Mathf.Lerp(0.55f, 1f, _lastIntensity)
                : 0f;
            float smoothTime = pushing ? _attackSeconds : _releaseSeconds;
            _source.volume = Mathf.SmoothDamp(
                _source.volume,
                targetVolume,
                ref _volumeVelocity,
                smoothTime,
                Mathf.Infinity,
                Time.deltaTime);

            if (!pushing && _source.volume <= 0.002f && _source.isPlaying)
            {
                _source.Stop();
                _source.volume = 0f;
                _volumeVelocity = 0f;
            }
        }

        private void OnSnowCleared(SnowStampArea area)
        {
            _lastPushTime = Time.time;
            _lastIntensity = Mathf.Sqrt(Mathf.Clamp01(_blade.LastRemovedCm / _referenceRemovedCm));
            if (_source.isPlaying)
            {
                return;
            }

            _source.pitch = Random.Range(_minPitch, _maxPitch);
            _source.Play();
        }

        private void OnValidate()
        {
            _maxPitch = Mathf.Max(_minPitch, _maxPitch);
        }
    }
}
