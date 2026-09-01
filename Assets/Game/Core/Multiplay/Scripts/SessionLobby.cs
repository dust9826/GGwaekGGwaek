using Fusion;
using Fusion.Sockets;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 세션의 <b>로비 상태</b>. 서버가 스폰하고 모두가 읽는다 — 방장이 누구인지, 몇 명이 붙어 있는지,
    /// 매치를 시작할 수 있는지.
    ///
    /// <para><b>"방장"과 "서버를 누가 돌리는가"는 별개다.</b> 서버는 프로세스이고(개발 중엔 MPPM 서버
    /// 인스턴스, 출시엔 데디 서버), 방장은 <b>세션 안의 역할</b>이다 — 방을 처음 만든 사람. 이 구분이
    /// 없으면 "서버를 띄운 사람만 START 를 누를 수 있다"가 되어, 사람이 없는 데디 서버에서는 아무도
    /// 게임을 시작할 수 없다.</para>
    ///
    /// <para>시작은 <b>요청</b>이다. 클라이언트가 RPC 로 부탁하고 서버가 권한을 검사한다. 클라이언트가
    /// 직접 씬을 올릴 수는 없다 — 씬 권위는 서버뿐이고, 그것이 이 구조의 규약이다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SessionLobby : NetworkBehaviour
    {
        /// <summary>이 세션의 로비. 서버·클라이언트 모두 스폰 후 이 값을 본다.</summary>
        public static SessionLobby Instance { get; private set; }

        /// <summary>방장. 방을 처음 만든(가장 먼저 붙은) 플레이어이고, 나가면 다음 사람에게 넘어간다.</summary>
        [Networked] public PlayerRef Owner { get; private set; }

        /// <summary>
        /// 방장이 아닌 사람도 매치를 시작할 수 있는가. 개발 중에는 켜 두는 편이 편하다 —
        /// 방장 인스턴스가 어느 창인지 찾아다니지 않아도 된다.
        /// </summary>
        [Networked] public NetworkBool AnyPlayerCanStart { get; private set; }

        /// <summary>지금 붙어 있는 플레이어 수. 서버가 채우고 모두가 읽는다.</summary>
        [Networked] public int PlayerCount { get; private set; }

        /// <summary>
        /// 방에 있는 사람들의 <c>PlayerId</c> 비트마스크. <b>인원수만으로는 명단을 그릴 수 없어서
        /// 이것도 복제한다.</b>
        ///
        /// <para><b>왜 <c>Runner.ActivePlayers</c> 를 쓰지 않는가(2026-08-24 실측).</b> 그것은 서버 쪽
        /// 상태라 클라이언트에서는 자기 자신만 보이거나 비어 있다. 그래서 클라마다 자기 한 줄만
        /// 그려지고 <b>각자 자기가 방장인 것처럼 보였다</b>. 증상이 UI 버그처럼 보이지만 원인은
        /// 복제되지 않는 값을 읽은 것이다.</para>
        ///
        /// <para>비트마스크인 이유는 <c>SnowBallCarrier.NetCoopParticipantMask</c> 와 같다 — 정수 하나로
        /// 끝나고 <c>[Networked]</c> 컬렉션을 새로 들이지 않는다. <c>MaxPlayers</c> 가 4 이므로 32 비트가
        /// 넉넉하다.</para>
        /// </summary>
        [Networked] private int NetPlayerMask { get; set; }

        /// <summary>
        /// 슬롯별 닉네임. <b>여기서만 [Networked] 컬렉션을 쓴다</b> — 위의 비트마스크가 컬렉션을 피한
        /// 이유는 "정수 하나로 끝나서" 였고, 이름은 정수로 담기지 않는다. <c>MaxPlayers</c> 가 4 이므로
        /// 길이도 4 다.
        ///
        /// <para><b>인덱스는 <c>PlayerId</c> 다.</b> 슬롯 순서로 담으면 누가 나갈 때 나머지가 밀려
        /// 이름이 한 칸씩 어긋난다. id 는 사람이 나가도 안 바뀐다.</para>
        ///
        /// <para><c>&lt;_16&gt;</c> 은 입력 칸의 <c>max-length="16"</c> 과 같은 값이다. 넘치면 Fusion 이
        /// 자른다.</para>
        /// </summary>
        [Networked, Capacity(MaxPlayerId)]
        private NetworkArray<NetworkString<_16>> NetNames { get; }

        /// <summary>이름 배열의 길이. <b>값은 <see cref="SessionLauncher.MaxPlayerId"/> 하나가 갖는다</b> —
        /// 증강 표가 같은 색인을 쓰게 되면서 두 곳에 8이 흩어지지 않게 올렸다(2026-09-01).</summary>
        private const int MaxPlayerId = SessionLauncher.MaxPlayerId;

        /// <summary>
        /// 이 사람이 방장인가. UI 가 START 버튼을 켤지 정할 때 쓴다.
        ///
        /// <para><b>서버는 방장이 아니다.</b> 서버의 <c>LocalPlayer</c> 도 <c>None</c> 이라 방장이 아직
        /// 없을 때 <c>None == None</c> 으로 참이 되어 버린다 — 그래서 방장이 실제 플레이어인지 먼저 본다.</para>
        /// </summary>
        public bool LocalIsOwner => Runner != null && Owner.IsRealPlayer && Runner.LocalPlayer == Owner;

        /// <summary>이 사람이 매치를 시작할 수 있는가.</summary>
        public bool LocalCanStart => LocalIsOwner || AnyPlayerCanStart;

        /// <summary>
        /// 로비 한 줄에 들어갈 표시용 요약. <b>UI 가 Fusion 타입을 알지 않아도 되게 여기서 만든다</b> —
        /// <c>PPack.OutGame</c> 은 Fusion 을 참조하지 않고, 화면 하나 때문에 참조를 늘리지 않는다.
        /// 루트 <c>AGENTS.md</c> 가 <c>OutGame</c> 의 Fusion 직접 참조를 허용한 것은
        /// <c>NetworkBehaviour</c> 를 상속해야 하는 경우이고, UI 는 그렇지 않다.
        /// </summary>
        public readonly struct LobbySlot
        {
            public readonly int PlayerId;
            public readonly bool IsLocal;
            public readonly bool IsOwner;

            /// <summary>그 사람이 적어 넣은 이름. 아직 안 왔으면 빈 문자열이다.</summary>
            public readonly string Nickname;

            public LobbySlot(int playerId, bool isLocal, bool isOwner, string nickname)
            {
                PlayerId = playerId;
                IsLocal = isLocal;
                IsOwner = isOwner;
                Nickname = nickname;
            }

            /// <summary>화면에 그대로 쓸 수 있는 이름. <b>이름이 겹쳐도 구분되고, 이름이 아직
            /// 안 와도 누군지는 남는다</b> — 그래서 접미사가 장식이 아니라 신원이다.</summary>
            public string Display => Format(Nickname, PlayerId);
        }

        /// <summary>
        /// 지금 방에 있는 사람들을 <paramref name="into"/> 에 채우고 그 수를 돌려준다.
        ///
        /// <para><b>배열을 받는다.</b> UI 가 매 프레임 폴링하므로(변경 알림이 없다) 반환값을 새로
        /// 만들면 프레임마다 쓰레기가 생긴다.</para>
        ///
        /// <para><b>닉네임이 들어간다</b>(2026-09-01). 전에는 "이름은 복제되지 않으므로 남의 이름을
        /// 알 방법이 없다" 였는데 이제 <see cref="NetNames"/> 가 그것을 나른다.</para>
        /// </summary>
        public int FillSlots(LobbySlot[] into)
        {
            if (into == null || Runner == null) return 0;

            int localId = Runner.LocalPlayer.IsRealPlayer ? Runner.LocalPlayer.PlayerId : -1;
            int ownerId = Owner.IsRealPlayer ? Owner.PlayerId : -1;

            int count = 0;
            int mask = NetPlayerMask;
            for (int id = 0; id < 32 && count < into.Length; id++)
            {
                if ((mask & (1 << id)) == 0) continue;
                into[count++] = new LobbySlot(id, id == localId, id == ownerId, NameOf(id));
            }

            return count;
        }

        /// <summary>그 id 의 이름. 없으면 빈 문자열이다.</summary>
        /// <summary>
        /// 복제된 이름을 <see cref="SessionLauncher"/> 에 베낀다. <b>이 오브젝트는 게임플레이 씬이
        /// 올라오면 사라지므로</b>(2026-09-01 실측) 살아 있는 동안 넘겨 두지 않으면 인게임에서 이름을
        /// 알 방법이 없다. 서버뿐 아니라 <b>모든 피어</b>가 자기 사본을 만든다.
        /// </summary>
        private void MirrorNamesToLauncher()
        {
            for (int id = 0; id < MaxPlayerId; id++)
                SessionLauncher.RememberPlayerName(id, NetNames[id].ToString());
        }

        public string NameOf(int playerId) =>
            playerId >= 0 && playerId < MaxPlayerId ? NetNames[playerId].ToString() : string.Empty;

        /// <summary>서버가 이름을 적는다. 클라이언트가 접속 직후 신뢰 채널로 보낸 것이다.</summary>
        public void ServerSetName(PlayerRef player, string nickname)
        {
            if (!HasStateAuthority || !player.IsRealPlayer) return;
            int id = player.PlayerId;
            if (id < 0 || id >= MaxPlayerId) return;
            NetNames.Set(id, nickname ?? string.Empty);
        }

        /// <summary>
        /// 화면에 쓰는 이름. <b>이름 + <c>#id</c></b> 다.
        ///
        /// <para>접미사가 둘을 동시에 푼다 — 닉네임이 겹쳐도(둘 다 <c>PENGUIN</c>) 구분되고,
        /// 이름을 아직 못 받았거나 이미 사라진 뒤에도 <c>#2</c> 만으로 누군지 특정된다. 그래서
        /// 이름을 못 읽는 경우가 "무의미한 문장" 이 아니라 "덜 친절한 문장" 으로 끝난다.</para>
        /// </summary>
        public static string Format(string nickname, int playerId) =>
            string.IsNullOrWhiteSpace(nickname) ? $"#{playerId}" : $"{nickname.Trim()}#{playerId}";

        /// <summary>
        /// 닉네임 전송 키. <b>RPC 를 쓰지 않는 이유는 위 <see cref="PollStartRequests"/> 와 같다</b> —
        /// 위버가 심는 internal 호출 때문에 런타임에 <c>MethodAccessException</c> 이 난다.
        ///
        /// <para>입력 채널에 싣지 않은 이유는 다르다. 시작 요청은 <b>한 틱짜리 사건</b>이라 입력이
        /// 맞지만, 이름은 <b>한 번 정해지면 안 바뀌는 값</b>이다. 구조체에 16바이트를 더하면 그 값을
        /// 매 틱 모두에게 보내게 된다. 신뢰 채널은 한 번만 보낸다(눈 격자 델타가 같은 채널이다).</para>
        /// </summary>
        public static readonly ReliableKey NicknameKey = ReliableKey.FromInts(0x4E, 0x41, 0x4D, 0x45);

        /// <summary>내 이름을 서버에 보낸다. 접속한 뒤 한 번이면 된다.</summary>
        public static void SendLocalNickname(NetworkRunner runner, string nickname)
        {
            if (runner == null || !runner.IsRunning || string.IsNullOrWhiteSpace(nickname)) return;
            runner.SendReliableDataToServer(
                NicknameKey, System.Text.Encoding.UTF8.GetBytes(nickname.Trim()));
        }

        /// <summary>서버만 받아 적는다. 키가 우리 것이 아니면 흘려보낸다 — 이 채널은 눈 격자도 쓴다.</summary>
        private void OnReliableData(NetworkRunner runner, PlayerRef player, ReliableKey key, byte[] data)
        {
            if (!HasStateAuthority || key != NicknameKey || data == null || data.Length == 0) return;
            ServerSetName(player, System.Text.Encoding.UTF8.GetString(data));
        }

        private bool _matchRequested;

        public override void Spawned()
        {
            Instance = this;

            SessionLauncher.ReliableDataReceived -= OnReliableData;
            SessionLauncher.ReliableDataReceived += OnReliableData;

            // ⚠ <b>호스트는 보내지 않고 직접 적는다</b>(2026-09-01 실측). 자기 자신에게 보낸 신뢰
            // 데이터는 돌아오지 않아 <see cref="OnReliableData"/> 가 안 불린다 — 그대로 두면
            // <b>방장 이름만 비어</b> 화면에 <c>#1</c> 로 뜬다. 호스트도 플레이어라 이름이 있어야 한다.
            // 데디 서버는 <c>LocalPlayer</c> 가 실플레이어가 아니라 아래 가드에서 걸러진다.
            if (!HasStateAuthority)
            {
                SendLocalNickname(Runner, SessionLauncher.LocalNickname);
                return;
            }

            ServerSetName(Runner.LocalPlayer, SessionLauncher.LocalNickname);

            AnyPlayerCanStart = Application.isEditor; // 에디터 검증에서는 아무나 시작할 수 있게 둔다.
            RefreshFromRunner();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            SessionLauncher.ReliableDataReceived -= OnReliableData;
            if (Instance == this) Instance = null;
        }

        public override void FixedUpdateNetwork()
        {
            // 거울은 전 피어가 돌린다 — 아래 서버 전용 구간보다 앞이다.
            MirrorNamesToLauncher();

            if (!HasStateAuthority) return;

            // 인원과 방장은 서버가 매 틱 확인한다. 이벤트만 믿으면 조인/리브가 겹칠 때 어긋난다.
            RefreshFromRunner();
            PollStartRequests();
        }

        private void RefreshFromRunner()
        {
            int count = 0;
            int mask = 0;
            bool ownerStillHere = false;
            PlayerRef first = PlayerRef.None;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (count == 0) first = player;
                if (player == Owner) ownerStillHere = true;
                if (player.PlayerId >= 0 && player.PlayerId < 32) mask |= 1 << player.PlayerId;
                count++;
            }


            PlayerCount = count;
            NetPlayerMask = mask;

            // 방장이 없거나 나갔으면 남은 사람 중 첫 번째가 이어받는다. 아무도 없으면 비운다.
            if (!ownerStillHere) Owner = first;
        }

        /// <summary>
        /// 서버가 플레이어들의 입력에서 <b>시작 요청</b>을 읽는다.
        ///
        /// <para>RPC 를 쓰지 않는다 — Fusion 위버가 사용자 어셈블리에 <c>Fusion.Runtime</c> 의 internal
        /// <c>CheckInvokeRpc</c> 호출을 심는데 그 어셈블리의 <c>InternalsVisibleTo</c> 에 우리가 없어
        /// 런타임에 <c>MethodAccessException</c> 이 난다(실측: 로비에서 START 를 누르면 그 예외가 났다).
        /// 입력은 이미 매 틱 서버로 오고 <c>TryGetInputForPlayer</c> 로 누가 보냈는지도 알 수 있으므로,
        /// 요청을 원인 채널에 실어 보내는 것이 우리 규약과도 맞는다.</para>
        /// </summary>
        private void PollStartRequests()
        {
            if (_matchRequested) return;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetInputForPlayer(player, out NetworkInputData input)) continue;
                if (!input.Buttons.IsSet((int)EInputButton.RequestStartMatch)) continue;

                if (player != Owner && !AnyPlayerCanStart)
                {
                    Debug.LogWarning($"[Lobby] 방장이 아닌 {player} 의 시작 요청을 거부했다.");
                    continue;
                }

                _matchRequested = true;
                Debug.Log($"[Lobby] {player} 의 요청으로 매치를 시작한다 (플레이어 {PlayerCount}명).");
                _ = SessionLauncher.StartMatch();
                return;
            }
        }
    }
}
