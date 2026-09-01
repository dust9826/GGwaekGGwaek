using NUnit.Framework;

namespace PPack
{
    public sealed class StageSessionTests
    {
        [Test]
        public void 세션이_없으면_싱글이고_권위이며_인원은_1이다()
        {
            StageSession session = StageSession.Resolve(hasSession: false, isServer: false, expectedPlayerCount: 4);

            Assert.That(session.IsAuthority, Is.True, "싱글은 권위다 — 서버와 같은 코드가 돌아야 한다");
            Assert.That(session.IsFollower, Is.False);
            Assert.That(session.PlayerCount, Is.EqualTo(1), "세션이 없으면 인원은 언제나 1이다");
        }

        [Test]
        public void 세션이_있고_서버면_권위이고_인원은_기대값이다()
        {
            StageSession session = StageSession.Resolve(hasSession: true, isServer: true, expectedPlayerCount: 3);

            Assert.That(session.IsAuthority, Is.True);
            Assert.That(session.IsFollower, Is.False);
            Assert.That(session.PlayerCount, Is.EqualTo(3));
        }

        [Test]
        public void 세션이_있고_서버가_아니면_팔로워다()
        {
            StageSession session = StageSession.Resolve(hasSession: true, isServer: false, expectedPlayerCount: 3);

            Assert.That(session.IsAuthority, Is.False, "클라이언트는 판정하지 않는다");
            Assert.That(session.IsFollower, Is.True);
            Assert.That(session.PlayerCount, Is.EqualTo(3));
        }

        [Test]
        public void 기대_인원이_0이어도_인원은_1_아래로_내려가지_않는다()
        {
            StageSession session = StageSession.Resolve(hasSession: true, isServer: true, expectedPlayerCount: 0);

            Assert.That(session.PlayerCount, Is.EqualTo(1),
                "StartMatch 전에는 ExpectedPlayerCount 가 0이다 — 밸런스가 0으로 나눠지면 안 된다");
        }

        [Test]
        public void 씬_경로가_같으면_이_씬의_세션이다()
        {
            Assert.That(
                StageSession.SceneOwnsSession(
                    "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity",
                    "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity"),
                Is.True);
        }

        [Test]
        public void 씬_경로가_어긋나면_남의_세션이다()
        {
            // 2026-08-31 회귀: MPPM host 태그를 단 채 SinglePlay 에 들어가면 러너가 따라오고
            // GetRunnerForScene 가 그것을 돌려준다. 경로 대조가 유일한 방어다.
            Assert.That(
                StageSession.SceneOwnsSession(
                    gameplayScenePath: "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity",
                    ownerScenePath: "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity"),
                Is.False);
        }

        [Test]
        public void 게임플레이_씬_경로가_비어_있으면_남의_세션으로_친다()
        {
            Assert.That(
                StageSession.SceneOwnsSession(null, "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity"),
                Is.False);
            Assert.That(
                StageSession.SceneOwnsSession("", "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity"),
                Is.False);
        }
    }
}
