using System;
using System.Collections.Generic;
using System.Reflection;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>의뢰 시스템(GameManager + RequestDirector)을 WinterVillage 맵 사본 위에 얹어
    /// 프로덕션 SinglePlay 씬을 생성한다. 도로망·집·판정 구역은
    /// <see cref="DeliverySceneRigBuilder"/>를 재사용한다.
    ///
    /// <para>플레이어는 <c>PF_Penguin_MomentumHandling</c>이다 — 펭귄 프리팹을 넘기면 <see cref="DeliverySceneRigBuilder"/>가
    /// 맵의 차량·차량 카메라·미니맵을 지우고 펭귄+눈덩이 스택을 얹는다. 중앙에 가장 가까운 집을
    /// <b>기지</b>로 잡고 나머지를 배달 대상 집으로 준다. 기존 싱글플레이의 인트로·HUD·결과 UI를
    /// 새 의뢰 시스템에 연결하며 트럭·보행자는 넣지 않는다.</para></summary>
    public static class SnowDeliverySceneBuilder
    {
        private const string ScenePath = "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";
        private const string SnowSourceScenePath =
            "Assets/Game/InGame/Snow/Tests/Snow_BallPush_Test.unity";
        private const string CatalogPath = "Assets/Game/InGame/Delivery/Data/GiftBoxCatalog.asset";
        private const string ConfigPath = "Assets/Game/InGame/Cleanliness/Data/Balance_Test.asset";
        private const string TimeOfDayConfigPath = "Assets/Game/InGame/Map/TimeOfDay/Data/TimeOfDay_Default.asset";
        private const string StarfieldTexturePath =
            "Assets/Game/InGame/Map/TimeOfDay/Textures/T_TimeOfDay_Starfield.png";
        private const string AuroraTexturePath =
            "Assets/Game/InGame/Map/TimeOfDay/Textures/T_TimeOfDay_Aurora.png";
        // 맵이 씬에 꽂아둔 것은 구워진 큐브맵(M_Everest_BlueHourSky)이다. 그건 고정된 사진이라
        // 하루 주기가 원리적으로 불가능하고, 해·달 원반도 그릴 수 없다. 절차적 하늘을 따로 쓴다.
        // 산맥은 큐브맵이 아니라 실제 메시(MSH_Everest_*)라 그대로 남는다.
        private const string ProceduralSkyPath =
            "Assets/Game/InGame/Map/WinterVillage/Lighting/Materials/M_WinterVillage_BlueHourSky.mat";
        private const string PenguinPrefabPath =
            "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/PF_Penguin_MomentumHandling.prefab";
        private const string SourceGroundMapPath =
            "Assets/Game/InGame/Map/WinterVillage/Generated/Snow/SnowGroundMap_WinterVillage.asset";
        private const string GroundMapPath =
            "Assets/Game/InGame/Cleanliness/Data/SnowGroundMap_SinglePlay.asset";
        private const string SnowGiftMachinePrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/PF_SnowGiftMachine.prefab";
        private const string ThiefRaidRigPrefabPath =
            "Assets/Game/InGame/Interaction/Thief/Prefabs/PF_ThiefRaidRig.prefab";
        private const string ThiefNavMeshDataPath =
            "Assets/Game/InGame/Interaction/Thief/NavMesh/NavMesh-ThiefSinglePlay.asset";
        private const string RigRootName = "SnowDeliveryRig";
        private const string StageIntroUxmlPath = "Assets/Game/InGame/UI/StageIntro/StageIntro.uxml";
        private const string StageIntroPanelSettingsPath =
            "Assets/Game/InGame/UI/StageIntro/StageIntroPanelSettings.asset";
        private const string StageHudUxmlPath = "Assets/Game/InGame/UI/StageHUD/StageHUD.uxml";
        private const string StageHudPanelSettingsPath =
            "Assets/Game/InGame/UI/StageHUD/StageHUDPanelSettings.asset";
        private const string StageOutroUxmlPath = "Assets/Game/InGame/UI/StageOutro/StageOutro.uxml";
        private const string StageOutroPanelSettingsPath =
            "Assets/Game/InGame/UI/StageOutro/StageOutroPanelSettings.asset";
        private const string AugmentPoolPath = "Assets/Game/InGame/Augment/Data/AugmentPool_Default.asset";
        private const string AugmentSelectUxmlPath =
            "Assets/Game/InGame/UI/AugmentSelect/AugmentSelect.uxml";
        private const string AugmentSelectPanelSettingsPath =
            "Assets/Game/InGame/UI/AugmentSelect/AugmentSelectPanelSettings.asset";
        private const string WorldMessageUxmlPath = "Assets/Game/InGame/UI/WorldMessage/WorldMessage.uxml";
        private const string WorldMessagePanelSettingsPath =
            "Assets/Game/InGame/UI/WorldMessage/WorldMessagePanelSettings.asset";
        private const string PauseMenuUxmlPath = "Assets/Game/InGame/UI/PauseMenu/PauseMenu.uxml";
        private const string PauseMenuPanelSettingsPath =
            "Assets/Game/InGame/UI/PauseMenu/PauseMenuPanelSettings.asset";
        private const string SnowballGrowthHudUxmlPath =
            "Assets/Game/InGame/Snow/NewSnowballSystem/UI/SnowballGrowthArcHud.uxml";
        private const string SnowballGrowthHudPanelSettingsPath =
            "Assets/Game/InGame/Snow/NewSnowballSystem/UI/SnowballGrowthPanelSettings.asset";
        private const string BlizzardWindAudioPath =
            "Assets/Game/InGame/evenT/weather/Audio/SFX_BlizzardSoftWind.wav";

        private const float FlatRoadSurfaceY = 0.01f;
        private static readonly Vector3 PlayerStart = new Vector3(-8f, FlatRoadSurfaceY, -9f);
        private static readonly Vector3 PlayerStartEuler = new Vector3(0f, 123f, 0f);
        private static readonly Vector3 SnowGiftMachinePosition = new Vector3(-34.63f, -0.2f, -15.51f);
        private static readonly Vector3 SnowGiftMachineEuler = new Vector3(0f, -29.805f, 0f);
        private static readonly Vector3 SnowGiftLandingLocalXZ = new Vector3(0f, 0f, -8f);
        private const float SnowGiftMachineOpeningRadius = 1.35f;
        private const float SnowGiftMachineFrontDepth = 1.5f;
        private const float SnowGiftMachineRearTolerance = 0.15f;
        private const float SnowGiftMachineSurfaceTolerance = 0.2f;

        [MenuItem("PPack/Cleanliness/Build SinglePlay Scene")]
        public static void Build()
        {
            EnsureFolder("Assets/Game/InGame/Cleanliness/Scenes");
            Scene scene = DeliverySceneRigBuilder.CopyMapSceneAndOpen(
                ScenePath, preserveDestinationGuid: true);
            int loweredRoadCount = LowerMapRoutesToFlatGround(scene);

            GameObject penguinPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(PenguinPrefabPath);
            if (penguinPrefab == null) throw new InvalidOperationException($"펭귄 프리팹이 없다: {PenguinPrefabPath}");

            var rigRoot = new GameObject(RigRootName);
            var tuning = new DeliverySceneRigBuilder.DirectorTuning(
                requestIntervalSeconds: 10f, snowCancelSeconds: 45f, pointsPerMeter: 10f, maxConcurrentTrucks: 0);
            DeliverySceneRigBuilder.RigResult rig = DeliverySceneRigBuilder.BuildRig(
                rigRoot.transform, null, tuning, PlayerStart, PlayerStartEuler,
                playerPrefab: penguinPrefab, includeLegacyTruckDelivery: false);

            RemoveRandomGiftSpawner(scene);
            BuildCentralTreePlazaAccess(scene);
            SnowGroundMap groundMap = BakeSnowDeliveryGroundMap();
            SnowCpuStage snowStage = BuildSnowSystem(scene, groundMap);
            Vector3 giftLandingPosition = BuildSnowGiftMachine(scene, rigRoot.transform, snowStage);

            PenguinInputReader playerInput = UnityEngine.Object.FindAnyObjectByType<PenguinInputReader>();
            if (playerInput == null) throw new InvalidOperationException("씬에 PenguinInputReader가 없다");
            PenguinLocomotion locomotion = playerInput.GetComponent<PenguinLocomotion>();
            if (locomotion != null) SetSerialized(locomotion, "_snowCpuStage", snowStage);
            BuildSnowballGrowthHud(rigRoot.transform, playerInput);

            // 옛 선물 배달 오케스트레이션은 끈다 — 이 씬은 새 RequestDirector가 유일한 의뢰 주체다.
            if (rig.GiftDirector != null)
            {
                rig.GiftDirector.enabled = false;
                EditorUtility.SetDirty(rig.GiftDirector);
            }

            GiftBoxCatalog catalog = LoadOrCreateCatalog();
            StageBalanceConfig config = LoadOrCreateConfig();
            if (config == null || catalog == null)
                throw new InvalidOperationException($"SO 로드 실패: config={config}, catalog={catalog}");

            if (rig.Houses.Count < 2)
                throw new InvalidOperationException($"집이 2채 미만이라 기지+배달집을 못 나눈다: {rig.Houses.Count}");

            int baseIndex = PickCentralHouse(rig.Houses);
            DeliveryHouse baseHouse = rig.Houses[baseIndex];
            var deliveryHouses = new List<DeliveryHouse>(rig.Houses.Count - 1);
            for (int index = 0; index < rig.Houses.Count; index++)
                if (index != baseIndex) deliveryHouses.Add(rig.Houses[index]);

            var directorObject = new GameObject("RequestDirector");
            directorObject.transform.SetParent(rigRoot.transform);
            RequestDirector director = directorObject.AddComponent<RequestDirector>();
            SetSerialized(director, "_network", rig.Network);
            SetSerialized(director, "_base", baseHouse.transform);
            SetSerialized(director, "_baseNode", baseHouse.RoadNode);
            SetSerializedArray(director, "_houses", deliveryHouses);
            SetSerialized(director, "_config", config);
            SetSerialized(director, "_catalog", catalog);
            BuildThiefRaidRig(scene, rigRoot.transform, director, giftLandingPosition);

            var managerObject = new GameObject("GameManager");
            managerObject.transform.SetParent(rigRoot.transform);
            GameManager manager = managerObject.AddComponent<GameManager>();
            SetSerialized(manager, "_config", config);
            SetSerialized(manager, "_requests", director);

            // 디버그 OnGUI 오버레이는 넣지 않는다. 시작 책임은 RequestStageFlowPresenter가 이미 지고
            // 있고(인트로 -> BeginPlaying), 상태는 실제 StageHUD에 나온다. 필요하면 씬에
            // RequestGameDebugHud를 손으로 붙이면 된다.

            TimeOfDayDirector timeOfDay = BuildTimeOfDay(rigRoot.transform);

            StageIntroController intro = BuildStageIntroUI(rigRoot.transform);
            BlizzardEvent blizzard = BuildBlizzardSystem(
                rigRoot.transform, snowStage, out ScheduledBlizzardDirector blizzardScheduler);
            BuildStageDateCoordinator(rigRoot.transform, manager, timeOfDay, blizzardScheduler);
            BuildStageHudUI(rigRoot.transform, director, manager, catalog, playerInput.transform, blizzard);

            // 집 위 표시는 싱글플레이와 같은 묶음을 그대로 쓴다. 다른 것은 신호를 채우는 Presenter뿐이다.
            StageHouseSignals houseSignals = DeliverySceneRigBuilder.BuildHouseSignalDisplays(rigRoot.transform);
            houseSignals.gameObject.AddComponent<RequestHouseSignalPresenter>()
                .Configure(director, houseSignals, manager);

            StageOutroController outro = BuildStageOutroUI(rigRoot.transform);
            RequestStageFlowPresenter flow = BuildStageFlowPresenter(
                rigRoot.transform, manager, director, intro, outro, playerInput, snowStage);
            BindOutroAction(outro, "_retryRequested", flow.OnRetryRequested);
            BindOutroAction(outro, "_continueRequested", flow.OnContinueRequested);
            // 증강을 먼저 세운다 — 일시정지 메뉴가 "증강이 떠 있으면 열지 않는다" 를 위해
            // 그 디렉터를 물어야 한다(2026-09-01, 정지 소유권 정리).
            AugmentSelectionDirector augments =
                BuildAugmentRig(rigRoot.transform, timeOfDay, playerInput, manager, director);
            BuildPauseMenuUI(rigRoot.transform, playerInput, flow, augments);
            BuildWorldMessageUI(rigRoot.transform);

            // 진단: 배선이 실제로 남았는지 읽어 확인한다.
            bool configWired = new SerializedObject(director).FindProperty("_config").objectReferenceValue != null;
            bool catalogWired = new SerializedObject(director).FindProperty("_catalog").objectReferenceValue != null;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"씬 저장 실패: {ScenePath}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[SnowDelivery] 씬 생성 완료: {ScenePath} · 펭귄+눈 · 기지=House{baseIndex}({baseHouse.name}) · " +
                      $"배달집 {deliveryHouses.Count}채 · 평탄화 도로 {loweredRoadCount}개 · " +
                      $"configWired={configWired} catalogWired={catalogWired}. " +
                      "Play를 누르면 WASD로 펭귄 이동, 좌상단 오버레이에 전역 시간·점수·의뢰가 뜬다.");
        }

        private static int LowerMapRoutesToFlatGround(Scene scene)
        {
            GameObject map = FindSceneObject(scene, "WinterVillageMap");
            Transform routes = map != null ? map.transform.Find("Geometry/Routes") : null;
            if (routes == null) throw new InvalidOperationException("WinterVillageMap/Geometry/Routes를 찾을 수 없다");

            var roadRenderers = new List<Renderer>();
            foreach (Renderer renderer in routes.GetComponentsInChildren<Renderer>(true))
            {
                if (renderer.name.StartsWith("Road_", StringComparison.Ordinal))
                    roadRenderers.Add(renderer);
            }

            if (roadRenderers.Count == 0)
                throw new InvalidOperationException("Routes 아래에 평탄화할 Road_* Renderer가 없다");

            float sourceSurfaceY = roadRenderers[0].bounds.max.y;
            foreach (Renderer renderer in roadRenderers)
            {
                if (Mathf.Abs(renderer.bounds.max.y - sourceSurfaceY) > 0.001f)
                    throw new InvalidOperationException(
                        $"도로 상판 높이가 서로 다르다: {roadRenderers[0].name}={sourceSurfaceY:0.###}, " +
                        $"{renderer.name}={renderer.bounds.max.y:0.###}");
            }

            Vector3 position = routes.position;
            position.y += FlatRoadSurfaceY - sourceSurfaceY;
            routes.position = position;

            Transform curbRamps = routes.Find("VehicleCurbRamps");
            if (curbRamps == null)
                throw new InvalidOperationException("Routes 아래에 VehicleCurbRamps가 없다");
            foreach (BoxCollider rampCollider in curbRamps.GetComponentsInChildren<BoxCollider>(true))
                rampCollider.enabled = false;
            curbRamps.gameObject.SetActive(false);
            Physics.SyncTransforms();

            foreach (Renderer renderer in roadRenderers)
            {
                if (Mathf.Abs(renderer.bounds.max.y - FlatRoadSurfaceY) > 0.002f)
                    throw new InvalidOperationException(
                        $"도로 평탄화 실패: {renderer.name} 상판={renderer.bounds.max.y:0.###}");
            }

            Debug.Log($"[SnowDelivery] Routes 전체 하강 · 도로 {roadRenderers.Count}개 · " +
                      $"상판 {sourceSurfaceY:0.###} -> {FlatRoadSurfaceY:0.###} · " +
                      $"불필요해진 커브 램프 {curbRamps.GetComponentsInChildren<BoxCollider>(true).Length}개 비활성");
            return roadRenderers.Count;
        }

        private static SnowGroundMap BakeSnowDeliveryGroundMap()
        {
            SnowGroundMap source = AssetDatabase.LoadAssetAtPath<SnowGroundMap>(SourceGroundMapPath);
            if (source == null || !source.IsBaked)
                throw new InvalidOperationException($"원본 눈 바닥 맵이 없거나 굽히지 않았다: {SourceGroundMapPath}");

            EnsureFolder("Assets/Game/InGame/Cleanliness/Data");
            SnowGroundMap map = AssetDatabase.LoadAssetAtPath<SnowGroundMap>(GroundMapPath);
            if (map == null)
            {
                map = UnityEngine.Object.Instantiate(source);
                map.name = "SnowGroundMap_SinglePlay";
                AssetDatabase.CreateAsset(map, GroundMapPath);
            }
            else
            {
                EditorUtility.CopySerialized(source, map);
                map.name = "SnowGroundMap_SinglePlay";
                EditorUtility.SetDirty(map);
            }

            if (!SnowGroundBake.Bake(map, out string report))
                throw new InvalidOperationException($"SnowDelivery 눈 바닥 맵 굽기 실패: {report}");

            Debug.Log($"[SnowDelivery] 파생 씬 전용 눈 바닥 맵 갱신 · {report}");
            return map;
        }

        private static void BuildSnowballGrowthHud(Transform parent, PenguinInputReader playerInput)
        {
            PenguinSnowball penguinSnowball = playerInput.GetComponent<PenguinSnowball>();
            PenguinMomentumHandling momentum = playerInput.GetComponent<PenguinMomentumHandling>();
            PenguinMomentumSnowballBinder binder =
                playerInput.GetComponent<PenguinMomentumSnowballBinder>();
            Camera playerCamera = playerInput.GetComponentInChildren<Camera>(true);
            if (penguinSnowball == null || momentum == null || binder == null || playerCamera == null)
                throw new InvalidOperationException(
                    "SinglePlay 관성 펭귄에 눈덩이 입력·관성·바인더·플레이 카메라가 모두 필요하다.");

            PanelSettings panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(SnowballGrowthHudPanelSettingsPath);
            VisualTreeAsset visualTree =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(SnowballGrowthHudUxmlPath);
            if (panelSettings == null || visualTree == null)
                throw new InvalidOperationException(
                    $"눈덩이 성장 HUD 에셋이 없다: panel={panelSettings}, uxml={visualTree}");

            var hudObject = new GameObject("SnowballGrowthHud");
            hudObject.transform.SetParent(parent);
            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = visualTree;
            document.sortingOrder = 20;

            SnowballGrowthArcHud hud = hudObject.AddComponent<SnowballGrowthArcHud>();
            hud.Configure(null, playerCamera);
            SnowballGrowthPlayableSceneController controller =
                hudObject.AddComponent<SnowballGrowthPlayableSceneController>();
            controller.Configure(hud, penguinSnowball, playerCamera);
        }

        private static void RemoveRandomGiftSpawner(Scene scene)
        {
            int removed = 0;
            foreach (GiftSpawner spawner in UnityEngine.Object.FindObjectsByType<GiftSpawner>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (spawner.gameObject.scene != scene) continue;
                UnityEngine.Object.DestroyImmediate(spawner.gameObject);
                removed++;
            }

            Debug.Log($"[SnowDelivery] 플레이어 주변 랜덤 GiftSpawner 제거: {removed}개");
        }

        private static void BuildCentralTreePlazaAccess(Scene scene)
        {
            GameObject plaza = FindSceneObject(scene, "CentralTreePlaza");
            if (plaza == null) throw new InvalidOperationException("CentralTreePlaza를 찾을 수 없다");

            Transform outer = plaza.transform.Find("PlazaOuter");
            Transform inner = plaza.transform.Find("PlazaInner");
            if (outer == null || inner == null)
                throw new InvalidOperationException("CentralTreePlaza의 PlazaOuter/PlazaInner를 찾을 수 없다");

            CapsuleCollider blockingCollider = outer.GetComponent<CapsuleCollider>();
            if (blockingCollider != null) UnityEngine.Object.DestroyImmediate(blockingCollider);

            MeshFilter innerFilter = inner.GetComponent<MeshFilter>();
            MeshRenderer innerRenderer = inner.GetComponent<MeshRenderer>();
            if (innerFilter == null || innerRenderer == null || innerFilter.sharedMesh == null)
                throw new InvalidOperationException("PlazaInner의 메시를 찾을 수 없다");

            MeshCollider innerCollider = inner.GetComponent<MeshCollider>();
            if (innerCollider == null) innerCollider = inner.gameObject.AddComponent<MeshCollider>();
            innerCollider.sharedMesh = innerFilter.sharedMesh;

            Transform blockedBench = plaza.transform.Find("Bench_North");
            if (blockedBench != null) UnityEngine.Object.DestroyImmediate(blockedBench.gameObject);

            Bounds innerBounds = innerRenderer.bounds;
            MeshRenderer outerRenderer = outer.GetComponent<MeshRenderer>();
            if (outerRenderer == null) throw new InvalidOperationException("PlazaOuter의 Renderer를 찾을 수 없다");

            Vector3 center = innerBounds.center;
            float innerRadiusM = Mathf.Min(innerBounds.extents.x, innerBounds.extents.z);
            float outerRadiusM = Mathf.Min(outerRenderer.bounds.extents.x, outerRenderer.bounds.extents.z);
            float surfaceY = innerBounds.max.y;
            BuildPlazaAccessRamps(plaza.transform, innerRenderer.sharedMaterial, center, innerRadiusM,
                outerRadiusM, surfaceY, outerRenderer.bounds.min.y - 0.04f);

            Debug.Log($"[SnowDelivery] CentralTreePlaza 운반 접근 경사로 생성 · surfaceY={surfaceY:0.###}");
        }

        private static Vector3 BuildSnowGiftMachine(Scene scene, Transform parent, SnowCpuStage snowStage)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(SnowGiftMachinePrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"눈 선물 기계 프리팹을 찾을 수 없다: {SnowGiftMachinePrefabPath}");

            GameObject machine = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            machine.name = "PF_SnowGiftMachine";
            machine.transform.SetParent(parent, true);
            machine.transform.SetPositionAndRotation(
                SnowGiftMachinePosition, Quaternion.Euler(SnowGiftMachineEuler));

            SnowGiftMachinePresentation presentation = machine.GetComponent<SnowGiftMachinePresentation>();
            if (presentation == null)
                throw new InvalidOperationException("PF_SnowGiftMachine에 SnowGiftMachinePresentation이 없다");

            Transform giftLandingAnchor = new GameObject("GiftLandingAnchor").transform;
            giftLandingAnchor.SetParent(machine.transform, false);
            Vector3 giftLandingPosition = machine.transform.TransformPoint(SnowGiftLandingLocalXZ);
            giftLandingPosition.y = FlatRoadSurfaceY + 0.04f;
            giftLandingAnchor.position = giftLandingPosition;
            presentation.ConfigureSnowDeliveryConversion(snowStage, giftLandingAnchor);

            SnowGiftMachineSuctionTrigger trigger =
                machine.GetComponentInChildren<SnowGiftMachineSuctionTrigger>(true);
            if (trigger == null)
                throw new InvalidOperationException("PF_SnowGiftMachine에 눈덩이 흡입 트리거가 없다");
            trigger.Configure(
                presentation,
                SnowGiftMachineOpeningRadius,
                SnowGiftMachineFrontDepth,
                SnowGiftMachineRearTolerance,
                SnowGiftMachineSurfaceTolerance);
            BoxCollider triggerCollider = trigger.GetComponent<BoxCollider>();
            if (triggerCollider == null)
                throw new InvalidOperationException("PF_SnowGiftMachine 흡입 트리거에 BoxCollider가 없다");
            float triggerDiameter = SnowGiftMachineOpeningRadius * 2f +
                                    SnowGiftMachineSurfaceTolerance;
            triggerCollider.size = new Vector3(
                triggerDiameter,
                triggerDiameter,
                SnowGiftMachineFrontDepth);

            SetReferencedBehaviourEnabled(presentation, "_intakeFeedback", false);
            SetReferencedBehaviourEnabled(presentation, "_digestFeedback", false);
            SetReferencedBehaviourEnabled(presentation, "_giftPopFeedback", false);

            EditorUtility.SetDirty(presentation);
            Debug.Log($"[SnowDelivery] PF_SnowGiftMachine 배치 · position={SnowGiftMachinePosition} · " +
                      $"giftLanding={giftLandingPosition} · 표면 흡입 트리거 + 성장 단계별 선물 변환 및 " +
                      "SnowCpuStage 장부 연결");
            return giftLandingPosition;
        }

        private static void BuildThiefRaidRig(Scene scene, Transform parent, RequestDirector requestDirector,
            Vector3 giftLandingPosition)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ThiefRaidRigPrefabPath);
            if (prefab == null)
                throw new InvalidOperationException($"도둑 습격 리그 프리팹을 찾을 수 없다: {ThiefRaidRigPrefabPath}");

            GameObject rig = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            rig.name = "ThiefRaidRig";
            rig.transform.SetParent(parent, true);
            rig.transform.position = giftLandingPosition;

            ThiefDirector thiefDirector = rig.GetComponentInChildren<ThiefDirector>(true);
            ThiefRaidSite raidSite = rig.GetComponentInChildren<ThiefRaidSite>(true);
            BoxCollider lootVolume = raidSite != null ? raidSite.GetComponent<BoxCollider>() : null;
            if (thiefDirector == null || raidSite == null || lootVolume == null)
                throw new InvalidOperationException("PF_ThiefRaidRig에 Director/Site/LootVolume 구성이 없다");

            lootVolume.size = new Vector3(10f, 4f, 10f);
            SetSerialized(thiefDirector, "_requestDirector", requestDirector);
            BuildThiefNavMesh(rig);
            EditorUtility.SetDirty(lootVolume);
            EditorUtility.SetDirty(thiefDirector);
        }

        private static void BuildThiefNavMesh(GameObject rig)
        {
            NavMeshSurface surface = rig.AddComponent<NavMeshSurface>();
            surface.collectObjects = CollectObjects.All;
            surface.useGeometry = UnityEngine.AI.NavMeshCollectGeometry.PhysicsColliders;
            surface.BuildNavMesh();
            if (surface.navMeshData == null)
                throw new InvalidOperationException("SinglePlay 도둑용 NavMesh를 만들지 못했다");

            UnityEngine.AI.NavMeshData generated = surface.navMeshData;
            UnityEngine.AI.NavMeshData existing =
                AssetDatabase.LoadAssetAtPath<UnityEngine.AI.NavMeshData>(ThiefNavMeshDataPath);
            if (existing == null)
            {
                EnsureFolder("Assets/Game/InGame/Interaction/Thief/NavMesh");
                AssetDatabase.CreateAsset(generated, ThiefNavMeshDataPath);
                return;
            }

            surface.RemoveData();
            EditorUtility.CopySerialized(generated, existing);
            UnityEngine.Object.DestroyImmediate(generated);
            surface.navMeshData = existing;
            surface.AddData();
            EditorUtility.SetDirty(existing);
            EditorUtility.SetDirty(surface);
        }

        private static void SetReferencedBehaviourEnabled(
            UnityEngine.Object owner, string propertyName, bool enabled)
        {
            SerializedProperty property = new SerializedObject(owner).FindProperty(propertyName);
            if (property?.objectReferenceValue is not Behaviour behaviour) return;
            behaviour.enabled = enabled;
            EditorUtility.SetDirty(behaviour);
        }

        private static void BuildPlazaAccessRamps(Transform parent, Material material, Vector3 center,
            float innerRadiusM, float outerRadiusM, float surfaceY, float groundY)
        {
            var group = new GameObject("GiftWrappingAccessRamps");
            group.transform.SetParent(parent, false);

            BuildPlazaAccessRamp(group.transform, material, center, innerRadiusM, outerRadiusM,
                surfaceY, groundY, Vector3.forward, "North");
            BuildPlazaAccessRamp(group.transform, material, center, innerRadiusM, outerRadiusM,
                surfaceY, groundY, Vector3.right, "East");
            BuildPlazaAccessRamp(group.transform, material, center, innerRadiusM, outerRadiusM,
                surfaceY, groundY, Vector3.back, "South");
            BuildPlazaAccessRamp(group.transform, material, center, innerRadiusM, outerRadiusM,
                surfaceY, groundY, Vector3.left, "West");
        }

        private static void BuildPlazaAccessRamp(Transform parent, Material material, Vector3 center,
            float innerRadiusM, float outerRadiusM, float surfaceY, float groundY,
            Vector3 outward, string suffix)
        {
            Vector3 lowerSurface = center + outward * (outerRadiusM + 4f);
            lowerSurface.y = FindApproachSurfaceY(lowerSurface + outward * 0.5f, groundY) - 0.12f;
            Vector3 upperSurface = center + outward * (innerRadiusM - 1.1f);
            upperSurface.y = surfaceY;

            Vector3 tangent = upperSurface - lowerSurface;
            float length = tangent.magnitude;
            tangent /= length;
            Vector3 horizontal = Vector3.ProjectOnPlane(tangent, Vector3.up).normalized;
            Vector3 widthAxis = Vector3.Cross(Vector3.up, horizontal).normalized;
            Vector3 normal = Vector3.Cross(tangent, widthAxis).normalized;
            const float thicknessM = 0.16f;

            GameObject ramp = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ramp.name = "GiftWrappingAccessRamp_" + suffix;
            ramp.transform.SetParent(parent, true);
            ramp.transform.position = (lowerSurface + upperSurface) * 0.5f - normal * (thicknessM * 0.5f);
            ramp.transform.rotation = Quaternion.LookRotation(tangent, normal);
            ramp.transform.localScale = new Vector3(6f, thicknessM, length);
            ramp.GetComponent<MeshRenderer>().sharedMaterial = material;
            Physics.SyncTransforms();
            OpenIntersectingRouteColliders(
                ramp.GetComponent<BoxCollider>(), lowerSurface, upperSurface);
            GameObjectUtility.SetStaticEditorFlags(ramp,
                StaticEditorFlags.BatchingStatic | StaticEditorFlags.OccluderStatic |
                StaticEditorFlags.OccludeeStatic);
        }

        private static void OpenIntersectingRouteColliders(
            BoxCollider rampCollider, Vector3 lowerSurface, Vector3 upperSurface)
        {
            BoxCollider[] colliders = UnityEngine.Object.FindObjectsByType<BoxCollider>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (BoxCollider route in colliders)
            {
                bool vehicleCurbRamp = route.name.StartsWith("Ramp_") &&
                                       route.name.Contains("_Curb_");
                if (!route.enabled || (!vehicleCurbRamp &&
                    !route.name.StartsWith("Curb_") && !route.name.StartsWith("Road_"))) continue;
                if (!IsRouteInAccessCorridor(
                        route, rampCollider.bounds, lowerSurface, upperSurface)) continue;

                if (vehicleCurbRamp)
                {
                    route.enabled = false;
                    continue;
                }

                SplitRouteCollider(route, rampCollider.bounds, 0.5f);
            }
        }

        private static bool IsRouteInAccessCorridor(
            BoxCollider route, Bounds rampBounds, Vector3 lowerSurface, Vector3 upperSurface)
        {
            if (route.bounds.max.y < rampBounds.min.y - 0.2f ||
                route.bounds.min.y > rampBounds.max.y + 0.2f) return false;

            for (int index = 0; index <= 16; index++)
            {
                Vector3 point = Vector3.Lerp(lowerSurface, upperSurface, index / 16f);
                Vector3 separation = route.ClosestPoint(point) - point;
                separation.y = 0f;
                if (separation.sqrMagnitude <= 3.5f * 3.5f) return true;
            }

            return false;
        }

        private static void SplitRouteCollider(
            BoxCollider source, Bounds accessBounds, float clearanceM)
        {
            Vector3 size = source.size;
            Vector3 center = source.center;
            Vector3 scale = source.transform.lossyScale;
            bool splitZ = Mathf.Abs(size.z * scale.z) >= Mathf.Abs(size.x * scale.x);
            int axis = splitZ ? 2 : 0;
            float axisScale = Mathf.Abs(scale[axis]);
            if (axisScale <= 0.0001f) return;

            float sourceMin = center[axis] - size[axis] * 0.5f;
            float sourceMax = center[axis] + size[axis] * 0.5f;
            float gapMin = float.MaxValue;
            float gapMax = float.MinValue;
            for (int xSign = -1; xSign <= 1; xSign += 2)
            {
                for (int zSign = -1; zSign <= 1; zSign += 2)
                {
                    Vector3 corner = accessBounds.center + new Vector3(
                        accessBounds.extents.x * xSign, 0f, accessBounds.extents.z * zSign);
                    float local = source.transform.InverseTransformPoint(corner)[axis];
                    gapMin = Mathf.Min(gapMin, local);
                    gapMax = Mathf.Max(gapMax, local);
                }
            }

            float clearanceLocal = clearanceM / axisScale;
            gapMin = Mathf.Clamp(gapMin - clearanceLocal, sourceMin, sourceMax);
            gapMax = Mathf.Clamp(gapMax + clearanceLocal, sourceMin, sourceMax);
            if (gapMax <= sourceMin || gapMin >= sourceMax) return;

            source.enabled = false;
            CreateRouteColliderSegment(source, axis, sourceMin, gapMin);
            CreateRouteColliderSegment(source, axis, gapMax, sourceMax);
        }

        private static void CreateRouteColliderSegment(
            BoxCollider source, int axis, float segmentMin, float segmentMax)
        {
            if (segmentMax - segmentMin <= 0.001f) return;

            BoxCollider segment = source.gameObject.AddComponent<BoxCollider>();
            Vector3 segmentCenter = source.center;
            Vector3 segmentSize = source.size;
            segmentCenter[axis] = (segmentMin + segmentMax) * 0.5f;
            segmentSize[axis] = segmentMax - segmentMin;
            segment.center = segmentCenter;
            segment.size = segmentSize;
            segment.sharedMaterial = source.sharedMaterial;
            segment.isTrigger = source.isTrigger;
        }

        private static float FindApproachSurfaceY(Vector3 position, float fallbackY)
        {
            Physics.SyncTransforms();
            Vector3 origin = new Vector3(position.x, position.y + 20f, position.z);
            RaycastHit[] hits = Physics.RaycastAll(origin, Vector3.down, 40f);
            Array.Sort(hits, (left, right) => left.distance.CompareTo(right.distance));
            foreach (RaycastHit hit in hits)
            {
                if (hit.collider.isTrigger || hit.collider.name.StartsWith("Curb_") ||
                    hit.collider.name.StartsWith("Road_")) continue;
                return hit.point.y;
            }

            return fallbackY;
        }

        private static GameObject FindSceneObject(Scene scene, string name)
        {
            foreach (Transform candidate in UnityEngine.Object.FindObjectsByType<Transform>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (candidate.gameObject.scene == scene && candidate.name == name) return candidate.gameObject;
            }

            return null;
        }

        /// <summary><c>Snow_BallPush_Test</c>의 최신 CPU 권위 눈 스택을 복제하고 맵 범위만 덮어쓴다.</summary>
        internal static SnowCpuStage BuildSnowSystem(Scene targetScene, SnowGroundMap groundMap = null)
        {
            RemoveSnowSystems(targetScene);

            Scene sourceScene = SceneManager.GetSceneByPath(SnowSourceScenePath);
            bool openedSourceScene = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (openedSourceScene)
                sourceScene = EditorSceneManager.OpenScene(SnowSourceScenePath, OpenSceneMode.Additive);

            SnowCpuStage stage;
            try
            {
                SnowCpuStage sourceStage = null;
                foreach (GameObject root in sourceScene.GetRootGameObjects())
                {
                    SnowCpuStage candidate = root.GetComponentInChildren<SnowCpuStage>(true);
                    if (candidate == null) continue;
                    if (sourceStage != null)
                        throw new InvalidOperationException(
                            $"원본 눈 씬에 SnowCpuStage가 둘 이상 있다: {SnowSourceScenePath}");
                    sourceStage = candidate;
                }

                if (sourceStage == null)
                    throw new InvalidOperationException(
                        $"원본 눈 씬에 SnowCpuStage가 없다: {SnowSourceScenePath}");

                GameObject clone = UnityEngine.Object.Instantiate(sourceStage.gameObject);
                clone.name = sourceStage.gameObject.name;
                SceneManager.MoveGameObjectToScene(clone, targetScene);
                stage = clone.GetComponent<SnowCpuStage>();
            }
            finally
            {
                if (openedSourceScene && sourceScene.IsValid() && sourceScene.isLoaded)
                    EditorSceneManager.CloseScene(sourceScene, true);
            }

            GameObject gameObject = stage.gameObject;
            SnowDisplaceView displaceView = gameObject.GetComponent<SnowDisplaceView>();
            if (gameObject.GetComponent<SnowCpuStageView>() == null ||
                gameObject.GetComponent<SnowSystem>() == null || displaceView == null)
                throw new InvalidOperationException(
                    $"원본 눈 씬의 SnowCpuStage 스택이 불완전하다: {SnowSourceScenePath}");

            if (groundMap == null)
                groundMap = AssetDatabase.LoadAssetAtPath<SnowGroundMap>(GroundMapPath);
            if (groundMap == null || !groundMap.IsBaked)
                throw new InvalidOperationException($"구운 눈 바닥 맵이 없다: {GroundMapPath}");

            SetSerialized(stage, "_originXZ", new Vector2(-60f, -55f));
            SetSerialized(stage, "_sizeMeters", new Vector2(120f, 110f));
            SetSerialized(stage, "_initialDepthMm", 300);
            SetSerialized(stage, "_groundMap", groundMap);
            return stage;
        }

        private static void RemoveSnowSystems(Scene targetScene)
        {
            var legacyObjects = new HashSet<GameObject>();
            foreach (SnowStage stage in UnityEngine.Object.FindObjectsByType<SnowStage>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (stage.gameObject.scene == targetScene) legacyObjects.Add(stage.gameObject);
            foreach (SnowSurfaceRenderer renderer in UnityEngine.Object.FindObjectsByType<SnowSurfaceRenderer>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (renderer.gameObject.scene == targetScene) legacyObjects.Add(renderer.gameObject);
            foreach (SnowPanelBuilder panel in UnityEngine.Object.FindObjectsByType<SnowPanelBuilder>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (panel.gameObject.scene == targetScene) legacyObjects.Add(panel.gameObject);
            foreach (SnowCpuStage stage in UnityEngine.Object.FindObjectsByType<SnowCpuStage>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (stage.gameObject.scene == targetScene) legacyObjects.Add(stage.gameObject);

            foreach (GameObject legacyObject in legacyObjects)
                if (legacyObject != null) UnityEngine.Object.DestroyImmediate(legacyObject);
        }

        /// <summary>모든 집의 문 위치 무게중심에 가장 가까운 집을 중앙(기지)으로 고른다.</summary>
        internal static int PickCentralHouse(IReadOnlyList<DeliveryHouse> houses)
        {
            Vector3 centroid = Vector3.zero;
            for (int index = 0; index < houses.Count; index++) centroid += houses[index].DoorPosition;
            centroid /= houses.Count;

            int best = 0;
            float bestSqr = float.PositiveInfinity;
            for (int index = 0; index < houses.Count; index++)
            {
                float sqr = (houses[index].DoorPosition - centroid).sqrMagnitude;
                if (sqr >= bestSqr) continue;
                bestSqr = sqr;
                best = index;
            }
            return best;
        }

        internal static GiftBoxCatalog LoadOrCreateCatalog()
        {
            GiftBoxCatalog catalog = AssetDatabase.LoadAssetAtPath<GiftBoxCatalog>(CatalogPath);
            if (catalog != null) return catalog;

            catalog = ScriptableObject.CreateInstance<GiftBoxCatalog>();
            typeof(GiftBoxCatalog)
                .GetField("_kinds", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                .SetValue(catalog, new List<GiftBoxKindEntry>(GiftBoxCatalog.SeedRainbow()));
            EnsureFolder("Assets/Game/InGame/Delivery/Data");
            AssetDatabase.CreateAsset(catalog, CatalogPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<GiftBoxCatalog>(CatalogPath);
        }

        private static StageIntroController BuildStageIntroUI(Transform parent)
        {
            var uiObject = new GameObject("StageIntroUI");
            uiObject.transform.SetParent(parent);
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(StageIntroPanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(StageIntroUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("StageIntro UXML/PanelSettings를 찾을 수 없다");

            StageIntroController intro = uiObject.AddComponent<StageIntroController>();
            SetSerialized(intro, "_playOnEnable", false);
            SetSerialized(intro, "_spaceToReplay", false);
            return intro;
        }

        private static void BuildStageHudUI(Transform parent, RequestDirector director, GameManager manager,
            GiftBoxCatalog catalog, Transform player, BlizzardEvent blizzard)
        {
            var uiObject = new GameObject("StageHudUI");
            uiObject.transform.SetParent(parent);
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(StageHudPanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(StageHudUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("StageHUD UXML/PanelSettings를 찾을 수 없다");

            StageHUDController hud = uiObject.AddComponent<StageHUDController>();
            DeliverySceneRigBuilder.BuildOrderAddedHudFeedback(uiObject.transform, hud);
            uiObject.AddComponent<RequestHudPresenter>().Configure(hud, director, manager, catalog, player);
            uiObject.AddComponent<BlizzardAlertPresenter>().Configure(blizzard);
        }

        private static BlizzardEvent BuildBlizzardSystem(Transform parent, SnowCpuStage snowStage,
            out ScheduledBlizzardDirector scheduler)
        {
            var weatherObject = new GameObject("ScheduledBlizzard");
            weatherObject.transform.SetParent(parent);

            var windAudio = weatherObject.AddComponent<AudioSource>();
            windAudio.playOnAwake = false;
            windAudio.loop = true;
            windAudio.spatialBlend = 0f;
            windAudio.dopplerLevel = 0f;

            BlizzardEvent weatherEvent = weatherObject.AddComponent<BlizzardEvent>();
            SetSerialized(weatherEvent, "_snowStage", snowStage);
            SetSerialized(weatherEvent, "_warningDuration", 5f);
            SetSerialized(weatherEvent, "_recoveryDuration", 3f);
            SetSerialized(weatherEvent, "_cooldownDuration", 0f);
            SetSerialized(weatherEvent, "_moveSpeedMps", 7f);
            SetSerialized(weatherEvent, "_coreRadiusM", 14f);
            SetSerialized(weatherEvent, "_edgeFeatherM", 5f);
            SetSerialized(weatherEvent, "_candidateStepM", 2f);
            SetSerialized(weatherEvent, "_minimumRegionSeparationM", 28f);
            SetSerialized(weatherEvent, "_snowfallAmountMm", 120);
            SetSerialized(weatherEvent, "_maximumSnowDepthMm", 300);

            BlizzardEventPresentation presentation = weatherObject.AddComponent<BlizzardEventPresentation>();
            SetSerialized(presentation, "_event", weatherEvent);
            SetSerialized(presentation, "_windAudio", windAudio);
            AudioClip windLoop = AssetDatabase.LoadAssetAtPath<AudioClip>(BlizzardWindAudioPath);
            if (windLoop != null) SetSerialized(presentation, "_windLoop", windLoop);

            scheduler = weatherObject.AddComponent<ScheduledBlizzardDirector>();
            SetSerialized(scheduler, "_event", weatherEvent);
            SetSerializedIntArray(scheduler, "_blizzardDayIndices", new[] { 1 });
            return weatherEvent;
        }

        private static void BuildStageDateCoordinator(Transform parent, GameManager manager,
            TimeOfDayDirector timeOfDay, ScheduledBlizzardDirector blizzard)
        {
            var coordinatorObject = new GameObject("StageDateCoordinator");
            coordinatorObject.transform.SetParent(parent);
            coordinatorObject.AddComponent<StageDateCoordinator>().Configure(manager, timeOfDay, blizzard);
        }

        private static StageOutroController BuildStageOutroUI(Transform parent)
        {
            var uiObject = new GameObject("StageOutroUI");
            uiObject.transform.SetParent(parent);
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(StageOutroPanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(StageOutroUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("StageOutro UXML/PanelSettings를 찾을 수 없다");

            StageOutroController outro = uiObject.AddComponent<StageOutroController>();
            SetSerialized(outro, "_playOnEnable", false);
            uiObject.SetActive(false);
            return outro;
        }

        /// <summary>
        /// 게임 중 ESC 로 여는 일시정지 메뉴. <b>손으로 씬에 넣지 않고 여기서 조립하는 이유</b>는
        /// 이 빌더가 <c>SinglePlay</c> 를 매번 처음부터 다시 만들기 때문이다 — 손으로 넣은 것은
        /// 다음 실행에 사라진다(2026-08-31 에 실제로 그렇게 될 뻔했다).
        ///
        /// <para>멀티에서는 <c>_playerInput</c> 과 <c>_cameraOrbit</c> 이 <c>NULL</c> 이 된다.
        /// <see cref="MultiPlaySceneBuilder"/> 가 씬의 펭귄을 지우기 때문이고, 그쪽은 스폰된 아바타가
        /// <c>PauseMenuController.BindLocalPlayer</c> 로 자기를 넣는다. 여기서 채우는 값은
        /// <b>싱글용</b>이다.</para>
        /// </summary>
        private static void BuildPauseMenuUI(Transform parent, PenguinInputReader playerInput,
            RequestStageFlowPresenter flow, AugmentSelectionDirector augments)
        {
            var uiObject = new GameObject("PauseMenuUI");
            uiObject.transform.SetParent(parent);
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PauseMenuPanelSettingsPath);
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(PauseMenuUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("PauseMenu UXML/PanelSettings를 찾을 수 없다");

            PauseMenuController pause = uiObject.AddComponent<PauseMenuController>();
            SetSerialized(pause, "_playerInput", playerInput);
            SetSerialized(pause, "_stageFlow", flow);
            SetSerialized(pause, "_augmentSelection", augments);

            // 카메라는 펭귄 밑에 있다. 끄면 커서 잠금도 함께 풀리므로 이 참조가 곧 커서 처리다.
            PenguinCameraOrbit orbit = playerInput != null
                ? playerInput.GetComponentInParent<PenguinCameraOrbit>()
                  ?? playerInput.transform.root.GetComponentInChildren<PenguinCameraOrbit>(true)
                : null;
            if (orbit == null) throw new InvalidOperationException("씬에 PenguinCameraOrbit이 없다");
            SetSerialized(pause, "_cameraOrbit", orbit);
        }

        /// <summary>
        /// 게임 중 짧게 뜨는 알림. 지금 태우는 것은 "누가 나갔다" 하나뿐이지만, 알릴 것이 생기는
        /// 자리는 앞으로 늘어난다 — 그래서 프레젠터는 무엇을 알릴지 모르고 <c>Post</c> 만 받는다.
        ///
        /// <para>인스펙터로 물릴 것이 없다. 알림을 넣는 쪽은 정적 <c>Post</c> 를 쓰고, 나간 사람을
        /// 문장으로 바꾸는 것은 같은 오브젝트의 <see cref="PlayerLeftAnnouncer"/> 가 한다.</para>
        /// </summary>
        /// <summary>
        /// 일차가 넘어갈 때 카드 3장을 띄우는 증강 리그와, 고른 효과를 받는 소비처 넷의 배선.
        ///
        /// <para><b>손으로 씬에 놓지 않는 이유.</b> 이 빌더가 <c>SinglePlay</c> 를 매번 처음부터
        /// 다시 만든다 — 손으로 놓은 리그는 다음 실행에 사라진다. 증강은 2026-09-01 에 실제로
        /// 그렇게 놓였다가 여기로 옮겼다.</para>
        ///
        /// <para><b>로드아웃은 판에 하나다</b>(팀 공유). 소비처 넷이 같은 인스턴스를 본다 —
        /// 비어 있으면 넷 다 기존 동작 그대로라, 이 배선이 없어도 게임은 돈다.</para>
        ///
        /// <para>⚠ <b>멀티에서는 펭귄 쪽 참조가 풀린다.</b> 아바타가 런타임 스폰이라
        /// <c>PenguinLocomotion</c> 의 로드아웃은 그때 다시 물려야 한다 — 일시정지 메뉴가
        /// <c>BindLocalPlayer</c> 로 푸는 것과 같은 문제이고, 멀티 배선을 할 때 다룬다.</para>
        /// </summary>
        private static AugmentSelectionDirector BuildAugmentRig(Transform parent,
            TimeOfDayDirector timeOfDay, PenguinInputReader playerInput, GameManager manager,
            RequestDirector director)
        {
            AugmentPool pool = AssetDatabase.LoadAssetAtPath<AugmentPool>(AugmentPoolPath);
            if (pool == null) throw new InvalidOperationException($"증강 풀이 없다: {AugmentPoolPath}");

            var rig = new GameObject("AugmentRig");
            rig.transform.SetParent(parent);
            AugmentLoadout loadout = rig.AddComponent<AugmentLoadout>();

            var viewObject = new GameObject("AugmentSelectUI");
            viewObject.transform.SetParent(rig.transform);
            var document = viewObject.AddComponent<UIDocument>();
            document.panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(AugmentSelectPanelSettingsPath);
            document.visualTreeAsset =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(AugmentSelectUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("AugmentSelect UXML/PanelSettings를 찾을 수 없다");
            AugmentSelectionView view = viewObject.AddComponent<AugmentSelectionView>();

            AugmentSelectionDirector selection = rig.AddComponent<AugmentSelectionDirector>();
            SetSerialized(selection, "_timeOfDay", timeOfDay);
            SetSerialized(selection, "_loadout", loadout);
            SetSerialized(selection, "_pool", pool);
            SetSerialized(selection, "_view", view);
            SetSerialized(selection, "_input", playerInput);

            // 소비처 넷. 기계는 씬에 이미 놓여 있으므로 찾아서 물린다.
            SetSerialized(manager, "_augments", loadout);
            SetSerialized(director, "_augments", loadout);

            // 쉬는 시간 게이트. 비어 있으면 게이트가 없는 것과 같으므로, 증강을 놓는 이 자리에서 같이 문다.
            SetSerialized(director, "_intermission", selection);
            SetSerialized(manager, "_intermission", selection);
            PenguinLocomotion locomotion = playerInput != null
                ? playerInput.GetComponent<PenguinLocomotion>()
                : null;
            if (locomotion != null) SetSerialized(locomotion, "_augments", loadout);
            SnowGiftMachinePresentation machine =
                UnityEngine.Object.FindAnyObjectByType<SnowGiftMachinePresentation>();
            if (machine != null) SetSerialized(machine, "_augments", loadout);
            return selection;
        }

        private static void BuildWorldMessageUI(Transform parent)
        {
            var uiObject = new GameObject("WorldMessageUI");
            uiObject.transform.SetParent(parent);
            var document = uiObject.AddComponent<UIDocument>();
            document.panelSettings =
                AssetDatabase.LoadAssetAtPath<PanelSettings>(WorldMessagePanelSettingsPath);
            document.visualTreeAsset =
                AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(WorldMessageUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("WorldMessage UXML/PanelSettings를 찾을 수 없다");

            uiObject.AddComponent<WorldMessagePresenter>();
            uiObject.AddComponent<PlayerLeftAnnouncer>();
        }

        private static RequestStageFlowPresenter BuildStageFlowPresenter(Transform parent,
            GameManager manager, RequestDirector director, StageIntroController intro, StageOutroController outro,
            PenguinInputReader playerInput, SnowCpuStage snowStage)
        {
            var presenterObject = new GameObject("RequestStageFlowPresenter");
            presenterObject.transform.SetParent(parent);
            RequestStageFlowPresenter presenter = presenterObject.AddComponent<RequestStageFlowPresenter>();
            presenter.Configure(manager, director, intro, outro, playerInput, snowStage);
            return presenter;
        }

        private static void BindOutroAction(StageOutroController outro, string fieldName, UnityAction listener)
        {
            FieldInfo field = typeof(StageOutroController).GetField(
                fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
            if (field?.GetValue(outro) is not UnityEvent unityEvent)
                throw new MissingFieldException(typeof(StageOutroController).Name, fieldName);
            UnityEventTools.AddPersistentListener(unityEvent, listener);
            EditorUtility.SetDirty(outro);
        }

        /// <summary>하루 주기(기본 5분) 하늘. 맵의 디렉셔널 라이트를 그대로 빌려 쓰고 스카이박스는
        /// 런타임 복제본에 칠하므로, 씬에 저장된 블루아워 라이팅 원본은 그대로 남는다.</summary>
        private static TimeOfDayDirector BuildTimeOfDay(Transform parent)
        {
            var timeObject = new GameObject("TimeOfDayDirector");
            timeObject.transform.SetParent(parent);
            TimeOfDayDirector director = timeObject.AddComponent<TimeOfDayDirector>();
            SetSerialized(director, "_config", LoadOrCreateTimeOfDayConfig());
            SetSerialized(director, "_autoStart", false);

            // 달은 맵이 소유하지 않는다 — 하루 주기가 데려오는 조명이므로 리그 아래에 둔다.
            var moonObject = new GameObject("Moon");
            moonObject.transform.SetParent(timeObject.transform);
            Light moon = moonObject.AddComponent<Light>();
            moon.type = LightType.Directional;
            moon.shadows = LightShadows.Soft;
            moon.intensity = 0f;
            moon.enabled = false;
            SetSerialized(director, "_moon", moon);

            var starObject = new GameObject("StarDome");
            starObject.transform.SetParent(timeObject.transform);
            SkyDome stars = starObject.AddComponent<SkyDome>();
            Texture starTexture = AssetDatabase.LoadAssetAtPath<Texture>(StarfieldTexturePath);
            if (starTexture != null) SetSerialized(stars, "_texture", starTexture);
            SetSerialized(director, "_stars", stars);

            var auroraObject = new GameObject("AuroraDome");
            auroraObject.transform.SetParent(timeObject.transform);
            SkyDome aurora = auroraObject.AddComponent<SkyDome>();
            Texture auroraTexture = AssetDatabase.LoadAssetAtPath<Texture>(AuroraTexturePath);
            if (auroraTexture != null) SetSerialized(aurora, "_texture", auroraTexture);
            // 오로라는 별보다 안쪽에서 조금 더 빠르게 흘러야 커튼이 움직이는 게 읽힌다.
            SetSerializedFloat(aurora, "_radius", 820f);
            SetSerializedFloat(aurora, "_rotationDegreesPerDay", 70f);
            SetSerializedFloat(aurora, "_maxIntensity", 0.85f);
            SetSerialized(director, "_aurora", aurora);

            var moonDiskObject = new GameObject("MoonDisk");
            moonDiskObject.transform.SetParent(timeObject.transform);
            MoonDisk moonDisk = moonDiskObject.AddComponent<MoonDisk>();
            SetSerialized(director, "_moonDisk", moonDisk);

            // 맵이 이미 들고 있는 글로벌 Volume을 재사용한다. 새로 만들면 둘이 겹쳐 싸운다.
            Volume volume = UnityEngine.Object.FindAnyObjectByType<Volume>();
            if (volume != null) SetSerialized(director, "_volume", volume);

            Light sun = RenderSettings.sun;
            if (sun == null)
            {
                Light[] lights = UnityEngine.Object.FindObjectsByType<Light>(
                    FindObjectsInactive.Include, FindObjectsSortMode.None);
                foreach (Light light in lights)
                {
                    if (light.type != LightType.Directional) continue;
                    sun = light;
                    break;
                }
            }
            if (sun != null) SetSerialized(director, "_sun", sun);
            return director;
        }

        private static TimeOfDayConfig LoadOrCreateTimeOfDayConfig()
        {
            TimeOfDayConfig config = AssetDatabase.LoadAssetAtPath<TimeOfDayConfig>(TimeOfDayConfigPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<TimeOfDayConfig>();
            config.ApplyDefaultSky();
            config.SecondsPerDay = 120f;
            config.StartTimeOfDay = 0.28f;
            config.SkyboxTemplate = AssetDatabase.LoadAssetAtPath<Material>(ProceduralSkyPath);
            EnsureFolder("Assets/Game/InGame/Map/TimeOfDay/Data");
            AssetDatabase.CreateAsset(config, TimeOfDayConfigPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<TimeOfDayConfig>(TimeOfDayConfigPath);
        }

        internal static StageBalanceConfig LoadOrCreateConfig()
        {
            StageBalanceConfig config = AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(ConfigPath);
            if (config != null) return config;

            config = ScriptableObject.CreateInstance<StageBalanceConfig>();
            config.StartSeconds = 90f;
            config.SpawnIntervalMin = 40f;
            config.SpawnIntervalMax = 40f;
            config.BurstSize = new Vector2Int(2, 2);
            config.MaxActiveRequests = 8;
            config.TtlBase = 35f;
            config.ClearTimeBonusBase = 12f;
            EnsureFolder("Assets/Game/InGame/Cleanliness/Data");
            AssetDatabase.CreateAsset(config, ConfigPath);
            AssetDatabase.SaveAssets();
            return AssetDatabase.LoadAssetAtPath<StageBalanceConfig>(ConfigPath);
        }

        internal static void SetSerialized(UnityEngine.Object target, string field, UnityEngine.Object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.objectReferenceValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetSerializedFloat(UnityEngine.Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerialized(UnityEngine.Object target, string field, int value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.intValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerialized(UnityEngine.Object target, string field, bool value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.boolValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerialized(UnityEngine.Object target, string field, float value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.floatValue = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerialized(UnityEngine.Object target, string field, Vector2 value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.vector2Value = value;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerializedIntArray(UnityEngine.Object target, string field, int[] values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.arraySize = values.Length;
            for (int index = 0; index < values.Length; index++)
                property.GetArrayElementAtIndex(index).intValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        internal static void SetSerializedArray(UnityEngine.Object target, string field, IReadOnlyList<DeliveryHouse> values)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(field);
            if (property == null) throw new MissingFieldException(target.GetType().Name, field);
            property.arraySize = values.Count;
            for (int index = 0; index < values.Count; index++)
                property.GetArrayElementAtIndex(index).objectReferenceValue = values[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = System.IO.Path.GetDirectoryName(folder).Replace("\\", "/");
            string leaf = System.IO.Path.GetFileName(folder);
            if (!AssetDatabase.IsValidFolder(parent)) EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, leaf);
        }
    }
}
