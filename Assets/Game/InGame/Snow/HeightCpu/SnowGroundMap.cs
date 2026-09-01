using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>구운 바닥.</b> 셀당 바닥 높이와 눈 가능 여부를 담은 에셋이고, 런타임에는 읽기 전용이다.
    ///
    /// <para><b>왜 에셋인가</b> — 이 데이터는 맵과 함께 배포되는 <b>불변</b> 저작물이므로 서버도
    /// 클라이언트도 로드 시점에 같은 값을 갖는다. 그래서 네트워크로 보낼 것이 하나도 없다.
    /// <c>RenderTexture</c> 에 담으면 루트 <c>AGENTS.md</c> 의 "RenderTexture 는 권위를 못 든다" 에
    /// 걸리고, 데디 서버(<c>-nographics</c>)가 못 읽는다.</para>
    ///
    /// <para><b>바이트로 담는 이유는 업로드다.</b> <see cref="FloorR16"/> 는 R16 UNorm 텍스처의
    /// 페이로드 그대로이고 <see cref="SnowableR8"/> 와 <see cref="InitialDepthR8"/> 는 R8 그대로다 —
    /// <c>Texture2D.LoadRawTextureData</c> 가 변환 없이 받는다. CPU 쪽은 <see cref="TryBuildField"/>
    /// 가 한 번만 <c>ushort[]</c> 로 펼친다.</para>
    ///
    /// <para>⚠ <b>리틀엔디언 레이아웃을 가정한다.</b> 굽기와 읽기가 같은
    /// <c>Buffer.BlockCopy</c> 를 쓰므로 이 프로젝트의 타깃(x64 · ARM64)에서는 항상 일치한다.
    /// 빅엔디언 타깃이 생기면 바이트 순서를 명시로 바꿔야 한다.</para>
    /// </summary>
    [CreateAssetMenu(menuName = "PPack/Snow/Ground Map", fileName = "SnowGroundMap")]
    public sealed class SnowGroundMap : ScriptableObject
    {
        [Header("범위 — 스테이지와 같아야 한다")]
        [Tooltip("필드의 월드 원점(XZ). SnowCpuStage 의 값과 같아야 한다.")]
        [SerializeField] private Vector2 _originXZ = new Vector2(-20f, -20f);

        [Tooltip("필드 크기(m). SnowCpuStage 의 값과 같아야 한다.")]
        [SerializeField] private Vector2 _sizeMeters = new Vector2(40f, 40f);

        [Header("굽기 설정")]
        [Tooltip("레이캐스트 broad phase. 실제 Ground/Road/Obstacle/Ignore 분류는 SnowBakeSurface가 한다.")]
        [SerializeField] private LayerMask _probeLayers = ~0;

        [Tooltip("이 각도를 넘는 면에는 눈이 앉지 않는다. XZ 격자로 깊이를 재는 한 45° 부근이 한계다 " +
                 "— 60° 에서 셀이 두 배로 늘어난다(스펙 §5).")]
        [SerializeField, Range(5f, 85f)] private float _maxSlopeDeg = 50f;

        [Tooltip("레이를 쏘기 시작하는 월드 Y. 맵의 가장 높은 바닥보다 위여야 한다.")]
        [SerializeField] private float _probeTopYM = 50f;

        [Tooltip("레이 길이(m). _probeTopYM 에서 이만큼 아래까지 본다.")]
        [SerializeField] private float _probeLengthM = 120f;

        [Header("구운 결과 — 손으로 고치지 않는다")]
        [SerializeField] private int _resX;
        [SerializeField] private int _resZ;

        /// <summary>바닥 높이의 기준(월드 Y). 실제로 맞은 가장 낮은 바닥에서 굽기가 정한다.</summary>
        [SerializeField] private float _originYM;

        // ⚠ <b>인스펙터에서 숨겨야 한다.</b> 기본 인스펙터는 SerializedProperty 트리를 원소 단위로
        // 순회하므로, 이 둘이 보이는 채로 에셋을 선택하면 <b>배열 원소 수만큼</b> 바인딩이 만들어진다.
        // 240 x 220 m(3,379,200 셀)에서 원소가 1,010만 개이고, 그때 에디터가 100% CPU · RSS 10 GB 로
        // 수 분을 멈춘다(2026-08-24 실측 — 빌더의 Selection.activeObject = map 한 줄이 그것을 밟았다).
        // 40 m 맵(30만)에서는 느릴 뿐이라 안 드러났다. 어차피 헤더가 "손으로 고치지 않는다" 이므로
        // 그리지 않는 것이 맞다.
        [SerializeField, HideInInspector] private byte[] _floorR16;
        [SerializeField, HideInInspector] private byte[] _snowableR8;
        [SerializeField, HideInInspector] private byte[] _initialDepthR8;

        [SerializeField] private int _snowableCells;
        [SerializeField] private int _minFloorMm;
        [SerializeField] private int _maxFloorMm;
        [SerializeField] private string _bakedScene;
        [SerializeField] private string _bakedAtUtc;

        public Vector2 OriginXZ => _originXZ;
        public Vector2 SizeMeters => _sizeMeters;
        public LayerMask ProbeLayers => _probeLayers;
        public float MaxSlopeDeg => _maxSlopeDeg;
        public float ProbeTopYM => _probeTopYM;
        public float ProbeLengthM => _probeLengthM;

        public int ResX => _resX;
        public int ResZ => _resZ;
        public float OriginYM => _originYM;

        /// <summary>R16 UNorm 페이로드 그대로. 셀당 2 바이트.</summary>
        public byte[] FloorR16 => _floorR16;

        /// <summary>R8 페이로드 그대로. 셀당 1 바이트, 0 또는 255.</summary>
        public byte[] SnowableR8 => _snowableR8;

        /// <summary>R8 시작 적설 배율. 오래된 에셋에는 없으며 그 경우 적설 가능 셀을 255로 읽는다.</summary>
        public byte[] InitialDepthR8 => _initialDepthR8;

        public int SnowableCells => _snowableCells;
        public int MinFloorMm => _minFloorMm;
        public int MaxFloorMm => _maxFloorMm;
        public string BakedScene => _bakedScene;
        public string BakedAtUtc => _bakedAtUtc;

        public bool IsBaked => _resX > 0 && _resZ > 0
                            && _floorR16 != null && _floorR16.Length == _resX * _resZ * 2
                            && _snowableR8 != null && _snowableR8.Length == _resX * _resZ
                            && (_initialDepthR8 == null || _initialDepthR8.Length == 0
                                || _initialDepthR8.Length == _resX * _resZ);

        /// <summary>
        /// 이 맵에 맞는 격자. <b>스테이지가 아니라 맵이 원점·크기의 주인이다</b> — 굽기가 쓴 격자와
        /// 런타임 격자가 한 글자라도 다르면 바닥이 셀 하나씩 밀려 눈이 지형을 비껴 앉는다.
        /// </summary>
        public SnowFieldGeometry BuildGeometry()
            => new SnowFieldGeometry(_sizeMeters.x, _sizeMeters.y, _originXZ.x, _originXZ.y, _originYM);

        /// <summary>
        /// 런타임 필드로 펼친다. 굽기와 격자가 어긋나면 <c>false</c> 와 이유를 돌려준다 —
        /// <b>조용히 평지로 되돌아가지 않는다</b>. 그 실패는 화면에서 "경사에 눈이 없다" 로만 보이고,
        /// 원인을 찾는 데 세션이 든다.
        /// </summary>
        public bool TryBuildField(SnowFieldGeometry geo, out SnowGroundFieldCpu ground, out string error)
        {
            ground = null;

            if (!IsBaked)
            {
                error = $"{name}: 아직 굽지 않았다";
                return false;
            }

            if (geo.ResX != _resX || geo.ResZ != _resZ)
            {
                error = $"{name}: 해상도가 다르다 — 맵 {_resX}x{_resZ}, 격자 {geo.ResX}x{geo.ResZ}";
                return false;
            }

            const float eps = 1e-3f;
            if (Mathf.Abs(geo.OriginXM - _originXZ.x) > eps || Mathf.Abs(geo.OriginZM - _originXZ.y) > eps)
            {
                error = $"{name}: 원점이 다르다 — 맵 {_originXZ}, 격자 ({geo.OriginXM}, {geo.OriginZM})";
                return false;
            }

            if (Mathf.Abs(geo.OriginYM - _originYM) > eps)
            {
                error = $"{name}: 바닥 기준 Y 가 다르다 — 맵 {_originYM}, 격자 {geo.OriginYM}";
                return false;
            }

            int cells = _resX * _resZ;
            var floor = new ushort[cells];
            System.Buffer.BlockCopy(_floorR16, 0, floor, 0, cells * 2);

            var snowable = new byte[cells];
            System.Array.Copy(_snowableR8, snowable, cells);

            var initialDepth = new byte[cells];
            if (_initialDepthR8 != null && _initialDepthR8.Length == cells)
            {
                System.Array.Copy(_initialDepthR8, initialDepth, cells);
            }
            else
            {
                for (int i = 0; i < cells; i++)
                    if (snowable[i] != 0) initialDepth[i] = byte.MaxValue;
            }

            ground = new SnowGroundFieldCpu(geo, floor, snowable, initialDepth);
            error = null;
            return true;
        }

        /// <summary>
        /// 굽기 결과를 심는다. <b>굽는 도구만 부른다</b> — 런타임에 부르는 경로는 없다.
        /// </summary>
        public void WriteBake(int resX, int resZ, float originYM, ushort[] floorMm, byte[] snowable,
                              byte[] initialDepth, string sceneName)
        {
            int cells = resX * resZ;
            if (floorMm == null || floorMm.Length != cells)
                throw new System.ArgumentException("바닥 배열 길이가 해상도와 다르다", nameof(floorMm));
            if (snowable == null || snowable.Length != cells)
                throw new System.ArgumentException("마스크 길이가 해상도와 다르다", nameof(snowable));
            if (initialDepth == null || initialDepth.Length != cells)
                throw new System.ArgumentException("시작 적설 배율 길이가 해상도와 다르다", nameof(initialDepth));

            _resX = resX;
            _resZ = resZ;
            _originYM = originYM;

            _floorR16 = new byte[cells * 2];
            System.Buffer.BlockCopy(floorMm, 0, _floorR16, 0, cells * 2);
            _snowableR8 = snowable;
            _initialDepthR8 = initialDepth;

            int count = 0;
            int min = int.MaxValue;
            int max = int.MinValue;
            for (int i = 0; i < cells; i++)
            {
                if (snowable[i] == 0) continue;
                count++;
                int f = floorMm[i];
                if (f < min) min = f;
                if (f > max) max = f;
            }

            _snowableCells = count;
            _minFloorMm = count == 0 ? 0 : min;
            _maxFloorMm = count == 0 ? 0 : max;
            _bakedScene = sceneName;
            _bakedAtUtc = System.DateTime.UtcNow.ToString("u");
        }

        /// <summary>시작 적설 배율이 생기기 전 호출부와 오래된 테스트를 위한 호환 경로.</summary>
        public void WriteBake(int resX, int resZ, float originYM, ushort[] floorMm, byte[] snowable,
                              string sceneName)
        {
            var initialDepth = new byte[snowable.Length];
            for (int i = 0; i < initialDepth.Length; i++)
                if (snowable[i] != 0) initialDepth[i] = byte.MaxValue;
            WriteBake(resX, resZ, originYM, floorMm, snowable, initialDepth, sceneName);
        }
    }
}
