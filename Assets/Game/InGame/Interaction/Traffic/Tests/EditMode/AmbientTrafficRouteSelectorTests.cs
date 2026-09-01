using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    public sealed class AmbientTrafficRouteSelectorTests
    {
        [Test]
        public void 겨울마을_교통망은_다섯_경계_포털을_가진다()
        {
            TrafficLaneNetwork network = TrafficLaneNetwork.CreateWinterVillage();

            Assert.That(network.Portals, Has.Count.EqualTo(5));
            Assert.That(network.FindNode("DiagNW").IsPortal, Is.True);
            Assert.That(network.FindNode("DiagSE").IsPortal, Is.True);
            Assert.That(network.FindNode("CrossSW").IsPortal, Is.True);
            Assert.That(network.FindNode("SouthGate").IsPortal, Is.True);
            Assert.That(network.FindNode("NorthGate").IsPortal, Is.True);
        }

        [Test]
        public void 모든_포털_쌍은_방향성_차선으로_연결된다()
        {
            TrafficLaneNetwork network = TrafficLaneNetwork.CreateWinterVillage();

            foreach (TrafficNode origin in network.Portals)
            foreach (TrafficNode destination in network.Portals)
            {
                if (origin == destination) continue;
                Assert.That(AmbientTrafficRouteSelector.TryPlan(
                    network, origin, destination, out TrafficRoute route), Is.True,
                    $"{origin.Id} -> {destination.Id}");
                Assert.That(route.Origin, Is.SameAs(origin));
                Assert.That(route.Destination, Is.SameAs(destination));
                for (int index = 0; index + 1 < route.Lanes.Count; index++)
                    Assert.That(route.Lanes[index].To,
                        Is.SameAs(route.Lanes[index + 1].From));
            }
        }

        [Test]
        public void 최단_포털_경로는_같은_도로를_즉시_U턴하지_않는다()
        {
            TrafficLaneNetwork network = TrafficLaneNetwork.CreateWinterVillage();

            foreach (TrafficNode destination in network.Portals)
            {
                TrafficNode origin = network.FindNode("DiagNW");
                if (destination == origin) continue;
                Assert.That(AmbientTrafficRouteSelector.TryPlan(
                    network, origin, destination, out TrafficRoute route), Is.True);
                var visited = new HashSet<TrafficNode> { route.Origin };
                foreach (TrafficLane lane in route.Lanes)
                    Assert.That(visited.Add(lane.To), Is.True,
                        $"경로가 {lane.To.Id} 노드를 다시 방문했다");
            }
        }

        [Test]
        public void 차량별_시드가_다르면_목적지_포털도_분산된다()
        {
            TrafficLaneNetwork network = TrafficLaneNetwork.CreateWinterVillage();
            TrafficNode origin = network.FindNode("DiagNW");
            var destinations = new HashSet<TrafficNode>();

            for (int seed = 0; seed < 12; seed++)
            {
                Assert.That(AmbientTrafficRouteSelector.TryChoosePortalRoute(
                    network, origin, new System.Random(seed), out TrafficRoute route), Is.True);
                destinations.Add(route.Destination);
            }

            Assert.That(destinations.Count, Is.GreaterThan(1));
        }
    }
}
