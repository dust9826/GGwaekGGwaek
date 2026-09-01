using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    [CreateAssetMenu(menuName = "PPack/UI/Vacuum Tool Mode Catalog")]
    public sealed class VacuumToolModeCatalog : ScriptableObject
    {
        [SerializeField] private List<VacuumToolModeDefinition> _modes = new List<VacuumToolModeDefinition>();

        public int Count => _modes.Count;
        public IReadOnlyList<VacuumToolModeDefinition> Modes => _modes;

        public VacuumToolModeDefinition GetWrapped(int index)
        {
            if (_modes.Count == 0)
            {
                return null;
            }

            int wrappedIndex = ((index % _modes.Count) + _modes.Count) % _modes.Count;
            return _modes[wrappedIndex];
        }

        public int IndexOf(string modeId)
        {
            return _modes.FindIndex(mode => mode != null &&
                string.Equals(mode.Id, modeId, StringComparison.Ordinal));
        }
    }
}
