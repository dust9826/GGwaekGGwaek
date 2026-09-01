using Opsive.BehaviorDesigner.Runtime.Tasks;
using Opsive.BehaviorDesigner.Runtime.Tasks.Conditionals;
using Opsive.GraphDesigner.Runtime.Variables;
using UnityEngine;

namespace PPack
{
    [Opsive.Shared.Utility.Category("PPack/NPC/Group")]
    public sealed class HasNpcGroupSignal : Conditional
    {
        [SerializeField] private ENpcGroupSignal _signal;
        [Tooltip("이 시퀀스보다 새 신호만 성공한다. 0이면 해당 종류의 첫 신호부터 받는다.")]
        [SerializeField] private SharedVariable<int> _minimumSequenceExclusive;
        [SerializeField] private SharedVariable<int> _signalSequence;
        [SerializeField] private SharedVariable<Vector3> _signalPosition;

        private NpcGroupMember _member;

        public override void OnAwake()
        {
            base.OnAwake();
            _member = GetComponent<NpcGroupMember>();
        }

        public override TaskStatus OnUpdate()
        {
            if (_member == null || !_member.TryGetGroupSignal(_signal, out NpcGroupSignal signal))
                return TaskStatus.Failure;

            int minimumSequence = _minimumSequenceExclusive != null ? _minimumSequenceExclusive.Value : 0;
            if (signal.Sequence <= minimumSequence) return TaskStatus.Failure;

            if (_signalSequence != null) _signalSequence.Value = signal.Sequence;
            if (_signalPosition != null) _signalPosition.Value = signal.Position;
            return TaskStatus.Success;
        }
    }
}

