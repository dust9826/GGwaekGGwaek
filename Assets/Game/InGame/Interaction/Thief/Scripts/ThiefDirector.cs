using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;
using UnityEngine.AI;

namespace PPack
{
    /// <summary>의뢰 실패를 지연된 도둑 습격으로 바꾸는 서버 권위 진입점이다.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefDirector : MonoBehaviour
    {
        [SerializeField] private RequestDirector _requestDirector;
        [SerializeField] private GiftDeliveryDirector _giftDeliveryDirector;
        [SerializeField] private ThiefRaidSite _raidSite;
        [SerializeField] private GameObject _thiefPrefab;
        [SerializeField] private Vector2 _spawnDelaySecondsRange = new Vector2(8f, 25f);
        [SerializeField] private AnimationCurve _spawnDelayDistribution01 =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        [SerializeField] private Vector2 _houseSpawnRadiusMRange = new Vector2(1.5f, 4f);
        [SerializeField] private AnimationCurve _houseSpawnRadiusDistribution01 =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        [SerializeField, Min(1)] private int _houseSpawnSampleAttempts = 16;
        [SerializeField, Min(0.1f)] private float _houseSpawnNavMeshRadiusM = 2f;
        [SerializeField, Min(0.1f)] private float _failedSpawnRetrySeconds = 2f;
        [SerializeField] private int _randomSeed;

        private readonly ThiefRaidSchedule _schedule = new ThiefRaidSchedule();
        private readonly Dictionary<DeliveryHouse, ThiefActor> _activeRaidByHouse =
            new Dictionary<DeliveryHouse, ThiefActor>();
        private System.Random _random;
        private float _nextSpawnRetryTime;
        private bool _requestSubscribed;
        private bool _giftDeliverySubscribed;

        public int PendingRaidCount => _schedule.Count;

        private void Awake()
        {
            _random = _randomSeed != 0 ? new System.Random(_randomSeed) : new System.Random();
            TryBindFailureSources();
        }

        private void OnEnable()
        {
            TryBindFailureSources();
        }

        private void OnDisable()
        {
            if (_requestSubscribed && _requestDirector != null)
                _requestDirector.RequestExpired -= OnRequestExpired;
            if (_giftDeliverySubscribed && _giftDeliveryDirector != null)
                _giftDeliveryDirector.OrderFailed -= OnDeliveryOrderFailed;
            _requestSubscribed = false;
            _giftDeliverySubscribed = false;
            _schedule.Clear();
        }

        private void Update()
        {
            if (!_requestSubscribed && !_giftDeliverySubscribed) TryBindFailureSources();
            if (!HasAuthority() || Time.time < _nextSpawnRetryTime) return;
            if (!_schedule.TryTakeDue(Time.time, out PendingThiefRaid raid)) return;
            if (TrySpawn(raid)) return;

            _schedule.Enqueue(new PendingThiefRaid(raid.RequestId, raid.HouseIndex,
                raid.AssignedHouse, raid.PreferredKind, Time.time + _failedSpawnRetrySeconds));
            _nextSpawnRetryTime = Time.time + _failedSpawnRetrySeconds;
        }

        public void EnqueueRaid(int requestId, EGiftBoxKind preferredKind)
            => EnqueueRaid(requestId, -1, null, preferredKind);

        public void EnqueueRaid(int requestId, int houseIndex, EGiftBoxKind preferredKind)
            => EnqueueRaid(requestId, houseIndex, ResolveHouse(houseIndex), preferredKind);

        private void EnqueueRaid(int requestId, int houseIndex, DeliveryHouse assignedHouse,
            EGiftBoxKind preferredKind)
        {
            if (!HasAuthority()) return;
            _random ??= _randomSeed != 0 ? new System.Random(_randomSeed) : new System.Random();
            float delay = ThiefRaidSchedule.SampleDelay(
                _random, _spawnDelaySecondsRange, _spawnDelayDistribution01);
            _schedule.Enqueue(new PendingThiefRaid(requestId, houseIndex, assignedHouse,
                preferredKind, Time.time + delay));
        }

        private void OnRequestExpired(GiftRequest request)
        {
            if (request == null) return;
            DeliveryHouse house = _requestDirector != null
                ? _requestDirector.HouseAt(request.HouseIndex) : null;
            EnqueueRaid(request.Id, request.HouseIndex, house, request.WantedKind);
        }

        private void OnDeliveryOrderFailed(GiftDeliveryOrder order, EGiftDeliveryFailReason reason)
        {
            if (order == null) return;
            DeliveryHouse house = _giftDeliveryDirector != null && order.HouseIndex >= 0 &&
                                  order.HouseIndex < _giftDeliveryDirector.Houses.Count
                ? _giftDeliveryDirector.Houses[order.HouseIndex] : null;
            EnqueueRaid(order.Id, order.HouseIndex, house, order.GiftKind);
        }

        private void TryBindFailureSources()
        {
            if (!_requestSubscribed)
            {
                if (_requestDirector == null) _requestDirector = FindFirstObjectByType<RequestDirector>();
                if (_requestDirector != null)
                {
                    _requestDirector.RequestExpired += OnRequestExpired;
                    _requestSubscribed = true;
                }
            }

            if (_giftDeliverySubscribed) return;
            if (_giftDeliveryDirector == null)
                _giftDeliveryDirector = FindFirstObjectByType<GiftDeliveryDirector>();
            if (_giftDeliveryDirector == null) return;
            _giftDeliveryDirector.OrderFailed += OnDeliveryOrderFailed;
            _giftDeliverySubscribed = true;
        }

        private bool TrySpawn(PendingThiefRaid raid)
        {
            if (_raidSite == null || _thiefPrefab == null || HasActiveRaid(raid.AssignedHouse) ||
                !TrySampleSpawnPoint(raid, out Vector3 spawnPosition)) return false;

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null)
            {
                GameObject instance = Instantiate(_thiefPrefab, spawnPosition, Quaternion.identity);
                Initialize(instance, raid, spawnPosition);
                return true;
            }

            if (!runner.IsRunning || !runner.IsServer) return false;
            NetworkObject networkPrefab = _thiefPrefab.GetComponent<NetworkObject>();
            if (networkPrefab == null)
            {
                Debug.LogError($"{name}: 멀티플레이 도둑 프리팹에는 NetworkObject가 필요합니다.", this);
                return false;
            }

            runner.Spawn(networkPrefab, spawnPosition, Quaternion.identity, null,
                (_, spawned) => Initialize(spawned.gameObject, raid, spawnPosition));
            return true;
        }

