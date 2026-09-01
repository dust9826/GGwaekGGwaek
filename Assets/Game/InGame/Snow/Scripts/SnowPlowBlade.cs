using System;
using UnityEngine;

namespace PPack
{
    /// <summary>블레이드 각도. 좌·정면·우 셋뿐이고 연속값이 아니다 — 동사는 세 개다.</summary>
    public enum EBladeAngle
    {
        Left = -1,
        Straight = 0,
        Right = 1,
    }

    /// <summary>
    /// 제설날. <b>세 동사</b>(날 내림/올림, 날 각도 좌/정면/우, 전진/후진)를 <see cref="SnowField"/> 의
    /// 1바이트 격자 위의 더미로 바꾼다.
    ///
    /// <b>덤프 버튼이 없다.</b> 날을 든 상태로 지고 다닐 수 없다는 것이 결정이지 부작용이 아니다 —
    /// 날의 판이 1.8톤을 받치는 유일한 물건이므로 들린 판은 그것을 지고 있을 수 없다. 후진도 같은
    /// 물리적 진술이다. 그래서 <b>내려놓기는 별 동작이 아니라 영수증의 퇴역</b>이고, 필드를 건드리지
    /// 않으므로 보존이 논증되는 대신 <b>자명</b>하다(<see cref="SnowPlowLedger.RetireReceipt"/>).
    ///
    /// <b>권위는 CPU 에 있다.</b> <c>RenderTexture</c>·컴퓨트 셰이더·<c>AsyncGPUReadback</c> 을 하나도
    /// 쓰지 않는다. 루트 <c>AGENTS.md</c> 가 Photon Fusion 2 를 처음부터 Server Mode(데디)로 못
    /// 박았고, 데디 서버는 <c>-batchmode -nographics</c> 라 <b>그래픽 디바이스가 아예 없다.</b>
    /// AnyTest v7 은 권위를 GPU 버퍼와 텍스처에 두었으므로 그대로 옮길 수 없었고, 더미·원장·자르기·
    /// 정착·안식각 relax 를 전부 CPU 정수 산술로 다시 썼다. 여기서 GPU 를 쓰는 것은
    /// <see cref="SnowSurfaceRenderer"/> 의 표현뿐이고 그것은 헤드리스에서 스스로 꺼진다.
    ///
    /// <b>스텝의 순서가 설계다</b>(v7 의 여섯 페이즈와 같은 이유로 같은 순서다):
    /// <code>
    ///   1 지우기/퇴역   영수증을 읽어 힙을 필드에서 걷고 원장으로 되돌린다. 무조건 돈다
    ///   2 자르기        날 앞의 지형을 깎아 conserved 를 원장에, 나머지를 장부에 적는다
    ///   3 소반          예약된 소반 몫을 소반 폭 밖에 놓는다
    ///   4 방출          힙을 새 포즈에 다시 놓고(영수증), 넘친 몫을 측면 벽으로 흘린다(영수증 없음)
    ///   5 relax         활동 창 안에서만 안식각 정착
    ///   6 반작용        각진 날의 횡력과 요 모멘트, 그리고 계측 발행
    /// </code>
    /// 1 이 2 보다 <b>먼저</b>여야 한다 — 뒤집으면 지우기와 자르기가 같은 질량을 원장에 두 번
    /// 넣고 불변식이 그 즉시 LEAK 를 찍는다. 좋은 실패지만 도달 가능해서는 안 된다.
    /// 4 가 5 보다 <b>먼저</b>여야 한다 — relax 가 힙을 봐야 벽과 소반을 그 주변에 정착시킬 수 있고,
    /// 가드가 읽는 영수증이 이번 스텝의 것이어야 한다.
    ///
    /// ⚠ <b><see cref="SnowVehiclePad"/> 와 같이 켜 두면 안 된다.</b> 둘 다 자른다. 패드의
    /// <see cref="SnowField.ApplyStamp"/> 는 사각형에 <b>균일한</b> 델타를 쓰고 총량만 돌려주므로
    /// 셀당 영수증을 만들 수 없다 — 그래서 이 컴포넌트가 자르기를 직접 갖는다.
    /// </summary>
    public sealed class SnowPlowBlade : MonoBehaviour
    {
        [Header("접점")]
        [SerializeField] private SnowStage _stage;
        [Tooltip("횡력·요 모멘트를 받을 몸. 없으면 반작용을 계산해 발행하기만 하고 물리에 넣지 않는다.")]
        [SerializeField] private Rigidbody _body;

        // ------------------------------------------------------------------ 날
        [Header("날 (m)")]
        [Tooltip("치우는 폭. **상수다.** 더미가 자라도 이 값은 안 자란다 — 넘친 눈은 이 폭 " +
                 "밖으로 벽이 되어 나가고, 그것이 소반이 조용히 넓어지지 않는 이유다.\n\n" +
                 "차폭(1.80m)보다 넓어야 한다: 같으면 자국이 차체에 완전히 가려 뒤에서 안 보인다" +
                 "(이 폴더의 2026-08-14 실측).")]
        [SerializeField, Range(0.3f, 8f)] private float _bladeWidthM = 2.3f;

        [Tooltip("날의 진행 방향 두께. 정지한 날이 덮는 발자국이고, 한 스텝의 스윕은 여기에 " +
                 "이동거리를 더한 것이다. 50Hz·8m/s 에서 이동거리는 0.16m 라 12.5cm 셀 두 칸이다 — " +
                 "그래서 v7 처럼 스윕을 여러 조각으로 쪼갤 필요가 없다.")]
        [SerializeField, Range(0.05f, 2f)] private float _bladeThicknessM = 0.35f;

        [Tooltip("차량 원점에서 날 선까지의 거리. 감속 샘플 지점(z=+2.35)보다 앞일 필요는 없다 — " +
                 "감속은 아직 안 치운 눈을 재고 이쪽은 실제로 자른다.")]
        [SerializeField, Range(0f, 4f)] private float _bladeAheadM = 1.1f;

        [Tooltip("좌/우로 꺾었을 때의 각도(도). 날의 자체 프레임이 이만큼 돌고, 원장의 일부가 " +
                 "배출단으로 옮겨간다.")]
        [SerializeField, Range(5f, 60f)] private float _bladeAngleDeg = 30f;

        // ------------------------------------------------------------------ 더미
        [Header("더미 — 모양. 폭 노브는 일부러 없다")]
        [Tooltip("정상 높이의 상한(cm). **cm 다** — 권위가 셀당 1바이트 cm 이므로 m 로 두면 " +
                 "표현할 수 없는 값을 넣을 수 있다.\n\n" +
                 "⚠ 실효 상한은 SnowStage 의 최대 깊이에서 **그 자리의 눈 깊이를 뺀 값**이다. " +
                 "최대 30cm·시작 30cm 로 두면 여유가 0 이라 더미가 설 자리가 없고, 날은 처음부터 " +
                 "벽만 쌓는다. v7 같은 축적 구간을 원하면 SnowStage 의 최대 깊이를 시작 깊이 + " +
                 "이 값 위로 올려야 하고, 1바이트의 천장이 255cm 다.")]
        [SerializeField, Range(5, 250)] private int _heapPeakCm = 120;

