using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Actions;
using UnityEngine;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/NPC/Pedestrian")]
    public sealed class RunPedestrianHitReaction : Action
    {
        private PedestrianContext _context;

        public override void OnAwake()
        {
            base.OnAwake();
            _context = GetComponent<PedestrianContext>();
        }

        public override void OnStart()
        {
            base.OnStart();
            _context?.ConsumeImpactAndBeginHitReaction();
        }

        public override TaskStatus OnUpdate()
        {
            if (_context == null || !_context.IsHitReacting) return TaskStatus.Failure;
            if (!_context.IsHitReactionComplete) return TaskStatus.Running;
            return _context.FinishHitReaction() ? TaskStatus.Success : TaskStatus.Failure;
        }
    }

    [Opsive.Shared.Utility.Category("PPack/NPC/Pedestrian")]
    public sealed class RunPedestrianReaction : Action
    {
        [SerializeField] private EPedestrianAction _reaction = EPedestrianAction.Flee;

        private PedestrianContext _context;

        public EPedestrianAction Reaction { get => _reaction; set => _reaction = value; }

        public override void OnAwake()
        {
            base.OnAwake();
            _context = GetComponent<PedestrianContext>();
        }

        public override void OnStart()
        {
            base.OnStart();
            _context?.BeginReaction(Reaction);
        }

        public override TaskStatus OnUpdate()
        {
            if (_context == null || _context.CurrentAction != Reaction) return TaskStatus.Failure;
            if (!_context.IsReactionComplete) return TaskStatus.Running;
            return _context.FinishReaction() ? TaskStatus.Success : TaskStatus.Failure;
        }
    }

    [Opsive.Shared.Utility.Category("PPack/NPC/Pedestrian")]
    public sealed class RunPedestrianNormalBehavior : Action
    {
        private PedestrianContext _context;

        public override void OnAwake()
        {
            base.OnAwake();
            _context = GetComponent<PedestrianContext>();
        }

        public override TaskStatus OnUpdate()
        {
            return _context != null && _context.CurrentAction == EPedestrianAction.Normal
                ? TaskStatus.Running
                : TaskStatus.Failure;
        }
    }
}
