using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Actions;
using Opsive.BehaviorDesigner.Runtime.Tasks.Conditionals;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class IsThiefImpactReacting : Conditional
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => _actor != null && _actor.IsImpactReacting
            ? TaskStatus.Success : TaskStatus.Failure;
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class RunThiefImpactReaction : Action
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => Convert(_actor != null
            ? _actor.TickImpactReaction(UnityEngine.Time.deltaTime) : EThiefTaskResult.Failure);

        private static TaskStatus Convert(EThiefTaskResult result) => result switch
        {
            EThiefTaskResult.Success => TaskStatus.Success,
            EThiefTaskResult.Running => TaskStatus.Running,
            _ => TaskStatus.Failure,
        };
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class IsThiefEscaping : Conditional
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => _actor != null && _actor.IsEscaping
            ? TaskStatus.Success : TaskStatus.Failure;
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class HasThiefGiftClaim : Conditional
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => _actor != null && _actor.HasClaimedGift
            ? TaskStatus.Success : TaskStatus.Failure;
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class RunThiefEscape : Action
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => Convert(_actor != null
            ? _actor.TickEscape(UnityEngine.Time.deltaTime) : EThiefTaskResult.Failure);

        private static TaskStatus Convert(EThiefTaskResult result) => result switch
        {
            EThiefTaskResult.Success => TaskStatus.Success,
            EThiefTaskResult.Running => TaskStatus.Running,
            _ => TaskStatus.Failure,
        };
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class RunThiefSteal : Action
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => Convert(_actor != null
            ? _actor.TickSteal(UnityEngine.Time.deltaTime) : EThiefTaskResult.Failure);

        private static TaskStatus Convert(EThiefTaskResult result) => result switch
        {
            EThiefTaskResult.Success => TaskStatus.Success,
            EThiefTaskResult.Running => TaskStatus.Running,
            _ => TaskStatus.Failure,
        };
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class RunThiefAcquireOrApproach : Action
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => Convert(_actor != null
            ? _actor.TickAcquireOrApproach(UnityEngine.Time.deltaTime) : EThiefTaskResult.Failure);

        private static TaskStatus Convert(EThiefTaskResult result) => result switch
        {
            EThiefTaskResult.Success => TaskStatus.Success,
            EThiefTaskResult.Running => TaskStatus.Running,
            _ => TaskStatus.Failure,
        };
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class IsThiefSpotted : Conditional
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => _actor != null && _actor.IsSpotted
            ? TaskStatus.Success : TaskStatus.Failure;
    }

    [Opsive.Shared.Utility.Category("PPack/Thief")]
    public sealed class RunThiefBeginSpottedRetreat : Action
    {
        private ThiefActor _actor;
        public override void OnAwake() { base.OnAwake(); _actor = GetComponent<ThiefActor>(); }
        public override TaskStatus OnUpdate() => _actor != null && _actor.BeginSpottedRetreat()
            ? TaskStatus.Success : TaskStatus.Failure;
    }
}
