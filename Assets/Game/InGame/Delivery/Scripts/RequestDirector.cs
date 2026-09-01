using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>의뢰 시스템의 권위 허브. 랜덤 간격으로 랜덤 집에 "이 종류 상자를 원한다"는 의뢰를
    /// 발행하고, TTL 만료와 집 앞 수령(일치 종류) 판정을 소유한다.
    ///
    /// <para><b>게임을 끝내지 않는다.</b> 개별 의뢰가 만료돼도 <see cref="RequestExpired"/>만 쏘고
    /// 사라진다 — 게임 종료는 오직 <see cref="GameManager"/>의 전역 시간이다. 멀티 대비로 의뢰는
    /// <see cref="GiftRequest.HouseIndex"/>(int)만 들고, <c>NetworkBehaviour</c>는 아직 안 쓴다.</para>
    ///
    /// <para>거리(기지→집)는 도로망이 있으면 <see cref="DeliveryRoutePlanner"/>로, 없으면 직선으로
    /// 잰다 — 테스트가 전체 도로 그래프 없이도 돌 수 있게 한 폴백이다. 밸런스는
    /// <see cref="StageBalanceConfig"/>를 매 틱 읽어 실시간 반영한다.</para></summary>
    public sealed class RequestDirector : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNetwork _network;
        [SerializeField] private Transform _base;
        [SerializeField] private DeliveryRoadNode _baseNode;
        [SerializeField] private DeliveryHouse[] _houses = Array.Empty<DeliveryHouse>();
        [SerializeField] private StageBalanceConfig _config;
        [SerializeField] private GiftBoxCatalog _catalog;
        [SerializeField] private int _randomSeed;

        [Tooltip("판이 얻은 증강. 비어 있으면 효과가 없고 기존 동작 그대로다.")]
        [SerializeField] private AugmentLoadout _augments;

        [Tooltip("증강 쉬는 시간. 열려 있는 동안 새 의뢰가 안 나오고 기존 의뢰의 제한시간도 멈춘다. " +
                 "비어 있으면 게이트가 없는 것과 같다 — 증강을 안 놓은 씬과 테스트가 영향받지 않는다.")]
        [SerializeField] private AugmentSelectionDirector _intermission;

        private static readonly EGiftBoxKind[] AllKinds = (EGiftBoxKind[])Enum.GetValues(typeof(EGiftBoxKind));

        private readonly List<GiftRequest> _active = new List<GiftRequest>();
        private readonly List<int> _freeHouses = new List<int>();
        private System.Random _random;
        private float _spawnTimer;
        // 한 배치는 같은 프레임에 다 터지지 않고 하나씩 남는다. 동시에 뜨면 주문서도 SFX도 뭉친다.
        private int _pendingBurst;
        private float _burstGapTimer;
        private float _elapsed;
        private int _nextId;
        private int _completedCount;
        private int _expiredCount;
        private bool _running;

        public IReadOnlyList<GiftRequest> ActiveRequests => _active;
        public bool IsRunning => _running;
        public float ElapsedSeconds => _elapsed;
        public int HouseCount => _houses.Length;
        public int CompletedCount => _completedCount;
        public int ExpiredCount => _expiredCount;
        public DeliveryHouse HouseAt(int index) => index >= 0 && index < _houses.Length ? _houses[index] : null;

        public event Action<GiftRequest> RequestStarted;
        public event Action<GiftRequest> RequestCompleted;
        public event Action<GiftRequest> RequestExpired;

        public void Configure(DeliveryRoadNetwork network, Transform baseTransform, DeliveryRoadNode baseNode,
                              IReadOnlyList<DeliveryHouse> houses, StageBalanceConfig config, GiftBoxCatalog catalog)
        {
            _network = network;
            _base = baseTransform;
            _baseNode = baseNode;
            _houses = new DeliveryHouse[houses.Count];
            for (int index = 0; index < houses.Count; index++) _houses[index] = houses[index];
            _config = config;
            _catalog = catalog;
        }

        public void Begin()
        {
            _running = true;
            _elapsed = 0f;
            _completedCount = 0;
            _expiredCount = 0;
            _random = _randomSeed != 0 ? new System.Random(_randomSeed) : new System.Random();
            _pendingBurst = 0;
            _burstGapTimer = 0f;
            // 첫 배치는 정규 간격이 아니라 짧은 전용 대기를 쓴다. 즉시 터지면 시작이 정신없고,
            // 한 간격(수십 초)을 통째로 기다리면 아무 일도 안 일어나는 시간이 생긴다.
            _spawnTimer = _config != null ? Mathf.Max(_config.FirstSpawnDelaySeconds, 0f) : 5f;
        }

        /// <summary>쉬는 시간 디렉터를 꽂는다. 씬에서는 빌더가 채우고, 테스트는 이것을 쓴다.</summary>
        public void SetIntermission(AugmentSelectionDirector intermission) =>
            _intermission = intermission;

        /// <summary>지금 쉬는 시간인가. 참조가 비어 있으면 언제나 거짓이다.</summary>
        private bool IsIntermission => _intermission != null && _intermission.IsOpen;

        /// <summary>
        /// <b>쉬는 시간에는 새 의뢰가 안 나오고 제한시간도 안 준다. 완료는 계속 받는다</b> —
        /// 쉬는 중에 배달을 마치는 것을 막을 이유가 없다
        /// (<c>docs/specs/2026-09-01-augment-vote-and-intermission.md</c> §3).
        ///
        /// <para><c>_elapsed</c> 는 계속 는다. 그것은 판 전체의 경과 시간이고 쉬는 시간도 판의 일부다.</para>
        ///
        /// <para>⚠ <b>클라이언트에서는 이 게이트가 돌지 않는다.</b> <c>MissionNetHub.Spawned()</c> 가
        /// 클라에서 이 컴포넌트를 통째로 끈다 — 쉬는 시간은 서버·싱글의 문제다.</para>
        /// </summary>
        private void FixedUpdate()
        {
            if (!_running) return;
            float delta = Time.fixedDeltaTime;
            _elapsed += delta;

            if (!IsIntermission)
            {
                TickSpawns(delta);
                TickRequests(delta);
            }

            TickCompletions();
        }

        /// <summary>지정한 집에 지정한 종류의 의뢰를 발행한다(결정론적, 테스트·내부 스폰 공용).
        /// 실패(집 없음·이미 의뢰 있음·스택 초과)면 null.</summary>
        public GiftRequest SpawnRequest(int houseIndex, EGiftBoxKind kind)
        {
            if (houseIndex < 0 || houseIndex >= _houses.Length || _houses[houseIndex] == null) return null;
            if (_active.Count >= MaxActiveRequests) return null;
            if (HasActiveRequestForHouse(houseIndex)) return null;

            float distance = DistanceToHouse(houseIndex);
            float kindWeight = _catalog != null ? _catalog.DifficultyWeight(kind) : 1f;
            RequestBalanceResult balance = RequestBalance.Evaluate(_config, kindWeight, distance, RollRatio(), _elapsed);

            // TTL 만 여기서 곱한다 — 타이머가 지금 시작하므로 지금 굳는 값이다. 보상과 추가시간은
            // 완료 시점에 GameManager 가 곱한다(스펙 §7). RequestBalance 는 "여기는 결정론적이다"
            // 라는 계약이 있어 건드리지 않고, 결과만 다시 만든다.
            if (_augments != null)
                balance = new RequestBalanceResult(
                    balance.Difficulty,
                    balance.Reward,
                    balance.TtlSeconds * _augments.GetMultiplier(EAugmentStat.RequestTtl),
                    balance.TimeBonusSeconds);

            var request = new GiftRequest(_nextId++, houseIndex, kind, distance, balance);
            _active.Add(request);
            RequestStarted?.Invoke(request);
            return request;
        }

        private int MaxActiveRequests => _config != null ? Mathf.Max(_config.MaxActiveRequests, 1) : int.MaxValue;

        private bool HasActiveRequestForHouse(int houseIndex)
        {
            for (int index = 0; index < _active.Count; index++)
                if (_active[index].HouseIndex == houseIndex) return true;
            return false;
        }

        private void TickSpawns(float delta)
        {
            // 배치가 남아 있으면 그것부터 하나씩 흘려보낸다. 다음 배치는 다 나온 뒤에 센다.
            if (_pendingBurst > 0)
            {
                _burstGapTimer -= delta;
                if (_burstGapTimer > 0f) return;

                if (TryPickFreeHouse(out int burstHouseIndex))
                {
                    SpawnRequest(burstHouseIndex, RollKind());
                    _pendingBurst--;
                }
                else
                {
                    // 빈 집이 없으면 남은 배치는 버린다. 집이 빌 때까지 들고 있으면
                    // 한참 뒤에 몰아서 터져 같은 문제가 다시 생긴다.
                    _pendingBurst = 0;
                }

                if (_pendingBurst > 0) _burstGapTimer = NextBurstGap();
                else _spawnTimer = NextSpawnInterval();
                return;
            }

            _spawnTimer -= delta;
            if (_spawnTimer > 0f) return;

            _pendingBurst = RollBurstSize();
            _burstGapTimer = 0f;
        }

        private float NextBurstGap()
        {
            if (_config == null || _random == null) return 1f;
            float min = Mathf.Max(_config.BurstGapSecondsMin, 0.1f);
            float max = Mathf.Max(_config.BurstGapSecondsMax, min);
            return min + (float)_random.NextDouble() * (max - min);
        }

        private void TickRequests(float delta)
        {
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                GiftRequest request = _active[index];
                request.Tick(delta);
                if (!request.IsExpired) continue;
                request.MarkExpired();
                _active.RemoveAt(index);
                _expiredCount++;
                RequestExpired?.Invoke(request);
            }
        }

        private void TickCompletions()
        {
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                GiftRequest request = _active[index];
                DeliveryHouse house = _houses[request.HouseIndex];
                if (house == null || house.Zone == null) continue;

                Gift delivered = FindMatchingGiftInZone(house.Zone, request.WantedKind);
                if (delivered == null) continue;

                request.MarkCompleted();
                _active.RemoveAt(index);
                _completedCount++;
                ConsumeDelivered(delivered); // 배달된 상자는 소비된다
                RequestCompleted?.Invoke(request);
            }
        }

        /// <summary>배달된 상자를 없앤다. 멀티의 상자는 서버가 스폰한 <c>NetworkObject</c> 라
        /// <c>Destroy</c> 로는 <b>서버 화면에서만</b> 사라진다 — 클라이언트에는 주인 없는 상자가
        /// 남는다. 없애는 것도 스폰한 쪽의 일이다.</summary>
        private static void ConsumeDelivered(Gift gift)
        {
            if (gift.TryGetComponent(out Fusion.NetworkObject networkObject) && networkObject.Runner != null)
            {
                networkObject.Runner.Despawn(networkObject);
                return;
            }

            Destroy(gift.gameObject);
        }

        private static Gift FindMatchingGiftInZone(GiftDropZone zone, EGiftBoxKind wantedKind)
        {
            IReadOnlyList<Gift> all = Gift.All;
            for (int index = 0; index < all.Count; index++)
            {
                Gift gift = all[index];
                if (gift == null || !gift.isActiveAndEnabled || gift.IsCarried) continue;
                if (gift.Kind != wantedKind) continue;
                if (!zone.Contains(gift.transform.position)) continue;
                return gift;
            }
            return null;
        }

        private bool TryPickFreeHouse(out int houseIndex)
        {
            _freeHouses.Clear();
            for (int index = 0; index < _houses.Length; index++)
                if (_houses[index] != null && !HasActiveRequestForHouse(index)) _freeHouses.Add(index);

            if (_freeHouses.Count == 0) { houseIndex = -1; return false; }
            houseIndex = _freeHouses[_random.Next(_freeHouses.Count)];
            return true;
        }

        private int RollBurstSize()
        {
            if (_config == null) return 1;
            int min = Mathf.Max(_config.BurstSize.x, 1);
            int max = Mathf.Max(_config.BurstSize.y, min);
            // 쉬운 쪽(작은 버스트) 편향: 두 번 뽑아 작은 값을 쓴다.
            int a = _random.Next(min, max + 1);
            int b = _random.Next(min, max + 1);
            return Mathf.Min(a, b);
        }

        private EGiftBoxKind RollKind()
        {
            if (_catalog != null && _catalog.Count > 0)
                return _catalog.KindAt(_random.Next(_catalog.Count));
            return AllKinds[_random.Next(AllKinds.Length)];
        }

        private float RollRatio()
        {
            if (_config == null || _random == null) return 1f;
            float min = _config.DifficultyRatioRange.x;
            float max = Mathf.Max(_config.DifficultyRatioRange.y, min);
            return min + (float)_random.NextDouble() * (max - min);
        }

        private float NextSpawnInterval()
        {
            if (_config == null || _random == null) return 30f;
            float min = Mathf.Max(_config.SpawnIntervalMin, 0.1f);
            float max = Mathf.Max(_config.SpawnIntervalMax, min);
            return min + (float)_random.NextDouble() * (max - min);
        }

        private float DistanceToHouse(int houseIndex)
        {
            DeliveryHouse house = _houses[houseIndex];
            if (house == null) return 0f;

            if (_network != null && _baseNode != null && house.RoadNode != null
                && DeliveryRoutePlanner.TryPlan(_network, _baseNode, house.RoadNode, out DeliveryRoute route))
                return route.Length;

            Vector3 from = _base != null ? _base.position : Vector3.zero;
            return Vector3.Distance(from, house.DoorPosition);
        }
    }
}
