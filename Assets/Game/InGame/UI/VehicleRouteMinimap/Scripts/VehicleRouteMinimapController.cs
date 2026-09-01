using System.Collections.Generic;
using System.Linq;
using DG.Tweening;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// 실제 바닥 Bounds에 차량, 장애물, 쓰레기와 청소 완료 셀을 동일한 X/Z 좌표계로 투영한다.
    /// 게임 판정은 Vehicle/Dust가 소유하고 이 컴포넌트는 실제 붓 입력과 오브젝트 생존 상태만 읽는다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class VehicleRouteMinimapController : MonoBehaviour
    {
        private const float MapPadding = 13f;
        private const int CoverageResolution = 96;

        [SerializeField] private VehicleController _vehicle;
        [SerializeField] private MapMinimapBounds _mapBoundsSource;

        private MopPad _mopPad;
        private SnowVehiclePad _snowPad;
        private VisualElement _backdrop;
        private VisualElement _shell;
        private VisualElement _mapCanvas;
        private Image _mapBackground;
        private VisualElement _worldMarkers;
        private VisualElement _vehicleMarker;
        private Label _titleLabel;
        private Label _progressLabel;
        private Label _taskLegendLabel;

        private readonly List<WorldMarker> _trashMarkers = new List<WorldMarker>();
        private readonly List<WorldMarker> _obstacleMarkers = new List<WorldMarker>();
        private readonly Dictionary<BlizzardEvent, VisualElement> _weatherMarkers = new Dictionary<BlizzardEvent, VisualElement>();
        private readonly List<Vector3> _routeWorldPoints = new List<Vector3>();
        private readonly bool[,] _cleanedCells = new bool[CoverageResolution, CoverageResolution];
        private Bounds _worldBounds;
        private int _cleanedCellCount;
        private int _remainingTrashCount;
        private int _displayedPercent = -1;
        private int _lastPulseStep;
        private Camera _mapCamera;
        private RenderTexture _mapTexture;
        private GameObject _mapCameraObject;
        private Sequence _visibilitySequence;
        private bool _isMapOpen;

        private static readonly string[] PercentLabels = BuildPercentLabelCache();

        private sealed class WorldMarker
        {
            public TrashMapTarget TrashTarget;
            public Transform Transform;
            public Collider Collider;
            public VisualElement Element;
            public Vector3 LastPosition;
        }

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _backdrop = root.Q<VisualElement>("minimap-backdrop");
            _shell = root.Q<VisualElement>("minimap-shell");
            _mapCanvas = root.Q<VisualElement>("route-canvas");
            _mapBackground = root.Q<Image>("minimap-background");
            _worldMarkers = root.Q<VisualElement>("world-markers");
            _vehicleMarker = root.Q<VisualElement>("vehicle-marker");
            _titleLabel = root.Q<Label>("map-title");
            _progressLabel = root.Q<Label>("route-progress");
            _taskLegendLabel = root.Q<Label>("legend-task");

            _vehicle ??= FindAnyObjectByType<VehicleController>();
            _mapBoundsSource ??= FindAnyObjectByType<MapMinimapBounds>();
            if (_vehicle == null || _mapBoundsSource == null)
            {
                Debug.LogError($"{nameof(VehicleRouteMinimapController)}: vehicle or MapMinimapBounds is missing.", this);
                enabled = false;
                return;
            }

            _worldBounds = _mapBoundsSource.WorldBounds;
            SetupWorldCamera();
            _mopPad = _vehicle.GetComponent<MopPad>();
            if (_mopPad != null)
            {
                _mopPad.SurfacePainted += RecordCleanedArea;
            }

            _snowPad = _vehicle.GetComponentInChildren<SnowVehiclePad>();
            if (_snowPad != null)
            {
                _snowPad.SnowCleared += RecordClearedSnow;
                _titleLabel.text = "SNOW ROUTE";
                _taskLegendLabel.text = "SNOW";
            }

            DiscoverWorldObjects();
            TrashMapTarget.Registered += RegisterTrashTarget;
            TrashMapTarget.Unregistered += UnregisterTrashTarget;
            BlizzardEvent.Registered += RegisterBlizzardEvent;
            BlizzardEvent.Unregistered += UnregisterBlizzardEvent;
            BuildRouteFromRemainingTargets();
            _mapCanvas.generateVisualContent += DrawMap;
            _mapCanvas.RegisterCallback<GeometryChangedEvent>(OnMapGeometryChanged);
            SetMapOpen(false, false);
        }

        private void OnDisable()
        {
            if (_mapCanvas != null)
            {
                _mapCanvas.generateVisualContent -= DrawMap;
                _mapCanvas.UnregisterCallback<GeometryChangedEvent>(OnMapGeometryChanged);
            }
            if (_mopPad != null)
            {
                _mopPad.SurfacePainted -= RecordCleanedArea;
            }
            if (_snowPad != null)
            {
                _snowPad.SnowCleared -= RecordClearedSnow;
            }
            TrashMapTarget.Registered -= RegisterTrashTarget;
            TrashMapTarget.Unregistered -= UnregisterTrashTarget;
            BlizzardEvent.Registered -= RegisterBlizzardEvent;
            BlizzardEvent.Unregistered -= UnregisterBlizzardEvent;

            DOTween.Kill(_shell);
            DOTween.Kill(_backdrop);
            DOTween.Kill(_vehicleMarker);
            _visibilitySequence?.Kill();
            ReleaseWorldCamera();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            bool shouldOpen = keyboard != null && keyboard.tabKey.isPressed;
            if (shouldOpen != _isMapOpen)
            {
                SetMapOpen(shouldOpen, true);
            }
        }

        /// <summary>
        /// 실제 맵을 정사영으로 렌더링해 도로와 건물 배치를 보여준다.
        /// UI는 저해상도 RenderTexture만 읽고 월드 오브젝트나 게임 판정을 변경하지 않는다.
        /// </summary>
        private void SetupWorldCamera()
        {
            if (_mapBackground == null || _mapCamera != null)
            {
                return;
            }

            _mapTexture = new RenderTexture(384, 384, 16, RenderTextureFormat.ARGB32)
            {
                name = "Vehicle Route Minimap",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                antiAliasing = 1,
                useMipMap = false,
                autoGenerateMips = false
            };
            _mapTexture.Create();

            _mapCameraObject = new GameObject("Minimap Runtime Camera");
            _mapCameraObject.transform.SetParent(transform, false);
            _mapCameraObject.transform.SetPositionAndRotation(
                new Vector3(_worldBounds.center.x, _worldBounds.max.y + 120f, _worldBounds.center.z),
                Quaternion.Euler(90f, 0f, 0f));

            _mapCamera = _mapCameraObject.AddComponent<Camera>();
            _mapCamera.clearFlags = CameraClearFlags.SolidColor;
            _mapCamera.backgroundColor = new Color(0.043f, 0.149f, 0.220f, 1f);
            _mapCamera.orthographic = true;
            _mapCamera.orthographicSize = Mathf.Max(_worldBounds.extents.x, _worldBounds.extents.z) * 1.04f;
            _mapCamera.nearClipPlane = 0.1f;
            _mapCamera.farClipPlane = 260f;
            _mapCamera.allowHDR = false;
            _mapCamera.allowMSAA = false;
            _mapCamera.useOcclusionCulling = false;
            int uiLayer = LayerMask.NameToLayer("UI");
            if (uiLayer >= 0)
            {
                _mapCamera.cullingMask &= ~(1 << uiLayer);
            }
            _mapCamera.targetTexture = _mapTexture;
            _mapCamera.enabled = false;
            _mapBackground.image = _mapTexture;
        }

        private void ReleaseWorldCamera()
        {
            if (_mapBackground != null)
            {
                _mapBackground.image = null;
            }
            if (_mapCamera != null)
            {
                _mapCamera.targetTexture = null;
            }
            if (_mapTexture != null)
            {
                _mapTexture.Release();
                Destroy(_mapTexture);
                _mapTexture = null;
            }
            if (_mapCameraObject != null)
            {
                Destroy(_mapCameraObject);
                _mapCameraObject = null;
                _mapCamera = null;
            }
        }

        private void LateUpdate()
        {
            if (!_isMapOpen || _vehicle == null || _mapCanvas == null || _mapCanvas.resolvedStyle.width <= 0f)
            {
                return;
            }

            PositionPointMarker(_vehicleMarker, _vehicle.transform.position, 46f, 58f);
            _vehicleMarker.style.rotate = new Rotate(new Angle(_vehicle.transform.eulerAngles.y));
            UpdateWorldMarkers();

            int percent = Mathf.Clamp(
                Mathf.RoundToInt(_cleanedCellCount / (float)(CoverageResolution * CoverageResolution) * 100f),
                0,
                100);
            if (percent != _displayedPercent)
            {
                _displayedPercent = percent;
                _progressLabel.text = PercentLabels[percent];
            }

            int pulseStep = percent / 20;
            if (pulseStep > _lastPulseStep)
            {
                _lastPulseStep = pulseStep;
                PulseMarker();
            }
        }

        private void DiscoverWorldObjects()
        {
            _worldMarkers.Clear();
            _trashMarkers.Clear();
            _obstacleMarkers.Clear();
            _weatherMarkers.Clear();

            foreach (TrashMapTarget target in FindObjectsByType<TrashMapTarget>())
            {
                RegisterTrashTarget(target);
            }

            foreach (BlizzardEvent weatherEvent in FindObjectsByType<BlizzardEvent>())
            {
                RegisterBlizzardEvent(weatherEvent);
            }

            foreach (BoxCollider obstacle in FindObjectsByType<BoxCollider>()
                         .Where(collider => collider.name.StartsWith("JumpObstacle")))
            {
                VisualElement element = new VisualElement { pickingMode = PickingMode.Ignore };
                element.AddToClassList("obstacle-marker");
                _worldMarkers.Add(element);
                _obstacleMarkers.Add(new WorldMarker
                {
                    Transform = obstacle.transform,
                    Collider = obstacle,
                    Element = element,
                    LastPosition = obstacle.transform.position
                });
            }

            _remainingTrashCount = _trashMarkers.Count;
        }

        private void RegisterTrashTarget(TrashMapTarget target)
        {
            if (target == null || _trashMarkers.Any(marker => marker.TrashTarget == target))
            {
                return;
            }

            VisualElement element = new VisualElement { pickingMode = PickingMode.Ignore };
            element.AddToClassList("trash-marker");
            element.EnableInClassList("trash-marker-medium", target.Size == TrashMapTarget.TrashSize.Medium);
            element.EnableInClassList("trash-marker-large", target.Size == TrashMapTarget.TrashSize.Large);
            _worldMarkers.Add(element);
            _trashMarkers.Add(new WorldMarker
            {
                TrashTarget = target,
                Transform = target.transform,
                Collider = target.GetComponent<Collider>(),
                Element = element,
                LastPosition = target.WorldPosition
            });
            _remainingTrashCount = _trashMarkers.Count(marker => marker.TrashTarget != null && marker.TrashTarget.isActiveAndEnabled);
            BuildRouteFromRemainingTargets();
        }

        private void UnregisterTrashTarget(TrashMapTarget target)
        {
            WorldMarker marker = _trashMarkers.FirstOrDefault(item => item.TrashTarget == target);
            if (marker == null)
            {
                return;
            }

            marker.LastPosition = target.WorldPosition;
            marker.TrashTarget = null;
            marker.Transform = null;
            marker.Collider = null;
            _remainingTrashCount = _trashMarkers.Count(item => item.TrashTarget != null && item.TrashTarget.isActiveAndEnabled);
            BuildRouteFromRemainingTargets();
        }

        private void RegisterBlizzardEvent(BlizzardEvent weatherEvent)
        {
            if (weatherEvent == null || _weatherMarkers.ContainsKey(weatherEvent))
            {
                return;
            }

            VisualElement marker = new VisualElement { pickingMode = PickingMode.Ignore };
            marker.AddToClassList("weather-event-marker");
            Label icon = new Label("❄") { name = "weather-event-icon", pickingMode = PickingMode.Ignore };
            icon.AddToClassList("weather-event-icon");
            marker.Add(icon);
            _worldMarkers.Add(marker);
            _weatherMarkers.Add(weatherEvent, marker);
        }

        private void UnregisterBlizzardEvent(BlizzardEvent weatherEvent)
        {
            if (weatherEvent == null || !_weatherMarkers.TryGetValue(weatherEvent, out VisualElement marker))
            {
                return;
            }

            marker.RemoveFromHierarchy();
            _weatherMarkers.Remove(weatherEvent);
        }

        private void BuildRouteFromRemainingTargets()
        {
            _routeWorldPoints.Clear();
            _routeWorldPoints.Add(_vehicle.transform.position);
            foreach (WorldMarker marker in _trashMarkers
                         .Where(marker => marker.TrashTarget != null && marker.TrashTarget.isActiveAndEnabled)
                         .OrderBy(marker => marker.Transform.position.z))
            {
                _routeWorldPoints.Add(marker.Transform.position);
            }
            _mapCanvas?.MarkDirtyRepaint();
        }

        private void UpdateWorldMarkers()
        {
            int remainingTrash = 0;
            foreach (WorldMarker marker in _trashMarkers)
            {
                if (marker.TrashTarget == null || !marker.TrashTarget.isActiveAndEnabled)
                {
                    marker.Element.EnableInClassList("trash-marker", false);
                    marker.Element.EnableInClassList("cleaned-target-marker", true);
                    marker.Element.style.display = DisplayStyle.Flex;
                    PositionPointMarker(marker.Element, marker.LastPosition, 15f, 15f);
                    continue;
                }

                remainingTrash++;
                marker.LastPosition = marker.TrashTarget.WorldPosition;
                marker.Element.EnableInClassList("trash-marker", true);
                marker.Element.EnableInClassList("cleaned-target-marker", false);
                marker.Element.style.display = DisplayStyle.Flex;
                PositionPointMarker(marker.Element, marker.Transform.position, 15f, 15f);
            }

            if (remainingTrash != _remainingTrashCount)
            {
                _remainingTrashCount = remainingTrash;
                BuildRouteFromRemainingTargets();
            }

            foreach (WorldMarker marker in _obstacleMarkers)
            {
                if (marker.Collider == null)
                {
                    continue;
                }

                Rect rect = WorldBoundsToMapRect(marker.Collider.bounds);
                marker.Element.style.left = rect.x;
                marker.Element.style.top = rect.y;
                marker.Element.style.width = Mathf.Max(8f, rect.width);
                marker.Element.style.height = Mathf.Max(5f, rect.height);
                marker.Element.style.rotate = new Rotate(new Angle(marker.Transform.eulerAngles.y));
            }

            foreach (KeyValuePair<BlizzardEvent, VisualElement> pair in _weatherMarkers)
            {
                BlizzardEvent weatherEvent = pair.Key;
                VisualElement marker = pair.Value;
                bool visible = weatherEvent != null
                    && weatherEvent.isActiveAndEnabled
                    && weatherEvent.IsVisibleOnMap;
                marker.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
                if (!visible)
                {
                    continue;
                }

                Vector3 center = weatherEvent.EventCenter;
                Vector2 halfExtents = weatherEvent.AffectedHalfExtents;
                Bounds affectedBounds = new Bounds(
                    center,
                    new Vector3(halfExtents.x * 2f, 1f, halfExtents.y * 2f));
                Rect rect = WorldBoundsToMapRect(affectedBounds);
                marker.style.left = rect.x;
                marker.style.top = rect.y;
                marker.style.width = Mathf.Max(28f, rect.width);
                marker.style.height = Mathf.Max(28f, rect.height);

                bool warning = weatherEvent.Phase == EBlizzardEventPhase.Warning;
                bool active = weatherEvent.Phase == EBlizzardEventPhase.Active;
                marker.EnableInClassList("weather-event-warning", warning);
                marker.EnableInClassList("weather-event-active", active);
                marker.EnableInClassList(
                    "weather-event-recovery",
                    weatherEvent.Phase == EBlizzardEventPhase.Recovery);

                float pulse = 0.5f + 0.5f * Mathf.Sin(Time.unscaledTime * 6f);
                float opacity = warning
                    ? Mathf.Lerp(0.55f, 0.9f, pulse)
                    : active
                        ? 1f
                        : 1f - weatherEvent.PhaseProgress;
                marker.style.opacity = opacity;

                VisualElement icon = marker.Q<VisualElement>("weather-event-icon");
                if (icon != null)
                {
                    float scale = warning ? Mathf.Lerp(0.92f, 1.08f, pulse) : 1f;
                    icon.style.scale = new Scale(new Vector2(scale, scale));
                }
            }
        }

        /// <summary>MopPad가 실제 바닥에 전달한 회전 사각 붓을 미니맵 셀에 누적한다.</summary>
        public void RecordCleanedArea(Vector3 worldPosition, Quaternion worldRotation, Vector2 halfExtents)
        {
            Quaternion inverseRotation = Quaternion.Inverse(worldRotation);
            float cellSizeX = _worldBounds.size.x / CoverageResolution;
            float cellSizeZ = _worldBounds.size.z / CoverageResolution;
            float radius = halfExtents.magnitude + Mathf.Max(cellSizeX, cellSizeZ);
            int minX = WorldToCellX(worldPosition.x - radius);
            int maxX = WorldToCellX(worldPosition.x + radius);
            int minZ = WorldToCellZ(worldPosition.z - radius);
            int maxZ = WorldToCellZ(worldPosition.z + radius);
            bool changed = false;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (_cleanedCells[x, z])
                    {
                        continue;
                    }

                    Vector3 cellWorld = new Vector3(
                        Mathf.Lerp(_worldBounds.min.x, _worldBounds.max.x, (x + 0.5f) / CoverageResolution),
                        worldPosition.y,
                        Mathf.Lerp(_worldBounds.min.z, _worldBounds.max.z, (z + 0.5f) / CoverageResolution));
                    Vector3 local = inverseRotation * (cellWorld - worldPosition);
                    if (Mathf.Abs(local.x) > halfExtents.x + cellSizeX * 0.5f
                        || Mathf.Abs(local.z) > halfExtents.y + cellSizeZ * 0.5f)
                    {
                        continue;
                    }

                    _cleanedCells[x, z] = true;
                    _cleanedCellCount++;
                    changed = true;
                }
            }

            if (changed)
            {
                _mapCanvas?.MarkDirtyRepaint();
            }
        }

        /// <summary>SnowVehiclePad가 실제로 눈을 제거한 회전 사각 영역을 동일한 미니맵 셀에 누적한다.</summary>
        private void RecordClearedSnow(SnowStampArea area)
        {
            int minX = WorldToCellX(area.MinX);
            int maxX = WorldToCellX(area.MaxX);
            int minZ = WorldToCellZ(area.MinZ);
            int maxZ = WorldToCellZ(area.MaxZ);
            bool changed = false;

            for (int z = minZ; z <= maxZ; z++)
            {
                for (int x = minX; x <= maxX; x++)
                {
                    if (_cleanedCells[x, z])
                    {
                        continue;
                    }

                    float worldX = Mathf.Lerp(
                        _worldBounds.min.x,
                        _worldBounds.max.x,
                        (x + 0.5f) / CoverageResolution);
                    float worldZ = Mathf.Lerp(
                        _worldBounds.min.z,
                        _worldBounds.max.z,
                        (z + 0.5f) / CoverageResolution);
                    if (!area.Contains(worldX, worldZ))
                    {
                        continue;
                    }

                    _cleanedCells[x, z] = true;
                    _cleanedCellCount++;
                    changed = true;
                }
            }

            if (changed)
            {
                _mapCanvas?.MarkDirtyRepaint();
            }
        }

        private void DrawMap(MeshGenerationContext context)
        {
            DrawCleanedCoverage(context.painter2D);
            if (_routeWorldPoints.Count < 2)
            {
                return;
            }

            Vector2[] points = _routeWorldPoints.Select(WorldToMap).ToArray();
            DrawPolyline(context.painter2D, points, new Color(0.043f, 0.149f, 0.220f, 0.94f), 15f);
            DrawPolyline(context.painter2D, points, Color.white, 10f);
            DrawPolyline(context.painter2D, points, new Color(0.388f, 0.839f, 0.757f, 0.95f), 5f);
        }

        private void DrawCleanedCoverage(Painter2D painter)
        {
            Color fill = new Color(0.388f, 0.839f, 0.757f, 0.58f);
            for (int z = 0; z < CoverageResolution; z++)
            {
                int runStart = -1;
                for (int x = 0; x <= CoverageResolution; x++)
                {
                    bool cleaned = x < CoverageResolution && _cleanedCells[x, z];
                    if (cleaned && runStart < 0)
                    {
                        runStart = x;
                    }
                    else if (!cleaned && runStart >= 0)
                    {
                        DrawCoverageRun(painter, runStart, x, z, fill);
                        runStart = -1;
                    }
                }
            }
        }

        private void DrawCoverageRun(Painter2D painter, int startX, int endX, int z, Color fill)
        {
            Vector3 worldMin = new Vector3(
                Mathf.Lerp(_worldBounds.min.x, _worldBounds.max.x, startX / (float)CoverageResolution),
                0f,
                Mathf.Lerp(_worldBounds.min.z, _worldBounds.max.z, z / (float)CoverageResolution));
            Vector3 worldMax = new Vector3(
                Mathf.Lerp(_worldBounds.min.x, _worldBounds.max.x, endX / (float)CoverageResolution),
                0f,
                Mathf.Lerp(_worldBounds.min.z, _worldBounds.max.z, (z + 1f) / CoverageResolution));
            Vector2 bottomLeft = WorldToMap(worldMin);
            Vector2 topRight = WorldToMap(worldMax);

            painter.BeginPath();
            painter.fillColor = fill;
            painter.MoveTo(new Vector2(bottomLeft.x, topRight.y));
            painter.LineTo(topRight);
            painter.LineTo(new Vector2(topRight.x, bottomLeft.y));
            painter.LineTo(bottomLeft);
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawPolyline(Painter2D painter, Vector2[] points, Color color, float width)
        {
            painter.BeginPath();
            painter.strokeColor = color;
            painter.lineWidth = width;
            painter.lineCap = LineCap.Round;
            painter.lineJoin = LineJoin.Round;
            painter.MoveTo(points[0]);
            for (int index = 1; index < points.Length; index++)
            {
                painter.LineTo(points[index]);
            }
            painter.Stroke();
        }

        private void OnMapGeometryChanged(GeometryChangedEvent evt)
        {
            UpdateWorldMarkers();
            _mapCanvas.MarkDirtyRepaint();
        }

        private void PositionPointMarker(VisualElement marker, Vector3 worldPosition, float width, float height)
        {
            Vector2 point = WorldToMap(worldPosition);
            marker.style.left = point.x - width * 0.5f;
            marker.style.top = point.y - height * 0.5f;
        }

        private Vector2 WorldToMap(Vector3 worldPosition)
        {
            float width = Mathf.Max(1f, _mapCanvas.resolvedStyle.width);
            float height = Mathf.Max(1f, _mapCanvas.resolvedStyle.height);
            float x = Mathf.InverseLerp(_worldBounds.min.x, _worldBounds.max.x, worldPosition.x);
            float z = Mathf.InverseLerp(_worldBounds.min.z, _worldBounds.max.z, worldPosition.z);
            return new Vector2(
                Mathf.Lerp(MapPadding, width - MapPadding, x),
                Mathf.Lerp(height - MapPadding, MapPadding, z));
        }

        private Rect WorldBoundsToMapRect(Bounds bounds)
        {
            Vector2 topLeft = WorldToMap(new Vector3(bounds.min.x, 0f, bounds.max.z));
            Vector2 bottomRight = WorldToMap(new Vector3(bounds.max.x, 0f, bounds.min.z));
            return Rect.MinMaxRect(topLeft.x, topLeft.y, bottomRight.x, bottomRight.y);
        }

        private int WorldToCellX(float worldX)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.InverseLerp(_worldBounds.min.x, _worldBounds.max.x, worldX) * CoverageResolution),
                0,
                CoverageResolution - 1);
        }

        private int WorldToCellZ(float worldZ)
        {
            return Mathf.Clamp(
                Mathf.FloorToInt(Mathf.InverseLerp(_worldBounds.min.z, _worldBounds.max.z, worldZ) * CoverageResolution),
                0,
                CoverageResolution - 1);
        }

        public void SetMapOpen(bool open, bool animate = true)
        {
            _isMapOpen = open;
            _visibilitySequence?.Kill();
            DOTween.Kill(_shell);
            DOTween.Kill(_backdrop);

            if (!animate)
            {
                _shell.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                _backdrop.style.display = open ? DisplayStyle.Flex : DisplayStyle.None;
                _shell.style.opacity = open ? 1f : 0f;
                _backdrop.style.opacity = open ? 1f : 0f;
                _shell.style.translate = new Translate(0f, 0f, 0f);
                _shell.style.scale = new Scale(Vector2.one);
                if (_mapCamera != null)
                {
                    _mapCamera.enabled = open;
                }
                return;
            }

            if (open)
            {
                if (_mapCamera != null)
                {
                    _mapCamera.enabled = true;
                }

                _shell.style.display = DisplayStyle.Flex;
                _backdrop.style.display = DisplayStyle.Flex;
                _shell.style.opacity = 0f;
                _backdrop.style.opacity = 0f;
                _shell.style.translate = new Translate(0f, 18f, 0f);
                _shell.style.scale = new Scale(new Vector2(0.94f, 0.94f));

                float shellOpacity = 0f;
                float backdropOpacity = 0f;
                Vector2 position = new Vector2(0f, 18f);
                Vector2 scale = new Vector2(0.94f, 0.94f);
                _visibilitySequence = DOTween.Sequence()
                    .SetTarget(_shell)
                    .SetUpdate(true)
                    .Append(DOTween.To(() => shellOpacity, value =>
                    {
                        shellOpacity = value;
                        _shell.style.opacity = value;
                    }, 1f, 0.16f).SetEase(Ease.OutQuad))
                    .Join(DOTween.To(() => backdropOpacity, value =>
                    {
                        backdropOpacity = value;
                        _backdrop.style.opacity = value;
                    }, 1f, 0.16f).SetEase(Ease.OutQuad))
                    .Join(DOTween.To(() => position, value =>
                    {
                        position = value;
                        _shell.style.translate = new Translate(value.x, value.y, 0f);
                    }, Vector2.zero, 0.28f).SetEase(Ease.OutBack))
                    .Join(DOTween.To(() => scale, value =>
                    {
                        scale = value;
                        _shell.style.scale = new Scale(value);
                    }, Vector2.one, 0.26f).SetEase(Ease.OutBack));
                _mapCanvas?.MarkDirtyRepaint();
                return;
            }

            float closingShellOpacity = _shell.resolvedStyle.opacity;
            float closingBackdropOpacity = _backdrop.resolvedStyle.opacity;
            Vector2 closingScale = _shell.resolvedStyle.scale.value;
            _visibilitySequence = DOTween.Sequence()
                .SetTarget(_shell)
                .SetUpdate(true)
                .Append(DOTween.To(() => closingShellOpacity, value =>
                {
                    closingShellOpacity = value;
                    _shell.style.opacity = value;
                }, 0f, 0.16f).SetEase(Ease.InQuad))
                .Join(DOTween.To(() => closingBackdropOpacity, value =>
                {
                    closingBackdropOpacity = value;
                    _backdrop.style.opacity = value;
                }, 0f, 0.16f).SetEase(Ease.InQuad))
                .Join(DOTween.To(() => closingScale, value =>
                {
                    closingScale = value;
                    _shell.style.scale = new Scale(value);
                }, new Vector2(0.96f, 0.96f), 0.18f).SetEase(Ease.InQuad))
                .OnComplete(() =>
                {
                    _shell.style.display = DisplayStyle.None;
                    _backdrop.style.display = DisplayStyle.None;
                    if (_mapCamera != null)
                    {
                        _mapCamera.enabled = false;
                    }
                });
        }

        private void PulseMarker()
        {
            DOTween.Kill(_vehicleMarker);
            Vector2 scale = Vector2.one;
            DOTween.Sequence()
                .SetTarget(_vehicleMarker)
                .Append(DOTween.To(() => scale, value =>
                {
                    scale = value;
                    _vehicleMarker.style.scale = new Scale(value);
                }, new Vector2(1.16f, 1.16f), 0.11f).SetEase(Ease.OutQuad))
                .Append(DOTween.To(() => scale, value =>
                {
                    scale = value;
                    _vehicleMarker.style.scale = new Scale(value);
                }, Vector2.one, 0.15f).SetEase(Ease.OutBack));
        }

        private static string[] BuildPercentLabelCache()
        {
            string[] labels = new string[101];
            for (int percent = 0; percent < labels.Length; percent++)
            {
                labels[percent] = $"{percent}%";
            }
            return labels;
        }
    }
}
