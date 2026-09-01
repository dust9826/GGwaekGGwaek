using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    [EditorTool("EasyRoads Template Road Builder")]
    internal sealed class EasyRoadDrawingTool : EditorTool
    {
        private const float MinimumPointDistance = 0.45f;
        private static readonly Color SnapColor = new(0.25f, 1f, 0.4f, 1f);

        private readonly List<Vector3> _controlPoints = new();
        private readonly List<RoadSnapResult?> _controlPointSnaps = new();
        private readonly List<Vector3> _previewControls = new();
        private List<Vector3> _sampledPreview = new();
        private Vector3 _hoverPoint;
        private RoadSnapResult _hoverSnap;
        private bool _hasHover;
        private bool _hoverSnapped;
        private bool _isDrawing;
        private string _message;
        private GUIContent _toolbarIcon;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_toolbarIcon != null) return _toolbarIcon;
                GUIContent icon = EditorGUIUtility.IconContent("Grid.PaintTool");
                _toolbarIcon = new GUIContent(icon.image, "EasyRoads Template Road Builder");
                return _toolbarIcon;
            }
        }

        [MenuItem("PPack/Level Design/Activate EasyRoads Template Road Builder")]
        private static void ActivateFromMenu()
        {
            EasyRoadTemplateAssets.GetOrCreateDefaultTemplate();
            ToolManager.SetActiveTool<EasyRoadDrawingTool>();
        }

        public override void OnActivated()
        {
            EasyRoadTemplateAssets.GetOrCreateDefaultTemplate();
            ResetRoad();
            SceneView.RepaintAll();
        }

        public override void OnWillBeDeactivated()
        {
            ResetRoad();
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

            if (current.type == EventType.MouseMove ||
                current.type == EventType.MouseDrag ||
                current.type == EventType.MouseDown)
            {
                RefreshHover(current.mousePosition);
                RefreshPreview();
            }

            if (current.type == EventType.Repaint) DrawPreview();
            if (current.type != EventType.MouseDown || current.button != 0 || !_hasHover) return;

            bool added = AddPoint(_hoverPoint, _hoverSnapped ? _hoverSnap : null);
            bool finish = _isDrawing && current.clickCount >= 2;
            _isDrawing = true;
            if (added)
                _message = _hoverSnapped
                    ? "미리보기 도로에 연결점이 고정되었습니다. 계속 그리거나 Enter로 생성하세요."
                    : "계속 지점을 찍고 Enter로 EasyRoads 도로를 생성하세요.";
            current.Use();
            if (finish && added) FinalizeRoad();
            else RefreshPreview();
        }

        private void RefreshHover(Vector2 mousePosition)
        {
            _hasHover = RoadSurfaceSampler.TryGetMouseGround(mousePosition, null, out Vector3 ground);
            _hoverSnapped = false;
            if (!_hasHover) return;

            if (RoadConnectionUtility.TrySnapToRoad(ground, null, out RoadSnapResult snap))
            {
                _hoverPoint = snap.Point;
                _hoverSnap = snap;
                _hoverSnapped = true;
            }
            else RoadSurfaceSampler.TryConformToGround(ground, out _hoverPoint);
        }

        private bool AddPoint(Vector3 point, RoadSnapResult? snap)
        {
            if (_controlPoints.Count > 0 &&
                Vector3.Distance(_controlPoints[^1], point) < MinimumPointDistance) return false;
            _controlPoints.Add(point);
            _controlPointSnaps.Add(snap);
            return true;
        }

        private void RefreshPreview()
        {
            _previewControls.Clear();
            _previewControls.AddRange(_controlPoints);
            bool appendedHover = false;
            if (_hasHover &&
                (_previewControls.Count == 0 ||
                 Vector3.Distance(_previewControls[^1], _hoverPoint) >= MinimumPointDistance))
            {
                _previewControls.Add(_hoverPoint);
                appendedHover = true;
            }

            RoadSnapResult? startConnection = _controlPointSnaps.Count > 0
                ? _controlPointSnaps[0]
                : null;
            RoadSnapResult? endConnection = appendedHover
                ? (_hoverSnapped ? _hoverSnap : null)
                : _controlPointSnaps.Count > 0
                    ? _controlPointSnaps[^1]
                    : null;
            List<Vector3> connectedPreview = EasyRoadAuthoring.BuildConnectedControlPoints(
                _previewControls,
                startConnection,
                endConnection);
            _sampledPreview = RoadSurfaceSampler.BuildConformedCenterLine(connectedPreview);
            SceneView.RepaintAll();
        }

        private void DrawPreview()
        {
            TerrainRoadPreview.Draw(
                _sampledPreview,
                EasyRoadBuilderPreferences.Width,
                0.02f,
                0.2f);

            if (!_hasHover || !_hoverSnapped) return;
            Handles.color = SnapColor;
            float size = HandleUtility.GetHandleSize(_hoverPoint) * 0.22f;
            Handles.DrawWireDisc(_hoverPoint, Vector3.up, size);
            Handles.DrawWireDisc(_hoverPoint, Vector3.up, size * 0.55f);
        }

        private void HandleKeyboard(Event current)
        {
            if (current.type != EventType.KeyDown) return;
            if (current.keyCode == KeyCode.Escape)
            {
                if (_isDrawing) ResetRoad();
                else ToolManager.RestorePreviousTool();
                current.Use();
            }
            else if ((current.keyCode == KeyCode.Return || current.keyCode == KeyCode.KeypadEnter) && _isDrawing)
            {
                FinalizeRoad();
                current.Use();
            }
            else if (current.keyCode == KeyCode.C && current.modifiers == EventModifiers.None)
            {
                ConnectAtCursor(current.mousePosition);
                current.Use();
            }
            else if (current.keyCode == KeyCode.Backspace && _controlPoints.Count > 0)
            {
                _controlPoints.RemoveAt(_controlPoints.Count - 1);
                _controlPointSnaps.RemoveAt(_controlPointSnaps.Count - 1);
                _isDrawing = _controlPoints.Count > 0;
                RefreshPreview();
                current.Use();
            }
            else if (current.keyCode == KeyCode.LeftBracket || current.keyCode == KeyCode.RightBracket)
            {
                float delta = current.keyCode == KeyCode.RightBracket ? 0.25f : -0.25f;
                EasyRoadBuilderPreferences.Width += delta;
                RefreshPreview();
                current.Use();
            }
        }

        private void ConnectAtCursor(Vector2 mousePosition)
        {
            if (!_isDrawing || _controlPoints.Count == 0)
            {
                _message = "먼저 Terrain에 도로 시작점을 찍어주세요.";
                return;
            }

            RefreshHover(mousePosition);
            RefreshPreview();
            if (!_hasHover || !_hoverSnapped)
            {
                _message = "연결할 기존 도로 위에 마우스를 올린 뒤 C를 눌러주세요.";
                return;
            }

            if (!AddPoint(_hoverPoint, _hoverSnap))
            {
                _message = "이미 같은 위치에 연결점이 있습니다. 계속 그리거나 Enter로 생성하세요.";
                return;
            }

            _isDrawing = true;
            _message = "미리보기 도로에 연결점을 고정했습니다. 계속 그리거나 Enter로 생성하세요.";
            RefreshPreview();
        }

        private void FinalizeRoad()
        {
            if (_controlPoints.Count < 2)
            {
                _message = "도로를 만들려면 서로 다른 지점을 두 곳 이상 찍어주세요.";
                return;
            }

            RoadSnapResult? startConnection = ResolveEndpointConnection(0);
            RoadSnapResult? endConnection = ResolveEndpointConnection(_controlPoints.Count - 1);
            List<Vector3> connectedControls = EasyRoadAuthoring.BuildConnectedControlPoints(
                _controlPoints,
                startConnection,
                endConnection);
            List<Vector3> finalPoints = RoadSurfaceSampler.BuildConformedCenterLine(connectedControls);
            if (finalPoints.Count < 2 || Vector3.Distance(finalPoints[0], finalPoints[^1]) < 1.2f)
            {
                _message = "도로가 너무 짧습니다. 지점을 하나 더 찍어주세요.";
                return;
            }

            EasyRoadTemplate template = EasyRoadBuilderPreferences.Template;
            TerrainRoadPath road = EasyRoadAuthoring.CreateRoad(
                _controlPoints,
                EasyRoadBuilderPreferences.Width,
                template,
                "EasyRoad",
                SceneManager.GetActiveScene(),
                startConnection,
                endConnection);
            if (road == null)
            {
                _message = "EasyRoads 네트워크와 Terrain 상태를 확인하세요.";
                return;
            }

            Selection.activeGameObject = road.gameObject;
            _message = "EasyRoads 도로 완료. Spline을 수정한 뒤 Rebuild EasyRoads From Splines를 사용하세요.";
            ResetRoad(false);
        }

        private RoadSnapResult? ResolveEndpointConnection(int pointIndex)
        {
            if (pointIndex < 0 || pointIndex >= _controlPoints.Count) return null;
            if (pointIndex < _controlPointSnaps.Count &&
                _controlPointSnaps[pointIndex] is RoadSnapResult recorded &&
                recorded.TargetRoad != null) return recorded;

            return RoadConnectionUtility.TrySnapToRoad(
                _controlPoints[pointIndex],
                null,
                out RoadSnapResult resolved)
                ? resolved
                : null;
        }

        private void ResetRoad(bool resetMessage = true)
        {
            _isDrawing = false;
            _hasHover = false;
            _hoverSnapped = false;
            _controlPoints.Clear();
            _controlPointSnaps.Clear();
            _previewControls.Clear();
            _sampledPreview.Clear();
            if (resetMessage)
                _message = "Terrain을 클릭해 시작하세요. 기존 도로 근처에서 클릭하면 자동 연결됩니다.";
        }

        private void DrawStatus()
        {
            EasyRoadTemplate template = EasyRoadBuilderPreferences.Template;
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(14f, 14f, 470f, 112f), EditorStyles.helpBox);
            GUILayout.Label("EASYROADS TEMPLATE ROAD", EditorStyles.boldLabel);
            GUILayout.Label(_message ?? string.Empty);
            GUILayout.Label(
                $"Template {(template == null ? "None" : template.name)} · 폭 {EasyRoadBuilderPreferences.Width:0.00} m · 기존 Terrain 보호",
                EditorStyles.miniLabel);
            GUILayout.Label("LMB 지점 · C 미리보기 연결 고정 · Enter 생성 · [ ] 폭 · Backspace · Esc", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
