using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack
{
    internal static class WinterTerrainTemplatePreviewBuilder
    {
        private const string PreviewScenePath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/Tests/Scenes/TerrainTemplate_Preview_Test.unity";
        private const string PreviewTerrainDataPath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/Tests/Generated/TD_TerrainTemplate_Preview.asset";
        private const string SkyboxMaterialPath =
            "Assets/Game/InGame/Map/WinterVillage/Lighting/Materials/M_WinterVillage_BlueHourSky.mat";
        private const string PreviousScenePathKey = "PPack.TerrainTemplatePreview.PreviousScenePath";
        private static readonly Vector3 PreviewOffset = new(1000f, 0f, 1000f);

        [MenuItem("PPack/Level Design/Terrain Templates/Build Ski Slope Preview Scene")]
        internal static void BuildPreviewScene()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Stop Play Mode before building the Terrain template preview.");

            Scene activeScene = SceneManager.GetActiveScene();
            SessionState.SetString(
                PreviousScenePathKey,
                activeScene.IsValid() ? activeScene.path : string.Empty);

            WinterTerrainTemplateGenerator.SetupIncludedTemplates();
            WinterTerrainTemplateGenerator.EnsureFolder(
                Path.GetDirectoryName(PreviewScenePath)?.Replace('\\', '/'));
            WinterTerrainTemplateGenerator.EnsureFolder(
                Path.GetDirectoryName(PreviewTerrainDataPath)?.Replace('\\', '/'));

            Scene existingPreview = SceneManager.GetSceneByPath(PreviewScenePath);
            if (existingPreview.IsValid() && existingPreview.isLoaded)
                EditorSceneManager.CloseScene(existingPreview, true);

            Scene previewScene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            previewScene.name = "TerrainTemplate_Preview_Test";
            SceneManager.SetActiveScene(previewScene);
            if (AssetDatabase.LoadAssetAtPath<TerrainData>(PreviewTerrainDataPath) != null)
                AssetDatabase.DeleteAsset(PreviewTerrainDataPath);

            WinterTerrainTemplateProfile profile =
                AssetDatabase.LoadAssetAtPath<WinterTerrainTemplateProfile>(
                    WinterTerrainTemplateGenerator.SkiProfilePath);
            if (profile == null) throw new FileNotFoundException("The included Ski Slope profile was not created.");

            GameObject root = WinterTerrainTemplateGenerator.CreateTerrainInScene(
                profile,
                PreviewTerrainDataPath,
                previewScene);
            root.name = "WinterTerrainTemplate_Preview";
            root.transform.position = PreviewOffset;
            BuildLighting(root.transform, previewScene);
            BuildCamera(root.transform, previewScene);

            EditorSceneManager.MarkSceneDirty(previewScene);
            if (!EditorSceneManager.SaveScene(previewScene, PreviewScenePath))
                throw new IOException("Failed to save " + PreviewScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Selection.activeGameObject = root;
            Debug.Log("[WinterTerrainTemplate] Ski Slope preview scene built at " + PreviewScenePath);
        }

        [MenuItem("PPack/Level Design/Terrain Templates/Close Preview And Return")]
        internal static void ClosePreviewAndReturn()
        {
            string previousPath = SessionState.GetString(PreviousScenePathKey, string.Empty);
            Scene previousScene = SceneManager.GetSceneByPath(previousPath);
            if ((!previousScene.IsValid() || !previousScene.isLoaded)
                && !string.IsNullOrEmpty(previousPath)
                && File.Exists(previousPath))
            {
                previousScene = EditorSceneManager.OpenScene(previousPath, OpenSceneMode.Additive);
            }
            if (previousScene.IsValid() && previousScene.isLoaded)
                SceneManager.SetActiveScene(previousScene);

            Scene previewScene = SceneManager.GetSceneByPath(PreviewScenePath);
            if (previewScene.IsValid() && previewScene.isLoaded && SceneManager.sceneCount > 1)
                EditorSceneManager.CloseScene(previewScene, true);
        }

        private static void BuildLighting(Transform root, Scene scene)
        {
            Transform lightingRoot = root.Find("Lighting");
            GameObject lightObject = new("Directional Light");
            SceneManager.MoveGameObjectToScene(lightObject, scene);
            lightObject.transform.SetParent(lightingRoot, false);
            lightObject.transform.rotation = Quaternion.Euler(46f, -34f, 0f);
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.25f;
            light.color = new Color(0.76f, 0.84f, 1f);
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.25f, 0.36f, 0.58f);
            RenderSettings.ambientEquatorColor = new Color(0.18f, 0.25f, 0.40f);
            RenderSettings.ambientGroundColor = new Color(0.10f, 0.14f, 0.23f);
            RenderSettings.reflectionIntensity = 0f;
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.Linear;
            RenderSettings.fogColor = new Color(0.20f, 0.31f, 0.50f);
            RenderSettings.fogStartDistance = 180f;
            RenderSettings.fogEndDistance = 430f;
            Material skybox = AssetDatabase.LoadAssetAtPath<Material>(SkyboxMaterialPath);
            if (skybox != null) RenderSettings.skybox = skybox;
        }

        private static void BuildCamera(Transform root, Scene scene)
        {
            Transform lightingRoot = root.Find("Lighting");
            GameObject cameraObject = new("Preview Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.transform.SetParent(lightingRoot, false);
            cameraObject.transform.position = PreviewOffset + new Vector3(-142f, 112f, -158f);
            cameraObject.transform.rotation = Quaternion.LookRotation(
                PreviewOffset + new Vector3(0f, 17f, 18f) - cameraObject.transform.position,
                Vector3.up);
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.tag = "MainCamera";
            camera.fieldOfView = 48f;
            camera.nearClipPlane = 0.3f;
            camera.farClipPlane = 650f;
            camera.allowHDR = true;
            camera.clearFlags = CameraClearFlags.Skybox;
        }
    }
}
