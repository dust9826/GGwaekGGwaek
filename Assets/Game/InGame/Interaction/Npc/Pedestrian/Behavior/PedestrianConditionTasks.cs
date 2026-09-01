using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Conditionals;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/NPC/Pedestrian")]
    public sealed class HasPendingPedestrianImpact : Conditional
    {
        private PedestrianContext _context;

        public override void OnAwake()
        {
            base.OnAwake();
            _context = GetComponent<PedestrianContext>();
        }

        public override TaskStatus OnUpdate()
        {
            return _context != null && _context.HasPendingStrongImpact
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }

    [Opsive.Shared.Utility.Category("PPack/NPC/Pedestrian")]
    public sealed class HasPedestrianIncident : Conditional
    {
        private PedestrianContext _context;

        public override void OnAwake()
        {
            base.OnAwake();
            _context = GetComponent<PedestrianContext>();
        }

        public override TaskStatus OnUpdate()
        {
            return _context != null && _context.HasPendingIncident
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }

    [Opsive.Shared.Utility.Category("PPack/NPC/Pedestrian")]
    public sealed class IsAggressivePedestrian : Conditional
    {
        private PedestrianContext _context;

        public override void OnAwake()
        {
            base.OnAwake();
            _context = GetComponent<PedestrianContext>();
        }

        public override TaskStatus OnUpdate()
        {
            return _context != null && _context.Temperament == ENpcTemperament.Aggressive
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }
}
