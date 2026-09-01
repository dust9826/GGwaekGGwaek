using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class EasyRoadBuilderPreferences
    {
        private const string TemplateGuidKey = "PPack.LevelDesign.EasyRoadTemplateGuid";
        private const string WidthKey = "PPack.LevelDesign.EasyRoadWidth";

        public static EasyRoadTemplate Template
        {
            get
            {
                string guid = SessionState.GetString(TemplateGuidKey, string.Empty);
                string path = AssetDatabase.GUIDToAssetPath(guid);
                EasyRoadTemplate template = AssetDatabase.LoadAssetAtPath<EasyRoadTemplate>(path);
                return template != null ? template : EasyRoadTemplateAssets.GetOrCreateDefaultTemplate();
            }
            set
            {
                string path = value == null ? string.Empty : AssetDatabase.GetAssetPath(value);
                string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
                SessionState.SetString(TemplateGuidKey, guid);
                if (value != null) Width = value.DefaultWidth;
            }
        }

        public static float Width
        {
            get
            {
                EasyRoadTemplate template = Template;
                float fallback = template == null ? 4.8f : template.DefaultWidth;
                return Mathf.Clamp(EditorPrefs.GetFloat(WidthKey, fallback), 0.5f, 12f);
            }
            set => EditorPrefs.SetFloat(WidthKey, Mathf.Clamp(value, 0.5f, 12f));
        }
    }
}
