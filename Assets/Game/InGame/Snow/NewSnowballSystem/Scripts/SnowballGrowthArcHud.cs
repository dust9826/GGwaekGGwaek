using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>눈덩이의 화면상 오른쪽 외곽을 따라 현재 단계 하나만 그리는 UI Toolkit HUD.</summary>
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class SnowballGrowthArcHud : MonoBehaviour
    {
        private static readonly Color[] StageColors =
        {
            new Color32(47, 131, 207, 255),
            new Color32(49, 180, 120, 255),
            new Color32(255, 211, 90, 255),
            new Color32(241, 91, 98, 255),
        };

        [SerializeField] private MonoBehaviour _target;
        [SerializeField] private Camera _worldCamera;
        [SerializeField, Range(12f, 32f)] private float _minimumGapPx = 18f;
        [SerializeField, Range(12f, 32f)] private float _maximumGapPx = 24f;

        private UIDocument _document;
        private VisualElement _host;
        private ArcVisualElement _arc;
        private Label _stageBadge;
        private ISnowballGrowthDisplay _display;

        public void Configure(MonoBehaviour target, Camera worldCamera)
        {
            _target = target;
            _display = target as ISnowballGrowthDisplay;
            _worldCamera = worldCamera;
        }

        private void OnEnable()
        {
            _display = _target as ISnowballGrowthDisplay;
            _document = GetComponent<UIDocument>();
            _host = _document.rootVisualElement.Q<VisualElement>("snowball-growth-root")
                    ?? _document.rootVisualElement;

            _arc = new ArcVisualElement { name = "snowball-growth-arc" };
            _arc.AddToClassList("snowball-growth-arc");
            _stageBadge = new Label("1") { name = "snowball-stage-badge" };
            _stageBadge.AddToClassList("snowball-stage-badge");
            _host.Add(_arc);
            _host.Add(_stageBadge);
        }

        private void OnDisable()
        {
            _arc?.RemoveFromHierarchy();
            _stageBadge?.RemoveFromHierarchy();
            _arc = null;
            _stageBadge = null;
        }

        private void LateUpdate()
        {
            if (_target == null || _display == null || _arc == null || _stageBadge == null
                || _host.panel == null)
            {
                SetVisible(false);
                return;
            }

            Camera camera = _worldCamera != null ? _worldCamera : Camera.main;
            if (camera == null)
            {
                SetVisible(false);
                return;
            }

            Vector3 worldCenter = _display.WorldCenter;
            Vector3 screenCenter = camera.WorldToScreenPoint(worldCenter);
            if (screenCenter.z <= 0f)
            {
                SetVisible(false);
                return;
            }

            Vector3 screenRight = camera.WorldToScreenPoint(
                worldCenter + camera.transform.right * _display.DisplayRadiusM);
            Vector2 panelCenter = RuntimePanelUtils.ScreenToPanel(_host.panel,
                new Vector2(screenCenter.x, Screen.height - screenCenter.y));
            Vector2 panelRight = RuntimePanelUtils.ScreenToPanel(_host.panel,
                new Vector2(screenRight.x, Screen.height - screenRight.y));
            float projectedRadius = Vector2.Distance(panelCenter, panelRight);

            if (projectedRadius <= 1f
                || panelCenter.x + projectedRadius < 0f
                || panelCenter.x - projectedRadius > _host.resolvedStyle.width
                || panelCenter.y + projectedRadius < 0f
                || panelCenter.y - projectedRadius > _host.resolvedStyle.height)
            {
                SetVisible(false);
                return;
            }

            float gap = Mathf.Clamp(projectedRadius * 0.1f, _minimumGapPx, _maximumGapPx);
            float lineWidth = Mathf.Clamp(projectedRadius * 0.12f, 9f, 16f);
            float arcRadius = projectedRadius + gap + lineWidth * 0.5f;
            Color stageColor = StageColors[Mathf.Clamp((int)_display.GrowthStage - 1, 0, 3)];

            _arc.SetData(panelCenter, arcRadius, lineWidth, _display.StageProgress01, stageColor);
            _stageBadge.text = _display.GrowthStage == ESnowBallGrowthStage.Seed
                ? "S"
                : ((int)_display.GrowthStage).ToString();
            _stageBadge.style.backgroundColor = (Color)new Color32(21, 57, 80, 255);

            const float badgeSize = 42f;
            _stageBadge.style.left = panelCenter.x + arcRadius + lineWidth * 0.5f + 10f - badgeSize * 0.5f;
            _stageBadge.style.top = panelCenter.y - badgeSize * 0.5f;
            SetVisible(true);
        }

        private void SetVisible(bool visible)
        {
            DisplayStyle display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            if (_arc != null) _arc.style.display = display;
            if (_stageBadge != null) _stageBadge.style.display = display;
        }

        private sealed class ArcVisualElement : VisualElement
        {
            private const int SegmentCount = 32;
            private static readonly Color ShadowColor = new Color32(11, 38, 56, 180);
            private static readonly Color OutlineColor = new Color32(11, 38, 56, 255);
            private static readonly Color TrackColor = new Color32(21, 57, 80, 205);

            private Vector2 _center;
            private float _radius;
            private float _lineWidth;
            private float _progress01;
            private Color _fillColor;

            public ArcVisualElement()
            {
                pickingMode = PickingMode.Ignore;
                style.position = Position.Absolute;
                style.left = 0f;
                style.top = 0f;
                style.right = 0f;
                style.bottom = 0f;
                generateVisualContent += Draw;
            }

            public void SetData(Vector2 center, float radius, float lineWidth, float progress01,
                Color fillColor)
            {
                _center = center;
                _radius = radius;
                _lineWidth = lineWidth;
                _progress01 = Mathf.Clamp01(progress01);
                _fillColor = fillColor;
                MarkDirtyRepaint();
            }

            private void Draw(MeshGenerationContext context)
            {
                Painter2D painter = context.painter2D;
                DrawArc(painter, _center + new Vector2(4f, 6f), _radius, 1f,
                    ShadowColor, _lineWidth + 9f);
                DrawArc(painter, _center, _radius, 1f, OutlineColor, _lineWidth + 8f);
                DrawArc(painter, _center, _radius, 1f, Color.white, _lineWidth + 4f);
                DrawArc(painter, _center, _radius, 1f, TrackColor, _lineWidth);
                if (_progress01 > 0.001f)
                    DrawArc(painter, _center, _radius, _progress01, _fillColor,
                        Mathf.Max(3f, _lineWidth - 3f));
            }

            private static void DrawArc(Painter2D painter, Vector2 center, float radius,
                float progress01, Color color, float width)
            {
                List<Vector2> points = new List<Vector2>(SegmentCount + 1);
                int lastSegment = Mathf.Max(1, Mathf.CeilToInt(SegmentCount * progress01));
                for (int index = 0; index <= lastSegment; index++)
                {
                    float t = index / (float)SegmentCount;
                    float angleDeg = Mathf.Lerp(58f, -58f, t);
                    float angleRad = angleDeg * Mathf.Deg2Rad;
                    points.Add(center + new Vector2(Mathf.Cos(angleRad), Mathf.Sin(angleRad)) * radius);
                }

                painter.BeginPath();
                painter.strokeColor = color;
                painter.lineWidth = width;
                painter.lineCap = LineCap.Round;
                painter.lineJoin = LineJoin.Round;
                painter.MoveTo(points[0]);
                for (int index = 1; index < points.Count; index++) painter.LineTo(points[index]);
                painter.Stroke();
            }
        }
    }
}
