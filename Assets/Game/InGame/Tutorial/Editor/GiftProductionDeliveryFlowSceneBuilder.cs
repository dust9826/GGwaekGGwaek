using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace PPack
{
    public static class GiftProductionDeliveryFlowSceneBuilder
    {
        public const string ScenePath =
            "Assets/Game/InGame/Tutorial/Scenes/GiftProductionDeliveryFlow.unity";

        private const string TutorialScenePath = PenguinTutorialSceneBuilder.ScenePath;
        private const string WarehousePrefabPath =
            "Assets/Game/InGame/Delivery/Prefabs/GiftProductionFlow/PF_SnowballWarehouse.prefab";
        private const string KoreanFontPath =
            "Assets/Game/InGame/UI/MissionHUD/Fonts/NotoSansKR-Variable.ttf";
        private const string CyanMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialCyan.mat";
        private const string GreenMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialGreen.mat";
        private const string OrangeMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialOrange.mat";
        private const string EdgeMaterialPath =
            "Assets/Game/InGame/Tutorial/Materials/M_TutorialEdge.mat";

        private static readonly Vector3 WarehousePosition = new Vector3(-29f, 0.1f, 22f);
        private static readonly Vector3 PlayerPosition = new Vector3(11f, 0.6f, 18.2f);

        [MenuItem("PPack/Tutorial/Build Gift Production Delivery Flow Scene")]
        public static void Build()
        {
            Scene originalScene = SceneManager.GetActiveScene();
            if (originalScene.IsValid() && originalScene.isDirty)
                throw new InvalidOperationException("현재 씬에 저장하지 않은 변경이 있다. 먼저 저장한 뒤 다시 빌드하세요.");

            SceneAsset source = AssetDatabase.LoadAssetAtPath<SceneAsset>(TutorialScenePath);
            GameObject warehousePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(WarehousePrefabPath);
            Font koreanFont = AssetDatabase.LoadAssetAtPath<Font>(KoreanFontPath);
            Material cyan = AssetDatabase.LoadAssetAtPath<Material>(CyanMaterialPath);
            Material green = AssetDatabase.LoadAssetAtPath<Material>(GreenMaterialPath);
            Material orange = AssetDatabase.LoadAssetAtPath<Material>(OrangeMaterialPath);
            Material edge = AssetDatabase.LoadAssetAtPath<Material>(EdgeMaterialPath);

            if (source == null) throw new InvalidOperationException($"기준 튜토리얼 씬이 없다: {TutorialScenePath}");
            if (warehousePrefab == null || warehousePrefab.GetComponent<SnowballWarehouseStorage>() == null)
                throw new InvalidOperationException($"창고 프리팹 또는 저장 컴포넌트가 없다: {WarehousePrefabPath}");
            if (koreanFont == null || cyan == null || green == null || orange == null || edge == null)
                throw new InvalidOperationException("플로우 씬용 폰트 또는 머티리얼을 찾을 수 없다.");

            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                // Replace only the scene contents so the generated scene keeps its stable meta GUID.
                FileUtil.ReplaceFile(TutorialScenePath, ScenePath);
            }
            else if (!AssetDatabase.CopyAsset(TutorialScenePath, ScenePath))
            {
                throw new InvalidOperationException($"플로우 씬 복사 실패: {ScenePath}");
            }
            AssetDatabase.ImportAsset(ScenePath, ImportAssetOptions.ForceSynchronousImport);

            Scene flowScene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                SceneManager.SetActiveScene(flowScene);
                GameObject root = FindRoot(flowScene, "PenguinTutorial");
                GameObject penguin = FindRoot(flowScene, "TutorialPenguin");
                if (root == null || penguin == null)
                    throw new InvalidOperationException("복사한 튜토리얼 씬의 루트 또는 펭귄을 찾을 수 없다.");

                root.name = "GiftProductionDeliveryFlow";
                RemoveTutorialOnlyObjects(root.transform);
                SimplifyCampus(root.transform.Find("TrainingCampus"));

                penguin.name = "FlowPenguin";
                penguin.transform.SetPositionAndRotation(PlayerPosition, Quaternion.Euler(0f, 270f, 0f));

                SnowCpuStage snowStage = root.GetComponentInChildren<SnowCpuStage>(true);
                SnowGiftMachinePresentation machine = root.GetComponentInChildren<SnowGiftMachinePresentation>(true);
                GiftDeliveryTerminal sender = FindNamedComponent<GiftDeliveryTerminal>(root, "GiftDeliveryTerminal_SnowMachine");
                GiftDeliveryTerminal receiver = FindNamedComponent<GiftDeliveryTerminal>(root, "GiftDeliveryTerminal_Tutorial");
                if (snowStage == null || machine == null || sender == null || receiver == null)
                    throw new InvalidOperationException("눈 기계 또는 배송 단말기 참조가 완전하지 않다.");

                // 기준 튜토리얼의 창고는 이 씬 전용 창고로 교체한다. 숨은 수신 단말기는
                // 먼저 루트로 빼서 기준 창고와 함께 삭제되지 않게 보존한다.
                receiver.transform.SetParent(root.transform, true);
                SnowballWarehouseStorage sourceWarehouse = root.GetComponentInChildren<SnowballWarehouseStorage>(true);
                if (sourceWarehouse != null) UnityEngine.Object.DestroyImmediate(sourceWarehouse.gameObject);

                var landingObject = new GameObject("FlowGiftLandingAnchor");
                landingObject.transform.SetParent(root.transform.Find("TrainingCampus/Room_SnowMachine"), true);
                landingObject.transform.position = new Vector3(-3f, 0.46f, 22f);
                machine.ConfigureSnowDeliveryConversion(snowStage, landingObject.transform);
                EditorUtility.SetDirty(machine);

                Transform giftRoom = root.transform.Find("TrainingCampus/Room_Gift");
                GameObject warehouse = PrefabUtility.InstantiatePrefab(warehousePrefab, flowScene) as GameObject;
                if (warehouse == null) throw new InvalidOperationException("창고 인스턴스 생성 실패");
                warehouse.name = "FlowWarehouse";
                warehouse.transform.SetParent(giftRoom, true);
                warehouse.transform.SetPositionAndRotation(WarehousePosition, Quaternion.identity);
                SnowballWarehouseStorage storage = warehouse.GetComponent<SnowballWarehouseStorage>();

                // The second terminal is a hidden network endpoint inside the warehouse. The visible ATM
                // remains physically independent from the distant warehouse, as in the approved concept.
                receiver.name = "HiddenWarehouseDeliveryEndpoint";
                receiver.transform.SetParent(warehouse.transform, true);
                receiver.transform.SetPositionAndRotation(WarehousePosition + new Vector3(0f, 0.13f, 0.5f),
                    Quaternion.identity);
                foreach (Renderer renderer in receiver.GetComponentsInChildren<Renderer>(true)) renderer.enabled = false;
                foreach (Collider collider in receiver.GetComponentsInChildren<Collider>(true)) collider.enabled = false;
                foreach (AudioSource audioSource in receiver.GetComponentsInChildren<AudioSource>(true)) audioSource.mute = true;

                BuildFlowGuide(root.transform, koreanFont, cyan, green, orange, edge,
                    out Text statusText, out Text counterText);

                var directorObject = new GameObject("GiftProductionDeliveryFlowDirector");
                directorObject.transform.SetParent(root.transform);
                GiftProductionDeliveryFlowDirector director =
                    directorObject.AddComponent<GiftProductionDeliveryFlowDirector>();
                SetSerialized(director, "_snowStage", snowStage);
                SetSerialized(director, "_snowGiftMachine", machine);
                SetSerialized(director, "_senderTerminal", sender);
                SetSerialized(director, "_warehouseStorage", storage);
                SetSerialized(director, "_statusText", statusText);
                SetSerialized(director, "_counterText", counterText);

                NormalizeParticleOrbitalVelocityModes(root);

                EditorSceneManager.MarkSceneDirty(flowScene);
                if (!EditorSceneManager.SaveScene(flowScene, ScenePath))
                    throw new InvalidOperationException($"플로우 씬 저장 실패: {ScenePath}");
                AssetDatabase.SaveAssets();
                Debug.Log($"Gift production-delivery flow scene built: {ScenePath}");
            }
            finally
            {
                if (originalScene.IsValid() && originalScene.isLoaded) SceneManager.SetActiveScene(originalScene);
                if (flowScene.IsValid() && flowScene.isLoaded) EditorSceneManager.CloseScene(flowScene, true);
            }
        }

        private static void RemoveTutorialOnlyObjects(Transform root)
        {
            DestroyChild(root, "TutorialDirector");
            DestroyChild(root, "TutorialHUD");
            DestroyChild(root, "OpeningComicCutscene");
            DestroyChild(root, "WorldTargetVFX");
            DestroyChild(root, "WarmFill_Walk");
            DestroyChild(root, "WarmFill_Run");
            DestroyChild(root, "WarmFill_Slide");
            DestroyChild(root, "WarmFill_Snowball");
        }

        private static void SimplifyCampus(Transform campus)
        {
            if (campus == null) throw new InvalidOperationException("TrainingCampus가 없다.");
            DestroyChild(campus, "Room_Walk");
            DestroyChild(campus, "Room_Run");
            DestroyChild(campus, "Room_Slide");
            DestroyChild(campus, "Room_Snowball");
            DestroyChild(campus, "Gate_WalkToRun");
            DestroyChild(campus, "Gate_RunToSlide");
            DestroyChild(campus, "Gate_SlideToSnowball");
            DestroyChild(campus, "Gate_SnowballToMachine");
            DestroyChild(campus, "Gate_MachineToGift");

            Transform giftRoom = campus.Find("Room_Gift");
            if (giftRoom == null) throw new InvalidOperationException("Room_Gift가 없다.");
            DestroyChild(giftRoom, "GiftPad_Shadow");
            DestroyChild(giftRoom, "GiftPad");
            for (int index = 0; index < 8; index++) DestroyChild(giftRoom, $"GiftPole_{index:00}");
        }

        private static void BuildFlowGuide(Transform root, Font font, Material cyan, Material green,
            Material orange, Material edge, out Text statusText, out Text counterText)
        {
            var guide = new GameObject("FlowGuide");
            guide.transform.SetParent(root);

            CreateFloorLane(guide.transform, "SnowLane", new Vector3(8.5f, 0.03f, 22f),
                new Vector3(6f, 0.035f, 1.25f), cyan);
            CreateFloorLane(guide.transform, "GiftLane", new Vector3(-5.4f, 0.03f, 22f),
                new Vector3(7f, 0.035f, 1.25f), orange);
            CreateFloorLane(guide.transform, "WarehouseLane", new Vector3(-18.5f, 0.03f, 22f),
                new Vector3(12f, 0.035f, 1.25f), green);

            var canvasObject = new GameObject("FlowHUD", typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(root);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 120;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            GameObject panelObject = new GameObject("GuidePanel", typeof(RectTransform), typeof(Image));
            panelObject.transform.SetParent(canvasObject.transform, false);
            RectTransform panel = panelObject.GetComponent<RectTransform>();
            panel.anchorMin = new Vector2(0.5f, 1f);
            panel.anchorMax = new Vector2(0.5f, 1f);
            panel.pivot = new Vector2(0.5f, 1f);
            panel.anchoredPosition = new Vector2(0f, -24f);
            panel.sizeDelta = new Vector2(1240f, 132f);
            panelObject.GetComponent<Image>().color = new Color(0.025f, 0.08f, 0.14f, 0.90f);

            statusText = CreateUiText(panel, "StatusText", font, 30, Color.white,
                new Vector2(0f, -18f), new Vector2(1180f, 58f));
            statusText.text = "배송 라인을 준비하는 중...";
            counterText = CreateUiText(panel, "CounterText", font, 22,
                new Color(0.42f, 0.92f, 1f), new Vector2(0f, -78f), new Vector2(1180f, 38f));
            counterText.text = "창고 선물  0 / 8";
        }

        private static void CreateFloorLane(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject lane = CreateCube(parent, name, position, scale, material);
            UnityEngine.Object.DestroyImmediate(lane.GetComponent<Collider>());
        }

        private static Text CreateUiText(Transform parent, string name, Font font, int fontSize,
            Color color, Vector2 anchoredPosition, Vector2 size)
        {
            var textObject = new GameObject(name, typeof(RectTransform), typeof(Text));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = new Vector2(0.5f, 1f);
            rect.anchorMax = new Vector2(0.5f, 1f);
            rect.pivot = new Vector2(0.5f, 1f);
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.fontSize = fontSize;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private static GameObject CreateCube(Transform parent, string name, Vector3 position,
            Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
            return cube;
        }

        private static void NormalizeParticleOrbitalVelocityModes(GameObject root)
        {
            foreach (ParticleSystem particles in root.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.VelocityOverLifetimeModule velocity = particles.velocityOverLifetime;
                if (!velocity.enabled) continue;
                ParticleSystem.MinMaxCurve orbitalZ = velocity.orbitalZ;
                if (velocity.orbitalX.mode == orbitalZ.mode && velocity.orbitalY.mode == orbitalZ.mode)
                    continue;

                // Unity 6 rejects mixed Constant/TwoConstants orbital axes even when the unused
                // axes are zero. Use the same TwoConstants mode on all three axes.
                velocity.orbitalX = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.orbitalY = new ParticleSystem.MinMaxCurve(0f, 0f);
                velocity.orbitalZ = new ParticleSystem.MinMaxCurve(
                    orbitalZ.constantMin, orbitalZ.constantMax);
                EditorUtility.SetDirty(particles);
            }
        }

        private static T FindNamedComponent<T>(GameObject root, string name) where T : Component
        {
            foreach (T component in root.GetComponentsInChildren<T>(true))
                if (component.name == name) return component;
            return null;
        }

        private static GameObject FindRoot(Scene scene, string name)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
                if (root.name == name) return root;
            return null;
        }

        private static void DestroyChild(Transform parent, string childName)
        {
            Transform child = parent != null ? parent.Find(childName) : null;
            if (child != null) UnityEngine.Object.DestroyImmediate(child.gameObject);
        }

        private static void SetSerialized(UnityEngine.Object target, string propertyName, object value)
        {
            var serialized = new SerializedObject(target);
            SerializedProperty property = serialized.FindProperty(propertyName);
            if (property == null) throw new InvalidOperationException($"직렬화 필드가 없다: {propertyName}");
            switch (value)
            {
                case UnityEngine.Object objectValue:
                    property.objectReferenceValue = objectValue;
                    break;
                case Vector3 vector3Value:
                    property.vector3Value = vector3Value;
                    break;
                case int intValue:
                    property.intValue = intValue;
                    break;
                default:
                    throw new InvalidOperationException($"지원하지 않는 직렬화 값: {value?.GetType().Name}");
            }
            serialized.ApplyModifiedPropertiesWithoutUndo();
            EditorUtility.SetDirty(target);
        }
    }
}
