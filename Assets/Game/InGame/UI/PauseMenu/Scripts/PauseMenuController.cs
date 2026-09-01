using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// 게임 도중 ESC 로 여는 일시정지 메뉴. 계속하기와 나가기 둘뿐이다.
    ///
    /// <para><b>싱글은 멈추고 멀티는 안 멈춘다.</b> 멀티에서 시간을 멈추면 그 피어만 멈추고 세션은
    /// 계속 돌아 — 재개하는 순간 자기 화면만 과거에 있다. 판정은
    /// <see cref="StageSession"/> 이 한다(<c>Core/Multiplay/AGENTS.md</c>).</para>
    ///
    /// <para><b>커서를 직접 만지지 않는다.</b> <see cref="PenguinCameraOrbit"/> 를 끄면 그쪽
    /// <c>OnDisable</c> 이 잠금을 풀고 커서를 보여 준다. 여기서 또 만지면 두 곳이 같은 전역 상태를
    /// 다투게 되고, 어느 쪽이 마지막이었는지로 버그가 갈린다.</para>
    ///
    /// <para><b>설정은 아직 없다.</b> 버튼 목록이 <c>pause-actions</c> 컨테이너 하나라 사운드·마이크
    /// 설정을 넣을 때 행만 늘리면 되고 이 클래스의 구조는 바뀌지 않는다.</para>
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class PauseMenuController : MonoBehaviour
    {
        [Tooltip("메뉴가 열려 있는 동안 끌 펭귄 입력. 비우면 입력을 건드리지 않는다.")]
        [SerializeField] private PenguinInputReader _playerInput;

        [Tooltip("메뉴가 열려 있는 동안 끌 카메라. 끄면 커서 잠금도 함께 풀린다.")]
        [SerializeField] private PenguinCameraOrbit _cameraOrbit;

        [Tooltip("나가기를 위임할 곳. 세션 종료와 씬 전환을 이미 소유한다.")]
        [SerializeField] private RequestStageFlowPresenter _stageFlow;

        [Tooltip("증강 선택 화면. 그것이 떠 있는 동안에는 열지 않는다. 비어 있으면 검사하지 않는다.")]
        [SerializeField] private AugmentSelectionDirector _augmentSelection;

        private VisualElement _root;
        private Button _resumeButton;
        private Button _quitButton;

        private bool _timeScaleHeld;
        private float _timeScaleBeforePause = 1f;

        /// <summary>메뉴가 떠 있는가.</summary>
        public bool IsOpen { get; private set; }

        /// <summary>
        /// 로컬 플레이어를 알려 준다. <b>멀티에서는 씬에 플레이어가 없으므로</b>(서버가 스폰한다)
        /// 인스펙터 참조가 비어 있고, 아바타가 스스로 자기를 넣는다 —
        /// <see cref="RequestStageFlowPresenter.BindLocalCameraOrbit"/> 와 같은 방식이다.
        ///
        /// <para><b>여기서 씬을 뒤져 찾지 않는 이유.</b> 찾기로 때우면 "누가 로컬인가" 를 이 클래스가
        /// 추측하게 되고, 4인이면 첫 번째로 걸린 남의 아바타를 끌 수 있다. 그리고 그런 폴백이 조용히
        /// 틀렸을 때 증상이 원인에서 멀어진다는 것을 이 프로젝트가 이미 겪었다
        /// (2026-08-31 눈덩이 교환기).</para>
        /// </summary>
        public void BindLocalPlayer(PenguinInputReader playerInput, PenguinCameraOrbit cameraOrbit)
        {
            _playerInput = playerInput;
            _cameraOrbit = cameraOrbit;

            // 메뉴가 열린 채로 아바타가 늦게 도착하면(스폰이 큐를 거친다) 그 순간부터 게이팅을 건다.
            if (!IsOpen) return;
            if (_playerInput != null) _playerInput.enabled = false;
            if (_cameraOrbit != null) _cameraOrbit.enabled = false;
        }

        public void Toggle()
        {
            if (IsOpen) Close();
            else Open();
        }

        public void Open()
        {
            if (IsOpen) return;

            // 끝난 판은 멈출 것이 없고, 결과 화면의 RETRY/CONTINUE 를 가리기만 한다.
            // 판정을 여기서 다시 하지 않는다 — 싱글이냐 멀티냐에 따라 종료를 아는 경로가 다르고,
            // 그 둘을 이미 아는 것은 결과 화면을 켜는 쪽이다.
            if (_stageFlow != null && _stageFlow.IsOutroShown) return;

            // ⚠ <b>이미 멈춰 있는 화면 위에 또 멈추지 않는다</b>(2026-09-01). 증강 선택도
            // <c>timeScale</c> 과 <see cref="PenguinInputReader"/> 를 잡는데, 그 위에서 이 메뉴를
            // 열었다 닫으면 <c>Close</c> 가 입력을 <b>무조건</b> 켜서 증강 화면이 떠 있는 채로
            // 조작이 살아난다. 정지의 주인이 둘이 되는 것이 문제의 뿌리다.
            //
            // <para>여는 것을 막는 쪽을 골랐다 — 증강은 몇 초짜리 강제 선택이라 고르고 나서 멈추면
            // 되고, 이렇게 두면 "닫을 때 내가 끈 것만 되돌린다" 같은 소유권 추적이 필요 없다.
            // <b>세 번째 주인이 생기면</b> 그때는 참조 계수 게이트를 만들 자리다.</para>
            if (_augmentSelection != null && _augmentSelection.IsOpen) return;

            IsOpen = true;

            if (_playerInput != null) _playerInput.enabled = false;
            if (_cameraOrbit != null) _cameraOrbit.enabled = false;

            // 멀티는 멈추지 않는다. 러너가 이 씬의 것이 아니면(태그가 남아 세션이 새어 들어온 경우)
            // 싱글로 친다 — 그 판정은 StageSession 이 소유한다.
            if (StageSession.For(gameObject).Runner == null) HoldTimeScale();

            ApplyVisibility();
        }

        public void Close()
        {
            if (!IsOpen) return;
            IsOpen = false;

            ReleaseTimeScale();
            if (_playerInput != null) _playerInput.enabled = true;
            if (_cameraOrbit != null) _cameraOrbit.enabled = true;

            ApplyVisibility();
        }

        /// <summary>나가기. 시간을 먼저 되돌린다 — <b>안 되돌리면 메인메뉴가 얼어붙는다.</b>
        /// 씬을 바꾸는 것은 <see cref="RequestStageFlowPresenter.OnContinueRequested"/> 가 이미
        /// 하고 있고, 멀티에서 세션을 먼저 닫는 이유도 거기 적혀 있다.</summary>
        public void Quit()
        {
            ReleaseTimeScale();
            if (_stageFlow != null) _stageFlow.OnContinueRequested();
        }

        private void OnEnable() => ApplyVisibility();

        /// <summary><b>열린 채 꺼지거나 파괴돼도 시간을 되돌린다.</b> 이것이 없으면 메뉴를 연 채 씬이
        /// 바뀌었을 때 다음 씬이 멈춘 채로 뜨고, PlayMode 배치에서는
        /// <c>DisableSceneReload</c> 때문에 그 뒤 테스트가 통째로 이상해진다.</summary>
        private void OnDisable()
        {
            ReleaseTimeScale();
            IsOpen = false;
        }

        private void Update()
        {
            // <b>멀티는 멈추지 않으므로 열어 둔 채로 판이 끝날 수 있다.</b> 그러면 결과 화면이 이
            // 메뉴 밑에 깔려 버튼을 누를 수 없다. 싱글에서는 시간이 멈춰 있어 일어나지 않는다.
            if (IsOpen && _stageFlow != null && _stageFlow.IsOutroShown)
            {
                Close();
                return;
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Toggle();
        }

        private void HoldTimeScale()
        {
            if (_timeScaleHeld) return;

            _timeScaleBeforePause = Time.timeScale;
            _timeScaleHeld = true;
            Time.timeScale = 0f;
        }

        private void ReleaseTimeScale()
        {
            if (!_timeScaleHeld) return;

            _timeScaleHeld = false;
            Time.timeScale = _timeScaleBeforePause;
        }

        /// <summary><c>UIDocument</c> 가 트리를 만들기 전에 <c>OnEnable</c> 이 돌면 Q 가 null 을 준다.
        /// 한 번 캐시하고 끝내면 그 판이 통째로 죽으므로 비어 있을 때마다 다시 찾는다
        /// (<see cref="StageHUDController"/> 와 같은 이유).</summary>
        private bool ResolveElements()
        {
            if (_root != null) return true;

            var document = GetComponent<UIDocument>();
            VisualElement documentRoot = document != null ? document.rootVisualElement : null;
            if (documentRoot == null) return false;

            _root = documentRoot.Q<VisualElement>("pause-root");
            if (_root == null) return false;

            _resumeButton = documentRoot.Q<Button>("action-resume");
            _quitButton = documentRoot.Q<Button>("action-quit");
            if (_resumeButton != null) _resumeButton.clicked += Close;
            if (_quitButton != null) _quitButton.clicked += Quit;
            return true;
        }

        private void ApplyVisibility()
        {
            if (!ResolveElements()) return;
            _root.style.display = IsOpen ? DisplayStyle.Flex : DisplayStyle.None;
        }
    }
}
