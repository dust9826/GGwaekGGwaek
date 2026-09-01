using UnityEngine;
using UnityEngine.AI;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PedestrianContext))]
    public sealed class PedestrianBehaviorExecutor : MonoBehaviour
    {
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField, Min(0f)] private float _wanderRadiusM = 8f;
        [SerializeField, Min(0f)] private float _walkSpeedMps = 1.5f;
        [SerializeField, Min(0f)] private float _runSpeedMps = 3.5f;
        [SerializeField, Min(0f)] private float _hitReactionSeconds = 0.8f;
        [SerializeField, Min(0f)] private float _fleeSeconds = 4f;
        [SerializeField, Min(0f)] private float _attackSeconds = 0.9f;

        private PedestrianContext _context;
        private PedestrianNetworkHub _networkHub;
        private EPedestrianAction _lastAction;
        private Vector3 _home;
        private float _actionElapsed;
        private float _attackElapsed;
        private bool _attackStarted;
        private float _nextWanderTime;

        private void Awake()
        {
            _context = GetComponent<PedestrianContext>();
            _networkHub = GetComponent<PedestrianNetworkHub>();
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            _home = transform.position;
            _lastAction = _context.CurrentAction;
        }

        private void Update()
        {
            if (_networkHub != null && !_networkHub.HasBehaviorAuthority) return;
            EPedestrianAction action = _context.CurrentAction;
            if (action != _lastAction) {
                _lastAction = action;
                _actionElapsed = 0f;
                _attackElapsed = 0f;
                _attackStarted = false;
                EnterAction(action);
            }
            _actionElapsed += Time.deltaTime;

            switch (action) {
                case EPedestrianAction.Normal: UpdateNormal(); break;
                case EPedestrianAction.HitReaction: UpdateHitReaction(); break;
                case EPedestrianAction.Flee: UpdateFlee(); break;
                case EPedestrianAction.Attack: UpdateAttack(); break;
            }
        }

        private void EnterAction(EPedestrianAction action)
        {
            if (_agent == null || !_agent.isOnNavMesh) return;
            _agent.isStopped = action == EPedestrianAction.HitReaction;
            if (action == EPedestrianAction.Normal) _agent.speed = _walkSpeedMps;
            if (action == EPedestrianAction.Flee || action == EPedestrianAction.Attack) {
                _agent.speed = _runSpeedMps;
                SetReactionDestination(action);
            }
        }

        private void UpdateNormal()
        {
            if (_agent == null || !_agent.isOnNavMesh || Time.time < _nextWanderTime) return;
            if (_agent.hasPath && _agent.remainingDistance > _agent.stoppingDistance) return;

            _nextWanderTime = Time.time + Random.Range(1.5f, 4f);
            Vector2 offset = Random.insideUnitCircle * _wanderRadiusM;
            Vector3 candidate = _home + new Vector3(offset.x, 0f, offset.y);
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 2f, NavMesh.AllAreas)) {
                _agent.isStopped = false;
                _agent.speed = _walkSpeedMps;
                _agent.SetDestination(hit.position);
            }
        }

        private void UpdateHitReaction()
        {
            if (_actionElapsed >= _hitReactionSeconds) _context.NotifyHitReactionComplete();
        }

        private void UpdateFlee()
        {
            if (_actionElapsed >= _fleeSeconds) _context.NotifyReactionComplete();
        }

        private void UpdateAttack()
        {
            if (_agent != null && _agent.isOnNavMesh && _agent.hasPath &&
                _agent.remainingDistance > _agent.stoppingDistance) return;
            if (!_attackStarted) {
                _attackStarted = true;
                _attackElapsed = 0f;
                return;
            }
            _attackElapsed += Time.deltaTime;
            if (_attackElapsed >= _attackSeconds) _context.NotifyReactionComplete();
        }

        private void SetReactionDestination(EPedestrianAction action)
        {
            Vector3 fromIncident = transform.position - _context.IncidentPosition;
            if (fromIncident.sqrMagnitude < 0.01f) fromIncident = transform.forward;
            Vector3 candidate = action == EPedestrianAction.Flee
                ? transform.position + fromIncident.normalized * _wanderRadiusM
                : _context.IncidentPosition;
            if (NavMesh.SamplePosition(candidate, out NavMeshHit hit, 3f, NavMesh.AllAreas)) {
                _agent.isStopped = false;
                _agent.SetDestination(hit.position);
            }
        }
    }
}
