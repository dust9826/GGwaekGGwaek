using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 협동 밀치기 <b>전원 성공</b>의 순간을 Feel 로 넘긴다. 눈덩이 자신에 붙는다.
    ///
    /// <para><b>왜 릴레이가 따로 있는가:</b> <see cref="SnowBallCarrier"/> 는 권위 로직이고 연출은
    /// 별도 계층이다(루트 <c>AGENTS.md</c>). <see cref="VehicleImpactRelay"/> ·
    /// <see cref="PenguinImpactRelay"/> 와 같은 분리다.</para>
    ///
    /// <para><b>이벤트가 아니라 상태의 에지를 본다.</b> 서버가 성공 이벤트를 따로 쏘지 않는다 —
    /// <see cref="SnowBallCarrier.CoopBoostCount"/> 가 이미 복제되므로 각 피어가 같은 결론에
    /// 도달한다. 질량이 쓰는 것과 같은 방식이고, 바이트도 유실될 자리도 늘지 않는다.</para>
    ///
    /// <para><b>세기는 좁은 폭으로만 움직인다.</b> 난이도가 오르면 성공 자체가 잦아지므로(쿨타임
    /// 하한이 0 이다) 상승 곡선은 진폭이 아니라 <b>빈도</b>가 맡는다. 여기서 진폭까지 키우면
    /// 무거운 공을 미는 내내 큰 소리가 반복된다.</para>
    ///
    /// <para>⚠ <b>스케일·위치·회전을 건드리는 피드백은 넣지 않는다.</b> 스케일의 주인은
    /// <c>SnowBallCarrier.ApplySize</c> 고 위치·회전의 주인은 <c>Rigidbody</c> 와
    /// <c>NetworkTransform</c> 이다. <c>MMF_SquashAndStretch</c> 를 얹으면 그쪽이 자기
    /// <c>_initialScale</c> 을 캡처해 공의 크기가 갈라진다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SnowBallCarrier))]
    public sealed class SnowBallCoopFeedback : MonoBehaviour
    {
        [SerializeField] private SnowBallCarrier _ball;

        [Tooltip("성공 순간에 재생할 MMF_Player. 스케일·위치·회전을 건드리는 피드백은 넣지 않는다.")]
        [SerializeField] private MMF_Player _successFeedback;

        [Header("세기 (난이도 0 -> 1)")]
        [Tooltip("쉬운 성공의 볼륨.")]
        [SerializeField, Range(0f, 1f)] private float _volumeAtEasy = 0.42f;

        [Tooltip("어려운 성공의 볼륨. 폭을 좁게 둔다 - 무거울수록 성공이 잦아지므로 진폭까지 " +
                 "키우면 반복이 피로해진다.")]
        [SerializeField, Range(0f, 1f)] private float _volumeAtHard = 0.6f;

        [Tooltip("쉬운 성공의 피치.")]
        [SerializeField, Range(0.5f, 1.5f)] private float _pitchAtEasy = 1.06f;

        [Tooltip("어려운 성공의 피치. 낮을수록 무겁게 들린다.")]
        [SerializeField, Range(0.5f, 1.5f)] private float _pitchAtHard = 0.94f;

        /// <summary>재생한 횟수. 검증이 읽는다.</summary>
        public int PlayCount { get; private set; }

        /// <summary>마지막으로 재생한 세기(0~1). 검증이 읽는다.</summary>
        public float LastStrength01 { get; private set; }

        /// <summary>
        /// <b><c>MMF_Sound</c> 가 아니라 <c>MMF_AudioSource</c> 다.</b> 차량·나무 쪽은
        /// <c>MMF_Sound</c> 를 <c>PlayMethod = Pool</c> 로 쓰고 그것도 잘 돈다 — 다만 풀은 소리를
        /// <b>발동 시점의 좌표에 놓고</b> 거기 두므로, 밀려서 계속 움직이는 공에는 공에 붙은
        /// <c>AudioSource</c> 를 직접 재생하는 쪽이 맞다. 3D 감쇠도 프리팹에서 저작된다.
        ///
        /// <para>기본값 <c>PlayMethod = Event</c> 는 쓰면 안 된다 — <c>MMSoundManager</c> 를
        /// 거치는데 이 프로젝트에는 그 매니저가 없어서 조용히 아무것도 하지 않는다.</para>
        /// </summary>
        private MMF_AudioSource _sound;
        private int _seenBoostCount;

        private void Reset() => _ball = GetComponent<SnowBallCarrier>();

        private void Awake()
        {
            if (_ball == null) _ball = GetComponent<SnowBallCarrier>();
            if (_successFeedback != null) _sound = _successFeedback.GetFeedbackOfType<MMF_AudioSource>();

            // 붙는 시점에 이미 쌓여 있던 성공은 지나간 일이다. 0 에서 시작하면 늦게 합류한
            // 피어가 첫 프레임에 밀린 성공을 한꺼번에 터뜨린다.
            if (_ball != null) _seenBoostCount = _ball.CoopBoostCount;
        }

        private void Update()
        {
            if (_ball == null) return;

            int count = _ball.CoopBoostCount;
            if (count == _seenBoostCount) return;
            _seenBoostCount = count;

            Play(Mathf.Clamp01(_ball.LastCoopBoostDifficulty01));
        }

        private void Play(float strength01)
        {
            PlayCount++;
            LastStrength01 = strength01;

            // 데디케이티드 서버는 GPU 도 스피커도 없다 - 판정은 위에서 끝났고 여기부터가 표현이다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

            // 세기를 PlayFeedbacks 의 intensity 인자로 넘기지 않는다. 그 값은 각 피드백의 remap 에
            // 곱해지므로 배율이 1 미만이면 오히려 반대로 작동한다(VehicleImpactRelay 에 실측이
            // 있다). 파라미터 자체를 보간해야 비례한다.
            if (_sound != null)
            {
                float volume = Mathf.Lerp(_volumeAtEasy, _volumeAtHard, strength01);
                float pitch = Mathf.Lerp(_pitchAtEasy, _pitchAtHard, strength01);
                _sound.MinVolume = volume * 0.9f;
                _sound.MaxVolume = volume;
                _sound.MinPitch = pitch - 0.03f;
                _sound.MaxPitch = pitch + 0.03f;
            }

            if (_successFeedback != null) _successFeedback.PlayFeedbacks();
        }
    }
}
