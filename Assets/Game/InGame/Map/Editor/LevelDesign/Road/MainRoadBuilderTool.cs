using System.Collections.Generic;
using UnityEditor;
using UnityEditor.EditorTools;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    [EditorTool("Connected Main Road Builder")]
    internal sealed class MainRoadBuilderTool : EditorTool
    {
        private const float MinimumPointDistance = 0.45f;
        private static readonly Color SnapColor = new(0.2f, 1f, 0.35f, 1f);

        private readonly List<Vector3> _controlPoints = new();
        private readonly List<Vector3> _previewControls = new();
        private List<Vector3> _sampledPreview = new();
        private Vector3 _hoverPoint;
        private bool _hasHover;
        private bool _hoverSnapped;
        private TerrainRoadPath _hoverSnapTarget;
        private bool _isDrawing;
        private string _message;
        private GUIContent _toolbarIcon;

        public override GUIContent toolbarIcon
        {
            get
            {
                if (_toolbarIcon != null) return _toolbarIcon;
                GUIContent icon = EditorGUIUtility.IconContent("Grid.PaintTool");
                _toolbarIcon = new GUIContent(icon.image, "Connected Main Road Builder");
                return _toolbarIcon;
            }
        }

        [MenuItem("PPack/Level Design/Activate Connected Main Road Builder")]
        private static void ActivateFromMenu()
        {
            RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
            ToolManager.SetActiveTool<MainRoadBuilderTool>();
        }

        public override void OnActivated()
        {
            RoadBuilderAssets.GetOrCreateTerrainRoadLayer();
            RoadBuilderAssets.GetOrCreateTerrainRoadBorderLayer();
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

            if (current.type == EventType.MouseMove
                || current.type == EventType.MouseDrag
                || current.type == EventType.MouseDown)
            {
                RefreshHover(current.mousePosition);
                RefreshPreview();
            }

            if (current.type == EventType.Repaint) DrawPreview();
            if (current.type != EventType.MouseDown || current.button != 0 || !_hasHover) return;

            AddPoint(_hoverPoint);
            bool finish = _isDrawing && current.clickCount >= 2;
            _isDrawing = true;
            _message = "계속 지점을 찍거나 기존 도로에 스냅한 뒤 Enter로 확정하세요.";
            current.Use();
            if (finish) FinalizeRoad();
            else RefreshPreview();
        }

        private void RefreshHover(Vector2 mousePosition)
        {
            _hasHover = RoadSurfaceSampler.TryGetMouseGround(mousePosition, null, out Vector3 ground);
            _hoverSnapped = false;
            if (!_hasHover) return;

            if (RoadConnectionUtility.TrySnapToRoad(ground, null, out Vector3 snapped, out _hoverSnapTarget))
            {
                _hoverPoint = snapped;
                _hoverSnapped = true;
            }
            else
            {
                _hoverSnapTarget = null;
                RoadSurfaceSampler.TryConformToGround(ground, out _hoverPoint);
            }
        }

        private void AddPoint(Vector3 point)
        {
            if (_controlPoints.Count > 0
                && Vector3.Distance(_controlPoints[^1], point) < MinimumPointDistance) return;
            _controlPoints.Add(point);
        }

        private void RefreshPreview()
        {
            _previewControls.Clear();
            _previewControls.AddRange(_controlPoints);
            if (_hasHover
                && (_previewControls.Count == 0
                    || Vector3.Distance(_previewControls[^1], _hoverPoint) >= MinimumPointDistance))
            {
                _previewControls.Add(_hoverPoint);
            }

            _sampledPreview = RoadSurfaceSampler.BuildConformedCenterLine(_previewControls);
            SceneView.RepaintAll();
        }

        private void DrawPreview()
        {
            TerrainRoadPreview.Draw(
                _sampledPreview,
                RoadBuilderPreferences.MainRoadWidth,
                RoadBuilderPreferences.EdgeFeather,
                RoadBuilderPreferences.BorderWidth);

            if (_hasHover && _hoverSnapped)
            {
                Handles.color = SnapColor;
                float size = HandleUtility.GetHandleSize(_hoverPoint) * 0.22f;
                Handles.DrawWireDisc(_hoverPoint, Vector3.up, size);
                Handles.DrawWireDisc(_hoverPoint, Vector3.up, size * 0.55f);
            }
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
            else if (current.keyCode == KeyCode.Backspace && _controlPoints.Count > 0)
            {
                _controlPoints.RemoveAt(_controlPoints.Count - 1);
                _isDrawing = _controlPoints.Count > 0;
                RefreshPreview();
                current.Use();
            }
            else if (current.keyCode == KeyCode.LeftBracket || current.keyCode == KeyCode.RightBracket)
            {
                float delta = current.keyCode == KeyCode.RightBracket ? 0.25f : -0.25f;
                RoadBuilderPreferences.MainRoadWidth += delta;
                RefreshPreview();
                current.Use();
            }
        }

        private void FinalizeRoad()
        {
            List<Vector3> finalPoints = RoadSurfaceSampler.BuildConformedCenterLine(_controlPoints);
            if (finalPoints.Count < 2 || Vector3.Distance(finalPoints[0], finalPoints[^1]) < 1.2f)
            {
                _message = "도로가 너무 짧습니다. 지점을 하나 더 찍어주세요.";
                return;
            }

            TerrainRoadPath road = TerrainRoadAuthoring.CreateRoad(
                _controlPoints,
                RoadBuilderPreferences.MainRoadWidth,
                RoadBuilderPreferences.EdgeFeather,
                "Connected_MainRoad",
                SceneManager.GetActiveScene(),
                "Create Terrain Main Road");
            if (road == null)
            {
                _message = "Terrain 위에서만 도로를 확정할 수 있습니다.";
                return;
            }
            Selection.activeGameObject = road.gameObject;
            _message = "Spline 도로 완료. 지형 경사와 모든 교차부를 한 번에 다시 계산했습니다.";
            ResetRoad(false);
        }

        private void ResetRoad(bool resetMessage = true)
        {
            _isDrawing = false;
            _hasHover = false;
            _hoverSnapped = false;
            _hoverSnapTarget = null;
            _controlPoints.Clear();
            _previewControls.Clear();
            _sampledPreview.Clear();
            if (resetMessage) _message = "Terrain을 클릭해 페인트 도로를 시작하세요.";
        }

        private void DrawStatus()
        {
            Handles.BeginGUI();
            GUILayout.BeginArea(new Rect(14f, 14f, 450f, 104f), EditorStyles.helpBox);
            GUILayout.Label("CONNECTED MAIN ROAD", EditorStyles.boldLabel);
            GUILayout.Label(_message ?? string.Empty);
            GUILayout.Label(
                $"Terrain 페인트 · 폭 {RoadBuilderPreferences.MainRoadWidth:0.00} m · 짙은 테두리 {RoadBuilderPreferences.BorderWidth:0.00} m",
                EditorStyles.miniLabel);
            GUILayout.Label("LMB 지점 · Enter 확정 · [ ] 폭 조절 · Backspace 취소 · Esc 이전 단계", EditorStyles.miniLabel);
            GUILayout.EndArea();
            Handles.EndGUI();
        }
    }
}