        [Tooltip("앞면 각도(도). **안식각보다 급한 것이 의도다** — 날에 능동적으로 압축되고 있으니 " +
                 "가라앉은 더미보다 서 있다. 55° 안식각에 65° 면 눈에 보이게 밀어붙인 얼굴이고, " +
                 "안식각과 같게 두면 급함이 무엇을 사는지 보여주는 A/B 가 된다.\n\n" +
                 "relax 가 몇 프레임에 이것을 안식각으로 되돌리므로 아래의 가드가 필요하다.")]
        [SerializeField, Range(20f, 85f)] private float _heapFrontAngleDeg = 65f;

        [Tooltip("뒷면 각도(도) — 날 쪽. 앞면보다 급하다: 판이 물리적으로 받치고 있다.")]
        [SerializeField, Range(20f, 88f)] private float _heapBackAngleDeg = 75f;

        [Tooltip("정상이 날 선보다 얼마나 **앞**에 있는지(m). 미학이 아니라 하중을 받는 값이다 — " +
                 "뒷면의 발끝이 날 선보다 뒤로 넘어가면 그 부분은 영수증 없이 맨땅에 절벽으로 서고, " +
                 "relax 가 그것을 소반으로 끌어내려 지우기의 min() 이 영구히 차감한다. 더미가 " +
                 "뒤로 새는 형태다. 제약은 crestAhead > peak/tan(back) - thickness/2 다.")]
        [SerializeField, Range(0.05f, 3f)] private float _heapCrestAheadM = 0.75f;

        [Tooltip("영수증이 걸린 셀끼리의 짝에만 쓰는 relax 기울기 한계(도). 두 면 중 급한 쪽 이상이어야 " +
                 "relax 가 면을 먹지 않는다.")]
        [SerializeField, Range(10f, 89f)] private float _heapGuardAngleDeg = 80f;

        [Tooltip("가드를 켠다. **끄는 것이 A/B 이고 한 번은 볼 만하다**: 끄면 relax 가 앞면을 " +
                 "몇 프레임에 안식각으로 무너뜨리고, 지우기의 min() 이 그 차이를 차감해 더미가 " +
                 "눈에 보이게 축적을 거부한다. 이 스위치가 존재하는 이유가 그 실패를 보이게 하는 것이다.")]
        [SerializeField] private bool _heapRelaxGuard = true;

        // ------------------------------------------------------------------ 방출
        [Header("방출 — 뒤에 남기는 벽. 문턱이 아니라 비율이다")]
        [Tooltip("눈이 날 끝에서 말려 나가기 시작하는 채움 비율. 실제 제설차는 연속으로 흘리므로 " +
                 "문턱이 아니라 여기서부터 용량까지 매끄럽게 오른다.")]
        [SerializeField, Range(0f, 1f)] private float _releaseStartFill = 0.35f;

        [Tooltip("용량에서 초당 방출되는 원장의 비율. 절대 유량이 아니라 비율인 이유는 가득 찬 날이 " +
                 "실제로 더 많이 흘리기 때문이다. **이 노브가 평형 채움을 정한다.**\n\n" +
                 "용량 위의 hard 항은 이 노브가 아니고 끌 수 없다 — 넘친 만큼을 정확히 방출하므로 " +
                 "이 값을 0 으로 둬도 더미는 상한에 고정된다.")]
        [SerializeField, Range(0f, 3f)] private float _releaseRatePerSec = 0.15f;

        [Tooltip("방출 원뿔의 중심이 날 반폭에서 얼마나 **밖**인지(m). 반지름보다 커야 원뿔의 " +
                 "안쪽 절반이 치운 소반 안으로 떨어지지 않는다.")]
        [SerializeField, Range(0f, 4f)] private float _spillOutM = 0.95f;
        [Tooltip("방출 원뿔이 날 선보다 얼마나 뒤인지(m).")]
        [SerializeField, Range(0f, 4f)] private float _spillBackM = 0.45f;
        [Tooltip("방출 원뿔의 반지름(m). 작다 — 방출은 사건이 아니라 매 스텝의 연속적인 흘림이라 " +
                 "벽은 이동으로 쌓인다.")]
        [SerializeField, Range(0.15f, 4f)] private float _spillRadiusM = 0.85f;

        // ------------------------------------------------------------------ 자르기의 장부
        [Header("자르기와 장부")]
        [Tooltip("한 번 지날 때 깎는 최대 깊이(cm). 전체 깊이를 다 깎지 않는 이유는 서 있는 더미로 " +
                 "다시 밀고 들어갈 때 그것을 한 프레임에 전부 원장에 올리지 않기 위해서다 — " +
                 "시각적 팝이고 조작감의 계단이다.")]
        [SerializeField, Range(1, 250)] private int _cutCmPerPass = 60;

        [Tooltip("깎은 양 중 **원장에 닿는** 비율(천분율). 나머지는 의도적 손실이다.\n\n" +
                 "손실이 필요한 이유는 사치가 아니다: 더미는 초당 50번 필드에서 들어올려져 다시 " +
                 "놓이므로, 손실을 원장이나 총 제거량에 물리면 더미의 60% 가 초당 50번 파괴되어 " +
                 "애초에 쌓이지 못한다. **더미는 다시 깎이는 것이 아니라 다시 놓이는 것이고**, " +
                 "새 지형만 통행료를 낸다.")]
        [SerializeField, Range(50, 1000)] private int _conservedPermille = 400;

        [Tooltip("손실 중 소반으로 짜여 나가는 비율(천분율). 0 이면 소반 경계가 깨끗하고, " +
                 "1000 이면 전부 옆에 쌓인다. **원장이 아니라 손실에서 떼는 것이 의도다** — " +
                 "그래서 소반은 더미의 성장에 아무 비용도 물리지 않는다.")]
        [SerializeField, Range(0, 1000)] private int _bermPermille = 350;

        [SerializeField, Range(0f, 4f)] private float _bermOutM = 1.25f;
        [SerializeField, Range(0f, 4f)] private float _bermBackM = 0.9f;
        [SerializeField, Range(0.15f, 4f)] private float _bermRadiusM = 0.55f;

        // ------------------------------------------------------------------ 동사
        [Header("세 동사")]
        [Tooltip("날의 판이 더미를 받치고 있다고 볼 최소 전진 속도(m/s). 붙는 데는 이 값, 떨어지는 " +
                 "데는 절반을 쓴다 — 2:1 이력이 없으면 문턱에서 지고 있음과 내려놓음이 떨린다.")]
        [SerializeField, Range(0.05f, 4f)] private float _attachSpeedMps = 0.6f;

        [Tooltip("앞면이 '완전히 밀어붙인' 상태로 읽히는 전진 속도(m/s). 이 속도에서 앞면이 " +
                 "Heap Front Angle 이고, 멈추면 안식각으로 가라앉는다.")]
        [SerializeField, Range(0.5f, 20f)] private float _pushFullSpeedMps = 4f;

        [Tooltip("앞면 각도가 요구값을 따라가는 속도(초당 도). 0 이면 밀어붙인 각도에 고정되고 " +
                 "그것이 A/B 다.")]
        [SerializeField, Range(0f, 180f)] private float _faceRelaxDegPerSec = 25f;

        [Tooltip("다져진 눈의 밀도(kg/m³). 300 은 바람·기계로 다져진 눈이고, 갓 내린 가루가 50~100, " +
                 "빙하 얼음이 900 이다. **부피를 조작감이 읽는 질량으로 바꾸는 유일한 값**이다.")]
        [SerializeField, Range(50f, 900f)] private float _snowDensityKgPerM3 = 300f;

