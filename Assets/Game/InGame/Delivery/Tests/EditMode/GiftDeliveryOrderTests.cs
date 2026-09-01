using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class GiftDeliveryOrderTests
    {
        [Test]
        public void 퀘스트_색은_주문_번호에_따라_순환한다()
        {
            var first = new GiftDeliveryOrder(0, 0, 40f, 1, 0, 5f);
            var second = new GiftDeliveryOrder(1, 0, 40f, 1, 0, 5f);
            var repeated = new GiftDeliveryOrder(6, 0, 40f, 1, 0, 5f);

            Assert.That(first.QuestColor, Is.Not.EqualTo(second.QuestColor));
            Assert.That(repeated.QuestColor, Is.EqualTo(first.QuestColor));
            Assert.That(first.QuestColor.a, Is.EqualTo(1f));
        }

        [Test]
        public void 퀘스트_데이터가_지정한_색을_사용할_수_있다()
        {
            Color color = new Color(0.2f, 0.4f, 0.8f, 0.3f);
            var order = new GiftDeliveryOrder(0, 0, 40f, 1, 0, 5f, color);

            Assert.That(order.QuestColor, Is.EqualTo(new Color(0.2f, 0.4f, 0.8f, 1f)));
        }

        [Test]
        public void 퀘스트_데이터가_요구_선물_종류를_지정할_수_있다()
        {
            var order = new GiftDeliveryOrder(
                0, 0, 40f, 2, 0, 5f, Color.cyan, EGiftBoxKind.Blue);

            Assert.That(order.GiftKind, Is.EqualTo(EGiftBoxKind.Blue));
            Assert.That(order.RequiredGiftCount, Is.EqualTo(2));
        }

        [Test]
        public void 시간_만료시_실패한다()
        {
            var order = new GiftDeliveryOrder(1, 0, 40f, 1, 0, 5f);

            order.Tick(5.1f);

            Assert.That(order.RemainingSeconds, Is.LessThan(0f));
            order.Fail(EGiftDeliveryFailReason.TimeExpired);
            Assert.That(order.State, Is.EqualTo(EGiftDeliveryOrderState.Failed));
            Assert.That(order.FailReason, Is.EqualTo(EGiftDeliveryFailReason.TimeExpired));
        }

        [Test]
        public void 개수만_채우고_값어치가_부족하면_미완료다()
        {
            var order = new GiftDeliveryOrder(1, 0, 40f, 2, 10, 30f);

            Assert.IsFalse(order.TryComplete(2, 5));
            Assert.That(order.State, Is.EqualTo(EGiftDeliveryOrderState.Active));
        }

        [Test]
        public void 개수와_값어치_둘_다_충족해야_완료된다()
        {
            var order = new GiftDeliveryOrder(1, 0, 40f, 2, 10, 30f);

            Assert.IsTrue(order.TryComplete(2, 10));
            Assert.That(order.State, Is.EqualTo(EGiftDeliveryOrderState.Completed));
        }

        [Test]
        public void 완료된_주문은_다시_완료되지_않는다()
        {
            var order = new GiftDeliveryOrder(1, 0, 40f, 1, 0, 30f);

            Assert.IsTrue(order.TryComplete(1, 0));
            Assert.IsFalse(order.TryComplete(1, 0));
        }
    }
}
