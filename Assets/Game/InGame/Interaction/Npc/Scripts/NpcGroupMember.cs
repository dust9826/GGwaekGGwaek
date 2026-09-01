using UnityEngine;

namespace PPack
{
    /// <summary>독립 NPC에 선택적으로 그룹 속성을 연결한다.</summary>
    [DisallowMultipleComponent]
    public sealed class NpcGroupMember : MonoBehaviour
    {
        [SerializeField] private NpcGroupContext _group;

        public NpcGroupContext Group => _group;
        public bool HasGroup => _group != null;
        public bool IsOutsideGroupTerritory =>
            _group != null && !_group.IsInsideTerritory(transform.position);

        private void OnEnable()
        {
            if (_group != null) _group.Register(this);
        }

        private void OnDisable()
        {
            if (_group != null) _group.Unregister(this);
        }

        public void SetGroup(NpcGroupContext group)
        {
            if (_group == group) return;
            if (_group != null) _group.Unregister(this);
            _group = group;
            if (isActiveAndEnabled && _group != null) _group.Register(this);
        }

        public Vector3 GetGroupReturnPoint(float insetM = 0.5f)
        {
            return _group != null
                ? _group.GetReturnPoint(transform.position, insetM)
                : transform.position;
        }

        public NpcGroupSignal BroadcastGroupSignal(ENpcGroupSignal type, Vector3 position)
        {
            return _group != null
                ? _group.BroadcastSignal(type, this, position)
                : default;
        }

        public bool TryGetGroupSignal(ENpcGroupSignal type, out NpcGroupSignal signal)
        {
            if (_group != null) return _group.TryGetSignal(type, out signal);
            signal = default;
            return false;
        }
    }
}

