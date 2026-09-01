using UnityEngine;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// v7 의 <b>눈 조작감</b>을 이 프로젝트의 <c>VehicleController</c> 에 옮긴 층이다.
    /// 구 <c>PPack.SnowVehicleDrag</c> 를 대신한다 — 그쪽은 CPU 격자(`SnowStage`)를 읽으므로 v7 리그가
    /// 도는 씬에서는 아무것도 못 읽는다.
    ///
    /// <para>v7 은 눈이 차를 늦추는 것을 <b>세 항으로 나눠 곱한다.</b> 나누는 이유는 진단이다 — 느린 차를
    /// 보고 "짐이 무거워서"인지 "깊은 데를 지나서"인지 "날이 물려서"인지 구분할 수 있어야 한다.</para>
    ///
    /// <list type="number">
    /// <item><b>깊이 천장</b>(<see cref="DepthSpeedFactor"/>) — <b>바퀴 밑</b> 깊이에 선형이다.
    ///       앞이 아니라 밑을 보는 것이 핵심이다: 블레이드가 이미 치운 자리에 올라서면 <b>즉시</b>
    ///       풀려야 하고, 그것이 "치운 차선이 길이 된다"는 감각을 만든다.</item>
    /// <item><b>짐 계수</b>(<see cref="LoadSpeedFactor"/>) — 더미 질량의 함수 <c>1/(1+k·m)</c>.
    ///       깊이와 <b>독립이라 곱한다.</b> 짐은 어디서나 느리게 하고 깊이는 여기서만 느리게 한다.</item>
    /// <item><b>제설 저항</b>(<see cref="SnowDragMps2"/>) — 천장이 아니라 <b>감속</b>이다.
    ///       <b>블레이드가 내려가 있을 때만</b> 붙고, 날 위치의 깊이로 물린다. 날을 올리면 사라진다 —
    ///       v7 에서 "올림"이 멈춰야 하는 세 가지 중 하나다.</item>
    /// </list>
    ///
    /// <para>천장 둘은 <c>VehicleController.GroundSpeedFactor</c> 하나로 곱해 넣는다(그 필드가 정면
    /// 최고속만 건드리고 횡그립은 손대지 않는다 — 차량 폴더의 규약). 저항은 천장으로 표현할 수 없으므로
    /// Rigidbody 의 정면 성분을 직접 깎는다.</para>
    ///
    /// <para><b>실행 순서가 규약이다.</b> <c>VehicleController</c> 가 <c>FixedUpdate</c> 끝에
    /// <c>linearVelocity</c> 를 대입하므로, 저항은 그 뒤에 깎아야 남는다. 그래서
    /// <c>DefaultExecutionOrder(200)</c> 이다 — 이 숫자를 지우면 저항이 조용히 사라진다(대입이 덮는다).</para>
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    public sealed class SnowV7VehicleFeel : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("비면 씬에서 찾는다. 여러 리그가 있으면 명시해야 한다.")]
        [SerializeField] private SnowV7MapRig _rig;

        [Header("깊이 천장 (v7: satDepth 0.30 / floor 0.35)")]
        [Tooltip("이 깊이에서 천장이 바닥값까지 내려간다. 이 프로젝트 눈이 30cm 라 그 값이다.")]
        [SerializeField, Min(0.01f)] private float _depthSatM = 0.30f;
        [Tooltip("포화 깊이에서 남는 최고속 비율. 0.35 면 처녀설에서 최고속의 35%.")]
        [SerializeField, Range(0.02f, 1f)] private float _depthFloor01 = 0.35f;

        [Header("짐 계수 (v7: refKg 1200 / speed 0.45)")]
        [Tooltip("이 질량을 실었을 때 아래 비율이 되도록 k 를 정한다.")]
        [SerializeField, Min(1f)] private float _massReferenceKg = 1200f;
        [Tooltip("기준 질량에서 남는 최고속 비율.")]
        [SerializeField, Range(0.05f, 1f)] private float _massSpeedFactorAtRef = 0.45f;
        [Tooltip("기준 질량에서 남는 코스트(관성) 비율. 저항에도 이 값이 곱해진다 - 무거운 짐은 같은 눈을 더 큰 관성으로 밀고 나간다.")]
        [SerializeField, Range(0.05f, 1f)] private float _massCoastFactorAtRef = 0.12f;

        [Header("제설 저항 (v7: base 3.0 / perSpeed 0.45 / bite 0.30)")]
        [Tooltip("물린 깊이에서의 기본 감속 (m/s²).")]
        [SerializeField, Min(0f)] private float _dragBaseMps2 = 3.0f;
        [Tooltip("속도에 비례해 더해지는 감속 (m/s² per m/s).")]
        [SerializeField, Min(0f)] private float _dragPerSpeed = 0.45f;
        [Tooltip("이 깊이에서 저항이 100% 로 물린다.")]
        [SerializeField, Min(0.01f)] private float _biteDepthM = 0.30f;

        [Header("차체 반응 (v7: rideFactor 0.35 / massPitch 6 / response 9)")]
        [Tooltip("비면 차량 밑에서 찾는다.")]
        [SerializeField] private PPack.VehicleBodyMotion _bodyMotion;
        [Tooltip("비면 차량 밑에서 찾는다. 제설 모드에서 날을 바닥에 붙여 두기 위해 필요하다.")]
        [SerializeField] private SnowV7BladeVisual _bladeVisual;
        [Tooltip("바퀴 밑 눈 깊이의 이 비율만큼 차가 눈 위로 올라탄다. v7 원본 0.35.")]
        [SerializeField, Range(0f, 1f)] private float _snowRideFactor = 0.35f;
        [Tooltip("기준 질량을 가득 실었을 때 더해지는 피치(도). v7 원본 6.")]
        [SerializeField, Range(0f, 25f)] private float _loadPitchDeg = 6f;
        [Tooltip("승차 높이와 짐 피치가 목표를 따라가는 속도 (1/초). v7 원본 9.")]
        [SerializeField, Min(0.01f)] private float _bodyResponsePerSec = 9f;

        /// <summary>바퀴 밑 눈 깊이 (m). 천장을 정하는 값이다.</summary>
        public float DepthUnderM { get; private set; }

        /// <summary>차체가 눈 위로 올라탄 높이 (m). 감쇠까지 끝난 값이다.</summary>
        public float RideOffsetM { get; private set; }

        /// <summary>날 위치의 눈 깊이 (m). 저항을 정하는 값이다.</summary>
        public float DepthAheadM { get; private set; }

        /// <summary>깊이가 만든 최고속 배수. 1 = 긁힌 바닥.</summary>
        public float DepthSpeedFactor { get; private set; } = 1f;

        /// <summary>짐이 만든 최고속 배수. 1 = 빈 날.</summary>
        public float LoadSpeedFactor { get; private set; } = 1f;

        /// <summary>지금 실제로 깎고 있는 감속 (m/s²). 0 이면 저항이 안 붙는 상태다.</summary>
        public float SnowDragMps2 { get; private set; }

        private Rigidbody _body;
        private PPack.VehicleController _controller;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _controller = GetComponent<PPack.VehicleController>();

            if (_rig == null) _rig = FindFirstObjectByType<SnowV7MapRig>();
            if (_bodyMotion == null) _bodyMotion = GetComponentInChildren<PPack.VehicleBodyMotion>(true);
            if (_bladeVisual == null) _bladeVisual = GetComponentInChildren<SnowV7BladeVisual>(true);
        }

        private void OnDisable()
        {
            // 꺼질 때 천장을 되돌린다. 안 그러면 차가 영구히 느린 채로 남는다.
            if (_controller != null) _controller.GroundSpeedFactor = 1f;
            SnowDragMps2 = 0f;

            // 차체도 같이 되돌린다 — 안 그러면 뜬 채로 굳는다.
            RideOffsetM = 0f;
            if (_bodyMotion != null) { _bodyMotion.RideOffsetY = 0f; _bodyMotion.LoadPitchDeg = 0f; }
            if (_bladeVisual != null) _bladeVisual.RideOffsetY = 0f;
        }

        private void FixedUpdate()
        {
            if (_rig == null) return;

            SnowPileFieldV7 field = _rig.Field;
            if (field == null || !field.Ready) return;

            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();

            Vector3 position = transform.position;
            Vector3 bladePosition = position + forward * _rig.BladeAheadM;

            DepthUnderM = field.HeightAt(new Vector3(position.x, 0f, position.z));
            DepthAheadM = field.HeightAt(new Vector3(bladePosition.x, 0f, bladePosition.z));

            // ---- 천장 둘: 곱해서 GroundSpeedFactor 하나로 넣는다 -------------------------------
            // 수식은 SnowV7Resistance 가 갖고 있다. 멀티의 네트워크 제설차도 같은 함수를 쓰므로
            // 여기서 다시 적으면 싱글과 멀티의 조작감이 조용히 갈린다.
            DepthSpeedFactor = SnowV7Resistance.DepthSpeedFactor(DepthUnderM, _depthSatM, _depthFloor01);

            float pileMassKg = field.PileMassKg;
            LoadSpeedFactor = SnowV7Resistance.MassFactor(pileMassKg, _massReferenceKg,
                                                          _massSpeedFactorAtRef);

            if (_controller != null)
            {
                _controller.GroundSpeedFactor = DepthSpeedFactor * LoadSpeedFactor;
            }

            // 저항보다 **앞**에 둔다. 아래는 날이 올라가 있으면 early return 하는데, 차체 반응은
            // 오히려 그때가 본체다 — 뒤에 두면 날을 올리는 순간 차체가 뜬 채로 굳는다.
            UpdateBodyReaction(pileMassKg);

            // ---- 저항: 블레이드가 내려가 있을 때만 --------------------------------------------
            if (!_rig.BladeDown)
            {
                SnowDragMps2 = 0f;
                return;
            }

            Vector3 velocity = _body.linearVelocity;
            float forwardSpeed = Vector3.Dot(velocity, forward);

            SnowDragMps2 = SnowV7Resistance.DragMps2(DepthUnderM, DepthAheadM, forwardSpeed, pileMassKg,
                                                     _biteDepthM, _dragBaseMps2, _dragPerSpeed,
                                                     _massReferenceKg, _massCoastFactorAtRef);
            if (SnowDragMps2 <= 0f) return;

            float slowed = Mathf.MoveTowards(forwardSpeed, 0f, SnowDragMps2 * Time.fixedDeltaTime);

            // 정면 성분만 깎는다. 횡을 같이 깎으면 드리프트가 눌린다(차량 폴더가 기록한 실패다).
            _body.linearVelocity = velocity + forward * (slowed - forwardSpeed);
        }

        /// <summary>
        /// v7 차체 반응 두 항을 그대로 옮긴 것 — <c>SnowPileCarV7.Integrate</c> 의 마지막 블록이다.
        ///
        /// <para><b>승차 높이</b>는 <c>rideLift = depthUnder × rideFactor</c> 다. 차가 눈을 뚫고
        /// 가는 것이 아니라 <b>위로 올라탄다</b>는 뜻이고, 치운 차선에 들어서면 내려앉는다.
        /// 그 오르내림이 곧 눈 위에서의 뒤뚱거림이다.</para>
        ///
        /// <para><b>깊이를 "앞"이 아니라 "밑"에서 읽는 것이 이 항의 핵심이다.</b> 날은 2.55 m 앞에
        /// 있으므로 제설 중에는 차체 밑이 이미 치워진 자리다 — 그래서 "날을 올렸을 때만 눈을 탄다"가
        /// 별도의 조건 없이 저절로 성립한다. 날 자신은 예외라 아래에서 명시적으로 눌러 둔다.</para>
        ///
        /// <para>감쇠는 원본대로 <c>k = 1 - exp(-response·dt)</c> 의 지수 lerp 다. 차체 쪽 스프링을
        /// 쓰지 않는 이유는 <b>한 값을 두 번 감쇠시키지 않기 위해서</b>다 — 그러면 눈에서 빠져나오는
        /// 순간이 뭉개진다.</para>
        /// </summary>
        private void UpdateBodyReaction(float pileMassKg)
        {
            float k = 1f - Mathf.Exp(-_bodyResponsePerSec * Time.fixedDeltaTime);
            RideOffsetM = Mathf.Lerp(RideOffsetM, DepthUnderM * _snowRideFactor, k);

            float load01 = Mathf.Clamp01(pileMassKg / _massReferenceKg);

            if (_bodyMotion != null)
            {
                _bodyMotion.RideOffsetY = RideOffsetM;
                _bodyMotion.LoadPitchDeg = load01 * _loadPitchDeg;
            }

            // 제설 모드에서 날은 바닥에 붙어 있어야 한다. 차에 실려 같이 뜨는 것은 올렸을 때뿐이다.
            if (_bladeVisual != null) _bladeVisual.RideOffsetY = _rig.BladeDown ? 0f : RideOffsetM;
        }
    }
}
