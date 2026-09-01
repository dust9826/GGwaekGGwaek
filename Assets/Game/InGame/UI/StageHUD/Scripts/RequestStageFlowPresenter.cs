using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
#if UNITY_EDITOR
using UnityEditor.SceneManagement;
#endif

namespace PPack
{
    /// <summary>새 의뢰 게임의 상태를 기존 Intro/HUD/Outro UI에 연결하는 Presentation coordinator.</summary>
    public sealed class RequestStageFlowPresenter : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private RequestDirector _requestDirector;
        [SerializeField] private StageIntroController _intro;
        [SerializeField] private StageOutroController _outro;
        [SerializeField] private PenguinInputReader _playerInput;
        [SerializeField] private PenguinCameraOrbit _cameraOrbit;
        [SerializeField] private SnowCpuStage _snowStage;

        [Header("Scene Navigation")]
        [SerializeField] private string _retryScenePath =
            "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";
        [SerializeField] private string _mainMenuScenePath =
            "Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity";

        [Tooltip("최고 점수를 저장할 스테이지 키. OutGame 의 PPack.SelectedStage 와 같은 id 를 쓴다.")]
        [SerializeField] private string _stageId = "winter-village";

        [Header("Feedback Hooks")]
        [SerializeField] private UnityEvent _playingStarted = new UnityEvent();
        [SerializeField] private UnityEvent _stageEnded = new UnityEvent();

        private long _initialSnowAmount;

        /// <summary>멀티의 권위·복제 창구. 허브가 스폰될 때 자기를 물린다. 싱글에서는 계속 null 이고,
        /// 그때 이 컴포넌트는 지금까지처럼 혼자 시작하고 혼자 판정한다.</summary>
        private MissionNetHub _mission;
        private bool _hostLeftHandled;

        /// <summary>표시 문자열은 영어다 — StageHUD·StageOutro·나감 알림이 이미 그렇다.</summary>
        private const string HostLeftMessage = "HOST LEFT - RETURNING TO MENU";

        /// <summary>알림을 읽을 시간. 짧으면 왜 튕겼는지 못 읽고, 길면 죽은 판에 붙들린다.</summary>
        private const float HostLeftNoticeSeconds = 2.5f;

        private bool _shownNetworkOutro;

        /// <summary>인트로를 이미 틀었는가. 대기 중이면 매 프레임 조건을 다시 본다.</summary>
        private bool _introStarted;

        /// <summary><b>시작과 판정은 서버만 한다.</b> 클라이언트가 자기 인트로가 끝났다고 게임을
        /// 시작하면 피어마다 다른 시각에 다른 게임이 시작된다. 화면(인트로·결과)은 양쪽 다 그린다.</summary>
        /// <summary>로컬 아바타의 카메라를 물린다. 멀티에서는 씬에 플레이어가 없으므로 스폰된
        /// 아바타가 자기를 넣는다 — 결과 화면에서 커서를 풀 대상이 그것이다.</summary>
        public void BindLocalCameraOrbit(PenguinCameraOrbit cameraOrbit) => _cameraOrbit = cameraOrbit;

        /// <summary>결과 화면이 떠 있는가. <b>싱글과 멀티 모두 이 프레젠터가 그것을 켠다</b>(멀티는
        /// 복제된 <see cref="MissionNetHub.Phase"/>, 싱글은 <see cref="GameManager"/> 를 보고
        /// 켠다). 그래서 "판이 끝났는가" 를 묻는 쪽은 모드를 몰라도 된다 —
        /// <see cref="PauseMenuController"/> 가 이것으로 ESC 를 막는다.</summary>
        public bool IsOutroShown => _outro != null && _outro.gameObject.activeInHierarchy;

        public void BindMission(MissionNetHub mission)
        {
            _mission = mission;
            _shownNetworkOutro = false;
        }

