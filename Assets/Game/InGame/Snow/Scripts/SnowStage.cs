using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 스테이지의 눈 <b>권위</b>를 소유한다. 격자 자체는 <see cref="SnowField"/>(순수 C#)이고
    /// 이 컴포넌트는 그것에 틱과 월드 범위를 붙여줄 뿐이다.
    ///
    /// <b>그래픽 의존이 없다.</b> 렌더링·업로드는 <see cref="SnowSurfaceRenderer"/> 가 하고,
    /// 헤드리스 서버에는 그것이 아예 없다(<c>docs/specs/2026-08-14-snow-surface.md</c> §3.5).
    ///
    /// 틱은 여기서 센다. Fusion 이 오면 <c>Runner.Tick</c> 으로 갈아끼우는 자리가 이 한 줄이다 —
    /// <see cref="SnowField"/> 는 <c>Time</c> 을 읽지 않고 틱을 인자로만 받는다.
    /// </summary>
    public sealed class SnowStage : MonoBehaviour
    {
        [Header("월드 범위 (XZ)")]
        [Tooltip("격자 원점. 이 오브젝트 위치가 아니라 명시값이다 — 패널을 옮겨도 격자가 흔들리면 안 된다.")]
        [SerializeField] private Vector2 _originXZ = new Vector2(-32f, -32f);
        [SerializeField] private Vector2 _sizeMeters = new Vector2(64f, 64f);

        [Header("해상도와 깊이")]
        [Tooltip("권위 셀 크기(m). 스펙 §5 의 12.5cm. 이 값을 줄이면 셀 수가 제곱으로 늘어난다 — 비싼 축이다.")]
        [SerializeField, Min(0.02f)] private float _cellSize = 0.125f;
        [Tooltip("최대 깊이(cm). 셰이더의 _SnowMaxDepth 와 맞춰야 한다.")]
        [SerializeField, Range(1, 255)] private int _maxDepthCm = 30;
        [Tooltip("시작 깊이(cm). 최대와 같으면 전면이 눈으로 덮인 상태로 시작한다.")]
        [SerializeField, Range(0, 255)] private int _startDepthCm = 30;

        public SnowField Field { get; private set; }

        /// <summary>현재 틱. <c>FixedUpdate</c> 마다 하나 오른다. Fusion 이 오면 <c>Runner.Tick</c> 이 된다.</summary>
        public int Tick { get; private set; }

        public Vector2 OriginXZ => _originXZ;
        public Vector2 SizeMeters => _sizeMeters;
        public float CellSize => _cellSize;
        public int MaxDepthCm => _maxDepthCm;

        /// <summary>
        /// 시작 깊이(cm). 계측이 <b>처녀설 합</b>을 다시 계산해 기준선이 이미 파였는지 볼 수 있게
        /// 노출한다 — <c>Field.TotalDepthCm</c> 만으로는 "원래 이만큼이었는지"를 알 수 없다.
        /// </summary>
        public int StartDepthCm => _startDepthCm;

        private void Awake()
        {
            Field = new SnowField(_originXZ.x, _originXZ.y, _sizeMeters.x, _sizeMeters.y,
                                  _cellSize, (byte)_maxDepthCm);
            Field.FillAll((byte)_startDepthCm);
        }

        private void FixedUpdate() => Tick++;

        /// <summary>
        /// 도구·이벤트가 부르는 유일한 접점. 밀기는 <paramref name="deltaCm"/> 이 음수, 적설은 양수다.
        /// 같은 <c>(Tick, stampId)</c> 는 다시 적용되지 않는다 — 재시뮬레이션이 중복으로 파지 않게 하는 규약.
        /// </summary>
        /// <returns>실제로 제거된 총량(cm·셀). 0 이면 연출도 뜨지 않아야 한다.</returns>
        public int ApplyStamp(int stampId, in SnowStampArea area, int deltaCm)
            => Field?.ApplyStamp(Tick, stampId, area, deltaCm) ?? 0;

        /// <summary>월드 XZ 의 깊이(cm). 감속 판정이 이걸 읽는다 — 텍스처는 절대 읽지 않는다.</summary>
        public int DepthCmAtWorld(Vector3 worldPosition)
            => Field?.DepthCmAtWorld(worldPosition.x, worldPosition.z) ?? 0;

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.4f, 0.7f, 1f, 0.6f);
            Gizmos.DrawWireCube(
                new Vector3(_originXZ.x + _sizeMeters.x * 0.5f, 0f, _originXZ.y + _sizeMeters.y * 0.5f),
                new Vector3(_sizeMeters.x, 0.01f, _sizeMeters.y));
        }
    }
}
