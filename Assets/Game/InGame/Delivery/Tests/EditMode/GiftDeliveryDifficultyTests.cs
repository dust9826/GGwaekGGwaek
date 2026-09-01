using NUnit.Framework;

namespace PPack
{
    public sealed class GiftDeliveryDifficultyTests
    {
        [Test]
        public void 완료_수가_늘수록_목표_길이는_단조_증가한다()
        {
            GiftDeliveryDifficultySettings settings = GiftDeliveryDifficultySettings.Default;

            GiftDeliveryTarget early = GiftDeliveryDifficulty.Evaluate(0, settings);
            GiftDeliveryTarget later = GiftDeliveryDifficulty.Evaluate(5, settings);

            Assert.That(later.TargetRouteLengthM, Is.GreaterThan(early.TargetRouteLengthM));
        }

        [Test]
        public void 목표_길이는_상한을_넘지_않는다()
        {
            GiftDeliveryDifficultySettings settings = GiftDeliveryDifficultySettings.Default;

            GiftDeliveryTarget target = GiftDeliveryDifficulty.Evaluate(10000, settings);

            Assert.That(target.TargetRouteLengthM, Is.EqualTo(settings.MaxRouteLengthM).Within(0.01f));
        }

        [Test]
        public void 완료_수가_늘수록_시간_여유는_단조_감소하고_하한을_지킨다()
        {
            GiftDeliveryDifficultySettings settings = GiftDeliveryDifficultySettings.Default;

            GiftDeliveryTarget early = GiftDeliveryDifficulty.Evaluate(0, settings);
            GiftDeliveryTarget later = GiftDeliveryDifficulty.Evaluate(10000, settings);

            float earlySlackImpliedLimit = early.TimeLimitSeconds / (early.TargetRouteLengthM / settings.AssumedSpeedMps);
            float laterSlackImpliedLimit = later.TimeLimitSeconds / (later.TargetRouteLengthM / settings.AssumedSpeedMps);

            Assert.That(laterSlackImpliedLimit, Is.LessThanOrEqualTo(earlySlackImpliedLimit));
            Assert.That(laterSlackImpliedLimit, Is.GreaterThanOrEqualTo(settings.MinTimeSlackMultiplier - 0.01f));
        }

        [Test]
        public void 요구_선물_수는_상한을_넘지_않는다()
        {
            GiftDeliveryDifficultySettings settings = GiftDeliveryDifficultySettings.Default;

            GiftDeliveryTarget target = GiftDeliveryDifficulty.Evaluate(10000, settings);

            Assert.That(target.RequiredGiftCount, Is.EqualTo(settings.MaxGiftCount));
        }

        [Test]
        public void 요구_값어치는_상한을_넘지_않는다()
        {
            GiftDeliveryDifficultySettings settings = GiftDeliveryDifficultySettings.Default;

            GiftDeliveryTarget target = GiftDeliveryDifficulty.Evaluate(10000, settings);

            Assert.That(target.RequiredTotalValue, Is.EqualTo(settings.MaxRequiredValue));
        }
    }
}
