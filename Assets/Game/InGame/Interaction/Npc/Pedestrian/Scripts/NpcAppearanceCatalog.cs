using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    [CreateAssetMenu(menuName = "PPack/NPC/Appearance Catalog")]
    public sealed class NpcAppearanceCatalog : ScriptableObject
    {
        [Serializable]
        public struct Entry
        {
            [SerializeField] private ENpcAppearanceSlot _slot;
            [SerializeField] private int _id;
            [SerializeField] private string _clueLabel;
            [SerializeField] private Mesh _mesh;

            public ENpcAppearanceSlot Slot => _slot;
            public int Id => _id;
            public string ClueLabel => _clueLabel;
            public Mesh Mesh => _mesh;

            public Entry(ENpcAppearanceSlot slot, int id, string clueLabel, Mesh mesh)
            {
                _slot = slot;
                _id = id;
                _clueLabel = clueLabel;
                _mesh = mesh;
            }
        }

        [SerializeField] private Entry[] _entries = Array.Empty<Entry>();

        public IReadOnlyList<Entry> Entries => _entries;

        public void GetIds(ENpcAppearanceSlot slot, List<int> results)
        {
            results.Clear();
            foreach (Entry entry in _entries) {
                if (entry.Slot == slot && entry.Id != 0) results.Add(entry.Id);
            }
        }

        public bool TryGet(ENpcAppearanceSlot slot, int id, out Entry result)
        {
            foreach (Entry entry in _entries) {
                if (entry.Slot != slot || entry.Id != id) continue;
                result = entry;
                return true;
            }
            result = default;
            return false;
        }

#if UNITY_EDITOR
        public void SetEntriesForEditor(Entry[] entries)
        {
            _entries = entries ?? Array.Empty<Entry>();
        }
#endif
    }
}
