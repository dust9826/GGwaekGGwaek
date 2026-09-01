using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// 멀티플레이 게임플레이 씬을 <b>배달 미션 씬에서 찍어 낸다</b>.
    ///
    /// <para><b>왜 맵에서 다시 만들지 않나.</b> <c>SinglePlay</c> 에는 도로망·집·
    /// 판정 구역·눈 스택·의뢰 리그에 더해 <b>인트로·HUD·결과 화면 배선</b>이 이미 들어 있다. 그것을
    /// 빌더로 다시 조립하면 두 씬이 조용히 갈라지고, 밸런싱을 하는 쪽과 멀티를 하는 쪽이 서로 다른
    /// 게임을 보게 된다. 원본은 하나여야 한다.</para>
    ///
    /// <para><b>왜 기존 씬 위에 저장하나.</b> <c>MultiPlay.unity</c> 는 Build Settings 에 GUID 로
    /// 등록돼 있다. 지우고 복사하면 GUID 가 바뀌어 그 항목이 끊기고, 모든 브랜치가 같은 줄을 고치는
    /// <c>EditorBuildSettings.asset</c> 을 건드려야 한다. 미션 씬을 열어 이 경로로 저장하면 파일만
    /// 덮이고 <c>.meta</c> 가 그대로라 GUID 가 유지된다(실측 확인).</para>
    ///
    /// <para>바꾸는 것은 넷뿐이다 — 로컬 플레이어를 빼고, 스폰 자세를 씬이 알려 주게 하고,
    /// 서버가 스폰할 것(미션 허브·선물)을 두고, <b>피어마다 갈릴 것을 끈다</b>.</para>
    /// </summary>
    public static class MultiPlaySceneBuilder
    {
        private const string MissionScenePath =
            "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";
        private const string ScenePath = "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity";
        private const string MissionHubPrefabPath = "Assets/Game/InGame/Cleanliness/Prefabs/PF_MissionHub.prefab";
        private const string GiftPrefabPath = "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";
        private const string NetGiftPrefabPath = "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBoxNet.prefab";
        private const string AugmentHubPrefabPath = "Assets/Game/InGame/Augment/Prefabs/PF_AugmentHub.prefab";
        private const string AvatarResource = "PF_PenguinNet";
        private const string MultiRootName = "MultiPlayRig";
        private const int SpawnPointCount = SessionLauncher.MaxPlayers;
        private const float SpawnSpacingM = 2f;

        [MenuItem("PPack/Cleanliness/Build MultiPlay Scene")]
        public static void Build()
        {
            // ⚠ <b>SaveCurrentModifiedScenesIfUserWantsTo 를 쓰지 않는다</b> (2026-08-26 실측).
            // 앞선 실행이 원본 미션 씬을 열어 놓고 죽으면 그 더러운 상태가 다음 실행에서 조용히
            // 저장된다 — 실제로 팀원의 미션 씬에서 펭귄이 사라지고 멀티 리그가 들어간 채로 저장됐다.
            // 더러운 씬은 사람이 처리할 일이므로 여기서는 멈춘다.
            for (int index = 0; index < SceneManager.sceneCount; index++)
            {
                Scene open = SceneManager.GetSceneAt(index);
                if (open.isDirty)
                    throw new InvalidOperationException(
                        $"저장하지 않은 씬이 열려 있다: {open.path}. 저장하거나 버린 뒤 다시 실행한다.");
            }

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
                throw new InvalidOperationException(
                    $"{ScenePath} 가 없다. 이 빌더는 Build Settings 항목을 지키려고 기존 씬 위에 저장한다.");
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(MissionScenePath) == null)
                throw new InvalidOperationException($"미션 씬이 없다: {MissionScenePath}");

            // <b>손대기 전에 먼저 대상 경로로 저장한다.</b> 그래야 이 다음의 어떤 변경도 원본 미션 씬에
            // 닿을 수 없다 — 중간에 예외가 나도 더러워지는 것은 MultiPlay.unity 쪽이다.
            Scene scene = EditorSceneManager.OpenScene(MissionScenePath, OpenSceneMode.Single);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"씬 저장 실패: {ScenePath}");

            RequestDirector director = UnityEngine.Object.FindAnyObjectByType<RequestDirector>(
                FindObjectsInactive.Include);
            if (director == null) throw new InvalidOperationException("미션 씬에 RequestDirector 가 없다");

            var multiRoot = new GameObject(MultiRootName);
            Transform[] spawnPoints = BuildSpawnPoints(multiRoot.transform);
            RemoveLocalPlayer();
            DisableSinglePeerRigs();
            ConfigureSnow();
            BuildMissionSpawner(multiRoot.transform);
            BuildAugmentSpawner(multiRoot.transform);
            BuildGiftSupplier(multiRoot.transform, director);
            BuildBootstrap(multiRoot.transform, spawnPoints);

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"씬 저장 실패: {ScenePath}");
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[MultiPlay] 씬 생성 완료: {ScenePath} · 원본 {MissionScenePath} · " +
                      $"배달집 {director.HouseCount}채 · 스폰 {spawnPoints.Length}자리. " +
                      "아바타·미션 허브·선물은 서버가 런타임에 스폰한다.");
        }

        /// <summary>사람 수만큼 자리를 만든다. 원본 씬의 펭귄이 서 있던 자세를 기준으로 옆으로 벌린다 —
        /// 한 자리에 겹쳐 스폰하면 둘이 하나로 보여 복제 여부를 화면으로 가릴 수 없다.</summary>
        private static Transform[] BuildSpawnPoints(Transform parent)
        {
            Transform reference = FindLocalPenguin();
            VehicleRespawnPoint marker = UnityEngine.Object.FindAnyObjectByType<VehicleRespawnPoint>(
                FindObjectsInactive.Include);
            Vector3 origin = reference != null ? reference.position
                : marker != null ? marker.transform.position : SinglePlayDirector.PlayerStart;
            Quaternion rotation = reference != null ? reference.rotation
                : marker != null ? marker.transform.rotation : Quaternion.identity;

            var group = new GameObject("SpawnPoints");
            group.transform.SetParent(parent);

            var points = new Transform[SpawnPointCount];
            for (int index = 0; index < SpawnPointCount; index++)
            {
                var point = new GameObject($"Spawn_{index}");
                point.transform.SetParent(group.transform);
                float offset = (index - (SpawnPointCount - 1) * 0.5f) * SpawnSpacingM;
                point.transform.SetPositionAndRotation(origin + rotation * new Vector3(offset, 0f, 0f), rotation);
                points[index] = point.transform;
            }

            return points;
        }

        private static Transform FindLocalPenguin()
        {
            PenguinLocomotion penguin = UnityEngine.Object.FindAnyObjectByType<PenguinLocomotion>(
                FindObjectsInactive.Include);
            return penguin != null ? penguin.transform : null;
        }

        /// <summary>서버가 <c>PF_PenguinNet</c> 을 스폰하므로 씬에 놓인 펭귄은 아무도 조종하지 않는
        /// 두 번째 펭귄이 된다. 카메라와 오디오 리스너도 아바타가 들고 온다.</summary>
        private static void RemoveLocalPlayer()
        {
            // ⚠ <b>씬 루트가 아니라 펭귄만 지운다</b> (2026-08-26 실측). 미션 씬의 펭귄은 리그 루트
            // (SnowDeliveryRig) 밑에 있어서 transform.root 를 지우면 의뢰 디렉터·HUD·집 신호까지
            // 통째로 사라진다 — 맵에서 찍던 시절에는 펭귄이 루트라 같은 코드가 맞았다.
            foreach (PenguinLocomotion penguin in UnityEngine.Object.FindObjectsByType<PenguinLocomotion>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                GameObject instanceRoot = PrefabUtility.GetOutermostPrefabInstanceRoot(penguin.gameObject);
                UnityEngine.Object.DestroyImmediate(instanceRoot != null ? instanceRoot : penguin.gameObject);
            }

            // 강물 리스폰 마커가 지워진 펭귄을 본다. 빈 참조면 아무 일도 하지 않는다
            // (VehicleRespawnPoint.Respawn). 멀티의 익사 복구는 서버가 아바타를 옮기는 별도 작업이다.
            foreach (VehicleRespawnPoint spawn in UnityEngine.Object.FindObjectsByType<VehicleRespawnPoint>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                spawn.Configure(null);
                EditorUtility.SetDirty(spawn);
            }
        }

        /// <summary>피어마다 결과가 갈리는 것을 끈다. <b>끄는 것이지 지우는 것이 아니다</b> —
        /// 원본 씬과의 차이를 한 눈에 보게 두고, 되돌릴 때도 체크박스 하나다.</summary>
        private static void DisableSinglePeerRigs()
        {
            // 시작·종료 흐름(RequestStageFlowPresenter)은 끄지 않는다 — 허브가 물리면 스스로 역할을
            // 가린다. 서버는 시작과 판정을, 클라이언트는 인트로·결과 화면만 맡는다.

            // 개발용 오버레이. 키 하나로 로컬에 __DEBUG__Gift 를 만들고 근접만으로 의뢰를 완료시킨다.
            foreach (RequestGameDebugHud hud in UnityEngine.Object.FindObjectsByType<RequestGameDebugHud>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Disable(hud);
            foreach (RequestCompletionCondition condition in
                     UnityEngine.Object.FindObjectsByType<RequestCompletionCondition>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Disable(condition);

            // 선물 공급은 GiftNetSpawner 가 서버에서만 한다.
            foreach (GiftSpawner spawner in UnityEngine.Object.FindObjectsByType<GiftSpawner>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Disable(spawner);

            // 옛 선물 배달 오케스트레이션. 이 씬의 의뢰 주체는 RequestDirector 하나다.
            foreach (GiftDeliveryDirector giftDirector in UnityEngine.Object.FindObjectsByType<GiftDeliveryDirector>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
                Disable(giftDirector);
        }

        private static void Disable(MonoBehaviour behaviour)
        {
            if (behaviour == null) return;
            behaviour.enabled = false;
            EditorUtility.SetDirty(behaviour);
        }

        /// <summary>눈은 피어마다 자기 것이다(Core/Multiplay/AGENTS.md). 복제를 켜는 판단은
        /// 버벅임 측정 뒤로 미룬다 — 지금 켜면 무엇이 비싼지 가르지 못한 채로 시작하게 된다.</summary>
        private static void ConfigureSnow()
        {
            SnowCpuStage stage = UnityEngine.Object.FindAnyObjectByType<SnowCpuStage>(FindObjectsInactive.Include);
            if (stage == null) throw new InvalidOperationException("미션 씬에 SnowCpuStage 가 없다");
            SnowDeliverySceneBuilder.SetSerialized(stage, "_replicateSnowToClients", false);
        }

        private static void BuildMissionSpawner(Transform parent)
        {
            var spawnerObject = new GameObject("MissionNetSpawner");
            spawnerObject.transform.SetParent(parent);
            MissionNetSpawner spawner = spawnerObject.AddComponent<MissionNetSpawner>();

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("_hubPrefab").objectReferenceValue = LoadOrCreateHubPrefab();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>기지 앞에 선물 공급기를 둔다. 싱글의 <see cref="GiftSpawner"/> 자리이며 같은 이유로
        /// 임시다 — 눈덩이 포장이 공급을 맡으면 이 오브젝트가 사라진다.</summary>
        private static void BuildGiftSupplier(Transform parent, RequestDirector director)
        {
            var directorSerialized = new SerializedObject(director);
            var baseTransform = directorSerialized.FindProperty("_base").objectReferenceValue as Transform;
            var catalog = directorSerialized.FindProperty("_catalog").objectReferenceValue as GiftBoxCatalog;

            var supplierObject = new GameObject("GiftNetSpawner");
            supplierObject.transform.SetParent(parent);
            supplierObject.transform.position = baseTransform != null ? baseTransform.position : Vector3.zero;
            GiftNetSpawner supplier = supplierObject.AddComponent<GiftNetSpawner>();

            var serialized = new SerializedObject(supplier);
            serialized.FindProperty("_giftPrefab").objectReferenceValue = LoadOrCreateNetGiftPrefab();
            serialized.FindProperty("_catalog").objectReferenceValue = catalog;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void BuildBootstrap(Transform parent, Transform[] spawnPoints)
        {
            var bootstrapObject = new GameObject("MultiPlayBootstrap");
            bootstrapObject.transform.SetParent(parent);
            MultiPlayBootstrap bootstrap = bootstrapObject.AddComponent<MultiPlayBootstrap>();

            var serialized = new SerializedObject(bootstrap);
            serialized.FindProperty("_gameplayScenePath").stringValue = ScenePath;
            serialized.FindProperty("_avatarResource").stringValue = AvatarResource;
            SerializedProperty points = serialized.FindProperty("_spawnPoints");
            points.arraySize = spawnPoints.Length;
            for (int index = 0; index < spawnPoints.Length; index++)
                points.GetArrayElementAtIndex(index).objectReferenceValue = spawnPoints[index];
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>컴포넌트 둘뿐이라 따로 빌더를 두지 않는다.</summary>
        /// <summary>증강 허브를 스폰할 자리. 미션 허브와 같은 이유로 씬이 프리팹 참조를 든다 —
        /// <c>Core</c> 의 런처가 이 프리팹을 알면 <c>Core</c> 가 <c>InGame</c> 의 증강을 아는 것이 된다.</summary>
        private static void BuildAugmentSpawner(Transform parent)
        {
            var spawnerObject = new GameObject("AugmentNetSpawner");
            spawnerObject.transform.SetParent(parent);
            AugmentNetSpawner spawner = spawnerObject.AddComponent<AugmentNetSpawner>();

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("_hubPrefab").objectReferenceValue = LoadOrCreateAugmentHubPrefab();
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static Fusion.NetworkObject LoadOrCreateAugmentHubPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(AugmentHubPrefabPath);
            if (existing != null) return existing.GetComponent<Fusion.NetworkObject>();

            if (!AssetDatabase.IsValidFolder("Assets/Game/InGame/Augment/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Game/InGame/Augment", "Prefabs");

            var template = new GameObject("PF_AugmentHub");
            template.AddComponent<Fusion.NetworkObject>();
            template.AddComponent<AugmentNetHub>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, AugmentHubPrefabPath);
            UnityEngine.Object.DestroyImmediate(template);
            if (saved == null)
                throw new InvalidOperationException($"증강 허브 프리팹 저장 실패: {AugmentHubPrefabPath}");

            // ⚠ NetworkObject 를 새로 만들면 Fusion 프리팹 표를 다시 구워야 한다. 안 돌리면 컴파일도
            // EditMode 도 통과하고 런타임 스폰에서만 죽는다(Core/Multiplay/AGENTS.md, 2026-08-24 실측).
            Debug.LogWarning("[MultiPlay] PF_AugmentHub 를 새로 만들었다. Tools/Fusion/Rebuild Prefab Table 을 돌려라.");
            return saved.GetComponent<Fusion.NetworkObject>();
        }

        private static Fusion.NetworkObject LoadOrCreateHubPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(MissionHubPrefabPath);
            if (existing != null) return existing.GetComponent<Fusion.NetworkObject>();

            if (!AssetDatabase.IsValidFolder("Assets/Game/InGame/Cleanliness/Prefabs"))
                AssetDatabase.CreateFolder("Assets/Game/InGame/Cleanliness", "Prefabs");

            var template = new GameObject("PF_MissionHub");
            template.AddComponent<Fusion.NetworkObject>();
            template.AddComponent<MissionNetHub>();
            GameObject saved = PrefabUtility.SaveAsPrefabAsset(template, MissionHubPrefabPath);
            UnityEngine.Object.DestroyImmediate(template);
            if (saved == null) throw new InvalidOperationException($"허브 프리팹 저장 실패: {MissionHubPrefabPath}");

            // ⚠ NetworkObject 를 새로 만들면 Fusion 프리팹 표를 다시 구워야 한다. 안 돌리면 컴파일도
            // EditMode 도 통과하고 런타임 스폰에서만 죽는다(Core/Multiplay/AGENTS.md, 2026-08-24 실측).
            Debug.LogWarning("[MultiPlay] PF_MissionHub 를 새로 만들었다. Tools/Fusion/Rebuild Prefab Table 을 돌려라.");
            return saved.GetComponent<Fusion.NetworkObject>();
        }

        /// <summary>네트워크용 선물 상자. 기존 상자 프리팹의 변형이라 룩은 한 곳에서만 관리된다.</summary>
        private static Fusion.NetworkObject LoadOrCreateNetGiftPrefab()
        {
            var existing = AssetDatabase.LoadAssetAtPath<GameObject>(NetGiftPrefabPath);
            if (existing != null) return existing.GetComponent<Fusion.NetworkObject>();

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(GiftPrefabPath);
            if (source == null) throw new InvalidOperationException($"선물 프리팹이 없다: {GiftPrefabPath}");

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);

            // 원본에는 Rigidbody 가 없다. 싱글에서는 GiftSpawner 가 런타임에 붙이지만, 네트워크
            // 프리팹은 NetworkRigidbody 가 스폰 시점에 바디를 요구하므로 프리팹에 박아 둔다.
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body == null) body = instance.AddComponent<Rigidbody>();
            body.mass = 2f;
            body.linearDamping = 0.8f;
            body.angularDamping = 0.8f;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            // 보간은 애드온이 맡는다. Rigidbody 쪽까지 켜면 두 번 보간된다.
            body.interpolation = RigidbodyInterpolation.None;

            instance.AddComponent<Fusion.NetworkObject>();
            instance.AddComponent<Fusion.Addons.Physics.NetworkRigidbody>();
            instance.AddComponent<GiftNetState>();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, NetGiftPrefabPath);
            UnityEngine.Object.DestroyImmediate(instance);
            if (saved == null) throw new InvalidOperationException($"네트워크 선물 프리팹 저장 실패: {NetGiftPrefabPath}");

            Debug.LogWarning("[MultiPlay] PF_GiftBoxNet 을 새로 만들었다. Tools/Fusion/Rebuild Prefab Table 을 돌려라.");
            return saved.GetComponent<Fusion.NetworkObject>();
        }
    }
}
