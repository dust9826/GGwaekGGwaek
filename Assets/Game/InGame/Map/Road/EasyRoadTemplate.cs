using UnityEngine;

namespace PPack
{
    [CreateAssetMenu(fileName = "EasyRoadTemplate", menuName = "PPack/Level Design/EasyRoad Template")]
    public sealed class EasyRoadTemplate : ScriptableObject
    {
        [Header("EasyRoads3D Road Type")]
        [SerializeField] private string _roadTypeName = "PPack Packed Snow Village Road";
        [SerializeField] private Material _roadMaterial;
        [SerializeField, Min(0.25f)] private float _defaultWidth = 4.8f;
        [SerializeField, Min(0.01f)] private float _surfaceOffset = 0.08f;
        [SerializeField, Min(0.1f)] private float _resolution = 2f;
        [SerializeField, Range(1f, 90f)] private float _angleThreshold = 45f;
        [SerializeField] private bool _meshCollider = true;
        [SerializeField] private bool _followTerrainContours = true;
        [SerializeField, Min(0f)] private float _terrainContourThreshold = 0.2f;
        [SerializeField] private bool _snapToTerrain = true;
        [SerializeField] private bool _terrainDeformation;
        [SerializeField] private bool _hideWhiteSurfaces = true;

        [Header("Terrain Grading")]
        [SerializeField] private bool _gradeTerrain = true;
        [SerializeField, Range(1f, 25f)] private float _maximumGrade = 10.5f;
        [SerializeField, Min(0.5f)] private float _minimumShoulder = 6f;
        [SerializeField, Min(0.5f)] private float _maximumShoulder = 22f;
        [SerializeField, Range(1f, 45f)] private float _maximumSideSlope = 12f;
        [SerializeField, Range(1f, 3f)] private float _sideSlopeSafety = 1.5f;

        public string RoadTypeName => string.IsNullOrWhiteSpace(_roadTypeName)
            ? "PPack EasyRoad"
            : _roadTypeName;
        public Material RoadMaterial => _roadMaterial;
        public float DefaultWidth => _defaultWidth;
        public float SurfaceOffset => _surfaceOffset;
        public float Resolution => _resolution;
        public float AngleThreshold => _angleThreshold;
        public bool MeshCollider => _meshCollider;
        public bool FollowTerrainContours => _followTerrainContours;
        public float TerrainContourThreshold => _terrainContourThreshold;
        public bool SnapToTerrain => _snapToTerrain;
        public bool TerrainDeformation => _terrainDeformation;
        public bool HideWhiteSurfaces => _hideWhiteSurfaces;
        public bool GradeTerrain => _gradeTerrain;
        public float MaximumGrade => _maximumGrade;
        public float MinimumShoulder => _minimumShoulder;
        public float MaximumShoulder => _maximumShoulder;
        public float MaximumSideSlope => _maximumSideSlope;
        public float SideSlopeSafety => _sideSlopeSafety;

        private void OnValidate()
        {
            _defaultWidth = Mathf.Max(0.25f, _defaultWidth);
            _surfaceOffset = Mathf.Max(0.01f, _surfaceOffset);
            _resolution = Mathf.Max(0.1f, _resolution);
            _angleThreshold = Mathf.Clamp(_angleThreshold, 1f, 90f);
            _terrainContourThreshold = Mathf.Max(0f, _terrainContourThreshold);
            _maximumGrade = Mathf.Clamp(_maximumGrade, 1f, 25f);
            _minimumShoulder = Mathf.Max(0.5f, _minimumShoulder);
            _maximumShoulder = Mathf.Max(_minimumShoulder, _maximumShoulder);
            _maximumSideSlope = Mathf.Clamp(_maximumSideSlope, 1f, 45f);
            _sideSlopeSafety = Mathf.Clamp(_sideSlopeSafety, 1f, 3f);
        }
    }
}
