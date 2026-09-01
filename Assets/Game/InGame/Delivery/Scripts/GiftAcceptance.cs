using System.Collections.Generic;

namespace PPack
{
    public readonly struct GiftEntry
    {
        public GiftEntry(int id, int value)
        {
            Id = id;
            Value = value;
        }

        public int Id { get; }
        public int Value { get; }
    }

    public static class GiftAcceptance
    {
        public static void Select(IReadOnlyList<GiftEntry> entries, int capacity,
                                  List<int> accepted, List<int> overflow)
        {
            accepted.Clear();
            overflow.Clear();

            var order = new List<int>(entries.Count);
            for (int index = 0; index < entries.Count; index++) order.Add(index);

            order.Sort((a, b) =>
            {
                int valueCompare = entries[b].Value.CompareTo(entries[a].Value);
                if (valueCompare != 0) return valueCompare;
                return entries[a].Id.CompareTo(entries[b].Id);
            });

            for (int rank = 0; rank < order.Count; rank++)
            {
                int index = order[rank];
                if (rank < capacity) accepted.Add(index);
                else overflow.Add(index);
            }
        }
    }
}
