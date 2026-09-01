using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    [EditorTool("Door Road Builder")]
    internal sealed class RoadBuilderTool : EditorTool
    {
        private const float EntranceLeadLength = 1.0f;
        private const float MinimumPointDistance = 0.45f;
        private static readonly Color EntranceColor = new(1f, 0.76f, 0.18f, 0.95f);
        private static readonly Color CenterLineColor = new(0.15f, 0.9f, 1f, 0.95f);

        private readonly List<Vector3> _controlPoints = new();
        private readonly List<Vector3> _previewControls = new();
        private RoadEntranceDatabase _database;
        private RoadEntranceWorldData _entrance;
        private List<Vector3> _sampledPreview = new();
        private Vector3 _hoverPoint;
        private bool _hasEntrance;
        private bool _hasHoverPoint;
        private bool _hoverSnapped;
        private TerrainRoadPath _hoverSnapTarget;
        private string _message;
        private GUIContent _toolbarIcon;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_toolbarIcon != null) return _toolbarIcon;
                GUIContent icon = EditorGUIUtility.IconContent("CustomTool");
                _toolbarIcon = new GUIContent(icon.image, "Door Road Builder");
                return _toolbarIcon;
            }
        }

        [MenuItem("PPack/Level Design/Activate Door Road Builder")]
        private static void ActivateFromMenu()
        {
            RoadBuilderAssets.GetOrCreateDatabase();
            RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            ToolManager.SetActiveTool<RoadBuilderTool>();
        }

        public override void OnActivated()
        {
            _database = RoadBuilderAssets.GetOrCreateDatabase();
            RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            _message = "집을 클릭해 출입구를 선택하세요.";
            ResetCurrentRoad();
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            ResetCurrentRoad();
            SceneView.RepaintAll();
        }

        public override void OnToolGUI(EditorWindow window)
        {
            if (window is not SceneView || EditorApplication.isPlayingOrWillChangePlaymode) return;

            Event current = Event.current;
            HandleKeyboard(current);
            DrawStatus();

            if (current.alt) return;
            if (current.type == EventType.Layout)
                HandleUtility.AddDefaultControl(GUIUtility.GetControlID(FocusType.Passive));

            if (!_hasEntrance)
            {
                HandleHouseSelection(current);
                return;
            }

            RefreshHoverAndPreview(current);
            if (current.type == EventType.Repaint) DrawPreview();
            if (current.type == EventType.MouseDown && current.button == 0 && _hasHoverPoint)
            {
                AddControlPoint(_hoverPoint);
                bool finish = current.clickCount >= 2;
                current.Use();
                if (finish) FinalizeCurrentRoad();
                else RefreshPreviewMesh();
            }
        }

        internal static TerrainRoadPath CreateRoad(
            IReadOnlyList<Vector3> worldControlPoints,
            float width,
            string roadName,
            Scene destinationScene)
        {
            return TerrainRoadAuthoring.CreateRoad(
                worldControlPoints,
                width,
                RoadBuilderPreferences.EdgeFeather,
                roadName,
                destinationScene,
                "Create Terrain Door Road");
        }

        private void HandleKeyboard(Event current)
        {
            if (current.type != EventType.KeyDown) return;

            if (current.keyCode == KeyCode.Escape)
            {
                if (_hasEntrance) ResetCurrentRoad();
                else ToolManager.RestorePreviousTool();
                current.Use();
            }
            else if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && _hasEntrance)
            {
                FinalizeCurrentRoad();
                current.Use();
            }
            else if (current.keyCode == KeyCode.Backspace && _hasEntrance)
            {
                if (_controlPoints.Count > 2)
                    _controlPoints.RemoveAt(_controlPoints.Count - 1);
                RefreshPreviewMesh();
                current.Use();
            }
            else if (current.keyCode == KeyCode.LeftBracket || current.keyCode == KeyCode.RightBracket)
            {
                float delta = current.keyCode == KeyCode.RightBracket ? 0.1f : -0.1f;
                RoadBuilderPreferences.EntranceRoadWidth += delta;
                RefreshPreviewMesh();
                current.Use();
            }
        }

        private void HandleHouseSelection(Event current)
        {
            if (current.type != EventType.MouseDown || current.button != 0) return;

            GameObject picked = HandleUtility.PickGameObject(current.mousePosition, true);
            if (_database != null && _database.TryResolve(picked, out RoadEntranceWorldData entrance))
            {
                BeginRoad(entrance);
                Selection.activeGameObject = entrance.HouseRoot;
                _message = $"{entrance.HouseRoot.name}: 도로 지점을 찍고 Enter로 확정하세요.";
            }
            else
            {
                _message = picked == null
                    ? "집 프리팹을 클릭하세요."
                    : $"{picked.name}에는 Entrance Anchor가 등록되어 있지 않습니다.";
            }

            current.Use();
            SceneView.RepaintAll();
        }

        private void BeginRoad(RoadEntranceWorldData entrance)
        {
            _entrance = entrance;
            _hasEntrance = true;
            _controlPoints.Clear();

            Vector3 start = entrance.Position;
            RoadSurfaceSampler.TryConformToGround(start, out start);
            Vector3 lead = start + entrance.Forward * EntranceLeadLength;
            RoadSurfaceSampler.TryConformToGround(lead, out lead);
            _controlPoints.Add(start);
            _controlPoints.Add(lead);
            _hasHoverPoint = false;
            RefreshPreviewMesh();
        }

        private void RefreshHoverAndPreview(Event current)
        {
            if (current.type != EventType.MouseMove
                && current.type != EventType.MouseDrag
                && current.type != EventType.MouseDown) return;

            _hasHoverPoint = RoadSurfaceSampler.TryGetMouseGround(
                current.mousePosition,
                _entrance.HouseRoot,
                out Vector3 groundPoint);
            _hoverSnapped = _hasHoverPoint
                            && RoadConnectionUtility.TrySnapToRoad(
                                groundPoint,
                                null,
                                out _hoverPoint,
                                out _hoverSnapTarget);
            if (_hasHoverPoint && !_hoverSnapped)
            {
                _hoverSnapTarget = null;
                RoadSurfaceSampler.TryConformToGround(groundPoint, out _hoverPoint);
            }
            RefreshPreviewMesh();
            SceneView.RepaintAll();
        }

        private void AddControlPoint(Vector3 point)
        {
            if (_controlPoints.Count > 0
                && Vector3.Distance(_controlPoints[_controlPoints.Count - 1], point) < MinimumPointDistance) return;
            _controlPoints.Add(point);
        }

        private void RefreshPreviewMesh()
        {
            _previewControls.Clear();
            _previewControls.AddRange(_controlPoints);
            if (_hasHoverPoint
                && (_previewControls.Count == 0
                    || Vector3.Distance(_previewControls[_previewControls.Count - 1], _hoverPoint) >= MinimumPointDistance))
            {
                _previewControls.Add(_hoverPoint);
            }

            _sampledPreview = RoadSurfaceSampler.BuildConformedCenterLine(_previewControls);
        }

        private void DrawPreview()
        {
            TerrainRoadPreview.Draw(
                _sampledPreview,
                CurrentRoadWidth,
                RoadBuilderPreferences.EdgeFeather,
                RoadBuilderPreferences.BorderWidth);
            if (_controlPoints.Count < 2) return;

            Handles.color = EntranceColor;
            Vector3 start = _controlPoints[0];
            Vector3 right = Vector3.Cross(Vector3.up, _entrance.Forward).normalized;
            Handles.DrawAAPolyLine(5f,
                start - right * CurrentRoadWidth * 0.5f,
                start + right * CurrentRoadWidth * 0.5f);
            float markerSize = HandleUtility.GetHandleSize(start) * 0.13f;
            Handles.SphereHandleCap(0, start, Quaternion.identity, markerSize, EventType.Repaint);

            if (_sampledPreview.Count > 1)
            {
                Handles.color = CenterLineColor;
                Handles.DrawAAPolyLine(2f, _sampledPreview.ToArray());
            }

            if (_hasHoverPoint && _hoverSnapped)
            {
                Handles.color = Color.green;
                float snapSize = HandleUtility.GetHandleSize(_hoverPoint) * 0.22f;
                Handles.DrawWireDisc(_hoverPoint, Vector3.up, snapSize);
            }
        }

        private void FinalizeCurrentRoad()
        {
            List<Vector3> finalPoints = RoadSurfaceSampler.BuildConformedCenterLine(_controlPoints);
            if (finalPoints.Count < 2 || Vector3.Distance(finalPoints[0], finalPoints[^1]) < 1.2f)
            {
                _message = "도로가 너무 짧습니다. 한 지점 이상 더 찍어주세요.";
                return;
            }

            TerrainRoadPath road = CreateRoad(
                _controlPoints,
                CurrentRoadWidth,
                $"Road_From_{_entrance.HouseRoot.name}",
                SceneManager.GetActiveScene());
            if (road == null)
            {
                _message = "집 앞 경로가 활성 Terrain 위에 있어야 합니다.";
                return;
            }
            Selection.activeGameObject = road.gameObject;
            _message = $"{road.name} Spline 도로 완료. 문 앞 지형까지 평탄화했습니다.";
            ResetCurrentRoad(false);
            SceneView.RepaintAll();
        }

        private static float CurrentRoadWidth => RoadBuilderPreferences.EntranceRoadWidth;

        private void ResetCurrentRoad(bool resetMessage = true)
        {
            _hasEntrance = false;
            _hasHoverPoint = false;
            _hoverSnapped = false;
            _hoverSnapTarget = null;
            _controlPoints.Clear();
            _previewControls.Clear();
            _sampledPreview.Clear();
            if (resetMessage) _message = "집을 클릭해 출입구를 선택하세요.";
        }

        private void DrawStatus()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(14f, 14f, 430f, 116f), EditorStyles.helpBox);
            GUILayout.Label("DOOR ROAD BUILDER", EditorStyles.boldLabel);
            GUILayout.Label(_message ?? string.Empty);
            if (_hasEntrance)
            {
                GUILayout.Label(
                    $"문 폭 {_entrance.DoorWidth:0.00} m · 도로 폭 {CurrentRoadWidth:0.00} m · Terrain 페인트 · 첫 1 m는 문에 수직",
                    EditorStyles.miniLabel);
                GUILayout.Label("LMB 지점 · 초록 원 도로 연결 · Enter 확정 · [ ] 폭 조절 · Esc 이전 단계", EditorStyles.miniLabel);
            }
            else GUILayout.Label("LMB 집 선택 · Alt/마우스 씬 이동 · Esc 도구 종료", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
