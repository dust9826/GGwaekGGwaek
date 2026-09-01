using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    public static class GiftBoxPrefabBuilder
    {
        public const string GiftPrefabPath = "Assets/Game/InGame/Delivery/Prefabs/PF_GiftBox_Variable.prefab";

        private const string BoxMaterialPath = "Assets/Game/InGame/Delivery/Materials/M_GiftBox_Colourable.mat";
        private const string RibbonMaterialPath = "Assets/Game/InGame/Delivery/Materials/M_GiftRibbon_Colourable.mat";
        private const string BowLoopMeshPath = "Assets/Game/InGame/Delivery/Meshes/SM_GiftBowLoop.asset";

        [InitializeOnLoadMethod]
        private static void QueueFirstBuild()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GiftPrefabPath) != null) return;
            EditorApplication.delayCall += BuildIfMissing;
        }

        private static void BuildIfMissing()
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(GiftPrefabPath) == null) BuildGiftPrefab();
        }

        [MenuItem("PPack/Delivery/Rebuild Variable Gift Box Prefab")]
        public static void BuildGiftPrefab()
        {
            Material boxMaterial = GetOrCreateMaterial(
                BoxMaterialPath, "M_GiftBox_Colourable", new Color(0.72f, 0.09f, 0.08f));
            Material ribbonMaterial = GetOrCreateMaterial(
                RibbonMaterialPath, "M_GiftRibbon_Colourable", new Color(1f, 0.68f, 0.16f));
            Mesh bowLoopMesh = GetOrCreateBowLoopMesh();

            var root = new GameObject("PF_GiftBox_Variable");
            try
            {
                BoxCollider boxCollider = root.AddComponent<BoxCollider>();
                root.AddComponent<Gift>();

                MeshRenderer body = CreatePart(root.transform, "Box", PrimitiveType.Cube, boxMaterial);
                MeshRenderer lid = CreatePart(root.transform, "Lid", PrimitiveType.Cube, boxMaterial);
                MeshRenderer ribbonWidth = CreatePart(root.transform, "Ribbon_Width", PrimitiveType.Cube, ribbonMaterial);
                MeshRenderer ribbonDepth = CreatePart(root.transform, "Ribbon_Depth", PrimitiveType.Cube, ribbonMaterial);
                MeshRenderer bowLeft = CreateMeshPart(root.transform, "Bow_Left", bowLoopMesh, ribbonMaterial);
                MeshRenderer bowRight = CreateMeshPart(root.transform, "Bow_Right", bowLoopMesh, ribbonMaterial);
                MeshRenderer bowKnot = CreatePart(root.transform, "Bow_Knot", PrimitiveType.Sphere, ribbonMaterial);

                GiftAppearance appearance = root.AddComponent<GiftAppearance>();
                appearance.Configure(
                    body.transform,
                    lid.transform,
                    ribbonWidth.transform,
                    ribbonDepth.transform,
                    bowLeft.transform,
                    bowRight.transform,
                    bowKnot.transform,
                    new Renderer[] { body, lid },
                    new Renderer[] { ribbonWidth, ribbonDepth, bowLeft, bowRight, bowKnot },
                    boxCollider,
                    false);
                appearance.ApplyAppearance(
                    new Vector3(0.72f, 0.55f, 0.66f),
                    new Color(0.72f, 0.09f, 0.08f),
                    new Color(1f, 0.68f, 0.16f));

                GameObject prefab = PrefabUtility.SaveAsPrefabAsset(root, GiftPrefabPath);
                if (prefab == null) throw new InvalidOperationException($"선물 프리팹 저장 실패: {GiftPrefabPath}");

                AssetDatabase.SaveAssets();
                Debug.Log($"Variable gift box prefab built: {GiftPrefabPath}");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(root);
            }
        }

        private static MeshRenderer CreatePart(
            Transform parent, string name, PrimitiveType primitiveType, Material material)
        {
            GameObject part = GameObject.CreatePrimitive(primitiveType);
            part.name = name;
            part.transform.SetParent(parent, false);

            Collider collider = part.GetComponent<Collider>();
            if (collider != null) UnityEngine.Object.DestroyImmediate(collider);

            MeshRenderer renderer = part.GetComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static MeshRenderer CreateMeshPart(
            Transform parent, string name, Mesh mesh, Material material)
        {
            var part = new GameObject(name);
            part.transform.SetParent(parent, false);
            MeshFilter filter = part.AddComponent<MeshFilter>();
            filter.sharedMesh = mesh;
            MeshRenderer renderer = part.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = material;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            return renderer;
        }

        private static Mesh GetOrCreateBowLoopMesh()
        {
            const string meshFolder = "Assets/Game/InGame/Delivery/Meshes";
            if (!AssetDatabase.IsValidFolder(meshFolder))
                AssetDatabase.CreateFolder("Assets/Game/InGame/Delivery", "Meshes");

            Mesh mesh = AssetDatabase.LoadAssetAtPath<Mesh>(BowLoopMeshPath);
            if (mesh == null)
            {
                mesh = new Mesh { name = "SM_GiftBowLoop" };
                AssetDatabase.CreateAsset(mesh, BowLoopMeshPath);
            }

            Vector2[] outline =
            {
                new Vector2(-0.50f, 0f),
                new Vector2(-0.25f, 0.48f),
                new Vector2(0.30f, 0.42f),
                new Vector2(0.50f, 0f),
                new Vector2(0.30f, -0.42f),
                new Vector2(-0.25f, -0.48f)
            };
            var vertices = new Vector3[outline.Length * 2];
            for (int index = 0; index < outline.Length; index++)
            {
                vertices[index] = new Vector3(outline[index].x, -0.5f, outline[index].y);
                vertices[index + outline.Length] = new Vector3(outline[index].x, 0.5f, outline[index].y);
            }

            var triangles = new int[60];
            int cursor = 0;
            for (int index = 1; index < outline.Length - 1; index++)
            {
                triangles[cursor++] = 0;
                triangles[cursor++] = index + 1;
                triangles[cursor++] = index;
                triangles[cursor++] = outline.Length;
                triangles[cursor++] = outline.Length + index;
                triangles[cursor++] = outline.Length + index + 1;
            }
            for (int index = 0; index < outline.Length; index++)
            {
                int next = (index + 1) % outline.Length;
                triangles[cursor++] = index;
                triangles[cursor++] = next;
                triangles[cursor++] = outline.Length + next;
                triangles[cursor++] = index;
                triangles[cursor++] = outline.Length + next;
                triangles[cursor++] = outline.Length + index;
            }

            mesh.Clear();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateNormals();
            mesh.RecalculateBounds();
            EditorUtility.SetDirty(mesh);
            return mesh;
        }

        private static Material GetOrCreateMaterial(string path, string materialName, Color defaultColor)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (material == null)
            {
                Shader shader = Shader.Find("Universal Render Pipeline/Lit");
                if (shader == null) throw new InvalidOperationException("URP Lit 셰이더를 찾을 수 없다");
                material = new Material(shader) { name = materialName };
                AssetDatabase.CreateAsset(material, path);
            }

            material.SetColor("_BaseColor", defaultColor);
            material.SetFloat("_Metallic", 0f);
            material.SetFloat("_Smoothness", 0.18f);
            EditorUtility.SetDirty(material);
            return material;
        }
    }
}
