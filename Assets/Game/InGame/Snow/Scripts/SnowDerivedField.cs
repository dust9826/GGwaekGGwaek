using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// CPU 권위 격자에서 <b>렌더링용 파생 텍스처 넷</b>을 만든다. GPU 절반(클라 전용)이고,
    /// <b>어떤 판정도 여기서 나온 것을 읽지 않는다</b> — 그래서 그래픽 디바이스가 없으면 아예 안 만든다.
    ///
    /// 이 컴포넌트가 v7 이식의 접합면이다. 스파이크는 이 넷을 GPU 시뮬(<c>SnowPileFieldV7.compute</c>)이
    /// 만들었는데 그 시뮬은 권위라서 데디 서버에 GPU 가 없는 이 프로젝트로 올 수 없다. 대신
    /// <see cref="SnowField"/>(CPU 권위)를 높이 텍스처로 올리고 <b>그 아래는 v7 커널 그대로</b> 돌린다.
    ///
    /// 넷과 그 순서(<c>SnowSurfaceDerive.compute</c>):
    /// <list type="number">
    /// <item><b>HeightDilate</b> — 두 반경의 팽창 상한. 존재 판정과 필렛의 소스.</item>
    /// <item><b>LumpBake</b> — 구운 로브. 눈이 각진 슬래브가 아니라 둥근 로브로 읽히게 만든다.</item>
    /// <item><b>CoarseMaxBlock → CoarseMaxDilate</b> — 빈 공간 건너뛰기의 상한.</item>
    /// </list>
    ///
    /// ⚠ <b>이 순서는 스타일이 아니라 의존 체인이다</b>(v7 원본 주석). 굽기의 relief 게이트가 팽창을 읽고
    /// 조대 최대가 굽기 결과를 읽는다. 그리고 조대 최대는 <b>매번 전 필드</b>를 다시 만든다 — 굽기 창이
    /// 무언가를 놓쳤거나 반지름 0 으로 굽기를 건너뛰어도 마처의 상한이 안전해야 하기 때문이다.
    /// </summary>
    [RequireComponent(typeof(SnowStage))]
    public sealed class SnowDerivedField : MonoBehaviour
    {
        private const int ThreadGroup = 8;      // 컴퓨트의 SNOW_TG 와 같아야 한다

        [Header("컴퓨트")]
        [Tooltip("Shaders/SnowSurfaceDerive.compute")]
        [SerializeField] private ComputeShader _derive;

        [Header("조대 최대 (빈 공간 건너뛰기)")]
        [Tooltip("블록 한 변의 텍셀 수. v7 기본값 8.")]
        [SerializeField, Range(2, 32)] private int _coarseBlock = 8;
        [Tooltip("블록 최대를 이만큼 팽창한다. 마처의 안전 반경이 이 값 × 블록 크기다.")]
        [SerializeField, Range(1, 8)] private int _coarseDilate = 2;

        [Header("높이 팽창")]
        [SerializeField, Range(0, 6)] private int _heightDilateRadius = 1;
        [SerializeField, Range(0, 8)] private int _filletDilateRadius = 2;

        [Header("로브 (v7 SnowLumpLattice)")]
        [Tooltip("구 반지름(m). **0 이면 로브가 통째로 꺼진다** — 마처가 탭도 하지 않는다(A/B 스위치).")]
        [SerializeField, Min(0f)] private float _lumpRadiusM = 0.30f;
        [SerializeField, Min(0.01f)] private float _lumpSpacingM = 0.42f;
        [SerializeField, Range(0f, 1f)] private float _lumpJitter = 0.65f;
        [SerializeField, Range(0f, 1f)] private float _lumpRadiusVary = 0.35f;
        [Tooltip("맨땅 문턱 위로 이 깊이만큼 쌓이면 로브가 완전히 든다.")]
        [SerializeField, Min(0.001f)] private float _lumpGateDepthM = 0.06f;
        [Tooltip("이 국소 기복(팽창 − 높이)에서 기울기 항이 포화한다.")]
        [SerializeField, Min(0.001f)] private float _lumpReliefM = 0.10f;
        [Tooltip("0 = 눈이 깊은 곳 전부에 로브, 1 = 기복이 있는 곳에만.")]
        [SerializeField, Range(0f, 1f)] private float _lumpSlopeStrength = 0.35f;
        [Tooltip("이 높이 미만은 맨땅이다. 마처의 같은 문턱과 맞춰야 로브가 맨땅에 떠다니지 않는다.")]
        [SerializeField, Min(0f)] private float _minSnowHeightM = 0.01f;

        private static readonly int SrcTexId = Shader.PropertyToID("_SrcTex");
        private static readonly int DilateDstId = Shader.PropertyToID("_DilateDst");
        private static readonly int DilateSrcId = Shader.PropertyToID("_DilateSrc");
        private static readonly int CoarseDstId = Shader.PropertyToID("_CoarseDst");
        private static readonly int CoarseSrcId = Shader.PropertyToID("_CoarseSrc");
        private static readonly int LumpSrcId = Shader.PropertyToID("_LumpBakeSrc");
        private static readonly int LumpDstId = Shader.PropertyToID("_LumpBakeDst");
        private static readonly int ResXId = Shader.PropertyToID("_ResX");
        private static readonly int ResZId = Shader.PropertyToID("_ResZ");
        private static readonly int CoarseResXId = Shader.PropertyToID("_CoarseResX");
        private static readonly int CoarseResZId = Shader.PropertyToID("_CoarseResZ");
        private static readonly int CoarseBlockId = Shader.PropertyToID("_CoarseBlock");
        private static readonly int CoarseDilateId = Shader.PropertyToID("_CoarseDilate");
        private static readonly int HeightDilateRId = Shader.PropertyToID("_HeightDilateRadius");
        private static readonly int FilletDilateRId = Shader.PropertyToID("_FilletDilateRadius");
        private static readonly int LumpResXId = Shader.PropertyToID("_LumpBakeResX");
        private static readonly int LumpResZId = Shader.PropertyToID("_LumpBakeResZ");
        private static readonly int LumpWindowId = Shader.PropertyToID("_LumpBakeWindow");
        private static readonly int LumpMinSnowId = Shader.PropertyToID("_LumpMinSnowHeight");
        private static readonly int PatchMinId = Shader.PropertyToID("_PatchMin");
        private static readonly int TexelSizeId = Shader.PropertyToID("_TexelSize");
        private static readonly int InvTexelSizeId = Shader.PropertyToID("_InvTexelSize");
        private static readonly int LumpRadiusId = Shader.PropertyToID("_LumpRadiusM");
        private static readonly int LumpSpacingId = Shader.PropertyToID("_LumpSpacingM");
        private static readonly int LumpSpacingInvId = Shader.PropertyToID("_LumpSpacingInv");
        private static readonly int LumpJitterId = Shader.PropertyToID("_LumpJitter");
        private static readonly int LumpRadiusVaryId = Shader.PropertyToID("_LumpRadiusVary");
        private static readonly int LumpGateInvId = Shader.PropertyToID("_LumpGateInv");
        private static readonly int LumpReliefInvId = Shader.PropertyToID("_LumpReliefInv");
        private static readonly int LumpSlopeStrId = Shader.PropertyToID("_LumpSlopeStrength");

        private SnowStage _stage;
        private Texture2D _heightTex;          // 눈 높이(m). CPU 격자의 업로드 결과
        private float[] _heightPixels;
        private RenderTexture _dilateRt;
        private RenderTexture _coarseBlockRt;
        private RenderTexture _coarseMaxRt;
        private RenderTexture _lumpBakeRt;
        private int _resX;
        private int _resZ;
        private int _coarseResX;
        private int _coarseResZ;
        private bool _ready;

        /// <summary>마처가 읽는 넷. 준비되지 않았으면 전부 null 이다.</summary>
        public Texture HeightTexture => _heightTex;
        public Texture CoarseMaxTexture => _coarseMaxRt;
        public Texture DilatedHeightTexture => _dilateRt;
        public Texture LumpBakeTexture => _lumpBakeRt;

        public bool Ready => _ready;

        /// <summary>격자의 월드 XZ 최소 코너. 마처의 <c>_PatchMin</c> 이다.</summary>
        public Vector2 PatchMin => _stage != null ? _stage.OriginXZ : Vector2.zero;
        public float PatchSizeX => _stage != null ? _stage.SizeMeters.x : 1f;
        public float PatchSizeZ => _stage != null ? _stage.SizeMeters.y : 1f;

        /// <summary>패널 바닥의 월드 Y. 눈은 여기서부터 위로 쌓인다.</summary>
        public float GroundY { get; set; }

        /// <summary>맨땅 문턱(m). 마처와 굽기가 같은 값을 써야 한다.</summary>
        public float MinSnowHeightM => _minSnowHeightM;

        /// <summary>조대 최대가 유효한 반경(m). 마처의 건너뛰기 상한이 이 안에서만 성립한다.</summary>
        public float CoarseSafeRadiusM =>
            Mathf.Clamp(_coarseDilate, 1, 8) * _coarseBlock * (_stage != null ? _stage.CellSize : 0.125f);

        public float LumpRadiusM => _lumpRadiusM;

        /// <summary>
        /// 관측된 최대 눈 높이(m). v7 은 GPU 의 HeightMax 커널 + 리드백으로 얻었지만 우리는 CPU 격자를
        /// 이미 손에 들고 있으므로 <b>업로드하면서 같이 잰다</b> — 리드백이 없으므로 지연도 없다.
        /// 마처의 천장(_MarchTopY)이 이 값을 따라 내려오면 빈 공간을 덜 훑는다.
        /// </summary>
        public float FieldMaxHeight { get; private set; }

        /// <summary>
        /// 로브 파라미터를 렌더러가 정하고 굽기가 받는다. v7 과 같은 방향이다 — 룩 노브는 렌더러 소유이고
        /// 굽기는 그것을 실행할 뿐이라, 두 곳이 다른 값을 갖는 상태가 생기지 않는다.
        /// </summary>
        public void ApplyLumpBakeParams(float radiusM, float spacingM, float jitter, float radiusVary,
                                        float gateDepthM, float reliefM, float slopeStrength,
                                        float minSnowHeightM)
        {
            _lumpRadiusM = radiusM;
            _lumpSpacingM = spacingM;
            _lumpJitter = jitter;
            _lumpRadiusVary = radiusVary;
            _lumpGateDepthM = Mathf.Max(0.001f, gateDepthM);
            _lumpReliefM = Mathf.Max(0.001f, reliefM);
            _lumpSlopeStrength = slopeStrength;
            _minSnowHeightM = minSnowHeightM;
        }

        private void Awake()
        {
            _stage = GetComponent<SnowStage>();

            // 권위는 SnowStage 에 있고 여기는 연출이다. 헤드리스에서는 존재하지 않는다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) enabled = false;
        }

        private void OnDestroy() => Release();

        private void Release()
        {
            if (_heightTex != null) Destroy(_heightTex);
            if (_dilateRt != null) _dilateRt.Release();
            if (_coarseBlockRt != null) _coarseBlockRt.Release();
            if (_coarseMaxRt != null) _coarseMaxRt.Release();
            if (_lumpBakeRt != null) _lumpBakeRt.Release();

            _heightTex = null;
            _dilateRt = null;
            _coarseBlockRt = null;
            _coarseMaxRt = null;
            _lumpBakeRt = null;
            _ready = false;
        }

        /// <summary>
        /// 텍스처를 만든다. <b><see cref="Awake"/> 에서 하지 않는다</b> — 같은 GameObject 의 컴포넌트
        /// <c>Awake</c> 순서가 보장되지 않아 <see cref="SnowStage.Field"/> 가 아직 null 일 수 있다.
        /// 이 프로젝트는 그 실수로 한 번 물렸다(<c>Snow/AGENTS.md</c> 함정 1).
        /// </summary>
        private bool Ensure()
        {
            if (_ready) return true;
            if (_derive == null || _stage == null) return false;

            SnowField field = _stage.Field;
            if (field == null) return false;

            _resX = field.Width;
            _resZ = field.Height;
            _coarseResX = Mathf.Max(1, (_resX + _coarseBlock - 1) / _coarseBlock);
            _coarseResZ = Mathf.Max(1, (_resZ + _coarseBlock - 1) / _coarseBlock);

            // 높이는 **미터**다. 마처가 그렇게 읽는다(SampleFieldH).
            _heightTex = new Texture2D(_resX, _resZ, TextureFormat.RFloat, mipChain: false, linear: true)
            {
                name = "SnowHeight",
                wrapMode = TextureWrapMode.Clamp,
                filterMode = FilterMode.Bilinear,
                hideFlags = HideFlags.DontSave,
            };
            _heightPixels = new float[_resX * _resZ];

            _dilateRt = CreateRt("SnowDilated", _resX, _resZ, RenderTextureFormat.RGFloat, FilterMode.Point);
            _coarseBlockRt = CreateRt("SnowCoarseBlock", _coarseResX, _coarseResZ,
                                      RenderTextureFormat.RFloat, FilterMode.Point);
            _coarseMaxRt = CreateRt("SnowCoarseMax", _coarseResX, _coarseResZ,
                                    RenderTextureFormat.RFloat, FilterMode.Point);

            // 굽기는 필드의 **2배 해상도**다(v7: 12.5cm 필드에 6.25cm 굽기).
            // R8 랜덤 라이트를 지원하지 않는 플랫폼에서는 RHalf 로 떨어진다.
            RenderTextureFormat bakeFormat =
                SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.R8)
                    ? RenderTextureFormat.R8
                    : RenderTextureFormat.RHalf;
            _lumpBakeRt = CreateRt("SnowLumpBake", _resX * 2, _resZ * 2, bakeFormat, FilterMode.Bilinear);

            _ready = true;
            return true;
        }

        private static RenderTexture CreateRt(string name, int width, int height,
                                              RenderTextureFormat format, FilterMode filter)
        {
            var rt = new RenderTexture(width, height, 0, format, RenderTextureReadWrite.Linear)
            {
                name = name,
                enableRandomWrite = true,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave,
            };
            rt.Create();
            return rt;
        }

        /// <summary>
        /// CPU 격자를 높이 텍스처로 올리고 파생 셋을 다시 만든다. 프레임당 한 번, 렌더러가 부른다.
        /// </summary>
        public void Refresh()
        {
            if (!enabled || !Ensure()) return;

            SnowField field = _stage.Field;
            const float cmToM = 0.01f;

            int maxCm = 0;
            for (int y = 0; y < _resZ; y++)
            {
                int row = y * _resX;
                for (int x = 0; x < _resX; x++)
                {
                    int cm = field.DepthCmAtCell(x, y);
                    if (cm > maxCm) maxCm = cm;
                    _heightPixels[row + x] = cm * cmToM;
                }
            }

            FieldMaxHeight = maxCm * cmToM;

            _heightTex.SetPixelData(_heightPixels, 0);
            _heightTex.Apply(updateMipmaps: false);

            PushShared();

            // ⚠ 순서가 의존 체인이다(v7 원본): 팽창 → 굽기 → 조대 최대.
            DispatchDilate();
            DispatchLumpBake();
            DispatchCoarseMax();
        }

        private void PushShared()
        {
            float cell = _stage.CellSize;
            _derive.SetInt(ResXId, _resX);
            _derive.SetInt(ResZId, _resZ);
            _derive.SetVector(PatchMinId, new Vector4(PatchMin.x, PatchMin.y, 0f, 0f));
            _derive.SetFloat(TexelSizeId, cell);
            _derive.SetFloat(InvTexelSizeId, 1f / Mathf.Max(1e-5f, cell));
        }

        private void DispatchDilate()
        {
            _derive.SetInt(HeightDilateRId, Mathf.Clamp(_heightDilateRadius, 0, 6));
            _derive.SetInt(FilletDilateRId, Mathf.Clamp(_filletDilateRadius, 0, 8));

            int kernel = _derive.FindKernel("HeightDilate");
            _derive.SetTexture(kernel, SrcTexId, _heightTex);
            _derive.SetTexture(kernel, DilateDstId, _dilateRt);
            Dispatch(kernel, _resX, _resZ);
        }

        private void DispatchLumpBake()
        {
            _derive.SetInt(LumpResXId, _resX * 2);
            _derive.SetInt(LumpResZId, _resZ * 2);
            _derive.SetFloat(LumpRadiusId, _lumpRadiusM);
            _derive.SetFloat(LumpSpacingId, _lumpSpacingM);
            _derive.SetFloat(LumpSpacingInvId, 1f / Mathf.Max(1e-4f, _lumpSpacingM));
            _derive.SetFloat(LumpJitterId, _lumpJitter);
            _derive.SetFloat(LumpRadiusVaryId, _lumpRadiusVary);
            _derive.SetFloat(LumpGateInvId, 1f / Mathf.Max(1e-4f, _lumpGateDepthM));
            _derive.SetFloat(LumpReliefInvId, 1f / Mathf.Max(1e-4f, _lumpReliefM));
            _derive.SetFloat(LumpSlopeStrId, _lumpSlopeStrength);
            _derive.SetFloat(LumpMinSnowId, _minSnowHeightM);

            // 반지름 0 은 "탭도 하지 않는다"는 뜻이다(v7 A/B 스위치). 그러면 굽지도 않는다.
            if (_lumpRadiusM <= 1e-5f) return;

            // 창은 지금 전 필드다. dirty rect 로 좁히는 것은 다음 최적화이고, 그때도 조대 최대는
            // 전 필드를 다시 만들어야 마처의 상한이 안전하다(v7 원본 주석).
            _derive.SetVector(LumpWindowId, new Vector4(0f, 0f, _resX * 2, _resZ * 2));

            int kernel = _derive.FindKernel("LumpBake");
            _derive.SetTexture(kernel, SrcTexId, _heightTex);
            _derive.SetTexture(kernel, DilateSrcId, _dilateRt);
            _derive.SetTexture(kernel, LumpDstId, _lumpBakeRt);
            Dispatch(kernel, _resX * 2, _resZ * 2);
        }

        private void DispatchCoarseMax()
        {
            _derive.SetInt(CoarseResXId, _coarseResX);
            _derive.SetInt(CoarseResZId, _coarseResZ);
            _derive.SetInt(CoarseBlockId, _coarseBlock);
            _derive.SetInt(CoarseDilateId, Mathf.Clamp(_coarseDilate, 1, 8));

            int block = _derive.FindKernel("CoarseMaxBlock");
            _derive.SetTexture(block, SrcTexId, _heightTex);
            _derive.SetTexture(block, LumpSrcId, _lumpBakeRt);
            _derive.SetTexture(block, CoarseDstId, _coarseBlockRt);
            Dispatch(block, _coarseResX, _coarseResZ);

            int dilate = _derive.FindKernel("CoarseMaxDilate");
            _derive.SetTexture(dilate, CoarseSrcId, _coarseBlockRt);
            _derive.SetTexture(dilate, CoarseDstId, _coarseMaxRt);
            Dispatch(dilate, _coarseResX, _coarseResZ);
        }

        private void Dispatch(int kernel, int width, int height)
        {
            int gx = Mathf.Max(1, (width + ThreadGroup - 1) / ThreadGroup);
            int gy = Mathf.Max(1, (height + ThreadGroup - 1) / ThreadGroup);
            _derive.Dispatch(kernel, gx, gy, 1);
        }
    }
}
