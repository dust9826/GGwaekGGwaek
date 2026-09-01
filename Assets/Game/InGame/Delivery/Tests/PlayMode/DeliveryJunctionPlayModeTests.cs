using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class DeliveryJunctionPlayModeTests
    {
        private const float NodeRouteDistance = 12f;
        private const float IntersectionHalfSpan = 3f;

        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__DeliveryJunction");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 교차로에_동시에_닿는_두_트럭은_한_대씩_통과한다()
        {
            // 십자 교차로. 두 트럭이 같은 속도로 같은 거리에서 출발하므로 중재가 없으면
            // 정확히 같은 순간에 가운데 노드를 지난다.
            DeliveryRoadNode center = Node("C", Vector3.zero);
            DeliveryRoadNode west = Node("W", new Vector3(-NodeRouteDistance, 0f, 0f));
            DeliveryRoadNode east = Node("E", new Vector3(NodeRouteDistance, 0f, 0f));
            DeliveryRoadNode south = Node("S", new Vector3(0f, 0f, -NodeRouteDistance));
            DeliveryRoadNode north = Node("N", new Vector3(0f, 0f, NodeRouteDistance));

            DeliveryRoadSegment westCenter = Road("WC", west, center);
            DeliveryRoadSegment centerEast = Road("CE", center, east);
            DeliveryRoadSegment southCenter = Road("SC", south, center);
            DeliveryRoadSegment centerNorth = Road("CN", center, north);

            SnowStage snowStage = Child("SnowStage").AddComponent<SnowStage>();
            snowStage.Field.FillAll(0);
            DeliveryTrafficController traffic = Child("Traffic").AddComponent<DeliveryTrafficController>();
            DeliveryDirector director = Child("Director").AddComponent<DeliveryDirector>();

            DeliveryTruck horizontal = Truck("Horizontal", 0, west, east,
                                             westCenter, centerEast, director, traffic, snowStage);
            DeliveryTruck vertical = Truck("Vertical", 1, south, north,
                                           southCenter, centerNorth, director, traffic, snowStage);

            bool bothInsideAtOnce = false;
            bool everHeld = false;
            bool horizontalMoved = false;
            bool verticalMoved = false;
            float minHorizontal = float.MaxValue;
            float minVertical = float.MaxValue;
            float deadline = Time.time + 25f;
            while (Time.time < deadline)
            {
                if (horizontal == null || vertical == null) break;
                if (horizontal.Request.State != EDeliveryRequestState.Active
                    && vertical.Request.State != EDeliveryRequestState.Active) break;

                bool horizontalInside = IsInsideIntersection(horizontal);
                bool verticalInside = IsInsideIntersection(vertical);
                if (horizontalInside && verticalInside) bothInsideAtOnce = true;
                // 출발 전 정지는 세지 않는다 — 한 번 달린 트럭이 실제로 다시 선 것만 본다.
                // (거리로 자르면 출발선 근처에서 서는 경우를 통째로 놓친다.)
                horizontalMoved |= horizontal.CurrentSpeed > 1f;
                verticalMoved |= vertical.CurrentSpeed > 1f;
                if (horizontalMoved && IsStopped(horizontal)) everHeld = true;
                if (verticalMoved && IsStopped(vertical)) everHeld = true;
                if (horizontalMoved) minHorizontal = Mathf.Min(minHorizontal, horizontal.CurrentSpeed);
                if (verticalMoved) minVertical = Mathf.Min(minVertical, vertical.CurrentSpeed);

                yield return new WaitForFixedUpdate();
            }

            Assert.IsFalse(bothInsideAtOnce, "두 트럭이 동시에 교차로 안에 있으면 안 된다");
            Assert.IsTrue(everHeld,
                $"한 대는 실제로 정지해서 기다려야 한다 (최저속도 h={minHorizontal:F3} v={minVertical:F3})");
            Assert.That(director.TotalPoints, Is.GreaterThan(0), "양보한 트럭도 결국 완주해야 한다");
        }

        private static bool IsStopped(DeliveryTruck truck)
            => truck.Request.State == EDeliveryRequestState.Active && truck.CurrentSpeed < 0.01f;

        private static bool IsInsideIntersection(DeliveryTruck truck)
            => truck.Request.State == EDeliveryRequestState.Active
               && Mathf.Abs(truck.RouteDistance - NodeRouteDistance) < IntersectionHalfSpan;

        private DeliveryTruck Truck(string name, int requestId,
                                    DeliveryRoadNode from, DeliveryRoadNode to,
                                    DeliveryRoadSegment first, DeliveryRoadSegment second,
                                    DeliveryDirector director, DeliveryTrafficController traffic,
                                    SnowStage snowStage)
        {
            DeliveryFactory origin = Child($"Factory{name}A").AddComponent<DeliveryFactory>();
            origin.Configure(from);
            DeliveryFactory destination = Child($"Factory{name}B").AddComponent<DeliveryFactory>();
            destination.Configure(to);

            var route = new DeliveryRoute(new[]
            {
                new DeliveryRoadTraversal(first, false),
                new DeliveryRoadTraversal(second, false)
            });
            var request = new DeliveryRequest(requestId, new[] { origin, destination }, route, 1f);

            DeliveryTruck truck = Child($"Truck{name}").AddComponent<DeliveryTruck>();
            truck.Initialize(request, director, traffic, snowStage);
            return truck;
        }

        private DeliveryRoadNode Node(string id, Vector3 position)
        {
            DeliveryRoadNode node = Child(id).AddComponent<DeliveryRoadNode>();
            node.Configure(id);
            node.transform.position = position;
            return node;
        }

        private DeliveryRoadSegment Road(string name, DeliveryRoadNode start, DeliveryRoadNode end)
        {
            DeliveryRoadSegment segment = Child(name).AddComponent<DeliveryRoadSegment>();
            segment.Configure(start, end, null, 6f, 0f, 0.25f);
            return segment;
        }

        private GameObject Child(string name)
        {
            var gameObject = new GameObject("__TEST__" + name);
            gameObject.transform.SetParent(_root.transform);
            return gameObject;
        }
    }
}
