using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>한 의뢰가 선을 타는 모양. 화면에 그릴 때 필요한 것만 든다 — 난이도·보상·추가시간은
    /// 서버가 발행 시점에 유도한 값이라 클라이언트가 다시 계산할 일이 없다.</summary>
    public struct NetMissionRequest : INetworkStruct
    {
        public int Id;
        public int HouseIndex;
        public byte Kind;
        public float RemainingSeconds;
        public float DistanceM;
    }

    /// <summary>
    /// 의뢰 미션의 <b>네트워크 창구</b>. 서버는 기존 <see cref="RequestDirector"/>·<see cref="GameManager"/>를
    /// 그대로 굴리고, 이 컴포넌트는 그 상태를 <c>[Networked]</c>로 복사한다. 클라이언트는 여기만 읽는다.
    ///
    /// <para><b>본문은 여기 없다.</b> 의뢰 발행 간격·TTL·완료 판정·시간 정산은 전부 두 디렉터가 소유하고,
    /// 이 컴포넌트가 하는 일은 그 결과를 선에 싣는 것뿐이다 — <see cref="PenguinNetAvatar"/>가
    /// <see cref="PenguinLocomotion"/>을 감싸는 것과 같은 이유로, 같은 게임을 두 벌 만들지 않는다.</para>
    ///
    /// <para><b>클라이언트에서는 두 디렉터를 끈다.</b> 켜 두면 자기 난수로 자기 의뢰를 만들고 자기
    /// 시계로 전역 시간을 깎아, 두 화면이 각자 다른 게임을 하게 된다.</para>
    ///
    /// <para>집·도로망·판정 구역은 복제하지 않는다. 모든 피어가 같은 씬을 열므로 이미 같고,
    /// 의뢰가 <see cref="GiftRequest.HouseIndex"/>(int)를 드는 것이 그것을 성립시킨다.
    /// <see cref="HouseAt"/>가 클라이언트에서도 도는 이유이기도 하다 — 배열 조회일 뿐이라
    /// 디렉터가 꺼져 있어도 답한다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionNetHub : NetworkBehaviour
    {
        /// <summary>복제 배열의 용량. <see cref="StageBalanceConfig.MaxActiveRequests"/>는 실시간으로
        /// 바뀔 수 있지만 <c>[Networked]</c> 배열 용량은 컴파일 타임 상수라, 넉넉히 잡고 넘치면 자른다.</summary>
        public const int MaxReplicatedRequests = 12;

        [Networked, Capacity(MaxReplicatedRequests)]
        private NetworkArray<NetMissionRequest> NetRequests { get; }

        [Networked] private int NetRequestCount { get; set; }
        [Networked] private float NetRemainingSeconds { get; set; }
        [Networked] private int NetScore { get; set; }
        [Networked] private byte NetPhase { get; set; }

        /// <summary>모든 피어가 씨를 올리고 아바타까지 받았는가. 서버가 한 번 참으로 바꾸면 다시 거짓이 되지 않는다.
        ///
        /// <para><b>왜 필요한가.</b> <c>OnSceneLoadDone</c> 은 피어마다 자기 로드가 끝나는 순간 불리므로
        /// 사양이 좋은 쪽이 먼저 <see cref="ESessionPhase.Playing"/> 으로 간다. 그 상태에서 각자
        /// 인트로를 돌리면 카운트다운 3-2-1 이 로드 시간 차만큼 어긋나고, 서버가 자기 인트로만 보고
        /// 게임을 시작하므로 <b>늦게 들어온 사람은 이미 시간이 깎인 판에 떨어진다</b>.</para>
        ///
        /// <para>준비의 증거로 <b>아바타 수</b>를 쓴다. 아바타는 씨가 올라온 뒤에야 스폰되므로
        /// (<c>SessionLauncher.OnSceneLoadDone</c>), 전원의 아바타가 있다는 것은 전원이 씨를 받았다는 뜻이다.
        /// 따로 준비 플래그를 주고받을 필요가 없다.</para></summary>
        [Networked] private NetworkBool NetAllPeersReady { get; set; }

        /// <summary>인트로를 틀 <b>틱</b>. 서버가 전원 준비를 확인한 틱에 한 번 찍고 다시 건드리지 않는다.
        ///
        /// <para><b>왜 시각이 아니라 틱인가.</b> 각 피어가 <see cref="NetAllPeersReady"/> 를 받는 프레임은
        /// 네트워크 지연만큼 다르다 — 그 프레임에 로컬로 재생하면 실측으로 0.73초까지 벌어졌고,
        /// 그만큼 카운트다운 "3" 이 피어마다 다른 순간에 뜼다. Fusion 의 틱은 모든 피어가 공유하므로
        /// "이 틱에 시작했다" 를 기준으로 삼으면 늦게 받은 피어는 그만큼 앞서 재생해 따라잡는다.</para></summary>
        [Networked] private int NetIntroStartTick { get; set; }

        /// <summary>결과 화면의 재료. <b>서버가 판정한 것을 그대로 보낸다</b> — 클라이언트가 자기
        /// 눈 총량과 자기 완료 수로 다시 계산하면 같은 판에서 다른 별점이 뜬다.</summary>
        [Networked] private byte NetStarCount { get; set; }

        /// <inheritdoc cref="NetStarCount"/>
        [Networked] private byte NetSnowClearedPercent { get; set; }

        /// <inheritdoc cref="NetStarCount"/>
        [Networked] private float NetElapsedSeconds { get; set; }

        /// <summary>시간 획득 연출의 신호. <b>이벤트를 복제할 수 없으니 번호를 센다</b> — 클라이언트는
        /// 이 값이 바뀐 것을 보고 연출을 한 번 재생한다. 값 자체에 의미는 없다.</summary>
        [Networked] private byte NetTimeGrantTicket { get; set; }

        /// <inheritdoc cref="NetTimeGrantTicket"/>
        [Networked] private float NetLastTimeGrant { get; set; }

        /// <summary>의뢰가 목록에서 사라진 이유. 복제된 목록만 보면 <b>완료와 만료가 구분되지 않아</b>
        /// 집 신호가 성공 연출로 닫을지 실패로 닫을지 알 수 없다. 판정한 쪽이 알려 준다.</summary>
        [Networked] private byte NetClosedTicket { get; set; }

        /// <inheritdoc cref="NetClosedTicket"/>
        [Networked] private int NetClosedHouseIndex { get; set; }

        /// <inheritdoc cref="NetClosedTicket"/>
        [Networked] private NetworkBool NetClosedCompleted { get; set; }

        /// <summary>인트로가 끝났다는 신호가 오지 않을 때 서버가 스스로 시작하기까지의 시간.
        /// <b>헤드리스 서버에는 화면이 없다</b> — 인트로 카운트다운이 시작 신호를 보내는 구조라
        /// 이 안전망이 없으면 아무도 시작하지 않은 채로 방이 서 있는다.</summary>
        [SerializeField, Min(1f)] private float _autoStartSeconds = 12f;

        private RequestDirector _director;
        private GameManager _manager;
        private float _authoritySeconds;
        private bool _loggedAutoStart;
        private int _droppedRequestWarnings;
        private bool _loggedFirstSnapshot;

        /// <summary>이 피어가 미션을 굴리는가. 호스트·데디 서버는 참, 클라이언트는 거짓이다.
        /// 시스템(발행·판정·정산)은 이것이 참인 곳에서만 돌고, 화면은 양쪽 모두 이 허브를 읽는다.</summary>
        public bool HasAuthority => Object != null && Object.HasStateAuthority;

        public EGamePhase Phase => (EGamePhase)NetPhase;

        /// <inheritdoc cref="NetAllPeersReady"/>
        public bool AllPeersReady => NetAllPeersReady;

        /// <summary>인트로가 이미 흘러간 시간(초). 아직 시작 전이면 음수다.
        ///
        /// <para>늦게 받은 피어는 이 값이 이미 양수라, 그만큼 <b>앞서 감기 시작</b>하면 모두가 같은
        /// 순간에 0 에 도달한다.</para></summary>
        public float IntroElapsedSeconds
        {
            get
            {
                if (!NetAllPeersReady || Runner == null) return -1f;
                return (Runner.Tick - NetIntroStartTick) * Runner.DeltaTime;
            }
        }
        public int StarCount => NetStarCount;
        public int SnowClearedPercent => NetSnowClearedPercent;
        public float ElapsedSeconds => NetElapsedSeconds;
        public int TimeGrantTicket => NetTimeGrantTicket;
        public float LastTimeGrantSeconds => NetLastTimeGrant;
        public int ClosedTicket => NetClosedTicket;
        public int ClosedHouseIndex => NetClosedHouseIndex;
        public bool ClosedCompleted => NetClosedCompleted;
        public float RemainingSeconds => NetRemainingSeconds;
        public int Score => NetScore;
        public int RequestCount => Mathf.Clamp(NetRequestCount, 0, MaxReplicatedRequests);
        public NetMissionRequest RequestAt(int index) => NetRequests.Get(index);
        public DeliveryHouse HouseAt(int houseIndex) => _director != null ? _director.HouseAt(houseIndex) : null;

        public override void Spawned()
        {
            _director = FindAnyObjectByType<RequestDirector>(FindObjectsInactive.Include);
            _manager = FindAnyObjectByType<GameManager>(FindObjectsInactive.Include);
            if (_director == null || _manager == null)
            {
                Debug.LogError($"{nameof(MissionNetHub)}: 씬에 RequestDirector 또는 GameManager 가 없다. " +
                               $"director={_director != null} manager={_manager != null}");
                return;
            }

            BindPresenters();

            if (Object.HasStateAuthority)
            {
                // 시간 획득은 이벤트라 복제되지 않는다. 서버가 받아서 티켓으로 바꿔 보낸다.
                _manager.TimeGranted -= OnServerTimeGranted;
                _manager.TimeGranted += OnServerTimeGranted;
                _director.RequestCompleted -= OnServerRequestCompleted;
                _director.RequestCompleted += OnServerRequestCompleted;
                _director.RequestExpired -= OnServerRequestExpired;
                _director.RequestExpired += OnServerRequestExpired;
                Debug.Log($"[MissionNetHub] 서버 권위로 미션을 맡는다 — 집 {_director.HouseCount}채.");
                return;
            }

            // 클라이언트에서 켜 두면 자기 난수로 자기 의뢰를 만들고 자기 시계로 시간을 깎는다.
            _director.enabled = false;
            _manager.enabled = false;
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_manager != null) _manager.TimeGranted -= OnServerTimeGranted;
            if (_director != null)
            {
                _director.RequestCompleted -= OnServerRequestCompleted;
                _director.RequestExpired -= OnServerRequestExpired;
            }

            foreach (RequestHudPresenter hud in FindObjectsByType<RequestHudPresenter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                hud.BindMission(null);
            foreach (RequestStageFlowPresenter flow in FindObjectsByType<RequestStageFlowPresenter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                flow.BindMission(null);
            foreach (RequestHouseSignalPresenter signals in FindObjectsByType<RequestHouseSignalPresenter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                signals.BindMission(null);
        }

        /// <summary>화면들이 이 허브를 읽게 한다. <b>호스트에서도 한다</b> — 자기가 방금 쓴 값을
        /// 그대로 읽으므로 값은 같고, 화면 코드가 역할별로 두 벌 갈라지지 않는다.</summary>
        private void BindPresenters()
        {
            foreach (RequestHudPresenter hud in FindObjectsByType<RequestHudPresenter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                hud.BindMission(this);
            foreach (RequestStageFlowPresenter flow in FindObjectsByType<RequestStageFlowPresenter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                flow.BindMission(this);
            foreach (RequestHouseSignalPresenter signals in FindObjectsByType<RequestHouseSignalPresenter>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                signals.BindMission(this);
        }

        /// <summary>전원이 씨를 받았는지 서버가 판정해 <see cref="NetAllPeersReady"/> 를 올린다.
        ///
        /// <para>판정 기준은 <b>로비에서 시작 버튼을 누른 순간의 인원</b>
        /// (<see cref="SessionLauncher.ExpectedPlayerCount"/>)만큼 아바타가 생겼는가다. 아바타는 씨 로드가
        /// 끝난 뒤에야 스폰되므로(<c>SessionLauncher.OnSceneLoadDone</c>) 그것이 곳 "그 피어는 씨를
        /// 받았다" 는 증거다. 따로 준비 플래그를 주고받을 필요가 없다.</para>
        ///
        /// <para>⚠ <b>현재 접속 인원(<c>Runner.ActivePlayers</c>)을 분모로 쓰면 안 된다.</b> 그러면
        /// 사람이 들어올 때마다 분모가 같이 늘어 조건이 항상 참이 된다 — 실측으로 2인 판에서
        /// 먼저 붙은 1명만으로 인트로가 시작됐고 늤에 온 사람은 이미 지나간 판에 합류했다.</para>
        ///
        /// <para>한 번 참이 되면 되돌리지 않는다. 중간에 누가 나가면 수가 줄어 다시 거짓이 되고,
        /// 그러면 이미 진행 중인 판이 인트로로 되돌아가버린다. 나간 사람을 기다리지 않는 것도 같은
        /// 이유다 — 접속이 끊긴 자리를 무한히 기다리면 남은 사람이 영영 시작하지 못한다.</para></summary>
        private void TickPeersReady()
        {
            if (NetAllPeersReady || Runner == null) return;

            int expected = SessionLauncher.ExpectedPlayerCount;
            if (expected <= 0) return;

            int avatars = 0;
            int present = 0;
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                present++;
                if (Runner.TryGetPlayerObject(player, out NetworkObject avatar) && avatar != null) avatars++;
            }

            // 기대 인원보다 적게 남았으면(대기 중 누가 나갔다) 현재 인원을 기준으로 내려잡는다.
            int target = Mathf.Min(expected, present);
            if (target <= 0 || avatars < target) return;

            NetIntroStartTick = Runner.Tick;
            NetAllPeersReady = true;
            Debug.Log($"[MissionNetHub] 전원 준비 완료 — 기대 {expected}명 · 접속 {present}명 · 아바타 {avatars}개. " +
                     "인트로를 시작한다.");
        }

        /// <summary>누군가 결과 화면에서 "다시 하기"를 눌렀다. 클라이언트가 보내고 서버만 받는다.
        ///
        /// <para><b>왜 각자 씨를 갈아끼지 않는가.</b> 한 피어가 혼자 <c>LoadScene</c> 을 하면 그 피어만
        /// 세션에서 빠지고 나머지는 끝난 판에 남는다. 씨 전환은 씨 권위(서버)만 할 수 있으므로
        /// 요청만 보내고 실제 재시작은 서버가 전원에게 한번에 건다.</para>
        ///
        /// <para>권한은 로비와 같은 규칙을 따른다 — <see cref="SessionLobby.LocalCanStart"/> 가 참인
        /// 피어만 보낸다. 버튼을 숨기는 것은 화면의 일이고, 여기서도 한 번 더 막는다 —
        /// 권한 판정을 화면에만 맡기면 버튼을 숨기는 것만으로 막은 것이 된다.</para></summary>
        /// <para><b>RPC 가 아니라 입력 채널로 온다.</b> 전에는 <c>[Rpc]</c> 였는데 이 프로젝트에서는
        /// 동작하지 않는다 — Fusion 위버가 우리 어셈블리에 <c>Fusion.Runtime</c> 의 internal
        /// <c>CheckInvokeRpc</c> 호출을 심고, 그 어셈블리의 <c>InternalsVisibleTo</c> 에 우리가 없어
        /// 런타임에 터진다. 실측 로그(2026-08-29): <c>MethodAccessException: Method
        /// `Fusion.NetworkBehaviourUtils.CheckInvokeRpc(Fusion.NetworkBehaviour)' is inaccessible
        /// from method `PPack.MissionNetHub.RpcRequestRestart(Fusion.RpcInfo)'</c>. 즉 <b>다시 하기
        /// 버튼은 눌러도 아무 일이 없었다.</b> 같은 제약을 로비의 시작 요청이 이미 겪었고
        /// (<c>SessionLobby.PollStartRequests</c>) 그때 입력 채널로 옮겼는데, 재시작만 남아 있었다.</para>
        private bool _restartRequested;

        /// <summary>결과 화면의 "다시 하기" 요청을 서버가 입력에서 읽는다.
        ///
        /// <para>권한은 로비와 같은 규칙이다 — 방장만 보낼 수 있고, 여기서 한 번 더 막는다.
        /// 버튼을 숨기는 것은 화면의 일이고 그것만으로 막은 상태를 만들지 않는다.</para>
        ///
        /// <para>한 번만 건다. 요청은 패킷 손실에 대비해 0.5초 동안 반복해서 오므로, 걸지 않으면
        /// 같은 요청으로 씬을 여러 번 올린다.</para></summary>
        private void PollRestartRequests()
        {
            if (_restartRequested) return;

            // <b>끝난 판에서만 받는다.</b> 화면이 이미 그 조건에서만 버튼을 보여 주지만, 판정을
            // 화면에만 맡기면 버튼을 숨기는 것만으로 막은 상태가 된다(방장 판정과 같은 이유).
            //
            // 그리고 이 검사가 <b>재시작이 두 번 걸리는 것</b>을 막는다. 요청은 패킷 손실에 대비해
            // 0.5초 동안 반복해서 오는데, 서버가 씬을 다시 올리면 이 허브도 새로 생기면서
            // <see cref="_restartRequested"/> 걸쇠가 풀린다. 그때 아직 펄스가 살아 있으면 새 허브가
            // 곧바로 또 재시작한다 — 씬 로드가 0.5초보다 빠른 순간에만 나는, 찾기 어려운 종류다.
            // 다시 올라온 판은 Ended 가 아니므로 남은 펄스는 여기서 조용히 무시된다.
            if (_manager.Phase != EGamePhase.Ended) return;

            SessionLobby lobby = SessionLobby.Instance;
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetInputForPlayer(player, out NetworkInputData input)) continue;
                if (!input.Buttons.IsSet((int)EInputButton.RequestRestartMatch)) continue;

                if (lobby != null && !lobby.AnyPlayerCanStart && player != lobby.Owner)
                {
                    Debug.LogWarning($"[MissionNetHub] 다시 하기를 방장이 아닌 피어({player})가 요청해 무시한다.");
                    continue;
                }

                _restartRequested = true;
                Debug.Log($"[MissionNetHub] 다시 하기 요청({player}) — 전원을 같은 씬으로 다시 보낸다.");
                _ = SessionLauncher.RestartMatch();
                return;
            }
        }

        /// <summary>서버가 미션을 시작한다. 인트로가 끝나는 시점을 아는 것은 화면이므로 그쪽이 부른다.</summary>
        public void ServerBeginPlaying()
        {
            if (!HasAuthority || _manager == null) return;
            _manager.BeginPlaying();
        }

        private void OnServerRequestCompleted(GiftRequest request) => PublishClosed(request, true);

        private void OnServerRequestExpired(GiftRequest request) => PublishClosed(request, false);

        /// <inheritdoc cref="NetClosedTicket"/>
        private void PublishClosed(GiftRequest request, bool completed)
        {
            if (!HasAuthority || request == null) return;
            NetClosedHouseIndex = request.HouseIndex;
            NetClosedCompleted = completed;
            NetClosedTicket = unchecked((byte)(NetClosedTicket + 1));
        }

        /// <inheritdoc cref="NetTimeGrantTicket"/>
        private void OnServerTimeGranted(float seconds)
        {
            if (!HasAuthority) return;
            NetLastTimeGrant = seconds;
            NetTimeGrantTicket = unchecked((byte)(NetTimeGrantTicket + 1));
        }

        /// <summary>서버가 판정한 결과를 실어 보낸다. 클라이언트의 결과 화면이 이 값을 그린다.</summary>
        public void ServerPublishResult(int starCount, int snowClearedPercent, float elapsedSeconds)
        {
            if (!HasAuthority) return;
            NetStarCount = (byte)Mathf.Clamp(starCount, 0, 255);
            NetSnowClearedPercent = (byte)Mathf.Clamp(snowClearedPercent, 0, 100);
            NetElapsedSeconds = elapsedSeconds;
        }

        /// <summary>클라이언트가 실제로 값을 받았는지는 화면에 의뢰가 뜨기 전까지 알 길이 없다.
        /// 복제가 죽으면 "아무 일도 안 일어난다"로 보이므로 첫 스냅샷만 한 번 남긴다.</summary>
        public override void Render()
        {
            if (Object.HasStateAuthority || _loggedFirstSnapshot || NetRequestCount <= 0) return;
            _loggedFirstSnapshot = true;
            NetMissionRequest first = RequestAt(0);
            Debug.Log($"[MissionNetHub] 클라이언트 첫 스냅샷 — 의뢰 {RequestCount}건 · 남은 시간 " +
                      $"{RemainingSeconds:F1}s · 점수 {Score} · 페이즈 {Phase} · " +
                      $"의뢰0(집 {first.HouseIndex}, 종류 {(EGiftBoxKind)first.Kind}, {first.RemainingSeconds:F1}s)");
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _manager == null || _director == null) return;

            TickPeersReady();
            TickAutoStart();
            PollRestartRequests();

            NetRemainingSeconds = _manager.RemainingSeconds;
            NetScore = _manager.Score;
            NetPhase = (byte)_manager.Phase;

            IReadOnlyList<GiftRequest> active = _director.ActiveRequests;
            int count = Mathf.Min(active.Count, MaxReplicatedRequests);
            for (int index = 0; index < count; index++)
            {
                GiftRequest request = active[index];
                NetRequests.Set(index, new NetMissionRequest
                {
                    Id = request.Id,
                    HouseIndex = request.HouseIndex,
                    Kind = (byte)request.WantedKind,
                    RemainingSeconds = request.RemainingSeconds,
                    DistanceM = request.DistanceM,
                });
            }

            NetRequestCount = count;
            WarnOnceIfTruncated(active.Count);
        }

        /// <inheritdoc cref="_autoStartSeconds"/>
        private void TickAutoStart()
        {
            if (_manager.Phase != EGamePhase.Intro) return;

            _authoritySeconds += Runner.DeltaTime;
            if (_authoritySeconds < _autoStartSeconds) return;
            if (_loggedAutoStart) return;

            _loggedAutoStart = true;
            Debug.LogWarning($"[MissionNetHub] 인트로 신호가 {_autoStartSeconds:F0}초 동안 오지 않아 서버가 시작한다. " +
                             "화면 없는 피어가 서버를 맡은 판이면 정상이다.");
            _manager.BeginPlaying();
        }

        /// <summary>잘린 의뢰는 클라이언트 화면에서 조용히 사라진다 — 그것이 밸런스 값 때문인지
        /// 버그인지 나중에 가르려면 로그가 필요하다. 매 틱 찍지는 않는다.</summary>
        private void WarnOnceIfTruncated(int activeCount)
        {
            if (activeCount <= MaxReplicatedRequests || _droppedRequestWarnings > 0) return;
            _droppedRequestWarnings++;
            Debug.LogWarning($"{nameof(MissionNetHub)}: 활성 의뢰 {activeCount}건 중 " +
                             $"{MaxReplicatedRequests}건만 복제한다. 용량을 늘리려면 " +
                             $"{nameof(MaxReplicatedRequests)} 를 올린다.");
        }
    }
}
