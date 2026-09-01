using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    internal static class TerrainRoadAuthoring
    {
        public static TerrainRoadPath CreateRoad(
            IReadOnlyList<Vector3> worldControlPoints,
            float width,
            float edgeFeather,
            string roadName,
            Scene destinationScene,
            string undoName)
        {
            if (!TerrainRoadPainter.TryFindTerrain(worldControlPoints, out Terrain terrain))
            {
                Debug.LogError("Terrain road could not be created because the path is not on an active Terrain.");
                return null;
            }

            TerrainLayer roadLayer = RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            TerrainLayer borderLayer = RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            if (roadLayer == null || borderLayer == null) return null;

            string actionName = string.IsNullOrEmpty(undoName) ? "Create Terrain Road" : undoName;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(actionName);

            GameObject roadObject = new(string.IsNullOrEmpty(roadName) ? "Terrain_Road" : roadName);
            SceneManager.MoveGameObjectToScene(roadObject, destinationScene);
            Undo.RegisterCreatedObjectUndo(roadObject, actionName);
            TerrainRoadPath roadPath = roadObject.AddComponent<TerrainRoadPath>();
            roadPath.Configure(
                worldControlPoints,
                width,
                edgeFeather,
                RoadBuilderPreferences.BorderWidth,
                RoadBuilderPreferences.BorderFeather,
                terrain,
                roadLayer,
                borderLayer);

            List<TerrainRoadPath> terrainPaths = CollectTerrainPaths(terrain);
            if (!TerrainRoadNetwork.Rebuild(terrain, terrainPaths, actionName))
            {
                Undo.DestroyObjectImmediate(roadObject);
                Undo.CollapseUndoOperations(undoGroup);
                return null;
            }

            EditorUtility.SetDirty(roadPath);
            Undo.CollapseUndoOperations(undoGroup);
            return roadPath;
        }

        internal static List<TerrainRoadPath> CollectTerrainPaths(Terrain terrain)
        {
            TerrainRoadPath[] allPaths = Object.FindObjectsByType<TerrainRoadPath>();
            List<TerrainRoadPath> terrainPaths = new();
            for (int i = 0; i < allPaths.Length; i++)
            {
                if (allPaths[i] != null &&
                    allPaths[i].Terrain == terrain &&
                    !EasyRoadAuthoring.IsEasyRoadSource(allPaths[i]))
                {
                    terrainPaths.Add(allPaths[i]);
                }
            }
            return terrainPaths;
        }
    }
}
