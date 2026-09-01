using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 출발과 급제동에 Feel 을 얹는다. 순수 연출이라 트럭의 상태를 읽기만 한다 — 이 컴포넌트가
    /// 없어도, 헤드리스 서버라도 주행은 그대로다.
    ///
    /// <b>스쿼시 하나를 세기만 바꿔 돌려 쓴다.</b> 플레이어 여러 개로 나누면 같은 스케일 채널을
    /// 서로 덮어쓴다 — 채널 하나에 주인 하나(<c>../Vehicle/AGENTS.md</c>).
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeliveryTruckFeedbacks : MonoBehaviour
    {
        [SerializeField] private DeliveryTruck _truck;
        [SerializeField] private MMF_Player _bodyFeedback;

        [Tooltip("이 속도를 넘어서면 '출발했다'고 본다.")]
        [SerializeField, Min(0.01f)] private float _departSpeedThreshold = 0.3f;
        [Tooltip("이보다 센 감속으로 멈춰야 '급제동'이다. 살살 서면 아무것도 안 튼다.")]
        [SerializeField, Min(0.01f)] private float _brakeAccelThreshold = 2.5f;

        [Tooltip("출발할 때 늘어나는 정도. 1보다 크면 늘어난다.")]
        [SerializeField, Min(1f)] private float _departStretch = 1.2f;
        [Tooltip("급제동에서 눌리는 정도. 1보다 작으면 눌린다.")]
        [SerializeField, Range(0.1f, 1f)] private float _brakeSquash = 0.78f;

        private MMF_SquashAndStretch _squash;
        private bool _isStopped = true;
        private float _previousSpeed;

        private void Awake()
        {
            if (_truck == null) _truck = GetComponentInParent<DeliveryTruck>();
            if (_bodyFeedback == null) _bodyFeedback = GetComponent<MMF_Player>();
            if (_bodyFeedback != null) _squash = _bodyFeedback.GetFeedbackOfType<MMF_SquashAndStretch>();
        }

        private void Update()
        {
            if (_truck == null || _bodyFeedback == null) return;

            float speed = _truck.CurrentSpeed;
            if (_isStopped && speed > _departSpeedThreshold)
            {
                _isStopped = false;
                Play(_departStretch);
            }
            else if (!_isStopped && speed <= 0.02f)
            {
                _isStopped = true;
                if (_previousSpeed > _departSpeedThreshold
                    && _truck.CurrentAcceleration <= -_brakeAccelThreshold)
                {
                    Play(_brakeSquash);
                }
            }

            _previousSpeed = speed;
        }

        /// <summary>
        /// 세기는 <c>PlayFeedbacks</c> 의 intensity 인자가 아니라 리맵 값으로 준다. intensity 는
        /// 리맵의 <b>배수</b>라, 리맵이 1보다 작은 스쿼시에서는 세기를 낮출수록 오히려 더 크게
        /// 변형된다(<c>../Vehicle/AGENTS.md</c>).
        /// </summary>
        private void Play(float remapOne)
        {
            if (_squash != null) _squash.RemapCurveOne = remapOne;
            _bodyFeedback.PlayFeedbacks();
        }
    }
}