        public void Configure(GameManager gameManager, RequestDirector requestDirector,
            StageIntroController intro, StageOutroController outro, PenguinInputReader playerInput,
            SnowCpuStage snowStage)
        {
            Unsubscribe();
            _gameManager = gameManager;
            _requestDirector = requestDirector;
            _intro = intro;
            _outro = outro;
            _playerInput = playerInput;
            _snowStage = snowStage;
            Subscribe();
        }

        private void OnEnable()
        {
            Subscribe();

            // 정적 이벤트라 해지를 빠뜨리면 씬을 넘어 살아남는다(PlayerLeftAnnouncer 와 같은 이유).
            SessionLauncher.HostDisconnected -= OnHostDisconnected;
            SessionLauncher.HostDisconnected += OnHostDisconnected;
        }

        private void Start()
        {
            if (_playerInput != null) _playerInput.enabled = false;
            if (_snowStage != null && _snowStage.Field != null)
                _initialSnowAmount = _snowStage.TotalHeightMm;

            // 멀티에서는 전원이 씨를 받을 때까지 인트로를 미룬다 — <see cref="TryStartIntro"/> 참고.
            // 허브가 아직 없는 순간에도 뚜어들지 않으므로 여기서 바로 틀지 않는다.
            TryStartIntro();
        }

        /// <summary>인트로를 틀 때가 됐는지 보고, 됐으면 한 번만 틀다.
        ///
        /// <para><b>싱글은 즉시</b> — 기다릴 남이 없다. <b>멀티는 허브의
        /// <see cref="MissionNetHub.AllPeersReady"/> 가 참이 된 뒤</b>에만 튼다. 씨 로드가 끝난 순서대로
        /// 각자 인트로를 돌리면 카운트다운 3-2-1 이 로드 시간 차만큼 어긋난다.</para></summary>
        private void TryStartIntro()
        {
            if (_introStarted || _intro == null) return;
            if (_mission != null && !_mission.AllPeersReady) return;

            _introStarted = true;

            // 멀티는 서버가 찍은 틱부터 이미 흘러간 만큼 앞서 감는다. 신호를 늦게 받은 피어도
            // 카운트다운이 같은 순간에 끝난다. 싱글은 0 이라 예전과 똑같이 처음부터 돌린다.
            float elapsed = _mission != null ? Mathf.Max(0f, _mission.IntroElapsedSeconds) : 0f;
            _intro.Play(elapsed);
        }

        private void OnDisable()
        {
            Unsubscribe();
            SessionLauncher.HostDisconnected -= OnHostDisconnected;
        }

        /// <summary>
        /// <b>호스트가 나가면 판이 끝난다</b>(루트 <c>AGENTS.md</c> 의 결정). 2026-09-01 전까지 그
        /// 결정은 문서에만 있었고 코드에는 없었다 — <c>OnDisconnectedFromServer</c> 가 단계만
        /// <c>Offline</c> 로 바꾸고 끝나서, 클라이언트는 <b>죽은 세션을 들고 게임플레이 씬에 그대로
        /// 서 있었다.</b> 메시지도 씬 전환도 없었다.
        ///
        /// <para>알리고 나서 내보낸다. 곧바로 씬을 바꾸면 왜 튕겼는지 알 수가 없다.
        /// 기다리는 것은 <b>실시간</b>이다 — 일시정지 메뉴가 <c>timeScale</c> 을 잡고 있는 동안
        /// 호스트가 죽으면 스케일된 대기는 영원히 안 끝난다.</para>
        ///
        /// <para>씬 전환은 <see cref="OnContinueRequested"/> 를 그대로 쓴다. 나가는 길을 두 개
        /// 만들지 않는다.</para>
        /// </summary>
        private void OnHostDisconnected()
        {
            if (_hostLeftHandled) return;
            _hostLeftHandled = true;

            WorldMessagePresenter.Post(HostLeftMessage);
            StartCoroutine(ReturnToMenuAfterNotice());
        }

        private IEnumerator ReturnToMenuAfterNotice()
        {
            yield return new WaitForSecondsRealtime(HostLeftNoticeSeconds);
            OnContinueRequested();
        }

