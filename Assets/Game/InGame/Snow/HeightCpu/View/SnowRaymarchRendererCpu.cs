using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 세 층 중 <b>③ 렌더</b>. CPU 권위 필드를 3D 로 그린다.
    ///
    /// 이 클래스가 존재한다는 사실 자체가 설계의 요점이다 — 권위 필드도, 높이 텍스처도 한 줄
    /// 바뀌지 않았다. 탑다운 램프와 이 레이마처는 <b>같은 텍스처를 읽는 두 개의 ③</b> 이고,
    /// 서로를 모른다. 나중에 무엇으로 바꾸든 네트워크 코드도 시뮬도 안 바뀐다.
    ///
    /// 층 ② 가 하나 늘었다: <see cref="SnowCoarseMaxCpu"/> 의 상한 텍스처. 이것도 권위 필드에서
    /// 파생되는 표현 데이터라 서버에는 존재하지 않는다.
    /// </summary>
    public sealed class SnowRaymarchRendererCpu
    {
        private static readonly int HeightTexId = Shader.PropertyToID("_HeightTex");
        private static readonly int CoarseTexId = Shader.PropertyToID("_CoarseMaxTex");
        private static readonly int PatchMinId = Shader.PropertyToID("_PatchMin");
        private static readonly int InvPatchId = Shader.PropertyToID("_InvPatchSize");
        private static readonly int BoxMinId = Shader.PropertyToID("_BoxMin");
        private static readonly int BoxMaxId = Shader.PropertyToID("_BoxMax");
        private static readonly int GroundYId = Shader.PropertyToID("_GroundY");
        private static readonly int MarchTopId = Shader.PropertyToID("_MarchTopY");
        private static readonly int MarchFloorId = Shader.PropertyToID("_MarchFloorY");
        private static readonly int MinSnowId = Shader.PropertyToID("_MinSnowHeightM");
        private static readonly int SafeRadiusId = Shader.PropertyToID("_CoarseSafeRadiusM");
        private static readonly int BiasId = Shader.PropertyToID("_CoarseMaxBiasM");
        private static readonly int MaxStepsId = Shader.PropertyToID("_MaxSteps");
        private static readonly int StepMId = Shader.PropertyToID("_StepM");
        private static readonly int RefineId = Shader.PropertyToID("_RefineSteps");
        private static readonly int SunDirId = Shader.PropertyToID("_SunDir");
        private static readonly int AoId = Shader.PropertyToID("_AoStrength");
        private static readonly int ShadowId = Shader.PropertyToID("_ShadowStrength");
        private static readonly int DebugId = Shader.PropertyToID("_DebugMode");
        private static readonly int LumpTexId = Shader.PropertyToID("_LumpTex");
        private static readonly int LumpRadiusId = Shader.PropertyToID("_LumpRadiusM");
        private static readonly int LumpAmountId = Shader.PropertyToID("_LumpAmount");
        private static readonly int FilletTexId = Shader.PropertyToID("_FilletTex");
        private static readonly int FilletRangeId = Shader.PropertyToID("_FilletRangeM");
        private static readonly int FilletAmountId = Shader.PropertyToID("_FilletAmount");
        private static readonly int CellSizeId = Shader.PropertyToID("_CellSizeM");

        private readonly SnowHeightFieldCpu _field;
        private readonly SnowCoarseMaxCpu _coarse;
        private readonly SnowSurfaceBakeCpu _lump;
        private readonly Texture2D _lumpTex;
        private readonly Texture2D _filletTex;
        private readonly Texture2D _heightTex;
        private readonly Texture2D _coarseTex;
        private readonly Material _mat;
        private readonly GameObject _box;

        public GameObject Box => _box;
        public Material Material => _mat;

        /// <summary>필드가 담을 수 있는 최고 높이 위로 잡는 마칭 천장. 상한을 깨지 않는 여유.</summary>
        public float MarchCeilingM { get; private set; } = 4f;

        /// <summary>로브 세기. 0 이면 각진 슬래브가 그대로 보인다 - 비교용으로 남겨둔다.</summary>
        public float LumpAmount { get; set; } = 1f;

        /// <summary>둥근 어깨 세기. 0 이면 권위 필드의 날카로운 능선이 그대로 보인다 - 비교용.</summary>
        public float FilletAmount { get; set; } = 1f;

        public SnowRaymarchRendererCpu(SnowHeightFieldCpu field, SnowCoarseMaxCpu coarse,
                                      SnowSurfaceBakeCpu lump, Transform parent)
        {
            _field = field;
            _coarse = coarse;
            _lump = lump;
            var geo = field.Geo;

            _lumpTex = new Texture2D(lump.ResX, lump.ResZ, TextureFormat.R8, false, true)
            {
                name = "SnowLumpLift",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            _filletTex = new Texture2D(lump.ResX, lump.ResZ, TextureFormat.R8, false, true)
            {
                name = "SnowFillet",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            _heightTex = new Texture2D(geo.ResX, geo.ResZ, TextureFormat.R16, false, true)
            {
                name = "SnowHeightMm_March",
                filterMode = FilterMode.Bilinear,      // 실제 높이라 보간해도 안전하다
                wrapMode = TextureWrapMode.Clamp
            };
            _coarseTex = new Texture2D(coarse.ResX, coarse.ResZ, TextureFormat.R16, false, true)
            {
                name = "SnowCoarseMaxMm",
                // POINT 여야 한다. 바이리니어는 다일레이트된 최대값을 도로 아래로 보간해서
                // 상한을 깬다 - 그러면 표면에 구멍이 뚫린다.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var shader = Shader.Find("PPack/SnowRaymarchCpu");
            if (shader == null) Debug.LogError("[SnowCpu] PPack/SnowRaymarchCpu 셰이더를 못 찾았다");
            _mat = new Material(shader) { name = "SnowRaymarchCpu" };

            float w = geo.ResX * SnowFieldGeometry.CellSizeM;
            float d = geo.ResZ * SnowFieldGeometry.CellSizeM;

            _box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            _box.name = "SnowMarchProxyBox";
            Object.Destroy(_box.GetComponent<Collider>());
            _box.transform.SetParent(parent, false);
            _box.transform.position = new Vector3(geo.OriginXM + w * 0.5f, MarchCeilingM * 0.5f,
                                                  geo.OriginZM + d * 0.5f);
            _box.transform.localScale = new Vector3(w, MarchCeilingM, d);

            var mr = _box.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;

            PushStaticUniforms();
            UploadAll();
        }

        private void PushStaticUniforms()
        {
            var geo = _field.Geo;
            float w = geo.ResX * SnowFieldGeometry.CellSizeM;
            float d = geo.ResZ * SnowFieldGeometry.CellSizeM;

            _mat.SetTexture(HeightTexId, _heightTex);
            _mat.SetTexture(CoarseTexId, _coarseTex);
            _mat.SetTexture(LumpTexId, _lumpTex);
            _mat.SetFloat(LumpRadiusId, _lump.RadiusM);
            _mat.SetFloat(LumpAmountId, LumpAmount);
            _mat.SetTexture(FilletTexId, _filletTex);
            _mat.SetFloat(FilletRangeId, _lump.FilletRangeM);
            _mat.SetFloat(FilletAmountId, FilletAmount);
            _mat.SetVector(PatchMinId, new Vector4(geo.OriginXM, geo.OriginZM, 0, 0));
            _mat.SetVector(InvPatchId, new Vector4(1f / w, 1f / d, 0, 0));
            _mat.SetVector(BoxMinId, new Vector4(geo.OriginXM, 0f, geo.OriginZM, 0));
            _mat.SetVector(BoxMaxId, new Vector4(geo.OriginXM + w, MarchCeilingM, geo.OriginZM + d, 0));
            _mat.SetFloat(GroundYId, 0f);
            _mat.SetFloat(MarchTopId, MarchCeilingM);
            _mat.SetFloat(MarchFloorId, -0.75f);   // 표면은 항상 지면 이상이라 여유를 둬도 안전하다
            // R16 권위 높이의 최소 단위는 1 mm 다. 0 셀의 보간값만 버리고 실제 1 mm 눈은 남긴다.
            _mat.SetFloat(MinSnowId, 0.0005f);
            _mat.SetFloat(CellSizeId, SnowFieldGeometry.CellSizeM);
            _mat.SetFloat(SafeRadiusId, _coarse.SafeRadiusM);

            // 상한은 0 이다. 로브의 들림은 coarse-max 자체가 블록마다 실제 값으로 담고 있으므로
            // 여기서 다시 더하면 이중 계상이고, 긁힌 바닥에서 상한이 표면 위로 떠서 스치는 광선이
            // 스텝 예산을 소진한다. 무엇이든 coarse-max 에 반영되지 않는 것을 표면에 더하면
            // 그때만 여기에 넣는다.
            _mat.SetFloat(BiasId, 0f);

            _mat.SetFloat(MaxStepsId, 160f);
            _mat.SetFloat(StepMId, 0.08f);
            _mat.SetFloat(Shader.PropertyToID("_StepGrowPerM"), 0.02f);
            _mat.SetFloat(RefineId, 5f);
            _mat.SetVector(SunDirId, new Vector3(0.52f, 0.46f, 0.40f).normalized);   // 낮은 해. 긴 그림자가 형상을 읽어준다
            _mat.SetFloat(AoId, 0.8f);
            _mat.SetFloat(ShadowId, 0.75f);
        }

        public void SetSun(Vector3 dir) => _mat.SetVector(SunDirId, dir.normalized);

        /// <summary>0 끄기 · 1 커버리지(초록 적중 / 빨강 마칭실패) · 2 스텝 히트맵.</summary>
        public void SetDebug(int mode) => _mat.SetFloat(DebugId, mode);

        /// <summary>로브 세기를 바꾼다. <b>상한도 같이 움직여야 한다</b> - 안 그러면 구멍이 뚫린다.</summary>
        public void SetLump(float amount)
        {
            LumpAmount = amount;
            _mat.SetFloat(LumpAmountId, amount);
        }

        public void SetFillet(float amount)
        {
            FilletAmount = amount;
            _mat.SetFloat(FilletAmountId, amount);
        }

        public void SetActive(bool on) { if (_box != null) _box.SetActive(on); }

        /// <summary>필드와 상한을 통째로 올린다. 둘 다 배열 그대로라 변환이 없다.</summary>
        public void UploadAll()
        {
            _heightTex.SetPixelData(_field.HeightMm, 0);
            _heightTex.Apply(false, false);
            _coarseTex.SetPixelData(_coarse.MaxMm, 0);
            _coarseTex.Apply(false, false);
            _lumpTex.SetPixelData(_lump.Lift, 0);
            _lumpTex.Apply(false, false);
            _filletTex.SetPixelData(_lump.Fillet, 0);
            _filletTex.Apply(false, false);
            FitCeiling();
        }

        /// <summary>
        /// 프록시 박스 천장을 필드의 실제 최고점에 맞춘다. 고정값으로 두면 그보다 높이 자란 더미가
        /// 박스에 잘려서 평평한 뚜껑으로 그려진다 - 마칭이 아니라 상자가 만든 인공물이다.
        /// </summary>
        private void FitCeiling()
        {
            int peak = 0;
            var m = _coarse.MaxMm;
            for (int i = 0; i < m.Length; i++) if (m[i] > peak) peak = m[i];

            float want = peak * 1e-3f + 0.6f;   // peak 에 이미 lift 가 들어 있다
            if (want < 2f) want = 2f;
            if (want <= MarchCeilingM && want > MarchCeilingM - 1.2f) return;   // 히스테리시스

            MarchCeilingM = want;
            var geo = _field.Geo;
            float w = geo.ResX * SnowFieldGeometry.CellSizeM;
            float d = geo.ResZ * SnowFieldGeometry.CellSizeM;
            _box.transform.position = new Vector3(geo.OriginXM + w * 0.5f, MarchCeilingM * 0.5f,
                                                  geo.OriginZM + d * 0.5f);
            _box.transform.localScale = new Vector3(w, MarchCeilingM, d);
            _mat.SetVector(BoxMaxId, new Vector4(geo.OriginXM + w, MarchCeilingM, geo.OriginZM + d, 0));
            _mat.SetFloat(MarchTopId, MarchCeilingM);
        }

        public void Dispose()
        {
            if (_box != null) Object.Destroy(_box);
            if (_mat != null) Object.Destroy(_mat);
            if (_heightTex != null) Object.Destroy(_heightTex);
            if (_coarseTex != null) Object.Destroy(_coarseTex);
            if (_lumpTex != null) Object.Destroy(_lumpTex);
            if (_filletTex != null) Object.Destroy(_filletTex);
        }
    }
}

namespace PPack
{
    public enum SnowViewMode
    {
        /// <summary>높이를 색으로만. 시뮬을 눈으로 읽고 검증하는 뷰다.</summary>
        TopDownRamp = 0,

        /// <summary>프록시 볼륨 레이마칭. 실제 룩이 어떻게 나오는지 보는 뷰다.</summary>
        Raymarch3D = 1
    }
}
