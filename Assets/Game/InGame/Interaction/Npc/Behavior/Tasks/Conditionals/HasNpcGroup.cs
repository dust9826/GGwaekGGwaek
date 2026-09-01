using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Conditionals;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/NPC/Group")]
    public sealed class HasNpcGroup : Conditional
    {
        private NpcGroupMember _member;

        public override void OnAwake()
        {
            base.OnAwake();
            _member = GetComponent<NpcGroupMember>();
        }

        public override TaskStatus OnUpdate()
        {
            return _member != null && _member.HasGroup
                ? TaskStatus.Success
                : TaskStatus.Failure;
        }
    }
}

