using NUnit.Framework;

namespace PPack
{
    public sealed class StageHighScoreTests
    {
        // 실제 스테이지 id 를 쓰면 테스트가 개발자의 진짜 기록을 지운다. PlayerPrefs 는 프로젝트
        // 전역이라 테스트 전용 id 를 따로 둔다.
        private const string TestStageId = "__TEST__stage";

        [SetUp]
        public void SetUp() => StageHighScore.Clear(TestStageId);

        [TearDown]
        public void TearDown() => StageHighScore.Clear(TestStageId);

        [Test]
        public void 기록이_없으면_0을_읽는다()
        {
            Assert.That(StageHighScore.Read(TestStageId), Is.EqualTo(0));
        }

        [Test]
        public void 첫_점수는_신기록이다()
        {
            Assert.That(StageHighScore.Submit(TestStageId, 120), Is.True);
            Assert.That(StageHighScore.Read(TestStageId), Is.EqualTo(120));
        }

        [Test]
        public void 낮은_점수는_기록을_밀어내지_않는다()
        {
            StageHighScore.Submit(TestStageId, 120);

            Assert.That(StageHighScore.Submit(TestStageId, 80), Is.False);
            Assert.That(StageHighScore.Read(TestStageId), Is.EqualTo(120));
        }

        [Test]
        public void 같은_점수는_신기록이_아니다()
        {
            StageHighScore.Submit(TestStageId, 120);

            Assert.That(StageHighScore.Submit(TestStageId, 120), Is.False);
        }

        // 0점으로 끝난 첫 판까지 NEW RECORD 를 띄우면 축하가 값싸진다.
        [Test]
        public void 첫_판이라도_0점은_신기록이_아니다()
        {
            Assert.That(StageHighScore.Submit(TestStageId, 0), Is.False);
        }
    }
}