        private void Subscribe()
        {
            if (_intro != null)
            {
                _intro.Completed -= OnIntroFinished;
                _intro.Completed += OnIntroFinished;
            }
            if (_gameManager != null)
            {
                _gameManager.GameEnded -= OnGameEnded;
                _gameManager.GameEnded += OnGameEnded;
            }
        }

        private void Unsubscribe()
        {
            if (_intro != null) _intro.Completed -= OnIntroFinished;
            if (_gameManager != null) _gameManager.GameEnded -= OnGameEnded;
        }

        /// <summary>클라이언트는 복제된 페이즈를 따라간다 — 시작 시점도 결과도 서버가 정한 것을 받는다.</summary>
        private void Update()
        {
            // 허브는 씨가 뜨고 난 뒤에 스폰되므로 <c>Start</c> 시점에는 없을 수 있다. 가진 뒤에도
            // 전원이 모일 때까지 기다려야 하므로 조건을 여기서 계속 본다(한 번 틀면 끝난다).
            TryStartIntro();

            if (_mission == null || _mission.HasAuthority) return;

            if (_mission.Phase == EGamePhase.Playing && !_shownNetworkOutro) _playingStarted.Invoke();
            if (_mission.Phase != EGamePhase.Ended || _shownNetworkOutro) return;

            _shownNetworkOutro = true;
            ReleaseCursorForResultScreen();

            // 기록은 기기마다 자기 것이다 — 클라이언트도 자기 PlayerPrefs 에 자기 점수를 남긴다.
            int score = _mission.Score;
            bool isNewRecord = StageHighScore.Submit(_stageId, score);

            if (_outro != null)
            {
                _outro.gameObject.SetActive(true);
                _outro.SetResult(score, StageHighScore.Read(_stageId), isNewRecord,
                                 _mission.SnowClearedPercent, FormatTime(_mission.ElapsedSeconds));
            }

            _stageEnded.Invoke();
        }

        private void OnIntroFinished()
        {
            if (_playerInput != null) _playerInput.enabled = true;

            if (_mission != null)
            {
                // 멀티에서는 시작 권위가 서버에 있다. 클라이언트의 인트로는 화면일 뿐이다.
                _mission.ServerBeginPlaying();
                return;
            }

            if (_gameManager == null || _gameManager.Phase != EGamePhase.Intro) return;
            _gameManager.BeginPlaying();
            _playingStarted.Invoke();
        }

        private void OnGameEnded()
        {
            if (_playerInput != null) _playerInput.enabled = false;
            if (_requestDirector != null) _requestDirector.enabled = false;

            ReleaseCursorForResultScreen();

            int completed = _requestDirector != null ? _requestDirector.CompletedCount : 0;
            int expired = _requestDirector != null ? _requestDirector.ExpiredCount : 0;
            int score = _gameManager != null ? _gameManager.Score : 0;
            long currentSnow = _snowStage != null ? _snowStage.TotalHeightMm : _initialSnowAmount;
            StageMetrics metrics = StageMetrics.Capture(
                completed, expired, score, currentSnow, _initialSnowAmount);

            // 화면이 없어도 기록은 남긴다. 최고 점수는 UI 상태가 아니라 게임 상태다.
            bool isNewRecord = StageHighScore.Submit(_stageId, score);

            if (_outro != null)
            {
                int clearPercent = Mathf.RoundToInt(metrics.SnowClearedPercent01 * 100f);
                float elapsed = _requestDirector != null ? _requestDirector.ElapsedSeconds : 0f;
                _outro.gameObject.SetActive(true);
                _outro.SetResult(score, StageHighScore.Read(_stageId), isNewRecord,
                                 clearPercent, FormatTime(elapsed));
            }

            _stageEnded.Invoke();
        }

