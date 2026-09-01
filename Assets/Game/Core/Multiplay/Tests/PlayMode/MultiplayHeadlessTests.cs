using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 서버 피어가 <b>그래픽 장치 없이</b> 세션을 여는지 지키는 게이트.
    ///
    /// <para><b>2026-08-30 부터 이것은 출시 경로가 아니다.</b> 출시 토폴로지가 호스트 모드로 바뀌어
    /// 서버는 언제나 누군가의 PC 이고 GPU 가 있다. 그래도 남기는 이유는 둘이다 — 세션 시작과 로비
    /// 스폰이 실제로 되는지 보는 스모크 테스트로서 값이 그대로이고, 데디가 필요해지는 날
    /// (전용 서버 빌드, 대회 서버) 그 선택지가 살아 있기 때문이다. 지우기는 쉽고 되살리기는 어렵다.
    ///
    /// <para>루트 <c>AGENTS.md</c> 가 요구하는 주기적 <c>-batchmode -nographics</c> 실행을 사람이 기억해야
    /// 하는 절차가 아니라 테스트로 만든 것이다. "에디터에서 Server 모드로 돌려봤다"는 거짓 통과다 —
    /// 에디터의 서버 피어에는 GPU 가 있어서 실제 서버가 못 하는 코드를 그대로 실행한다.</para>
    ///
    /// <para>그래픽이 있는 실행에서도 통과해야 한다. 그래야 평소 실행에서 배선이 깨진 것을 잡고,
    /// <c>-nographics</c> 실행에서는 그 위에 GPU 의존을 추가로 잡는다.</para>
    /// </summary>
    public sealed class MultiplayHeadlessTests
    {
        private static bool IsHeadless => SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null;

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var leave = SessionLauncher.Leave();
            while (!leave.IsCompleted) yield return null;
            yield return null;

            // <b>러너를 하나도 남기지 않는다.</b> 이것이 없으면 다음 테스트가 조용히 망가진다 —
            // <c>SnowCpuStage</c> 는 <c>NetworkRunner.Instances.Count == 0</c> 일 때만 standalone 으로
            // 가서 격자를 만든다. 러너가 하나라도 살아 있으면 자기 씬의 러너를 찾다가 못 찾고
            // <b>격자를 만들지 않은 채 나간다</b>. 그러면 눈보라 테스트가 Field == null 로 죽는데,
            // 증상이 눈보라처럼 보여서 원인이 아주 멀리 보인다(2026-08-31 실측: 테스트 어셈블리가
            // 하나 늘면서 실행 순서가 바뀌자 눈보라 4개가 한꺼번에 실패했다).
            //
            // <c>Leave()</c> 는 <c>SessionLauncher</c> 가 아는 피어만 정리하고 Unity 의 <c>Destroy</c>
            // 는 프레임 끝까지 지연되므로, 0 이 될 때까지 기다린다.
            for (int i = 0; i < 300 && Fusion.NetworkRunner.Instances.Count > 0; i++) yield return null;
            Assert.That(Fusion.NetworkRunner.Instances.Count, Is.Zero,
                        "러너가 남았다 - 이 테스트가 뒤에 오는 테스트를 망가뜨린다");
        }

        [UnityTest]
        public IEnumerator 서버는_그래픽_없이_방을_열고_로비를_스폰한다()
        {
            // 실행마다 다른 방 이름. 이유는 MultiPlaySceneHeadlessTests 주석 참고 - 지금은 통과하지만
            // 같은 결함이 그대로 있어서, 남겨 두면 다음에 이쪽이 터진다.
            var host = SessionLauncher.HostServerOnly("HDLS1-" + System.DateTime.UtcNow.Ticks % 1000000L);
            while (!host.IsCompleted) yield return null;

            Assert.That(host.Result, Is.True, "서버 피어가 시작되지 않았다");
            Assert.That(SessionLauncher.LocalServer, Is.Not.Null, "서버 런처가 없다");
            Assert.That(SessionLauncher.Phase, Is.EqualTo(ESessionPhase.Lobby),
                        "방을 연 직후 단계는 로비여야 한다");

            // 로비 오브젝트는 서버가 스폰한다. 여기서 null 이면 방 코드·인원수 UI 가 읽을 상태가 없다.
            Assert.That(SessionLobby.Instance, Is.Not.Null, "로비 오브젝트가 스폰되지 않았다");
            Assert.That(SessionLobby.Instance.Object.IsValid, Is.True, "로비 NetworkObject 가 무효다");
        }

        // 2026-08-24: <c>서버는_그래픽_없이_게임플레이_씬을_올린다</c> 를 들어냈다. 그 테스트의
        // 대상이 MP_Gameplay 씬과 그 안의 SnowV7MapRig 였는데, 씬은 제설차 철거가 지우고 리그는
        // 애초에 그 씬에 없었다(GUID 대조 — rigsTotal > 0 단언이 이미 거짓이었다).
        // <b>없어진 것은 검사가 아니라 대상이다.</b>
        //
        // 2026-08-29: 새 게임플레이 씬 MultiPlay.unity 로 되살렸다. 다만 <b>이 파일이 아니라</b>
        // PPack.Cleanliness.PlayModeTests 의 MultiPlaySceneHeadlessTests 에 있다 — 이 어셈블리는
        // PPack.Core 만 참조해서 검사 대상인 SnowCpuStage·GiftNetSpawner(InGame)가 보이지 않는다.
        // 어셈블리 경계를 넓히는 대신 테스트를 대상 쪽으로 옮겼다.
    }
}
