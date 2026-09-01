using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public enum EGiftDeliveryPhase
    {
        Idle,
        Running,
        GameOver
    }

    public sealed class GiftDeliveryDirector : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNetwork _network;
        [SerializeField] private DeliveryHouse[] _houses = Array.Empty<DeliveryHouse>();
        [SerializeField] private Transform[] _participants = Array.Empty<Transform>();
        [SerializeField] private GiftDeliveryDifficultySettings _difficulty = GiftDeliveryDifficultySettings.Default;
        [SerializeField] private int _ordersPerParticipant = 1;
        [SerializeField] private float _orderStaggerSeconds = 3f;
        [SerializeField, Min(0f)] private float _announcementSeconds = 1.4f;
        [SerializeField] private int _recentHouseExclusion = 2;
        [SerializeField] private int _randomSeed;
        [SerializeField] private bool _wrongHouseFails;

        private readonly List<GiftDeliveryOrder> _activeOrders = new List<GiftDeliveryOrder>();
        private readonly List<int> _recentHouseIndices = new List<int>();
        private readonly List<float> _pendingSpawnTimers = new List<float>();
        private readonly List<PendingAnnouncement> _pendingAnnouncements = new List<PendingAnnouncement>();
        private System.Random _random;
        private int _nextOrderId;
        private int _completedCount;
        private int _totalScore;

        public EGiftDeliveryPhase Phase { get; private set; } = EGiftDeliveryPhase.Idle;
        public IReadOnlyList<GiftDeliveryOrder> ActiveOrders => _activeOrders;
        public IReadOnlyList<DeliveryHouse> Houses => _houses;
        public IReadOnlyList<Transform> Participants => _participants;
        public int CompletedCount => _completedCount;
        public int TotalScore => _totalScore;
        public int PendingAnnouncementCount => _pendingAnnouncements.Count;

        public int MaxConcurrentOrders =>
            Mathf.Clamp(_participants.Length * Mathf.Max(_ordersPerParticipant, 1), 1, Mathf.Max(_houses.Length, 1));

        public event Action<GiftDeliveryOrder> OrderAnnounced;
        public event Action<GiftDeliveryOrder> OrderStarted;
        public event Action<GiftDeliveryOrder> OrderCompleted;
        public event Action<GiftDeliveryOrder, EGiftDeliveryFailReason> OrderFailed;
        public event Action GameOver;

        public void Configure(DeliveryRoadNetwork network, IReadOnlyList<DeliveryHouse> houses)
        {
            _network = network;
            _houses = new DeliveryHouse[houses.Count];
            for (int index = 0; index < houses.Count; index++) _houses[index] = houses[index];
        }

        public void SetParticipants(IReadOnlyList<Transform> participants)
        {
            _participants = new Transform[participants.Count];
            for (int index = 0; index < participants.Count; index++) _participants[index] = participants[index];
        }

        public void SetOrdersPerParticipant(int count)
        {
            _ordersPerParticipant = Mathf.Max(1, count);
        }

        public void Begin()
        {
            if (Phase != EGiftDeliveryPhase.Idle) return;
            int seed = _randomSeed != 0 ? _randomSeed : Guid.NewGuid().GetHashCode();
            _random = new System.Random(seed);
            Phase = EGiftDeliveryPhase.Running;
            _pendingSpawnTimers.Clear();
            _pendingAnnouncements.Clear();
            int slots = MaxConcurrentOrders;
            for (int index = 0; index < slots; index++) _pendingSpawnTimers.Add(0f);
        }

        /// <summary>
        /// Play 진입 시 자동으로 루프를 시작한다. 씬 빌더가 에디트 모드에서 <see cref="Configure"/>·
        /// <see cref="SetParticipants"/> 로 채운 직렬화 필드는 도메인 리로드를 넘어 남지만, <see cref="Phase"/>
        /// 는 직렬화되지 않는 런타임 값이라 매 Play 마다 <see cref="EGiftDeliveryPhase.Idle"/> 로 되돌아간다
        /// — 그래서 별도 시작 트리거 없이 여기서 <see cref="Begin"/> 을 부른다. 이미 <see cref="Begin"/> 을
        /// 직접 부른 뒤라면(테스트 등) <see cref="Phase"/> 가 더 이상 Idle 이 아니므로 아무 일도 안 한다.
        /// </summary>
        private void Start()
        {
            Begin();
        }

        private void FixedUpdate()
        {
            if (Phase != EGiftDeliveryPhase.Running) return;

            TickAnnouncements(Time.fixedDeltaTime);
            TickSpawns(Time.fixedDeltaTime);

            for (int index = 0; index < _houses.Length; index++)
            {
                DeliveryHouse house = _houses[index];
                if (house == null || house.Zone == null) continue;
                GiftDeliveryOrder order = FindActiveOrderForHouse(index);
                house.Zone.Evaluate(order?.GiftKind, out int acceptedCount, out int acceptedValue);
                EvaluateHouse(index, acceptedCount, acceptedValue);
            }

            for (int index = _activeOrders.Count - 1; index >= 0; index--)
            {
                GiftDeliveryOrder order = _activeOrders[index];
                order.Tick(Time.fixedDeltaTime);
                if (order.RemainingSeconds <= 0f && order.State == EGiftDeliveryOrderState.Active)
                {
                    order.Fail(EGiftDeliveryFailReason.TimeExpired);
                    OrderFailed?.Invoke(order, EGiftDeliveryFailReason.TimeExpired);
                    RaiseGameOver();
                    return;
                }
            }
        }

        private void EvaluateHouse(int houseIndex, int acceptedCount, int acceptedValue)
        {
            GiftDeliveryOrder targetingOrder = FindActiveOrderForHouse(houseIndex);

            if (targetingOrder != null)
            {
                if (targetingOrder.TryComplete(acceptedCount, acceptedValue))
                {
                    _activeOrders.Remove(targetingOrder);
                    _completedCount++;
                    _totalScore += targetingOrder.RequiredGiftCount;
                    _recentHouseIndices.Add(houseIndex);
                    while (_recentHouseIndices.Count > _recentHouseExclusion) _recentHouseIndices.RemoveAt(0);
                    _pendingSpawnTimers.Add(_orderStaggerSeconds);
                    OrderCompleted?.Invoke(targetingOrder);
                }
                return;
            }

            if (_wrongHouseFails && acceptedCount > 0)
            {
                var failed = new GiftDeliveryOrder(-1, houseIndex, 0f, 0, 0, 0f);
                failed.Fail(EGiftDeliveryFailReason.WrongHouse);
                OrderFailed?.Invoke(failed, EGiftDeliveryFailReason.WrongHouse);
                RaiseGameOver();
            }
        }

        private void TickSpawns(float delta)
        {
            for (int index = _pendingSpawnTimers.Count - 1; index >= 0; index--)
            {
                float remaining = _pendingSpawnTimers[index] - delta;
                if (remaining > 0f)
                {
                    _pendingSpawnTimers[index] = remaining;
                    continue;
                }

                if (_activeOrders.Count + _pendingAnnouncements.Count >= MaxConcurrentOrders)
                {
                    _pendingSpawnTimers[index] = 0f;
                    continue;
                }

                if (TryAnnounceOrder()) _pendingSpawnTimers.RemoveAt(index);
                else _pendingSpawnTimers[index] = 0f;
            }
        }

        private bool TryAnnounceOrder()
        {
            if (_network == null || _houses.Length == 0 || _participants.Length == 0) return false;

            var startNodes = new List<DeliveryRoadNode>(_participants.Length);
            for (int index = 0; index < _participants.Length; index++)
            {
                Transform participant = _participants[index];
                if (participant == null) continue;
                DeliveryRoadNode node = _network.FindNearestNode(participant.position);
                if (node != null) startNodes.Add(node);
            }
            if (startNodes.Count == 0) return false;

            var hardExcluded = new List<int>(_activeOrders.Count + _pendingAnnouncements.Count);
            foreach (GiftDeliveryOrder activeOrder in _activeOrders) hardExcluded.Add(activeOrder.HouseIndex);
            foreach (PendingAnnouncement announcement in _pendingAnnouncements)
                hardExcluded.Add(announcement.Order.HouseIndex);

            var preferredExcluded = new List<int>(_recentHouseIndices.Count + hardExcluded.Count);
            preferredExcluded.AddRange(hardExcluded);
            preferredExcluded.AddRange(_recentHouseIndices);

            GiftDeliveryTarget target = GiftDeliveryDifficulty.Evaluate(_completedCount, _difficulty);

            bool selected = GiftDeliveryHouseSelector.TrySelectRandom(
                _network, _houses, startNodes, preferredExcluded, _random,
                out int houseIndex, out float routeLength, out _);
            if (!selected)
            {
                selected = GiftDeliveryHouseSelector.TrySelectRandom(
                    _network, _houses, startNodes, hardExcluded, _random,
                    out houseIndex, out routeLength, out _);
            }
            if (!selected) return false;

            var order = new GiftDeliveryOrder(_nextOrderId++, houseIndex, routeLength,
                                              target.RequiredGiftCount, target.RequiredTotalValue, target.TimeLimitSeconds);
            if (_announcementSeconds <= 0f)
            {
                StartOrder(order);
                return true;
            }

            _pendingAnnouncements.Add(new PendingAnnouncement(order, _announcementSeconds));
            OrderAnnounced?.Invoke(order);
            return true;
        }

        private void TickAnnouncements(float delta)
        {
            for (int index = _pendingAnnouncements.Count - 1; index >= 0; index--)
            {
                PendingAnnouncement announcement = _pendingAnnouncements[index];
                announcement.RemainingSeconds -= delta;
                if (announcement.RemainingSeconds > 0f) continue;

                _pendingAnnouncements.RemoveAt(index);
                StartOrder(announcement.Order);
            }
        }

        private void StartOrder(GiftDeliveryOrder order)
        {
            _activeOrders.Add(order);
            OrderStarted?.Invoke(order);
        }

        private GiftDeliveryOrder FindActiveOrderForHouse(int houseIndex)
        {
            for (int index = 0; index < _activeOrders.Count; index++)
                if (_activeOrders[index].HouseIndex == houseIndex) return _activeOrders[index];
            return null;
        }

        private void RaiseGameOver()
        {
            Phase = EGiftDeliveryPhase.GameOver;
            _pendingAnnouncements.Clear();
            _pendingSpawnTimers.Clear();
            GameOver?.Invoke();
        }

        private sealed class PendingAnnouncement
        {
            public PendingAnnouncement(GiftDeliveryOrder order, float remainingSeconds)
            {
                Order = order;
                RemainingSeconds = remainingSeconds;
            }

            public GiftDeliveryOrder Order { get; }
            public float RemainingSeconds { get; set; }
        }
    }
}
