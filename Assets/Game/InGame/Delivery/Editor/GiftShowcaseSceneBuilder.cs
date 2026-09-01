using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.SceneManagement;

namespace PPack
{
    public static class GiftShowcaseSceneBuilder
    {
        public const string ScenePath =
            "Assets/Game/InGame/Delivery/Tests/Gift_RandomSpawn_Showcase_Test.unity";

        private const string SnowMaterialPath =
            "Assets/Game/InGame/Delivery/Materials/M_GiftShowcase_Snow.mat";
        private const string TrimMaterialPath =
            "Assets/Game/InGame/Delivery/Materials/M_GiftShowcase_Trim.mat";

        [MenuItem("PPack/Delivery/Build Random Gift Showcase Scene")]
        public static void Build()
        {
            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo()) return;

            GameObject giftPrefab =
                AssetDatabase.LoadAssetAtPath<GameObject>(GiftBoxPrefabBuilder.GiftPrefabPath);
            if (giftPrefab == null || giftPrefab.GetComponent<Gift>() == null)
            {
                GiftBoxPrefabBuilder.BuildGiftPrefab();
                giftPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(GiftBoxPrefabBuilder.GiftPrefabPath);
            }
            if (giftPrefab == null || giftPrefab.GetComponent<Gift>() == null)
                throw new InvalidOperationException($"선물 프리팹을 찾을 수 없다: {GiftBoxPrefabBuilder.GiftPrefabPath}");

            Material snowMaterial = GetOrCreateMaterial(
                SnowMaterialPath, "M_GiftShowcase_Snow", new Color(0.72f, 0.82f, 0.91f), 0.08f);
            Material trimMaterial = GetOrCreateMaterial(
                TrimMaterialPath, "M_GiftShowcase_Trim", new Color(0.055f, 0.11f, 0.19f), 0.28f);

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var root = new GameObject("Gift_RandomSpawn_Showcase_Test");

            BuildStage(root.transform, snowMaterial, trimMaterial);
            BuildLighting(root.transform);
            BuildCamera(root.transform);

            var spawnerObject = new GameObject("RandomGiftSpawner");
            spawnerObject.transform.SetParent(root.transform);
            GiftShowcaseSpawner spawner = spawnerObject.AddComponent<GiftShowcaseSpawner>();
            spawner.Configure(giftPrefab, new Vector2(10f, 5.8f), 14, 20, 0.65f, 1.05f);

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.32f, 0.40f, 0.54f);
            RenderSettings.fog = true;
            RenderSettings.fogMode = FogMode.ExponentialSquared;
            RenderSettings.fogColor = new Color(0.075f, 0.12f, 0.20f);
            RenderSettings.fogDensity = 0.008f;

            EditorSceneManager.MarkSceneDirty(scene);
            if (!EditorSceneManager.SaveScene(scene, ScenePath))
                throw new InvalidOperationException($"씬 저장 실패: {ScenePath}");

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"Random gift showcase scene built: {ScenePath}");
        }

        private static void BuildStage(Transform parent, Material snowMaterial, Material trimMaterial)
        {
            CreateCube(parent, "Platform_Trim", new Vector3(0f, -0.29f, 0f),
                new Vector3(12.5f, 0.24f, 8.3f), trimMaterial);
            CreateCube(parent, "Snow_Platform", new Vector3(0f, -0.12f, 0f),
                new Vector3(12f, 0.24f, 7.8f), snowMaterial);

            CreateCube(parent, "Edge_Back", new Vector3(0f, 0.12f, 3.82f),
                new Vector3(12f, 0.24f, 0.16f), trimMaterial);
            CreateCube(parent, "Edge_Left", new Vector3(-5.92f, 0.12f, 0f),
                new Vector3(0.16f, 0.24f, 7.5f), trimMaterial);
            CreateCube(parent, "Edge_Right", new Vector3(5.92f, 0.12f, 0f),
                new Vector3(0.16f, 0.24f, 7.5f), trimMaterial);
        }

        private static void BuildLighting(Transform parent)
        {
            var keyObject = new GameObject("Winter_Key_Light");
            keyObject.transform.SetParent(parent);
            keyObject.transform.rotation = Quaternion.Euler(46f, -32f, 0f);
            Light key = keyObject.AddComponent<Light>();
            key.type = LightType.Directional;
            key.color = new Color(0.72f, 0.82f, 1f);
            key.intensity = 1.25f;
            key.shadows = LightShadows.Soft;

            CreatePointLight(parent, "Warm_Fill_Left", new Vector3(-4.4f, 3.2f, -1.5f));
            CreatePointLight(parent, "Warm_Fill_Right", new Vector3(4.4f, 3.2f, -1.5f));
        }

        private static void BuildCamera(Transform parent)
        {
            var cameraObject = new GameObject("Main Camera");
            cameraObject.transform.SetParent(parent);
            cameraObject.tag = "MainCamera";
            cameraObject.transform.position = new Vector3(0f, 8.2f, -10.4f);
            cameraObject.transform.LookAt(new Vector3(0f, 0.55f, 0.25f));

            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.065f, 0.115f);
            camera.fieldOfView = 43f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            cameraObject.AddComponent<AudioListener>();
        }

        private static void CreatePointLight(Transform parent, string name, Vector3 position)
        {
            var lightObject = new GameObject(name);
            lightObject.transform.SetParent(parent);
            lightObject.transform.position = position;
            Light light = lightObject.AddComponent<Light>();
            light.type = LightType.Point;
            light.color = new Color(1f, 0.68f, 0.32f);
            light.intensity = 18f;
            light.range = 7f;
            light.shadows = LightShadows.Soft;
        }

        private static void CreateCube(
            Transform parent, string name, Vector3 position, Vector3 scale, Material material)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        private static Material GetOrCreateMaterial(
            string path, string materialName, Color color, float smoothness)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾을 수 없다");
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
