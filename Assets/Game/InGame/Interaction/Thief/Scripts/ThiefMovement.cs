using UnityEngine;
using UnityEngine.AI;

namespace PPack
{
    public enum EThiefGait
    {
        Idle,
        Walk,
        Crouch,
        Run,
    }

    /// <summary>목적지는 행동 트리가 정하고, 시야 기반 자세와 우회는 이 계층이 겹쳐 적용한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(NavMeshAgent))]
    public sealed class ThiefMovement : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private ThiefPlayerSensor _sensor;
        [SerializeField, Min(0f)] private float _crouchSpeedMps = 0.85f;
        [SerializeField, Min(0f)] private float _runSpeedMps = 4.2f;
        [SerializeField, Min(0f)] private float _avoidanceDistanceM = 3.5f;
        [SerializeField, Min(0.05f)] private float _arrivalDistanceM = 0.8f;
        [SerializeField, Min(0f)] private float _threatMemorySeconds = 4f;

        private Vector3 _primaryDestination;
        private Vector3 _avoidanceWaypoint;
        private bool _hasAvoidanceWaypoint;
        private Vector3 _lastKnownThreatPosition;
        private float _lastKnownThreatTime = float.NegativeInfinity;

        public EThiefGait CurrentGait { get; private set; }
        public float ArrivalDistanceM => _arrivalDistanceM;
        public EThiefAwarenessStage AwarenessStage => _sensor != null ? _sensor.AwarenessStage : EThiefAwarenessStage.Calm;

        private void Awake()
        {
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (_sensor == null) _sensor = GetComponent<ThiefPlayerSensor>();
        }

        public bool MoveTo(Vector3 destination, bool carrying)
        {
            return MoveTo(destination, carrying, _arrivalDistanceM);
        }

        public bool MoveTo(Vector3 destination, bool carrying, float arrivalDistanceM)
        {
            _primaryDestination = destination;
            if (_agent == null || !_agent.isOnNavMesh)
            {
                CurrentGait = EThiefGait.Idle;
                return false;
            }

            Vector3 flat = destination - transform.position;
            flat.y = 0f;
            float arrivalDistance = Mathf.Max(0.01f, arrivalDistanceM);
            if (flat.sqrMagnitude <= arrivalDistance * arrivalDistance)
            {
                Stop();
                return true;
            }

            _sensor?.Refresh();
            Transform threat = _sensor != null ? _sensor.VisiblePlayer : null;
            EThiefAwarenessStage stage = AwarenessStage;
            if (carrying)
            {
                SetGait(EThiefGait.Run);
            }
            else if (stage == EThiefAwarenessStage.Spotted)
            {
                SetGait(EThiefGait.Run);
            }
            else if (stage == EThiefAwarenessStage.Wary)
            {
                SetGait(EThiefGait.Crouch);
            }
            else
            {
                SetGait(EThiefGait.Run);
            }

            if (threat != null && stage == EThiefAwarenessStage.Spotted)
            {
                _lastKnownThreatPosition = threat.position;
                _lastKnownThreatTime = Time.time;
            }
            bool hasThreatMemory = Time.time <= _lastKnownThreatTime + _threatMemorySeconds;

            if (_hasAvoidanceWaypoint &&
                (transform.position - _avoidanceWaypoint).sqrMagnitude <= _arrivalDistanceM * _arrivalDistanceM)
            {
                _hasAvoidanceWaypoint = false;
            }
            if (hasThreatMemory && !_hasAvoidanceWaypoint)
                RefreshAvoidanceWaypoint(destination, _lastKnownThreatPosition);
            if (!hasThreatMemory) _hasAvoidanceWaypoint = false;

            Vector3 actualDestination = _hasAvoidanceWaypoint ? _avoidanceWaypoint : destination;

            _agent.stoppingDistance = arrivalDistance;
            _agent.isStopped = false;
            _agent.SetDestination(actualDestination);
            return false;
        }

        public void Stop()
        {
            if (_agent != null && _agent.isOnNavMesh)
            {
                _agent.isStopped = true;
                _agent.ResetPath();
            }
            CurrentGait = EThiefGait.Idle;
        }

        private void SetGait(EThiefGait gait)
        {
            CurrentGait = gait;
            if (_agent == null) return;
            _agent.speed = gait switch
            {
                EThiefGait.Crouch => _crouchSpeedMps,
                EThiefGait.Idle => 0f,
                _ => _runSpeedMps,
            };
        }

        private void RefreshAvoidanceWaypoint(Vector3 destination, Vector3 threat)
        {
            Vector3 away = transform.position - threat;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f) away = -transform.forward;
            away.Normalize();

            Vector3 towardGoal = destination - transform.position;
            towardGoal.y = 0f;
            Vector3 side = Vector3.Cross(Vector3.up, away).normalized;
            if (Vector3.Dot(side, towardGoal) < 0f) side = -side;
            Vector3 direction = (away * 0.55f + side * 0.85f).normalized;
            Vector3 candidate = transform.position + direction * _avoidanceDistanceM;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas))
            {
                _avoidanceWaypoint = hit.position;
                _hasAvoidanceWaypoint = true;
            }
            else
            {
                _hasAvoidanceWaypoint = false;
            }
        }
    }
}
