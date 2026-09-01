using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class AmbientTrafficFlowPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__AmbientTrafficFlow");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 교차하는_두_차량은_통행권을_나눠_받고_모두_빠져나간다()
        {
            TrafficLaneNetwork network = CrossNetwork();
            Assert.That(AmbientTrafficRouteSelector.TryPlan(network,
                network.FindNode("West"), network.FindNode("East"), out TrafficRoute westEast),
                Is.True);
            Assert.That(AmbientTrafficRouteSelector.TryPlan(network,
                network.FindNode("South"), network.FindNode("North"), out TrafficRoute southNorth),
                Is.True);

            AmbientTrafficWorld world = _root.AddComponent<AmbientTrafficWorld>();
            AmbientTrafficVehicle first = Vehicle("WestEast", world, westEast, 1);
            AmbientTrafficVehicle second = Vehicle("SouthNorth", world, southNorth, 2);
            int maximumVehiclesInsideJunction = 0;
            float timeout = Time.time + 8f;

            while (Time.time < timeout
                   && (first.LaneDistance < first.CurrentLane.Length
                       || second.LaneDistance < second.CurrentLane.Length))
            {
                yield return new WaitForFixedUpdate();
                int inside = IsInsideJunction(first) ? 1 : 0;
                if (IsInsideJunction(second)) inside++;
                maximumVehiclesInsideJunction = Mathf.Max(maximumVehiclesInsideJunction, inside);
            }

            Assert.That(first.TotalDistanceTravelled, Is.GreaterThan(19f));
            Assert.That(second.TotalDistanceTravelled, Is.GreaterThan(19f));
            Assert.That(maximumVehiclesInsideJunction, Is.LessThanOrEqualTo(1));
        }

        private AmbientTrafficVehicle Vehicle(string name, AmbientTrafficWorld world,
            TrafficRoute route, int id)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            gameObject.transform.SetParent(_root.transform);
            Rigidbody body = gameObject.AddComponent<Rigidbody>();
            body.isKinematic = true;
            gameObject.AddComponent<BoxCollider>().size = new Vector3(1.8f, 1.4f, 3.6f);
            AmbientTrafficVehicle vehicle = gameObject.AddComponent<AmbientTrafficVehicle>();
            vehicle.Initialize(world, null, route, 6f, id, 0f);
            return vehicle;
        }

        private static bool IsInsideJunction(AmbientTrafficVehicle vehicle)
            => vehicle.transform.position.sqrMagnitude < 2.5f * 2.5f;

        private static TrafficLaneNetwork CrossNetwork()
            => new(
                new[]
                {
                    new TrafficNodeSpec("West", new Vector3(-10f, 0f, 0f)),
                    new TrafficNodeSpec("Center", Vector3.zero),
                    new TrafficNodeSpec("East", new Vector3(10f, 0f, 0f)),
                    new TrafficNodeSpec("South", new Vector3(0f, 0f, -10f)),
                    new TrafficNodeSpec("North", new Vector3(0f, 0f, 10f))
                },
                new[]
                {
                    new TrafficRoadSpec("WestRoad", "West", "Center"),
                    new TrafficRoadSpec("EastRoad", "Center", "East"),
                    new TrafficRoadSpec("SouthRoad", "South", "Center"),
                    new TrafficRoadSpec("NorthRoad", "Center", "North")
                });
    }
}
