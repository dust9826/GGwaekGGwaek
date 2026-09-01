using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack.InGame.Map.WinterVillage.Editor
{
    /// <summary>
    /// Rebuilds the connected winter-world concept from one project-owned painted terrain mesh
    /// and read-only Low Poly Winter composition prefabs. Vendor assets are never modified.
    /// </summary>
    public static class ConnectedWinterWorldBuilder
    {
        private const string MenuPath = "PPack/Map/Build Connected Winter World Concept";
        private const string CaptureMenuPath = "PPack/Map/Capture Connected Winter World QA Views";
        private const string ScenePath = "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_ConnectedWorld_Concept.unity";
        private const string PreviewRoot = "Assets/Game/InGame/Map/WinterVillage/Preview/ConnectedWorld/FinalQA";
        private const string GeneratedRoot = "Assets/Game/InGame/Map/WinterVillage/Generated/ConnectedWorld";
        private const string MaterialsRoot = "Assets/Game/InGame/Map/WinterVillage/Materials/ConnectedWorld";
        private const string VendorEnvironmentRoot = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Locations with Env/Low Poly Winter";
        private const string VendorBridgePath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Bridges/winter bridge.prefab";
        private const string SnowfallPrefabPath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/VFX/PF_WinterSnowfall.prefab";
        private const string LanternPrefabPath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Lighting/PF_WinterLantern_Glow.prefab";
        private const string VehiclePrefabPath = "Assets/Game/InGame/Vehicle/Prefabs/PF_VehicleProto.prefab";
        private const string SkyboxMaterialPath = "Assets/Game/InGame/Map/WinterVillage/Lighting/Materials/M_WinterVillage_BlueHourSky.mat";

        private const float MapHalfExtent = 116f;
        private const float WaterLevel = 2.15f;
        private const float BridgeHalfLength = 8.55f;
        private const float BridgeApproachLength = 9.0f;
        private const float BridgeDeckSurfaceHeight = WaterLevel + 0.51f;

        private sealed class ZoneSpec
        {
            public string Name;
            public string PrefabName;
            public Vector2 Center;
            public float GroundY;
            public float SourceGroundY;
            public float Scale;
            public float Yaw;
            public bool KeepLand;
            public float PadRadius;
        }

        private sealed class RouteSpec
        {
            public string Name;
            public float Width;
            public Vector2[] Points;
        }

        private sealed class QaViewSpec
        {
            public string Name;
            public Vector3 Position;
            public Vector3 Target;
        }

        private static readonly ZoneSpec[] Zones =
        {
            new ZoneSpec { Name = "01_ChristmasFair", PrefabName = "christmass fair with env", Center = new Vector2(-68f, -72f), GroundY = 0.7f, SourceGroundY = 3.35f, Scale = 0.82f, Yaw = 8f, PadRadius = 19f },
            new ZoneSpec { Name = "02_WinterTown", PrefabName = "winter town with env", Center = new Vector2(-18f, -72f), GroundY = 0.9f, SourceGroundY = 4.05f, Scale = 0.82f, Yaw = -5f, PadRadius = 19f },
            new ZoneSpec { Name = "03_RiverHouse", PrefabName = "river winter house with env", Center = new Vector2(34f, -84f), GroundY = 1.0f, SourceGroundY = 4.10f, Scale = 0.82f, Yaw = -12f, PadRadius = 18f },
            new ZoneSpec { Name = "04_CountryHouse", PrefabName = "country winter house with env", Center = new Vector2(78f, -62f), GroundY = 1.7f, SourceGroundY = 4.15f, Scale = 0.80f, Yaw = 18f, PadRadius = 18f },
            new ZoneSpec { Name = "05_ChristmasVillage", PrefabName = "christmas village with env", Center = new Vector2(-70f, -23f), GroundY = 2.0f, SourceGroundY = 4.05f, Scale = 0.82f, Yaw = -15f, PadRadius = 19f },
            new ZoneSpec { Name = "06_WinterRoadForest", PrefabName = "winter road forest with env", Center = new Vector2(30f, -24f), GroundY = 4.2f, SourceGroundY = 4.00f, Scale = 0.84f, Yaw = 10f, PadRadius = 19f },
            new ZoneSpec { Name = "07_SantaDistrict", PrefabName = "santa claus house with env", Center = new Vector2(-29f, 30f), GroundY = 7.8f, SourceGroundY = 4.10f, Scale = 0.86f, Yaw = 6f, PadRadius = 20f },
            new ZoneSpec { Name = "08_SkiResort", PrefabName = "ski resort with env", Center = new Vector2(6f, 68f), GroundY = 13.2f, SourceGroundY = 3.85f, Scale = 0.88f, Yaw = -10f, KeepLand = true, PadRadius = 21f },
            new ZoneSpec { Name = "09_NorthPoleCamp", PrefabName = "life north pole with env", Center = new Vector2(53f, 34f), GroundY = 10.7f, SourceGroundY = 4.00f, Scale = 0.86f, Yaw = 14f, KeepLand = true, PadRadius = 20f },
            new ZoneSpec { Name = "10_CrystalMountain", PrefabName = "crystal mountain with env", Center = new Vector2(69f, 82f), GroundY = 18.0f, SourceGroundY = 3.80f, Scale = 0.92f, Yaw = -8f, KeepLand = true, PadRadius = 22f }
        };

        private static readonly Vector2[] RiverPoints =
        {
            new Vector2(-108f, 8f),
            new Vector2(-92f, 2f),
            new Vector2(-62f, 10f),
            new Vector2(-28f, 4f),
            new Vector2(4f, 13f),
            new Vector2(36f, 7f),
            new Vector2(72f, 14f),
            new Vector2(108f, 8f)
        };

        private static readonly RouteSpec[] Routes =
        {
            new RouteSpec
            {
                Name = "Route_ValleyVillage",
                Width = 6.4f,
                Points = new[]
                {
                    new Vector2(-112f, -91f), new Vector2(-88f, -58f), new Vector2(-65f, -49f),
                    new Vector2(-36f, -49f), new Vector2(-5f, -50f), new Vector2(25f, -58f),
                    new Vector2(53f, -53f), new Vector2(77f, -41f), new Vector2(65f, -26f),
                    new Vector2(42f, -13f), new Vector2(20f, -7f), new Vector2(-9f, -7f),
                    new Vector2(-38f, -2f), new Vector2(-67f, -3f), new Vector2(-101f, -17f)
                }
            },
            new RouteSpec
            {
                Name = "Route_MountainSpine",
                Width = 5.8f,
                Points = new[]
                {
                    new Vector2(30f, -24f), new Vector2(23f, -8f), new Vector2(14f, 5f),
                    new Vector2(9f, 17f), new Vector2(-7f, 26f), new Vector2(-29f, 30f),
                    new Vector2(-18f, 43f), new Vector2(-5f, 56f), new Vector2(6f, 68f),
                    new Vector2(27f, 74f), new Vector2(48f, 79f), new Vector2(69f, 82f),
                    new Vector2(96f, 93f)
                }
            },
            new RouteSpec
            {
                Name = "Route_NorthPoleBranch",
                Width = 5.6f,
                Points = new[]
                {
                    new Vector2(77f, -41f), new Vector2(82f, -23f), new Vector2(78f, -6f),
                    new Vector2(68f, 12f), new Vector2(58f, 27f), new Vector2(53f, 34f),
                    new Vector2(68f, 40f), new Vector2(96f, 38f)
                }
            },
            new RouteSpec
            {
                Name = "Route_SantaConnector",
                Width = 5.2f,
                Points = new[]
                {
                    new Vector2(-67f, -3f), new Vector2(-57f, 6f), new Vector2(-49f, 17f),
                    new Vector2(-40f, 25f), new Vector2(-29f, 30f)
                }
            },
            new RouteSpec
            {
                Name = "Access_ChristmasFair",
                Width = 3.4f,
                Points = new[] { new Vector2(-65f, -49f), new Vector2(-66f, -57f), new Vector2(-68f, -64f) }
            },
            new RouteSpec
            {
                Name = "Access_WinterTown",
                Width = 3.4f,
                Points = new[] { new Vector2(-18f, -50f), new Vector2(-18f, -57f), new Vector2(-18f, -64f) }
            },
            new RouteSpec
            {
                Name = "Access_RiverHouse",
                Width = 3.4f,
                Points = new[] { new Vector2(25f, -58f), new Vector2(30f, -68f), new Vector2(34f, -76f) }
            },
            new RouteSpec
            {
                Name = "Access_CountryHouse",
                Width = 3.4f,
                Points = new[] { new Vector2(77f, -41f), new Vector2(78f, -49f), new Vector2(78f, -56f) }
            },
            new RouteSpec
            {
                Name = "Access_ChristmasVillage",
                Width = 3.4f,
                Points = new[] { new Vector2(-67f, -3f), new Vector2(-68f, -11f), new Vector2(-70f, -16f) }
            }
        };

        private static readonly Vector3[] LanternPositions =
        {
            new Vector3(-91f, 0f, -57f), new Vector3(-50f, 0f, -49f), new Vector3(-18f, 0f, -50f),
            new Vector3(15f, 0f, -57f), new Vector3(52f, 0f, -53f), new Vector3(75f, 0f, -35f),
            new Vector3(34f, 0f, -11f), new Vector3(10f, 0f, 16f), new Vector3(-20f, 0f, 30f),
            new Vector3(-10f, 0f, 50f), new Vector3(19f, 0f, 72f), new Vector3(53f, 0f, 79f),
            new Vector3(79f, 0f, -7f), new Vector3(62f, 0f, 24f), new Vector3(-52f, 0f, 12f)
        };

        private static readonly Vector3[] BridgePlacements =
        {
            new Vector3(-51f, 0f, 8f),
            new Vector3(11f, 0f, 12f),
            new Vector3(69f, 0f, 12f)
        };

        private static readonly float[] BridgeYaws = { 35f, -23f, -25f };

        [MenuItem(MenuPath)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                throw new InvalidOperationException("Stop Play Mode before rebuilding the connected winter world.");
            }

            Scene currentScene = SceneManager.GetActiveScene();
            if (currentScene.IsValid() && currentScene.isDirty && currentScene.path != ScenePath)
            {
                throw new InvalidOperationException("The active scene has unsaved changes. Save it before rebuilding the connected world.");
            }

            EnsureFolder(GeneratedRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(Path.GetDirectoryName(ScenePath).Replace('\\', '/'));
            CleanLegacyRouteAssets();

            Texture2D roadMask = CreateOrUpdateRoadMaskTexture(
                GeneratedRoot + "/TX_ConnectedWorld_RoadMask.asset");
            Material groundMaterial = CreateOrUpdatePaintedTerrainMaterial(
                MaterialsRoot + "/M_ConnectedWorld_PaintedTerrain.mat", roadMask);
            Material waterMaterial = CreateOrUpdateLitMaterial(
                MaterialsRoot + "/M_ConnectedWorld_IceRiver.mat",
                new Color(0.06f, 0.42f, 0.72f), 0.08f, 0.86f);

            Scene targetScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            targetScene.name = "WinterVillage_ConnectedWorld_Concept";

            GameObject worldRoot = NewSceneObject("ConnectedWinterWorld", targetScene);
            GameObject geometryRoot = NewChild("Geometry", worldRoot.transform);
            GameObject districtRoot = NewChild("Districts_From_LowPolyWinter_Samples", worldRoot.transform);
            GameObject routesRoot = NewChild("PaintedRouteGuides_NoGeometry", geometryRoot.transform);
            GameObject landmarksRoot = NewChild("Landmarks", worldRoot.transform);
            GameObject natureRoot = NewChild("BoundaryNature", worldRoot.transform);
            GameObject gameplayRoot = NewChild("Gameplay", worldRoot.transform);
            GameObject lightingRoot = NewChild("LightingAndVFX", worldRoot.transform);

            BuildTerrain(geometryRoot.transform, groundMaterial);
            BuildRiver(geometryRoot.transform, waterMaterial);
            BuildRouteGuides(routesRoot.transform);
            BuildDistricts(districtRoot.transform, targetScene);
            BuildBridges(landmarksRoot.transform, targetScene);
            BuildBoundaryNature(natureRoot.transform, targetScene);
            BuildLanterns(lightingRoot.transform, targetScene);
            BuildLightingAndAtmosphere(lightingRoot.transform, targetScene);
            BuildGameplay(gameplayRoot.transform, targetScene);
            BuildBoundaries(gameplayRoot.transform);

            RemoveMissingScripts(worldRoot);
            EditorSceneManager.MarkSceneDirty(targetScene);
            if (!EditorSceneManager.SaveScene(targetScene, ScenePath))
            {
                throw new IOException("Failed to save " + ScenePath);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = worldRoot;
            if (SceneView.lastActiveSceneView != null)
            {
                SceneView.lastActiveSceneView.FrameSelected();
            }

            Debug.Log("[ConnectedWinterWorldBuilder] Built 10 linked winter districts with 9 routes painted directly into one continuous terrain/collider, plus river, bridges, lighting, snow and gameplay spawn at " + ScenePath);
        }

        [MenuItem(CaptureMenuPath)]
        public static void CaptureQaViews()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
            {
                throw new InvalidOperationException("Open the connected winter world scene before capturing QA views.");
            }

            GameObject cameraObject = GameObject.Find("ConnectedWinterWorld/LightingAndVFX/Main Camera");
            Camera camera = cameraObject != null ? cameraObject.GetComponent<Camera>() : null;
            if (camera == null)
            {
                throw new InvalidOperationException("Connected-world camera was not found.");
            }

            EnsureFolder(PreviewRoot);
            Vector3 originalPosition = camera.transform.position;
            Quaternion originalRotation = camera.transform.rotation;
            bool originalOrthographic = camera.orthographic;
            float originalOrthographicSize = camera.orthographicSize;
            float originalFieldOfView = camera.fieldOfView;

            try
            {
                CaptureCameraPng(camera, PreviewRoot + "/00_Isometric_Overview.png");
                QaViewSpec[] views =
                {
                    new QaViewSpec { Name = "01_SouthWest_LowAngle", Position = new Vector3(-113f, 17f, -102f), Target = new Vector3(-45f, 2f, -54f) },
                    new QaViewSpec { Name = "02_SouthEast_LowAngle", Position = new Vector3(110f, 18f, -108f), Target = new Vector3(45f, 3f, -52f) },
                    new QaViewSpec { Name = "03_CentralBridges_LowAngle", Position = new Vector3(-82f, 18f, -24f), Target = new Vector3(8f, 4f, 7f) },
                    new QaViewSpec { Name = "04_NorthWest_LowAngle", Position = new Vector3(-105f, 29f, 5f), Target = new Vector3(-13f, 10f, 52f) },
                    new QaViewSpec { Name = "05_NorthEast_LowAngle", Position = new Vector3(115f, 32f, 18f), Target = new Vector3(54f, 14f, 63f) },
                    new QaViewSpec { Name = "06_WestBridge_Close", Position = new Vector3(-74f, 8.5f, -5f), Target = new Vector3(-51f, 2.7f, 8f) },
                    new QaViewSpec { Name = "07_CentralBridge_Close", Position = new Vector3(-12f, 8.5f, -2f), Target = new Vector3(11f, 2.7f, 12f) },
                    new QaViewSpec { Name = "08_EastBridge_Close", Position = new Vector3(45f, 8.5f, -2f), Target = new Vector3(69f, 2.7f, 12f) }
                };

                camera.orthographic = false;
                camera.fieldOfView = 42f;
                foreach (QaViewSpec view in views)
                {
                    camera.transform.position = view.Position;
                    camera.transform.rotation = Quaternion.LookRotation(view.Target - view.Position, Vector3.up);
                    CaptureCameraPng(camera, PreviewRoot + "/" + view.Name + ".png");
                }
            }
            finally
            {
                camera.transform.position = originalPosition;
                camera.transform.rotation = originalRotation;
                camera.orthographic = originalOrthographic;
                camera.orthographicSize = originalOrthographicSize;
                camera.fieldOfView = originalFieldOfView;
                EditorSceneManager.SaveScene(scene);
                AssetDatabase.Refresh();
            }

            Debug.Log("[ConnectedWinterWorldBuilder] Captured isometric plus eight low-angle QA views at " + PreviewRoot);
        }

        private static void CaptureCameraPng(Camera camera, string assetPath)
        {
            const int width = 1600;
            const int height = 900;
            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture previousTarget = camera.targetTexture;
            try
            {
                camera.targetTexture = renderTexture;
                camera.Render();
                RenderTexture.active = renderTexture;
                Texture2D screenshot = new Texture2D(width, height, TextureFormat.RGB24, false, false);
                screenshot.ReadPixels(new Rect(0f, 0f, width, height), 0, 0, false);
                screenshot.Apply(false, false);
                string absolutePath = Path.Combine(Directory.GetCurrentDirectory(), assetPath);
                Directory.CreateDirectory(Path.GetDirectoryName(absolutePath));
                File.WriteAllBytes(absolutePath, screenshot.EncodeToPNG());
                UnityEngine.Object.DestroyImmediate(screenshot);
            }
            finally
            {
                camera.targetTexture = previousTarget;
                RenderTexture.active = previousActive;
                RenderTexture.ReleaseTemporary(renderTexture);
            }
        }

        private static void BuildTerrain(Transform parent, Material material)
        {
            const int resolution = 193;
            int vertexCount = resolution * resolution;
            Vector3[] vertices = new Vector3[vertexCount];
            Vector2[] uv = new Vector2[vertexCount];
            Vector2[] paintMaskUv = new Vector2[vertexCount];
            Color[] colors = new Color[vertexCount];
            int[] triangles = new int[(resolution - 1) * (resolution - 1) * 6];

            for (int zIndex = 0; zIndex < resolution; zIndex++)
            {
                float z = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, zIndex / (float)(resolution - 1));
                for (int xIndex = 0; xIndex < resolution; xIndex++)
                {
                    float x = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, xIndex / (float)(resolution - 1));
                    int index = zIndex * resolution + xIndex;
                    vertices[index] = new Vector3(x, EvaluateTerrainHeight(x, z), z);
                    uv[index] = new Vector2(xIndex / (float)(resolution - 1), zIndex / (float)(resolution - 1));
                    EvaluateRoadPaint(x, z, out float roadMask, out float edgeMask);
                    paintMaskUv[index] = new Vector2(roadMask, edgeMask);
                    colors[index] = new Color(roadMask, edgeMask, 0f, 1f);
                }
            }

            int triangleIndex = 0;
            for (int zIndex = 0; zIndex < resolution - 1; zIndex++)
            {
                for (int xIndex = 0; xIndex < resolution - 1; xIndex++)
                {
                    int a = zIndex * resolution + xIndex;
                    int b = a + 1;
                    int c = a + resolution;
                    int d = c + 1;
                    triangles[triangleIndex++] = a;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = b;
                    triangles[triangleIndex++] = c;
                    triangles[triangleIndex++] = d;
                }
            }

            Mesh mesh = new Mesh { name = "MSH_ConnectedWorld_ContinuousTerrain", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.uv2 = paintMaskUv;
            mesh.colors = colors;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            mesh = SaveMeshAsset(mesh, GeneratedRoot + "/MSH_ConnectedWorld_ContinuousTerrain.asset");

            GameObject terrain = NewChild("ContinuousSnowTerrain", parent);
            terrain.AddComponent<MeshFilter>().sharedMesh = mesh;
            terrain.AddComponent<MeshRenderer>().sharedMaterial = material;
            terrain.AddComponent<MeshCollider>().sharedMesh = mesh;
            terrain.isStatic = true;
        }

        private static void BuildRiver(Transform parent, Material material)
        {
            List<Vector3> samples = SampleCurve(RiverPoints, 12, false)
                .Select(p => new Vector3(p.x, WaterLevel, p.z))
                .ToList();
            Mesh mesh = CreateRibbonMesh("MSH_ConnectedWorld_River", samples, 12.0f, true);
            mesh = SaveMeshAsset(mesh, GeneratedRoot + "/MSH_ConnectedWorld_River.asset");

            GameObject river = NewChild("ConnectedIceRiver", parent);
            river.AddComponent<MeshFilter>().sharedMesh = mesh;
            river.AddComponent<MeshRenderer>().sharedMaterial = material;
            river.isStatic = true;

            for (int i = 0; i < 2; i++)
            {
                Vector2 point = i == 0 ? RiverPoints[0] : RiverPoints[RiverPoints.Length - 1];
                string suffix = i == 0 ? "West" : "East";
                Mesh capMesh = CreateDiscMesh("MSH_ConnectedWorld_RiverCap_" + suffix, point, 6f, WaterLevel);
                capMesh = SaveMeshAsset(capMesh, GeneratedRoot + "/MSH_ConnectedWorld_RiverCap_" + suffix + ".asset");
                GameObject cap = NewChild("RiverCap_" + suffix, parent);
                cap.AddComponent<MeshFilter>().sharedMesh = capMesh;
                cap.AddComponent<MeshRenderer>().sharedMaterial = material;
                cap.isStatic = true;
            }
        }

        private static void BuildRouteGuides(Transform parent)
        {
            foreach (RouteSpec route in Routes)
            {
                GameObject routeGuide = NewChild(route.Name, parent);
                for (int pointIndex = 0; pointIndex < route.Points.Length; pointIndex++)
                {
                    Vector2 point = route.Points[pointIndex];
                    GameObject guide = NewChild("ControlPoint_" + (pointIndex + 1).ToString("00"), routeGuide.transform);
                    guide.transform.localPosition = new Vector3(point.x, EvaluateTerrainHeight(point.x, point.y) + 0.05f, point.y);
                }
            }
        }

        private static void BuildDistricts(Transform parent, Scene targetScene)
        {
            foreach (ZoneSpec zone in Zones)
            {
                string prefabPath = VendorEnvironmentRoot + "/" + zone.PrefabName + ".prefab";
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
                if (prefab == null)
                {
                    Debug.LogWarning("[ConnectedWinterWorldBuilder] Missing district prefab: " + prefabPath);
                    continue;
                }

                GameObject instance = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (instance == null)
                {
                    continue;
                }

                if (instance.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(instance, targetScene);
                }

                instance.name = zone.Name;
                instance.transform.SetParent(parent, false);
                instance.transform.localScale = Vector3.one * zone.Scale;
                instance.transform.localRotation = Quaternion.Euler(0f, zone.Yaw, 0f);
                instance.transform.localPosition = new Vector3(
                    zone.Center.x,
                    zone.GroundY - zone.SourceGroundY * zone.Scale,
                    zone.Center.y);

                foreach (Transform child in instance.transform)
                {
                    string lowered = child.name.ToLowerInvariant();
                    if (lowered.StartsWith("land ", StringComparison.Ordinal))
                    {
                        child.gameObject.SetActive(false);
                    }
                    if (lowered.StartsWith("water ", StringComparison.Ordinal))
                    {
                        child.gameObject.SetActive(false);
                    }
                    if (lowered == "road" ||
                        lowered.Contains("pedestrian road") ||
                        lowered.StartsWith("road ", StringComparison.Ordinal) ||
                        lowered.StartsWith("winter road", StringComparison.Ordinal) ||
                        lowered.StartsWith("sidewalk", StringComparison.Ordinal))
                    {
                        child.gameObject.SetActive(false);
                    }
                }
            }
        }

        private static void BuildBridges(Transform parent, Scene targetScene)
        {
            GameObject bridgePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VendorBridgePath);
            if (bridgePrefab == null)
            {
                return;
            }

            for (int i = 0; i < BridgePlacements.Length; i++)
            {
                GameObject bridge = PrefabUtility.InstantiatePrefab(bridgePrefab) as GameObject;
                if (bridge == null)
                {
                    continue;
                }
                if (bridge.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(bridge, targetScene);
                }
                bridge.name = "Bridge_" + (i + 1).ToString("00");
                bridge.transform.SetParent(parent, false);
                bridge.transform.localScale = Vector3.one * 1.35f;
                bridge.transform.localRotation = Quaternion.Euler(0f, BridgeYaws[i], 0f);
                bridge.transform.localPosition = new Vector3(
                    BridgePlacements[i].x,
                    WaterLevel + 0.20f,
                    BridgePlacements[i].z);
            }
        }

        private static void BuildBoundaryNature(Transform parent, Scene targetScene)
        {
            string naturePath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Nature";
            string[] prefabPaths = Directory.GetFiles(naturePath, "*.prefab", SearchOption.TopDirectoryOnly)
                .Select(ToAssetPath)
                .OrderBy(path => path, StringComparer.Ordinal)
                .ToArray();
            if (prefabPaths.Length == 0)
            {
                return;
            }

            System.Random random = new System.Random(12062026);
            int placed = 0;
            int attempts = 0;
            while (placed < 210 && attempts++ < 3400)
            {
                bool edgeBand = random.NextDouble() < 0.46;
                bool verticalBand = random.NextDouble() > 0.5;
                float x;
                float z;
                if (!edgeBand)
                {
                    x = Mathf.Lerp(-110f, 110f, (float)random.NextDouble());
                    z = Mathf.Lerp(-110f, 110f, (float)random.NextDouble());
                }
                else if (verticalBand)
                {
                    x = Mathf.Lerp(-112f, 112f, (float)random.NextDouble());
                    z = (random.NextDouble() > 0.5 ? 1f : -1f) * Mathf.Lerp(95f, 113f, (float)random.NextDouble());
                }
                else
                {
                    x = (random.NextDouble() > 0.5 ? 1f : -1f) * Mathf.Lerp(95f, 113f, (float)random.NextDouble());
                    z = Mathf.Lerp(-112f, 112f, (float)random.NextDouble());
                }

                Vector2 candidate = new Vector2(x, z);
                if (Zones.Any(zone => Vector2.Distance(candidate, zone.Center) < zone.PadRadius + 8f))
                {
                    continue;
                }
                if (Routes.Any(route => DistanceToPolyline(candidate, route.Points) < route.Width * 0.5f + 4f))
                {
                    continue;
                }
                if (DistanceToPolyline(candidate, RiverPoints) < 9f)
                {
                    continue;
                }

                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPaths[placed % prefabPaths.Length]);
                GameObject tree = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (tree == null)
                {
                    continue;
                }
                if (tree.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(tree, targetScene);
                }
                tree.name = "BoundaryTree_" + (placed + 1).ToString("00");
                tree.transform.SetParent(parent, false);
                tree.transform.localPosition = new Vector3(x, EvaluateTerrainHeight(x, z), z);
                tree.transform.localRotation = Quaternion.Euler(0f, (float)random.NextDouble() * 360f, 0f);
                tree.transform.localScale = Vector3.one * Mathf.Lerp(1.02f, 1.48f, (float)random.NextDouble());
                placed++;
            }
        }

        private static void BuildLanterns(Transform parent, Scene targetScene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefabPath);
            if (prefab == null)
            {
                return;
            }

            for (int i = 0; i < LanternPositions.Length; i++)
            {
                Vector3 source = LanternPositions[i];
                GameObject lantern = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
                if (lantern == null)
                {
                    continue;
                }
                if (lantern.scene != targetScene)
                {
                    SceneManager.MoveGameObjectToScene(lantern, targetScene);
                }
                lantern.name = "RouteLantern_" + (i + 1).ToString("00");
                lantern.transform.SetParent(parent, false);
                lantern.transform.localPosition = new Vector3(source.x, EvaluateTerrainHeight(source.x, source.z), source.z);
                lantern.transform.localRotation = Quaternion.Euler(0f, (i * 47f) % 360f, 0f);
            }
        }

        private static void BuildLightingAndAtmosphere(Transform parent, Scene targetScene)
        {
            GameObject lightObject = NewChild("MoonKeyLight", parent);
            Light keyLight = lightObject.AddComponent<Light>();
            keyLight.type = LightType.Directional;
            keyLight.color = new Color(0.88f, 0.93f, 1f);
            keyLight.intensity = 1.65f;
            keyLight.shadows = LightShadows.Soft;
            lightObject.transform.localRotation = Quaternion.Euler(43f, -32f, 0f);

            foreach (ZoneSpec zone in Zones.Where(zone => !zone.Name.Contains("Crystal")))
            {
                GameObject warmPool = NewChild("WarmPool_" + zone.Name, parent);
                warmPool.transform.localPosition = new Vector3(zone.Center.x, zone.GroundY + 6f, zone.Center.y);
                Light light = warmPool.AddComponent<Light>();
                light.type = LightType.Point;
                light.color = new Color(1f, 0.58f, 0.23f);
                light.intensity = zone.Name.Contains("Santa") || zone.Name.Contains("Fair") ? 3.2f : 1.8f;
                light.range = zone.Name.Contains("Santa") || zone.Name.Contains("Fair") ? 27f : 20f;
                light.shadows = LightShadows.None;
            }

            GameObject snowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowfallPrefabPath);
            if (snowPrefab != null)
            {
                GameObject snow = PrefabUtility.InstantiatePrefab(snowPrefab) as GameObject;
                if (snow != null)
                {
                    if (snow.scene != targetScene)
                    {
                        SceneManager.MoveGameObjectToScene(snow, targetScene);
                    }
                    snow.name = "LocalSnowfall";
                    snow.transform.SetParent(parent, false);
                    snow.transform.localPosition = new Vector3(0f, 35f, 0f);
                }
            }

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.34f, 0.44f, 0.62f);
            RenderSettings.ambientEquatorColor = new Color(0.23f, 0.30f, 0.43f);
            RenderSettings.ambientGroundColor = new Color(0.12f, 0.16f, 0.24f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.28f, 0.38f, 0.55f);
            RenderSettings.fogStartDistance = 205f;
            RenderSettings.fogEndDistance = 520f;

            Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (skybox != null)
            {
                RenderSettings.skybox = skybox;
            }

            GameObject cameraObject = NewChild("Main Camera", parent);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.orthographic = true;
            camera.orthographicSize = 122f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 650f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.018f, 0.035f, 0.075f);
            cameraObject.transform.position = new Vector3(-95f, 205f, -185f);
            cameraObject.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 6f, 0f) - cameraObject.transform.position, Vector3.up);
        }

        private static void BuildGameplay(Transform parent, Scene targetScene)
        {
            GameObject spawn = NewChild("PlayerSpawn", parent);
            float spawnX = -88f;
            float spawnZ = -58f;
            spawn.transform.localPosition = new Vector3(spawnX, EvaluateTerrainHeight(spawnX, spawnZ) + 0.65f, spawnZ);
            spawn.transform.localRotation = Quaternion.Euler(0f, 62f, 0f);

            GameObject vehiclePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(VehiclePrefabPath);
            if (vehiclePrefab == null)
            {
                return;
            }
            GameObject vehicle = PrefabUtility.InstantiatePrefab(vehiclePrefab) as GameObject;
            if (vehicle == null)
            {
                return;
            }
            if (vehicle.scene != targetScene)
            {
                SceneManager.MoveGameObjectToScene(vehicle, targetScene);
            }
            vehicle.name = "PlayerVehicle";
            vehicle.transform.SetParent(parent, false);
            vehicle.transform.localPosition = spawn.transform.localPosition;
            vehicle.transform.localRotation = spawn.transform.localRotation;
        }

        private static void BuildBoundaries(Transform parent)
        {
            GameObject boundaryRoot = NewChild("InvisibleBoundary", parent);
            CreateBoundary("North", boundaryRoot.transform, new Vector3(0f, 10f, MapHalfExtent + 2f), new Vector3(MapHalfExtent * 2f + 8f, 24f, 2f));
            CreateBoundary("South", boundaryRoot.transform, new Vector3(0f, 10f, -MapHalfExtent - 2f), new Vector3(MapHalfExtent * 2f + 8f, 24f, 2f));
            CreateBoundary("East", boundaryRoot.transform, new Vector3(MapHalfExtent + 2f, 10f, 0f), new Vector3(2f, 24f, MapHalfExtent * 2f + 8f));
            CreateBoundary("West", boundaryRoot.transform, new Vector3(-MapHalfExtent - 2f, 10f, 0f), new Vector3(2f, 24f, MapHalfExtent * 2f + 8f));
        }

        private static void CreateBoundary(string name, Transform parent, Vector3 position, Vector3 size)
        {
            GameObject boundary = NewChild(name, parent);
            boundary.transform.localPosition = position;
            boundary.AddComponent<BoxCollider>().size = size;
        }

        private static float EvaluateTerrainHeight(float x, float z)
        {
            float north = Smooth01(Mathf.InverseLerp(-105f, 102f, z));
            float baseHeight = Mathf.Lerp(-0.25f, 9.2f, north);
            baseHeight += 7.5f * Mathf.Pow(Smooth01(Mathf.InverseLerp(18f, 104f, z)), 1.55f);
            baseHeight += 4.6f * Mathf.Exp(-((x - 68f) * (x - 68f)) / 2400f - ((z - 80f) * (z - 80f)) / 1700f);
            baseHeight += (Mathf.PerlinNoise((x + 250f) * 0.032f, (z + 180f) * 0.032f) - 0.5f) * 0.85f;

            float riverDistance = DistanceToPolyline(new Vector2(x, z), RiverPoints);
            float riverBlend = 1f - SmoothStep(7.0f, 15.5f, riverDistance);
            baseHeight = Mathf.Lerp(baseHeight, WaterLevel - 1.05f, riverBlend);

            float weightTotal = 0f;
            float targetTotal = 0f;
            float strongestWeight = 0f;
            foreach (ZoneSpec zone in Zones)
            {
                float distance = Vector2.Distance(new Vector2(x, z), zone.Center);
                float weight = 1f - SmoothStep(zone.PadRadius, zone.PadRadius + 13f, distance);
                if (weight <= 0f)
                {
                    continue;
                }
                float padHeight = zone.GroundY;
                weightTotal += weight;
                targetTotal += padHeight * weight;
                strongestWeight = Mathf.Max(strongestWeight, weight);
            }
            if (weightTotal > 0f)
            {
                baseHeight = Mathf.Lerp(baseHeight, targetTotal / weightTotal, strongestWeight);
            }

            // Keep every driving approach on the same height as the bridge deck.
            // The blend begins just beneath each bridge end and tapers back into the
            // surrounding terrain, so low-angle views do not reveal a floating deck
            // or a road continuing underneath it.
            Vector2 terrainPoint = new Vector2(x, z);
            for (int i = 0; i < BridgePlacements.Length; i++)
            {
                Vector2 bridgeCenter = new Vector2(BridgePlacements[i].x, BridgePlacements[i].z);
                float yawRadians = BridgeYaws[i] * Mathf.Deg2Rad;
                Vector2 forward = new Vector2(Mathf.Sin(yawRadians), Mathf.Cos(yawRadians));
                Vector2 right = new Vector2(forward.y, -forward.x);
                Vector2 delta = terrainPoint - bridgeCenter;
                float along = Mathf.Abs(Vector2.Dot(delta, forward));
                float across = Mathf.Abs(Vector2.Dot(delta, right));

                float innerStart = BridgeHalfLength - 2.7f;
                float levelStart = BridgeHalfLength - 1.2f;
                float levelEnd = BridgeHalfLength + 1.2f;
                float outerEnd = BridgeHalfLength + BridgeApproachLength;
                if (along < innerStart || along > outerEnd || across > 6.0f)
                {
                    continue;
                }

                float longitudinalWeight;
                if (along < levelStart)
                {
                    longitudinalWeight = Smooth01(Mathf.InverseLerp(innerStart, levelStart, along));
                }
                else if (along <= levelEnd)
                {
                    longitudinalWeight = 1f;
                }
                else
                {
                    longitudinalWeight = 1f - Smooth01(Mathf.InverseLerp(levelEnd, outerEnd, along));
                }
                float lateralWeight = 1f - SmoothStep(3.1f, 6.0f, across);
                float approachWeight = longitudinalWeight * lateralWeight;
                baseHeight = Mathf.Lerp(baseHeight, BridgeDeckSurfaceHeight + 0.045f, approachWeight);
            }

            foreach (RouteSpec route in Routes)
            {
                float routeDistance = DistanceToPolyline(new Vector2(x, z), route.Points);
                float corridor = 1f - SmoothStep(route.Width * 0.5f + 0.4f, route.Width * 0.5f + 2.8f, routeDistance);
                baseHeight -= corridor * 0.045f;
            }

            return baseHeight;
        }

        private static void EvaluateRoadPaint(float x, float z, out float roadMask, out float edgeMask)
        {
            Vector2 point = new Vector2(x, z);
            roadMask = 0f;
            edgeMask = 0f;

            foreach (RouteSpec route in Routes)
            {
                float distance = DistanceToSmoothedRoute(point, route.Points, 8);
                float halfWidth = route.Width * 0.5f;
                float routeMask = 1f - SmoothStep(halfWidth - 0.65f, halfWidth + 0.18f, distance);
                float outerMask = 1f - SmoothStep(halfWidth + 0.12f, halfWidth + 1.15f, distance);
                roadMask = Mathf.Max(roadMask, routeMask);
                edgeMask = Mathf.Max(edgeMask, Mathf.Clamp01(outerMask - routeMask));
            }
        }

        private static List<Vector3> SampleCurve(IReadOnlyList<Vector2> points, int subdivisionsPerSegment, bool roadHeight)
        {
            List<Vector3> result = new List<Vector3>();
            for (int segment = 0; segment < points.Count - 1; segment++)
            {
                Vector2 p0 = points[Mathf.Max(segment - 1, 0)];
                Vector2 p1 = points[segment];
                Vector2 p2 = points[segment + 1];
                Vector2 p3 = points[Mathf.Min(segment + 2, points.Count - 1)];
                for (int step = 0; step <= subdivisionsPerSegment; step++)
                {
                    if (segment > 0 && step == 0)
                    {
                        continue;
                    }
                    float t = step / (float)subdivisionsPerSegment;
                    Vector2 point = CatmullRom(p0, p1, p2, p3, t);
                    float y = roadHeight ? EvaluateTerrainHeight(point.x, point.y) : WaterLevel;
                    result.Add(new Vector3(point.x, y, point.y));
                }
            }
            return result;
        }

        private static Vector2 CatmullRom(Vector2 p0, Vector2 p1, Vector2 p2, Vector2 p3, float t)
        {
            float t2 = t * t;
            float t3 = t2 * t;
            return 0.5f * ((2f * p1) + (-p0 + p2) * t + (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 + (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
        }

        private static Mesh CreateRibbonMesh(string name, IReadOnlyList<Vector3> samples, float width, bool keepFlat)
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
                Vector3 side = new Vector3(-tangent.z, 0f, tangent.x) * (width * 0.5f);

                Vector3 left = samples[i] - side;
                Vector3 right = samples[i] + side;
                if (!keepFlat)
                {
                    left.y = EvaluateTerrainHeight(left.x, left.z);
                    right.y = EvaluateTerrainHeight(right.x, right.z);
                }
                vertices[i * 2] = left;
                vertices[i * 2 + 1] = right;
                if (i > 0)
                {
                    distance += Vector3.Distance(samples[i - 1], samples[i]);
                }
                uv[i * 2] = new Vector2(0f, distance / 5f);
                uv[i * 2 + 1] = new Vector2(1f, distance / 5f);
            }

            int triangleIndex = 0;
            for (int i = 0; i < samples.Count - 1; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[triangleIndex++] = a;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = c;
                triangles[triangleIndex++] = b;
                triangles[triangleIndex++] = d;
                triangles[triangleIndex++] = c;
            }

            Mesh mesh = new Mesh { name = name, indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateDiscMesh(string name, Vector2 center, float radius, float y)
        {
            const int segments = 24;
            Vector3[] vertices = new Vector3[segments + 2];
            Vector2[] uv = new Vector2[segments + 2];
            int[] triangles = new int[segments * 3];
            vertices[0] = new Vector3(center.x, y, center.y);
            uv[0] = new Vector2(0.5f, 0.5f);
            for (int i = 0; i <= segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                float cosine = Mathf.Cos(angle);
                float sine = Mathf.Sin(angle);
                vertices[i + 1] = new Vector3(center.x + cosine * radius, y, center.y + sine * radius);
                uv[i + 1] = new Vector2(cosine * 0.5f + 0.5f, sine * 0.5f + 0.5f);
                if (i < segments)
                {
                    triangles[i * 3] = 0;
                    triangles[i * 3 + 1] = i + 2;
                    triangles[i * 3 + 2] = i + 1;
                }
            }

            Mesh mesh = new Mesh { name = name };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static float DistanceToPolyline(Vector2 point, IReadOnlyList<Vector2> line)
        {
            float minimum = float.MaxValue;
            for (int i = 0; i < line.Count - 1; i++)
            {
                Vector2 a = line[i];
                Vector2 b = line[i + 1];
                Vector2 ab = b - a;
                float denominator = Mathf.Max(Vector2.Dot(ab, ab), 0.0001f);
                float t = Mathf.Clamp01(Vector2.Dot(point - a, ab) / denominator);
                minimum = Mathf.Min(minimum, Vector2.Distance(point, a + ab * t));
            }
            return minimum;
        }

        private static float DistanceToSmoothedRoute(Vector2 point, IReadOnlyList<Vector2> route, int subdivisionsPerSegment)
        {
            float minimum = float.MaxValue;
            Vector2 previous = route[0];
            for (int segment = 0; segment < route.Count - 1; segment++)
            {
                Vector2 p0 = route[Mathf.Max(segment - 1, 0)];
                Vector2 p1 = route[segment];
                Vector2 p2 = route[segment + 1];
                Vector2 p3 = route[Mathf.Min(segment + 2, route.Count - 1)];
                for (int step = 1; step <= subdivisionsPerSegment; step++)
                {
                    Vector2 current = CatmullRom(p0, p1, p2, p3, step / (float)subdivisionsPerSegment);
                    Vector2 delta = current - previous;
                    float denominator = Mathf.Max(Vector2.Dot(delta, delta), 0.0001f);
                    float t = Mathf.Clamp01(Vector2.Dot(point - previous, delta) / denominator);
                    minimum = Mathf.Min(minimum, Vector2.Distance(point, previous + delta * t));
                    previous = current;
                }
            }
            return minimum;
        }

        private static Texture2D CreateOrUpdateRoadMaskTexture(string path)
        {
            const int resolution = 512;
            Color32[] pixels = new Color32[resolution * resolution];
            float pixelsPerMeter = (resolution - 1f) / (MapHalfExtent * 2f);

            foreach (RouteSpec route in Routes)
            {
                List<Vector2> samples = SampleCurve2D(route.Points, 10);
                float halfWidth = route.Width * 0.5f;
                float paintRadius = halfWidth + 1.35f;
                for (int segment = 0; segment < samples.Count - 1; segment++)
                {
                    Vector2 a = samples[segment];
                    Vector2 b = samples[segment + 1];
                    int minX = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.x, b.x) - paintRadius + MapHalfExtent) * pixelsPerMeter), 0, resolution - 1);
                    int maxX = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.x, b.x) + paintRadius + MapHalfExtent) * pixelsPerMeter), 0, resolution - 1);
                    int minZ = Mathf.Clamp(Mathf.FloorToInt((Mathf.Min(a.y, b.y) - paintRadius + MapHalfExtent) * pixelsPerMeter), 0, resolution - 1);
                    int maxZ = Mathf.Clamp(Mathf.CeilToInt((Mathf.Max(a.y, b.y) + paintRadius + MapHalfExtent) * pixelsPerMeter), 0, resolution - 1);

                    for (int z = minZ; z <= maxZ; z++)
                    {
                        float worldZ = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, z / (resolution - 1f));
                        for (int x = minX; x <= maxX; x++)
                        {
                            float worldX = Mathf.Lerp(-MapHalfExtent, MapHalfExtent, x / (resolution - 1f));
                            float distance = DistanceToSegment(new Vector2(worldX, worldZ), a, b);
                            float routeMask = 1f - SmoothStep(halfWidth - 0.38f, halfWidth + 0.22f, distance);
                            float outerMask = 1f - SmoothStep(halfWidth + 0.12f, halfWidth + 1.18f, distance);
                            float edgeMask = Mathf.Clamp01(outerMask - routeMask);
                            int index = z * resolution + x;
                            Color32 current = pixels[index];
                            pixels[index] = new Color32(
                                (byte)Mathf.Max(current.r, Mathf.RoundToInt(routeMask * 255f)),
                                (byte)Mathf.Max(current.g, Mathf.RoundToInt(edgeMask * 255f)),
                                0,
                                255);
                        }
                    }
                }
            }

            Texture2D texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);
            if (texture == null)
            {
                texture = new Texture2D(resolution, resolution, TextureFormat.RGBA32, false, true)
                {
                    name = "TX_ConnectedWorld_RoadMask"
                };
                AssetDatabase.CreateAsset(texture, path);
            }
            else if (texture.width != resolution || texture.height != resolution || texture.format != TextureFormat.RGBA32)
            {
                texture.Reinitialize(resolution, resolution, TextureFormat.RGBA32, false);
            }

            texture.wrapMode = TextureWrapMode.Clamp;
            texture.filterMode = FilterMode.Bilinear;
            texture.anisoLevel = 1;
            texture.SetPixels32(pixels);
            texture.Apply(false, false);
            EditorUtility.SetDirty(texture);
            return texture;
        }

        private static List<Vector2> SampleCurve2D(IReadOnlyList<Vector2> points, int subdivisionsPerSegment)
        {
            List<Vector2> result = new List<Vector2>();
            for (int segment = 0; segment < points.Count - 1; segment++)
            {
                Vector2 p0 = points[Mathf.Max(segment - 1, 0)];
                Vector2 p1 = points[segment];
                Vector2 p2 = points[segment + 1];
                Vector2 p3 = points[Mathf.Min(segment + 2, points.Count - 1)];
                for (int step = 0; step <= subdivisionsPerSegment; step++)
                {
                    if (segment > 0 && step == 0)
                    {
                        continue;
                    }
                    result.Add(CatmullRom(p0, p1, p2, p3, step / (float)subdivisionsPerSegment));
                }
            }
            return result;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 a, Vector2 b)
        {
            Vector2 delta = b - a;
            float denominator = Mathf.Max(Vector2.Dot(delta, delta), 0.0001f);
            float t = Mathf.Clamp01(Vector2.Dot(point - a, delta) / denominator);
            return Vector2.Distance(point, a + delta * t);
        }

        private static Material CreateOrUpdatePaintedTerrainMaterial(string path, Texture2D roadMask)
        {
            Shader shader = Shader.Find("PPack/Connected Winter Terrain");
            if (shader == null)
            {
                throw new InvalidOperationException("Missing shader: PPack/Connected Winter Terrain");
            }

            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            material.SetColor("_SnowColor", new Color(0.47f, 0.59f, 0.76f));
            material.SetColor("_RoadEdgeColor", new Color(0.36f, 0.43f, 0.51f));
            material.SetColor("_RoadColor", new Color(0.22f, 0.18f, 0.15f));
            material.SetTexture("_RoadMask", roadMask);
            material.SetFloat("_Smoothness", 0.16f);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void CleanLegacyRouteAssets()
        {
            string[] materialPaths =
            {
                MaterialsRoot + "/M_ConnectedWorld_Snow.mat",
                MaterialsRoot + "/M_ConnectedWorld_PackedRoad.mat",
                MaterialsRoot + "/M_ConnectedWorld_RoadsideSnow.mat"
            };
            foreach (string path in materialPaths)
            {
                AssetDatabase.DeleteAsset(path);
            }

            foreach (RouteSpec route in Routes)
            {
                AssetDatabase.DeleteAsset(GeneratedRoot + "/MSH_" + route.Name + ".asset");
                AssetDatabase.DeleteAsset(GeneratedRoot + "/MSH_" + route.Name + "_RoadsideSnow.asset");
                foreach (string endpoint in new[] { "Start", "End" })
                {
                    AssetDatabase.DeleteAsset(GeneratedRoot + "/MSH_" + route.Name + "_" + endpoint + "_Road.asset");
                    AssetDatabase.DeleteAsset(GeneratedRoot + "/MSH_" + route.Name + "_" + endpoint + "_RoadsideSnow.asset");
                }
            }
        }

        private static Material CreateOrUpdateLitMaterial(string path, Color color, float metallic, float smoothness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                material = new Material(shader) { name = Path.GetFileNameWithoutExtension(path) };
                AssetDatabase.CreateAsset(material, path);
            }
            else
            {
                material.shader = shader;
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", metallic);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh SaveMeshAsset(Mesh generated, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static void EnsureFolder(string folder)
        {
            string normalized = folder.Replace('\\', '/');
            if (AssetDatabase.IsValidFolder(normalized))
            {
                return;
            }
            string parent = Path.GetDirectoryName(normalized).Replace('\\', '/');
            string name = Path.GetFileName(normalized);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }

        private static GameObject NewSceneObject(string name, Scene scene)
        {
            GameObject gameObject = new GameObject(name);
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            GameObject gameObject = new GameObject(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject;
        }

        private static string ToAssetPath(string absoluteOrRelativePath)
        {
            string normalized = absoluteOrRelativePath.Replace('\\', '/');
            int assetsIndex = normalized.IndexOf("Assets/", StringComparison.Ordinal);
            return assetsIndex >= 0 ? normalized.Substring(assetsIndex) : normalized;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private static float SmoothStep(float from, float to, float value)
        {
            return Smooth01(Mathf.InverseLerp(from, to, value));
        }

        private static void RemoveMissingScripts(GameObject root)
        {
            int removed = 0;
            foreach (Transform transform in root.GetComponentsInChildren<Transform>(true))
            {
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);
            }
            if (removed > 0)
            {
                Debug.LogWarning("[ConnectedWinterWorldBuilder] Removed " + removed + " missing script reference(s) from copied concept objects.");
            }
        }
    }
}
