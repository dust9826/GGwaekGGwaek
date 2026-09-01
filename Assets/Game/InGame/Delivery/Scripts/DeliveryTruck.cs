using System;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryTruck : MonoBehaviour
    {
        [Header("차체와 주행")]
        [SerializeField, Min(0.1f)] private float _speedMetersPerSecond = 5f;
        [SerializeField, Min(0.1f)] private float _reverseSpeedMetersPerSecond = 3f;
        [SerializeField, Min(0.1f)] private float _truckLength = 4f;
        [SerializeField, Min(0.1f)] private float _truckWidth = 2f;
        [SerializeField, Min(0f)] private float _roadSafetyMargin = 0.25f;
        [SerializeField, Min(0f)] private float _lateralMoveMetersPerSecond = 2f;

        [Header("가감속")]
        [Tooltip("가속도. 순항 속도 자체는 안 바꾼다 — 거기 도달하는 방식만 정한다.")]
        [SerializeField, Min(0.1f)] private float _accelMps2 = 2f;
        [Tooltip("평상시 감속도. 미리 보이는 것(코너·교차로·도착지) 앞에서 여유 있게 줄일 때 쓴다.")]
        [SerializeField, Min(0.1f)] private float _brakeMps2 = 4f;
        [Tooltip("낼 수 있는 최대 감속도. 계획에는 안 쓰고 갑자기 나타난 것 앞에서만 실제로 여기까지 나온다. 순항 5m/s 에서 제동거리 1m.")]
        [SerializeField, Min(0.1f)] private float _emergencyBrakeMps2 = 12.5f;
        [Tooltip("코너에서 허용하는 횡가속도. 낮출수록 코너를 더 느리게 돈다.")]
        [SerializeField, Min(0.1f)] private float _maxLateralAccelMps2 = 2.5f;
        [Tooltip("속도 상한을 미리 보는 거리. 제동거리보다 넉넉해야 코너 앞에서 미리 준다.")]
        [SerializeField, Min(1f)] private float _speedLookAheadMeters = 14f;
        [SerializeField, Min(0.25f)] private float _speedSampleSpacing = 1f;
        [Tooltip("노드에서 도로가 꺾이는 각도를 펴서 보는 거리. 짧을수록 교차로를 더 느리게 돈다.")]
        [SerializeField, Min(0.5f)] private float _turnBlendMeters = 4f;
        [Tooltip("전진과 후진을 바꿀 때 서 있는 시간. 현실 트럭이 기어를 바꾸는 사이다.")]
        [SerializeField, Min(0f)] private float _gearChangeSeconds = 0.6f;
        [Tooltip("차체가 도로 방향을 따라가는 최대 각속도. 낮출수록 코너에서 더 늦게 돈다.")]
        [SerializeField, Min(10f)] private float _maxYawRateDegPerSecond = 120f;
        [Tooltip("방향 오차를 다 풀 때까지 미끄러져도 되는 거리. 작을수록 사소한 꺾임(30도 안팎)에도 " +
                 "속도를 세게 누른다 — 방향이 몸보다 늦게 따라오는 느낌을 줄인다.")]
        [SerializeField, Min(0.05f)] private float _headingCreepToleranceMeters = 0.5f;

        [Header("눈 통로")]
        [SerializeField, Min(0.5f)] private float _snowLookAheadMeters = 4f;
        [SerializeField, Range(1, 255)] private int _blockingSnowDepthCm = 5;
        [SerializeField, Min(0.1f)] private float _longitudinalSampleSpacing = 0.75f;
        [SerializeField, Min(0.1f)] private float _lateralSampleSpacing = 0.5f;

        /// <summary>곡률을 평균 내는 창. 도로 곡선의 코너 반경(기본 2m)과 같은 규모여야 한다.</summary>
        private const float CurvatureWindowMeters = 2f;

        /// <summary>이만큼 남으면 도착으로 친다. 감속하며 붙으므로 0에 정확히 닿기를 기다리지 않는다.</summary>
        private const float ArrivalEpsilonMeters = 0.1f;

        /// <summary>이 아래면 "섰다"고 본다.</summary>
        private const float StoppedSpeed = 0.05f;

        /// <summary>정지선에 이만큼 붙었으면 "정지선까지 갔다"고 본다.</summary>
        private const float StopLineReachedMeters = 0.5f;

        /// <summary>방향 오차가 이 아래면 무시한다. 요 각속도 제한이 정상적으로 따라잡는
        /// 범위(작은 코너)까지 속도를 누르지 않기 위한 문턱이다.</summary>
        private const float HeadingErrorIgnoreDeg = 3f;

        private DeliveryDirector _director;
        private DeliveryTrafficController _trafficController;
        private SnowStage _snowStage;
        private float _desiredSnowOffset;
        private float _trafficOffset;
        private bool _hasTrafficOffset;
        private float _trafficSpeedFactor = 1f;
        private DeliveryTruck _yieldWinner;
        private DeliveryYieldPlan _yieldPlan;
        private float _yieldOriginalRouteDistance;
        private float _yieldSideProgress;
        private float _currentSpeed;
        private float _holdRemaining;
        private float _trafficStopRouteDistance;
        private float _trafficStopDwellSeconds;
        private bool _hasTrafficStopLine;
        private bool _stopCommitted;
        private float _committedStopRouteDistance;
        private Vector3 _previousPosition;
        private Vector3 _targetPosition;
        private Quaternion _previousRotation = Quaternion.identity;
        private Quaternion _targetRotation = Quaternion.identity;
        private float _lastPoseFixedTime;
        private bool _hasPose;

        public DeliveryRequest Request { get; private set; }
        /// <summary>지금 실제로 내고 있는 속력(m/s). 연출이 읽는다 — 권위는 이쪽에 있다.</summary>
        public float CurrentSpeed => _currentSpeed;
        /// <summary>직전 물리 스텝의 종가속도(m/s²). 양수면 가속, 음수면 제동이다.</summary>
        public float CurrentAcceleration { get; private set; }
        public EDeliveryTruckState State { get; private set; } = EDeliveryTruckState.Driving;
        public float RouteDistance { get; private set; }
        public float LateralOffset { get; private set; }
        public float HalfLength => _truckLength * 0.5f;
        public float HalfWidth => _truckWidth * 0.5f;
        public float RequiredHalfWidth => HalfWidth + _roadSafetyMargin;
        public float SpeedMetersPerSecond => _speedMetersPerSecond;
        public DeliveryTruck YieldWinner => _yieldWinner;
        public DeliveryRoutePose CurrentRoutePose => Request.Route.Evaluate(RouteDistance);

        /// <summary>
        /// 눈 판정을 시작하는 지점. 차체 앞끝에 걸친 눈 셀까지 제외한다 — 경계점만 자르면 그 점이
        /// 가리키는 셀 중심이 차체 밑에 남을 수 있으므로 셀 대각선만큼 앞으로 나간 곳부터 본다.
        /// </summary>
        private float CurrentFootprintExclusionStart
            => RouteDistance + HalfLength
               + (_snowStage == null ? 0f : _snowStage.CellSize * Mathf.Sqrt(2f));

        public void Initialize(DeliveryRequest request, DeliveryDirector director,
                               DeliveryTrafficController trafficController, SnowStage snowStage)
        {
            Request = request ?? throw new ArgumentNullException(nameof(request));
            _director = director;
            _trafficController = trafficController;
            _snowStage = snowStage;
            RouteDistance = 0f;
            State = EDeliveryTruckState.Driving;

            // 기본 차선에서 출발한다. 예전엔 LateralOffset 초기값 0(= 도로 정중앙)인 채로
            // ApplyMainRoutePose 를 불러서, 트럭이 중앙선에 스폰됐다가 TickDriving 이 채우는
            // _desiredSnowOffset 을 향해 _lateralMoveMetersPerSecond 로 슬금슬금 차선으로
            // 옮겨갔다 — 그동안 경로 리본(항상 PreferredLateralOffset 에 그려진다)과 폭 4.5m
            // 도로 기준 약 1.13m 어긋나 보였다.
            LateralOffset = Request.Route.Evaluate(0f).PreferredLateralOffset;
            ApplyMainRoutePose();
            _trafficController?.Register(this);
        }

        private void OnDestroy() => _trafficController?.Unregister(this);

        private void FixedUpdate()
        {
            if (Request == null || Request.State != EDeliveryRequestState.Active) return;

            float deltaSeconds = Time.fixedDeltaTime;
            switch (State)
            {
                case EDeliveryTruckState.Driving:
                case EDeliveryTruckState.SnowBlocked:
                    TickDriving(deltaSeconds);
                    break;
                case EDeliveryTruckState.YieldReversing:
                    TickYieldReversing(deltaSeconds);
                    break;
                case EDeliveryTruckState.YieldWaiting:
                    ApplyYieldPose();
                    break;
                case EDeliveryTruckState.ResumeRoute:
                    TickResume(deltaSeconds);
                    break;
            }
        }

        public void ResetTrafficGuidance()
        {
            _hasTrafficOffset = false;
            _trafficSpeedFactor = 1f;
            _hasTrafficStopLine = false;
        }

        /// <summary>
        /// 교차로 정지선. 매 물리 스텝 다시 받으므로(<see cref="ResetTrafficGuidance"/>) 지정이
        /// 끊기면 자동으로 풀린다. 여러 번 불리면 가장 앞선 정지선이 이긴다.
        /// </summary>
        public void SetTrafficStopLine(float routeDistance, float dwellSeconds)
        {
            if (_hasTrafficStopLine && routeDistance >= _trafficStopRouteDistance) return;
            _trafficStopRouteDistance = routeDistance;
            _trafficStopDwellSeconds = dwellSeconds;
            _hasTrafficStopLine = true;

            // 한 번 정지선을 받으면 실제로 설 때까지 물러서지 않는다. 중재는 상대가 노드를
            // 지나는 순간 풀리는데, 그때 정지선을 같이 놓아 버리면 양보하던 트럭이 서지 않고
            // 슬금슬금 굴러서 통과한다 — 실측으로 5m/s 에서 2.0m/s 까지만 줄고 말았다
            // (2026-08-16). "교차로에서 잠시 선다" 는 이 약속에서 나온다.
            _committedStopRouteDistance = _stopCommitted
                ? Mathf.Min(_committedStopRouteDistance, routeDistance)
                : routeDistance;
            _stopCommitted = true;
        }

        public void SetTrafficOffset(float offset)
        {
            _trafficOffset = offset;
            _hasTrafficOffset = true;
        }

        public void SetTrafficSpeedFactor(float factor) => _trafficSpeedFactor = Mathf.Clamp01(factor);

        public bool CanUseOffset(float offset, float routeDistance = -1f)
        {
            // routeDistance 를 안 넘기면 "나는 지금 여기 서 있다" 는 뜻이라 내 발밑·후미는
            // 판정에서 뺀다. 명시적으로 다른 거리를 넘기면(후진 양보 지점 탐색처럼 아직 트럭이
            // 없는 자리를 검사하는 경우) 그 자리는 실제로 전부 비어 있어야 하므로 빼지 않는다.
            bool ownPosition = routeDistance < 0f;
            float distance = ownPosition ? RouteDistance : routeDistance;
            return DeliverySnowClearance.IsOffsetClear(
                Request.Route, distance, offset, _snowLookAheadMeters,
                HalfLength, HalfWidth, _roadSafetyMargin,
                _blockingSnowDepthCm, _longitudinalSampleSpacing,
                _snowStage == null ? null : _snowStage.DepthCmAtWorld,
                ownPosition ? CurrentFootprintExclusionStart : float.NegativeInfinity);
        }

        public bool CanUseSideRoad(DeliveryRoadSegment segment, bool reverse, float sideDistance)
        {
            if (segment == null || sideDistance <= 0f) return false;
            var sideRoute = new DeliveryRoute(new[] { new DeliveryRoadTraversal(segment, reverse) });
            float lookAhead = Mathf.Min(sideDistance, sideRoute.Length);
            return DeliverySnowClearance.IsOffsetClear(
                sideRoute, 0f, 0f, lookAhead,
                HalfLength, HalfWidth, _roadSafetyMargin,
                _blockingSnowDepthCm, _longitudinalSampleSpacing,
                _snowStage == null ? null : _snowStage.DepthCmAtWorld);
        }

        public void BeginYield(DeliveryTruck winner, in DeliveryYieldPlan plan)
        {
            if (State == EDeliveryTruckState.YieldReversing || State == EDeliveryTruckState.YieldWaiting) return;
            _yieldWinner = winner;
            _yieldPlan = plan;
            _yieldOriginalRouteDistance = RouteDistance;
            _yieldSideProgress = 0f;
            _stopCommitted = false;
            _holdRemaining = _gearChangeSeconds;
            State = EDeliveryTruckState.YieldReversing;
            Request.ClearSnowBlocked();
        }

        public void ResumeFromYield()
        {
            if (State != EDeliveryTruckState.YieldWaiting) return;
            _holdRemaining = _gearChangeSeconds;
            State = EDeliveryTruckState.ResumeRoute;
        }

        private void TickDriving(float deltaSeconds)
        {
            // 차체 앞끝에 걸친 눈 셀까지 제외한다 — 안 그러면 "내가 서 있는 자리에 아직 눈이
            // 있다" 는 사실만으로 영원히 SnowBlocked 에 갇힌다(2026-08-16 실측 버그).
            float preferredOffset = CurrentRoutePose.PreferredLateralOffset;
            bool hasClearPath = DeliverySnowClearance.TryFindOffset(
                Request.Route, RouteDistance, preferredOffset, _snowLookAheadMeters,
                HalfLength, HalfWidth, _roadSafetyMargin,
                _blockingSnowDepthCm, _longitudinalSampleSpacing, _lateralSampleSpacing,
                _snowStage == null ? null : _snowStage.DepthCmAtWorld,
                out _desiredSnowOffset,
                CurrentFootprintExclusionStart);

            if (!hasClearPath)
            {
                State = EDeliveryTruckState.SnowBlocked;
                if (Request.TickSnowBlocked(deltaSeconds, _director.SnowCancelSeconds))
                {
                    State = EDeliveryTruckState.Cancelled;
                    _director.NotifyCancelled(Request);
                    // 완주와 같은 이유로 스스로를 치운다 — 아래 주석 참조.
                    Destroy(gameObject);
                    return;
                }

                // 눈 앞에서 뚝 서지 않고 눈이 시작되는 자리까지 굴러가 선다. 목표를 0 으로
                // 두면 남은 거리와 무관하게 최대 감속이 걸리므로, 달리는 중일 때와 똑같이
                // 평상시 감속도로 계획한다 — 원래 있던 눈이면 여유 있게 줄어든다.
                float blockedStop = SnowStopRouteDistance();
                AdvanceAlongRoute(
                    Mathf.Min(_speedMetersPerSecond, StopLineLimit(blockedStop, _brakeMps2, deltaSeconds)),
                    blockedStop, deltaSeconds);
                ApplyMainRoutePose();
                return;
            }

            State = EDeliveryTruckState.Driving;
            Request.ClearSnowBlocked();
            float desiredOffset = _hasTrafficOffset ? _trafficOffset : _desiredSnowOffset;
            LateralOffset = Mathf.MoveTowards(LateralOffset, desiredOffset,
                                              _lateralMoveMetersPerSecond * deltaSeconds);

            // 교차로에 서 있는 동안은 대기 시간을 계속 채워 둔다. 길이 열려도 곧바로 튀어나가지
            // 않고 잠깐 서 있다가 출발하는 것이 여기서 나온다.
            // ⚠ "서 있다" 만으로 판정하면 안 된다. 스폰 직후 속도가 0 이라 정지선을 받은 트럭이
            // 출발조차 못 하고 제자리에서 기다린다(2026-08-16 실측: 4.2초 동안 RouteDistance 0).
            // 정지선까지 실제로 가서 선 것만 대기로 친다.
            float activeStopLine = _hasTrafficStopLine ? _trafficStopRouteDistance : _committedStopRouteDistance;
            bool waitingAtStopLine = _stopCommitted
                                     && _currentSpeed <= StoppedSpeed
                                     && RouteDistance >= activeStopLine - StopLineReachedMeters;
            if (waitingAtStopLine)
            {
                if (_hasTrafficStopLine) _holdRemaining = _trafficStopDwellSeconds;
                else if (_holdRemaining <= 0f) _stopCommitted = false;
            }
            if (!TickHold(deltaSeconds))
            {
                ApplyMainRoutePose();
                return;
            }

            // 순항 속도는 그대로 _speedMetersPerSecond 다. 코너·노드가 상한을 내릴 뿐이고,
            // 직선에 나오면 정확히 이 값으로 돌아온다.
            float targetSpeed = Mathf.Min(_speedMetersPerSecond * _trafficSpeedFactor, RouteSpeedLimit());
            // 노드를 이미 지났는데 방향이 아직 안 따라잡혔으면 여기서 다시 누른다 — 미리
            // 감속하는 RouteSpeedLimit 은 노드를 지나는 순간 손을 뗀다(HeadingErrorSpeedLimit 참조).
            targetSpeed = Mathf.Min(targetSpeed, HeadingErrorSpeedLimit());

            // 정지선은 눈이든 교차로든 전부 평상시 감속도로 계획한다. "갑자기 나타난 것 앞에서
            // 급정지" 는 여기서 만드는 것이 아니라, 낼 수 있는 감속도가 그보다 크다는 사실
            // (_emergencyBrakeMps2)에서 저절로 나온다 — SnowStopRouteDistance 주석 참조.
            float plannedStop = Request.Route.Length;
            if (_hasTrafficStopLine) plannedStop = Mathf.Min(plannedStop, _trafficStopRouteDistance);
            else if (_stopCommitted) plannedStop = Mathf.Min(plannedStop, _committedStopRouteDistance);
            float snowStop = SnowStopRouteDistance();
            targetSpeed = Mathf.Min(targetSpeed, StopLineLimit(plannedStop, _brakeMps2, deltaSeconds));
            targetSpeed = Mathf.Min(targetSpeed, StopLineLimit(snowStop, _brakeMps2, deltaSeconds));

            AdvanceAlongRoute(targetSpeed, Mathf.Min(plannedStop, snowStop), deltaSeconds);
            ApplyMainRoutePose();

            if (RouteDistance < Request.Route.Length - ArrivalEpsilonMeters) return;
            RouteDistance = Request.Route.Length;
            State = EDeliveryTruckState.Completed;
            _director.NotifyCompleted(Request);

            // 완주·취소된 트럭은 스스로를 치운다. 예전엔 여기서 멈춘 채 영원히 남아 있었다 —
            // FixedUpdate 의 switch 문에 Completed/Cancelled 케이스가 없어 그냥 아무 일도 안 하고
            // 계속 존재했고, 트럭 수 상한(DeliveryDirector.ActiveTruckCount)도 실질적으로 의미가
            // 없어졌다(다 완료된 트럭까지 자리를 영원히 차지). 표지·경로 화살표·도착지 핀은 전부
            // 이 트럭의 자식이거나 OnDestroy 로 정리되므로 같이 사라진다(2026-08-16).
            Destroy(gameObject);
        }

        /// <summary>
        /// 눈이 시작되는 자리(= 정지선)의 경로 거리. 없으면 검사 창 끝이다.
        ///
        /// <b>원래 있던 눈과 갑자기 쌓인 눈을 코드가 구분하지 않는다 — 구분할 필요가 없다.</b>
        /// 둘 다 평상시 감속도로 계획하되(<c>_brakeMps2</c>), 실제로 낼 수 있는 감속도는 그보다
        /// 큰 <c>_emergencyBrakeMps2</c> 이므로 결과가 저절로 갈린다:
        /// <list type="bullet">
        /// <item>원래 있던 눈은 검사 창(4m) 끝에서 보이고 평상시 제동거리(3.1m)가 그 안에
        /// 들어오므로 <b>여유 있게</b> 줄어든다.</item>
        /// <item>몬스터가 코앞에 쌓은 눈은 남은 거리가 갑자기 1m 가 되고, 그 거리에 맞는 목표
        /// 속도로 떨어지려면 최대 감속도가 다 나온다 — <b>급정지</b>가 된다.</item>
        /// </list>
        /// </summary>
        private float SnowStopRouteDistance()
        {
            float clear = DeliverySnowClearance.ClearDistance(
                Request.Route, RouteDistance, LateralOffset, _snowLookAheadMeters,
                HalfLength, HalfWidth, _roadSafetyMargin,
                _blockingSnowDepthCm, _longitudinalSampleSpacing,
                _snowStage == null ? null : _snowStage.DepthCmAtWorld,
                CurrentFootprintExclusionStart);
            return RouteDistance + clear;
        }

        /// <summary>
        /// 정지선 하나를 지정한 감속도로 환산한 속도 상한. 이번 스텝에 갈 거리를 미리 빼는 것은
        /// 이산 적분이 정지선을 <c>v·dt</c> 만큼 넘어가는 것을 막기 위해서다(실측 4cm, 0.56m/s).
        /// </summary>
        private float StopLineLimit(float stopRouteDistance, float brake, float deltaSeconds)
            => RemainingLimit(stopRouteDistance - RouteDistance, brake, deltaSeconds);

        /// <summary>남은 거리를 지정한 감속도로 환산한 속도 상한. 후진처럼 방향이 다를 때도 쓴다.</summary>
        private float RemainingLimit(float remainingDistance, float brake, float deltaSeconds)
            => DeliveryTruckMotion.ApproachSpeedLimit(
                0f,
                Mathf.Max(0f, remainingDistance - _currentSpeed * deltaSeconds),
                brake);

        /// <summary>
        /// 목표 속도로 가·감속하며 전진한다. <paramref name="stopRouteDistance"/> 는 절대 못 넘는다.
        /// <b>목표를 깎는 것은 호출부의 몫이다</b> — 정지선마다 쓰는 감속도가 다르기 때문이다
        /// (<see cref="StopLineLimit"/>).
        /// </summary>
        private void AdvanceAlongRoute(float targetSpeed, float stopRouteDistance, float deltaSeconds)
        {
            float step = StepSpeed(targetSpeed, deltaSeconds);
            // 정지선이 이미 지나간 자리여도 뒤로 가지는 않는다 — 전진 전용이다.
            RouteDistance = Mathf.Max(RouteDistance, Mathf.Min(stopRouteDistance, RouteDistance + step));
        }

        /// <summary>속도를 한 스텝 적분하고 이번 스텝에 갈 거리를 돌려준다. 전진·후진이 함께 쓴다.</summary>
        private float StepSpeed(float targetSpeed, float deltaSeconds)
        {
            // 감속 자체는 언제나 최대 감속도까지 낼 수 있다. "천천히 감속" 은 목표 속도를 미리
            // 낮춰서 만드는 것이지(StopLineLimit 에 _brakeMps2 를 넘긴다), 낼 수 있는 감속도를
            // 묶어서 만드는 것이 아니다 — 묶으면 눈처럼 갑자기 나타난 것 앞에서 못 선다.
            float speed = DeliveryTruckMotion.StepSpeed(_currentSpeed, targetSpeed,
                                                        _accelMps2, _emergencyBrakeMps2, deltaSeconds);
            CurrentAcceleration = deltaSeconds > 0f ? (speed - _currentSpeed) / deltaSeconds : 0f;
            _currentSpeed = speed;
            return speed * deltaSeconds;
        }

        /// <summary>
        /// 전진과 후진 사이에 서서 기어를 바꾸는 시간. 다 됐으면 true.
        /// 아직 굴러가고 있으면 먼저 세운다 — 방향을 즉시 뒤집지 않는다.
        /// </summary>
        private bool TickHold(float deltaSeconds)
        {
            if (_holdRemaining <= 0f) return true;
            if (_currentSpeed > 0.01f)
            {
                AdvanceAlongRoute(0f, Request.Route.Length, deltaSeconds);
                return false;
            }

            _holdRemaining -= deltaSeconds;
            return _holdRemaining <= 0f;
        }

        /// <summary>
        /// 앞을 훑어 지금 낼 수 있는 최대 속도를 구한다. 도로 안의 곡률과 노드의 꺾임을 둘 다 보고,
        /// 각각을 제동거리로 되돌려 "코너에 닿기 전에" 속도를 내린다.
        /// </summary>
        private float RouteSpeedLimit()
        {
            DeliveryRoute route = Request.Route;
            float end = Mathf.Min(route.Length, RouteDistance + _speedLookAheadMeters);
            float limit = float.PositiveInfinity;

            for (float distance = RouteDistance; distance <= end + 0.001f; distance += _speedSampleSpacing)
            {
                float corner = DeliveryTruckMotion.CornerSpeedLimit(
                    route.CurvatureAt(distance, CurvatureWindowMeters), _maxLateralAccelMps2);
                limit = Mathf.Min(limit, DeliveryTruckMotion.ApproachSpeedLimit(
                    corner, distance - RouteDistance, _brakeMps2));
            }

            // 곡선 평가는 도로 하나 안에서만 이어진다. 노드에서 꺾이는 각도는 곡률로 안 잡히므로
            // 따로 본다 — 교차로에서 방향을 트는 트럭이 감속하는 것이 여기서 나온다.
            for (int index = 0; index < route.BoundaryCount; index++)
            {
                float distance = route.BoundaryDistance(index);
                if (distance < RouteDistance || distance > end) continue;
                float turn = DeliveryTruckMotion.TurnSpeedLimit(
                    route.BoundaryTurnDegrees(index), _turnBlendMeters, _maxLateralAccelMps2);
                limit = Mathf.Min(limit, DeliveryTruckMotion.ApproachSpeedLimit(
                    turn, distance - RouteDistance, _brakeMps2));
            }

            return limit;
        }

        /// <summary>
        /// 지금 실제로 벌어진 방향 오차만큼 속도를 누른다.
        ///
        /// <b>노드 앞에서 미리 감속하는 것만으로는 부족하다.</b> `RouteSpeedLimit`은 노드에
        /// 닿기 <i>전</i>에만 속도를 낮춘다 — 일단 지나가면 그 경계는 더 이상 "앞"이 아니라서
        /// 손을 뗀다. 그런데 꺾이는 각도가 크면(실측 149.6°) 요 각속도 상한(120°/s)이 방향을
        /// 다 돌리는 데 1초 넘게 걸리고, 그동안 순항 속도로 돌아가 버리면 <b>몸은 새 도로를
        /// 따라가는데 얼굴은 옛 방향을 본 채로 미끄러진다</b> — 실측: 노드를 지나 1.3m 만에
        /// 방향 오차 85° (2026-08-16, 사용자가 재현 요청). 이게 "회전이 안 부드럽다"의 실체다.
        ///
        /// 그래서 사전 계산이 아니라 <b>지금 이 순간의 실제 오차</b>를 본다. `_targetRotation`
        /// (요 각속도 제한이 이미 따라가고 있는 방향)과 지금 위치의 이상적 방향 사이 각도가
        /// 크면 클수록 속도를 더 세게 누른다 — 오차가 다 풀릴 때까지
        /// <c>_headingCreepToleranceMeters</c> 만큼만 미끄러지도록 역산한다. 오차가 줄면
        /// 자동으로 풀린다.
        ///
        /// ⚠ <b>허용 거리를 차체 길이 규모로 두면 30° 안팎의 흔한 꺾임에는 아무 효과가 없다.</b>
        /// 처음엔 반 차체 길이(2m)로 뒀는데, 그러면 `2×120/30 = 8m/s`가 나와 순항 5m/s 를 절대
        /// 못 넘으므로 아예 개입하지 않았다(2026-08-16, 사용자가 "사소한 각도도 부드럽게" 요청
        /// 하며 드러남). 지금 값(0.5m)은 30°에서 `0.5×120/30 = 2m/s`로 실제로 느려진다.
        /// </summary>
        private float HeadingErrorSpeedLimit()
        {
            if (!_hasPose) return float.PositiveInfinity;

            DeliveryRoutePose pose = Request.Route.Evaluate(RouteDistance);
            Quaternion idealRotation = Quaternion.LookRotation(pose.Forward, Vector3.up);
            float errorDeg = Quaternion.Angle(_targetRotation, idealRotation);
            if (errorDeg < HeadingErrorIgnoreDeg) return float.PositiveInfinity;

            return DeliveryTruckMotion.HeadingCatchUpSpeedLimit(
                errorDeg, _headingCreepToleranceMeters, _maxYawRateDegPerSecond);
        }

        private void TickYieldReversing(float deltaSeconds)
        {
            Request.ClearSnowBlocked();

            // 전진하다 그 자리에서 방향만 뒤집지 않는다 — 먼저 서고, 기어를 바꾸는 시간을 보낸다.
            if (!TickHold(deltaSeconds))
            {
                ApplyMainRoutePose();
                return;
            }

            if (RouteDistance > _yieldPlan.RetreatRouteDistance + 0.01f)
            {
                float remaining = RouteDistance - _yieldPlan.RetreatRouteDistance
                                  + (_yieldPlan.UsesSideRoad ? _yieldPlan.SideDistance : 0f);
                float step = StepSpeed(
                    Mathf.Min(_reverseSpeedMetersPerSecond, RemainingLimit(remaining, _brakeMps2, deltaSeconds)),
                    deltaSeconds);
                RouteDistance = Mathf.Max(_yieldPlan.RetreatRouteDistance, RouteDistance - step);
                LateralOffset = Mathf.MoveTowards(LateralOffset, _yieldPlan.LateralOffset,
                                                  _lateralMoveMetersPerSecond * deltaSeconds);
                ApplyMainRoutePose();
                return;
            }

            if (_yieldPlan.UsesSideRoad && _yieldSideProgress < _yieldPlan.SideDistance - 0.01f)
            {
                float sideRemaining = _yieldPlan.SideDistance - _yieldSideProgress;
                float step = StepSpeed(
                    Mathf.Min(_reverseSpeedMetersPerSecond,
                              RemainingLimit(sideRemaining, _brakeMps2, deltaSeconds)),
                    deltaSeconds);
                _yieldSideProgress = Mathf.Min(_yieldPlan.SideDistance, _yieldSideProgress + step);
                ApplyYieldPose();
                return;
            }

            _currentSpeed = 0f;
            State = EDeliveryTruckState.YieldWaiting;
            ApplyYieldPose();
        }

        private void TickResume(float deltaSeconds)
        {
            if (!TickHold(deltaSeconds))
            {
                ApplyYieldPose();
                return;
            }

            if (_yieldSideProgress > 0.01f)
            {
                float remaining = _yieldSideProgress + (_yieldOriginalRouteDistance - RouteDistance);
                float step = StepSpeed(
                    Mathf.Min(_speedMetersPerSecond, RemainingLimit(remaining, _brakeMps2, deltaSeconds)),
                    deltaSeconds);
                _yieldSideProgress = Mathf.Max(0f, _yieldSideProgress - step);
                ApplyYieldPose();
                return;
            }

            if (RouteDistance < _yieldOriginalRouteDistance - 0.01f)
            {
                AdvanceAlongRoute(
                    Mathf.Min(_speedMetersPerSecond,
                              StopLineLimit(_yieldOriginalRouteDistance, _brakeMps2, deltaSeconds)),
                    _yieldOriginalRouteDistance, deltaSeconds);
                ApplyMainRoutePose();
                return;
            }

            _yieldWinner = null;
            State = EDeliveryTruckState.Driving;
        }

        private void ApplyMainRoutePose()
        {
            DeliveryRoutePose pose = Request.Route.Evaluate(RouteDistance);
            SetPose(pose.Position + pose.SegmentRight * LateralOffset,
                    Quaternion.LookRotation(pose.Forward, Vector3.up));
        }

        /// <summary>
        /// 물리 스텝의 포즈를 기록한다. 실제 트랜스폼은 <see cref="Update"/> 가 두 스텝 사이를
        /// 보간해서 쓴다 — 50Hz 로 직접 쓰면 화면이 훨씬 빨리 그려지는 만큼 계단으로 튄다
        /// (<c>../Vehicle/AGENTS.md</c> 의 지터 항목과 같은 뿌리다. 트럭은 Rigidbody 가 아니라
        /// <c>interpolation</c> 을 못 쓰므로 직접 보간한다).
        ///
        /// 방향은 곡선 접선을 그대로 대입하지 않고 최대 각속도로 따라간다. 접선이 샘플 구간마다
        /// 상수라 그대로 쓰면 코너에서 계단으로 꺾이고, 노드에서는 한 프레임에 통째로 돌아간다.
        /// </summary>
        private void SetPose(Vector3 position, Quaternion rotation)
        {
            _previousPosition = _hasPose ? _targetPosition : position;
            _previousRotation = _hasPose ? _targetRotation : rotation;
            _targetPosition = position;
            _targetRotation = _hasPose
                ? Quaternion.RotateTowards(_targetRotation, rotation,
                                           _maxYawRateDegPerSecond * Time.fixedDeltaTime)
                : rotation;
            _lastPoseFixedTime = Time.fixedTime;

            if (_hasPose) return;
            _hasPose = true;
            transform.SetPositionAndRotation(position, rotation);
        }

        private void Update()
        {
            if (!_hasPose) return;
            float step = Mathf.Max(0.0001f, Time.fixedDeltaTime);
            float t = Mathf.Clamp01((Time.time - _lastPoseFixedTime) / step);
            transform.SetPositionAndRotation(Vector3.Lerp(_previousPosition, _targetPosition, t),
                                             Quaternion.Slerp(_previousRotation, _targetRotation, t));
        }

        private void ApplyYieldPose()
        {
            if (!_yieldPlan.UsesSideRoad || _yieldSideProgress <= 0f)
            {
                ApplyMainRoutePose();
                return;
            }

            float segmentDistance = _yieldPlan.SideReverse
                ? _yieldPlan.SideSegment.Length - _yieldSideProgress
                : _yieldSideProgress;
            DeliveryRoadPose pose = _yieldPlan.SideSegment.Evaluate(segmentDistance);
            Vector3 outward = _yieldPlan.SideReverse ? -pose.Tangent : pose.Tangent;
            transform.SetPositionAndRotation(pose.Position, Quaternion.LookRotation(-outward, Vector3.up));
        }
    }
}
