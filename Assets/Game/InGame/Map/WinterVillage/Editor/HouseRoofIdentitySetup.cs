using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace PPack.InGame.Map.WinterVillage.Editor
{
    internal static class HouseRoofIdentitySetup
    {
        private const string HousesFolder = "Assets/Game/InGame/Map/WinterVillage/Prefabs/Houses";
        private const string GeneratedFolder =
            "Assets/Game/InGame/Map/WinterVillage/Generated/HouseQuestRoofs";
        private const string OverlayName = "QuestRoofOverlay";
        private const float RoofHeightRatio = 0.5f;
        private const float MinimumUpwardNormal = 0.16f;

        [MenuItem("PPack/Winter Village/Setup Quest Roof Colors (Active Scene)")]
        public static void SetupActiveScene()
        {
            EnsureFolder(GeneratedFolder);
            string[] prefabGuids = AssetDatabase.FindAssets("t:Prefab", new[] { HousesFolder });
            int updatedPrefabs = 0;
            for (int i = 0; i < prefabGuids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(prefabGuids[i]);
                if (SetupPrefab(path)) updatedPrefabs++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            int assignedHouses = AssignActiveSceneIdentities();
            Debug.Log(
                $"[HouseRoofIdentity] Updated {updatedPrefabs} house prefabs and assigned IDs to " +
                $"{assignedHouses} houses in '{SceneManager.GetActiveScene().name}'.");
        }

        [MenuItem("PPack/Winter Village/Setup Quest Roof Colors (Active Scene)", true)]
        private static bool ValidateSetupActiveScene()
        {
            return !EditorApplication.isPlayingOrWillChangePlaymode &&
                   SceneManager.GetActiveScene().IsValid();
        }

        internal static int AssignActiveSceneIdentities()
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid() || !scene.isLoaded) return 0;

            List<HouseRoofIdentity> houses = scene.GetRootGameObjects()
                .SelectMany(root => root.GetComponentsInChildren<HouseRoofIdentity>(true))
                .OrderBy(house => house.transform.position.z)
                .ThenBy(house => house.transform.position.x)
                .ToList();

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Assign House Quest Roof Identities");
            for (int i = 0; i < houses.Count; i++)
            {
                HouseRoofIdentity house = houses[i];
                Undo.RecordObject(house, "Assign House Quest Roof Identity");
                house.ConfigureIdentity($"House_{i + 1:00}", Color.white, false);
                EditorUtility.SetDirty(house);
                PrefabUtility.RecordPrefabInstancePropertyModifications(house);
            }
            Undo.CollapseUndoOperations(undoGroup);

            if (houses.Count > 0)
            {
                EditorSceneManager.MarkSceneDirty(scene);
                EditorSceneManager.SaveScene(scene);
                SceneView.RepaintAll();
            }
            return houses.Count;
        }

        private static bool SetupPrefab(string prefabPath)
        {
            GameObject root = PrefabUtility.LoadPrefabContents(prefabPath);
            bool restoreReadability = false;
            string sourceAssetPath = null;
            try
            {
                MeshRenderer sourceRenderer = FindSourceRenderer(root);
                MeshFilter sourceFilter = sourceRenderer == null
                    ? null
                    : sourceRenderer.GetComponent<MeshFilter>();
                if (sourceFilter == null || sourceFilter.sharedMesh == null)
                {
                    Debug.LogWarning($"[HouseRoofIdentity] No source house mesh found in {prefabPath}.");
                    return false;
                }

                Mesh sourceMesh = ResolveOriginalMesh(sourceFilter);
                sourceAssetPath = AssetDatabase.GetAssetPath(sourceMesh);
                ModelImporter importer = AssetImporter.GetAtPath(sourceAssetPath) as ModelImporter;
                if (!sourceMesh.isReadable && importer != null)
                {
                    PrefabUtility.UnloadPrefabContents(root);
                    root = null;
                    importer.isReadable = true;
                    importer.SaveAndReimport();
                    restoreReadability = true;

                    root = PrefabUtility.LoadPrefabContents(prefabPath);
                    sourceRenderer = FindSourceRenderer(root);
                    sourceFilter = sourceRenderer == null
                        ? null
                        : sourceRenderer.GetComponent<MeshFilter>();
                    sourceMesh = ResolveOriginalMesh(sourceFilter);
                }

                if (sourceRenderer == null || sourceMesh == null || !sourceMesh.isReadable)
                {
                    Debug.LogWarning($"[HouseRoofIdentity] House mesh is not readable: {prefabPath}.");
                    return false;
                }

                string meshPath = $"{GeneratedFolder}/{SanitizeFileName(root.name)}_Roof.asset";
                Mesh separatedMesh = BuildRoofColorMesh(sourceMesh, root.name, out Bounds roofLocalBounds);
                if (separatedMesh == null)
                {
                    Debug.LogWarning($"[HouseRoofIdentity] Roof faces were not detected in {prefabPath}.");
                    return false;
                }
                Mesh savedMesh = SaveOrReplaceMesh(meshPath, separatedMesh);

                Transform overlayTransform = root.transform.Find(OverlayName);
                if (overlayTransform != null) Object.DestroyImmediate(overlayTransform.gameObject);

                int originalSubMeshCount = sourceMesh.subMeshCount;
                Material[] originalMaterials = GetOriginalMaterials(sourceRenderer, originalSubMeshCount);
                sourceFilter.sharedMesh = savedMesh;
                sourceRenderer.sharedMaterials = BuildSeparatedMaterials(
                    originalMaterials,
                    originalSubMeshCount);
                int[] roofMaterialIndices = Enumerable.Range(
                    originalSubMeshCount,
                    originalSubMeshCount).ToArray();

                HouseRoofIdentity identity =
                    root.GetComponent<HouseRoofIdentity>() ?? root.AddComponent<HouseRoofIdentity>();
                identity.ConfigureRoofMaterials(
                    new Renderer[] { sourceRenderer },
                    roofMaterialIndices,
                    roofLocalBounds);
                identity.ConfigureIdentity(root.name, Color.white, false);

                PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
                return true;
            }
            finally
            {
                if (root != null) PrefabUtility.UnloadPrefabContents(root);
                if (restoreReadability && !string.IsNullOrEmpty(sourceAssetPath) &&
                    AssetImporter.GetAtPath(sourceAssetPath) is ModelImporter importer)
                {
                    importer.isReadable = false;
                    importer.SaveAndReimport();
                }
            }
        }

        private static MeshRenderer FindSourceRenderer(GameObject root)
        {
            return root.GetComponentsInChildren<MeshRenderer>(true)
                .Where(renderer => renderer.name != OverlayName)
                .Where(renderer => renderer.GetComponent<MeshFilter>()?.sharedMesh != null)
                .OrderByDescending(renderer => renderer.bounds.size.sqrMagnitude)
                .FirstOrDefault();
        }

        private static Mesh ResolveOriginalMesh(MeshFilter filter)
        {
            if (filter == null) return null;
            MeshFilter sourceFilter = PrefabUtility.GetCorrespondingObjectFromSource(filter) as MeshFilter;
            return sourceFilter != null && sourceFilter.sharedMesh != null
                ? sourceFilter.sharedMesh
                : filter.sharedMesh;
        }

        private static Material[] GetOriginalMaterials(MeshRenderer renderer, int subMeshCount)
        {
            Material[] current = renderer.sharedMaterials;
            var result = new Material[subMeshCount];
            for (int index = 0; index < result.Length; index++)
            {
                if (current.Length == 0) break;
                result[index] = current[Mathf.Min(index, current.Length - 1)];
            }
            return result;
        }

        private static Material[] BuildSeparatedMaterials(Material[] original, int subMeshCount)
        {
            var materials = new Material[subMeshCount * 2];
            for (int index = 0; index < subMeshCount; index++)
            {
                Material material = index < original.Length ? original[index] : null;
                materials[index] = material;
                materials[subMeshCount + index] = material;
            }
            return materials;
        }

        private static Mesh BuildRoofColorMesh(
            Mesh source,
            string houseName,
            out Bounds roofLocalBounds)
        {
            roofLocalBounds = default;
            Mesh separated = Object.Instantiate(source);
            // NativeFormatImporter는 메인 오브젝트 이름과 파일명이 같을 때 경고를 내지 않는다.
            separated.name = $"{houseName}_Roof";
            Bounds bounds = source.bounds;
            float minimumY = bounds.min.y + bounds.size.y * RoofHeightRatio;
            Vector3[] vertices = source.vertices;
            int selectedTriangleCount = 0;
            bool hasRoofBounds = false;
            int originalSubMeshCount = source.subMeshCount;
            separated.subMeshCount = originalSubMeshCount * 2;

            for (int subMesh = 0; subMesh < originalSubMeshCount; subMesh++)
            {
                int[] sourceTriangles = source.GetTriangles(subMesh);
                List<int> bodyTriangles = new(sourceTriangles.Length);
                List<int> roofTriangles = new(sourceTriangles.Length / 2);
                for (int i = 0; i + 2 < sourceTriangles.Length; i += 3)
                {
                    int a = sourceTriangles[i];
                    int b = sourceTriangles[i + 1];
                    int c = sourceTriangles[i + 2];
                    Vector3 center = (vertices[a] + vertices[b] + vertices[c]) / 3f;
                    Vector3 faceNormal = Vector3.Cross(vertices[b] - vertices[a], vertices[c] - vertices[a]);
                    bool isRoof = center.y >= minimumY && faceNormal.sqrMagnitude >= 0.000001f &&
                                  faceNormal.normalized.y >= MinimumUpwardNormal;
                    if (!isRoof)
                    {
                        bodyTriangles.Add(a);
                        bodyTriangles.Add(b);
                        bodyTriangles.Add(c);
                        continue;
                    }

                    roofTriangles.Add(a);
                    roofTriangles.Add(b);
                    roofTriangles.Add(c);
                    selectedTriangleCount++;

                    EncapsulatePoint(ref roofLocalBounds, ref hasRoofBounds, vertices[a]);
                    EncapsulatePoint(ref roofLocalBounds, ref hasRoofBounds, vertices[b]);
                    EncapsulatePoint(ref roofLocalBounds, ref hasRoofBounds, vertices[c]);
                }
                separated.SetTriangles(bodyTriangles, subMesh, false);
                separated.SetTriangles(roofTriangles, originalSubMeshCount + subMesh, false);
            }

            if (selectedTriangleCount == 0)
            {
                Object.DestroyImmediate(separated);
                return null;
            }

            // 정점을 복제하거나 밀어내지 않는다. 원본 표면을 두 서브메시가 나눠 가지므로 겹침이 없다.
            separated.RecalculateBounds();
            return separated;
        }

        private static void EncapsulatePoint(ref Bounds bounds, ref bool hasBounds, Vector3 point)
        {
            if (!hasBounds)
            {
                bounds = new Bounds(point, Vector3.zero);
                hasBounds = true;
                return;
            }
            bounds.Encapsulate(point);
        }

        private static Mesh SaveOrReplaceMesh(string path, Mesh generated)
        {
            Mesh existing = AssetDatabase.LoadAssetAtPath<Mesh>(path);
            if (existing == null)
            {
                AssetDatabase.CreateAsset(generated, path);
                return generated;
            }

            EditorUtility.CopySerialized(generated, existing);
            Object.DestroyImmediate(generated);
            EditorUtility.SetDirty(existing);
            return existing;
        }

        private static string SanitizeFileName(string value)
        {
            foreach (char invalid in Path.GetInvalidFileNameChars()) value = value.Replace(invalid, '_');
            return value;
        }

        private static void EnsureFolder(string path)
        {
            string[] parts = path.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next)) AssetDatabase.CreateFolder(current, parts[i]);
                current = next;
            }
        }
    }

    [CustomEditor(typeof(HouseRoofIdentity))]
    internal sealed class HouseRoofIdentityEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_houseId"), new GUIContent("House ID"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_useRoofColor"), new GUIContent("Use Roof Color"));
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_roofColor"), new GUIContent("Roof Color"));
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_isQuestSender"), new GUIContent("Quest Sender"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_questHighlightColor"),
                new GUIContent("Quest Highlight"));
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_questHighlightBlend"),
                new GUIContent("Quest Glow Strength"));
            EditorGUILayout.Space(4f);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_roofRenderers"),
                new GUIContent("Roof Renderers"),
                true);
            EditorGUILayout.PropertyField(
                serializedObject.FindProperty("_roofMaterialIndices"),
                new GUIContent("Roof Material Slots"),
                true);

            if (serializedObject.ApplyModifiedProperties())
            {
                foreach (Object selected in targets)
                {
                    if (selected is HouseRoofIdentity identity) identity.ApplyRoofAppearance();
                }
                SceneView.RepaintAll();
            }

            EditorGUILayout.Space(8f);
            EditorGUILayout.HelpBox(
                "The original house mesh is split into body and roof material slots without an overlay. " +
                "Active delivery quests tint only the roof slot, preserving its texture and lighting.",
                MessageType.Info);
        }
    }
}