        // ------------------------------------------------------------------ 캐스팅 반작용
        [Header("각진 날의 반작용")]
        [Tooltip("캐스팅 이득. 눈이 날 면을 따라 speed·sin(angle) 로 일해 나가므로 " +
                 "bladeWidth/(speed·sin) 초 동안 날 위에 머문다 — 한 스텝에 떠나는 원장의 비율이 " +
                 "|speed|·sin(angle)·efficiency·dt / bladeWidth 다. 잔류시간 논증이고 눈금맞춤이 아니다.")]
        [SerializeField, Range(0f, 3f)] private float _castEfficiency = 1f;

        [Tooltip("캐스팅 유량 1 m³/s 당 차량에 걸리는 횡가속(m/s²). **한쪽으로 던지면 차는 " +
                 "반대로 밀린다.** 깊은 눈에서 커지고 치운 길에서 사라지므로 플레이어가 배울 수 있다.")]
        [SerializeField, Range(0f, 4f)] private float _castPushMps2PerM3s = 0.6f;

        [Tooltip("캐스팅 유량 1 m³/s 당의 요 각속도(초당 도). **너무 크면 플레이어의 조향과 싸운다** — " +
                 "v7 은 2.08 m³/s 에서 8.3 deg/s 를 냈고 그것이 1,456kg 을 실은 차에 남은 조향 " +
                 "권한 25.7 deg/s 의 32% 였다. 그 이상은 좁은 통로에서 의도한 라인을 잡아먹는다.\n\n" +
                 "⚠ 이 프로젝트의 VehicleController 는 매 스텝 angularVelocity 를 절대각 오차로 " +
                 "덮어쓰므로 요 모멘트는 한 스텝 안에 되돌려진다. 값은 발행하고 물리에는 " +
                 "AddForceAtPosition 의 모멘트로만 들어간다 — 자세한 내용은 클래스 주석 아래.")]
        [SerializeField, Range(0f, 20f)] private float _castYawDegPerM3s = 4f;

        // ------------------------------------------------------------------ relax
        [Header("relax (안식각)")]
        [SerializeField, Range(0, 8)] private int _relaxIterations = 4;

        [Tooltip("안식각(도). 손대지 않은 슬래브는 평평해서 기울기가 0 이라 이 커널이 어떤 각도로도 " +
                 "무너뜨릴 수 없다. 이것이 정하는 것은 **소반 벽·측면 벽·내려놓은 더미**가 어떻게 " +
                 "서는가이고, 동시에 더미의 **측면 각도**라 주어진 높이에서 옆으로 얼마나 퍼지는지도 정한다.")]
        [SerializeField, Range(10f, 80f)] private float _reposeAngleDeg = 55f;

        [Tooltip("짝마다 초과 기울기의 몇 분율을 옮길지. 0.24 를 넘기지 않는다.\n\n" +
                 "⚠ 1cm 양자 때문에 **1cm 사역대**가 있다: 초과가 2cm 미만이면 흐르지 않는다. " +
                 "12.5cm 셀에서 안식각 위로 4.6° 의 여유이고, 이 값을 올려도 사역대는 안 줄어든다.")]
        [SerializeField, Range(0.01f, 0.24f)] private float _relaxRate = 0.22f;

        [Tooltip("차량이 떠난 뒤에도 그 발자국을 계속 정착시키는 시간(초). **내려놓은 더미가 이 " +
                 "변종에서 가장 높고 가장 오래 걸린다** — 앞면이 밀어붙인 각도로 서 있고 영수증이 " +
                 "없어서 가드가 더 지켜주지 않으므로 10° 의 초과를 털어야 한다. 창이 먼저 만료되면 " +
                 "무너지다 멎고, 그것은 내려놓기의 버그처럼 보인다.")]
        [SerializeField, Range(0.05f, 8f)] private float _relaxTrailSeconds = 2.5f;

        [SerializeField, Range(0, 64)] private int _relaxPadCells = 6;

        [Tooltip("한 스텝에 relax 가 도는 셀 수의 상한. 넘으면 창을 중심에서 자르고 " +
                 "Relax Window Clipped 가 참이 된다 — 잃는 것이 아니라 미뤄지는 것이다.")]
        [SerializeField, Min(0)] private int _relaxMaxWindowCells = 40000;

        // ------------------------------------------------------------------ 불변식 계기
        [Header("질량 불변식 계기")]
        [Tooltip("절대 부분(L). 1바이트 정수 격자에서 잔차는 **정확히 0** 이므로 이 톨러런스는 " +
                 "산술 잡음이 아니라 **결함**을 잡기 위한 것이다.")]
        [SerializeField, Range(0.05f, 50f)] private float _massToleranceL = 3f;

        [Tooltip("비례 부분(초기 부피의 ppm). v7 은 3,960,000 L 필드에 절대 3L(0.76ppm)를 걸었다가 " +
                 "**계기가 자기 산술에 대고 늑대를 외쳤고**, 그것을 고친 것이 이 항이다. 여기서는 " +
                 "잔차가 0 이라 필요하지 않지만, 격자가 커져도 문턱이 같이 크는 성질은 유지한다.")]
        [SerializeField, Range(0f, 50f)] private float _massTolerancePpm = 2f;

        // ------------------------------------------------------------------ 상태
        private SnowPlowLedger _ledger;
        private readonly SnowRepose _repose = new SnowRepose();
        private SnowSurfaceRenderer _renderer;

        private bool _bladeDown = true;
        private EBladeAngle _angle = EBladeAngle.Straight;
        private bool _attached;
        private float _faceAngleDeg = 65f;
        private float _push01;
        private float _clock;
        private bool _checkedStartup;

        private float _pileHeightM;
        private float _pileCapacityM3;
        private float _pileFootprintWidthM;
        private float _pileSupportWidthM;
        private float _castRateM3PerSec;
        private float _castPushMps2;
        private float _castYawDegPerSec;
        private float _releaseFrac;
        private float _heapFrac;
        private float _depositVolumeM3;
        private int _depositCount;
        private float _forwardSpeedMps;

        // 스탬프 주체 id. SnowVehiclePad 의 카운터와 겹치지 않게 다른 대역에서 뽑는다 —
        // 같은 (Tick, stampId) 는 조용히 버려지므로 겹치면 한쪽의 자르기가 사라진다.
        private const int StampIdBase = 1000;
        private static int _nextStampId;
        private int _stampId;

        // ------------------------------------------------------------------ 동사, 공개
        //
        // 서버 전용으로 쓸 수 있게 **입력이 아니라 상태**로 노출한다. 로컬 플레이어·입력 장치·
        // 카메라·UI 가 있다고 가정하지 않는다(루트 AGENTS.md). SnowPlowInput 이 이것을 부른다.

        public bool BladeDown => _bladeDown;
        public EBladeAngle Angle => _angle;

        /// <summary>날의 판이 더미를 받치고 있는가. 날이 내려가 있고 <b>앞면으로 밀고 있을 때</b>만 참.</summary>
        public bool BladeAttached => _attached;

        public void SetBladeDown(bool down) => _bladeDown = down;
        public void ToggleBlade() => _bladeDown = !_bladeDown;
        public void SetAngle(EBladeAngle angle) => _angle = angle;

        // ------------------------------------------------------------------ 계측, 공개

        /// <summary>이번 스텝에 <b>실제로</b> 제거된 총량(cm·셀). 연출은 이 값을 구독한다.</summary>
        public int LastRemovedCm { get; private set; }

        /// <summary>이번 스텝에 자른 패드. 연출이 방출 위치·방향을 여기서 읽는다.</summary>
        public SnowStampArea LastArea { get; private set; }

