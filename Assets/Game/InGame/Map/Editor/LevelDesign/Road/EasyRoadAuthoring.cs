using System;
using System.Collections.Generic;
using System.Linq;
using EasyRoads3Dv3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    internal static class EasyRoadAuthoring
    {
        internal const string SourceRootName = "EditableTerrainRoadNetwork";
        private const string DefaultNetworkName = "EasyRoads3D_LevelDesignNetwork";
        private const float MinimumConnectionOverlap = 0.65f;
        private const float MaximumConnectionOverlap = 1.2f;
        private const float ConnectionOverlapWidthRatio = 0.18f;
        private const float ConnectionAlignmentLength = 2.25f;

        public static TerrainRoadPath CreateRoad(
            IReadOnlyList<Vector3> worldControlPoints,
            float width,
            EasyRoadTemplate template,
            string sourceName,
            Scene scene)
        {
            return CreateRoad(
                worldControlPoints,
                width,
                template,
                sourceName,
                scene,
                null,
                null);
        }

        internal static TerrainRoadPath CreateRoad(
            IReadOnlyList<Vector3> worldControlPoints,
            float width,
            EasyRoadTemplate template,
            string sourceName,
            Scene scene,
            RoadSnapResult? startConnection,
            RoadSnapResult? endConnection)
        {
            if (template == null || template.RoadMaterial == null)
            {
                Debug.LogError("An EasyRoad Template with a road material is required.");
                return null;
            }
            if (worldControlPoints == null || worldControlPoints.Count < 2) return null;
            List<Vector3> connectedControlPoints = BuildConnectedControlPoints(
                worldControlPoints,
                startConnection,
                endConnection);
            if (!TerrainRoadPainter.TryFindTerrain(connectedControlPoints, out Terrain terrain))
            {
                Debug.LogError("EasyRoads road could not be created because the path is not on an active Terrain.");
                return null;
            }

            TerrainLayer roadLayer = RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            TerrainLayer borderLayer = RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            if (roadLayer == null || borderLayer == null) return null;

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create EasyRoads Template Road");

            Transform sourceRoot = GetOrCreateSourceRoot(scene);
            string uniqueName = GetUniqueSourceName(sourceRoot, sourceName);
            GameObject sourceObject = new(uniqueName);
            SceneManager.MoveGameObjectToScene(sourceObject, scene);
            sourceObject.transform.SetParent(sourceRoot, true);
            Undo.RegisterCreatedObjectUndo(sourceObject, "Create EasyRoads Source Spline");

            TerrainRoadPath path = sourceObject.AddComponent<TerrainRoadPath>();
            path.Configure(
                connectedControlPoints,
                width,
                RoadBuilderPreferences.EdgeFeather,
                RoadBuilderPreferences.BorderWidth,
                RoadBuilderPreferences.BorderFeather,
                terrain,
                roadLayer,
                borderLayer);
            EasyRoadSource source = sourceObject.AddComponent<EasyRoadSource>();
            source.Configure(template, "ER_" + uniqueName);

            if (!BuildSingleRoad(path, template, scene, "Create EasyRoads Template Road", true))
            {
                Undo.DestroyObjectImmediate(sourceObject);
                Undo.CollapseUndoOperations(undoGroup);
                return null;
            }

            EditorUtility.SetDirty(path);
            EditorUtility.SetDirty(source);
            EditorSceneManager.MarkSceneDirty(scene);
            Undo.CollapseUndoOperations(undoGroup);
            return path;
        }

        [MenuItem("PPack/Level Design/Roads/Rebuild EasyRoads From Splines")]
        public static void RebuildActiveSceneFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            EasyRoadTemplate fallback = EasyRoadBuilderPreferences.Template;
            int rebuilt = RebuildScene(scene, fallback, "Rebuild EasyRoads From Splines", true);
            Debug.Log($"[EasyRoadTool] Rebuilt {rebuilt} EasyRoads road(s) from editable source splines.");
        }

        [MenuItem("PPack/Level Design/Roads/Validate EasyRoads From Splines")]
        public static void ValidateActiveSceneFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            List<TerrainRoadPath> paths = CollectEasyRoadPaths(scene);
            ERModularBase modularBase = FindSceneModularBase(scene);
            if (paths.Count == 0)
            {
                Debug.LogWarning("[EasyRoadTool] No editable EasyRoads source splines were found in the active scene.");
                return;
            }
            if (modularBase == null)
            {
                Debug.LogError("[EasyRoadTool] EasyRoads3D Road Network is missing from the active scene.");
                return;
            }

            ERRoadNetwork network = new();
            List<string> missing = new();
            for (int i = 0; i < paths.Count; i++)
            {
                string roadName = ResolveRoadName(paths[i]);
                if (network.GetRoadByName(roadName) == null) missing.Add(roadName);
            }

            if (missing.Count > 0)
            {
                Debug.LogError("[EasyRoadTool] Generated roads missing for: " + string.Join(", ", missing));
                return;
            }

            Debug.Log(
                $"[EasyRoadTool] Scene validation passed: sources={paths.Count}, " +
                $"generatedRoads={paths.Count}, network={modularBase.gameObject.name}.");
        }

        internal static bool IsEasyRoadSource(TerrainRoadPath path)
        {
            return path != null && path.GetComponent<EasyRoadSource>() != null;
        }

        [MenuItem("PPack/Level Design/Roads/Repair Existing EasyRoads Connections Without Terrain Changes")]
        public static void RepairExistingConnectionsFromMenu()
        {
            Scene scene = SceneManager.GetActiveScene();
            Transform sourceRoot = FindTransform(scene, SourceRootName);
            ERModularBase modularBase = FindSceneModularBase(scene);
            if (sourceRoot == null || modularBase == null)
            {
                Debug.LogError("[EasyRoadTool] Existing source splines or EasyRoads network were not found.");
                return;
            }

            ERRoadNetwork network = new();
            if (network.GetRoadNetworkStatus() == ERRoadNetworkStatus.BuildMode)
            {
                Debug.LogError("[EasyRoadTool] Restore the EasyRoads network to Edit Mode before repairing connections.");
                return;
            }

            TerrainRoadPath[] paths = sourceRoot
                .GetComponentsInChildren<TerrainRoadPath>(true)
                .Where(path => path.GetComponent<EasyRoadSource>() == null)
                .OrderBy(path => path.name, StringComparer.Ordinal)
                .ToArray();
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Repair Existing EasyRoads Connections");
            Undo.RegisterFullObjectHierarchyUndo(modularBase.gameObject, "Repair Existing EasyRoads Connections");

            int repaired = 0;
            List<Vector3> controls = new();
            for (int i = 0; i < paths.Length; i++)
            {
                TerrainRoadPath path = paths[i];
                if (path.name.StartsWith("00_", StringComparison.Ordinal) ||
                    path.name.StartsWith("10_", StringComparison.Ordinal)) continue;

                path.GetWorldControlPoints(controls);
                if (controls.Count < 2 || path.Terrain == null) continue;
                ERRoad road = network.GetRoadByName("ER_" + path.name);
                if (road == null) continue;

                int markerCount = road.GetMarkerCount();
                if (markerCount != controls.Count && markerCount != controls.Count + 1) continue;

                Vector3 outward = Vector3.ProjectOnPlane(controls[1] - controls[0], Vector3.up).normalized;
                if (outward.sqrMagnitude < 0.0001f) continue;
                Vector3 overlap = controls[0] - outward * (path.Width * 0.55f);
                int originalStartIndex = markerCount == controls.Count ? 0 : 1;
                Vector3 originalStart = road.GetMarkerPosition(originalStartIndex);
                float originalGround = path.Terrain.SampleHeight(controls[0]) +
                                       path.Terrain.transform.position.y;
                float surfaceOffset = originalStart.y - originalGround;
                overlap.y = path.Terrain.SampleHeight(overlap) +
                            path.Terrain.transform.position.y +
                            surfaceOffset;

                if (markerCount == controls.Count) road.InsertMarkerAt(overlap, 0);
                else road.SetMarkerPosition(0, overlap);
                road.SetMarkerControlType(0, ERMarkerControlType.StraightXZ);
                repaired++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
            Debug.Log(
                $"[EasyRoadTool] Repaired {repaired} existing connection(s) without changing Terrain heights or other road markers.");
        }

        [MenuItem("PPack/Level Design/Roads/Refinish Selected EasyRoad Connections")]
        public static void RefinishSelectedConnectionsFromMenu()
        {
            TerrainRoadPath path = Selection.activeGameObject == null
                ? null
                : Selection.activeGameObject.GetComponentInParent<TerrainRoadPath>();
            EasyRoadSource source = path == null ? null : path.GetComponent<EasyRoadSource>();
            if (path == null || source == null)
            {
                Debug.LogWarning("[EasyRoadTool] Select an EasyRoad created by the Road Builder first.");
                return;
            }

            List<Vector3> controls = new();
            path.GetWorldControlPoints(controls);
            if (controls.Count < 2) return;

            RoadSnapResult? startConnection = RoadConnectionUtility.TrySnapToRoad(
                controls[0],
                path,
                out RoadSnapResult start)
                ? start
                : null;
            RoadSnapResult? endConnection = RoadConnectionUtility.TrySnapToRoad(
                controls[^1],
                path,
                out RoadSnapResult end)
                ? end
                : null;
            if (!startConnection.HasValue && !endConnection.HasValue)
            {
                Debug.LogWarning("[EasyRoadTool] Neither endpoint is close enough to an existing road.");
                return;
            }

            EasyRoadTemplate template = source.Template ?? EasyRoadBuilderPreferences.Template;
            if (template == null || template.RoadMaterial == null) return;
            List<Vector3> connected = BuildConnectedControlPoints(
                controls,
                startConnection,
                endConnection);

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Refinish Selected EasyRoad Connections");
            Undo.RegisterFullObjectHierarchyUndo(path.gameObject, "Refinish Selected EasyRoad Connections");
            path.Configure(
                connected,
                path.Width,
                path.EdgeFeather,
                path.BorderWidth,
                path.BorderFeather,
                path.Terrain,
                path.RoadLayer,
                path.BorderLayer);
            if (!BuildSingleRoad(
                    path,
                    template,
                    path.gameObject.scene,
                    "Refinish Selected EasyRoad Connections",
                    true))
            {
                Undo.RevertAllDownToGroup(undoGroup);
                Debug.LogError("[EasyRoadTool] The selected road could not be refinished.");
                return;
            }
            EditorUtility.SetDirty(path);
            Undo.CollapseUndoOperations(undoGroup);
            Debug.Log("[EasyRoadTool] Refinished the selected road endpoints without changing Terrain or target roads.");
        }

        internal static List<Vector3> BuildConnectedControlPoints(
            IReadOnlyList<Vector3> worldControlPoints,
            RoadSnapResult? startConnection,
            RoadSnapResult? endConnection)
        {
            List<Vector3> result = worldControlPoints == null
                ? new List<Vector3>()
                : new List<Vector3>(worldControlPoints);
            if (result.Count < 2) return result;

            if (startConnection.HasValue)
                ApplyConnection(result, true, startConnection.Value);
            if (endConnection.HasValue)
                ApplyConnection(result, false, endConnection.Value);
            return result;
        }

        private static void ApplyConnection(
            List<Vector3> controlPoints,
            bool atStart,
            RoadSnapResult connection)
        {
            if (connection.TargetRoad == null || controlPoints.Count < 2) return;

            int endpointIndex = atStart ? 0 : controlPoints.Count - 1;
            int neighborIndex = atStart ? 1 : controlPoints.Count - 2;
            Vector3 center = connection.Point;
            Vector3 towardOutside = Vector3.ProjectOnPlane(
                controlPoints[neighborIndex] - center,
                Vector3.up);
            if (towardOutside.sqrMagnitude < 0.0001f) return;
            towardOutside.Normalize();

            Vector3 outward;
            Vector3 endpoint;
            float overlap = Mathf.Clamp(
                connection.TargetWidth * ConnectionOverlapWidthRatio,
                MinimumConnectionOverlap,
                MaximumConnectionOverlap);
            if (connection.IsEndpoint)
            {
                outward = Vector3.ProjectOnPlane(connection.Tangent, Vector3.up).normalized;
                if (outward.sqrMagnitude < 0.0001f) outward = towardOutside;
                if (Vector3.Dot(outward, towardOutside) < 0f) outward = -outward;
                endpoint = center - outward * overlap;
            }
            else
            {
                Vector3 tangent = Vector3.ProjectOnPlane(connection.Tangent, Vector3.up).normalized;
                if (tangent.sqrMagnitude < 0.0001f) return;
                outward = Vector3.Cross(Vector3.up, tangent).normalized;
                if (Vector3.Dot(outward, towardOutside) < 0f) outward = -outward;
                float edgeDistance = Mathf.Max(0f, connection.TargetWidth * 0.5f - overlap);
                endpoint = center + outward * edgeDistance;
            }

            endpoint.y = center.y;
            controlPoints[endpointIndex] = endpoint;
            InsertAlignmentPoint(controlPoints, atStart, endpoint, outward);
        }

        private static void InsertAlignmentPoint(
            List<Vector3> controlPoints,
            bool atStart,
            Vector3 endpoint,
            Vector3 outward)
        {
            int neighborIndex = atStart ? 1 : controlPoints.Count - 2;
            Vector3 neighbor = controlPoints[neighborIndex];
            Vector3 neighborOffset = Vector3.ProjectOnPlane(neighbor - endpoint, Vector3.up);
            float availableLength = Vector3.Dot(neighborOffset, outward);
            if (availableLength < 0.65f) return;

            Vector3 lateralOffset = neighborOffset - outward * availableLength;
            if (lateralOffset.sqrMagnitude < 0.01f) return;

            float alignmentLength = Mathf.Min(ConnectionAlignmentLength, availableLength * 0.65f);
            Vector3 aligned = endpoint + outward * alignmentLength;
            aligned.y = Mathf.Lerp(endpoint.y, neighbor.y, alignmentLength / availableLength);
            if (Vector3.Distance(aligned, neighbor) < 0.3f) return;

            if (atStart) controlPoints.Insert(1, aligned);
            else controlPoints.Insert(controlPoints.Count - 1, aligned);
        }

        private static bool BuildSingleRoad(
            TerrainRoadPath path,
            EasyRoadTemplate template,
            Scene scene,
            string undoName,
            bool registerUndo)
        {
            ERModularBase modularBase = FindSceneModularBase(scene);
            bool createdNetwork = modularBase == null;
            ERRoadNetwork network = new();
            modularBase = FindSceneModularBase(scene);
            if (modularBase == null)
            {
                Debug.LogError("[EasyRoadTool] EasyRoads3D did not create its Road Network object.");
                return false;
            }
            if (network.GetRoadNetworkStatus() == ERRoadNetworkStatus.BuildMode)
            {
                Debug.LogError("[EasyRoadTool] Restore the EasyRoads network to Edit Mode before adding a road.");
                return false;
            }

            if (createdNetwork)
            {
                modularBase.gameObject.name = DefaultNetworkName;
                Transform geometry = FindTransform(scene, "Geometry");
                if (geometry != null) modularBase.transform.SetParent(geometry, true);
            }

            if (registerUndo)
            {
                if (createdNetwork)
                    Undo.RegisterCreatedObjectUndo(modularBase.gameObject, undoName);
                else
                    Undo.RegisterFullObjectHierarchyUndo(modularBase.gameObject, undoName);
            }

            List<Vector3> controls = new();
            path.GetWorldControlPoints(controls);
            if (controls.Count < 2 || path.Terrain == null) return false;

            Vector3[] markers = BuildTerrainMarkers(path, template, controls);
            ERRoadType roadType = GetOrCreateRoadType(network, template, false);
            string roadName = ResolveRoadName(path);
            ERRoad road = network.GetRoadByName(roadName);
            if (road != null && road.GetMarkerCount() != markers.Length)
            {
                road.Destroy();
                road = null;
            }

            if (road == null) road = network.CreateRoad(roadName, roadType, markers);
            else
            {
                road.SetRoadType(roadType);
                road.SetMarkerPositions(markers);
            }

            ApplyTemplate(road, path.Width, template);
            if (createdNetwork) network.HideWhiteSurfaces(template.HideWhiteSurfaces);
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
            return true;
        }

        internal static int RebuildScene(
            Scene scene,
            EasyRoadTemplate fallbackTemplate,
            string undoName,
            bool registerUndo)
        {
            if (!scene.IsValid() || !scene.isLoaded || EditorApplication.isPlayingOrWillChangePlaymode)
                return 0;
            if (fallbackTemplate == null || fallbackTemplate.RoadMaterial == null)
            {
                Debug.LogError("[EasyRoadTool] A valid fallback EasyRoad Template is required.");
                return 0;
            }

            List<TerrainRoadPath> paths = CollectEasyRoadPaths(scene);
            if (paths.Count == 0) return 0;

            ERModularBase modularBase = FindSceneModularBase(scene);
            bool createdNetwork = modularBase == null;
            ERRoadNetwork network = new();
            modularBase = FindSceneModularBase(scene);
            if (modularBase == null)
            {
                Debug.LogError("[EasyRoadTool] EasyRoads3D did not create its Road Network object.");
                return 0;
            }
            if (network.GetRoadNetworkStatus() == ERRoadNetworkStatus.BuildMode)
            {
                Debug.LogError("[EasyRoadTool] Restore the EasyRoads network to Edit Mode before rebuilding.");
                return 0;
            }

            if (modularBase.gameObject.name == "Road Network" || modularBase.gameObject.name == "ER Road Network")
                modularBase.gameObject.name = DefaultNetworkName;
            Transform geometry = FindTransform(scene, "Geometry");
            if (geometry != null && modularBase.transform.parent == null)
                modularBase.transform.SetParent(geometry, true);

            if (registerUndo)
            {
                if (createdNetwork)
                    Undo.RegisterCreatedObjectUndo(modularBase.gameObject, undoName);
                else
                    Undo.RegisterFullObjectHierarchyUndo(modularBase.gameObject, undoName);
            }

            Dictionary<EasyRoadTemplate, ERRoadType> roadTypes = new();
            int rebuilt = 0;
            List<Vector3> controls = new();
            for (int i = 0; i < paths.Count; i++)
            {
                TerrainRoadPath path = paths[i];
                EasyRoadTemplate template = ResolveTemplate(path, fallbackTemplate);
                if (template == null || template.RoadMaterial == null) continue;
                if (!roadTypes.TryGetValue(template, out ERRoadType roadType))
                {
                    roadType = GetOrCreateRoadType(network, template);
                    roadTypes.Add(template, roadType);
                }

                path.GetWorldControlPoints(controls);
                if (controls.Count < 2 || path.Terrain == null) continue;
                Vector3[] markers = BuildTerrainMarkers(path, template, controls);

                string roadName = ResolveRoadName(path);
                ERRoad road = network.GetRoadByName(roadName);
                if (road != null && road.GetMarkerCount() != markers.Length)
                {
                    road.Destroy();
                    road = null;
                }

                if (road == null) road = network.CreateRoad(roadName, roadType, markers);
                else
                {
                    road.SetRoadType(roadType);
                    road.SetMarkerPositions(markers);
                }

                ApplyTemplate(road, path.Width, template);
                rebuilt++;
            }

            network.HideWhiteSurfaces(fallbackTemplate.HideWhiteSurfaces);
            EditorSceneManager.MarkSceneDirty(scene);
            SceneView.RepaintAll();
            return rebuilt;
        }

        private static Vector3[] BuildTerrainMarkers(
            TerrainRoadPath path,
            EasyRoadTemplate template,
            IReadOnlyList<Vector3> controls)
        {
            Vector3[] markers = new Vector3[controls.Count];
            for (int markerIndex = 0; markerIndex < controls.Count; markerIndex++)
            {
                Vector3 point = controls[markerIndex];
                point.y = path.Terrain.SampleHeight(point) +
                          path.Terrain.transform.position.y +
                          template.SurfaceOffset;
                markers[markerIndex] = point;
            }
            return markers;
        }

        private static ERRoadType GetOrCreateRoadType(
            ERRoadNetwork network,
            EasyRoadTemplate template,
            bool updateExisting = true)
        {
            ERRoadType roadType = network.GetRoadTypeByName(template.RoadTypeName);
            if (roadType != null && !updateExisting) return roadType;

            roadType ??= new ERRoadType();
            roadType.roadTypeName = template.RoadTypeName;
            roadType.roadWidth = template.DefaultWidth;
            roadType.roadMaterial = template.RoadMaterial;
            roadType.isSideObject = false;
            roadType.layer = 0;
            roadType.tag = "Untagged";
            roadType.Update();
            return roadType;
        }

        private static void ApplyTemplate(ERRoad road, float width, EasyRoadTemplate template)
        {
            road.SetWidth(width);
            road.SetMaterial(template.RoadMaterial);
            road.SetMeshCollider(template.MeshCollider);
            road.SetResolution(template.Resolution);
            road.SetAngleThreshold(template.AngleThreshold);
            road.SetTerrainDeformation(template.TerrainDeformation);
            road.FollowTerrainContours(template.FollowTerrainContours);
            road.FollowTerrainContourThreshold(template.TerrainContourThreshold);
            if (template.SnapToTerrain) road.SnapToTerrain(true, template.SurfaceOffset);
            road.SetSplatmap(false);
            road.SetLayer(0);
            for (int i = 0; i < road.GetMarkerCount(); i++)
            {
                bool endpoint = i == 0 || i == road.GetMarkerCount() - 1;
                road.SetMarkerControlType(
                    i,
                    endpoint ? ERMarkerControlType.StraightXZ : ERMarkerControlType.Spline);
            }
        }

        private static EasyRoadTemplate ResolveTemplate(
            TerrainRoadPath path,
            EasyRoadTemplate fallbackTemplate)
        {
            EasyRoadSource source = path == null ? null : path.GetComponent<EasyRoadSource>();
            return source != null && source.Template != null ? source.Template : fallbackTemplate;
        }

        private static string ResolveRoadName(TerrainRoadPath path)
        {
            EasyRoadSource source = path.GetComponent<EasyRoadSource>();
            return source == null ? "ER_" + path.name : source.GeneratedRoadName;
        }

        private static List<TerrainRoadPath> CollectEasyRoadPaths(Scene scene)
        {
            return scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<TerrainRoadPath>(true))
                .Where(IsEasyRoadSource)
                .OrderBy(path => path.name, StringComparer.Ordinal)
                .ToList();
        }

        private static Transform GetOrCreateSourceRoot(Scene scene)
        {
            Transform existing = FindTransform(scene, SourceRootName);
            if (existing != null) return existing;

            GameObject root = new(SourceRootName);
            SceneManager.MoveGameObjectToScene(root, scene);
            Undo.RegisterCreatedObjectUndo(root, "Create EasyRoads Source Root");
            return root.transform;
        }

        private static string GetUniqueSourceName(Transform root, string requestedName)
        {
            string baseName = string.IsNullOrWhiteSpace(requestedName) ? "EasyRoad" : requestedName;
            if (root.Find(baseName) == null) return baseName;
            for (int index = 2; index < 10000; index++)
            {
                string candidate = baseName + "_" + index.ToString("00");
                if (root.Find(candidate) == null) return candidate;
            }
            return baseName + "_" + Guid.NewGuid().ToString("N")[..6];
        }

        private static ERModularBase FindSceneModularBase(Scene scene)
        {
            return Resources.FindObjectsOfTypeAll<ERModularBase>()
                .FirstOrDefault(component =>
                    component != null &&
                    component.gameObject.scene.IsValid() &&
                    component.gameObject.scene == scene);
        }

        private static Transform FindTransform(Scene scene, string objectName)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                Transform[] transforms = root.GetComponentsInChildren<Transform>(true);
                for (int i = 0; i < transforms.Length; i++)
                {
                    if (transforms[i].name == objectName) return transforms[i];
                }
            }
            return null;
        }
    }
}
