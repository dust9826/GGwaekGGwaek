using System.Collections.Generic;
using Unity.Mathematics;
using UnityEditor;
using UnityEngine;
using UnityEngine.Splines;

namespace PPack
{
    internal static class TerrainRoadCommands
    {
        private const float LegacySimplificationTolerance = 0.35f;

        [MenuItem("PPack/Level Design/Roads/Capture Current Terrain As Road Baseline")]
        public static void CaptureCurrentTerrainAsRoadBaseline()
        {
            Terrain selected = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponentInParent<Terrain>();
            Terrain[] targets = selected == null ? Terrain.activeTerrains : new[] { selected };
            int captured = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                TerrainRoadBaseline baseline = RoadBuilderAssets.RecaptureTerrainBaseline(targets[i]);
                if (baseline == null) continue;
                captured++;
                if (!AssetDatabase.Contains(baseline)) Object.DestroyImmediate(baseline);
            }

            Debug.Log(
                $"Captured {captured} Terrain road baseline(s). Future rebuilds restore these heights before grading.");
        }

        [MenuItem("PPack/Level Design/Roads/Upgrade Terrain Roads To Splines")]
        public static void UpgradeTerrainRoadsToSplines()
        {
            TerrainRoadPath[] roads = Object.FindObjectsByType<TerrainRoadPath>();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Upgrade Terrain Roads To Splines");
            int upgraded = 0;
            List<Vector3> sampled = new();
            List<Vector3> controls = new();

            for (int i = 0; i < roads.Length; i++)
            {
                TerrainRoadPath road = roads[i];
                if (road == null || road.HasEditableSpline || road.Terrain == null) continue;
                road.GetWorldCenterPoints(sampled);
                if (sampled.Count < 2) continue;
                ReduceControlPoints(sampled, controls);

                Undo.RecordObject(road, "Upgrade Terrain Road To Spline");
                if (road.GetComponent<SplineContainer>() == null)
                    Undo.AddComponent<SplineContainer>(road.gameObject);
                road.Configure(
                    controls,
                    road.Width,
                    road.EdgeFeather,
                    road.BorderWidth,
                    road.BorderFeather,
                    road.Terrain,
                    road.RoadLayer ?? RoadBuilderAssets.GetOrCreateTerrainRoadLayer(),
                    road.BorderLayer ?? RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer());
                EditorUtility.SetDirty(road);
                upgraded++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
            Debug.Log(
                $"Upgraded {upgraded} Terrain road(s) to editable Unity Splines. " +
                "The scene remains unsaved so the migration can be reviewed or undone.");
        }

        [MenuItem("PPack/Level Design/Roads/Convert Legacy Mesh Roads To Terrain")]
        public static void ConvertLegacyMeshRoadsToTerrain()
        {
            RoadPath[] legacyRoads = Object.FindObjectsByType<RoadPath>();
            if (legacyRoads.Length == 0)
            {
                Debug.Log("No legacy mesh roads were found in the loaded scenes.");
                return;
            }

            TerrainLayer roadLayer = RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            TerrainLayer borderLayer = RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            if (roadLayer == null || borderLayer == null) return;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Convert Mesh Roads To Terrain Splines");
            int convertedCount = 0;
            List<Vector3> worldPoints = new();
            List<Vector3> controls = new();

            for (int roadIndex = 0; roadIndex < legacyRoads.Length; roadIndex++)
            {
                RoadPath legacy = legacyRoads[roadIndex];
                if (legacy == null || legacy.LocalCenterPoints.Count < 2) continue;

                worldPoints.Clear();
                for (int pointIndex = 0; pointIndex < legacy.LocalCenterPoints.Count; pointIndex++)
                    worldPoints.Add(legacy.transform.TransformPoint(legacy.LocalCenterPoints[pointIndex]));

                if (!TerrainRoadPainter.TryFindTerrain(worldPoints, out Terrain terrain))
                {
                    Debug.LogWarning($"Skipped {legacy.name}: its center line is not on an active Terrain.", legacy);
                    continue;
                }

                ReduceControlPoints(worldPoints, controls);
                GameObject roadObject = legacy.gameObject;
                TerrainRoadPath terrainRoad = roadObject.GetComponent<TerrainRoadPath>();
                if (terrainRoad == null) terrainRoad = Undo.AddComponent<TerrainRoadPath>(roadObject);
                if (terrainRoad.GetComponent<SplineContainer>() == null)
                    Undo.AddComponent<SplineContainer>(roadObject);
                terrainRoad.Configure(
                    controls,
                    legacy.Width,
                    RoadBuilderPreferences.EdgeFeather,
                    RoadBuilderPreferences.BorderWidth,
                    RoadBuilderPreferences.BorderFeather,
                    terrain,
                    roadLayer,
                    borderLayer);
                EditorUtility.SetDirty(terrainRoad);

                Undo.DestroyObjectImmediate(legacy);
                DestroyComponent<MeshCollider>(roadObject);
                DestroyComponent<MeshRenderer>(roadObject);
                DestroyComponent<MeshFilter>(roadObject);
                convertedCount++;
            }

            RebuildAllLoadedTerrains("Convert Mesh Roads To Terrain Splines");
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
            Debug.Log(
                $"Converted {convertedCount} legacy mesh road(s) to Terrain spline roads. " +
                "The scene remains unsaved so the conversion can be reviewed or undone.");
        }

        [MenuItem("PPack/Level Design/Roads/Rebuild Road Network")]
        [MenuItem("PPack/Level Design/Roads/Rebuild All Terrain Roads")]
        public static void RebuildAllTerrainRoads()
        {
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Rebuild Terrain Road Network");
            int rebuilt = RebuildAllLoadedTerrains("Rebuild Terrain Road Network");
            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
            Debug.Log(
                $"Rebuilt {rebuilt} Terrain road network(s) from their baselines and global spline union masks.");
        }

        private static int RebuildAllLoadedTerrains(string undoName)
        {
            TerrainRoadPath[] allPaths = Object.FindObjectsByType<TerrainRoadPath>();
            Dictionary<Terrain, List<TerrainRoadPath>> groups = new();
            Terrain[] activeTerrains = Terrain.activeTerrains;
            for (int i = 0; i < activeTerrains.Length; i++)
            {
                Terrain terrain = activeTerrains[i];
                if (terrain != null && terrain.terrainData != null)
                    groups[terrain] = new List<TerrainRoadPath>();
            }

            for (int i = 0; i < allPaths.Length; i++)
            {
                TerrainRoadPath path = allPaths[i];
                if (path == null ||
                    path.Terrain == null ||
                    EasyRoadAuthoring.IsEasyRoadSource(path)) continue;
                if (!groups.TryGetValue(path.Terrain, out List<TerrainRoadPath> paths))
                {
                    paths = new List<TerrainRoadPath>();
                    groups.Add(path.Terrain, paths);
                }
                paths.Add(path);
            }

            int rebuilt = 0;
            foreach (KeyValuePair<Terrain, List<TerrainRoadPath>> group in groups)
            {
                if (TerrainRoadNetwork.Rebuild(group.Key, group.Value, undoName)) rebuilt++;
            }
            return rebuilt;
        }

        private static void ReduceControlPoints(
            IReadOnlyList<Vector3> source,
            List<Vector3> destination)
        {
            destination.Clear();
            List<float3> input = new(source.Count);
            for (int i = 0; i < source.Count; i++)
                input.Add(new float3(source[i].x, source[i].y, source[i].z));
            List<float3> reduced = SplineUtility.ReducePoints(input, LegacySimplificationTolerance);
            if (reduced.Count < 2)
            {
                destination.Add(source[0]);
                destination.Add(source[^1]);
                return;
            }

            for (int i = 0; i < reduced.Count; i++)
                destination.Add(new Vector3(reduced[i].x, reduced[i].y, reduced[i].z));
        }

        private static void DestroyComponent<T>(GameObject target) where T : Component
        {
            T component = target.GetComponent<T>();
            if (component != null) Undo.DestroyObjectImmediate(component);
        }
    }
}
