using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    internal static class CreativeCharacterCustomizationSceneBuilder
    {
        private const string PreviewSessionKey = "PPack.CharacterCustomizationPreview";
        private const string FeatureRoot = "Assets/Game/OutGame/UI/CharacterCustomization";
        private const string ScenePath = FeatureRoot + "/Scenes/CharacterCustomization.unity";
        private const string UxmlPath = FeatureRoot + "/CharacterCustomization.uxml";
        private const string PanelSettingsPath = "Assets/Game/OutGame/UI/StartScreen/StartScreenPanelSettings.asset";
        private const string CharacterRoot = "Assets/Game/InGame/creature asset/Creative_Characters";
        private const string BaseMeshPath = CharacterRoot + "/Prefabs/Base_Mesh.prefab";
        private const string MainMaterialPath = CharacterRoot + "/Materials/Color.mat";

        private sealed class SlotDefinition
        {
            public readonly string Id;
            public readonly string DisplayName;
            public readonly string RendererPrefix;
            public readonly string PrefabFolder;
            public readonly bool AllowNone;
            public readonly int DefaultIndex;
            public readonly Func<string, bool> PathFilter;

            public SlotDefinition(string id, string displayName, string rendererPrefix, string prefabFolder,
                                  bool allowNone, int defaultIndex, Func<string, bool> pathFilter = null)
            {
                Id = id;
                DisplayName = displayName;
                RendererPrefix = rendererPrefix;
                PrefabFolder = prefabFolder;
                AllowNone = allowNone;
                DefaultIndex = defaultIndex;
                PathFilter = pathFilter;
            }
        }

        private static readonly SlotDefinition[] SlotDefinitions =
        {
            new("body", "Body", "Body", "Body", false, 0),
            new("face", "Face", "Faces", "Faces", false, 0,
                path => path.IndexOf("emotion_neutral", StringComparison.OrdinalIgnoreCase) >= 0),
            new("hair", "Hair", "Hairstyle", "Hairstyle", true, 1),
            new("top", "Top", "T_Shirt", "Outfit", false, 0),
            new("coat", "Coat", "Outerwear", "Outwear", true, 0),
            new("pants", "Pants", "Pants", "Pants", false, 0),
            new("shoes", "Shoes", "Shoes", "Shoes", false, 0),
            new("hat", "Hat", "Hat", "Hat", true, 0),
        };

        [InitializeOnLoadMethod]
        private static void ScheduleInitialBuild()
        {
            EditorApplication.delayCall += BuildIfMissing;
            EditorApplication.playModeStateChanged -= HandlePreviewPlayMode;
            EditorApplication.playModeStateChanged += HandlePreviewPlayMode;
        }

        [MenuItem("PPack/OutGame/Rebuild Character Customization Scene")]
        private static void RebuildFromMenu()
        {
            BuildScene();
        }

        [MenuItem("PPack/OutGame/Validate Character Customization Scene")]
        private static void ValidateFromMenu()
        {
            Scene scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Additive);
            try
            {
                CreativeCharacterCustomizationController controller = scene.GetRootGameObjects()
                    .SelectMany(root => root.GetComponentsInChildren<CreativeCharacterCustomizationController>(true))
                    .FirstOrDefault();
                if (controller == null) throw new InvalidOperationException("Customization controller is missing.");

                SerializedObject serialized = new(controller);
                SerializedProperty slots = serialized.FindProperty("_slots");
                if (slots.arraySize != SlotDefinitions.Length)
                {
                    throw new InvalidOperationException(
                        $"Expected {SlotDefinitions.Length} customization slots but found {slots.arraySize}.");
                }

                for (int i = 0; i < slots.arraySize; i++)
                {
                    SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                    string id = slot.FindPropertyRelative("_id").stringValue;
                    if (slot.FindPropertyRelative("_renderer").objectReferenceValue == null)
                    {
                        throw new InvalidOperationException($"Renderer is missing for slot '{id}'.");
                    }

                    if (slot.FindPropertyRelative("_variants").arraySize == 0)
                    {
                        throw new InvalidOperationException($"Variants are missing for slot '{id}'.");
                    }
                }

                VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
                VisualElement root = visualTree.CloneTree();
                foreach (SlotDefinition definition in SlotDefinitions)
                {
                    RequireElement<Button>(root, $"previous-{definition.Id}");
                    RequireElement<Label>(root, $"value-{definition.Id}");
                    RequireElement<Button>(root, $"next-{definition.Id}");
                }

                RequireElement<Button>(root, "action-randomize");
                RequireElement<Button>(root, "action-confirm-character");
                RequireElement<Button>(root, "action-skip-character");
                RequireElement<VisualElement>(root, "preview-drag-zone");
                Debug.Log("Character customization validation passed: 8 mesh slots, UI bindings and scene references are valid.");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        [MenuItem("PPack/OutGame/Play Character Customization Preview")]
        private static void PlayPreviewFromMenu()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode) return;

            SceneAsset scene = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
            if (scene == null)
            {
                Debug.LogError($"Character customization scene is missing: {ScenePath}");
                return;
            }

            SessionState.SetBool(PreviewSessionKey, true);
            EditorSceneManager.playModeStartScene = scene;
            EditorApplication.isPlaying = true;
        }

        [MenuItem("PPack/OutGame/Capture Character Customization Preview")]
        private static void CapturePreviewFromMenu()
        {
            if (!EditorApplication.isPlaying)
            {
                Debug.LogWarning("Enter the Character Customization preview before capturing it.");
                return;
            }

            ScreenCapture.CaptureScreenshot(FeatureRoot + "/Preview/CharacterCustomizationPreview.png");
            Debug.Log("Character customization preview capture requested.");
        }

        private static void HandlePreviewPlayMode(PlayModeStateChange change)
        {
            if (change != PlayModeStateChange.EnteredEditMode || !SessionState.GetBool(PreviewSessionKey, false))
            {
                return;
            }

            SessionState.EraseBool(PreviewSessionKey);
            EditorSceneManager.playModeStartScene = null;
        }

        private static void BuildIfMissing()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode || File.Exists(ScenePath)) return;

            BuildScene();
        }

        private static void BuildScene()
        {
            VisualTreeAsset visualTree = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            PanelSettings panelSettings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
            GameObject basePrefab = AssetDatabase.LoadAssetAtPath<GameObject>(BaseMeshPath);
            Material characterMaterial = AssetDatabase.LoadAssetAtPath<Material>(MainMaterialPath);

            if (visualTree == null || panelSettings == null || basePrefab == null || characterMaterial == null)
            {
                Debug.LogError("Character customization scene build stopped: a required UI or character asset is missing.");
                return;
            }

            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Additive);
            scene.name = "CharacterCustomization";

            try
            {
                CreateCamera(scene);
                CreateLighting(scene);
                CreateStudio(scene);

                GameObject character = (GameObject)PrefabUtility.InstantiatePrefab(basePrefab, scene);
                character.name = "CharacterPreview";
                character.transform.SetPositionAndRotation(new Vector3(-1.15f, 0.22f, 0f),
                                                           Quaternion.Euler(0f, 180f, 0f));
                character.transform.localScale = Vector3.one * 1.35f;

                foreach (SkinnedMeshRenderer renderer in character.GetComponentsInChildren<SkinnedMeshRenderer>(true))
                {
                    renderer.sharedMaterial = characterMaterial;
                    renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
                    renderer.receiveShadows = true;
                }

                Animator animator = character.GetComponent<Animator>();
                if (animator != null)
                {
                    animator.applyRootMotion = false;
                    animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
                }

                GameObject uiRoot = new("CharacterCustomizationUI");
                SceneManager.MoveGameObjectToScene(uiRoot, scene);
                UIDocument document = uiRoot.AddComponent<UIDocument>();
                document.panelSettings = panelSettings;
                document.visualTreeAsset = visualTree;
                document.sortingOrder = 20;

                CreativeCharacterCustomizationController controller =
                    uiRoot.AddComponent<CreativeCharacterCustomizationController>();
                ConfigureController(controller, document, character.transform, character, characterMaterial);

                EditorSceneManager.SaveScene(scene, ScenePath);
                EnsureFirstBuildScene();
                AssetDatabase.SaveAssets();
                Debug.Log($"Character customization scene built: {ScenePath}");
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
            finally
            {
                if (scene.IsValid() && scene.isLoaded) EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void ConfigureController(CreativeCharacterCustomizationController controller,
                                                UIDocument document,
                                                Transform characterRoot,
                                                GameObject character,
                                                Material characterMaterial)
        {
            SerializedObject serialized = new(controller);
            serialized.FindProperty("_document").objectReferenceValue = document;
            serialized.FindProperty("_characterRoot").objectReferenceValue = characterRoot;

            SerializedProperty slots = serialized.FindProperty("_slots");
            var configuredSlots = new List<(SlotDefinition definition, SkinnedMeshRenderer renderer, Mesh[] meshes)>();

            foreach (SlotDefinition definition in SlotDefinitions)
            {
                SkinnedMeshRenderer renderer = FindRenderer(character, definition.RendererPrefix);
                Mesh[] meshes = LoadMeshes(definition);
                if (renderer == null || meshes.Length == 0)
                {
                    Debug.LogWarning($"Skipping customization slot '{definition.Id}': renderer or variants are missing.");
                    continue;
                }

                renderer.sharedMaterial = characterMaterial;
                ApplyDefault(renderer, meshes, definition.AllowNone, definition.DefaultIndex);
                configuredSlots.Add((definition, renderer, meshes));
            }

            slots.arraySize = configuredSlots.Count;
            for (int i = 0; i < configuredSlots.Count; i++)
            {
                (SlotDefinition definition, SkinnedMeshRenderer renderer, Mesh[] meshes) = configuredSlots[i];
                SerializedProperty slot = slots.GetArrayElementAtIndex(i);
                slot.FindPropertyRelative("_id").stringValue = definition.Id;
                slot.FindPropertyRelative("_displayName").stringValue = definition.DisplayName;
                slot.FindPropertyRelative("_renderer").objectReferenceValue = renderer;
                slot.FindPropertyRelative("_allowNone").boolValue = definition.AllowNone;
                slot.FindPropertyRelative("_defaultIndex").intValue = definition.DefaultIndex;

                SerializedProperty variants = slot.FindPropertyRelative("_variants");
                variants.arraySize = meshes.Length;
                for (int meshIndex = 0; meshIndex < meshes.Length; meshIndex++)
                {
                    variants.GetArrayElementAtIndex(meshIndex).objectReferenceValue = meshes[meshIndex];
                }
            }

            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        private static SkinnedMeshRenderer FindRenderer(GameObject character, string prefix)
        {
            return character.GetComponentsInChildren<SkinnedMeshRenderer>(true)
                .FirstOrDefault(renderer => renderer.name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase));
        }

        private static Mesh[] LoadMeshes(SlotDefinition definition)
        {
            string folder = $"{CharacterRoot}/Prefabs/{definition.PrefabFolder}";
            return AssetDatabase.FindAssets("t:Prefab", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Where(path => definition.PathFilter == null || definition.PathFilter(path))
                .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
                .Select(AssetDatabase.LoadAssetAtPath<GameObject>)
                .Where(prefab => prefab != null)
                .Select(prefab => prefab.GetComponentInChildren<SkinnedMeshRenderer>(true))
                .Where(renderer => renderer != null && renderer.sharedMesh != null)
                .Select(renderer => renderer.sharedMesh)
                .Distinct()
                .ToArray();
        }

        private static void ApplyDefault(SkinnedMeshRenderer renderer, Mesh[] meshes, bool allowNone, int defaultIndex)
        {
            int meshIndex = allowNone ? defaultIndex - 1 : defaultIndex;
            bool enabled = meshIndex >= 0 && meshIndex < meshes.Length;
            renderer.enabled = enabled;
            if (!enabled) return;

            renderer.sharedMesh = meshes[meshIndex];
            renderer.localBounds = meshes[meshIndex].bounds;
        }

        private static void CreateCamera(Scene scene)
        {
            GameObject cameraObject = new("Main Camera");
            SceneManager.MoveGameObjectToScene(cameraObject, scene);
            cameraObject.tag = "MainCamera";
            Camera camera = cameraObject.AddComponent<Camera>();
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(0.035f, 0.09f, 0.14f, 1f);
            camera.fieldOfView = 35f;
            camera.nearClipPlane = 0.1f;
            camera.farClipPlane = 80f;
            cameraObject.transform.position = new Vector3(0f, 1.75f, -7.5f);
            cameraObject.transform.LookAt(new Vector3(0f, 1.55f, 0f));
        }

        private static void CreateLighting(Scene scene)
        {
            GameObject moon = new("Moon Light");
            SceneManager.MoveGameObjectToScene(moon, scene);
            Light moonLight = moon.AddComponent<Light>();
            moonLight.type = LightType.Directional;
            moonLight.color = new Color(0.68f, 0.84f, 1f);
            moonLight.intensity = 1.1f;
            moonLight.shadows = LightShadows.Soft;
            moon.transform.rotation = Quaternion.Euler(42f, -28f, 0f);

            GameObject lantern = new("Fitting Lantern");
            SceneManager.MoveGameObjectToScene(lantern, scene);
            Light lanternLight = lantern.AddComponent<Light>();
            lanternLight.type = LightType.Point;
            lanternLight.color = new Color(1f, 0.72f, 0.34f);
            lanternLight.intensity = 5.2f;
            lanternLight.range = 5.5f;
            lanternLight.shadows = LightShadows.Soft;
            lantern.transform.position = new Vector3(-2.4f, 2.8f, -1.4f);

            GameObject fill = new("Snow Fill");
            SceneManager.MoveGameObjectToScene(fill, scene);
            Light fillLight = fill.AddComponent<Light>();
            fillLight.type = LightType.Point;
            fillLight.color = new Color(0.47f, 0.82f, 0.9f);
            fillLight.intensity = 3.2f;
            fillLight.range = 5f;
            fill.transform.position = new Vector3(0.9f, 1.8f, -0.8f);
        }

        private static void CreateStudio(Scene scene)
        {
            Material snow = GetOrCreateMaterial("CustomizationSnow", new Color(0.78f, 0.9f, 0.92f));
            Material ice = GetOrCreateMaterial("CustomizationIce", new Color(0.16f, 0.39f, 0.48f));
            Material gold = GetOrCreateMaterial("CustomizationGold", new Color(0.95f, 0.68f, 0.24f));

            GameObject floor = CreatePrimitive(scene, PrimitiveType.Plane, "Snow Floor");
            floor.transform.SetPositionAndRotation(new Vector3(0f, 0f, 0.6f), Quaternion.identity);
            floor.transform.localScale = new Vector3(2.4f, 1f, 1.6f);
            floor.GetComponent<Renderer>().sharedMaterial = snow;
            RemoveCollider(floor);

            GameObject pedestal = CreatePrimitive(scene, PrimitiveType.Cylinder, "Crew Pedestal");
            pedestal.transform.position = new Vector3(-1.15f, 0.12f, 0f);
            pedestal.transform.localScale = new Vector3(1.22f, 0.12f, 1.22f);
            pedestal.GetComponent<Renderer>().sharedMaterial = ice;
            RemoveCollider(pedestal);

            GameObject rim = CreatePrimitive(scene, PrimitiveType.Cylinder, "Pedestal Gold Rim");
            rim.transform.position = new Vector3(-1.15f, 0.255f, 0f);
            rim.transform.localScale = new Vector3(1.26f, 0.035f, 1.26f);
            rim.GetComponent<Renderer>().sharedMaterial = gold;
            RemoveCollider(rim);
        }

        private static GameObject CreatePrimitive(Scene scene, PrimitiveType type, string name)
        {
            GameObject gameObject = GameObject.CreatePrimitive(type);
            gameObject.name = name;
            SceneManager.MoveGameObjectToScene(gameObject, scene);
            return gameObject;
        }

        private static void RemoveCollider(GameObject gameObject)
        {
            Collider collider = gameObject.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{FeatureRoot}/Materials/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard");
                material = new Material(shader) { name = name };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", color);
            else material.color = color;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFirstBuildScene()
        {
            var scenes = EditorBuildSettings.scenes
                .Where(scene => !string.Equals(scene.path, ScenePath, StringComparison.OrdinalIgnoreCase))
                .ToList();
            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }

        private static void RequireElement<T>(VisualElement root, string name) where T : VisualElement
        {
            if (root.Q<T>(name) == null)
            {
                throw new InvalidOperationException($"UI element is missing: {name}");
            }
        }
    }
}
