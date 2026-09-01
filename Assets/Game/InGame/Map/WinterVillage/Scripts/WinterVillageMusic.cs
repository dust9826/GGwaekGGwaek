using System.Collections;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(AudioSource))]
    public sealed class WinterVillageMusic : MonoBehaviour
    {
        [Header("Tracks")]
        [SerializeField] private AudioClip _firstTrack;
        [SerializeField] private AudioClip _secondTrack;

        [Header("Transition")]
        [SerializeField, Range(0f, 1f)] private float _volume = 0.24f;
        [SerializeField, Min(0f)] private float _fadeInDuration = 2.5f;
        [SerializeField, Min(0f)] private float _fadeOutDuration = 4f;
        [SerializeField] private Vector2 _gapRange = new Vector2(3f, 6f);

        private AudioSource _source;
        private bool _playFirstTrack;

        private void Awake()
        {
            _source = GetComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.loop = false;
            _source.spatialBlend = 0f;
            _source.pitch = 1f;
            _source.volume = 0f;
        }

        private void OnEnable()
        {
            if (_firstTrack == null || _secondTrack == null)
            {
                Debug.LogError($"{nameof(WinterVillageMusic)} requires two music clips.", this);
                return;
            }

            _playFirstTrack = true;
            StartCoroutine(PlayTracks());
        }

        private void OnDisable()
        {
            StopAllCoroutines();

            if (_source == null)
            {
                return;
            }

            _source.Stop();
            _source.volume = 0f;
        }

        private IEnumerator PlayTracks()
        {
            while (true)
            {
                AudioClip track = _playFirstTrack ? _firstTrack : _secondTrack;
                _playFirstTrack = !_playFirstTrack;

                _source.clip = track;
                _source.volume = 0f;
                _source.Play();

                float fadeInDuration = Mathf.Min(_fadeInDuration, track.length);
                yield return FadeVolume(0f, _volume, fadeInDuration);

                float fullVolumeDuration = Mathf.Max(0f, track.length - fadeInDuration - _fadeOutDuration);
                if (fullVolumeDuration > 0f)
                {
                    yield return new WaitForSecondsRealtime(fullVolumeDuration);
                }

                float fadeOutDuration = Mathf.Min(_fadeOutDuration, track.length - fadeInDuration);
                yield return FadeVolume(_volume, 0f, fadeOutDuration);
                _source.Stop();

                float gap = Random.Range(_gapRange.x, _gapRange.y);
                yield return new WaitForSecondsRealtime(gap);
            }
        }

        private IEnumerator FadeVolume(float from, float to, float duration)
        {
            if (duration <= 0f)
            {
                _source.volume = to;
                yield break;
            }

            float elapsed = 0f;
            while (elapsed < duration)
            {
                elapsed += Time.unscaledDeltaTime;
                _source.volume = Mathf.Lerp(from, to, Mathf.Clamp01(elapsed / duration));
                yield return null;
            }

            _source.volume = to;
        }

        private void OnValidate()
        {
            _gapRange.x = Mathf.Max(0f, _gapRange.x);
            _gapRange.y = Mathf.Max(_gapRange.x, _gapRange.y);
        }
    }
}
