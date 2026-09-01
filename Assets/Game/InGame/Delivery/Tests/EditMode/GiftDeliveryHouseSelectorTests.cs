using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class GiftDeliveryHouseSelectorTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 목표_길이에_가장_가까운_집을_고른다()
        {
            // 시작(A) -- 5m -- B(집0) -- 15m -- C(집1)
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 5f));
            DeliveryRoadNode c = Node("C", new Vector3(0f, 0f, 20f));
            DeliveryRoadNetwork network = Network(new[] { a, b, c },
                new[] { Segment("AB", a, b), Segment("BC", b, c) });
            DeliveryHouse houseNear = House(b);
            DeliveryHouse houseFar = House(c);

            bool found = GiftDeliveryHouseSelector.TrySelect(network, new[] { houseNear, houseFar },
                new[] { a }, targetLengthM: 6f, excludedHouseIndices: null,
                out int houseIndex, out float routeLength, out DeliveryRoute route);

            Assert.IsTrue(found);
            Assert.That(houseIndex, Is.EqualTo(0));
            Assert.That(routeLength, Is.EqualTo(5f).Within(0.01f));
            Assert.IsNotNull(route);
        }

        [Test]
        public void 진행중이거나_최근_사용한_집은_제외한다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 5f));
            DeliveryRoadNode c = Node("C", new Vector3(0f, 0f, 6f));
            DeliveryRoadNetwork network = Network(new[] { a, b, c },
                new[] { Segment("AB", a, b), Segment("AC", a, c) });
            DeliveryHouse houseB = House(b);
            DeliveryHouse houseC = House(c);

            bool found = GiftDeliveryHouseSelector.TrySelect(network, new[] { houseB, houseC },
                new[] { a }, targetLengthM: 5f, excludedHouseIndices: new[] { 0 },
                out int houseIndex, out _, out _);

            Assert.IsTrue(found);
            Assert.That(houseIndex, Is.EqualTo(1));
        }

        [Test]
        public void 참가자_여럿이면_최단_참가자_기준으로_길이를_잡는다()
        {
            // 참가자1(A) -- 20m -- 집(C) ; 참가자2(B) -- 3m -- 집(C)
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode c = Node("C", new Vector3(0f, 0f, 20f));
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 23f));
            DeliveryRoadNetwork network = Network(new[] { a, c, b },
                new[] { Segment("AC", a, c), Segment("BC", b, c) });
            DeliveryHouse house = House(c);

            bool found = GiftDeliveryHouseSelector.TrySelect(network, new[] { house },
                new[] { a, b }, targetLengthM: 5f, excludedHouseIndices: null,
                out _, out float routeLength, out _);

            Assert.IsTrue(found);
            // A->C 는 20m, B->C 는 3m 이므로 더 짧은 참가자 기준(3m)으로 잡혀야 한다.
            Assert.That(routeLength, Is.EqualTo(3f).Within(0.01f));
        }

        [Test]
        public void 도달_가능한_집이_없으면_false를_돌려준다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode isolated = Node("Isolated", new Vector3(0f, 0f, 5f));
            DeliveryRoadNetwork network = Network(new[] { a, isolated }, new DeliveryRoadSegment[0]);
            DeliveryHouse house = House(isolated);

            bool found = GiftDeliveryHouseSelector.TrySelect(network, new[] { house },
                new[] { a }, targetLengthM: 5f, excludedHouseIndices: null,
                out _, out _, out _);

            Assert.IsFalse(found);
        }

        private DeliveryRoadNode Node(string id, Vector3 position)
        {
            GameObject gameObject = NewObject(id);
            gameObject.transform.position = position;
            DeliveryRoadNode node = gameObject.AddComponent<DeliveryRoadNode>();
            node.Configure(id);
            return node;
        }

        private DeliveryRoadSegment Segment(string name, DeliveryRoadNode start, DeliveryRoadNode end)
        {
            DeliveryRoadSegment segment = NewObject(name).AddComponent<DeliveryRoadSegment>();
            segment.Configure(start, end, null, 6f, 0f, 0.25f);
            return segment;
        }

        private DeliveryHouse House(DeliveryRoadNode node)
        {
            DeliveryHouse house = NewObject("House").AddComponent<DeliveryHouse>();
            house.Configure(node, null, null);
            return house;
        }

        private DeliveryRoadNetwork Network(IReadOnlyList<DeliveryRoadNode> nodes,
                                            IReadOnlyList<DeliveryRoadSegment> segments)
        {
            DeliveryRoadNetwork network = NewObject("Network").AddComponent<DeliveryRoadNetwork>();
            network.Configure(nodes, segments, new DeliveryFactory[0]);
            return network;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
