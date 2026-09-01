using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 세 층 중 <b>가운데 층</b>: 권위 필드를 텍스처 한 장으로 옮긴다.
    ///
    /// <c>ushort[]</c> 가 R16 UNorm 텍스처와 같은 바이트라서 <c>SetPixelData</c> 가 memcpy 다.
    /// 변환 패스가 없다는 것이 uint16 을 int32 대신 고른 이유였다.
    ///
    /// 나중에 진짜 렌더러(레이마처든 무엇이든)를 붙이는 것은 <b>이 층 위쪽만 갈아끼우는 일</b>이고,
    /// 권위 필드는 건드리지 않는다. 그래서 이 클래스는 필드를 읽기만 하고, 필드는 이 클래스의
    /// 존재를 모른다 — 데디서버에는 이 파일이 아예 로드되지 않는다.
    /// </summary>
    public sealed class SnowHeightMapRenderer
    {
        private static readonly int RampMaxId = Shader.PropertyToID("_RampMaxM");

        private readonly SnowHeightFieldCpu _field;
        private readonly Texture2D _tex;
        private readonly Material _mat;
        private readonly GameObject _quad;

        public Texture2D Texture => _tex;
        public GameObject Quad => _quad;
        public Material Material => _mat;

        public SnowHeightMapRenderer(SnowHeightFieldCpu field, float rampMaxM, Transform parent = null)
        {
            _field = field;
            var geo = field.Geo;

            _tex = new Texture2D(geo.ResX, geo.ResZ, TextureFormat.R16, mipChain: false, linear: true)
            {
                name = "SnowHeightMm",
                // 셀 경계가 보여야 시뮬을 눈으로 읽을 수 있다. 보간하면 계단이 사라지는 대신
                // 무엇이 한 셀이고 무엇이 형상인지 구별할 수 없게 된다.
                filterMode = FilterMode.Point,
                wrapMode = TextureWrapMode.Clamp
            };

            var shader = Shader.Find("PPack/SnowHeightRamp");
            if (shader == null) Debug.LogError("[SnowCpu] PPack/SnowHeightRamp 셰이더를 못 찾았다");
            _mat = new Material(shader) { name = "SnowHeightRamp" };
            _mat.mainTexture = _tex;
            _mat.SetFloat(RampMaxId, rampMaxM);

            _quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            _quad.name = "SnowHeightMapQuad";
            Object.Destroy(_quad.GetComponent<Collider>());
            if (parent != null) _quad.transform.SetParent(parent, false);

            float w = geo.ResX * SnowFieldGeometry.CellSizeM;
            float d = geo.ResZ * SnowFieldGeometry.CellSizeM;
            _quad.transform.SetPositionAndRotation(
                new Vector3(geo.OriginXM + w * 0.5f, 0f, geo.OriginZM + d * 0.5f),
                Quaternion.Euler(90f, 0f, 0f));
            _quad.transform.localScale = new Vector3(w, d, 1f);

            var mr = _quad.GetComponent<MeshRenderer>();
            mr.sharedMaterial = _mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            mr.lightProbeUsage = LightProbeUsage.Off;
            mr.reflectionProbeUsage = ReflectionProbeUsage.Off;

            Upload();
        }

        /// <summary>필드 전체를 올린다. 변환도 복사도 없이 배열 그대로다.</summary>
        public void Upload()
        {
            _tex.SetPixelData(_field.HeightMm, 0);
            _tex.Apply(updateMipmaps: false, makeNoLongerReadable: false);
        }

        public void SetRampMax(float metres) => _mat.SetFloat(RampMaxId, Mathf.Max(metres, 1e-4f));

        public void Dispose()
        {
            if (_quad != null) Object.Destroy(_quad);
            if (_mat != null) Object.Destroy(_mat);
            if (_tex != null) Object.Destroy(_tex);
        }
    }
}
