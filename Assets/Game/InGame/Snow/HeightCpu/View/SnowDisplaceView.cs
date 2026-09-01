using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 저사양 눈을 그린다. <see cref="SnowCpuStageView"/> 의 대안이고, 둘 중 하나만 켜진다
    /// (<see cref="SnowSystem"/> 가 정한다).
    ///
    /// <para><b>이쪽이 존재하는 이유는 셰이더 하나가 아니다.</b> 마처는 GLES3 에서 컴파일되지 않고
    /// (`target 4.5` + 프래그먼트 `SV_Depth`), 그 위에 마처를 <b>먹이기 위한</b> CPU 굽기가 남는다 -
    /// 로브·둥근 어깨·coarse-max 가 M5 Pro 에서 1.79 ms(844,800 셀)다. 이 경로는 그 굽기를 하나도
    /// 하지 않는다. 올리는 것은 높이 텍스처 하나이고, 그것은 권위 격자의 <c>ushort[]</c> 를 그대로
    /// 넘긴 것이다.</para>
    ///
    /// <para><b>정점 간격은 셀 간격이 아니다.</b> 셀은 12.5 cm 이고 그대로 정점을 깔면 60x60 m 필드가
    /// 23 만 정점이 된다. 기본 0.25 m 로 2 배 거칠게 깔아 약 5.8 만 정점으로 만든다 - 실루엣의 선명함을
    /// 정점 수와 맞바꾸는 노브이고, 그것이 이 경로가 "저사양" 인 이유의 절반이다(나머지 절반은 굽기 0).</para>
    ///
    /// <para><b>필드마다 패널 하나다.</b> 지면 시트 하나 + 눈 상자(<see cref="SnowZone"/>) 하나씩.
    /// 상자 패널은 그 상자의 자식으로 붙고 로컬 포즈가 항등이므로, <b>정점의 오브젝트 XZ 가 곧 그
    /// 필드의 좌표</b>다 — 셰이더가 오브젝트 공간에서 도는 이유이고, 그래서 기울어진 상자가 코드
    /// 분기 없이 그려진다.</para>
    /// </summary>
    [RequireComponent(typeof(SnowCpuStage))]
    [DisallowMultipleComponent]
    public sealed class SnowDisplaceView : MonoBehaviour
    {
        [Tooltip("정점 간격(m). 좁히면 자국의 벽이 선명해지고 정점이 제곱으로 늘어난다.\n\n" +
                 "0.25 m 는 60x60 m 필드에서 약 5.8만 정점이다. 셀(12.5 cm)까지 좁히면 23만이 된다.")]
        [SerializeField, Range(0.125f, 2f)] private float _vertexSpacingM = 0.25f;

        [SerializeField] private Light _sun;

        [Tooltip("가장자리 단면. 0 = 둥근 어깨(두께를 끝까지 들고 가다 굴러 떨어진다), " +
                 "1 = 흘러내린 치마(가장자리로 갈수록 얇아지며 퍼진다).\n\n" +
                 "폭은 SnowZone / SnowCpuStage 의 Edge Fade M 이 정한다 — 이 값은 모양만 바꾼다.")]
        [SerializeField, Range(0f, 1f)] private float _edgeProfile;

        [Tooltip("지면 패널을 이 크기로 쪼갠다(m). 쪼개면 프러스텀 컬링이 들어서 화면 밖 타일을 " +
                 "안 그린다. 작을수록 컬링이 촘촘하고 드로우 콜이 는다.\n\n" +
                 "0 이면 안 쪼갠다(2026-08-25 이전 동작).")]
        [SerializeField, Range(0f, 60f)] private float _tileSizeM = 16f;

        [Tooltip("지면의 높이 텍스처를 더러운 사각형만 올린다. 끄면 매 프레임 통째로 올린다" +
                 "(2026-08-25 이전 동작).\n\n" +
                 "타일링과 독립이다 — 둘 다 끄면 예전 동작 그대로이고, 그래서 결함이 나왔을 때 " +
                 "어느 쪽이 원인인지 하나씩 가를 수 있다.")]
        [SerializeField] private bool _partialUpload = true;

        private static readonly int HeightTexId = Shader.PropertyToID("_HeightTex");
        private static readonly int FloorTexId = Shader.PropertyToID("_FloorTex");
        private static readonly int MaskTexId = Shader.PropertyToID("_MaskTex");
        private static readonly int FloorOriginYId = Shader.PropertyToID("_FloorOriginY");
        private static readonly int PatchMinId = Shader.PropertyToID("_PatchMin");
        private static readonly int InvPatchId = Shader.PropertyToID("_InvPatchSize");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSizeM");
        private static readonly int EdgeProfileId = Shader.PropertyToID("_EdgeProfile");



        /// <summary>필드 하나를 그리는 한 벌. 지면과 상자가 같은 것을 쓴다.</summary>
        private sealed class Panel
        {
            public SnowHeightFieldCpu Field;

            /// <summary>타일들의 부모. 예전 단일 패널의 자리를 그대로 쓴다.</summary>
            public GameObject Root;

            /// <summary><b>타일이 공유한다.</b> 텍스처도 유니폼도 전부 같으므로 한 장이면 된다.</summary>
            public Material Material;

            public GameObject[] TileObjects;
            public Mesh[] TileMeshes;

            public Texture2D HeightTex;
            public Texture2D FloorTex;
            public Texture2D MaskTex;

            /// <summary>세운 직후에는 텍스처가 빈 상태다. 반드시 한 번은 통째로 올려야 한다.</summary>
            public bool NeedsFullUpload = true;
        }

        private SnowCpuStage _stage;
        private readonly List<Panel> _panels = new List<Panel>(8);

        /// <summary>스테이징 한 변의 상한(셀). 넘는 사각형은 전체 업로드로 되돌아간다.</summary>
        private const int MaxStagingCells = 256;

        /// <summary>2의 거듭제곱 한 변마다 하나. 매 프레임 새로 만들지 않으려고 재사용한다.</summary>
        private readonly Dictionary<int, Texture2D> _staging = new Dictionary<int, Texture2D>(6);

        /// <summary>스테이징에 담을 때 쓰는 임시 버퍼. 가장 큰 스테이징에 맞춰 한 번만 만든다.</summary>
        private ushort[] _stagingScratch;

        private bool _partialUploadSupported;

        /// <summary>
        /// 검증용 — 지난 프레임에 GPU 로 올린 높이 바이트.
        ///
        /// <para>⚠ <b>이 값 하나만 보고 판단하지 마라.</b> 프레임은 수백 Hz 인데 시뮬은
        /// <c>FixedUpdate</c>(50 Hz)라, <b>대부분의 프레임은 정말로 올릴 것이 없어서 0 이다.</b>
        /// 실측에는 <see cref="DebugUploadedBytesTotal"/> 의 <b>차분</b>을 쓴다.</para>
        /// </summary>
        public long DebugUploadedBytesLastFrame { get; private set; }

        /// <summary>검증용 — 켜진 뒤로 올린 높이 바이트의 누적. 두 번 읽어 차분을 낸다.</summary>
        public long DebugUploadedBytesTotal { get; private set; }

        /// <summary>이 벌이 어떤 필드 집합으로 세워졌는지. 하나라도 갈리면 통째로 다시 세운다.</summary>
        private readonly List<SnowHeightFieldCpu> _builtFor = new List<SnowHeightFieldCpu>(8);

        /// <summary>검증용 — 지금 그리는 패널 수. 지면 1 + 상자 수여야 한다.</summary>
        public int PanelCount => _panels.Count;

        /// <summary>검증용 — 지금 그리는 타일 총수. 지면이 쪼개졌는지 이걸로 본다.</summary>
        public int TileCount
        {
            get
            {
                int n = 0;
                for (int i = 0; i < _panels.Count; i++)
                    n += _panels[i].TileMeshes == null ? 0 : _panels[i].TileMeshes.Length;
                return n;
            }
        }

        private void Awake()
        {
            _stage = GetComponent<SnowCpuStage>();
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) enabled = false;

            // 영역 복사가 없으면 부분 업로드를 할 수 없다. 그때는 예전처럼 통째로 올린다.
            _partialUploadSupported =
                (SystemInfo.copyTextureSupport & CopyTextureSupport.Basic) != 0;
        }

        private void LateUpdate()
        {
            SnowHeightFieldCpu ground = _stage.Field;
            if (ground == null) return;

            if (!MatchesCurrentFields())
            {
                Dispose();
                Build();
            }

            DebugUploadedBytesLastFrame = 0;
            for (int i = 0; i < _panels.Count; i++)
            {
                // 부분 업로드는 지면만 한다. Build() 가 지면을 항상 먼저 넣으므로 0번이 지면이다.
                UploadHeight(_panels[i], i == 0);
            }

            // 아무 라이트나 집으면 안 된다 — 근거는 SnowSunLight 주석.
            if (_sun == null) _sun = SnowSunLight.Resolve();
            if (_sun == null) return;

            Vector4 sun = -_sun.transform.forward;
            for (int i = 0; i < _panels.Count; i++)
            {
                if (_panels[i].Material != null) _panels[i].Material.SetVector(SunDirId, sun);
            }
        }

        /// <summary>
        /// 높이 텍스처를 갱신한다. <paramref name="partial"/> 이 참이면 <b>더러운 사각형만</b>
        /// 올리고, 아니면 통째로 올린다.
        ///
        /// <para><b>지면만 부분 업로드를 한다.</b> 상자는 6,144 셀 = 12 KB 라 사각형을 구하고
        /// 스테이징을 거치는 값이 아끼는 값보다 크다. 대신 <b>깨끗하면 아예 건너뛴다</b> —
        /// 조용한 상자 50개면 그것만으로 프레임당 600 KB 가 사라진다.</para>
        /// </summary>
        private void UploadHeight(Panel panel, bool allowPartial)
        {
            SnowHeightFieldCpu field = panel.Field;
            if (field == null || panel.HeightTex == null) return;

            // 세운 직후에는 텍스처가 빈 상태다. 더러운 사각형만 올리면 나머지가 쓰레기로 남는다.
            if (panel.NeedsFullUpload) { FullUpload(panel, field); return; }

            // 깨끗하면 아무것도 안 한다 - 지면도 상자도 같다.
            if (field.RenderDirtyChunks.Count == 0) return;

            if (allowPartial && _partialUpload && _partialUploadSupported
                && TryUploadDirtyRect(panel, field))
            {
                field.ClearRenderDirty();
                return;
            }

            FullUpload(panel, field);
        }

        private void FullUpload(Panel panel, SnowHeightFieldCpu field)
        {
            panel.HeightTex.SetPixelData(field.HeightMm, 0);
            panel.HeightTex.Apply(false, false);
            DebugUploadedBytesLastFrame += (long)field.HeightMm.Length * 2;
            DebugUploadedBytesTotal += (long)field.HeightMm.Length * 2;
            panel.NeedsFullUpload = false;
            field.ClearRenderDirty();
        }

        /// <summary>
        /// 더러운 청크의 바운딩 사각형만 올린다. 사각형이 없거나 스테이징 상한을 넘으면
        /// <c>false</c> 를 돌려주고, 호출자가 전체 업로드로 되돌아간다.
        /// </summary>
        private bool TryUploadDirtyRect(Panel panel, SnowHeightFieldCpu field)
        {
            IReadOnlyList<int> dirty = field.RenderDirtyChunks;
            if (dirty.Count == 0) return false;

            SnowFieldGeometry geo = field.Geo;
            if (!SnowPanelTiling.TryDirtyCellRect(geo, dirty,
                                                  out int cx0, out int cz0, out int cx1, out int cz1))
            {
                return false;
            }

            int w = cx1 - cx0 + 1;
            int h = cz1 - cz0 + 1;
            int size = SnowPanelTiling.StagingSizeFor(w, h, MaxStagingCells);
            if (size == 0) return false;                       // 너무 크다 - 전체가 싸다

            Texture2D staging = GetStaging(size);
            if (staging == null) return false;

            if (_stagingScratch == null || _stagingScratch.Length < size * size)
                _stagingScratch = new ushort[size * size];

            ushort[] src = field.HeightMm;
            for (int r = 0; r < h; r++)
            {
                System.Array.Copy(src, (cz0 + r) * geo.ResX + cx0, _stagingScratch, r * size, w);
            }

            staging.SetPixelData(_stagingScratch, 0);
            staging.Apply(false, false);
            Graphics.CopyTexture(staging, 0, 0, 0, 0, w, h, panel.HeightTex, 0, 0, cx0, cz0);
            DebugUploadedBytesLastFrame += (long)size * size * 2;
            DebugUploadedBytesTotal += (long)size * size * 2;
            return true;
        }

        private Texture2D GetStaging(int size)
        {
            if (_staging.TryGetValue(size, out Texture2D tex) && tex != null) return tex;

            tex = new Texture2D(size, size, TextureFormat.R16, false, true)
            {
                name = $"SnowUploadStaging{size}",
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp,
            };
            _staging[size] = tex;
            return tex;
        }

        private void OnDisable() => Dispose();

        /// <summary>
        /// 세워 둔 필드 목록이 스테이지의 현재 목록과 같은가. <b>참조 비교</b>다 — 런너가 바뀌면
        /// 스테이지가 격자를 새로 만들고, 그때 낡은 텍스처를 계속 올리면 이전 세션의 눈이 보인다.
        /// </summary>
        private bool MatchesCurrentFields()
        {
            if (_panels.Count == 0) return false;

            int expected = 1 + _stage.Zones.Count;
            if (_builtFor.Count != expected) return false;
            if (!ReferenceEquals(_builtFor[0], _stage.Field)) return false;

            for (int i = 0; i < _stage.Zones.Count; i++)
            {
                SnowZone zone = _stage.Zones[i];
                if (zone == null || !ReferenceEquals(_builtFor[i + 1], zone.Field)) return false;
            }

            return true;
        }

        private void Build()
        {
            Shader shader = Shader.Find("PPack/SnowDisplace");
            if (shader == null)
            {
                Debug.LogError($"{nameof(SnowDisplaceView)}: PPack/SnowDisplace 셰이더가 없다.");
                enabled = false;
                return;
            }

            _builtFor.Clear();

            SnowHeightFieldCpu ground = _stage.Field;
            _builtFor.Add(ground);

            // 지면 패널은 <b>월드 원점에 축 정렬</b>로 둔다. 그러면 정점의 오브젝트 XZ 가 월드 XZ 와
            // 같아지고, 필드 좌표와도 같아진다(메시를 필드 좌표로 만들기 때문에).
            // 지면의 눈 사각형은 필드 전체다. 상자와 달리 저작된 사각형이 따로 없다.
            _panels.Add(BuildPanel(shader, ground, transform, Vector3.zero, Quaternion.identity,
                                   "SnowDisplacePanel"));

            for (int i = 0; i < _stage.Zones.Count; i++)
            {
                SnowZone zone = _stage.Zones[i];
                if (zone == null || zone.Field == null) continue;

                Vector3 scale = zone.transform.lossyScale;
                if (Mathf.Abs(scale.x - 1f) > 1e-3f || Mathf.Abs(scale.y - 1f) > 1e-3f
                                                    || Mathf.Abs(scale.z - 1f) > 1e-3f)
                {
                    Debug.LogWarning($"{nameof(SnowZone)} '{zone.name}': 스케일이 1 이 아니다{scale} — " +
                                     "격자와 그림이 그만큼 늘어난다. 크기는 스케일이 아니라 " +
                                     $"{nameof(SnowZone)} 의 size 로 준다.");
                }

                _builtFor.Add(zone.Field);

                // 상자의 눈 사각형은 <b>저작된 크기</b>다 — 청크 배수로 올림된 격자가 아니라.
                // 그래야 마감이 인스펙터의 상자 가장자리에서 정확히 일어난다.
                _panels.Add(BuildPanel(shader, zone.Field, zone.transform, Vector3.zero,
                                       Quaternion.identity, $"SnowZonePanel_{zone.name}",
                                       zone.SizeXZ));
            }
        }

        /// <summary>
        /// 필드 하나를 그리는 패널을 만든다. <paramref name="parent"/> 아래 로컬 포즈로 붙으므로
        /// 상자의 회전이 그대로 적용된다.
        /// </summary>
        private Panel BuildPanel(Shader shader, SnowHeightFieldCpu field, Transform parent,
                                 Vector3 localPos, Quaternion localRot, string name,
                                 Vector2 snowSizeXZ = default)
        {
            SnowFieldGeometry geo = field.Geo;
            SnowGroundFieldCpu ground = field.Ground;

            var panel = new Panel { Field = field };

            panel.HeightTex = new Texture2D(geo.ResX, geo.ResZ, TextureFormat.R16, false, true)
            {
                name = $"{name}_Height",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            float w = geo.ResX * SnowFieldGeometry.CellSizeM;
            float d = geo.ResZ * SnowFieldGeometry.CellSizeM;

            panel.Material = new Material(shader) { name = $"M_{name}" };
            panel.Material.SetTexture(HeightTexId, panel.HeightTex);
            panel.Material.SetVector(PatchMinId, new Vector4(geo.OriginXM, geo.OriginZM, 0, 0));
            panel.Material.SetVector(InvPatchId, new Vector4(1f / w, 1f / d, 0, 0));
            panel.Material.SetFloat(CellSizeId, SnowFieldGeometry.CellSizeM);
            panel.Material.SetFloat(EdgeProfileId, _edgeProfile);


            // <b>바닥과 마스크는 한 번만 올린다.</b> 구운 데이터라 프레임마다 바뀔 것이 없고,
            // 바닥이 없는 필드에는 1x1(바닥 0 · 마스크 1)을 물려 준다 - 셰이더에 분기를 만들지
            // 않으려는 것이고, clamp 샘플이라 1x1 은 상수와 같다.
            BuildGroundTextures(panel, geo, ground, name);

            float lowY = geo.OriginYM + (ground == null ? 0 : ground.MinFloorMm) * 0.001f;
            float highY = geo.OriginYM + (ground == null ? 0 : ground.MaxFloorMm) * 0.001f;

            // <b>메시는 눈이 있을 수 있는 범위만 덮는다.</b> 상자의 격자는 청크 배수로 올림돼 상자보다
            // 최대 1 m 넓은데, 그 여유 칸까지 메시를 깔면 커버리지 0 인 자리에도 <b>삼각형이 남는다</b>.
            // 픽셀 clip 은 깊이로 자르므로 마지막 정점(깊이 0)과 그 안쪽 정점 사이 한 칸은 살아남고,
            // 그것이 램프 모서리 밖으로 <b>얇은 처마</b>가 되어 아래에서 보였다. 지면 시트는 필드가 곧
            // 범위라 예전과 같다.
            float meshW = snowSizeXZ.x > 0f ? snowSizeXZ.x : w;
            float meshD = snowSizeXZ.y > 0f ? snowSizeXZ.y : d;
            float meshMinX = snowSizeXZ.x > 0f ? -meshW * 0.5f : geo.OriginXM;
            float meshMinZ = snowSizeXZ.y > 0f ? -meshD * 0.5f : geo.OriginZM;
            panel.Root = new GameObject(name);
            panel.Root.transform.SetParent(parent, false);
            panel.Root.transform.localPosition = localPos;
            panel.Root.transform.localRotation = localRot;
            panel.Root.transform.localScale = Vector3.one;

            BuildTiles(panel, geo, ground, meshMinX, meshMinZ, meshW, meshD, name);
            return panel;
        }

        private void BuildGroundTextures(Panel panel, SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                         string name)
        {
            int rx = ground == null ? 1 : geo.ResX;
            int rz = ground == null ? 1 : geo.ResZ;

            panel.FloorTex = new Texture2D(rx, rz, TextureFormat.R16, false, true)
            {
                name = $"{name}_Floor",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };
            panel.MaskTex = new Texture2D(rx, rz, TextureFormat.R8, false, true)
            {
                name = $"{name}_Snowable",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
            };

            if (ground == null)
            {
                panel.FloorTex.SetPixelData(new ushort[] { 0 }, 0);
                panel.MaskTex.SetPixelData(new[] { SnowGroundFieldCpu.SnowableValue }, 0);
            }
            else
            {
                panel.FloorTex.SetPixelData(ground.FloorMm, 0);
                panel.MaskTex.SetPixelData(ground.Coverage, 0);
            }

            panel.FloorTex.Apply(false, false);
            panel.MaskTex.Apply(false, false);

            panel.Material.SetTexture(FloorTexId, panel.FloorTex);
            panel.Material.SetTexture(MaskTexId, panel.MaskTex);
            panel.Material.SetFloat(FloorOriginYId, ground == null ? 0f : geo.OriginYM);
        }

        /// <summary>
        /// 패널을 덮는 타일들을 만든다. <b>정점 격자는 전역 하나</b>이고 타일은 그 인덱스 구간만
        /// 가져간다 — 그래서 이웃과 공유하는 모서리 정점이 비트 단위로 같고 실금이 안 간다.
        /// </summary>
        private void BuildTiles(Panel panel, SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                float minX, float minZ, float w, float d, string name)
        {
            int nx = SnowPanelTiling.LatticeCount(w, _vertexSpacingM);
            int nz = SnowPanelTiling.LatticeCount(d, _vertexSpacingM);

            // 0 이면 안 쪼갠다 - 예전 동작으로 돌아가는 탈출구다.
            float tileM = _tileSizeM > 0f ? _tileSizeM : float.MaxValue;
            int quadsX = _tileSizeM > 0f ? SnowPanelTiling.QuadsPerTile(tileM, _vertexSpacingM) : nx - 1;
            int quadsZ = _tileSizeM > 0f ? SnowPanelTiling.QuadsPerTile(tileM, _vertexSpacingM) : nz - 1;

            int tilesX = SnowPanelTiling.TileCountOnAxis(nx, quadsX);
            int tilesZ = SnowPanelTiling.TileCountOnAxis(nz, quadsZ);

            panel.TileObjects = new GameObject[tilesX * tilesZ];
            panel.TileMeshes = new Mesh[tilesX * tilesZ];

            for (int tz = 0; tz < tilesZ; tz++)
            {
                for (int tx = 0; tx < tilesX; tx++)
                {
                    SnowPanelTiling.TileVertexRange(nx, quadsX, tx, out int x0, out int x1);
                    SnowPanelTiling.TileVertexRange(nz, quadsZ, tz, out int z0, out int z1);

                    int index = tz * tilesX + tx;
                    Mesh mesh = BuildTileMesh(geo, ground, minX, minZ, w, d, nx, nz,
                                              x0, x1, z0, z1, $"{name}_Tile{tx}x{tz}");
                    panel.TileMeshes[index] = mesh;

                    var go = new GameObject($"{name}_Tile{tx}x{tz}");
                    go.transform.SetParent(panel.Root.transform, false);
                    go.transform.localPosition = Vector3.zero;
                    go.transform.localRotation = Quaternion.identity;
                    go.transform.localScale = Vector3.one;
                    go.AddComponent<MeshFilter>().sharedMesh = mesh;

                    var mr = go.AddComponent<MeshRenderer>();
                    mr.sharedMaterial = panel.Material;
                    mr.shadowCastingMode = ShadowCastingMode.On;
                    mr.receiveShadows = true;
                    mr.lightProbeUsage = LightProbeUsage.Off;
                    mr.reflectionProbeUsage = ReflectionProbeUsage.Off;
                    panel.TileObjects[index] = go;
                }
            }
        }

        /// <summary>
        /// 전역 격자의 <c>[x0..x1] × [z0..z1]</c> 구간만 덮는 메시. 정점 좌표는 <b>전역 인덱스</b>로
        /// 계산한다(<see cref="SnowPanelTiling.LatticePos"/>) — 타일 로컬로 계산하면 공유 모서리가
        /// 갈려 실금이 간다.
        ///
        /// <para><b>바운즈를 손으로 넉넉히 준다.</b> 정점이 CPU 에서 움직이지 않으므로 유니티가 계산한
        /// 바운즈는 두께 0 인 판이고, 그러면 카메라가 눈 위를 볼 때 컬링이 판 전체를 잘라 <b>눈이
        /// 통째로 사라진다</b>. 정점 변위 경로의 고전적인 실수다. 바닥 범위는 <b>이 타일이 덮는
        /// 셀만</b> 훑어서 구한다 — 필드 전체를 쓰면 타일마다 바운즈가 같아져 컬링이 안 든다.</para>
        /// </summary>
        private Mesh BuildTileMesh(SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                   float minX, float minZ, float w, float d, int nx, int nz,
                                   int x0, int x1, int z0, int z1, string name)
        {
            int vx = x1 - x0 + 1;
            int vz = z1 - z0 + 1;

            var verts = new Vector3[vx * vz];
            for (int z = 0; z < vz; z++)
            {
                float wz = SnowPanelTiling.LatticePos(minZ, d, nz, z0 + z);
                for (int x = 0; x < vx; x++)
                {
                    float wx = SnowPanelTiling.LatticePos(minX, w, nx, x0 + x);
                    verts[z * vx + x] = new Vector3(wx, 0f, wz);
                }
            }

            var tris = new int[(vx - 1) * (vz - 1) * 6];
            int t = 0;
            for (int z = 0; z < vz - 1; z++)
            {
                for (int x = 0; x < vx - 1; x++)
                {
                    int i0 = z * vx + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + vx;
                    int i3 = i2 + 1;

                    tris[t++] = i0; tris[t++] = i2; tris[t++] = i1;
                    tris[t++] = i1; tris[t++] = i2; tris[t++] = i3;
                }
            }

            var mesh = new Mesh
            {
                name = $"{name}_Grid{vx}x{vz}",
                indexFormat = verts.Length > 65000
                    ? UnityEngine.Rendering.IndexFormat.UInt32
                    : UnityEngine.Rendering.IndexFormat.UInt16,
            };
            mesh.SetVertices(verts);
            mesh.SetTriangles(tris, 0, false);

            float tileMinX = verts[0].x;
            float tileMinZ = verts[0].z;
            float tileMaxX = verts[verts.Length - 1].x;
            float tileMaxZ = verts[verts.Length - 1].z;

            TileFloorRange(geo, ground, tileMinX, tileMinZ, tileMaxX, tileMaxZ,
                           out float lowY, out float highY);

            float minY = lowY - 4f;
            float maxY = highY + 12f;
            mesh.bounds = new Bounds(
                new Vector3((tileMinX + tileMaxX) * 0.5f, (minY + maxY) * 0.5f,
                            (tileMinZ + tileMaxZ) * 0.5f),
                new Vector3(tileMaxX - tileMinX, maxY - minY, tileMaxZ - tileMinZ));
            mesh.UploadMeshData(true);
            return mesh;
        }

        /// <summary>이 사각형이 덮는 셀의 바닥 최소·최대(월드 Y). 바닥이 없으면 둘 다 0 이다.</summary>
        private static void TileFloorRange(SnowFieldGeometry geo, SnowGroundFieldCpu ground,
                                           float minX, float minZ, float maxX, float maxZ,
                                           out float lowY, out float highY)
        {
            lowY = geo.OriginYM;
            highY = geo.OriginYM;
            if (ground == null) return;

            if (!geo.TryWorldRectToCellRect(minX, minZ, maxX, maxZ,
                                            out int cx0, out int cz0, out int cx1, out int cz1))
            {
                return;
            }

            int lo = int.MaxValue;
            int hi = int.MinValue;
            for (int cz = cz0; cz <= cz1; cz++)
            {
                int row = cz * geo.ResX;
                for (int cx = cx0; cx <= cx1; cx++)
                {
                    int v = ground.FloorMm[row + cx];
                    if (v < lo) lo = v;
                    if (v > hi) hi = v;
                }
            }

            if (lo > hi) return;
            lowY = geo.OriginYM + lo * 0.001f;
            highY = geo.OriginYM + hi * 0.001f;
        }

        private void Dispose()
        {
            for (int i = 0; i < _panels.Count; i++)
            {
                Panel panel = _panels[i];
                if (panel.TileObjects != null)
                    for (int t = 0; t < panel.TileObjects.Length; t++)
                        if (panel.TileObjects[t] != null) Destroy(panel.TileObjects[t]);
                if (panel.TileMeshes != null)
                    for (int t = 0; t < panel.TileMeshes.Length; t++)
                        if (panel.TileMeshes[t] != null) Destroy(panel.TileMeshes[t]);
                if (panel.Root != null) Destroy(panel.Root);
                if (panel.Material != null) Destroy(panel.Material);
                if (panel.HeightTex != null) Destroy(panel.HeightTex);
                if (panel.FloorTex != null) Destroy(panel.FloorTex);
                if (panel.MaskTex != null) Destroy(panel.MaskTex);
            }

            foreach (KeyValuePair<int, Texture2D> kv in _staging)
                if (kv.Value != null) Destroy(kv.Value);
            _staging.Clear();
            _stagingScratch = null;

            _panels.Clear();
            _builtFor.Clear();
        }
    }
}
