using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;

namespace PPack
{
    /// <summary>새 RequestDirector 모델을 기존 주문표 ViewModel로 바꾸는 Presenter/Adapter.</summary>
    [RequireComponent(typeof(StageHUDController))]
    public sealed class RequestHudPresenter : MonoBehaviour
    {
        [SerializeField] private StageHUDController _hud;
        [SerializeField] private RequestDirector _director;
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private GiftBoxCatalog _catalog;
        [SerializeField] private Transform _player;

        [Header("Feedback Hooks")]
        [SerializeField] private UnityEvent _requestStarted = new UnityEvent();
        [SerializeField] private UnityEvent _requestCompleted = new UnityEvent();
        [SerializeField] private UnityEvent _requestExpired = new UnityEvent();
        [SerializeField] private UnityEvent _timeGranted = new UnityEvent();

        private readonly List<StageHudOrderView> _orders = new List<StageHudOrderView>();

        /// <summary>체력을 읽어 오는 로컬 펭귄. <b>싱글도 멀티도 같은 곳에서 읽는다</b> —
        /// 권위 피어는 <c>Step</c> 이, 비권위 피어는 <c>PenguinNetAvatar.Render</c> 의
        /// <c>ApplyPresentation</c> 이 같은 필드를 채워 두기 때문이다.</summary>
        private PenguinLocomotion _playerLocomotion;

        private Transform _staminaSource;

        /// <summary>멀티에서 읽는 곳. 허브가 스폰될 때 자기를 물린다.</summary>
        private MissionNetHub _mission;

        /// <summary>마지막으로 연출한 시간 획득 티켓. -1 은 아직 아무것도 안 봤다는 뜻이라,
        /// 붙자마자 이전 획득을 소급해서 재생하지 않는다.</summary>
        private int _seenTimeGrantTicket = -1;

        /// <summary>
        /// 멀티의 읽기 원본을 물린다. <b>호스트에서도 허브를 읽는다</b> — 자기가 그 틱에 쓴 값을
        /// 그대로 읽는 것이라 값은 같고, 화면 코드가 호스트용·클라이언트용 두 벌로 갈라지지 않는다.
        /// 갈라지는 것은 <b>시스템</b>이다(발행·판정·정산은 서버만).
        /// </summary>
        public void BindMission(MissionNetHub mission) => _mission = mission;

        /// <summary>로컬 아바타를 물린다. 화살표 기준점이자 "들고 있는 상자" 판정의 주체다 —
        /// 멀티에서는 씬이 아니라 스폰된 아바타가 자기를 넣는다.</summary>
        public void BindLocalPlayer(Transform player) => _player = player;

        public void Configure(StageHUDController hud, RequestDirector director, GameManager gameManager,
                              GiftBoxCatalog catalog, Transform player)
        {
            Unsubscribe();
            _hud = hud;
            _director = director;
            _gameManager = gameManager;
            _catalog = catalog;
            _player = player;
            Subscribe();
        }

        private void PushStamina()
        {
            if (_player == null) return;

            if (_staminaSource != _player)
            {
                _staminaSource = _player;
                _playerLocomotion = _player.GetComponentInParent<PenguinLocomotion>();
            }

            if (_playerLocomotion == null) return;
            _hud.SetStamina01(_playerLocomotion.Stamina01, _playerLocomotion.StaminaExhausted);
        }

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

        private void Update()
        {
            if (_hud == null) return;

            if (_mission != null)
            {
                UpdateFromMission();
                return;
            }

            if (_director == null || _gameManager == null)
            {
                _hud.SetVisible(false);
                return;
            }

            bool playing = _gameManager.Phase == EGamePhase.Playing;
            _hud.SetVisible(playing);
            if (!playing) return;

            _hud.SetRemainingSeconds(_gameManager.RemainingSeconds);
            _hud.SetScore(_gameManager.Score);
            PushStamina();

            EGiftBoxKind? heldKind = ResolveHeldKind();
            _orders.Clear();
            IReadOnlyList<GiftRequest> requests = _director.ActiveRequests;
            for (int index = 0; index < requests.Count; index++)
            {
                GiftRequest request = requests[index];
                DeliveryHouse house = _director.HouseAt(request.HouseIndex);
                Vector3 target = house != null ? house.DoorPosition : _player != null
                    ? _player.position + _player.forward * request.DistanceM
                    : Vector3.forward * request.DistanceM;
                Color color = _catalog != null
                    ? _catalog.ColorOf(request.WantedKind)
                    : Gift.ColorForKind(request.WantedKind);
                _orders.Add(StageHudOrderView.FromWorldTarget(
                    request.Id, color, request.RemainingSeconds, _player, target, request.DistanceM,
                    heldKind.HasValue && heldKind.Value == request.WantedKind));
            }

            _hud.SetOrders(_orders);
        }

