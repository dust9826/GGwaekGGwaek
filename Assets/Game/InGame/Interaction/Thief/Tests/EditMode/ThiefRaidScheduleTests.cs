using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class ThiefRaidScheduleTests
    {
        [Test]
        public void 지연_샘플은_설정한_최소_최대_안에_있다()
        {
            var random = new System.Random(7123);
            AnimationCurve distribution = AnimationCurve.Linear(0f, 0f, 1f, 1f);
            for (int index = 0; index < 1000; index++)
            {
                float delay = ThiefRaidSchedule.SampleDelay(random, new Vector2(8f, 25f), distribution);
                Assert.That(delay, Is.InRange(8f, 25f));
            }
        }

        [Test]
        public void 분포_커브가_최소와_최대_쪽으로_샘플을_보낼_수_있다()
        {
            var random = new System.Random(1);
            AnimationCurve minimum = AnimationCurve.Constant(0f, 1f, 0f);
            AnimationCurve maximum = AnimationCurve.Constant(0f, 1f, 1f);

            Assert.That(ThiefRaidSchedule.SampleDelay(random, new Vector2(5f, 20f), minimum), Is.EqualTo(5f));
            Assert.That(ThiefRaidSchedule.SampleDelay(random, new Vector2(5f, 20f), maximum), Is.EqualTo(20f));
        }

        [Test]
        public void 실패마다_하나씩_보관하고_시간순으로_꺼낸다()
        {
            var schedule = new ThiefRaidSchedule();
            schedule.Enqueue(new PendingThiefRaid(1, 3, null, EGiftBoxKind.Red, 12f));
            schedule.Enqueue(new PendingThiefRaid(2, 7, null, EGiftBoxKind.Blue, 8f));

            Assert.That(schedule.Count, Is.EqualTo(2));
            Assert.That(schedule.TryTakeDue(7.99f, out _), Is.False);
            Assert.That(schedule.TryTakeDue(8f, out PendingThiefRaid first), Is.True);
            Assert.That(first.RequestId, Is.EqualTo(2));
            Assert.That(first.HouseIndex, Is.EqualTo(7));
            Assert.That(schedule.Count, Is.EqualTo(1));
        }

        [Test]
        public void 배달_실패는_의뢰의_집과_선물_단계를_습격_큐에_보존한다()
        {
            GameObject houseObject = new GameObject("__TEST__House");
            GameObject deliveryObject = new GameObject("__TEST__DeliveryDirector");
            GameObject thiefObject = new GameObject("__TEST__ThiefDirector");
            try
            {
                DeliveryHouse house = houseObject.AddComponent<DeliveryHouse>();
                GiftDeliveryDirector delivery = deliveryObject.AddComponent<GiftDeliveryDirector>();
                delivery.Configure(null, new[] { house });
                ThiefDirector thief = thiefObject.AddComponent<ThiefDirector>();
                SetPrivateField(thief, "_giftDeliveryDirector", delivery);
                var order = new GiftDeliveryOrder(42, 0, 10f, 1, 0, 5f,
                    Color.white, EGiftBoxKind.Green);

                MethodInfo onFailed = typeof(ThiefDirector).GetMethod("OnDeliveryOrderFailed",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(onFailed, Is.Not.Null);
                onFailed.Invoke(thief, new object[] { order, EGiftDeliveryFailReason.TimeExpired });

                var schedule = (ThiefRaidSchedule)GetPrivateField(thief, "_schedule");
                Assert.That(schedule.TryTakeDue(float.MaxValue, out PendingThiefRaid raid), Is.True);
                Assert.That(raid.RequestId, Is.EqualTo(42));
                Assert.That(raid.HouseIndex, Is.Zero);
                Assert.That(raid.AssignedHouse, Is.SameAs(house));
                Assert.That(raid.PreferredKind, Is.EqualTo(EGiftBoxKind.Green));
            }
            finally
            {
                Object.DestroyImmediate(thiefObject);
                Object.DestroyImmediate(deliveryObject);
                Object.DestroyImmediate(houseObject);
            }
        }

        private static object GetPrivateField(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            return field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, fieldName);
            field.SetValue(target, value);
        }
    }
}
