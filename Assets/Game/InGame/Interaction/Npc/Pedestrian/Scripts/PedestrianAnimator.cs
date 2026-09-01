using UnityEngine;
using UnityEngine.AI;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class PedestrianAnimator : MonoBehaviour
    {
        private static readonly int IdleState = Animator.StringToHash("Base Layer.Idle");
        private static readonly int WalkState = Animator.StringToHash("Base Layer.Walk");
        private static readonly int RunState = Animator.StringToHash("Base Layer.Run");
        private static readonly int HitReactionState = Animator.StringToHash("Base Layer.HitReaction");
        private static readonly int AttackState = Animator.StringToHash("Base Layer.Attack");

        [SerializeField] private Animator _animator;
        [SerializeField] private NavMeshAgent _agent;
        [SerializeField] private PedestrianContext _context;

        private int _currentState;

        private void Awake()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_agent == null) _agent = GetComponent<NavMeshAgent>();
            if (_context == null) _context = GetComponent<PedestrianContext>();
            if (_animator == null) return;
            _animator.applyRootMotion = false;
        }

        private void Update()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null || _context == null) return;
            int state = GetDesiredState();
            if (state == _currentState || !_animator.HasState(0, state)) return;
            _currentState = state;
            _animator.CrossFadeInFixedTime(state, 0.12f);
        }

        private int GetDesiredState()
        {
            switch (_context.CurrentAction) {
                case EPedestrianAction.HitReaction:
                    return HitReactionState;
                case EPedestrianAction.Flee:
                    return RunState;
                case EPedestrianAction.Attack:
                    bool approaching = _agent != null && _agent.velocity.sqrMagnitude > 0.04f;
                    return approaching ? RunState : AttackState;
                default:
                    bool moving = _agent != null && _agent.velocity.sqrMagnitude > 0.04f;
                    return moving ? WalkState : IdleState;
            }
        }
    }
}
