using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class DeliveryTruckPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__DeliveryTruckPlayMode");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator CPU_눈_필드가_치워져_있으면_트럭이_경로를_완주한다()
        {
            DeliveryRoadNode a = Child("A").AddComponent<DeliveryRoadNode>();
            a.Configure("A");
            DeliveryRoadNode b = Child("B").AddComponent<DeliveryRoadNode>();
            b.Configure("B");
            b.transform.position = Vector3.forward * 4f;

            DeliveryRoadSegment road = Child("Road").AddComponent<DeliveryRoadSegment>();
            road.Configure(a, b, null, 6f, 0f, 0.25f);
            DeliveryFactory fa = Child("FactoryA").AddComponent<DeliveryFactory>();
            fa.Configure(a);
            DeliveryFactory fb = Child("FactoryB").AddComponent<DeliveryFactory>();
            fb.Configure(b);
            DeliveryRoadNetwork network = Child("Network").AddComponent<DeliveryRoadNetwork>();
            network.Configure(new[] { a, b }, new[] { road }, new[] { fa, fb });

            SnowStage snowStage = Child("SnowStage").AddComponent<SnowStage>();
            snowStage.Field.FillAll(0);
            DeliveryTrafficController traffic = Child("Traffic").AddComponent<DeliveryTrafficController>();
            traffic.Configure(network);
            DeliveryDirector director = Child("Director").AddComponent<DeliveryDirector>();
            director.Configure(network, null, snowStage, traffic);

            DeliveryRequest request = director.CreateRequest(new[] { fa, fb });
            DeliveryTruck truck = Child("Truck").AddComponent<DeliveryTruck>();
            truck.Initialize(request, director, traffic, snowStage);

            // 등속이 아니라 가속·감속한다. 4m 경로는 가속(2m/s²)으로 붙었다가 도착지 앞에서
            // 제동(4m/s²)으로 서므로 최고속이 3.3m/s 언저리이고 2.5초쯤 걸린다 — 옛 등속
            // 모델의 0.8초를 기준으로 잡았던 데드라인을 여유 있게 늘렸다.
            float deadline = Time.time + 8f;
            while (request.State == EDeliveryRequestState.Active && Time.time < deadline) yield return null;

            Assert.That(request.State, Is.EqualTo(EDeliveryRequestState.Completed));
            Assert.That(director.TotalPoints, Is.EqualTo(request.SuccessPoints));
        }

        [UnityTest]
        public IEnumerator 경로_화살표는_트럭_변환을_중복하지_않고_월드_도로에_놓인다()
        {
            DeliveryRoadNode a = Child("ArrowA").AddComponent<DeliveryRoadNode>();
            a.transform.position = new Vector3(12f, 0f, 8f);
            a.Configure("ArrowA");
            DeliveryRoadNode b = Child("ArrowB").AddComponent<DeliveryRoadNode>();
            b.transform.position = new Vector3(12f, 0f, 28f);
            b.Configure("ArrowB");

            DeliveryRoadSegment road = Child("ArrowRoad").AddComponent<DeliveryRoadSegment>();
            road.Configure(a, b, null, 6f, 0f, 0.25f);
            DeliveryFactory fa = Child("ArrowFactoryA").AddComponent<DeliveryFactory>();
            fa.Configure(a);
            DeliveryFactory fb = Child("ArrowFactoryB").AddComponent<DeliveryFactory>();
            fb.Configure(b);
            DeliveryRoadNetwork network = Child("ArrowNetwork").AddComponent<DeliveryRoadNetwork>();
            network.Configure(new[] { a, b }, new[] { road }, new[] { fa, fb });

            SnowStage snowStage = Child("ArrowSnowStage").AddComponent<SnowStage>();
            DeliveryTrafficController traffic = Child("ArrowTraffic").AddComponent<DeliveryTrafficController>();
            traffic.Configure(network);
            DeliveryDirector director = Child("ArrowDirector").AddComponent<DeliveryDirector>();
            director.Configure(network, null, snowStage, traffic);

            DeliveryRequest request = director.CreateRequest(new[] { fa, fb });
            GameObject truckObject = Child("ArrowTruck");
            DeliveryTruck truck = truckObject.AddComponent<DeliveryTruck>();
            truckObject.AddComponent<DeliveryRouteDisplay>();
            truck.Initialize(request, director, traffic, snowStage);

            yield return null;

            MeshFilter arrowFilter = null;
            MeshFilter[] filters = Object.FindObjectsByType<MeshFilter>(FindObjectsSortMode.None);
            for (int index = 0; index < filters.Length; index++)
            {
                if (filters[index].sharedMesh != null
                    && filters[index].sharedMesh.name == "DeliveryRouteRibbon")
                {
                    arrowFilter = filters[index];
                    break;
                }
            }

            Assert.That(arrowFilter, Is.Not.Null);
            Assert.That(arrowFilter.transform.parent, Is.Null,
                "월드 좌표 정점을 가진 화살표 오브젝트는 움직이는 트럭의 자식이면 안 된다");
            Assert.That(arrowFilter.transform.position, Is.EqualTo(Vector3.zero));
            Assert.That(arrowFilter.transform.rotation, Is.EqualTo(Quaternion.identity));

            Vector3[] vertices = arrowFilter.sharedMesh.vertices;
            Assert.That(vertices.Length, Is.GreaterThan(0));

            // 도로(x=12, DrivableWidth=6, 정방향)의 선호 차선 오프셋은
            // DeliveryRoutePose.PreferredLaneOffset = DrivableWidth * 0.25 = 1.5 다. 트럭은
            // 스폰 시점부터 이 오프셋에서 출발하므로(DeliveryTruck.Initialize, 2026-08-17 수정)
            // 차선 합류 구간에서도 드리프트가 없다 — 리본은 x=13.5 를 중심으로
            // ±_ribbonWidth/2(기본 0.25)만 걸친다. 이전 값(13.5~15.1)은 화살표 시절 폭이 더
            // 넓었을 때 값이라 리본 재설계(2026-08-18 main 병합) 이후 더는 맞지 않는다.
            const float expectedLaneX = 13.5f;
            const float ribbonHalfWidth = 0.3f; // 기본 0.25 + 모서리 라운딩 여유
            for (int index = 0; index < vertices.Length; index++)
            {
                Vector3 worldVertex = arrowFilter.transform.TransformPoint(vertices[index]);
                Assert.That(worldVertex.x, Is.InRange(expectedLaneX - ribbonHalfWidth, expectedLaneX + ribbonHalfWidth));
                Assert.That(worldVertex.z, Is.InRange(7f, 29f));
            }

            Object.Destroy(truckObject);
            yield return null;
            Assert.That(arrowFilter == null, Is.True, "트럭이 사라지면 독립 화살표 오브젝트도 정리돼야 한다");
        }

        [UnityTest]
        public IEnumerator 강_트리거에_들어간_차량은_스폰_자세로_돌아간다()
        {
            GameObject vehicle = Child("RiverVehicle");
            Rigidbody body = vehicle.AddComponent<Rigidbody>();
            body.useGravity = false;
            vehicle.AddComponent<BoxCollider>();
            vehicle.AddComponent<VehicleController>();

            GameObject spawnObject = Child("RiverSpawn");
            spawnObject.transform.SetPositionAndRotation(
                new Vector3(5f, 1f, 7f), Quaternion.Euler(0f, 60f, 0f));
            DeliveryPlayerSpawn spawn = spawnObject.AddComponent<DeliveryPlayerSpawn>();
            spawn.Configure(vehicle.transform);

            GameObject river = Child("RiverVolume");
            BoxCollider riverCollider = river.AddComponent<BoxCollider>();
            riverCollider.isTrigger = true;
            riverCollider.size = Vector3.one * 4f;
            river.AddComponent<DeliveryRiverRespawnVolume>().Configure(spawn);

            vehicle.transform.position = Vector3.forward * 10f;
            body.linearVelocity = new Vector3(3f, -5f, 2f);
            body.angularVelocity = Vector3.up * 4f;
            yield return new WaitForFixedUpdate();

            body.position = Vector3.zero;
            Physics.SyncTransforms();
            yield return new WaitForFixedUpdate();

            Assert.That(body.position, Is.EqualTo(spawnObject.transform.position)
                .Using(UnityEngine.TestTools.Utils.Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(Quaternion.Angle(body.rotation, spawnObject.transform.rotation), Is.LessThan(0.01f));
            Assert.That(body.linearVelocity, Is.EqualTo(Vector3.zero)
                .Using(UnityEngine.TestTools.Utils.Vector3ComparerWithEqualsOperator.Instance));
            Assert.That(body.angularVelocity, Is.EqualTo(Vector3.zero)
                .Using(UnityEngine.TestTools.Utils.Vector3ComparerWithEqualsOperator.Instance));
        }

        private GameObject Child(string name)
        {
            var gameObject = new GameObject("__TEST__" + name);
            gameObject.transform.SetParent(_root.transform);
            return gameObject;
        }
    }
}
