using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>새 의뢰 흐름을 집 신호로 옮기는 어댑터. <see cref="RequestDirector"/>는 표시가
    /// 있는지 모르고, 표시는 <see cref="RequestDirector"/>가 있는지 모른다.</summary>
    [DisallowMultipleComponent]
    public sealed class RequestHouseSignalPresenter : MonoBehaviour
    {
        [SerializeField] private RequestDirector _requestDirector;
        [SerializeField] private StageHouseSignals _signals;
        [SerializeField] private GameManager _gameManager;

        /// <summary>멀티에서 읽는 곳. 허브가 스폰될 때 자기를 물린다.</summary>
        private MissionNetHub _mission;

        /// <summary>지금 열려 있는 집. 복제 목록에는 "새로 떴다"가 없어 직접 비교한다.</summary>
        private readonly HashSet<int> _openHouses = new HashSet<int>();

        private int _seenClosedTicket = -1;
        private bool _publishedHouses;

        /// <summary>
        /// 멀티의 읽기 원본을 물린다. <b>클라이언트에는 디렉터 이벤트가 오지 않는다</b> — 시스템이
        /// 서버에만 있기 때문이다. 그래서 복제된 의뢰 목록을 매 프레임 견주어 열고, 닫는 이유
        /// (완료냐 만료냐)는 판정한 서버가 티켓으로 알려 준 것을 쓴다.
        /// </summary>
        public void BindMission(MissionNetHub mission)
        {
            _mission = mission;
            _openHouses.Clear();
            _seenClosedTicket = -1;
            _publishedHouses = false;
        }

        public void Configure(RequestDirector requestDirector, StageHouseSignals signals,
                              GameManager gameManager)
        {
            Unsubscribe();
            _requestDirector = requestDirector;
            _signals = signals;
            _gameManager = gameManager;
            Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void Update()
        {
            if (_mission == null || _signals == null) return;

            if (!_publishedHouses)
            {
                PublishHousesFromMission();
                _publishedHouses = true;
            }

            SyncClosedFromMission();
            SyncOpenFromMission();

            if (_mission.Phase != EGamePhase.Ended || _openHouses.Count == 0) return;
            _signals.CloseAll();
            _openHouses.Clear();
        }

        /// <summary>집 목록은 씬에서 읽는다 — 모든 피어가 같은 씬을 열므로 선을 타지 않는다.</summary>
        private void PublishHousesFromMission()
        {
            if (_requestDirector == null) return;
            PublishHouses();
        }

        private void SyncClosedFromMission()
        {
            int ticket = _mission.ClosedTicket;
            if (ticket == _seenClosedTicket) return;

            bool first = _seenClosedTicket < 0;
            _seenClosedTicket = ticket;
            if (first) return;

            int houseIndex = _mission.ClosedHouseIndex;
            _signals.Close(houseIndex, _mission.ClosedCompleted);
            _openHouses.Remove(houseIndex);
        }

        private void SyncOpenFromMission()
        {
            for (int index = 0; index < _mission.RequestCount; index++)
            {
                NetMissionRequest request = _mission.RequestAt(index);
                if (!_openHouses.Add(request.HouseIndex)) continue;
                _signals.Open(request.HouseIndex, Gift.ColorForKind((EGiftBoxKind)request.Kind));
            }
        }

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_gameManager != null)
            {
                _gameManager.GameEnded -= HandleGameEnded;
                _gameManager.GameEnded += HandleGameEnded;
            }

            if (_requestDirector == null || _signals == null) return;

            _requestDirector.RequestStarted -= HandleStarted;
            _requestDirector.RequestCompleted -= HandleCompleted;
            _requestDirector.RequestExpired -= HandleExpired;
            _requestDirector.RequestStarted += HandleStarted;
            _requestDirector.RequestCompleted += HandleCompleted;
            _requestDirector.RequestExpired += HandleExpired;

            PublishHouses();

            // 늦게 켜져도 이미 떠 있는 의뢰의 집을 복원한다.
            IReadOnlyList<GiftRequest> active = _requestDirector.ActiveRequests;
            for (int index = 0; index < active.Count; index++) HandleStarted(active[index]);
        }

        private void Unsubscribe()
        {
            if (_gameManager != null) _gameManager.GameEnded -= HandleGameEnded;
            if (_requestDirector == null) return;
            _requestDirector.RequestStarted -= HandleStarted;
            _requestDirector.RequestCompleted -= HandleCompleted;
            _requestDirector.RequestExpired -= HandleExpired;
        }

        private void PublishHouses()
        {
            var houses = new List<DeliveryHouse>(_requestDirector.HouseCount);
            for (int index = 0; index < _requestDirector.HouseCount; index++)
                houses.Add(_requestDirector.HouseAt(index));
            _signals.SetHouses(houses);
        }

        // 의뢰는 색이 아니라 선물 종류를 들고 있다. 색은 상자와 같은 표에서 뽑아야
        // 지붕·말풍선과 손에 든 선물이 어긋나지 않는다.
        private void HandleStarted(GiftRequest request)
        {
            if (request == null || _signals == null) return;
            _signals.Open(request.HouseIndex, Gift.ColorForKind(request.WantedKind));
        }

        private void HandleCompleted(GiftRequest request) => Close(request, true);

        private void HandleExpired(GiftRequest request) => Close(request, false);

        private void Close(GiftRequest request, bool completed)
        {
            if (request == null || _signals == null) return;
            _signals.Close(request.HouseIndex, completed);
        }

        /// <summary>게임이 끝나면 남아 있던 의뢰는 완료도 만료도 되지 않는다. 그대로 두면
        /// HELP 말풍선과 지붕색이 결과 화면 뒤에 계속 떠 있는다.</summary>
        private void HandleGameEnded()
        {
            if (_signals != null) _signals.CloseAll();
        }
    }
}
