using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;

namespace PPack
{
    public sealed class PrefabPaletteWindow : EditorWindow
    {
        private const string LastPaletteGuidKey = "PPack.LevelDesign.LastPaletteGuid";
        private const string DefaultPaletteGuid = "d67d771076fec41ac80fe4c2eba5d655";
        private const string DefaultPalettePath =
            "Assets/Game/InGame/Map/Editor/LevelDesign/Palettes/WinterVillagePrefabPalette.asset";
        private const float TileWidth = 86f;
        private const float TileHeight = 106f;

        [SerializeField] private PrefabPalette _palette;
        [SerializeField] private bool _editPalette;
        private Vector2 _scrollPosition;
        private UnityEditor.Editor _paletteEditor;

        [MenuItem("Window/PPack/Prefab Palette")]
        public static void Open()
        {
            PrefabPaletteWindow window = OpenWindow();
            window.Show();
        }

        [MenuItem("PPack/Level Design/Restore Winter Village Prefab Palette", priority = 11)]
        private static void RestoreDefaultPaletteFromMenu()
        {
            PrefabPaletteWindow window = OpenWindow();
            window.SetPalette(LoadDefaultPalette());
            window.Show();
        }

        private void OnEnable()
        {
            if (_palette != null)
            {
                RememberPalette(_palette);
                return;
            }

            string guid = SessionState.GetString(LastPaletteGuidKey, string.Empty);
            string path = AssetDatabase.GUIDToAssetPath(guid);
            PrefabPalette palette = AssetDatabase.LoadAssetAtPath<PrefabPalette>(path);
            SetPalette(palette != null ? palette : LoadDefaultPalette());
        }

        private void OnDisable()
        {
            DestroyPaletteEditor();
        }

        private void OnInspectorUpdate()
        {
            if (AssetPreview.IsLoadingAssetPreviews()) Repaint();
        }

        private void OnGUI()
        {
            EditorGUILayout.Space(6f);
            EditorGUILayout.LabelField("LEVEL PLACEMENT", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Choose a Prefab, activate the tool, then click a Collider in the Scene View.",
                MessageType.Info);

            EditorGUI.BeginChangeCheck();
            PrefabPalette newPalette = (PrefabPalette)EditorGUILayout.ObjectField(
                "Palette", _palette, typeof(PrefabPalette), false);
            if (EditorGUI.EndChangeCheck()) SetPalette(newPalette);

            if (_palette == null)
            {
                EditorGUILayout.HelpBox(
                    "The palette link was lost. Restore the Winter Village palette or create a new one.",
                    MessageType.Warning);
                if (GUILayout.Button("Restore Winter Village Palette"))
                {
                    SetPalette(LoadDefaultPalette());
                }
                if (GUILayout.Button("Create Prefab Palette")) CreatePalette();
                return;
            }

            DrawToolbar();
            DrawSelectedPrefab();

            _editPalette = EditorGUILayout.Foldout(_editPalette, "Edit Palette Contents", true);
            if (_editPalette)
            {
                UnityEditor.Editor.CreateCachedEditor(_palette, null, ref _paletteEditor);
                _paletteEditor.OnInspectorGUI();
                EditorGUILayout.Space(6f);
            }

            _scrollPosition = EditorGUILayout.BeginScrollView(_scrollPosition);
            DrawPaletteTiles();
            EditorGUILayout.EndScrollView();
        }

        private void DrawToolbar()
        {
            EditorGUILayout.BeginHorizontal();
            if (GUILayout.Button("Select Asset")) Selection.activeObject = _palette;
            if (GUILayout.Button("Activate Placement Tool")) ToolManager.SetActiveTool<PlacementTool>();
            EditorGUILayout.EndHorizontal();
        }

        private static void DrawSelectedPrefab()
        {
            GameObject selectedPrefab = PlacementTool.SelectedPrefab;
            EditorGUILayout.Space(4f);
            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.ObjectField("Selected", selectedPrefab, typeof(GameObject), false);
            }
        }

