using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;
using Object = UnityEngine.Object;

namespace PPack
{
    public static class PenguinMomentumHandlingBuilder
    {
        public const string VariantPath =
            "Assets/Game/InGame/Penguin/MomentumHandling/Prefabs/PF_Penguin_MomentumHandling.prefab";
        public const string ScenePath =
            "Assets/Game/InGame/Penguin/MomentumHandling/Tests/Penguin_MomentumHandling_Test.unity";

        private const string PenguinPrefabPath =
            "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";
        private const string GiftPrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";
        private const string SnowSourceScenePath =
            "Assets/Game/InGame/Snow/Tests/Snow_BallPush_Test.unity";
        private const string GrowthHudUxmlPath =
            "Assets/Game/InGame/Snow/NewSnowballSystem/UI/SnowballGrowthArcHud.uxml";
        private const string GrowthHudPanelSettingsPath =
            "Assets/Game/InGame/Snow/NewSnowballSystem/UI/SnowballGrowthPanelSettings.asset";
        private const string GroundMaterialPath =
            "Assets/Game/InGame/Penguin/Tests/M_TestGrid.mat";
        private const string RampMaterialPath =
            "Assets/Game/InGame/Penguin/Tests/M_TestGridRamp.mat";
        private const string MarkerMaterialPath =
            "Assets/Game/InGame/Penguin/Tests/M_TestPillar.mat";

        [InitializeOnLoadMethod]
        private static void BuildMissingAssetsAfterImport()
        {
            EditorApplication.delayCall += () =>
            {
                if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling ||
                    AssetDatabase.IsAssetImportWorkerProcess()) return;
                if (AssetDatabase.LoadAssetAtPath<GameObject>(VariantPath) != null &&
                    AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null) return;

                Build();
            };
        }

        [MenuItem("PPack/Penguin/Build Momentum Handling Lab")]
        public static void Build()
        {
            GameObject penguinPrefab = RequiredPrefab(PenguinPrefabPath);
            GameObject giftPrefab = RequiredPrefab(GiftPrefabPath);
            Scene previousActive = SceneManager.GetActiveScene();
            Scene lab = SceneManager.GetSceneByPath(ScenePath);
            bool openedTargetScene = lab.IsValid() && lab.isLoaded;
            if (openedTargetScene)
            {
                if (lab.isDirty)
                    throw new InvalidOperationException(
                        "열린 관성 테스트 씬에 저장되지 않은 변경이 있어 다시 만들지 않았다.");
            }
            else
            {
                lab = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            }

            try
            {
                SceneManager.SetActiveScene(lab);
                GameObject variant = BuildVariant(penguinPrefab, lab);
                if (openedTargetScene)
                    UpdateTestLoads(lab, giftPrefab);
                else
                    BuildLab(lab, variant, giftPrefab);
                BuildSnowFieldAndHud(lab);
                EditorSceneManager.MarkSceneDirty(lab);
                if (!EditorSceneManager.SaveScene(lab, ScenePath))
                    throw new InvalidOperationException($"관성 테스트 씬 저장 실패: {ScenePath}");
                AssetDatabase.SaveAssets();
                Debug.Log($"Momentum handling lab built: {ScenePath}");
            }
            finally
            {
                if (previousActive.IsValid() && previousActive.isLoaded)
                    SceneManager.SetActiveScene(previousActive);
                if (!openedTargetScene && lab.IsValid() && lab.isLoaded)
                    EditorSceneManager.CloseScene(lab, true);
            }
        }

