using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>
    /// 레이마칭 렌더러를 세우고 프레임마다 돌린다. GPU 절반(클라 전용)의 진입점이다.
    ///
    /// v7 스파이크는 부트스트랩 하나가 GPU 시뮬과 렌더러를 같이 세웠는데, 여기서는 <b>시뮬이 없다</b> —
    /// 권위는 <see cref="SnowStage"/> 의 CPU 격자이고 이 컴포넌트는 그것을 읽어 그리는 쪽만 세운다.
    /// 그래서 순서도 단순하다: 파생 텍스처를 다시 굽고(<see cref="SnowDerivedField.Refresh"/>),
    /// 마처의 유니폼을 싣는다(<see cref="SnowRaymarchRenderer.UpdateUniforms"/>).
    ///
    /// <b>정점 변위 렌더러와 배타적이다.</b> 둘을 같이 켜면 같은 눈을 두 번 그려 A/B 가 아니라 겹침이 된다.
    /// A/B 스위치가 이 컴포넌트의 <see cref="_disablePanels"/> 다.
    /// </summary>
    [RequireComponent(typeof(SnowStage), typeof(SnowDerivedField), typeof(SnowRaymarchRenderer))]
    public sealed class SnowRaymarchBootstrap : MonoBehaviour
    {
        [Tooltip("켜면 씬의 SnowPanelBuilder 패널(정점 변위)을 끈다. A/B 를 볼 때만 끈다.")]
        [SerializeField] private bool _disablePanels = true;

        [Tooltip("눈이 얹히는 바닥의 월드 Y. 패널 오브젝트의 y 와 같아야 한다.")]
        [SerializeField] private float _groundY;

        private SnowStage _stage;
        private SnowDerivedField _derived;
        private SnowRaymarchRenderer _renderer;
        private bool _initialized;

        private void Awake()
        {
            _stage = GetComponent<SnowStage>();
            _derived = GetComponent<SnowDerivedField>();
            _renderer = GetComponent<SnowRaymarchRenderer>();

            // 헤드리스에서는 렌더링이 존재하지 않는다. 권위는 SnowStage 가 그대로 돌린다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null) enabled = false;
        }

        private void OnEnable()
        {
            if (!_disablePanels) return;

            foreach (var panel in FindObjectsByType<SnowPanelBuilder>(FindObjectsInactive.Include,
                                                                      FindObjectsSortMode.None))
            {
                panel.gameObject.SetActive(false);
            }
        }

        private void LateUpdate()
        {
            // Awake 순서에 의존하지 않는다 — 필드는 SnowStage.Awake 가 만들고, 여기서는 준비됐을 때
            // 한 번만 Initialize 한다. 이 프로젝트는 Awake 순서 가정으로 한 번 물렸다(Snow/AGENTS.md).
            if (_stage == null || _stage.Field == null) return;

            _derived.GroundY = _groundY;
            _derived.Refresh();
            if (!_derived.Ready) return;

            if (!_initialized)
            {
                _renderer.Initialize(_derived);
                _initialized = true;
            }

            _renderer.UpdateUniforms(Time.deltaTime);
        }
    }
}