        /// <summary>다시 하기. 싱글은 씬을 다시 올리고, 멀티는 서버에게 요청만 보낸다.
        ///
        /// <para>멀티에서 자기 씬만 갈아 끼우면 <b>그 피어만 세션에서 빠지고</b> 나머지는 끝난 판에
        /// 남는다. 씬 전환은 씬 권위만 할 수 있으므로 요청을 보내고 서버가 전원을 한번에 돌린다.
        /// 권한 판정(방장인가)은 허브가 한다 — 여기서 막지 않는 이유는 버튼을 숨기는 것만으로 막은
        /// 상태를 만들지 않기 위해서다.</para></summary>
        public void OnRetryRequested()
        {
            if (_mission != null)
            {
                // 입력 채널로 요청한다 — RPC 는 이 프로젝트에서 MethodAccessException 이 난다
                // (MissionNetHub.PollRestartRequests 주석에 실측 로그가 있다).
                SessionLauncher.RequestRestartMatch();
                return;
            }

            LoadScene(_retryScenePath);
        }

        /// <summary>나가기. 멀티라면 <b>세션을 먼저 닫고</b> 메인메뉴로 간다.
        ///
        /// <para>세션을 닫지 않고 씬만 바꾸면 러너가 살아있는 채로 메인메뉴에 가 다음 접속이 꼬인다.
        /// <see cref="SessionLauncher.Leave"/> 를 기다리지 않고 씬을 바꾸는 것은, 기다리는 동안 화면이
        /// 멈춰 있으면 누른 사람은 먹혔다고 생각하기 때문이다. 종료는 씬이 바뀐 뒤에도 안전하게
        /// 끝난다 — 러너는 <c>DontDestroyOnLoad</c> 에 있다.</para></summary>
        public void OnContinueRequested()
        {
            if (_mission != null) _ = SessionLauncher.Leave();
            LoadScene(_mainMenuScenePath);
        }

        private static void LoadScene(string scenePath)
        {
#if UNITY_EDITOR
            // 에디터 전용 검증 씬을 연결해도 Retry를 확인할 수 있게 한다.
            if (!Application.CanStreamedLevelBeLoaded(scenePath))
            {
                EditorSceneManager.LoadSceneInPlayMode(
                    scenePath, new LoadSceneParameters(LoadSceneMode.Single));
                return;
            }
#endif
            SceneManager.LoadScene(scenePath, LoadSceneMode.Single);
        }

        /// <summary>결과 화면은 버튼을 눌러야 하는데 커서가 잠겨 있으면 누를 수가 없다.
        ///
        /// <para>커서 상태는 <see cref="PenguinCameraOrbit"/> 가 OnEnable/OnDisable 에서 소유하므로
        /// 여기서 <c>Cursor</c> 를 직접 건드리지 않고 그 컴포넌트를 끔다 — 전역 상태에 주인이
        /// 둘이면 반드시 어긋난다.</para>
        ///
        /// <para>참조가 비어 있으면 씨에서 찾는다. 싱글은 씨에 펩귄이 이미 있어 씨 빌더가 이 필드를
        /// 채우지 않았고(멀티만 <see cref="BindLocalCameraOrbit"/> 로 아바타가 자기를 넣는다),
        /// 그 탓에 결과 화면이 뗴는데도 커서가 잠긴 채로 남아 버튼을 누를 수 없었다. 씨를 다시
        /// 생성하지 않고도 기존 씨가 고쳐지도록 여기서 보완한다. 끝날 때 한 번만 도는 탐색이라
        /// 비용은 문제되지 않는다.</para></summary>
        private void ReleaseCursorForResultScreen()
        {
            if (_cameraOrbit == null)
                _cameraOrbit = FindAnyObjectByType<PenguinCameraOrbit>(FindObjectsInactive.Include);

            if (_cameraOrbit != null)
            {
                _cameraOrbit.enabled = false;
                return;
            }

            // 그래도 없으면 커서를 잡은 주인이 없다는 뜻이다. 결과 화면을 못 누르는 것보다는
            // 직접 푸는 편이 낫다.
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
