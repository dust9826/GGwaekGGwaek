using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Actions;
using UnityEngine;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/NPC/Group")]
    public sealed class BroadcastNpcGroupSignal : Action
    {
        [SerializeField] private ENpcGroupSignal _signal;

        private NpcGroupMember _member;

        public override void OnAwake()
        {
            base.OnAwake();
            _member = GetComponent<NpcGroupMember>();
        }

        public override TaskStatus OnUpdate()
        {
            if (_member == null || !_member.HasGroup) return TaskStatus.Failure;
            _member.BroadcastGroupSignal(_signal, m_Transform.position);
            return TaskStatus.Success;
        }
    }
}

