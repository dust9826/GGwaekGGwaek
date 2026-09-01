using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// "지금 이 씬은 싱글인가, 권위인가, 팔로워인가" 를 한 곳에서 답한다.
    ///
    /// <para><b>싱글과 권위는 같은 코드가 돈다.</b> 그것이 이 구조체의 존재 이유다 — 싱글과 멀티는
    /// 흐름이 같고 인원과 밸런스만 다르다(<c>docs/specs/2026-08-31-single-multi-pipeline.md</c>).
    /// 컴포넌트가 <see cref="IsAuthority"/> 하나만 보면 두 모드에서 같은 판정을 돌릴 수 있다.</para>
    ///
    /// <para><b>판정은 <see cref="Resolve"/> 가 갖고 <c>For</c> 는 갖지 않는다.</b>
    /// <c>NetworkRunner.IsRunning</c> 과 <c>IsServer</c> 는 세터가 없어 EditMode 에서 만들어 낼 수
    /// 없다. 분기를 순수 함수로 떼어 놓아야 전부 테스트할 수 있다.</para>
    /// </summary>
    public readonly struct StageSession
    {
        /// <summary>이 씬의 러너. 싱글이거나 남의 세션이면 <c>null</c>.</summary>
        public NetworkRunner Runner { get; }

        /// <summary>싱글이거나 서버다. <b>판정과 스폰을 돌려도 되는 쪽.</b></summary>
        public bool IsAuthority { get; }

        /// <summary>세션이 있고 서버가 아니다. <b>판정하지 않고 복제값만 읽는 쪽.</b></summary>
        public bool IsFollower { get; }

        /// <summary>이 판의 인원. 싱글은 언제나 1이다.</summary>
        public int PlayerCount { get; }

        private StageSession(NetworkRunner runner, bool isAuthority, bool isFollower, int playerCount)
        {
            Runner = runner;
            IsAuthority = isAuthority;
            IsFollower = isFollower;
            PlayerCount = playerCount;
        }

        /// <summary>순수 판정. 러너 없이 상태만 정한다 — 테스트가 부르는 것이 이것이다.</summary>
        internal static StageSession Resolve(bool hasSession, bool isServer, int expectedPlayerCount) =>
            ResolveWith(null, hasSession, isServer, expectedPlayerCount);

        /// <summary>같은 판정에 러너를 실어 준다. <c>For</c> 만 부른다.</summary>
        private static StageSession ResolveWith(
            NetworkRunner runner, bool hasSession, bool isServer, int expectedPlayerCount)
        {
            if (!hasSession) return new StageSession(null, true, false, 1);

            // StartMatch 전에는 ExpectedPlayerCount 가 0이다. 밸런스가 이 값으로 나누므로 1로 막는다.
            int players = Mathf.Max(1, expectedPlayerCount);
            return new StageSession(runner, isServer, !isServer, players);
        }

        /// <summary>
        /// 지금 이 씬이 그 세션의 게임플레이 씬인가.
        ///
        /// <para><b>왜 필요한가</b>(2026-08-31 실측). 러너는 <c>DontDestroyOnLoad</c> 라 멀티 세션이
        /// 살아 있는 채로 SinglePlay 에 들어가면 따라온다. 그때
        /// <c>NetworkRunner.GetRunnerForScene</c> 는 그 러너를 <b>그대로 돌려준다</b> — 막아 주지
        /// 않는다. 경로 대조가 유일한 방어다.</para>
        /// </summary>
        internal static bool SceneOwnsSession(string gameplayScenePath, string ownerScenePath)
        {
            if (string.IsNullOrEmpty(gameplayScenePath)) return false;
            return string.Equals(gameplayScenePath, ownerScenePath, System.StringComparison.Ordinal);
        }

        /// <summary>
        /// 이 오브젝트가 선 씬의 세션을 답한다. 세션이 없거나 남의 세션이면 싱글로 답한다.
        ///
        /// <para><b>프레임마다 부르지 않는다.</b> 러너 조회가 들어 있다. 한 번 받아 캐시한다.</para>
        ///
        /// <para>분기는 여기 없다 — <see cref="ResolveWith"/> 가 갖는다. 여기는 Fusion 에서 신호
        /// 셋(세션 있음·서버임·기대 인원)을 뽑아 넘기기만 한다.</para>
        /// </summary>
        public static StageSession For(GameObject owner)
        {
            if (owner == null) return Resolve(false, false, 0);

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(owner.scene);
            bool hasSession =
                runner != null &&
                runner.IsRunning &&
                SceneOwnsSession(SessionLauncher.GameplayScenePath, owner.scene.path);

            return ResolveWith(
                hasSession ? runner : null,
                hasSession,
                hasSession && runner.IsServer,
                SessionLauncher.ExpectedPlayerCount);
        }
    }
}
