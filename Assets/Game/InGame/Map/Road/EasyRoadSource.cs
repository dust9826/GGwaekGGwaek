using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(TerrainRoadPath))]
    public sealed class EasyRoadSource : MonoBehaviour
    {
        [SerializeField] private EasyRoadTemplate _template;
        [SerializeField] private string _generatedRoadName;

        public EasyRoadTemplate Template => _template;
        public string GeneratedRoadName => string.IsNullOrWhiteSpace(_generatedRoadName)
            ? "ER_" + gameObject.name
            : _generatedRoadName;

        public void Configure(EasyRoadTemplate template, string generatedRoadName)
        {
            _template = template;
            _generatedRoadName = string.IsNullOrWhiteSpace(generatedRoadName)
                ? "ER_" + gameObject.name
                : generatedRoadName;
        }
    }
}
