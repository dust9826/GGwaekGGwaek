using UnityEngine;

namespace PPack
{
    /// <summary>하늘에 겹치는 발광 레이어 하나. 카메라를 따라다니는 안쪽을 향한 구에 적도투영 텍스처를
    /// 가산 합성으로 얹는다. 별과 오로라가 이 컴포넌트를 값만 바꿔서 각각 쓴다 — 두 번째 소비자가
    /// 생겼으므로 공통으로 올린 것이고, 그 전까지는 별 전용이었다.
    ///
    /// <para><b>왜 스카이박스가 아닌가.</b> 절차적 스카이박스(<c>Skybox/Procedural</c>)는 텍스처 슬롯이
    /// 없다. 별·오로라를 스카이박스로 넣으려면 큐브맵으로 갈아타야 하고, 그러면
    /// <see cref="TimeOfDayDirector"/>가 시간대별로 하늘을 물들이는 방식과 해·달 원반을 통째로 버려야
    /// 한다. 따로 겹치는 쪽이 훨씬 싸다 — 삼각형 몇 백 개에 드로우콜 하나다.</para>
    ///
    /// <para>가산 합성이라 텍스처의 검은 배경은 그대로 투명해진다. 알파 채널이 필요 없고,
    /// <see cref="SetVisibility"/>가 0을 받으면 렌더러째 꺼진다.</para></summary>
    [DisallowMultipleComponent]
    public sealed class SkyDome : MonoBehaviour
    {
        private static readonly int BaseMapId = Shader.PropertyToID("_BaseMap");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int OcclusionId = Shader.PropertyToID("_Occlusion");

        /// <summary>Transparent 기본 큐. 가리는 레이어는 여기에 오프셋을 더해 늦게 그린다.</summary>
        private const int TransparentQueue = 3000;

        [Tooltip("적도 투영(2:1) 텍스처. 검은 배경 위에 빛나는 부분만 있어야 한다.")]
        [SerializeField] private Texture _texture;

        [Tooltip("가장 밝을 때의 세기.")]
        [SerializeField, Min(0f)] private float _maxIntensity = 1f;

        [Tooltip("색조. 흰색이면 텍스처 색 그대로.")]
        [SerializeField] private Color _tint = Color.white;

        [Tooltip("뒤에 있는 하늘 레이어를 얼마나 가리는가. 0이면 순수 가산이라 아무것도 가리지 않는다(별). " +
                 "1이면 진한 부분이 뒤쪽을 덮는다(오로라) — 밝은 커튼을 통과해 별이 보이는 것을 막는다. " +
                 "0보다 크면 그리는 순서도 뒤로 밀린다 — 가리려면 가려질 대상보다 나중에 그려야 하기 때문이다.")]
        [SerializeField, Range(0f, 1f)] private float _occlusion;

        [Tooltip("돔 반지름(m). 카메라 원거리 클립보다 확실히 안쪽이어야 잘리지 않는다.")]
        [SerializeField, Min(10f)] private float _radius = 900f;

        [Tooltip("하루에 도는 각도. 0이면 고정. 살짝 돌면 시간이 흐르는 게 읽힌다.")]
        [SerializeField] private float _rotationDegreesPerDay = 20f;

        [Tooltip("비우면 Camera.main을 따라간다. 별이 멀어지지 않게 위치만 따라붙는다.")]
        [SerializeField] private Transform _followTarget;

        private MeshRenderer _renderer;
        private Material _material;
        private float _visibility;
        private int _warmFrames = 3;

