using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>개발용 온스크린 오버레이 + 임시 배달/길안내 조작. 씬 리그 검증·밸런스 튜닝용이다.
    ///
    /// <para><b>임시 조작(실제 이동 확인용):</b> 중앙 기지에서 남은 시간이 가장 많은 의뢰를 하나
    /// 고정하고, 중앙→집→중앙의 편도·복귀·왕복 시간을 <c>[DeliveryTiming]</c> 로그로 남긴다.
    /// 화살표는 가는 동안 집을, 돌아오는 동안 기지를 가리킨다. 테스트 씬이 지정한
    /// <see cref="RequestCompletionCondition"/>을 만족하면 해당 집 판정 구역에 일치 종류 상자를 한 번
    /// 생성해 <see cref="RequestDirector"/>의 실제 완료 판정을 자동으로 태운다(펭귄이 등에 지고 들어가는
    /// 진짜 루프 대신 이동+도착만 먼저 확인하는 용도).</para>
    ///
    /// <para>프로덕션 HUD가 아니다 — 실제 UI는 <c>../UI/</c>가 소유한다. 테스트 씬 전용 dev 도구다.</para></summary>
    [DisallowMultipleComponent]
    public sealed class RequestGameDebugHud : MonoBehaviour
    {
        private enum ERoundTripTimingState
        {
            WaitingForCenter,
            TravelingToHouse,
            ReturningToCenter,
        }

        [SerializeField] private GameManager _gameManager;
        [SerializeField] private RequestDirector _requestDirector;
        [SerializeField] private Transform _base;
        [SerializeField] private bool _beginOnStart = true;
        // 화살표는 밸런싱에 계속 쓰이므로 패널만 따로 끈다. 둘 다 이 컴포넌트가 그린다.
        [SerializeField] private bool _showPanel = true;
        [SerializeField, Min(0.5f)] private float _baseRadius = 6f;

        private Transform _player;
        private int _targetRequestId = -1;
        private ERoundTripTimingState _timingState;
        private int _timingRequestId = -1;
        private int _timingHouseIndex = -1;
        private GiftRequest _timingRequest;
        private float _timingRouteDistanceM;
        private double _roundTripStartedAt;
        private double _returnStartedAt;
        // 직선거리와 흐른 시간만으로는 속도·우회·정지가 한 숫자에 뭉친다. 걸어간 거리와
        // 실제로 움직인 시간을 따로 재서 셋을 가른다.
        private const float MovingSpeedThresholdMps = 0.2f;
        private Vector3 _lastPlayerPosition;
        private bool _hasLastPlayerPosition;
        private double _legPathM;
        private double _legMovingSeconds;
        private double _outboundPathM;
        private double _outboundMovingSeconds;
        private bool _wasNearBase;
        private readonly DeliveryTimingSummary _timingSummary = new DeliveryTimingSummary();

        public void Configure(GameManager gameManager, RequestDirector requestDirector)
        {
            _gameManager = gameManager;
            _requestDirector = requestDirector;
        }

        private void OnEnable()
        {
            if (_requestDirector == null) return;
            _requestDirector.RequestCompleted -= HandleTimedRequestCompleted;
            _requestDirector.RequestExpired -= HandleTimedRequestExpired;
            _requestDirector.RequestCompleted += HandleTimedRequestCompleted;
            _requestDirector.RequestExpired += HandleTimedRequestExpired;
        }

        private void OnDisable()
        {
            if (_requestDirector == null) return;
            _requestDirector.RequestCompleted -= HandleTimedRequestCompleted;
            _requestDirector.RequestExpired -= HandleTimedRequestExpired;
        }

        private void HandleTimedRequestCompleted(GiftRequest request)
        {
            if (request == null || request.Id != _timingRequestId) return;
            if (_timingState != ERoundTripTimingState.TravelingToHouse) return;
            ReachHouse();
        }

        /// <summary>계측하던 의뢰가 만료되면 그 왕복은 표본이 아니다. 중앙 대기로 되돌린다.</summary>
        private void HandleTimedRequestExpired(GiftRequest request)
        {
            if (request == null || request.Id != _timingRequestId) return;
            AbandonRoundTrip();
        }

        private void AbandonRoundTrip()
        {
            _timingRequestId = -1;
            _timingHouseIndex = -1;
            _timingRequest = null;
            _timingRouteDistanceM = 0f;
            _roundTripStartedAt = 0d;
            _returnStartedAt = 0d;
            _outboundPathM = 0d;
            _outboundMovingSeconds = 0d;
            ResetLeg();
            _timingState = ERoundTripTimingState.WaitingForCenter;
        }

        private void Start()
        {
            if (_beginOnStart && _gameManager != null) _gameManager.BeginPlaying();

            PenguinInputReader reader = FindAnyObjectByType<PenguinInputReader>();
            if (reader != null) _player = reader.transform;
        }

        private void Update()
        {
            if (_requestDirector == null) return;

            UpdateRoundTripTiming();

            // 계측 대상 표시용. 패널의 ◆ 표식이 읽는다.
            _targetRequestId = _timingState == ERoundTripTimingState.TravelingToHouse
                ? _timingRequestId
                : -1;
        }

        private void UpdateRoundTripTiming()
        {
            if (_player == null || _base == null) return;

            TrackPlayerMovement();

            bool nearBase = IsNearPosition(_base.position, _baseRadius);
            switch (_timingState)
            {
                case ERoundTripTimingState.WaitingForCenter:
                    GiftRequest request = FindActiveById(PickMostTimeRemainingId());
                    if (nearBase && request != null) BeginRoundTrip(request);
                    break;

                case ERoundTripTimingState.TravelingToHouse:
                    // 도착 판정은 이제 근접이 아니라 실제 배달이다. RequestDirector 가 존에서
                    // 종류가 맞는 상자를 찾았을 때 RequestCompleted 를 쏘고, 그때 구간이 끊긴다.
                    break;

                case ERoundTripTimingState.ReturningToCenter:
                    if (nearBase && !_wasNearBase) CompleteRoundTrip();
                    break;
            }

            _wasNearBase = nearBase;
        }

        /// <summary>이번 구간에서 실제로 걸어간 거리와 움직인 시간을 누적한다.
        /// 계측 중이 아닐 때는 위치만 따라가고 누적은 하지 않는다.</summary>
        private void TrackPlayerMovement()
        {
            Vector3 position = _player.position;
            if (!_hasLastPlayerPosition)
            {
                _lastPlayerPosition = position;
                _hasLastPlayerPosition = true;
                return;
            }

            Vector3 delta = position - _lastPlayerPosition;
            delta.y = 0f;
            _lastPlayerPosition = position;

            if (_timingState == ERoundTripTimingState.WaitingForCenter) return;

            float step = delta.magnitude;
            float dt = Time.deltaTime;
            _legPathM += step;
            if (dt > 0f && step / dt >= MovingSpeedThresholdMps) _legMovingSeconds += dt;
        }

        private void ResetLeg()
        {
            _legPathM = 0d;
            _legMovingSeconds = 0d;
        }

        private static string LegLabel(double pathM, double straightM, double movingSeconds, double elapsedSeconds)
        {
            double speed = movingSeconds > 0.001d ? pathM / movingSeconds : 0d;
            double detour = straightM > 0.01d ? pathM / straightM : 0d;
            return $"path={pathM:0.0}m detour={detour:0.00}x moving={movingSeconds:0.00}s " +
                   $"idle={elapsedSeconds - movingSeconds:0.00}s speed={speed:0.00}m/s";
        }

        private void BeginRoundTrip(GiftRequest request)
        {
            _timingRequestId = request.Id;
            _timingHouseIndex = request.HouseIndex;
            _timingRequest = request;
            _timingRouteDistanceM = request.DistanceM;
            _roundTripStartedAt = Time.timeAsDouble;
            _returnStartedAt = 0d;
            _outboundPathM = 0d;
            _outboundMovingSeconds = 0d;
            ResetLeg();
            _timingState = ERoundTripTimingState.TravelingToHouse;

            Debug.Log($"[DeliveryTiming] START request={_timingRequestId} house={_timingHouseIndex} " +
                      $"route={_timingRouteDistanceM:0.0}m");
        }

        private void ReachHouse()
        {
            double now = Time.timeAsDouble;
            double outboundSeconds = now - _roundTripStartedAt;
            _returnStartedAt = now;
            _outboundPathM = _legPathM;
            _outboundMovingSeconds = _legMovingSeconds;
            _timingState = ERoundTripTimingState.ReturningToCenter;

            Debug.Log($"[DeliveryTiming] CENTER_TO_HOUSE request={_timingRequestId} house={_timingHouseIndex} " +
                      $"elapsed={outboundSeconds:0.00}s route={_timingRouteDistanceM:0.0}m " +
                      $"{LegLabel(_outboundPathM, _timingRouteDistanceM, _outboundMovingSeconds, outboundSeconds)}");
            ResetLeg();
        }

        private void CompleteRoundTrip()
        {
            double now = Time.timeAsDouble;
            double returnSeconds = now - _returnStartedAt;
            double roundTripSeconds = now - _roundTripStartedAt;

            double roundTripPathM = _outboundPathM + _legPathM;
            double roundTripMovingSeconds = _outboundMovingSeconds + _legMovingSeconds;

            Debug.Log($"[DeliveryTiming] HOUSE_TO_CENTER request={_timingRequestId} house={_timingHouseIndex} " +
                      $"elapsed={returnSeconds:0.00}s route={_timingRouteDistanceM:0.0}m " +
                      $"{LegLabel(_legPathM, _timingRouteDistanceM, _legMovingSeconds, returnSeconds)} " +
                      $"roundTrip={roundTripSeconds:0.00}s");
            // 선물 종류는 지금은 참고용이다. 눈덩이 제작·교환이 실제 상호작용으로 붙기 전에는
            // 집에 닿는 즉시 완료라 종류별 소요 시간 차이가 0으로 나온다.
            Debug.Log($"[DeliveryTiming] TRIP request={_timingRequestId} house={_timingHouseIndex} " +
                      $"kind={(_timingRequest != null ? _timingRequest.WantedKind.ToString() : "?")} " +
                      $"{LegLabel(roundTripPathM, _timingRouteDistanceM * 2d, roundTripMovingSeconds, roundTripSeconds)}");

            _timingSummary.Add(_timingRequestId, _timingHouseIndex, _timingRouteDistanceM,
                               _timingRequest != null ? _timingRequest.Difficulty : 0f,
                               _timingRequest != null ? _timingRequest.TtlSeconds : 0f,
                               now - _roundTripStartedAt - returnSeconds, returnSeconds,
                               roundTripPathM, roundTripMovingSeconds);
            LogTimingSummary();

            _timingRequestId = -1;
            _timingHouseIndex = -1;
            _timingRequest = null;
            _timingRouteDistanceM = 0f;
            _roundTripStartedAt = 0d;
            _returnStartedAt = 0d;
            _outboundPathM = 0d;
            _outboundMovingSeconds = 0d;
            ResetLeg();
            _timingState = ERoundTripTimingState.WaitingForCenter;
        }


        private int PickMostTimeRemainingId()
        {
            var requests = _requestDirector.ActiveRequests;
            int bestId = -1;
            float best = float.NegativeInfinity;
            for (int index = 0; index < requests.Count; index++)
            {
                if (requests[index].RemainingSeconds <= best) continue;
                best = requests[index].RemainingSeconds;
                bestId = requests[index].Id;
            }
            return bestId;
        }

        private GiftRequest FindActiveById(int id)
        {
            if (id < 0) return null;
            var requests = _requestDirector.ActiveRequests;
            for (int index = 0; index < requests.Count; index++)
                if (requests[index].Id == id) return requests[index];
            return null;
        }

        private bool IsNearPosition(Vector3 position, float radius)
        {
            Vector3 a = _player.position;
            Vector3 b = position;
            a.y = 0f;
            b.y = 0f;
            return Vector3.Distance(a, b) <= radius;
        }

        private void OnGUI()
        {
            if (!_showPanel) return;
            if (_gameManager == null || _requestDirector == null) return;

            GUILayout.BeginArea(new Rect(12, 12, 400, 680), GUI.skin.box);
            GUILayout.Label($"<b>PHASE</b> {_gameManager.Phase}");
            GUILayout.Label($"<b>TIME</b> {_gameManager.RemainingSeconds:0.0} s");
            GUILayout.Label($"<b>SCORE</b> {_gameManager.Score}");
            GUILayout.Space(4);
            GUILayout.Label($"<b>ROUTE TIMER</b> {TimingLabel()}");
            GUILayout.Label($"<b>SUMMARY</b> {_timingSummary.ToCompactLabel()}");
            GUILayout.Label("<b>REQUESTS</b>  (◆ 화살표=계측 대상 / 종류가 맞는 상자를 존에 놓아야 완료)");

            var requests = _requestDirector.ActiveRequests;
            int count = Mathf.Min(requests.Count, 9);
            for (int index = 0; index < count; index++)
            {
                GiftRequest request = requests[index];
                DeliveryHouse house = _requestDirector.HouseAt(request.HouseIndex);
                bool near = house != null && house.Zone != null && _player != null
                            && IsNearPosition(house.Zone.transform.position, _baseRadius);
                bool targeted = request.Id == _targetRequestId;
                string tag = near ? "<color=#7CFC00>▶ NEAR</color>" : "<color=#888888>far</color>";
                string star = targeted ? "<color=#FFD24A>◆</color> " : "";
                GUILayout.Label($"{star}House {request.HouseIndex} · {request.WantedKind} · " +
                                $"TTL {request.RemainingSeconds:0} · R{request.Reward}   {tag}");
            }
            if (GUILayout.Button("LOG TIMING SUMMARY")) LogTimingSummary();
            GUILayout.EndArea();
        }

        [ContextMenu("Log Timing Summary")]
        private void LogTimingSummary()
        {
            Debug.Log(_timingSummary.ToLogLine());
        }

        private string TimingLabel()
        {
            switch (_timingState)
            {
                case ERoundTripTimingState.TravelingToHouse:
                    return $"CENTER → HOUSE {_timingHouseIndex} · {Time.timeAsDouble - _roundTripStartedAt:0.0}s";
                case ERoundTripTimingState.ReturningToCenter:
                    return $"HOUSE {_timingHouseIndex} → CENTER · {Time.timeAsDouble - _returnStartedAt:0.0}s";
                default:
                    return "중앙에서 의뢰 대기";
            }
        }

    }
}
