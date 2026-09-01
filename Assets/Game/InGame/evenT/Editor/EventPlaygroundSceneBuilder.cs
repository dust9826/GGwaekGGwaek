using System;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using UnityEngine.SceneManagement;

namespace PPack
{
    [InitializeOnLoad]
    internal static class EventPlaygroundSceneBuilder
    {
        private const string ScenePath = "Assets/Game/InGame/evenT/Tests/Event_Playground_Test.unity";
        private const string MaterialFolder = "Assets/Game/InGame/evenT/Tests/Materials";
        private const string PenguinPrefabPath = "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab";
        private const string RabbitPrefabPath = "Assets/Game/InGame/creature asset/Low Poly Animated Animals/Prefabs/Animals/Rabbit_White.prefab";
        private const string SessionKey = "PPack.EventPlayground.AutoBuild2";

        static EventPlaygroundSceneBuilder()
        {
            EditorApplication.delayCall += CreateMissingSceneOnce;
        }

        [MenuItem("PPack/Events/Open Event Playground")]
        private static void OpenScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) == null)
            {
                CreateScene();
            }

            EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
        }

        [MenuItem("PPack/Events/Select Event Playground")]
        private static void SelectScene()
        {
            Selection.activeObject = AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath);
        }

        private static void CreateMissingSceneOnce()
        {
            if (SessionState.GetBool(SessionKey, false))
            {
                return;
            }

            SessionState.SetBool(SessionKey, true);
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(ScenePath) != null)
            {
                return;
            }

            try
            {
                CreateScene();
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
            }
        }

        private static void CreateScene()
        {
            EnsureFolders();

            Scene previousScene = SceneManager.GetActiveScene();
            bool hasSavedScene = previousScene.IsValid() && !string.IsNullOrEmpty(previousScene.path);
            if (!hasSavedScene && previousScene.isDirty && previousScene.rootCount > 0)
            {
                throw new InvalidOperationException("Save the current untitled scene before creating the event playground.");
            }

            NewSceneMode mode = hasSavedScene ? NewSceneMode.Additive : NewSceneMode.Single;
            Scene scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, mode);
            SceneManager.SetActiveScene(scene);

            Material snow = GetOrCreateMaterial("M_EventPlayground_Snow", new Color(0.72f, 0.9f, 1f));
            Material ice = GetOrCreateMaterial("M_EventPlayground_Ice", new Color(0.18f, 0.62f, 0.8f));
            Material rabbitBay = GetOrCreateMaterial("M_EventPlayground_RabbitBay", new Color(0.3f, 0.76f, 0.48f));
            Material emptyBay = GetOrCreateMaterial("M_EventPlayground_EmptyBay", new Color(0.32f, 0.48f, 0.82f));
            Material orange = GetOrCreateMaterial("M_EventPlayground_Trap", new Color(1f, 0.43f, 0.08f));
            Material cyan = GetOrCreateMaterial("M_EventPlayground_Spawn", new Color(0.1f, 0.9f, 1f));
            Material pink = GetOrCreateMaterial("M_EventPlayground_Reward", new Color(1f, 0.22f, 0.58f));
            Material dark = GetOrCreateMaterial("M_EventPlayground_Sign", new Color(0.035f, 0.08f, 0.13f));
            Material white = GetOrCreateMaterial("M_EventPlayground_White", new Color(0.95f, 0.98f, 1f));

            Transform root = new GameObject("EventPlayground").transform;
            Transform environment = CreateRoot("Environment", root);
            Transform lighting = CreateRoot("Lighting", root);
            Transform eventSlots = CreateRoot("EventSlots", root);
            Transform shared = CreateRoot("SharedPreviewAssets", root);

            CreateEnvironment(environment, snow, ice, dark);
            CreateLighting(lighting);
            CreatePlayer(shared);
            CreateRabbitTrapSlot(eventSlots, rabbitBay, orange, cyan, pink, dark, white);
            CreateEmptySlot(eventSlots, emptyBay, orange, cyan, pink, dark);

            if (!EditorSceneManager.SaveScene(scene, ScenePath))
            {
                throw new InvalidOperationException($"Failed to save {ScenePath}");
            }

            AssetDatabase.SaveAssets();
            Debug.Log($"Created event playground scene: {ScenePath}");

            if (hasSavedScene && previousScene.IsValid() && previousScene.isLoaded)
            {
                SceneManager.SetActiveScene(previousScene);
                EditorSceneManager.CloseScene(scene, true);
            }
        }

        private static void CreateEnvironment(Transform parent, Material snow, Material ice, Material dark)
        {
            CreateCube("Ground", parent, new Vector3(0f, -0.25f, 2f), new Vector3(30f, 0.5f, 24f), snow, true);
            CreateCube("CenterLane", parent, new Vector3(0f, 0.02f, 1.5f), new Vector3(1.2f, 0.04f, 19f), ice, false);

            CreateCube("Boundary_West", parent, new Vector3(-15.25f, 1f, 2f), new Vector3(0.5f, 2f, 24f), dark, true);
            CreateCube("Boundary_East", parent, new Vector3(15.25f, 1f, 2f), new Vector3(0.5f, 2f, 24f), dark, true);
            CreateCube("Boundary_North", parent, new Vector3(0f, 1f, 14.25f), new Vector3(31f, 2f, 0.5f), dark, true);
            CreateCube("Boundary_South", parent, new Vector3(0f, 1f, -10.25f), new Vector3(31f, 2f, 0.5f), dark, true);

            Transform start = new GameObject("PlayerStart").transform;
            start.SetParent(parent);
            start.position = new Vector3(0f, 0f, -7f);
            start.rotation = Quaternion.identity;
        }

        private static void CreateLighting(Transform parent)
        {
            GameObject sun = new GameObject("Sun");
            sun.transform.SetParent(parent);
            sun.transform.rotation = Quaternion.Euler(48f, -32f, 0f);

            Light light = sun.AddComponent<Light>();
            light.type = LightType.Directional;
            light.color = new Color(1f, 0.93f, 0.82f);
            light.intensity = 1.25f;
            light.shadows = LightShadows.Soft;

            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Trilight;
            RenderSettings.ambientSkyColor = new Color(0.38f, 0.55f, 0.72f);
            RenderSettings.ambientEquatorColor = new Color(0.22f, 0.34f, 0.48f);
            RenderSettings.ambientGroundColor = new Color(0.09f, 0.13f, 0.2f);
        }

        private static void CreatePlayer(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(PenguinPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Penguin prefab was not found at {PenguinPrefabPath}");
                return;
            }

            GameObject player = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (player == null)
            {
                return;
            }

            player.name = "Player_Penguin";
            player.transform.SetParent(parent);
            player.transform.SetPositionAndRotation(new Vector3(0f, 0.6f, -7f), Quaternion.identity);
        }

        private static void CreateRabbitTrapSlot(
            Transform parent,
            Material bayMaterial,
            Material trapMaterial,
            Material spawnMaterial,
            Material rewardMaterial,
            Material signMaterial,
            Material whiteMaterial)
        {
            Transform slot = CreateRoot("EventSlot_01_RabbitTrap", parent);
            CreateCube("RabbitTrap_TestPad", slot, new Vector3(-7.25f, 0.03f, 3f), new Vector3(12.5f, 0.06f, 10f), bayMaterial, false);
            CreateSign("RabbitTrap_Sign", slot, new Vector3(-7.25f, 1.35f, -1.6f), "RABBIT TRAP EVENT", signMaterial);

            Transform eventOrigin = CreateAnchor("EventOrigin", slot, new Vector3(-7.25f, 0.08f, 1f), trapMaterial);
            Transform rabbitSpawn = CreateAnchor("RabbitSpawn", slot, new Vector3(-10f, 0.08f, 5.5f), spawnMaterial);
            Transform trapPoint = CreateAnchor("TrapInstallPoint", slot, new Vector3(-7.25f, 0.08f, 5f), trapMaterial);
            Transform giftPoint = CreateAnchor("GiftSpawnPoint", slot, new Vector3(-4.5f, 0.08f, 5.5f), rewardMaterial);

            CreateRabbitPreview(rabbitSpawn);
            CreateCarrotPreview(trapPoint, trapMaterial, bayMaterial);
            CreateGiftPreview(giftPoint, rewardMaterial, whiteMaterial);

            eventOrigin.gameObject.SetActive(true);
        }

        private static void CreateEmptySlot(
            Transform parent,
            Material bayMaterial,
            Material trapMaterial,
            Material spawnMaterial,
            Material rewardMaterial,
            Material signMaterial)
        {
            Transform slot = CreateRoot("EventSlot_02_Empty", parent);
            CreateCube("EmptyEvent_TestPad", slot, new Vector3(7.25f, 0.03f, 3f), new Vector3(12.5f, 0.06f, 10f), bayMaterial, false);
            CreateSign("EmptySlot_Sign", slot, new Vector3(7.25f, 1.35f, -1.6f), "EMPTY EVENT SLOT", signMaterial);

            CreateAnchor("EventOrigin", slot, new Vector3(7.25f, 0.08f, 2f), trapMaterial);
            CreateAnchor("ActorSpawn", slot, new Vector3(4.5f, 0.08f, 5.5f), spawnMaterial);
            CreateAnchor("RewardSpawn", slot, new Vector3(10f, 0.08f, 5.5f), rewardMaterial);
        }

        private static void CreateRabbitPreview(Transform parent)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(RabbitPrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"Rabbit prefab was not found at {RabbitPrefabPath}");
                return;
            }

            GameObject rabbit = PrefabUtility.InstantiatePrefab(prefab) as GameObject;
            if (rabbit == null)
            {
                return;
            }

            rabbit.name = "Rabbit_EventPreview";
            rabbit.transform.SetParent(parent);
            rabbit.transform.localPosition = new Vector3(0f, 0.05f, 0f);
            rabbit.transform.localRotation = Quaternion.Euler(0f, 145f, 0f);
            rabbit.transform.localScale = Vector3.one * 1.8f;

            NavMeshAgent agent = rabbit.GetComponent<NavMeshAgent>();
            if (agent != null)
            {
                agent.enabled = false;
            }

            foreach (MonoBehaviour behaviour in rabbit.GetComponents<MonoBehaviour>())
            {
                behaviour.enabled = false;
            }
        }

        private static void CreateCarrotPreview(Transform parent, Material orange, Material green)
        {
            GameObject carrot = GameObject.CreatePrimitive(PrimitiveType.Capsule);
            carrot.name = "TrapPreview_Carrot";
            carrot.transform.SetParent(parent);
            carrot.transform.localPosition = new Vector3(0f, 0.45f, 0f);
            carrot.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            carrot.transform.localScale = new Vector3(0.24f, 0.48f, 0.24f);
            carrot.GetComponent<Renderer>().sharedMaterial = orange;
            UnityEngine.Object.DestroyImmediate(carrot.GetComponent<Collider>());

            CreateCube("CarrotLeaf_A", parent, carrot.transform.position + new Vector3(-0.12f, 0.42f, 0f), new Vector3(0.08f, 0.35f, 0.08f), green, false);
            CreateCube("CarrotLeaf_B", parent, carrot.transform.position + new Vector3(0.12f, 0.42f, 0f), new Vector3(0.08f, 0.35f, 0.08f), green, false);
        }

        private static void CreateGiftPreview(Transform parent, Material boxMaterial, Material ribbonMaterial)
        {
            Transform gift = CreateRoot("GiftResultPreview_Disabled", parent);
            CreateCube("GiftBox", gift, parent.position + new Vector3(0f, 0.55f, 0f), Vector3.one, boxMaterial, false);
            CreateCube("Ribbon_Vertical", gift, parent.position + new Vector3(0f, 0.55f, -0.51f), new Vector3(0.18f, 1.02f, 0.04f), ribbonMaterial, false);
            CreateCube("Ribbon_Horizontal", gift, parent.position + new Vector3(0f, 0.55f, -0.52f), new Vector3(1.02f, 0.18f, 0.04f), ribbonMaterial, false);
            gift.gameObject.SetActive(false);
        }

        private static Transform CreateAnchor(string name, Transform parent, Vector3 position, Material material)
        {
            Transform anchor = new GameObject(name).transform;
            anchor.SetParent(parent);
            anchor.position = position;

            GameObject marker = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            marker.name = "Marker";
            marker.transform.SetParent(anchor);
            marker.transform.localPosition = Vector3.zero;
            marker.transform.localScale = new Vector3(0.75f, 0.025f, 0.75f);
            marker.GetComponent<Renderer>().sharedMaterial = material;
            UnityEngine.Object.DestroyImmediate(marker.GetComponent<Collider>());
            return anchor;
        }

        private static void CreateSign(string name, Transform parent, Vector3 position, string text, Material material)
        {
            GameObject sign = CreateCube(name, parent, position, new Vector3(6f, 1.2f, 0.12f), material, false);

            GameObject label = new GameObject("Label");
            label.transform.SetParent(sign.transform);
            label.transform.localPosition = new Vector3(0f, 0f, -0.56f);
            label.transform.localRotation = Quaternion.identity;
            label.transform.localScale = new Vector3(1f / 6f, 1f / 1.2f, 1f / 0.12f);

            TextMesh textMesh = label.AddComponent<TextMesh>();
            textMesh.text = text;
            textMesh.anchor = TextAnchor.MiddleCenter;
            textMesh.alignment = TextAlignment.Center;
            textMesh.fontSize = 64;
            textMesh.characterSize = 0.08f;
            textMesh.color = Color.white;

            Font font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font != null)
            {
                textMesh.font = font;
                label.GetComponent<MeshRenderer>().sharedMaterial = font.material;
            }
        }

        private static GameObject CreateCube(
            string name,
            Transform parent,
            Vector3 position,
            Vector3 scale,
            Material material,
            bool keepCollider)
        {
            GameObject cube = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cube.name = name;
            cube.transform.SetParent(parent);
            cube.transform.position = position;
            cube.transform.localScale = scale;
            cube.GetComponent<Renderer>().sharedMaterial = material;
            cube.isStatic = true;

            if (!keepCollider)
            {
                UnityEngine.Object.DestroyImmediate(cube.GetComponent<Collider>());
            }

            return cube;
        }

        private static Transform CreateRoot(string name, Transform parent)
        {
            Transform root = new GameObject(name).transform;
            root.SetParent(parent);
            root.localPosition = Vector3.zero;
            root.localRotation = Quaternion.identity;
            root.localScale = Vector3.one;
            return root;
        }

        private static Material GetOrCreateMaterial(string name, Color color)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material != null)
            {
                return material;
            }

            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null)
            {
                shader = Shader.Find("Standard");
            }

            material = new Material(shader)
            {
                name = name,
                color = color,
                enableInstancing = true
            };
            AssetDatabase.CreateAsset(material, path);
            return material;
        }

        private static void EnsureFolders()
        {
            EnsureFolder("Assets/Game/InGame/evenT/Tests");
            EnsureFolder(MaterialFolder);
        }

        private static void EnsureFolder(string path)
        {
            if (AssetDatabase.IsValidFolder(path))
            {
                return;
            }

            string parent = path[..path.LastIndexOf('/')];
            string folderName = path[(path.LastIndexOf('/') + 1)..];
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, folderName);
        }
    }
}
