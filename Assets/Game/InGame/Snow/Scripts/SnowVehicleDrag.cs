using UnityEngine;
using UnityEngine.Serialization;

namespace PPack
{
    /// <summary>
    /// 눈에 잠긴 바퀴의 비율로 차를 늦춘다. <see cref="VehicleController.GroundSpeedFactor"/> 가
    /// 지면이 차를 늦추는 유일한 접점이다.
    ///
    /// <b>깊이는 CPU 격자에서 읽는다.</b> 텍스처를 읽지 않으므로 헤드리스 서버에서 그대로 돈다 —
    /// 이것이 눈 깊이를 처음부터 CPU 에 둔 이유다(<c>docs/specs/2026-08-14-snow-surface.md</c> §3).
    ///
    /// 판정은 <b>0/1</b> 이다. 깊이→저항 곡선을 만들지 않는다. 대신 셀 하나로 bool 을 읽으면
    /// 경계에서 켜졌다 꺼졌다 떨리므로, <b>네 지점을 재서 0~4/4 로 부드럽게 만든다</b> —
    /// 상태를 늘리지 않고 떨림을 없애는 방법이다.
    ///
    /// ⚠ <b>재는 곳은 바퀴가 아니라 블레이드 앞이다 (2026-08-14 실측으로 정정).</b> 스펙 §10 은
    /// 바퀴 네 곳이라고 썼는데, 패드를 차폭보다 넓게(4.0 × 2.3 m) 잡으면 <b>패드가 축간거리를 덮어
    /// 바퀴가 항상 이미 치워진 자리에 놓인다</b> — `covered` 가 영구히 0 이 된다. 물리적으로도
    /// 제설차가 느려지는 이유는 바퀴 침하가 아니라 <b>블레이드가 눈을 미는 저항</b>이다.
    /// 그래서 샘플 지점을 차체 앞, 아직 안 치운 눈 위에 둔다.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class SnowVehicleDrag : MonoBehaviour
    {
        [SerializeField] private SnowStage _stage;
        [Tooltip("깊이를 재는 지점. 차체 **앞**에 폭 방향으로 벌려 둔다 — 블레이드가 곧 밀 눈이다.\n" +
                 "넷을 권장한다: 개수가 곧 감속의 단계 수가 된다.")]
        [FormerlySerializedAs("_wheels")]
        [SerializeField] private Transform[] _samplePoints;

        [Tooltip("이 깊이(cm) 이상이면 그 지점은 눈으로 덮인 것으로 본다.")]
        [SerializeField, Range(1, 60)] private int _sinkThresholdCm = 5;
        [Tooltip("네 지점 모두 눈일 때의 최고속 배율. 1 이면 감속 없음.")]
        [SerializeField, Range(0.1f, 1f)] private float _fullSnowSpeedFactor = 0.55f;
        [Tooltip("배율이 목표까지 따라가는 속도(초당). 0 이면 즉시. 눈에 들어가는 순간이 툭 끊기지 않게 한다.")]
        [SerializeField, Min(0f)] private float _factorLerpPerSecond = 4f;

        [Header("실린 짐 (SnowPlowBlade 가 있을 때만)")]
        [Tooltip("제설날. 비어 있으면 짐 배율이 항상 1 이고 이 컴포넌트는 이전과 똑같이 동작한다.")]
        [SerializeField] private SnowPlowBlade _blade;
        [Tooltip("기준 질량(kg). 이 질량에서 최고속 배율이 아래 값이 된다.\n" +
                 "1200 은 12.5cm 셀·55° 안식각에서 폭 2.3m·높이 1.2m 더미의 대략적인 무게다.")]
        [SerializeField, Min(1f)] private float _loadReferenceKg = 1200f;
        [Tooltip("기준 질량에서의 최고속 배율. 1 이면 짐이 조작감에 아무 영향도 없다.")]
        [SerializeField, Range(0.1f, 1f)] private float _loadSpeedFactorAtReference = 0.6f;

        /// <summary>눈으로 덮인 샘플 지점 비율 0~1. 연출·계측이 구독한다.</summary>
        public float Covered { get; private set; }

        /// <summary>
        /// 실린 짐이 최고속에 먹이는 배율 0~1. <b>깊이 배율과 곱해진다.</b>
        ///
        /// 둘을 따로 두고 곱하는 것은 <b>서로 다른 질문에 답하기 때문</b>이다. 깊이는 "지금 밀고
        /// 있는 눈이 얼마나 깊은가"이고 짐은 "얼마나 무거운 것을 지고 있는가"다. 치운 길에서
        /// 무거운 짐을 지고 달리는 것과 빈 날로 처녀설에 들어가는 것은 다른 상황이며, 하나로
        /// 합치면 그 둘이 구별되지 않는다.
        /// </summary>
        public float LoadSpeedFactor { get; private set; } = 1f;

        private VehicleController _controller;
        private float _factor = 1f;

        private void Awake()
        {
            _controller = GetComponent<VehicleController>();
            if (_stage == null) _stage = FindAnyObjectByType<SnowStage>();
        }

        private void FixedUpdate()
        {
            if (_controller == null) return;

            if (_stage == null || _samplePoints == null || _samplePoints.Length == 0)
            {
                _controller.GroundSpeedFactor = 1f;
                return;
            }

            int sunk = 0;
            int counted = 0;
            for (int i = 0; i < _samplePoints.Length; i++)
            {
                Transform point = _samplePoints[i];
                if (point == null) continue;
                counted++;
                if (_stage.DepthCmAtWorld(point.position) >= _sinkThresholdCm) sunk++;
            }

            Covered = counted == 0 ? 0f : (float)sunk / counted;

            // 깊이 배율 × 짐 배율. 순서는 의미 없고 **곱셈**이라는 것만 의미가 있다.
            LoadSpeedFactor = _blade == null
                ? 1f
                : SnowPlowBlade.LoadFactor(_blade.CarriedMassKg, _loadReferenceKg,
                                           _loadSpeedFactorAtReference);

            float target = Mathf.Lerp(1f, _fullSnowSpeedFactor, Covered) * LoadSpeedFactor;
            _factor = _factorLerpPerSecond <= 0f
                ? target
                : Mathf.MoveTowards(_factor, target, _factorLerpPerSecond * Time.fixedDeltaTime);

            _controller.GroundSpeedFactor = _factor;
        }
    }
}
