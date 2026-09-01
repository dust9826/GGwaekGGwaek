using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    [CreateAssetMenu(menuName = "PPack/UI/Vacuum Tool Mode Definition")]
    public sealed class VacuumToolModeDefinition : ScriptableObject
    {
        [SerializeField] private string _id = "tool";
        [SerializeField] private string _displayName = "TOOL";
        [SerializeField] private VisualTreeAsset _iconTemplate;

        public string Id => _id;
        public string DisplayName => _displayName;
        public VisualTreeAsset IconTemplate => _iconTemplate;
    }
}
