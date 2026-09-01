using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryRoadNetworkTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 최근접_노드를_찾는다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(10f, 0f, 0f));
            DeliveryRoadNode c = Node("C", new Vector3(20f, 0f, 0f));
            DeliveryRoadNetwork network = NewObject("Network").AddComponent<DeliveryRoadNetwork>();
            network.Configure(new[] { a, b, c }, new DeliveryRoadSegment[0], new DeliveryFactory[0]);

            DeliveryRoadNode nearest = network.FindNearestNode(new Vector3(11f, 0f, 0f));

            Assert.That(nearest, Is.EqualTo(b));
        }

        private DeliveryRoadNode Node(string id, Vector3 position)
        {
            GameObject gameObject = NewObject(id);
            gameObject.transform.position = position;
            DeliveryRoadNode node = gameObject.AddComponent<DeliveryRoadNode>();
            node.Configure(id);
            return node;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
