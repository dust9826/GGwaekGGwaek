using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 게이트 판정용 <b>절상(하드코딩) 높이 필드</b>. <c>SnowField</c> 도 차량도 없이
    /// "밴드 B 가 격자를 숨기는가"만 보기 위한 도구다(스펙 §4 · 합격 기준 4).
    ///
    /// 세 가지를 한 화면에 만든다:
    /// <list type="bullet">
    /// <item>격자축과 나란한 직선 자국 — 계단이 가장 잘 보이는 방향</item>
    /// <item>원호 자국 — 축과 어긋난 경계</item>
    /// <item>부분만 밀린 띠 — 4단계 양자화가 계단으로 읽히는지</item>
    /// </list>
    ///
    /// 이것은 <b>버리는 도구가 아니라 회귀 도구</b>다. 필드가 들어온 뒤에도 셰이더만
    /// 따로 보고 싶을 때 쓴다. 씬에서 꺼두고 필요할 때 켠다.
    /// </summary>
    public sealed class SnowGateField : MonoBehaviour
    {
        [Header("필드 범위 (월드 XZ)")]
        [SerializeField] private Vector2 _originXZ = new Vector2(-8f, -8f);
        [SerializeField] private Vector2 _sizeMeters = new Vector2(16f, 16f);
        [Tooltip("권위 셀 크기(m). 스펙 §5 의 12.5cm.")]
        [SerializeField, Min(0.01f)] private float _cellSize = 0.125f;

        [Header("패턴")]
        [Tooltip("깊이 단계 수. 셰이더는 이 값을 모른다 — 저장·전송의 양자화일 뿐이다.")]
        [SerializeField, Min(2)] private int _levels = 4;
        [SerializeField] private float _straightHalfWidth = 0.9f;
        [SerializeField] private float _arcRadius = 5.5f;
        [SerializeField] private float _arcHalfWidth = 0.9f;

        private static readonly int FieldId = Shader.PropertyToID("_SnowField");
        private static readonly int OriginId = Shader.PropertyToID("_SnowFieldOrigin");
        private static readonly int InvSizeId = Shader.PropertyToID("_SnowFieldInvSize");
        private static readonly int TexelSizeId = Shader.PropertyToID("_SnowFieldTexelSize");
        private static readonly int CellSizeId = Shader.PropertyToID("_SnowFieldCellSize");

        private Texture2D _field;

        private void OnEnable() => Apply();

        [ContextMenu("Apply Gate Field")]
        public void Apply()
        {
            int w = Mathf.Max(4, Mathf.RoundToInt(_sizeMeters.x / _cellSize));
            int h = Mathf.Max(4, Mathf.RoundToInt(_sizeMeters.y / _cellSize));

            if (_field == null || _field.width != w || _field.height != h)
            {
                if (_field != null) DestroyImmediate(_field);
                // 밉이 필요하다 — 변위가 저역 통과 단계를 읽는다(나이퀴스트, 스펙 §5).
                _field = new Texture2D(w, h, TextureFormat.RG16, mipChain: true, linear: true)
                {
                    name = "SnowGateField",
                    wrapMode = TextureWrapMode.Clamp,
                    filterMode = FilterMode.Bilinear,
                    hideFlags = HideFlags.DontSave,
                };
            }

            var pixels = new Color32[w * h];
            float step = 1f / (_levels - 1);

            for (int y = 0; y < h; y++)
            {
                for (int x = 0; x < w; x++)
                {
                    // 셀 중심의 월드 좌표
                    float wx = _originXZ.x + (x + 0.5f) * _cellSize;
                    float wz = _originXZ.y + (y + 0.5f) * _cellSize;

                    float level = _levels - 1;   // 기본은 눈 가득
                    float fresh = 0f;

                    // 격자축과 나란한 직선 자국 — 완전히 밀림
                    if (Mathf.Abs(wx) <= _straightHalfWidth)
                    {
                        level = 0f;
                        fresh = 1f;
                    }

                    // 원호 자국 — 축과 어긋난 경계
                    float r = Mathf.Sqrt(wx * wx + wz * wz);
                    if (Mathf.Abs(r - _arcRadius) <= _arcHalfWidth && wx > _straightHalfWidth)
                    {
                        level = 0f;
                        fresh = 0.55f;
                    }

                    // 부분만 밀린 띠 — 한 단계씩 남긴다
                    if (wz > 2f && wz < 5f && wx < -_straightHalfWidth)
                    {
                        level = Mathf.Floor(Mathf.InverseLerp(2f, 5f, wz) * (_levels - 1));
                        fresh = 0.3f;
                    }

                    byte depth01 = (byte)Mathf.RoundToInt(Mathf.Clamp01(level * step) * 255f);
                    pixels[y * w + x] = new Color32(depth01, (byte)Mathf.RoundToInt(fresh * 255f), 0, 255);
                }
            }

            _field.SetPixels32(pixels);
            _field.Apply(updateMipmaps: true);

            Shader.SetGlobalTexture(FieldId, _field);
            Shader.SetGlobalVector(OriginId, new Vector4(_originXZ.x, _originXZ.y, 0f, 0f));
            Shader.SetGlobalVector(InvSizeId, new Vector4(1f / _sizeMeters.x, 1f / _sizeMeters.y, 0f, 0f));
            Shader.SetGlobalVector(TexelSizeId, new Vector4(1f / w, 1f / h, w, h));
            Shader.SetGlobalFloat(CellSizeId, _cellSize);
        }

        private void OnDisable()
        {
            if (_field != null) DestroyImmediate(_field);
            _field = null;
        }
    }
}