        private void Initialize(GameObject instance, PendingThiefRaid raid, Vector3 home)
        {
            ThiefActor actor = instance != null ? instance.GetComponent<ThiefActor>() : null;
            if (actor == null)
            {
                Debug.LogError($"{name}: 도둑 프리팹에 ThiefActor가 필요합니다.", instance);
                return;
            }
            actor.Initialize(_raidSite, raid.PreferredKind, home);
            if (raid.AssignedHouse != null) _activeRaidByHouse[raid.AssignedHouse] = actor;
        }

        private bool HasActiveRaid(DeliveryHouse house)
        {
            return house != null && _activeRaidByHouse.TryGetValue(house, out ThiefActor actor) && actor != null;
        }

        private bool TrySampleSpawnPoint(PendingThiefRaid raid, out Vector3 position)
        {
            DeliveryHouse house = raid.AssignedHouse != null
                ? raid.AssignedHouse : ResolveHouse(raid.HouseIndex);
            if (house == null)
            {
                if (raid.HouseIndex < 0) return _raidSite.TrySampleSpawnPoint(_random, out position);
                position = default;
                return false;
            }

            float min = Mathf.Max(0f, Mathf.Min(_houseSpawnRadiusMRange.x, _houseSpawnRadiusMRange.y));
            float max = Mathf.Max(min, Mathf.Max(_houseSpawnRadiusMRange.x, _houseSpawnRadiusMRange.y));
            Vector3 center = house.DoorPosition;
            for (int attempt = 0; attempt < Mathf.Max(1, _houseSpawnSampleAttempts); attempt++)
            {
                double angle = _random.NextDouble() * Math.PI * 2.0;
                float uniform = (float)_random.NextDouble();
                float ratio = _houseSpawnRadiusDistribution01 != null &&
                              _houseSpawnRadiusDistribution01.length > 0
                    ? Mathf.Clamp01(_houseSpawnRadiusDistribution01.Evaluate(uniform))
                    : Mathf.Sqrt(uniform);
                float radius = Mathf.Lerp(min, max, ratio);
                Vector3 candidate = center + new Vector3(
                    (float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                        _houseSpawnNavMeshRadiusM, NavMesh.AllAreas)) continue;
                position = hit.position;
                return true;
            }

            if (NavMesh.SamplePosition(center, out NavMeshHit fallback,
                    _houseSpawnNavMeshRadiusM, NavMesh.AllAreas))
            {
                position = fallback.position;
                return true;
            }

            position = default;
            return false;
        }

        private DeliveryHouse ResolveHouse(int houseIndex)
        {
            if (houseIndex < 0) return null;
            DeliveryHouse requestHouse = _requestDirector != null
                ? _requestDirector.HouseAt(houseIndex) : null;
            if (requestHouse != null) return requestHouse;
            return _giftDeliveryDirector != null && houseIndex < _giftDeliveryDirector.Houses.Count
                ? _giftDeliveryDirector.Houses[houseIndex] : null;
        }

        private bool HasAuthority()
        {
            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            return runner == null || (runner.IsRunning && runner.IsServer);
        }
    }
}
