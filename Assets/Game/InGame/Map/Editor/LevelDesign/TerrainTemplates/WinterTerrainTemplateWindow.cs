using System;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    internal sealed class WinterTerrainTemplateWindow : EditorWindow
    {
        private WinterTerrainTemplateProfile[] _profiles = Array.Empty<WinterTerrainTemplateProfile>();
        private int _selectedProfile;

        [MenuItem("PPack/Level Design/Open Terrain Templates")]
        internal static void Open()
        {
            WinterTerrainTemplateWindow window = GetWindow<WinterTerrainTemplateWindow>("Terrain Templates");
            window.minSize = new Vector2(360f, 310f);
            window.Show();
        }

        private void OnEnable()
        {
            ReloadProfiles();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(8f);
            EditorGUILayout.LabelField("WINTER TERRAIN STARTERS", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "TerrainData, five matte winter paint layers, Road Builder baseline, and the standard map hierarchy are created together.",
                MessageType.Info);

            if (_profiles.Length == 0)
            {
                EditorGUILayout.HelpBox("The included profiles have not been created yet.", MessageType.Warning);
                if (GUILayout.Button("Setup Included Templates", GUILayout.Height(32f)))
                {
                    WinterTerrainTemplateGenerator.SetupIncludedTemplates();
                    ReloadProfiles();
                }
                return;
            }

            string[] labels = new string[_profiles.Length];
            for (int i = 0; i < _profiles.Length; i++) labels[i] = _profiles[i].name;
            _selectedProfile = EditorGUILayout.Popup("Template", _selectedProfile, labels);
            WinterTerrainTemplateProfile profile = _profiles[_selectedProfile];

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.EnumPopup("Shape", profile.Shape);
                EditorGUILayout.Vector3Field("Terrain Size", profile.Size);
                EditorGUILayout.IntField("Height Resolution", profile.HeightmapResolution);
                EditorGUILayout.IntField("Paint Resolution", profile.AlphamapResolution);
            }

            EditorGUILayout.Space(6f);
            DrawDescription(profile.Shape);
            EditorGUILayout.Space(6f);

            if (GUILayout.Button("Create Editable Terrain In Active Scene", GUILayout.Height(38f)))
                CreateTerrain(profile);

            EditorGUILayout.HelpBox(
                "The new Terrain is centered at world origin and remains a normal editable Unity Terrain. " +
                "Use Paint Terrain, the Prefab Palette, and Road Builder after creation.",
                MessageType.None);
        }

        private static void DrawDescription(EWinterTerrainTemplateShape shape)
        {
            string description = shape switch
            {
                EWinterTerrainTemplateShape.VillageBasin =>
                    "Flat village core with gently raised outer terrain. Best for houses, plazas, and short roads.",
                EWinterTerrainTemplateShape.AlpineHillside =>
                    "Continuous low-to-high district slope with a broad buildable shelf.",
                _ =>
                    "Pronounced mountain slope with a smooth central piste and raised side ridges."
            };
            EditorGUILayout.HelpBox(description, MessageType.None);
        }

        private static void CreateTerrain(WinterTerrainTemplateProfile profile)
        {
            Scene scene = SceneManager.GetActiveScene();
            if (!scene.IsValid())
            {
                EditorUtility.DisplayDialog("Terrain Templates", "Open a Scene first.", "OK");
                return;
            }

            string defaultName = $"TD_{profile.name.Replace("TP_", string.Empty)}";
            string path = EditorUtility.SaveFilePanelInProject(
                "Create editable TerrainData",
                defaultName,
                "asset",
                "Choose where the new editable TerrainData should be saved.",
                "Assets/Game/InGame/Map/WinterVillage/Generated/TerrainTemplates");
            if (string.IsNullOrEmpty(path)) return;

            try
            {
                WinterTerrainTemplateGenerator.CreateTerrainInScene(profile, path, scene);
            }
            catch (Exception exception)
            {
                Debug.LogException(exception);
                EditorUtility.DisplayDialog("Terrain Templates", exception.Message, "OK");
            }
        }

        private void ReloadProfiles()
        {
            WinterTerrainTemplateGenerator.SetupIncludedTemplates();
            string[] guids = AssetDatabase.FindAssets(
                "t:WinterTerrainTemplateProfile",
                new[] { WinterTerrainTemplateGenerator.ProfilesFolder });
            _profiles = new WinterTerrainTemplateProfile[guids.Length];
            for (int i = 0; i < guids.Length; i++)
            {
                string path = AssetDatabase.GUIDToAssetPath(guids[i]);
                _profiles[i] = AssetDatabase.LoadAssetAtPath<WinterTerrainTemplateProfile>(path);
            }
            Array.Sort(_profiles, (a, b) => string.CompareOrdinal(a.name, b.name));
            _selectedProfile = Mathf.Clamp(_selectedProfile, 0, Mathf.Max(0, _profiles.Length - 1));
            Repaint();
        }
    }
}
