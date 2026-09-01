using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryRequestTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 성공_점수는_계획_경로_길이에만_비례한다()
        {
            DeliveryRequest request = MakeRequest(2.5f);

            Assert.That(request.PlannedLength, Is.EqualTo(10f).Within(0.01f));
            Assert.That(request.SuccessPoints, Is.EqualTo(25));
            Assert.IsTrue(request.Complete());
            Assert.That(request.State, Is.EqualTo(EDeliveryRequestState.Completed));
        }

        [Test]
        public void 눈으로_연속_정차한_시간만_취소를_만든다()
        {
            DeliveryRequest request = MakeRequest(1f);

            Assert.IsFalse(request.TickSnowBlocked(3f, 5f));
            request.ClearSnowBlocked();
            Assert.That(request.ContinuousSnowBlockedSeconds, Is.Zero);
            Assert.IsFalse(request.TickSnowBlocked(4.9f, 5f));
            Assert.IsTrue(request.TickSnowBlocked(0.1f, 5f));
            Assert.That(request.State, Is.EqualTo(EDeliveryRequestState.Cancelled));
            Assert.IsFalse(request.Complete());
        }

        private DeliveryRequest MakeRequest(float pointsPerMeter)
        {
            DeliveryRoadNode start = NewObject("Start").AddComponent<DeliveryRoadNode>();
            start.transform.position = Vector3.zero;
            DeliveryRoadNode end = NewObject("End").AddComponent<DeliveryRoadNode>();
            end.transform.position = new Vector3(0f, 0f, 10f);
            DeliveryRoadSegment segment = NewObject("Road").AddComponent<DeliveryRoadSegment>();
            segment.Configure(start, end, null, 6f, 0f, 0.25f);
            DeliveryFactory first = NewObject("FactoryA").AddComponent<DeliveryFactory>();
            first.Configure(start);
            DeliveryFactory second = NewObject("FactoryB").AddComponent<DeliveryFactory>();
            second.Configure(end);
            var route = new DeliveryRoute(new[] { new DeliveryRoadTraversal(segment, false) });
            return new DeliveryRequest(1, new[] { first, second }, route, pointsPerMeter);
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}

