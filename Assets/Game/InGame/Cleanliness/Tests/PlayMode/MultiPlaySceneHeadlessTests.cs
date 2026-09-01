using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 서버 피어가 <b>그래픽 장치 없이</b> 게임플레이 씬을 올리고, 그 안의 권위 부품이 서는지
    /// 지키는 게이트.
    ///
    /// <para><b>2026-08-30 부터 이것은 출시 경로가 아니다</b>(출시 토폴로지가 호스트 모드로 바뀌었다).
    /// 그래도 값이 남는 쪽이 오히려 더 크다 — 이 테스트가 실제로 잡는 것은 <b>씬 배선</b>이기
    /// 때문이다. 아래 단언들(권위 격자가 서는가, 선물 공급원이 정확히 하나인가)은 토폴로지와
    /// 무관하게 깨질 수 있고, 깨지면 화면에서 "선물이 안 나온다" 로만 보인다.
    ///
    /// <para>2026-08-24 에 <c>MultiplayHeadlessTests</c> 에서 같은 이름의 테스트를 들어냈다.
    /// 그때 사라진 것은 검사가 아니라 <b>대상</b>이었다 — 가리키던 <c>MP_Gameplay</c> 씬을 제설차
    /// 철거가 지웠다. 새 펭귄 게임플레이 씬(<c>MultiPlay.unity</c>)이 생겼으므로 되살린다.</para>
    ///
    /// <para><b>왜 <c>Core</c> 가 아니라 여기인가.</b> 원래 자리인
    /// <c>PPack.Multiplay.PlayModeTests</c> 는 <c>PPack.Core</c> 만 참조한다. 검사 대상인
    /// <see cref="SnowCpuStage"/> 와 <see cref="GiftNetSpawner"/> 는 <c>InGame</c> 이라 거기서는
    /// 보이지 않는다. 어셈블리 경계를 넓히는 대신 테스트를 대상 쪽으로 옮겼다.</para>
    ///
    /// <para><b>이 테스트가 <see cref="SnowHeadlessTests"/> 와 겹치지 않는 이유.</b> 그쪽은 씬을
    /// 로드하지 않고 코드로 스테이지를 세운다. 그래서 <b>씬의 배선</b>은 아무것도 지키지 못한다 —
    /// 인스펙터 참조가 끊겨도 그쪽은 초록이다. 여기서 확인하는 것이 정확히 그 부분이다.</para>
    ///
    /// <para>에디터에서 돌리면 GPU 가 있으므로 절반만 검사한다. 본론은
    /// <c>-batchmode -nographics</c> 실행이다 — 루트 <c>AGENTS.md</c>: <b>에디터의 Server 모드는
    /// 가짜 통과다.</b></para>
    /// </summary>
    public sealed class MultiPlaySceneHeadlessTests
    {
        private const string MultiPlayScenePath =
            "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity";

        private string _originalGameplayScenePath;

        [SetUp]
        public void SetUp()
        {
            _originalGameplayScenePath = SessionLauncher.GameplayScenePath;
        }

        /// <summary>
        /// <b>씬을 반드시 내린다.</b> 이 테스트는 <c>LoadSceneMode.Single</c> 로 게임플레이 씬을
        /// 올리는데, PlayMode 배치는 <c>DisableSceneReload</c> 라 그대로 두면 <b>뒤에 오는 테스트가
        /// 남의 씬 위에서 돈다</b>. 그러면 단독으로는 통과하고 배치에서만 떨어져 원인이 엉뚱한
        /// 곳에 보인다(2026-08-20 실측, <c>SnowHeadlessTests</c> 의 남은 눈덩이와 같은 부류다).
        /// </summary>
        [UnityTearDown]
        public IEnumerator TearDown()
        {
            var leave = SessionLauncher.Leave();
            while (!leave.IsCompleted) yield return null;
            yield return null;

            SessionLauncher.GameplayScenePath = _originalGameplayScenePath;

            Scene loaded = SceneManager.GetSceneByPath(MultiPlayScenePath);
            if (loaded.IsValid() && loaded.isLoaded)
            {
                Scene blank = SceneManager.CreateScene("__TEST__BlankAfterMultiPlay");
                SceneManager.SetActiveScene(blank);
                AsyncOperation unload = SceneManager.UnloadSceneAsync(loaded);
                while (unload != null && !unload.isDone) yield return null;
            }

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

        /// <summary>
        /// 확인하는 것은 "에러가 안 났다" 가 아니라 <b>권위가 실제로 섰다</b> 이다 — 헤드리스에서
        /// 조용히 비는 것이 바로 이 부분이기 때문이다. 눈 격자는 그래픽 없이도 값을 갖고 있어야
        /// 하고, 선물 공급원은 정확히 하나여야 한다(교환기가 <c>FindFirstObjectByType</c> 으로
        /// 찾으므로 둘이면 어느 쪽이 걸릴지 정해지지 않는다).
        /// </summary>
        [UnityTest]
        public IEnumerator 서버는_그래픽_없이_게임플레이_씬을_올린다()
        {
            // <b>방 이름은 실행마다 다르게 만든다.</b> 고정 이름을 쓰면 앞선 실행의 방이 Photon 에
            // 남아 있을 때 GameIdAlreadyExists 로 죽는다(2026-08-30 실측 - 같은 에디터 세션에서
            // 테스트를 몇 번 돌리자 이 테스트만 계속 실패했고, 90초를 기다려도 그대로였다).
            // <b>테스트가 외부 전역 이름 공간이 깨끗하기를 기대하면 안 된다.</b>
            var host = SessionLauncher.HostServerOnly("HDLS2-" + System.DateTime.UtcNow.Ticks % 1000000L);
            while (!host.IsCompleted) yield return null;
            Assert.That(host.Result, Is.True, "서버 피어가 시작되지 않았다");

            // 실제 실행에서는 MultiPlayBootstrap 이 넣어 준다. 테스트에는 그 씬이 아직 없으므로
            // 여기서 직접 넣는다 — 이 값이 비면 StartMatch 가 "경로가 정해지지 않았다" 로 죽는다.
            SessionLauncher.GameplayScenePath = MultiPlayScenePath;

            var start = SessionLauncher.StartMatch();
            while (!start.IsCompleted) yield return null;
            Assert.That(start.Result, Is.True, "게임플레이 씬을 올리지 못했다");

            for (int i = 0; i < 600; i++)
            {
                if (SceneManager.GetSceneByPath(MultiPlayScenePath).isLoaded) break;
                yield return null;
            }

            Scene scene = SceneManager.GetSceneByPath(MultiPlayScenePath);
            Assert.That(scene.isLoaded, Is.True, $"{MultiPlayScenePath} 가 올라오지 않았다");

            SnowCpuStage stage = Object.FindAnyObjectByType<SnowCpuStage>();
            Assert.That(stage, Is.Not.Null, "게임플레이 씬에 SnowCpuStage 가 없다");
            Assert.That(stage.Field, Is.Not.Null, "권위 격자가 그래픽 없이 서지 않았다");
            Assert.That(stage.HasSimulationAuthority, Is.True, "서버 피어가 시뮬 권위를 갖지 않았다");
            Assert.That(stage.TotalHeightMm, Is.GreaterThan(0L), "격자에 눈이 하나도 없다");

            GiftNetSpawner[] spawners = Object.FindObjectsByType<GiftNetSpawner>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(spawners.Length, Is.EqualTo(1),
                        "선물 공급원은 정확히 하나여야 한다 — 교환기가 타입으로 찾는다");

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                Assert.That(Camera.allCamerasCount, Is.Zero,
                            "그래픽 없는 서버에 카메라가 살아 있다 — 로컬 전용 부품이 안 꺼졌다");
        }
    }
}
