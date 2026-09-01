using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 3D 뷰의 차량. 코드로 만든 상자 몇 개다 - 이 변종은 에셋 없이 돈다.
    ///
    /// <b>플레이트는 마커이지 블레이드가 아니다.</b> 진짜 블레이드는 필드가 깎는 스윕 박스 합집합이고,
    /// 그것은 요각만 있는 프레임에서 표현된다. 플레이트를 바디에 붙이는 것은 v7 이 한 그대로이고
    /// 의도적이다 - 마커가 차체와 함께 기울면 그것이 장식이라는 사실이 솔직하게 드러난다. 만약
    /// 이것이 컷이었다면 차체가 기울 때마다 컷이 떠돌았을 것이다.
    ///
    /// 날개가 달린 프로파일이면 날개 플레이트도 같이 세운다. 그리는 것과 깎는 것이 어긋나면
    /// 어디가 실제로 눈을 막는지 눈으로 읽을 수 없게 된다.
    ///
    /// 레이마처가 <c>SV_Depth</c> 를 쓰므로 이 상자들이 눈과 <b>깊이에서</b> 교차한다 - 눈이 섀시
    /// 아랫부분을 가리고 더미가 플레이트 윗변 위로 얹힌다. 지오메트리끼리 맞물리는 것이 아니다.
    /// </summary>
    public sealed class SnowVehicleRigCpu
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        public const float BodyWidthM = 1.9f;
        public const float BodyHeightM = 0.95f;
        public const float BodyLengthM = 4.1f;
        public const float PlateHeightM = 0.95f;

        private readonly Transform _root;
        private readonly Transform _body;
        private readonly Transform _plate;
        private readonly Transform _wingL;
        private readonly Transform _wingR;
        private readonly Material _bodyMat;
        private readonly Material _cabMat;
        private readonly Material _plateMat;

        public GameObject Root { get; }

        public SnowVehicleRigCpu(Transform parent)
        {
            Root = new GameObject("SnowVehicleRig");
            Root.transform.SetParent(parent, false);
            _root = Root.transform;

            var body = new GameObject("Body");
            body.transform.SetParent(_root, false);
            _body = body.transform;

            _bodyMat = MakeLit(new Color(0.86f, 0.42f, 0.13f));      // 제설차 주황
            _cabMat = MakeLit(new Color(0.22f, 0.24f, 0.28f));
            _plateMat = MakeLit(new Color(0.78f, 0.80f, 0.84f));

            Box("Chassis", _body, new Vector3(BodyWidthM, BodyHeightM, BodyLengthM),
                new Vector3(0f, BodyHeightM * 0.5f, 0f), _bodyMat);

            // 캡이 있으면 스틸 한 장에서도 진행 방향이 헷갈리지 않는다.
            Box("Cab", _body, new Vector3(BodyWidthM * 0.82f, BodyHeightM * 0.62f, BodyLengthM * 0.36f),
                new Vector3(0f, BodyHeightM * 1.28f, -BodyLengthM * 0.14f), _cabMat);

            _plate = Box("BladePlate", _body, Vector3.one, Vector3.zero, _plateMat).transform;
            _wingL = Box("BladeWingL", _plate, Vector3.one, Vector3.zero, _plateMat).transform;
            _wingR = Box("BladeWingR", _plate, Vector3.one, Vector3.zero, _plateMat).transform;
        }

        private static Material MakeLit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            var m = new Material(sh);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            m.color = c;
            if (m.HasProperty("_Smoothness")) m.SetFloat("_Smoothness", 0.32f);
            return m;
        }

        private static GameObject Box(string name, Transform parent, Vector3 scale, Vector3 pos, Material mat)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Cube);
            go.name = name;
            Object.Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(parent, false);
            go.transform.localScale = scale;
            go.transform.localPosition = pos;
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            return go;
        }

        public void SetActive(bool on) => Root.SetActive(on);

        /// <summary>
        /// 차량 상태와 블레이드 형상을 그대로 옮긴다. 형상은 시뮬이 실제로 쓰는 값이어야 한다 -
        /// 그리는 것과 깎는 것이 어긋나면 어디가 눈을 막는지 눈으로 읽을 수 없다.
        /// </summary>
        public void Sync(SnowBladeVehicleCpu v, in SnowBladeShape shape, float rideHeightM)
        {
            _root.SetPositionAndRotation(new Vector3(v.PosX, rideHeightM, v.PosZ),
                                         Quaternion.Euler(0f, v.HeadingDeg, 0f));

            // 블레이드를 들면 플레이트도 뜬다. 그래야 UP 이 눈에 보인다.
            float lift = v.BladeDown ? 0f : 0.55f;

            float w = shape.HalfWidthM * 2f;
            float d = Mathf.Max(0.06f, shape.HalfDepthM * 2f);
            _plate.localScale = new Vector3(w, PlateHeightM, d);
            _plate.localPosition = new Vector3(0f, PlateHeightM * 0.34f + lift, v.BladeOffsetM);
            _plate.localRotation = Quaternion.Euler(0f, v.BladeAngleDeg, 0f);

            SyncWing(_wingL, shape, shape.HasLeftWing, -1f, w, d);
            SyncWing(_wingR, shape, shape.HasRightWing, +1f, w, d);
        }

        /// <summary>날개는 플레이트의 자식이라 로컬 프레임에서 뿌리를 잡고 앞으로 꺾기만 하면 된다.</summary>
        private static void SyncWing(Transform t, in SnowBladeShape shape, bool on, float sign,
                                     float plateW, float plateD)
        {
            t.gameObject.SetActive(on);
            if (!on) return;

            float len = shape.WingLengthM;
            float a = shape.WingAngleDeg;

            // 부모 스케일이 (plateW, PlateHeightM, plateD) 라 로컬 스케일은 그것으로 나눠줘야 한다.
            t.localScale = new Vector3(len / plateW, 1f, plateD / plateD);
            t.localRotation = Quaternion.Euler(0f, -sign * a, 0f);

            float rad = a * Mathf.Deg2Rad;
            float halfLen = len * 0.5f;
            float rootX = sign * shape.HalfWidthM;
            t.localPosition = new Vector3(
                (rootX + sign * halfLen * Mathf.Cos(rad)) / plateW,
                0f,
                (halfLen * Mathf.Sin(rad)) / plateD);
        }

        public void Dispose()
        {
            if (Root != null) Object.Destroy(Root);
            if (_bodyMat != null) Object.Destroy(_bodyMat);
            if (_cabMat != null) Object.Destroy(_cabMat);
            if (_plateMat != null) Object.Destroy(_plateMat);
        }
    }
}
