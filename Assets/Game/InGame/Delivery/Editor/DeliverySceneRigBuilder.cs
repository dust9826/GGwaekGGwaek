using System;
using System.Collections.Generic;
using MoreMountains.Feedbacks;
using MoreMountains.Tools;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering.Universal;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// WinterVillage 맵 위에 배송 도로망·플레이어·리스폰 리그를 얹는 절차.
    /// 원래 <see cref="DeliveryTestSceneBuilder"/> 안에만 있던 것을 <c>Cleanliness/Editor/SnowDeliverySceneBuilder.cs</c>가
    /// 두 번째 소비자가 되며 공용부로 뽑았다 — 노드/도로/공장 좌표표를 두 벌로 만들지 않기 위해서다.
    /// 씬 경로·리그 루트 이름·플레이어 시작 위치처럼 씬마다 다른 값만 호출부가 매개변수로 넘긴다.
    /// </summary>
    public static class DeliverySceneRigBuilder
    {
        private const string Root = "Assets/Game/InGame/Delivery";
        private const string PrefabPath = Root + "/Prefabs/PF_DeliveryTruck.prefab";
        private const string MaterialsPath = Root + "/Materials";
        private const string HelpFontPath = "Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf";

        public const string MapScenePath =
            "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_ConceptMap.unity";

        /// <summary>도로 메시 윗면 높이. 곡선은 y=0 에서 평가되므로 차체 비주얼만 이만큼 올린다.</summary>
        public const float RoadSurfaceY = 0.31f;

        private const float TruckBodyHeight = 1.4f;
        private const float TruckHalfWidth = 1f;
        private const float WheelRadius = 0.35f;
        private const float WheelWidth = 0.2f;
        private const float WheelAxleZ = 1.3f;

        private readonly struct RoadSpec
        {
            public RoadSpec(string name, string start, string end, float width, params Vector3[] controls)
            {
                Name = name;
                Start = start;
                End = end;
                Width = width;
                Controls = controls ?? Array.Empty<Vector3>();
            }

            public string Name { get; }
            public string Start { get; }
            public string End { get; }
            public float Width { get; }
            public Vector3[] Controls { get; }
        }

        private readonly struct HouseSpec
        {
            public HouseSpec(string houseName, string nodeId, Vector3 stopPosition)
            {
                HouseName = houseName;
                NodeId = nodeId;
                StopPosition = stopPosition;
            }

            public string HouseName { get; }
            public string NodeId { get; }
            public Vector3 StopPosition { get; }
        }

        // 노드·구간·정차점은 전부 WinterVillage_ConceptMap 의 실제 지오메트리에서 뽑았다.
        // Routes/*(폭 4.5) 와 PedestrianPaths/NorthBank_Promenade_*(저작 폭 1.65)를 하나의 그래프로 합쳤다.
        private static readonly (string Id, Vector3 Position)[] NodeSpecs =
        {
            ("Central", new Vector3(-4.00f, 0f, -5.00f)),
            ("South", new Vector3(22.00f, 0f, -22.00f)),
            ("East", new Vector3(35.00f, 0f, 14.00f)),
            ("NorthWest", new Vector3(-36.00f, 0f, 29.00f)),
            ("NorthEast", new Vector3(35.00f, 0f, 36.00f)),
            ("DiagNW", new Vector3(-50.00f, 0f, 42.00f)),
            ("DiagSE", new Vector3(50.00f, 0f, -45.00f)),
            ("CrossSW", new Vector3(-52.00f, 0f, -43.00f)),
            ("SouthGate", new Vector3(7.00f, 0f, -48.00f)),
            ("NorthGate", new Vector3(35.00f, 0f, 50.00f)),
            ("H01", new Vector3(-24.00f, 0f, 27.30f)),
            ("H02", new Vector3(-14.00f, 0f, 31.20f)),
            ("H03", new Vector3(0.00f, 0f, 34.00f)),
            ("H04", new Vector3(16.00f, 0f, 34.20f)),
            ("H05", new Vector3(48.00f, 0f, 37.00f)),
            ("H06", new Vector3(10.94f, 0f, 2.28f)),
            ("H07", new Vector3(25.03f, 0f, -13.60f)),
            ("H08", new Vector3(28.78f, 0f, -3.23f)),
            ("H09", new Vector3(38.64f, 0f, -35.66f)),
            ("H10", new Vector3(-32.50f, 0f, -33.50f)),
            ("H11", new Vector3(-29.67f, 0f, -30.67f)),
        };

        public const float VehicleRoadWidth = 4.5f;

        // 프롬나드의 저작 폭은 1.65m 라 트럭(폭 2.0m + 여유 0.25m)이 물리적으로 못 들어간다.
        // 북쪽 집 다섯(01~05)은 이 길이 유일한 접근로이므로 배송 그래프에서만 4.0m 로 넓혔다.
        // 시각 메시는 그대로다 — 테스트 씬에서 북쪽 순환로를 살리기 위한 의도적인 값이다.
        public const float PromenadeWidth = 4.0f;

        private static readonly RoadSpec[] RoadSpecs =
        {
            new RoadSpec("Road_Diagonal_DiagNW_NorthWest", "DiagNW", "NorthWest", VehicleRoadWidth),
            new RoadSpec("Road_Diagonal_NorthWest_Central", "NorthWest", "Central", VehicleRoadWidth,
                         new Vector3(-24.00f, 0f, 8.00f)),
            new RoadSpec("Road_Diagonal_Central_South", "Central", "South", VehicleRoadWidth),
            new RoadSpec("Road_Diagonal_South_H09", "South", "H09", VehicleRoadWidth),
            new RoadSpec("Road_Diagonal_H09_DiagSE", "H09", "DiagSE", VehicleRoadWidth),

            new RoadSpec("Road_Cross_CrossSW_H10", "CrossSW", "H10", VehicleRoadWidth,
                         new Vector3(-39.00f, 0f, -40.00f)),
            new RoadSpec("Road_Cross_H10_H11", "H10", "H11", VehicleRoadWidth),
            new RoadSpec("Road_Cross_H11_Central", "H11", "Central", VehicleRoadWidth),
            new RoadSpec("Road_Cross_Central_H06", "Central", "H06", VehicleRoadWidth),
            new RoadSpec("Road_Cross_H06_East", "H06", "East", VehicleRoadWidth),

            new RoadSpec("Road_NorthSouth_SouthGate_South", "SouthGate", "South", VehicleRoadWidth),
            new RoadSpec("Road_NorthSouth_South_H07", "South", "H07", VehicleRoadWidth),
            new RoadSpec("Road_NorthSouth_H07_H08", "H07", "H08", VehicleRoadWidth),
            new RoadSpec("Road_NorthSouth_H08_East", "H08", "East", VehicleRoadWidth),
            new RoadSpec("Road_NorthSouth_East_NorthEast", "East", "NorthEast", VehicleRoadWidth),
            new RoadSpec("Road_NorthSouth_NorthEast_NorthGate", "NorthEast", "NorthGate", VehicleRoadWidth),

            new RoadSpec("Road_PromWest_NorthWest_H01", "NorthWest", "H01", PromenadeWidth,
                         new Vector3(-32.00f, 0f, 26.70f)),
            new RoadSpec("Road_PromWest_H01_H02", "H01", "H02", PromenadeWidth),
            new RoadSpec("Road_PromWest_H02_H03", "H02", "H03", PromenadeWidth),
            new RoadSpec("Road_PromWest_H03_H04", "H03", "H04", PromenadeWidth),
            new RoadSpec("Road_PromWest_H04_NorthEast", "H04", "NorthEast", PromenadeWidth,
                         new Vector3(28.00f, 0f, 35.00f), new Vector3(32.25f, 0f, 36.00f)),
            new RoadSpec("Road_PromEast_NorthEast_H05", "NorthEast", "H05", PromenadeWidth,
                         new Vector3(43.00f, 0f, 36.20f)),
        };

        // 정차점은 각 EntryWalkway_VillageHouse_XX 의 도로쪽 끝점이다 — 집 현관이 아니라 트럭이 서는 자리.
        private static readonly HouseSpec[] HouseSpecs =
        {
            new HouseSpec("VillageHouse_01", "H01", new Vector3(-24.00f, 0f, 27.30f)),
            new HouseSpec("VillageHouse_02", "H02", new Vector3(-14.00f, 0f, 31.20f)),
            new HouseSpec("VillageHouse_03", "H03", new Vector3(0.00f, 0f, 34.00f)),
            new HouseSpec("VillageHouse_04", "H04", new Vector3(16.00f, 0f, 34.20f)),
            new HouseSpec("VillageHouse_05", "H05", new Vector3(48.45f, 0f, 36.23f)),
            new HouseSpec("VillageHouse_06", "H06", new Vector3(10.15f, 0f, 3.89f)),
            new HouseSpec("VillageHouse_07", "H07", new Vector3(26.92f, 0f, -14.28f)),
            new HouseSpec("VillageHouse_08", "H08", new Vector3(30.61f, 0f, -3.89f)),
            new HouseSpec("VillageHouse_09", "H09", new Vector3(40.20f, 0f, -33.76f)),
            new HouseSpec("VillageHouse_10", "H10", new Vector3(-34.33f, 0f, -31.67f)),
            new HouseSpec("VillageHouse_11", "H11", new Vector3(-29.68f, 0f, -34.31f)),
        };

        public readonly struct DirectorTuning
        {
            public DirectorTuning(float requestIntervalSeconds, float snowCancelSeconds, float pointsPerMeter,
                int maxConcurrentTrucks)
            {
                RequestIntervalSeconds = requestIntervalSeconds;
                SnowCancelSeconds = snowCancelSeconds;
                PointsPerMeter = pointsPerMeter;
                MaxConcurrentTrucks = maxConcurrentTrucks;
            }

            public float RequestIntervalSeconds { get; }
            public float SnowCancelSeconds { get; }
            public float PointsPerMeter { get; }
            public int MaxConcurrentTrucks { get; }
        }

        /// <summary>집 앞 선물 배치 범위 크기(m)와 정원. 모든 집이 공유한다 — 집마다 다른 값을 줄
        /// 근거가 아직 없다.</summary>
        private static readonly Vector3 GiftZoneSize = new Vector3(2.5f, 2f, 2.5f);
        private const int GiftZoneCapacity = 3;

        public sealed class RigResult
        {
            public Dictionary<string, DeliveryRoadNode> Nodes;
            public List<DeliveryRoadSegment> Segments;
            public List<DeliveryFactory> Factories;
            public List<DeliveryHouse> Houses;
            public DeliveryRoadNetwork Network;
            public DeliveryTrafficController Traffic;
            public DeliveryDirector Director;
            public GiftDeliveryDirector GiftDirector;
            public SnowStage SnowStage;
            public VehicleRespawnPoint PlayerSpawn;
            public int RiverRespawnVolumeCount;
            public int FlushedCurbColliderCount;
            public int CurbRampCount;
        }

        /// <summary>
        /// 맵 씬을 대상 경로로 복사해서 연다. 프리팹 연결·라이팅·머티리얼이 전부 살아 있는
        /// 사본을 얻는 가장 안전한 방법이다. Build Settings가 대상 GUID를 참조한다면
        /// <paramref name="preserveDestinationGuid"/>를 사용해 기존 meta를 유지한다.
        /// </summary>
        public static Scene CopyMapSceneAndOpen(string destinationScenePath,
            bool preserveDestinationGuid = false)
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MapScenePath) == null)
                throw new InvalidOperationException($"맵 씬을 찾을 수 없다: {MapScenePath}");

            bool destinationExists =
                AssetDatabase.LoadAssetAtPath<SceneAsset>(destinationScenePath) != null;
            if (destinationExists && preserveDestinationGuid)
            {
                Scene sourceScene = EditorSceneManager.OpenScene(MapScenePath, OpenSceneMode.Single);
                if (!EditorSceneManager.SaveScene(sourceScene, destinationScenePath, true))
                    throw new InvalidOperationException(
                        $"GUID 유지 씬 복사 실패: {MapScenePath} -> {destinationScenePath}");
            }
            else
            {
                if (destinationExists && !AssetDatabase.DeleteAsset(destinationScenePath))
                    throw new InvalidOperationException($"기존 씬을 지우지 못했다: {destinationScenePath}");

                if (!AssetDatabase.CopyAsset(MapScenePath, destinationScenePath))
                    throw new InvalidOperationException(
                        $"맵 씬 복사 실패: {MapScenePath} -> {destinationScenePath}");
            }

            AssetDatabase.Refresh();
            Scene scene = EditorSceneManager.OpenScene(destinationScenePath, OpenSceneMode.Single);

            // ConceptMap은 현재 펭귄이 플레이 주체라 기존 차량과 차량 카메라가 비활성 상태다.
            // 파생 씬에서는 호출자가 차량 또는 새 펭귄을 다시 구성하므로, 원본 펭귄을 먼저 제거하고
            // 맵에 이미 배선된 차량 리그를 후보로 복원한다. BuildPrefabPlayer 경로에서는 곧바로
            // 이 차량 리그를 제거하고 요청된 펭귄 프리팹을 하나만 만든다.
            foreach (PenguinLocomotion penguin in UnityEngine.Object.FindObjectsByType<PenguinLocomotion>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(penguin.transform.root.gameObject);

            foreach (VehicleController vehicle in UnityEngine.Object.FindObjectsByType<VehicleController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                vehicle.gameObject.SetActive(true);

            foreach (VehicleCamera vehicleCamera in UnityEngine.Object.FindObjectsByType<VehicleCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                vehicleCamera.gameObject.SetActive(true);

            // 맵이 이미 VehicleCamera 로 배선된 카메라를 갖고 있으면 그대로 쓴다. 그 배선이 없는
            // 낡은 맵이라면 예전처럼 탑다운 카메라를 치운다 — BuildPlayer가 그 경우에만 만든다.
            foreach (GameObject rootObject in scene.GetRootGameObjects())
            {
                if (rootObject.GetComponent<Camera>() == null) continue;
                if (rootObject.GetComponent<VehicleCamera>() != null) continue;
                UnityEngine.Object.DestroyImmediate(rootObject);
            }
            return scene;
        }

        /// <summary>
        /// 맵이 소유한 <see cref="SnowStage"/> 를 그대로 쓴다(2026-08-16 맵 쪽에서 통합됨 — 지면 8구간 +
        /// 다리 2개를 덮는 <see cref="SnowPanelBuilder"/> 10장과 함께). Delivery 가 따로 만들지 않는 이유는
        /// 같은 씬에 <see cref="SnowSurfaceRenderer"/> 가 둘이면 <c>_SnowField*</c> 셰이더 전역을 서로
        /// 덮어써서 한쪽 격자로 다른 쪽 패널이 그려지기 때문이다(Snow/AGENTS.md 의 "해상도 문제처럼
        /// 보이는" 실패 모드와 근본 원인이 같다).
        /// </summary>
        public static SnowStage FindMapSnowStage()
        {
            SnowStage stage = UnityEngine.Object.FindAnyObjectByType<SnowStage>();
            if (stage == null)
                throw new InvalidOperationException(
                    "맵에 SnowStage 가 없다 — WinterVillage_ConceptMap 이 아직 눈을 통합하지 않은 옛 버전인지 확인할 것");
            return stage;
        }

        /// <summary>
        /// 도로망·트럭 디렉터·플레이어 차량·리스폰 리그를 <paramref name="parent"/> 밑에 얹는다.
        /// 노드/구간/공장 생성, 갓돌 콜라이더 보정, 맵 커브 램프 재사용(옛 맵만 생성 폴백),
        /// 차량·카메라 재사용/폴백, 강 리스폰까지 전부 포함한다. 호출부는 씬 저장·로그만 책임진다.
        /// </summary>
        public static RigResult BuildRig(Transform parent, DeliveryTruck truckPrefab, in DirectorTuning tuning,
            Vector3 playerStart, Vector3 playerStartEuler, GameObject playerPrefab = null,
            bool includeLegacyTruckDelivery = true)
        {
            Dictionary<string, DeliveryRoadNode> nodes = BuildNodes(parent);
            List<DeliveryRoadSegment> segments = BuildSegments(parent, nodes);
            List<DeliveryFactory> factories = BuildFactories(parent, nodes, out List<DeliveryHouse> houses);

            var networkObject = new GameObject("RoadNetwork");
            networkObject.transform.SetParent(parent);
            DeliveryRoadNetwork network = networkObject.AddComponent<DeliveryRoadNetwork>();
            network.Configure(new List<DeliveryRoadNode>(nodes.Values), segments, factories);
            if (!network.TryValidate(out string error)) throw new InvalidOperationException(error);

            SnowStage snowStage = null;
            DeliveryTrafficController traffic = null;
            DeliveryDirector director = null;
            if (includeLegacyTruckDelivery)
            {
                snowStage = FindMapSnowStage();
                traffic = new GameObject("DeliveryTrafficController").AddComponent<DeliveryTrafficController>();
                traffic.transform.SetParent(parent);
                traffic.Configure(network);

                // 테스트 씬은 과거 트럭 회귀 확인을 위해 비활성 리그를 남긴다.
                director = new GameObject("DeliveryDirector").AddComponent<DeliveryDirector>();
                director.transform.SetParent(parent);
                director.Configure(network, truckPrefab, snowStage, traffic);
                SetSerialized(director, "_requestIntervalSeconds", tuning.RequestIntervalSeconds);
                SetSerialized(director, "_snowCancelSeconds", tuning.SnowCancelSeconds);
                SetSerialized(director, "_pointsPerMeter", tuning.PointsPerMeter);
                SetSerialized(director, "_maxConcurrentTrucks", 0);
                director.enabled = false;
            }

            VehicleRespawnPoint playerSpawn = BuildPlayer(parent, playerStart, playerStartEuler, playerPrefab);
            int riverRespawnVolumes = BuildRiverRespawnVolumes(parent, playerSpawn);
            int flushed = FlushCurbCollidersToRoadTop();
            int ramps = ReuseMapCurbRampsOrBuild(parent);

            GiftDeliveryDirector giftDirector = new GameObject("GiftDeliveryDirector").AddComponent<GiftDeliveryDirector>();
            giftDirector.transform.SetParent(parent);
            giftDirector.Configure(network, houses);
            giftDirector.SetParticipants(new[] { playerSpawn.Player });
            // 싱글플레이 주문은 한 번에 하나만 연다. 완료 뒤 3초 쉬고 다음 집의 HELP 예고가 시작된다.
            // HUD가 네 칸을 지원하는 것과 실제 동시 주문 수는 별개다.
            giftDirector.SetOrdersPerParticipant(1);
            SetSerialized(giftDirector, "_orderStaggerSeconds", 3f);
            SetSerialized(giftDirector, "_announcementSeconds", 1.4f);

            // 표시는 이제 디렉터가 아니라 집 신호를 읽는다. 어느 주문 모델이 신호를 채우는지는
            // Presenter가 정하므로, 싱글플레이는 여기서 기존 배달 디렉터 쪽을 붙인다.
            StageHouseSignals houseSignals = BuildHouseSignalDisplays(parent);
            houseSignals.gameObject.AddComponent<GiftDeliveryHouseSignalPresenter>()
                .Configure(giftDirector, houseSignals);

            BuildGiftSpawner(parent, playerStart);

            return new RigResult
            {
                Nodes = nodes,
                Segments = segments,
                Factories = factories,
                Houses = houses,
                Network = network,
                Traffic = traffic,
                Director = director,
                GiftDirector = giftDirector,
                SnowStage = snowStage,
                PlayerSpawn = playerSpawn,
                RiverRespawnVolumeCount = riverRespawnVolumes,
                FlushedCurbColliderCount = flushed,
                CurbRampCount = ramps
            };
        }

        /// <summary>
        /// 갓돌(<c>Curb_NN</c>) 콜라이더 윗면을 같은 구간 도로 슬래브(<c>Road_NN</c>) 윗면까지 올린다.
        ///
        /// 맵의 도로는 폭 4.5m 슬래브(윗면 0.315m)가 폭 5.5m 갓돌(윗면 0.230m) 위에 얹힌 구조라
        /// 도로 가장자리에 <b>8.5cm 수직 단차</b>가 생긴다. 차량은 바퀴 없는 BoxCollider 하나이고
        /// 서스펜션 힘도 없어서, 평평한 바닥면이 이 수직면에 닿으면 올라탈 성분이 없어 그대로 잼이 걸린다.
        ///
        /// <b>렌더러는 건드리지 않는다.</b> 갓돌은 단위 BoxCollider 를 트랜스폼 스케일로 늘린 구조라
        /// 트랜스폼을 만지면 메시까지 같이 늘어난다. 그래서 콜라이더의 <c>center</c>/<c>size</c> 만 고친다 —
        /// 보이는 턱은 그대로 있고 충돌만 도로와 같은 높이로 평평해진다.
        ///
        /// 값을 상수로 굳히지 않고 짝이 되는 도로에서 매번 재는 이유는, 맵이 바뀌면 이 보정도 따라가야 하기 때문이다.
        /// </summary>
        /// <returns>보정한 갓돌 수.</returns>
        private static int FlushCurbCollidersToRoadTop()
        {
            var routes = GameObject.Find("WinterVillageMap/Geometry/Routes");
            if (routes == null) throw new InvalidOperationException("맵에서 Routes 를 찾지 못했다");

            int flushed = 0;
            foreach (Transform group in routes.transform)
            {
                foreach (Transform curb in group)
                {
                    if (!curb.name.StartsWith("Curb_", StringComparison.Ordinal)) continue;

                    Transform road = group.Find("Road_" + curb.name.Substring("Curb_".Length));
                    if (road == null) continue;
                    if (curb.GetComponent<BoxCollider>() is not BoxCollider curbBox) continue;
                    if (road.GetComponent<BoxCollider>() is not BoxCollider roadBox) continue;

                    float roadTop = WorldTop(road, roadBox);
                    float curbBottom = WorldBottom(curb, curbBox);
                    float scaleY = curb.lossyScale.y;
                    if (roadTop <= curbBottom || Mathf.Abs(scaleY) < 1e-6f) continue;

                    // 아랫면은 그대로 두고 윗면만 도로까지 끌어올린다.
                    float worldHeight = roadTop - curbBottom;
                    float worldCenter = (curbBottom + roadTop) * 0.5f;
                    curbBox.size = new Vector3(curbBox.size.x, worldHeight / scaleY, curbBox.size.z);
                    curbBox.center = new Vector3(curbBox.center.x,
                                                 (worldCenter - curb.position.y) / scaleY,
                                                 curbBox.center.z);
                    flushed++;
                }
            }
            return flushed;
        }

        /// <summary>도로 갓길에서 지면까지 내려가는 경사 길이(수평). 0.315m 를 내려오므로 14.7° 가 된다.</summary>
        private const float CurbRampRun = 1.2f;

        private const float CurbRampThickness = 0.4f;

        /// <summary>
        /// 현행 WinterVillage 맵이 소유한 커브 램프를 재사용한다. 파생 씬에서 같은 위치에
        /// <c>CurbRamps</c>를 한 벌 더 만들면 Rigidbody가 겹친 접촉면에서 걸릴 수 있다.
        /// 오래된 맵 사본에 아직 공용 램프가 없을 때만 기존 생성 경로로 폴백한다.
        /// </summary>
        private static int ReuseMapCurbRampsOrBuild(Transform parent)
        {
            GameObject mapRamps = GameObject.Find("WinterVillageMap/Geometry/Routes/VehicleCurbRamps");
            if (mapRamps == null) return BuildCurbRamps(parent);

            int count = mapRamps.GetComponentsInChildren<BoxCollider>(true).Length;
            if (count == 0)
                throw new InvalidOperationException("VehicleCurbRamps 그룹에 경사 콜라이더가 없다");
            return count;
        }

        /// <summary>
        /// 도로 양 옆에 <b>보이지 않는 경사 콜라이더</b>를 깔아 지면과 잇는다.
        ///
        /// <see cref="FlushCurbCollidersToRoadTop"/> 이 도로 <i>안쪽</i> 단차를 없앴지만, 도로 갓길
        /// (0.315m)과 지면(0) 사이 <b>31.5cm 수직면</b>은 그대로 남는다. 바퀴도 서스펜션도 없는
        /// BoxCollider 한 장이라 그 면에서는 올라탈 성분이 없어, 도로를 벗어나면 다시 못 올라온다.
        ///
        /// 경사면은 갓돌 바깥 모서리에서 시작해 <see cref="CurbRampRun"/> 만큼 나가며 지면에 닿는다.
        /// 렌더러가 없으므로 <b>보이는 지형은 전혀 바뀌지 않는다</b> — 눈밭으로 나갔다 들어오는 길만 생긴다.
        ///
        /// 갓돌의 자식으로 두지 않는 이유는 갓돌 트랜스폼의 스케일이 비균등(5.5, 0.22, L)이라
        /// 회전시킨 자식이 전단(shear)되기 때문이다. 스케일 1 인 별도 그룹 밑에 월드 좌표로 놓는다.
        /// </summary>
        /// <returns>만든 경사면 수.</returns>
        private static int BuildCurbRamps(Transform parent)
        {
            var routes = GameObject.Find("WinterVillageMap/Geometry/Routes");
            if (routes == null) throw new InvalidOperationException("맵에서 Routes 를 찾지 못했다");

            var group = new GameObject("CurbRamps");
            group.transform.SetParent(parent);

            int made = 0;
            foreach (Transform roadGroup in routes.transform)
            {
                foreach (Transform curb in roadGroup)
                {
                    if (!curb.name.StartsWith("Curb_", StringComparison.Ordinal)) continue;
                    if (curb.GetComponent<BoxCollider>() is not BoxCollider box) continue;

                    float rise = WorldTop(curb, box);
                    if (rise <= 0.01f) continue;

                    float halfWidth = Mathf.Abs(curb.lossyScale.x * box.size.x) * 0.5f;
                    float length = Mathf.Abs(curb.lossyScale.z * box.size.z);
                    float slopeLength = Mathf.Sqrt(CurbRampRun * CurbRampRun + rise * rise);
                    float angle = Mathf.Atan2(rise, CurbRampRun) * Mathf.Rad2Deg;
                    var flatCenter = new Vector3(curb.position.x, 0f, curb.position.z);

                    for (int side = -1; side <= 1; side += 2)
                    {
                        // 부호에 주의 — 갓돌 쪽이 높고 바깥이 낮아야 한다. 뒤집으면 도로 옆에 도랑이 생긴다.
                        Quaternion rotation = curb.rotation * Quaternion.AngleAxis(-side * angle, Vector3.forward);

                        // 경사면 윗면의 중점. 높은 끝이 갓돌 모서리(높이 rise), 낮은 끝이 지면(높이 0)에 닿는다.
                        Vector3 topFaceCenter = flatCenter
                                                + curb.right * (side * (halfWidth + CurbRampRun * 0.5f))
                                                + Vector3.up * (rise * 0.5f);

                        var ramp = new GameObject($"Ramp_{roadGroup.name}_{curb.name}_{(side < 0 ? "L" : "R")}");
                        ramp.transform.SetParent(group.transform);
                        ramp.transform.SetPositionAndRotation(
                            topFaceCenter - (rotation * Vector3.up) * (CurbRampThickness * 0.5f), rotation);
                        BoxCollider rampBox = ramp.AddComponent<BoxCollider>();
                        rampBox.size = new Vector3(slopeLength, CurbRampThickness, length);
                        made++;
                    }
                }
            }
            return made;
        }

        private static float WorldTop(Transform transform, BoxCollider box)
            => transform.position.y + (box.center.y + box.size.y * 0.5f) * transform.lossyScale.y;

        private static float WorldBottom(Transform transform, BoxCollider box)
            => transform.position.y + (box.center.y - box.size.y * 0.5f) * transform.lossyScale.y;

        private static Dictionary<string, DeliveryRoadNode> BuildNodes(Transform parent)
        {
            var group = new GameObject("Nodes");
            group.transform.SetParent(parent);

            var nodes = new Dictionary<string, DeliveryRoadNode>(NodeSpecs.Length, StringComparer.Ordinal);
            foreach ((string id, Vector3 position) in NodeSpecs)
            {
                var gameObject = new GameObject("Node_" + id);
                gameObject.transform.SetParent(group.transform);
                gameObject.transform.position = position;
                DeliveryRoadNode node = gameObject.AddComponent<DeliveryRoadNode>();
                node.Configure(id);
                nodes.Add(id, node);
            }
            return nodes;
        }

        private static List<DeliveryRoadSegment> BuildSegments(
            Transform parent, Dictionary<string, DeliveryRoadNode> nodes)
        {
            var group = new GameObject("Segments");
            group.transform.SetParent(parent);

            var segments = new List<DeliveryRoadSegment>(RoadSpecs.Length);
            foreach (RoadSpec spec in RoadSpecs)
            {
                var gameObject = new GameObject(spec.Name);
                gameObject.transform.SetParent(group.transform);
                DeliveryRoadSegment segment = gameObject.AddComponent<DeliveryRoadSegment>();
                bool isSidewalk = spec.Name.StartsWith("Road_Prom", StringComparison.Ordinal);
                segment.Configure(nodes[spec.Start], nodes[spec.End], spec.Controls, spec.Width, 2.5f, 0.25f,
                    isSidewalk);
                segments.Add(segment);
            }
            return segments;
        }

        private static List<DeliveryFactory> BuildFactories(
            Transform parent, Dictionary<string, DeliveryRoadNode> nodes, out List<DeliveryHouse> houses)
        {
            var group = new GameObject("Factories");
            group.transform.SetParent(parent);

            var houseTransforms = FindHouseTransforms();
            var factories = new List<DeliveryFactory>(HouseSpecs.Length);
            houses = new List<DeliveryHouse>(HouseSpecs.Length);
            foreach (HouseSpec spec in HouseSpecs)
            {
                if (!houseTransforms.TryGetValue(spec.HouseName, out Transform house))
                    throw new InvalidOperationException($"맵에서 집을 찾지 못했다: {spec.HouseName}");

                // 맵 프리팹은 건드리지 않는다 — 배송 의미의 오브젝트는 이 기능이 따로 소유한다.
                var gameObject = new GameObject("Factory_" + spec.HouseName);
                gameObject.transform.SetParent(group.transform);
                gameObject.transform.position = house.position;

                var stop = new GameObject("TruckStop");
                stop.transform.SetParent(gameObject.transform);
                stop.transform.position = new Vector3(spec.StopPosition.x, RoadSurfaceY, spec.StopPosition.z);

                DeliveryFactory factory = gameObject.AddComponent<DeliveryFactory>();
                factory.Configure(nodes[spec.NodeId], stop.transform);
                factories.Add(factory);

                // 선물 배치 범위는 트럭 정차점(진입로가 도로와 만나는 자리)에 둔다 — 현관까지 들어가는
                // 별도 지점 데이터가 없고, 이 자리는 이미 도로에서 걸어(눈덩이를 굴려) 닿는 자리다.
                var zoneObject = new GameObject("GiftZone");
                zoneObject.transform.SetParent(gameObject.transform);
                zoneObject.transform.position = stop.transform.position;
                GiftDropZone zone = zoneObject.AddComponent<GiftDropZone>();
                SetSerialized(zone, "_size", GiftZoneSize);
                SetSerialized(zone, "_capacity", GiftZoneCapacity);

                DeliveryHouse deliveryHouse = gameObject.AddComponent<DeliveryHouse>();
                deliveryHouse.Configure(nodes[spec.NodeId], stop.transform, zone);
                houses.Add(deliveryHouse);
            }
            return factories;
        }

        private static Dictionary<string, Transform> FindHouseTransforms()
        {
            var result = new Dictionary<string, Transform>(StringComparer.Ordinal);
            foreach (GameObject rootObject in SceneManager.GetActiveScene().GetRootGameObjects())
            {
                foreach (Transform child in rootObject.GetComponentsInChildren<Transform>(true))
                {
                    if (child.name.StartsWith("VillageHouse_", StringComparison.Ordinal))
                        result[child.name] = child;
                }
            }
            return result;
        }

        /// <summary>
        /// 맵이 이미 심어 둔 <c>PlayerVehicle</c>(2026-08-16 통합, <c>SnowVehiclePad</c>·
        /// <c>SnowVehicleDrag</c> 가 맵의 <see cref="SnowStage"/> 에 배선된 채로 옴)을 지정된
        /// 시작 지점으로 옮긴다. 새로 심지 않는 이유는 씬에 차량이 둘이 되는 것을 피하기 위해서다 —
        /// <see cref="PrefabUtility.InstantiatePrefab(UnityEngine.Object)"/> 로 새로 심는 대신
        /// 이미 배선된 인스턴스를 재배치하면 그 배선을 그대로 물려받는다.
        ///
        /// 카메라도 마찬가지다. <see cref="CopyMapSceneAndOpen"/> 이 <see cref="VehicleCamera"/> 가
        /// 없는 카메라만 치우므로, 맵의 <c>Main Camera</c>(이미 이 차량에 배선됨)가 여기 그대로 있다.
        /// 맵이 아직 차량·카메라를 통합하지 않은 옛 버전이면 직접 만든다 — 하위 호환이다.
        /// </summary>
        private static VehicleRespawnPoint BuildPlayer(Transform parent, Vector3 playerStart,
            Vector3 playerStartEuler, GameObject playerPrefab)
        {
            if (playerPrefab != null) return BuildPrefabPlayer(parent, playerStart, playerStartEuler, playerPrefab);

            VehicleController controller = UnityEngine.Object.FindAnyObjectByType<VehicleController>();
            Transform vehicle = controller != null ? controller.transform : BuildFallbackVehicle(parent);

            // 맵이 소유한 공용 스폰을 재사용한다. 낡은 맵에만 호환용 DeliveryPlayerSpawn을 만든다.
            VehicleRespawnPoint playerSpawn = UnityEngine.Object.FindAnyObjectByType<VehicleRespawnPoint>();
            if (playerSpawn == null)
            {
                var spawnObject = new GameObject("PlayerSpawn");
                spawnObject.transform.SetParent(parent);
                playerSpawn = spawnObject.AddComponent<DeliveryPlayerSpawn>();
            }
            playerSpawn.transform.SetPositionAndRotation(playerStart, Quaternion.Euler(playerStartEuler));
            playerSpawn.Configure(vehicle);
            vehicle.SetPositionAndRotation(playerStart, Quaternion.Euler(playerStartEuler));

            VehicleCamera vehicleCamera = UnityEngine.Object.FindAnyObjectByType<VehicleCamera>();
            if (vehicleCamera == null) BuildFallbackCamera(parent, vehicle, controller);
            return playerSpawn;
        }

        private static VehicleRespawnPoint BuildPrefabPlayer(Transform parent, Vector3 playerStart,
            Vector3 playerStartEuler, GameObject playerPrefab)
        {
            foreach (VehicleRouteMinimapController minimap in
                     UnityEngine.Object.FindObjectsByType<VehicleRouteMinimapController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(minimap.gameObject);

            foreach (VehicleCamera camera in UnityEngine.Object.FindObjectsByType<VehicleCamera>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(camera.gameObject);

            foreach (VehicleController vehicle in UnityEngine.Object.FindObjectsByType<VehicleController>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                UnityEngine.Object.DestroyImmediate(vehicle.gameObject);

            var player = (GameObject)PrefabUtility.InstantiatePrefab(playerPrefab);
            player.name = "Penguin";
            player.transform.SetParent(parent);
            player.transform.SetPositionAndRotation(playerStart, Quaternion.Euler(playerStartEuler));

            VehicleRespawnPoint playerSpawn = UnityEngine.Object.FindAnyObjectByType<VehicleRespawnPoint>();
            if (playerSpawn == null)
            {
                var spawnObject = new GameObject("PlayerSpawn");
                spawnObject.transform.SetParent(parent);
                playerSpawn = spawnObject.AddComponent<DeliveryPlayerSpawn>();
            }
            playerSpawn.transform.SetPositionAndRotation(playerStart, Quaternion.Euler(playerStartEuler));
            playerSpawn.Configure(player.transform);
            return playerSpawn;
        }

        /// <summary>
        /// 눈덩이 없이도 배달 루프를 눈으로 확인하기 위한 테스트용 선물 스포너. 눈덩이 담당자의
        /// 선물 공급 설계가 나오면 버려도 되는 임시 리그다(Delivery/AGENTS.md).
        /// </summary>
        private static void BuildGiftSpawner(Transform parent, Vector3 playerStart)
        {
            var template = GameObject.CreatePrimitive(PrimitiveType.Cube);
            template.name = "GiftTemplate";
            template.transform.SetParent(parent);
            template.transform.localScale = Vector3.one * 0.4f;
            template.AddComponent<Gift>();
            template.SetActive(false);

            var spawnerObject = new GameObject("GiftSpawner");
            spawnerObject.transform.SetParent(parent);
            spawnerObject.transform.position = playerStart;
            GiftSpawner spawner = spawnerObject.AddComponent<GiftSpawner>();
            SetSerialized(spawner, "_giftPrefab", template.GetComponent<Gift>());
        }

        private static int BuildRiverRespawnVolumes(Transform parent, VehicleRespawnPoint playerSpawn)
        {
            VehicleRespawnVolume[] mapVolumes = UnityEngine.Object.FindObjectsByType<VehicleRespawnVolume>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            if (mapVolumes.Length > 0)
            {
                foreach (VehicleRespawnVolume volume in mapVolumes) volume.Configure(playerSpawn);
                return mapVolumes.Length;
            }

            const float triggerDepth = 4f;
            const float triggerTopBelowWater = 0.05f;

            GameObject river = GameObject.Find("River");
            if (river == null) throw new InvalidOperationException("WinterVillage River 오브젝트가 없다");

            var group = new GameObject("RiverRespawnVolumes");
            group.transform.SetParent(parent);

            int count = 0;
            foreach (Renderer water in river.GetComponentsInChildren<Renderer>(true))
            {
                if (!water.name.StartsWith("Water_", StringComparison.Ordinal)) continue;

                Bounds bounds = water.bounds;
                var volume = new GameObject($"Respawn_{water.name}");
                volume.transform.SetParent(group.transform);
                volume.transform.position = new Vector3(
                    bounds.center.x,
                    bounds.max.y - triggerTopBelowWater - triggerDepth * 0.5f,
                    bounds.center.z);

                BoxCollider collider = volume.AddComponent<BoxCollider>();
                collider.isTrigger = true;
                collider.size = new Vector3(bounds.size.x, triggerDepth, bounds.size.z);
                volume.AddComponent<DeliveryRiverRespawnVolume>().Configure(playerSpawn);
                count++;
            }

            if (count == 0) throw new InvalidOperationException("River 아래 Water_* 렌더러가 없다");
            return count;
        }

        /// <summary>맵에 아직 차량이 없을 때만 쓰는 경로 — 프리팹을 직접 심는다.</summary>
        private static Transform BuildFallbackVehicle(Transform parent)
        {
            const string vehiclePrefabPath = "Assets/Game/InGame/Vehicle/Prefabs/PF_VehicleProto.prefab";
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(vehiclePrefabPath);
            if (prefab == null) throw new InvalidOperationException($"차량 프리팹이 없다: {vehiclePrefabPath}");

            // Object.Instantiate 로 심으면 프리팹 연결이 끊긴다 (Vehicle/AGENTS.md).
            var vehicle = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            vehicle.name = "PlayerVehicle";
            vehicle.transform.SetParent(parent);
            return vehicle.transform;
        }

        /// <summary>맵에 아직 차량 카메라가 없을 때만 쓰는 경로.</summary>
        private static void BuildFallbackCamera(Transform parent, Transform vehicle, VehicleController controller)
        {
            var cameraObject = new GameObject("ChaseCamera");
            cameraObject.transform.SetParent(parent);
            var camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            cameraObject.AddComponent<AudioListener>();

            // URP 에서는 이걸 켜야 씬의 Volume 이 실제로 렌더된다 (Vehicle/AGENTS.md).
            var cameraData = cameraObject.AddComponent<UniversalAdditionalCameraData>();
            cameraData.renderPostProcessing = true;

            VehicleCamera vehicleCamera = cameraObject.AddComponent<VehicleCamera>();
            SetSerialized(vehicleCamera, "_vehicle", vehicle);
            SetSerialized(vehicleCamera, "_controller", controller);
        }

        public static DeliveryTruck BuildTruckPrefab()
        {
            EnsureFolder(Root, "Prefabs");
            EnsureFolder(Root, "Materials");

            var root = new GameObject("PF_DeliveryTruck");
            DeliveryTruck truck = root.AddComponent<DeliveryTruck>();
            SetSerialized(truck, "_snowLookAheadMeters", 4f);

            // 도로 곡선은 y=0 에서 평가되므로(DeliveryRoadCurve.Flatten) 차체만 도로 윗면 위로 올린다.
            // 높이 오프셋은 BodyPivot 이 들고 Body 는 로컬 원점에 둔다 — 피치·롤은 BodyPivot 의
            // 회전 채널, 스쿼시는 Body 의 스케일 채널이라 둘이 안 부딪힌다.
            var bodyPivot = new GameObject("BodyPivot");
            bodyPivot.transform.SetParent(root.transform, false);
            bodyPivot.transform.localPosition = new Vector3(0f, RoadSurfaceY + TruckBodyHeight * 0.5f, 0f);
            bodyPivot.AddComponent<DeliveryTruckBodyMotion>();

            var body = GameObject.CreatePrimitive(PrimitiveType.Cube);
            body.name = "Body";
            body.transform.SetParent(bodyPivot.transform, false);
            body.transform.localScale = new Vector3(2f, TruckBodyHeight, 4f);
            SetColor(body, new Color(0.9f, 0.12f, 0.12f));

            var cab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cab.name = "Cab";
            cab.transform.SetParent(body.transform, false);
            cab.transform.localPosition = new Vector3(0f, 0.65f, 0.28f);
            cab.transform.localScale = new Vector3(0.9f, 0.65f, 0.42f);
            SetColor(cab, new Color(0.95f, 0.95f, 1f));

            BuildBodyFeedback(body);
            BuildWheels(root, truck);

            // 트럭은 공장 노드에 흩어져 스폰되고(플레이어 시작점에서 최대 70m대), 맵이 야간+안개
            // 조명이라 차체만으로는 위치를 찾기 어렵다(2026-08-16 실측). 월드 Y축으로 계속 도는
            // 얇은 반투명 삼각 판 표지를 머리 위에 띄운다 — UI/DeliveryTruckBeacon, Delivery 상태는 읽기만 한다.
            var beacon = new GameObject("Beacon");
            beacon.transform.SetParent(root.transform, false);
            DeliveryTruckBeacon beaconMarker = beacon.AddComponent<DeliveryTruckBeacon>();
            beaconMarker.Configure(root.transform);

            // routeDisplay 와 같은 이유로 여기서 찍는다 — 직렬화된 값이 C# 기본값을 이기므로,
            // 안 찍으면 프리팹에 굳어 있던 예전 값(두께 0.3 = 크기의 43%, 통통한 피크)이 그대로 남는다.
            SetSerialized(beaconMarker, "_thickness", 0.09f);
            SetSerialized(beaconMarker, "_cornerRadius", 0.2f);
            SetSerialized(beaconMarker, "_profileSegments", 6);

            // 표지가 "저기 있다" 만 알려주고 "어디로 가는지" 는 안 알려줘서, 현재 트럭 위치부터
            // 목적지까지 계속 흐르는 경로 화살표와 도착지 표시를 더한다.
            DeliveryRouteDisplay routeDisplay = root.AddComponent<DeliveryRouteDisplay>();

            // 직렬화된 값이 C# 기본값(0.5/6/6)을 이긴다 — 이 프리팹은 이 빌더가 매번 처음부터
            // 새로 만들므로, 여기서 안 찍으면 재빌드할 때마다 조용히 기본값으로 되돌아간다
            // (실제로 그랬다 — main 이 튜닝한 값을 이 함수가 반영하지 않고 있었다. 2026-08-18).
            SetSerialized(routeDisplay, "_ribbonSampleSpacing", 0.4f);
            SetSerialized(routeDisplay, "_edgeSegments", 8);
            SetSerialized(routeDisplay, "_flowSpeed", 14f);

            GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            UnityEngine.Object.DestroyImmediate(root);
            return prefab.GetComponent<DeliveryTruck>();
        }

        /// <summary>
        /// 출발·급제동 스쿼시. 플레이어 하나에 스쿼시 하나만 둔다 — 여럿이면 같은 스케일 채널을
        /// 서로 덮어쓴다. 세기는 <c>DeliveryTruckFeedbacks</c> 가 리맵 값으로 바꿔 가며 쓴다.
        /// </summary>
        private static void BuildBodyFeedback(GameObject body)
        {
            MMF_Player player = body.AddComponent<MMF_Player>();
            player.AddFeedback(new MMF_SquashAndStretch
            {
                SquashAndStretchTarget = body.transform,
                Axis = MMF_SquashAndStretch.PossibleAxis.YtoXZ,
                AnimateScaleDuration = 0.25f,
                RemapCurveOne = 1.2f
            });
            body.AddComponent<DeliveryTruckFeedbacks>();
        }

        /// <summary>집 신호를 그리는 표시 묶음(바닥 표식·지붕색·HELP 말풍선)을 만들고 신호원을 돌려준다.
        ///
        /// <para>신호를 채우는 Presenter는 호출자가 붙인다. 그 한 줄이 싱글플레이와 새 의뢰 흐름의
        /// 유일한 차이이고, Feel 등장·유휴 연출을 포함한 나머지는 두 쪽이 같은 것을 쓴다.</para></summary>
        public static StageHouseSignals BuildHouseSignalDisplays(Transform parent)
        {
            var houseBeaconObject = new GameObject("GiftDeliveryHouseBeaconDisplay");
            houseBeaconObject.transform.SetParent(parent);

            StageHouseSignals signals = houseBeaconObject.AddComponent<StageHouseSignals>();
            houseBeaconObject.AddComponent<GiftDeliveryHouseBeaconDisplay>().Configure(signals);
            houseBeaconObject.AddComponent<GiftDeliveryHouseRoofDisplay>().Configure(signals);

            Font helpFont = AssetDatabase.LoadAssetAtPath<Font>(HelpFontPath);
            BuildHouseHelpFeelFeedbacks(
                houseBeaconObject.transform,
                out MMF_Player helpEntranceFeedback,
                out MMF_Player helpIdleFeedback);
            houseBeaconObject.AddComponent<GiftDeliveryHouseHelpDisplay>().Configure(
                signals,
                helpFont,
                helpEntranceFeedback,
                helpIdleFeedback);

            return signals;
        }

        /// <summary>
        /// HELP 월드 UI가 런타임에 Feel 컴포넌트를 AddComponent 하면 MMF_Player.Awake가 빈 목록보다
        /// 먼저 실행되는 Feel 버전 특성에 걸린다. 그래서 플레이 전 씬 조립 단계에서 플레이어와
        /// 피드백 목록을 직렬화해 두고, 런타임에는 생성된 카드 Transform만 목표로 다시 연결한다.
        /// </summary>
        private static void BuildHouseHelpFeelFeedbacks(
            Transform parent,
            out MMF_Player entranceFeedback,
            out MMF_Player idleFeedback)
        {
            var entranceObject = new GameObject("Feel_HelpSequentialEntrance");
            entranceObject.transform.SetParent(parent, false);
            entranceFeedback = entranceObject.AddComponent<MMF_Player>();
            entranceFeedback.RestoreInitialValuesOnDisable = false;
            entranceFeedback.AddFeedback(CreateHelpPopFeedback(0f, 0.8f, 1.07f));
            entranceFeedback.AddFeedback(CreateHelpPopFeedback(0.9f, 0.84f, 1.08f));
            entranceFeedback.AddFeedback(CreateHelpPopFeedback(1.8f, 0.88f, 1.1f));

            var idleObject = new GameObject("Feel_HelpIdlePulse");
            idleObject.transform.SetParent(parent, false);
            idleFeedback = idleObject.AddComponent<MMF_Player>();
            idleFeedback.RestoreInitialValuesOnDisable = false;
            // 첫 등장 Feel이 끝난 다음 세 카드의 지속 펄스를 시작해 두 스케일 애니메이션이 충돌하지 않게 한다.
            idleFeedback.AddFeedback(new MMF_LooperStart { PauseDuration = 2.8f });
            idleFeedback.AddFeedback(CreateHelpPulseFeedback(0f, 1.045f));
            idleFeedback.AddFeedback(CreateHelpPulseFeedback(0.9f, 1.055f));
            idleFeedback.AddFeedback(CreateHelpPulseFeedback(1.8f, 1.07f));
            idleFeedback.AddFeedback(new MMF_Pause { PauseDuration = 2.4f });
            idleFeedback.AddFeedback(new MMF_Looper
            {
                InfiniteLoop = true,
                PauseDuration = 0.02f,
                LoopAtLastPause = false,
                LoopAtLastLoopStart = true,
                TriggerMMFeedbacksEvents = false
            });
        }

        private static MMF_Scale CreateHelpPopFeedback(float delay, float duration, float overshoot)
        {
            return new MMF_Scale
            {
                Timing = new MMFeedbackTiming { InitialDelay = delay },
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = duration,
                RemapCurveZero = 0f,
                RemapCurveOne = 1f,
                UniformScaling = true,
                AnimateScaleTweenX = HelpPopTween(overshoot),
                AnimateScaleTweenY = HelpPopTween(overshoot),
                AnimateScaleTweenZ = HelpPopTween(overshoot),
                AllowAdditivePlays = false,
                DetermineScaleOnPlay = false
            };
        }

        private static MMF_Scale CreateHelpPulseFeedback(float delay, float peakScale)
        {
            return new MMF_Scale
            {
                Timing = new MMFeedbackTiming { InitialDelay = delay },
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = 1.05f,
                RemapCurveZero = 1f,
                RemapCurveOne = peakScale,
                UniformScaling = true,
                AnimateScaleTweenX = HelpPulseTween(),
                AnimateScaleTweenY = HelpPulseTween(),
                AnimateScaleTweenZ = HelpPulseTween(),
                AllowAdditivePlays = false,
                DetermineScaleOnPlay = false
            };
        }

        /// <summary>
        /// UI Toolkit은 MMF_Scale의 직접 대상이 아니므로 Feel이 중립 Transform을 움직이고,
        /// StageHUDController가 그 값을 새 티켓의 팝·회전·상승 연출로 전달한다.
        /// </summary>
        public static void BuildOrderAddedHudFeedback(Transform parent, StageHUDController hud)
        {
            if (hud == null) throw new ArgumentNullException(nameof(hud));

            var scaleDriverObject = new GameObject("Feel_OrderAddedScaleDriver");
            scaleDriverObject.transform.SetParent(parent, false);
            scaleDriverObject.transform.localScale = Vector3.one;

            var feedbackObject = new GameObject("Feel_OrderAdded");
            feedbackObject.transform.SetParent(parent, false);
            MMF_Player feedback = feedbackObject.AddComponent<MMF_Player>();
            feedback.RestoreInitialValuesOnDisable = false;
            feedback.AddFeedback(new MMF_Scale
            {
                AnimateScaleTarget = scaleDriverObject.transform,
                Mode = MMF_Scale.Modes.Absolute,
                AnimateScaleDuration = 0.72f,
                RemapCurveZero = 0.92f,
                RemapCurveOne = 1f,
                UniformScaling = true,
                AnimateScaleTweenX = OrderAddedTween(),
                AnimateScaleTweenY = OrderAddedTween(),
                AnimateScaleTweenZ = OrderAddedTween(),
                AllowAdditivePlays = false,
                DetermineScaleOnPlay = false
            });

            hud.ConfigureOrderAddedFeedback(scaleDriverObject.transform, feedback, 0.72f);
            EditorUtility.SetDirty(hud);
        }

        private static MMTweenType OrderAddedTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.52f, 1.08f),
                new Keyframe(0.78f, 0.98f),
                new Keyframe(1f, 1f)));
        }

        private static MMTweenType HelpPopTween(float overshoot)
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.62f, overshoot),
                new Keyframe(1f, 1f)));
        }

        private static MMTweenType HelpPulseTween()
        {
            return new MMTweenType(new AnimationCurve(
                new Keyframe(0f, 0f),
                new Keyframe(0.42f, 1f),
                new Keyframe(1f, 0f)));
        }

        /// <summary>
        /// 바퀴는 <c>BodyPivot</c> 이 아니라 루트에 단다. 피치·롤은 차체만 기울어야 하고 바퀴는
        /// 땅에 붙어 있어야 한다. 원통 축이 로컬 Y 라 좌우로 눕히는 것은 컴포넌트가 한다.
        /// </summary>
        private static void BuildWheels(GameObject root, DeliveryTruck truck)
        {
            var wheels = new GameObject("Wheels");
            wheels.transform.SetParent(root.transform, false);

            var front = new Transform[2];
            var rear = new Transform[2];
            for (int index = 0; index < 4; index++)
            {
                bool isFront = index < 2;
                float x = index % 2 == 0 ? -TruckHalfWidth : TruckHalfWidth;
                var wheel = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                wheel.name = $"Wheel_{(isFront ? "F" : "R")}{(index % 2 == 0 ? "L" : "R")}";
                wheel.transform.SetParent(wheels.transform, false);
                wheel.transform.localPosition = new Vector3(x, RoadSurfaceY + WheelRadius,
                                                            isFront ? WheelAxleZ : -WheelAxleZ);
                wheel.transform.localScale = new Vector3(WheelRadius * 2f, WheelWidth * 0.5f, WheelRadius * 2f);
                SetColor(wheel, new Color(0.12f, 0.12f, 0.14f));
                UnityEngine.Object.DestroyImmediate(wheel.GetComponent<Collider>());

                if (isFront) front[index] = wheel.transform;
                else rear[index - 2] = wheel.transform;
            }

            root.AddComponent<DeliveryTruckWheels>().Configure(truck, front, rear);
        }

        private static void EnsureFolder(string parent, string name)
        {
            string path = parent + "/" + name;
            if (!AssetDatabase.IsValidFolder(path)) AssetDatabase.CreateFolder(parent, name);
        }

        private static void SetColor(GameObject gameObject, Color color)
        {
            Renderer renderer = gameObject.GetComponent<Renderer>();
            if (renderer == null) return;
            string colorKey = ColorUtility.ToHtmlStringRGB(color);
            string materialPath = $"{MaterialsPath}/M_Delivery_{colorKey}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
            if (material == null)
            {
                material = new Material(Shader.Find("Universal Render Pipeline/Lit")) { color = color };
                AssetDatabase.CreateAsset(material, materialPath);
            }
            renderer.sharedMaterial = material;
        }

        private static void SetSerialized(UnityEngine.Object target, string propertyName, object value)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            if (property == null) throw new MissingFieldException(target.GetType().Name, propertyName);
            switch (value)
            {
                case float floatValue: property.floatValue = floatValue; break;
                case int intValue: property.intValue = intValue; break;
                case Vector2 vector2Value: property.vector2Value = vector2Value; break;
                case Vector3 vector3Value: property.vector3Value = vector3Value; break;
                case UnityEngine.Object objectValue: property.objectReferenceValue = objectValue; break;
                default: throw new NotSupportedException(value.GetType().Name);
            }
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }
    }
}
