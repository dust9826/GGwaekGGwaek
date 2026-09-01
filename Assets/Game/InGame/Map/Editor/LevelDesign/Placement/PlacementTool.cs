using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    [EditorTool("Level Placement")]
    public sealed class PlacementTool : EditorTool
    {
        private const string SelectedPrefabGuidKey = "PPack.LevelDesign.SelectedPrefabGuid";
        private const string SelectedTerrainModeKey = "PPack.LevelDesign.SelectedTerrainMode";
        private const string PlacementYawKey = "PPack.LevelDesign.PlacementYaw";
        private const string UndoLabel = "Place Level Prefab";
        private const float RotationStep = 15f;
        private const float FastRotationStep = 45f;
        private static readonly Color SurfaceMarkerColor = new Color(0.1f, 0.82f, 1f, 0.9f);

        private static GameObject _selectedPrefab;
        private static TerrainPlacementMode _selectedTerrainMode;
        private PlacementPreview _preview;
        private TerrainFlattenPreview _terrainPreview;
        private HouseSidewalkPreview _sidewalkPreview;
        private RoadEntranceDatabase _entranceDatabase;
        private GameObject _previewPrefab;
        private TerrainFlattenPlan _terrainPlan;
        private RaycastHit _surfaceHit;
        private bool _hasSurfaceHit;
        private float _placementYaw;
        private GUIContent _toolbarIcon;

        public static GameObject SelectedPrefab
        {
            get
            {
                if (_selectedPrefab != null) return _selectedPrefab;

                string guid = SessionState.GetString(SelectedPrefabGuidKey, string.Empty);
                if (string.IsNullOrEmpty(guid)) return null;

                string path = AssetDatabase.GUIDToAssetPath(guid);
                _selectedPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(path);
                return _selectedPrefab;
            }
        }

        public static TerrainPlacementMode SelectedTerrainMode
        {
            get
            {
                _selectedTerrainMode = (TerrainPlacementMode)SessionState.GetInt(
                    SelectedTerrainModeKey,
                    (int)_selectedTerrainMode);
                return _selectedTerrainMode;
            }
        }

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_toolbarIcon != null) return _toolbarIcon;

                GUIContent prefabIcon = EditorGUIUtility.IconContent("Prefab Icon");
                _toolbarIcon = new GUIContent(prefabIcon.image, "Level Placement");
                return _toolbarIcon;
            }
        }

        [MenuItem("PPack/Level Design/Open Prefab Palette")]
        private static void OpenPalette()
        {
            PrefabPaletteWindow.Open();
        }

        [MenuItem("PPack/Level Design/Activate Placement Tool")]
        private static void ActivateFromMenu()
        {
            ToolManager.SetActiveTool<PlacementTool>();
        }

        public static void SelectPrefab(GameObject prefab)
        {
            SelectPrefab(prefab, TerrainPlacementMode.KeepSurface);
        }

        public static void SelectPrefab(GameObject prefab, TerrainPlacementMode terrainPlacementMode)
        {
            if (prefab != null && !IsPrefabAsset(prefab))
            {
                Debug.LogWarning("Level Placement only accepts persistent Prefab assets from the Project window.");
                return;
            }

            _selectedPrefab = prefab;
            string path = prefab == null ? string.Empty : AssetDatabase.GetAssetPath(prefab);
            string guid = string.IsNullOrEmpty(path) ? string.Empty : AssetDatabase.AssetPathToGUID(path);
            SessionState.SetString(SelectedPrefabGuidKey, guid);
            _selectedTerrainMode = prefab == null ? TerrainPlacementMode.KeepSurface : terrainPlacementMode;
            SessionState.SetInt(SelectedTerrainModeKey, (int)_selectedTerrainMode);
            SceneView.RepaintAll();
        }

        public override void OnActivated()
        {
            _preview = new PlacementPreview();
            _terrainPreview = new TerrainFlattenPreview();
            _sidewalkPreview = new HouseSidewalkPreview();
            _entranceDatabase = RoadBuilderAssets.GetOrCreateDatabase();
            _previewPrefab = null;
            _terrainPlan = null;
            _hasSurfaceHit = false;
            _placementYaw = SessionState.GetFloat(PlacementYawKey, 0f);
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            _preview?.Dispose();
            _terrainPreview?.Dispose();
            _sidewalkPreview?.Clear();
            _preview = null;
            _terrainPreview = null;
            _sidewalkPreview = null;
            _entranceDatabase = null;
            _previewPrefab = null;
            _terrainPlan = null;
            _hasSurfaceHit = false;
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView || EditorApplication.isPlayingOrWillChangePlaymode) return;

            Event currentEvent = Event.current;
            GameObject prefab = SelectedPrefab;
            RefreshPreview(prefab);
            DrawToolStatus(prefab);

            if (currentEvent.type == EventType.KeyDown && currentEvent.keyCode == KeyCode.Escape)
            {
                ToolManager.RestorePreviousTool();
                currentEvent.Use();
                return;
            }

            if (prefab != null && !currentEvent.alt && currentEvent.type == EventType.KeyDown &&
                (currentEvent.keyCode == KeyCode.Q || currentEvent.keyCode == KeyCode.E))
            {
                float step = currentEvent.shift ? FastRotationStep : RotationStep;
                _placementYaw = Mathf.Repeat(
                    _placementYaw + (currentEvent.keyCode == KeyCode.E ? step : -step),
                    360f);
                SessionState.SetFloat(PlacementYawKey, _placementYaw);
                RefreshTerrainPlan(prefab);
                currentEvent.Use();
                SceneView.RepaintAll();
                return;
            }

            if (prefab == null || currentEvent.alt) return;

            if (currentEvent.type == EventType.Layout)
            {
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));
            }

            if (ShouldRefreshSurface(currentEvent.type))
            {
                _hasSurfaceHit = SceneRaycaster.TryGetSurfaceHit(currentEvent.mousePosition, out _surfaceHit);
                RefreshTerrainPlan(prefab);
            }

            if (currentEvent.type == EventType.Repaint && _hasSurfaceHit)
            {
                Vector3 placementPosition = _terrainPlan == null ? _surfaceHit.point : _terrainPlan.PlacementPosition;
                Matrix4x4 previewMatrix = Matrix4x4.TRS(
                    placementPosition,
                    GetPlacementRotation(prefab, _placementYaw),
                    prefab.transform.localScale);
                _terrainPreview.Draw();
                _preview.Draw(previewMatrix);
                _sidewalkPreview.Draw();

                Handles.color = SurfaceMarkerColor;
                float markerSize = HandleUtility.GetHandleSize(_surfaceHit.point) * 0.18f;
                Handles.DrawWireDisc(_surfaceHit.point, _surfaceHit.normal, markerSize);
            }

            if (currentEvent.type != EventType.MouseDown || currentEvent.button != 0 || !_hasSurfaceHit) return;

            _sidewalkPreview.TryGetCreationData(
                out List<Vector3> sidewalkPoints,
                out float sidewalkWidth,
                out RoadSnapResult? sidewalkConnection);

            if (_terrainPlan == null)
            {
                PlacePrefabWithSidewalk(
                    prefab,
                    _surfaceHit.point,
                    GetPlacementRotation(prefab, _placementYaw),
                    SceneManager.GetActiveScene(),
                    sidewalkPoints,
                    sidewalkWidth,
                    sidewalkConnection,
                    out _);
            }
            else
            {
                PlacePrefabAndFlattenTerrainWithSidewalk(
                    prefab,
                    _terrainPlan,
                    GetPlacementRotation(prefab, _placementYaw),
                    SceneManager.GetActiveScene(),
                    sidewalkPoints,
                    sidewalkWidth,
                    sidewalkConnection,
                    out _);
                _hasSurfaceHit = false;
                _terrainPlan = null;
                _terrainPreview.SetPlan(null);
            }
            currentEvent.Use();
            SceneView.RepaintAll();
        }

        internal static GameObject PlacePrefab(GameObject prefab, Vector3 position)
        {
            return PlacePrefab(prefab, position, SceneManager.GetActiveScene());
        }

        internal static GameObject PlacePrefab(GameObject prefab, Vector3 position, Scene destinationScene)
        {
            return PlacePrefab(prefab, position, GetPlacementRotation(prefab, 0f), destinationScene);
        }

        internal static GameObject PlacePrefab(GameObject prefab, Vector3 position, Quaternion rotation)
        {
            return PlacePrefab(prefab, position, rotation, SceneManager.GetActiveScene());
        }

        internal static GameObject PlacePrefab(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Scene destinationScene)
        {
            if (!IsPrefabAsset(prefab))
            {
                throw new ArgumentException("A persistent Prefab asset is required.", nameof(prefab));
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            GameObject instance = InstantiatePrefabWithUndo(prefab, position, rotation, destinationScene);
            if (instance == null) return null;
            Undo.CollapseUndoOperations(undoGroup);
            return instance;
        }

        internal static GameObject PlacePrefabAndFlattenTerrain(
            GameObject prefab,
            TerrainFlattenPlan terrainPlan,
            Scene destinationScene)
        {
            return PlacePrefabAndFlattenTerrain(
                prefab,
                terrainPlan,
                GetPlacementRotation(prefab, 0f),
                destinationScene);
        }

        internal static GameObject PlacePrefabAndFlattenTerrain(
            GameObject prefab,
            TerrainFlattenPlan terrainPlan,
            Quaternion rotation,
            Scene destinationScene)
        {
            if (!IsPrefabAsset(prefab))
                throw new ArgumentException("A persistent Prefab asset is required.", nameof(prefab));
            if (terrainPlan == null) throw new ArgumentNullException(nameof(terrainPlan));

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);
            terrainPlan.Apply(UndoLabel);
            GameObject instance = InstantiatePrefabWithUndo(
                prefab,
                terrainPlan.PlacementPosition,
                rotation,
                destinationScene);
            Undo.CollapseUndoOperations(undoGroup);
            return instance;
        }

        internal static GameObject PlacePrefabWithSidewalk(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Scene destinationScene,
            IReadOnlyList<Vector3> sidewalkPoints,
            float sidewalkWidth,
            out TerrainRoadPath sidewalk)
        {
            return PlacePrefabWithSidewalk(
                prefab,
                position,
                rotation,
                destinationScene,
                sidewalkPoints,
                sidewalkWidth,
                null,
                out sidewalk);
        }

        internal static GameObject PlacePrefabWithSidewalk(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Scene destinationScene,
            IReadOnlyList<Vector3> sidewalkPoints,
            float sidewalkWidth,
            RoadSnapResult? sidewalkConnection,
            out TerrainRoadPath sidewalk)
        {
            return PlacePrefabWithSidewalkInternal(
                prefab,
                position,
                rotation,
                destinationScene,
                null,
                sidewalkPoints,
                sidewalkWidth,
                sidewalkConnection,
                out sidewalk);
        }

        internal static GameObject PlacePrefabAndFlattenTerrainWithSidewalk(
            GameObject prefab,
            TerrainFlattenPlan terrainPlan,
            Quaternion rotation,
            Scene destinationScene,
            IReadOnlyList<Vector3> sidewalkPoints,
            float sidewalkWidth,
            RoadSnapResult? sidewalkConnection,
            out TerrainRoadPath sidewalk)
        {
            if (terrainPlan == null) throw new ArgumentNullException(nameof(terrainPlan));
            return PlacePrefabWithSidewalkInternal(
                prefab,
                terrainPlan.PlacementPosition,
                rotation,
                destinationScene,
                terrainPlan,
                sidewalkPoints,
                sidewalkWidth,
                sidewalkConnection,
                out sidewalk);
        }

        private static GameObject PlacePrefabWithSidewalkInternal(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Scene destinationScene,
            TerrainFlattenPlan terrainPlan,
            IReadOnlyList<Vector3> sidewalkPoints,
            float sidewalkWidth,
            RoadSnapResult? sidewalkConnection,
            out TerrainRoadPath sidewalk)
        {
            if (!IsPrefabAsset(prefab))
                throw new ArgumentException("A persistent Prefab asset is required.", nameof(prefab));

            sidewalk = null;
            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(UndoLabel);

            GameObject instance = InstantiatePrefabWithUndo(prefab, position, rotation, destinationScene);
            if (instance == null)
            {
                Undo.CollapseUndoOperations(undoGroup);
                return null;
            }

            // EasyRoads markers must sample the final flattened Terrain heights.
            terrainPlan?.Apply(UndoLabel);

            if (sidewalkPoints != null && sidewalkPoints.Count >= 2 && sidewalkWidth > 0f)
            {
                sidewalk = EasyRoadAuthoring.CreateRoad(
                    sidewalkPoints,
                    sidewalkWidth,
                    EasyRoadBuilderPreferences.Template,
                    $"Sidewalk_From_{instance.name}",
                    destinationScene,
                    null,
                    sidewalkConnection);
            }

            Undo.SetCurrentGroupName(UndoLabel);
            Undo.CollapseUndoOperations(undoGroup);
            return instance;
        }

        private static GameObject InstantiatePrefabWithUndo(
            GameObject prefab,
            Vector3 position,
            Quaternion rotation,
            Scene destinationScene)
        {
            GameObject instance = PrefabUtility.InstantiatePrefab(prefab, destinationScene) as GameObject;
            if (instance == null) return null;

            Undo.RegisterCreatedObjectUndo(instance, UndoLabel);
            Undo.RecordObject(instance.transform, UndoLabel);
            instance.transform.SetPositionAndRotation(position, rotation);
            instance.transform.localScale = prefab.transform.localScale;
            PrefabUtility.RecordPrefabInstancePropertyModifications(instance.transform);
            return instance;
        }

        internal static bool IsPrefabAsset(GameObject prefab)
        {
            return prefab != null
                   && EditorUtility.IsPersistent(prefab)
                   && PrefabUtility.GetPrefabAssetType(prefab) != PrefabAssetType.NotAPrefab;
        }

        internal static Quaternion GetPlacementRotation(GameObject prefab, float yaw)
        {
            return prefab == null
                ? Quaternion.Euler(0f, yaw, 0f)
                : Quaternion.Euler(0f, yaw, 0f) * prefab.transform.rotation;
        }

        private void RefreshPreview(GameObject prefab)
        {
            if (_previewPrefab == prefab) return;

            _previewPrefab = prefab;
            _preview?.SetPrefab(prefab);
            _terrainPlan = null;
            _terrainPreview?.SetPlan(null);
            _sidewalkPreview?.Clear();
            _hasSurfaceHit = false;
        }

        private void RefreshTerrainPlan(GameObject prefab)
        {
            _terrainPlan = null;
            if (!_hasSurfaceHit || SelectedTerrainMode != TerrainPlacementMode.FlattenTerrain)
            {
                _terrainPreview?.SetPlan(null);
            }
            else
            {
                Terrain terrain = _surfaceHit.collider.GetComponent<Terrain>();
                if (terrain != null)
                {
                    TerrainFlattenPlan.TryCreate(
                        terrain,
                        prefab,
                        _surfaceHit.point,
                        GetPlacementRotation(prefab, _placementYaw),
                        out _terrainPlan);
                }

                _terrainPreview?.SetPlan(_terrainPlan);
            }

            if (!_hasSurfaceHit)
            {
                _sidewalkPreview?.Clear();
                return;
            }

            Vector3 placementPosition = _terrainPlan == null ? _surfaceHit.point : _terrainPlan.PlacementPosition;
            _sidewalkPreview?.Set(
                prefab,
                placementPosition,
                GetPlacementRotation(prefab, _placementYaw),
                _entranceDatabase);
        }

        private static bool ShouldRefreshSurface(EventType eventType)
        {
            return eventType == EventType.MouseMove
                   || eventType == EventType.MouseDrag
                   || eventType == EventType.MouseDown;
        }

        private void DrawToolStatus(GameObject prefab)
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(14f, 14f, 400f, 106f), EditorStyles.helpBox);
            GUILayout.Label("LEVEL PLACEMENT", EditorStyles.boldLabel);
            GUILayout.Label(prefab == null ? "Select a Prefab in the Palette." : $"Selected: {prefab.name}");
            if (prefab != null && SelectedTerrainMode == TerrainPlacementMode.FlattenTerrain)
            {
                string gradingStatus = _terrainPlan != null
                    ? $"Terrain grading preview ({_terrainPlan.MaximumAdjustment:0.0} m max)"
                    : "Terrain grading requires a Unity Terrain surface";
                GUILayout.Label(gradingStatus, EditorStyles.miniLabel);
            }
            if (prefab != null)
                GUILayout.Label($"Rotation Y: {_placementYaw:0}°", EditorStyles.miniLabel);
            GUILayout.Label("Q/E Rotate 15°   Shift+Q/E 45°   LMB Place   Alt Navigate   Esc Exit", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