        /// <summary>실제로 눈이 제거된 영역. HUD/VFX 는 권위 격자를 다시 읽지 않고 이 이벤트만 따른다.</summary>
        public event Action<SnowStampArea> SnowCleared;

        public float CarriedLitres => _ledger?.CarriedLitres ?? 0f;

        /// <summary>더미의 부피(m³). <b>필드에 실제로 쓴 것의 합</b>이라 화면과 어긋날 수 없다.</summary>
        public float PileVolumeM3 => _ledger?.PileVolumeM3 ?? 0f;

        public float PileHeightM => _pileHeightM;
        public float PileCapacityM3 => _pileCapacityM3;
        public float PileFill01 => PileVolumeM3 / Mathf.Max(1e-4f, _pileCapacityM3);

        /// <summary>
        /// 더미의 발자국 폭(m) = <b>정상 길이</b>. 부피와 무관하게 <b>날 폭에 고정</b>이고, 그것이
        /// "날 폭만큼 치운다"의 전부다(<see cref="SnowHeapShape"/>: 폭 노브를 두지 않는다).
        ///
        /// ⚠ 여기서 <b>지지 폭을 찍으면 안 된다.</b> 처음에 <c>날폭 + 2·높이/tan(안식각)</c> 을 찍었고,
        /// 그것은 우진각의 발끝까지 포함한 <b>다른 양</b>이라 높이에 따라 2.38 → 2.68m 로 움직였다.
        /// 계기가 상수여야 할 값을 변수로 찍으면 "폭이 자라고 있다"로 읽히고, 실제로 자라는 것은
        /// 우진각뿐이다. 지지 폭이 궁금하면 <see cref="PileSupportWidthM"/> 를 본다.
        ///
        /// 값은 <b>실제로 방출에 쓴 모양</b>에서 나온다 — 같은 식을 두 곳에 두지 않으므로 누군가
        /// 폭 노브를 만들면 이 계기가 그 즉시 움직인다.
        /// </summary>
        public float PileFootprintWidthM => _pileFootprintWidthM;

        /// <summary>
        /// 더미가 실제로 <b>덮는</b> 폭(m) — 정상 길이 + 양쪽 안식각 우진각. 높이에 따라 자란다.
        /// 측면이 안식각이므로 <see cref="SnowRepose"/> 가 여기서 옮길 초과 기울기를 못 찾는다.
        /// </summary>
        public float PileSupportWidthM => _pileSupportWidthM;

        /// <summary>지고 있는 질량(kg). 조작감이 이것을 읽는다.</summary>
        public float CarriedMassKg => PileVolumeM3 * _snowDensityKgPerM3;

        /// <summary>앞면의 살아 있는 각도(도). 밀 때 급해지고 멈추면 안식각으로 가라앉는다.</summary>
        public float FaceAngleDeg => _faceAngleDeg;

        /// <summary>세 번째 동사가 읽는 값 — 정면 방향 속도(m/s). 음수가 후진이다.</summary>
        public float ForwardSpeedMps => _forwardSpeedMps;

        public float CastRateM3PerSec => _castRateM3PerSec;
        public float CastPushMps2 => _castPushMps2;
        public float CastYawDegPerSec => _castYawDegPerSec;
        public float ReleaseFraction => _releaseFrac;
        public float HeapFraction => _heapFrac;

        /// <summary>내려놓기가 일어난 횟수와 그렇게 남긴 총 부피(m³).</summary>
        public int DepositCount => _depositCount;
        public float DepositVolumeM3 => _depositVolumeM3;

        public float InvariantErrorL => _ledger?.InvariantErrorL ?? 0f;
        public float DeletedLitres => _ledger?.DeletedLitres ?? 0f;
        public float UnplacedLitres => _ledger?.UnplacedLitres ?? 0f;
        public float UnplacedPeakLitres => _ledger?.UnplacedPeakLitres ?? 0f;

        /// <summary>누출 문턱(L) — <c>max(절대, 초기 부피의 ppm)</c>. 비례이지 절대만이 아니다.</summary>
        public float MassToleranceL => _ledger is null
            ? _massToleranceL
            : Mathf.Max(_massToleranceL, _massTolerancePpm * 1e-6f * _ledger.InitialLitres);

        public bool MassLeaking => Mathf.Abs(InvariantErrorL) > MassToleranceL;

        public int RelaxWindowCells => _repose.WindowCells;
        public int RelaxFlows => _repose.Flows;
        public bool RelaxWindowClipped => _repose.WindowClipped;

        /// <summary>
        /// 질량이 조작감에 먹이는 배율. <c>f(M) = 1 / (1 + k·M)</c>, <c>k</c> 는
        /// <c>f(refKg) == atRef</c> 가 되게 푼다.
        ///
        /// 편리해서가 아니라 <b>모양이 맞아서</b> 쓴다: 가속은 힘/질량이므로 질량에 반비례하고,
        /// 0 질량에서 정확히 1, 기준 질량에서 정확히 공표값, 그리고 <b>0 에 닿지 않으면서 계속
        /// 떨어진다</b> — 선형이면 어떤 질량에서 차가 후진한다.
        /// </summary>
        public static float LoadFactor(float massKg, float referenceKg, float factorAtReference)
        {
            float a = Mathf.Clamp(factorAtReference, 0.02f, 1f);
            float k = (1f / a - 1f) / Mathf.Max(1f, referenceKg);
            return 1f / (1f + k * Mathf.Max(0f, massKg));
        }

        // ------------------------------------------------------------------

        private void Awake()
        {
            if (_stage == null) _stage = FindAnyObjectByType<SnowStage>();
            if (_stage != null) _renderer = _stage.GetComponent<SnowSurfaceRenderer>();
            if (_body == null) _body = GetComponentInParent<Rigidbody>();

            _stampId = StampIdBase + ++_nextStampId;
            _faceAngleDeg = Mathf.Clamp(_heapFrontAngleDeg, 20f, 85f);
            _pileFootprintWidthM = _bladeWidthM;
            _pileSupportWidthM = _bladeWidthM;

            // 장부를 합성 수열로 한 번 돌려 보고 한 줄로 찍는다. **몰기 전에** 판정이 나와야 한다 —
            // 계기가 LEAK 를 외쳤을 때 "커널이 새는가 / 기준선이 이미 망가졌는가"를 사람이 주행으로
            // 갈라내야 했던 것이 이 폴더가 실제로 물린 지점이다.
            if (SnowPlowLedger.SelfCheck(out string report)) Debug.Log("[Snow] " + report, this);
            else Debug.LogError("[Snow] " + report + " ← 커널 결함이다. 씬 배선이 아니다", this);

            DisableForeignCutters();
        }

