using UnityEngine;

namespace PPack
{
    /// <summary>하늘에 뜨는 달 원반. 달빛 라이트가 가리키는 방향에 카메라를 향한 사각형을 띄운다.
    ///
    /// <para><b>왜 스카이박스에 맡기지 않는가.</b> 절차적 스카이박스는 원반을 하나만, 그것도
    /// <see cref="RenderSettings.sun"/>의 방향으로만 그린다. 밤에 그 자리를 달에게 넘겨 봤더니
    /// 하늘 밝기가 주인의 방향으로 다시 계산되면서 <b>밤인데 화면이 도로 밝아졌다</b>(측정: +16.4%).
    /// 해를 주인으로 고정하면 −3.9%/−5.6%로 단조 감소한다. 그래서 하늘 밝기는 해에게 맡기고,
    /// 달은 여기서 직접 그린다. 덤으로 원반 크기·색을 달에 맞게 따로 줄 수 있다.</para>
    ///
    /// <para>가산 합성이라 사각형의 검은 구석은 보이지 않는다. <see cref="SkyDome"/>과 같은 셰이더를
    /// 쓰므로 안개도 먹지 않는다.</para></summary>
    [DisallowMultipleComponent]
    public sealed class MoonDisk : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Tooltip("달 텍스처. 비우면 부드러운 원반이 절차적으로 만들어진다.")]
        [SerializeField] private Texture _texture;

        [Tooltip("카메라에서 달까지의 거리(m). 하늘 레이어보다 안쪽이어야 별에 가리지 않는다.")]
        [SerializeField, Min(10f)] private float _distance = 780f;

        [Tooltip("원반 지름(m). 거리에 비례해 화면 크기가 정해진다.")]
        [SerializeField, Min(1f)] private float _size = 62f;

        [SerializeField] private Color _tint = new Color(0.92f, 0.95f, 1f);

        private Transform _followTarget;
        private MeshRenderer _renderer;
        private Material _material;
        private Vector3 _direction = Vector3.up;
        private int _warmFrames = 3;

        /// <summary>달빛이 오는 방향(라이트의 forward의 반대)과 세기를 받는다. 세기가 0이면 꺼진다.</summary>
        public void SetMoon(Vector3 lightForward, float intensity01)
        {
            _direction = -lightForward.normalized;
            if (_renderer == null || _material == null) return;

            // 지평선을 넘는 순간 원반이 톡 사라지지 않도록 부드럽게 죽인다. 예전엔 y > -0.05를
            // 불리언으로 끊어서, 달이 내려앉는 것이 아니라 깜빡하고 사라졌다.
            float horizon = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-0.07f, 0.07f, _direction.y));
            float strength = Mathf.Clamp01(intensity01) * horizon;

            Color color = _tint * strength;
            color.a = 1f;
            _material.SetColor(BaseColorId, color);

            // SkyDome과 같은 이유로 첫 몇 프레임은 검게라도 그려 배리언트를 미리 컴파일해 둔다.
            if (_warmFrames > 0)
            {
                _warmFrames--;
                _renderer.enabled = true;
                return;
            }

            _renderer.enabled = strength > 0.0015f;
        }

        private void Awake()
        {
            Build();
            if (_renderer == null || _material == null) return;
            // 로딩 중 한 번 그려야 하므로 꺼두지 않는다. 검은색 가산은 화면에 아무것도 더하지 않는다.
            _material.SetColor(BaseColorId, new Color(0f, 0f, 0f, 1f));
            _renderer.enabled = true;
        }

        private void LateUpdate()
        {
            if (_followTarget == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                _followTarget = main.transform;
            }

            transform.position = _followTarget.position + (_direction * _distance);
            // 카메라를 등지고 서면 뒤집혀 보인다. 항상 카메라 쪽을 향하게 둔다.
            transform.rotation = Quaternion.LookRotation(transform.position - _followTarget.position);
            transform.localScale = Vector3.one * _size;
        }

        private void OnDestroy()
        {
            if (_material != null)
            {
                if (Application.isPlaying) Destroy(_material);
                else DestroyImmediate(_material);
            }
            if (_texture != null && _texture.name == "MoonDisk (Procedural)")
            {
                if (Application.isPlaying) Destroy(_texture);
                else DestroyImmediate(_texture);
            }
        }

        private void Build()
        {
            var filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            filter.sharedMesh = quad.GetComponent<MeshFilter>().sharedMesh;
            if (Application.isPlaying) Destroy(quad); else DestroyImmediate(quad);

            Shader shader = Shader.Find("PPack/SkyDome");
            if (shader == null)
            {
                Debug.LogError("PPack/SkyDome 셰이더를 찾지 못했다. 달은 렌더링되지 않는다.");
                return;
            }

            _material = new Material(shader) { name = "M_MoonDisk (Runtime)" };
            if (_texture == null) _texture = BuildSoftDisk();
            _material.SetTexture(BaseMapId, _texture);

            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        /// <summary>텍스처를 안 주면 만들어 쓰는 부드러운 원반. 가운데는 꽉 차고 가장자리만 빠르게
        /// 사라져서, 가산 합성에서 달 모양으로 읽힌다.</summary>
        private static Texture2D BuildSoftDisk()
        {
            const int size = 128;
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, true)
            {
                name = "MoonDisk (Procedural)",
                wrapMode = TextureWrapMode.Clamp,
            };

            var pixels = new Color32[size * size];
            float half = size * 0.5f;
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f - half) / half;
                    float dy = (y + 0.5f - half) / half;
                    float r = Mathf.Sqrt((dx * dx) + (dy * dy));
                    // 0.86까지 꽉 찬 원, 1.0까지 부드럽게 소멸.
                    float a = 1f - Mathf.SmoothStep(0.86f, 1f, r);
                    byte v = (byte)Mathf.RoundToInt(Mathf.Clamp01(a) * 255f);
                    pixels[(y * size) + x] = new Color32(v, v, v, 255);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(true);
            return texture;
        }
    }
}
