using System;
using System.Reflection;
using MoreMountains.Feedbacks;
using UnityEditor;
using UnityEditor.Events;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    [InitializeOnLoad]
    internal static class MissionHUDSceneBuilder
    {
        private const string FeatureRoot = "Assets/Game/InGame/UI/MissionHUD";
        private const string UxmlPath = FeatureRoot + "/MissionHUD.uxml";
        private const string PanelSettingsPath = FeatureRoot + "/MissionHUDPanelSettings.asset";
        private const string PrefabPath = FeatureRoot + "/Prefabs/PF_MissionHUD.prefab";
        private const string ScenePath = FeatureRoot + "/Tests/MissionHUD_RequiredMissions_Test.unity";
        private const string WinterVillageConceptMapPath =
            "Assets/Game/InGame/Map/WinterVillage/Scenes/WinterVillage_ConceptMap.unity";
        private const string PreviewPath = FeatureRoot + "/Preview/MissionHUDPreview.png";
        private const string WinterVillagePreviewPath =
            FeatureRoot + "/Preview/MissionHUDWinterVillagePreview.png";
        private const string SessionKey = "PPack.MissionHUD.AutoBuild1";

        static MissionHUDSceneBuilder()
        {
            EditorApplication.delayCall += BuildMissingAssetsOnce;
        }

        [MenuItem("PPack/UI/Mission HUD/Rebuild Assets")]
        private static void RebuildAssets()
        {
            BuildAll();
        }

        [MenuItem("PPack/UI/Mission HUD/Open Test Scene")]
        private static void OpenTestScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                BuildAll();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("PPack/UI/Mission HUD/Install Rabbit Preview In Winter Village Concept Map")]
        private static void InstallRabbitPreviewInWinterVillageConceptMap()
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            if (prefab == null)
            {
                BuildAll();
                prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            }

            if (prefab == null)
            {
                throw new InvalidOperationException($"Mission HUD prefab을 찾을 수 없습니다: {PrefabPath}");
            }

            Scene scene = SceneManager.GetSceneByPath(WinterVillageConceptMapPath);
            bool openedForInstall = !scene.IsValid() || !scene.isLoaded;
            Scene previousScene = SceneManager.GetActiveScene();

            if (openedForInstall)
            {
                scene = EditorSceneManager.OpenScene(WinterVillageConceptMapPath, OpenSceneMode.Additive);
            }

            SceneManager.SetActiveScene(scene);
            try
            {
                MissionHUDController controller = FindMissionHud(scene);
                if (controller == null)
                {
                    GameObject hud = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                    if (hud == null)
                    {
                        throw new InvalidOperationException("Winter Village Concept Map에 Mission HUD를 생성하지 못했습니다.");
                    }

                    hud.name = "MissionHUD_RabbitTrapPreview";
                    controller = hud.GetComponent<MissionHUDController>();
                }

                MissionHUDPreviewDriver preview = controller.GetComponent<MissionHUDPreviewDriver>();
                if (preview == null)
                {
                    preview = controller.gameObject.AddComponent<MissionHUDPreviewDriver>();
                }

                preview.Configure(controller, true);
                EditorUtility.SetDirty(preview);
                EditorSceneManager.MarkSceneDirty(scene);

                if (!EditorSceneManager.SaveScene(scene, WinterVillageConceptMapPath))
                {
                    throw new InvalidOperationException("Winter Village Concept Map 저장에 실패했습니다.");
                }

                AssetDatabase.SaveAssets();
                Debug.Log("Installed rabbit-trap Mission HUD preview in WinterVillage_ConceptMap.");
            }
            finally
            {
                if (openedForInstall)
                {
                    if (previousScene.IsValid() && previousScene.isLoaded)
                    {
                        SceneManager.SetActiveScene(previousScene);
                    }

                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        [MenuItem("PPack/UI/Mission HUD/Capture Preview")]
        private static void CapturePreview()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Mission HUD test scene을 Play한 뒤 미리보기를 캡처하세요.");
                return;
            }

            string capturePath = SceneManager.GetActiveScene().path == WinterVillageConceptMapPath
                ? WinterVillagePreviewPath
                : PreviewPath;
            ScreenCapture.CaptureScreenshot(capturePath, 1);
            Debug.Log($"Mission HUD preview capture requested: {capturePath}");
        }

        private static void BuildMissingAssetsOnce()
        {
            if (SessionState.GetBool(SessionKey, false) || EditorApplication.isPlayingOrWillChangePlaymode)
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            if (AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath) != null &&
                AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null &&
                AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath) != null)
            {
                return;
            }

            try
            {
                BuildAll();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void BuildAll()
        {
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            if (visualTree == null)
            {
                throw new InvalidOperationException($"Mission HUD UXML을 찾을 수 없습니다: {UxmlPath}");
            }

            PanelSettings panelSettings = GetOrCreatePanelSettings();
            GameObject prefab = BuildPrefab(visualTree, panelSettings);
            BuildTestScene(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Mission HUD assets rebuilt: {PrefabPath}, {ScenePath}");
        }

        private static PanelSettings GetOrCreatePanelSettings()
        {
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            if (panelSettings == null)
            {
                panelSettings = ScriptableObject.CreateInstance<PanelSettings>();
                AssetDatabase.CreateAsset(panelSettings, PanelSettingsPath);
            }

            panelSettings.scaleMode = PanelScaleMode.ScaleWithScreenSize;
            panelSettings.referenceResolution = new Vector2Int(1920, 1080);
            panelSettings.screenMatchMode = PanelScreenMatchMode.MatchWidthOrHeight;
            panelSettings.match = 0.5f;
            EditorUtility.SetDirty(panelSettings);
            return panelSettings;
        }

        private static GameObject BuildPrefab(VisualTreeAsset visualTree, PanelSettings panelSettings)
        {
            GameObject root = new GameObject("MissionHUD");
            try
            {
                UIDocument document = root.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 20;
                MissionHUDController controller = root.AddComponent<MissionHUDController>();
                BuildMissionFeelFeedbacks(root, controller);

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
                if (prefab == null)
                {
                    throw new InvalidOperationException($"Mission HUD prefab 저장에 실패했습니다: {PrefabPath}");
                }

                return prefab;
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static void BuildTestScene(GameObject prefab)
        {
            Scene previousScene = SceneManager.GetActiveScene();
            bool hasPreviousScene = previousScene.IsValid() && previousScene.isLoaded;
            Scene scene = SceneManager.GetSceneByPath(ScenePath);
            bool closeAfterBuild = !scene.IsValid() || !scene.isLoaded;

            if (closeAfterBuild && AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }
            else if (closeAfterBuild)
            {
                bool canCreateAdditively = hasPreviousScene && !string.IsNullOrEmpty(previousScene.path);
                NewSceneMode mode = canCreateAdditively ? NewSceneMode.Additive : NewSceneMode.Single;
                scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
                scene.name = "MissionHUD_RequiredMissions_Test";
            }
            else
            {
                foreach (GameObject root in scene.GetRootGameObjects())
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }
            }

            SceneManager.SetActiveScene(scene);

            try
            {
                CreateFallbackCamera(scene);

                GameObject hud = PrefabUtility.InstantiatePrefab(prefab, scene) as GameObject;
                if (hud == null)
                {
                    throw new InvalidOperationException("Mission HUD prefab 인스턴스 생성에 실패했습니다.");
                }

                hud.name = "MissionHUD_RabbitTrapPreview";
                MissionHUDController controller = hud.GetComponent<MissionHUDController>();
                MissionHUDPreviewDriver preview = hud.AddComponent<MissionHUDPreviewDriver>();
                preview.Configure(controller);

                if (!EditorSceneManager.SaveScene(scene, ScenePath))
                {
                    throw new InvalidOperationException($"Mission HUD 테스트 씬 저장에 실패했습니다: {ScenePath}");
                }
            }
            finally
            {
                if (closeAfterBuild && hasPreviousScene && previousScene.IsValid() && previousScene.isLoaded)
                {
                    SceneManager.SetActiveScene(previousScene);
                    EditorSceneManager.CloseScene(scene, true);
                }
            }
        }

        private static void CreateFallbackCamera(Scene scene)
        {
            GameObject cameraObject = new GameObject("FallbackCamera_UIOnly");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.10f, 0.17f, 0.23f, 1f);
            camera.cullingMask = 0;
            camera.depth = -100f;
        }

        private static void BuildMissionFeelFeedbacks(GameObject root, MissionHUDController controller)
        {
            GameObject scaleDriver = new GameObject("FeelScaleDriver");
            scaleDriver.transform.SetParent(root.transform, false);
            scaleDriver.transform.localScale = Vector3.one;

            MMF_Player receivedPlayer = CreateSquashFeedback(
                root.transform,
                "Feel_MissionReceived",
                scaleDriver.transform,
                MMF_SquashAndStretch.PossibleAxis.XtoY,
                0.26f,
                1.04f);
            MMF_Player clearedPlayer = CreateSquashFeedback(
                root.transform,
                "Feel_MissionCleared",
                scaleDriver.transform,
                MMF_SquashAndStretch.PossibleAxis.YtoX,
                0.28f,
                1.02f);

            SerializedObject serializedController = new SerializedObject(controller);
            serializedController.FindProperty("_feelScaleDriver").objectReferenceValue = scaleDriver.transform;
            serializedController.ApplyModifiedPropertiesWithoutUndo();

            BindFeelEvent(controller, "_missionReceivedFeedback", receivedPlayer);
            BindFeelEvent(controller, "_missionClearedFeedback", clearedPlayer);
            EditorUtility.SetDirty(controller);
        }

        private static MMF_Player CreateSquashFeedback(
            Transform parent,
            string name,
            Transform scaleDriver,
            MMF_SquashAndStretch.PossibleAxis axis,
            float duration,
            float maximumScale)
        {
            GameObject feedbackObject = new GameObject(name);
            feedbackObject.transform.SetParent(parent, false);

            MMF_Player player = feedbackObject.AddComponent<MMF_Player>();
            player.RestoreInitialValuesOnDisable = true;
            player.AddFeedback(new MMF_SquashAndStretch
            {
                SquashAndStretchTarget = scaleDriver,
                Mode = MMF_SquashAndStretch.Modes.Absolute,
                Axis = axis,
                AnimateScaleDuration = duration,
                RemapCurveZero = 1f,
                RemapCurveOne = maximumScale,
                AnimateCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.36f, 1f),
                    new Keyframe(1f, 0f)),
                AllowAdditivePlays = false,
                DetermineScaleOnPlay = false
            });
            return player;
        }

        private static void BindFeelEvent(MissionHUDController controller, string fieldName, MMF_Player player)
        {
            FieldInfo field = typeof(MissionHUDController).GetField(
                fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            UnityEvent unityEvent = field?.GetValue(controller) as UnityEvent;
            if (unityEvent == null)
            {
                throw new InvalidOperationException($"Mission HUD Feel event를 찾을 수 없습니다: {fieldName}");
            }

            UnityAction action = player.PlayFeedbacks;
            UnityEventTools.AddPersistentListener(unityEvent, action);
        }

        private static MissionHUDController FindMissionHud(Scene scene)
        {
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                MissionHUDController controller = root.GetComponentInChildren<MissionHUDController>(true);
                if (controller != null)
                {
                    return controller;
                }
            }

            return null;
        }
    }
}
