using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class TerrainRoadNetwork
    {
        public static bool Rebuild(
            Terrain terrain,
            IReadOnlyList<TerrainRoadPath> paths,
            string undoName,
            bool registerUndo = true)
        {
            if (terrain == null || terrain.terrainData == null) return false;

            TerrainLayer roadLayer = RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            TerrainLayer borderLayer = RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            TerrainRoadBaseline baseline = RoadBuilderAssets.GetOrCreateTerrainBaseline(terrain);
            if (roadLayer == null || borderLayer == null || baseline == null) return false;

            bool transientBaseline = !AssetDatabase.Contains(baseline);
            try
            {
                if (registerUndo)
                {
                    Undo.RegisterCompleteObjectUndo(
                        terrain.terrainData,
                        string.IsNullOrEmpty(undoName) ? "Rebuild Terrain Road Network" : undoName);
                }

                bool graded = TerrainRoadGrader.GradeTerrain(
                    terrain,
                    baseline,
                    paths,
                    RoadBuilderPreferences.MaximumGrade,
                    RoadBuilderPreferences.GradingShoulder,
                    undoName,
                    false);
                bool painted = graded && TerrainRoadPainter.RebuildTerrainRoads(
                    terrain,
                    roadLayer,
                    borderLayer,
                    paths,
                    undoName,
                    false);
                return graded && painted;
            }
            finally
            {
                if (transientBaseline && baseline != null)
                    Object.DestroyImmediate(baseline);
            }
        }
    }
}
