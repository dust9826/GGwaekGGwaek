using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools.Utils;

namespace PPack
{
    public sealed class DeliveryRoutePlannerTests
    {
        [Test]
        public void 네트워크_검증은_기준_공장_자기_자신을_경로로_요구하지_않는다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", Vector3.forward * 5f);
            DeliveryRoadSegment road = Segment("AB", a, b);
            DeliveryFactory fa = Factory("FA", a);
            DeliveryFactory fb = Factory("FB", b);
            DeliveryRoadNetwork network = Network(new[] { a, b }, new[] { road }, new[] { fa, fb });

            Assert.IsTrue(network.TryValidate(out string error), error);
        }

        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 길이가_짧은_두_구간을_직선_한_구간보다_선택한다()
        {
            DeliveryRoadNode a = Node("A", new Vector3(0f, 0f, 0f));
            DeliveryRoadNode b = Node("B", new Vector3(10f, 0f, 0f));
            DeliveryRoadNode c = Node("C", new Vector3(3f, 0f, 0f));
            DeliveryRoadSegment direct = Segment("Direct", a, b, new[] { new Vector3(0f, 0f, 10f) });
            DeliveryRoadSegment first = Segment("First", a, c);
            DeliveryRoadSegment second = Segment("Second", c, b);
            DeliveryRoadNetwork network = Network(new[] { a, b, c }, new[] { direct, first, second });

            Assert.IsTrue(DeliveryRoutePlanner.TryPlan(network, a, b, out DeliveryRoute route));
            Assert.That(route.Traversals.Count, Is.EqualTo(2));
            Assert.That(route.Traversals[0].Segment, Is.EqualTo(first));
            Assert.That(route.Traversals[1].Segment, Is.EqualTo(second));
            Assert.That(route.Length, Is.EqualTo(10f).Within(0.05f));
        }

        [Test]
        public void 역방향_경로는_접선을_뒤집는다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 10f));
            DeliveryRoadSegment road = Segment("Road", a, b);
            DeliveryRoadNetwork network = Network(new[] { a, b }, new[] { road });

            Assert.IsTrue(DeliveryRoutePlanner.TryPlan(network, b, a, out DeliveryRoute route));
            Assert.IsTrue(route.Traversals[0].Reverse);
            Assert.That(route.Evaluate(0f).Forward, Is.EqualTo(Vector3.back).Using(Vector3ComparerWithEqualsOperator.Instance));
        }

        [Test]
        public void 여러_공장_방문_경로를_순서대로_이어붙인다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 5f));
            DeliveryRoadNode c = Node("C", new Vector3(0f, 0f, 12f));
            DeliveryRoadSegment ab = Segment("AB", a, b);
            DeliveryRoadSegment bc = Segment("BC", b, c);
            DeliveryFactory fa = Factory("FA", a);
            DeliveryFactory fb = Factory("FB", b);
            DeliveryFactory fc = Factory("FC", c);
            DeliveryRoadNetwork network = Network(new[] { a, b, c }, new[] { ab, bc }, new[] { fa, fb, fc });

            Assert.IsTrue(DeliveryRoutePlanner.TryPlanStops(network, new[] { fa, fb, fc }, out DeliveryRoute route));
            Assert.That(route.Traversals.Count, Is.EqualTo(2));
            Assert.That(route.Length, Is.EqualTo(12f).Within(0.01f));
        }

        private DeliveryRoadNode Node(string id, Vector3 position)
        {
            GameObject gameObject = NewObject(id);
            gameObject.transform.position = position;
            DeliveryRoadNode node = gameObject.AddComponent<DeliveryRoadNode>();
            node.Configure(id);
            return node;
        }

        private DeliveryRoadSegment Segment(string name, DeliveryRoadNode start, DeliveryRoadNode end,
                                            IReadOnlyList<Vector3> intermediate = null, float width = 6f)
        {
            DeliveryRoadSegment segment = NewObject(name).AddComponent<DeliveryRoadSegment>();
            segment.Configure(start, end, intermediate, width, 0f, 0.25f);
            return segment;
        }

        private DeliveryFactory Factory(string name, DeliveryRoadNode node)
        {
            DeliveryFactory factory = NewObject(name).AddComponent<DeliveryFactory>();
            factory.Configure(node);
            return factory;
        }

        private DeliveryRoadNetwork Network(IReadOnlyList<DeliveryRoadNode> nodes,
                                            IReadOnlyList<DeliveryRoadSegment> segments,
                                            IReadOnlyList<DeliveryFactory> factories = null)
        {
            DeliveryRoadNetwork network = NewObject("Network").AddComponent<DeliveryRoadNetwork>();
            network.Configure(nodes, segments, factories ?? new DeliveryFactory[0]);
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
