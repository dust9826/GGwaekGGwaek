using System.Collections.Generic;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    public static class NeighborhoodModularMapBuilder
    {
        private const string ScenePath = "Assets/Game/InGame/Map/Neighborhood/Scenes/Neighborhood_ConceptMap.unity";
        private const string MaterialFolder = "Assets/Game/InGame/Map/Neighborhood/Materials/Modular";

        [MenuItem("PPack/Map/Rebuild Modular Neighborhood")]
        public static void Rebuild()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (scene.path != ScenePath)
            {
                scene = EditorSceneManager.OpenScene(ScenePath, OpenSceneMode.Single);
            }

            GameObject mapRoot = GameObject.Find("NeighborhoodMap");
            if (mapRoot == null)
            {
                Debug.LogError("NeighborhoodMap root is missing.");
                return;
            }

            DestroyChild(mapRoot.transform, "Model");
            DestroyChild(mapRoot.transform, "ModularNeighborhood");

            CreateMaterials();

            Transform modularRoot = CreateRoot("ModularNeighborhood", mapRoot.transform);
            Transform housesRoot = CreateRoot("Houses", modularRoot);
            Transform vegetationRoot = CreateRoot("Vegetation", modularRoot);
            Transform streetDetailsRoot = CreateRoot("StreetDetails", modularRoot);
            Transform boundaryRoot = CreateRoot("InvisibleBoundary", modularRoot);

            BuildHouses(housesRoot);
            BuildVegetation(vegetationRoot);
            BuildStreetDetails(streetDetailsRoot);
            BuildInvisibleBoundary(boundaryRoot);
            RemovePrimitiveColliders(modularRoot);

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);
            Selection.activeGameObject = modularRoot.gameObject;
            Debug.Log($"Rebuilt modular neighborhood: {housesRoot.childCount} houses, {vegetationRoot.childCount} vegetation groups.");
        }

        private static void CreateMaterials()
        {
            EnsureFolder(MaterialFolder);
            CreateMaterial("M_ModularRoof", new Color(0.105f, 0.135f, 0.18f));
            CreateMaterial("M_ModularTrim", new Color(0.88f, 0.86f, 0.77f));
            CreateMaterial("M_ModularWood", new Color(0.32f, 0.18f, 0.09f));
            CreateMaterial("M_ModularWindow", new Color(0.12f, 0.33f, 0.39f), 0.28f);
            CreateMaterial("M_ModularWallBlue", new Color(0.22f, 0.43f, 0.46f));
            CreateMaterial("M_ModularWallCream", new Color(0.72f, 0.67f, 0.48f));
            CreateMaterial("M_ModularWallRed", new Color(0.55f, 0.18f, 0.12f));
            CreateMaterial("M_ModularWallMustard", new Color(0.69f, 0.43f, 0.08f));
            CreateMaterial("M_ModularWallSage", new Color(0.36f, 0.49f, 0.35f));
            CreateMaterial("M_ModularTreeTrunk", new Color(0.24f, 0.13f, 0.055f));
            CreateMaterial("M_ModularLeafDark", new Color(0.16f, 0.36f, 0.07f));
            CreateMaterial("M_ModularLeafLight", new Color(0.40f, 0.61f, 0.10f));
            CreateMaterial("M_ModularStone", new Color(0.48f, 0.50f, 0.44f));
            AssetDatabase.SaveAssets();
        }

        private static void BuildHouses(Transform parent)
        {
            Material blue = LoadMaterial("M_ModularWallBlue");
            Material cream = LoadMaterial("M_ModularWallCream");
            Material red = LoadMaterial("M_ModularWallRed");
            Material mustard = LoadMaterial("M_ModularWallMustard");
            Material sage = LoadMaterial("M_ModularWallSage");

            BuildHouse(parent, "House_Lot_L1", new Vector3(-12.4f, 0.02f, 14.1f), 4.7f, 3.8f, blue, true, true, 0);
            BuildHouse(parent, "House_Lot_R1", new Vector3(12.5f, 0.02f, 14.0f), 4.7f, 4.0f, cream, true, true, 1);
            BuildHouse(parent, "House_Lot_L2", new Vector3(-12.1f, 0.02f, 5.5f), 4.6f, 3.6f, red, false, true, 2);
            BuildHouse(parent, "House_Lot_R2", new Vector3(12.2f, 0.02f, 5.3f), 4.7f, 3.7f, sage, true, false, 3);
            BuildHouse(parent, "House_Lot_L3", new Vector3(-12.0f, 0.02f, -4.0f), 4.5f, 3.6f, cream, false, true, 4);
            BuildHouse(parent, "House_Lot_R3", new Vector3(12.1f, 0.02f, -4.3f), 4.5f, 3.7f, red, true, true, 5);
            BuildHouse(parent, "House_Lot_L4", new Vector3(-11.7f, 0.02f, -13.4f), 4.7f, 3.8f, mustard, true, false, 6);
            BuildHouse(parent, "House_Lot_R4", new Vector3(11.8f, 0.02f, -13.5f), 4.8f, 3.9f, blue, true, true, 7);
        }

        private static void BuildVegetation(Transform parent)
        {
            var trees = new (float x, float z, float scale, bool pine)[]
            {
                (-16.2f, 16.8f, 1f, true), (-7.7f, 16.7f, 0.85f, false),
                (16.2f, 16.5f, 0.95f, false), (7.3f, 16.4f, 0.85f, true),
                (-16.3f, 8.5f, 0.9f, false), (-8.2f, 8.4f, 0.75f, true),
                (16f, 8.4f, 0.85f, true), (7.5f, 8f, 0.8f, false),
                (-16.1f, -0.5f, 0.85f, true), (-7.7f, -0.2f, 0.8f, false),
                (16.2f, -0.4f, 0.9f, false), (7.6f, -0.8f, 0.8f, true),
                (-16f, -9.5f, 0.85f, false), (-7.6f, -9.8f, 0.8f, true),
                (16.1f, -9.4f, 0.9f, true), (7.4f, -9.7f, 0.78f, false),
                (-16.4f, -17.2f, 0.9f, true), (-7.8f, -17.2f, 0.82f, false),
                (16.2f, -17f, 0.9f, false), (7.6f, -17f, 0.82f, true)
            };

            for (int i = 0; i < trees.Length; i++)
            {
                (float x, float z, float scale, bool pine) tree = trees[i];
                BuildTree(parent, $"Tree_{i + 1:00}", new Vector3(tree.x, 0.02f, tree.z), tree.scale, tree.pine);
            }

            Vector3[] bushes =
            {
                new(-10f, 0.02f, 11.4f), new(-13.8f, 0.02f, 11.4f), new(10f, 0.02f, 11.4f), new(13.7f, 0.02f, 11.3f),
                new(-10.1f, 0.02f, 2.8f), new(-13.6f, 0.02f, 2.7f), new(10f, 0.02f, 2.6f), new(13.7f, 0.02f, 2.6f),
                new(-10f, 0.02f, -6.9f), new(-13.5f, 0.02f, -6.9f), new(10f, 0.02f, -7.1f), new(13.5f, 0.02f, -7.1f),
                new(-9.8f, 0.02f, -16.2f), new(-13.4f, 0.02f, -16.2f), new(9.9f, 0.02f, -16.2f), new(13.5f, 0.02f, -16.2f)
            };

            for (int i = 0; i < bushes.Length; i++)
            {
                BuildBush(parent, bushes[i], 0.7f + i % 3 * 0.08f);
            }
        }

        private static void BuildStreetDetails(Transform parent)
        {
            BuildFence(parent, "Fence_LeftTop", new Vector3(-16.4f, 0f, 12f), 90f, 4.2f);
            BuildFence(parent, "Fence_RightTop", new Vector3(16.4f, 0f, 11.5f), 90f, 4f);
            BuildFence(parent, "Fence_LeftBottom", new Vector3(-16.3f, 0f, -12f), 90f, 4.2f);
            BuildFence(parent, "Fence_RightBottom", new Vector3(16.3f, 0f, -11.8f), 90f, 4f);

            Vector3[] stonePositions =
            {
                new(-15f, 0.26f, 3.4f), new(-8.6f, 0.22f, 6.6f), new(15.1f, 0.24f, -1.7f),
                new(8.8f, 0.2f, -11f), new(-15.1f, 0.21f, -15.5f), new(15.4f, 0.23f, 15.2f)
            };

            for (int i = 0; i < stonePositions.Length; i++)
            {
                GameObject stone = CreatePrimitive(PrimitiveType.Sphere, $"Stone_{i + 1:00}", parent, stonePositions[i], new Vector3(0.65f, 0.45f, 0.55f), LoadMaterial("M_ModularStone"));
                stone.transform.rotation = Quaternion.Euler(i * 7f, i * 31f, i * 11f);
            }
        }

        private static void BuildInvisibleBoundary(Transform parent)
        {
            const float innerHalfWidth = 18.35f;
            const float innerHalfDepth = 20.35f;
            const float wallThickness = 0.6f;
            const float wallHeight = 6f;
            float wallY = wallHeight * 0.5f - 0.02f;

            CreateBoundaryWall(parent, "Boundary_North", new Vector3(0f, wallY, innerHalfDepth + wallThickness * 0.5f), new Vector3(innerHalfWidth * 2f + wallThickness * 2f, wallHeight, wallThickness));
            CreateBoundaryWall(parent, "Boundary_South", new Vector3(0f, wallY, -innerHalfDepth - wallThickness * 0.5f), new Vector3(innerHalfWidth * 2f + wallThickness * 2f, wallHeight, wallThickness));
            CreateBoundaryWall(parent, "Boundary_East", new Vector3(innerHalfWidth + wallThickness * 0.5f, wallY, 0f), new Vector3(wallThickness, wallHeight, innerHalfDepth * 2f));
            CreateBoundaryWall(parent, "Boundary_West", new Vector3(-innerHalfWidth - wallThickness * 0.5f, wallY, 0f), new Vector3(wallThickness, wallHeight, innerHalfDepth * 2f));
        }

        private static void CreateBoundaryWall(Transform parent, string name, Vector3 localPosition, Vector3 size)
        {
            GameObject wall = new(name);
            wall.transform.SetParent(parent, false);
            wall.transform.localPosition = localPosition;
            BoxCollider collider = wall.AddComponent<BoxCollider>();
            collider.size = size;
            collider.isTrigger = false;
        }

        private static GameObject BuildHouse(Transform parent, string name, Vector3 position, float width, float depth, Material wall, bool upperFloor, bool garage, int variant)
        {
            Transform root = CreateRoot(name, parent);
            root.position = position;
            Material roof = LoadMaterial("M_ModularRoof");
            Material trim = LoadMaterial("M_ModularTrim");
            Material wood = LoadMaterial("M_ModularWood");
            float lowerHeight = 2.15f;
            float upperHeight = upperFloor ? 1.65f : 0f;

            CreateCube("Foundation", root, new Vector3(0f, 0.12f, 0f), new Vector3(width + 0.35f, 0.24f, depth + 0.35f), LoadMaterial("M_ModularStone"));
            CreateCube("MainWall", root, new Vector3(0f, 0.24f + lowerHeight * 0.5f, 0f), new Vector3(width, lowerHeight, depth), wall);
            if (upperFloor)
            {
                CreateCube("UpperWall", root, new Vector3(0f, 0.24f + lowerHeight + upperHeight * 0.5f, 0f), new Vector3(width * 0.82f, upperHeight, depth * 0.88f), wall);
            }

            float roofY = 0.24f + lowerHeight + upperHeight + 0.22f;
            GameObject roofLeft = CreateCube("RoofLeft", root, new Vector3(-width * 0.25f, roofY, 0f), new Vector3(width * 0.58f, 0.34f, depth + 0.6f), roof);
            GameObject roofRight = CreateCube("RoofRight", root, new Vector3(width * 0.25f, roofY, 0f), new Vector3(width * 0.58f, 0.34f, depth + 0.6f), roof);
            roofLeft.transform.localRotation = Quaternion.Euler(0f, 0f, 28f);
            roofRight.transform.localRotation = Quaternion.Euler(0f, 0f, -28f);

            float frontZ = -depth * 0.5f - 0.07f;
            CreateCube("Door", root, new Vector3(variant % 2 == 0 ? -width * 0.16f : width * 0.14f, 1f, frontZ), new Vector3(0.62f, 1.55f, 0.11f), wood);
            AddWindow(root, -width * 0.32f, 1.25f, frontZ, width * 0.22f, 0.72f);
            AddWindow(root, width * 0.32f, 1.25f, frontZ, width * 0.22f, 0.72f);
            if (upperFloor)
            {
                AddWindow(root, -width * 0.24f, 3f, frontZ * 0.9f, width * 0.17f, 0.62f);
                AddWindow(root, width * 0.24f, 3f, frontZ * 0.9f, width * 0.17f, 0.62f);
            }

            CreateCube("PorchSlab", root, new Vector3(0f, 0.22f, frontZ - 0.38f), new Vector3(width * 0.52f, 0.18f, 0.72f), trim);
            if (variant % 3 != 1)
            {
                CreateCube("PorchRoof", root, new Vector3(0f, 2.02f, frontZ - 0.35f), new Vector3(width * 0.58f, 0.16f, 0.92f), roof);
                CreateCube("PorchPostL", root, new Vector3(-width * 0.24f, 1.08f, frontZ - 0.47f), new Vector3(0.1f, 1.75f, 0.1f), trim);
                CreateCube("PorchPostR", root, new Vector3(width * 0.24f, 1.08f, frontZ - 0.47f), new Vector3(0.1f, 1.75f, 0.1f), trim);
            }

            if (garage)
            {
                float side = variant % 2 == 0 ? 1f : -1f;
                CreateCube("GarageWing", root, new Vector3(side * width * 0.47f, 1.15f, 0.15f), new Vector3(width * 0.46f, 2f, depth * 0.86f), wall);
                CreateCube("GarageDoor", root, new Vector3(side * width * 0.47f, 1f, frontZ - 0.05f), new Vector3(width * 0.34f, 1.45f, 0.11f), roof);
            }

            return root.gameObject;
        }

        private static void AddWindow(Transform parent, float x, float y, float z, float width, float height)
        {
            CreateCube("WindowTrim", parent, new Vector3(x, y, z + 0.02f), new Vector3(width + 0.18f, height + 0.18f, 0.07f), LoadMaterial("M_ModularTrim"));
            CreateCube("Window", parent, new Vector3(x, y, z - 0.04f), new Vector3(width, height, 0.08f), LoadMaterial("M_ModularWindow"));
        }

        private static void BuildTree(Transform parent, string name, Vector3 position, float scale, bool pine)
        {
            Transform root = CreateRoot(name, parent);
            root.position = position;
            root.localScale = Vector3.one * scale;
            CreatePrimitive(PrimitiveType.Cylinder, "Trunk", root, new Vector3(0f, 0.65f, 0f), new Vector3(0.28f, 0.65f, 0.28f), LoadMaterial("M_ModularTreeTrunk"));
            if (pine)
            {
                CreatePrimitive(PrimitiveType.Sphere, "PineLow", root, new Vector3(0f, 1.25f, 0f), new Vector3(1.05f, 1.15f, 1.05f), LoadMaterial("M_ModularLeafDark"));
                CreatePrimitive(PrimitiveType.Sphere, "PineMid", root, new Vector3(0f, 1.95f, 0f), new Vector3(0.76f, 1.05f, 0.76f), LoadMaterial("M_ModularLeafLight"));
                CreatePrimitive(PrimitiveType.Sphere, "PineTop", root, new Vector3(0f, 2.55f, 0f), new Vector3(0.48f, 0.82f, 0.48f), LoadMaterial("M_ModularLeafDark"));
            }
            else
            {
                CreatePrimitive(PrimitiveType.Sphere, "Crown", root, new Vector3(0f, 2.15f, 0f), new Vector3(1.45f, 1.65f, 1.45f), LoadMaterial("M_ModularLeafLight"));
                CreatePrimitive(PrimitiveType.Sphere, "CrownDark", root, new Vector3(-0.45f, 2f, 0.25f), new Vector3(0.78f, 1.05f, 0.82f), LoadMaterial("M_ModularLeafDark"));
            }
        }

        private static void BuildBush(Transform parent, Vector3 position, float scale)
        {
            Transform root = CreateRoot("Bush", parent);
            root.position = position;
            CreatePrimitive(PrimitiveType.Sphere, "BushA", root, new Vector3(-0.32f, 0.42f, 0f), new Vector3(0.72f, 0.62f, 0.72f) * scale, LoadMaterial("M_ModularLeafDark"));
            CreatePrimitive(PrimitiveType.Sphere, "BushB", root, new Vector3(0.3f, 0.48f, 0.08f), new Vector3(0.76f, 0.7f, 0.76f) * scale, LoadMaterial("M_ModularLeafLight"));
        }

        private static void BuildFence(Transform parent, string name, Vector3 position, float yaw, float length)
        {
            Transform root = CreateRoot(name, parent);
            root.position = position;
            root.rotation = Quaternion.Euler(0f, yaw, 0f);
            Material material = LoadMaterial("M_ModularWood");
            CreateCube("RailTop", root, new Vector3(0f, 0.85f, 0f), new Vector3(length, 0.12f, 0.12f), material);
            CreateCube("RailLow", root, new Vector3(0f, 0.42f, 0f), new Vector3(length, 0.12f, 0.12f), material);
            int posts = Mathf.Max(2, Mathf.CeilToInt(length / 1.4f));
            for (int i = 0; i < posts; i++)
            {
                float x = Mathf.Lerp(-length * 0.5f, length * 0.5f, i / (float)(posts - 1));
                CreateCube("Post", root, new Vector3(x, 0.62f, 0f), new Vector3(0.16f, 1.25f, 0.16f), material);
            }
        }

        private static GameObject CreateCube(string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            return CreatePrimitive(PrimitiveType.Cube, name, parent, localPosition, localScale, material);
        }

        private static GameObject CreatePrimitive(PrimitiveType primitiveType, string name, Transform parent, Vector3 localPosition, Vector3 localScale, Material material)
        {
            GameObject gameObject = GameObject.CreatePrimitive(primitiveType);
            gameObject.name = name;
            gameObject.transform.SetParent(parent, false);
            gameObject.transform.localPosition = localPosition;
            gameObject.transform.localScale = localScale;
            gameObject.GetComponent<MeshRenderer>().sharedMaterial = material;
            return gameObject;
        }

        private static Transform CreateRoot(string name, Transform parent)
        {
            GameObject gameObject = new(name);
            gameObject.transform.SetParent(parent, false);
            return gameObject.transform;
        }

        private static void DestroyChild(Transform parent, string name)
        {
            Transform child = parent.Find(name);
            if (child != null)
            {
                Object.DestroyImmediate(child.gameObject);
            }
        }

        private static void RemovePrimitiveColliders(Transform root)
        {
            Collider[] colliders = root.GetComponentsInChildren<Collider>(true);
            foreach (Collider collider in colliders)
            {
                if (collider.transform.IsChildOf(root.Find("InvisibleBoundary")))
                {
                    continue;
                }
                Object.DestroyImmediate(collider);
            }
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }
                current = next;
            }
        }

        private static void CreateMaterial(string name, Color color, float smoothness = 0.06f)
        {
            string path = $"{MaterialFolder}/{name}.mat";
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (material == null)
            {
                material = new Material(shader);
                AssetDatabase.CreateAsset(material, path);
            }
            material.shader = shader;
            material.SetColor("_BaseColor", color);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", smoothness);
            EditorUtility.SetDirty(material);
        }

        private static Material LoadMaterial(string name)
        {
            return AssetDatabase.LoadAssetAtPath<Material>($"{MaterialFolder}/{name}.mat");
        }
    }
}
