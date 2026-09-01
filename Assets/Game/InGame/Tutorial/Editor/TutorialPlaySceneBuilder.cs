using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// Builds a compact village-shaped tutorial that keeps the production tutorial logic while
    /// replacing the fenced training rooms with a replayable SinglePlay-inspired road layout.
    /// </summary>
    public static class TutorialPlaySceneBuilder
    {
        public const string ScenePath = "Assets/Game/InGame/Tutorial/Scenes/TutorialPlay.unity";

        private const string SourceScenePath = PenguinTutorialSceneBuilder.ScenePath;
        private const string WarehousePrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/PF_SnowballWarehouse.prefab";
        private const string QuestJournalUxmlPath =
            "Assets/Game/InGame/Tutorial/UI/TutorialQuestJournal.uxml";
        private const string SantaGuideUxmlPath =
            "Assets/Game/InGame/Tutorial/UI/TutorialSantaGuide.uxml";
        private const string SantaPortraitPath =
            "Assets/Game/InGame/Tutorial/Cutscene/Images/TutorialComic_03_SantaLeft_GoldenCelV4Matched.png";
        private const string RoadMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialPlayRoad.mat";
        private const string PathMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialPlayPath.mat";
        private const string FenceMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialPlayFence.mat";
        private const string RockMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialPlayRock.mat";
        private const string SnowMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialSnowGround.mat";
        private const string EdgeMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialEdge.mat";
        private const string JunctionMeshFolderPath =
            "Assets/Game/InGame/Tutorial/Generated/TutorialPlay/JunctionMeshes";
        private const string UnsavedSceneBackupFolderPath =
            "Assets/Game/InGame/Tutorial/Generated/TutorialPlay/Recovery";

        private const string LanternPrefabPath =
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit/05_Lighting_VFX/PF_MapKit_WinterLantern_Glow.prefab";
        private const string TreePrefabPath =
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit/02_Nature/PF_MapKit_WinterTreeSway_FirMedium.prefab";

        private static readonly string[] HousePrefabPaths =
        {
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit/01_Buildings/PF_MapKit_WinterHouse_Lit_A.prefab",
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit/01_Buildings/PF_MapKit_WoodenWinterHouse_Lit_A.prefab",
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit/01_Buildings/PF_MapKit_WinterHouse_Lit_B.prefab",
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit/01_Buildings/PF_MapKit_WoodenWinterHouse_Lit_B.prefab"
        };

        private static readonly Vector3 PlayerSpawn = new Vector3(0f, 0.6f, -30f);
        private static readonly Vector3 WalkTarget = new Vector3(3f, 0.04f, -25f);
        private static readonly Vector3 RunTarget = new Vector3(8f, 0.04f, -20f);
        private static readonly Vector3 SlideTarget = new Vector3(-1f, 0.04f, -10f);
        private static readonly Vector3 SnowballSpawn = new Vector3(-35f, 0.25f, -5f);
        private static readonly Vector3 MachinePosition = new Vector3(-6f, 0f, 5f);
        private static readonly Vector3 SenderPosition = new Vector3(-18f, 0.13f, 7.5f);
        private static readonly Vector3 WarehousePosition = new Vector3(-39f, 0.1f, 24f);

        private static readonly Vector2 WalkAreaCenter = new Vector2(3f, -27f);
        private static readonly Vector2 WalkAreaSize = new Vector2(14f, 12f);
        private static readonly Vector2 RunAreaCenter = new Vector2(7f, -21f);
        private static readonly Vector2 RunAreaSize = new Vector2(16f, 14f);
        private static readonly Vector2 SlideAreaCenter = new Vector2(0f, -11f);
        private static readonly Vector2 SlideAreaSize = new Vector2(22f, 18f);

        private static readonly Vector3[] TreePositions =
        {
            new(-42f, 0f, -29f), new(-43f, 0f, -12f), new(-45f, 0f, 8f),
            new(-30f, 0f, 33f), new(-22f, 0f, 31f), new(25f, 0f, 31f),
            new(42f, 0f, 29f), new(44f, 0f, 14f), new(44f, 0f, -7f),
            new(42f, 0f, -29f), new(20f, 0f, -30f), new(-14f, 0f, -31f),
            new(-31f, 0f, -14f), new(18f, 0f, 13f), new(20f, 0f, -5f)
        };

        private static readonly Vector3[] LanternPositions =
        {
            new(-40f, 0f, -5f), new(-40f, 0f, 5f), new(-46f, 0f, 30f),
            new(-20f, 0f, 29f), new(8f, 0f, 35f), new(29f, 0f, 31f),
            new(45f, 0f, 15f), new(45f, 0f, -10f), new(39f, 0f, -30f),
            new(-10f, 0f, -35f)
        };

        [InitializeOnLoadMethod]
        private static void QueueFirstBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;
            EditorApplication.delayCall += BuildIfMissing;
        }

        private static void BuildIfMissing()
        {
            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                EditorApplication.delayCall += BuildIfMissing;
                return;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null) Build();
        }

        [MenuItem("PPack/Tutorial/Build TutorialPlay Scene")]
        public static void Build()
        {
            SceneAsset source = AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath);
            GameObject warehousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            VisualTreeAsset questJournalUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(QuestJournalUxmlPath);
            VisualTreeAsset santaGuideUxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SantaGuideUxmlPath);
            Texture2D santaPortrait = AssetDatabase.LoadAssetAtPath<Texture2D>(SantaPortraitPath);
            if (source == null) throw new InvalidOperationException($"기준 튜토리얼 씬이 없다: {SourceScenePath}");
            if (warehousePrefab == null || warehousePrefab.GetComponent<SnowballWarehouseStorage>() == null)
                throw new InvalidOperationException($"창고 프리팹 또는 저장 컴포넌트가 없다: {WarehousePrefabPath}");
            if (questJournalUxml == null)
                throw new InvalidOperationException($"퀘스트 일지 UXML이 없다: {QuestJournalUxmlPath}");
            if (santaGuideUxml == null || santaPortrait == null)
                throw new InvalidOperationException("산타 길라잡이 UXML 또는 초상 이미지가 없다.");

            GameObject[] housePrefabs = new GameObject[HousePrefabPaths.Length];
            for (int i = 0; i < HousePrefabPaths.Length; i++)
            {
                housePrefabs[i] = AssetDatabase.LoadAssetAtPath<GameObject>(HousePrefabPaths[i]);
                if (housePrefabs[i] == null)
                    throw new InvalidOperationException($"집 프리팹이 없다: {HousePrefabPaths[i]}");
            }

            GameObject lanternPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(LanternPrefabPath);
            GameObject treePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(TreePrefabPath);
            if (lanternPrefab == null || treePrefab == null)
                throw new InvalidOperationException("TutorialPlay용 랜턴 또는 나무 프리팹이 없다.");

            bool reopenTargetAfterBuild = false;
            Scene loadedTarget = SceneManager.GetSceneByPath(ScenePath);
            if (loadedTarget.IsValid() && loadedTarget.isLoaded)
            {
                if (loadedTarget.isDirty)
                {
                    EnsureAssetFolder(UnsavedSceneBackupFolderPath);
                    string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
                    string backupPath = $"{UnsavedSceneBackupFolderPath}/TutorialPlay_Unsaved_{timestamp}.unity";
                    if (!EditorSceneManager.SaveScene(loadedTarget, backupPath, true))
                        throw new InvalidOperationException("현재 열린 TutorialPlay의 미저장 복구본을 만들지 못했다.");
                    Debug.LogWarning($"TutorialPlay 미저장 변경을 복구용 씬에 보존했다: {backupPath}");
                }
                EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Single);
                reopenTargetAfterBuild = true;
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
                FileUtil.ReplaceFile(SourceScenePath, ScenePath);
            else if (!AssetDatabase.CopyAsset(SourceScenePath, ScenePath))
                throw new InvalidOperationException($"TutorialPlay 씬 복사 실패: {ScenePath}");
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);

            Scene originalScene = SceneManager.GetActiveScene();
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(scene);
                GameObject root = FindRoot(scene, "PenguinTutorial");
                GameObject penguin = FindRoot(scene, "TutorialPenguin");
                if (root == null || penguin == null)
                    throw new InvalidOperationException("복사한 튜토리얼 씬의 루트 또는 펭귄이 없다.");

                root.name = "TutorialPlay";
                penguin.name = "TutorialPlayPenguin";

                SnowCpuStage snowStage = root.GetComponentInChildren<SnowCpuStage>(true);
                SnowGiftMachinePresentation machine = root.GetComponentInChildren<SnowGiftMachinePresentation>(true);
                GiftDeliveryTerminal sender = FindNamedComponent<GiftDeliveryTerminal>(root, "GiftDeliveryTerminal_SnowMachine");
                GiftDeliveryTerminal receiver = FindNamedComponent<GiftDeliveryTerminal>(root, "GiftDeliveryTerminal_Tutorial");
                PenguinTutorialDirector director = root.GetComponentInChildren<PenguinTutorialDirector>(true);
                PenguinTutorialHud legacyHud = root.GetComponentInChildren<PenguinTutorialHud>(true);
                UIDocument legacyHudDocument = legacyHud == null ? null : legacyHud.GetComponent<UIDocument>();
                if (snowStage == null || machine == null || sender == null || receiver == null || director == null ||
                    legacyHudDocument == null || legacyHudDocument.panelSettings == null)
                    throw new InvalidOperationException("TutorialPlay에 필요한 기존 튜토리얼 참조가 완전하지 않다.");

                sender.transform.SetParent(root.transform, true);
                receiver.transform.SetParent(root.transform, true);
                RemoveOldCampus(root.transform);
                RemoveStageGates(root);
                TutorialQuestJournal questJournal = ReplaceTutorialHud(
                    root.transform, legacyHud, legacyHudDocument.panelSettings, questJournalUxml);
                TutorialSantaGuide santaGuide = BuildSantaGuide(
                    root.transform, legacyHudDocument.panelSettings, santaGuideUxml, santaPortrait);

                Material snow = RequireMaterial(SnowMaterialPath);
                Material edge = RequireMaterial(EdgeMaterialPath);
                Material road = GetOrCreateMaterial(RoadMaterialPath, "M_TutorialPlayRoad",
                    new Color(0.065f, 0.105f, 0.155f), 0.28f);
                Material path = GetOrCreateMaterial(PathMaterialPath, "M_TutorialPlayPath",
                    new Color(0.48f, 0.63f, 0.73f), 0.22f);
                Material fence = GetOrCreateMaterial(FenceMaterialPath, "M_TutorialPlayFence",
                    new Color(0.23f, 0.12f, 0.07f), 0.16f);
                Material rock = GetOrCreateMaterial(RockMaterialPath, "M_TutorialPlayRock",
                    new Color(0.20f, 0.27f, 0.36f), 0.12f);

                Transform map = BuildMapRoot(root.transform, snow, edge, road, path);
                Transform gameplay = Child(map, "Gameplay");
                Transform environment = Child(map, "Environment");
                Transform serviceYard = Child(gameplay, "ProductionServiceYard");
                Transform houses = Child(environment, "RoadFacingHouses");

                penguin.transform.SetPositionAndRotation(PlayerSpawn, Quaternion.identity);
                ConfigureDirector(director, penguin.transform, machine, sender, questJournal, santaGuide);

                machine.transform.SetParent(serviceYard, true);
                machine.transform.SetPositionAndRotation(MachinePosition, Quaternion.Euler(0f, -90f, 0f));
                machine.ConfigureSnowDeliveryConversion(snowStage);
                EditorUtility.SetDirty(machine);

                sender.name = "TutorialPlayDeliveryTerminal";
                sender.transform.SetParent(serviceYard, true);
                sender.transform.SetPositionAndRotation(SenderPosition, Quaternion.Euler(0f, 270f, 0f));
                SetSerialized(director, "_giftDeliveryTerminal", sender);

                BuildServiceYard(serviceYard, path, fence, rock);
                BuildRoadNetwork(map, road, path);
                GiftDropZone neighborDeliveryZone = BuildHouses(scene, houses, housePrefabs, path);
                BuildNature(scene, environment, treePrefab, lanternPrefab, rock);

                GameObject warehouse = PrefabUtility.InstantiatePrefab(warehousePrefab, scene) as GameObject;
                if (warehouse == null) throw new InvalidOperationException("TutorialPlay 창고 생성 실패");
                warehouse.name = "TutorialPlayWarehouse";
                warehouse.transform.SetParent(gameplay, true);
                warehouse.transform.SetPositionAndRotation(WarehousePosition, Quaternion.identity);
                SnowballWarehouseStorage warehouseStorage = warehouse.GetComponent<SnowballWarehouseStorage>();

                receiver.name = "HiddenWarehouseDeliveryEndpoint";
                receiver.transform.SetParent(warehouse.transform, true);
                receiver.transform.SetPositionAndRotation(WarehousePosition + new Vector3(0f, 0.13f, 0.4f),
                    Quaternion.identity);
                HideEndpoint(receiver);
                SetSerialized(director, "_warehouseStorage", warehouseStorage);
                SetSerialized(director, "_warehouseDeliveryTerminal", receiver);
                SetSerialized(director, "_houseDeliveryZone", neighborDeliveryZone);

                BuildLocalLighting(map);
                ConfigureRenderSettings();
                EnsureBuildSettingsScene();
                ValidateGeneratedScene(root, machine, sender, warehouse);

                EditorSceneManager.MarkSceneDirty(scene);
                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                    throw new InvalidOperationException($"TutorialPlay 씬 저장 실패: {ScenePath}");

                CaptureOverview(scene, root.transform);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"TutorialPlay scene built: {ScenePath}");
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
                if (reopenTargetAfterBuild && !EditorApplication.isPlayingOrWillChangePlaymode)
                    EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }
        }

        private static Transform BuildMapRoot(Transform root, Material snow, Material edge,
            Material road, Material path)
        {
            Transform map = Child(root, "TutorialPlayMap");
            CreateCube(map, "Foundation", new Vector3(0f, -0.48f, 0f),
                new Vector3(94f, 0.72f, 70f), edge, true);
            CreateCube(map, "SnowGround", new Vector3(0f, -0.08f, 0f),
                new Vector3(92f, 0.16f, 68f), snow, true);

            Transform boundaries = Child(map, "Boundaries");
            CreateCube(boundaries, "WestSnowbank", new Vector3(-45.6f, 0.42f, 0f),
                new Vector3(0.8f, 1.0f, 68f), snow, true);
            CreateCube(boundaries, "EastSnowbank", new Vector3(45.6f, 0.42f, 0f),
                new Vector3(0.8f, 1.0f, 68f), snow, true);
            CreateCube(boundaries, "NorthSnowbank", new Vector3(0f, 0.42f, 33.6f),
                new Vector3(92f, 1.0f, 0.8f), snow, true);
            CreateCube(boundaries, "SouthSnowbank_Left", new Vector3(-25f, 0.42f, -33.6f),
                new Vector3(42f, 1.0f, 0.8f), snow, true);
            CreateCube(boundaries, "SouthSnowbank_Right", new Vector3(25f, 0.42f, -33.6f),
                new Vector3(42f, 1.0f, 0.8f), snow, true);

            Transform navigation = Child(map, "NavigationGround");
            CreateCylinder(navigation, "SnowGatheringYard", new Vector3(-35f, 0.035f, -5f),
                new Vector3(6.5f, 0.035f, 6.5f), path);
            return map;
        }

        private static void BuildRoadNetwork(Transform map, Material road, Material path)
        {
            Transform navigation = map.Find("NavigationGround");
            Transform nodes = Child(map, "LayoutNodes_SinglePlayGrammar");

            Vector3 entry = LayoutNode(nodes, "Node_Entry", new Vector3(0f, 0f, -34f));
            Vector3 south = LayoutNode(nodes, "Node_South", new Vector3(10f, 0f, -20f));
            Vector3 central = LayoutNode(nodes, "Node_Central", new Vector3(-2f, 0f, -5f));
            Vector3 northWest = LayoutNode(nodes, "Node_NorthWest", new Vector3(-25f, 0f, 11f));
            Vector3 warehouseFront = LayoutNode(nodes, "Node_WarehouseFront", new Vector3(-39f, 0f, 18f));
            Vector3 north01 = LayoutNode(nodes, "Node_North01", new Vector3(-12f, 0f, 17f));
            Vector3 north02 = LayoutNode(nodes, "Node_North02", new Vector3(1f, 0f, 20f));
            Vector3 north03 = LayoutNode(nodes, "Node_North03", new Vector3(14f, 0f, 20f));
            Vector3 northEast = LayoutNode(nodes, "Node_NorthEast", new Vector3(27f, 0f, 18f));
            Vector3 eastNorth = LayoutNode(nodes, "Node_EastNorth", new Vector3(38f, 0f, 20f));
            Vector3 east = LayoutNode(nodes, "Node_East", new Vector3(28f, 0f, 10f));
            Vector3 eastMiddle = LayoutNode(nodes, "Node_EastMiddle", new Vector3(28f, 0f, 0f));
            Vector3 eastSouth = LayoutNode(nodes, "Node_EastSouth", new Vector3(23f, 0f, -12f));
            Vector3 centralHouse = LayoutNode(nodes, "Node_CentralHouse", new Vector3(8f, 0f, 2f));
            Vector3 southWest = LayoutNode(nodes, "Node_SouthWest", new Vector3(-25f, 0f, -17f));
            Vector3 crossSouthWest = LayoutNode(nodes, "Node_CrossSouthWest", new Vector3(-40f, 0f, -24f));

            CreateVillageRoad(navigation, "Arrival_Entry_South", entry, south, 5.4f, road, path);
            CreateVillageRoad(navigation, "Diagonal_South_Central", south, central, 5.4f, road, path);
            CreateVillageRoad(navigation, "Diagonal_Central_NorthWest", central, northWest, 5.4f, road, path);
            CreateVillageRoad(navigation, "Service_NorthWest_Warehouse", northWest, warehouseFront, 4.8f, road, path);

            CreateVillageRoad(navigation, "Promenade_NorthWest_N01", northWest, north01, 4.8f, road, path);
            CreateVillageRoad(navigation, "Promenade_N01_N02", north01, north02, 4.8f, road, path);
            CreateVillageRoad(navigation, "Promenade_N02_N03", north02, north03, 4.8f, road, path);
            CreateVillageRoad(navigation, "Promenade_N03_NorthEast", north03, northEast, 4.8f, road, path);
            CreateVillageRoad(navigation, "Promenade_NorthEast_EastHouse", northEast, eastNorth, 4.8f, road, path);

            CreateVillageRoad(navigation, "EastAxis_South_EastSouth", south, eastSouth, 5.0f, road, path);
            CreateVillageRoad(navigation, "EastAxis_EastSouth_EastMiddle", eastSouth, eastMiddle, 5.0f, road, path);
            CreateVillageRoad(navigation, "EastAxis_EastMiddle_East", eastMiddle, east, 5.0f, road, path);
            CreateVillageRoad(navigation, "EastAxis_East_NorthEast", east, northEast, 5.0f, road, path);
            CreateVillageRoad(navigation, "Cross_Central_CentralHouse", central, centralHouse, 4.8f, road, path);
            CreateVillageRoad(navigation, "Cross_CentralHouse_East", centralHouse, east, 4.8f, road, path);
            CreateVillageRoad(navigation, "SouthWest_Cross_Home", crossSouthWest, southWest, 4.8f, road, path);
            CreateVillageRoad(navigation, "SouthWest_Home_Central", southWest, central, 4.8f, road, path);

            CreateRoad(navigation, "SnowYardApproach", new Vector3(-24f, 0.05f, -5f),
                new Vector3(-31f, 0.05f, -5f), 3.8f, path);
            CreateRoad(navigation, "DeliveryYardApproach", new Vector3(-19f, 0.05f, 4.5f),
                new Vector3(-18f, 0.05f, 7.5f), 3.8f, path);
            CreateRoad(navigation, "MachineYardApproach", new Vector3(-10.8f, 0.05f, 1.1f),
                new Vector3(-9f, 0.05f, 3.5f), 3.4f, path);

            CreateSmoothJunction(navigation, "RoadJunction_South", south, 5.4f, road, path,
                entry, central, eastSouth);
            CreateSmoothJunction(navigation, "RoadJunction_Central", central, 5.4f, road, path,
                south, northWest, centralHouse, southWest);
            CreateSmoothJunction(navigation, "RoadJunction_NorthWest", northWest, 5.2f, road, path,
                central, warehouseFront, north01);
            CreateSmoothJunction(navigation, "RoadJunction_North01", north01, 4.8f, road, path,
                northWest, north02);
            CreateSmoothJunction(navigation, "RoadJunction_North02", north02, 4.8f, road, path,
                north01, north03);
            CreateSmoothJunction(navigation, "RoadJunction_North03", north03, 4.8f, road, path,
                north02, northEast);
            CreateSmoothJunction(navigation, "RoadJunction_NorthEast", northEast, 5.2f, road, path,
                north03, eastNorth, east);
            CreateSmoothJunction(navigation, "RoadJunction_East", east, 5.0f, road, path,
                northEast, eastMiddle, centralHouse);
            CreateSmoothJunction(navigation, "RoadJunction_EastMiddle", eastMiddle, 5.0f, road, path,
                eastSouth, east);
            CreateSmoothJunction(navigation, "RoadJunction_EastSouth", eastSouth, 5.0f, road, path,
                south, eastMiddle);
            CreateSmoothJunction(navigation, "RoadJunction_CentralHouse", centralHouse, 4.8f, road, path,
                central, east);
            CreateSmoothJunction(navigation, "RoadJunction_SouthWest", southWest, 4.8f, road, path,
                crossSouthWest, central);
        }

        private static void BuildServiceYard(Transform yard, Material path, Material fence, Material rock)
        {
            CreateCube(yard, "MachineApron_Central", new Vector3(-6f, 0.04f, 5f),
                new Vector3(12f, 0.08f, 11f), path, false);
            CreateCube(yard, "DeliveryApron_West", new Vector3(-18f, 0.04f, 7.5f),
                new Vector3(9f, 0.08f, 10f), path, false);
            BuildFenceSegment(yard, "MachineYardFence_North", new Vector3(-6f, 0.55f, 10.5f),
                new Vector3(8f, 1.1f, 0.22f), fence);
            BuildFenceSegment(yard, "DeliveryYardFence_West", new Vector3(-22.5f, 0.55f, 9f),
                new Vector3(0.22f, 1.1f, 5f), fence);
            CreateRock(yard, "YardRock_01", new Vector3(-11.5f, 0.35f, 10.2f), rock, 1.25f);
            CreateRock(yard, "YardRock_02", new Vector3(-22.8f, 0.28f, 11.8f), rock, 0.9f);
        }

        private static GiftDropZone BuildHouses(Scene scene, Transform parent, GameObject[] prefabs, Material path)
        {
            GiftDropZone neighborDeliveryZone = PlaceHouse(
                scene, parent, prefabs[0], "House_NorthWest", new Vector3(-27f, 0f, 30f),
                new Vector3(-39f, 0f, 18f), path, true);
            PlaceHouse(scene, parent, prefabs[1], "House_NorthCenter", new Vector3(2f, 0f, 30f),
                new Vector3(1f, 0f, 20f), path, false);
            PlaceHouse(scene, parent, prefabs[2], "House_NorthEast", new Vector3(16f, 0f, 29f),
                new Vector3(14f, 0f, 20f), path, false);
            PlaceHouse(scene, parent, prefabs[3], "House_EastNorth", new Vector3(40f, 0f, 27f),
                new Vector3(38f, 0f, 20f), path, false);
            PlaceHouse(scene, parent, prefabs[0], "House_Central", new Vector3(13f, 0f, 10f),
                new Vector3(8f, 0f, 2f), path, false);
            PlaceHouse(scene, parent, prefabs[1], "House_EastMiddle", new Vector3(40f, 0f, 5f),
                new Vector3(28f, 0f, 0f), path, false);
            PlaceHouse(scene, parent, prefabs[2], "House_EastSouth", new Vector3(34f, 0f, -22f),
                new Vector3(23f, 0f, -12f), path, false);
            PlaceHouse(scene, parent, prefabs[3], "House_SouthWest", new Vector3(-35f, 0f, -27f),
                new Vector3(-25f, 0f, -17f), path, false);
            return neighborDeliveryZone;
        }

        private static GiftDropZone PlaceHouse(Scene scene, Transform parent, GameObject prefab, string name,
            Vector3 position, Vector3 roadPoint, Material path, bool isQuestDestination)
        {
            GameObject house = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (house == null) throw new InvalidOperationException($"집 생성 실패: {name}");
            house.name = name;
            house.transform.SetParent(parent, true);

            Vector3 towardRoad = roadPoint - position;
            towardRoad.y = 0f;
            float setback = towardRoad.magnitude;
            if (setback < 7f || setback > 17f)
                throw new InvalidOperationException($"{name}의 도로 이격 거리({setback:0.0}m)가 SinglePlay 기준을 벗어났다.");
            towardRoad.Normalize();
            // WindowSpillLight and the entrance of the WinterVillage houses are on local +Z.
            house.transform.SetPositionAndRotation(position, Quaternion.LookRotation(towardRoad, Vector3.up));
            if (Vector3.Dot(house.transform.forward, towardRoad) < 0.995f)
                throw new InvalidOperationException($"{name}의 정면이 도로를 향하지 않는다.");

            Vector3 frontYardAnchor = position + towardRoad * 3.2f;
            if (!isQuestDestination) return null;

            Vector3 right = Vector3.Cross(Vector3.up, towardRoad).normalized;
            Vector3 padPosition = frontYardAnchor + towardRoad * 0.65f + right * 1.45f + Vector3.up * 0.035f;
            GameObject pad = CreateCube(parent, name + "_QuestDeliveryPad",
                padPosition, new Vector3(3.2f, 0.07f, 3.2f), path, false);

            GiftDropZone zone = pad.AddComponent<GiftDropZone>();
            zone.Configure(new Vector3(3.4f, 2.2f, 3.4f), 1);
            return zone;
        }

        private static void BuildNature(Scene scene, Transform parent, GameObject treePrefab,
            GameObject lanternPrefab, Material rock)
        {
            Transform trees = Child(parent, "Trees");
            for (int i = 0; i < TreePositions.Length; i++)
                PlacePrefab(scene, trees, treePrefab, $"Tree_{i:00}", TreePositions[i],
                    Quaternion.Euler(0f, (i * 47f) % 360f, 0f));

            Transform lamps = Child(parent, "Lanterns");
            for (int i = 0; i < LanternPositions.Length; i++)
                PlacePrefab(scene, lamps, lanternPrefab, $"Lantern_{i:00}", LanternPositions[i], Quaternion.identity);

            Transform rocks = Child(parent, "Rocks");
            Vector3[] positions =
            {
                new(-46f, 0.2f, 25f), new(-32f, 0.2f, 31f), new(18f, 0.2f, 25f),
                new(42f, 0.2f, 26f), new(43f, 0.2f, -29f), new(-42f, 0.2f, -30f),
                new(23f, 0.2f, 18f), new(-25f, 0.2f, -28f)
            };
            for (int i = 0; i < positions.Length; i++)
                CreateRock(rocks, $"Rock_{i:00}", positions[i], rock, 0.8f + (i % 3) * 0.22f);
        }

        private static void ConfigureDirector(PenguinTutorialDirector director, Transform player,
            SnowGiftMachinePresentation machine, GiftDeliveryTerminal sender,
            TutorialQuestJournal questJournal, TutorialSantaGuide santaGuide)
        {
            SetSerialized(director, "_player", player);
            SetSerialized(director, "_snowGiftMachine", machine);
            SetSerialized(director, "_giftDeliveryTerminal", sender);
            SetSerialized(director, "_questJournal", questJournal);
            SetSerialized(director, "_santaGuide", santaGuide);
            SetSerialized(director, "_walkTarget", WalkTarget);
            SetSerialized(director, "_runTarget", RunTarget);
            SetSerialized(director, "_slideTarget", SlideTarget);
            SetSerialized(director, "_snowballSpawn", SnowballSpawn);
            SetSerialized(director, "_walkRoomCenter", WalkAreaCenter);
            SetSerialized(director, "_walkRoomSize", WalkAreaSize);
            SetSerialized(director, "_runRoomCenter", RunAreaCenter);
            SetSerialized(director, "_runRoomSize", RunAreaSize);
            SetSerialized(director, "_slideRoomCenter", SlideAreaCenter);
            SetSerialized(director, "_slideRoomSize", SlideAreaSize);
            SetSerialized(director, "_useVillageInstructions", true);
            SetSerialized(director, "_useQuestCycle", true);
            SetSerialized(director, "_useWorldMarker", true);
        }

        private static TutorialQuestJournal ReplaceTutorialHud(
            Transform parent,
            PenguinTutorialHud legacyHud,
            PanelSettings panelSettings,
            VisualTreeAsset questJournalUxml)
        {
            if (legacyHud != null) UnityEngine.Object.DestroyImmediate(legacyHud.gameObject);

            GameObject journalObject = new GameObject("TutorialQuestJournal");
            journalObject.transform.SetParent(parent);
            UIDocument document = journalObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = questJournalUxml;
            document.sortingOrder = 10;
            return journalObject.AddComponent<TutorialQuestJournal>();
        }

        private static TutorialSantaGuide BuildSantaGuide(
            Transform parent,
            PanelSettings panelSettings,
            VisualTreeAsset guideUxml,
            Texture2D portrait)
        {
            GameObject guideObject = new GameObject("TutorialSantaGuide");
            guideObject.transform.SetParent(parent);
            UIDocument document = guideObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = guideUxml;
            document.sortingOrder = 12;

            AudioSource voiceSource = guideObject.AddComponent<AudioSource>();
            voiceSource.playOnAwake = false;
            voiceSource.loop = false;
            voiceSource.spatialBlend = 0f;

            TutorialSantaGuide guide = guideObject.AddComponent<TutorialSantaGuide>();
            guide.Configure(portrait, voiceSource);
            EditorUtility.SetDirty(guide);
            return guide;
        }

        private static void RemoveStageGates(GameObject root)
        {
            TutorialStageGate[] gates = root.GetComponentsInChildren<TutorialStageGate>(true);
            for (int i = gates.Length - 1; i >= 0; i--)
                if (gates[i] != null) UnityEngine.Object.DestroyImmediate(gates[i].gameObject);
        }

        private static void RemoveOldCampus(Transform root)
        {
            string[] directChildren =
            {
                "TrainingCampus", "Foundation", "SnowGround", "OuterWall_West", "OuterWall_East",
                "OuterWall_North", "OuterWall_South_Left", "OuterWall_South_Right",
                "WarmFill_Walk", "WarmFill_Run", "WarmFill_Slide", "WarmFill_Snowball",
                "WarmFill_SnowMachine", "WarmFill_Gift", "TutorialPlayMap"
            };
            foreach (string childName in directChildren) DestroyChild(root, childName);
        }

        private static void HideEndpoint(GiftDeliveryTerminal endpoint)
        {
            foreach (Renderer renderer in endpoint.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
            foreach (Collider collider in endpoint.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
            foreach (AudioSource source in endpoint.GetComponentsInChildren<AudioSource>(true)) source.mute = true;
        }

        private static void ValidateGeneratedScene(GameObject root, SnowGiftMachinePresentation machine,
            GiftDeliveryTerminal sender, GameObject warehouse)
        {
            Transform map = root.transform.Find("TutorialPlayMap");
            Transform navigation = map == null ? null : map.Find("NavigationGround");
            Transform nodes = map == null ? null : map.Find("LayoutNodes_SinglePlayGrammar");
            Transform houses = map == null ? null : map.Find("Environment/RoadFacingHouses");
            Transform lanterns = map == null ? null : map.Find("Environment/Lanterns");
            if (map == null || navigation == null || nodes == null || houses == null || lanterns == null)
                throw new InvalidOperationException("TutorialPlay의 도로 우선 배치 루트가 완전하지 않다.");

            int houseCount = houses.GetComponentsInChildren<HouseRoofIdentity>(true).Length;
            if (houseCount != 8)
                throw new InvalidOperationException($"TutorialPlay 집 수가 8이 아니다: {houseCount}");
            if (nodes.childCount != 16)
                throw new InvalidOperationException($"TutorialPlay 레이아웃 노드 수가 16이 아니다: {nodes.childCount}");

            int surfaceCount = 0;
            int shoulderCount = 0;
            foreach (Transform child in navigation)
            {
                if (child.name.EndsWith("_Surface", StringComparison.Ordinal)) surfaceCount++;
                if (child.name.EndsWith("_Shoulder", StringComparison.Ordinal)) shoulderCount++;
            }
            if (surfaceCount < 25 || shoulderCount != surfaceCount)
                throw new InvalidOperationException(
                    $"TutorialPlay 도로 표면/어깨면 구성이 잘못됐다: surface={surfaceCount}, shoulder={shoulderCount}");

            TutorialStageGate[] gates = root.GetComponentsInChildren<TutorialStageGate>(true);
            GiftDeliveryTerminal[] terminals = root.GetComponentsInChildren<GiftDeliveryTerminal>(true);
            TutorialQuestJournal[] questJournals = root.GetComponentsInChildren<TutorialQuestJournal>(true);
            TutorialSantaGuide[] santaGuides = root.GetComponentsInChildren<TutorialSantaGuide>(true);
            PenguinTutorialHud[] legacyHuds = root.GetComponentsInChildren<PenguinTutorialHud>(true);
            GiftDropZone[] houseDeliveryZones = houses.GetComponentsInChildren<GiftDropZone>(true);
            int entryWalkwayCount = 0;
            foreach (Transform child in houses)
                if (child.name.EndsWith("_EntryWalkway", StringComparison.Ordinal)) entryWalkwayCount++;
            int extraGiftDropPads = 0;
            foreach (Transform child in houses)
                if (child.name.EndsWith("_GiftDropPad", StringComparison.Ordinal)) extraGiftDropPads++;
            if (gates.Length != 0 || terminals.Length != 2 || questJournals.Length != 1 || santaGuides.Length != 1 ||
                legacyHuds.Length != 0 || houseDeliveryZones.Length != 1 || entryWalkwayCount != 0)
                throw new InvalidOperationException(
                    $"TutorialPlay 퀘스트 구성이 잘못됐다: gates={gates.Length}, terminals={terminals.Length}, " +
                    $"questJournals={questJournals.Length}, santaGuides={santaGuides.Length}, " +
                    $"legacyHuds={legacyHuds.Length}, deliveryZones={houseDeliveryZones.Length}, " +
                    $"entryWalkways={entryWalkwayCount}, extraGiftDropPads={extraGiftDropPads}");
            if (extraGiftDropPads != 0)
                throw new InvalidOperationException($"일반 집 앞에 불필요한 파란 납품 패드가 남아 있다: {extraGiftDropPads}");
            if (warehouse.GetComponent<SnowballWarehouseStorage>() == null)
                throw new InvalidOperationException("TutorialPlay 창고 저장 컴포넌트가 없다.");

            float machineToSender = PlanarDistance(machine.transform.position, sender.transform.position);
            float senderToWarehouse = PlanarDistance(sender.transform.position, warehouse.transform.position);
            float machineToWarehouse = PlanarDistance(machine.transform.position, warehouse.transform.position);
            float machineFromCenter = PlanarDistance(machine.transform.position, Vector3.zero);
            bool warehouseAtMapEdge = Mathf.Abs(warehouse.transform.position.x) >= 34f ||
                Mathf.Abs(warehouse.transform.position.z) >= 29f;
            if (machineToSender < 10f || machineToSender > 16f ||
                senderToWarehouse < 22f || senderToWarehouse > 32f ||
                machineToWarehouse < 32f || machineToWarehouse > 45f ||
                machineFromCenter > 12f || !warehouseAtMapEdge)
                throw new InvalidOperationException(
                    $"TutorialPlay 물류 설비 거리가 기준을 벗어났다: machine-sender={machineToSender:0.0}, " +
                    $"sender-warehouse={senderToWarehouse:0.0}, machine-warehouse={machineToWarehouse:0.0}, " +
                    $"machine-center={machineFromCenter:0.0}, warehouse-at-edge={warehouseAtMapEdge}");

            ValidateLanternClearance(navigation, houses, lanterns);

            int missingScripts = 0;
            foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                missingScripts += GameObjectUtility.GetMonoBehavioursWithMissingScriptCount(child.gameObject);
            if (missingScripts > 0)
                throw new InvalidOperationException($"TutorialPlay에 Missing Script가 있다: {missingScripts}");

            Debug.Log(
                $"TutorialPlay layout validation passed: houses={houseCount}, nodes={nodes.childCount}, " +
                $"roadSurfaces={surfaceCount}, questSteps=8, gates={gates.Length}, entryWalkways={entryWalkwayCount}, " +
                $"machine-sender={machineToSender:0.0}m, sender-warehouse={senderToWarehouse:0.0}m.");
        }

        private static void ValidateLanternClearance(Transform navigation, Transform houses,
            Transform lanterns)
        {
            const float minimumClearance = 0.9f;
            var straightPaths = new List<Transform>();
            var junctionSurfaces = new List<Transform>();

            foreach (Transform child in navigation)
            {
                bool isJunctionSurface = child.name.StartsWith("RoadJunction_", StringComparison.Ordinal) &&
                    child.name.EndsWith("_Surface", StringComparison.Ordinal);
                if (isJunctionSurface)
                {
                    junctionSurfaces.Add(child);
                    continue;
                }

                if (child.name.EndsWith("_Surface", StringComparison.Ordinal) ||
                    child.name.EndsWith("Approach", StringComparison.Ordinal))
                    straightPaths.Add(child);
            }

            foreach (Transform child in houses)
            {
                if (child.name.EndsWith("_EntryWalkway", StringComparison.Ordinal))
                    straightPaths.Add(child);
            }

            foreach (Transform lantern in lanterns)
            {
                foreach (Transform path in straightPaths)
                {
                    float clearance = DistanceToOrientedRectangle(lantern.position, path);
                    if (clearance < minimumClearance)
                        throw new InvalidOperationException(
                            $"{lantern.name}이 {path.name} 가장자리에서 {clearance:0.00}m 떨어져 있다. " +
                            $"램프는 길 밖으로 최소 {minimumClearance:0.0}m 이격해야 한다.");
                }

                foreach (Transform junction in junctionSurfaces)
                {
                    float clearance = DistanceToJunctionOutline(lantern.position, junction);
                    if (clearance < minimumClearance)
                        throw new InvalidOperationException(
                            $"{lantern.name}이 {junction.name} 가장자리에서 {clearance:0.00}m 떨어져 있다. " +
                            $"램프는 교차로 밖으로 최소 {minimumClearance:0.0}m 이격해야 한다.");
                }
            }

            Debug.Log(
                $"TutorialPlay lantern clearance validation passed: lanterns={lanterns.childCount}, " +
                $"straightPaths={straightPaths.Count}, junctions={junctionSurfaces.Count}, " +
                $"minimumClearance={minimumClearance:0.0}m.");
        }

        private static float DistanceToOrientedRectangle(Vector3 point, Transform rectangle)
        {
            Vector3 delta = point - rectangle.position;
            delta.y = 0f;
            Vector3 right = rectangle.right;
            right.y = 0f;
            right.Normalize();
            Vector3 forward = rectangle.forward;
            forward.y = 0f;
            forward.Normalize();

            float halfWidth = Mathf.Abs(rectangle.lossyScale.x) * 0.5f;
            float halfLength = Mathf.Abs(rectangle.lossyScale.z) * 0.5f;
            float outsideRight = Mathf.Max(Mathf.Abs(Vector3.Dot(delta, right)) - halfWidth, 0f);
            float outsideForward = Mathf.Max(Mathf.Abs(Vector3.Dot(delta, forward)) - halfLength, 0f);
            return Mathf.Sqrt(outsideRight * outsideRight + outsideForward * outsideForward);
        }

        private static float DistanceToJunctionOutline(Vector3 point, Transform junction)
        {
            MeshFilter filter = junction.GetComponent<MeshFilter>();
            Mesh mesh = filter == null ? null : filter.sharedMesh;
            if (mesh == null || mesh.vertexCount < 4)
                throw new InvalidOperationException($"{junction.name}의 교차로 메시가 유효하지 않다.");

            Vector3[] vertices = mesh.vertices;
            var outline = new List<Vector2>(vertices.Length - 1);
            for (int i = 1; i < vertices.Length; i++)
            {
                Vector3 world = junction.TransformPoint(vertices[i]);
                outline.Add(new Vector2(world.x, world.z));
            }

            Vector2 point2 = new Vector2(point.x, point.z);
            if (IsPointInsidePolygon(point2, outline)) return 0f;

            float minimum = float.PositiveInfinity;
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 start = outline[i];
                Vector2 end = outline[(i + 1) % outline.Count];
                minimum = Mathf.Min(minimum, DistanceToSegment(point2, start, end));
            }
            return minimum;
        }

        private static bool IsPointInsidePolygon(Vector2 point, IReadOnlyList<Vector2> polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Count - 1; i < polygon.Count; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                bool crosses = (a.y > point.y) != (b.y > point.y) &&
                    point.x < (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (crosses) inside = !inside;
            }
            return inside;
        }

        private static float DistanceToSegment(Vector2 point, Vector2 start, Vector2 end)
        {
            Vector2 segment = end - start;
            float lengthSquared = segment.sqrMagnitude;
            if (lengthSquared <= Mathf.Epsilon) return Vector2.Distance(point, start);
            float t = Mathf.Clamp01(Vector2.Dot(point - start, segment) / lengthSquared);
            return Vector2.Distance(point, start + segment * t);
        }

        private static float PlanarDistance(Vector3 a, Vector3 b)
        {
            return Vector2.Distance(new Vector2(a.x, a.z), new Vector2(b.x, b.z));
        }

        private static void BuildLocalLighting(Transform parent)
        {
            CreatePointLight(parent, "MachineYardLight", new Vector3(-6f, 7f, 5f), 18f, 13f);
            CreatePointLight(parent, "DeliveryYardLight", new Vector3(-18f, 7f, 7.5f), 16f, 11f);
            CreatePointLight(parent, "WarehouseLight", new Vector3(-39f, 7f, 24f), 18f, 12f);
            CreatePointLight(parent, "EastHomesLight", new Vector3(31f, 7f, 2f), 14f, 16f);
            CreatePointLight(parent, "SouthHomesLight", new Vector3(-27f, 7f, -20f), 14f, 15f);
        }

        private static void ConfigureRenderSettings()
        {
            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.25f, 0.31f, 0.43f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.16f, 0.23f, 0.36f);
            RenderSettings.fogDensity = 0.0045f;
        }

        private static void CreatePointLight(Transform parent, string name, Vector3 position,
            float intensity, float range)
        {
            GameObject lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.63f, 0.28f);
            light.intensity = intensity;
            light.range = range;
            light.shadows = LightShadows.None;
        }

        private static void CaptureOverview(Scene scene, Transform root)
        {
            const string previewPath = "Assets/Game/InGame/Tutorial/Docs/TutorialPlay_Overview.png";
            GameObject cameraObject = new GameObject("TutorialPlayPreviewCamera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.overrideSceneCullingMask = EditorSceneManager.GetSceneCullingMask(scene);
            camera.transform.position = new Vector3(56f, 65f, -63f);
            camera.transform.LookAt(new Vector3(0f, 0f, 0f));
            camera.fieldOfView = 44f;
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.025f, 0.055f, 0.12f);
            camera.nearClipPlane = 0.2f;
            camera.farClipPlane = 300f;

            RenderTexture renderTexture = new RenderTexture(1600, 900, 24, RenderTextureFormat.ARGB32);
            camera.targetTexture = renderTexture;
            RenderTexture previous = RenderTexture.active;
            RenderTexture.active = renderTexture;
            camera.Render();

            Texture2D texture = new Texture2D(1600, 900, TextureFormat.RGB24, false);
            texture.ReadPixels(new Rect(0f, 0f, 1600f, 900f), 0, 0);
            texture.Apply(false, false);
            File.WriteAllBytes(Path.GetFullPath(previewPath), texture.EncodeToPNG());

            RenderTexture.active = previous;
            camera.targetTexture = null;
            UnityEngine.Object.DestroyImmediate(texture);
            renderTexture.Release();
            UnityEngine.Object.DestroyImmediate(renderTexture);
            UnityEngine.Object.DestroyImmediate(cameraObject);
            AssetDatabase.ImportAsset(previewPath, ImportAssetOptions.ForceSynchronousImport);
        }

        private static Vector3 LayoutNode(Transform parent, string name, Vector3 position)
        {
            GameObject node = new GameObject(name);
            node.transform.SetParent(parent);
            node.transform.position = position;
            return position;
        }

        private static void CreateVillageRoad(Transform parent, string name, Vector3 start, Vector3 end,
            float width, Material road, Material shoulder)
        {
            Vector3 shoulderStart = new Vector3(start.x, 0.018f, start.z);
            Vector3 shoulderEnd = new Vector3(end.x, 0.018f, end.z);
            Vector3 roadStart = new Vector3(start.x, 0.055f, start.z);
            Vector3 roadEnd = new Vector3(end.x, 0.055f, end.z);
            CreateRoad(parent, name + "_Shoulder", shoulderStart, shoulderEnd, width + 1.35f, shoulder);
            CreateRoad(parent, name + "_Surface", roadStart, roadEnd, width, road);
        }

        private static void CreateSmoothJunction(Transform parent, string name, Vector3 center,
            float width, Material road, Material shoulder, params Vector3[] connectedPoints)
        {
            CreateSmoothJunctionMesh(parent, name + "_Shoulder", center, width + 1.35f,
                0.054f, shoulder, connectedPoints);
            CreateSmoothJunctionMesh(parent, name + "_Surface", center, width,
                0.092f, road, connectedPoints);
        }

        private static void CreateSmoothJunctionMesh(Transform parent, string name, Vector3 center,
            float width, float height, Material material, IReadOnlyList<Vector3> connectedPoints)
        {
            if (connectedPoints == null || connectedPoints.Count < 2)
                throw new InvalidOperationException($"{name}에 연결된 도로가 부족하다.");

            float halfWidth = width * 0.52f;
            float blendLength = width * 0.58f;
            var supportPoints = new List<Vector2>(connectedPoints.Count * 2);
            Vector2 center2 = new Vector2(center.x, center.z);
            for (int i = 0; i < connectedPoints.Count; i++)
            {
                Vector2 direction = new Vector2(
                    connectedPoints[i].x - center.x,
                    connectedPoints[i].z - center.z).normalized;
                Vector2 right = new Vector2(direction.y, -direction.x);
                Vector2 along = center2 + direction * blendLength;
                supportPoints.Add(along + right * halfWidth);
                supportPoints.Add(along - right * halfWidth);
            }

            List<Vector2> outline = BuildConvexHull(supportPoints);
            outline = SmoothClosedOutline(outline, 2, 1.02f);
            if (outline.Count < 3)
                throw new InvalidOperationException($"{name} 교차로 외곽선을 만들 수 없다.");

            EnsureAssetFolder(JunctionMeshFolderPath);
            string meshPath = $"{JunctionMeshFolderPath}/{name}.asset";
            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(meshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = name };
                AssetDatabase.CreateAsset(mesh, meshPath);
            }
            else
            {
                mesh.Clear();
                mesh.name = name;
            }

            var vertices = new List<Vector3>(outline.Count + 1) { new Vector3(center.x, height, center.z) };
            var normals = new List<Vector3>(outline.Count + 1) { Vector3.up };
            var uvs = new List<Vector2>(outline.Count + 1) { new Vector2(0.5f, 0.5f) };
            for (int i = 0; i < outline.Count; i++)
            {
                Vector2 point = outline[i];
                vertices.Add(new Vector3(point.x, height, point.y));
                normals.Add(Vector3.up);
                uvs.Add(new Vector2(
                    0.5f + (point.x - center.x) / (width * 4f),
                    0.5f + (point.y - center.z) / (width * 4f)));
            }

            var triangles = new List<int>(outline.Count * 3);
            for (int i = 0; i < outline.Count; i++)
            {
                int next = (i + 1) % outline.Count;
                triangles.Add(0);
                triangles.Add(next + 1);
                triangles.Add(i + 1);
            }

            mesh.SetVertices(vertices);
            mesh.SetNormals(normals);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);

            GameObject junction = new GameObject(name, typeof(MeshFilter), typeof(MeshRenderer));
            junction.transform.SetParent(parent);
            junction.GetComponent<MeshFilter>().sharedMesh = mesh;
            junction.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static List<Vector2> BuildConvexHull(List<Vector2> points)
        {
            points.Sort((a, b) =>
            {
                int x = a.x.CompareTo(b.x);
                return x != 0 ? x : a.y.CompareTo(b.y);
            });

            var hull = new List<Vector2>(points.Count * 2);
            for (int i = 0; i < points.Count; i++)
            {
                while (hull.Count >= 2 && Cross(hull[^1] - hull[^2], points[i] - hull[^1]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(points[i]);
            }

            int lowerCount = hull.Count;
            for (int i = points.Count - 2; i >= 0; i--)
            {
                while (hull.Count > lowerCount && Cross(hull[^1] - hull[^2], points[i] - hull[^1]) <= 0f)
                    hull.RemoveAt(hull.Count - 1);
                hull.Add(points[i]);
            }
            if (hull.Count > 1) hull.RemoveAt(hull.Count - 1);
            return hull;
        }

        private static List<Vector2> SmoothClosedOutline(List<Vector2> points, int iterations, float inflate)
        {
            List<Vector2> result = points;
            for (int iteration = 0; iteration < iterations; iteration++)
            {
                var next = new List<Vector2>(result.Count * 2);
                for (int i = 0; i < result.Count; i++)
                {
                    Vector2 a = result[i];
                    Vector2 b = result[(i + 1) % result.Count];
                    next.Add(Vector2.Lerp(a, b, 0.25f));
                    next.Add(Vector2.Lerp(a, b, 0.75f));
                }
                result = next;
            }

            Vector2 centroid = Vector2.zero;
            for (int i = 0; i < result.Count; i++) centroid += result[i];
            centroid /= result.Count;
            for (int i = 0; i < result.Count; i++)
                result[i] = centroid + (result[i] - centroid) * inflate;
            return result;
        }

        private static float Cross(Vector2 a, Vector2 b)
        {
            return a.x * b.y - a.y * b.x;
        }

        private static void EnsureAssetFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }

        private static void CreateRoad(Transform parent, string name, Vector3 start, Vector3 end,
            float width, Material material)
        {
            Vector3 direction = end - start;
            direction.y = 0f;
            float length = direction.magnitude;
            if (length < 0.01f) return;
            GameObject road = CreateCube(parent, name, (start + end) * 0.5f,
                new Vector3(width, 0.07f, length), material, false);
            road.transform.rotation = Quaternion.LookRotation(direction.normalized, Vector3.up);
        }

        private static void BuildFenceSegment(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material, Quaternion? rotation = null)
        {
            GameObject fence = CreateCube(parent, name, position, scale, material, true);
            if (rotation.HasValue) fence.transform.rotation = rotation.Value;
        }

        private static void CreateRock(Transform parent, string name, Vector3 position,
            Material material, float size)
        {
            GameObject rock = GameObject.CreatePrimitive(PrimitiveType.Sphere);
            rock.name = name;
            rock.transform.SetParent(parent);
            rock.transform.position = position;
            rock.transform.localScale = new Vector3(size * 1.3f, size * 0.65f, size);
            rock.transform.rotation = Quaternion.Euler(0f, size * 91f, 0f);
            rock.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static GameObject CreateCube(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material, bool keepCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            if (!keepCollider) UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            return cube;
        }

        private static GameObject CreateCylinder(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject cylinder = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            cylinder.name = name;
            cylinder.transform.SetParent(parent);
            cylinder.transform.position = position;
            cylinder.transform.localScale = scale;
            cylinder.GetComponent<MeshRenderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(cylinder.GetComponent<Collider>());
            return cylinder;
        }

        private static void PlacePrefab(Scene scene, Transform parent, GameObject prefab, string name,
            Vector3 position, Quaternion rotation)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
            if (instance == null) throw new InvalidOperationException($"프리팹 생성 실패: {name}");
            instance.name = name;
            instance.transform.SetParent(parent, true);
            instance.transform.SetPositionAndRotation(position, rotation);
        }

        private static Transform Child(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent);
            return child.transform;
        }

        private static Material RequireMaterial(string path)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null) throw new InvalidOperationException($"머티리얼이 없다: {path}");
            return material;
        }

        private static Material GetOrCreateMaterial(string path, string name, Color color, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾을 수 없다.");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void SetSerialized(UnityEngine.Object target, string propertyName, object value)
        {
            SerializedObject serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null)
                throw new InvalidOperationException($"{target.GetType().Name}.{propertyName} 필드를 찾을 수 없다.");

            switch (value)
            {
                case UnityEngine.Object objectReference:
                    property.objectReferenceValue = objectReference;
                    break;
                case Vector2 vector2:
                    property.vector2Value = vector2;
                    break;
                case Vector3 vector3:
                    property.vector3Value = vector3;
                    break;
                case int integer:
                    property.intValue = integer;
                    break;
                case bool boolean:
                    property.boolValue = boolean;
                    break;
                default:
                    throw new ArgumentException($"지원하지 않는 직렬화 값: {value?.GetType().Name}");
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static T FindNamedComponent<T>(GameObject root, string name) where T : Component
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (component.gameObject.name == name) return component;
            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        private static void EnsureBuildSettingsScene()
        {
            EditorBuildSettingsScene[] scenes = EditorBuildSettings.scenes;
            for (int i = 0; i < scenes.Length; i++)
            {
                if (scenes[i].path != ScenePath) continue;
                scenes[i] = new EditorBuildSettingsScene(ScenePath, true);
                EditorBuildSettings.scenes = scenes;
                return;
            }

            Array.Resize(ref scenes, scenes.Length + 1);
            scenes[^1] = new EditorBuildSettingsScene(ScenePath, true);
            EditorBuildSettings.scenes = scenes;
        }
    }
}
