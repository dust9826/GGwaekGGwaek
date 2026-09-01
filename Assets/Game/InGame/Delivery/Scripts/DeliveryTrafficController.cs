using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 트럭의 도로 진입을 막지 않고 실제 조우만 중재한다. 넓으면 서로 다른 통로를 주고,
    /// 불가능하면 낮은 우선순위 트럭을 뒤쪽 넓은 구간이나 빈 옆길로 후진시킨다.
    /// </summary>
    public sealed class DeliveryTrafficController : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNetwork _network;
        [SerializeField] private DeliveryYieldPoint[] _authoredYieldPoints = Array.Empty<DeliveryYieldPoint>();
        [SerializeField, Min(1f)] private float _conflictPredictionDistance = 10f;
        [SerializeField, Min(1f)] private float _passClearDistance = 8f;
        [SerializeField, Min(1f)] private float _maxReverseSearchDistance = 30f;
        [SerializeField, Min(0.5f)] private float _yieldSearchStep = 1f;
        [SerializeField, Min(0f)] private float _trafficSafetyGap = 0.5f;

        [Header("교차로")]
        [Tooltip("노드를 얼마나 앞에서부터 예약 대상으로 보는가.")]
        [SerializeField, Min(1f)] private float _junctionPredictionDistance = 18f;
        [Tooltip("노드 앞뒤로 이만큼은 교차로 안쪽으로 친다. 정지선도 이 앞에 선다.")]
        [SerializeField, Min(0.5f)] private float _junctionClearance = 3f;
        [Tooltip("길이 열린 뒤에도 서 있는 시간. 0이면 열리자마자 곧바로 출발한다.")]
        [SerializeField, Min(0f)] private float _junctionDwellSeconds = 0.6f;

        private readonly List<DeliveryTruck> _trucks = new List<DeliveryTruck>();
        private readonly List<JunctionClaim> _claims = new List<JunctionClaim>();

        /// <summary>한 트럭이 곧 지나갈 노드와 그 자리를 점유하는 시간창.</summary>
        private readonly struct JunctionClaim
        {
            public JunctionClaim(DeliveryTruck truck, DeliveryRoadNode node, float nodeRouteDistance,
                                 float enterSeconds, float exitSeconds)
            {
                Truck = truck;
                Node = node;
                NodeRouteDistance = nodeRouteDistance;
                EnterSeconds = enterSeconds;
                ExitSeconds = exitSeconds;
            }

            public DeliveryTruck Truck { get; }
            public DeliveryRoadNode Node { get; }
            public float NodeRouteDistance { get; }
            public float EnterSeconds { get; }
            public float ExitSeconds { get; }

            public bool OverlapsInTime(in JunctionClaim other)
                => EnterSeconds <= other.ExitSeconds && other.EnterSeconds <= ExitSeconds;
        }

        public void Configure(DeliveryRoadNetwork network,
                              IReadOnlyList<DeliveryYieldPoint> authoredYieldPoints = null)
        {
            _network = network;
            if (authoredYieldPoints == null)
            {
                _authoredYieldPoints = Array.Empty<DeliveryYieldPoint>();
                return;
            }

            _authoredYieldPoints = new DeliveryYieldPoint[authoredYieldPoints.Count];
            for (int index = 0; index < authoredYieldPoints.Count; index++)
                _authoredYieldPoints[index] = authoredYieldPoints[index];
        }

        public void Register(DeliveryTruck truck)
        {
            if (truck != null && !_trucks.Contains(truck)) _trucks.Add(truck);
        }

        public void Unregister(DeliveryTruck truck) => _trucks.Remove(truck);

        private void FixedUpdate()
        {
            _trucks.RemoveAll(truck => truck == null);
            foreach (DeliveryTruck truck in _trucks) truck.ResetTrafficGuidance();

            ResumeClearYielders();
            ResolveJunctions();
            for (int firstIndex = 0; firstIndex < _trucks.Count; firstIndex++)
            {
                DeliveryTruck first = _trucks[firstIndex];
                if (!CanParticipate(first)) continue;

                for (int secondIndex = firstIndex + 1; secondIndex < _trucks.Count; secondIndex++)
                {
                    DeliveryTruck second = _trucks[secondIndex];
                    if (!CanParticipate(second)) continue;
                    ResolvePair(first, second);
                }
            }
        }

        /// <summary>
        /// 교차로는 도로를 막는 것이 아니라 노드를 예약한다. 같은 노드를 겹치는 시간에 요구하는
        /// 트럭이 둘 이상일 때만 하나가 통과하고 나머지가 정지선 앞에 선다 — 아무와도 안 겹치는
        /// 트럭은 감속조차 하지 않는다(꺾이는 각도에 따른 감속은 트럭 자신이 따로 한다).
        /// </summary>
        private void ResolveJunctions()
        {
            _claims.Clear();
            foreach (DeliveryTruck truck in _trucks)
            {
                if (!CanParticipate(truck)) continue;
                if (TryBuildClaim(truck, out JunctionClaim claim)) _claims.Add(claim);
            }

            // 노드마다 승자를 하나만 뽑는다. 쌍으로 정하면 셋 이상이 물릴 때 서로 양보하는
            // 고리가 생길 수 있다 — 노드 단위로 뽑으면 그 고리가 아예 만들어지지 않는다.
            for (int index = 0; index < _claims.Count; index++)
            {
                JunctionClaim claim = _claims[index];
                bool hasConflict = false;
                bool isWinner = true;

                for (int other = 0; other < _claims.Count; other++)
                {
                    if (other == index) continue;
                    JunctionClaim rival = _claims[other];
                    if (rival.Node != claim.Node) continue;
                    // 같은 도로 위의 조우는 ResolvePair 가 통과 폭·후진 양보로 이미 다룬다.
                    if (rival.Truck.CurrentRoutePose.Segment == claim.Truck.CurrentRoutePose.Segment) continue;
                    if (!claim.OverlapsInTime(rival)) continue;

                    hasConflict = true;
                    if (ComparePriority(claim.Truck, rival.Truck) > 0) isWinner = false;
                }

                if (!hasConflict || isWinner) continue;

                // 이미 정지선을 지나 교차로 안에 들어와 있으면 세우지 않는다 — 거기서 멈추면
                // 교차로를 막은 채 굳는다. 빠져나가는 것이 먼저다.
                float stopLine = claim.NodeRouteDistance - _junctionClearance - claim.Truck.HalfLength;
                if (stopLine <= claim.Truck.RouteDistance) continue;
                claim.Truck.SetTrafficStopLine(stopLine, _junctionDwellSeconds);
            }
        }

        /// <summary>
        /// 트럭이 곧 지나갈 노드와 그 점유 시간창. 시간은 <b>실제 속도가 아니라 순항 속도</b>로
        /// 잰다 — 실제 속도로 재면 정지한 트럭의 도착 시간이 무한이 되어 서로를 영원히 기다린다.
        /// </summary>
        private bool TryBuildClaim(DeliveryTruck truck, out JunctionClaim claim)
        {
            claim = default;
            DeliveryRoute route = truck.Request.Route;
            float cruise = Mathf.Max(0.1f, truck.SpeedMetersPerSecond);

            for (int index = 0; index < route.BoundaryCount; index++)
            {
                float nodeDistance = route.BoundaryDistance(index);
                if (nodeDistance < truck.RouteDistance) continue;
                if (nodeDistance - truck.RouteDistance > _junctionPredictionDistance) return false;

                DeliveryRoadNode node = route.BoundaryNode(index);
                if (node == null) return false;

                float span = _junctionClearance + truck.HalfLength;
                float enter = Mathf.Max(0f, nodeDistance - span - truck.RouteDistance) / cruise;
                float exit = Mathf.Max(0f, nodeDistance + span - truck.RouteDistance) / cruise;
                claim = new JunctionClaim(truck, node, nodeDistance, enter, exit);
                return true;
            }

            return false;
        }

        private void ResolvePair(DeliveryTruck first, DeliveryTruck second)
        {
            DeliveryRoutePose firstPose = first.CurrentRoutePose;
            DeliveryRoutePose secondPose = second.CurrentRoutePose;
            if (firstPose.Segment != secondPose.Segment) return;

            bool opposing = Vector3.Dot(firstPose.Forward, secondPose.Forward) < -0.5f;
            float worldDistance = Vector3.Distance(first.transform.position, second.transform.position);
            if (!opposing)
            {
                ResolveFollowing(first, second, firstPose, secondPose, worldDistance);
                return;
            }
            if (worldDistance > _conflictPredictionDistance) return;

            float laneOffset = firstPose.Segment.DrivableWidth * 0.25f;
            float firstOffset = firstPose.Reverse ? -laneOffset : laneOffset;
            float secondOffset = secondPose.Reverse ? -laneOffset : laneOffset;
            float requiredWidth = first.HalfWidth * 2f + second.HalfWidth * 2f + _trafficSafetyGap * 2f;
            bool canPass = firstPose.Segment.DrivableWidth >= requiredWidth
                           && Mathf.Abs(firstOffset - secondOffset) >= first.HalfWidth + second.HalfWidth + _trafficSafetyGap
                           && first.CanUseOffset(firstOffset)
                           && second.CanUseOffset(secondOffset);
            if (canPass)
            {
                first.SetTrafficOffset(firstOffset);
                second.SetTrafficOffset(secondOffset);
                return;
            }

            DeliveryTruck winner = ComparePriority(first, second) <= 0 ? first : second;
            DeliveryTruck loser = winner == first ? second : first;
            if (TryBuildYieldPlan(loser, winner, out DeliveryYieldPlan plan)) loser.BeginYield(winner, plan);
            else
            {
                // 안전한 후진 경로가 전혀 없으면 충돌보다 정지가 우선이다. 교통 정지는 의뢰 취소에 포함되지 않는다.
                first.SetTrafficSpeedFactor(0f);
                second.SetTrafficSpeedFactor(0f);
            }
        }

        private void ResolveFollowing(DeliveryTruck first, DeliveryTruck second,
                                      DeliveryRoutePose firstPose, DeliveryRoutePose secondPose,
                                      float worldDistance)
        {
            float safeDistance = first.HalfLength + second.HalfLength + _trafficSafetyGap;
            if (worldDistance >= safeDistance * 2f) return;

            bool naturalForward = !firstPose.Reverse;
            DeliveryTruck trailing = naturalForward
                ? (firstPose.SegmentDistance < secondPose.SegmentDistance ? first : second)
                : (firstPose.SegmentDistance > secondPose.SegmentDistance ? first : second);
            float factor = Mathf.InverseLerp(safeDistance, safeDistance * 2f, worldDistance);
            trailing.SetTrafficSpeedFactor(factor);
        }

        /// <summary>음수면 first, 양수면 second가 우선이다.</summary>
        public static int ComparePriority(DeliveryTruck first, DeliveryTruck second)
        {
            DeliveryRoutePose firstPose = first.CurrentRoutePose;
            DeliveryRoutePose secondPose = second.CurrentRoutePose;
            float firstExit = firstPose.Reverse ? firstPose.SegmentDistance : firstPose.Segment.Length - firstPose.SegmentDistance;
            float secondExit = secondPose.Reverse ? secondPose.SegmentDistance : secondPose.Segment.Length - secondPose.SegmentDistance;
            float firstDepth = firstPose.Reverse ? firstPose.Segment.Length - firstPose.SegmentDistance : firstPose.SegmentDistance;
            float secondDepth = secondPose.Reverse ? secondPose.Segment.Length - secondPose.SegmentDistance : secondPose.SegmentDistance;
            return DeliveryTrafficPriority.Compare(firstExit, firstDepth, first.Request.Id,
                                                   secondExit, secondDepth, second.Request.Id);
        }

        private bool TryBuildYieldPlan(DeliveryTruck loser, DeliveryTruck winner, out DeliveryYieldPlan plan)
        {
            if (TryAuthoredYieldPoint(loser, winner, out plan)) return true;
            if (TryWideRoadBehind(loser, winner, out plan)) return true;
            if (TryEmptySideRoad(loser, out plan)) return true;

            DeliveryRoutePose pose = loser.CurrentRoutePose;
            float traversalStart = loser.Request.Route.TraversalStartDistance(pose.TraversalIndex);
            if (loser.RouteDistance - traversalStart <= _maxReverseSearchDistance)
            {
                plan = new DeliveryYieldPlan(traversalStart, 0f);
                return true;
            }

            plan = default;
            return false;
        }

        private bool TryAuthoredYieldPoint(DeliveryTruck loser, DeliveryTruck winner, out DeliveryYieldPlan plan)
        {
            float bestDistance = float.PositiveInfinity;
            DeliveryYieldPlan best = default;
            DeliveryRoutePose current = loser.CurrentRoutePose;

            foreach (DeliveryYieldPoint point in _authoredYieldPoints)
            {
                if (point == null || point.Segment != current.Segment) continue;
                float local = current.Reverse
                    ? current.Segment.Length - point.SegmentDistance
                    : point.SegmentDistance;
                float routeDistance = loser.Request.Route.TraversalStartDistance(current.TraversalIndex) + local;
                float reverseDistance = loser.RouteDistance - routeDistance;
                if (reverseDistance < 0f || reverseDistance > _maxReverseSearchDistance || reverseDistance >= bestDistance) continue;
                if (!loser.CanUseOffset(point.LateralOffset, routeDistance)) continue;
                bestDistance = reverseDistance;
                best = new DeliveryYieldPlan(routeDistance, point.LateralOffset);
            }

            plan = best;
            return !float.IsPositiveInfinity(bestDistance);
        }

        private bool TryWideRoadBehind(DeliveryTruck loser, DeliveryTruck winner, out DeliveryYieldPlan plan)
        {
            float minimum = Mathf.Max(0f, loser.RouteDistance - _maxReverseSearchDistance);
            for (float distance = loser.RouteDistance - _yieldSearchStep; distance >= minimum; distance -= _yieldSearchStep)
            {
                DeliveryRoutePose pose = loser.Request.Route.Evaluate(distance);
                float requiredWidth = loser.HalfWidth * 2f + winner.HalfWidth * 2f + _trafficSafetyGap * 2f;
                if (pose.Segment.DrivableWidth < requiredWidth) continue;
                float side = pose.Segment.DrivableWidth * 0.5f - loser.RequiredHalfWidth;
                float offset = pose.Reverse ? side : -side;
                if (!loser.CanUseOffset(offset, distance)) continue;
                plan = new DeliveryYieldPlan(distance, offset);
                return true;
            }

            plan = default;
            return false;
        }

        private bool TryEmptySideRoad(DeliveryTruck loser, out DeliveryYieldPlan plan)
        {
            if (_network == null)
            {
                plan = default;
                return false;
            }

            DeliveryRoutePose pose = loser.CurrentRoutePose;
            DeliveryRoadTraversal traversal = loser.Request.Route.Traversals[pose.TraversalIndex];
            float routeNodeDistance = loser.Request.Route.TraversalStartDistance(pose.TraversalIndex);
            if (loser.RouteDistance - routeNodeDistance > _maxReverseSearchDistance)
            {
                plan = default;
                return false;
            }

            DeliveryRoadNode node = traversal.Reverse ? traversal.Segment.End : traversal.Segment.Start;
            foreach (DeliveryRoadSegment segment in _network.Segments)
            {
                if (segment == null || segment == traversal.Segment || segment.Other(node) == null) continue;
                if (IsSegmentOccupied(segment)) continue;
                bool reverse = segment.End == node;
                float sideDistance = Mathf.Min(segment.Length * 0.5f, Mathf.Max(loser.HalfLength * 2f, 4f));
                if (!loser.CanUseSideRoad(segment, reverse, sideDistance)) continue;
                plan = new DeliveryYieldPlan(routeNodeDistance, 0f, segment, reverse, sideDistance);
                return true;
            }

            plan = default;
            return false;
        }

        private bool IsSegmentOccupied(DeliveryRoadSegment segment)
        {
            foreach (DeliveryTruck truck in _trucks)
            {
                if (truck != null && truck.Request != null && truck.CurrentRoutePose.Segment == segment) return true;
            }
            return false;
        }

        private void ResumeClearYielders()
        {
            foreach (DeliveryTruck truck in _trucks)
            {
                if (truck == null || truck.State != EDeliveryTruckState.YieldWaiting) continue;
                DeliveryTruck winner = truck.YieldWinner;
                if (winner == null || winner.Request.State != EDeliveryRequestState.Active
                                   || Vector3.Distance(truck.transform.position, winner.transform.position) >= _passClearDistance)
                {
                    truck.ResumeFromYield();
                }
            }
        }

        private static bool CanParticipate(DeliveryTruck truck)
            => truck != null && truck.Request != null
                             && truck.Request.State == EDeliveryRequestState.Active
                             && (truck.State == EDeliveryTruckState.Driving
                                 || truck.State == EDeliveryTruckState.SnowBlocked);
    }
}
