using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class StageMetricsTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 완료와_취소를_상태별로_센다()
        {
            var requests = new List<DeliveryRequest>
            {
                MakeRequest(id: 1, completeIt: true),
                MakeRequest(id: 2, completeIt: true),
                MakeRequest(id: 3, cancelIt: true),
                MakeRequest(id: 4)
            };

            StageMetrics metrics = StageMetrics.Capture(requests, totalPoints: 42, field: null,
                initialTotalDepthCm: 0);

            Assert.That(metrics.DeliveriesCompleted, Is.EqualTo(2));
            Assert.That(metrics.DeliveriesCancelled, Is.EqualTo(1));
            Assert.That(metrics.TotalPoints, Is.EqualTo(42));
        }

        [Test]
        public void 초기_깊이가_0이면_제설률은_0이다()
        {
            StageMetrics metrics = StageMetrics.Capture(new List<DeliveryRequest>(), totalPoints: 0,
                field: null, initialTotalDepthCm: 0);

            Assert.That(metrics.SnowClearedPercent01, Is.Zero);
        }

        [Test]
        public void CPU_눈_총량으로_제설률을_계산한다()
        {
            StageMetrics metrics = StageMetrics.Capture(
                completed: 2, cancelled: 0, totalPoints: 7,
                currentSnowAmount: 400L, initialSnowAmount: 1000L);

            Assert.That(metrics.SnowClearedPercent01, Is.EqualTo(0.6f).Within(0.001f));
        }

        private DeliveryRequest MakeRequest(int id, bool completeIt = false, bool cancelIt = false)
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
            var request = new DeliveryRequest(id, new[] { first, second }, route, 1f);

            if (completeIt) request.Complete();
            if (cancelIt) request.TickSnowBlocked(999f, 0.01f);

            return request;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
