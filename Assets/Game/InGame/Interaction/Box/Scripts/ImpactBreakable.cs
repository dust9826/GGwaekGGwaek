using Fusion;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 일정 운동량 이상으로 부딪히면 부서지는 상자. <see cref="ImpactDoor"/> 와 마찬가지로 부딪히는
    /// 쪽을 참조하지 않는다 — 계약은 <see cref="ImpactMomentum"/> 이 뽑는 값(질량·속도)뿐이다.
    ///
    /// <para><b>판정은 서버(또는 러너 없는 단독 모드)만 한다.</b> 복제되는 것은 "부서졌는가" 한
    /// 비트뿐이고, 파편은 각 피어가 로컬로 만든다 — <c>Snow/</c> 의 전파 원칙, <c>ImpactDoor</c> 의
    /// "원인만 복제" 와 같은 이유다.</para>
    ///
    /// <para><b>부서지기 전엔 kinematic 이다.</b> 문과 같은 이유 — 약한 충돌에 밀려다니지 않고
    /// 제자리에 서 있다가, 문턱을 넘는 순간에만 사라진다. 문과 달리 회전하지 않으므로
    /// <c>MoveRotation</c> 스윕 불안정(<c>DoorSwing</c> 의 각속도 상한 주석 참고)은 여기 없다 —
    /// 콜라이더를 끄는 것뿐이라 폭발할 물리 상태가 없다.</para>
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [DisallowMultipleComponent]
    public sealed class ImpactBreakable : NetworkBehaviour
    {
        [Header("문턱")]
        [Tooltip("이 운동량(kg·m/s)을 넘는 충돌에 부서진다. 실측으로 채운다.")]
        [SerializeField, Min(0.01f)] private float _breakMomentumKgMps = 90f;

        [Header("파편 (로컬 전용 — 복제하지 않는다)")]
        [SerializeField, Range(1, 12)] private int _debrisCount = 6;
        [SerializeField, Min(0.01f)] private float _debrisSizeM = 0.18f;
        [SerializeField, Min(0f)] private float _debrisSpeedMps = 4f;
        [SerializeField, Min(0.1f)] private float _debrisLifetimeSeconds = 2f;

        [Header("피드백")]
        [SerializeField] private MMF_Player _breakFeedback;

        [Networked] private NetworkBool NetBroken { get; set; }

        private Rigidbody _body;
        private Collider _collider;
        private Transform _visual;
        private bool _localBroken;
        private bool _handled;

        private bool IsNetworked => Object != null && Object.IsValid;
        private bool IsAuthority => !IsNetworked || Object.HasStateAuthority;

        /// <summary>표시·디버그용.</summary>
        public bool IsBroken => IsNetworked ? (bool)NetBroken : _localBroken;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.isKinematic = true;
            _collider = GetComponent<Collider>();
            _visual = transform.Find("Visual");
        }

        private void Update()
        {
            if (_handled || !IsBroken) return;
            _handled = true;
            HandleBreak();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (!IsAuthority || IsBroken) return;
            if (!ImpactMomentum.TryCompute(collision, out float momentum, out _)) return;
            if (momentum < _breakMomentumKgMps) return;

            if (IsNetworked) NetBroken = true;
            else _localBroken = true;
        }

        private void HandleBreak()
        {
            if (_collider != null) _collider.enabled = false;
            if (_visual != null) _visual.gameObject.SetActive(false);
            if (_breakFeedback != null) _breakFeedback.PlayFeedbacks();
            SpawnDebris();
        }

        private void SpawnDebris()
        {
            // 데디 서버는 GPU 가 없고 표현 계층을 만들 이유도 없다 — 루트 AGENTS.md 의 "표현은
            // 별도 계층" 이 여기서는 파편 생성 자체를 건너뛰는 것으로 나타난다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) return;

            Vector3 origin = _visual != null ? _visual.position : transform.position;
            for (int i = 0; i < _debrisCount; i++)
            {
                GameObject chunk = GameObject.CreatePrimitive(PrimitiveType.Cube);
                chunk.name = "DebrisChunk";
                chunk.transform.SetPositionAndRotation(origin, Random.rotation);
                chunk.transform.localScale = Vector3.one * _debrisSizeM;

                Rigidbody chunkBody = chunk.AddComponent<Rigidbody>();
                chunkBody.mass = 0.5f;
                Vector3 direction = Random.onUnitSphere;
                if (direction.y < 0f) direction.y = -direction.y;
                chunkBody.linearVelocity = direction * _debrisSpeedMps;
                chunkBody.angularVelocity = Random.insideUnitSphere * 10f;

                Destroy(chunk, _debrisLifetimeSeconds);
            }
        }
    }
}
