using UnityEngine;
using UnityEngine.Serialization;

namespace PPack
{
    /// <summary>
    /// <b>씬에 놓는 눈 상자.</b> 자기 원점·회전·크기를 갖고 <b>자기 격자를 소유</b>하는 독립 필드다
    /// (설계: <c>docs/specs/2026-08-23-snow-regions.md</c> §2·§5B).
    ///
    /// <para><b>왜 지면 시트로는 안 되는가.</b> 높이장은 XZ 당 한 층이라 시트 한 장으로는 두 가지를 못 한다 —
    /// 지붕 위 눈과 그 아래 땅의 눈을 동시에 들지 못하고, 바닥이 램프 높이로 뛰는 자리를 한 셀 안에서
    /// 이으므로 <b>램프 밑으로 눈 커튼이 내려온다</b>. 상자는 둘 다 없앤다 — 격자가 상자 로컬이라 겹쳐도
    /// 인덱스가 충돌하지 않고, 메시가 상자 경계에서 끊기므로 밖으로 흐르지 않는다.</para>
    ///
    /// <para><b>기울이면 그것이 경사 눈이다.</b> 격자가 램프 표면에 붙으므로 셀이 <c>1/cos θ</c> 로
    /// 늘어나지 않고(45°에서 12.5 → 17.7 cm 가 되던 것), 깊이를 상자의 로컬 +Y 로 재므로 이완도
    /// 그 평면 안에서 정상적으로 돈다.</para>
    ///
    /// <para><b>기본값은 굽기 0 이다.</b> 상자의 평면이 곧 표면이므로 바닥은 전 셀 0 이고 눈은 전 셀
    /// 가능하다 — 상자만 놓으면 눈이 생긴다. 상자 <b>안</b>이 울퉁불퉁할 때만 <see cref="SnowGroundMap"/>
    /// 을 물린다.</para>
    ///
    /// <para>⚠ <b>크기는 상자 그대로다.</b> 격자 해상도는 청크(2 m) 배수로 올림되지만, 올림으로 생긴
    /// 여분 셀은 <b>눈 불가</b>로 꺼진다. 그래서 화면의 눈 경계가 인스펙터의 상자와 정확히 같다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowZone : MonoBehaviour
    {
        /// <summary>상자 하나가 담을 수 있는 셀 수의 상한. 넘으면 지면 시트로 다룰 크기다.</summary>
        public const int MaxCells = 1 << 14;

        [Tooltip("상자의 로컬 XZ 크기(m). 눈이 그리는 경계가 정확히 이 사각형이다.")]
        [SerializeField] private Vector2 _sizeXZ = new Vector2(6f, 10f);

        [Tooltip("상자의 로컬 높이(m). 눈 두께가 아니라 '이 상자에 속한 공간' 의 높이다 — " +
                 "액터가 어느 zone 에 서 있는지 가리는 데 쓴다.")]
        [SerializeField, Min(0.1f)] private float _heightM = 2f;

        [Tooltip("바닥면 아래로 이만큼까지는 이 상자에 속한 것으로 본다(m). 표면에 딱 붙어 선 액터가 " +
                 "부동소수 오차로 상자 밖으로 판정되는 것을 막는다.")]
        [SerializeField, Min(0f)] private float _baseSlackM = 0.2f;

        [SerializeField, Min(0)] private int _initialDepthMm = 600;

        [Tooltip("상자 안이 울퉁불퉁할 때만 쓴다. 비우면 상자의 평면이 그대로 바닥이다.")]
        [SerializeField] private SnowGroundMap _groundMap;

        [Tooltip("상자 가장자리에서 눈을 0 으로 재우는 폭(m). 0 이면 깊이 그대로 끊겨 수직 절벽이 " +
                 "서고 눈이 떠 보인다.")]
        [SerializeField, Range(0f, 2f)] private float _edgeFadeM = 0.45f;

        [Tooltip("경계선이 상자 안쪽으로 흔들리는 최대 폭(m). 0 이면 눈이 정확한 사각형으로 끊겨 " +
                 "저작물처럼 보인다.\n\n안쪽으로만 흔들리므로 상자가 곧 눈의 최대 범위다 — " +
                 "상자를 받치는 메시에 맞춰 놓으면 눈이 메시 밖으로 나가지 않는다.")]
        [SerializeField, Range(0f, 1f), FormerlySerializedAs("_edgeSpillM")]
        private float _edgeJitterM = 0.3f;

        private SnowHeightFieldCpu _field;
        private SnowPlowStepCpu _sim;

        public Vector2 SizeXZ => _sizeXZ;
        public float HeightM => _heightM;
        public int InitialDepthMm => _initialDepthMm;
        public float EdgeFadeM => _edgeFadeM;
        public float EdgeJitterM => _edgeJitterM;

        /// <summary>이 상자의 눈. <see cref="EnsureBuilt"/> 전에는 <c>null</c>.</summary>
        public SnowHeightFieldCpu Field => _field;

        /// <summary>이완과 청크 색인. 상자마다 하나다 — 상자가 작아서 트리도 작다.</summary>
        public SnowPlowStepCpu Sim => _sim;

        public long TotalHeightMm => _field == null ? 0L : _field.TotalHeightMm;

        /// <summary>
        /// 피어 사이에서 같은 순서를 얻기 위한 안정 키. 이름이 아니라 <b>계층 경로</b>여야 한다 —
        /// 같은 이름의 상자가 둘 있을 수 있고, 그때 순서가 갈리면 복제 대상이 서로 다른 상자가 된다.
        /// </summary>
        public string StableId
        {
            get
            {
                var sb = new System.Text.StringBuilder(64);
                Transform t = transform;
                while (t != null)
                {
                    sb.Insert(0, t.name).Insert(0, '/');
                    t = t.parent;
                }
                return sb.ToString();
            }
        }

        /// <summary>
        /// 격자를 세운다. 두 번 불러도 안전하다.
        ///
        /// <para>격자는 <b>상자 로컬</b>이다 — 원점이 <c>-크기/2</c> 이므로 상자 중심이 로컬 (0,0) 이고,
        /// 트랜스폼을 옮기거나 돌려도 셀 인덱스가 바뀌지 않는다.</para>
        ///
        /// <para><b>격자는 상자보다 한 칸씩 넓다.</b> 흔들림이 안쪽으로만 가므로 넘칠 자리는 필요 없지만,
        /// <b>커버리지가 0 인 칸이 격자 안에 반드시 있어야</b> 셰이더의 clamp 샘플러가 마지막 값을
        /// 패널 밖으로 끌고 나가지 않는다(`SnowGroundFieldCpu.FromRect` 의 테두리 규칙). 그 여유 칸은
        /// 커버리지 0(용량 0)이라 눈이 실리지 않는다.</para>
        /// </summary>
        public void EnsureBuilt()
        {
            if (_field != null) return;

            // 청크 배수 올림까지 포함해 <b>상자 중심에 대칭</b>으로 만든다. 요청 크기로 원점을
            // 잡으면 올림분이 한쪽에만 붙어 격자와 메시가 상자에서 밀린다.
            float padX = _sizeXZ.x + SnowFieldGeometry.CellSizeM * 2f;
            float padZ = _sizeXZ.y + SnowFieldGeometry.CellSizeM * 2f;
            var probe = new SnowFieldGeometry(padX, padZ, 0f, 0f);
            float fullX = probe.ResX * SnowFieldGeometry.CellSizeM;
            float fullZ = probe.ResZ * SnowFieldGeometry.CellSizeM;
            var geo = new SnowFieldGeometry(fullX, fullZ, -fullX * 0.5f, -fullZ * 0.5f);
            if (geo.CellCount > MaxCells)
            {
                Debug.LogError($"{nameof(SnowZone)} '{name}': 셀 {geo.CellCount} 개는 상자로 다룰 크기가 " +
                               $"아니다(상한 {MaxCells}). 지면 시트로 옮기거나 상자를 쪼갤 것.");
                return;
            }

            SnowGroundFieldCpu ground = BuildGround(geo);
            _field = new SnowHeightFieldCpu(geo, _initialDepthMm, ground);
            _sim = new SnowPlowStepCpu(_field);
        }

        public void Release()
        {
            _field = null;
            _sim = null;
        }

        /// <summary>
        /// 이 상자의 바닥·커버리지. 굽힌 맵이 없으면 <b>평면 + 흩뜨린 상자 사각형</b>을 그 자리에서
        /// 만든다 — 커버리지 하나가 권위(용량)와 렌더(마감)를 동시에 정의한다.
        /// </summary>
        private SnowGroundFieldCpu BuildGround(SnowFieldGeometry geo)
        {
            if (_groundMap != null)
            {
                if (_groundMap.TryBuildField(geo, out SnowGroundFieldCpu baked, out string error)) return baked;
                Debug.LogError($"{nameof(SnowZone)} '{name}': 바닥 맵을 쓸 수 없다 — {error}. 평면으로 돈다.");
            }

            return SnowGroundFieldCpu.FromRect(geo, null,
                                               -_sizeXZ.x * 0.5f, -_sizeXZ.y * 0.5f,
                                               _sizeXZ.x * 0.5f, _sizeXZ.y * 0.5f,
                                               _edgeFadeM, _edgeJitterM);
        }

        /// <summary>
        /// 월드 점이 이 상자 안인가. 로컬 XZ 는 상자 사각형, 로컬 Y 는
        /// <c>[-baseSlack, height]</c> 다. <b>깊이가 아니라 공간</b>을 묻는 것이다.
        /// </summary>
        public bool Contains(Vector3 worldPos)
        {
            Vector3 local = transform.InverseTransformPoint(worldPos);
            return local.x >= -_sizeXZ.x * 0.5f && local.x <= _sizeXZ.x * 0.5f
                && local.z >= -_sizeXZ.y * 0.5f && local.z <= _sizeXZ.y * 0.5f
                && local.y >= -_baseSlackM && local.y <= _heightM;
        }

        /// <summary>이 상자에서의 표면 높이(로컬 Y) = 바닥 + 깊이. 밖이면 false.</summary>
        public bool TrySurfaceLocalY(Vector3 worldPos, out float localSurfaceY, out float depthM)
        {
            localSurfaceY = 0f;
            depthM = 0f;
            if (_field == null) return false;

            Vector3 local = transform.InverseTransformPoint(worldPos);
            SnowFieldGeometry geo = _field.Geo;
            if (!geo.TryWorldToCell(local.x, local.z, out int cx, out int cz)) return false;

            int ci = geo.CellIndex(cx, cz);
            depthM = _field.GetAt(ci) * 0.001f;
            float floorM = _field.Ground == null ? 0f : _field.Ground.FloorMm[ci] * 0.001f;
            localSurfaceY = floorM + depthM;
            return true;
        }

        /// <summary>표면의 <b>월드</b> Y. 상자가 기울어져 있어도 맞는 값이 나온다.</summary>
        public bool TrySurfaceWorldY(Vector3 worldPos, out float worldY, out float depthM)
        {
            worldY = 0f;
            if (!TrySurfaceLocalY(worldPos, out float localY, out depthM)) return false;

            Vector3 local = transform.InverseTransformPoint(worldPos);
            worldY = transform.TransformPoint(new Vector3(local.x, localY, local.z)).y;
            return true;
        }

        /// <summary>월드 점을 상자 로컬로. 격자 질의는 전부 이 좌표로 한다.</summary>
        public Vector3 ToLocal(Vector3 worldPos) => transform.InverseTransformPoint(worldPos);

        public Vector3 ToWorld(Vector3 localPos) => transform.TransformPoint(localPos);

        private void OnDrawGizmosSelected()
        {
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.color = new Color(0.5f, 0.8f, 1f, 0.9f);
            var center = new Vector3(0f, (_heightM - _baseSlackM) * 0.5f, 0f);
            Gizmos.DrawWireCube(center, new Vector3(_sizeXZ.x, _heightM + _baseSlackM, _sizeXZ.y));

            // 바닥면을 따로 그린다 — 눈이 앉는 평면이 어디인지가 이 컴포넌트의 핵심 정보다.
            Gizmos.color = new Color(1f, 1f, 1f, 0.9f);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_sizeXZ.x, 0f, _sizeXZ.y));
        }
    }
}
