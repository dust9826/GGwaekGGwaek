using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>눈을 어떻게 그릴지. <b>값마다 실제로 도는 경로가 있다</b> — 예약된 값을 두지 않는다.</summary>
    public enum ESnowLook
    {
        /// <summary>CPU 격자를 레이마칭한다(`SnowCpuStageView`). 지금의 기본이고 데스크톱 전용이다.</summary>
        Raymarch = 0,

        /// <summary>그리지 않는다. 데디 서버·헤드리스, 그리고 눈을 끄고 성능을 재는 A/B 에 쓴다.</summary>
        Hidden = 1,

        /// <summary>
        /// <b>저사양.</b> 미리 잘게 쪼갠 패널의 정점을 밀어올린다(<see cref="SnowDisplaceView"/>).
        /// 마칭도 <c>SV_Depth</c> 도 구운 텍스처도 없어서 GLES3 에서 돌고 CPU 굽기가 0 이다.
        /// 대가는 실루엣이 정점 간격까지만 선명한 것과 픽셀 단위 교차가 없는 것이다.
        /// </summary>
        Displace = 2,
    }

    /// <summary>
    /// <b>씬에 눈을 놓는 단 하나의 입구.</b> 이 컴포넌트가 붙은 오브젝트 하나가 곧 그 씬의 눈이다.
    ///
    /// <para>왜 필요한가 — 이 프로젝트는 눈 세대가 셋(구 <c>Scripts/</c> · <c>V7Spike/</c> ·
    /// <c>HeightCpu/</c>)이고, 씬마다 무엇이 그리는지가 손으로 배선돼 있었다. 그 결과 <b>한 바닥에 두
    /// 눈이 그려지는 사고</b>가 반복됐다(<c>AGENTS.md</c> 의 "한 바닥에 두 눈을 그리지 마라"). 규칙을
    /// 사람이 기억하는 대신 <b>코드가 강제한다</b>.</para>
    ///
    /// <list type="number">
    /// <item>권위는 항상 <see cref="SnowCpuStage"/>(CPU 정수 격자)다. 이 컴포넌트는 그것을 요구한다.</item>
    /// <item>보이는 것은 <see cref="Look"/> 하나로 정한다.</item>
    /// <item><b>같은 씬의 구 세대 렌더러를 스스로 끈다</b> — 끄는 것은 그리는 쪽뿐이고,
    /// <b>구 <c>SnowStage</c> 데이터는 건드리지 않는다.</b> 구 시스템을 아직 보존하는 배송 회귀 씬이
    /// 그 격자를 읽기 때문이다. SinglePlay 빌더는 구 오브젝트를 별도로 제거한다.</item>
    /// </list>
    ///
    /// <para><b>모바일은 아직 이 enum 에 없다.</b> 실측(2026-08-19)으로 막는 것이 셰이더 하나가 아니기
    /// 때문이다 — 마처는 `#pragma target 4.5` 에 프래그먼트 `SV_Depth` 라 GLES3 에서 컴파일되지 않고,
    /// 그 위에 <b>굽기 1.79 ms + relax 2.14 ms</b>(M5 Pro, 844,800셀)가 CPU 에 남는다. 굽기는 마처를
    /// 먹이려고만 존재하므로 정점 변위로 바꾸면 사라지고, relax 는 반복·창으로 선형 조절된다. 그 두
    /// 가지를 실제로 만든 뒤에 값을 추가한다.</para>
    /// </summary>
    [RequireComponent(typeof(SnowCpuStage))]
    [DisallowMultipleComponent]
    public sealed class SnowSystem : MonoBehaviour
    {
        [Tooltip("눈을 어떻게 그릴지. Hidden 은 데디 서버와 성능 A/B 용이다.\n\n" +
                 "자동 선택이 켜져 있으면 이 값은 '요청' 이고, 기기가 못 하면 낮춰서 적용된다.")]
        [SerializeField] private ESnowLook _look = ESnowLook.Raymarch;

        [Tooltip("기기가 마처를 못 돌리면 저사양으로 자동 낮춘다.\n\n" +
                 "판단 기준은 셰이더 레벨이다 - 마처는 target 4.5 + 프래그먼트 SV_Depth 라 " +
                 "GLES3(35)에서 컴파일되지 않는다. 끄면 요청한 값을 그대로 쓴다(개발용 강제).")]
        [SerializeField] private bool _autoDowngrade = true;

        [Tooltip("눈의 생김새. 0 이면 사실적, 1 이면 토이. 두 렌더 경로가 같은 값을 읽어야 " +
                 "옵션을 바꾼 것이 조명을 바꾼 것처럼 보이지 않는다.")]
        [SerializeField, Range(0f, 1f)] private float _casualLook = 1f;

        [Tooltip("같은 씬의 구 세대 렌더러를 끌지. 끄지 않으면 한 바닥에 눈이 두 벌 그려진다.\n\n" +
                 "끄는 것은 그리는 쪽뿐이다 — 구 SnowStage 의 격자 데이터는 그대로 두므로 회귀 씬 판정은 " +
                 "계속 동작한다.")]
        [SerializeField] private bool _silenceLegacyRenderers = true;

        /// <summary>지금 적용된 룩. 런타임에 바꾸면 즉시 반영된다.</summary>
        public ESnowLook Look
        {
            get => _look;
            set { _look = value; Apply(); }
        }

        /// <summary>이번에 끈 구 세대 컴포넌트 수. 검증이 읽는다 — 0 이면 이미 정리된 씬이라는 뜻이다.</summary>
        public int SilencedLegacyCount { get; private set; }

        /// <summary>
        /// 실제로 적용된 룩. <see cref="Look"/> 은 <b>요청</b>이고 이것은 <b>결과</b>다 - 기기가
        /// 마처를 못 돌리면 다르다. HUD·검증이 읽는 것은 이쪽이어야 한다.
        /// </summary>
        public ESnowLook EffectiveLook { get; private set; }

        private SnowCpuStage _stage;
        private SnowCpuStageView _view;
        private SnowDisplaceView _displace;

        private void Awake()
        {
            _stage = GetComponent<SnowCpuStage>();
            _view = GetComponent<SnowCpuStageView>();
            _displace = GetComponent<SnowDisplaceView>();
            Apply();
        }

        /// <summary>
        /// 룩 전역을 민다. <b>여기서 하는 이유</b>: 렌더 경로가 둘이고 둘 다 같은 팔레트를 읽어야
        /// 하는데, 어느 한쪽 뷰에 두면 그 뷰가 꺼진 모드에서 팔레트가 갱신되지 않는다(스파클이 시간을
        /// 필요로 한다). 이 컴포넌트는 어느 모드에서도 살아 있으므로 여기가 유일하게 맞는 자리다.
        /// </summary>
        private void LateUpdate() => SnowLookStyle.Apply(SnowLookSettings.V6(_casualLook));

        private void Apply()
        {
            EffectiveLook = Resolve(_look);

            // 그래픽 장치가 없으면 뷰는 스스로도 꺼지지만, 여기서 먼저 끊어 두면 데디 서버에서
            // 굽기·업로드 경로가 아예 시작되지 않는다.
            if (_view != null) _view.enabled = EffectiveLook == ESnowLook.Raymarch;
            if (_displace != null) _displace.enabled = EffectiveLook == ESnowLook.Displace;

            if (_silenceLegacyRenderers) SilenceLegacy();
        }

        /// <summary>
        /// 요청을 이 기기가 할 수 있는 것으로 낮춘다.
        ///
        /// <para><b>기준은 셰이더 레벨 하나다.</b> 마처는 <c>target 4.5</c> 에 프래그먼트가
        /// <c>SV_Depth</c> 를 쓰므로 GLES3(레벨 35)에서는 컴파일 자체가 안 된다 - 즉 "느리다" 가 아니라
        /// "안 된다" 이고, 그래서 품질 설정이 아니라 <b>능력</b>으로 가른다. 품질로 가르면 저사양 PC 가
        /// 멀쩡한 마처를 못 쓰고, 고품질로 맞춘 모바일이 검은 화면을 본다.</para>
        ///
        /// <para>저사양 경로는 <c>target 3.5</c> 뿐이므로 <see cref="ESnowLook.Displace"/> 는 낮출 곳이
        /// 없다. 그래픽 장치가 아예 없으면(데디 서버·헤드리스) 무엇을 요청했든
        /// <see cref="ESnowLook.Hidden"/> 이다.</para>
        /// </summary>
        private ESnowLook Resolve(ESnowLook want)
        {
            if (SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null)
            {
                return ESnowLook.Hidden;
            }

            if (!_autoDowngrade) return want;

            if (want == ESnowLook.Raymarch && SystemInfo.graphicsShaderLevel < 45)
            {
                return _displace != null ? ESnowLook.Displace : ESnowLook.Hidden;
            }

            return want;
        }

        /// <summary>
        /// 같은 씬의 구 세대 <b>그리는 쪽</b>만 끈다. 지우지 않는 이유는 미니맵·차량 프리팹 오버라이드·
        /// 테스트 스윕이 그 오브젝트들을 참조하기 때문이다(<c>AGENTS.md</c> cs:276 의 기록).
        /// </summary>
        private void SilenceLegacy()
        {
            SilencedLegacyCount = 0;

            foreach (SnowSurfaceRenderer legacy in FindObjectsByType<SnowSurfaceRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (legacy.gameObject.scene != gameObject.scene) continue;
                if (!legacy.enabled) continue;
                legacy.enabled = false;
                SilencedLegacyCount++;
            }

            // 패널은 컴포넌트가 아니라 <b>메시가 그린다</b>. 렌더러를 끊어야 사라진다.
            foreach (SnowPanelBuilder panel in FindObjectsByType<SnowPanelBuilder>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (panel.gameObject.scene != gameObject.scene) continue;

                var mesh = panel.GetComponent<MeshRenderer>();
                if (mesh == null || !mesh.enabled) continue;
                mesh.enabled = false;
                SilencedLegacyCount++;
            }

            // v7 리그도 그리는 쪽이다. 오브젝트를 지우지 않고 비활성만 확인한다.
            foreach (MonoBehaviour mb in FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (mb == null || mb.gameObject.scene != gameObject.scene) continue;
                if (mb.GetType().Name != "SnowRaymarchRendererV7") continue;
                if (!mb.enabled) continue;
                mb.enabled = false;
                SilencedLegacyCount++;
            }
        }

        /// <summary>씬의 눈 시스템. 없으면 null — 소비자가 스스로 만들지 않는다.</summary>
        public static SnowSystem FindInScene(UnityEngine.SceneManagement.Scene scene)
        {
            foreach (SnowSystem s in FindObjectsByType<SnowSystem>(FindObjectsSortMode.None))
            {
                if (s.gameObject.scene == scene) return s;
            }

            return null;
        }
    }
}
