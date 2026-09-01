#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    public static class WinterVillageDeliveryMapKitBuilder
    {
        private const string SourceScenePath =
            "Assets/Game/InGame/Delivery/Tests/Delivery_RequestFlow_Test.unity";
        private const string CatalogRoot =
            "Assets/Game/InGame/Map/WinterVillage/Prefabs/DeliveryMapKit";

        [MenuItem("PPack/Map/Winter Village/Build Delivery Map Prefab Kit")]
        public static void Build()
        {
            SceneAsset sourceAsset = AssetDatabase.LoadAssetAtPath<SceneAsset>(SourceScenePath);
            if (sourceAsset == null)
                throw new InvalidOperationException("배송 통합 씬을 찾을 수 없다: " + SourceScenePath);

            EnsureFolder(CatalogRoot);
            foreach (string category in Categories()) EnsureFolder(CatalogRoot + "/" + category);

            Scene sourceScene = SceneManager.GetSceneByPath(SourceScenePath);
            bool openedHere = !sourceScene.IsValid() || !sourceScene.isLoaded;
            if (openedHere)
                sourceScene = EditorSceneManager.OpenScene(SourceScenePath, OpenSceneMode.Additive);

            Scene previewScene = EditorSceneManager.NewPreviewScene();
            try
            {
                List<string> sourcePaths = CollectDirectPrefabPaths(sourceScene);
                var desiredPaths = new HashSet<string>(StringComparer.Ordinal);
                var usedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (string sourcePath in sourcePaths)
                {
                    string category = CategoryFor(sourcePath);
                    string name = CatalogName(sourcePath);
                    if (!usedNames.Add(category + "/" + name))
                        name += "_" + AssetDatabase.AssetPathToGUID(sourcePath).Substring(0, 8);

                    string destination = CatalogRoot + "/" + category + "/" + name + ".prefab";
                    desiredPaths.Add(destination);
                    CreateOrUpdateVariant(sourcePath, destination, previewScene);
                }

                RemoveStaleVariants(desiredPaths);
                AssetDatabase.SaveAssets();
                AssetDatabase.Refresh();
                Debug.Log($"[WinterVillageDeliveryMapKit] Built {sourcePaths.Count} prefab variants at {CatalogRoot}");
            }
            finally
            {
                EditorSceneManager.ClosePreviewScene(previewScene);
                if (openedHere && sourceScene.IsValid() && sourceScene.isLoaded)
                    EditorSceneManager.CloseScene(sourceScene, true);
            }
        }

        [MenuItem("PPack/Map/Winter Village/Open Delivery Map Prefab Kit")]
        public static void OpenCatalog()
        {
            DefaultAsset folder = AssetDatabase.LoadAssetAtPath<DefaultAsset>(CatalogRoot);
            if (folder == null) throw new InvalidOperationException("맵 키트 폴더가 없다. 먼저 Build 메뉴를 실행하세요.");
            Selection.activeObject = folder;
            EditorGUIUtility.PingObject(folder);
        }

        private static List<string> CollectDirectPrefabPaths(Scene scene)
        {
            var paths = new HashSet<string>(StringComparer.Ordinal);
            foreach (GameObject root in scene.GetRootGameObjects())
            {
                foreach (Transform child in root.GetComponentsInChildren<Transform>(true))
                {
                    GameObject instance = child.gameObject;
                    if (!PrefabUtility.IsAnyPrefabInstanceRoot(instance)) continue;
                    if (child.parent != null && PrefabUtility.IsPartOfPrefabInstance(child.parent.gameObject))
                        continue;

                    string path = PrefabUtility.GetPrefabAssetPathOfNearestInstanceRoot(instance);
                    if (!string.IsNullOrEmpty(path) && path.EndsWith(".prefab", StringComparison.OrdinalIgnoreCase))
                        paths.Add(path);
                }
            }

            return paths.OrderBy(path => path, StringComparer.Ordinal).ToList();
        }

        private static void CreateOrUpdateVariant(string sourcePath, string destination, Scene previewScene)
        {
            GameObject source = AssetDatabase.LoadAssetAtPath<GameObject>(sourcePath);
            if (source == null) throw new InvalidOperationException("프리팹을 불러올 수 없다: " + sourcePath);

            GameObject instance = PrefabUtility.InstantiatePrefab(source, previewScene) as GameObject;
            if (instance == null) throw new InvalidOperationException("프리팹 인스턴스 생성 실패: " + sourcePath);

            try
            {
                instance.name = Path.GetFileNameWithoutExtension(destination);
                if (PrefabUtility.SaveAsPrefabAsset(instance, destination) == null)
                    throw new InvalidOperationException("맵 키트 Variant 저장 실패: " + destination);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(instance);
            }
        }

        private static void RemoveStaleVariants(HashSet<string> desiredPaths)
        {
            foreach (string guid in AssetDatabase.FindAssets("t:Prefab", new[] { CatalogRoot }))
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (!desiredPaths.Contains(path)) AssetDatabase.DeleteAsset(path);
            }
        }

        private static string CatalogName(string sourcePath)
        {
            string name = Path.GetFileNameWithoutExtension(sourcePath);
            if (name.StartsWith("PF_", StringComparison.OrdinalIgnoreCase)) name = name.Substring(3);

            char[] characters = name.Select(character =>
                char.IsLetterOrDigit(character) ? character : '_').ToArray();
            string sanitized = new string(characters);
            while (sanitized.Contains("__")) sanitized = sanitized.Replace("__", "_");
            return "PF_MapKit_" + sanitized.Trim('_');
        }

        private static string CategoryFor(string path)
        {
            string lower = path.ToLowerInvariant();
            if (lower.Contains("/houses/")) return "01_Buildings";
            if (lower.Contains("/trees/") || lower.Contains("/plants/") || lower.Contains("/stones/") ||
                lower.Contains("/nature/") || lower.Contains("/animals/")) return "02_Nature";
            if (lower.Contains("/vehicles/") || lower.Contains("/vehicle/")) return "04_Vehicles";
            if (lower.Contains("/lighting/") || lower.Contains("/vfx/")) return "05_Lighting_VFX";
            if (lower.Contains("/penguin/") || lower.Contains("/player/") || lower.Contains("/snow/prefabs/"))
                return "06_Gameplay";
            return "03_Props";
        }

        private static IEnumerable<string> Categories()
        {
            yield return "01_Buildings";
            yield return "02_Nature";
            yield return "03_Props";
            yield return "04_Vehicles";
            yield return "05_Lighting_VFX";
            yield return "06_Gameplay";
        }

        private static void EnsureFolder(string folder)
        {
            if (AssetDatabase.IsValidFolder(folder)) return;
            string parent = Path.GetDirectoryName(folder)?.Replace('\\', '/');
            string name = Path.GetFileName(folder);
            if (string.IsNullOrEmpty(parent) || string.IsNullOrEmpty(name))
                throw new InvalidOperationException("폴더 경로가 잘못됐다: " + folder);
            EnsureFolder(parent);
            AssetDatabase.CreateFolder(parent, name);
        }
    }
}
#endif