        [MenuItem("PPack/Penguin/Apply Momentum Snowball Tuning")]
        public static void ApplySnowballTuning()
        {
            GameObject root = PrefabUtility.LoadPrefabContents(VariantPath);
            try
            {
                PenguinMomentumHandling profile =
                    root.GetComponent<PenguinMomentumHandling>();
                if (profile == null)
                    throw new InvalidOperationException(
                        $"관성 프리팹에 {nameof(PenguinMomentumHandling)}이 없다: {VariantPath}");
                ConfigureMomentumProfile(profile);
                PrefabUtility.SaveAsPrefabAsset(root, VariantPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }
        }

        private static GameObject BuildVariant(GameObject source, Scene scene)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(source, scene);
            instance.name = "PF_Penguin_MomentumHandling";
            PenguinMomentumHandling profile = instance.GetComponent<PenguinMomentumHandling>();
            if (profile == null) profile = instance.AddComponent<PenguinMomentumHandling>();
            ConfigureMomentumProfile(profile);
            PenguinMomentumSnowballBinder binder =
                instance.GetComponent<PenguinMomentumSnowballBinder>();
            if (binder == null) binder = instance.AddComponent<PenguinMomentumSnowballBinder>();
            var serializedBinder = new SerializedObject(binder);
            serializedBinder.FindProperty("_maximumMassKg").floatValue = 300f;
            serializedBinder.FindProperty("_rollingResistanceCoefficient").floatValue = 0.015f;
            serializedBinder.FindProperty("_dragCoefficient").floatValue = 0.47f;
            serializedBinder.ApplyModifiedPropertiesWithoutUndo();

            GameObject saved = PrefabUtility.SaveAsPrefabAsset(instance, VariantPath);
            Object.DestroyImmediate(instance);
            if (saved == null)
                throw new InvalidOperationException($"관성 펭귄 Variant 저장 실패: {VariantPath}");
            return saved;
        }

