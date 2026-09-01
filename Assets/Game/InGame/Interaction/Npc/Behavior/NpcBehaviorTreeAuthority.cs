using Fusion;
using Opsive.BehaviorDesigner.Runtime;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Behavior Designer 트리를 서버 또는 NetworkRunner가 없는 단독 모드에서만 실행한다.
    /// 클라이언트는 NPC가 복제한 결과만 표현한다.
    /// </summary>
    [DefaultExecutionOrder(-1000)]
    [DisallowMultipleComponent]
    public sealed class NpcBehaviorTreeAuthority : NetworkBehaviour
    {
        [SerializeField] private BehaviorTree _behaviorTree;

        private bool _started;

        private void Awake()
        {
            if (_behaviorTree == null) _behaviorTree = GetComponent<BehaviorTree>();
            if (_behaviorTree != null) _behaviorTree.StartWhenEnabled = false;
        }

        private void Start()
        {
            if (NetworkRunner.Instances.Count == 0) StartTree();
        }

        public override void Spawned()
        {
            if (Object.HasStateAuthority) StartTree();
            else StopTree();
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            StopTree();
        }

        private void OnDisable()
        {
            StopTree();
        }

        private void StartTree()
        {
            if (_started || _behaviorTree == null) return;
            if (_behaviorTree.IsRunning())
            {
                _started = true;
                return;
            }
            _started = _behaviorTree.StartBehavior();
        }

        private void StopTree()
        {
            if (_behaviorTree == null) return;
            if (_started || _behaviorTree.IsRunning()) _behaviorTree.StopBehavior();
            _started = false;
        }
    }
}