        /// <summary>
        /// 같은 권위 격자를 <b>영수증 없이</b> 자르는 컴포넌트를 끈다. 경고가 아니라 <b>차단</b>이다.
        ///
        /// <b>왜 경고로는 안 되는가 (2026-08-17 실측).</b> 여기 있던 검사는
        /// <c>GetComponentInParent&lt;SnowVehiclePad&gt;()</c> 였고, 그것은 자기 자신과 <b>조상</b>만 본다.
        /// 실제 배선은 날이 <c>/Vehicle</c> 에, 패드가 그 <b>자식</b> <c>/Vehicle/SnowPad</c> 에 있어
        /// 검사가 한 번도 걸리지 않았다. 그 사이 패드는 4.0 × 2.3m 사각형을 매 스텝 10cm 씩 파냈고,
        /// 그 정수는 필드에서만 빠지고 어느 장부에도 오르지 않았다:
        /// <list type="bullet">
        /// <item>정지: 발자국 한 번 = 2304셀 × 30cm = <b>-2.70 m³</b> 로 <b>고정된</b> 불변식 오차</item>
        /// <item>8.8m/s: 폭 2.3m × 8.8m/s × 0.30m = <b>-6.07 m³/s</b> 로 <b>발산하는</b> 오차</item>
        /// </list>
        /// 같은 원인이 두 증상을 만든 것이고, 크기가 다른 이유는 정지가 면적 한 번이고 주행이
        /// 스와스라는 것뿐이다.
        ///
        /// <b>왜 리그가 아니라 씬 전체인가.</b> 불변식은 <c>field + carried + deleted - initial</c> 로
        /// <b>격자 전역</b>이다. 다른 차량에 달린 패드도 같은 격자를 파므로 같은 크기로 장부를 깨뜨린다.
        /// 그래서 리그 경계가 아니라 <b>격자 경계</b>로 판정한다.
        ///
        /// ⚠ 끄면 <see cref="SnowSprayVfx"/>·<see cref="SnowPushAudio"/> 가 조용해진다 — 둘은
        /// <c>SnowVehiclePad.LastRemovedCm</c> 을 구독한다. 이 컴포넌트가 <see cref="LastRemovedCm"/> 과
        /// <see cref="SnowCleared"/> 로 같은 신호를 내므로 연출을 이쪽으로 옮기면 되지만, 그것은
        /// 보존과 무관한 별 변경이라 여기서 하지 않는다.
        /// </summary>
        private void DisableForeignCutters()
        {
            var pads = FindObjectsByType<SnowVehiclePad>(FindObjectsInactive.Include);

            for (int i = 0; i < pads.Length; i++)
            {
                SnowVehiclePad pad = pads[i];
                if (pad == null || !pad.enabled) continue;

                pad.enabled = false;
                Debug.LogError(
                    $"{nameof(SnowPlowBlade)}: {nameof(SnowVehiclePad)} '{pad.name}' 가 같은 눈 격자를 " +
                    "영수증 없이 자르고 있었다 — 껐다. 그 스탬프는 필드에서만 빠지고 장부에 오르지 " +
                    "않으므로 불변식이 그만큼 음수로 앉고, 주행 중에는 스와스만큼 초당 발산한다. " +
                    "영수증을 가진 이쪽이 권위다.", pad);
            }
        }

        private void FixedUpdate()
        {
            LastRemovedCm = 0;
            if (_stage == null) return;

            SnowField field = _stage.Field;
            if (field == null) return;

            if (_ledger is null)
            {
                // ⚠ 기준선을 잡기 <b>전에</b> 다른 자르개를 확인한다. 여기서 잡는 필드 합이 불변식의
                // 0 점이므로, 이 프레임에 남이 이미 파낸 양은 <b>기준선에 흡수되어 영원히 안 보인다</b>.
                DisableForeignCutters();

                _ledger = new SnowPlowLedger(field);
                _repose.Clear();
            }

            float dt = Time.fixedDeltaTime;
            _clock += dt;
            _ledger.BeginStep();

            if (!_checkedStartup) CheckStartupState(field);

            // ---- 포즈 ------------------------------------------------------------------
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();

            _forwardSpeedMps = _body != null
                ? Vector3.Dot(_body.linearVelocity, forward)
                : 0f;

            int angleSign = (int)_angle;
            Quaternion faceRot = Quaternion.AngleAxis(angleSign * _bladeAngleDeg, Vector3.up);
            Vector3 faceFwd = faceRot * forward;
            Vector3 faceRight = Vector3.Cross(Vector3.up, faceFwd);

            Vector3 bladeCentre = transform.position + forward * _bladeAheadM;
            float halfWidth = _bladeWidthM * 0.5f;

            // ---- 동사 ------------------------------------------------------------------
            float attachOn = Mathf.Max(0.01f, _attachSpeedMps);
            float attachOff = attachOn * 0.5f;
            _attached = _bladeDown && (_attached ? _forwardSpeedMps > attachOff : _forwardSpeedMps > attachOn);

            _push01 = _attached ? Mathf.Clamp01(_forwardSpeedMps / Mathf.Max(0.5f, _pushFullSpeedMps)) : 0f;

            float shovedDeg = Mathf.Clamp(_heapFrontAngleDeg, 20f, 85f);
            float reposeDeg = Mathf.Clamp(_reposeAngleDeg, 10f, 80f);
            float wantDeg = Mathf.Lerp(Mathf.Min(reposeDeg, shovedDeg), shovedDeg, _push01);
            _faceAngleDeg = _faceRelaxDegPerSec <= 0f
                ? shovedDeg
                : Mathf.MoveTowards(_faceAngleDeg, wantDeg, _faceRelaxDegPerSec * dt);

            // ---- 페이즈 1: 지우기 / 퇴역 -------------------------------------------------
            //
            // 무조건 돈다. 조용한 프레임에 건너뛰면 영수증이 미결로 남고 필드가 그 질량을 갖고
            // 있는 동안 원장도 그것을 세고 있다 — 이 설계가 LEAK 를 찍을 수 있는 유일한 경로다.
            if (_ledger.HasReceipt)
            {
                // 흔적은 영수증이 사라지기 **전에** 남긴다. 내려놓은 더미가 정착하려면 relax 창이
                // 그 자리에 남아 있어야 하고, 그때는 영수증이 없어 가드가 더 지켜주지 않는다.
                _repose.Touch(_ledger.ReceiptMinX, _ledger.ReceiptMinY,
                              _ledger.ReceiptMaxX, _ledger.ReceiptMaxY, _clock);

                if (_attached) _ledger.ErasePaidReceipt();
                else
                {
                    _depositVolumeM3 += _ledger.RetireReceipt();
                    _depositCount++;
                }
            }

            // ---- 페이즈 2: 자르기 -------------------------------------------------------
            if (_bladeDown) Cut(field, forward, faceFwd, bladeCentre, halfWidth, dt);

            // ---- 페이즈 3: 소반 --------------------------------------------------------
            if (_bladeDown && _ledger.BermCm > 0)
                PlaceCones(field, bladeCentre, faceFwd, faceRight, halfWidth,
                           _bermOutM, _bermBackM, _bermRadiusM, 0, berm: true, amountCm: _ledger.BermCm);

            // ---- 페이즈 4: 방출 --------------------------------------------------------
            EmitHeap(field, faceFwd, faceRight, bladeCentre, halfWidth, angleSign, dt);

            // ---- 페이즈 5: relax -------------------------------------------------------
            RunRelax(field);

            // ---- 페이즈 6: 반작용 ------------------------------------------------------
            ApplyCastReaction(faceRight, bladeCentre, angleSign);
        }

        // ------------------------------------------------------------------ 페이즈 2

