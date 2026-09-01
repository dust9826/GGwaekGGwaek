using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public sealed class GiftDropZone : MonoBehaviour
    {
        [SerializeField] private Vector3 _size = new Vector3(2f, 2f, 2f);
        [SerializeField] private int _capacity = 3;

        private readonly List<GiftEntry> _entries = new List<GiftEntry>();
        private readonly List<Gift> _giftsInZone = new List<Gift>();
        private readonly List<int> _acceptedIndices = new List<int>();
        private readonly List<int> _overflowIndices = new List<int>();

        public int Capacity => _capacity;
        public Vector3 Size => _size;

        public void Configure(Vector3 size, int capacity)
        {
            _size = new Vector3(
                Mathf.Max(0.1f, size.x),
                Mathf.Max(0.1f, size.y),
                Mathf.Max(0.1f, size.z));
            _capacity = Mathf.Max(1, capacity);
        }

        public bool Contains(Vector3 worldPosition)
        {
            Vector3 local = transform.InverseTransformPoint(worldPosition);
            Vector3 half = _size * 0.5f;
            return Mathf.Abs(local.x) <= half.x
                && Mathf.Abs(local.y) <= half.y
                && Mathf.Abs(local.z) <= half.z;
        }

        public void Evaluate(out int acceptedCount, out int acceptedValue)
        {
            Evaluate(null, out acceptedCount, out acceptedValue);
        }

        public void Evaluate(EGiftBoxKind? requiredKind, out int acceptedCount, out int acceptedValue)
        {
            _entries.Clear();
            _giftsInZone.Clear();

            IReadOnlyList<Gift> all = Gift.All;
            for (int index = 0; index < all.Count; index++)
            {
                Gift gift = all[index];
                if (gift == null || !gift.isActiveAndEnabled || gift.IsCarried) continue;
                if (!Contains(gift.transform.position)) continue;
                if (requiredKind.HasValue && gift.Kind != requiredKind.Value) continue;
                _giftsInZone.Add(gift);
                _entries.Add(new GiftEntry(gift.Id, gift.Value));
            }

            GiftAcceptance.Select(_entries, _capacity, _acceptedIndices, _overflowIndices);

            acceptedCount = _acceptedIndices.Count;
            acceptedValue = 0;
            for (int index = 0; index < _acceptedIndices.Count; index++)
                acceptedValue += _entries[_acceptedIndices[index]].Value;

            for (int index = 0; index < _overflowIndices.Count; index++)
            {
                Gift overflowGift = _giftsInZone[_overflowIndices[index]];
                if (overflowGift != null) Destroy(overflowGift.gameObject);
            }
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.2f, 0.9f, 0.4f, 0.6f);
            Gizmos.matrix = transform.localToWorldMatrix;
            Gizmos.DrawWireCube(Vector3.zero, _size);
        }
    }
}
