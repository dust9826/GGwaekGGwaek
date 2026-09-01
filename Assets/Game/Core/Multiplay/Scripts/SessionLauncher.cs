using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Fusion;
using Fusion.Addons.Physics;
using Fusion.Sockets;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// 한 피어의 세션 전체를 소유한다 — 시작, 단계 전이, 씬 로드, 아바타 스폰, 입력 수집.
    ///
    /// <b>피어 하나에 런처 하나다.</b> 서버 피어와 클라이언트 피어는 각자의 <see cref="NetworkRunner"/> 와
    /// 각자의 런처를 갖는다. 그래서 이 클래스에 싱글턴이 없고, UI 가 보는 <see cref="Local"/> 만
    /// 정적이다 — 그것이 <b>이 사람의</b> 플레이어 피어(클라이언트 또는 호스트)다.
    ///
    /// 현재 UI 의 "방 만들기" 는 <b><see cref="GameMode.Host"/></b> 로 연다. 방을 만든 피어가 서버 권위와
    /// 로컬 플레이어 역할을 함께 맡는다. <see cref="HostServerOnly"/> 의 데디 서버 경로는 헤드리스 검증과
    /// MPPM 용으로 그대로 남기며 UI 에서는 사용하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionLauncher : MonoBehaviour, INetworkRunnerCallbacks
    {
        /// <summary>
        /// 게임플레이 씬. 씬 권위(서버)가 <see cref="StartMatch"/> 에서 이것을 올리고 모든 피어가 따라온다.
        /// 빌드 인덱스가 아니라 경로로 잡는다 — 인덱스는 팀원이 빌드 세팅을 건드리면 조용히 어긋난다.
        ///
        /// <para><b>인게임이 넣는다. Core 는 씬 이름을 모른다.</b> 상수로 박아 두면 <c>Core</c> 가
        /// <c>InGame</c> 의 파일 경로를 아는 것이고, 그것은 어셈블리로 막아 둔 경계를 문자열로 우회하는
        /// 것이다(2026-08-24, 제설차 슬라이스를 들어내면서 드러났다). 비어 있으면 로딩에서 멈춘다.</para>
        /// </summary>
        public static string GameplayScenePath { get; set; }

        /// <summary>세션이 열릴 때의 시작 씬. <c>PeerMode = Multiple</c> 검증에서만 인자로 쓴다.</summary>
        private const string MenuScenePath = "Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity";

        /// <summary>
        /// 아바타 프리팹. 런처가 런타임에 생기므로 인스펙터로 물릴 수 없어 <c>Resources</c> 로 찾는다.
        /// </summary>
        /// <summary>
        /// 플레이어 아바타 프리팹 이름. <b>게임플레이 씬이 정한다.</b> 기본값은 없다 —
        /// 전에는 <c>"PF_MultiplayPlow"</c> 로 떨어졌는데, 그러면 씬이 아무것도 넣지 않았을 때
        /// 조용히 제설차가 스폰되고 원인이 씬에서 멀리 보인다. 비어 있으면 스폰하지 않고 에러를 남긴다.
        ///
        /// <para>씬에 직렬화 필드로 두지 못하는 이유: 런처는 <see cref="StartPeer"/> 가 코드로
        /// 만드는 오브젝트라서 인스펙터가 없다.</para>
        /// </summary>
        public static string AvatarResourcePath
            => AvatarResourceOverride ?? SceneAvatarResource;

        /// <summary>씬이 고른 아바타. 게임플레이 씬의 부트스트랩이 <c>Awake</c> 에서 넣는다.</summary>
        public static string SceneAvatarResource { get; set; }

        /// <summary>
        /// 씬이 고른 스폰 자세들. 아바타 이름과 같은 이유로 <b>씬이 넣는다</b> — 마을 좌표를 여기
        /// 상수로 두면 <c>Core</c> 가 맵을 아는 것이 된다. 비어 있으면 원점 둘레의 원으로 떨어지는데,
        /// 그것은 빈 샌드박스 씬에서만 맞는 자리다.
        /// </summary>
        public static Pose[] SceneSpawnPoses { get; set; }

        /// <summary>
        /// <b>명시 요구가 씬을 이긴다.</b> 차량 동기화 검증처럼 "이 세션은 차량이어야 한다" 가
        /// 테스트의 전제인 경우가 있고, 그 전제는 씬이 무엇을 들고 있든 성립해야 한다. 씬이
        /// <c>Awake</c> 에서 값을 넣는 시점은 서버가 씬을 로드한 뒤이므로, 우선순위 없이 두면
        /// 테스트가 먼저 정해도 씬이 덮어쓴다(실측: 그래서 차량 테스트가 "입력이 서버까지 오지
        /// 않았다" 로 죽었다 - 차량이 아예 없었다).
        /// </summary>
        public static string AvatarResourceOverride { get; set; }

        /// <summary>
        /// 로컬 플레이어의 입력을 만드는 곳. <b><c>InGame</c> 이 채운다</b> — <c>Core</c> 는 펭귄의
        /// 입력 액션(<c>PenguinControls.inputactions</c>)을 알 수 없다. 어셈블리가 그것을 컴파일
        /// 에러로 만든다.
        ///
        /// <para><b>왜 뒤집었나(2026-08-24).</b> 전에는 <see cref="OnInput"/> 이 <c>Keyboard.current</c>
        /// 를 직접, 그것도 하드코딩한 키로 읽었다. 그러면 키 바인딩이 인풋 액션 에셋과 이 함수
        /// <b>두 곳</b>에 살고, 싱글과 멀티의 조작이 조용히 갈린다.</para>
        ///
        /// <para>비어 있으면 입력이 나가지 않는다 — 아바타가 스폰되기 전이 정확히 그 상태다.</para>
        /// </summary>
        public static System.Func<NetworkInputData> LocalInputSource { get; set; }


        /// <summary>
        /// 로비 상태 오브젝트. 서버가 세션을 열자마자 스폰하고, 방장·인원·시작 권한이 여기 있다.
        /// </summary>
        private const string LobbyResourcePath = "PF_SessionLobby";

        /// <summary>최대 플레이어 수. 호스트 모드에서는 방을 만든 플레이어도 여기에 포함된다.</summary>
        public const int MaxPlayers = 4;

        /// <summary>
        /// <c>PlayerId</c> 의 상한. <b><see cref="MaxPlayers"/> 와 다르고, 달라야 한다</b> —
        /// id 는 사람이 나가도 재사용되지 않으므로 동시 인원보다 커질 수 있다. <c>PlayerId</c> 로
        /// 색인하는 배열은 전부 이 값을 용량으로 쓴다(로비 닉네임, 증강 표).
        ///
        /// <para>4로 잡으면 다섯 번째로 접속한 사람의 자리가 <b>조용히 버려진다</b> — 에러도 경고도
        /// 나지 않는다. <c>SessionLobby</c> 가 처음부터 8을 쓴 이유가 그것이고, 두 번째 소비자가
        /// 생겨서 여기로 올렸다.</para>
        /// </summary>
        public const int MaxPlayerId = 8;

        /// <summary>이 사람의 플레이어 피어. 클라이언트와 플레이어 호스트가 여기에 들어온다.</summary>
        public static SessionLauncher Local { get; private set; }

        /// <summary>이 프로세스의 서버 권위 피어. 데디 서버와 플레이어 호스트가 여기에 들어온다.</summary>
        public static SessionLauncher LocalServer { get; private set; }

        /// <summary>단계가 바뀔 때 부른다. 인자는 바뀐 뒤의 단계다.</summary>
        public static event Action<ESessionPhase> PhaseChanged;

        /// <summary>이 사람이 보는 단계. 플레이어 피어가 없으면 서버 권위 피어의 단계를 본다.</summary>
        /// <remarks>
        /// 클라이언트 피어가 없으면 <b>서버 피어의 단계</b>를 준다. MPPM 전환 이후 메인 에디터는 서버만
        /// 띄우므로, 클라이언트만 보면 그 인스턴스의 로비 UI 가 영원히 <c>Offline</c> 을 읽는다.
        /// </remarks>
        public static ESessionPhase Phase => Local != null ? Local._phase
                                           : LocalServer != null ? LocalServer._phase
                                           : ESessionPhase.Offline;

        /// <summary>
        /// UI 가 START 를 눌렀을 때 세운다. 몇 틱 동안 입력에 실려 서버로 가고, 서버가 방장 여부를
        /// 판정한다. 한 틱만 보내면 패킷 손실로 사라질 수 있어 짧은 시간 동안 반복해서 보낸다.
        /// </summary>
        private static float _startRequestUntil;

        /// <summary>매치 시작을 서버에 요청한다. 실제 판정은 <see cref="SessionLobby"/> 가 한다.</summary>
        public static void RequestStartMatch() => _startRequestUntil = Time.realtimeSinceStartup + 0.5f;

        /// <inheritdoc cref="_startRequestUntil"/>
        private static float _restartRequestUntil;

        /// <summary>같은 씬으로 다시 시작해 달라고 서버에 요청한다. 권한 판정은
        /// <c>MissionNetHub</c> 가 한다. 시작 요청과 같은 이유로 RPC 를 쓰지 않는다.</summary>
        public static void RequestRestartMatch() => _restartRequestUntil = Time.realtimeSinceStartup + 0.5f;

        /// <summary>
        /// 마지막으로 피어를 시작하지 못한 이유. <b>화면이 사람에게 보여 줄 문장이다</b> —
        /// "접속이 안 된다" 만 남으면 다음에 무엇을 할지 알 수 없다. <c>GameNotFound</c> 는
        /// "아직 방이 없다(호스트보다 먼저 눌렀다)" 이고, 설정 로드 실패는 전혀 다른 문제다.
        /// </summary>
        public static string LastStartFailure { get; private set; } = string.Empty;

        /// <summary>세션의 방 코드. 매치메이킹 키다.</summary>
        public static string RoomCode { get; private set; } = string.Empty;

        /// <summary>
        /// 이 사람이 적어 넣은 이름. <b><c>OutGame</c> 의 로비 UI 가 채운다</b> —
        /// <c>Core</c> 는 입력 칸도 기본값도 모른다(<see cref="GameplayScenePath"/> 와 같은 방향).
        /// 비어 있으면 아무것도 보내지 않고 화면은 <c>#id</c> 로 떨어진다.
        /// </summary>
        public static string LocalNickname { get; set; } = string.Empty;

        /// <summary>
        /// 이 판에 있는 사람들의 이름. <b>왜 여기 두는가</b>(2026-09-01 실측) —
        /// <see cref="SessionLobby"/> 는 게임플레이 씬이 올라오면 사라진다. 그래서 인게임에서는
        /// 로비를 읽을 수 없고, 이름이 필요한 곳(나감 알림)이 바로 거기다.
        ///
        /// <para>런처는 <c>DontDestroyOnLoad</c> 라 씬을 넘어 산다. <see cref="ExpectedPlayerCount"/>
        /// 가 같은 이유로 여기 있다 — <b>"이 판의 사람들" 은 세션 계층의 사실</b>이고 씬보다 오래 산다.</para>
        ///
        /// <para>복제하지 않는다. 로비가 살아 있는 동안 각 피어가 복제된 값을 그대로 베껴 둔다.</para>
        /// </summary>
        private static readonly Dictionary<int, string> _playerNames = new Dictionary<int, string>();

        /// <summary>로비가 본 이름을 적어 둔다. 빈 이름은 무시한다 — 아직 안 온 것과 지우는 것은 다르다.</summary>
        public static void RememberPlayerName(int playerId, string nickname)
        {
            if (playerId < 0 || string.IsNullOrWhiteSpace(nickname)) return;
            _playerNames[playerId] = nickname.Trim();
        }

        /// <summary>그 사람의 이름. 모르면 빈 문자열이다.</summary>
        public static string NameOf(int playerId) =>
            _playerNames.TryGetValue(playerId, out string name) ? name : string.Empty;

        /// <summary>
        /// 이 프로세스가 띄운 모든 피어(서버·호스트·클라이언트). 스폰 순서대로 들어간다.
        /// 멀티피어 검증이 피어별 상태를 직접 읽고, <see cref="Leave"/> 가 전부 내리는 데 쓴다.
        /// </summary>
        /// <summary>
        /// <b>Play 를 시작할 때마다 static 을 비운다.</b>
        ///
        /// <para>이 프로젝트는 <c>Enter Play Mode Options</c> 가 <c>DisableDomainReload</c> 다(빠른 반복을
        /// 위해). 그러면 Play 를 끝내도 <see cref="LocalServer"/> · <see cref="Local"/> · <see cref="_peers"/>
        /// 가 <b>지난 세션의 값을 그대로 들고 있다.</b> 그 상태로 다시 Play 하면 <see cref="StartPeer"/> 의
        /// "이미 서버 피어가 있다" 검사에 걸려 <b>방을 만들지 못한다</b> - 증상은 "포톤 연결 실패" 처럼
        /// 보이지만 네트워크는 손대지도 않았다.</para>
        ///
        /// <para>실측(2026-08-20): 에디터를 새로 띄운 뒤 첫 Play 는 붙고, 그 다음부터는 전부 실패했다.
        /// 에디터를 다시 띄우면 또 한 번 된다 - 도메인 리로드가 그때만 일어나기 때문이다.</para>
        /// </summary>
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _peers.Clear();
            Local = null;
            LocalServer = null;
            RoomCode = string.Empty;
            TestInputSource = null;

            // 정적 이벤트도 지난 Play 의 구독을 들고 있다. 구독자는 OnDisable 에서 해지하지만,
            // 씬이 통째로 사라지는 경로에서는 그것이 불리지 않을 수 있다 — 그러면 다음 판이
            // 죽은 오브젝트를 부른다.
            PlayerLeft = null;
            HostDisconnected = null;
            LocalNickname = string.Empty;
            _playerNames.Clear();
            AugmentPickInputRelay.Reset();
        }

        private static readonly List<SessionLauncher> _peers = new List<SessionLauncher>();

        /// <inheritdoc cref="_peers"/>
        public static IReadOnlyList<SessionLauncher> Peers => _peers;

        /// <summary>이 피어가 서버 권위를 갖는지. 데디 서버와 호스트 모두 참이다.</summary>
        public bool IsServerPeer => _isServerPeer;

        private readonly List<PlayerRef> _players = new List<PlayerRef>();

        private NetworkRunner _runner;
        private ESessionPhase _phase = ESessionPhase.Offline;

        private bool _isServerPeer;

        /// <summary>이 런처의 런너. 테스트가 상태를 직접 읽을 때 쓴다.</summary>
        public NetworkRunner Runner => _runner;

        /// <summary>세션에 붙어 있는 플레이어 수. 서버 권위 피어에서만 정확하다.</summary>
        public int PlayerCount => _players.Count;

        /// <summary>매치를 시작한 순간에 로비에 있던 사람 수. 게임플레이 쎌이 "전원" 을 세는 기준이다.
        ///
        /// <para>0 이면 아직 잠기지 않았다는 뜻이다(싱글플레이는 계속 0). 이 값은 <b>서버에서만</b>
        /// 의미가 있다 — 판정은 서버가 하고 결과만 복제되므로 클라이언트가 읽을 이유가 없다.</para></summary>
        public static int ExpectedPlayerCount { get; private set; }

        /// <summary>이 피어의 단계.</summary>
        public ESessionPhase PeerPhase => _phase;

        // ---------------------------------------------------------------- 시작

        /// <summary>
        /// UI 에서 방을 만든다. 방을 만든 사람이 서버 권위와 로컬 플레이어 역할을 함께 맡는다.
        /// </summary>
        public static Task<bool> HostRoom(string roomCode) => HostAsPlayer(roomCode);

        /// <summary>방을 만든 사람도 플레이어로 참가하는 호스트 피어를 띄운다.</summary>
        public static Task<bool> HostAsPlayer(string roomCode) => StartPeer(GameMode.Host, roomCode);

        /// <summary>
        /// 플레이어가 없는 서버 피어만 띄운다. UI 출시 경로에서는 현재 사용하지 않지만,
        /// 헤드리스 검증·MPPM 서버 역할과 향후 데디 운영을 위해 유지한다.
        /// </summary>
        public static Task<bool> HostServerOnly(string roomCode) => StartPeer(GameMode.Server, roomCode);

        /// <summary>이미 있는 방에 이 사람의 클라이언트 피어로 붙는다.</summary>
        public static Task<bool> JoinRoom(string roomCode) => StartPeer(GameMode.Client, roomCode);

        private static async Task<bool> StartPeer(GameMode mode, string roomCode)
        {
            // 한 프로세스에 피어는 하나다(MPPM 전환 이후). 버튼을 두 번 누르거나 자동 시작과 사람이
            // 겹치면 같은 방에 서버가 둘 생기고 무엇이 씬 권위인지 알 수 없게 되므로 막는다.
            bool hasServerAuthority = mode == GameMode.Server || mode == GameMode.Host;
            bool isPlayer = mode == GameMode.Client || mode == GameMode.Host;

            if (hasServerAuthority && LocalServer != null)
            {
                Debug.LogWarning($"{nameof(SessionLauncher)}: 이미 서버 피어가 있다 - 방 만들기를 무시한다.");
                return false;
            }

            // <b>Fusion 설정을 피어를 만들기 전에 읽는다.</b> 전에는 아래 StartGameArgs 초기화 안에서
            // 읽었는데, <c>NetworkProjectConfig.Global</c> 은 설정 자산을 못 불러오면 <b>예외를 던진다</b>.
            // 그러면 이 메서드가 <c>Task&lt;bool&gt;</c> 계약을 깨고 예외로 빠져나가고, 호출부인
            // <c>OutGameScreenController.JoinRoomAsync</c> 가 <c>async void</c> 라 그 예외는
            // 아무도 못 받는다 — 상태 라벨조차 안 바뀌어 <b>화면에는 단서 없이 "접속이 안 된다" 만
            // 남는다</b>. 실제로 그렇게 죽었다(2026-08-29, MPPM 클론 로그).
            //
            // 원인은 이 코드가 아니라 MPPM 이다. 클론은 자기 <c>ArtifactDB</c> 가 없고 AssetDatabase 가
            // 읽기 전용이라 본체의 아티팩트를 읽어 쓴다. 그런데 Fusion 의 <c>FusionCustomDependency</c> 는
            // 클론에서도 돌면서 <c>AssetDatabase.RegisterCustomDependency</c> 를 부르고, 그것이
            // <c>NetworkProjectConfig.fusion</c> 아티팩트를 낡은 것으로 만든다. 클론은 그것을 다시 굽지
            // 못한다("Asset Database is set to Read Only, but it has found out-of-date assets").
            // 의존성 넷(PrefabsDependency·ScriptOrderDependency·NetworkObjectPostprocessor·
            // ILWeaverTriggerImporter/ConfigHash)은 <b>재컴파일이나 네트워크 프리팹 표 변경마다</b>
            // 해시가 바뀌므로, 클론이 떠 있는 동안 본체가 컴파일하면 그 클론은 그때부터 못 붙는다.
            //
            // 여기서 고칠 수 있는 것은 원인이 아니라 <b>진단 가능성</b>이다. 빌드는 이 경로를 타지
            // 않는다 — 플레이어에서는 자산이 Resources 에 구워져 들어가고 임포터가 돌지 않는다.
            NetworkProjectConfig.PeerModes peerMode;
            try
            {
                peerMode = NetworkProjectConfig.Global.PeerMode;
            }
            catch (Exception ex)
            {
                LastStartFailure = "Fusion 설정을 읽지 못했다";
                Debug.LogError(
                    $"{nameof(SessionLauncher)}: Fusion 설정(NetworkProjectConfig)을 읽지 못해 {mode} 를 " +
                    "시작할 수 없다. MPPM 클론이라면 클론을 만지지 말고 <b>본체 에디터를 재시작</b>한다 — " +
                    "클론만 다시 띄우거나 클론 폴더를 지워도 낫지 않는 것을 실측했다" +
                    "(Core/Multiplay/AGENTS.md). " +
                    $"({ex.GetType().Name}: {ex.Message})");
                return false;
            }

            RoomCode = string.IsNullOrWhiteSpace(roomCode) ? "PPACK" : roomCode.Trim().ToUpperInvariant();

            int peerIndex = _peers.Count;
            string peerName = mode == GameMode.Host ? "SessionPeer_Host"
                : hasServerAuthority ? "SessionPeer_Server"
                : $"SessionPeer_Client{peerIndex}";
            var go = new GameObject(peerName);
            DontDestroyOnLoad(go);

            var runner = go.AddComponent<NetworkRunner>();
            // 서버만 씬 권위를 갖지만, 클라이언트도 따라 로드해야 하므로 씬 매니저는 양쪽 다 필요하다.
            go.AddComponent<NetworkSceneManagerDefault>();

            // 물리를 <b>Fusion 틱</b>에서 돌린다. 없으면 Unity 의 FixedUpdate 가 돌리는데, 그것은
            // Fusion 의 시계와 다르다 — 힘은 FixedUpdateNetwork 에서 쌓고 적분은 FixedUpdate 에서
            // 하게 되어 스텝마다 받는 힘의 개수가 달라진다(0.02s 물리 vs Fusion 틱).
            //
            // 그리고 <b>PeerMode = Multiple 에서는 물리가 아예 돌지 않는다.</b> 그 모드에서 Fusion 은
            // 피어마다 LocalPhysicsMode 씬을 만드는데(NetworkSceneManagerDefault), Unity 는 로컬 물리
            // 씬을 자동 시뮬레이션에서 제외한다. Simulate() 를 불러 줄 주체가 이것뿐이다.
            //
            // 전역 Physics.simulationMode 를 Script 로 바꾸지만, 애드온이 런너 수를 세어 마지막
            // 런너가 내려갈 때 원래 값으로 되돌린다 — 런너 없는 싱글플레이는 영향받지 않는다.
            // <b>StartGame 전에</b> 붙여야 Fusion 이 시작 과정에서 집어간다.
            var physics = go.AddComponent<RunnerSimulatePhysics>();
            physics.Update3DPhysicsScene = true;

            var launcher = go.AddComponent<SessionLauncher>();
            launcher._runner = runner;
            launcher._isServerPeer = hasServerAuthority;
            runner.ProvideInput = isPlayer;
            runner.AddCallbacks(launcher);

            _peers.Add(launcher);
            if (hasServerAuthority) LocalServer = launcher;
            if (isPlayer && Local == null) Local = launcher;

            launcher.SetPhase(ESessionPhase.Matchmaking);

            StartGameResult result = await runner.StartGame(new StartGameArgs
            {
                GameMode = mode,
                SessionName = RoomCode,
                PlayerCount = MaxPlayers,
                // 로비 단계에서는 씬을 바꾸지 않는다. Single 피어에서는 이것이 허용된다.
                // <b>Multiple 은 씬을 요구한다</b>(Fusion 이 명시적으로 거부한다) — 한 프로세스에 피어가
                // 여럿이면 피어마다 씬 사본이 필요하기 때문이다. 그 구성은 검증에서만 쓰므로, 그때만
                // 시작 씬(메뉴)을 넘긴다. 게임플레이 씬을 넘기면 로비를 건너뛰고 바로 들어가 버린다.
                Scene = peerMode == NetworkProjectConfig.PeerModes.Multiple
                    ? BuildSceneRef(MenuScenePath)
                    : null,
            });

            if (!result.Ok)
            {
                LastStartFailure = result.ShutdownReason.ToString();
                Debug.LogError($"{nameof(SessionLauncher)}: {mode} 시작 실패 - {result.ShutdownReason}");
                launcher.SetPhase(ESessionPhase.Offline);
                _peers.Remove(launcher);
                Destroy(go);
                if (hasServerAuthority && LocalServer == launcher) LocalServer = null;
                if (isPlayer && Local == launcher) Local = null;
                return false;
            }

            launcher.SetPhase(ESessionPhase.Lobby);

            // 로비 상태(방장·인원·시작 권한)는 서버가 소유한다. 클라이언트는 스폰된 것을 읽기만 한다.
            if (hasServerAuthority) launcher.SpawnLobby(runner);

            return true;
        }

        /// <summary>
        /// 로비 상태 오브젝트를 띄운다. 이것이 없으면 방장 개념이 없고, 그러면 <b>사람이 없는 데디
        /// 서버에서는 아무도 매치를 시작할 수 없다</b> — 시작 권한이 프로세스가 아니라 세션 안의 역할이어야
        /// 하는 이유가 그것이다.
        /// </summary>

        /// <summary>빌드 세팅의 씬 경로를 <see cref="SceneRef"/> 로 바꾼다. 인덱스를 직렬화해 두지 않는 이유는
        /// 팀원이 빌드 세팅을 건드릴 때 조용히 어긋나기 때문이다(폴더 규칙).</summary>
        private static SceneRef? BuildSceneRef(string scenePath)
        {
            int index = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (index >= 0) return SceneRef.FromIndex(index);

            Debug.LogError($"{nameof(SessionLauncher)}: {scenePath} 가 빌드 세팅에 없다.");
            return null;
        }

        private void SpawnLobby(NetworkRunner runner)
        {
            if (SessionLobby.Instance != null) return;

            var prefab = Resources.Load<NetworkObject>(LobbyResourcePath);
            if (prefab == null)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: Resources 에 {LobbyResourcePath} 가 없다.");
                return;
            }

            runner.Spawn(prefab, Vector3.zero, Quaternion.identity);
        }

        /// <summary>
        /// 게임플레이로 넘어간다. <b>씬 권위만</b> 할 수 있으므로 서버 피어가 호출한다 —
        /// 클라이언트는 자기가 로드하지 않고 서버가 올린 씬을 따라온다.
        /// </summary>
        public static async Task<bool> StartMatch()
        {
            SessionLauncher server = LocalServer;
            if (server == null || server._runner == null || !server._runner.IsRunning)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: 서버 피어가 없어 매치를 시작할 수 없다.");
                return false;
            }

            // <b>이 판의 인원을 여기서 잠근다.</b> 게임플레이 쎌은 "전원이 준비됐는가" 를 판정해야
            // 하는데, 그때 <c>Runner.ActivePlayers</c> 를 세면 <b>분모가 같이 늘어나 항상 참이 된다</b> —
            // 실측으로 2인이 붙을 판인데 먼저 붙은 1명만으로 인트로가 시작됐고, 늤에 온 사람은
            // 이미 지나간 판에 합류했다. 시작 버튼을 누른 순간의 인원이 곳 그 판의 인원이다.
            ExpectedPlayerCount = server._players.Count;

            if (string.IsNullOrEmpty(GameplayScenePath))
            {
                Debug.LogError($"{nameof(SessionLauncher)}: 게임플레이 씬 경로가 정해지지 않았다. " +
                               "인게임 부트스트랩이 GameplayScenePath 를 넣어야 한다.");
                return false;
            }

            int index = SceneUtility.GetBuildIndexByScenePath(GameplayScenePath);
            if (index < 0)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: {GameplayScenePath} 가 빌드 세팅에 없다.");
                return false;
            }

            await server._runner.LoadScene(SceneRef.FromIndex(index), LoadSceneMode.Single);
            return true;
        }

        /// <summary>
        /// 빌드 세팅의 경로로 <see cref="NetworkSceneInfo"/> 를 만든다. 인덱스를 직렬화해 두지 않는
        /// 이유는 팀원이 빌드 세팅 순서를 바꾸면 조용히 다른 씬이 올라가기 때문이다.
        /// 경로가 빌드 세팅에 없으면 <c>null</c> 을 돌려주고 에러를 남긴다 — 그 상태로 Fusion 에
        /// 넘기면 "requires a scene to be set" 이라는 딴 얘기로 실패해 원인을 찾기 어려워진다.
        /// </summary>
        private static NetworkSceneInfo? BuildSceneInfo(string scenePath)
        {
            int index = SceneUtility.GetBuildIndexByScenePath(scenePath);
            if (index < 0)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: {scenePath} 가 빌드 세팅에 없다.");
                return null;
            }

            var info = new NetworkSceneInfo();
            info.AddSceneRef(SceneRef.FromIndex(index), LoadSceneMode.Single);
            return info;
        }

        /// <summary>같은 게임플레이 씨를 다시 올려 전원을 처음으로 되돌린다.
        ///
        /// <para><b>씨 권위만 할 수 있다</b> — <see cref="StartMatch"/> 와 같은 이유다. 한 피어가
        /// 혼자 <c>LoadScene</c> 을 하면 그 피어만 세션에서 빠진다.</para>
        ///
        /// <para>씨를 다시 올리면 씨 안의 것은 전부 새로 생기고 아바타도 <c>OnSceneLoadDone</c> 에서
        /// 다시 스폰된다. 인원은 <b>재시작 시점 기준으로 다시 잠근다</b> — 중간에 나간 사람을
        /// 기다리며 인트로가 멈춤다.</para></summary>
        public static async Task<bool> RestartMatch()
        {
            SessionLauncher server = LocalServer;
            if (server == null || server._runner == null || !server._runner.IsRunning)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: 서버 피어가 없어 다시 시작할 수 없다.");
                return false;
            }

            int index = SceneUtility.GetBuildIndexByScenePath(GameplayScenePath);
            if (index < 0)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: {GameplayScenePath} 가 빌드 세팅에 없다.");
                return false;
            }

            ExpectedPlayerCount = server._players.Count;
            await server._runner.LoadScene(SceneRef.FromIndex(index), LoadSceneMode.Single);
            return true;
        }

        /// <summary>세션을 닫는다. 이 프로세스의 모든 피어(검증용 클라이언트까지)가 내려간다.</summary>
        public static async Task Leave()
        {
            SessionLauncher[] peers = _peers.ToArray();
            _peers.Clear();
            Local = null;
            LocalServer = null;
            ExpectedPlayerCount = 0;

            foreach (var peer in peers)
            {
                if (peer == null) continue;
                if (peer._runner != null) await peer._runner.Shutdown();
                if (peer != null) Destroy(peer.gameObject);
            }
        }

        private void SetPhase(ESessionPhase phase)
        {
            if (_phase == phase) return;
            _phase = phase;

            // UI 는 이 사람의 플레이어 피어(클라이언트 또는 호스트)를 따라간다. 플레이어 피어가 없는
            // 데디 서버 인스턴스에서만 서버 피어가 직접 단계를 알린다.
            if (this == Local || (Local == null && _isServerPeer)) PhaseChanged?.Invoke(phase);
        }

        // ------------------------------------------------------------ 콜백

        public void OnPlayerJoined(NetworkRunner runner, PlayerRef player)
        {
            if (!_players.Contains(player)) _players.Add(player);
            if (!runner.IsServer) return;

            // 스폰은 서버만 한다. 게임플레이 씬이 아직이면 씬이 뜬 뒤에 스폰한다.
            if (_phase == ESessionPhase.Playing) SpawnAvatar(runner, player);
        }

        /// <summary>
        /// 누군가 세션에서 빠졌다. <b>전 피어에서 발생한다</b> — 아래 despawn 은 서버만 하지만 이
        /// 콜백 자체는 Fusion 이 모두에게 부르므로, 각자 자기 화면에 알릴 수 있다.
        ///
        /// <para><b>여기서 UI 를 부르지 않는다.</b> <c>Core</c> 는 <c>InGame</c> 을 참조할 수 없고,
        /// 그 전에 <b>세션 계층이 화면을 알아야 할 이유가 없다</b>. 이것은 사실이고, 그것을 토스트로
        /// 볼지는 읽는 쪽이 정한다(<c>PhaseChanged</c> 와 같은 방향).</para>
        /// </summary>
        public static event Action<PlayerRef> PlayerLeft;

        public void OnPlayerLeft(NetworkRunner runner, PlayerRef player)
        {
            _players.Remove(player);
            PlayerLeft?.Invoke(player);
            if (!runner.IsServer) return;

            if (runner.TryGetPlayerObject(player, out NetworkObject avatar) && avatar != null)
            {
                runner.Despawn(avatar);
            }
        }

        /// <summary>
        /// 검증이 입력을 대신 만들어 넣는 자리. <c>null</c> 이면 평소처럼 키보드를 읽는다.
        ///
        /// <para>왜 필요한가: 동기화 검증은 <b>차가 실제로 움직여야</b> 의미가 있고, 자동화된 실행에는
        /// 키보드가 없다(배치모드에는 장치 자체가 없다). 입력 시스템에 가짜 키 이벤트를 밀어 넣는 방법도
        /// 있지만 그것은 포커스와 프레임 타이밍에 흔들려 <b>재현되지 않는 검증</b>이 된다. 여기서는
        /// 무엇을 보냈는지가 코드에 그대로 남는다.</para>
        ///
        /// <para>피어를 인자로 준다 — 한 프로세스에 클라이언트가 둘일 때 서로 다른 입력을 넣어야
        /// "각자 자기 차선만 깎았다"를 구분할 수 있다.</para>
        /// </summary>
        public static Func<SessionLauncher, NetworkInputData?> TestInputSource;

        /// <summary>검증용 계수기 — 입력을 몇 번 만들어 보냈는가(클라이언트 쪽).</summary>
        public static int DebugInputsProvided;

        public void OnInput(NetworkRunner runner, NetworkInput input)
        {
            if (TestInputSource != null)
            {
                NetworkInputData? injected = TestInputSource(this);
                if (injected.HasValue)
                {
                    DebugInputsProvided++;
                    input.Set(injected.Value);
                    return;
                }
            }

            // 로컬 입력은 InGame 이 만든다(LocalInputSource). 여기서 키를 읽지 않는다.
            NetworkInputData data = LocalInputSource != null ? LocalInputSource() : default;

            data.Buttons.Set((int)EInputButton.CoopShoveSuccess, CoopShoveInputRelay.SuccessActive);
            data.Buttons.Set((int)EInputButton.CoopShoveFailure, CoopShoveInputRelay.FailureActive);

            // 증강 카드도 화면이 세운다. 비트가 셋이라 카드 수가 3으로 고정된다.
            int augmentPick = AugmentPickInputRelay.ActiveIndex;
            data.Buttons.Set((int)EInputButton.AugmentPick0, augmentPick == 0);
            data.Buttons.Set((int)EInputButton.AugmentPick1, augmentPick == 1);
            data.Buttons.Set((int)EInputButton.AugmentPick2, augmentPick == 2);

            // 매치 시작 요청은 키보드가 아니라 UI 가 세운다.
            data.Buttons.Set((int)EInputButton.RequestStartMatch,
                             Time.realtimeSinceStartup < _startRequestUntil);
            data.Buttons.Set((int)EInputButton.RequestRestartMatch,
                             Time.realtimeSinceStartup < _restartRequestUntil);

            input.Set(data);
        }

        /// <summary>
        /// 씬 로드가 시작됐다. <b>세션을 여는 순간의 로드는 로딩이 아니다</b> — <c>StartGame</c> 이
        /// 요구하는 빈 세션 루트를 올리는 것이고, 사람이 보는 화면은 여전히 로비다. 그때 단계를
        /// <see cref="ESessionPhase.Loading"/> 로 바꾸면 로비에 "LOADING GAMEPLAY..." 가 뜬다.
        /// 매치 시작으로 올라오는 로드는 단계가 이미 <see cref="ESessionPhase.Lobby"/> 일 때만 온다.
        /// </summary>
        public void OnSceneLoadStart(NetworkRunner runner)
        {
            if (_phase != ESessionPhase.Lobby) return;
            SetPhase(ESessionPhase.Loading);
        }


        public void OnSceneLoadDone(NetworkRunner runner)
        {
            SetPhase(ESessionPhase.Playing);

            if (!runner.IsServer) return;



            // 씬이 올라온 다음에야 스폰할 수 있다. 로비에서 기다리던 사람들을 여기서 한 번에 넣는다.
            foreach (var player in _players)
            {
                if (runner.TryGetPlayerObject(player, out NetworkObject existing) && existing != null) continue;
                SpawnAvatar(runner, player);
            }
        }

        /// <summary>주인 없는 씬 오브젝트를 하나 스폰한다. 실패는 조용히 넘기지 않고 이름과 함께 남긴다.</summary>
        private static NetworkObject SpawnFromResources(NetworkRunner runner, string resourcePath)
        {
            var prefab = Resources.Load<NetworkObject>(resourcePath);
            if (prefab == null)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: Resources 에 {resourcePath} 가 없다.");
                return null;
            }

            // 큐에 들어가면 null 이 온다. 그 경우 호출자가 다음 콜백에서 다시 시도한다.
            return runner.Spawn(prefab, Vector3.zero, Quaternion.identity);
        }

        private void SpawnAvatar(NetworkRunner runner, PlayerRef player)
        {
            string resource = AvatarResourcePath;
            if (string.IsNullOrEmpty(resource))
            {
                Debug.LogError($"{nameof(SessionLauncher)}: 아바타 리소스가 정해지지 않았다. " +
                               "게임플레이 씬이 SceneAvatarResource 를 넣어야 한다.");
                return;
            }

            var prefab = Resources.Load<NetworkObject>(resource);
            if (prefab == null)
            {
                Debug.LogError($"{nameof(SessionLauncher)}: Resources 에 {resource} 가 없다.");
                return;
            }

            ResolveSpawnPose(Mathf.Max(_players.IndexOf(player), 0), out Vector3 position, out Quaternion rotation);

            // <b>반환값으로 매핑하지 않는다.</b> 프리팹 로드가 끝나지 않았으면 Fusion 이 스폰을 큐에 넣고
            // <c>null</c> 을 준다(<c>EnqueueIncompleteSynchronousSpawns</c>). 그때 <c>SetPlayerObject(null)</c>
            // 을 부르면 매핑이 비고, 다음 틱에 "매핑이 없으니 또 스폰" 으로 차가 둘이 된다.
            // 매핑은 차량이 스폰될 때 자기가 한다 - 큐를 거쳐도 정확히 한 번이다.
            runner.Spawn(prefab, position, rotation, player);
        }

        /// <summary>씬이 준 자세를 슬롯 순서대로 쓰고, 없으면 원점 둘레의 원에 세운다.
        /// 겹쳐 스폰하면 둘이 하나로 보여 복제를 확인할 수 없다.</summary>
        private static void ResolveSpawnPose(int slot, out Vector3 position, out Quaternion rotation)
        {
            Pose[] poses = SceneSpawnPoses;
            if (poses == null || poses.Length == 0)
            {
                float angle = slot * Mathf.PI * 2f / MaxPlayers;
                position = new Vector3(Mathf.Cos(angle) * 3f, 1f, Mathf.Sin(angle) * 3f);
                rotation = Quaternion.identity;
                return;
            }

            Pose pose = poses[slot % poses.Length];
            // 자리보다 사람이 많으면 같은 자세가 다시 나온다. 한 바퀴마다 옆으로 민다.
            position = pose.position + pose.rotation * new Vector3((slot / poses.Length) * 1.5f, 0f, 0f);
            rotation = pose.rotation;
        }

        public void OnShutdown(NetworkRunner runner, ShutdownReason shutdownReason)
        {
            SetPhase(ESessionPhase.Offline);
            _peers.Remove(this);
            if (Local == this) Local = null;
            if (LocalServer == this) LocalServer = null;
        }

        /// <summary>
        /// <b>호스트가 사라졌다.</b> 클라이언트에서만 불린다 — 호스트 자신은 이 콜백을 받지 않는다.
        ///
        /// <para><c>Core</c> 는 사실만 알리고 <b>무엇을 할지는 <c>InGame</c> 이 정한다</b> —
        /// <see cref="PlayerLeft"/> 와 같은 방향이다. 씬을 여기서 바꾸면 <c>Core</c> 가 메인 메뉴
        /// 경로를 알게 되고, 로비에서 끊긴 경우까지 같은 처리를 강요하게 된다.</para>
        /// </summary>
        public static event Action HostDisconnected;

        public void OnDisconnectedFromServer(NetworkRunner runner, NetDisconnectReason reason)
        {
            Debug.LogWarning($"{nameof(SessionLauncher)}: 호스트와의 연결이 끊겼다 - {reason}");
            SetPhase(ESessionPhase.Offline);
            HostDisconnected?.Invoke();
        }

        public void OnConnectedToServer(NetworkRunner runner) { }
        public void OnConnectRequest(NetworkRunner runner, NetworkRunnerCallbackArgs.ConnectRequest request,
                                     byte[] token) => request.Accept();
        public void OnConnectFailed(NetworkRunner runner, NetAddress remoteAddress,
                                    NetConnectFailedReason reason)
            => Debug.LogError($"{nameof(SessionLauncher)}: 접속 실패 - {reason}");
        public void OnInputMissing(NetworkRunner runner, PlayerRef player, NetworkInput input) { }
        public void OnUserSimulationMessage(NetworkRunner runner, SimulationMessagePtr message) { }
        public void OnSessionListUpdated(NetworkRunner runner, List<SessionInfo> sessionList) { }
        public void OnCustomAuthenticationResponse(NetworkRunner runner, Dictionary<string, object> data) { }
        public void OnHostMigration(NetworkRunner runner, HostMigrationToken hostMigrationToken) { }
        // Fusion 2.1.1 은 ArraySegment 가 아니라 ReadOnlySpan 을 넘긴다(release_history.txt: 할당을 줄이려는 변경).
        /// <summary>
        /// 신뢰 데이터가 도착했다. <b>Core 는 내용을 해석하지 않는다</b> — 키로 자기 것을 알아보는 쪽이
        /// 처리하게 흘려준다. 눈 격자의 청크 델타가 이 채널을 쓴다.
        /// </summary>
        public static event Action<NetworkRunner, PlayerRef, ReliableKey, byte[]> ReliableDataReceived;


        public void OnReliableDataReceived(NetworkRunner runner, PlayerRef player, ReliableKey key,
                                           ReadOnlySpan<byte> data)
        {
            if (ReliableDataReceived == null) return;

            ReliableDataReceived(runner, player, key, data.ToArray());
        }
        public void OnReliableDataProgress(NetworkRunner runner, PlayerRef player, ReliableKey key,
                                           float progress) { }
        public void OnObjectExitAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
        public void OnObjectEnterAOI(NetworkRunner runner, NetworkObject obj, PlayerRef player) { }
    }
}
