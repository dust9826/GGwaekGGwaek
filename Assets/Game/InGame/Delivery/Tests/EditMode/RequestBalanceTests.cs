using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class RequestBalanceTests
    {
        private static StageBalanceConfig Config()
        {
            // ScriptableObject.CreateInstance 는 필드 초기값(StartSeconds=60 등)을 그대로 얻는다.
            return ScriptableObject.CreateInstance<StageBalanceConfig>();
        }

        [Test]
        public void 난이도는_양수다()
        {
            StageBalanceConfig c = Config();
            RequestBalanceResult r = RequestBalance.Evaluate(c, 1.5f, 60f, 1f, 0f);
            Assert.That(r.Difficulty, Is.GreaterThan(0f));
        }

        [Test]
        public void 거리가_멀수록_난이도가_오른다()
        {
            StageBalanceConfig c = Config();
            RequestBalanceResult near = RequestBalance.Evaluate(c, 1f, 30f, 1f, 0f);
            RequestBalanceResult far = RequestBalance.Evaluate(c, 1f, 120f, 1f, 0f);
            Assert.That(far.Difficulty, Is.GreaterThan(near.Difficulty));
        }

        [Test]
        public void 종류_가중치가_클수록_난이도가_오른다()
        {
            StageBalanceConfig c = Config();
            RequestBalanceResult easy = RequestBalance.Evaluate(c, 1f, 60f, 1f, 0f);
            RequestBalanceResult hard = RequestBalance.Evaluate(c, 1.9f, 60f, 1f, 0f);
            Assert.That(hard.Difficulty, Is.GreaterThan(easy.Difficulty));
        }

        [Test]
        public void 지터가_클수록_난이도가_오른다()
        {
            StageBalanceConfig c = Config();
            RequestBalanceResult low = RequestBalance.Evaluate(c, 1f, 60f, 0.9f, 0f);
            RequestBalanceResult high = RequestBalance.Evaluate(c, 1f, 60f, 1.1f, 0f);
            Assert.That(high.Difficulty, Is.GreaterThan(low.Difficulty));
        }

        [Test]
        public void 난이도가_클수록_보상과_TTL과_추가시간이_커진다()
        {
            StageBalanceConfig c = Config();
            RequestBalanceResult easy = RequestBalance.Evaluate(c, 1f, 30f, 0.9f, 0f);
            RequestBalanceResult hard = RequestBalance.Evaluate(c, 1.9f, 120f, 1.1f, 0f);
            Assert.That(hard.Reward, Is.GreaterThan(easy.Reward));
            Assert.That(hard.TtlSeconds, Is.GreaterThan(easy.TtlSeconds));
            Assert.That(hard.TimeBonusSeconds, Is.GreaterThan(easy.TimeBonusSeconds));
        }

        [Test]
        public void 전역_난이도_스칼라는_1에서_시작해_시간이_지날수록_커지고_상한을_지킨다()
        {
            StageBalanceConfig c = Config();
            float s0 = RequestBalance.GlobalDifficultyScalar(c, 0f);
            float sLater = RequestBalance.GlobalDifficultyScalar(c, 600f);
            float sHuge = RequestBalance.GlobalDifficultyScalar(c, 1_000_000f);
            Assert.That(s0, Is.EqualTo(1f).Within(0.001f));
            Assert.That(sLater, Is.GreaterThan(s0));
            Assert.That(sHuge, Is.LessThanOrEqualTo(c.GlobalDifficultyMax + 0.001f));
        }

        [Test]
        public void TTL_스칼라는_시간이_지날수록_줄고_하한을_지킨다()
        {
            StageBalanceConfig c = Config();
            float s0 = RequestBalance.TtlScalar(c, 0f);
            float sLater = RequestBalance.TtlScalar(c, 600f);
            float sHuge = RequestBalance.TtlScalar(c, 1_000_000f);
            Assert.That(s0, Is.EqualTo(1f).Within(0.001f));
            Assert.That(sLater, Is.LessThan(s0));
            Assert.That(sHuge, Is.GreaterThanOrEqualTo(c.TtlScalarMin - 0.001f));
        }

        [Test]
        public void 클리어_보너스_스칼라는_시간이_지날수록_줄고_하한을_지킨다()
        {
            StageBalanceConfig c = Config();
            float s0 = RequestBalance.ClearBonusScalar(c, 0f);
            float sLater = RequestBalance.ClearBonusScalar(c, 600f);
            float sHuge = RequestBalance.ClearBonusScalar(c, 1_000_000f);
            Assert.That(s0, Is.EqualTo(1f).Within(0.001f));
            Assert.That(sLater, Is.LessThan(s0));
            Assert.That(sHuge, Is.GreaterThanOrEqualTo(c.ClearBonusScalarMin - 0.001f));
        }
    }
}
