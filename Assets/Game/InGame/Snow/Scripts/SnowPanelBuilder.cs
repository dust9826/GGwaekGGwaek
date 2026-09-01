using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 바닥 위에 얹는 눈 패널의 그리드 메시를 만든다.
    ///
    /// 정점 간격은 <b>권위 셀보다 조대해도 된다</b> — 데이터가 없는 곳을 보간할 뿐이므로
    /// 12.5cm 필드에 25cm 정점이 기본이다(스펙 §5). 64×64m 스테이지 전체가 131k 삼각형.
    ///
    /// UV0 을 만들지 않는다. 필드 참조가 월드 좌표에서 나오므로 언랩이 필요 없고,
    /// 덕분에 크기가 같은 패널들이 <b>메시 한 장과 머티리얼 한 장</b>을 공유한다.
    /// </summary>
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer))]
    public sealed class SnowPanelBuilder : MonoBehaviour
    {
        [SerializeField] private Vector2 _sizeMeters = new Vector2(16f, 16f);
        [Tooltip("정점 간격(m). 권위 셀(0.125)보다 조대하게 두는 것이 기본이다 — 잘게 해도 데이터가 없다.")]
        [SerializeField, Min(0.02f)] private float _vertexSpacing = 0.25f;
        [Tooltip("메시 bounds 를 이만큼 위로 넓힌다. 셰이더가 정점을 밀어내므로 원래 bounds 로는 컬링에 잘린다.")]
        [SerializeField, Min(0f)] private float _maxDepth = 0.3f;

        [Header("Optional surface conformance")]
        [Tooltip("지정하면 생성한 눈 정점을 이 콜라이더의 표면에 맞춘다. 경사 지형을 평탄화한 구역에서도 눈과 지면 사이가 벌어지지 않게 한다.")]
        [SerializeField] private MeshCollider _conformSurface;
        [Tooltip("맞춘 표면에서 눈을 법선 방향으로 띄우는 거리(m). Z-fighting 방지용이다.")]
        [SerializeField, Min(0f)] private float _surfaceOffset = 0.055f;

        private Mesh _mesh;

        private void Awake() => Rebuild();

        [ContextMenu("Rebuild Mesh")]
        public void Rebuild()
        {
            int nx = Mathf.Max(1, Mathf.RoundToInt(_sizeMeters.x / _vertexSpacing));
            int nz = Mathf.Max(1, Mathf.RoundToInt(_sizeMeters.y / _vertexSpacing));

            int vertexCount = (nx + 1) * (nz + 1);
            var vertices = new Vector3[vertexCount];

            float halfX = _sizeMeters.x * 0.5f;
            float halfZ = _sizeMeters.y * 0.5f;
            float stepX = _sizeMeters.x / nx;
            float stepZ = _sizeMeters.y / nz;

            bool conformToSurface = _conformSurface != null && _conformSurface.enabled;
            float rayStartY = conformToSurface
                ? _conformSurface.bounds.max.y + Mathf.Max(1f, _maxDepth + 0.5f)
                : 0f;
            float rayDistance = conformToSurface
                ? Mathf.Max(2f, _conformSurface.bounds.size.y + _maxDepth + 2f)
                : 0f;

            for (int z = 0; z <= nz; z++)
            {
                for (int x = 0; x <= nx; x++)
                {
                    int i = z * (nx + 1) + x;
                    Vector3 localVertex = new Vector3(-halfX + x * stepX, 0f, -halfZ + z * stepZ);

                    if (conformToSurface)
                    {
                        Vector3 worldVertex = transform.TransformPoint(localVertex);
                        var ray = new Ray(new Vector3(worldVertex.x, rayStartY, worldVertex.z), Vector3.down);

                        if (_conformSurface.Raycast(ray, out RaycastHit hit, rayDistance))
                        {
                            Vector3 worldSnowVertex = hit.point + hit.normal * _surfaceOffset;
                            localVertex = transform.InverseTransformPoint(worldSnowVertex);
                        }
                    }

                    vertices[i] = localVertex;
                }
            }

            var indices = new int[nx * nz * 6];
            int t = 0;
            for (int z = 0; z < nz; z++)
            {
                for (int x = 0; x < nx; x++)
                {
                    int i0 = z * (nx + 1) + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + (nx + 1);
                    int i3 = i2 + 1;

                    indices[t++] = i0; indices[t++] = i2; indices[t++] = i1;
                    indices[t++] = i1; indices[t++] = i2; indices[t++] = i3;
                }
            }

            if (_mesh == null)
            {
                _mesh = new Mesh { name = "SnowPanel", hideFlags = HideFlags.DontSave };
                GetComponent<MeshFilter>().sharedMesh = _mesh;
            }

            _mesh.Clear();
            // 16m·25cm 면 4,225 정점이지만 더 잘게 두면 65,535 를 넘는다.
            _mesh.indexFormat = vertexCount > 65535
                ? UnityEngine.Rendering.IndexFormat.UInt32
                : UnityEngine.Rendering.IndexFormat.UInt16;
            _mesh.vertices = vertices;
            _mesh.triangles = indices;

            // MeshCollider.Raycast 의 hit.normal 은 경사면에서 삼각형 단위로 끊길 수 있다.
            // 그 값을 정점 노멀로 복사하면 높이는 연속이어도 삼각형 경계가 검은 선처럼 보인다.
            // 완성된 공유 정점 그리드에서 다시 계산해 패널 전체의 조명을 연속으로 만든다.
            _mesh.RecalculateNormals();

            // 변위는 GPU 에서 일어나므로 CPU 쪽 bounds 가 그것을 모른다. 넓혀주지 않으면
            // 카메라가 비스듬할 때 패널이 통째로 컬링된다 — "alive > 0 인데 화면이 빈다" 계열의 함정.
            _mesh.RecalculateBounds();
            Bounds bounds = _mesh.bounds;
            bounds.Expand(new Vector3(0f, _maxDepth, 0f));
            bounds.center += Vector3.up * (_maxDepth * 0.5f);
            _mesh.bounds = bounds;
        }

        private void OnDestroy()
        {
            if (_mesh != null) Destroy(_mesh);
        }
    }
}
