using System.Diagnostics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;

namespace PPack
{
    /// <summary>
    /// 빈 씬의 빈 GameObject 하나에 붙이고 Play 를 누르면 전부 만들어진다 (v7 부트스트랩과 같은 관례).
    ///
    /// <b>이 파일이 세 층 중 가장 바깥이다.</b> <c>Time.fixedDeltaTime</c> 을 읽는 것도, 키보드를
    /// 읽는 것도, 카메라를 만드는 것도 전부 여기서만 한다. 시뮬 코어는 <c>Time</c> 도 <c>Input</c> 도
    /// <c>Camera</c> 도 모르고, 그래서 데디서버에서 <c>-batchmode -nographics</c> 로 그대로 돈다.
    ///
    /// 조작: <b>W/S</b> 가감속 · <b>A/D</b> 조향 · <b>Space</b> 블레이드 업다운 · <b>R</b> 리셋 ·
    /// <b>P</b> 일시정지 · <b>,/.</b> 램프 상한
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowSimCpuBootstrap : MonoBehaviour
    {
        [Header("맵 - 셀 크기는 컴파일 상수, 이것만 바꿀 수 있다")]
        [Tooltip("가로 세로 미터. 셀 0.125 m 로 나뉘고 청크 16셀 배수로 올림된다.\n" +
                 "128 은 PPack 실제 스테이지(64 x 64 m)의 4배 면적이다. " +
                 "스텝당 비용은 이 값과 무관하다 - 활성 반경에만 의존한다.")]
        [Range(16f, 512f)]
        [SerializeField] private float _mapSizeM = 128f;

        [Tooltip("초기 눈 깊이 mm. 300 은 PPack 의 maxDepthCm 30 과 같다.")]
        [Range(0, 2000)]
        [SerializeField] private int _initialDepthMm = 300;

        [Header("블레이드 - 인스펙터에서도, 플레이 중 키로도 바뀐다")]
        [Tooltip("블레이드 폭 m. 2.30 은 v5/v7 의 실제 블레이드다.\n\n" +
                 "0.125 m 셀에서 18.4 셀. 0.5 m 아래로 내리면 4 셀이 되어 배리어가 한두 셀만 막게 되고, " +
                 "그러면 더미가 벽을 돌아 뒤로 새기 시작한다. 위로는 활성 반경 10 m 가 둔덕까지 " +
                 "덮어야 하므로 폭 + 여유가 그 안에 들어와야 한다.")]
        [Range(0.5f, 12f)]
        [SerializeField] private float _bladeWidthM = 2.3f;

        [Tooltip("블레이드 두께 m (진행 방향). 스윕 세그먼트 수가 이 값의 절반에서 나오므로, " +
                 "얇게 할수록 빠른 주행에서 세그먼트가 늘어난다. 상한 8 에 걸리면 차선에 구멍이 난다.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float _bladeThicknessM = 0.35f;

        [Tooltip("차 중심에서 블레이드 선까지 m. 블레이드를 넓히면 보통 같이 키운다.")]
        [Range(0.5f, 8f)]
        [SerializeField] private float _bladeOffsetM = 1.6f;

        [Tooltip("차체 전방 대비 블레이드 요각. 음수가 좌향, 양수가 우향.\n\n" +
                 "컷 상자 · 퇴적 밴드 · relax 배리어가 함께 돌기 때문에 비스듬한 블레이드는 눈을 " +
                 "진행 방향이 아니라 자기 법선 방향으로 민다. 그 결과 더미가 한쪽 끝으로 밀려가고 " +
                 "그 끝에서 넘쳐 나가면서 한쪽에만 둔덕이 생긴다.\n\n" +
                 "45도를 넘기면 블레이드가 거의 옆으로 눕고, 스윕 상자가 진행 방향을 제대로 덮지 " +
                 "못해 차선에 안 깎인 줄이 남는다.")]
        [Range(-45f, 45f)]
        [SerializeField] private float _bladeAngleDeg = 0f;

        [Tooltip("블레이드 평면 형상.\n\n" +
                 "직선은 양 끝이 열려 있어서 각도를 줘도 넘친 눈이 좌우로 똑같이 샌다. 한쪽에만 " +
                 "날개를 달면 그쪽이 막히므로 반대쪽으로만 뱉는다 - 왼쪽 날개면 오른쪽으로.")]
        [SerializeField] private SnowBladeProfileKind _bladeProfile = SnowBladeProfileKind.Straight;

        [Tooltip("날개 하나의 길이 m. 0 이면 프로파일과 무관하게 직선이다.")]
        [Range(0f, 2f)]
        [SerializeField] private float _wingLengthM = 0.45f;

        [Tooltip("날개가 블레이드 선에서 앞으로 꺾인 각도. 0 이면 그냥 폭이 늘어난 직선이다.")]
        [Range(0f, 80f)]
        [SerializeField] private float _wingAngleDeg = 35f;

        [Header("눈 물성 - variant")]
        [Tooltip("프리셋. Custom 이면 아래 값들을 쓴다.\n\n" +
                 "Default 는 지금까지 쓰던 값 그대로여서 거동이 한 비트도 안 바뀐다.")]
        [SerializeField] private SnowMaterialPreset _materialPreset = SnowMaterialPreset.Default;

        [Tooltip("퍼지는 정도. 낮으면 물처럼 넓게 퍼지고 높으면 제자리에 서서 탑이 된다.\n" +
                 "마른 가루눈 35~40, 젖은 눈 50~60, 다져진 눈 60~70.")]
        [Range(10f, 80f)]
        [SerializeField] private float _customReposeDeg = 55f;

        [Tooltip("뭉치는 정도. 이 낙차를 넘기 전에는 한 밀리미터도 안 움직인다 - 정지 마찰이다.\n" +
                 "0 이면 마른 가루눈처럼 안식각에 닿는 즉시 무너진다.")]
        [Range(0, 200)]
        [SerializeField] private int _customCohesionMm = 0;

        [Tooltip("스텝당 이완 반복. 비용이 여기 비례한다.")]
        [Range(1, 12)]
        [SerializeField] private int _customRelaxIterations = 4;

        [Tooltip("눈 깊이 1 m 당 최고속 배수 = 1 / (1 + 이 값 x 깊이).")]
        [Range(0f, 12f)]
        [SerializeField] private float _customDragPerMetre = 3f;

        [Tooltip("더미가 도달할 수 있는 최대 높이 mm. 0 이면 상한 없음.\n\n" +
                 "상한 자체보다 중요한 것은 그것이 어떻게 지켜지느냐다. 퇴적이 남은 여유 높이에 " +
                 "비례해서 붓기 때문에, 상한에 가까워진 셀은 거의 안 받고 낮은 셀이 대신 받는다. " +
                 "그래서 더미가 위로 솟는 대신 옆으로 퍼진다.")]
        [Range(0, 5000)]
        [SerializeField] private int _customMaxPileHeightMm = 1600;

        [Tooltip("밴드 전체가 상한에 닿았을 때 밴드를 넓히는 횟수. 한 번에 셀 두 칸씩.\n" +
                 "클수록 넓게 퍼지고, 0 이면 상한을 무시하고 그 자리에 쌓는다.")]
        [Range(0, 16)]
        [SerializeField] private int _customSpreadRings = 6;

        [Header("차량")]
        [Tooltip("고정 계측 코스를 자동으로 달린다. v7 부트스트랩과 같은 이유로 있다 - 손으로 몬 " +
                 "주행은 재현이 안 되므로 스크린샷도 계측도 비교할 수가 없다.\n\n" +
                 "직진 -> 우선회 -> 직진 -> 좌선회 를 반복한다. G 키로 토글.")]
        [SerializeField] private bool _autoDriveCourse = false;
        [Tooltip("엔진 출력. 가속도와 최고속에 같이 곱해진다.\n\n" +
                 "계측을 위한 노브다. 더미 성장 · 둔덕 위치 · 스텝 ms 가 전부 속도에 딸려 있는데 " +
                 "속도를 손으로 유지하는 것은 재현이 안 된다. 낮추면 같은 코스를 천천히 훑으며 " +
                 "형상을 볼 수 있고, 높이면 스윕 세그먼트가 상한 8 에 걸리는 지점을 찾을 수 있다.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float _enginePower01 = 1f;

        [Header("표시")]
        [Tooltip("탑다운 높이 램프(시뮬 검증용) 와 3D 레이마칭(실제 룩) 중 무엇을 볼지. V 키로 전환.\n\n" +
                 "둘은 같은 권위 필드를 읽는 서로 다른 ③ 층이고 서로를 모른다. 그것이 " +
                 "\"나중에 렌더러를 갈아끼운다\" 가 실제로 가능하다는 증거다.")]
        [SerializeField] private SnowViewMode _viewMode = SnowViewMode.TopDownRamp;
        [Tooltip("이 높이가 램프의 빨강 끝이다. 더미가 1.2 m 언저리에서 포화하므로 1.5 가 기본값.")]
        [Range(0.1f, 5f)]
        [SerializeField] private float _rampMaxM = 1.5f;

        [SerializeField] private bool _showHud = true;

        [Tooltip("레이마칭 진단. 0 끄기 · 1 커버리지(초록 적중 / 빨강 마칭실패) · 2 스텝 히트맵. B 키로 순환.")]
        [Range(0, 2)]
        [SerializeField] private int _marchDebug = 0;

        [Tooltip("구운 로브의 세기. 0 이면 각진 계단식 슬래브가 그대로 보인다 - 비교용.\n\n" +
                 "높이필드 레이마칭만으로는 눈이 슬래브로 읽힌다는 것이 v7 의 실측이었고, " +
                 "둥근 구 격자를 높이 기여로 굽는 것이 그 해법이었다.")]
        [Range(0f, 1.5f)]
        [SerializeField] private float _lumpAmount = 1f;

        [Tooltip("구 반지름 m. 이 값이 그대로 coarse-max 상한에 더해진다 - 키우면 마칭 스텝이 늘어난다.")]
        [Range(0.05f, 0.8f)]
        [SerializeField] private float _lumpRadiusM = 0.30f;

        [Tooltip("구 격자 간격 m. 반지름보다 조금 촘촘해야 로브가 이어진다.")]
        [Range(0.1f, 1.2f)]
        [SerializeField] private float _lumpSpacingM = 0.23f;

        [Tooltip("둥근 어깨 세기. 0 이면 권위 필드의 날카로운 능선이 그대로 보인다 - 비교용. K 키로 토글.\n\n" +
                 "블레이드가 밀어놓은 자리는 relax 가 스텝당 초과분의 41% 밖에 못 풀기 때문에 늘 " +
                 "평형 밖에 있고, 그래서 날카롭다. 그것을 권위 필드가 아니라 렌더에서만 둥글린다.")]
        [Range(0f, 1f)]
        [SerializeField] private float _filletAmount = 1f;

        [Tooltip("저역통과 반경, 텍셀 단위. 4 면 25 cm - v7 fillet 과 같은 폭이다.")]
        [Range(1, 12)]
        [SerializeField] private int _filletRadiusTexels = 4;

        [Tooltip("저역통과한 표면을 얼마나 따라갈지. 1 이면 완전히 블러된 표면이 된다.")]
        [Range(0f, 1f)]
        [SerializeField] private float _filletStrength = 0.55f;

        private SnowHeightFieldCpu _field;
        private SnowPlowStepCpu _sim;
        private SnowBladeVehicleCpu _vehicle;
        private SnowHeightMapRenderer _renderer;
        private SnowCoarseMaxCpu _coarse;
        private SnowRaymarchRendererCpu _march;
        private SnowSurfaceBakeCpu _lump;
        private Light _sun;
        private SnowVehicleRigCpu _rig;
        private Camera _camera;
        private GameObject _body;
        private GameObject _blade;
        private Material _overlayBody;
        private Material _overlayBlade;

        private SnowVehicleInput _input;
        private bool _bladeDown = true;
        private bool _paused;
        private float _courseT;

        private SnowPlowStepStats _stats;
        private double _simMs;
        private double _simMsSmoothed;
        private double _bakeMs, _bakeMsSmoothed;
        private double _uploadMs, _uploadMsSmoothed;
        private double _frameMsSmoothed;
        private int _perfLogCountdown = 180;
        private double _gpuMsSmoothed;
        private double _changedAvg;
        [Tooltip("레이마칭 프록시 박스만 끈다. 나머지는 그대로여서 A/B 로 한계비용이 나온다. J 키.")]
        [SerializeField] private bool _marchEnabled = true;
        private readonly Stopwatch _watch2 = new Stopwatch();
        private readonly FrameTiming[] _frameTimings = new FrameTiming[1];
        private long _initialTotalMm;
        private readonly Stopwatch _watch = new Stopwatch();

        [Header("눈덩이 — 차로 밀면 굴러가고 커진다")]
        [Tooltip("공을 세울지. 끄면 이 씬은 예전과 완전히 같다.")]
        [SerializeField] private bool _ballEnabled = true;

        [Tooltip("지나간 셀에 남기는 눈 mm. 성장 속도의 유일한 노브다.\n\n" +
                 "실측(2026-08-19): 150 이면 6 m 를 밀어 지름 2.41 m · 7,347 L 로 상한에 닿는다 - " +
                 "게임으로는 너무 빠르다. 250 은 300 mm 눈에서 셀당 50 mm 를 걷으므로 " +
                 "지름 1 m 까지 약 17 m 를 굴려야 한다.")]
        [Range(0, 1000)]
        [SerializeField] private int _ballResidueMm = 250;

        private SnowBallCpu _ball;
        private GameObject _ballView;
        private Vector2 _ballXZ;
        private long _ballReleasedMm;

        private void Awake()
        {
            Build();
        }

        private void Build()
        {
            var geo = new SnowFieldGeometry(_mapSizeM, _mapSizeM, -_mapSizeM * 0.5f, -_mapSizeM * 0.5f);
            _field = new SnowHeightFieldCpu(geo, _initialDepthMm);
            _initialTotalMm = _field.TotalHeightMm;
            _sim = new SnowPlowStepCpu(_field);
            _vehicle = new SnowBladeVehicleCpu(0f, -_mapSizeM * 0.3f, 0f);
            ApplyBladeSize();
            _renderer = new SnowHeightMapRenderer(_field, _rampMaxM, transform);
            _lump = new SnowSurfaceBakeCpu(_field)
            {
                RadiusM = _lumpRadiusM,
                SpacingM = _lumpSpacingM,
                FilletRadiusTexels = _filletRadiusTexels,
                FilletStrength = _filletStrength
            };
            _lump.RebuildAll();
            // 로브를 먼저 굽고 상한을 만든다. 상한이 로브의 실제 들림을 담아야 하기 때문이다.
            _coarse = new SnowCoarseMaxCpu(_field, _lump);
            _march = new SnowRaymarchRendererCpu(_field, _coarse, _lump, transform)
            {
                LumpAmount = _lumpAmount,
                FilletAmount = _filletAmount
            };
            _march.SetDebug(_marchDebug);

            BuildCamera(geo);
            BuildOverlays();
            BuildSun();
            _rig = new SnowVehicleRigCpu(transform);

            // 카메라와 오버레이가 생긴 뒤에 불러야 한다. 앞에서 부르면 _camera 가 아직 null 이라
            // 원근 전환이 통째로 스킵되고, 직교 카메라로 프록시 박스를 보게 된다.
            ApplyViewMode();
            BuildBall();


            Debug.Log($"[SnowSimCpu] {geo.ResX}x{geo.ResZ} 셀 ({geo.CellCount:N0}) · " +
                      $"{geo.ChunksX}x{geo.ChunksZ} 청크 · 쿼드트리 깊이 {geo.QuadtreeDepth} · " +
                      $"높이 {geo.CellCount * 2 / 1024 / 1024f:0.0} MB · 초기 {_field.TotalVolumeM3:N1} m3");
        }

        /// <summary>
        /// 눈덩이를 차 앞에 세운다. <b>물리는 쓰지 않는다</b> — 이 씬의 차량은 순수 CPU 시뮬
        /// (<see cref="SnowBladeVehicleCpu"/>)이라 <c>Rigidbody</c> 도 콜라이더도 없다. 접촉은
        /// <see cref="StepBall"/> 이 직접 잰다. 멀티에서는 반대로 서버의 Unity 물리가 굴린다
        /// (<see cref="SnowBallCarrier"/>) — 권위 클래스(<see cref="SnowBallCpu"/>)는 양쪽이 같다.
        /// </summary>
        private void BuildBall()
        {
            if (!_ballEnabled) return;

            _ball = new SnowBallCpu(_field, _ballResidueMm);
            _ballXZ = new Vector2(_vehicle.PosX, _vehicle.PosZ + 8f);
            _ballReleasedMm = 0;

            _ballView = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            _ballView.name = "SnowBall";
            _ballView.transform.SetParent(transform, false);
            Destroy(_ballView.GetComponent<Collider>());   // 접촉은 CPU 로 잰다 - 콜라이더는 거짓 단서다

            var mat = new Material(Shader.Find("Universal Render Pipeline/Lit"))
            {
                hideFlags = HideFlags.HideAndDontSave,
            };
            if (mat.HasProperty(BaseColorId)) mat.SetColor(BaseColorId, new Color(0.94f, 0.96f, 1f));

            // 매끈한 구는 눈이 아니라 당구공으로 읽힌다 — 마처가 그리는 눈이 거칠기 때문에 대비가 더 크다.
            // 룩은 여기까지가 이 씬의 몫이고(검증용 대역), 실제 눈덩이 메시·셰이더는 아직 없다.
            if (mat.HasProperty(SmoothnessPropId)) mat.SetFloat(SmoothnessPropId, 0.05f);
            _ballView.GetComponent<MeshRenderer>().sharedMaterial = mat;

            SyncBallView();
        }

        /// <summary>
        /// 블레이드가 공에 닿아 있으면 진행 방향으로 밀고, 움직인 거리만큼 눈을 걷는다.
        ///
        /// <para>접촉은 <b>블레이드 선분에서 공 중심까지의 거리</b>로 본다. 공을 진행 방향으로만 밀고
        /// 옆으로 튕기지 않는 이유는 이 씬이 손맛이 아니라 <b>걷기·보존·성장</b>을 보이기 위한 것이기
        /// 때문이다. 옆으로 도는 맛은 물리가 있는 멀티 쪽의 일이다.</para>
        /// </summary>
        private void StepBall(float dt)
        {
            if (_ball == null) return;

            SnowBladePose pose = _vehicle.BladePose;
            float r = _ball.RadiusM;

            float dx = _ballXZ.x - pose.CenterX;
            float dz = _ballXZ.y - pose.CenterZ;

            float along = dx * pose.ForwardX + dz * pose.ForwardZ;   // 블레이드 법선 방향 거리
            float across = dx * pose.RightX + dz * pose.RightZ;      // 블레이드 선 위 위치

            bool touching = along > 0f
                            && along < r + _sim.Shape.HalfDepthM + 0.25f
                            && Mathf.Abs(across) < r + _sim.Shape.HalfWidthM;

            var prev = _ballXZ;
            if (touching && _vehicle.SpeedMps > 0f)
            {
                float push = _vehicle.SpeedMps * dt;
                _ballXZ.x += pose.ForwardX * push;
                _ballXZ.y += pose.ForwardZ * push;
            }

            _ball.ResidueMm = _ballResidueMm;
            _ball.Harvest(prev.x, prev.y, _ballXZ.x, _ballXZ.y);

            // 터짐은 크기가 아니라 사람이 누른다 - Q. 단품·멀티와 같은 규칙이어야 한다.
            if (Keyboard.current != null && Keyboard.current[Key.Q].wasPressedThisFrame)
            {
                _ballReleasedMm += _ball.MassMm;
                _stats.UnplacedMm += _ball.Release(_ballXZ.x, _ballXZ.y,
                                                   _sim.Material.MaxPileHeightMm,
                                                   _sim.Material.DepositSpreadRings * 4);
            }

            SyncBallView();
        }

        /// <summary>공을 눈 표면 위에 앉힌다. 파인 자리로 굴러 들어가면 같이 내려간다.</summary>
        private void SyncBallView()
        {
            if (_ballView == null || _ball == null) return;

            float r = _ball.RadiusM;
            float ground = 0f;
            if (_field.Geo.TryWorldToCell(_ballXZ.x, _ballXZ.y, out int cx, out int cz))
            {
                ground = _field.Get(cx, cz) * 1e-3f;
            }

            _ballView.transform.localScale = Vector3.one * (r * 2f);
            _ballView.transform.localPosition = new Vector3(_ballXZ.x, ground + r, _ballXZ.y);
        }

        /// <summary>들고 있는 눈을 그 자리에 붓는다. 키 <b>B</b>.</summary>
        private void ReleaseBall()
        {
            if (_ball == null) return;

            long before = _ball.MassMm;
            long unplaced = _ball.Release(_ballXZ.x, _ballXZ.y,
                                          _sim.Material.MaxPileHeightMm, _sim.Material.DepositSpreadRings);
            _ballReleasedMm += before - unplaced;
            _lump.RebuildAll();
            SyncBallView();
            Debug.Log($"[SnowBall] 놓음: {before:N0} mm 중 {unplaced:N0} mm 못 놓음");
        }

        /// <summary>
        /// 블레이드 치수를 시뮬과 오버레이에 밀어넣는다. 매 스텝 이 값에서 컷 · 배리어 · 퇴적 밴드가
        /// 다시 파생되므로 플레이 중에 불러도 안전하다.
        /// </summary>
        private void ApplyBladeSize()
        {
            _bladeWidthM = Mathf.Clamp(_bladeWidthM, 0.5f, 12f);
            _bladeThicknessM = Mathf.Clamp(_bladeThicknessM, 0.05f, 2f);
            _bladeOffsetM = Mathf.Clamp(_bladeOffsetM, 0.5f, 8f);
            _bladeAngleDeg = Mathf.Clamp(_bladeAngleDeg, -45f, 45f);

            _sim.Shape = new SnowBladeShape
            {
                HalfWidthM = _bladeWidthM * 0.5f,
                HalfDepthM = _bladeThicknessM * 0.5f,
                Profile = _bladeProfile,
                WingLengthM = _wingLengthM,
                WingAngleDeg = _wingAngleDeg
            };
            ApplyMaterial();
            _vehicle.BladeOffsetM = _bladeOffsetM;
            _vehicle.BladeAngleDeg = _bladeAngleDeg;
            _enginePower01 = Mathf.Clamp(_enginePower01, 0.05f, 2f);
            _vehicle.EnginePower01 = _enginePower01;
            if (_blade != null) _blade.transform.localScale = new Vector3(_bladeWidthM, _bladeThicknessM, 1f);
        }

        /// <summary>프리셋이거나 Custom 이면 인스펙터 값. 매 스텝 시뮬이 여기서 다시 읽는다.</summary>
        private void ApplyMaterial()
        {
            SnowMaterialCpu m;
            if (_materialPreset == SnowMaterialPreset.Custom)
            {
                m = SnowMaterialCpu.Default;
                m.ReposeAngleDeg = _customReposeDeg;
                m.CohesionMm = _customCohesionMm;
                m.RelaxIterations = _customRelaxIterations;
                m.DragPerMetre = _customDragPerMetre;
                m.MaxPileHeightMm = _customMaxPileHeightMm;
                m.DepositSpreadRings = _customSpreadRings;
            }
            else
            {
                m = SnowMaterialCpu.FromPreset(_materialPreset);
            }
            _sim.Material = m;
            _vehicle.DragPerMetre = _sim.Material.DragPerMetre;
        }

        /// <summary>보이는 쪽만 켠다. 둘 다 같은 필드를 읽으므로 전환에 상태가 없다.</summary>
        private void ApplyViewMode()
        {
            bool top = _viewMode == SnowViewMode.TopDownRamp;
            if (_renderer?.Quad != null) _renderer.Quad.SetActive(top);
            _march?.SetActive(!top && _marchEnabled);

            // 탑다운은 납작한 오버레이, 3D 는 상자 리그. 둘 다 같은 차량 상태를 읽는다.
            if (_body != null) _body.SetActive(top);
            if (_blade != null) _blade.SetActive(top);
            _rig?.SetActive(!top);
            if (_sun != null) _sun.enabled = !top;
            if (_camera != null) ApplyCamera();
        }

        /// <summary>탑다운은 직교로 위에서, 3D 는 차량 뒤를 따르는 원근 카메라.</summary>
        private void ApplyCamera()
        {
            var geo = _field.Geo;
            if (_viewMode == SnowViewMode.TopDownRamp)
            {
                _camera.orthographic = true;
                _camera.orthographicSize = geo.ResZ * SnowFieldGeometry.CellSizeM * 0.5f;
                _camera.transform.SetPositionAndRotation(
                    new Vector3(geo.OriginXM + geo.ResX * SnowFieldGeometry.CellSizeM * 0.5f, 60f,
                                geo.OriginZM + geo.ResZ * SnowFieldGeometry.CellSizeM * 0.5f),
                    Quaternion.Euler(90f, 0f, 0f));
            }
            else
            {
                _camera.orthographic = false;
                _camera.fieldOfView = 55f;
            }
        }

        private void BuildCamera(SnowFieldGeometry geo)
        {
            var go = new GameObject("SnowSimCamera");
            go.transform.SetParent(transform, false);
            _camera = go.AddComponent<Camera>();
            _camera.orthographic = true;
            _camera.orthographicSize = geo.ResZ * SnowFieldGeometry.CellSizeM * 0.5f;
            _camera.nearClipPlane = 0.1f;
            _camera.farClipPlane = 200f;
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = new Color(0.06f, 0.07f, 0.09f);
            go.transform.SetPositionAndRotation(
                new Vector3(geo.OriginXM + geo.ResX * SnowFieldGeometry.CellSizeM * 0.5f, 60f,
                            geo.OriginZM + geo.ResZ * SnowFieldGeometry.CellSizeM * 0.5f),
                Quaternion.Euler(90f, 0f, 0f));
        }

        /// <summary>3D 뷰의 상자들이 형태로 읽히려면 방향광이 필요하다. 탑다운에서는 꺼도 무관하다.</summary>
        private void BuildSun()
        {
            var go = new GameObject("SnowSun");
            go.transform.SetParent(transform, false);
            _sun = go.AddComponent<Light>();
            _sun.type = LightType.Directional;
            _sun.color = new Color(1f, 0.96f, 0.90f);
            _sun.intensity = 1.15f;
            _sun.shadows = LightShadows.None;
            go.transform.rotation = Quaternion.LookRotation(-new Vector3(0.52f, 0.46f, 0.40f).normalized);
        }

        private void BuildOverlays()
        {
            // 색만으로는 차가 어디 있는지 모른다. 필드 평면 위에 얇게 얹는다.
            // 빌트인 "Unlit/Color" 가 아니라 URP 것이어야 한다 - 아니면 마젠타로 뜬다.
            _overlayBody = MakeUnlit(new Color(0.08f, 0.08f, 0.10f));
            _overlayBlade = MakeUnlit(new Color(0.95f, 0.95f, 0.35f));
            _body = MakeOverlay("VehicleBody", 1.8f, 4.0f, _overlayBody, 0.05f);
            _blade = MakeOverlay("Blade", _bladeWidthM, _bladeThicknessM, _overlayBlade, 0.06f);
        }

        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int SmoothnessPropId = Shader.PropertyToID("_Smoothness");

        private static Material MakeUnlit(Color c)
        {
            var sh = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            var m = new Material(sh);
            if (m.HasProperty(BaseColorId)) m.SetColor(BaseColorId, c);
            m.color = c;
            return m;
        }

        private GameObject MakeOverlay(string name, float w, float d, Material mat, float y)
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Quad);
            go.name = name;
            Destroy(go.GetComponent<Collider>());
            go.transform.SetParent(transform, false);
            go.transform.localScale = new Vector3(w, d, 1f);
            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = mat;
            mr.shadowCastingMode = ShadowCastingMode.Off;
            mr.receiveShadows = false;
            go.transform.position = new Vector3(0f, y, 0f);
            return go;
        }

        /// <summary>
        /// 이 프로젝트는 <c>activeInputHandler = 1</c>(Input System 전용)이라 구 <c>UnityEngine.Input</c> 은
        /// 런타임에 예외를 던진다. 스파이크에서 옮겨온 키 조작은 전부 <see cref="Keyboard"/> 로 읽는다.
        /// 키보드가 없으면(헤드리스·배치모드) 조작을 아예 읽지 않는다 - 없는 장치를 참조하면 죽는다.
        /// </summary>
        private static Keyboard _kb;

        private void Update()
        {
            _kb = Keyboard.current;
            if (_kb == null) return;

            float throttle = 0f, steer = 0f;
            if (_kb[Key.W].isPressed || _kb[Key.UpArrow].isPressed) throttle += 1f;
            if (_kb[Key.S].isPressed || _kb[Key.DownArrow].isPressed) throttle -= 1f;
            if (_kb[Key.D].isPressed || _kb[Key.RightArrow].isPressed) steer += 1f;
            if (_kb[Key.A].isPressed || _kb[Key.LeftArrow].isPressed) steer -= 1f;

            if (_kb[Key.Space].wasPressedThisFrame) _bladeDown = !_bladeDown;
            if (_kb[Key.P].wasPressedThisFrame) _paused = !_paused;
            if (_kb[Key.H].wasPressedThisFrame) _showHud = !_showHud;
            if (_kb[Key.B].wasPressedThisFrame) { _marchDebug = (_marchDebug + 1) % 3; _march.SetDebug(_marchDebug); }

            // 레이마칭만 끈다. 나머지는 그대로여서 A/B 로 한계비용이 나온다.
            if (_kb[Key.J].wasPressedThisFrame)
            {
                _marchEnabled = !_marchEnabled;
                _march.SetActive(_marchEnabled && _viewMode == SnowViewMode.Raymarch3D);
            }
            if (_kb[Key.L].wasPressedThisFrame) { _lumpAmount = _lumpAmount > 0.5f ? 0f : 1f; _march.SetLump(_lumpAmount); }
            if (_kb[Key.K].wasPressedThisFrame) { _filletAmount = _filletAmount > 0.5f ? 0f : 1f; _march.SetFillet(_filletAmount); }
            if (_kb[Key.V].wasPressedThisFrame)
            {
                _viewMode = _viewMode == SnowViewMode.TopDownRamp
                    ? SnowViewMode.Raymarch3D : SnowViewMode.TopDownRamp;
                ApplyViewMode();
            }
            if (_kb[Key.Comma].wasPressedThisFrame) { _rampMaxM = Mathf.Max(0.1f, _rampMaxM - 0.25f); _renderer.SetRampMax(_rampMaxM); }
            if (_kb[Key.Period].wasPressedThisFrame) { _rampMaxM += 0.25f; _renderer.SetRampMax(_rampMaxM); }
            if (_kb[Key.R].wasPressedThisFrame) { Teardown(); Build(); return; }

            // 눈덩이가 들고 있는 눈을 그 자리에 붓는다. B 는 이미 마처 디버그가 쓴다.
            if (_kb[Key.N].wasPressedThisFrame) ReleaseBall();

            // 블레이드 치수를 플레이 중에 바꾼다. 인스펙터가 없는 런타임에서도 실험할 수 있어야 한다.
            bool blade = false;
            if (_kb[Key.LeftBracket].wasPressedThisFrame)  { _bladeWidthM -= 0.25f; blade = true; }
            if (_kb[Key.RightBracket].wasPressedThisFrame) { _bladeWidthM += 0.25f; blade = true; }
            if (_kb[Key.Minus].wasPressedThisFrame)        { _bladeThicknessM -= 0.05f; blade = true; }
            if (_kb[Key.Equals].wasPressedThisFrame)       { _bladeThicknessM += 0.05f; blade = true; }
            if (_kb[Key.Semicolon].wasPressedThisFrame)    { _bladeOffsetM -= 0.2f; blade = true; }
            if (_kb[Key.Quote].wasPressedThisFrame)        { _bladeOffsetM += 0.2f; blade = true; }

            // 각도. Q/E 로 5도씩, 1/2/3 은 v7 의 좌 / 직 / 우 프리셋.
            if (_kb[Key.Q].wasPressedThisFrame)      { _bladeAngleDeg -= 5f; blade = true; }
            if (_kb[Key.E].wasPressedThisFrame)      { _bladeAngleDeg += 5f; blade = true; }
            if (_kb[Key.Digit1].wasPressedThisFrame) { _bladeAngleDeg = -30f; blade = true; }
            if (_kb[Key.Digit2].wasPressedThisFrame) { _bladeAngleDeg = 0f;   blade = true; }
            if (_kb[Key.Digit3].wasPressedThisFrame) { _bladeAngleDeg = 30f;  blade = true; }

            // 블레이드 종류와 눈 물성 순환.
            if (_kb[Key.F].wasPressedThisFrame)
            {
                _bladeProfile = (SnowBladeProfileKind)(((int)_bladeProfile + 1) % 4);
                blade = true;
            }
            if (_kb[Key.M].wasPressedThisFrame)
            {
                _materialPreset = (SnowMaterialPreset)(((int)_materialPreset + 1) % 5);
                blade = true;
            }

            // 출력. 계측할 때 같은 속도를 재현하려면 손이 아니라 노브여야 한다.
            if (_kb[Key.Digit9].wasPressedThisFrame) { _enginePower01 -= 0.05f; blade = true; }
            if (_kb[Key.Digit0].wasPressedThisFrame) { _enginePower01 += 0.05f; blade = true; }

            if (blade) ApplyBladeSize();

            if (_kb[Key.G].wasPressedThisFrame) { _autoDriveCourse = !_autoDriveCourse; _courseT = 0f; }
            if (_autoDriveCourse)
            {
                _courseT += Time.deltaTime;
                throttle = 1f;
                // 직진 4s -> 우선회 3s -> 직진 4s -> 좌선회 3s. 14초 주기.
                float u = _courseT % 14f;
                steer = u < 4f ? 0f : (u < 7f ? 1f : (u < 11f ? 0f : -1f));
            }

            _input = new SnowVehicleInput { Throttle = throttle, Steer = steer, BladeDown = _bladeDown };
        }

#if UNITY_EDITOR
        /// <summary>플레이 중 인스펙터로 슬라이더를 끌면 바로 반영된다.</summary>
        private void OnValidate()
        {
            if (!Application.isPlaying || _sim == null) return;
            ApplyBladeSize();
            _march?.SetDebug(_marchDebug);
            _march?.SetLump(_lumpAmount);
            _march?.SetFillet(_filletAmount);
            ApplyViewMode();
        }
#endif

        private void FixedUpdate()
        {
            if (_paused) return;

            // Time 을 읽는 것은 여기까지다. 시뮬은 dt 를 인자로만 받는다.
            float dt = Time.fixedDeltaTime;
            _vehicle.Integrate(_input, dt, _field);

            _watch.Restart();
            _stats = _sim.Step(new SnowPlowStepInput
            {
                Prev = _vehicle.PrevBladePose,
                Now = _vehicle.BladePose,
                BladeDown = _vehicle.BladeDown,
                SignedSpeedMps = _vehicle.SpeedMps,
                DtSeconds = dt
            });
            _watch.Stop();

            // <b>시뮬 스텝 뒤에 걷는다.</b> Step 이 첫 줄에서 BeginStep 으로 변경 목록을 비우므로,
            // 먼저 걷으면 그 청크가 목록에서 지워지고 굽기가 한 프레임 늦는다 — 멀티 쪽
            // SnowCpuStage.StepBalls 와 같은 순서다.
            StepBall(dt);
            _simMs = _watch.Elapsed.TotalMilliseconds;
            _changedAvg += (_sim.ChangedChunks.Count - _changedAvg) * 0.05;
            _simMsSmoothed += (_simMs - _simMsSmoothed) * 0.1;
        }

        private void LateUpdate()
        {
            _frameMsSmoothed += (Time.unscaledDeltaTime * 1000.0 - _frameMsSmoothed) * 0.05;

            // GPU 프레임 시간. 레이마칭 비용은 CPU 계측으로는 안 보인다.
            FrameTimingManager.CaptureFrameTimings();
            if (FrameTimingManager.GetLatestTimings(1, _frameTimings) > 0)
            {
                double g = _frameTimings[0].gpuFrameTime;
                if (g > 0.0) _gpuMsSmoothed += (g - _gpuMsSmoothed) * 0.05;
            }
            if (--_perfLogCountdown <= 0)
            {
                _perfLogCountdown = 180;
                Debug.Log($"[SnowPerf] frame {_frameMsSmoothed:0.00} ms ({1000.0 / System.Math.Max(_frameMsSmoothed, 0.01):0} fps) | " +
                          $"gpu {_gpuMsSmoothed:0.00} | march {(_marchEnabled ? "ON" : "OFF")} | " +
                          $"sim {_simMsSmoothed:0.00} | bake {_bakeMsSmoothed:0.00} | upload {_uploadMsSmoothed:0.00} | " +
                          $"active {_stats.ActiveChunks} chunks / {_stats.CellsVisited:N0} cells x{_stats.RelaxIterations} | " +
                          $"changed {_changedAvg:0.0} chunks ({_changedAvg * 4:0} net blocks, {_changedAvg * 4 * 64:0} B/step raw) | " +
                          $"view {_viewMode}");
            }

            if (_viewMode == SnowViewMode.TopDownRamp)
            {
                _renderer.Upload();
            }
            else
            {
                // 활동한 청크만 다시 굽는다. 전부 굽는 것은 1M 셀 전수 스캔이라 dirty 청크
                // 설계를 통째로 무효화한다 - 상한 텍스처가 렌더링에 붙는 유일한 추가 비용이고,
                // 그 비용이 시뮬과 같은 집합에 비례해야 한다.
                // 순서가 중요하다. 상한이 로브를 읽으므로 로브가 먼저다.
                _watch2.Restart();
                // 활성 집합이 아니라 <b>실제로 바뀐 청크</b>다. 활성 반경은 대부분 손대지 않은
                // 처녀설이고, 그걸 매 프레임 다시 굽는 것이 실측 34 ms 의 정체였다.
                _lump.RebuildChunks(_sim.ChangedChunks);
                _coarse.RebuildChunks(_sim.ChangedChunks);
                _watch2.Stop();
                _bakeMs = _watch2.Elapsed.TotalMilliseconds;
                _bakeMsSmoothed += (_bakeMs - _bakeMsSmoothed) * 0.1;

                _watch2.Restart();
                _march.UploadAll();
                _watch2.Stop();
                _uploadMs = _watch2.Elapsed.TotalMilliseconds;
                _uploadMsSmoothed += (_uploadMs - _uploadMsSmoothed) * 0.1;
                _rig.Sync(_vehicle, _sim.Shape, RideHeightM());
                UpdateChaseCamera();
            }

            _body.transform.SetPositionAndRotation(
                new Vector3(_vehicle.PosX, 0.05f, _vehicle.PosZ),
                Quaternion.Euler(90f, _vehicle.HeadingDeg, 0f));

            var p = _vehicle.BladePose;
            _blade.transform.SetPositionAndRotation(
                new Vector3(p.CenterX, 0.06f, p.CenterZ),
                Quaternion.Euler(90f, _vehicle.HeadingDeg + _vehicle.BladeAngleDeg, 0f));
            var bladeColor = _vehicle.BladeDown
                ? new Color(0.95f, 0.95f, 0.35f)
                : new Color(0.45f, 0.45f, 0.50f);
            if (_overlayBlade.HasProperty(BaseColorId)) _overlayBlade.SetColor(BaseColorId, bladeColor);
            _overlayBlade.color = bladeColor;
        }

        /// <summary>차량이 눈 위에 얹히는 높이. 지나온 자리와 처녀설 사이에서 오르내린다.</summary>
        private float RideHeightM()
        {
            if (!_field.Geo.TryWorldToCell(_vehicle.PosX, _vehicle.PosZ, out int cx, out int cz)) return 0f;
            return _field.Get(cx, cz) * 1e-3f;
        }

        /// <summary>PPack 실제 카메라대로 높이 2.5~3.5 m · 거리 5.5~8 m. v7 이 가장 비싸다고 잰 프레이밍이다.</summary>
        private void UpdateChaseCamera()
        {
            float rad = _vehicle.HeadingDeg * Mathf.Deg2Rad;
            var fwd = new Vector3(Mathf.Sin(rad), 0f, Mathf.Cos(rad));
            var target = new Vector3(_vehicle.PosX, 0f, _vehicle.PosZ);
            var want = target - fwd * 7.5f + Vector3.up * 3.4f;
            var t = _camera.transform;
            t.position = Vector3.Lerp(t.position, want, 1f - Mathf.Exp(-6f * Time.deltaTime));
            t.rotation = Quaternion.LookRotation((target + fwd * 3f + Vector3.up * 0.4f) - t.position, Vector3.up);
        }

        private void OnGUI()
        {
            if (!_showHud || _field == null) return;

            // <b>공이 든 눈은 필드에서 빠져 있다.</b> 그 항을 빼놓으면 공을 밀기 시작한 순간부터
            // 계측기가 거짓 누출을 외친다 — 불변량은 `필드 + 공 = 초기` 다.
            long held = _ball?.MassMm ?? 0L;
            long conservation = _field.TotalHeightMm + held - _initialTotalMm;
            var style = new GUIStyle(GUI.skin.label) { fontSize = 13, richText = true };

            string Bad(string label, long v) => v == 0
                ? $"{label}<b>0</b>"
                : $"<color=#ff5555>{label}<b>{v:N0}</b>  ← 그 자리에 버그가 있다</color>";

            string text =
                $"<b>SnowHeightSimCpu</b>  [{_viewMode} <color=#8899bb>V</color>]{(_autoDriveCourse ? "  <color=#66cc88>AUTO</color>" : "")}   W/S 가감속 · A/D 조향 · Space 블레이드 · , . 램프 · P 정지 · R 리셋 · H HUD\n" +
                $"블레이드     폭 <b>{_bladeWidthM:0.00} m</b> ({_bladeWidthM / SnowFieldGeometry.CellSizeM:0.0} 셀)  " +
                $"두께 <b>{_bladeThicknessM:0.00} m</b>  오프셋 {_bladeOffsetM:0.0} m  " +
                $"각도 <b>{_bladeAngleDeg:+0;-0;0}°</b> {(_bladeAngleDeg < -0.5f ? "좌향" : _bladeAngleDeg > 0.5f ? "우향" : "직각")}   세그먼트 {_stats.Segments}\n" +
                $"<color=#8899bb>            [ ] 폭 · - = 두께 · ; ' 오프셋 · Q E 각도 · 1 2 3 프리셋 · F 종류</color>\n" +
                $"형상        <b>{_bladeProfile}</b>" +
                (_bladeProfile == SnowBladeProfileKind.Straight ? "" : $"  날개 {_wingLengthM:0.00} m @ {_wingAngleDeg:0}°") +
                $"      눈 <b>{_materialPreset}</b> <color=#8899bb>(M)</color>  " +
                $"안식각 {_sim.Material.ReposeAngleDeg:0}° · 응집 {_sim.Material.CohesionMm} mm · " +
                $"상한 {(_sim.Material.MaxPileHeightMm == 0 ? "없음" : _sim.Material.MaxPileHeightMm + " mm")} · " +
                $"퍼짐 {_sim.Material.DepositSpreadRings} · 저항 {_sim.Material.DragPerMetre:0.0}\n" +
                $"맵          {_field.Geo.ResX} x {_field.Geo.ResZ} 셀 · {_field.Geo.ChunksX} x {_field.Geo.ChunksZ} 청크 · 셀 {SnowFieldGeometry.CellSizeM} m\n" +
                $"활성 청크    {_stats.ActiveChunks} / {SnowPlowStepCpu.ActiveChunkCap}" +
                (_stats.DroppedByCap > 0 ? $"  <color=#ffaa44>(상한에 걸려 버림 {_stats.DroppedByCap})</color>" : "") + "\n" +
                $"처리 셀      {_stats.CellsVisited:N0}   relax x{_stats.RelaxIterations} = {_stats.CellsVisited * _stats.RelaxIterations:N0} 방문\n" +
                $"프레임      <b>{_frameMsSmoothed:0.00} ms</b> ({1000.0 / System.Math.Max(_frameMsSmoothed, 0.01):0} fps)   " +
                $"시뮬 {_simMsSmoothed:0.00}  굽기 {_bakeMsSmoothed:0.00}  업로드 {_uploadMsSmoothed:0.00}\n" +
                $"속도        {_vehicle.SpeedMps:0.0} / {_vehicle.TopSpeedMps:0.0} m/s   출력 <b>{_enginePower01 * 100f:0}%</b> <color=#8899bb>(9 0)</color>   " +
                $"앞 눈깊이 {_vehicle.SnowDepthAheadM * 100f:0} cm   블레이드 {(_vehicle.BladeDown ? "DOWN" : "UP")}\n" +
                $"총 부피      {_field.TotalVolumeM3:N2} m3   램프 상한 {_rampMaxM:0.00} m\n" +
                (_ball == null ? "" :
                    $"눈덩이      지름 <b>{_ball.DiameterM:0.00} m</b>  든 눈 {held:N0} mm ({_ball.VolumeM3 * 1000.0:N0} L)" +
                    $"   <color=#8899bb>Q 터뜨리기</color>" +
                    $"   <color=#8899bb>차로 밀어서 굴린다 · N 놓기</color>\n") +
                Bad("보존 오차    ", conservation) + "\n" +
                Bad("미배치      ", _stats.UnplacedMm) + "\n" +
                Bad("클램프 손실  ", _stats.ClampedMm);

            GUI.Box(new Rect(8, 8, 860, 256), GUIContent.none);
            GUI.Label(new Rect(18, 14, 840, 246), text, style);
        }

        private void Teardown()
        {
            _renderer?.Dispose();
            _march?.Dispose();
            _rig?.Dispose();
            if (_sun != null) Destroy(_sun.gameObject);
            if (_camera != null) Destroy(_camera.gameObject);
            if (_body != null) Destroy(_body);
            if (_blade != null) Destroy(_blade);
            if (_overlayBody != null) Destroy(_overlayBody);
            if (_overlayBlade != null) Destroy(_overlayBlade);
            if (_ballView != null) Destroy(_ballView);
            _ball = null;
            _ballView = null;
        }

        private void OnDestroy() => Teardown();
    }
}
