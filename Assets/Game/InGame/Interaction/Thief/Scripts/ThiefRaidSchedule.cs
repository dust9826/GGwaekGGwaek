using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public readonly struct PendingThiefRaid
    {
        public int RequestId { get; }
        public int HouseIndex { get; }
        public DeliveryHouse AssignedHouse { get; }
        public EGiftBoxKind PreferredKind { get; }
        public float DueTime { get; }

        public PendingThiefRaid(int requestId, int houseIndex, DeliveryHouse assignedHouse,
            EGiftBoxKind preferredKind, float dueTime)
        {
            RequestId = requestId;
            HouseIndex = houseIndex;
            AssignedHouse = assignedHouse;
            PreferredKind = preferredKind;
            DueTime = dueTime;
        }
    }

    /// <summary>의뢰 실패와 실제 스폰 사이의 결정론적 지연 큐다.</summary>
    public sealed class ThiefRaidSchedule
    {
        private readonly List<PendingThiefRaid> _pending = new List<PendingThiefRaid>();

        public int Count => _pending.Count;

        public void Enqueue(PendingThiefRaid raid)
        {
            int index = _pending.Count;
            while (index > 0 && _pending[index - 1].DueTime > raid.DueTime) index--;
            _pending.Insert(index, raid);
        }

        public bool TryTakeDue(float now, out PendingThiefRaid raid)
        {
            if (_pending.Count == 0 || _pending[0].DueTime > now)
            {
                raid = default;
                return false;
            }

            raid = _pending[0];
            _pending.RemoveAt(0);
            return true;
        }

        public void Clear() => _pending.Clear();

        public static float SampleDelay(System.Random random, Vector2 range, AnimationCurve distribution)
        {
            float min = Mathf.Max(0f, Mathf.Min(range.x, range.y));
            float max = Mathf.Max(min, Mathf.Max(range.x, range.y));
            float uniform = random != null ? (float)random.NextDouble() : UnityEngine.Random.value;
            float ratio = distribution != null && distribution.length > 0
                ? Mathf.Clamp01(distribution.Evaluate(uniform))
                : uniform;
            return Mathf.Lerp(min, max, ratio);
        }
    }
}
