using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack
{
    internal static class WinterTerrainTemplateGenerator
    {
        internal const string ProfilesFolder =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainTemplates/Profiles";
        internal const string VillageProfilePath = ProfilesFolder + "/TP_WinterVillageBasin.asset";
        internal const string HillsideProfilePath = ProfilesFolder + "/TP_AlpineHillside.asset";
        internal const string SkiProfilePath = ProfilesFolder + "/TP_SkiSlope.asset";

        private const string SnowLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_01_SnowWhite.terrainlayer";
        private const string PackedSnowLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_02_PackedSnow.terrainlayer";
        private const string ShadowSnowLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_03_ShadowSnow.terrainlayer";
        private const string RockLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_07_RockGray.terrainlayer";
        private const string PineLayerPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_08_PineGround.terrainlayer";

        [MenuItem("PPack/Level Design/Terrain Templates/Setup Included Templates")]
        internal static void SetupIncludedTemplates()
        {
            EnsureFolder(ProfilesFolder);
            CreateOrUpdateProfile(
                VillageProfilePath,
                "TP_WinterVillageBasin",
                EWinterTerrainTemplateShape.VillageBasin,
                new Vector3(160f, 36f, 160f),
                1025,
                1024,
                1024,
                1207,
                0.08f,
                0.34f);
            CreateOrUpdateProfile(
                HillsideProfilePath,
                "TP_AlpineHillside",
                EWinterTerrainTemplateShape.AlpineHillside,
                new Vector3(180f, 48f, 180f),
                1025,
                1024,
                1024,
                2401,
                0.06f,
                0.54f);
            CreateOrUpdateProfile(
                SkiProfilePath,
                "TP_SkiSlope",
                EWinterTerrainTemplateShape.SkiSlope,
                new Vector3(180f, 54f, 200f),
                1025,
                1024,
                1024,
                3817,
                0.05f,
                0.64f);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log("[WinterTerrainTemplate] Three included Terrain profiles are ready.");
        }

        internal static GameObject CreateTerrainInScene(
            WinterTerrainTemplateProfile profile,
            string terrainDataPath,
            Scene destinationScene)
        {
            if (profile == null) throw new ArgumentNullException(nameof(profile));
            if (!destinationScene.IsValid()) throw new ArgumentException("Destination Scene is invalid.");
            if (string.IsNullOrWhiteSpace(terrainDataPath)) throw new ArgumentException("TerrainData path is empty.");
            if (AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(terrainDataPath) != null)
                throw new IOException($"An asset already exists at {terrainDataPath}.");

            EnsureFolder(Path.GetDirectoryName(terrainDataPath)?.Replace('\\', '/'));
            TerrainData data = new() { name = Path.GetFileNameWithoutExtension(terrainDataPath) };
            ConfigureTerrainData(profile, data);
            AssetDatabase.CreateAsset(data, terrainDataPath);

            GameObject root = new($"TerrainTemplate_{profile.name}");
            SceneManager.MoveGameObjectToScene(root, destinationScene);
            Undo.RegisterCreatedObjectUndo(root, "Create Winter Terrain Template");

            Transform geometry = CreateRoleRoot("Geometry", root.transform);
            CreateRoleRoot("Landmarks", root.transform);
            CreateRoleRoot("RouteGuides", root.transform);
            CreateRoleRoot("BoundaryNature", root.transform);
            CreateRoleRoot("SetDressing", root.transform);
            CreateRoleRoot("Gameplay", root.transform);
            CreateRoleRoot("Lighting", root.transform);

            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = $"Terrain_{profile.name}";
            SceneManager.MoveGameObjectToScene(terrainObject, destinationScene);
            terrainObject.transform.SetParent(geometry, false);
            terrainObject.transform.localPosition = new Vector3(
                -profile.Size.x * 0.5f,
                0f,
                -profile.Size.z * 0.5f);

            Terrain terrain = terrainObject.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 3f;
            terrain.basemapDistance = 500f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            terrain.allowAutoConnect = false;

            RoadBuilderAssets.GetOrCreateTerrainBaseline(terrain);
            EditorSceneManagerBridge.MarkDirty(destinationScene);
            AssetDatabase.SaveAssets();
            Selection.activeGameObject = terrainObject;
            EditorGUIUtility.PingObject(data);
            return root;
        }

        internal static float[,] BuildHeightmap(WinterTerrainTemplateProfile profile)
        {
            int resolution = profile.HeightmapResolution;
            float[,] heights = new float[resolution, resolution];
            Vector2 noiseOffset = SeedOffset(profile.Seed);
            for (int z = 0; z < resolution; z++)
            {
                float nz = z / (float)(resolution - 1);
                for (int x = 0; x < resolution; x++)
                {
                    float nx = x / (float)(resolution - 1);
                    heights[z, x] = EvaluateHeight(profile, nx, nz, noiseOffset);
                }
            }
            return heights;
        }

        private static void ConfigureTerrainData(WinterTerrainTemplateProfile profile, TerrainData data)
        {
            data.heightmapResolution = profile.HeightmapResolution;
            data.alphamapResolution = profile.AlphamapResolution;
            data.baseMapResolution = profile.BaseMapResolution;
            data.size = profile.Size;
            float[,] heights = BuildHeightmap(profile);
            data.SetHeights(0, 0, heights);

            TerrainLayer[] layers = LoadSurfaceLayers();
            data.terrainLayers = layers;
            data.SetAlphamaps(0, 0, BuildSurfaceWeights(profile, heights, layers.Length));
            EditorUtility.SetDirty(data);
        }

        private static float[,,] BuildSurfaceWeights(
            WinterTerrainTemplateProfile profile,
            float[,] heights,
            int layerCount)
        {
            int resolution = profile.AlphamapResolution;
            int heightResolution = profile.HeightmapResolution;
            float[,,] weights = new float[resolution, resolution, layerCount];
            float stepX = profile.Size.x / (heightResolution - 1f);
            float stepZ = profile.Size.z / (heightResolution - 1f);

            for (int z = 0; z < resolution; z++)
            {
                float nz = z / (float)(resolution - 1);
                int hz = Mathf.Clamp(Mathf.RoundToInt(nz * (heightResolution - 1)), 1, heightResolution - 2);
                for (int x = 0; x < resolution; x++)
                {
                    float nx = x / (float)(resolution - 1);
                    int hx = Mathf.Clamp(Mathf.RoundToInt(nx * (heightResolution - 1)), 1, heightResolution - 2);
                    float dx = (heights[hz, hx + 1] - heights[hz, hx - 1]) * profile.Size.y / (stepX * 2f);
                    float dz = (heights[hz + 1, hx] - heights[hz - 1, hx]) * profile.Size.y / (stepZ * 2f);
                    float grade = Mathf.Sqrt(dx * dx + dz * dz);
                    float rock = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 0.78f, grade));
                    float packed = EvaluatePackedSnow(profile.Shape, nx, nz) * (1f - rock);
                    float edge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.68f, 1f, RadialEdge(nx, nz)));
                    float pine = edge * (1f - rock) * 0.42f;
                    float shadow = Mathf.Clamp01(0.10f + grade * 0.28f) * (1f - rock) * (1f - packed);
                    float snow = Mathf.Max(0.05f, 1f - rock - packed - pine - shadow);
                    float sum = snow + packed + shadow + rock + pine;
                    weights[z, x, 0] = snow / sum;
                    weights[z, x, 1] = packed / sum;
                    weights[z, x, 2] = shadow / sum;
                    weights[z, x, 3] = rock / sum;
                    weights[z, x, 4] = pine / sum;
                }
            }
            return weights;
        }

        private static float EvaluateHeight(
            WinterTerrainTemplateProfile profile,
            float nx,
            float nz,
            Vector2 noiseOffset)
        {
            float x = nx * 2f - 1f;
            float z = nz * 2f - 1f;
            float lowNoise = Mathf.PerlinNoise(
                noiseOffset.x + nx * 2.25f,
                noiseOffset.y + nz * 2.25f) - 0.5f;
            float broadNoise = Mathf.PerlinNoise(
                noiseOffset.x * 0.37f + nx * 0.85f,
                noiseOffset.y * 0.37f + nz * 0.85f) - 0.5f;
            float value;

            switch (profile.Shape)
            {
                case EWinterTerrainTemplateShape.AlpineHillside:
                    value = EvaluateHillside(x, z, lowNoise, broadNoise);
                    break;
                case EWinterTerrainTemplateShape.SkiSlope:
                    value = EvaluateSkiSlope(x, z, lowNoise, broadNoise);
                    break;
                default:
                    value = EvaluateVillageBasin(x, z, lowNoise, broadNoise);
                    break;
            }

            return Mathf.Clamp01(profile.BaseHeight + value * profile.Relief);
        }

        private static float EvaluateVillageBasin(float x, float z, float lowNoise, float broadNoise)
        {
            float radius = Mathf.Sqrt(x * x + z * z);
            float rim = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.38f, 1.25f, radius));
            float centerFlat = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.12f, 0.42f, radius));
            float rolling = lowNoise * 0.18f + broadNoise * 0.16f;
            return 0.10f + rim * 0.66f + rolling * (1f - centerFlat * 0.92f);
        }

        private static float EvaluateHillside(float x, float z, float lowNoise, float broadNoise)
        {
            float climb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 1f, z));
            float shoulder = Mathf.Exp(-Mathf.Pow(x + 0.56f, 2f) * 5.5f) * 0.18f;
            float shelf = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.18f, 0.50f, Mathf.Abs(z + 0.18f)));
            float rolling = lowNoise * 0.14f + broadNoise * 0.18f;
            return 0.08f + climb * 0.72f + shoulder + rolling * (1f - shelf * 0.72f);
        }

        private static float EvaluateSkiSlope(float x, float z, float lowNoise, float broadNoise)
        {
            float climb = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(-1f, 1f, z));
            float sideRidge = Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.34f, 1f, Mathf.Abs(x))) * 0.28f;
            float piste = 1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.28f, 0.56f, Mathf.Abs(x)));
            float rolling = lowNoise * 0.11f + broadNoise * 0.13f;
            return 0.05f + climb * 0.76f + sideRidge + rolling * (1f - piste * 0.88f);
        }

        private static float EvaluatePackedSnow(EWinterTerrainTemplateShape shape, float nx, float nz)
        {
            float x = nx * 2f - 1f;
            float z = nz * 2f - 1f;
            switch (shape)
            {
                case EWinterTerrainTemplateShape.SkiSlope:
                    return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.43f, Mathf.Abs(x)))) * 0.72f;
                case EWinterTerrainTemplateShape.AlpineHillside:
                    return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.10f, 0.32f, Mathf.Abs(x + z * 0.22f)))) * 0.34f;
                default:
                    float radius = Mathf.Sqrt(x * x + z * z);
                    return (1f - Mathf.SmoothStep(0f, 1f, Mathf.InverseLerp(0.20f, 0.42f, radius))) * 0.46f;
            }
        }

        private static float RadialEdge(float nx, float nz)
        {
            float x = Mathf.Abs(nx * 2f - 1f);
            float z = Mathf.Abs(nz * 2f - 1f);
            return Mathf.Max(x, z);
        }

        private static Vector2 SeedOffset(int seed)
        {
            System.Random random = new(seed);
            return new Vector2((float)random.NextDouble() * 80f, (float)random.NextDouble() * 80f);
        }

        private static TerrainLayer[] LoadSurfaceLayers()
        {
            TerrainLayer[] layers =
            {
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(SnowLayerPath),
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(PackedSnowLayerPath),
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(ShadowSnowLayerPath),
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(RockLayerPath),
                AssetDatabase.LoadAssetAtPath<TerrainLayer>(PineLayerPath)
            };
            for (int i = 0; i < layers.Length; i++)
            {
                if (layers[i] == null)
                    throw new FileNotFoundException("A required solid winter TerrainLayer is missing.");
            }
            return layers;
        }

        private static void CreateOrUpdateProfile(
            string path,
            string assetName,
            EWinterTerrainTemplateShape shape,
            Vector3 size,
            int heightResolution,
            int alphaResolution,
            int baseMapResolution,
            int seed,
            float baseHeight,
            float relief)
        {
            WinterTerrainTemplateProfile profile =
                AssetDatabase.LoadAssetAtPath<WinterTerrainTemplateProfile>(path);
            if (profile != null) return;

            profile = ScriptableObject.CreateInstance<WinterTerrainTemplateProfile>();
            profile.name = assetName;
            AssetDatabase.CreateAsset(profile, path);
            profile.Configure(
                shape,
                size,
                heightResolution,
                alphaResolution,
                baseMapResolution,
                seed,
                baseHeight,
                relief);
            EditorUtility.SetDirty(profile);
        }

        private static Transform CreateRoleRoot(string name, Transform parent)
        {
            GameObject child = new(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        internal static void EnsureFolder(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || AssetDatabase.IsValidFolder(folderPath)) return;
            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static class EditorSceneManagerBridge
        {
            internal static void MarkDirty(Scene scene)
            {
                UnityEditor.SceneManagement.EditorSceneManager.MarkSceneDirty(scene);
            }
        }
    }
}
