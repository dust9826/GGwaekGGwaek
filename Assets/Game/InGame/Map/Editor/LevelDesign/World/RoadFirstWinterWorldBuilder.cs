using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// Builds a project-owned, road-first winter world. Roads remain editable Unity Splines,
    /// while the Terrain owns their rendered surface and collision.
    /// </summary>
    internal static class RoadFirstWinterWorldBuilder
    {
        private const string MenuPath = "PPack/Map/Build Road-First Winter World";
        private const string ScenePath =
            "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_RoadFirstWorld.unity";
        private const string GeneratedRoot =
            "Assets/Game/InGame/Map/WinterVillage/Generated/RoadFirstWorld";
        private const string MaterialsRoot =
            "Assets/Game/InGame/Map/WinterVillage/Materials/RoadFirstWorld";
        private const string TerrainLayersRoot = GeneratedRoot + "/TerrainLayers";
        private const string ToonShaderName = "PPack/Road First Toon Matte";
        private const string TerrainDataPath = GeneratedRoot + "/TD_RoadFirstWinterWorld.asset";
        private const string RiverMeshPath = GeneratedRoot + "/MSH_RoadFirst_River.asset";
        private const string BackdropMountainMeshPath = GeneratedRoot + "/MSH_RoadFirst_BackdropMountain.asset";
        private const string OuterSnowMaterialPath = MaterialsRoot + "/M_RoadFirst_OuterSnow.mat";
        private const string BackdropMountainMaterialPath = MaterialsRoot + "/M_RoadFirst_BackdropMountain.mat";
        private const string ToonWorldMaterialPath = MaterialsRoot + "/M_RoadFirst_ToonLowPolyWinter.mat";
        private const string ToonWaterMaterialPath = MaterialsRoot + "/M_RoadFirst_ToonWater.mat";
        private const string VendorEnvironmentRoot =
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Locations with Env/Low Poly Winter";
        private const string VendorBridgePath =
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Bridges/winter bridge.prefab";
        private const string NatureRoot =
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/Nature";
        private const string SnowfallPrefabPath =
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/VFX/PF_WinterSnowfall.prefab";
        private const string LanternPrefabPath =
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/Lighting/PF_WinterLantern_Glow.prefab";
        private const string PenguinPrefabPath =
            "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";
        private const string SkyboxMaterialPath =
            "Assets/Game/InGame/Map/WinterVillage/Lighting/Materials/M_WinterVillage_BlueHourSky.mat";
        private const string WaterMaterialPath =
            "Assets/Game/InGame/Map/WinterVillage/Materials/M_WinterWater.mat";
        private const string VendorMainMaterialPath =
            "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Materials/mat main.mat";
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
        private const string RoadLayerSourcePath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_06_WinterPath.terrainlayer";
        private const string BorderLayerSourcePath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/TerrainLayers/Solid/TL_Solid_05_FrozenDirt.terrainlayer";

        private const float MapHalfExtent = 120f;
        private const float TerrainHeight = 36f;
        private const float WaterLevel = 2.45f;

        private sealed class ZoneSpec
        {
            public string Name;
            public string PrefabName;
            public Vector2 Center;
            public float TargetHeight;
            public float SourceGroundY;
            public float Scale;
            public float Yaw;
            public float PadRadius;
        }

        private sealed class RoadSpec
        {
            public string Name;
            public float Width;
            public Vector3[] Points;
        }

        private static Vector3 P(float x, float y, float z) => new(x, y, z);

        private static readonly ZoneSpec[] Zones =
        {
            new ZoneSpec { Name = "01_WinterTown", PrefabName = "winter town with env", Center = new Vector2(-99f, -63f), TargetHeight = 3.15f, SourceGroundY = 4.05f, Scale = 0.64f, Yaw = 13f, PadRadius = 15f },
            new ZoneSpec { Name = "02_ChristmasFair", PrefabName = "christmass fair with env", Center = new Vector2(-61f, -43f), TargetHeight = 4.10f, SourceGroundY = 3.35f, Scale = 0.66f, Yaw = -8f, PadRadius = 15f },
            new ZoneSpec { Name = "03_ChristmasVillage", PrefabName = "christmas village with env", Center = new Vector2(-38f, -7f), TargetHeight = 6.15f, SourceGroundY = 4.05f, Scale = 0.66f, Yaw = 17f, PadRadius = 16f },
            new ZoneSpec { Name = "04_RiverHouse", PrefabName = "river winter house with env", Center = new Vector2(37f, -84f), TargetHeight = 3.25f, SourceGroundY = 4.10f, Scale = 0.63f, Yaw = -12f, PadRadius = 15f },
            new ZoneSpec { Name = "05_CountryHouse", PrefabName = "country winter house with env", Center = new Vector2(66f, -34f), TargetHeight = 5.55f, SourceGroundY = 4.15f, Scale = 0.63f, Yaw = 20f, PadRadius = 15f },
            new ZoneSpec { Name = "06_SantaDistrict", PrefabName = "santa claus house with env", Center = new Vector2(43f, 2f), TargetHeight = 8.85f, SourceGroundY = 4.10f, Scale = 0.68f, Yaw = -12f, PadRadius = 16f },
            new ZoneSpec { Name = "07_WinterRoadForest", PrefabName = "winter road forest with env", Center = new Vector2(-13f, 35f), TargetHeight = 11.65f, SourceGroundY = 4.00f, Scale = 0.69f, Yaw = 8f, PadRadius = 16f },
            new ZoneSpec { Name = "08_NorthPoleCamp", PrefabName = "life north pole with env", Center = new Vector2(70f, 33f), TargetHeight = 12.75f, SourceGroundY = 4.00f, Scale = 0.70f, Yaw = 12f, PadRadius = 16f },
            new ZoneSpec { Name = "09_SkiResort", PrefabName = "ski resort with env", Center = new Vector2(38f, 79f), TargetHeight = 18.75f, SourceGroundY = 3.85f, Scale = 0.72f, Yaw = -10f, PadRadius = 17f }
        };

        private static readonly RoadSpec[] Roads =
        {
            new RoadSpec
            {
                Name = "00_Main_Valley_To_Summit",
                Width = 7.2f,
                Points = new[]
                {
                    P(-113f, 2.55f, -101f), P(-93f, 2.85f, -84f), P(-74f, 3.45f, -62f),
                    P(-53f, 4.35f, -43f), P(-31f, 5.35f, -28f), P(-12f, 6.45f, -15f),
                    P(7f, 7.70f, -3f), P(25f, 8.85f, 9f), P(18f, 11.00f, 29f),
                    P(10f, 14.20f, 50f), P(14f, 18.00f, 73f), P(31f, 21.30f, 103f)
                }
            },
            new RoadSpec
            {
                Name = "01_Access_WinterTown",
                Width = 4.6f,
                Points = new[]
                {
                    P(-93f, 2.85f, -84f), P(-101f, 2.95f, -78f), P(-105f, 3.10f, -73f)
                }
            },
            new RoadSpec
            {
                Name = "02_Access_ChristmasFair",
                Width = 4.5f,
                Points = new[]
                {
                    P(-74f, 3.45f, -62f), P(-72f, 3.70f, -53f), P(-69f, 3.95f, -49f)
                }
            },
            new RoadSpec
            {
                Name = "03_Access_ChristmasVillage",
                Width = 4.4f,
                Points = new[]
                {
                    P(-12f, 6.45f, -15f), P(-22f, 6.25f, -13f), P(-26f, 6.15f, -12f)
                }
            },
            new RoadSpec
            {
                Name = "04_Access_Santa",
                Width = 4.8f,
                Points = new[]
                {
                    P(25f, 8.85f, 9f), P(31f, 8.85f, 6f), P(33f, 8.85f, 4f)
                }
            },
            new RoadSpec
            {
                Name = "05_Access_RoadForest",
                Width = 4.4f,
                Points = new[]
                {
                    P(18f, 11.00f, 29f), P(6f, 11.25f, 31f), P(-1f, 11.55f, 33f)
                }
            },
            new RoadSpec
            {
                Name = "06_Access_SkiResort",
                Width = 4.8f,
                Points = new[]
                {
                    P(14f, 18.00f, 73f), P(22f, 18.35f, 75f), P(26f, 18.60f, 77f)
                }
            },
            new RoadSpec
            {
                Name = "07_NorthPole_Branch",
                Width = 4.8f,
                Points = new[]
                {
                    P(25f, 8.85f, 9f), P(41f, 9.95f, 16f), P(54f, 11.15f, 23f),
                    P(59f, 11.75f, 27f)
                }
            },
            new RoadSpec
            {
                Name = "08_CountryHouse_Branch",
                Width = 4.6f,
                Points = new[]
                {
                    P(7f, 7.70f, -3f), P(27f, 7.05f, -10f), P(43f, 6.35f, -18f),
                    P(53f, 5.80f, -25f)
                }
            },
            new RoadSpec
            {
                Name = "09_Riverside_NorthApproach",
                Width = 4.5f,
                Points = new[]
                {
                    P(-31f, 5.35f, -28f), P(-8f, 4.75f, -40f), P(7f, 4.15f, -50f),
                    P(13f, 3.75f, -54f)
                }
            },
            new RoadSpec
            {
                Name = "10_Riverside_SouthApproach",
                Width = 4.5f,
                Points = new[]
                {
                    P(25f, 3.45f, -70f), P(30f, 3.30f, -76f), P(31f, 3.25f, -78f)
                }
            }
        };

        private static readonly Vector2[] RiverPoints =
        {
            new Vector2(2f, -119f), new Vector2(-3f, -108f), new Vector2(-8f, -84f),
            new Vector2(-8f, -70f), new Vector2(13f, -62f), new Vector2(37f, -67f),
            new Vector2(61f, -62f), new Vector2(84f, -68f), new Vector2(119f, -55f)
        };

        private static readonly Vector3[] LanternPositions =
        {
            new Vector3(-104f, 0f, -94f), new Vector3(-83f, 0f, -72f),
            new Vector3(-62f, 0f, -51f), new Vector3(-41f, 0f, -35f),
            new Vector3(-20f, 0f, -19f), new Vector3(1f, 0f, -7f),
            new Vector3(20f, 0f, 7f), new Vector3(20f, 0f, 25f),
            new Vector3(13f, 0f, 45f), new Vector3(12f, 0f, 65f),
            new Vector3(24f, 0f, 86f), new Vector3(42f, 0f, 17f),
            new Vector3(56f, 0f, 26f), new Vector3(35f, 0f, -15f),
            new Vector3(7f, 0f, -48f), new Vector3(28f, 0f, -75f)
        };

        [MenuItem(MenuPath)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before rebuilding the road-first winter world.");

            Scene current = SceneManager.GetActiveScene();
            if (current.IsValid() && current.isDirty && current.path != ScenePath)
                throw new InvalidOperationException("The active scene has unsaved changes. Save it before building the road-first world.");

            EnsureFolder(GeneratedRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(TerrainLayersRoot);
            EnsureFolder(Path.GetDirectoryName(ScenePath)?.Replace('\\', '/'));

            TerrainData terrainData = GetOrCreateTerrainData();
            ConfigureTerrainData(terrainData);

            Scene targetScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            targetScene.name = "WinterVillage_RoadFirstWorld";

            GameObject worldRoot = NewSceneObject("RoadFirstWinterWorld", targetScene);
            Transform geometryRoot = NewChild("Geometry", worldRoot.transform).transform;
            Transform roadsRoot = NewChild("EditableTerrainRoadNetwork", geometryRoot).transform;
            Transform riverRoot = NewChild("RiverAndBridge", geometryRoot).transform;
            Transform districtsRoot = NewChild("Districts_LowPolyWinter_NoVendorLand", worldRoot.transform).transform;
            Transform natureRoot = NewChild("BoundaryForest", worldRoot.transform).transform;
            Transform lightingRoot = NewChild("LightingAndVFX", worldRoot.transform).transform;
            Transform gameplayRoot = NewChild("Gameplay", worldRoot.transform).transform;

            Terrain terrain = BuildTerrain(terrainData, geometryRoot);
            BuildSnowBackdrop(geometryRoot);
            List<TerrainRoadPath> roads = BuildRoadNetwork(terrain, roadsRoot);
            BuildRiver(riverRoot);
            BuildBridge(riverRoot, targetScene);
            BuildDistricts(districtsRoot, terrain, targetScene);
            BuildBoundaryNature(natureRoot, terrain, targetScene);
            BuildLanterns(lightingRoot, terrain, targetScene);
            BuildLightingAndAtmosphere(lightingRoot, terrain, targetScene);
            BuildGameplay(gameplayRoot, terrain, targetScene);
            BuildBoundaries(gameplayRoot);
            ApplyToonMaterialOverrides(worldRoot);

            EditorSceneManager.MarkSceneDirty(targetScene);
            if (!EditorSceneManager.SaveScene(targetScene, ScenePath))
                throw new IOException("Failed to save " + ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = worldRoot;
            Debug.Log(
                $"[RoadFirstWinterWorldBuilder] Built {Zones.Length} glacier-free winter districts around " +
                $"{roads.Count} editable Terrain spline roads, one river bridge, the existing penguin and snowfall at {ScenePath}");
        }

        private static TerrainData GetOrCreateTerrainData()
        {
            TerrainData data = AssetDatabase.LoadAssetAtPath<TerrainData>(TerrainDataPath);
            if (data != null) return data;

            data = new TerrainData { name = "TD_RoadFirstWinterWorld" };
            AssetDatabase.CreateAsset(data, TerrainDataPath);
            return data;
        }

        private static void ConfigureTerrainData(TerrainData data)
        {
            // Third-person views get close enough to expose Terrain texel stair-stepping.
            // Keep the road fully Terrain-owned, but provide roughly 12 cm height/paint texels.
            data.heightmapResolution = 2049;
            data.alphamapResolution = 2048;
            data.baseMapResolution = 2048;
            data.size = new Vector3(MapHalfExtent * 2f, TerrainHeight, MapHalfExtent * 2f);

            int heightResolution = data.heightmapResolution;
            float[,] heights = new float[heightResolution, heightResolution];
            for (int z = 0; z < heightResolution; z++)
            {
                float worldZ = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, z / (float)(heightResolution - 1));
                for (int x = 0; x < heightResolution; x++)
                {
                    float worldX = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, x / (float)(heightResolution - 1));
                    heights[z, x] = Mathf.Clamp01(EvaluateBaseHeight(worldX, worldZ) / TerrainHeight);
                }
            }
            data.SetHeights(0, 0, heights);

            TerrainLayer[] baseLayers =
            {
                GetOrCreateMatteTerrainLayer(SnowLayerPath, "TL_RoadFirst_01_SnowWhite"),
                GetOrCreateMatteTerrainLayer(PackedSnowLayerPath, "TL_RoadFirst_02_PackedSnow"),
                GetOrCreateMatteTerrainLayer(ShadowSnowLayerPath, "TL_RoadFirst_03_ShadowSnow"),
                GetOrCreateMatteTerrainLayer(RockLayerPath, "TL_RoadFirst_04_RockGray"),
                GetOrCreateMatteTerrainLayer(PineLayerPath, "TL_RoadFirst_05_PineGround")
            };
            if (baseLayers.Any(layer => layer == null))
                throw new FileNotFoundException("One or more solid winter Terrain layers are missing.");
            data.terrainLayers = baseLayers;

            int alphaResolution = data.alphamapResolution;
            float[,,] alpha = new float[alphaResolution, alphaResolution, baseLayers.Length];
            for (int z = 0; z < alphaResolution; z++)
            {
                float nz = z / (float)(alphaResolution - 1);
                float worldZ = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, nz);
                for (int x = 0; x < alphaResolution; x++)
                {
                    float nx = x / (float)(alphaResolution - 1);
                    float worldX = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, nx);
                    float slope = data.GetSteepness(nx, nz);
                    float detailNoise = Mathf.PerlinNoise((worldX + 180f) * 0.045f, (worldZ + 230f) * 0.045f);
                    float rock = SmoothStep(20f, 38f, slope) * 0.82f;
                    float pine = (1f - SmoothStep(24f, 50f, Vector2.Distance(new Vector2(worldX, worldZ), new Vector2(-13f, 35f)))) * 0.20f;
                    float packed = 0.12f + detailNoise * 0.15f;
                    float shadow = 0.08f + (1f - detailNoise) * 0.10f;
                    float snow = Mathf.Max(0.08f, 1f - rock - pine - packed - shadow);
                    float sum = snow + packed + shadow + rock + pine;
                    alpha[z, x, 0] = snow / sum;
                    alpha[z, x, 1] = packed / sum;
                    alpha[z, x, 2] = shadow / sum;
                    alpha[z, x, 3] = rock / sum;
                    alpha[z, x, 4] = pine / sum;
                }
            }
            data.SetAlphamaps(0, 0, alpha);
            EditorUtility.SetDirty(data);
            AssetDatabase.SaveAssetIfDirty(data);
        }

        private static TerrainLayer GetOrCreateMatteTerrainLayer(string sourcePath, string assetName)
        {
            TerrainLayer source = AssetDatabase.LoadAssetAtPath<TerrainLayer>(sourcePath);
            if (source == null) return null;

            string path = TerrainLayersRoot + "/" + assetName + ".terrainlayer";
            TerrainLayer layer = AssetDatabase.LoadAssetAtPath<TerrainLayer>(path);
            if (layer == null)
            {
                layer = new TerrainLayer();
                AssetDatabase.CreateAsset(layer, path);
            }

            EditorUtility.CopySerialized(source, layer);
            layer.name = assetName;
            layer.metallic = 0f;
            layer.smoothness = 0f;
            layer.specular = Color.black;
            layer.normalMapTexture = null;
            layer.maskMapTexture = null;
            EditorUtility.SetDirty(layer);
            AssetDatabase.SaveAssetIfDirty(layer);
            return layer;
        }

        private static Terrain BuildTerrain(TerrainData data, Transform parent)
        {
            GameObject terrainObject = Terrain.CreateTerrainGameObject(data);
            terrainObject.name = "ContinuousSnowTerrain_RoadsArePaintedHere";
            terrainObject.transform.SetParent(parent, true);
            terrainObject.transform.position = new Vector3(-MapHalfExtent, 0f, -MapHalfExtent);
            terrainObject.isStatic = true;

            Terrain terrain = terrainObject.GetComponent<Terrain>();
            terrain.drawInstanced = true;
            terrain.heightmapPixelError = 1f;
            terrain.basemapDistance = 500f;
            terrain.shadowCastingMode = ShadowCastingMode.On;
            return terrain;
        }

        private static void BuildSnowBackdrop(Transform parent)
        {
            Material snowMaterial = GetOrCreateToonColorMaterial(
                OuterSnowMaterialPath,
                "M_RoadFirst_OuterSnow",
                new Color(0.48f, 0.61f, 0.80f),
                new Color(0.31f, 0.40f, 0.58f));
            Material mountainMaterial = GetOrCreateToonColorMaterial(
                BackdropMountainMaterialPath,
                "M_RoadFirst_BackdropMountain",
                new Color(0.20f, 0.29f, 0.46f),
                new Color(0.10f, 0.16f, 0.29f));
            if (snowMaterial == null || mountainMaterial == null) return;

            GameObject shelf = GameObject.CreatePrimitive(PrimitiveType.Plane);
            shelf.name = "OuterSnowShelf_NoCollision";
            shelf.transform.SetParent(parent, false);
            shelf.transform.localPosition = new Vector3(0f, -0.85f, 0f);
            shelf.transform.localScale = new Vector3(58f, 1f, 58f);
            shelf.GetComponent<MeshRenderer>().sharedMaterial = snowMaterial;
            UnityEngine.Object.DestroyImmediate(shelf.GetComponent<Collider>());

            Mesh mountainMesh = SaveOrUpdateMesh(
                CreateBackdropMountainMesh(),
                BackdropMountainMeshPath);
            Transform mountainRoot = NewChild("DistantSnowMountainRing", parent).transform;
            Vector3[] positions =
            {
                new Vector3(-100f, -1.0f, 274f), new Vector3(-35f, -1.0f, 282f),
                new Vector3(35f, -1.0f, 278f), new Vector3(105f, -1.0f, 271f),
                new Vector3(170f, -1.0f, 252f), new Vector3(238f, -1.0f, 184f),
                new Vector3(250f, -1.0f, 111f)
            };
            for (int i = 0; i < positions.Length; i++)
            {
                GameObject mountain = NewChild("BackdropMountain_" + (i + 1).ToString("00"), mountainRoot);
                mountain.transform.localPosition = positions[i];
                mountain.transform.localRotation = Quaternion.Euler(0f, i * 31f, 0f);
                float width = 16f + i % 4 * 2f;
                float height = 48f + i % 3 * 8f;
                mountain.transform.localScale = new Vector3(width, height, width);
                mountain.AddComponent<MeshFilter>().sharedMesh = mountainMesh;
                mountain.AddComponent<MeshRenderer>().sharedMaterial = mountainMaterial;
                mountain.isStatic = true;
            }
        }

        private static Material GetOrCreateToonColorMaterial(
            string path,
            string assetName,
            Color color,
            Color shadeColor)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find(ToonShaderName);
            if (shader == null) return null;
            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.color = color;
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_ShadeColor")) material.SetColor("_ShadeColor", shadeColor);
            if (material.HasProperty("_Bands")) material.SetFloat("_Bands", 3f);
            if (material.HasProperty("_AmbientStrength")) material.SetFloat("_AmbientStrength", 0.30f);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static Material GetOrCreateToonVariant(
            Material source,
            string path,
            string assetName,
            Color shadeColor,
            float bands,
            float ambientStrength)
        {
            if (source == null) return null;
            Shader shader = Shader.Find(ToonShaderName);
            if (shader == null) return null;

            Texture baseMap = source.HasProperty("_BaseMap")
                ? source.GetTexture("_BaseMap")
                : source.mainTexture;
            Color baseColor = source.HasProperty("_BaseColor")
                ? source.GetColor("_BaseColor")
                : source.color;
            Vector2 textureScale = source.HasProperty("_BaseMap")
                ? source.GetTextureScale("_BaseMap")
                : source.mainTextureScale;
            Vector2 textureOffset = source.HasProperty("_BaseMap")
                ? source.GetTextureOffset("_BaseMap")
                : source.mainTextureOffset;

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = assetName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.shader = shader;
            material.SetTexture("_BaseMap", baseMap);
            material.SetTextureScale("_BaseMap", textureScale);
            material.SetTextureOffset("_BaseMap", textureOffset);
            material.SetColor("_BaseColor", baseColor);
            material.SetColor("_ShadeColor", shadeColor);
            material.SetFloat("_Bands", bands);
            material.SetFloat("_AmbientStrength", ambientStrength);
            EditorUtility.SetDirty(material);
            AssetDatabase.SaveAssetIfDirty(material);
            return material;
        }

        private static void ApplyToonMaterialOverrides(GameObject worldRoot)
        {
            Material source = AssetDatabase.LoadAssetAtPath<Material>(VendorMainMaterialPath);
            Material toon = GetOrCreateToonVariant(
                source,
                ToonWorldMaterialPath,
                "M_RoadFirst_ToonLowPolyWinter",
                new Color(0.32f, 0.40f, 0.58f),
                3f,
                0.36f);
            if (source == null || toon == null) return;

            Renderer[] renderers = worldRoot.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in renderers)
            {
                Material[] materials = renderer.sharedMaterials;
                bool changed = false;
                for (int i = 0; i < materials.Length; i++)
                {
                    if (materials[i] != source) continue;
                    materials[i] = toon;
                    changed = true;
                }
                if (changed) renderer.sharedMaterials = materials;
            }
        }

        private static List<TerrainRoadPath> BuildRoadNetwork(Terrain terrain, Transform parent)
        {
            TerrainLayer roadLayer = GetOrCreateMatteTerrainLayer(
                RoadLayerSourcePath,
                "TL_RoadFirst_06_WinterRoad");
            TerrainLayer borderLayer = GetOrCreateMatteTerrainLayer(
                BorderLayerSourcePath,
                "TL_RoadFirst_07_RoadBorder");
            if (roadLayer == null || borderLayer == null)
                throw new InvalidOperationException("Terrain road layers could not be created.");

            List<TerrainRoadPath> paths = new(Roads.Length);
            foreach (RoadSpec spec in Roads)
            {
                GameObject roadObject = NewChild(spec.Name, parent);
                TerrainRoadPath path = roadObject.AddComponent<TerrainRoadPath>();
                path.Configure(spec.Points, spec.Width, 0.32f, 1.35f, 0.28f, terrain, roadLayer, borderLayer);
                paths.Add(path);
            }

            TerrainRoadBaseline baseline = RoadBuilderAssets.RecaptureTerrainBaseline(terrain);
            if (baseline == null)
                throw new InvalidOperationException("Could not capture the road-first Terrain baseline.");

            bool graded = TerrainRoadGrader.GradeTerrain(
                terrain, baseline, paths, 11.5f, 4.0f, "Build Road-First Terrain", false);
            bool painted = graded && TerrainRoadPainter.RebuildTerrainRoads(
                terrain, roadLayer, borderLayer, paths, "Paint Road-First Network", false);
            if (!graded || !painted)
                throw new InvalidOperationException("The Terrain road network failed to grade or paint.");
            return paths;
        }

        private static void BuildRiver(Transform parent)
        {
            List<Vector3> samples = SampleCurve(RiverPoints, 12)
                .Select(point => new Vector3(point.x, WaterLevel, point.y))
                .ToList();
            Mesh mesh = CreateRibbonMesh("MSH_RoadFirst_River", samples, 8.6f);
            Mesh saved = SaveOrUpdateMesh(mesh, RiverMeshPath);

            GameObject river = NewChild("ModestRiver", parent);
            river.AddComponent<MeshFilter>().sharedMesh = saved;
            Material material = GetOrCreateToonVariant(
                AssetDatabase.LoadAssetAtPath<Material>(WaterMaterialPath),
                ToonWaterMaterialPath,
                "M_RoadFirst_ToonWater",
                new Color(0.08f, 0.25f, 0.47f),
                3f,
                0.34f);
            if (material != null) river.AddComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static void BuildBridge(Transform parent, Scene targetScene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(VendorBridgePath);
            if (prefab == null) return;

            GameObject bridge = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (bridge == null) return;
            if (bridge.scene != targetScene) SceneManager.MoveGameObjectToScene(bridge, targetScene);
            bridge.name = "Bridge_RiversideAccess";
            bridge.transform.SetParent(parent, false);
            bridge.transform.localPosition = new Vector3(19f, WaterLevel + 0.18f, -62f);
            bridge.transform.localRotation = Quaternion.Euler(0f, 151f, 0f);
            bridge.transform.localScale = Vector3.one * 1.35f;
        }

        private static void BuildDistricts(Transform parent, Terrain terrain, Scene targetScene)
        {
            foreach (ZoneSpec zone in Zones)
            {
                string path = VendorEnvironmentRoot + "/" + zone.PrefabName + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                if (prefab == null)
                {
                    Debug.LogWarning("[RoadFirstWinterWorldBuilder] Missing district prefab: " + path);
                    continue;
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null) continue;
                if (instance.scene != targetScene) SceneManager.MoveGameObjectToScene(instance, targetScene);
                instance.name = zone.Name;
                instance.transform.SetParent(parent, false);
                instance.transform.localScale = Vector3.one * zone.Scale;
                instance.transform.localRotation = Quaternion.Euler(0f, zone.Yaw, 0f);
                float groundY = terrain.SampleHeight(new Vector3(zone.Center.x, 0f, zone.Center.y)) + terrain.transform.position.y;
                instance.transform.localPosition = new Vector3(
                    zone.Center.x,
                    groundY - zone.SourceGroundY * zone.Scale,
                    zone.Center.y);
                DisableVendorGroundWaterAndRoads(instance.transform);
            }
        }

        private static void DisableVendorGroundWaterAndRoads(Transform instance)
        {
            foreach (Transform child in instance)
            {
                string lowered = child.name.ToLowerInvariant();
                bool isGround = lowered.StartsWith("land ", StringComparison.Ordinal);
                bool isWater = lowered.StartsWith("water ", StringComparison.Ordinal);
                bool isRoad = lowered == "road" ||
                              lowered.Contains("pedestrian road") ||
                              lowered.StartsWith("road ", StringComparison.Ordinal) ||
                              lowered.StartsWith("winter road", StringComparison.Ordinal) ||
                              lowered.StartsWith("sidewalk", StringComparison.Ordinal);
                if (isGround || isWater || isRoad) child.gameObject.SetActive(false);
            }
        }

        private static void BuildBoundaryNature(Transform parent, Terrain terrain, Scene targetScene)
        {
            string[] prefabPaths = AssetDatabase.FindAssets("t:Prefab", new[] { NatureRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (prefabPaths.Length == 0) return;

            System.Random random = new(24082026);
            int placed = 0;
            int attempts = 0;
            while (placed < 230 && attempts++ < 5600)
            {
                bool edge = random.NextDouble() < 0.58;
                float x;
                float z;
                if (edge)
                {
                    bool vertical = random.NextDouble() < 0.5;
                    if (vertical)
                    {
                        x = Mathf.Lerp(-116f, 116f, (float)random.NextDouble());
                        z = (random.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Lerp(91f, 116f, (float)random.NextDouble());
                    }
                    else
                    {
                        x = (random.NextDouble() < 0.5 ? -1f : 1f) * Mathf.Lerp(91f, 116f, (float)random.NextDouble());
                        z = Mathf.Lerp(-116f, 116f, (float)random.NextDouble());
                    }
                }
                else
                {
                    x = Mathf.Lerp(-112f, 112f, (float)random.NextDouble());
                    z = Mathf.Lerp(-112f, 112f, (float)random.NextDouble());
                }

                Vector2 candidate = new(x, z);
                if (Zones.Any(zone => Vector2.Distance(candidate, zone.Center) < zone.PadRadius + 5f)) continue;
                if (Roads.Any(road => DistanceToPolyline(candidate, road.Points) < road.Width * 0.5f + 4.0f)) continue;
                if (DistanceToPolyline(candidate, RiverPoints) < 9.5f) continue;

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[placed % prefabPaths.Length]);
                GameObject tree = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (tree == null) continue;
                if (tree.scene != targetScene) SceneManager.MoveGameObjectToScene(tree, targetScene);
                tree.name = "WinterTree_" + (placed + 1).ToString("000");
                tree.transform.SetParent(parent, false);
                tree.transform.localPosition = new Vector3(x, terrain.SampleHeight(new Vector3(x, 0f, z)), z);
                tree.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                float scale = Mathf.Lerp(0.86f, 1.30f, (float)random.NextDouble());
                tree.transform.localScale *= scale;
                placed++;
            }
        }

        private static void BuildLanterns(Transform parent, Terrain terrain, Scene targetScene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefabPath);
            if (prefab == null) return;

            for (int i = 0; i < LanternPositions.Length; i++)
            {
                Vector3 source = LanternPositions[i];
                GameObject lantern = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (lantern == null) continue;
                if (lantern.scene != targetScene) SceneManager.MoveGameObjectToScene(lantern, targetScene);
                lantern.name = "RoadLantern_" + (i + 1).ToString("00");
                lantern.transform.SetParent(parent, false);
                lantern.transform.localPosition = new Vector3(
                    source.x,
                    terrain.SampleHeight(source) + terrain.transform.position.y,
                    source.z);
                lantern.transform.localRotation = Quaternion.Euler(0f, i * 53f % 360f, 0f);
            }
        }

        private static void BuildLightingAndAtmosphere(Transform parent, Terrain terrain, Scene targetScene)
        {
            GameObject keyObject = NewChild("MoonKeyLight", parent);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.78f, 0.87f, 1f);
            key.intensity = 1.05f;
            key.shadows = LightShadows.Soft;
            keyObject.transform.rotation = Quaternion.Euler(48f, -38f, 0f);

            foreach (ZoneSpec zone in Zones)
            {
                GameObject poolObject = NewChild("WarmPool_" + zone.Name, parent);
                poolObject.transform.position = new Vector3(zone.Center.x, zone.TargetHeight + 6f, zone.Center.y);
                Light pool = poolObject.AddComponent<Light>();
                pool.type = LightType.Point;
                pool.color = new Color(1f, 0.53f, 0.20f);
                pool.intensity = zone.Name.Contains("Santa") || zone.Name.Contains("Fair") ? 3.0f : 1.65f;
                pool.range = zone.Name.Contains("Santa") || zone.Name.Contains("Fair") ? 25f : 19f;
                pool.shadows = LightShadows.None;
            }

            GameObject snowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowfallPrefabPath);
            if (snowPrefab != null)
            {
                GameObject snow = PrefabUtility.InstantiatePrefab(snowPrefab) as GameObject;
                if (snow != null)
                {
                    if (snow.scene != targetScene) SceneManager.MoveGameObjectToScene(snow, targetScene);
                    snow.name = "WorldSnowfall";
                    snow.transform.SetParent(parent, false);
                    snow.transform.localPosition = new Vector3(0f, 34f, 0f);
                }
            }

            GameObject cameraObject = NewChild("ConceptOverviewCamera", parent);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.enabled = false;
            camera.orthographic = true;
            camera.orthographicSize = 108f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 600f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
            cameraObject.transform.position = new Vector3(-132f, 188f, -174f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 8f, -2f) - cameraObject.transform.position, Vector3.up);

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.30f, 0.40f, 0.61f);
            RenderSettings.ambientEquatorColor = new Color(0.20f, 0.28f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.14f, 0.23f);
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.25f, 0.36f, 0.56f);
            RenderSettings.fogStartDistance = 190f;
            RenderSettings.fogEndDistance = 470f;
            Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (skybox != null) RenderSettings.skybox = skybox;
        }

        private static void BuildGameplay(Transform parent, Terrain terrain, Scene targetScene)
        {
            Vector3 spawnPosition = new(-108f, 0f, -97f);
            spawnPosition.y = terrain.SampleHeight(spawnPosition) + terrain.transform.position.y + 0.9f;
            GameObject spawn = NewChild("PenguinSpawn", parent);
            spawn.transform.position = spawnPosition;
            spawn.transform.rotation = Quaternion.Euler(0f, 48f, 0f);

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PenguinPrefabPath);
            if (prefab == null) return;
            GameObject penguin = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (penguin == null) return;
            if (penguin.scene != targetScene) SceneManager.MoveGameObjectToScene(penguin, targetScene);
            penguin.name = "PlayerPenguin";
            penguin.transform.SetParent(parent, false);
            penguin.transform.position = spawnPosition;
            penguin.transform.rotation = spawn.transform.rotation;
        }

        private static void BuildBoundaries(Transform parent)
        {
            Transform root = NewChild("InvisibleBoundary", parent).transform;
            CreateBoundary("North", root, new Vector3(0f, 13f, MapHalfExtent + 1f), new Vector3(MapHalfExtent * 2f, 30f, 2f));
            CreateBoundary("South", root, new Vector3(0f, 13f, -MapHalfExtent - 1f), new Vector3(MapHalfExtent * 2f, 30f, 2f));
            CreateBoundary("East", root, new Vector3(MapHalfExtent + 1f, 13f, 0f), new Vector3(2f, 30f, MapHalfExtent * 2f));
            CreateBoundary("West", root, new Vector3(-MapHalfExtent - 1f, 13f, 0f), new Vector3(2f, 30f, MapHalfExtent * 2f));
        }

        private static void CreateBoundary(string name, Transform parent, Vector3 position, Vector3 size)
        {
            GameObject boundary = NewChild(name, parent);
            boundary.transform.position = position;
            boundary.AddComponent<BoxCollider>().size = size;
        }

        private static float EvaluateBaseHeight(float x, float z)
        {
            float north = Smooth01(Mathf.InverseLerp(-110f, 108f, z));
            float height = Mathf.Lerp(2.25f, 19.8f, north);
            height += (Mathf.PerlinNoise((x + 260f) * 0.026f, (z + 210f) * 0.026f) - 0.5f) * 1.15f;
            height += 1.8f * Mathf.Exp(-((x + 58f) * (x + 58f)) / 3000f - ((z - 58f) * (z - 58f)) / 2100f);
            height += 2.2f * Mathf.Exp(-((x - 76f) * (x - 76f)) / 2500f - ((z - 78f) * (z - 78f)) / 1800f);

            float edge = Mathf.Max(
                SmoothStep(0.80f, 1f, Mathf.Abs(x) / MapHalfExtent),
                SmoothStep(0.84f, 1f, Mathf.Abs(z) / MapHalfExtent));
            height += edge * 2.8f;

            foreach (ZoneSpec zone in Zones)
            {
                float distance = Vector2.Distance(new Vector2(x, z), zone.Center);
                float innerRadius = zone.PadRadius * 0.74f;
                float weight = 1f - SmoothStep(innerRadius, zone.PadRadius + 16f, distance);
                height = Mathf.Lerp(height, zone.TargetHeight, weight);
            }

            float riverDistance = DistanceToPolyline(new Vector2(x, z), RiverPoints);
            float riverWeight = 1f - SmoothStep(5.2f, 12.0f, riverDistance);
            height = Mathf.Lerp(height, WaterLevel - 0.95f, riverWeight);
            return Mathf.Clamp(height, 0.35f, TerrainHeight - 1f);
        }

        private static List<Vector2> SampleCurve(IReadOnlyList<Vector2> points, int subdivisionsPerSegment)
        {
            List<Vector2> result = new();
            for (int segment = 0; segment < points.Count - 1; segment++)
            {
                Vector2 p0 = points[Mathf.Max(0, segment - 1)];
                Vector2 p1 = points[segment];
                Vector2 p2 = points[segment + 1];
                Vector2 p3 = points[Mathf.Min(points.Count - 1, segment + 2)];
                for (int step = 0; step <= subdivisionsPerSegment; step++)
                {
                    if (segment > 0 && step == 0) continue;
                    float t = step / (float)subdivisionsPerSegment;
                    result.Add(0.5f * ((2f * p1) + (-p0 + p2) * t +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t * t +
                        (-p0 + 3f * p1 - 3f * p2 + p3) * t * t * t));
                }
            }
            return result;
        }

        private static Mesh CreateRibbonMesh(string name, IReadOnlyList<Vector3> samples, float width)
        {
            Vector3[] vertices = new Vector3[samples.Count * 2];
            Vector2[] uv = new Vector2[vertices.Length];
            int[] triangles = new int[(samples.Count - 1) * 6];
            float distance = 0f;
            for (int i = 0; i < samples.Count; i++)
            {
                Vector3 previous = samples[Mathf.Max(0, i - 1)];
                Vector3 next = samples[Mathf.Min(samples.Count - 1, i + 1)];
                Vector3 tangent = next - previous;
                tangent.y = 0f;
                tangent.Normalize();
                float startTaper = Mathf.SmoothStep(0f, 1f, i / 7f);
                float endTaper = Mathf.SmoothStep(0f, 1f, (samples.Count - 1 - i) / 7f);
                float taperedWidth = width * Mathf.Min(startTaper, endTaper);
                Vector3 side = new(-tangent.z * taperedWidth * 0.5f, 0f, tangent.x * taperedWidth * 0.5f);
                vertices[i * 2] = samples[i] - side;
                vertices[i * 2 + 1] = samples[i] + side;
                if (i > 0) distance += Vector3.Distance(samples[i - 1], samples[i]);
                uv[i * 2] = new Vector2(0f, distance / 7f);
                uv[i * 2 + 1] = new Vector2(1f, distance / 7f);
            }

            int triangle = 0;
            for (int i = 0; i < samples.Count - 1; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[triangle++] = a;
                triangles[triangle++] = b;
                triangles[triangle++] = c;
                triangles[triangle++] = b;
                triangles[triangle++] = d;
                triangles[triangle++] = c;
            }

            Mesh mesh = new() { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateBackdropMountainMesh()
        {
            const int segments = 10;
            Vector3[] vertices = new Vector3[segments * 2 + 1];
            int[] triangles = new int[segments * 9];
            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float uneven = 0.91f + (i % 3) * 0.055f;
                vertices[i] = new Vector3(Mathf.Cos(angle) * uneven, 0f, Mathf.Sin(angle) * uneven);
                float shoulder = 0.61f + (i % 2) * 0.07f;
                vertices[segments + i] = new Vector3(
                    Mathf.Cos(angle) * shoulder,
                    0.50f + (i % 3) * 0.035f,
                    Mathf.Sin(angle) * shoulder);
            }
            vertices[^1] = new Vector3(0.09f, 1f, -0.05f);

            int triangle = 0;
            int peak = vertices.Length - 1;
            for (int i = 0; i < segments; i++)
            {
                int next = (i + 1) % segments;
                int lower = i;
                int lowerNext = next;
                int upper = segments + i;
                int upperNext = segments + next;
                triangles[triangle++] = lower;
                triangles[triangle++] = upper;
                triangles[triangle++] = lowerNext;
                triangles[triangle++] = lowerNext;
                triangles[triangle++] = upper;
                triangles[triangle++] = upperNext;
                triangles[triangle++] = upper;
                triangles[triangle++] = peak;
                triangles[triangle++] = upperNext;
            }

            Mesh mesh = new() { name = "MSH_RoadFirst_BackdropMountain" };
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh SaveOrUpdateMesh(Mesh source, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(source, path);
                return source;
            }

            EditorUtility.CopySerialized(source, existing);
            UnityEngine.Object.DestroyImmediate(source);
            EditorUtility.SetDirty(existing);
            AssetDatabase.SaveAssetIfDirty(existing);
            return existing;
        }

        private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> line)
        {
            float minimum = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
                minimum = Mathf.Min(minimum, DistanceToSegment(point, line[i], line[i + 1]));
            return minimum;
        }

        private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector3> line)
        {
            float minimum = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
                minimum = Mathf.Min(minimum,
                    DistanceToSegment(point, new Vector2(line[i].x, line[i].z), new Vector2(line[i + 1].x, line[i + 1].z)));
            return minimum;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float denominator = Mathf.Max(0.0001f, Vector2.Dot(delta, delta));
            float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / denominator);
            return Vector2.Distance(point, a + delta * t);
        }

        private static float Smooth01(float value)
        {
            float t = Mathf.Clamp01(value);
            return t * t * (3f - 2f * t);
        }

        private static float SmoothStep(float start, float end, float value)
        {
            return Smooth01(Mathf.InverseLerp(start, end, value));
        }

        private static GameObject NewSceneObject(string name, Scene scene)
        {
            GameObject gameObject = new(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            GameObject gameObject = new(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static void EnsureFolder(string path)
        {
            if (string.IsNullOrEmpty(path) || AssetDatabase.IsValidFolder(path)) return;
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