        private void DrawPaletteTiles()
        {
            foreach (PrefabPalette.Category category in _palette.Categories)
            {
                if (category == null) continue;

                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField(category.Name, EditorStyles.boldLabel);

                int columns = Mathf.Max(1, Mathf.FloorToInt((position.width - 34f) / TileWidth));
                int column = 0;
                foreach (GameObject prefab in category.Prefabs)
                {
                    if (prefab == null) continue;
                    if (column == 0) EditorGUILayout.BeginHorizontal();

                    DrawPrefabTile(prefab, category.TerrainPlacementMode);
                    column++;

                    if (column >= columns)
                    {
                        EditorGUILayout.EndHorizontal();
                        column = 0;
                    }
                }

                if (column != 0)
                {
                    GUILayout.FlexibleSpace();
                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private static void DrawPrefabTile(GameObject prefab, TerrainPlacementMode terrainPlacementMode)
        {
            bool selected = PlacementTool.SelectedPrefab == prefab;
            Color previousColor = GUI.backgroundColor;
            if (selected) GUI.backgroundColor = new Color(0.35f, 0.85f, 1f);

            EditorGUILayout.BeginVertical(GUILayout.Width(TileWidth), GUILayout.Height(TileHeight));
            Texture2D preview = AssetPreview.GetAssetPreview(prefab);
            if (preview == null) preview = AssetPreview.GetMiniThumbnail(prefab);

            GUIContent content = new GUIContent(preview, prefab.name);
            if (GUILayout.Button(content, GUILayout.Width(78f), GUILayout.Height(78f)))
            {
                PlacementTool.SelectPrefab(prefab, terrainPlacementMode);
                ToolManager.SetActiveTool<PlacementTool>();
            }

            GUILayout.Label(prefab.name, EditorStyles.miniLabel, GUILayout.Width(78f));
            EditorGUILayout.EndVertical();
            GUI.backgroundColor = previousColor;
        }

        private void CreatePalette()
        {
            string path = EditorUtility.SaveFilePanelInProject(
                "Create Prefab Palette",
                "PrefabPalette",
                "asset",
                "Choose where to save the level design Prefab Palette.");
            if (string.IsNullOrEmpty(path)) return;

            PrefabPalette palette = CreateInstance<PrefabPalette>();
            AssetDatabase.CreateAsset(palette, path);
            AssetDatabase.SaveAssets();
            SetPalette(palette);
            Selection.activeObject = palette;
        }

        private void SetPalette(PrefabPalette palette)
        {
            if (_palette == palette)
            {
                RememberPalette(palette);
                Repaint();
                return;
            }

            DestroyPaletteEditor();
            _palette = palette;
            RememberPalette(palette);
            Repaint();
        }

        private static PrefabPaletteWindow OpenWindow()
        {
            PrefabPaletteWindow window = GetWindow<PrefabPaletteWindow>();
            window.titleContent = new GUIContent("Prefab Palette", EditorGUIUtility.IconContent("Prefab Icon").image);
            window.minSize = new Vector2(300f, 320f);
            return window;
        }

        private static PrefabPalette LoadDefaultPalette()
        {
            string path = AssetDatabase.GUIDToAssetPath(DefaultPaletteGuid);
            if (string.IsNullOrEmpty(path)) path = DefaultPalettePath;
            return AssetDatabase.LoadAssetAtPath<PrefabPalette>(path);
        }

        private static void RememberPalette(PrefabPalette palette)
        {
            string path = palette == null ? string.Empty : AssetDatabase.GetAssetPath(palette);
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            SessionState.SetString(LastPaletteGuidKey, guid);
        }

        private void DestroyPaletteEditor()
        {
            if (_paletteEditor == null) return;

            DestroyImmediate(_paletteEditor);
            _paletteEditor = null;
        }
    }
}