        /// <summary>복제된 의뢰로 같은 주문표를 그린다. 집·문 위치는 씬에서 읽으므로 선을 타지 않는다.</summary>
        private void UpdateFromMission()
        {
            bool playing = _mission.Phase == EGamePhase.Playing;
            _hud.SetVisible(playing);
            if (!playing) return;

            _hud.SetRemainingSeconds(_mission.RemainingSeconds);
            _hud.SetScore(_mission.Score);
            PushStamina();
            TickReplicatedTimeGain();

            EGiftBoxKind? heldKind = ResolveHeldKind();
            _orders.Clear();
            for (int index = 0; index < _mission.RequestCount; index++)
            {
                NetMissionRequest request = _mission.RequestAt(index);
                var kind = (EGiftBoxKind)request.Kind;
                DeliveryHouse house = _mission.HouseAt(request.HouseIndex);
                Vector3 target = house != null ? house.DoorPosition : _player != null
                    ? _player.position + _player.forward * request.DistanceM
                    : Vector3.forward * request.DistanceM;
                Color color = _catalog != null ? _catalog.ColorOf(kind) : Gift.ColorForKind(kind);
                _orders.Add(StageHudOrderView.FromWorldTarget(
                    request.Id, color, request.RemainingSeconds, _player, target, request.DistanceM,
                    heldKind.HasValue && heldKind.Value == kind));
            }

            _hud.SetOrders(_orders);
        }

        private EGiftBoxKind? ResolveHeldKind()
        {
            PenguinCarry carry = _player != null ? _player.GetComponentInChildren<PenguinCarry>() : null;
            if (carry == null || carry.Cargo == null) return null;

            Gift gift = carry.Cargo as Gift;
            if (gift == null) gift = carry.Cargo.GetComponentInParent<Gift>();
            if (gift == null) gift = carry.Cargo.GetComponentInChildren<Gift>();
            return gift != null ? gift.Kind : (EGiftBoxKind?)null;
        }

        private void Subscribe()
        {
            if (_gameManager != null)
            {
                _gameManager.TimeGranted -= OnTimeGranted;
                _gameManager.TimeGranted += OnTimeGranted;
            }

            if (_director == null) return;
            _director.RequestStarted -= OnRequestStarted;
            _director.RequestCompleted -= OnRequestCompleted;
            _director.RequestExpired -= OnRequestExpired;
            _director.RequestStarted += OnRequestStarted;
            _director.RequestCompleted += OnRequestCompleted;
            _director.RequestExpired += OnRequestExpired;
        }

        private void Unsubscribe()
        {
            if (_gameManager != null) _gameManager.TimeGranted -= OnTimeGranted;

            if (_director == null) return;
            _director.RequestStarted -= OnRequestStarted;
            _director.RequestCompleted -= OnRequestCompleted;
            _director.RequestExpired -= OnRequestExpired;
        }

        private void OnRequestStarted(GiftRequest _) => _requestStarted.Invoke();
        private void OnRequestCompleted(GiftRequest _) => _requestCompleted.Invoke();
        private void OnRequestExpired(GiftRequest _)
        {
            if (_hud != null) _hud.PlayOrderExpired();
            _requestExpired.Invoke();
        }

        private void OnTimeGranted(float seconds)
        {
            if (_hud != null) _hud.ShowTimeGain(seconds);
            _timeGranted.Invoke();
        }

        /// <summary>클라이언트에는 시간 획득 이벤트가 없다 — 시스템이 서버에만 있기 때문이다.
        /// 서버가 붙인 티켓 번호가 바뀐 것을 보고 같은 연출을 한 번 재생한다.</summary>
        private void TickReplicatedTimeGain()
        {
            int ticket = _mission.TimeGrantTicket;
            if (ticket == _seenTimeGrantTicket) return;

            bool first = _seenTimeGrantTicket < 0;
            _seenTimeGrantTicket = ticket;
            if (first) return;

            if (_hud != null) _hud.ShowTimeGain(_mission.LastTimeGrantSeconds);
            _timeGranted.Invoke();
        }
    }
}
