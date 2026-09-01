using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 눈덩이가 지면에 닿은 채 굴러가는 동안 마찰음을 낸다. 클립은 제설차 시절
    /// <see cref="SnowPushAudio"/> 가 쓰던 <c>SnowPush_Soft_CC0.wav</c> 를 그대로 재사용한다 —
    /// 6.9 초 지속음이라 원샷으로는 못 쓰지만 <b>굴리기 루프에는 그래서 맞다</b>
    /// (<c>Snow/AGENTS.md</c> 의 클립 유래 참고).
    ///
    /// <para><b>전용 <see cref="AudioSource"/> 를 쓴다.</b> 공에 이미 붙어 있는 소스는
    /// <see cref="SnowBallCoopFeedback"/> 의 <c>MMF_AudioSource</c> 것이라, 공유하면 협동 성공이
    /// 날 때마다 굴리기 루프가 끊긴다.</para>
    ///
    /// <para><b>모든 피어가 돈다.</b> <see cref="SnowBallCarrier.FixedUpdateNetwork"/> 가
    /// 프록시에서도 물리를 돌리므로 접지 여부도 속도도 여기서 유효하다 — 새로 복제할 것이 없다.
    /// 판정이 아니라 표현이므로 <see cref="SnowBallCoopFeedback"/> 과 같은 모양이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SnowBallCarrier))]
    public sealed class SnowBallRollAudio : MonoBehaviour
    {
        [SerializeField] private SnowBallCarrier _ball;
        [Tooltip("굴리기 전용 소스. 협동 성공음이 쓰는 소스와 같은 것을 넣으면 안 된다.")]
        [SerializeField] private AudioSource _source;

        [Header("속력 매핑")]
        [Tooltip("이 접선 속력(m/s) 아래에서는 소리를 내지 않는다.")]
        [SerializeField, Min(0f)] private float _minSpeedMps = 0.35f;

        [Tooltip("이 접선 속력(m/s)에서 최대 볼륨이 된다.")]
        [SerializeField, Min(0.1f)] private float _fullSpeedMps = 4.5f;

        [Header("편안한 청감")]
        [SerializeField, Range(0f, 1f)] private float _maxVolume = 0.3f;
        [SerializeField, Min(0.01f)] private float _attackSeconds = 0.09f;
        [SerializeField, Min(0.01f)] private float _releaseSeconds = 0.22f;
        [SerializeField, Range(0.5f, 1.5f)] private float _minPitch = 0.94f;
        [SerializeField, Range(0.5f, 1.5f)] private float _maxPitch = 1.06f;

        private Rigidbody _body;
        private float _volumeVelocity;

        /// <summary>지금 굴러가는 것으로 판정했는가. 검증이 읽는다.</summary>
        public bool IsRolling { get; private set; }

        /// <summary>현재 볼륨. 검증이 읽는다.</summary>
        public float CurrentVolume => _source != null ? _source.volume : 0f;

        private void Reset()
        {
            _ball = GetComponent<SnowBallCarrier>();
            _source = GetComponent<AudioSource>();
        }

        private void Awake()
        {
            if (_ball == null) _ball = GetComponent<SnowBallCarrier>();
            _body = GetComponent<Rigidbody>();
            if (_source != null) _source.volume = 0f;
        }

        private void OnEnable()
        {
            // 데디케이티드 서버에는 스피커가 없다. 판정은 위에서 끝났고 여기부터가 표현이다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }

            if (_ball == null || _source == null || _source.clip == null)
            {
                Debug.LogError($"{nameof(SnowBallRollAudio)}: {nameof(SnowBallCarrier)}, AudioSource 또는 clip 이 없다.", this);
                enabled = false;
                return;
            }

            _source.loop = true;
            _source.playOnAwake = false;
            _source.volume = 0f;
            _volumeVelocity = 0f;
        }

        private void OnDisable()
        {
            IsRolling = false;
            if (_source == null) return;
            _source.Stop();
            _source.volume = 0f;
            _volumeVelocity = 0f;
        }

        private void Update()
        {
            float speed01 = ReadSpeed01();
            IsRolling = speed01 > 0f;

            float targetVolume = IsRolling ? _maxVolume * speed01 : 0f;
            float smoothTime = IsRolling ? _attackSeconds : _releaseSeconds;
            _source.volume = Mathf.SmoothDamp(_source.volume, targetVolume, ref _volumeVelocity,
                smoothTime, Mathf.Infinity, Time.deltaTime);

            if (IsRolling)
            {
                _source.pitch = Mathf.Lerp(_minPitch, _maxPitch, speed01);
                if (!_source.isPlaying) _source.Play();
                return;
            }

            if (_source.volume <= 0.002f && _source.isPlaying)
            {
                _source.Stop();
                _source.volume = 0f;
                _volumeVelocity = 0f;
            }
        }

        /// <summary>
        /// 0 이면 굴러가지 않는 것이다. <see cref="SnowBallCarrier.HasSupport"/> 가 등에 멘 경우
        /// (<c>isKinematic</c>)와 공중을 함께 걸러 주므로 여기서 다시 판정하지 않는다.
        /// </summary>
        private float ReadSpeed01()
        {
            if (_ball == null || _body == null || !_ball.HasSupport) return 0f;

            float speed = Vector3.ProjectOnPlane(_body.linearVelocity, _ball.SupportNormal).magnitude;
            if (speed < _minSpeedMps) return 0f;

            return Mathf.Clamp01(Mathf.InverseLerp(_minSpeedMps, Mathf.Max(_minSpeedMps + 0.01f, _fullSpeedMps), speed));
        }

        private void OnValidate()
        {
            _maxPitch = Mathf.Max(_minPitch, _maxPitch);
            _fullSpeedMps = Mathf.Max(_minSpeedMps + 0.01f, _fullSpeedMps);
        }
    }
}
