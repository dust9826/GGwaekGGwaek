using UnityEngine;

namespace PPack
{
    /// <summary>두 AudioSource를 DSP 예약 재생해 튜토리얼 음악을 부드럽게 반복한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class TutorialMusicLooper : MonoBehaviour
    {
        [Header("Music")]
        [SerializeField] private AudioClip _music;
        [SerializeField] private AudioSource _sourceA;
        [SerializeField] private AudioSource _sourceB;

        [Header("Transition")]
        [SerializeField, Range(0f, 1f)] private float _volume = 0.22f;
        [SerializeField, Min(0f)] private float _fadeInDuration = 3f;
        [SerializeField, Min(0.1f)] private float _crossfadeDuration = 4.5f;
        [SerializeField, Min(0.1f)] private float _scheduleLeadTime = 1f;

        private AudioSource _current;
        private AudioSource _next;
        private double _currentStartDsp;
        private double _crossfadeStartDsp;
        private bool _nextScheduled;
        private bool _initialFadeActive;

        public AudioClip Music => _music;
        public bool IsPlaying => _current != null && _current.isPlaying;
        public bool IsCrossfading => _nextScheduled;

        private void Reset()
        {
            EnsureSources();
            ConfigureSources();
        }

        private void Awake()
        {
            EnsureSources();
            ConfigureSources();
        }

        private void OnEnable()
        {
            if (_music == null)
            {
                Debug.LogError($"{nameof(TutorialMusicLooper)} requires a music clip.", this);
                return;
            }

            StartPlayback();
        }

        private void Update()
        {
            if (_music == null || _current == null || _next == null) return;

            double now = AudioSettings.dspTime;
            if (!_nextScheduled)
            {
                UpdateInitialFade(now);

                double crossfadeDuration = EffectiveCrossfadeDuration();
                double nextCrossfadeStart = _currentStartDsp + _music.length - crossfadeDuration;
                if (now >= nextCrossfadeStart - _scheduleLeadTime)
                    ScheduleNextLoop(nextCrossfadeStart);
                return;
            }

            UpdateCrossfade(now);
        }

        private void OnDisable()
        {
            StopPlayback();
        }

        private void StartPlayback()
        {
            StopPlayback();
            _current = _sourceA;
            _next = _sourceB;
            _current.clip = _music;
            _current.volume = 0f;
            _current.timeSamples = 0;

            _currentStartDsp = AudioSettings.dspTime + 0.1d;
            _current.PlayScheduled(_currentStartDsp);
            _initialFadeActive = true;
        }

        private void ScheduleNextLoop(double crossfadeStartDsp)
        {
            _crossfadeStartDsp = System.Math.Max(crossfadeStartDsp,
                AudioSettings.dspTime + 0.05d);
            _next.Stop();
            _next.clip = _music;
            _next.volume = 0f;
            _next.timeSamples = 0;
            _next.PlayScheduled(_crossfadeStartDsp);
            _nextScheduled = true;
            _initialFadeActive = false;
        }

        private void UpdateInitialFade(double now)
        {
            if (!_initialFadeActive) return;
            if (now < _currentStartDsp)
            {
                _current.volume = 0f;
                return;
            }

            float duration = Mathf.Max(0.01f, _fadeInDuration);
            float progress = Mathf.Clamp01((float)((now - _currentStartDsp) / duration));
            _current.volume = Mathf.SmoothStep(0f, _volume, progress);
            if (progress >= 1f) _initialFadeActive = false;
        }

        private void UpdateCrossfade(double now)
        {
            double duration = EffectiveCrossfadeDuration();
            float progress = Mathf.Clamp01((float)((now - _crossfadeStartDsp) / duration));
            float angle = progress * Mathf.PI * 0.5f;
            _current.volume = _volume * Mathf.Cos(angle);
            _next.volume = _volume * Mathf.Sin(angle);

            if (progress < 1f) return;

            _current.Stop();
            (_current, _next) = (_next, _current);
            _current.volume = _volume;
            _next.volume = 0f;
            _currentStartDsp = _crossfadeStartDsp;
            _nextScheduled = false;
        }

        private double EffectiveCrossfadeDuration()
        {
            return Mathf.Clamp(_crossfadeDuration, 0.1f, Mathf.Max(0.1f, _music.length * 0.45f));
        }

        private void StopPlayback()
        {
            if (_sourceA != null)
            {
                _sourceA.Stop();
                _sourceA.volume = 0f;
            }

            if (_sourceB != null)
            {
                _sourceB.Stop();
                _sourceB.volume = 0f;
            }

            _nextScheduled = false;
            _initialFadeActive = false;
        }

        private void EnsureSources()
        {
            AudioSource[] sources = GetComponents<AudioSource>();
            if (sources.Length == 0) sources = new[] { gameObject.AddComponent<AudioSource>() };
            _sourceA ??= sources[0];

            if (_sourceB == null)
            {
                _sourceB = sources.Length > 1 ? sources[1] : gameObject.AddComponent<AudioSource>();
            }
        }

        private void ConfigureSources()
        {
            ConfigureSource(_sourceA);
            ConfigureSource(_sourceB);
        }

        private static void ConfigureSource(AudioSource source)
        {
            if (source == null) return;
            source.playOnAwake = false;
            source.loop = false;
            source.spatialBlend = 0f;
            source.dopplerLevel = 0f;
            source.pitch = 1f;
            source.volume = 0f;
            source.priority = 64;
        }

        private void OnValidate()
        {
            _volume = Mathf.Clamp01(_volume);
            _fadeInDuration = Mathf.Max(0f, _fadeInDuration);
            _crossfadeDuration = Mathf.Max(0.1f, _crossfadeDuration);
            _scheduleLeadTime = Mathf.Max(0.1f, _scheduleLeadTime);
        }
    }
}
