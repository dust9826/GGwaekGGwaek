using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 표시 문자열만 본다. 복제와 전송은 여기서 못 덮는다 — <c>[Networked]</c> 배열도 신뢰 채널도
    /// 살아 있는 러너가 필요하고, 그것은 EditMode 에서 만들 수 없다(<c>Core/Multiplay/AGENTS.md</c>).
    /// </summary>
    public sealed class LobbyNicknameTests
    {
        [Test]
        public void 이름이_있으면_이름과_번호를_같이_보여_준다()
        {
            Assert.That(SessionLobby.Format("PENGUIN", 2), Is.EqualTo("PENGUIN#2"));
        }

        [Test]
        public void 이름이_겹쳐도_번호로_갈린다()
        {
            Assert.That(SessionLobby.Format("PENGUIN", 1), Is.Not.EqualTo(SessionLobby.Format("PENGUIN", 2)),
                "둘 다 PENGUIN 이어도 화면에서 구분돼야 한다");
        }

        [Test]
        public void 이름이_없으면_번호만_남는다()
        {
            // 아직 안 왔거나 이미 사라진 경우다. 문장이 무의미해지지 않는 것이 요점이다.
            Assert.That(SessionLobby.Format(null, 2), Is.EqualTo("#2"));
            Assert.That(SessionLobby.Format("", 2), Is.EqualTo("#2"));
            Assert.That(SessionLobby.Format("   ", 2), Is.EqualTo("#2"));
        }

        [Test]
        public void 앞뒤_공백은_지운다()
        {
            Assert.That(SessionLobby.Format("  PENGUIN  ", 3), Is.EqualTo("PENGUIN#3"));
        }
    }
}
