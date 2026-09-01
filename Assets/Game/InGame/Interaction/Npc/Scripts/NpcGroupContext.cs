using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public enum ENpcGroupSignal
    {
        Alert,
        Flee,
    }

    public readonly struct NpcGroupSignal
    {
        public ENpcGroupSignal Type { get; }
        public int Sequence { get; }
        public NpcGroupMember Source { get; }
        public Vector3 Position { get; }
        public float SentAtTime { get; }

        public NpcGroupSignal(ENpcGroupSignal type, int sequence, NpcGroupMember source,
            Vector3 position, float sentAtTime)
        {
            Type = type;
            Sequence = sequence;
            Source = source;
            Position = position;
            SentAtTime = sentAtTime;
        }
    }

    /// <summary>
    /// 선택적으로 여러 NPC에 더하는 그룹 속성. 이동 영역과 공유 신호만 소유하며, 멤버의 행동을
    /// 직접 실행하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcGroupContext : MonoBehaviour
    {
        [Tooltip("비우면 이 오브젝트의 위치가 영역 중심이다.")]
        [SerializeField] private Transform _territoryCenter;
        [SerializeField, Min(0.1f)] private float _territoryRadiusM = 8f;

        private readonly List<NpcGroupMember> _members = new List<NpcGroupMember>();
        private readonly NpcGroupSignal?[] _signals =
            new NpcGroupSignal?[Enum.GetValues(typeof(ENpcGroupSignal)).Length];
        private int _nextSignalSequence = 1;

        public IReadOnlyList<NpcGroupMember> Members => _members;
        public int MemberCount => _members.Count;
        public Vector3 TerritoryCenter => _territoryCenter != null
            ? _territoryCenter.position
            : transform.position;
        public float TerritoryRadiusM => _territoryRadiusM;

        internal void Register(NpcGroupMember member)
        {
            if (member == null || _members.Contains(member)) return;
            _members.Add(member);
        }

        internal void Unregister(NpcGroupMember member)
        {
            if (member == null) return;
            _members.Remove(member);
        }

        public bool IsInsideTerritory(Vector3 position, float insetM = 0f)
        {
            float radius = Mathf.Max(0f, _territoryRadiusM - Mathf.Max(0f, insetM));
            Vector3 delta = position - TerritoryCenter;
            delta.y = 0f;
            return delta.sqrMagnitude <= radius * radius;
        }

        public Vector3 GetReturnPoint(Vector3 position, float insetM = 0.5f)
        {
            Vector3 center = TerritoryCenter;
            Vector3 delta = position - center;
            delta.y = 0f;

            float radius = Mathf.Max(0f, _territoryRadiusM - Mathf.Max(0f, insetM));
            if (delta.sqrMagnitude <= radius * radius) return position;
            if (delta.sqrMagnitude < 0.0001f) return center;

            Vector3 point = center + delta.normalized * radius;
            point.y = position.y;
            return point;
        }

        public NpcGroupSignal BroadcastSignal(ENpcGroupSignal type, NpcGroupMember source, Vector3 position)
        {
            int sequence = _nextSignalSequence++;
            if (_nextSignalSequence <= 0) _nextSignalSequence = 1;

            var signal = new NpcGroupSignal(type, sequence, source, position, Time.time);
            _signals[(int)type] = signal;
            return signal;
        }

        public bool TryGetSignal(ENpcGroupSignal type, out NpcGroupSignal signal)
        {
            NpcGroupSignal? stored = _signals[(int)type];
            if (!stored.HasValue)
            {
                signal = default;
                return false;
            }

            signal = stored.Value;
            return true;
        }

        public void ClearSignal(ENpcGroupSignal type)
        {
            _signals[(int)type] = null;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.25f, 0.75f, 1f, 0.65f);
            Gizmos.DrawWireSphere(TerritoryCenter, _territoryRadiusM);
        }
    }
}