        private void Cut(SnowField field, Vector3 forward, Vector3 faceFwd,
                         Vector3 bladeCentre, float halfWidth, float dt)
        {
            // 후진하면서 자르지 않는다 — 판의 뒷면이 눈을 향하고 있다. 후진의 동사는 자르기가
            // 아니라 내려놓기다.
            float travel = Mathf.Max(0f, _forwardSpeedMps) * dt;

            // 스윕을 날 면의 법선에 투영한다. 각진 날에서 남는 면 방향 미끄러짐은 8m/s·30° 에서
            // 8cm 로 12.5cm 셀 하나보다 작다 — 그래서 v7 처럼 스윕을 여러 조각으로 쪼개지 않는다.
            float along = travel * Mathf.Max(0f, Vector3.Dot(forward, faceFwd));
            float halfLength = _bladeThicknessM * 0.5f + along * 0.5f;

            Vector3 centre = bladeCentre + faceFwd * (along * 0.5f);
            var area = new SnowStampArea(centre.x, centre.z, faceFwd.x, faceFwd.z, halfLength, halfWidth);

            int cut = Mathf.Min(_cutCmPerPass, field.MaxDepthCm);
            int removed = _stage.ApplyStamp(_stampId, area, -cut);

            LastArea = area;
            LastRemovedCm = removed;
            if (removed <= 0) return;

            _ledger.CreditCut(removed, _conservedPermille, _bermPermille);

            TouchArea(field, area);
            _renderer?.MarkFresh(area);
            SnowCleared?.Invoke(area);
        }

        // ------------------------------------------------------------------ 페이즈 4

        private void EmitHeap(SnowField field, Vector3 faceFwd, Vector3 faceRight,
                              Vector3 bladeCentre, float halfWidth, int angleSign, float dt)
        {
            float tanFace = Mathf.Tan(Mathf.Clamp(_faceAngleDeg, 20f, 85f) * Mathf.Deg2Rad);
            float tanBack = Mathf.Tan(Mathf.Clamp(_heapBackAngleDeg, 20f, 88f) * Mathf.Deg2Rad);
            float tanRepose = Mathf.Tan(Mathf.Clamp(_reposeAngleDeg, 10f, 80f) * Mathf.Deg2Rad);

            float maxPeakM = Mathf.Min(_heapPeakCm, field.MaxDepthCm) * 0.01f;
            _pileCapacityM3 = SnowHeapShape.VolumeM3(maxPeakM, halfWidth, tanFace, tanBack, tanRepose);

            float m3PerCm = field.CellSize * field.CellSize * 0.01f;
            long carried = _ledger.CarriedCm;
            long capacityCm = (long)(_pileCapacityM3 / m3PerCm);

            float pileM3 = carried * m3PerCm;
            _pileHeightM = SnowHeapShape.PeakForVolumeM(Mathf.Min(pileM3, _pileCapacityM3), maxPeakM,
                                                        halfWidth, tanFace, tanBack, tanRepose);

            // ---- 캐스팅: 각진 날의 원장 이동 ------------------------------------------
            //
            // **셀 사이를 옆으로 미는 것이 아니라 원장의 채널이 바뀐다.** 텍셀 대 텍셀 이동은
            // 확산되고 보간에서 질량을 잃는다 — v7 이 그것 때문에 이 형태를 골랐다.
            float castFrac = 0f;
            if (angleSign != 0 && _attached && _castEfficiency > 0f)
            {
                float sin = Mathf.Abs(Mathf.Sin(_bladeAngleDeg * Mathf.Deg2Rad));
                castFrac = Mathf.Clamp01(Mathf.Abs(_forwardSpeedMps) * sin * _castEfficiency * dt
                                         / Mathf.Max(0.1f, _bladeWidthM));
            }

            if (!_attached)
            {
                // 받치고 있지 않은 날은 아무것도 지고 있지 않다. 갓 자른 것을 옆에 흘려 버리는
                // 것이 후진하며 끌리는 날의 정직한 동작이다.
                _heapFrac = 0f;
                _releaseFrac = 1f;
            }
            else
            {
                float fill = capacityCm <= 0 ? 1f : (float)carried / capacityCm;
                float start = Mathf.Clamp01(_releaseStartFill);
                float ramp = Mathf.SmoothStep(0f, 1f,
                                              Mathf.Clamp01((fill - start) / Mathf.Max(1e-4f, 1f - start)));

                float soft = Mathf.Clamp01(Mathf.Max(0f, _releaseRatePerSec) * dt) * ramp;

                // hard 항이 실제로 더미를 고정한다: 용량 위에서 정확히 초과만큼을 방출하므로
                // soft 를 0 으로 둬도 상한을 넘지 못한다. 끌 수 없는 것이 의도다.
                float hard = carried > capacityCm && carried > 0
                           ? Mathf.Clamp01(1f - (float)capacityCm / carried)
                           : 0f;

                // 더한 뒤 클램프한다. 흘림과 캐스팅은 같은 양동이의 다른 구멍이고 경우가 아니다.
                _releaseFrac = Mathf.Clamp01(soft + hard + castFrac);
                _heapFrac = 1f - _releaseFrac;
            }

            _castRateM3PerSec = dt > 0f ? castFrac * pileM3 / dt : 0f;

            long heapAmount = (long)(carried * _heapFrac);
            long releaseAmount = carried - heapAmount;      // 둘의 합은 항상 carried 다

            // ---- 힙 --------------------------------------------------------------------
            //
            // 모양은 조건 밖에서 만든다. 계기가 **실제로 쓴 모양**의 폭을 찍어야 하고, 그것이
            // 같은 식을 블레이드와 계기 두 곳에 두지 않는 유일한 방법이다(영수증과 같은 논리).
            var shape = new SnowHeapShape(_pileHeightM, halfWidth, tanFace, tanBack, tanRepose);
            _pileFootprintWidthM = 2f * shape.HalfCrestM;      // 정상 길이 — 날 폭에 고정, 부피와 무관
            _pileSupportWidthM = 2f * shape.AcrossHalfM;       // 정상 + 양쪽 우진각 — 높이에 따라 자란다

            if (heapAmount > 0 && _pileHeightM > 0f)
            {
                Vector3 crest = bladeCentre + faceFwd * _heapCrestAheadM;

                if (ScanHeap(field, shape, crest, faceFwd, faceRight,
                             out int x0, out int y0, out int x1, out int y1))
                {
                    // 발자국이 거절 없이 받을 수 있는 양이 모양의 용량보다 작으면 그쪽이 실효
                    // 상한이다. 넘는 몫은 unplaced 로 흘리는 대신 **방출로 돌린다** — 그래야
                    // unplaced 가 계기로 살아 있고, 1바이트 천장이 낮게 설정된 사실을
                    // PileFill01 이 말해 준다.
                    long room = _ledger.ScannedCapacityCm;
                    if (heapAmount > room)
                    {
                        releaseAmount += heapAmount - room;
                        heapAmount = room;
                    }

                    if (heapAmount > 0)
                    {
                        _ledger.EmitFromCarried(heapAmount, recordReceipt: true);
                        _repose.Touch(x0, y0, x1, y1, _clock);
                    }
                }
            }

            // ---- 측면 벽 ----------------------------------------------------------------
            //
            // 정면 날은 두 개, 각진 날은 **배출단 하나**다. 그것이 두 개의 벽이 아니라 하나의
            // 긴 벽(windrow)을 만드는 차이다.
            if (_bladeDown && releaseAmount > 0)
            {
                PlaceCones(field, bladeCentre, faceFwd, faceRight, halfWidth,
                           _spillOutM, _spillBackM, _spillRadiusM, angleSign,
                           berm: false, amountCm: releaseAmount);
            }
        }

