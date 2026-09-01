using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class RoadBuilderPreferences
    {
        private const string MainWidthKey = "PPack.LevelDesign.MainRoadWidth";
        private const string EntranceWidthKey = "PPack.LevelDesign.EntranceRoadWidth";
        private const string EdgeFeatherKey = "PPack.LevelDesign.RoadEdgeFeather";
        private const string BorderWidthKey = "PPack.LevelDesign.RoadBorderWidth";
        private const string GradingShoulderKey = "PPack.LevelDesign.RoadGradingShoulder";
        private const string MaximumGradeKey = "PPack.LevelDesign.RoadMaximumGrade";

        public static float MainRoadWidth
        {
            get => EditorPrefs.GetFloat(MainWidthKey, 4.5f);
            set => EditorPrefs.SetFloat(MainWidthKey, Mathf.Clamp(value, 1f, 12f));
        }

        public static float EntranceRoadWidth
        {
            get => EditorPrefs.GetFloat(EntranceWidthKey, 1.8f);
            set => EditorPrefs.SetFloat(EntranceWidthKey, Mathf.Clamp(value, 0.6f, 6f));
        }

        public static float EdgeFeather
        {
            get => Mathf.Clamp(EditorPrefs.GetFloat(EdgeFeatherKey, 0.12f), 0.02f, 0.35f);
            set => EditorPrefs.SetFloat(EdgeFeatherKey, Mathf.Clamp(value, 0.02f, 0.35f));
        }

        public static float BorderWidth
        {
            get => EditorPrefs.GetFloat(BorderWidthKey, 1.5f);
            set => EditorPrefs.SetFloat(BorderWidthKey, Mathf.Clamp(value, 0.5f, 3f));
        }

        public static float BorderFeather => Mathf.Min(0.1f, EdgeFeather);

        public static float GradingShoulder
        {
            get => EditorPrefs.GetFloat(GradingShoulderKey, 3f);
            set => EditorPrefs.SetFloat(GradingShoulderKey, Mathf.Clamp(value, 0.5f, 10f));
        }

        public static float MaximumGrade
        {
            get => EditorPrefs.GetFloat(MaximumGradeKey, 12f);
            set => EditorPrefs.SetFloat(MaximumGradeKey, Mathf.Clamp(value, 2f, 25f));
        }
    }
}