        /// <summary>0이면 완전히 꺼진다. 값은 smoothstep으로 완화해서 넣는다 — 밤 계수가 선형으로
        /// 올라오면 별이 켜지는 순간이 문턱처럼 읽힌다.</summary>
        public void SetVisibility(float visibility01)
        {
            _visibility = Mathf.Clamp01(visibility01);
            if (_renderer == null || _material == null) return;

            float eased = _visibility * _visibility * (3f - 2f * _visibility);
            Color color = _tint * (eased * _maxIntensity);
            // 알파는 밝기가 아니라 “얼마나 떴느냐”다. 셀이더가 이 값에 _Occlusion 을 곱해
            // 가림을 만든다 — eased 를 실어 오로라가 약해지면 가림도 같이 풀린다.
            color.a = eased;
            _material.SetColor(BaseColorId, color);

            // 첫 몇 프레임은 보이지 않아도 그린다. 셰이더 배리언트는 <b>처음 그려질 때</b> 컴파일되는데,
            // 그 순간이 하필 해질녘이면 별·오로라·달 원반이 한꺼번에 처음 켜지며 컴파일이 한 프레임에
            // 몰려 눈에 띄게 끊긴다. 로딩 중에 검은색으로 한 번 그려서 컴파일을 끝내 둔다.
            if (_warmFrames > 0)
            {
                _warmFrames--;
                _renderer.enabled = true;
                return;
            }

            _renderer.enabled = eased > 0.0015f;
        }

        public void SetDayProgress(float normalizedTime) =>
            transform.rotation = Quaternion.Euler(0f, normalizedTime * _rotationDegreesPerDay, 0f);

        private void Awake()
        {
            BuildDome();
            SetVisibility(0f);
        }

        private void LateUpdate()
        {
            if (_followTarget == null)
            {
                Camera main = Camera.main;
                if (main == null) return;
                _followTarget = main.transform;
            }
            transform.position = _followTarget.position;
        }

        private void OnDestroy()
        {
            if (_material == null) return;
            if (Application.isPlaying) Destroy(_material);
            else DestroyImmediate(_material);
        }

        private void BuildDome()
        {
            var filter = gameObject.GetComponent<MeshFilter>();
            if (filter == null) filter = gameObject.AddComponent<MeshFilter>();
            _renderer = gameObject.GetComponent<MeshRenderer>();
            if (_renderer == null) _renderer = gameObject.AddComponent<MeshRenderer>();

            filter.sharedMesh = InvertedSphere();
            transform.localScale = Vector3.one * _radius;

            // 전용 셰이더다. 재질은 평범하지만 안개가 없어야 한다 — 자세한 이유는 셰이더 주석에.
            Shader shader = Shader.Find("PPack/SkyDome");
            if (shader == null)
            {
                Debug.LogError("PPack/SkyDome 셰이더를 찾지 못했다. 하늘 레이어는 렌더링되지 않는다.");
                return;
            }

            _material = new Material(shader) { name = $"M_SkyDome_{name} (Runtime)" };
            if (_texture != null) _material.SetTexture(BaseMapId, _texture);
            _material.SetFloat(OcclusionId, _occlusion);

            // 가리는 레이어는 반드시 나중에 그려야 한다. 두 돔 모두 카메라 위치에 중심을 두므로
            // URP 의 투명 거리 정렬로는 순서가 정해지지 않는다 — 큐를 명시해야 결정적이다.
            _material.renderQueue = TransparentQueue + (_occlusion > 0f ? 10 : 0);

            _renderer.sharedMaterial = _material;
            _renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = UnityEngine.Rendering.LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = UnityEngine.Rendering.ReflectionProbeUsage.Off;
        }

        private static Mesh _invertedSphere;

        /// <summary>안쪽을 향한 구는 기본 Sphere의 삼각형을 뒤집어 만든다. 별도 메시 에셋을 두지
        /// 않는 이유는 여기서만 쓰이기 때문이다. 인스턴스끼리 공유한다.</summary>
        private static Mesh InvertedSphere()
        {
            if (_invertedSphere != null) return _invertedSphere;

            var source = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            Mesh primitive = source.GetComponent<MeshFilter>().sharedMesh;
            var mesh = new Mesh { name = "SkyDome (Inverted Sphere)" };
            mesh.vertices = primitive.vertices;
            mesh.uv = primitive.uv;
            mesh.normals = primitive.normals;

            int[] triangles = (int[])primitive.triangles.Clone();
            for (int index = 0; index < triangles.Length; index += 3)
                (triangles[index], triangles[index + 2]) = (triangles[index + 2], triangles[index]);
            mesh.triangles = triangles;
            mesh.RecalculateBounds();

            if (Application.isPlaying) Destroy(source);
            else DestroyImmediate(source);

            _invertedSphere = mesh;
            return _invertedSphere;
        }
    }
}
