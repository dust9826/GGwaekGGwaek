using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryDirector : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNetwork _network;
        [SerializeField] private DeliveryTruck _truckPrefab;
        [SerializeField] private SnowStage _snowStage;
        [SerializeField] private DeliveryTrafficController _trafficController;

        [Header("의뢰")]
        [SerializeField, Min(0.1f)] private float _requestIntervalSeconds = 15f;
        [SerializeField, Min(0.1f)] private float _snowCancelSeconds = 5f;
        [SerializeField, Min(0f)] private float _pointsPerMeter = 10f;
        [Tooltip("의뢰 출발·도착지를 고르는 난수 시드. 0이면 매 Play 마다 다른 시드로 무작위 뽑는다. " +
                 "0이 아닌 값을 넣으면 그 판을 그대로 재현한다 — 버그를 재현할 때만 쓴다.")]
        [SerializeField] private int _randomSeed;

        [Tooltip("자동 생성(주기 스폰)이 동시에 띄울 수 있는 트럭 수. CreateRequest 를 직접 부르는 " +
                 "호출(테스트 등)은 이 제한을 받지 않는다. SetMaxConcurrentTrucks 로 런타임에도 바꿀 수 있다.")]
        [SerializeField, Min(0)] private int _maxConcurrentTrucks = 1;

        [Tooltip("이 간격마다 동시 트럭 상한이 1씩 늘어난다 — 게임 시작 시 상한 1로 시작해서, " +
                 "이 시간이 지날 때마다 2, 3, 4... 로 늘어난다.")]
        [SerializeField, Min(0.1f)] private float _maxConcurrentTrucksRampSeconds = 30f;

        private readonly List<DeliveryRequest> _requests = new List<DeliveryRequest>();
        private System.Random _random;
        private float _countdown;
        private float _truckCapRampCountdown;
        private int _nextRequestId;

        public event Action<DeliveryRequest> RequestCreated;
        public event Action<DeliveryRequest> RequestCompleted;
        public event Action<DeliveryRequest> RequestCancelled;

        public IReadOnlyList<DeliveryRequest> Requests => _requests;
        public int TotalPoints { get; private set; }
        public int CompletedCount { get; private set; }
        public int CancelledCount { get; private set; }
        public float CurrentRequestIntervalSeconds { get; private set; }
        public float SnowCancelSeconds => _snowCancelSeconds;
        public int MaxConcurrentTrucks { get; private set; }

        /// <summary>이번 판이 실제로 쓴 시드. 0을 넣으면 여기 무작위로 뽑힌 값이 들어온다 —
        /// 콘솔에 로그도 남으므로, 재현하려는 판의 값을 <c>_randomSeed</c>에 그대로 넣으면 된다.</summary>
        public int ActiveRandomSeed { get; private set; }

        /// <summary>
        /// 지금 화면에 떠 있는(=아직 완주·취소되지 않은) 트럭 수. 트럭은 완주·취소되는 순간
        /// 스스로를 파괴하므로(<see cref="DeliveryTruck.OnDestroy"/> 참조 불필요 — TickDriving 이
        /// 직접 Destroy 를 부른다) 이 값은 곧 "지금 존재하는 트럭 GameObject 수" 와 같다.
        /// </summary>
        public int ActiveTruckCount
        {
            get
            {
                int count = 0;
                for (int index = 0; index < _requests.Count; index++)
                    if (_requests[index].State == EDeliveryRequestState.Active) count++;
                return count;
            }
        }

        public void Configure(DeliveryRoadNetwork network, DeliveryTruck truckPrefab,
                              SnowStage snowStage, DeliveryTrafficController trafficController)
        {
            _network = network;
            _truckPrefab = truckPrefab;
            _snowStage = snowStage;
            _trafficController = trafficController;
        }

        private void Awake()
        {
            CurrentRequestIntervalSeconds = Mathf.Max(0.1f, _requestIntervalSeconds);
            MaxConcurrentTrucks = Mathf.Max(0, _maxConcurrentTrucks);
            _countdown = CurrentRequestIntervalSeconds;
            _truckCapRampCountdown = _maxConcurrentTrucksRampSeconds;

            // 0은 "매 판 다른 시드"라는 뜻이다. 인스펙터 기본값이 고정 상수였던 탓에 의뢰 순서가
            // 매 Play 마다 완전히 똑같았다(2026-08-16 사용자 실측) — 뽑는 로직 자체는 균등한데
            // System.Random 이 같은 시드로는 항상 같은 수열을 낸다는 사실을 놓쳤다.
            ActiveRandomSeed = _randomSeed != 0 ? _randomSeed : Guid.NewGuid().GetHashCode();
            _random = new System.Random(ActiveRandomSeed);
            Debug.Log($"DeliveryDirector: 의뢰 난수 시드 = {ActiveRandomSeed}" +
                      (_randomSeed == 0 ? " (무작위)" : " (인스펙터에 고정됨 — 판 재현용)"));

            if (_snowStage == null) _snowStage = FindAnyObjectByType<SnowStage>();
            if (_trafficController == null) _trafficController = FindAnyObjectByType<DeliveryTrafficController>();
        }

        private void Update()
        {
            _truckCapRampCountdown -= Time.deltaTime;
            if (_truckCapRampCountdown <= 0f)
            {
                MaxConcurrentTrucks++;
                _truckCapRampCountdown += _maxConcurrentTrucksRampSeconds;
            }

            if (_network == null || _network.Factories.Count < 2) return;

            _countdown -= Time.deltaTime;
            if (_countdown > 0f) return;

            // 자리가 없으면 카운트다운을 다시 채우지 않고 그냥 기다린다 — 트럭이 완주·취소돼
            // 자리가 나는 바로 다음 프레임에 곧장 새 의뢰가 뜬다. 인터벌은 "최소 간격"이지
            // "고정 주기"가 아니다.
            if (ActiveTruckCount >= MaxConcurrentTrucks) return;

            GenerateRandomRequest();
            _countdown += CurrentRequestIntervalSeconds;
            if (_countdown <= 0f) _countdown = CurrentRequestIntervalSeconds;
        }

        /// <summary>동시 트럭 상한을 런타임에 바꾼다. 자동 생성에만 적용되고, 이미 떠 있는 트럭은
        /// 안 건드린다 — 낮춰도 초과분이 강제로 사라지지 않고, 다음 완주·취소 때부터 안 채워진다.</summary>
        public void SetMaxConcurrentTrucks(int count)
        {
            if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
            MaxConcurrentTrucks = count;
        }

        public void SetRequestInterval(float seconds)
        {
            if (seconds <= 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            CurrentRequestIntervalSeconds = seconds;
            _countdown = Mathf.Min(_countdown, seconds);
        }

        public void SetSnowCancelSeconds(float seconds)
        {
            if (seconds <= 0f) throw new ArgumentOutOfRangeException(nameof(seconds));
            _snowCancelSeconds = seconds;
        }

        public DeliveryRequest CreateRequest(IReadOnlyList<DeliveryFactory> stops)
        {
            if (_network == null) throw new InvalidOperationException("배송 도로 네트워크가 없다");
            if (!DeliveryRoutePlanner.TryPlanStops(_network, stops, out DeliveryRoute route))
                throw new InvalidOperationException("방문 공장을 잇는 배송 경로를 만들 수 없다");

            var request = new DeliveryRequest(_nextRequestId++, stops, route, _pointsPerMeter);
            _requests.Add(request);
            SpawnTruck(request);
            RequestCreated?.Invoke(request);
            return request;
        }

        public void NotifyCompleted(DeliveryRequest request)
        {
            if (request == null || !request.Complete()) return;
            TotalPoints += request.SuccessPoints;
            CompletedCount++;
            RequestCompleted?.Invoke(request);
        }

        public void NotifyCancelled(DeliveryRequest request)
        {
            if (request == null || request.State != EDeliveryRequestState.Cancelled) return;
            CancelledCount++;
            RequestCancelled?.Invoke(request);
        }

        private void GenerateRandomRequest()
        {
            int count = _network.Factories.Count;
            int startIndex = _random.Next(count);
            int endIndex = _random.Next(count - 1);
            if (endIndex >= startIndex) endIndex++;
            CreateRequest(new[] { _network.Factories[startIndex], _network.Factories[endIndex] });
        }

        private void SpawnTruck(DeliveryRequest request)
        {
            if (_truckPrefab == null) return;
            DeliveryRoutePose startPose = request.Route.Evaluate(0f);
            DeliveryTruck truck = Instantiate(_truckPrefab, startPose.Position,
                Quaternion.LookRotation(startPose.Forward, Vector3.up));
            truck.name = $"DeliveryTruck_{request.Id}";
            truck.Initialize(request, this, _trafficController, _snowStage);
        }
    }
}
