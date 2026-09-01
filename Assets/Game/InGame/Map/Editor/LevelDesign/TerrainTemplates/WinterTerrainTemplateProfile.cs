using UnityEngine;

namespace PPack
{
    public enum EWinterTerrainTemplateShape
    {
        VillageBasin,
        AlpineHillside,
        SkiSlope
    }

    [CreateAssetMenu(
        fileName = "WinterTerrainTemplate",
        menuName = "PPack/Level Design/Winter Terrain Template")]
    public sealed class WinterTerrainTemplateProfile : ScriptableObject
    {
        [SerializeField] private EWinterTerrainTemplateShape _shape;
        [SerializeField] private Vector3 _size = new(160f, 40f, 160f);
        [SerializeField] private int _heightmapResolution = 1025;
        [SerializeField] private int _alphamapResolution = 1024;
        [SerializeField] private int _baseMapResolution = 1024;
        [SerializeField] private int _seed = 1207;
        [SerializeField, Range(0f, 1f)] private float _baseHeight = 0.08f;
        [SerializeField, Range(0.05f, 0.9f)] private float _relief = 0.42f;

        public EWinterTerrainTemplateShape Shape => _shape;
        public Vector3 Size => _size;
        public int HeightmapResolution => _heightmapResolution;
        public int AlphamapResolution => _alphamapResolution;
        public int BaseMapResolution => _baseMapResolution;
        public int Seed => _seed;
        public float BaseHeight => _baseHeight;
        public float Relief => _relief;

        internal void Configure(
            EWinterTerrainTemplateShape shape,
            Vector3 size,
            int heightmapResolution,
            int alphamapResolution,
            int baseMapResolution,
            int seed,
            float baseHeight,
            float relief)
        {
            _shape = shape;
            _size = size;
            _heightmapResolution = heightmapResolution;
            _alphamapResolution = alphamapResolution;
            _baseMapResolution = baseMapResolution;
            _seed = seed;
            _baseHeight = baseHeight;
            _relief = relief;
        }

        private void OnValidate()
        {
            _size.x = Mathf.Max(32f, _size.x);
            _size.y = Mathf.Max(8f, _size.y);
            _size.z = Mathf.Max(32f, _size.z);
            _heightmapResolution = ClosestHeightmapResolution(_heightmapResolution);
            _alphamapResolution = ClosestTextureResolution(_alphamapResolution);
            _baseMapResolution = ClosestTextureResolution(_baseMapResolution);
        }

        private static int ClosestHeightmapResolution(int value)
        {
            int[] resolutions = { 129, 257, 513, 1025, 2049, 4097 };
            int closest = resolutions[0];
            for (int i = 1; i < resolutions.Length; i++)
            {
                if (Mathf.Abs(resolutions[i] - value) >= Mathf.Abs(closest - value)) continue;
                closest = resolutions[i];
            }
            return closest;
        }

        private static int ClosestTextureResolution(int value)
        {
            return Mathf.ClosestPowerOfTwo(Mathf.Clamp(value, 16, 2048));
        }
    }
}
