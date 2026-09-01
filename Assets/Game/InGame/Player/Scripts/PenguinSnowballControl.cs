using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 펭귄의 눈덩이 두 동작 — <b>뭉치기</b>와 <b>굴리기</b>.
    ///
    /// <para><b>왜 둘이 한 컴포넌트인가:</b> 같은 키가 상황에 따라 둘 중 하나를 한다. 눈덩이가 없으면
    /// 발밑 눈을 뭉치고, 앞에 눈덩이가 있으면 잡는다. 키를 따로 두면 플레이어가 "지금 뭘 누를지" 를
    /// 상태로 기억해야 하고, 그 상태는 이미 화면에 보인다(공이 있느냐 없느냐).</para>
    ///
    /// <para><b>굴리기는 물리에 맡기지 않는다.</b> 그냥 몸으로 밀면 공이 옆으로 빠지고 펭귄이 공 위로
    /// 올라탄다 — 실측으로 그랬다. 잡고 있는 동안은 공을 <b>펭귄 앞 고정 거리</b>로 몰고(속도를 직접
    /// 준다), 놓는 순간 다시 물리에 돌려준다. 그러면 "굴리다가 실수하면 굴러가 버린다" 가 공짜로 나온다 —
    /// 브레인스토밍의 그 감각이 놓는 순간의 물리다.</para>
    ///
    /// <para>권위는 언제나 <see cref="SnowCpuStage"/> 다. 이 컴포넌트는 <b>만들라고 요청</b>하고
    /// 잡은 공을 <b>몰기만</b> 한다 — 눈을 걷는 것은 스테이지의 공 루프가 한다.</para>
    ///
    /// 조작: <b>E</b> 뭉치기 / 잡기 / 놓기.
    /// </summary>
    [RequireComponent(typeof(PenguinProtoMotor))]
    public sealed class PenguinSnowballControl : MonoBehaviour
    {
        [Header("붙기")]
        [Tooltip("이 거리 안의 눈덩이에 붙는다(m). 공 표면까지의 거리다 — 공이 커지면 자동으로 넓어진다.")]
        [SerializeField, Min(0.1f)] private float _reachM = 0.9f;

        [Tooltip("표면이 이만큼 안에 들어오면 닿은 것으로 본다(m).\n\n" +
                 "붙어 있어도 걸어서 떨어질 수 있고, 떨어진 동안은 밀리지 않는다 — 손으로 잡는 것이 " +
                 "아니라 몸으로 미는 것이기 때문이다.")]
        [SerializeField, Min(0.01f)] private float _contactSlackM = 0.22f;

        [Header("밀기 — 무거움은 공의 질량이 만든다")]
        [Tooltip("몸이 공에 실을 수 있는 최대 추진력(N).\n\n" +
                 "<b>이 값과 공의 질량이 조작감 전부다.</b> 공은 부피 x 400 kg/m3 이라 지름 1 m 가 " +
                 "약 210 kg, 2 m 가 약 1.7 t, 4 m 가 약 13 t 이다 — 같은 힘으로 밀면 큰 공은 " +
                 "스스로 거의 안 움직인다. 그것이 '더 키우지 말라' 는 압력이고, 속도 배수로 펭귄을 " +
                 "늦추는 방식을 대체한다(늦추는 원인이 둘이면 서로 상쇄된다 - 루트 규약).")]
        [SerializeField, Min(1f)] private float _pushForceN = 2600f;

        [Header("뭉치기")]
        [Tooltip("발밑에서 이만큼 앞에서 뭉친다(m). 0 이면 자기 발밑을 판다.")]
        [SerializeField, Min(0f)] private float _gatherAheadM = 0.7f;

        /// <summary>지금 잡고 있는 눈덩이. 없으면 null. HUD·검증이 읽는다.</summary>
        public SnowBallCarrier Held { get; private set; }

        /// <summary>마지막 뭉치기가 실패한 이유. 화면에 띄우려면 이것을 쓴다.</summary>
        public string LastFailure { get; private set; } = string.Empty;

        private PenguinProtoMotor _motor;
        private SnowCpuStage _stage;
        private CapsuleCollider _capsule;

        /// <summary>펭귄 몸의 반지름. 공을 <b>몸 밖에</b> 두려면 이것이 필요하다.</summary>
        private float BodyRadiusM
        {
            get
            {
                if (_capsule == null) _capsule = GetComponent<CapsuleCollider>();
                if (_capsule == null) return 0.3f;

                Vector3 s = transform.localScale;
                return _capsule.radius * Mathf.Max(Mathf.Abs(s.x), Mathf.Abs(s.z));
            }
        }

        private void Awake()
        {
            _motor = GetComponent<PenguinProtoMotor>();
            _capsule = GetComponent<CapsuleCollider>();
        }

        private void Update()
        {
            Keyboard kb = Keyboard.current;
            if (kb == null) return;

            if (kb[Key.E].wasPressedThisFrame) Toggle();
            if (kb[Key.Q].wasPressedThisFrame) Burst();
        }

        /// <summary>E 한 번. 붙어 있으면 떼고, 앞에 공이 있으면 붙고, 없으면 뭉친다.</summary>
        public void Toggle()
        {
            if (Held != null) { Release(); return; }
            if (TryGrabNearby()) return;
            TryGather();
        }

        /// <summary>
        /// Q 한 번. <b>붙어 있는 공을 터뜨린다.</b>
        ///
        /// <para>크기가 아니라 사람이 정한다 — 무게는 "더 키우지 말라" 는 압력일 뿐이고, 어디서
        /// 그만둘지는 플레이어의 선택이어야 한다. 요청만 남기고 실제 터짐은 서버가 다음 틱에
        /// 처리한다(<see cref="SnowBallCarrier.ServerBurstRequested"/>).</para>
        ///
        /// <para>요청한 자리에서 곧바로 손을 뗀다 — 사라질 공을 한 프레임 더 미는 것을 막는다.</para>
        /// </summary>
        public void Burst()
        {
            if (Held == null) return;
            Held.ServerBurstRequested = true;
            Release();
        }

        /// <summary>
        /// <b>미는 것이지 드는 것이 아니다 (2026-08-21).</b>
        ///
        /// <para>전에는 공을 펭귄 앞 고정 거리에 두고 <c>linearVelocity</c> 를 직접 줬다. 그러면
        /// 공의 질량이 조작에 아무 영향을 주지 못한다 — 솔버에게 펭귄은 무한히 강한 몸이고, 13 t
        /// 짜리도 12 m/s 로 끌려온다. 무게를 느끼게 하려고 펭귄의 최고속을 따로 깎았는데, 그것은
        /// 원인이 둘이 되는 것이라 루트 규약이 금지한다.</para>
        ///
        /// <para>지금은 <b>접촉 법선으로 힘만 준다.</b> 무거움은 <c>F = ma</c> 하나에서 나오고,
        /// 중심이 어긋나면 공이 옆으로 새는 것도 공짜로 나온다 — 그것이 조향이다. 접선 성분을
        /// 주지 않는 이유가 여기 있다: 예전에 몸으로 밀었을 때 공이 폭발적으로 튄 원인이 접선이었다.</para>
        ///
        /// <para><b>펭귄–공 물리 충돌은 계속 끈다.</b> 실측으로 지름 1.24 m 에서 펭귄이 공 위로
        /// 올라탔고(y 0.65), 원인은 캡슐이 구의 곡면을 타고 밀려 올라가는 것이라 힘을 어떻게 주든
        /// 남는다. 대신 표면을 통과하지 못하게 <b>법선 방향 접근 속도만</b> 깎는다.</para>
        /// </summary>
        private void FixedUpdate()
        {
            if (Held == null) return;

            var body = Held.GetComponent<Rigidbody>();
            if (body == null) { Release(); return; }

            Vector3 d = body.position - transform.position;
            d.y = 0f;

            float dist = d.magnitude;
            if (dist < 1e-4f) return;

            Vector3 n = d / dist;
            float gap = dist - Held.RadiusM - BodyRadiusM;

            // 걸어서 떨어졌다 - 붙어 있어도 닿지 않으면 밀리지 않는다.
            if (gap > _contactSlackM) { _motor.BlockNormal = Vector3.zero; return; }

            Vector3 move = _motor.MoveInput;
            float into = Vector3.Dot(new Vector3(move.x, 0f, move.y), n);
            if (into <= 0f) { _motor.BlockNormal = gap < 0f ? n : Vector3.zero; return; }

            // <b>미는 것은 걷는 속도보다 빠를 수 없고, 필요한 만큼만 밀 수 있다.</b>
            //
            // 상한만 걸고 힘은 고정으로 주면 안 된다 - 질량 범위가 1300 배(씨앗 23 kg ~ 30 t)라
            // 씨앗에 2600 N 은 한 스텝에 Δv 2.26 m/s 이고, 클램프는 언제나 한 스텝 늦으므로 공이
            // 상한을 넘어 튀어나간다(실측: 걷기 2.6 인데 공이 4.0 으로 도망갔다).
            //
            // 그래서 <b>이번 스텝에 상한까지 올리는 데 필요한 힘</b>을 먼저 구하고, 근력
            // (<see cref="_pushForceN"/>)으로 자른다. 가벼운 공은 필요한 힘이 작아 얌전히 걷는
            // 속도로 밀리고, 무거운 공은 필요한 힘이 근력을 넘어 그만큼만 느리게 움직인다 -
            // 두 끝이 한 식에서 나온다.
            Vector3 bv = body.linearVelocity;
            float along = Vector3.Dot(new Vector3(bv.x, 0f, bv.z), n);
            float cap = _motor.DesiredSpeedMps * into;
            float needed = (cap - along) * body.mass / Time.fixedDeltaTime;

            if (needed > 0f)
                body.AddForce(n * Mathf.Min(needed, _pushForceN * into), ForceMode.Force);

            // <b>펭귄이 느려지는 것은 이 한 줄에서 나온다.</b> 표면을 통과하지 못하게 막으면, 무거워서
            // 안 밀리는 공 앞에서 펭귄은 제자리걸음이 된다 - 속도 배수로 깎을 이유가 없다.
            //
            // 속도를 여기서 직접 쓰지 않는 이유: 같은 오브젝트의 두 컴포넌트가 같은 프레임에
            // linearVelocity 를 쓰면 실행 순서에 결과가 걸린다. 모터가 유일한 소유자로 남고,
            // 이쪽은 "이 방향으로는 못 간다" 만 넘긴다.
            _motor.BlockNormal = gap < 0f ? n : Vector3.zero;
        }

        private bool TryGrabNearby()
        {
            SnowBallCarrier best = null;
            float bestDist = float.MaxValue;

            foreach (SnowBallCarrier ball in FindObjectsByType<SnowBallCarrier>(FindObjectsSortMode.None))
            {
                if (ball.gameObject.scene != gameObject.scene) continue;

                Vector3 d = ball.transform.position - transform.position;
                d.y = 0f;
                float surface = d.magnitude - ball.RadiusM;      // 표면까지의 거리
                if (surface > _reachM || surface >= bestDist) continue;

                bestDist = surface;
                best = ball;
            }

            if (best == null) return false;

            Grab(best);
            return true;
        }

        private void TryGather()
        {
            if (_stage == null) _stage = FindAnyObjectByType<SnowCpuStage>();
            if (_stage == null) { LastFailure = "이 씬에 눈이 없다"; return; }

            Vector3 at = transform.position + transform.forward * _gatherAheadM;
            SnowBallCarrier made = _stage.TryCreateBall(at);

            if (made == null)
            {
                LastFailure = $"눈이 얕아 뭉칠 수 없다 (걷은 양 {_stage.LastGatheredMm} < 필요 {SnowBallCpu.MinCreateMassMm})";
                return;
            }

            Grab(made);
        }

        /// <summary>
        /// 잡는다. <b>잡은 동안은 서로 충돌하지 않게 한다</b> — 공은 커지면서 수백 kg 이 되고 펭귄은
        /// 35 kg 이라, 목표 자리로 몰리는 공이 펭귄을 밀어 올려 <b>공 위에 태운다</b>(실측: 지름 1.24 m 에서
        /// 펭귄 y 가 0.65, 공이 뒤로 밀려남). 목표 거리를 몸 밖으로 잡는 것만으로는 부족했다 —
        /// 속도로 몰기 때문에 접촉이 계속 일어난다.
        ///
        /// <para>세계와의 충돌은 그대로 둔다 — 공은 여전히 집·지면과 부딪히고, 놓으면 굴러 내려간다.
        /// 끄는 것은 <b>펭귄과 공 사이</b> 하나뿐이다.</para>
        /// </summary>
        private void Grab(SnowBallCarrier ball)
        {
            Held = ball;
            LastFailure = string.Empty;
            SetIgnore(ball, true);
        }

        /// <summary>뗀다. 여기서부터는 물리다 — 경사면 굴러 내려간다.</summary>
        public void Release()
        {
            if (Held != null) SetIgnore(Held, false);
            Held = null;
            if (_motor != null) _motor.BlockNormal = Vector3.zero;
        }

        private void SetIgnore(SnowBallCarrier ball, bool ignore)
        {
            if (_capsule == null) _capsule = GetComponent<CapsuleCollider>();
            if (_capsule == null) return;

            var ballCollider = ball.GetComponent<Collider>();
            if (ballCollider == null) return;

            Physics.IgnoreCollision(_capsule, ballCollider, ignore);
        }
    }
}
