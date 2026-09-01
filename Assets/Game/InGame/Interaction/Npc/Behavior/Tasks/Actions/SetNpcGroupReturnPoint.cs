using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Actions;
using Opsive.GraphDesigner.Runtime.Variables;
using UnityEngine;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/NPC/Group")]
    public sealed class SetNpcGroupReturnPoint : Action
    {
        [SerializeField, Min(0f)] private float _insetM = 0.5f;
        [SerializeField] private SharedVariable<Vector3> _returnPoint;

        private NpcGroupMember _member;

        public override void OnAwake()
        {
            base.OnAwake();
            _member = GetComponent<NpcGroupMember>();
        }

        public override TaskStatus OnUpdate()
        {
            if (_member == null || !_member.HasGroup || _returnPoint == null) return TaskStatus.Failure;
            _returnPoint.Value = _member.GetGroupReturnPoint(_insetM);
            return TaskStatus.Success;
        }
    }
}

