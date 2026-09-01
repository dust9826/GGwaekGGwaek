using NUnit.Framework;

namespace PPack
{
    public sealed class DeliveryTrafficPriorityTests
    {
        [Test]
        public void 출구에_가까운_트럭이_먼저다()
        {
            int comparison = DeliveryTrafficPriority.Compare(
                2f, 8f, 10,
                7f, 20f, 1);

            Assert.That(comparison, Is.LessThan(0));
        }

        [Test]
        public void 출구_거리가_같으면_더_깊이_진입한_트럭이_먼저다()
        {
            int comparison = DeliveryTrafficPriority.Compare(
                5f, 12f, 10,
                5f, 8f, 1);

            Assert.That(comparison, Is.LessThan(0));
        }

        [Test]
        public void 모든_공간_조건이_같으면_오래된_의뢰_ID가_먼저다()
        {
            int comparison = DeliveryTrafficPriority.Compare(
                5f, 8f, 2,
                5f, 8f, 7);

            Assert.That(comparison, Is.LessThan(0));
        }
    }
}

