using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 펭귄의 <b>임시 대역</b>. 캡슐 하나로 걷고 뒤뚱거린다.
    ///
    /// <para><b>왜 `PF_Player` 를 쓰지 않는가:</b> 그쪽은 Synty 로코모션을 포크한 스택
    /// (<see cref="PlayerAnimationController"/> · <see cref="PlayerCameraController"/> ·
    /// <c>CharacterController</c>)이고 gnome 의 비율에 맞춰 튜닝돼 있다. 눈덩이를 밀려면 필요한 것은
    /// <b>물리로 밀 수 있는 몸</b>이고, 그것은 <c>Rigidbody</c> 다. 애니메이션이 붙은 스택에 물리를
    /// 얹으면 둘이 서로를 밀어낸다.</para>
    ///
    /// <para><b>속도가 아니라 힘으로 밀지 않는다.</b> 아케이드 감각을 원하므로 목표 속도를 직접 준다
    /// (<see cref="Rigidbody.linearVelocity"/>). 대신 눈덩이는 질량이 커지면서 무거워지므로
    /// (<see cref="SnowBallCarrier.SnowDensityKgPerM3"/>) 밀리는 속도는 물리가 정한다 — 큰 공은 안 밀린다.</para>
    ///
    /// 조작: <b>W/A/S/D</b> 이동 · <b>Shift</b> 달리기. 카메라는 이 스크립트가 소유하지 않는다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PenguinProtoMotor : MonoBehaviour
    {
        [Header("이동")]
        [SerializeField, Min(0.1f)] private float _walkSpeedMps = 2.6f;
        [SerializeField, Min(0.1f)] private float _runSpeedMps = 4.4f;

        [Tooltip("목표 속도에 도달하는 시간(s). 0 이면 즉시 — 얼음 위에서 미끄러지는 느낌을 주려면 키운다.")]
        [SerializeField, Min(0f)] private float _accelSeconds = 0.12f;

        [Tooltip("초당 회전 각도. 뒤뚱거리는 몸은 빠르게 못 돈다.")]
        [SerializeField, Min(1f)] private float _turnRateDegPerSecond = 540f;

        [Header("뒤뚱거림 — 임시 대역의 유일한 연출")]
        [Tooltip("좌우로 기우는 각도. 걸을 때만 흔들린다.")]
        [SerializeField, Range(0f, 20f)] private float _waddleDegrees = 7f;

        [Tooltip("초당 흔드는 횟수. 속도에 비례해 빨라진다.")]
        [SerializeField, Min(0.1f)] private float _waddleHz = 2.2f;

        [Tooltip("기울일 대상. 비우면 이 오브젝트의 첫 자식을 쓴다 — 루트를 기울이면 콜라이더가 같이 눕는다.")]
        [SerializeField] private Transform _body;

        /// <summary>지금 평면 속도(m/s). 검증과 HUD 가 읽는다.</summary>
        public float SpeedMps { get; private set; }

        /// <summary>
        /// 입력이 요구하는 속도(m/s) — <b>실제로 나고 있는 속도가 아니다.</b>
        ///
        /// <para>눈덩이를 밀 때 공의 속도 상한으로 쓴다. 실측 속도를 상한으로 쓰면 교착이 생긴다:
        /// 무거운 공에 막힌 펭귄은 속도가 0 이고, 그러면 상한도 0 이라 공이 영원히 못 움직인다
        /// (실측 - 3 t 공이 0.04 m/s 에 고정됐다). 의도 속도는 막혀도 줄지 않으므로 공은 힘이
        /// 허용하는 만큼 가속하고, 걷는 속도에 닿으면 멈춘다.</para>
        /// </summary>
        public float DesiredSpeedMps { get; private set; }

        /// <summary>
        /// 이 방향으로는 나아갈 수 없다(월드 XZ, 단위벡터). 0 이면 제약 없음.
        ///
        /// <para><b>무거운 것을 밀 때 펭귄이 느려지는 것이 여기서 나온다.</b> 눈덩이를 미는
        /// <see cref="PenguinSnowballControl"/> 이 공 표면의 법선을 넘기고, 이 모터가 그 방향
        /// 성분만 깎는다 — 안 밀리는 공 앞에서는 제자리걸음이 된다.</para>
        ///
        /// <para><b>속도 배수(<c>SpeedFactor</c>)를 없애고 이것으로 바꿨다 (2026-08-21).</b> 최고속을
        /// 따로 깎으면 늦추는 원인이 둘이 되고(공의 질량 + 배수) 루트 규약이 금지하는 이중 감속이다.
        /// 게다가 배수는 <b>모든</b> 방향을 늦춰서, 무거운 공을 두고 옆으로 걸어 나오는 것까지
        /// 느려졌다. 법선 하나만 막으면 공 주위를 도는 것은 그대로 빠르다.</para>
        ///
        /// <para>속도를 쓰는 곳은 이 모터 하나로 유지한다 — 같은 오브젝트의 두 컴포넌트가 같은
        /// 프레임에 <c>linearVelocity</c> 를 쓰면 실행 순서에 결과가 걸린다.</para>
        /// </summary>
        public Vector3 BlockNormal { get; set; }

        /// <summary>지금 입력 방향(월드 XZ). 0 이면 서 있다.</summary>
        public Vector2 MoveInput { get; private set; }

        private Rigidbody _rigidbody;
        private float _waddlePhase;
        private Vector3 _velocityXZ;

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();

            // 캡슐이 넘어지면 조작이 무너진다. 회전은 이 스크립트가 직접 준다.
            _rigidbody.constraints = RigidbodyConstraints.FreezeRotation;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;

            if (_body == null && transform.childCount > 0) _body = transform.GetChild(0);
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) { MoveInput = Vector2.zero; return; }

            float x = (kb[Key.D].isPressed ? 1f : 0f) - (kb[Key.A].isPressed ? 1f : 0f);
            float z = (kb[Key.W].isPressed ? 1f : 0f) - (kb[Key.S].isPressed ? 1f : 0f);
            var input = new Vector2(x, z);
            MoveInput = input.sqrMagnitude > 1f ? input.normalized : input;

            Waddle();
        }

        private void FixedUpdate()
        {
            float top = (Keyboard.current != null && Keyboard.current[Key.LeftShift].isPressed)
                ? _runSpeedMps
                : _walkSpeedMps;

            Vector3 want = new Vector3(MoveInput.x, 0f, MoveInput.y) * top;
            DesiredSpeedMps = want.magnitude;

            // 막힌 방향으로 가려는 성분을 깎는다. 접선은 그대로 둬서 옆으로는 빠져나올 수 있다.
            if (BlockNormal.sqrMagnitude > 1e-6f)
            {
                float into = Vector3.Dot(want, BlockNormal);
                if (into > 0f) want -= BlockNormal * into;
            }

            // 목표 속도로 수평만 몬다. 수직은 중력에 맡긴다 — 여기서 y 를 덮으면 경사에서 떠오른다.
            _velocityXZ = _accelSeconds <= 0f
                ? want
                : Vector3.MoveTowards(_velocityXZ, want,
                                      top / Mathf.Max(_accelSeconds, 1e-3f) * Time.fixedDeltaTime);

            Vector3 v = _rigidbody.linearVelocity;
            _rigidbody.linearVelocity = new Vector3(_velocityXZ.x, v.y, _velocityXZ.z);
            SpeedMps = new Vector2(_velocityXZ.x, _velocityXZ.z).magnitude;

            if (_velocityXZ.sqrMagnitude > 1e-4f)
            {
                Quaternion look = Quaternion.LookRotation(
                    new Vector3(_velocityXZ.x, 0f, _velocityXZ.z), Vector3.up);
                _rigidbody.MoveRotation(Quaternion.RotateTowards(
                    _rigidbody.rotation, look, _turnRateDegPerSecond * Time.fixedDeltaTime));
            }
        }

        /// <summary>몸만 좌우로 기울인다. 루트를 기울이면 캡슐 콜라이더가 같이 누워 발이 파묻힌다.</summary>
        private void Waddle()
        {
            if (_body == null) return;

            if (SpeedMps < 0.05f)
            {
                _body.localRotation = Quaternion.Slerp(_body.localRotation, Quaternion.identity,
                                                       1f - Mathf.Exp(-8f * Time.deltaTime));
                return;
            }

            _waddlePhase += Time.deltaTime * _waddleHz * Mathf.PI * 2f * (SpeedMps / Mathf.Max(_walkSpeedMps, 0.01f));
            float roll = Mathf.Sin(_waddlePhase) * _waddleDegrees;
            _body.localRotation = Quaternion.Euler(0f, 0f, roll);
        }
    }
}
