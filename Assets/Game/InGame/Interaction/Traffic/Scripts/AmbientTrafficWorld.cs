using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    [DefaultExecutionOrder(-100)]
    [DisallowMultipleComponent]
    public sealed class AmbientTrafficWorld : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _minimumGapM = 1.5f;
        [SerializeField, Min(0.1f)] private float _timeHeadwaySeconds = 1f;
        [SerializeField, Min(1f)] private float _leaderLookAheadM = 12f;
        [SerializeField, Min(1f)] private float _junctionPredictionM = 14f;
        [SerializeField, Min(0.5f)] private float _junctionStopLineM = 3f;
        [SerializeField, Min(0.5f)] private float _junctionClearanceM = 3f;

        private readonly List<AmbientTrafficVehicle> _vehicles = new();
        private readonly Dictionary<TrafficNode, AmbientTrafficVehicle> _junctionOwners = new();
        private readonly List<TrafficNode> _releasedNodes = new();

        public IReadOnlyList<AmbientTrafficVehicle> Vehicles => _vehicles;

        public void Register(AmbientTrafficVehicle vehicle)
        {
            if (vehicle != null && !_vehicles.Contains(vehicle)) _vehicles.Add(vehicle);
        }

        public void Unregister(AmbientTrafficVehicle vehicle)
        {
            _vehicles.Remove(vehicle);
            _releasedNodes.Clear();
            foreach ((TrafficNode node, AmbientTrafficVehicle owner) in _junctionOwners)
                if (owner == vehicle) _releasedNodes.Add(node);
            foreach (TrafficNode node in _releasedNodes) _junctionOwners.Remove(node);
        }

        private void FixedUpdate()
        {
            _vehicles.RemoveAll(vehicle => vehicle == null || !vehicle.isActiveAndEnabled);
            ReleaseClearedJunctions();
            foreach (AmbientTrafficVehicle vehicle in _vehicles)
                vehicle.SetTrafficTargetSpeed(vehicle.CruiseSpeedMps);
            ResolveFollowing();
            ResolveJunctions();
        }

        private void ResolveFollowing()
        {
            foreach (AmbientTrafficVehicle follower in _vehicles)
            {
                AmbientTrafficVehicle leader = null;
                float bestGap = float.PositiveInfinity;
                foreach (AmbientTrafficVehicle candidate in _vehicles)
                {
                    if (candidate == follower) continue;
                    float gap = DistanceAhead(follower, candidate);
                    if (gap < 0f || gap >= bestGap || gap > _leaderLookAheadM) continue;
                    bestGap = gap;
                    leader = candidate;
                }

                if (leader == null) continue;
                float desiredGap = _minimumGapM + follower.CurrentSpeedMps * _timeHeadwaySeconds;
                float usableGap = bestGap - follower.HalfLength - leader.HalfLength;
                float target = usableGap <= _minimumGapM
                    ? 0f
                    : leader.CurrentSpeedMps
                      + Mathf.Max(0f, usableGap - desiredGap) / _timeHeadwaySeconds;
                follower.SetTrafficTargetSpeed(Mathf.Min(follower.TrafficTargetSpeedMps, target));
            }
        }

        private static float DistanceAhead(AmbientTrafficVehicle follower,
            AmbientTrafficVehicle candidate)
        {
            if (candidate.CurrentLane == follower.CurrentLane
                && candidate.LaneDistance > follower.LaneDistance)
                return candidate.LaneDistance - follower.LaneDistance;

            if (follower.NextLane != null && candidate.CurrentLane == follower.NextLane)
                return follower.CurrentLane.Length - follower.LaneDistance + candidate.LaneDistance;

            return -1f;
        }

        private void ResolveJunctions()
        {
            foreach (AmbientTrafficVehicle vehicle in _vehicles)
                vehicle.SetWaitingForJunction(false);

            foreach (TrafficNode node in JunctionsWithContenders())
            {
                if (!_junctionOwners.TryGetValue(node, out AmbientTrafficVehicle owner))
                {
                    owner = ChooseWinner(node);
                    if (owner != null && IsExitClear(owner)) _junctionOwners.Add(node, owner);
                    else owner = null;
                }

                foreach (AmbientTrafficVehicle contender in _vehicles)
                {
                    if (!IsContender(contender, node) || contender == owner) continue;
                    contender.SetWaitingForJunction(true);
                    float available = contender.CurrentLane.Length - contender.LaneDistance
                                      - _junctionStopLineM - contender.HalfLength;
                    float safeSpeed = available <= 0f
                        ? 0f
                        : Mathf.Sqrt(2f * contender.BrakingMps2 * available);
                    contender.SetTrafficTargetSpeed(Mathf.Min(
                        contender.TrafficTargetSpeedMps, safeSpeed));
                }
            }
        }

        private HashSet<TrafficNode> JunctionsWithContenders()
        {
            var result = new HashSet<TrafficNode>();
            foreach (AmbientTrafficVehicle vehicle in _vehicles)
            {
                TrafficNode node = vehicle.CurrentLane?.To;
                if (node != null && node.IsJunction
                                 && vehicle.NextLane != null
                                 && vehicle.CurrentLane.Length - vehicle.LaneDistance
                                 <= _junctionPredictionM)
                    result.Add(node);
            }
            return result;
        }

        private AmbientTrafficVehicle ChooseWinner(TrafficNode node)
        {
            AmbientTrafficVehicle winner = null;
            float bestDistance = float.PositiveInfinity;
            float bestWait = float.NegativeInfinity;
            foreach (AmbientTrafficVehicle contender in _vehicles)
            {
                if (!IsContender(contender, node)) continue;
                float distance = contender.CurrentLane.Length - contender.LaneDistance;
                if (winner != null && distance > bestDistance + 0.25f) continue;
                if (winner != null && Mathf.Abs(distance - bestDistance) <= 0.25f
                                   && contender.JunctionWaitSeconds < bestWait) continue;
                if (winner != null && Mathf.Abs(distance - bestDistance) <= 0.25f
                                   && Mathf.Approximately(contender.JunctionWaitSeconds, bestWait)
                                   && contender.VehicleId > winner.VehicleId) continue;
                winner = contender;
                bestDistance = distance;
                bestWait = contender.JunctionWaitSeconds;
            }
            return winner;
        }

        private bool IsContender(AmbientTrafficVehicle vehicle, TrafficNode node)
            => vehicle.CurrentLane != null
               && vehicle.CurrentLane.To == node
               && vehicle.NextLane != null
               && vehicle.CurrentLane.Length - vehicle.LaneDistance <= _junctionPredictionM;

        private bool IsExitClear(AmbientTrafficVehicle contender)
        {
            TrafficLane exit = contender.NextLane;
            float required = contender.HalfLength * 2f + _minimumGapM;
            foreach (AmbientTrafficVehicle vehicle in _vehicles)
            {
                if (vehicle == contender || vehicle.CurrentLane != exit) continue;
                if (vehicle.LaneDistance < required) return false;
            }
            return true;
        }

        private void ReleaseClearedJunctions()
        {
            _releasedNodes.Clear();
            foreach ((TrafficNode node, AmbientTrafficVehicle owner) in _junctionOwners)
            {
                if (owner == null || !owner.isActiveAndEnabled
                                  || owner.CurrentLane == null
                                  || owner.CurrentLane.From == node
                                  && owner.LaneDistance >= _junctionClearanceM)
                    _releasedNodes.Add(node);
            }
            foreach (TrafficNode node in _releasedNodes) _junctionOwners.Remove(node);
        }
    }
}
