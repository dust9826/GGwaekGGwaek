using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack.InGame.Map.WinterVillage.Editor
{
    /// <summary>
    /// Adds an authored ski-resort district to WinterVillage_ConceptMap.
    ///
    /// The vendor "ski resort with env" and "land ski resort" composition prefabs are
    /// deliberately never loaded. The hill, piste, access road, cable and safety geometry
    /// are project-owned meshes; only individual props from the vendor sample are reused.
    /// </summary>
    public static class ConceptMapSkiResortBuilder
    {
        private const string MenuPath = "PPack/Map/Winter Village/Build Authored Ski Resort Expansion";
        private const string CaptureMenuPath = "PPack/Map/Winter Village/Capture Ski Resort QA Views";
        private const string ScenePath = "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_ConceptMap.unity";
        private const string RootName = "SkiResortExpansion_Authored_NoVendorResortPrefab";
        private const string GeneratedRoot = "Assets/Game/InGame/Map/WinterVillage/Generated/ConceptSkiResort";
        private const string MaterialsRoot = "Assets/Game/InGame/Map/WinterVillage/Materials/ConceptSkiResort";
        private const string PreviewRoot = "Assets/Game/InGame/Map/WinterVillage/Preview/ConceptSkiResort";

        private const string SkiHousePath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Houses/Winter houses/ski house.prefab";
        private const string SkiLiftPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/ski lift.prefab";
        private const string ChairliftPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/chairlift.prefab";
        private const string SnowSlidePath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/snow slide.prefab";
        private const string RedFlagPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/ski flag red.prefab";
        private const string BlueFlagPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/ski flag blue.prefab";
        private const string InformationSignPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/information plate winter.prefab";
        private const string SnowdriftPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/snowdrift.prefab";
        private const string RentalHousePath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Houses/PF_WoodenWinterHouse_Lit_B.prefab";
        private const string PatrolHousePath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Houses/PF_WinterHouse_Lit_C.prefab";
        private const string SummitHousePath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Houses/PF_WoodenWinterHouse_Lit_A.prefab";
        private const string PenguinPath = "Assets/Game/InGame/Player/Prefabs/PF_PenguinProto.prefab";
        private const string MaintenancePickupPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Vehicles/Cars/Cars SUVs pickups lights/car SUVs pickups lights red.prefab";
        private const string SledPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/Christmas env/Sleds/sled blue.prefab";
        private const string SnowShovelPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/Winter env/Snow shovels/snow shovel yellow.prefab";
        private const string CafeBoardPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Environment/cafe board.prefab";
        private const string TreeLargePath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Trees/Winter trees/Fir trees winter/fir tree winter large 3.prefab";
        private const string TreeMediumPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Trees/Winter trees/Fir trees winter/fir tree winter medium 3.prefab";
        private const string TreeSmallPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Trees/Winter trees/Fir trees winter/fir tree winter small 3.prefab";
        private const string RockLargePath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Stones/Stones winter/Stones winter large/stone winter large 2.prefab";
        private const string RockSmallPath = "Assets/Game/InGame/map asset/Low Poly Locations Ultimate Pack/Prefabs/Stones/Stones winter/Stones winter small/stone winter small 10.prefab";
        private const string LanternPath = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Lighting/PF_WinterLantern_Glow.prefab";

        private const float CenterX = 31f;
        private const float HillMinZ = 53f;
        private const float ClimbStartZ = 67f;
        private const float HillMaxZ = 145f;
        private const float BaseY = 0.12f;
        private const float ClimbPerMeter = 0.305f;
        private const float SummitPadCenterX = 13.5f;
        private const float SummitPadCenterZ = 139f;

        private sealed class BuildMaterials
        {
            public Material Snow;
            public Material FeatureSnow;
            public Material Piste;
            public Material GroomLine;
            public Material Road;
            public Material RoadEdge;
            public Material SideSnow;
            public Material Cable;
            public Material SafetyRed;
            public Material SafetyBlue;
            public Material Wood;
        }

        private readonly struct PropSpec
        {
            public readonly string Path;
            public readonly string Name;
            public readonly Vector3 Position;
            public readonly float Yaw;
            public readonly float Scale;
            public readonly float VerticalOffset;

            public PropSpec(string path, string name, float x, float z, float yaw = 0f, float scale = 1f, float verticalOffset = 0f)
            {
                Path = path;
                Name = name;
                Position = new Vector3(x, 0f, z);
                Yaw = yaw;
                Scale = scale;
                VerticalOffset = verticalOffset;
            }
        }

        [MenuItem(MenuPath)]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before building the ConceptMap ski resort.");

            Scene active = SceneManager.GetActiveScene();
            if (active.IsValid() && active.isDirty && active.path != ScenePath)
                throw new InvalidOperationException("The active scene has unsaved changes. Save it before opening WinterVillage_ConceptMap.");

            Scene scene = active.path == ScenePath
                ? active
                : EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);

            EnsureFolder(GeneratedRoot);
            EnsureFolder(MaterialsRoot);
            EnsureFolder(PreviewRoot);

            GameObject oldRoot = GameObject.Find(RootName);
            if (oldRoot != null)
                UnityEngine.Object.DestroyImmediate(oldRoot);

            BuildMaterials materials = CreateMaterials();
            Transform mapRoot = GameObject.Find("WinterVillageMap")?.transform;
            if (mapRoot == null)
                throw new InvalidOperationException("WinterVillageMap root was not found in the ConceptMap scene.");

            GameObject resortRoot = NewChild(RootName, mapRoot);
            Transform connectionRoot = NewChild("00_Connection_To_Village", resortRoot.transform).transform;
            Transform terrainRoot = NewChild("01_Custom_Slope_Geometry", resortRoot.transform).transform;
            Transform pisteRoot = NewChild("02_Groomed_Piste", resortRoot.transform).transform;
            Transform liftRoot = NewChild("03_Lift_IndividualProps", resortRoot.transform).transform;
            Transform baseRoot = NewChild("04_BaseLodge_IndividualProps", resortRoot.transform).transform;
            Transform dressingRoot = NewChild("05_SlopeDressing_IndividualProps", resortRoot.transform).transform;
            Transform safetyRoot = NewChild("06_SafetyAndWayfinding", resortRoot.transform).transform;
            Transform lightingRoot = NewChild("07_Lighting", resortRoot.transform).transform;
            Transform servicesRoot = NewChild("08_BaseVillageServices", resortRoot.transform).transform;
            Transform beginnerRoot = NewChild("09_BeginnerTrainingZone", resortRoot.transform).transform;
            Transform snowParkRoot = NewChild("10_SnowPark", resortRoot.transform).transform;
            Transform summitRoot = NewChild("11_SummitLodge", resortRoot.transform).transform;
            Transform penguinRoot = NewChild("12_PenguinLife", resortRoot.transform).transform;

            Mesh hillMesh = SaveMesh(CreateHillMesh(), GeneratedRoot + "/MSH_ConceptSkiResort_Hill.asset");
            CreateMeshObject("CustomHillFoundation", terrainRoot, hillMesh, materials.Snow, true, true);

            Mesh sideMesh = SaveMesh(CreateHillSkirtMesh(), GeneratedRoot + "/MSH_ConceptSkiResort_Skirts.asset");
            CreateMeshObject("HillEdgeSkirts", terrainRoot, sideMesh, materials.SideSnow, false, true);

            Mesh pisteMesh = SaveMesh(CreatePisteMesh(), GeneratedRoot + "/MSH_ConceptSkiResort_Piste.asset");
            CreateMeshObject("StraightGroomedPiste", pisteRoot, pisteMesh, materials.Piste, false, false);
            BuildGroomLines(pisteRoot, materials.GroomLine);

            BuildVillageConnection(connectionRoot, materials);
            BuildBaseArea(baseRoot, safetyRoot, lightingRoot, materials, scene);
            BuildLift(liftRoot, materials, scene);
            BuildCourseFlags(safetyRoot, scene);
            BuildSlopeDressing(dressingRoot, scene);
            BuildSafetyFences(safetyRoot, materials);
            BuildBaseVillageServices(servicesRoot, lightingRoot, materials, scene);
            BuildBeginnerTrainingZone(beginnerRoot, materials);
            BuildSnowPark(snowParkRoot, materials, scene);
            BuildSummitLodge(summitRoot, lightingRoot, materials, scene);
            BuildPenguinLife(penguinRoot, scene);
            ImproveSkiDistrictBackdropClearance();
            ExpandMapBounds();

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new IOException("Failed to save " + ScenePath);

            AssetDatabase.SaveAssets();
            Physics.SyncTransforms();
            Selection.activeGameObject = resortRoot;
            if (SceneView.lastActiveSceneView != null)
                SceneView.lastActiveSceneView.FrameSelected();

            CaptureQaViewsInternal(scene);
            Debug.Log("[ConceptMapSkiResortBuilder] Built a project-owned ski district with a continuous main piste, grounded lift, village services, beginner training lane, snow park, summit lodge and decorative penguins without instantiating either vendor ski-resort composition prefab.");
        }

        [MenuItem(CaptureMenuPath)]
        public static void CaptureQaViews()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || scene.path != ScenePath)
                throw new InvalidOperationException("Open WinterVillage_ConceptMap before capturing ski-resort QA views.");
            if (GameObject.Find(RootName) == null)
                throw new InvalidOperationException("Build the authored ski resort before capturing QA views.");
            EnsureFolder(PreviewRoot);
            CaptureQaViewsInternal(scene);
        }

        private static BuildMaterials CreateMaterials()
        {
            return new BuildMaterials
            {
                Snow = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_Snow.mat", new Color(0.67f, 0.82f, 0.98f), 0.05f),
                FeatureSnow = CreateOrUpdateUnlitMaterial(MaterialsRoot + "/M_ConceptSki_FeatureSnow.mat", new Color(0.62f, 0.78f, 0.94f)),
                Piste = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_Piste.mat", new Color(0.78f, 0.90f, 1.00f), 0.04f),
                GroomLine = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_GroomLine.mat", new Color(0.53f, 0.73f, 0.93f), 0.02f),
                Road = LoadOrCreateRoadMaterial(),
                RoadEdge = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_RoadEdge.mat", new Color(0.87f, 0.94f, 1.00f), 0.03f),
                SideSnow = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_SideSnow.mat", new Color(0.43f, 0.61f, 0.84f), 0.02f),
                Cable = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_Cable.mat", new Color(0.055f, 0.075f, 0.10f), 0.12f),
                SafetyRed = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_SafetyRed.mat", new Color(0.78f, 0.09f, 0.08f), 0.04f),
                SafetyBlue = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_SafetyBlue.mat", new Color(0.04f, 0.36f, 0.72f), 0.04f),
                Wood = CreateOrUpdateMatteMaterial(MaterialsRoot + "/M_ConceptSki_Wood.mat", new Color(0.23f, 0.11f, 0.055f), 0.03f)
            };
        }

        private static Material LoadOrCreateRoadMaterial()
        {
            return CreateOrUpdateUnlitMaterial(
                MaterialsRoot + "/M_ConceptSki_AccessRoad.mat",
                new Color(0.12f, 0.15f, 0.18f));
        }

        private static Material CreateOrUpdateUnlitMaterial(string path, Color color)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Unlit") ?? Shader.Find("Unlit/Color");
            if (shader == null)
                throw new InvalidOperationException("No compatible Unlit shader was found for the ski access road.");

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
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Material CreateOrUpdateMatteMaterial(string path, Color color, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            if (shader == null)
                throw new InvalidOperationException("No compatible Lit shader was found for ConceptMap ski-resort materials.");

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
            if (material.HasProperty("_Metallic")) material.SetFloat("_Metallic", 0f);
            if (material.HasProperty("_Smoothness")) material.SetFloat("_Smoothness", smoothness);
            if (material.HasProperty("_SpecularHighlights")) material.SetFloat("_SpecularHighlights", 0f);
            if (material.HasProperty("_EnvironmentReflections")) material.SetFloat("_EnvironmentReflections", 0f);
            material.enableInstancing = true;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static Mesh CreateHillMesh()
        {
            const int xSegments = 28;
            const int zSegments = 48;
            int row = xSegments + 1;
            var vertices = new Vector3[row * (zSegments + 1)];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[xSegments * zSegments * 6];

            for (int z = 0; z <= zSegments; z++)
            {
                float z01 = z / (float)zSegments;
                float worldZ = Mathf.Lerp(HillMinZ, HillMaxZ, z01);
                float halfWidth = Mathf.Lerp(36f, 31f, z01);
                for (int x = 0; x <= xSegments; x++)
                {
                    float x01 = x / (float)xSegments;
                    float worldX = CenterX + Mathf.Lerp(-halfWidth, halfWidth, x01);
                    int index = z * row + x;
                    vertices[index] = new Vector3(worldX, HillHeight(worldX, worldZ), worldZ);
                    uv[index] = new Vector2(x01 * 3f, z01 * 5f);
                }
            }

            int t = 0;
            for (int z = 0; z < zSegments; z++)
            {
                for (int x = 0; x < xSegments; x++)
                {
                    int i0 = z * row + x;
                    int i1 = i0 + 1;
                    int i2 = i0 + row;
                    int i3 = i2 + 1;
                    triangles[t++] = i0; triangles[t++] = i2; triangles[t++] = i1;
                    triangles[t++] = i1; triangles[t++] = i2; triangles[t++] = i3;
                }
            }

            var mesh = new Mesh { name = "MSH_ConceptSkiResort_Hill", indexFormat = IndexFormat.UInt32 };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateHillSkirtMesh()
        {
            const int segments = 48;
            var vertices = new List<Vector3>();
            var triangles = new List<int>();

            AddSkirtEdge(vertices, triangles, segments, true);
            AddSkirtEdge(vertices, triangles, segments, false);

            float northHalf = 31f;
            Vector3 northLeft = new Vector3(CenterX - northHalf, HillHeight(CenterX - northHalf, HillMaxZ), HillMaxZ);
            Vector3 northRight = new Vector3(CenterX + northHalf, HillHeight(CenterX + northHalf, HillMaxZ), HillMaxZ);
            AddQuad(vertices, triangles, northLeft, northRight,
                new Vector3(northLeft.x, -1.5f, northLeft.z + 0.2f),
                new Vector3(northRight.x, -1.5f, northRight.z + 0.2f));

            var mesh = new Mesh { name = "MSH_ConceptSkiResort_Skirts", indexFormat = IndexFormat.UInt32 };
            mesh.SetVertices(vertices);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddSkirtEdge(List<Vector3> vertices, List<int> triangles, int segments, bool left)
        {
            for (int i = 0; i < segments; i++)
            {
                float a01 = i / (float)segments;
                float b01 = (i + 1) / (float)segments;
                float az = Mathf.Lerp(HillMinZ, HillMaxZ, a01);
                float bz = Mathf.Lerp(HillMinZ, HillMaxZ, b01);
                float ah = Mathf.Lerp(36f, 31f, a01);
                float bh = Mathf.Lerp(36f, 31f, b01);
                float ax = CenterX + (left ? -ah : ah);
                float bx = CenterX + (left ? -bh : bh);
                Vector3 topA = new Vector3(ax, HillHeight(ax, az), az);
                Vector3 topB = new Vector3(bx, HillHeight(bx, bz), bz);
                AddQuad(vertices, triangles, topA, topB,
                    new Vector3(ax, -1.5f, az), new Vector3(bx, -1.5f, bz), left);
            }
        }

        private static Mesh CreatePisteMesh()
        {
            const int segments = 64;
            var vertices = new Vector3[(segments + 1) * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];

            for (int i = 0; i <= segments; i++)
            {
                float t = i / (float)segments;
                float z = Mathf.Lerp(ClimbStartZ + 1f, HillMaxZ - 2f, t);
                float halfWidth = Mathf.Lerp(11f, 8.2f, t);
                float leftX = CenterX - halfWidth;
                float rightX = CenterX + halfWidth;
                vertices[i * 2] = new Vector3(leftX, HillHeight(leftX, z) + 0.055f, z);
                vertices[i * 2 + 1] = new Vector3(rightX, HillHeight(rightX, z) + 0.055f, z);
                uv[i * 2] = new Vector2(0f, t * 7f);
                uv[i * 2 + 1] = new Vector2(1f, t * 7f);
            }

            int index = 0;
            for (int i = 0; i < segments; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[index++] = a; triangles[index++] = c; triangles[index++] = b;
                triangles[index++] = b; triangles[index++] = c; triangles[index++] = d;
            }

            var mesh = new Mesh { name = "MSH_ConceptSkiResort_Piste" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void BuildGroomLines(Transform parent, Material material)
        {
            float[] offsets = { -7.2f, -4.8f, -2.4f, 0f, 2.4f, 4.8f, 7.2f };
            for (int i = 0; i < offsets.Length; i++)
            {
                var points = new List<Vector3>();
                const int samples = 45;
                for (int s = 0; s < samples; s++)
                {
                    float t = s / (float)(samples - 1);
                    float z = Mathf.Lerp(ClimbStartZ + 2f, HillMaxZ - 3f, t);
                    float widthScale = Mathf.Lerp(1f, 0.74f, t);
                    float x = CenterX + offsets[i] * widthScale;
                    points.Add(new Vector3(x, HillHeight(x, z) + 0.076f, z));
                }
                Mesh line = SaveMesh(CreateRibbon(points, 0.11f), GeneratedRoot + "/MSH_GroomLine_" + i.ToString("00") + ".asset");
                CreateMeshObject("GroomLine_" + i.ToString("00"), parent, line, material, false, false);
            }
        }

        private static void BuildVillageConnection(Transform parent, BuildMaterials materials)
        {
            var center = new List<Vector3>();
            Vector2[] control =
            {
                new Vector2(34f, 47f), new Vector2(34f, 54.5f), new Vector2(31f, 59f),
                new Vector2(26f, 61.5f), new Vector2(19f, 63f), new Vector2(12f, 63.5f),
                new Vector2(5f, 67f)
            };

            foreach (Vector2 p in SampleCatmullRom(control, 16))
            {
                center.Add(new Vector3(p.x, Mathf.Max(BaseY, HillHeight(p.x, p.y)) + 0.09f, p.y));
            }

            Mesh shoulder = SaveMesh(CreateHillConformingRibbon(center, 3.55f, 0.055f), GeneratedRoot + "/MSH_SkiAccessRoad_Shoulders.asset");
            CreateMeshObject("SnowShoulder_CleanContinuous", parent, shoulder, materials.RoadEdge, false, false);
            Mesh road = SaveMesh(CreateHillConformingRibbon(center, 2.85f, 0.115f), GeneratedRoot + "/MSH_SkiAccessRoad.asset");
            CreateMeshObject("VillageToSkiLodge_AccessRoad", parent, road, materials.Road, false, false);

            CreateRoadSnowBanks(parent, center, materials.RoadEdge);
        }

        private static void CreateRoadSnowBanks(Transform parent, List<Vector3> center, Material material)
        {
            List<Vector3> left = OffsetPolyline(center, -3.35f, 0.12f);
            List<Vector3> right = OffsetPolyline(center, 3.35f, 0.12f);
            Mesh leftMesh = SaveMesh(CreateHillConformingRibbon(left, 0.36f, 0.18f), GeneratedRoot + "/MSH_SkiAccessRoad_LeftBank.asset");
            Mesh rightMesh = SaveMesh(CreateHillConformingRibbon(right, 0.36f, 0.18f), GeneratedRoot + "/MSH_SkiAccessRoad_RightBank.asset");
            CreateMeshObject("AccessRoad_SnowEdge_Left", parent, leftMesh, material, false, false);
            CreateMeshObject("AccessRoad_SnowEdge_Right", parent, rightMesh, material, false, false);
        }

        private static List<Vector3> OffsetPolyline(IReadOnlyList<Vector3> source, float offset, float yOffset)
        {
            var result = new List<Vector3>(source.Count);
            for (int i = 0; i < source.Count; i++)
            {
                Vector3 previous = source[Mathf.Max(0, i - 1)];
                Vector3 next = source[Mathf.Min(source.Count - 1, i + 1)];
                Vector3 tangent = next - previous;
                tangent.y = 0f;
                tangent.Normalize();
                Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x);
                result.Add(source[i] + normal * offset + Vector3.up * yOffset);
            }
            return result;
        }

        private static void BuildBaseArea(Transform baseRoot, Transform safetyRoot, Transform lightingRoot, BuildMaterials materials, Scene scene)
        {
            PlaceGroundedPrefab(SkiHousePath, "SkiLodge_Individual", new Vector3(4.5f, 0f, 61.5f), 12f, 1.20f, 0.04f, baseRoot, scene);
            PlaceGroundedPrefab(SnowSlidePath, "BeginnerSnowSlide_Individual", new Vector3(16f, 0f, 59.5f), 8f, 1.05f, 0.04f, baseRoot, scene);
            PlaceGroundedPrefab(InformationSignPath, "TrailInformation_Individual", new Vector3(22f, 0f, 66.5f), 180f, 1.15f, 0.04f, safetyRoot, scene);

            PlaceGroundedPrefab(SnowdriftPath, "BaseSnowdrift_01", new Vector3(-0.5f, 0f, 54.5f), 23f, 1.25f, -0.04f, baseRoot, scene);
            PlaceGroundedPrefab(SnowdriftPath, "BaseSnowdrift_02", new Vector3(14.5f, 0f, 68f), -28f, 1.0f, -0.04f, baseRoot, scene);
            PlaceGroundedPrefab(SnowdriftPath, "BaseSnowdrift_03", new Vector3(40f, 0f, 60f), 15f, 1.35f, -0.04f, baseRoot, scene);

            Vector3[] lanterns =
            {
                new Vector3(0f, 0f, 55.5f), new Vector3(10.5f, 0f, 59.5f),
                new Vector3(17.5f, 0f, 64f), new Vector3(23.5f, 0f, 68f)
            };
            for (int i = 0; i < lanterns.Length; i++)
                PlaceGroundedPrefab(LanternPath, "SkiBaseLantern_" + (i + 1).ToString("00"), lanterns[i], 0f, 1f, 0.025f, lightingRoot, scene);

            BuildStartGate(safetyRoot, materials);
        }

        private static void BuildStartGate(Transform parent, BuildMaterials materials)
        {
            float z = 72f;
            float yLeft = HillHeight(CenterX - 10.5f, z);
            float yRight = HillHeight(CenterX + 10.5f, z);
            CreateBox("StartGate_RedPost", parent, new Vector3(CenterX - 10.5f, yLeft + 1.8f, z), new Vector3(0.35f, 3.6f, 0.35f), materials.SafetyRed);
            CreateBox("StartGate_BluePost", parent, new Vector3(CenterX + 10.5f, yRight + 1.8f, z), new Vector3(0.35f, 3.6f, 0.35f), materials.SafetyBlue);
            CreateBox("StartGate_Header", parent, new Vector3(CenterX, Mathf.Max(yLeft, yRight) + 3.45f, z), new Vector3(21.2f, 0.42f, 0.42f), materials.SafetyBlue);
        }

        private static void BuildLift(Transform parent, BuildMaterials materials, Scene scene)
        {
            // Build the support towers from project-owned primitives. The vendor
            // tower prop contains an oversized crossbar/pulley composition that
            // becomes visually tangled when repeated on this shorter authored hill.
            float liftX = 44f;
            float[] towerZ = { 76f, 97f, 118f, 138f };
            var cableAnchors = new List<Vector3>();
            BuildLiftSnowAccumulation(parent, materials, liftX);
            for (int i = 0; i < towerZ.Length; i++)
            {
                float z = towerZ[i];
                float ground = HillHeight(liftX, z);
                float height = i == 0 || i == towerZ.Length - 1 ? 5.4f : 5.0f;
                cableAnchors.Add(CreateCustomLiftTower(parent, materials, i + 1,
                    new Vector3(liftX, ground, z), height));
            }

            for (int side = -1; side <= 1; side += 2)
            {
                float xOffset = side * 1.15f;
                for (int i = 0; i < cableAnchors.Count - 1; i++)
                {
                    Vector3 a = cableAnchors[i] + Vector3.right * xOffset;
                    Vector3 b = cableAnchors[i + 1] + Vector3.right * xOffset;
                    CreateCylinderBetween("LiftCable_" + (side < 0 ? "Down" : "Up") + "_" + i.ToString("00"), parent, a, b, 0.035f, materials.Cable);
                }
            }

            float[] chairZ = { 84f, 104f, 124f, 134f };
            for (int i = 0; i < chairZ.Length; i++)
            {
                int side = i % 2 == 0 ? -1 : 1;
                float x = liftX + side * 1.15f;
                float z = chairZ[i];
                float cableY = SamplePolylineHeight(cableAnchors, z);
                GameObject chair = PlacePrefab(ChairliftPath, "Chairlift_Individual_" + (i + 1).ToString("00"),
                    new Vector3(x, cableY, z), side < 0 ? 180f : 0f, 0.56f, parent, scene);
                AttachPrefabTopToHeight(chair, cableY + 0.02f);
            }
        }

        private static void BuildLiftSnowAccumulation(Transform parent, BuildMaterials materials, float liftX)
        {
            var center = new List<Vector3>();
            const int samples = 38;
            for (int i = 0; i < samples; i++)
            {
                float t = i / (float)(samples - 1);
                float z = Mathf.Lerp(72f, 142f, t);
                center.Add(new Vector3(liftX, HillHeight(liftX, z), z));
            }

            Mesh bed = SaveMesh(CreateHillConformingRibbon(center, 2.35f, 0.062f),
                GeneratedRoot + "/MSH_LiftCorridor_SnowBed.asset");
            CreateMeshObject("LiftCorridor_FreshSnowBed", parent, bed, materials.Piste, false, false);

            List<Vector3> left = OffsetPolyline(center, -2.18f, 0f);
            List<Vector3> right = OffsetPolyline(center, 2.18f, 0f);
            Mesh leftBank = SaveMesh(CreateHillConformingRibbon(left, 0.24f, 0.13f),
                GeneratedRoot + "/MSH_LiftCorridor_LeftSnowBank.asset");
            Mesh rightBank = SaveMesh(CreateHillConformingRibbon(right, 0.24f, 0.13f),
                GeneratedRoot + "/MSH_LiftCorridor_RightSnowBank.asset");
            CreateMeshObject("LiftCorridor_SnowBank_Left", parent, leftBank, materials.Snow, false, false);
            CreateMeshObject("LiftCorridor_SnowBank_Right", parent, rightBank, materials.Snow, false, false);
        }

        private static Vector3 CreateCustomLiftTower(Transform parent, BuildMaterials materials, int number, Vector3 ground, float height)
        {
            Transform root = NewChild("LiftTower_CustomGrounded_" + number.ToString("00"), parent).transform;
            CreateLiftFoundation("SnowFooting", root, ground, 1.04f, materials.Piste);

            CreateBox("ConcreteFoot", root, ground + Vector3.up * 0.24f,
                new Vector3(1.15f, 0.48f, 1.05f), materials.SafetyRed);
            CreateBox("FootSnowCap", root, ground + Vector3.up * 0.53f,
                new Vector3(1.30f, 0.10f, 1.20f), materials.Snow);
            CreateBox("MainPost", root, ground + Vector3.up * (0.35f + height * 0.5f),
                new Vector3(0.42f, height, 0.42f), materials.Wood);

            float topY = ground.y + height;
            CreateBox("TopCrossbar", root, new Vector3(ground.x, topY, ground.z),
                new Vector3(3.55f, 0.30f, 0.42f), materials.SafetyRed);
            CreateBox("TopCrossbar_SnowCap", root, new Vector3(ground.x, topY + 0.20f, ground.z),
                new Vector3(3.68f, 0.10f, 0.50f), materials.Snow);

            CreateCylinderBetween("Brace_Left", root,
                ground + new Vector3(-0.13f, 1.25f, 0f),
                new Vector3(ground.x - 1.48f, topY - 0.18f, ground.z), 0.075f, materials.Wood);
            CreateCylinderBetween("Brace_Right", root,
                ground + new Vector3(0.13f, 1.25f, 0f),
                new Vector3(ground.x + 1.48f, topY - 0.18f, ground.z), 0.075f, materials.Wood);

            for (int side = -1; side <= 1; side += 2)
            {
                float x = ground.x + side * 1.15f;
                CreateBox("PulleyHousing_" + (side < 0 ? "Down" : "Up"), root,
                    new Vector3(x, topY - 0.22f, ground.z), new Vector3(0.66f, 0.34f, 0.58f), materials.Cable);
                CreateCylinderBetween("PulleyWheel_" + (side < 0 ? "Down" : "Up"), root,
                    new Vector3(x - 0.17f, topY - 0.28f, ground.z),
                    new Vector3(x + 0.17f, topY - 0.28f, ground.z), 0.29f, materials.Cable);
            }

            return new Vector3(ground.x, topY - 0.28f, ground.z);
        }

        private static Bounds GroundPrefabByRendererBounds(GameObject instance, float groundY, float buryDepth)
        {
            if (instance == null || !TryGetRendererBounds(instance, out Bounds bounds))
                return new Bounds(instance != null ? instance.transform.position : Vector3.zero, Vector3.zero);

            instance.transform.position += Vector3.up * (groundY - buryDepth - bounds.min.y);
            TryGetRendererBounds(instance, out bounds);
            return bounds;
        }

        private static void AttachPrefabTopToHeight(GameObject instance, float targetTopY)
        {
            if (instance == null || !TryGetRendererBounds(instance, out Bounds bounds)) return;
            instance.transform.position += Vector3.up * (targetTopY - bounds.max.y);
        }

        private static bool TryGetRendererBounds(GameObject instance, out Bounds bounds)
        {
            bounds = default;
            if (instance == null) return false;

            Renderer[] renderers = instance.GetComponentsInChildren<Renderer>(true);
            bool found = false;
            foreach (Renderer renderer in renderers)
            {
                if (renderer == null) continue;
                if (!found)
                {
                    bounds = renderer.bounds;
                    found = true;
                }
                else
                {
                    bounds.Encapsulate(renderer.bounds);
                }
            }
            return found;
        }

        private static float SamplePolylineHeight(IReadOnlyList<Vector3> points, float z)
        {
            if (points == null || points.Count == 0) return 0f;
            if (points.Count == 1) return points[0].y;

            for (int i = 0; i < points.Count - 1; i++)
            {
                Vector3 a = points[i];
                Vector3 b = points[i + 1];
                if (z < a.z || z > b.z) continue;
                return Mathf.Lerp(a.y, b.y, Mathf.InverseLerp(a.z, b.z, z));
            }
            return z <= points[0].z ? points[0].y : points[points.Count - 1].y;
        }

        private static GameObject CreateLiftFoundation(string name, Transform parent, Vector3 ground, float radius, Material material)
        {
            GameObject foundation = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            foundation.name = name;
            foundation.transform.SetParent(parent, true);
            foundation.transform.position = ground + Vector3.up * 0.055f;
            foundation.transform.localScale = new Vector3(radius, 0.11f, radius);
            foundation.GetComponent<MeshRenderer>().sharedMaterial = material;
            Collider collider = foundation.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            return foundation;
        }

        private static void BuildCourseFlags(Transform parent, Scene scene)
        {
            float[] flagZ = { 78f, 90f, 102f, 114f, 126f, 137f };
            for (int i = 0; i < flagZ.Length; i++)
            {
                float t = Mathf.InverseLerp(ClimbStartZ, HillMaxZ, flagZ[i]);
                float halfWidth = Mathf.Lerp(11f, 8.2f, t) + 0.9f;
                float leftX = CenterX - halfWidth;
                float rightX = CenterX + halfWidth;
                PlaceGroundedPrefab(RedFlagPath, "CourseFlag_Red_" + (i + 1).ToString("00"), new Vector3(leftX, 0f, flagZ[i]), 0f, 1.1f, 0.04f, parent, scene);
                PlaceGroundedPrefab(BlueFlagPath, "CourseFlag_Blue_" + (i + 1).ToString("00"), new Vector3(rightX, 0f, flagZ[i]), 180f, 1.1f, 0.04f, parent, scene);
            }
        }

        private static void BuildSlopeDressing(Transform parent, Scene scene)
        {
            PropSpec[] trees =
            {
                new PropSpec(TreeLargePath, "SlopeTree_L01", -1f, 74f, 12f, 1.10f),
                new PropSpec(TreeMediumPath, "SlopeTree_L02", 4f, 83f, -22f, 1.18f),
                new PropSpec(TreeSmallPath, "SlopeTree_L03", 7f, 94f, 35f, 1.15f),
                new PropSpec(TreeLargePath, "SlopeTree_L04", 2f, 105f, -8f, 1.05f),
                new PropSpec(TreeMediumPath, "SlopeTree_L05", 8f, 116f, 24f, 1.25f),
                new PropSpec(TreeSmallPath, "SlopeTree_L06", 5f, 128f, -31f, 1.20f),
                new PropSpec(TreeLargePath, "SlopeTree_L07", 3f, 139f, 17f, 0.95f),
                new PropSpec(TreeSmallPath, "SlopeTree_L08", 3.5f, 88f, -18f, 1.05f),
                new PropSpec(TreeMediumPath, "SlopeTree_L09", 2.5f, 110f, 9f, 1.08f),
                new PropSpec(TreeLargePath, "SlopeTree_R01", 65f, 76f, -14f, 1.05f),
                new PropSpec(TreeSmallPath, "SlopeTree_R02", 64f, 86f, 28f, 1.25f),
                new PropSpec(TreeMediumPath, "SlopeTree_R03", 64.5f, 99f, -36f, 1.10f),
                new PropSpec(TreeLargePath, "SlopeTree_R04", 64f, 113f, 15f, 1.02f),
                new PropSpec(TreeSmallPath, "SlopeTree_R05", 63f, 124f, -7f, 1.18f),
                new PropSpec(TreeMediumPath, "SlopeTree_R06", 61f, 137f, 33f, 1.06f),
                new PropSpec(TreeSmallPath, "SlopeTree_R07", 63f, 79f, -15f, 1.10f),
                new PropSpec(TreeMediumPath, "SlopeTree_R08", 63f, 104f, 21f, 0.95f)
            };

            for (int i = 0; i < trees.Length; i++)
            {
                PropSpec spec = trees[i];
                PlaceGroundedPrefab(spec.Path, spec.Name, spec.Position, spec.Yaw, spec.Scale, spec.VerticalOffset, parent, scene);
            }

            PropSpec[] rocks =
            {
                new PropSpec(RockLargePath, "SlopeRock_Large_01", 7f, 78f, 12f, 0.70f, -0.10f),
                new PropSpec(RockLargePath, "SlopeRock_Large_02", 62f, 106f, -25f, 0.64f, -0.12f),
                new PropSpec(RockSmallPath, "SlopeRock_Small_01", 3f, 99f, 33f, 1.20f, -0.08f),
                new PropSpec(RockSmallPath, "SlopeRock_Small_02", 48f, 122f, -19f, 1.05f, -0.08f),
                new PropSpec(RockSmallPath, "SlopeRock_Small_03", 10f, 133f, 7f, 0.95f, -0.08f),
                new PropSpec(SnowdriftPath, "SlopeSnowdrift_01", 5.5f, 82f, 18f, 1.15f, -0.04f),
                new PropSpec(SnowdriftPath, "SlopeSnowdrift_02", 47f, 96f, -16f, 1.00f, -0.04f),
                new PropSpec(SnowdriftPath, "SlopeSnowdrift_03", 4.5f, 121f, 31f, 1.25f, -0.04f),
                new PropSpec(SnowdriftPath, "SlopeSnowdrift_04", 48f, 134f, -29f, 1.10f, -0.04f)
            };

            for (int i = 0; i < rocks.Length; i++)
            {
                PropSpec spec = rocks[i];
                PlaceGroundedPrefab(spec.Path, spec.Name, spec.Position, spec.Yaw, spec.Scale, spec.VerticalOffset, parent, scene);
            }
        }

        private static void BuildBaseVillageServices(Transform parent, Transform lightingRoot, BuildMaterials materials, Scene scene)
        {
            float plazaY = HillHeight(11f, 69f) + 0.045f;
            CreateDisc("BaseResortPlaza_SnowApron", parent, new Vector3(11f, plazaY, 69f),
                new Vector3(8.5f, 0.055f, 6.4f), materials.Piste, false);

            GameObject rental = PlaceGroundedPrefab(RentalHousePath, "RentalAndCafe_LitHouse", new Vector3(0.5f, 0f, 71.5f),
                112f, 0.78f, 0f, parent, scene);
            GroundPrefabByRendererBounds(rental, HillHeight(0.5f, 71.5f), 0.03f);

            GameObject patrol = PlaceGroundedPrefab(PatrolHousePath, "SkiPatrol_LitHouse", new Vector3(25.5f, 0f, 67.5f),
                194f, 0.72f, 0f, parent, scene);
            GroundPrefabByRendererBounds(patrol, HillHeight(25.5f, 67.5f), 0.03f);

            PlaceGroundedPrefab(CafeBoardPath, "RentalCafeBoard", new Vector3(7f, 0f, 68f), 152f, 1.05f, 0.02f, parent, scene);
            PlaceGroundedPrefab(SledPath, "RentalSled_Display", new Vector3(12.5f, 0f, 67.6f), -24f, 0.92f, 0.02f, parent, scene);
            PlaceGroundedPrefab(SnowShovelPath, "PatrolSnowShovel", new Vector3(23.4f, 0f, 65.2f), 18f, 0.95f, 0.02f, parent, scene);

            Vector3[] serviceLanterns =
            {
                new Vector3(3.5f, 0f, 67.5f), new Vector3(9f, 0f, 72f),
                new Vector3(15.5f, 0f, 71.5f), new Vector3(22f, 0f, 69.5f)
            };
            for (int i = 0; i < serviceLanterns.Length; i++)
            {
                PlaceGroundedPrefab(LanternPath, "ServiceLantern_" + (i + 1).ToString("00"), serviceLanterns[i],
                    0f, 0.92f, 0.02f, lightingRoot, scene);
            }

            BuildMaintenancePlow(parent, materials, scene);
        }

        private static void BuildMaintenancePlow(Transform parent, BuildMaterials materials, Scene scene)
        {
            Vector3 position = new Vector3(31.5f, HillHeight(31.5f, 67.5f), 67.5f);
            GameObject pickup = PlacePrefab(MaintenancePickupPath, "StaticSkiPatrol_PloughPickup", position,
                166f, 0.82f, parent, scene);
            if (pickup == null) return;

            GroundPrefabByRendererBounds(pickup, position.y, 0.02f);
            DisableBehaviourAndPhysics(pickup);

            Transform ploughRoot = NewChild("ProjectOwned_VPloughBlade", pickup.transform).transform;
            Vector3 bladeCenter = pickup.transform.TransformPoint(new Vector3(0f, 0.48f, 2.05f));
            GameObject left = CreateBox("Blade_Left", ploughRoot, bladeCenter + pickup.transform.right * -0.52f,
                new Vector3(1.35f, 0.58f, 0.16f), materials.SafetyRed);
            left.transform.rotation = pickup.transform.rotation * Quaternion.Euler(0f, -24f, -7f);
            GameObject right = CreateBox("Blade_Right", ploughRoot, bladeCenter + pickup.transform.right * 0.52f,
                new Vector3(1.35f, 0.58f, 0.16f), materials.SafetyRed);
            right.transform.rotation = pickup.transform.rotation * Quaternion.Euler(0f, 24f, 7f);
            CreateBox("Blade_SnowCap", ploughRoot, bladeCenter + Vector3.up * 0.31f,
                new Vector3(2.35f, 0.08f, 0.23f), materials.Snow).transform.rotation = pickup.transform.rotation;
        }

        private static void BuildBeginnerTrainingZone(Transform parent, BuildMaterials materials)
        {
            const float laneX = 54.3f;
            var laneCenter = new List<Vector3>();
            const int samples = 30;
            for (int i = 0; i < samples; i++)
            {
                float z = Mathf.Lerp(76f, 105f, i / (float)(samples - 1));
                laneCenter.Add(new Vector3(laneX, HillHeight(laneX, z), z));
            }

            Mesh lane = SaveMesh(CreateHillConformingRibbon(laneCenter, 3.55f, 0.064f),
                GeneratedRoot + "/MSH_BeginnerTrainingLane.asset");
            CreateMeshObject("BeginnerLane_GroomedSnow", parent, lane, materials.Piste, false, false);

            var beltCenter = new List<Vector3>();
            for (int i = 0; i < samples; i++)
            {
                float z = Mathf.Lerp(77.5f, 103.5f, i / (float)(samples - 1));
                beltCenter.Add(new Vector3(58.45f, HillHeight(58.45f, z), z));
            }
            Mesh belt = SaveMesh(CreateHillConformingRibbon(beltCenter, 0.52f, 0.105f),
                GeneratedRoot + "/MSH_BeginnerMagicCarpet.asset");
            CreateMeshObject("ProjectOwned_MagicCarpet", parent, belt, materials.Road, false, false);
            BuildMagicCarpetRails(parent, beltCenter, materials);

            BuildTrainingArch(parent, "BeginnerEntryArch", laneX, 77f, materials.SafetyBlue, materials.SafetyRed);
            BuildTrainingArch(parent, "BeginnerSummitArch", laneX, 104.5f, materials.SafetyRed, materials.SafetyBlue);

            Mesh coneMesh = SaveMesh(CreateConeMesh(0.34f, 0.95f, 12), GeneratedRoot + "/MSH_TrainingCone.asset");
            for (int i = 0; i < 8; i++)
            {
                float z = 81f + i * 2.85f;
                float x = laneX + Mathf.Sin(i * 1.65f) * 1.65f;
                GameObject cone = CreateMeshObject("TrainingCone_" + (i + 1).ToString("00"), parent, coneMesh,
                    i % 2 == 0 ? materials.SafetyRed : materials.SafetyBlue, false, false);
                cone.transform.position = new Vector3(x, HillHeight(x, z) + 0.07f, z);
            }

            BuildFenceRun(parent, "BeginnerSafetyFence_Left", new Vector3(49.9f, 0f, 76f), new Vector3(49.9f, 0f, 106f), materials.SafetyRed);
            BuildFenceRun(parent, "BeginnerSafetyFence_Right", new Vector3(60.5f, 0f, 76f), new Vector3(60.5f, 0f, 106f), materials.SafetyBlue);
        }

        private static void BuildMagicCarpetRails(Transform parent, IReadOnlyList<Vector3> center, BuildMaterials materials)
        {
            for (int side = -1; side <= 1; side += 2)
            {
                var rail = new List<Vector3>(center.Count);
                for (int i = 0; i < center.Count; i++)
                    rail.Add(center[i] + Vector3.right * side * 0.72f + Vector3.up * 0.34f);

                for (int i = 0; i < rail.Count - 1; i++)
                    CreateCylinderBetween("MagicCarpetRail_" + side + "_" + i.ToString("00"), parent, rail[i], rail[i + 1], 0.045f, materials.SafetyBlue);
            }

            Vector3 lower = center[0] + Vector3.up * 0.18f;
            Vector3 upper = center[center.Count - 1] + Vector3.up * 0.18f;
            CreateCylinderBetween("MagicCarpet_LowerRoller", parent, lower + Vector3.left * 0.58f, lower + Vector3.right * 0.58f, 0.16f, materials.Cable);
            CreateCylinderBetween("MagicCarpet_UpperRoller", parent, upper + Vector3.left * 0.58f, upper + Vector3.right * 0.58f, 0.16f, materials.Cable);
        }

        private static void BuildTrainingArch(Transform parent, string name, float x, float z, Material leftMaterial, Material rightMaterial)
        {
            Transform root = NewChild(name, parent).transform;
            float leftY = HillHeight(x - 3.05f, z);
            float rightY = HillHeight(x + 3.05f, z);
            CreateBox("LeftPost", root, new Vector3(x - 3.05f, leftY + 1.6f, z), new Vector3(0.28f, 3.2f, 0.28f), leftMaterial);
            CreateBox("RightPost", root, new Vector3(x + 3.05f, rightY + 1.6f, z), new Vector3(0.28f, 3.2f, 0.28f), rightMaterial);
            CreateBox("Header", root, new Vector3(x, Mathf.Max(leftY, rightY) + 3.05f, z), new Vector3(6.35f, 0.36f, 0.36f), leftMaterial);
        }

        private static void BuildSnowPark(Transform parent, BuildMaterials materials, Scene scene)
        {
            Mesh largeRamp = SaveMesh(CreateSnowRampMesh(12.5f, 111f, 5.4f, 8f, 1.25f),
                GeneratedRoot + "/MSH_SnowParkRamp_Large.asset");
            CreateMeshObject("SnowParkRamp_Large", parent, largeRamp, materials.FeatureSnow, true, false);

            Mesh smallRamp = SaveMesh(CreateSnowRampMesh(15.2f, 94f, 4.4f, 6.2f, 0.86f),
                GeneratedRoot + "/MSH_SnowParkRamp_Small.asset");
            CreateMeshObject("SnowParkRamp_Small", parent, smallRamp, materials.FeatureSnow, true, false);

            BuildSnowParkRail(parent, "SnowParkRail_Long", new Vector3(10f, 0f, 120f), new Vector3(10f, 0f, 113.5f), materials.Cable);
            BuildSnowParkRail(parent, "SnowParkRail_Short", new Vector3(17f, 0f, 104f), new Vector3(17f, 0f, 99.5f), materials.SafetyBlue);

            PlaceGroundedPrefab(InformationSignPath, "SnowParkInformation", new Vector3(19.3f, 0f, 87f), 170f, 0.94f, 0.02f, parent, scene);
            PlaceGroundedPrefab(SnowdriftPath, "SnowParkRampBank_01", new Vector3(8.7f, 0f, 110f), 12f, 0.92f, -0.04f, parent, scene);
            PlaceGroundedPrefab(SnowdriftPath, "SnowParkRampBank_02", new Vector3(18.2f, 0f, 94f), -18f, 0.82f, -0.04f, parent, scene);
            BuildFenceRun(parent, "SnowParkSafetyFence", new Vector3(6.8f, 0f, 86f), new Vector3(6.8f, 0f, 126f), materials.SafetyRed);
        }

        private static void BuildSnowParkRail(Transform parent, string name, Vector3 from, Vector3 to, Material material)
        {
            from.y = HillHeight(from.x, from.z) + 0.85f;
            to.y = HillHeight(to.x, to.z) + 0.85f;
            Transform root = NewChild(name, parent).transform;
            CreateCylinderBetween("TopRail", root, from, to, 0.075f, material);
            CreateCylinderBetween("Support_A", root, new Vector3(from.x, HillHeight(from.x, from.z), from.z), from, 0.065f, material);
            CreateCylinderBetween("Support_B", root, new Vector3(to.x, HillHeight(to.x, to.z), to.z), to, 0.065f, material);
        }

        private static void BuildSummitLodge(Transform parent, Transform lightingRoot, BuildMaterials materials, Scene scene)
        {
            float padY = HillHeight(SummitPadCenterX, SummitPadCenterZ);
            CreateBox("SummitViewDeck", parent, new Vector3(SummitPadCenterX, padY + 0.14f, SummitPadCenterZ),
                new Vector3(12.5f, 0.28f, 7.4f), materials.Wood);
            GameObject snowCap = CreateBox("SummitDeck_SnowCap", parent, new Vector3(SummitPadCenterX, padY + 0.31f, SummitPadCenterZ),
                new Vector3(12.1f, 0.08f, 7f), materials.Snow);
            Collider snowCapCollider = snowCap.GetComponent<Collider>();
            if (snowCapCollider != null) UnityEngine.Object.DestroyImmediate(snowCapCollider);

            GameObject cabin = PlacePrefab(SummitHousePath, "SummitLodge_LitHouse",
                new Vector3(SummitPadCenterX - 1.2f, padY + 0.35f, SummitPadCenterZ + 0.7f), 182f, 0.72f, parent, scene);
            GroundPrefabByRendererBounds(cabin, padY + 0.35f, 0.015f);

            BuildDeckFence(parent, materials, padY + 0.36f);
            BuildTrainingArch(parent, "SummitPhotoPoint", SummitPadCenterX + 3.3f, SummitPadCenterZ - 2.4f,
                materials.SafetyRed, materials.SafetyBlue);

            PlaceGroundedPrefab(RedFlagPath, "SummitFlag_Red", new Vector3(7.4f, 0f, 137f), 0f, 1.25f, 0.03f, parent, scene);
            PlaceGroundedPrefab(BlueFlagPath, "SummitFlag_Blue", new Vector3(20.4f, 0f, 137f), 180f, 1.25f, 0.03f, parent, scene);

            Vector3[] lanterns =
            {
                new Vector3(7.8f, padY + 0.34f, 136.2f), new Vector3(19.2f, padY + 0.34f, 136.2f),
                new Vector3(7.8f, padY + 0.34f, 142f), new Vector3(19.2f, padY + 0.34f, 142f)
            };
            for (int i = 0; i < lanterns.Length; i++)
                PlacePrefab(LanternPath, "SummitLantern_" + (i + 1).ToString("00"), lanterns[i], 0f, 0.92f, lightingRoot, scene);
        }

        private static void BuildDeckFence(Transform parent, BuildMaterials materials, float deckY)
        {
            Vector3[] corners =
            {
                new Vector3(7.5f, deckY, 135.4f), new Vector3(19.5f, deckY, 135.4f),
                new Vector3(19.5f, deckY, 142.6f), new Vector3(7.5f, deckY, 142.6f)
            };
            for (int i = 0; i < corners.Length; i++)
            {
                Vector3 p = corners[i];
                CreateBox("SummitDeckPost_" + i.ToString("00"), parent, p + Vector3.up * 0.72f,
                    new Vector3(0.16f, 1.44f, 0.16f), materials.Wood);
            }
            CreateCylinderBetween("SummitDeckRail_South", parent, corners[0] + Vector3.up, corners[1] + Vector3.up, 0.07f, materials.Wood);
            CreateCylinderBetween("SummitDeckRail_East", parent, corners[1] + Vector3.up, corners[2] + Vector3.up, 0.07f, materials.Wood);
            CreateCylinderBetween("SummitDeckRail_North", parent, corners[2] + Vector3.up, corners[3] + Vector3.up, 0.07f, materials.Wood);
            CreateCylinderBetween("SummitDeckRail_West", parent, corners[3] + Vector3.up, corners[0] + Vector3.up, 0.07f, materials.Wood);
        }

        private static void BuildPenguinLife(Transform parent, Scene scene)
        {
            PropSpec[] penguins =
            {
                new PropSpec(PenguinPath, "Penguin_BaseQueue_01", 6.5f, 73f, 150f, 0.92f, 0.02f),
                new PropSpec(PenguinPath, "Penguin_BaseQueue_02", 9f, 74f, 185f, 0.86f, 0.02f),
                new PropSpec(PenguinPath, "Penguin_Beginner_01", 53f, 82f, 22f, 0.82f, 0.02f),
                new PropSpec(PenguinPath, "Penguin_Beginner_02", 56.2f, 96f, 198f, 0.88f, 0.02f),
                new PropSpec(PenguinPath, "Penguin_SnowPark", 18.5f, 108f, 245f, 0.90f, 0.02f),
                new PropSpec(PenguinPath, "Penguin_LiftQueue", 43f, 75f, 4f, 0.86f, 0.02f),
                new PropSpec(PenguinPath, "Penguin_SummitPhoto", 17.3f, 137f, 164f, 0.92f, 0.34f)
            };

            for (int i = 0; i < penguins.Length; i++)
            {
                PropSpec spec = penguins[i];
                GameObject penguin = PlaceGroundedPrefab(spec.Path, spec.Name, spec.Position, spec.Yaw,
                    spec.Scale, spec.VerticalOffset, parent, scene);
                if (penguin == null) continue;
                GroundPrefabByRendererBounds(penguin, HillHeight(spec.Position.x, spec.Position.z) + spec.VerticalOffset, 0f);
                DisableBehaviourAndPhysics(penguin);
            }
        }

        private static void DisableBehaviourAndPhysics(GameObject instance)
        {
            foreach (MonoBehaviour behaviour in instance.GetComponentsInChildren<MonoBehaviour>(true))
                behaviour.enabled = false;

            foreach (Rigidbody body in instance.GetComponentsInChildren<Rigidbody>(true))
            {
                body.isKinematic = true;
                body.useGravity = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
            }

            foreach (Collider collider in instance.GetComponentsInChildren<Collider>(true))
                collider.enabled = false;
        }

        private static void ImproveSkiDistrictBackdropClearance()
        {
            // The near-foothill mesh was authored before the northern ski district and
            // contains foreground peaks across the playable map footprint. The far
            // layered Himalaya and crown massif remain active, so disabling only this
            // layer preserves the mountain skyline while clearing the snow park and piste.
            GameObject nearFoothills = GameObject.Find("FacetedSnowFoothills");
            if (nearFoothills != null)
                nearFoothills.SetActive(false);
        }

        private static void BuildSafetyFences(Transform parent, BuildMaterials materials)
        {
            BuildFenceRun(parent, "SummitFence_Left", new Vector3(7f, 0f, 142f), new Vector3(21f, 0f, 142f), materials.Wood);
            BuildFenceRun(parent, "SummitFence_Right", new Vector3(42f, 0f, 142f), new Vector3(61f, 0f, 142f), materials.Wood);
            BuildFenceRun(parent, "BaseFence_West", new Vector3(-1f, 0f, 70f), new Vector3(13f, 0f, 73f), materials.Wood);
        }

        private static void BuildFenceRun(Transform parent, string name, Vector3 from, Vector3 to, Material material)
        {
            GameObject root = NewChild(name, parent);
            float distance = Vector3.Distance(from, to);
            int posts = Mathf.Max(2, Mathf.CeilToInt(distance / 3f) + 1);
            var postTops = new List<Vector3>();
            for (int i = 0; i < posts; i++)
            {
                float t = i / (float)(posts - 1);
                Vector3 p = Vector3.Lerp(from, to, t);
                p.y = HillHeight(p.x, p.z);
                CreateBox("Post_" + i.ToString("00"), root.transform, p + Vector3.up * 0.75f, new Vector3(0.16f, 1.5f, 0.16f), material);
                postTops.Add(p + Vector3.up * 0.95f);
            }

            for (int i = 0; i < postTops.Count - 1; i++)
            {
                CreateCylinderBetween("Rail_" + i.ToString("00"), root.transform,
                    postTops[i], postTops[i + 1], 0.07f, material);
            }
        }

        private static float HillHeight(float x, float z)
        {
            float rawHeight = UngradedHillHeight(x, z);
            float padTarget = UngradedHillHeight(SummitPadCenterX, SummitPadCenterZ);
            float xBlend = 1f - Smooth01(Mathf.InverseLerp(5.7f, 8.3f, Mathf.Abs(x - SummitPadCenterX)));
            float zBlend = 1f - Smooth01(Mathf.InverseLerp(3.4f, 5.9f, Mathf.Abs(z - SummitPadCenterZ)));
            return Mathf.Lerp(rawHeight, padTarget, xBlend * zBlend);
        }

        private static float UngradedHillHeight(float x, float z)
        {
            float climb = Mathf.Max(0f, z - ClimbStartZ);
            float bottomBlend = Smooth01(Mathf.InverseLerp(ClimbStartZ, ClimbStartZ + 7f, z));
            float mainHeight = BaseY + climb * ClimbPerMeter * bottomBlend;
            float t = Mathf.Clamp01(Mathf.InverseLerp(ClimbStartZ, HillMaxZ, z));
            float halfWidth = Mathf.Lerp(36f, 31f, Mathf.Clamp01(Mathf.InverseLerp(HillMinZ, HillMaxZ, z)));
            float pisteHalf = Mathf.Lerp(11f, 8.2f, t);
            float lateral = Mathf.Abs(x - CenterX);
            float side = Smooth01(Mathf.InverseLerp(pisteHalf + 3f, halfWidth, lateral));
            float sideRise = side * Mathf.Lerp(0.16f, 4.15f, t);
            float undulation = side * (Mathf.Sin(x * 0.24f + z * 0.12f) * 0.24f + Mathf.Sin(z * 0.31f) * 0.12f);
            return mainHeight + sideRise + undulation;
        }

        private static Mesh CreateRibbon(IReadOnlyList<Vector3> center, float halfWidth)
        {
            if (center.Count < 2) throw new ArgumentException("A ribbon requires at least two points.", nameof(center));
            var vertices = new Vector3[center.Count * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(center.Count - 1) * 6];
            float distance = 0f;

            for (int i = 0; i < center.Count; i++)
            {
                Vector3 previous = center[Mathf.Max(0, i - 1)];
                Vector3 next = center[Mathf.Min(center.Count - 1, i + 1)];
                Vector3 tangent = next - previous;
                tangent.y = 0f;
                tangent.Normalize();
                Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x);
                if (i > 0) distance += Vector3.Distance(center[i - 1], center[i]);
                // Keep vertex 0 on the geometric left and vertex 1 on the right.
                // The triangle winding below then faces upward, so roads and groom lines
                // remain visible from both the gameplay camera and top-down minimap.
                vertices[i * 2] = center[i] + normal * halfWidth;
                vertices[i * 2 + 1] = center[i] - normal * halfWidth;
                uv[i * 2] = new Vector2(0f, distance * 0.2f);
                uv[i * 2 + 1] = new Vector2(1f, distance * 0.2f);
            }

            int index = 0;
            for (int i = 0; i < center.Count - 1; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[index++] = a; triangles[index++] = c; triangles[index++] = b;
                triangles[index++] = b; triangles[index++] = c; triangles[index++] = d;
            }

            var mesh = new Mesh { name = "MSH_Ribbon" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateHillConformingRibbon(IReadOnlyList<Vector3> center, float halfWidth, float verticalOffset)
        {
            if (center.Count < 2) throw new ArgumentException("A ribbon requires at least two points.", nameof(center));
            var vertices = new Vector3[center.Count * 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[(center.Count - 1) * 6];
            float distance = 0f;

            for (int i = 0; i < center.Count; i++)
            {
                Vector3 previous = center[Mathf.Max(0, i - 1)];
                Vector3 next = center[Mathf.Min(center.Count - 1, i + 1)];
                Vector3 tangent = next - previous;
                tangent.y = 0f;
                tangent.Normalize();
                Vector3 normal = new Vector3(-tangent.z, 0f, tangent.x);
                if (i > 0) distance += Vector3.Distance(center[i - 1], center[i]);

                Vector3 left = center[i] + normal * halfWidth;
                Vector3 right = center[i] - normal * halfWidth;
                left.y = HillHeight(left.x, left.z) + verticalOffset;
                right.y = HillHeight(right.x, right.z) + verticalOffset;
                vertices[i * 2] = left;
                vertices[i * 2 + 1] = right;
                uv[i * 2] = new Vector2(0f, distance * 0.2f);
                uv[i * 2 + 1] = new Vector2(1f, distance * 0.2f);
            }

            int index = 0;
            for (int i = 0; i < center.Count - 1; i++)
            {
                int a = i * 2;
                int b = a + 1;
                int c = a + 2;
                int d = a + 3;
                triangles[index++] = a; triangles[index++] = c; triangles[index++] = b;
                triangles[index++] = b; triangles[index++] = c; triangles[index++] = d;
            }

            var mesh = new Mesh { name = "MSH_HillConformingRibbon" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static void AddQuad(List<Vector3> vertices, List<int> triangles, Vector3 topA, Vector3 topB, Vector3 bottomA, Vector3 bottomB, bool flip = false)
        {
            int start = vertices.Count;
            vertices.Add(topA);
            vertices.Add(topB);
            vertices.Add(bottomA);
            vertices.Add(bottomB);
            if (!flip)
            {
                triangles.Add(start); triangles.Add(start + 2); triangles.Add(start + 1);
                triangles.Add(start + 1); triangles.Add(start + 2); triangles.Add(start + 3);
            }
            else
            {
                triangles.Add(start); triangles.Add(start + 1); triangles.Add(start + 2);
                triangles.Add(start + 1); triangles.Add(start + 3); triangles.Add(start + 2);
            }
        }

        private static Mesh CreateConeMesh(float radius, float height, int segments)
        {
            segments = Mathf.Max(3, segments);
            var vertices = new Vector3[segments + 2];
            var uv = new Vector2[vertices.Length];
            var triangles = new int[segments * 6];
            vertices[0] = new Vector3(0f, height, 0f);
            vertices[1] = Vector3.zero;
            uv[0] = new Vector2(0.5f, 1f);
            uv[1] = new Vector2(0.5f, 0.5f);

            for (int i = 0; i < segments; i++)
            {
                float angle = i / (float)segments * Mathf.PI * 2f;
                vertices[i + 2] = new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
                uv[i + 2] = new Vector2(i / (float)segments, 0f);
            }

            int index = 0;
            for (int i = 0; i < segments; i++)
            {
                int current = i + 2;
                int next = (i + 1) % segments + 2;
                triangles[index++] = 0;
                triangles[index++] = current;
                triangles[index++] = next;
                triangles[index++] = 1;
                triangles[index++] = next;
                triangles[index++] = current;
            }

            var mesh = new Mesh { name = "MSH_TrainingCone" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Mesh CreateSnowRampMesh(float centerX, float centerZ, float width, float length, float lipHeight)
        {
            float left = centerX - width * 0.5f;
            float right = centerX + width * 0.5f;
            float downhill = centerZ - length * 0.5f;
            float uphill = centerZ + length * 0.5f;
            float surfaceOffset = 0.065f;

            Vector3 leftDownBase = new Vector3(left, HillHeight(left, downhill) + surfaceOffset, downhill);
            Vector3 rightDownBase = new Vector3(right, HillHeight(right, downhill) + surfaceOffset, downhill);
            Vector3 leftUp = new Vector3(left, HillHeight(left, uphill) + surfaceOffset, uphill);
            Vector3 rightUp = new Vector3(right, HillHeight(right, uphill) + surfaceOffset, uphill);
            Vector3 leftLip = leftDownBase + Vector3.up * lipHeight;
            Vector3 rightLip = rightDownBase + Vector3.up * lipHeight;

            Vector3[] vertices =
            {
                leftDownBase, rightDownBase, leftUp, rightUp, leftLip, rightLip
            };
            int[] triangles =
            {
                4, 2, 5, 5, 2, 3,
                0, 4, 1, 1, 4, 5,
                0, 2, 4,
                1, 5, 3,
                0, 1, 2, 1, 3, 2
            };
            Vector2[] uv =
            {
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0f, 1f),
                new Vector2(1f, 1f), new Vector2(0f, 0.15f), new Vector2(1f, 0.15f)
            };

            var mesh = new Mesh { name = "MSH_SnowParkRamp" };
            mesh.vertices = vertices;
            mesh.uv = uv;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static GameObject CreateMeshObject(string name, Transform parent, Mesh mesh, Material material, bool collider, bool shadows)
        {
            GameObject go = new GameObject(name);
            go.transform.SetParent(parent, false);
            MeshFilter filter = go.AddComponent<MeshFilter>();
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            filter.sharedMesh = mesh;
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = shadows ? ShadowCastingMode.On : ShadowCastingMode.Off;
            renderer.receiveShadows = true;
            if (collider)
            {
                MeshCollider meshCollider = go.AddComponent<MeshCollider>();
                meshCollider.sharedMesh = mesh;
            }
            GameObjectUtility.SetStaticEditorFlags(go, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic | StaticEditorFlags.OccludeeStatic);
            return go;
        }

        private static GameObject CreateDisc(string name, Transform parent, Vector3 position, Vector3 radiiAndHalfHeight,
            Material material, bool collider)
        {
            GameObject disc = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            disc.name = name;
            disc.transform.SetParent(parent, true);
            disc.transform.position = position;
            disc.transform.localScale = new Vector3(radiiAndHalfHeight.x * 2f, radiiAndHalfHeight.y, radiiAndHalfHeight.z * 2f);
            disc.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!collider)
            {
                Collider primitiveCollider = disc.GetComponent<Collider>();
                if (primitiveCollider != null) UnityEngine.Object.DestroyImmediate(primitiveCollider);
            }
            GameObjectUtility.SetStaticEditorFlags(disc, StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccludeeStatic);
            return disc;
        }

        private static GameObject PlaceGroundedPrefab(string path, string name, Vector3 position, float yaw, float scale, float verticalOffset, Transform parent, Scene scene)
        {
            position.y = HillHeight(position.x, position.z) + verticalOffset;
            return PlacePrefab(path, name, position, yaw, scale, parent, scene);
        }

        private static GameObject PlacePrefab(string path, string name, Vector3 position, float yaw, float scale, Transform parent, Scene scene)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null)
            {
                Debug.LogWarning("[ConceptMapSkiResortBuilder] Individual prop was not found: " + path);
                return null;
            }

            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null) return null;
            instance.name = name;
            instance.transform.SetParent(parent, true);
            instance.transform.position = position;
            instance.transform.rotation = Quaternion.Euler(0f, yaw, 0f);
            instance.transform.localScale = Vector3.one * scale;
            return instance;
        }

        private static GameObject CreateBox(string name, Transform parent, Vector3 position, Vector3 scale, Material material)
        {
            GameObject box = GameObject.CreatePrimitive(PrimitiveType.Cube);
            box.name = name;
            box.transform.SetParent(parent, true);
            box.transform.position = position;
            box.transform.localScale = scale;
            box.GetComponent<MeshRenderer>().sharedMaterial = material;
            return box;
        }

        private static GameObject CreateCylinderBetween(string name, Transform parent, Vector3 a, Vector3 b, float radius, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent, true);
            Vector3 delta = b - a;
            cylinder.transform.position = (a + b) * 0.5f;
            cylinder.transform.up = delta.normalized;
            cylinder.transform.localScale = new Vector3(radius, delta.magnitude * 0.5f, radius);
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
            Collider collider = cylinder.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
            return cylinder;
        }

        private static Mesh SaveMesh(Mesh source, string path)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(source, path);
                return source;
            }

            EditorUtility.CopySerialized(source, existing);
            existing.name = source.name;
            EditorUtility.SetDirty(existing);
            UnityEngine.Object.DestroyImmediate(source);
            return existing;
        }

        private static void ExpandMapBounds()
        {
            GameObject boundsVolume = GameObject.Find("MinimapBoundsVolume");
            if (boundsVolume != null)
            {
                BoxCollider box = boundsVolume.GetComponent<BoxCollider>();
                boundsVolume.transform.position = new Vector3(5f, 12f, 45f);
                if (box != null)
                {
                    box.center = Vector3.zero;
                    box.size = new Vector3(134f, 30f, 206f);
                }
            }

            Component boundsComponent = GameObject.Find("WinterVillageMap")?.GetComponent("MapMinimapBounds");
            if (boundsComponent != null)
            {
                SerializedObject serialized = new SerializedObject(boundsComponent);
                SerializedProperty fallback = serialized.FindProperty("_fallbackSize");
                if (fallback != null) fallback.vector2Value = new Vector2(134f, 206f);
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            MoveBoundary("Boundary_North", new Vector3(5f, 12f, 148f), new Vector3(134f, 30f, 1f));
            MoveBoundary("Boundary_East", new Vector3(72f, 12f, 45f), new Vector3(1f, 30f, 206f));
            MoveBoundary("Boundary_West", new Vector3(-62f, 12f, 45f), new Vector3(1f, 30f, 206f));
            MoveBoundary("Boundary_South", new Vector3(5f, 12f, -58f), new Vector3(134f, 30f, 1f));
        }

        private static void MoveBoundary(string name, Vector3 position, Vector3 scale)
        {
            GameObject boundary = GameObject.Find(name);
            if (boundary == null) return;
            boundary.transform.position = position;
            boundary.transform.localScale = scale;
        }

        private static void CaptureQaViewsInternal(Scene scene)
        {
            GameObject cameraObject = new GameObject("__ConceptSkiResort_QA_Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.Skybox;
            camera.allowHDR = true;
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 650f;
            camera.fieldOfView = 48f;

            try
            {
                Capture(camera, "01_SkiResort_Isometric.png", new Vector3(125f, 96f, 9f), new Vector3(29f, 10f, 99f), false, 65f);
                Capture(camera, "02_SkiResort_Reverse.png", new Vector3(-62f, 68f, 42f), new Vector3(30f, 11f, 105f), false, 65f);
                Capture(camera, "03_SkiResort_TopDown.png", new Vector3(31f, 190f, 98f), new Vector3(31f, 0f, 98f), true, 67f);
                Capture(camera, "04_VillageToResort_Connection.png", new Vector3(103f, 62f, 0f), new Vector3(22f, 5f, 70f), false, 55f);
                Capture(camera, "05_BaseVillageServices.png", new Vector3(68f, 31f, 39f), new Vector3(14f, 2f, 69f), false, 46f);
                Capture(camera, "06_BeginnerTrainingZone.png", new Vector3(101f, 53f, 63f), new Vector3(54f, 8f, 91f), false, 46f);
                Capture(camera, "07_SnowPark.png", new Vector3(-35f, 51f, 77f), new Vector3(13f, 10f, 106f), false, 46f);
                Capture(camera, "08_SummitLodge.png", new Vector3(-27f, 49f, 118f), new Vector3(14f, 23f, 139f), false, 46f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(cameraObject);
                AssetDatabase.Refresh();
            }
        }

        private static void Capture(Camera camera, string fileName, Vector3 position, Vector3 target, bool orthographic, float sizeOrFov)
        {
            camera.transform.position = position;
            camera.transform.rotation = Quaternion.LookRotation((target - position).normalized, Vector3.up);
            camera.orthographic = orthographic;
            if (orthographic) camera.orthographicSize = sizeOrFov;
            else camera.fieldOfView = sizeOrFov;

            const int width = 1600;
            const int height = 900;
            RenderTexture renderTexture = RenderTexture.GetTemporary(width, height, 24, RenderTextureFormat.ARGB32);
            RenderTexture previous = RenderTexture.active;
            camera.targetTexture = renderTexture;
            camera.Render();
            RenderTexture.active = renderTexture;
            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false, false);
            texture.ReadPixels(new Rect(0, 0, width, height), 0, 0);
            texture.Apply();
            File.WriteAllBytes(Path.Combine(PreviewRoot, fileName), texture.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(texture);
            camera.targetTexture = null;
            RenderTexture.active = previous;
            RenderTexture.ReleaseTemporary(renderTexture);
        }

        private static GameObject NewChild(string name, Transform parent)
        {
            var child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child;
        }

        private static float Smooth01(float t)
        {
            t = Mathf.Clamp01(t);
            return t * t * (3f - 2f * t);
        }

        private static List<Vector2> SampleCatmullRom(IReadOnlyList<Vector2> control, int samplesPerSegment)
        {
            if (control == null || control.Count < 2)
                throw new ArgumentException("A spline requires at least two control points.", nameof(control));

            samplesPerSegment = Mathf.Max(2, samplesPerSegment);
            var result = new List<Vector2>((control.Count - 1) * samplesPerSegment + 1);
            for (int segment = 0; segment < control.Count - 1; segment++)
            {
                Vector2 p0 = control[Mathf.Max(0, segment - 1)];
                Vector2 p1 = control[segment];
                Vector2 p2 = control[segment + 1];
                Vector2 p3 = control[Mathf.Min(control.Count - 1, segment + 2)];

                for (int sample = 0; sample < samplesPerSegment; sample++)
                {
                    float t = sample / (float)samplesPerSegment;
                    float t2 = t * t;
                    float t3 = t2 * t;
                    Vector2 point = 0.5f * ((2f * p1) +
                        (-p0 + p2) * t +
                        (2f * p0 - 5f * p1 + 4f * p2 - p3) * t2 +
                        (-p0 + 3f * p1 - 3f * p2 + p3) * t3);
                    result.Add(point);
                }
            }

            result.Add(control[control.Count - 1]);
            return result;
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
