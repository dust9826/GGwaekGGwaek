using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class RoadBuilderAssets
    {
        internal const string DatabasePath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/Road/Databases/WinterVillageRoadEntrances.asset";
        internal const string MaterialPath =
            "Assets/Game/InGame/Map/Road/Materials/M_LevelDesignWinterRoad.mat";
        internal const string TerrainRoadLayerPath =
            "Assets/Game/InGame/Map/Road/TerrainLayers/TL_RoadBuilder_WinterPath.terrainlayer";
        internal const string TerrainRoadBorderLayerPath =
            "Assets/Game/InGame/Map/Road/TerrainLayers/TL_RoadBuilder_WinterBorder.terrainlayer";
        internal const string TerrainBaselineFolder =
            "Assets/Game/InGame/Map/Road/Baselines";
        private const string SourceTerrainLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_06_WinterPath.terrainlayer";
        private const string SourceBorderLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_05_FrozenDirt.terrainlayer";
        private const string TerrainLayerFolder = "Assets/Game/InGame/Map/Road/TerrainLayers";
        private const string HousesFolder = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Houses";

        [MenuItem("PPack/Level Design/Setup Road Builder Assets")]
        public static void SetupFromMenu()
        {
            RoadEntranceDatabase database = GetOrCreateDatabase();
            TerrainLayer terrainLayer = GetOrCreateTerrainRoadLayer();
            TerrainLayer borderLayer = GetOrCreateTerrainRoadBorderLayer();
            Selection.activeObject = database;
            EditorGUIUtility.PingObject(database);
            Debug.Log(
                $"Terrain Road Builder ready: {database.Entries.Count} house entrances, " +
                $"road {(terrainLayer == null ? "missing" : terrainLayer.name)}, " +
                $"border {(borderLayer == null ? "missing" : borderLayer.name)}.");
        }

        public static RoadEntranceDatabase GetOrCreateDatabase()
        {
            RoadEntranceDatabase database = AssetDatabase.LoadAssetAtPath<RoadEntranceDatabase>(DatabasePath);
            if (database == null)
            {
                database = ScriptableObject.CreateInstance<RoadEntranceDatabase>();
                AssetDatabase.CreateAsset(database, DatabasePath);
            }

            bool changed = AddMissingHousePrefabs(database);
            if (changed)
            {
                EditorUtility.SetDirty(database);
                AssetDatabase.SaveAssetIfDirty(database);
            }
            return database;
        }

        public static Material GetOrCreateMaterial()
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
            if (material != null) return material;

            Shader shader = Shader.Find("PPack/Level Design Winter Road");
            if (shader == null)
            {
                Debug.LogError("Road Builder shader was not found. Reimport LevelDesignWinterRoad.shader.");
                return null;
            }

            material = new Material(shader) { name = "M_LevelDesignWinterRoad" };
            AssetDatabase.CreateAsset(material, MaterialPath);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        public static TerrainLayer GetOrCreateTerrainRoadLayer()
        {
            return GetOrCreateTerrainLayer(
                TerrainRoadLayerPath,
                SourceTerrainLayerPath,
                "TL_RoadBuilder_WinterPath");
        }

        public static TerrainLayer GetOrCreateTerrainRoadBorderLayer()
        {
            return GetOrCreateTerrainLayer(
                TerrainRoadBorderLayerPath,
                SourceBorderLayerPath,
                "TL_RoadBuilder_WinterBorder");
        }

        public static TerrainRoadBaseline GetOrCreateTerrainBaseline(Terrain terrain)
        {
            if (terrain == null || terrain.terrainData == null) return null;

            TerrainData terrainData = terrain.terrainData;
            string terrainDataPath = AssetDatabase.GetAssetPath(terrainData);
            if (string.IsNullOrEmpty(terrainDataPath))
            {
                TerrainRoadBaseline transient = ScriptableObject.CreateInstance<TerrainRoadBaseline>();
                transient.hideFlags = HideFlags.HideAndDontSave;
                transient.Capture(terrainData);
                return transient;
            }

            EnsureAssetFolder(TerrainBaselineFolder);
            string[] baselineGuids = AssetDatabase.FindAssets(
                "t:TerrainRoadBaseline",
                new[] { TerrainBaselineFolder });
            for (int i = 0; i < baselineGuids.Length; i++)
            {
                string baselinePath = AssetDatabase.GUIDToAssetPath(baselineGuids[i]);
                TerrainRoadBaseline existing =
                    AssetDatabase.LoadAssetAtPath<TerrainRoadBaseline>(baselinePath);
                if (existing == null || existing.TerrainData != terrainData) continue;
                if (!existing.Matches(terrainData))
                {
                    existing.Capture(terrainData);
                    EditorUtility.SetDirty(existing);
                    AssetDatabase.SaveAssetIfDirty(existing);
                    Debug.LogWarning(
                        $"Terrain '{terrain.name}' heightmap resolution changed. Its road baseline was recaptured.",
                        terrain);
                }
                return existing;
            }

            string terrainGuid = AssetDatabase.AssetPathToGUID(terrainDataPath);
            string shortGuid = terrainGuid.Length > 12 ? terrainGuid.Substring(0, 12) : terrainGuid;
            string assetPath = $"{TerrainBaselineFolder}/TerrainRoadBaseline_{shortGuid}.asset";
            TerrainRoadBaseline baseline = ScriptableObject.CreateInstance<TerrainRoadBaseline>();
            baseline.name = $"TerrainRoadBaseline_{shortGuid}";
            baseline.Capture(terrainData);
            AssetDatabase.CreateAsset(baseline, assetPath);
            AssetDatabase.SaveAssetIfDirty(baseline);
            return baseline;
        }

        public static TerrainRoadBaseline RecaptureTerrainBaseline(Terrain terrain)
        {
            TerrainRoadBaseline baseline = GetOrCreateTerrainBaseline(terrain);
            if (baseline == null) return null;

            baseline.Capture(terrain.terrainData);
            if (AssetDatabase.Contains(baseline))
            {
                EditorUtility.SetDirty(baseline);
                AssetDatabase.SaveAssetIfDirty(baseline);
            }
            return baseline;
        }

        private static TerrainLayer GetOrCreateTerrainLayer(
            string destinationPath,
            string sourcePath,
            string assetName)
        {
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(destinationPath);
            if (layer != null) return layer;

            EnsureTerrainLayerFolder();
            TerrainLayer source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourcePath);
            if (source == null)
            {
                Debug.LogError($"Terrain road source layer was not found at {sourcePath}.");
                return null;
            }

            layer = new TerrainLayer
            {
                name = assetName,
                diffuseTexture = source.diffuseTexture,
                normalMapTexture = source.normalMapTexture,
                maskMapTexture = source.maskMapTexture,
                tileSize = source.tileSize,
                tileOffset = source.tileOffset,
                normalScale = source.normalScale,
                metallic = source.metallic,
                smoothness = source.smoothness,
                specular = source.specular
            };
            AssetDatabase.CreateAsset(layer, destinationPath);
            AssetDatabase.SaveAssetIfDirty(layer);
            return layer;
        }

        private static void EnsureTerrainLayerFolder()
        {
            if (AssetDatabase.IsValidFolder(TerrainLayerFolder)) return;
            AssetDatabase.CreateFolder("Assets/Game/InGame/Map/Road", "TerrainLayers");
        }

        private static void EnsureAssetFolder(string folderPath)
        {
            if (AssetDatabase.IsValidFolder(folderPath)) return;

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static bool AddMissingHousePrefabs(RoadEntranceDatabase database)
        {
            bool changed = false;
            string[] guids = AssetDatabase.FindAssets("t:Prefab", new[] { HousesFolder });
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null || database.ContainsPrefab(prefab)) continue;

                if (!TryEstimateEntrance(prefab, out Vector3 position, out float width))
                {
                    position = new Vector3(0f, 0f, 2f);
                    width = 1.5f;
                }

                database.Add(new RoadEntranceProfile(
                    prefab,
                    position,
                    Vector3.forward,
                    width,
                    true));
                changed = true;
            }
            return changed;
        }

        private static bool TryEstimateEntrance(GameObject prefab, out Vector3 localPosition, out float doorWidth)
        {
            localPosition = default;
            doorWidth = 1.5f;
            GameObject contents = PrefabUtility.LoadPrefabContents(AssetDatabase.GetAssetPath(prefab));
            try
            {
                Renderer[] renderers = contents.GetComponentsInChildren<Renderer>(true);
                if (renderers.Length == 0) return false;

                Bounds localBounds = CalculateLocalBounds(contents.transform, renderers);
                localPosition = new Vector3(
                    localBounds.center.x,
                    localBounds.min.y + 0.04f,
                    localBounds.max.z + 0.20f);
                doorWidth = Mathf.Clamp(localBounds.size.x * 0.24f, 1.3f, 2.4f);
                return true;
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }
        }

        private static Bounds CalculateLocalBounds(Transform root, Renderer[] renderers)
        {
            bool initialized = false;
            Bounds result = default;
            for (int rendererIndex = 0; rendererIndex < renderers.Length; rendererIndex++)
            {
                Bounds bounds = renderers[rendererIndex].bounds;
                Vector3 min = bounds.min;
                Vector3 max = bounds.max;
                for (int corner = 0; corner < 8; corner++)
                {
                    Vector3 world = new Vector3(
                        (corner & 1) == 0 ? min.x : max.x,
                        (corner & 2) == 0 ? min.y : max.y,
                        (corner & 4) == 0 ? min.z : max.z);
                    Vector3 local = root.InverseTransformPoint(world);
                    if (!initialized)
                    {
                        result = new Bounds(local, Vector3.zero);
                        initialized = true;
                    }
                    else result.Encapsulate(local);
                }
            }
            return result;
        }
    }
}
