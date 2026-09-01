using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class EasyRoadTemplateAssets
    {
        internal const string DefaultTemplatePath =
            "Assets/Game/InGame/Map/Road/Templates/ERT_WinterVillagePackedSnow.asset";
        private const string TemplateFolder = "Assets/Game/InGame/Map/Road/Templates";
        private const string CurrentRoadMaterialPath =
            "Assets/Game/InGame/Map/WinterVillage/Materials/RoadFirstWorld/M_RoadFirst_EasyRoadsPackedSnow.mat";

        [MenuItem("PPack/Level Design/Roads/Setup EasyRoads Village Template")]
        public static void SetupFromMenu()
        {
            EasyRoadTemplate template = GetOrCreateDefaultTemplate();
            Selection.activeObject = template;
            EditorGUIUtility.PingObject(template);
            Debug.Log(
                $"EasyRoads template ready: {template.name}, width={template.DefaultWidth:0.0}m, " +
                $"material={(template.RoadMaterial == null ? "missing" : template.RoadMaterial.name)}.");
        }

        public static EasyRoadTemplate GetOrCreateDefaultTemplate()
        {
            EasyRoadTemplate template = AssetDatabase.LoadAssetAtPath<EasyRoadTemplate>(DefaultTemplatePath);
            if (template != null)
            {
                RepairMissingMaterial(template);
                return template;
            }

            EnsureFolder(TemplateFolder);
            template = ScriptableObject.CreateInstance<EasyRoadTemplate>();
            template.name = "ERT_WinterVillagePackedSnow";
            AssignMaterial(template);
            AssetDatabase.CreateAsset(template, DefaultTemplatePath);
            AssetDatabase.SaveAssetIfDirty(template);
            return template;
        }

        [MenuItem("PPack/Level Design/Roads/Validate EasyRoads Template Tool")]
        public static void ValidateFromMenu()
        {
            EasyRoadTemplate template = GetOrCreateDefaultTemplate();
            if (template == null || template.RoadMaterial == null)
            {
                Debug.LogError("[EasyRoadTool] Default Winter Village template or its road material is missing.");
                return;
            }

            Debug.Log(
                $"[EasyRoadTool] Validation passed: template={template.name}, " +
                $"type={template.RoadTypeName}, width={template.DefaultWidth:0.00}m, " +
                $"grade={template.MaximumGrade:0.0}deg, shoulder={template.MinimumShoulder:0.0}-" +
                $"{template.MaximumShoulder:0.0}m, material={template.RoadMaterial.name}.");
        }

        private static void RepairMissingMaterial(EasyRoadTemplate template)
        {
            if (template.RoadMaterial != null) return;
            AssignMaterial(template);
            EditorUtility.SetDirty(template);
            AssetDatabase.SaveAssetIfDirty(template);
        }

        private static void AssignMaterial(EasyRoadTemplate template)
        {
            Material material = AssetDatabase.LoadAssetAtPath<Material>(CurrentRoadMaterialPath);
            if (material == null)
            {
                Debug.LogError(
                    $"Current Road First World EasyRoads material was not found at {CurrentRoadMaterialPath}.");
                return;
            }

            SerializedObject serialized = new(template);
            serialized.FindProperty("_roadMaterial").objectReferenceValue = material;
            serialized.ApplyModifiedPropertiesWithoutUndo();
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
