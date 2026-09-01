using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    public static class SnowballGrowthPrototypeBuilder
    {
        private const string Root = "Assets/Game/InGame/Snow/NewSnowballSystem";
        private const string PrefabPath = Root + "/Prefabs/PF_SnowballGrowthPrototype.prefab";
        private const string ScenePath = Root + "/Tests/SnowballGrowthHud_Look_Test.unity";
        private const string PlayableSourceScenePath =
            "Assets/Game/InGame/Snow/Tests/Snow_BallPush_Test.unity";
        private const string UxmlPath = Root + "/UI/SnowballGrowthArcHud.uxml";
        private const string PanelSettingsPath = Root + "/UI/SnowballGrowthPanelSettings.asset";
        private const string GroundMaterialPath = Root + "/Materials/M_SnowballGrowthGround.mat";
        private const string SnowMaterialPath = "Assets/Game/InGame/Snow/Materials/M_SnowBall.mat";

        [MenuItem("Tools/PPack/Snow/Build Snowball Growth Prototype")]
        public static void Build()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException("Play Mode에서는 프로토타입 씬을 만들 수 없습니다.");

            PanelSettings panelSettings = CreatePanelSettings();
            CreatePrefab();
            CreatePlayableScene(panelSettings);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[SnowballGrowthPrototype] Built {ScenePath}");
        }

        private static Material CreateGroundMaterial()
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
            Material material = AssetDatabase.LoadAssetAtPath<Material>(GroundMaterialPath);
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, GroundMaterialPath);
            }
            else
            {
                material.shader = shader;
            }

            Color color = new Color32(219, 235, 247, 255);
            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            if (material.HasProperty("_Color")) material.SetColor("_Color", color);
            return material;
        }

        private static PanelSettings CreatePanelSettings()
        {
            PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (settings == null)
            {
                settings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(settings, PanelSettingsPath);
            }

            settings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            settings.referenceResolution = new Vector2Int(1920, 1080);
            settings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            settings.match = 0.5f;
            EditorUtility.SetDirty(settings);
            return settings;
        }

        private static GameObject CreatePrefab()
        {
            GameObject root = new GameObject("PF_SnowballGrowthPrototype");
            try
            {
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;

                SphereCollider sphereCollider = root.AddComponent<SphereCollider>();
                sphereCollider.radius = SnowballStageModel.MinRadiusM;

                SnowballStagePrototypeActor actor = root.AddComponent<SnowballStagePrototypeActor>();

                Transform sizePivot = CreateChild(root.transform, "StageSizePivot");
                Transform feedbackPivot = CreateChild(sizePivot, "StagePopFeedbackPivot");
                MMF_Player feedback = feedbackPivot.gameObject.AddComponent<MMF_Player>();
                feedback.AddFeedback(new MMF_SquashAndStretch
                {
                    SquashAndStretchTarget = feedbackPivot,
                    Mode = MMF_SquashAndStretch.Modes.Absolute,
                    Axis = MMF_SquashAndStretch.PossibleAxis.YtoXZ,
                    AnimateScaleDuration = 0.25f,
                    RemapCurveZero = 1f,
                    RemapCurveOne = 1.16f,
                    DetermineScaleOnPlay = false,
                    AllowAdditivePlays = false,
                    AnimateCurve = new AnimationCurve(
                        new Keyframe(0f, 0f),
                        new Keyframe(0.14f, -0.22f),
                        new Keyframe(0.42f, 1f),
                        new Keyframe(0.72f, -0.12f),
                        new Keyframe(1f, 0f))
                });

                GameObject mesh = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                mesh.name = "SnowballMesh";
                mesh.transform.SetParent(feedbackPivot, false);
                Object.DestroyImmediate(mesh.GetComponent<Collider>());
                Material snowMaterial = AssetDatabase.LoadAssetAtPath<Material>(SnowMaterialPath);
                if (snowMaterial != null) mesh.GetComponent<MeshRenderer>().sharedMaterial = snowMaterial;

                actor.Configure(sizePivot, feedbackPivot, sphereCollider, body, feedback);
                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void CreatePlayableScene(PanelSettings panelSettings)
        {
            Scene scene = EditorSceneManager.OpenScene(PlayableSourceScenePath, OpenSceneMode.Single);
            Camera camera = Object.FindFirstObjectByType<Camera>();
            PenguinSnowball penguinSnowball = Object.FindFirstObjectByType<PenguinSnowball>();
            if (camera == null || penguinSnowball == null)
                throw new System.InvalidOperationException(
                    "플레이 테스트 원본 씬에서 펭귄 카메라 또는 PenguinSnowball을 찾지 못했습니다.");

            GameObject penguin = penguinSnowball.gameObject;
            if (penguin.GetComponent<PenguinMomentumHandling>() == null)
                penguin.AddComponent<PenguinMomentumHandling>();
            if (penguin.GetComponent<PenguinMomentumSnowballBinder>() == null)
                penguin.AddComponent<PenguinMomentumSnowballBinder>();

            GameObject hudObject = new GameObject("SnowballGrowthHud");
            UIDocument document = hudObject.AddComponent<UIDocument>();
            document.panelSettings = panelSettings;
            document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            document.sortingOrder = 20;
            SnowballGrowthArcHud hud = hudObject.AddComponent<SnowballGrowthArcHud>();
            hud.Configure(null, camera);

            SnowballGrowthPlayableSceneController controller =
                hudObject.AddComponent<SnowballGrowthPlayableSceneController>();
            controller.Configure(hud, penguinSnowball, camera);

            EditorSceneManager.SaveScene(scene, ScenePath, true);
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }
    }
}
