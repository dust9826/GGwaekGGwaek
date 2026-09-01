using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 트럭의 <b>남은 경로</b>를 트럭이 실제로 달릴 차선을 따라 이어지는 둥근 단면의 리본(관)으로
    /// 그리고, 그 위를 목적지 쪽으로 흐르는 하이라이트로 방향을 보여준다. <b>도착지</b>에 핀을 세우고,
    /// 그 트럭의 머리 위 표지(<see cref="DeliveryTruckBeacon"/>)까지 <b>같은 색</b>으로 묶는다.
    ///
    /// 셋(현재 위치·경로·도착지)이 전부 멀리서도 보인다. 트럭 인스턴스당 <see cref="Palette"/>에서
    /// 고정 색을 하나 뽑는다(2026-08-18, 트럭 상한이 10으로 늘며 "항상 마젠타 하나"였던 예전 전제가
    /// 깨졌다) — 같은 색의 핀 두 개(트럭·도착지)와 그 사이를 흐르는 리본이 한 세트로 읽힌다.
    ///
    /// <b>리본은 각진 평면 화살표에서 시작했다.</b> 처음엔 간격을 두고 흐르는 납작한 삼각형이었는데,
    /// 비콘을 둥근 "피크" 모양으로 바꾼 뒤 나란히 두니 너무 단순해 보인다는 피드백을 받았다. 비콘과
    /// 같은 기법 — <see cref="DeliveryTruckBeacon.BuildRoundedOutline"/> 로 둥글린 알약(스타디움)
    /// 모양 단면을 경로를 따라 훑어(loft) 연속된 관을 만들고, <c>PPack/DeliveryRouteRibbonUnlit</c>
    /// 셰이더가 비콘과 같은 고정 가짜 광원으로 반사되는 느낌을 내면서, 경로를 따라간 거리(UV.y)로
    /// 목적지 쪽으로 흐르는 하이라이트 띠를 얹는다.
    ///
    /// <b>트럭이 실제로 달릴 차선을 그대로 따라간다.</b> <see cref="DeliveryRoutePose.PreferredLateralOffset"/>
    /// 를 그대로 써서 — 보행로(차선 하나뿐)는 정중앙, 일반 도로는 진행 방향 기준 우측 차선. 이 값은
    /// <see cref="DeliveryRoute.Evaluate"/> 가 교차로 부근에서 이미 부드럽게 블렌딩해 주므로,
    /// 트럭이 실제로 도는 원호 필릿과 리본이 항상 같은 곡선을 그린다(2026-08-18).
    ///
    /// <b>순수 표시 레이어다.</b> <see cref="DeliveryTruck"/> 의 상태를 읽기만 하고 바꾸지 않는다
    /// (`../Delivery/AGENTS.md` 의 UI 경계). 권위 로직이 전혀 없으므로 헤드리스 서버에서는
    /// 렌더러가 없어 아무 일도 안 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeliveryRouteDisplay : MonoBehaviour
    {
        /// <summary>
        /// 마을 팔레트(따뜻한 주황 불빛·푸른 흰 눈·짙은 녹색 나무)와 겹치지 않는 고채도 팔레트.
        /// 트럭 인스턴스당 하나씩 <see cref="Palette"/>에서 고정으로 뽑는다(2026-08-18).
        ///
        /// <b>예전엔 고정 마젠타 하나였다</b> — 트럭 상한이 1이라 "지금 화면에 트럭은 항상 하나뿐"
        /// 이었고, 그 전제로 <c>Request.Id % 6</c> 순환을 시도했다가 되돌린 적이 있다: 그때는 트럭
        /// 하나가 완료·취소되고 다음 의뢰로 교체되는 구조였어서(같은 GameObject가 여러 Request를
        /// 거쳐감), 같은 화면 요소가 다음 의뢰로 넘어가는 순간 갑자기 색을 바꿨다.
        ///
        /// <b>지금은 트럭 상한이 10이라 여러 대가 동시에 화면에 있고</b>, 위 되돌린 이유의 전제도
        /// 이미 사실이 아니다 — <see cref="DeliveryTruck.Request"/> 는 private set 으로 트럭
        /// 생애(Instantiate ~ 완료 즉시 Destroy) 동안 딱 한 번만 할당된다(<c>DeliveryDirector.
        /// SpawnTruck</c>, `Delivery/Scripts/DeliveryTruck.cs`). 즉 트럭 하나 = 의뢰 하나이고 절대
        /// 안 바뀐다. 그래서 같은 계산(<c>Request.Id % Palette.Length</c>)이 지금은 "트럭 인스턴스당
        /// 고정 색"이 된다 — 색이 바뀌는 건 서로 다른 트럭 사이에서만이고, 그게 바로 원하는 동작이다.
        /// </summary>
        private static readonly Color[] Palette =
        {
            new Color(1f, 0.05f, 0.85f),    // 마젠타 — 예전 고정색, 팔레트의 첫 색으로 유지
            new Color(0.05f, 0.85f, 0.85f), // 시안
            new Color(0.95f, 0.85f, 0.05f), // 레몬 옐로
            new Color(0.25f, 0.3f, 1f),     // 인디고 블루
            new Color(1f, 0.2f, 0.45f),     // 로즈
            new Color(0.6f, 0.25f, 1f),     // 바이올렛
        };

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int FlowSpeedId = Shader.PropertyToID("_FlowSpeed");

        [SerializeField] private DeliveryTruck _truck;

        [Header("경로 리본")]
        [Tooltip("경로 y=0 기준 높이. 눈이 가득 찼을 때 윗면(0.56m)보다 높아야 리본이 파묻히지 않는다.")]
        [SerializeField, Min(0f)] private float _arrowHeight = 0.7f;

        [SerializeField, Min(0.05f)] private float _ribbonWidth = 0.5f;
        [SerializeField, Min(0.02f)] private float _ribbonHeight = 0.16f;
        [SerializeField, Min(0f)] private float _ribbonCornerRadius = 0.08f;
        [Tooltip("경로를 따라 링을 찍는 간격(m). 낮을수록 곡선이 매끈해지지만 정점이 늘어난다.")]
        [SerializeField, Min(0.2f)] private float _ribbonSampleSpacing = 0.5f;
        [Tooltip("단면(알약 모양)의 모서리 하나를 둥글릴 때의 분할 수.")]
        [SerializeField, Min(1)] private int _edgeSegments = 6;

        [Tooltip("리본이 트럭의 실제 가로 위치에서 출발해 기본 차선으로 합류하기까지의 거리(m). " +
                 "트럭은 눈 회피·양보로 차선을 벗어나는데 리본은 늘 기본 차선에 그려지므로, " +
                 "0 으로 두면 그 차이만큼 리본이 트럭 발밑에서 떨어져 보인다.")]
        [SerializeField, Min(0f)] private float _laneMergeMeters = 6f;

        [Tooltip("초당 흐르는 거리(m). 목적지 쪽으로 흐른다. 셰이더가 이 값을 그대로 m/s 로 쓴다 " +
                 "— 예전엔 '주기/초'로 해석돼 실제로는 10m/s 로 흘렀다.")]
        [SerializeField] private float _flowSpeed = 6f;

        [Header("도착지 표지")]
        [SerializeField, Min(0f)] private float _destinationPinHeight = 3.5f;
        [SerializeField, Min(0.05f)] private float _destinationPinSize = 0.9f;

        private static Material _sharedRibbonMaterial;

        /// <summary>
        /// 같은 공장을 목적지로 하는 활성 의뢰가 동시에 여럿이면(트럭 상한이 1일 때는 없던 일이지만
        /// 지금은 최대 10대라 흔하다) 도착 핀이 정확히 같은 좌표에 겹쳐 반투명 두 장이 서로를 뚫고
        /// 지나가 보인다(2026-08-18 실측: 서로 다른 의뢰 ID의 핀이 같은 XZ에 찍힘). 공장별로 "지금
        /// 몇 번째 자리가 비었는지"만 기억해 핀을 그 자리만큼 위로 쌓는다 — 슬롯을 반납하지 않으면
        /// 계속 늘어나므로 <see cref="OnDestroy"/> 에서 반드시 돌려준다.
        /// </summary>
        private static readonly Dictionary<DeliveryFactory, HashSet<int>> DestinationSlots =
            new Dictionary<DeliveryFactory, HashSet<int>>();

        private const float DestinationStackSpacing = 1.3f;

        private GameObject _ribbonObject;
        private MeshRenderer _ribbonRenderer;
        private Mesh _ribbonMesh;
        private MaterialPropertyBlock _propertyBlock;
        private GameObject _destinationAnchor;
        private DeliveryFactory _destinationSlotFactory;
        private int _destinationSlotIndex = -1;
        private bool _visible;
        private bool _colorApplied;
        private Color _truckColor;

        private readonly List<Vector3> _vertices = new List<Vector3>();
        private readonly List<Vector3> _normals = new List<Vector3>();
        private readonly List<Vector2> _uvs = new List<Vector2>();
        private readonly List<int> _triangles = new List<int>();

        private void Awake()
        {
            if (_truck == null) _truck = GetComponent<DeliveryTruck>();
            BuildRibbonObject();
        }

        private void OnDestroy()
        {
            // 리본과 도착지 앵커는 월드 좌표를 그대로 쓰므로 트럭의 자식이 아니다. 직접 지운다.
            if (_ribbonObject != null) Destroy(_ribbonObject);
            if (_destinationAnchor != null) Destroy(_destinationAnchor);
            if (_ribbonMesh != null) Destroy(_ribbonMesh);
            ReleaseDestinationSlot();
        }

        private void LateUpdate()
        {
            if (_truck == null || _truck.Request == null
                || _truck.Request.State != EDeliveryRequestState.Active)
            {
                SetVisible(false);
                return;
            }

            ApplyTruckColor();
            EnsureDestinationPin();
            SetVisible(true);
            RebuildRibbon();
        }

        /// <summary>
        /// 트럭 하나의 생애 동안(의뢰가 절대 안 바뀌므로) 같은 표시색을 유지한다. 머리 위 표지·
        /// 리본·도착지 핀이 전부 이 색을 공유해 한 세트로 읽힌다.
        /// </summary>
        private void ApplyTruckColor()
        {
            if (_colorApplied) return;
            _colorApplied = true;
            _truckColor = Palette[(uint)_truck.Request.Id % Palette.Length];

            // 머리 위 표지도 같은 색으로 묶는다 — 셋이 한 세트로 읽혀야 한다.
            DeliveryTruckBeacon beacon = _truck.GetComponentInChildren<DeliveryTruckBeacon>(true);
            if (beacon != null) beacon.SetColor(_truckColor);

            _propertyBlock ??= new MaterialPropertyBlock();
            _propertyBlock.SetColor(BaseColorId, _truckColor);
            _propertyBlock.SetFloat(FlowSpeedId, _flowSpeed);
            _ribbonRenderer.SetPropertyBlock(_propertyBlock);
        }

        /// <summary>
        /// 남은 경로 위에만 리본을 그린다 — 이미 지나온 구간은 "어디로 가는지" 에 아무 정보도 없다.
        /// 트럭 위치(<c>from</c>)에서 목적지(<c>to</c>)까지 <see cref="_ribbonSampleSpacing"/> 간격으로
        /// 링을 찍어 훑고(loft), 정점의 UV.y 에 경로를 따라간 거리(월드 미터)를 담는다 — 셰이더가 그
        /// 값으로 흐르는 하이라이트를 그린다. 흐름 애니메이션 자체는 셰이더의 <c>_Time.y</c> 기반이라
        /// 트럭이 서 있든 달리든(정차·주행) 항상 같은 속도로 흐른다.
        /// </summary>
        private void RebuildRibbon()
        {
            DeliveryRoute route = _truck.Request.Route;
            float from = _truck.RouteDistance;
            float to = route.Length;

            _vertices.Clear();
            _normals.Clear();
            _uvs.Clear();
            _triangles.Clear();
            _ribbonMesh.Clear();

            if (to - from < 0.05f) return;

            List<Vector3> crossSection = BuildCrossSection(out Vector3[] crossNormals);
            int m = crossSection.Count;

            var sampleDistances = new List<float>();
            float spacing = Mathf.Max(0.2f, _ribbonSampleSpacing);
            for (float distance = from; distance < to; distance += spacing) sampleDistances.Add(distance);
            sampleDistances.Add(to);

            // 교차로 블렌딩의 t 는 경로 거리에 선형인데 이차 베지어는 등속이 아니다 — 꺾임이 급할수록
            // 정점 부근에서 곡선이 느려져, 같은 거리 간격으로 찍어도 공간상 거의 겹치는 점들이 나온다.
            // 그런 점들의 차분으로 단면 방향을 구하면 방향이 마구 튀어 리본이 찢어져 보인다. 너무
            // 가까운 샘플은 버린다.
            const float MinRingSpacing = 0.05f;
            var distances = new List<float>(sampleDistances.Count);
            var centerlines = new List<Vector3>(sampleDistances.Count);
            var forwards = new List<Vector3>(sampleDistances.Count);
            for (int s = 0; s < sampleDistances.Count; s++)
            {
                float distance = sampleDistances[s];
                DeliveryRoutePose pose = route.Evaluate(distance);

                // 트럭의 실제 가로 위치에서 출발해 기본 차선으로 합류시킨다. 트럭은 눈 회피·양보로
                // 차선을 벗어나 있을 수 있는데(DeliveryTruck.LateralOffset) 남은 경로는 어디까지나
                // 기본 차선을 따라가므로, 이어 붙이지 않으면 리본이 트럭 발밑에서 떨어져 시작한다.
                float laneOffset = pose.PreferredLateralOffset;
                if (_laneMergeMeters > 0f)
                {
                    float merge = Mathf.SmoothStep(0f, 1f, (distance - from) / _laneMergeMeters);
                    laneOffset = Mathf.Lerp(_truck.LateralOffset, laneOffset, merge);
                }
                Vector3 centerline = pose.Position + pose.SegmentRight * laneOffset + Vector3.up * _arrowHeight;

                if (centerlines.Count > 0
                    && (centerline - centerlines[^1]).sqrMagnitude < MinRingSpacing * MinRingSpacing)
                {
                    // 마지막 샘플은 버리지 않고 끝점으로 옮긴다 — 리본이 목적지 앞에서 끊기면 안 된다.
                    if (s < sampleDistances.Count - 1) continue;
                    distances[^1] = distance;
                    centerlines[^1] = centerline;
                    forwards[^1] = pose.Forward;
                    continue;
                }

                distances.Add(distance);
                centerlines.Add(centerline);
                forwards.Add(pose.Forward);
            }

            int ringCount = centerlines.Count;
            if (ringCount < 2) return;
            var ringStart = new int[ringCount];

            // route.Evaluate 의 Position(베지어 보간)과 Forward(정점 탄젠트 lerp)는 서로 다른 보간이라
            // 완전히 일치하지 않는다. 급커브에서 이 미세한 어긋남 때문에 인접한 링끼리 서로 비틀리며
            // 리본이 잘록해 보였다 — 실제로 찍힌 중심선 점들의 차분으로 단면 방향을 다시 구하면 인접
            // 링끼리 항상 서로 일치해 그 비틀림이 사라진다.
            var ringRight = new Vector3[ringCount];
            var ringLateralScale = new float[ringCount];
            float halfWidth = _ribbonWidth * 0.5f;
            for (int r = 0; r < ringCount; r++)
            {
                Vector3 prevRaw = r > 0 ? centerlines[r] - centerlines[r - 1] : Vector3.zero;
                Vector3 nextRaw = r < ringCount - 1 ? centerlines[r + 1] - centerlines[r] : Vector3.zero;
                // 방향만 평균한다 — 마지막 구간은 간격이 짧을 수 있어 날것의 차분을 더하면 그쪽으로 쏠린다.
                Vector3 prevDir = prevRaw.normalized;
                Vector3 nextDir = nextRaw.normalized;

                Vector3 tangent = prevDir + nextDir;
                Vector3 forward = tangent.sqrMagnitude > 1e-8f ? tangent.normalized : forwards[r];
                ringRight[r] = Vector3.Cross(Vector3.up, forward).normalized;

                // 마이터 보정. 단면을 이등분선에 수직으로 놓기만 하면 각 직선 구간에서 본 리본 폭이
                // cos(θ/2) 만큼 좁아져 코너가 찌그러져 보인다 — 1/cos(θ/2) 로 옆으로 늘려 폭을 유지한다.
                Vector3 segmentDir = prevDir.sqrMagnitude > 1e-8f ? prevDir : nextDir;
                Vector3 segmentRight = Vector3.Cross(Vector3.up, segmentDir).normalized;
                float cosHalf = Mathf.Abs(Vector3.Dot(ringRight[r], segmentRight));
                float scale = cosHalf > 0.5f ? 1f / cosHalf : 2f;

                // 폭이 국소 회전 반경보다 크면 안쪽 모서리가 이웃 링을 넘어가 뒤집히고(자기교차) 리본이
                // 찢어진다. 반경 = 호 길이 / 회전각 으로 재서 폭을 반경의 80% 안으로 조인다 — 급한
                // 코너에서 살짝 가늘어지는 편이 찢어지는 것보다 낫다.
                if (prevDir.sqrMagnitude > 1e-8f && nextDir.sqrMagnitude > 1e-8f)
                {
                    float turnRadians = Vector3.Angle(prevDir, nextDir) * Mathf.Deg2Rad;
                    if (turnRadians > 1e-4f)
                    {
                        float arc = 0.5f * (prevRaw.magnitude + nextRaw.magnitude);
                        float maxHalfWidth = arc / turnRadians * 0.8f;
                        float wanted = halfWidth * scale;
                        if (wanted > maxHalfWidth) scale *= maxHalfWidth / wanted;
                    }
                }

                ringLateralScale[r] = scale;
            }

            for (int r = 0; r < ringCount; r++)
            {
                Vector3 centerline = centerlines[r];
                Vector3 right = ringRight[r];
                float miter = ringLateralScale[r];
                float travelled = distances[r] - from;

                ringStart[r] = _vertices.Count;
                for (int i = 0; i < m; i++)
                {
                    Vector3 local = crossSection[i];
                    // 옆으로만 늘린다 — 높이까지 늘리면 코너에서 리본이 두꺼워진다.
                    _vertices.Add(centerline + right * (local.x * miter) + Vector3.up * local.y);
                    Vector3 n2 = crossNormals[i];
                    _normals.Add((right * n2.x + Vector3.up * n2.y).normalized);
                    _uvs.Add(new Vector2((float)i / m, travelled));
                }
            }

            for (int r = 0; r < ringCount - 1; r++)
            {
                int ringA = ringStart[r];
                int ringB = ringStart[r + 1];
                for (int i = 0; i < m; i++)
                {
                    int iNext = (i + 1) % m;
                    _triangles.Add(ringA + i);
                    _triangles.Add(ringB + i);
                    _triangles.Add(ringB + iNext);
                    _triangles.Add(ringA + i);
                    _triangles.Add(ringB + iNext);
                    _triangles.Add(ringA + iNext);
                }
            }

            // 양 끝(트럭 쪽·목적지 쪽)을 평평하게 막아 닫힌 관으로 마무리한다. 단면 외곽선의 감김
            // 방향은 -Forward 쪽 뚜껑과 맞으므로, +Forward 쪽(목적지 끝)만 뒤집어야 바깥을 향한다
            // — 셰이더가 Cull Back 이라 방향이 틀리면 그 뚜껑이 통째로 사라진다.
            AddRibbonCap(ringStart[0], m, centerlines[0], -forwards[0], 0f, false);
            AddRibbonCap(ringStart[ringCount - 1], m, centerlines[ringCount - 1],
                        forwards[ringCount - 1], to - from, true);

            _ribbonMesh.SetVertices(_vertices);
            _ribbonMesh.SetNormals(_normals);
            _ribbonMesh.SetUVs(0, _uvs);
            _ribbonMesh.SetTriangles(_triangles, 0);
            _ribbonMesh.RecalculateBounds();
        }

        private void AddRibbonCap(int ringStart, int m, Vector3 center, Vector3 normal, float travelled,
                                  bool flipWinding)
        {
            int centerIndex = _vertices.Count;
            _vertices.Add(center);
            _normals.Add(normal);
            _uvs.Add(new Vector2(0.5f, travelled));
            for (int i = 0; i < m; i++)
            {
                int iNext = (i + 1) % m;
                _triangles.Add(centerIndex);
                _triangles.Add(ringStart + (flipWinding ? iNext : i));
                _triangles.Add(ringStart + (flipWinding ? i : iNext));
            }
        }

        /// <summary>알약(스타디움) 모양 단면 하나를 만든다 — 폭 <see cref="_ribbonWidth"/>, 높이
        /// <see cref="_ribbonHeight"/> 사각형의 네 모서리를 <see cref="DeliveryTruckBeacon.BuildRoundedOutline"/>
        /// 로 둥글인다(도로 코너를 둥글리는 것과 같은 기법). 각 점의 법선은 인접한 두 변의 바깥 방향을
        /// 평균해 모서리에서도 부드럽게 이어지게 한다.</summary>
        private List<Vector3> BuildCrossSection(out Vector3[] normals2D)
        {
            float halfWidth = _ribbonWidth * 0.5f;
            float halfHeight = _ribbonHeight * 0.5f;
            var corners = new[]
            {
                new Vector3(-halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, halfHeight, 0f),
                new Vector3(halfWidth, -halfHeight, 0f),
                new Vector3(-halfWidth, -halfHeight, 0f),
            };
            List<Vector3> outline = DeliveryTruckBeacon.BuildRoundedOutline(corners, _ribbonCornerRadius, _edgeSegments);
            int m = outline.Count;

            var segmentNormal = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                Vector3 d = outline[(i + 1) % m] - outline[i];
                segmentNormal[i] = new Vector3(-d.y, d.x, 0f).normalized;
            }

            normals2D = new Vector3[m];
            for (int i = 0; i < m; i++)
            {
                Vector3 prevSegment = segmentNormal[(i - 1 + m) % m];
                normals2D[i] = (prevSegment + segmentNormal[i]).normalized;
            }
            return outline;
        }

        private void BuildRibbonObject()
        {
            _ribbonObject = new GameObject("RouteRibbon");

            // DeliveryRoute.Evaluate 가 돌려주는 위치는 월드 좌표다. 이 오브젝트를 움직이는 트럭의
            // 자식으로 두면 트럭의 위치와 회전이 정점에 한 번 더 적용되어 리본이 실제 도로에서 멀리
            // 튄다. 월드 원점의 독립 오브젝트에 월드 정점을 담아 경로와 화면 표시를 일치시킨다.
            _ribbonObject.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            _ribbonObject.transform.localScale = Vector3.one;

            _ribbonMesh = new Mesh { name = "DeliveryRouteRibbon" };
            _ribbonMesh.MarkDynamic();
            _ribbonObject.AddComponent<MeshFilter>().sharedMesh = _ribbonMesh;

            _ribbonRenderer = _ribbonObject.AddComponent<MeshRenderer>();
            _ribbonRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            _ribbonRenderer.receiveShadows = false;
            _ribbonRenderer.sharedMaterial = GetSharedRibbonMaterial();

            _ribbonObject.SetActive(false);
        }

        private void EnsureDestinationPin()
        {
            if (_destinationAnchor != null) return;

            IReadOnlyList<DeliveryFactory> stops = _truck.Request.Stops;
            DeliveryFactory destination = stops[stops.Count - 1];
            if (destination == null) return;

            _destinationSlotFactory = destination;
            _destinationSlotIndex = ClaimDestinationSlot(destination);

            // 트럭의 자식으로 두면 트럭을 따라 움직인다. 도착지는 고정된 자리이므로 루트에 만들고
            // OnDestroy 에서 직접 지운다 — 자식으로 두고 매 프레임 월드 위치를 되돌리는 방법은
            // 표지의 LateUpdate 와 실행 순서가 보장되지 않아 한 프레임씩 떨린다.
            _destinationAnchor = new GameObject($"DeliveryDestination_{_truck.Request.Id}");

            // destination.StopPosition(진입 보행로의 도로쪽 끝점)이 아니라 경로가 실제로 끝나는
            // 좌표를 쓴다. DeliveryTruck은 RouteDistance == Route.Length에서 멈추므로(도로 그래프
            // 노드 위치) StopPosition까지는 가지 않는다 — 둘은 DeliverySceneRigBuilder의 서로
            // 다른 표(NodeSpecs/HouseSpecs)에서 독립적으로 잡은 좌표라 최대 4m 가까이 어긋난다.
            //
            // 리본 끝과 같은 식으로 차선 오프셋까지 더한다(2026-08-18) — 예전엔 중심선(오프셋 0)만
            // 썼는데, 리본은 항상 PreferredLateralOffset 만큼 옆으로 그려지므로 도로로 끝나는
            // 목적지에서는 핀이 리본 끝과 1.125m(도로 폭 4.5m 기준 차선 오프셋) 떨어져 보였다.
            // 실측: 도로로 끝나는 의뢰에서 gap=1.125m, 보행로로 끝나는 의뢰는 오프셋이 0이라
            // 우연히 안 보였다. 같은 좌표를 쓰면 트럭이 실제로 서는 자리·리본 끝·핀이 항상 한 점에서
            // 만난다.
            //
            // 같은 목적지를 향하는 다른 의뢰의 핀과 겹치지 않도록 슬롯 번호만큼 위로 쌓는다 —
            // 슬롯 0(첫 핀)은 항상 이 좌표 그대로라 기존 동작과 다르지 않다.
            DeliveryRoutePose endPose = _truck.Request.Route.Evaluate(_truck.Request.Route.Length);
            _destinationAnchor.transform.position = endPose.Position
                + endPose.SegmentRight * endPose.PreferredLateralOffset
                + Vector3.up * (_destinationSlotIndex * DestinationStackSpacing);

            var pin = new GameObject("Pin");
            pin.SetActive(false);   // Configure 가 Awake 보다 먼저 돌아야 크기·색이 반영된다
            pin.transform.SetParent(_destinationAnchor.transform, false);
            pin.AddComponent<DeliveryTruckBeacon>().Configure(
                _destinationAnchor.transform, _truckColor, _destinationPinHeight, _destinationPinSize);
            pin.SetActive(true);

            _destinationAnchor.SetActive(_visible);
        }

        /// <summary>공장별로 가장 작은 빈 슬롯 번호를 배정한다. 반납은 <see cref="ReleaseDestinationSlot"/>.</summary>
        private static int ClaimDestinationSlot(DeliveryFactory destination)
        {
            if (!DestinationSlots.TryGetValue(destination, out HashSet<int> slots))
            {
                slots = new HashSet<int>();
                DestinationSlots[destination] = slots;
            }
            int index = 0;
            while (slots.Contains(index)) index++;
            slots.Add(index);
            return index;
        }

        private void ReleaseDestinationSlot()
        {
            if (_destinationSlotFactory == null || _destinationSlotIndex < 0) return;
            if (DestinationSlots.TryGetValue(_destinationSlotFactory, out HashSet<int> slots))
                slots.Remove(_destinationSlotIndex);
        }

        private void SetVisible(bool visible)
        {
            if (_visible == visible) return;
            _visible = visible;
            if (_ribbonObject != null) _ribbonObject.SetActive(visible);
            if (_destinationAnchor != null) _destinationAnchor.SetActive(visible);
        }

        private static Material GetSharedRibbonMaterial()
        {
            if (_sharedRibbonMaterial != null) return _sharedRibbonMaterial;
            Shader shader = Shader.Find("PPack/DeliveryRouteRibbonUnlit");
            _sharedRibbonMaterial = new Material(shader) { hideFlags = HideFlags.DontSave };
            return _sharedRibbonMaterial;
        }
    }
}
