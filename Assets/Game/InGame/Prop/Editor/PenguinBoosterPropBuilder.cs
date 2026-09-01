using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    public static class PenguinBoosterPropBuilder
    {
        private const string RootFolder = "Assets/Game/InGame/Prop";
        private const string MaterialFolder = RootFolder + "/Materials";
        private const string PrefabFolder = RootFolder + "/Prefabs";
        private const string PrefabPath = PrefabFolder + "/PF_PenguinBooster.prefab";
        private const string FireBulletPath =
            "Assets/Plugins/AllIn1VfxToolkit/Demo & Assets/Demo/Prefabs/Fire Bullet.prefab";
        private const string LegacyBulletMeshPath =
            "Assets/Plugins/AllIn1VfxToolkit/Demo & Assets/Meshes/Bullet.mesh";
        private const string SinglePlayPath =
            "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";

        [MenuItem("PPack/Prop/Build Booster And Install In SinglePlay")]
        public static void BuildAndInstall()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new System.InvalidOperationException(
                    "Exit Play Mode before building the penguin booster.");

            EnsureFolder(MaterialFolder);
            EnsureFolder(PrefabFolder);
            UpgradeLegacyFireBulletMesh();

            GameObject prefab = BuildPrefab();
            InstallInSinglePlay(prefab);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[PenguinBooster] Built {PrefabPath} and installed it in {SinglePlayPath}.");
        }

        private static void UpgradeLegacyFireBulletMesh()
        {
            // Toolkit의 .mesh는 Unity 6 최소 직렬화 버전보다 오래됐다. 시각 데이터는
            // 건드리지 않고 Editor 직렬화만 최신 포맷으로 올려 런타임 로드 오류를 막는다.
            if (AssetDatabase.LoadAssetAtPath<Mesh>(LegacyBulletMeshPath) == null)
                throw new System.IO.FileNotFoundException("Fire Bullet mesh was not found.",
                    LegacyBulletMeshPath);

            AssetDatabase.ForceReserializeAssets(new[] { LegacyBulletMeshPath });
        }

        private static GameObject BuildPrefab()
        {
            GameObject fireBullet = AssetDatabase.LoadAssetAtPath<GameObject>(FireBulletPath);
            if (fireBullet == null)
                throw new System.IO.FileNotFoundException("Fire Bullet VFX prefab was not found.",
                    FireBulletPath);

            Material red = GetOrCreateMaterial("MAT_Booster_Red",
                new Color(0.84f, 0.035f, 0.025f), new Color(4.2f, 0.08f, 0.015f));
            Material gold = GetOrCreateMaterial("MAT_Booster_Gold",
                new Color(1f, 0.46f, 0.035f), new Color(5.5f, 1.3f, 0.04f));
            Material ice = GetOrCreateMaterial("MAT_Booster_Ice",
                new Color(0.82f, 0.95f, 1f), new Color(0.18f, 0.75f, 1.5f));

            GameObject root = new GameObject("PF_PenguinBooster");
            try
            {
                SphereCollider trigger = root.AddComponent<SphereCollider>();
                trigger.isTrigger = true;
                trigger.center = new Vector3(0f, 1.05f, 0f);
                trigger.radius = 0.9f;

                GameObject visualRoot = new GameObject("VisualRoot");
                visualRoot.transform.SetParent(root.transform, false);
                visualRoot.transform.localPosition = new Vector3(0f, 1.05f, 0f);
                visualRoot.transform.localRotation = Quaternion.Euler(0f, 0f, -12f);

                CreatePart(PrimitiveType.Capsule, "RocketBody", visualRoot.transform,
                    Vector3.zero, new Vector3(0.44f, 0.58f, 0.44f), red);
                CreatePart(PrimitiveType.Cylinder, "PowerBand", visualRoot.transform,
                    new Vector3(0f, 0.08f, 0f), new Vector3(0.48f, 0.1f, 0.48f), gold);
                CreatePart(PrimitiveType.Sphere, "IceCap", visualRoot.transform,
                    new Vector3(0f, 0.62f, 0f), new Vector3(0.3f, 0.24f, 0.3f), ice);
                CreatePart(PrimitiveType.Sphere, "FlameCore", visualRoot.transform,
                    new Vector3(0f, -0.68f, 0f), new Vector3(0.28f, 0.42f, 0.28f), gold);

                CreateFin(visualRoot.transform, new Vector3(0.35f, -0.38f, 0f),
                    new Vector3(0f, 0f, -26f), red);
                CreateFin(visualRoot.transform, new Vector3(-0.35f, -0.38f, 0f),
                    new Vector3(0f, 0f, 26f), red);
                CreateFin(visualRoot.transform, new Vector3(0f, -0.38f, 0.35f),
                    new Vector3(26f, 0f, 0f), gold);
                CreateFin(visualRoot.transform, new Vector3(0f, -0.38f, -0.35f),
                    new Vector3(-26f, 0f, 0f), gold);

                GameObject emblem = CreatePart(PrimitiveType.Cube, "BoostMark",
                    visualRoot.transform, new Vector3(0f, 0.15f, -0.43f),
                    new Vector3(0.11f, 0.3f, 0.035f), ice);
                emblem.transform.localRotation = Quaternion.Euler(0f, 0f, -32f);

                GameObject lightObject = new GameObject("BoosterGlow");
                lightObject.transform.SetParent(visualRoot.transform, false);
                Light glow = lightObject.AddComponent<Light>();
                glow.type = LightType.Point;
                glow.color = new Color(1f, 0.28f, 0.04f);
                glow.intensity = 3.5f;
                glow.range = 3.5f;
                glow.shadows = LightShadows.None;

                PenguinBoosterPickup pickup = root.AddComponent<PenguinBoosterPickup>();
                SerializedObject serializedPickup = new SerializedObject(pickup);
                serializedPickup.FindProperty("_fireBulletVfx").objectReferenceValue = fireBullet;
                serializedPickup.FindProperty("_visualRoot").objectReferenceValue = visualRoot.transform;
                serializedPickup.FindProperty("_speedMultiplier").floatValue = 1.6f;
                serializedPickup.FindProperty("_durationSeconds").floatValue = 5f;
                serializedPickup.FindProperty("_respawnSeconds").floatValue = 8f;
                serializedPickup.ApplyModifiedPropertiesWithoutUndo();

                return PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        private static void InstallInSinglePlay(GameObject prefab)
        {
            Scene current = SceneManager.GetActiveScene();
            if (current.isDirty)
                throw new System.InvalidOperationException(
                    $"The active scene '{current.name}' has unsaved edits. Save it before installing the booster.");

            Scene scene = EditorSceneManager.OpenScene(SinglePlayPath, OpenSceneMode.Single);
            GameObject gameplay = GameObject.Find("Gameplay");
            if (gameplay == null)
                throw new System.InvalidOperationException("SinglePlay is missing the Gameplay root.");

            Transform propRoot = gameplay.transform.Find("Prop_Boosters");
            if (propRoot == null)
            {
                GameObject propRootObject = new GameObject("Prop_Boosters");
                propRootObject.transform.SetParent(gameplay.transform, false);
                propRoot = propRootObject.transform;
            }

            Transform existing = propRoot.Find("PenguinBooster");
            if (existing != null)
                Object.DestroyImmediate(existing.gameObject);

            GameObject instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab, scene);
            instance.name = "PenguinBooster";
            instance.transform.SetParent(propRoot, true);
            instance.transform.position = FindPlacementPosition();
            instance.transform.rotation = Quaternion.identity;
            EditorUtility.SetDirty(instance);
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = instance;
        }

        private static Vector3 FindPlacementPosition()
        {
            GameObject spawn = GameObject.Find("PlayerSpawn");
            Vector3 target = spawn != null
                ? spawn.transform.position + spawn.transform.forward * 5f +
                  spawn.transform.right * 1.5f
                : new Vector3(-1f, 1f, -8f);

            Physics.SyncTransforms();
            Vector3 rayOrigin = target + Vector3.up * 50f;
            if (Physics.Raycast(rayOrigin, Vector3.down, out RaycastHit hit, 150f,
                    Physics.DefaultRaycastLayers, QueryTriggerInteraction.Ignore))
                target.y = hit.point.y;

            return target;
        }

        private static void CreateFin(Transform parent, Vector3 localPosition,
            Vector3 localEuler, Material material)
        {
            GameObject fin = CreatePart(PrimitiveType.Cube, "RocketFin", parent,
                localPosition, new Vector3(0.18f, 0.38f, 0.34f), material);
            fin.transform.localRotation = Quaternion.Euler(localEuler);
        }

        private static GameObject CreatePart(PrimitiveType type, string partName,
            Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(type);
            part.name = partName;
            part.transform.SetParent(parent, false);
            part.transform.localPosition = localPosition;
            part.transform.localScale = localScale;
            Collider collider = part.GetComponent<Collider>();
            if (collider != null)
                Object.DestroyImmediate(collider);
            Renderer renderer = part.GetComponent<Renderer>();
            renderer.sharedMaterial = material;
            return part;
        }

        private static Material GetOrCreateMaterial(string materialName, Color baseColor,
            Color emissionColor)
        {
            string path = $"{MaterialFolder}/{materialName}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit") ??
                                Shader.Find("Standard");
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }

            if (material.HasProperty("_BaseColor"))
                material.SetColor("_BaseColor", baseColor);
            else
                material.color = baseColor;
            material.EnableKeyword("_EMISSION");
            if (material.HasProperty("_EmissionColor"))
                material.SetColor("_EmissionColor", emissionColor);
            material.globalIlluminationFlags = MaterialGlobalIlluminationFlags.RealtimeEmissive;
            EditorUtility.SetDirty(material);
            return material;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                    AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }
}