        private static void ConfigureMomentumProfile(PenguinMomentumHandling profile)
        {
            var serialized = new SerializedObject(profile);
            serialized.FindProperty("_snowballSteerAtZeroMps").floatValue = 1f;
            serialized.FindProperty("_snowballSteerAtNineMps").floatValue = 0.28f;
            serialized.FindProperty("_snowballDriveReleaseSeconds").floatValue = 0.2f;
            serialized.FindProperty("_snowballMaximumCoastStopSeconds").floatValue = 5.2f;
            serialized.FindProperty("_snowballMaximumBrakeStopSeconds").floatValue = 1.9f;
            SetHandlingPoint(serialized, "_snowballSeed", 2.4f, 3.2f, 0.65f, 0.35f);
            SetHandlingPoint(serialized, "_snowballStage1Point", 2.15f, 3.65f, 1.2f, 0.8f);
            SetHandlingPoint(serialized, "_snowballStage2Point", 1.8f, 4.1f, 2f, 1.4f);
            SetHandlingPoint(serialized, "_snowballStage3Point", 1.4f, 4.55f, 3f, 2.1f);
            SetHandlingPoint(serialized, "_snowballStage4Point", 1.1f, 5f, 4.2f, 3f);
            serialized.FindProperty("_snowballMaximumQuarterTurnSeconds").floatValue = 3.5f;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetHandlingPoint(SerializedObject serialized, string propertyName,
            float initialSpeedMps, float maximumSpeedMps, float accelerationSeconds,
            float steerResponseSeconds)
        {
            SerializedProperty point = serialized.FindProperty(propertyName);
            point.FindPropertyRelative("InitialTargetSpeedMps").floatValue = initialSpeedMps;
            point.FindPropertyRelative("MaximumSpeedMps").floatValue = maximumSpeedMps;
            point.FindPropertyRelative("AccelerationSeconds").floatValue = accelerationSeconds;
            point.FindPropertyRelative("SteerResponseSeconds").floatValue = steerResponseSeconds;
        }

        private static void BuildLab(Scene scene, GameObject penguinVariant,
            GameObject giftPrefab)
        {
            var root = new GameObject("Penguin_MomentumHandling_Test");
            root.AddComponent<PenguinMomentumHandlingProbe>();

            Material ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            Material ramp = AssetDatabase.LoadAssetAtPath<Material>(RampMaterialPath);
            Material marker = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
            BuildCourse(root.transform, ground, ramp, marker);

            GameObject player = (GameObject)PrefabUtility.InstantiatePrefab(penguinVariant, scene);
            player.name = "Momentum_Penguin";
            player.transform.position = new Vector3(0f, 0.02f, -27f);
            player.transform.rotation = Quaternion.identity;

            CreateGift(scene, giftPrefab, "Gift_Light", new Vector3(-5f, 0f, -20f), 8f);
            CreateGift(scene, giftPrefab, "Gift_Heavy", new Vector3(-5f, 0f, -12f), 120f);

            BuildLighting(root.transform);
            BuildOverviewCamera(root.transform);
        }

        private static void BuildDistanceMarkers(Transform parent, Material material)
        {
            for (int z = -50; z <= 160; z += 10)
            {
                CreateCube(parent, $"BrakeMarker_{z + 50:000}m_Left", new Vector3(-34.4f, 0.03f, z),
                    new Vector3(0.3f, 0.06f, 1.2f), Quaternion.identity, material);
                CreateCube(parent, $"BrakeMarker_{z + 50:000}m_Right", new Vector3(34.4f, 0.03f, z),
                    new Vector3(0.3f, 0.06f, 1.2f), Quaternion.identity, material);
            }
        }

        private static void BuildSlalom(Transform parent, Material material)
        {
            float[] laneCenters = { -24f, -8f, 8f, 24f };
            for (int lane = 0; lane < laneCenters.Length; lane++)
            {
                for (int i = 0; i < 15; i++)
                {
                    float offset = i % 2 == 0 ? -3f : 3f;
                    GameObject pole = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
                    pole.name = $"Slalom_Lane{lane + 1:00}_Pole{i + 1:00}";
                    pole.transform.SetParent(parent);
                    pole.transform.position = new Vector3(laneCenters[lane] + offset,
                        1.5f, 10f + i * 10f);
                    pole.transform.localScale = new Vector3(0.55f, 1.5f, 0.55f);
                    SetMaterial(pole, material);
                }
            }
        }

        private static void BuildCourse(Transform parent, Material ground, Material ramp,
            Material marker)
        {
            var course = new GameObject("Expanded_Course");
            course.transform.SetParent(parent);
            CreateCube(course.transform, "Flat_Acceleration_And_Braking",
                new Vector3(0f, -0.3f, 50f), new Vector3(70f, 0.6f, 240f),
                Quaternion.identity, ground);
            BuildDistanceMarkers(course.transform, marker);
            BuildSlalom(course.transform, marker);
            CreateCube(course.transform, "Slope_10deg", new Vector3(45f, 7.2f, 40f),
                new Vector3(10f, 0.6f, 80f), Quaternion.Euler(-10f, 0f, 0f), ramp);
            CreateCube(course.transform, "Slope_20deg", new Vector3(60f, 14f, 40f),
                new Vector3(10f, 0.6f, 80f), Quaternion.Euler(-20f, 0f, 0f), ramp);
        }

        private static void CreateGift(Scene scene, GameObject prefab, string name,
            Vector3 position, float massKg)
        {
            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = name;
            ConfigureGift(instance, position, massKg);
        }

        private static void ConfigureGift(GameObject instance, Vector3 position, float massKg)
        {
            instance.transform.position = position;
            Rigidbody body = instance.GetComponent<Rigidbody>();
            if (body != null) body.mass = massKg;
            PlaceColliderBottomOnGround(instance, position.y + 0.02f);
        }

        private static void UpdateTestLoads(Scene scene, GameObject giftPrefab)
        {
            RebuildCourse(scene);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name is "Snowball_Light" or "Snowball_Heavy" or
                    "Snowball_Stage1" or "Snowball_Stage2" or
                    "Snowball_Stage3" or "Snowball_Stage4" or "Snowball_MaxRadius")
                    Object.DestroyImmediate(root);
            }

