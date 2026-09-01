using System;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Immutable rebuild origin for road grading. Rebuilds always start here, never from a previous bake.
    /// </summary>
    internal sealed class TerrainRoadBaseline : ScriptableObject
    {
        [SerializeField] private TerrainData _terrainData;
        [SerializeField] private int _heightmapResolution;
        [SerializeField, HideInInspector] private float[] _heights = Array.Empty<float>();

        public TerrainData TerrainData => _terrainData;
        public int HeightmapResolution => _heightmapResolution;

        public bool Matches(TerrainData terrainData)
        {
            return terrainData != null
                   && terrainData == _terrainData
                   && terrainData.heightmapResolution == _heightmapResolution
                   && _heights != null
                   && _heights.Length == _heightmapResolution * _heightmapResolution;
        }

        public void Capture(TerrainData terrainData)
        {
            if (terrainData == null) throw new ArgumentNullException(nameof(terrainData));

            _terrainData = terrainData;
            _heightmapResolution = terrainData.heightmapResolution;
            float[,] source = terrainData.GetHeights(
                0,
                0,
                _heightmapResolution,
                _heightmapResolution);
            _heights = new float[_heightmapResolution * _heightmapResolution];
            for (int z = 0; z < _heightmapResolution; z++)
            {
                for (int x = 0; x < _heightmapResolution; x++)
                    _heights[z * _heightmapResolution + x] = source[z, x];
            }
        }

        public bool TryCopyHeights(out float[,] heights)
        {
            heights = null;
            if (!Matches(_terrainData)) return false;

            heights = new float[_heightmapResolution, _heightmapResolution];
            for (int z = 0; z < _heightmapResolution; z++)
            {
                for (int x = 0; x < _heightmapResolution; x++)
                    heights[z, x] = _heights[z * _heightmapResolution + x];
            }
            return true;
        }
    }
}