        /// <summary>
        /// 힙 발자국을 스캔한다. 지지 사각형의 네 꼭짓점을 월드로 돌려 AABB 를 잡는다 —
        /// 프로파일이 양수인 곳은 정확히 <c>peak - drop > 0</c> 이므로 지지가 <b>추정이 아니라 정확</b>하다.
        /// 이것을 작게 잡으면 기록한 창 밖에 영수증이 남고, 그것이 이 설계가 누출할 수 있는 유일한 길이다.
        /// </summary>
        private bool ScanHeap(SnowField field, in SnowHeapShape shape, Vector3 crest,
                              Vector3 faceFwd, Vector3 faceRight,
                              out int x0, out int y0, out int x1, out int y1)
        {
            float ahead = shape.AheadM;
            float behind = shape.BehindM;
            float across = shape.AcrossHalfM;

            float extentX = Mathf.Abs(faceFwd.x) * Mathf.Max(ahead, behind) + Mathf.Abs(faceRight.x) * across;
            float extentZ = Mathf.Abs(faceFwd.z) * Mathf.Max(ahead, behind) + Mathf.Abs(faceRight.z) * across;

            if (!CellRect(field, crest.x - extentX, crest.z - extentZ,
                          crest.x + extentX, crest.z + extentZ, out x0, out y0, out x1, out y1))
                return false;

            _ledger.BeginScan();

            for (int y = y0; y <= y1; y++)
            {
                float wz = field.WorldZAtCell(y);
                for (int x = x0; x <= x1; x++)
                {
                    float wx = field.WorldXAtCell(x);
                    float dx = wx - crest.x;
                    float dz = wz - crest.z;

                    float along = dx * faceFwd.x + dz * faceFwd.z;
                    float acrossM = dx * faceRight.x + dz * faceRight.z;

                    _ledger.ScanCell(x, y, shape.HeightM(along, acrossM));
                }
            }

            return _ledger.ScannedCells > 0;
        }

        /// <summary>
        /// 원뿔로 놓는다. 안식각 원뿔이라 놓인 순간부터 relax 가 옮길 초과 기울기가 거의 없다.
        /// <paramref name="angleSign"/> 이 0 이면 양쪽, 아니면 <b>배출단 한쪽</b>만.
        /// </summary>
        private void PlaceCones(SnowField field, Vector3 bladeCentre, Vector3 faceFwd, Vector3 faceRight,
                                float halfWidth, float outM, float backM, float radiusM,
                                int angleSign, bool berm, long amountCm)
        {
            if (amountCm <= 0 || radiusM <= 0f) return;

            float tanRepose = Mathf.Tan(Mathf.Clamp(_reposeAngleDeg, 10f, 80f) * Mathf.Deg2Rad);
            float offset = halfWidth + outM;

            Vector3 behind = bladeCentre - faceFwd * backM;
            Vector3 left = behind - faceRight * offset;
            Vector3 right = behind + faceRight * offset;

            // 각진 날은 **배출단 하나**만 쓴다. 그것이 두 개의 벽이 아니라 하나의 긴 벽을 만든다.
            bool useLeft = angleSign <= 0;
            bool useRight = angleSign >= 0;

            Vector3 a = useLeft ? left : right;
            Vector3 b = useRight ? right : left;

            float minX = Mathf.Min(a.x, b.x) - radiusM;
            float maxX = Mathf.Max(a.x, b.x) + radiusM;
            float minZ = Mathf.Min(a.z, b.z) - radiusM;
            float maxZ = Mathf.Max(a.z, b.z) + radiusM;

            if (!CellRect(field, minX, minZ, maxX, maxZ, out int x0, out int y0, out int x1, out int y1)) return;

            _ledger.BeginScan();

            for (int y = y0; y <= y1; y++)
            {
                float wz = field.WorldZAtCell(y);
                for (int x = x0; x <= x1; x++)
                {
                    float wx = field.WorldXAtCell(x);

                    // 겹치는 원뿔이 이중으로 가중되지 않게 max 로 합친다.
                    float h = 0f;
                    if (useLeft) h = ConeHeight(wx, wz, left, radiusM, tanRepose);
                    if (useRight)
                    {
                        float r = ConeHeight(wx, wz, right, radiusM, tanRepose);
                        if (r > h) h = r;
                    }

                    _ledger.ScanCell(x, y, h);
                }
            }

            if (_ledger.ScannedCells == 0) return;

            // 원뿔이 거절 없이 받을 수 있는 양까지만 놓는다. **넘는 몫은 원장에 그대로 남는다** —
            // 파괴하지 않으므로 불변식은 안전하고, 벽은 차량이 움직이면서 새 자리의 여유로
            // 이어서 쌓인다. 벽이 '사건'이 아니라 '이동으로 쌓이는 것'인 이유가 이것이다.
            long room = _ledger.ScannedCapacityCm;
            if (amountCm > room) amountCm = room;
            if (amountCm <= 0) return;

            long placed = berm
                ? _ledger.EmitFromBerm(amountCm)
                : _ledger.EmitFromCarried(amountCm, recordReceipt: false);

            if (placed > 0) _repose.Touch(x0, y0, x1, y1, _clock);
        }

        private static float ConeHeight(float worldX, float worldZ, Vector3 centre, float radiusM, float tanRepose)
        {
            float dx = worldX - centre.x;
            float dz = worldZ - centre.z;
            float d = Mathf.Sqrt(dx * dx + dz * dz);
            return d >= radiusM ? 0f : (radiusM - d) * tanRepose;
        }

        // ------------------------------------------------------------------ 페이즈 5

        private void RunRelax(SnowField field)
        {
            float tanRepose = Mathf.Tan(Mathf.Clamp(_reposeAngleDeg, 10f, 80f) * Mathf.Deg2Rad);

            // 가드의 한계는 두 면 중 급한 쪽 이상이어야 하고, 절대 안식각 아래로 내려가면 안 된다 —
            // 제약을 느슨하게 하는 가드는 힙 내부가 주변보다 **빨리** 무너지게 만들어 목적의 반대가 된다.
            float guardDeg = Mathf.Max(_heapGuardAngleDeg,
                                       Mathf.Max(_heapFrontAngleDeg, _heapBackAngleDeg));
            float tanGuard = Mathf.Tan(Mathf.Clamp(guardDeg, 10f, 89f) * Mathf.Deg2Rad);

            float cellCm = field.CellSize * 100f;
            int maxDelta = Mathf.Max(1, Mathf.RoundToInt(tanRepose * cellCm));
            int maxDeltaDiag = Mathf.Max(1, Mathf.RoundToInt(tanRepose * cellCm * 1.41421356f));
            int guardDelta = Mathf.Max(maxDelta, Mathf.RoundToInt(tanGuard * cellCm));
            int guardDeltaDiag = Mathf.Max(maxDeltaDiag, Mathf.RoundToInt(tanGuard * cellCm * 1.41421356f));

            int ratePermille = Mathf.Clamp(Mathf.RoundToInt(_relaxRate * 500f), 1, 120);

            _repose.Run(field, _ledger, _clock, _relaxTrailSeconds, _relaxPadCells, _relaxMaxWindowCells,
                        _relaxIterations, ratePermille,
                        maxDelta, maxDeltaDiag, guardDelta, guardDeltaDiag, _heapRelaxGuard);
        }

        // ------------------------------------------------------------------ 페이즈 6

