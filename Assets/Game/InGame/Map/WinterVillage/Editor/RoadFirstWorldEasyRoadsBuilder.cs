using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using EasyRoads3Dv3;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack.InGame.Editor
{
    internal static class RoadFirstWorldEasyRoadsBuilder
    {
        private const string ScenePath =
            "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_RoadFirstWorld.unity";
        private const string SourceRootName = "EditableTerrainRoadNetwork";
        private const string NetworkName = "EasyRoads3D_RoadFirstNetwork";
        private const string AdjustmentRootName = "PPack_JunctionAdjustments";
        private const string MarkerName = "__RoadFirstEasyRoadsPro_v1";
        private const string GeneratedRoot =
            "Assets/Game/InGame/Map/WinterVillage/Generated/RoadFirstWorld/EasyRoads";
        private const string MaterialRoot =
            "Assets/Game/InGame/Map/WinterVillage/Materials/RoadFirstWorld";
        private const string TerrainBackupPath =
            GeneratedRoot + "/TD_RoadFirstWinterWorld_PreEasyRoads.asset";
        private const string RoadMaterialPath =
            MaterialRoot + "/M_RoadFirst_EasyRoadsPackedSnow.mat";
        private const string CapMaterialPath =
            MaterialRoot + "/M_RoadFirst_EasyRoadsEndCap.mat";
        private const string SourceRoadMaterialPath =
            "Assets/Game/InGame/Map/Road/Materials/M_LevelDesignWinterRoad.mat";
        private const string BuildMenuPath =
            "PPack/Map/Road First World/Rebuild Roads With EasyRoads3D Pro";
        private const string ValidateMenuPath =
            "PPack/Map/Road First World/Validate EasyRoads3D Roads";
        private const string AuditMenuPath =
            "PPack/Map/Road First World/Audit EasyRoads Terrain Profiles";

        private const float RoadSurfaceOffset = 0.08f;
        private const float CapSurfaceOffset = 0.09f;
        private const float TerrainDesignSurfaceOffset = 0.035f;
        private const float RoadsideMinimumBlendWidth = 6f;
        private const float RoadsideMaximumBlendWidth = 44f;
        private const float RoadsideBlendSlopeDegrees = 8f;
        private const float RoadsideBankAuditStep = 2f;
        private const float RoadsideBankAuditDistance = 12f;
        private const float JunctionHeightBlendLength = 12f;
        private const int CapRings = 16;
        private const int EndCapSegments = 32;

        [MenuItem(BuildMenuPath)]
        public static void Rebuild()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding Road First World roads.");

            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject sourceRoot = GameObject.Find(SourceRootName);
            GameObject geometry = GameObject.Find("Geometry");
            if (terrain == null || sourceRoot == null || geometry == null)
                throw new InvalidOperationException("Road First World terrain, Geometry, or source road root was not found.");

            TerrainRoadPath[] sourcePaths = sourceRoot.GetComponentsInChildren<TerrainRoadPath>(true)
                .OrderBy(path => path.name, StringComparer.Ordinal)
                .ToArray();
            if (sourcePaths.Length == 0)
                throw new InvalidOperationException("No TerrainRoadPath source splines were found.");

            EnsureFolder(GeneratedRoot);
            EnsureFolder(MaterialRoot);
            BackupTerrainData(terrain.terrainData);
            RestoreTerrainHeightsFromBackup(terrain.terrainData);
            ConformTerrainToRoadProfiles(terrain, sourcePaths);
            RemoveExistingEasyRoadsNetwork(scene);

            Material roadMaterial = GetOrCreateRoadMaterial();
            Material capMaterial = GetOrCreateCapMaterial(roadMaterial);
            ClearLegacyRoadPaint(terrain.terrainData);

            ERRoadNetwork network = new ERRoadNetwork();
            ERModularBase modularBase = FindSceneModularBase(scene);
            if (modularBase == null)
                throw new InvalidOperationException("EasyRoads3D did not create its Road Network object.");

            GameObject networkRoot = modularBase.gameObject;
            networkRoot.name = NetworkName;
            networkRoot.transform.SetParent(geometry.transform, true);
            networkRoot.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);
            networkRoot.transform.localScale = Vector3.one;

            ERRoadType roadType = new ERRoadType
            {
                roadTypeName = "PPack Packed Snow Village Road",
                roadWidth = sourcePaths.Max(path => path.Width),
                roadMaterial = roadMaterial,
                layer = 0,
                tag = "Untagged"
            };
            roadType.Update();

            var controlPoints = new List<Vector3>();
            var builtRoads = new List<ERRoad>(sourcePaths.Length);
            for (int sourceIndex = 0; sourceIndex < sourcePaths.Length; sourceIndex++)
            {
                TerrainRoadPath sourcePath = sourcePaths[sourceIndex];
                sourcePath.GetWorldControlPoints(controlPoints);
                float surfaceOffset = RoadSurfaceOffset + (sourceIndex > 0 ? 0.004f : 0f);
                var markerList = controlPoints
                    .Select(point => GroundPoint(terrain, point, surfaceOffset))
                    .ToList();
                if (sourceIndex > 0 &&
                    !sourcePath.name.StartsWith("10_", StringComparison.Ordinal) &&
                    markerList.Count >= 2)
                {
                    Vector3 inward = GetHorizontalDirection(markerList[1] - markerList[0]);
                    Vector3 overlapPoint = markerList[0] - inward * (sourcePath.Width * 0.55f);
                    markerList.Insert(0, GroundPoint(terrain, overlapPoint, surfaceOffset));
                }
                Vector3[] markers = markerList.ToArray();
                if (markers.Length < 2) continue;

                ERRoad road = network.CreateRoad("ER_" + sourcePath.name, roadType, markers);
                road.SetWidth(sourcePath.Width);
                road.SetMaterial(roadMaterial);
                road.SetMeshCollider(true);
                road.SetTerrainDeformation(false);
                road.FollowTerrainContours(true);
                road.FollowTerrainContourThreshold(0.2f);
                road.SnapToTerrain(true, surfaceOffset);
                road.SetLayer(0);
                for (int markerIndex = 0; markerIndex < road.GetMarkerCount(); markerIndex++)
                    road.SetMarkerControlType(markerIndex, ERMarkerControlType.Spline);
                builtRoads.Add(road);
            }

            network.HideWhiteSurfaces(true);
            BuildJunctionAdjustments(
                networkRoot.transform,
                sourcePaths,
                terrain,
                capMaterial);

            GameObject marker = new GameObject(MarkerName) { tag = "EditorOnly" };
            marker.transform.SetParent(networkRoot.transform, false);

            EditorUtility.SetDirty(terrain.terrainData);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            ValidateInternal(scene, terrain, network, sourcePaths.Length, true);
            Selection.activeGameObject = networkRoot;
            Debug.Log(
                "[RoadFirstEasyRoads] Rebuilt " + builtRoads.Count +
                " roads from the retained TerrainRoadPath splines. EasyRoads now owns visible road meshes and colliders; " +
                "legacy Terrain road paint was returned to packed snow.");
        }

        [MenuItem(ValidateMenuPath)]
        public static void Validate()
        {
            Scene scene = RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject sourceRoot = GameObject.Find(SourceRootName);
            if (terrain == null || sourceRoot == null)
                throw new InvalidOperationException("Road First World terrain or source road root was not found.");

            TerrainRoadPath[] sourcePaths = sourceRoot.GetComponentsInChildren<TerrainRoadPath>(true);
            ERRoadNetwork network = new ERRoadNetwork();
            ValidateInternal(scene, terrain, network, sourcePaths.Length, true);
        }

        [MenuItem(AuditMenuPath)]
        public static void AuditTerrainProfiles()
        {
            RequireRoadFirstScene();
            Terrain terrain = Terrain.activeTerrain;
            GameObject sourceRoot = GameObject.Find(SourceRootName);
            if (terrain == null || sourceRoot == null)
                throw new InvalidOperationException("Road First World terrain or source road root was not found.");

            TerrainRoadPath[] sourcePaths = sourceRoot.GetComponentsInChildren<TerrainRoadPath>(true)
                .OrderBy(path => path.name, StringComparer.Ordinal)
                .ToArray();
            ProfileMetrics metrics = AnalyzeTerrainProfiles(sourcePaths, terrain, true);
            Debug.Log(
                "[RoadFirstEasyRoadsProfile] Network maximums: grade=" + metrics.MaximumGrade.ToString("F2") +
                "deg, crossfall=" + metrics.MaximumCrossfall.ToString("F2") +
                "deg, localRoughness=" + metrics.MaximumRoughness.ToString("F3") +
                "m, bilateralDepression=" + metrics.MaximumDepression.ToString("F3") +
                "m, roadsideBank=" + metrics.MaximumRoadsideBank.ToString("F2") + "deg.");
        }

        private static void ValidateInternal(
            Scene scene,
            Terrain terrain,
            ERRoadNetwork network,
            int expectedRoadCount,
            bool throwOnFailure)
        {
            var failures = new List<string>();
            ERModularBase modularBase = FindSceneModularBase(scene);
            ERRoad[] roads = network.GetRoadObjects() ?? Array.Empty<ERRoad>();
            if (modularBase == null || modularBase.gameObject.name != NetworkName)
                failures.Add("EasyRoads network root is missing or misnamed.");
            if (roads.Length != expectedRoadCount)
                failures.Add("Expected " + expectedRoadCount + " EasyRoads roads, found " + roads.Length + ".");

            int meshCount = 0;
            int colliderCount = 0;
            float maximumGroundGap = 0f;
            if (modularBase != null)
            {
                meshCount = modularBase.GetComponentsInChildren<MeshFilter>(true)
                    .Count(filter => filter.sharedMesh != null && filter.sharedMesh.vertexCount > 0);
                colliderCount = modularBase.GetComponentsInChildren<MeshCollider>(true)
                    .Count(collider => collider.sharedMesh != null);
            }

            if (meshCount < expectedRoadCount)
                failures.Add("Only " + meshCount + " generated meshes were found.");
            if (colliderCount < expectedRoadCount)
                failures.Add("Only " + colliderCount + " generated mesh colliders were found.");

            Transform roadObjects = modularBase == null
                ? null
                : modularBase.GetComponentsInChildren<Transform>(true)
                    .FirstOrDefault(child => child.name == "Road Objects");
            if (roadObjects != null)
            {
                foreach (MeshFilter filter in roadObjects.GetComponentsInChildren<MeshFilter>(true))
                {
                    Mesh mesh = filter.sharedMesh;
                    if (mesh == null || mesh.vertexCount == 0) continue;

                    Vector3[] vertices = mesh.vertices;
                    int stride = Mathf.Max(1, vertices.Length / 512);
                    for (int i = 0; i < vertices.Length; i += stride)
                    {
                        Vector3 world = filter.transform.TransformPoint(vertices[i]);
                        float ground = terrain.SampleHeight(world) + terrain.transform.position.y + RoadSurfaceOffset;
                        maximumGroundGap = Mathf.Max(maximumGroundGap, Mathf.Abs(world.y - ground));
                    }
                }
            }

            if (maximumGroundGap > 0.14f)
                failures.Add("Maximum rendered-road-to-terrain gap is " + maximumGroundGap.ToString("F3") + " m.");

            int missingScripts = 0;
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            if (missingScripts > 0)
                failures.Add("Scene contains " + missingScripts + " missing scripts.");

            if (GameObject.Find(MarkerName) == null)
                failures.Add("Conversion marker is missing.");

            GameObject sourceRoot = GameObject.Find(SourceRootName);
            if (sourceRoot != null)
            {
                TerrainRoadPath[] sourcePaths = sourceRoot.GetComponentsInChildren<TerrainRoadPath>(true);
                ProfileMetrics profiles = AnalyzeTerrainProfiles(sourcePaths, terrain, false);
                if (profiles.MaximumGrade > 13f)
                    failures.Add("Longitudinal road grade reaches " + profiles.MaximumGrade.ToString("F2") + " degrees.");
                if (profiles.MaximumCrossfall > 6f)
                    failures.Add("Road crossfall reaches " + profiles.MaximumCrossfall.ToString("F2") + " degrees.");
                if (profiles.MaximumRoughness > 0.12f)
                    failures.Add("Local road roughness reaches " + profiles.MaximumRoughness.ToString("F3") + " m.");
                if (profiles.MaximumDepression > 0.45f)
                    failures.Add("Road is depressed below both shoulders by " + profiles.MaximumDepression.ToString("F3") + " m.");
                if (profiles.MaximumRoadsideBank > 30f)
                    failures.Add("Roadside terrain bank reaches " + profiles.MaximumRoadsideBank.ToString("F2") + " degrees.");
            }

            if (failures.Count > 0)
            {
                string message = "[RoadFirstEasyRoads] Validation failed: " + string.Join(" ", failures);
                if (throwOnFailure) throw new InvalidOperationException(message);
                Debug.LogError(message);
                return;
            }

            Debug.Log(
                "[RoadFirstEasyRoads] Validation passed: roads=" + roads.Length +
                ", meshes=" + meshCount +
                ", meshColliders=" + colliderCount +
                ", maxRenderedGroundGap=" + maximumGroundGap.ToString("F3") + "m, missingScripts=0.");
        }

        private static ProfileMetrics AnalyzeTerrainProfiles(
            IReadOnlyList<TerrainRoadPath> sourcePaths,
            Terrain terrain,
            bool logPerRoad)
        {
            ProfileMetrics network = default;
            var samples = new List<Vector3>();
            for (int pathIndex = 0; pathIndex < sourcePaths.Count; pathIndex++)
            {
                TerrainRoadPath path = sourcePaths[pathIndex];
                path.GetWorldCenterPoints(samples, 0.5f);
                if (samples.Count < 2) continue;

                for (int i = 0; i < samples.Count; i++)
                {
                    Vector3 point = samples[i];
                    point.y = terrain.SampleHeight(point) + terrain.transform.position.y;
                    samples[i] = point;
                }

                ProfileMetrics road = default;
                for (int i = 1; i < samples.Count; i++)
                {
                    float planarDistance = Vector2.Distance(
                        new Vector2(samples[i - 1].x, samples[i - 1].z),
                        new Vector2(samples[i].x, samples[i].z));
                    if (planarDistance <= 0.001f) continue;
                    float grade = Mathf.Atan2(
                        Mathf.Abs(samples[i].y - samples[i - 1].y),
                        planarDistance) * Mathf.Rad2Deg;
                    if (grade > road.MaximumGrade)
                    {
                        road.MaximumGrade = grade;
                        road.GradePosition = samples[i];
                    }
                }

                for (int i = 1; i < samples.Count - 1; i++)
                {
                    float expectedHeight = (samples[i - 1].y + samples[i + 1].y) * 0.5f;
                    float roughness = Mathf.Abs(samples[i].y - expectedHeight);
                    if (roughness > road.MaximumRoughness)
                    {
                        road.MaximumRoughness = roughness;
                        road.RoughnessPosition = samples[i];
                    }

                    Vector3 tangent = samples[i + 1] - samples[i - 1];
                    tangent.y = 0f;
                    if (tangent.sqrMagnitude < 0.001f) continue;
                    tangent.Normalize();
                    Vector3 perpendicular = new(-tangent.z, 0f, tangent.x);

                    float interiorDistance = path.Width * 0.45f;
                    Vector3 left = samples[i] + perpendicular * interiorDistance;
                    Vector3 right = samples[i] - perpendicular * interiorDistance;
                    left.y = terrain.SampleHeight(left) + terrain.transform.position.y;
                    right.y = terrain.SampleHeight(right) + terrain.transform.position.y;
                    float crossfall = Mathf.Atan2(
                        Mathf.Abs(left.y - right.y),
                        interiorDistance * 2f) * Mathf.Rad2Deg;
                    if (crossfall > road.MaximumCrossfall)
                    {
                        road.MaximumCrossfall = crossfall;
                        road.CrossfallPosition = samples[i];
                    }

                    float shoulderDistance = path.Width * 0.5f + 1.2f;
                    left = samples[i] + perpendicular * shoulderDistance;
                    right = samples[i] - perpendicular * shoulderDistance;
                    left.y = terrain.SampleHeight(left) + terrain.transform.position.y;
                    right.y = terrain.SampleHeight(right) + terrain.transform.position.y;
                    float depression = Mathf.Min(left.y - samples[i].y, right.y - samples[i].y);
                    if (depression > road.MaximumDepression)
                    {
                        road.MaximumDepression = depression;
                        road.DepressionPosition = samples[i];
                    }

                    MeasureRoadsideBank(samples[i], perpendicular, path.Width, terrain, ref road);
                }

                network.MaximumGrade = Mathf.Max(network.MaximumGrade, road.MaximumGrade);
                network.MaximumCrossfall = Mathf.Max(network.MaximumCrossfall, road.MaximumCrossfall);
                network.MaximumRoughness = Mathf.Max(network.MaximumRoughness, road.MaximumRoughness);
                network.MaximumDepression = Mathf.Max(network.MaximumDepression, road.MaximumDepression);
                network.MaximumRoadsideBank = Mathf.Max(network.MaximumRoadsideBank, road.MaximumRoadsideBank);

                if (logPerRoad)
                {
                    Debug.Log(
                        "[RoadFirstEasyRoadsProfile] " + path.name +
                        ": grade=" + road.MaximumGrade.ToString("F2") +
                        "deg@" + FormatXZ(road.GradePosition) +
                        ", crossfall=" + road.MaximumCrossfall.ToString("F2") +
                        "deg@" + FormatXZ(road.CrossfallPosition) +
                        ", roughness=" + road.MaximumRoughness.ToString("F3") +
                        "m@" + FormatXZ(road.RoughnessPosition) +
                        ", depression=" + road.MaximumDepression.ToString("F3") +
                        "m@" + FormatXZ(road.DepressionPosition) +
                        ", roadsideBank=" + road.MaximumRoadsideBank.ToString("F2") +
                        "deg@" + FormatXZ(road.RoadsideBankPosition) + ".");
                }
            }

            return network;
        }

        private static void MeasureRoadsideBank(
            Vector3 center,
            Vector3 perpendicular,
            float roadWidth,
            Terrain terrain,
            ref ProfileMetrics metrics)
        {
            float halfWidth = roadWidth * 0.5f;
            for (int side = -1; side <= 1; side += 2)
            {
                Vector3 previous = center + perpendicular * (halfWidth * side);
                previous.y = terrain.SampleHeight(previous) + terrain.transform.position.y;
                for (float offset = RoadsideBankAuditStep;
                     offset <= RoadsideBankAuditDistance;
                     offset += RoadsideBankAuditStep)
                {
                    Vector3 current = center + perpendicular * ((halfWidth + offset) * side);
                    current.y = terrain.SampleHeight(current) + terrain.transform.position.y;
                    float bank = Mathf.Atan2(
                        Mathf.Abs(current.y - previous.y),
                        RoadsideBankAuditStep) * Mathf.Rad2Deg;
                    if (bank > metrics.MaximumRoadsideBank)
                    {
                        metrics.MaximumRoadsideBank = bank;
                        metrics.RoadsideBankPosition = current;
                    }
                    previous = current;
                }
            }
        }

        private struct ProfileMetrics
        {
            public float MaximumGrade;
            public float MaximumCrossfall;
            public float MaximumRoughness;
            public float MaximumDepression;
            public float MaximumRoadsideBank;
            public Vector3 GradePosition;
            public Vector3 CrossfallPosition;
            public Vector3 RoughnessPosition;
            public Vector3 DepressionPosition;
            public Vector3 RoadsideBankPosition;
        }

        private static string FormatXZ(Vector3 point)
        {
            return "(" + point.x.ToString("F1") + "," + point.z.ToString("F1") + ")";
        }

        private static void ConformTerrainToRoadProfiles(
            Terrain terrain,
            IReadOnlyList<TerrainRoadPath> sourcePaths)
        {
            TerrainData data = terrain.terrainData;
            int resolution = data.heightmapResolution;
            float[,] original = data.GetHeights(0, 0, resolution, resolution);
            float[,] influence = new float[resolution, resolution];
            float[,] targetSum = new float[resolution, resolution];
            float[,] targetWeight = new float[resolution, resolution];
            float[,] surfaceTargetSum = new float[resolution, resolution];
            float[,] surfaceTargetWeight = new float[resolution, resolution];
            Vector3 origin = terrain.transform.position;
            Vector3 size = data.size;
            var sampled = new List<Vector3>();
            var designed = new List<Vector3>();
            var mainProfile = new List<Vector3>();

            TerrainRoadPath mainPath = sourcePaths.FirstOrDefault(
                path => path.name.StartsWith("00_", StringComparison.Ordinal));
            if (mainPath != null)
            {
                mainPath.GetWorldCenterPoints(sampled, 0.5f);
                if (sampled.Count >= 2)
                    BuildGradeLimitedCenterLine(sampled, 10.5f, mainProfile);
            }

            foreach (TerrainRoadPath path in sourcePaths)
            {
                path.GetWorldCenterPoints(sampled, 0.5f);
                if (sampled.Count < 2) continue;
                BuildGradeLimitedCenterLine(sampled, 10.5f, designed);
                if (path != mainPath && mainProfile.Count >= 2)
                    AlignConnectedEndsToMainProfile(designed, mainProfile, path.Width);
                float halfWidth = path.Width * 0.5f;
                float radius = halfWidth + RoadsideMaximumBlendWidth;

                for (int segment = 0; segment < designed.Count - 1; segment++)
                {
                    Vector3 start = designed[segment];
                    Vector3 end = designed[segment + 1];
                    int xMin = WorldToHeightCoordinate(
                        Mathf.Min(start.x, end.x) - radius, origin.x, size.x, resolution, true);
                    int xMax = WorldToHeightCoordinate(
                        Mathf.Max(start.x, end.x) + radius, origin.x, size.x, resolution, false);
                    int zMin = WorldToHeightCoordinate(
                        Mathf.Min(start.z, end.z) - radius, origin.z, size.z, resolution, true);
                    int zMax = WorldToHeightCoordinate(
                        Mathf.Max(start.z, end.z) + radius, origin.z, size.z, resolution, false);

                    Vector2 segmentStart = new(start.x, start.z);
                    Vector2 segmentEnd = new(end.x, end.z);
                    Vector2 delta = segmentEnd - segmentStart;
                    float lengthSquared = delta.sqrMagnitude;
                    for (int z = zMin; z <= zMax; z++)
                    {
                        float worldZ = origin.z + z / (float)(resolution - 1) * size.z;
                        for (int x = xMin; x <= xMax; x++)
                        {
                            float worldX = origin.x + x / (float)(resolution - 1) * size.x;
                            Vector2 point = new(worldX, worldZ);
                            float t = lengthSquared <= 0.0001f
                                ? 0f
                                : Mathf.Clamp01(Vector2.Dot(point - segmentStart, delta) / lengthSquared);
                            float distance = Vector2.Distance(point, segmentStart + delta * t);
                            if (distance > radius) continue;

                            float worldHeight = Mathf.Lerp(start.y, end.y, t) - TerrainDesignSurfaceOffset;
                            float originalWorldHeight = origin.y + original[z, x] * size.y;
                            float heightDelta = Mathf.Abs(originalWorldHeight - worldHeight);
                            float slopeLimitedWidth = heightDelta * 1.5f /
                                Mathf.Tan(RoadsideBlendSlopeDegrees * Mathf.Deg2Rad);
                            float blendWidth = Mathf.Clamp(
                                Mathf.Max(RoadsideMinimumBlendWidth, slopeLimitedWidth),
                                RoadsideMinimumBlendWidth,
                                RoadsideMaximumBlendWidth);
                            if (distance > halfWidth + blendWidth) continue;

                            float weight = distance <= halfWidth
                                ? 1f
                                : 1f - Mathf.SmoothStep(
                                    0f,
                                    1f,
                                    (distance - halfWidth) / blendWidth);
                            float normalizedHeight = Mathf.Clamp01((worldHeight - origin.y) / size.y);
                            float contribution = weight * weight;
                            influence[z, x] = Mathf.Max(influence[z, x], weight);
                            targetSum[z, x] += normalizedHeight * contribution;
                            targetWeight[z, x] += contribution;
                            if (distance <= halfWidth)
                            {
                                surfaceTargetSum[z, x] += normalizedHeight;
                                surfaceTargetWeight[z, x] += 1f;
                            }
                        }
                    }
                }
            }

            var result = new float[resolution, resolution];
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float averagedTarget;
                if (surfaceTargetWeight[z, x] > 0.0001f)
                    averagedTarget = surfaceTargetSum[z, x] / surfaceTargetWeight[z, x];
                else if (targetWeight[z, x] > 0.0001f)
                    averagedTarget = targetSum[z, x] / targetWeight[z, x];
                else
                    averagedTarget = original[z, x];
                result[z, x] = Mathf.Lerp(original[z, x], averagedTarget, influence[z, x]);
            }

            data.SetHeightsDelayLOD(0, 0, result);
            data.SyncHeightmap();
            terrain.Flush();
            EditorUtility.SetDirty(data);
        }

        private static void AlignConnectedEndsToMainProfile(
            List<Vector3> branch,
            IReadOnlyList<Vector3> mainProfile,
            float branchWidth)
        {
            AlignConnectedEnd(branch, mainProfile, branchWidth, true);
            AlignConnectedEnd(branch, mainProfile, branchWidth, false);
        }

        private static void AlignConnectedEnd(
            List<Vector3> branch,
            IReadOnlyList<Vector3> mainProfile,
            float branchWidth,
            bool start)
        {
            int endpointIndex = start ? 0 : branch.Count - 1;
            Vector3 endpoint = branch[endpointIndex];
            Vector3 nearest = mainProfile[0];
            float nearestDistance = HorizontalSqrDistance(endpoint, nearest);
            for (int i = 1; i < mainProfile.Count; i++)
            {
                float distance = HorizontalSqrDistance(endpoint, mainProfile[i]);
                if (distance >= nearestDistance) continue;
                nearestDistance = distance;
                nearest = mainProfile[i];
            }

            float connectionRadius = Mathf.Max(4f, branchWidth * 0.8f);
            if (nearestDistance > connectionRadius * connectionRadius) return;

            float heightCorrection = nearest.y - endpoint.y;
            if (Mathf.Abs(heightCorrection) <= 0.001f)
            {
                LimitGradeAwayFromEndpoint(branch, start, 10.5f);
                return;
            }

            float traveled = 0f;
            int previousIndex = endpointIndex;
            for (int step = 0; step < branch.Count; step++)
            {
                int index = start ? step : branch.Count - 1 - step;
                if (step > 0)
                {
                    traveled += Vector2.Distance(
                        new Vector2(branch[previousIndex].x, branch[previousIndex].z),
                        new Vector2(branch[index].x, branch[index].z));
                }
                if (traveled > JunctionHeightBlendLength) break;

                float weight = 1f - Mathf.SmoothStep(0f, 1f, traveled / JunctionHeightBlendLength);
                Vector3 point = branch[index];
                point.y += heightCorrection * weight;
                branch[index] = point;
                previousIndex = index;
            }

            LimitGradeAwayFromEndpoint(branch, start, 10.5f);
        }

        private static void LimitGradeAwayFromEndpoint(
            List<Vector3> points,
            bool start,
            float maximumGradeDegrees)
        {
            float maximumSlope = Mathf.Tan(maximumGradeDegrees * Mathf.Deg2Rad);
            int previousIndex = start ? 0 : points.Count - 1;
            for (int step = 1; step < points.Count; step++)
            {
                int index = start ? step : points.Count - 1 - step;
                Vector3 previous = points[previousIndex];
                Vector3 point = points[index];
                float distance = Vector2.Distance(
                    new Vector2(previous.x, previous.z),
                    new Vector2(point.x, point.z));
                float maximumDelta = Mathf.Max(0.0001f, distance * maximumSlope);
                point.y = Mathf.Clamp(
                    point.y,
                    previous.y - maximumDelta,
                    previous.y + maximumDelta);
                points[index] = point;
                previousIndex = index;
            }
        }

        private static void BuildGradeLimitedCenterLine(
            IReadOnlyList<Vector3> source,
            float maximumGradeDegrees,
            List<Vector3> destination)
        {
            destination.Clear();
            for (int i = 0; i < source.Count; i++) destination.Add(source[i]);
            if (destination.Count < 3) return;

            var heights = new float[destination.Count];
            for (int i = 0; i < heights.Length; i++) heights[i] = destination[i].y;
            for (int pass = 0; pass < 2; pass++)
            {
                float previous = heights[0];
                for (int i = 1; i < heights.Length - 1; i++)
                {
                    float current = heights[i];
                    heights[i] = (previous + current * 2f + heights[i + 1]) * 0.25f;
                    previous = current;
                }
            }

            float maximumSlope = Mathf.Tan(maximumGradeDegrees * Mathf.Deg2Rad);
            for (int pass = 0; pass < 2; pass++)
            {
                for (int i = 1; i < heights.Length; i++)
                {
                    float distance = Vector2.Distance(
                        new Vector2(destination[i - 1].x, destination[i - 1].z),
                        new Vector2(destination[i].x, destination[i].z));
                    float delta = Mathf.Max(0.0001f, distance * maximumSlope);
                    heights[i] = Mathf.Clamp(heights[i], heights[i - 1] - delta, heights[i - 1] + delta);
                }

                for (int i = heights.Length - 2; i >= 0; i--)
                {
                    float distance = Vector2.Distance(
                        new Vector2(destination[i].x, destination[i].z),
                        new Vector2(destination[i + 1].x, destination[i + 1].z));
                    float delta = Mathf.Max(0.0001f, distance * maximumSlope);
                    heights[i] = Mathf.Clamp(heights[i], heights[i + 1] - delta, heights[i + 1] + delta);
                }
            }

            for (int i = 0; i < destination.Count; i++)
            {
                Vector3 point = destination[i];
                point.y = heights[i];
                destination[i] = point;
            }
        }

        private static int WorldToHeightCoordinate(
            float world,
            float origin,
            float terrainSize,
            int resolution,
            bool floor)
        {
            float coordinate = (world - origin) / terrainSize * (resolution - 1);
            int value = floor ? Mathf.FloorToInt(coordinate) - 1 : Mathf.CeilToInt(coordinate) + 1;
            return Mathf.Clamp(value, 0, resolution - 1);
        }

        private static void BuildJunctionAdjustments(
            Transform networkRoot,
            IReadOnlyList<TerrainRoadPath> sourcePaths,
            Terrain terrain,
            Material capMaterial)
        {
            Transform root = new GameObject(AdjustmentRootName).transform;
            root.SetParent(networkRoot, false);
            var positions = new List<Vector3>();

            foreach (TerrainRoadPath sourcePath in sourcePaths)
            {
                if (sourcePath.name.StartsWith("09_", StringComparison.Ordinal) ||
                    sourcePath.name.StartsWith("10_", StringComparison.Ordinal))
                    continue;

                var controlPoints = new List<Vector3>();
                sourcePath.GetWorldControlPoints(controlPoints);
                if (controlPoints.Count < 2) continue;

                var capCenters = new List<(Vector3 Center, Vector3 Outward)>
                {
                    (
                        controlPoints[controlPoints.Count - 1],
                        GetHorizontalDirection(
                            controlPoints[controlPoints.Count - 1] - controlPoints[controlPoints.Count - 2]))
                };
                if (sourcePath.name.StartsWith("00_", StringComparison.Ordinal))
                {
                    capCenters.Add((
                        controlPoints[0],
                        GetHorizontalDirection(controlPoints[0] - controlPoints[1])));
                }

                foreach ((Vector3 center, Vector3 outward) in capCenters)
                {
                    if (positions.Any(existing => HorizontalSqrDistance(existing, center) < 0.25f)) continue;
                    positions.Add(center);

                    Mesh mesh = CreateTerrainConformingEndCap(
                        "MSH_ER_EndCap_" + positions.Count.ToString("00"),
                        center,
                        outward,
                        sourcePath.Width * 0.52f,
                        terrain);
                    string meshPath = GeneratedRoot + "/" + mesh.name + ".asset";
                    Mesh savedMesh = SaveOrUpdateMesh(mesh, meshPath);

                    GameObject cap = new GameObject("TerminalBlend_" + positions.Count.ToString("00"));
                    cap.transform.SetParent(root, false);
                    cap.AddComponent<MeshFilter>().sharedMesh = savedMesh;
                    MeshRenderer capRenderer = cap.AddComponent<MeshRenderer>();
                    capRenderer.sharedMaterial = capMaterial;
                    capRenderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                    capRenderer.receiveShadows = false;
                }
            }
        }

        private static Mesh CreateTerrainConformingEndCap(
            string name,
            Vector3 center,
            Vector3 outward,
            float radius,
            Terrain terrain)
        {
            outward = GetHorizontalDirection(outward);
            Vector3 right = new(outward.z, 0f, -outward.x);
            int arcVertexCount = EndCapSegments + 1;
            int vertexCount = 1 + CapRings * arcVertexCount;
            int triangleCount = EndCapSegments + (CapRings - 1) * EndCapSegments * 2;
            var vertices = new Vector3[vertexCount];
            var uv = new Vector2[vertexCount];
            var triangles = new int[triangleCount * 3];

            vertices[0] = GroundPoint(terrain, center, CapSurfaceOffset);
            uv[0] = new Vector2(0.5f, 0.5f);

            for (int ring = 1; ring <= CapRings; ring++)
            {
                float normalizedRadius = ring / (float)CapRings;
                int ringStart = 1 + (ring - 1) * arcVertexCount;
                for (int segment = 0; segment <= EndCapSegments; segment++)
                {
                    float angle = Mathf.Lerp(-Mathf.PI * 0.5f, Mathf.PI * 0.5f,
                        segment / (float)EndCapSegments);
                    // Sweep right -> outward -> left. The diameter stays on the
                    // road endpoint while only the curved half extends beyond it.
                    Vector3 direction = outward * Mathf.Cos(angle) - right * Mathf.Sin(angle);
                    Vector3 point = center + direction * (radius * normalizedRadius);
                    int vertex = ringStart + segment;
                    vertices[vertex] = GroundPoint(terrain, point, CapSurfaceOffset);
                    uv[vertex] = new Vector2(direction.x, direction.z) * (0.5f * normalizedRadius) +
                                 Vector2.one * 0.5f;
                }
            }

            int triangleIndex = 0;
            for (int segment = 0; segment < EndCapSegments; segment++)
            {
                triangles[triangleIndex++] = 0;
                triangles[triangleIndex++] = 1 + segment + 1;
                triangles[triangleIndex++] = 1 + segment;
            }

            for (int ring = 1; ring < CapRings; ring++)
            {
                int innerStart = 1 + (ring - 1) * arcVertexCount;
                int outerStart = innerStart + arcVertexCount;
                for (int segment = 0; segment < EndCapSegments; segment++)
                {
                    triangles[triangleIndex++] = innerStart + segment;
                    triangles[triangleIndex++] = outerStart + segment + 1;
                    triangles[triangleIndex++] = outerStart + segment;
                    triangles[triangleIndex++] = innerStart + segment;
                    triangles[triangleIndex++] = innerStart + segment + 1;
                    triangles[triangleIndex++] = outerStart + segment + 1;
                }
            }

            var mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void ClearLegacyRoadPaint(TerrainData terrainData)
        {
            TerrainLayer[] layers = terrainData.terrainLayers;
            int packedSnow = FindLayer(layers, "PackedSnow");
            int road = FindLayer(layers, "WinterRoad");
            int border = FindLayer(layers, "RoadBorder");
            if (packedSnow < 0 || road < 0 || border < 0) return;

            int resolution = terrainData.alphamapResolution;
            float[,,] maps = terrainData.GetAlphamaps(0, 0, resolution, resolution);
            for (int z = 0; z < resolution; z++)
            for (int x = 0; x < resolution; x++)
            {
                float legacyWeight = maps[z, x, road] + maps[z, x, border];
                if (legacyWeight <= 0.0001f) continue;
                maps[z, x, packedSnow] += legacyWeight;
                maps[z, x, road] = 0f;
                maps[z, x, border] = 0f;
            }
            terrainData.SetAlphamaps(0, 0, maps);
        }

        private static int FindLayer(IReadOnlyList<TerrainLayer> layers, string nameFragment)
        {
            for (int i = 0; i < layers.Count; i++)
            {
                if (layers[i] != null &&
                    layers[i].name.IndexOf(nameFragment, StringComparison.OrdinalIgnoreCase) >= 0)
                    return i;
            }
            return -1;
        }

        private static Material GetOrCreateRoadMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(RoadMaterialPath);
            if (material == null)
            {
                Material source = AssetDatabase.LoadAssetAtPath<Material>(SourceRoadMaterialPath);
                if (source == null)
                    throw new FileNotFoundException("Winter road source material was not found.", SourceRoadMaterialPath);
                material = new Material(source) { name = "M_RoadFirst_EasyRoadsPackedSnow" };
                AssetDatabase.CreateAsset(material, RoadMaterialPath);
            }

            material.SetColor("_RoadColor", new Color(0.52f, 0.46f, 0.39f, 1f));
            material.SetColor("_EdgeColor", new Color(0.82f, 0.89f, 0.98f, 1f));
            // EasyRoads' UV layout varies by generated mesh chunk. Keep the
            // road body borderless; snowy shoulders are supplied by terrain.
            material.SetFloat("_EdgeWidth", 0f);
            material.SetFloat("_RadialEdge", 0f);
            material.SetFloat("_NoiseScale", 1.7f);
            material.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material GetOrCreateCapMaterial(Material roadMaterial)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CapMaterialPath);
            if (material == null)
            {
                material = new Material(roadMaterial) { name = "M_RoadFirst_EasyRoadsEndCap" };
                AssetDatabase.CreateAsset(material, CapMaterialPath);
            }
            else
            {
                material.shader = roadMaterial.shader;
            }

            ConfigureBlendMaterial(material, 0.12f);
            return material;
        }

        private static void ConfigureBlendMaterial(Material material, float edgeWidth)
        {
            material.SetColor("_RoadColor", new Color(0.52f, 0.46f, 0.39f, 1f));
            material.SetColor("_EdgeColor", new Color(0.82f, 0.89f, 0.98f, 1f));
            material.SetFloat("_EdgeWidth", edgeWidth);
            material.SetFloat("_RadialEdge", 1f);
            material.SetFloat("_NoiseScale", 1.7f);
            material.SetFloat("_Smoothness", 0.08f);
            EditorUtility.SetDirty(material);
        }

        private static void BackupTerrainData(TerrainData terrainData)
        {
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainBackupPath) != null) return;
            string sourcePath = AssetDatabase.GetAssetPath(terrainData);
            if (string.IsNullOrEmpty(sourcePath) || !AssetDatabase.CopyAsset(sourcePath, TerrainBackupPath))
                throw new IOException("Failed to create the pre-EasyRoads TerrainData backup.");
        }

        private static void RestoreTerrainHeightsFromBackup(TerrainData terrainData)
        {
            TerrainData backup = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainBackupPath);
            if (backup == null)
                throw new IOException("The pre-EasyRoads TerrainData backup could not be loaded.");
            if (backup.heightmapResolution != terrainData.heightmapResolution)
                throw new InvalidOperationException(
                    "The Road First World terrain heightmap resolution no longer matches its baseline backup.");

            int resolution = terrainData.heightmapResolution;
            float[,] baselineHeights = backup.GetHeights(0, 0, resolution, resolution);
            terrainData.SetHeightsDelayLOD(0, 0, baselineHeights);
            terrainData.SyncHeightmap();
            EditorUtility.SetDirty(terrainData);
        }

        private static void RemoveExistingEasyRoadsNetwork(Scene scene)
        {
            ERModularBase modularBase = FindSceneModularBase(scene);
            if (modularBase != null)
            {
                ERRoadNetwork network = new ERRoadNetwork();
                if (network.GetRoadNetworkStatus() == ERRoadNetworkStatus.BuildMode)
                    network.RestoreRoadNetwork();
                UnityEngine.Object.DestroyImmediate(modularBase.gameObject);
            }

            GameObject stale = GameObject.Find(NetworkName);
            if (stale != null) UnityEngine.Object.DestroyImmediate(stale);
        }

        private static ERModularBase FindSceneModularBase(Scene scene)
        {
            return Resources.FindObjectsOfTypeAll<ERModularBase>()
                .FirstOrDefault(component =>
                    component != null &&
                    component.gameObject.scene.IsValid() &&
                    component.gameObject.scene == scene);
        }

        private static Vector3 GroundPoint(Terrain terrain, Vector3 point, float offset)
        {
            point.y = terrain.SampleHeight(point) + terrain.transform.position.y + offset;
            return point;
        }

        private static Vector3 GetHorizontalDirection(Vector3 direction)
        {
            direction.y = 0f;
            return direction.sqrMagnitude > 0.0001f ? direction.normalized : Vector3.forward;
        }

        private static float HorizontalSqrDistance(Vector3 a, Vector3 b)
        {
            float x = a.x - b.x;
            float z = a.z - b.z;
            return x * x + z * z;
        }

        private static Mesh SaveOrUpdateMesh(Mesh mesh, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(mesh, path);
                return mesh;
            }

            EditorUtility.CopySerialized(mesh, existing);
            UnityEngine.Object.DestroyImmediate(mesh);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static Scene RequireRoadFirstScene()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
                throw new InvalidOperationException("Open WinterVillage_RoadFirstWorld before running this command.");
            if (scene.isDirty)
                throw new InvalidOperationException("Save Road First World before rebuilding its roads.");
            return scene;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
