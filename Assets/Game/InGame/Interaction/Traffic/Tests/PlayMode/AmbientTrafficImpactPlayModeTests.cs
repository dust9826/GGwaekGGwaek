using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    [Category("AmbientTraffic")]
    public sealed class AmbientTrafficImpactPlayModeTests
    {
        private GameObject _root;

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__AmbientTrafficImpact");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 달리는_차에_부딪힌_펭귄은_임펄스로_날아간다()
        {
            TrafficLaneNetwork network = StraightNetwork();
            Assert.That(AmbientTrafficRouteSelector.TryPlan(network,
                network.FindNode("Start"), network.FindNode("End"), out TrafficRoute route), Is.True);
            AmbientTrafficWorld world = Child("World", Vector3.zero)
                .AddComponent<AmbientTrafficWorld>();

            GameObject penguin = Child("Penguin", new Vector3(0f, 0.9f, 0f));
            Rigidbody penguinBody = penguin.AddComponent<Rigidbody>();
            penguinBody.mass = 30f;
            penguin.AddComponent<CapsuleCollider>();
            penguin.AddComponent<PenguinControlState>();
            penguin.AddComponent<PenguinLocomotion>();
            PenguinImpactRelay relay = penguin.AddComponent<PenguinImpactRelay>();
            penguinBody.useGravity = false;
            penguinBody.constraints = RigidbodyConstraints.FreezeRotation;

            GameObject car = Child("Car", Vector3.zero);
            Rigidbody carBody = car.AddComponent<Rigidbody>();
            carBody.isKinematic = true;
            BoxCollider carCollider = car.AddComponent<BoxCollider>();
            carCollider.center = new Vector3(0f, 0.7f, 0f);
            carCollider.size = new Vector3(1.8f, 1.4f, 3.6f);
            AmbientTrafficVehicle vehicle = car.AddComponent<AmbientTrafficVehicle>();
            vehicle.Initialize(world, null, route, 6f, 17, 0f);

            float timeout = Time.time + 2f;
            float maximumSpeed = 0f;
            do
            {
                yield return new WaitForFixedUpdate();
                maximumSpeed = Mathf.Max(maximumSpeed, penguinBody.linearVelocity.magnitude);
            }
            while (!relay.IsHeavyImpactActive && Time.time < timeout);

            for (int index = 0; index < 2; index++)
            {
                yield return new WaitForFixedUpdate();
                maximumSpeed = Mathf.Max(maximumSpeed, penguinBody.linearVelocity.magnitude);
            }

            Assert.That(relay.IsHeavyImpactActive, Is.True);
            Assert.That(vehicle.PlayerImpactCount, Is.EqualTo(1));
            Assert.That(maximumSpeed, Is.GreaterThan(3f));
            Assert.That(penguinBody.linearVelocity.y, Is.GreaterThan(0f));
        }

        [UnityTest]
        public IEnumerator 차량_지붕으로_점프해_착지하면_큰충격과_회전을_발동하지_않는다()
        {
            GameObject car = Child("RoofCar", Vector3.zero);
            Rigidbody carBody = car.AddComponent<Rigidbody>();
            carBody.isKinematic = true;
            BoxCollider carCollider = car.AddComponent<BoxCollider>();
            carCollider.center = new Vector3(0f, 0.7f, 0f);
            carCollider.size = new Vector3(1.8f, 1.4f, 3.6f);
            AmbientTrafficVehicle vehicle = car.AddComponent<AmbientTrafficVehicle>();
            SetField(vehicle, "_cruiseSpeedMps", 6f);
            SetField(vehicle, "_currentSpeedMps", 6f);

            GameObject penguin = Child("RoofLandingPenguin", new Vector3(0f, 1.45f, 0f));
            Rigidbody penguinBody = penguin.AddComponent<Rigidbody>();
            penguinBody.mass = 30f;
            penguinBody.useGravity = false;
            penguinBody.constraints = RigidbodyConstraints.FreezeRotationX |
                                      RigidbodyConstraints.FreezeRotationZ;
            CapsuleCollider capsule = penguin.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.85f, 0f);
            capsule.height = 1.7f;
            capsule.radius = 0.4f;
            penguin.AddComponent<PenguinControlState>();
            PenguinLocomotion locomotion = penguin.AddComponent<PenguinLocomotion>();
            locomotion.enabled = false;
            PenguinImpactRelay relay = penguin.AddComponent<PenguinImpactRelay>();
            penguinBody.linearVelocity = Vector3.down;

            for (int index = 0; index < 10; index++) yield return new WaitForFixedUpdate();

            Assert.That(vehicle.PlayerImpactCount, Is.EqualTo(0));
            Assert.That(relay.IsHeavyImpactActive, Is.False);
            Assert.That(penguinBody.constraints, Is.EqualTo(
                RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ));
        }

        [UnityTest]
        public IEnumerator 비활성_차량은_스폰_위치로_옮긴_뒤_활성화된다()
        {
            GameObject spawnerObject = Child("Spawner", Vector3.zero);
            spawnerObject.AddComponent<AmbientTrafficWorld>();
            AmbientTrafficSpawner spawner = spawnerObject.AddComponent<AmbientTrafficSpawner>();

            GameObject penguin = Child("SpawnSweepProbe", new Vector3(-4f, 0.9f, -12f));
            Rigidbody penguinBody = penguin.AddComponent<Rigidbody>();
            penguinBody.mass = 30f;
            penguinBody.useGravity = false;
            penguinBody.constraints = RigidbodyConstraints.FreezeRotation;
            CapsuleCollider capsule = penguin.AddComponent<CapsuleCollider>();
            capsule.center = new Vector3(0f, 0.85f, 0f);
            capsule.height = 1.7f;
            capsule.radius = 0.4f;
            penguin.AddComponent<PenguinControlState>();
            PenguinLocomotion locomotion = penguin.AddComponent<PenguinLocomotion>();
            locomotion.enabled = false;
            PenguinImpactRelay relay = penguin.AddComponent<PenguinImpactRelay>();

            GameObject visual = Child("VisualPrefab", Vector3.zero);
            visual.SetActive(false);
            spawner.Configure(new[] { visual }, 6, 20260825, 0.31f);
            spawner.Spawn();

            Assert.That(spawner.Spawned.Count, Is.EqualTo(6));
            foreach (AmbientTrafficVehicle vehicle in spawner.Spawned)
                Assert.That(vehicle.gameObject.activeSelf, Is.True);

            for (int index = 0; index < 3; index++) yield return new WaitForFixedUpdate();

            int impactCount = 0;
            foreach (AmbientTrafficVehicle vehicle in spawner.Spawned)
                impactCount += vehicle.PlayerImpactCount;
            Assert.That(impactCount, Is.Zero,
                "활성 상태에서 원점→포털로 옮기면 kinematic 속도가 맵 전체를 스윕한다");
            Assert.That(relay.IsHeavyImpactActive, Is.False);
            Vector2 horizontalVelocity = new Vector2(
                penguinBody.linearVelocity.x, penguinBody.linearVelocity.z);
            Assert.That(horizontalVelocity.magnitude, Is.LessThan(0.1f));
        }

        [UnityTest]
        public IEnumerator 재스폰_포털_사이의_펭귄을_순간이동_경로로_치지_않는다()
        {
            TrafficLaneNetwork westNetwork = StraightNetwork(-40f, -30f);
            TrafficLaneNetwork eastNetwork = StraightNetwork(30f, 40f);
            Assert.That(AmbientTrafficRouteSelector.TryPlan(westNetwork,
                westNetwork.FindNode("Start"), westNetwork.FindNode("End"), out TrafficRoute westRoute), Is.True);
            Assert.That(AmbientTrafficRouteSelector.TryPlan(eastNetwork,
                eastNetwork.FindNode("Start"), eastNetwork.FindNode("End"), out TrafficRoute eastRoute), Is.True);

            AmbientTrafficWorld world = Child("RespawnWorld", Vector3.zero)
                .AddComponent<AmbientTrafficWorld>();
            GameObject penguin = Child("RespawnSweepProbe", new Vector3(0f, 0.9f, 0f));
            Rigidbody penguinBody = penguin.AddComponent<Rigidbody>();
            penguinBody.mass = 30f;
            penguinBody.useGravity = false;
            penguin.AddComponent<CapsuleCollider>();
            penguin.AddComponent<PenguinControlState>();
            PenguinLocomotion locomotion = penguin.AddComponent<PenguinLocomotion>();
            locomotion.enabled = false;
            penguinBody.useGravity = false;
            PenguinImpactRelay relay = penguin.AddComponent<PenguinImpactRelay>();

            GameObject car = Child("RespawningCar", Vector3.zero);
            Rigidbody carBody = car.AddComponent<Rigidbody>();
            carBody.isKinematic = true;
            BoxCollider carCollider = car.AddComponent<BoxCollider>();
            carCollider.center = new Vector3(0f, 0.7f, 0f);
            carCollider.size = new Vector3(1.8f, 1.4f, 3.6f);
            AmbientTrafficVehicle vehicle = car.AddComponent<AmbientTrafficVehicle>();
            vehicle.Initialize(world, null, westRoute, 6f, 1, 0f);
            for (int index = 0; index < 3; index++) yield return new WaitForFixedUpdate();

            car.SetActive(false);
            vehicle.Initialize(world, null, eastRoute, 6f, 1, 0f);
            car.SetActive(true);
            for (int index = 0; index < 4; index++) yield return new WaitForFixedUpdate();

            Assert.That(vehicle.PlayerImpactCount, Is.Zero);
            Assert.That(relay.IsHeavyImpactActive, Is.False);
            Assert.That(penguinBody.linearVelocity.magnitude, Is.LessThan(0.1f));
        }

        private GameObject Child(string name, Vector3 position)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            gameObject.transform.SetParent(_root.transform);
            gameObject.transform.position = position;
            return gameObject;
        }

        private static void SetField(object target, string name, object value)
            => target.GetType().GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
                .SetValue(target, value);

        private static TrafficLaneNetwork StraightNetwork()
            => StraightNetwork(-4f, 4f);

        private static TrafficLaneNetwork StraightNetwork(float startX, float endX)
            => new(
                new[]
                {
                    new TrafficNodeSpec("Start", new Vector3(startX, 0f, 0f)),
                    new TrafficNodeSpec("End", new Vector3(endX, 0f, 0f))
                },
                new[] { new TrafficRoadSpec("Road", "Start", "End") },
                laneOffsetM: 0f);
    }
}
