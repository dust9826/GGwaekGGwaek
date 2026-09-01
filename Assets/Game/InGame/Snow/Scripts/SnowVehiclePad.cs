using System;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차량 밑의 제설 패드. 자기 트랜스폼으로 <see cref="SnowStampArea"/> 를 채워
    /// <see cref="SnowStage"/> 에 넘긴다 — 여기는 격자도 텍스처도 모른다.
    ///
    /// 먼지의 <c>MopPad</c> 와 달리 <b>레이캐스트를 하지 않는다.</b> 눈 필드는 세계 좌표 격자라
    /// 어느 렌더러를 맞췄는지가 의미 없고, 레이가 자기 차체를 때려 청소가 멈추는 함정
    /// (<c>Mop/AGENTS.md</c>)도 원천적으로 생기지 않는다.
    /// </summary>
    public sealed class SnowVehiclePad : MonoBehaviour
    {
        [SerializeField] private SnowStage _stage;

        [Header("패드 크기 (m)")]
        [Tooltip("진행 방향 반길이. 차량 패드는 2.4 × 1.8 m 라 1.2 다.")]
        [SerializeField, Min(0.05f)] private float _halfLength = 1.2f;
        [Tooltip("좌우 반폭.")]
        [SerializeField, Min(0.05f)] private float _halfWidth = 0.9f;

        [Header("제거량")]
        [Tooltip("한 번 지날 때 깎는 깊이(cm). **레벨 개수가 아니라 cm 로 정의한다** — " +
                 "레벨로 정의하면 깊이 단계를 늘리는 순간 필요 통과 횟수가 함께 늘어 손맛이 바뀐다.")]
        [SerializeField, Range(1, 60)] private int _removeCmPerPass = 10;
        [Tooltip("초당 몇 번까지 스탬프를 찍을지. 0 이면 물리 스텝마다 찍는다.\n" +
                 "빠르게 달릴 때 자국이 끊기면 올린다.")]
        [SerializeField, Min(0f)] private float _stampsPerSecond = 0f;

        /// <summary>이번 스텝에 <b>실제로</b> 제거된 총량(cm·셀). 연출은 이 값을 구독한다.</summary>
        public int LastRemovedCm { get; private set; }

        /// <summary>이번 스텝에 넘긴 패드. 연출이 방출 위치·방향을 여기서 읽는다.</summary>
        public SnowStampArea LastArea { get; private set; }

        /// <summary>실제로 눈이 제거된 패드 영역. HUD/VFX는 권위 격자를 다시 읽지 않고 이 이벤트만 따른다.</summary>
        public event Action<SnowStampArea> SnowCleared;

        private SnowSurfaceRenderer _renderer;
        private static int _nextStampId;
        private int _stampId;
        private float _cooldown;

        private void Awake()
        {
            if (_stage == null) _stage = FindAnyObjectByType<SnowStage>();
            if (_stage != null) _renderer = _stage.GetComponent<SnowSurfaceRenderer>();
            // 스탬프 주체를 구분하는 값. 여러 대가 같은 틱에 겹쳐 밀어도 서로를 지우지 않게 한다.
            //
            // 인스턴스 id 는 쓰지 않는다 — Unity 6 에서 obsolete 이고, 무엇보다 **피어마다 다른 값**이라
            // Fusion 이 오면 그대로 못 쓴다. 그때는 복제되는 신원(플레이어·차량 id)에서 뽑아야 한다.
            _stampId = ++_nextStampId;
        }

        private void FixedUpdate()
        {
            LastRemovedCm = 0;
            if (_stage == null) return;

            if (_stampsPerSecond > 0f)
            {
                _cooldown -= Time.fixedDeltaTime;
                if (_cooldown > 0f) return;
                _cooldown = 1f / _stampsPerSecond;
            }

            Vector3 forward = transform.forward;
            var area = new SnowStampArea(transform.position.x, transform.position.z,
                                         forward.x, forward.z, _halfLength, _halfWidth);
            LastArea = area;

            LastRemovedCm = _stage.ApplyStamp(_stampId, area, -_removeCmPerPass);

            // 연출은 **실제로 적용된 제거량**을 따른다. 0 이면 아무것도 하지 않는다 —
            // 이미 치워진 길에서 눈보라가 뜨지 않는 것이 요건이다.
            if (LastRemovedCm > 0)
            {
                _renderer?.MarkFresh(area);
                SnowCleared?.Invoke(area);
            }
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.3f, 0.8f, 1f, 0.9f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(transform.position,
                Quaternion.LookRotation(new Vector3(transform.forward.x, 0f, transform.forward.z)),
                Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_halfWidth * 2f, 0.05f, _halfLength * 2f));
            Gizmos.matrix = previous;
        }
    }
}
