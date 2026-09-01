using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class AmbientTrafficSpawner : MonoBehaviour
    {
        [SerializeField] private GameObject[] _vehicleVisualPrefabs = Array.Empty<GameObject>();
        [SerializeField, Range(1, 24)] private int _vehicleCount = 6;
        [SerializeField] private int _seed = 20260825;
        [SerializeField] private Vector2 _speedRangeMps = new Vector2(4.5f, 7f);
        [SerializeField] private float _roadSurfaceY = 0.31f;
        [SerializeField] private Vector3 _colliderCenter = new Vector3(0f, 0.72f, 0f);
        [SerializeField] private Vector3 _colliderSize = new Vector3(1.8f, 1.4f, 3.6f);
        [SerializeField, Min(1f)] private float _spawnClearanceM = 8f;
        [SerializeField] private Vector2 _respawnDelaySeconds = new Vector2(1f, 3f);

        private readonly List<AmbientTrafficVehicle> _vehicles = new();
        private readonly Dictionary<AmbientTrafficVehicle, float> _respawnAt = new();
        private readonly Dictionary<AmbientTrafficVehicle, int> _vehicleIds = new();
        private TrafficLaneNetwork _network;
        private AmbientTrafficWorld _world;
        private System.Random _random;

        public IReadOnlyList<AmbientTrafficVehicle> Spawned => _vehicles;
        public TrafficLaneNetwork Network => _network;

        public void Configure(IReadOnlyList<GameObject> visualPrefabs, int vehicleCount, int seed,
            float roadSurfaceY)
        {
            _vehicleVisualPrefabs = Copy(visualPrefabs);
            _vehicleCount = Mathf.Clamp(vehicleCount, 1, 24);
            _seed = seed;
            _roadSurfaceY = roadSurfaceY;
        }

        private void Start()
        {
            Spawn();
        }

        public void Spawn()
        {
            if (_vehicles.Count > 0 || _vehicleVisualPrefabs.Length == 0) return;

            _network = TrafficLaneNetwork.CreateWinterVillage();
            _world = GetComponent<AmbientTrafficWorld>();
            if (_world == null) _world = gameObject.AddComponent<AmbientTrafficWorld>();
            _random = new System.Random(_seed);
            for (int index = 0; index < _vehicleCount; index++)
            {
                GameObject visualPrefab = _vehicleVisualPrefabs[index % _vehicleVisualPrefabs.Length];
                AmbientTrafficVehicle vehicle = CreateVehicle(index, visualPrefab);
                _vehicles.Add(vehicle);
                _respawnAt.Add(vehicle, 0f);
                _vehicleIds.Add(vehicle, index);
            }
            IgnoreVehicleCollisions();
            RespawnReadyVehicles();
        }

        private void FixedUpdate()
        {
            if (_network != null) RespawnReadyVehicles();
        }

        public void Release(AmbientTrafficVehicle vehicle)
        {
            if (vehicle == null || !_respawnAt.ContainsKey(vehicle)) return;
            _world.Unregister(vehicle);
            vehicle.gameObject.SetActive(false);
            _respawnAt[vehicle] = Time.time + Mathf.Lerp(_respawnDelaySeconds.x,
                _respawnDelaySeconds.y, (float)_random.NextDouble());
        }

        private void RespawnReadyVehicles()
        {
            foreach (AmbientTrafficVehicle vehicle in _vehicles)
            {
                if (vehicle == null || vehicle.gameObject.activeSelf) continue;
                if (_respawnAt[vehicle] > Time.time) continue;
                if (!TryChooseSpawnRoute(out TrafficRoute route)) continue;

                float speed = Mathf.Lerp(_speedRangeMps.x, _speedRangeMps.y,
                    (float)_random.NextDouble());
                vehicle.Initialize(_world, this, route, speed, _vehicleIds[vehicle], _roadSurfaceY);
                vehicle.gameObject.SetActive(true);
            }
        }

        private bool TryChooseSpawnRoute(out TrafficRoute route)
        {
            route = null;
            var portals = new List<TrafficNode>(_network.Portals);
            Shuffle(portals, _random);
            foreach (TrafficNode portal in portals)
            {
                TrafficLane firstLane = portal.Outgoing[0];
                Vector3 spawnPosition = firstLane.Evaluate(0f).Position;
                if (!IsSpawnClear(spawnPosition)) continue;
                if (AmbientTrafficRouteSelector.TryChoosePortalRoute(
                        _network, portal, _random, out route)) return true;
            }
            return false;
        }

        private bool IsSpawnClear(Vector3 position)
        {
            float clearanceSqr = _spawnClearanceM * _spawnClearanceM;
            foreach (AmbientTrafficVehicle vehicle in _vehicles)
            {
                if (vehicle == null || !vehicle.gameObject.activeSelf) continue;
                if ((vehicle.transform.position - position).sqrMagnitude < clearanceSqr) return false;
            }
            return true;
        }

        private AmbientTrafficVehicle CreateVehicle(int index, GameObject visualPrefab)
        {
            var root = new GameObject($"AmbientCar_{index + 1:00}_{visualPrefab.name}");
            root.transform.SetParent(transform);

            Rigidbody body = root.AddComponent<Rigidbody>();
            body.isKinematic = true;
            BoxCollider collider = root.AddComponent<BoxCollider>();
            collider.center = _colliderCenter;
            collider.size = _colliderSize;
            AmbientTrafficVehicle vehicle = root.AddComponent<AmbientTrafficVehicle>();

            GameObject visual = Instantiate(visualPrefab, root.transform);
            visual.name = "Visual";
            visual.transform.SetLocalPositionAndRotation(Vector3.zero, Quaternion.identity);
            foreach (Collider childCollider in visual.GetComponentsInChildren<Collider>(true))
                childCollider.enabled = false;

            vehicle.enabled = true;
            root.SetActive(false);
            return vehicle;
        }

        private void IgnoreVehicleCollisions()
        {
            for (int first = 0; first < _vehicles.Count; first++)
            {
                Collider firstCollider = _vehicles[first].GetComponent<Collider>();
                for (int second = first + 1; second < _vehicles.Count; second++)
                {
                    Collider secondCollider = _vehicles[second].GetComponent<Collider>();
                    Physics.IgnoreCollision(firstCollider, secondCollider, true);
                }
            }
        }

        private static GameObject[] Copy(IReadOnlyList<GameObject> source)
        {
            if (source == null) return Array.Empty<GameObject>();
            var result = new GameObject[source.Count];
            for (int index = 0; index < source.Count; index++) result[index] = source[index];
            return result;
        }

        private static void Shuffle<T>(IList<T> values, System.Random random)
        {
            for (int index = values.Count - 1; index > 0; index--)
            {
                int other = random.Next(index + 1);
                (values[index], values[other]) = (values[other], values[index]);
            }
        }
    }
}