        /// <summary>
        /// 각진 날의 반작용. <b>부호는 던지는 쪽의 반대</b>다 — 오른쪽으로 던지면 차가 왼쪽으로 밀린다.
        ///
        /// ⚠ <b>요 모멘트는 이 프로젝트의 <see cref="VehicleController"/> 가 되돌린다.</b> 그것은
        /// 매 스텝 <c>angularVelocity</c> 를 목표 요각과의 절대 오차로 덮어쓰므로(그 파일의 주석이
        /// "충돌 토크가 Y 를 돌려도 같은 방식으로 되돌아온다"고 못 박고 있다) 외부에서 넣은 토크는
        /// 한 스텝 안에 교정된다. 그래서 여기서는 힘을 <b>날 선에</b> 걸어 모멘트가 물리적으로
        /// 생기게 하고, 실제로 남는 것은 <b>횡속도</b>다 — 그것은 컨트롤러가 보존하고 그립으로만
        /// 빼므로 차가 옆으로 게처럼 밀리는 것으로 읽힌다. <see cref="CastYawDegPerSec"/> 는
        /// 발행하지만 조향에 실제로 들어가려면 컨트롤러에 요 바이어스 훅이 필요하다.
        /// </summary>
        private void ApplyCastReaction(Vector3 faceRight, Vector3 bladeCentre, int angleSign)
        {
            _castPushMps2 = 0f;
            _castYawDegPerSec = 0f;

            if (angleSign == 0 || !_attached || _castRateM3PerSec <= 0f) return;

            _castPushMps2 = -angleSign * _castPushMps2PerM3s * _castRateM3PerSec;
            _castYawDegPerSec = -angleSign * _castYawDegPerM3s * _castRateM3PerSec;

            if (_body == null || _castPushMps2 == 0f) return;

            _body.AddForceAtPosition(faceRight * (_castPushMps2 * _body.mass), bladeCentre, ForceMode.Force);
        }

        // ------------------------------------------------------------------ 도우미

        private void TouchArea(SnowField field, in SnowStampArea area)
        {
            if (CellRect(field, area.MinX, area.MinZ, area.MaxX, area.MaxZ,
                         out int x0, out int y0, out int x1, out int y1))
                _repose.Touch(x0, y0, x1, y1, _clock);
        }

        /// <summary>월드 AABB → 클램프된 셀 범위. 2셀 패드가 셀 중심 오프셋을 흡수한다.</summary>
        private static bool CellRect(SnowField field, float minX, float minZ, float maxX, float maxZ,
                                     out int x0, out int y0, out int x1, out int y1)
        {
            x0 = field.CellXAtWorld(minX) - 2;
            y0 = field.CellYAtWorld(minZ) - 2;
            x1 = field.CellXAtWorld(maxX) + 2;
            y1 = field.CellYAtWorld(maxZ) + 2;

            if (x0 < 0) x0 = 0;
            if (y0 < 0) y0 = 0;
            if (x1 >= field.Width) x1 = field.Width - 1;
            if (y1 >= field.Height) y1 = field.Height - 1;

            return x1 >= x0 && y1 >= y0;
        }

        /// <summary>
        /// 첫 스텝에 한 번만 도는 시동 점검. <b>프레임 경로가 아니다.</b> 두 가지를 본다.
        ///
        /// <b>① 기준선이 처녀설인가.</b> 원장의 0 점은 <see cref="SnowPlowLedger.Reset"/> 이 잡은
        /// 필드 합이다. 우리보다 먼저 누가 필드를 팠으면 그 양은 0 점에 흡수되어 <b>불변식으로는
        /// 영원히 안 보이고</b>, 우리가 그 뒤에 세는 모든 것이 그만큼 어긋난 세계 위에서 센다.
        /// 그래서 스테이지의 시작 깊이로 <b>처녀설 합을 따로 계산해</b> 비교한다 — 이것이
        /// "정지한 차량이 -2542 L 로 앉아 있는데 그게 기준선 문제인지 누출인지 모르겠다"를 없앤다.
        ///
        /// <b>② 천장이 더미보다 높은가.</b> 프로파일은 "필드가 이미 갖고 있는 것 <b>위로</b>" 쌓이므로
        /// 실효 여유는 <c>MaxDepthCm - 그 자리의 깊이</c>다. 최대 30cm·시작 30cm 이면 여유가 0 이라
        /// 더미가 아예 설 수 없고, 그때 <c>cap</c> 이 두 자리 작게 읽히는 것은 결함이 아니라 <b>사실</b>이다.
        /// </summary>
        private void CheckStartupState(SnowField field)
        {
            _checkedStartup = true;

            long pristine = (long)Mathf.Min(_stage.StartDepthCm, field.MaxDepthCm) * field.Width * field.Height;
            long already = pristine - field.TotalDepthCm;
            if (already != 0)
            {
                Debug.LogError(
                    $"{nameof(SnowPlowBlade)}: 기준선이 처녀설이 아니다 — 원장이 0 점을 잡기 전에 " +
                    $"{already * _ledger.LitresPerCm:F3}L({already} ccell)가 이미 필드에서 빠져 있었다 " +
                    $"(처녀설 {pristine} ccell, 지금 {field.TotalDepthCm} ccell). 이 양은 불변식에 " +
                    "안 나타나므로 계기가 조용해도 세계가 어긋나 있다. 같은 격자를 파는 다른 " +
                    "컴포넌트를 찾아라 — 이 블레이드는 방금 자기가 찾은 것을 껐다.", this);
            }

            Vector3 p = transform.position;
            int here = field.DepthCmAtWorld(p.x, p.z);
            int room = field.MaxDepthCm - here;
            if (room >= _heapPeakCm) return;

            Debug.LogWarning(
                $"{nameof(SnowPlowBlade)}: 여유 깊이가 {room}cm 인데 더미 정상은 {_heapPeakCm}cm 다 " +
                $"(SnowStage 최대 {field.MaxDepthCm}cm, 이 자리 눈 {here}cm). 더미가 천장에 눌려 " +
                "낮고 넓게 퍼지고 방출이 즉시 시작된다 — 계기의 cap 이 두 자리 작게 읽히는 것이 " +
                "그 결과다. 축적 구간을 원하면 SnowStage 의 최대 깊이를 시작 깊이 + 이 값 위로 올리고 " +
                "셰이더의 _SnowMaxDepth 를 같이 맞춰라 — 1바이트의 천장은 255cm 다.", this);
        }

        private void OnDrawGizmosSelected()
        {
            Vector3 forward = transform.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();

            Vector3 faceFwd = Quaternion.AngleAxis((int)_angle * _bladeAngleDeg, Vector3.up) * forward;
            Vector3 centre = transform.position + forward * _bladeAheadM;

            Gizmos.color = _bladeDown ? new Color(0.3f, 0.8f, 1f, 0.9f) : new Color(1f, 0.6f, 0.2f, 0.6f);
            Matrix4x4 previous = Gizmos.matrix;
            Gizmos.matrix = Matrix4x4.TRS(centre, Quaternion.LookRotation(faceFwd), Vector3.one);
            Gizmos.DrawWireCube(Vector3.zero, new Vector3(_bladeWidthM, 0.05f, _bladeThicknessM));
            Gizmos.matrix = previous;

            // 정상선. 발자국 폭이 날 폭에 고정이라는 것이 눈으로 보여야 한다.
            Gizmos.color = new Color(1f, 1f, 0.4f, 0.8f);
            Vector3 crest = centre + faceFwd * _heapCrestAheadM;
            Vector3 faceRight = Vector3.Cross(Vector3.up, faceFwd);
            Gizmos.DrawLine(crest - faceRight * (_bladeWidthM * 0.5f),
                            crest + faceRight * (_bladeWidthM * 0.5f));
        }
    }
}
