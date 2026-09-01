using System.Threading.Tasks;
using Unity.Multiplayer.PlayMode;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// MPPM(Play Mode Scenarios) 인스턴스마다 <b>역할을 자동으로 정한다.</b>
    /// 태그는 셋이다 — <c>host</c>(방장이자 플레이어), <c>server</c>(데디케이티드, 플레이어 아님),
    /// <c>client</c>(붙기만 한다). 태그가 없으면 아무것도 하지 않는다.
    ///
    /// <para><b>출시 토폴로지는 호스트 모드다(2026-08-30).</b> 방을 연 사람이 서버 권위를 갖고 동시에
    /// 플레이어이므로 <c>host</c> 태그가 기본이고, <c>server</c>(데디케이티드)는 테스트와 헤드리스
    /// 게이트가 쓰는 경로로 남아 있다. 그래서 <b>"서버에는 로컬 플레이어가 없다" 도, "항상 있다" 도
    /// 가정하면 안 된다</b> — 양쪽 다 돌 수 있어야 한다.
    /// MPPM 이전에는 한 프로세스에 서버+클라 셋을 띄우는
    /// <c>PeerMode = Multiple</c> 로 흉내냈는데, 그 방식은 피어별 씬 사본·가시성 토글·GPU 자원 공유 문제를
    /// 줄줄이 낳았다(<c>AGENTS.md</c> 의 함정 목록이 전부 그것이었다). 인스턴스가 <b>진짜 프로세스</b>면
    /// 그 문제가 전부 사라진다.</para>
    ///
    /// <para>씬에 오브젝트를 두지 않는다 — 로비 씬은 팀원 소유 파일이고, 역할 결정은 씬과 무관한
    /// 프로세스 속성이다. 그래서 <see cref="RuntimeInitializeOnLoadMethodAttribute"/> 로 붙는다.</para>
    ///
    /// <para><b>메인 에디터는 자동으로 아무것도 하지 않는다.</b> 로비 UI 를 그대로 쓰고, 사람이 방을
    /// 만들면 서버가 뜬다. 추가 에디터만 <see cref="DevRoomCode"/> 로 자동 접속한다 — 클릭 없이 붙는 것이
    /// 검증 반복의 대부분이기 때문이다.</para>
    /// </summary>
    public static class MultiplayerRoleBootstrap
    {
        /// <summary>
        /// 추가 에디터가 자동으로 붙는 방 코드. 메인 에디터에서 이 코드로 방을 만들면 바로 만난다.
        /// 태그 <c>room:XXXX</c> 를 인스턴스에 달면 그 값이 이긴다.
        /// </summary>
        public const string DevRoomCode = "MPPMDEV";

        private const string RoomTagPrefix = "room:";
        private const string ClientTag = "client";
        private const string ServerTag = "server";

        /// <summary>
        /// 방을 열되 <b>그 인스턴스도 플레이어로 참가한다</b>(<c>GameMode.Host</c>).
        ///
        /// <para><see cref="ServerTag"/> 와 갈리는 지점은 하나다 — <c>server</c> 는 데디케이티드라
        /// 그 화면에 펭귄이 없고, <c>host</c> 는 방장이 곧 플레이어다. 로비 UI 의 "방 만들기" 가
        /// 원래 타는 경로(<see cref="SessionLauncher.HostRoom"/>)와 같은 것이며, 이 태그는 그
        /// 클릭을 없앨 뿐이다.</para>
        ///
        /// <para><b>호스트로 검증하면 데디 서버의 GPU 의존을 못 잡는다.</b> 에디터의 호스트 피어에는
        /// GPU 가 있어서, 데디 서버가 못 하는 코드를 그대로 실행한다. 그 구멍은
        /// <c>MultiplayHeadlessTests</c> 와 <c>MultiPlaySceneHeadlessTests</c> 가 대신 지킨다 —
        /// 호스트로 옮겨도 그 둘을 지우지 않는 이유다.</para>
        /// </summary>
        private const string HostTag = "host";

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Bootstrap()
        {
            // 빌드에서는 아무 일도 하지 않는다. 이건 에디터 검증 편의 장치다.
            if (!Application.isEditor) return;

            string room = DevRoomCode;
            bool wantsServer = false;
            bool wantsClient = false;
            bool wantsHost = false;

            foreach (string tag in CurrentPlayer.ReadOnlyTags())
            {
                if (tag.StartsWith(RoomTagPrefix, System.StringComparison.OrdinalIgnoreCase))
                    room = tag.Substring(RoomTagPrefix.Length).Trim().ToUpperInvariant();
                else if (string.Equals(tag, ServerTag, System.StringComparison.OrdinalIgnoreCase))
                    wantsServer = true;
                else if (string.Equals(tag, ClientTag, System.StringComparison.OrdinalIgnoreCase))
                    wantsClient = true;
                else if (string.Equals(tag, HostTag, System.StringComparison.OrdinalIgnoreCase))
                    wantsHost = true;
            }

            // 태그가 없으면 아무것도 하지 않는다. 그것이 "로비 UI 흐름을 사람이 직접 밟는" 시나리오다.
            //
            // 처음에는 "태그가 없는 추가 에디터는 클라이언트"로 두었는데, 그러면 태그 없는 시나리오에서도
            // 클론이 DevRoomCode 에 자동 접속하려다 방이 없어 GameNotFound 로 죽었다(실측). 자동 시작은
            // 명시적으로 태그를 준 인스턴스만 한다.
            if (!wantsServer && !wantsClient && !wantsHost)
            {
                Debug.Log("[MPPM] 태그가 없다 - 자동 시작하지 않는다. 로비 UI 로 방을 만들거나 코드로 붙어라.");
                return;
            }

            if (wantsServer && wantsHost)
            {
                Debug.LogError("[MPPM] server 와 host 를 같이 달 수 없다 - 하나는 데디케이티드이고 " +
                               "하나는 방장이 곧 플레이어다. 태그를 하나만 남겨라.");
                return;
            }

            if (wantsServer || wantsHost)
            {
                Debug.Log($"[MPPM] 이 인스턴스는 {(wantsHost ? "호스트" : "서버")}다 - 방 {room} 을 연다.");
                _ = HostAndWaitForPlayers(room, wantsHost);
                return;
            }

            Debug.Log($"[MPPM] 이 인스턴스는 클라이언트다 - 방 {room} 에 붙는다.");
            _ = JoinWhenRoomExists(room);
        }

        /// <summary>
        /// 서버를 열고, <b>첫 플레이어가 들어오면 매치를 자동으로 시작한다.</b>
        ///
        /// <para>개발 편의 장치다 — 서버 인스턴스에는 사람이 없으니 누군가 START 를 눌러 줄 수 없고,
        /// 눌러 주려면 서버 화면으로 가서 로비 UI 를 조작해야 한다. 태그로 역할을 준 인스턴스는 그
        /// 클릭까지 없애는 것이 검증 반복을 짧게 만든다.</para>
        ///
        /// <para>사람이 로비로 직접 방을 만든 경우에는 이 경로를 타지 않으므로, 그때는 START 버튼이
        /// 그대로 매치를 시작한다.</para>
        /// </summary>
        private static async Task HostAndWaitForPlayers(string room, bool asHost)
        {
            // 호스트면 이 인스턴스도 플레이어다. 그래서 기다릴 인원이 하나 줄어든다 - 아래 wanted 참고.
            bool started = asHost
                ? await SessionLauncher.HostRoom(room)
                : await SessionLauncher.HostServerOnly(room);
            if (!started) return;

            SessionLauncher server = SessionLauncher.LocalServer;
            if (server == null) return;

            // 붙을 때까지 기다린다. 프레임 단위로 도는 것이 아니라 짧은 폴링이라 비용이 없다.
            //
            // <b>시나리오가 띄우려는 인원을 다 기다린다.</b> 먼저 붙은 한 명으로 시작하면
            // <c>StartMatch</c> 가 그 순간의 인원을 잠그므로(그것이 곳 "이 판의 인원"이다)
            // 늤에 들어온 클라이언트는 이미 지나간 판에 합류한다 — 검증이 무의미해진다.
            // <b>호스트는 자기도 한 명이므로 더한다.</b> <see cref="ExpectedClientCount"/> 는 "이
            // 시나리오가 띄우는 <b>클라이언트</b> 수" 이고, 비교 대상인 <c>PlayerCount</c> 는
            // <c>OnPlayerJoined</c> 가 <b>호스트 자신까지</b> 담은 값이다. 데디는 서버가 플레이어가
            // 아니라 그대로 맞고, 호스트는 하나를 더해야 맞는다.
            //
            // <b>빼면 정반대로 망가진다</b> — 처음에 그렇게 썼다가 잡았다. 클라 1명을 기대할 때
            // <c>wanted</c> 가 1 이 되고 호스트 혼자로 이미 1 이므로, <b>클라이언트가 붙기 전에</b>
            // 매치가 시작된다. 그러면 늦게 온 사람은 <see cref="SessionLauncher.StartMatch"/> 가
            // 인원을 잠근 뒤에 합류해 이미 지나간 판에 들어온다 — 검증이 조용히 무의미해진다.
            int wanted = ExpectedClientCount() + (asHost ? 1 : 0);
            while (server != null && server.Runner != null && server.Runner.IsRunning)
            {
                if (server.PlayerCount >= wanted && server.PeerPhase == ESessionPhase.Lobby)
                {
                    Debug.Log($"[MPPM] 플레이어 {server.PlayerCount}명(기대 {wanted}명) - 매치를 시작한다.");
                    _ = SessionLauncher.StartMatch();
                    return;
                }

                await Task.Delay(250);
            }
        }

        /// <summary>방이 생길 때까지 재시도하며 붙는다.
        ///
        /// <para><b>왜 필요한가.</b> 서버와 클라이언트 인스턴스는 동시에 올라오고, 각자 자기 프로세스에서
        /// 독립적으로 이 코드를 돌린다. 누가 먼저랑 보장이 없다. 클라이언트가 먼저면 방이 아직 없어
        /// <c>GameNotFound</c> 로 죽고, 한 번 죽으면 끝이라 그 인스턴스는 영영 못 붙는다(실측).
        /// 서버 쪽 <see cref="HostAndWaitForPlayers"/> 는 이미 폴링으로 기다리는데 클라이언트만
        /// 단발이었다. 같은 패턴으로 맞춘다.</para>
        ///
        /// <para><b>MPPM 자동 시작 전용이다.</b> 사람이 로비 UI 로 붙는 경로는 방이 이미 있을 때만
        /// 누를 수 있으므로 이 경로를 타지 않는다. 그래서 재시도를 게임 로직이 아니라 여기에 둔다.</para>
        ///
        /// <para>사유를 구분하지 않는다 — <see cref="SessionLauncher.JoinRoom"/> 가 <c>bool</c> 만 돌려주기
        /// 때문이기도 하고, 시나리오 기동 중에는 어느 실패든 잠시 뒤 다시 해 보는 것이 맞기 때문이다.
        /// 상한을 두어 영원히 돌지는 않게 한다.</para></summary>
        /// <summary>이 시나리오가 띄우는 플레이어 수. 태그를 직접 세지 않고 환경 변수로 받는다 —
        /// MPPM 시나리오 정의를 읽는 API 가 버전마다 달라서 거기에 의존하면 쉽게 깨진다.
        /// 기본값 2 는 현재 시나리오(서버+클라2)의 클라이언트 수다.</summary>
        private static int ExpectedClientCount()
        {
            string raw = System.Environment.GetEnvironmentVariable("PPACK_MPPM_CLIENTS");
            return int.TryParse(raw, out int parsed) && parsed > 0 ? parsed : 2;
        }

        private static async Task JoinWhenRoomExists(string room)
        {
            const int AttemptIntervalMs = 250;
            const int TimeoutMs = 20000;

            for (int waited = 0; waited < TimeoutMs; waited += AttemptIntervalMs)
            {
                if (await SessionLauncher.JoinRoom(room)) return;
                await Task.Delay(AttemptIntervalMs);
            }

            Debug.LogError($"[MPPM] 방 {room} 에 {TimeoutMs / 1000}초 동안 붙지 못했다. " +
                           "서버 인스턴스가 떴는지, 시나리오 태그가 맞는지 확인할 것.");
        }
    }
}
