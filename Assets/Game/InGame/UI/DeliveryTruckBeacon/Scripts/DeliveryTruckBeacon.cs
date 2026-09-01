using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 배송 트럭 머리 위에 얇고 반투명한 삼각 판 표지를 띄우고 월드 Y축으로 계속 돌린다.
    /// 트럭의 위치를 읽기만 하고 상태를 바꾸지 않는다 — UI 는 표시만 한다는 경계(Delivery/AGENTS.md)를 따른다.
    ///
    /// 차체 자체로는 위치를 알기 어렵다. WinterVillage 는 블루아워 야간 조명 + 안개 씬이고, 트럭은
    /// 공장(집) 노드에 흩어져 스폰되어 플레이어 시작점에서 40~70m 떨어지는 경우가 흔하다(2026-08-16 실측).
    ///
    /// <b>처음엔 주황색을 썼는데 실제로 몰아보니 "이게 뭔지 모르겠다"는 피드백이 나왔다.</b> 원인을 재보니
    /// 맵의 <c>M_WinterLantern_Glow</c>·<c>M_WinterHouse_WindowGlow</c> 이미션 색이 각각
    /// (1, 0.66, 0.2)·(1, 0.6, 0.2) 로 우리가 쓰던 주황(1, 0.65, 0)과 사실상 같은 색상이었다 — 맵에
    /// 가로등·창문 불빛이 이미 촘촘히 깔려 있어 표지가 그 사이에 그냥 묻혔다. 마을 팔레트(따뜻한 주황·
    /// 눈의 흰/파랑·짙은 녹색 나무)와 정반대인 마젠타로 바꿨다.
    ///
    /// <b>정지된 도형은 시선을 안 끈다.</b> 처음엔 평면 핀(머리 삼각형+꼬리)을 카메라 쪽으로 매 프레임
    /// 회전시키는 빌보드로 만들었다 — 평면 메시라 옆에서 보면 선처럼 사라지기 때문이었다. 두께가
    /// 있는 도형으로 바꾸면서 그 제약이 사라져 빌보드를 걷어내고, 대신 월드 Y축으로 계속 스핀시킨다
    /// — 회전은 펄스보다 훨씬 강한 움직임 신호라 "표지"로 더 잘 읽힌다.
    ///
    /// <b>모양을 리본 단면 문법으로 다시 만들었다(2026-08-18).</b> 그 전에는 삼각형 외곽선을 무게중심
    /// 쪽으로 <c>bevelRatio</c> 만큼(0.6) 오므린 "안쪽 외곽선"까지 사분원으로 말아 넣는 방식이라,
    /// 테두리가 도형 폭의 절반 이상을 먹어 평평한 면이 거의 남지 않았다 — 두께가 크기에 비례해서
    /// 커지는 구조였고(프리팹에는 크기 0.7 에 두께 0.3 이 굳어 있었다) 그래서 얇은 판이 아니라
    /// 통통한 기타 피크로 보였다. 지금은 <see cref="DeliveryRouteDisplay"/> 의 리본 단면(알약)과
    /// 똑같은 규칙을 쓴다: <b>테두리는 두께의 절반을 반지름으로 하는 반원 비드</b>이고, 그 반원의
    /// 중심선이 외곽선에서 안쪽으로 정확히 그 반지름만큼 들어간 자리에 놓인다. 테두리가 도형 크기와
    /// 무관하게 두께에만 비례하므로, 두께를 줄이면 실제로 얇아지고 평평한 면은 그대로 넓게 남는다.
    ///
    /// 정점을 면마다 복제하지 않고 링끼리 공유해서 법선이 매끈하게 보간되고(Gouraud), 셰이더(
    /// <c>PPack/DeliveryBeaconUnlit</c>)가 실제 씬 조명과 무관한 고정 가짜 광원으로 fake diffuse +
    /// specular 를 계산해 반사되는 느낌을 낸다 — 조명 방향이 고정이라 안개·야간에도 항상 같은 대비로
    /// 반짝인다. 렌더 스테이트(<c>Cull Back</c> · <c>ZWrite Off</c> · 알파 블렌드)도 리본과 같다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeliveryTruckBeacon : MonoBehaviour
    {
        [Tooltip("표지가 따라갈 대상. 비어 있으면 부모 트랜스폼을 쓴다.")]
        [SerializeField] private Transform _target;
        [SerializeField, Min(0f)] private float _height = 3f;
        [SerializeField, Min(0.05f)] private float _size = 0.7f;

        [Tooltip("판의 전체 두께(m). 테두리 반원 비드의 반지름은 이 값의 절반이다 — 리본 단면의 " +
                 "높이(0.16m)가 그 알약 끝 반원의 지름인 것과 같은 규칙이다.")]
        [SerializeField, Min(0.01f)] private float _thickness = 0.09f;

        [Tooltip("삼각형 세 모서리를 둥글리는 반경. 변 길이(약 0.7~0.78m)의 45%가 상한이라 그보다 크게 " +
                 "잡아도 잘린다. 0.3 은 변이 거의 다 호가 되어 삼각형이 아니라 둥근 덩어리로 읽히고, " +
                 "0.12 이하로 내리면 뾰족해진 꼭짓점 안쪽에서 캡 부채꼴이 겹쳐 반투명이 두 번 블렌딩되며 " +
                 "실선이 보인다 — 둘 사이인 0.2 가 '모서리가 부드러운 삼각형'으로 읽히는 값이다(2026-08-18 렌더 비교).")]
        [SerializeField, Min(0f)] private float _cornerRadius = 0.2f;
        [Tooltip("모서리 하나를 둥글릴 때의 분할 수. 낮으면 하이라이트에 각진 면 경계가 보인다.")]
        [SerializeField, Min(1)] private int _edgeSegments = 10;
        [Tooltip("테두리 반원 비드를 얼마나 매끄럽게 말아 넣을지의 분할 수(반원의 사분면 하나 기준).")]
        [SerializeField, Min(1)] private int _profileSegments = 6;

        [Tooltip("평평한 캡(중앙 면) 법선을 테두리 쪽으로 얼마나 기울일지(도). 0 이면 캡 전체가 " +
                 "순수 ±Z 법선이라 셰이딩이 완전히 평평해지고, 밝기 변화가 4.5cm 테두리에만 몰려 " +
                 "\"테두리만 진한 윤곽선 + 창백한 속\"으로 보인다(2026-08-18 실측). 실제 형상은 " +
                 "그대로 평평하게 두고 법선만 돔처럼 기울여 가짜 볼록함을 낸다.")]
        [SerializeField, Range(0f, 60f)] private float _capBulgeDegrees = 30f;
        [Tooltip("캡의 동심 링 분할 수. 1 이면 중심 정점과 경계 링만 있어 돔 기울기를 표현할 정점이 " +
                 "없다 — 2 이상이어야 _capBulgeDegrees 가 실제로 보인다.")]
        [SerializeField, Min(1)] private int _capRingSegments = 3;

        [Tooltip("마을 팔레트(주황 불빛·흰/파랑 눈·짙은 녹색 나무)와 겹치지 않는 마젠타.")]
        [SerializeField] private Color _color = new Color(1f, 0.05f, 0.85f);

        [SerializeField, Min(0f)] private float _bobAmplitude = 0.15f;
        [SerializeField, Min(0f)] private float _bobSpeedRadiansPerSecond = 1.6f;

        [Tooltip("정지된 도형은 시선을 안 끈다 — 크기를 주기적으로 펄스시켜 움직임으로 눈에 띄게 한다.")]
        [SerializeField, Range(0f, 0.6f)] private float _pulseScaleAmplitude = 0.18f;
        [SerializeField, Min(0f)] private float _pulseSpeedRadiansPerSecond = 3.2f;

        [Tooltip("월드 Y축 기준 회전 속도(도/초). 두께가 있어 어느 각도에서 봐도 형태가 유지된다.")]
        [SerializeField, Min(0f)] private float _spinSpeedDegreesPerSecond = 120f;

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static Material _sharedMaterial;

        private float _bobPhase;
        private MaterialPropertyBlock _propertyBlock;
        private MeshRenderer _meshRenderer;

        public void Configure(Transform target) => _target = target;

        /// <summary>
        /// 도착지 표지처럼 같은 표지 언어를 쓰되 색·높이·크기만 다르게 쓸 때.
        /// <b>메시는 <c>Awake</c> 에서 <c>_size</c> 로 만들어지므로 반드시 활성화 전에 불러야 한다</b> —
        /// 비활성 상태로 만든 오브젝트에 컴포넌트를 붙이고, 이걸 부른 뒤 활성화하면 순서가 맞는다.
        /// </summary>
        public void Configure(Transform target, Color color, float height, float size)
        {
            _target = target;
            _color = color;
            _height = height;
            _size = size;
        }

        /// <summary>
        /// 색만 런타임에 바꾼다. 색은 <see cref="MaterialPropertyBlock"/> 으로 얹으므로 메시가
        /// 이미 만들어진 뒤(<c>Awake</c> 이후)에도 언제든 바꿀 수 있다 — 트럭별로 색을 나눠 줄 때 쓴다.
        /// </summary>
        public void SetColor(Color color)
        {
            _color = color;
            ApplyColor();
        }

        private void ApplyColor()
        {
            if (_meshRenderer == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.SetColor(BaseColorId, _color);
            _meshRenderer.SetPropertyBlock(_propertyBlock);
        }

        private void Awake()
        {
            if (_target == null) _target = transform.parent;
            _bobPhase = Random.Range(0f, Mathf.PI * 2f);
            BuildMesh();
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            float bob = Mathf.Sin(Time.time * _bobSpeedRadiansPerSecond + _bobPhase) * _bobAmplitude;
            transform.position = _target.position + Vector3.up * (_height + bob);
            transform.rotation = Quaternion.Euler(0f, Time.time * _spinSpeedDegreesPerSecond, 0f);

            float pulse = 1f + Mathf.Sin(Time.time * _pulseSpeedRadiansPerSecond + _bobPhase) * _pulseScaleAmplitude;
            transform.localScale = Vector3.one * pulse;
        }

        /// <summary>
        /// 얇은 판 + 반원 테두리. 둥글린 삼각 외곽선을 적도로 두고, 그 안쪽으로 비드 반지름만큼
        /// 들어간 자리를 반원의 중심선으로 삼아 앞뒤 평평한 캡(±반지름)까지 말아 넣는다.
        /// </summary>
        private void BuildMesh()
        {
            var meshFilter = gameObject.AddComponent<MeshFilter>();
            _meshRenderer = gameObject.AddComponent<MeshRenderer>();
            _meshRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _meshRenderer.receiveShadows = false;
            _meshRenderer.sharedMaterial = GetSharedMaterial();

            // 색은 머티리얼이 아니라 프로퍼티 블록으로 얹는다 — 머티리얼은 셰이더 하나를 모든 표지가
            // 공유하고(드로우콜 배칭), 개별 색만 인스턴스별로 다르게 줄 수 있다.
            ApplyColor();

            float half = _size * 0.5f;
            Vector3 a = new Vector3(-half, half, 0f);
            Vector3 b = new Vector3(half, half, 0f);
            Vector3 c = new Vector3(0f, -half, 0f);
            Vector3 centroid = (a + b + c) / 3f;

            List<Vector3> outer = BuildRoundedOutline(new[] { a, b, c }, _cornerRadius, _edgeSegments);
            int n = outer.Count;

            // 각 점의 바깥 방향은 인접한 두 변의 바깥 법선을 평균한다 — 리본 단면
            // (DeliveryRouteDisplay.BuildCrossSection)이 쓰는 것과 같은 방법이라 모서리에서도
            // 방향이 부드럽게 이어진다.
            var edgeNormal = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                Vector3 d = outer[(i + 1) % n] - outer[i];
                edgeNormal[i] = new Vector3(-d.y, d.x, 0f).normalized;
            }
            var outward = new Vector3[n];
            for (int i = 0; i < n; i++)
            {
                outward[i] = (edgeNormal[(i - 1 + n) % n] + edgeNormal[i]).normalized;
                // 감김 방향이 반대면 평균 법선이 안쪽을 향한다. 무게중심에서 멀어지는 쪽으로 맞춘다.
                if (Vector3.Dot(outward[i], outer[i] - centroid) < 0f) outward[i] = -outward[i];
            }

            // 비드 반지름이 삼각형 내접원에 가까워지면 안쪽 중심선이 자기 자신을 넘어 뒤집힌다.
            // 두께는 어차피 얇게 쓰는 값이라 넉넉히 잘라 둔다.
            float maxRadius = Vector3.Distance(outer[0], centroid) * 0.3f;
            float radius = Mathf.Min(_thickness * 0.5f, maxRadius);

            var inner = new Vector3[n];
            for (int i = 0; i < n; i++) inner[i] = outer[i] - outward[i] * radius;

            int profileSegments = _profileSegments;
            var vertices = new List<Vector3>((2 * profileSegments + 1) * n + 2);
            var normals = new List<Vector3>(vertices.Capacity);
            var triangles = new List<int>();

            // 뒷면 캡 경계(k=-P, z=-r)에서 적도(k=0, 외곽선 그 자체)를 거쳐 앞면 캡 경계(k=+P, z=+r)까지.
            // 정점을 면마다 복제하지 않고 링끼리 공유해서 법선이 매끈하게 보간된다.
            var ringStart = new int[2 * profileSegments + 1];
            for (int k = -profileSegments; k <= profileSegments; k++)
            {
                float angle = (Mathf.Abs(k) / (float)profileSegments) * (Mathf.PI * 0.5f);
                float side = k < 0 ? -1f : 1f;
                float sinA = Mathf.Sin(angle);
                float cosA = Mathf.Cos(angle);

                ringStart[k + profileSegments] = vertices.Count;
                for (int i = 0; i < n; i++)
                {
                    Vector3 xy = inner[i] + outward[i] * (radius * cosA);
                    vertices.Add(new Vector3(xy.x, xy.y, side * radius * sinA));
                    normals.Add(new Vector3(outward[i].x * cosA, outward[i].y * cosA, side * sinA).normalized);
                }
            }

            for (int k = -profileSegments; k < profileSegments; k++)
            {
                int ringA = ringStart[k + profileSegments];
                int ringB = ringStart[k + 1 + profileSegments];
                for (int i = 0; i < n; i++)
                {
                    int iNext = (i + 1) % n;
                    triangles.Add(ringA + i);
                    triangles.Add(ringB + i);
                    triangles.Add(ringB + iNext);
                    triangles.Add(ringA + i);
                    triangles.Add(ringB + iNext);
                    triangles.Add(ringA + iNext);
                }
            }

            // 평평한 앞/뒤 캡. 형상은 그대로 평평하지만, 법선만 중심(순수 ±Z)에서 테두리 쪽으로
            // _capBulgeDegrees 만큼 서서히 기울여 돔처럼 셰이딩한다 — 앞뒤 캡은 서로 반대 방향을
            // 바라봐야 하므로 감김 순서도 반대로 줘야 한다. Cull Back 은 노멀이 아니라 감김 순서만
            // 으로 앞/뒤를 가르기 때문에, 둘 다 같은 순서면 한쪽은 항상 컬링되어 뚫려 보인다
            // (2026-08-18, 반투명 전환 때 드러남).
            AddCapDome(vertices, normals, triangles, ringStart[2 * profileSegments], n,
                      new Vector3(centroid.x, centroid.y, radius), Vector3.forward, outward,
                      _capBulgeDegrees, _capRingSegments, flipWinding: false);
            AddCapDome(vertices, normals, triangles, ringStart[0], n,
                      new Vector3(centroid.x, centroid.y, -radius), Vector3.back, outward,
                      _capBulgeDegrees, _capRingSegments, flipWinding: true);

            var mesh = new Mesh { name = "DeliveryTruckBeaconPlate" };
            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            meshFilter.sharedMesh = mesh;
        }

        /// <summary>임의 개수의 꼭짓점을 이차 베지어 컷으로 둥글린 닫힌 외곽선. 도로 코너를 둥글리는
        /// 것과 같은 기법(<see cref="DeliveryRoadCurve.EvaluateQuadraticBezierPoint"/>)을 재사용한다.
        /// 두 번째 사용처(<see cref="DeliveryRouteDisplay"/>의 경로 리본 단면)가 생겨 공개 정적으로
        /// 승격했다.</summary>
        public static List<Vector3> BuildRoundedOutline(IReadOnlyList<Vector3> corners,
                                                         float cornerRadius, int edgeSegments)
        {
            int count = corners.Count;
            var outline = new List<Vector3>((edgeSegments + 1) * count);
            for (int i = 0; i < count; i++)
            {
                Vector3 prev = corners[(i + count - 1) % count];
                Vector3 corner = corners[i];
                Vector3 next = corners[(i + 1) % count];
                Vector3 incoming = (corner - prev).normalized;
                Vector3 outgoing = (next - corner).normalized;
                float cut = Mathf.Min(cornerRadius,
                                      Vector3.Distance(prev, corner) * 0.45f,
                                      Vector3.Distance(corner, next) * 0.45f);
                Vector3 entry = corner - incoming * cut;
                Vector3 exit = corner + outgoing * cut;
                for (int step = 0; step <= edgeSegments; step++)
                {
                    float t = step / (float)edgeSegments;
                    outline.Add(DeliveryRoadCurve.EvaluateQuadraticBezierPoint(entry, corner, exit, t));
                }
            }
            return outline;
        }

        /// <summary>
        /// 평평한 캡을 중심(t=0)에서 이미 만들어진 경계 링(t=ringSegments, 실제 테두리 시작점)까지
        /// 동심 링으로 채운다. 정점 위치는 항상 중심과 경계 사이의 순수한 XY 보간이라 실제 형상은
        /// 여전히 평평하다 — <paramref name="outward"/>(경계 링과 같은 순서로 인덱싱되는 바깥 방향)를
        /// 이용해 법선만 <paramref name="flatNormal"/>(순수 ±Z)에서 테두리 쪽으로 서서히 기울여,
        /// 눈에는 볼록한 돔처럼 셰이딩되게 하는 가짜 범프다.
        /// </summary>
        private static void AddCapDome(List<Vector3> vertices, List<Vector3> normals, List<int> triangles,
                                       int boundaryRingStart, int ringCount, Vector3 center, Vector3 flatNormal,
                                       Vector3[] outward, float bulgeDegrees, int ringSegments, bool flipWinding)
        {
            float bulgeRad = bulgeDegrees * Mathf.Deg2Rad;
            var ringStarts = new int[ringSegments + 1];

            ringStarts[0] = vertices.Count;
            vertices.Add(center);
            normals.Add(flatNormal);

            for (int t = 1; t < ringSegments; t++)
            {
                float r = t / (float)ringSegments;
                float tilt = r * bulgeRad;
                float cosT = Mathf.Cos(tilt);
                float sinT = Mathf.Sin(tilt);
                ringStarts[t] = vertices.Count;
                for (int i = 0; i < ringCount; i++)
                {
                    Vector3 boundaryPos = vertices[boundaryRingStart + i];
                    vertices.Add(Vector3.Lerp(center, boundaryPos, r));
                    normals.Add((flatNormal * cosT + outward[i] * sinT).normalized);
                }
            }
            ringStarts[ringSegments] = boundaryRingStart;   // 이미 만들어진 경계 링을 그대로 재사용

            int firstRing = ringSegments > 1 ? ringStarts[1] : boundaryRingStart;
            for (int i = 0; i < ringCount; i++)
            {
                int iNext = (i + 1) % ringCount;
                triangles.Add(ringStarts[0]);
                triangles.Add(firstRing + (flipWinding ? iNext : i));
                triangles.Add(firstRing + (flipWinding ? i : iNext));
            }

            for (int t = 1; t < ringSegments; t++)
            {
                int ringA = ringStarts[t];
                int ringB = ringStarts[t + 1];
                for (int i = 0; i < ringCount; i++)
                {
                    int iNext = (i + 1) % ringCount;
                    if (!flipWinding)
                    {
                        triangles.Add(ringA + i);
                        triangles.Add(ringB + i);
                        triangles.Add(ringB + iNext);
                        triangles.Add(ringA + i);
                        triangles.Add(ringB + iNext);
                        triangles.Add(ringA + iNext);
                    }
                    else
                    {
                        triangles.Add(ringA + i);
                        triangles.Add(ringB + iNext);
                        triangles.Add(ringB + i);
                        triangles.Add(ringA + i);
                        triangles.Add(ringA + iNext);
                        triangles.Add(ringB + iNext);
                    }
                }
            }
        }

        private static Material GetSharedMaterial()
        {
            if (_sharedMaterial != null) return _sharedMaterial;
            Shader shader = Shader.Find("PPack/DeliveryBeaconUnlit");
            _sharedMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            return _sharedMaterial;
        }
    }
}