            ConfigureOrCreateGift(scene, giftPrefab, "Gift_Light",
                new Vector3(-5f, 0f, -20f), 8f);
            ConfigureOrCreateGift(scene, giftPrefab, "Gift_Heavy",
                new Vector3(-5f, 0f, -12f), 120f);
        }

        private static void BuildSnowFieldAndHud(Scene scene)
        {
            RemoveOwnedSceneObjects(scene);
            SnowCpuStage stage = CloneSnowStage(scene);

            var serializedStage = new SerializedObject(stage);
            serializedStage.FindProperty("_originXZ").vector2Value = new Vector2(-35f, -70f);
            serializedStage.FindProperty("_sizeMeters").vector2Value = new Vector2(100f, 240f);
            serializedStage.FindProperty("_initialDepthMm").intValue = 300;
            serializedStage.FindProperty("_groundMap").objectReferenceValue = null;
            serializedStage.ApplyModifiedPropertiesWithoutUndo();

            SnowCpuStageView stageView = stage.GetComponent<SnowCpuStageView>();
            Light sun = FindInScene<Light>(scene, light => light.type == LightType.Directional);
            if (stageView != null)
            {
                var serializedView = new SerializedObject(stageView);
                serializedView.FindProperty("_sun").objectReferenceValue = sun;
                serializedView.ApplyModifiedPropertiesWithoutUndo();
            }

            PenguinSnowball penguinSnowball = FindInScene<PenguinSnowball>(scene);
            Camera camera = FindInScene<Camera>(scene, candidate => candidate.enabled);
            if (penguinSnowball == null || camera == null)
                throw new InvalidOperationException(
                    "관성 테스트 씬에서 HUD를 연결할 펭귄 또는 플레이 카메라를 찾지 못했다.");

            var hudObject = new GameObject("SnowballGrowthHud");
            SceneManager.MoveGameObjectToScene(hudObject, scene);
            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.panelSettings = RequiredAsset<PanelSettings>(GrowthHudPanelSettingsPath);
            document.visualTreeAsset = RequiredAsset<VisualTreeAsset>(GrowthHudUxmlPath);
            document.sortingOrder = 20;

            SnowballGrowthArcHud hud = hudObject.AddComponent<SnowballGrowthArcHud>();
            hud.Configure(null, camera);
            SnowballGrowthPlayableSceneController controller =
                hudObject.AddComponent<SnowballGrowthPlayableSceneController>();
            controller.Configure(hud, penguinSnowball, camera);
        }

        private static SnowCpuStage CloneSnowStage(Scene targetScene)
        {
            Scene sourceScene = SceneManager.GetSceneByPath(SnowSourceScenePath);
            bool closeSource = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (closeSource)
                sourceScene = EditorSceneManager.OpenScene(SnowSourceScenePath, OpenSceneMode.Additive);

            try
            {
                SnowCpuStage sourceStage = FindInScene<SnowCpuStage>(sourceScene);
                if (sourceStage == null)
                    throw new InvalidOperationException(
                        $"눈 테스트 원본 씬에서 SnowCpuStage를 찾지 못했다: {SnowSourceScenePath}");

                GameObject clone = Object.Instantiate(sourceStage.gameObject);
                clone.name = "SnowCpuStage";
                SceneManager.MoveGameObjectToScene(clone, targetScene);
                return clone.GetComponent<SnowCpuStage>();
            }
            finally
            {
                if (closeSource && sourceScene.IsValid() && sourceScene.isLoaded)
                    EditorSceneManager.CloseScene(sourceScene, true);
            }
        }

        private static void RemoveOwnedSceneObjects(Scene scene)
        {
            var remove = new List<GameObject>();
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name == "SnowballGrowthHud" ||
                    root.GetComponent<SnowCpuStage>() != null ||
                    root.GetComponent<SnowBallCarrier>() != null)
                    remove.Add(root);
            }
            foreach (GameObject target in remove) Object.DestroyImmediate(target);
        }

        private static T FindInScene<T>(Scene scene, Predicate<T> predicate = null)
            where T : Component
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (predicate == null || predicate(component)) return component;
            return null;
        }

        private static void RebuildCourse(Scene scene)
        {
            GameObject labRoot = null;
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != "Penguin_MomentumHandling_Test") continue;
                labRoot = root;
                break;
            }
            if (labRoot == null)
                throw new InvalidOperationException("관성 테스트 씬 루트를 찾을 수 없다.");

            var remove = new List<GameObject>();
            foreach (Transform child in labRoot.transform)
            {
                string childName = child.name;
                if (childName == "Expanded_Course" ||
                    childName == "Flat_Acceleration_And_Braking" ||
                    childName == "Slope_10deg" || childName == "Slope_20deg" ||
                    childName.StartsWith("BrakeMarker_", StringComparison.Ordinal) ||
                    childName.StartsWith("Slalom_", StringComparison.Ordinal))
                    remove.Add(child.gameObject);
            }
            foreach (GameObject target in remove) Object.DestroyImmediate(target);

            Material ground = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            Material ramp = AssetDatabase.LoadAssetAtPath<Material>(RampMaterialPath);
            Material marker = AssetDatabase.LoadAssetAtPath<Material>(MarkerMaterialPath);
            BuildCourse(labRoot.transform, ground, ramp, marker);
        }

        private static void ConfigureOrCreateGift(Scene scene, GameObject prefab, string name,
            Vector3 position, float massKg)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                if (root.name != name) continue;
                ConfigureGift(root, position, massKg);
                return;
            }

            CreateGift(scene, prefab, name, position, massKg);
        }

        private static void PlaceColliderBottomOnGround(GameObject instance, float groundY)
        {
            Physics.SyncTransforms();
            Collider[] colliders = instance.GetComponentsInChildren<Collider>(true);
            bool found = false;
            Bounds combined = default;
            foreach (Collider collider in colliders)
            {
                if (!collider.enabled || collider.isTrigger) continue;
                if (!found)
                {
                    combined = collider.bounds;
                    found = true;
                }
                else
                {
                    combined.Encapsulate(collider.bounds);
                }
            }

            if (!found)
                throw new InvalidOperationException($"{instance.name}에 지면 배치용 Collider가 없다.");

            instance.transform.position += Vector3.up * (groundY - combined.min.y);
        }

        private static void BuildLighting(Transform parent)
        {
            var lightObject = new GameObject("Directional Light");
            lightObject.transform.SetParent(parent);
            lightObject.transform.rotation = Quaternion.Euler(48f, -30f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.2f;
            light.shadows = LightShadows.Soft;
        }

        private static void BuildOverviewCamera(Transform parent)
        {
            var cameraObject = new GameObject("Overview Camera");
            cameraObject.transform.SetParent(parent);
            cameraObject.transform.position = new Vector3(34f, 38f, -42f);
            cameraObject.transform.LookAt(new Vector3(8f, 0f, 5f));
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.fieldOfView = 48f;
            camera.enabled = false;
        }

        private static GameObject RequiredPrefab(string path)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
            if (prefab == null) throw new InvalidOperationException($"필수 프리팹을 찾을 수 없다: {path}");
            return prefab;
        }

        private static T RequiredAsset<T>(string path) where T : Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(path);
            if (asset == null) throw new InvalidOperationException($"필수 에셋을 찾을 수 없다: {path}");
            return asset;
        }

        private static void CreateCube(Transform parent, string name, Vector3 position,
            Vector3 scale, Quaternion rotation, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.SetPositionAndRotation(position, rotation);
            cube.transform.localScale = scale;
            SetMaterial(cube, material);
        }

        private static void SetMaterial(GameObject target, Material material)
        {
            if (material != null && target.TryGetComponent(out Renderer renderer))
                renderer.sharedMaterial = material;
        }
    }
}
