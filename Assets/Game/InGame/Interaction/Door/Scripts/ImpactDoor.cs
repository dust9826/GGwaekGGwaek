using Fusion;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 운동량을 실은 물체(눈덩이든 차량이든)가 부딪히면 각도로 열리는 문.
    ///
    /// <para><b>눈덩이를 참조하지 않는다.</b> 계약은 <c>Collision.rigidbody.mass</c> 와
    /// <c>relativeVelocity</c> 뿐이다 — <c>Assets/Game/InGame/Snow/</c> 의 어떤 타입도 여기 나오지
    /// 않는다. 부수는 쪽 구현이 바뀌어도 이 스크립트는 안 바뀐다(<c>AGENTS.md</c> 참고).</para>
    ///
    /// <para><b>이 오브젝트가 곧 힌지다.</b> <c>transform.position</c> 이 힌지 축 위의 한 점,
    /// <c>transform.up</c> 이 힌지 축이다. 그래서 별도 피벗 자식이 필요 없다 — 문을 배치할 때
    /// 회전축이 되도록 놓기만 하면 된다.</para>
    ///
    /// <para><b>물리는 서버(또는 러너 없는 단독 모드)만 적분한다.</b> 복제되는 것은
    /// <see cref="NetAngleDeg"/> 뿐이고, 막힌 충돌의 덜컹·소리는 <b>원인</b>(히트 카운터 + 세기 +
    /// 열렸는지)만 복제해서 각 피어가 자기 쪽에서 재생한다 — 결과 자체는 복제하지 않는다. 러너가
    /// 없으면(싱글, 테스트 씬) <c>Object</c> 가 유효하지 않으므로 모든 분기가 로컬 사본으로 대체된다.</para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class ImpactDoor : NetworkBehaviour
    {
        [Header("범위 (양방향 여닫이, 0=닫힘)")]
        [Tooltip("밀든 당기든(어느 쪽에서 부딪히든) 0 을 중심으로 이 각도까지 양쪽 다 열린다.")]
        [SerializeField, Min(1f)] private float _maxAngleDeg = 90f;

        [Header("경첩 물리")]
        [Tooltip("문의 회전 관성(kg·m²). 크면 같은 충격에도 천천히 돈다.")]
        [SerializeField, Min(0.01f)] private float _inertiaKgM2 = 6f;
        [SerializeField, Range(0f, 10f)] private float _angularDampingPerSecond = 2.5f;
        [SerializeField, Range(0f, 1f)] private float _bounce01 = 0.15f;

        [Header("래치 (닫힘 근처에서만 적용)")]
        [SerializeField, Range(0f, 15f)] private float _latchAngleDeg = 3f;
        [Tooltip("이 각운동량(kg·m²/s)을 못 넘으면 열리지 않는다. 실측으로 채운다 — " +
                 "\"눈덩이를 굴려 키워야 열린다\"가 기준.")]
        [SerializeField, Min(0.01f)] private float _latchBreakL = 40f;

        [Header("덜컹 (막힌 충돌의 시각 피드백)")]
        [SerializeField, Range(0f, 10f)] private float _rattleMaxDeg = 1.5f;
        [SerializeField, Min(0.1f)] private float _rattleFrequencyHz = 9f;

        [Header("피드백")]
        [Tooltip("래치를 못 이긴 충돌마다 재생. 세기(0~1)에 볼륨·피치를 비례시킨다.")]
        [SerializeField] private MMF_Player _blockedFeedback;
        [SerializeField] private MMF_Player _openFeedback;
        [SerializeField, Range(0f, 1f)] private float _minBlockedVolume = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _maxBlockedVolume = 0.7f;

        [Networked] private float NetAngleDeg { get; set; }
        [Networked] private int NetHitTick { get; set; }
        [Networked] private NetworkBool NetHitOpened { get; set; }
        [Networked] private float NetHitStrength01 { get; set; }

        private DoorSwing _swing;
        private Rigidbody _body;
        private Quaternion _closedRotation;
        private MMF_Sound _blockedSound;

        private int _localHitTick;
        private bool _localHitOpened;
        private float _localHitStrength01;
        private int _lastSeenHitTick = -1;

        /// <summary>현재 문 각도(도). 표시·디버그용 — 물리 입력은 <see cref="OnCollisionEnter"/> 뿐이다.</summary>
        public float AngleDeg => IsRemoteView ? NetAngleDeg : _swing.AngleDeg;

        private bool IsNetworked => Object != null && Object.IsValid;
        private bool IsAuthority => !IsNetworked || Object.HasStateAuthority;
        private bool IsRemoteView => IsNetworked && !Object.HasStateAuthority;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _closedRotation = transform.rotation;

            _swing = new DoorSwing(_maxAngleDeg, _inertiaKgM2, _angularDampingPerSecond, _bounce01,
                _latchAngleDeg, _latchBreakL, _rattleMaxDeg, _rattleFrequencyHz);

            if (_blockedFeedback != null) _blockedSound = _blockedFeedback.GetFeedbackOfType<MMF_Sound>();
        }

        public override void Render()
        {
            if (IsRemoteView) _swing.SetAngleFromNetwork(NetAngleDeg);
        }

        private void FixedUpdate()
        {
            if (IsAuthority)
            {
                _swing.Step(Time.fixedDeltaTime);
                if (IsNetworked) NetAngleDeg = _swing.AngleDeg;
            }
            else
            {
                // 각도는 Render() 가 이미 반영했다 — 덜컹 스프링만 여기서 계속 굴린다.
                _swing.StepRattleOnly(Time.fixedDeltaTime);
            }

            ApplyVisualRotation();
            PollHitFeedback();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsAuthority) return;
            if (!ImpactMomentum.TryCompute(collision, out float momentum, out ContactPoint contact)) return;

            // 실측(Play 모드, contact.normal 로그): 이 컴포넌트가 붙은 쪽(문)이 OnCollisionEnter
            // 를 받을 때 contact.normal 은 부딪힌 쪽의 진행 방향과 같은 쪽을 가리켰다. 처음에
            // -contact.normal 로 힘 방향을 뒤집었더니 문이 미는 방향과 반대로 돌아 당겨지는
            // 것처럼 보였다 — 부호를 떼서 고쳤다.
            Vector3 impulse = contact.normal * momentum;
            Vector3 leverArm = contact.point - transform.position;
            float angularImpulseL = Vector3.Dot(Vector3.Cross(leverArm, impulse), transform.up);

            bool opened = _swing.TryApplyHit(angularImpulseL, out float blockedStrength01);
            float strength01 = opened ? 1f : blockedStrength01;

            if (IsNetworked)
            {
                NetHitTick++;
                NetHitOpened = opened;
                NetHitStrength01 = strength01;
            }
            else
            {
                _localHitTick++;
                _localHitOpened = opened;
                _localHitStrength01 = strength01;
            }
        }

        /// <summary>모든 피어가 매 프레임 확인한다 — 히트 카운터가 늘었으면 그 결과(원인)를 자기 쪽에서 재생한다.</summary>
        private void PollHitFeedback()
        {
            int tick = IsNetworked ? NetHitTick : _localHitTick;
            if (tick == _lastSeenHitTick) return;
            _lastSeenHitTick = tick;

            bool opened = IsNetworked ? (bool)NetHitOpened : _localHitOpened;
            float strength01 = IsNetworked ? NetHitStrength01 : _localHitStrength01;

            if (opened)
            {
                if (_openFeedback != null) _openFeedback.PlayFeedbacks();
                return;
            }

            _swing.Kick(strength01);

            if (_blockedSound != null)
            {
                float volume = Mathf.Lerp(_minBlockedVolume, _maxBlockedVolume, strength01);
                _blockedSound.MinVolume = volume * 0.9f;
                _blockedSound.MaxVolume = volume;
            }

            if (_blockedFeedback != null) _blockedFeedback.PlayFeedbacks();
        }

        private void ApplyVisualRotation()
        {
            Quaternion rotation = _closedRotation * Quaternion.AngleAxis(_swing.DisplayAngleDeg, Vector3.up);
            if (_body != null) _body.MoveRotation(rotation);
            else transform.rotation = rotation;
        }
    }
}
