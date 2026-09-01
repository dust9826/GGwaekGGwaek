using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// 배송 통합 테스트 씬을 WinterVillage 맵 위에 다시 찍어낸다.
    /// 리그 조립 자체는 <see cref="DeliverySceneRigBuilder"/>가 소유한다 — 이 클래스는 테스트 씬
    /// 경로·리그 루트 이름·플레이어 시작 위치처럼 이 씬만의 값과 씬 저장·로그만 책임진다.
    /// </summary>
    public static class DeliveryTestSceneBuilder
    {
        private const string ScenePath = "Assets/Game/InGame/Delivery/Tests/Delivery_RequestFlow_Test.unity";

        private const string StageHudUxmlPath = "Assets/Game/InGame/UI/StageHUD/StageHUD.uxml";
        private const string StageHudPanelSettingsPath = "Assets/Game/InGame/UI/StageHUD/StageHUDPanelSettings.asset";

        /// <summary>
        /// 플레이어 차량 출발 지점. Central 교차로에서 South 로 내려가는 도로 위, 진행 방향을 보고 선다.
        /// 씬에서 바꿀 때는 이 값이 아니라 <c>PlayerSpawn</c> 오브젝트를 옮긴다 — 빌드를 다시 하면 이 값으로 돌아온다.
        /// </summary>
        private static readonly Vector3 PlayerStart = new Vector3(-8f, DeliverySceneRigBuilder.RoadSurfaceY, -9f);

        /// <summary>Central -> South 방향(도로 진행 방향)을 바라보게 한다.</summary>
        private static readonly Vector3 PlayerStartEuler = new Vector3(0f, 123f, 0f);

        [MenuItem("PPack/Delivery/Build Request Flow Test Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            DeliveryTruck truckPrefab = DeliverySceneRigBuilder.BuildTruckPrefab();
            Scene scene = DeliverySceneRigBuilder.CopyMapSceneAndOpen(ScenePath);

            var rig = new GameObject("Delivery_RequestFlow_Test");

            // 의뢰 건수를 10으로 늘렸다(2026-08-18) — HUD 프로토타입에서 배송 활동이 더 잘 보이도록.
            var tuning = new DeliverySceneRigBuilder.DirectorTuning(
                requestIntervalSeconds: 10f, snowCancelSeconds: 45f, pointsPerMeter: 10f, maxConcurrentTrucks: 10);
            DeliverySceneRigBuilder.RigResult result = DeliverySceneRigBuilder.BuildRig(
                rig.transform, truckPrefab, tuning, PlayerStart, PlayerStartEuler);

            BuildHud(rig.transform, result.GiftDirector);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"씬 저장 실패: {ScenePath}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Delivery test scene built on WinterVillage map: {ScenePath} " +
                      $"(nodes={result.Nodes.Count}, segments={result.Segments.Count}, factories={result.Factories.Count}, " +
                      $"houses={result.Houses.Count}, " +
                      $"riverRespawnVolumes={result.RiverRespawnVolumeCount}, " +
                      $"curbColliders={result.FlushedCurbColliderCount}, curbRamps={result.CurbRampCount})");
        }

        /// <summary>현재 목표 집·주문 제한시간·요구 선물·완료 수를 표시하는 플레이어 배송 HUD.</summary>
        private static void BuildHud(Transform parent, GiftDeliveryDirector director)
        {
            var uiObject = new GameObject("StageHudUI");
            uiObject.transform.SetParent(parent);

            var document = uiObject.AddComponent<UnityEngine.UIElements.UIDocument>();
            document.panelSettings =
                AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.PanelSettings>(StageHudPanelSettingsPath);
            document.visualTreeAsset =
                AssetDatabase.LoadAssetAtPath<UnityEngine.UIElements.VisualTreeAsset>(StageHudUxmlPath);
            if (document.panelSettings == null || document.visualTreeAsset == null)
                throw new InvalidOperationException("StageHUD UXML/PanelSettings를 찾을 수 없다");

            StageHUDController hud = uiObject.AddComponent<StageHUDController>();
            DeliverySceneRigBuilder.BuildOrderAddedHudFeedback(uiObject.transform, hud);
            uiObject.AddComponent<GiftDeliveryHudPresenter>().Configure(hud, director);
        }
    }
}
