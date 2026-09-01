using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal sealed class HouseSidewalkPreview
    {
        internal const float MaximumConnectionDistance = 12f;
        private const float EntranceLeadLength = 1f;
        private const float MinimumWidth = 0.9f;
        private const float EasyRoadMarkerSpacing = 2.25f;
        private static readonly Color FillColor = new(1f, 0.28f, 0.04f, 0.34f);
        private static readonly Color EdgeColor = new(1f, 0.78f, 0.08f, 1f);
        private static readonly Color CenterColor = new(1f, 1f, 1f, 0.95f);

        private readonly List<Vector3> _points = new();
        private float _width;
        private RoadSnapResult? _roadConnection;

        internal bool HasPath => _points.Count >= 2;
        internal IReadOnlyList<Vector3> Points => _points;
        internal float Width => _width;

        public void Set(
            GameObject prefab,
            Vector3 placementPosition,
            Quaternion placementRotation,
            RoadEntranceDatabase database)
        {
            Clear();
            if (prefab == null || database == null ||
                !database.TryGetProfile(prefab, out RoadEntranceProfile profile)) return;

            Matrix4x4 matrix = Matrix4x4.TRS(
                placementPosition,
                placementRotation,
                prefab.transform.localScale);
            if (!TryBuildPath(
                    matrix,
                    profile,
                    out List<Vector3> path,
                    out float doorWidth,
                    out RoadSnapResult roadConnection)) return;

            _points.AddRange(path);
            _width = Mathf.Min(
                RoadBuilderPreferences.EntranceRoadWidth,
                Mathf.Max(MinimumWidth, doorWidth));
            _roadConnection = roadConnection;
        }

        public void Clear()
        {
            _points.Clear();
            _width = 0f;
            _roadConnection = null;
        }

        public bool TryGetCreationData(
            out List<Vector3> points,
            out float width,
            out RoadSnapResult? roadConnection)
        {
            points = HasPath ? BuildEasyRoadControlPoints(_points) : null;
            width = _width;
            roadConnection = _roadConnection;
            return points != null && width > 0f && roadConnection.HasValue;
        }

        internal static List<Vector3> BuildEasyRoadControlPoints(IReadOnlyList<Vector3> sampledPath)
        {
            List<Vector3> controls = new();
            if (sampledPath == null || sampledPath.Count == 0) return controls;

            controls.Add(sampledPath[0]);
            Vector3 end = sampledPath[^1];
            for (int i = 1; i < sampledPath.Count - 1; i++)
            {
                Vector3 candidate = sampledPath[i];
                if (Vector3.Distance(controls[^1], candidate) < EasyRoadMarkerSpacing) continue;
                if (Vector3.Distance(candidate, end) < EasyRoadMarkerSpacing) continue;
                controls.Add(candidate);
            }

            if (Vector3.Distance(controls[^1], end) > 0.01f) controls.Add(end);
            return controls;
        }

        public void Draw()
        {
            if (!HasPath || Event.current.type != EventType.Repaint) return;

            int count = _points.Count;
            Vector3[] left = new Vector3[count];
            Vector3[] right = new Vector3[count];
            Vector3[] center = new Vector3[count];
            float halfWidth = _width * 0.5f;

            for (int i = 0; i < count; i++)
            {
                Vector3 direction = i == 0
                    ? _points[1] - _points[0]
                    : i == count - 1
                        ? _points[^1] - _points[^2]
                        : _points[i + 1] - _points[i - 1];
                direction.y = 0f;
                Vector3 side = direction.sqrMagnitude < 0.0001f
                    ? Vector3.right
                    : Vector3.Cross(Vector3.up, direction.normalized);
                Vector3 lifted = _points[i] + Vector3.up * 0.09f;
                center[i] = lifted;
                left[i] = lifted - side * halfWidth;
                right[i] = lifted + side * halfWidth;
            }

            Handles.color = FillColor;
            for (int i = 0; i < count - 1; i++)
                Handles.DrawAAConvexPolygon(left[i], left[i + 1], right[i + 1], right[i]);

            Handles.color = EdgeColor;
            Handles.DrawAAPolyLine(5f, left);
            Handles.DrawAAPolyLine(5f, right);
            Handles.DrawAAPolyLine(4f, left[0], right[0]);
            Handles.DrawAAPolyLine(4f, left[^1], right[^1]);

            Handles.color = CenterColor;
            Handles.DrawAAPolyLine(2.5f, center);
        }

        internal static bool TryBuildPath(
            Matrix4x4 placementMatrix,
            RoadEntranceProfile profile,
            out List<Vector3> path,
            out float doorWidth)
        {
            return TryBuildPath(
                placementMatrix,
                profile,
                out path,
                out doorWidth,
                out _);
        }

        internal static bool TryBuildPath(
            Matrix4x4 placementMatrix,
            RoadEntranceProfile profile,
            out List<Vector3> path,
            out float doorWidth,
            out RoadSnapResult roadConnection)
        {
            path = new List<Vector3>();
            doorWidth = 0f;
            roadConnection = default;
            if (profile == null) return false;

            Vector3 start = placementMatrix.MultiplyPoint3x4(profile.LocalPosition);
            Vector3 forward = Vector3.ProjectOnPlane(
                placementMatrix.MultiplyVector(profile.LocalForward),
                Vector3.up);
            if (forward.sqrMagnitude < 0.0001f) forward = Vector3.forward;
            forward.Normalize();

            Vector3 scale = new(
                placementMatrix.GetColumn(0).magnitude,
                placementMatrix.GetColumn(1).magnitude,
                placementMatrix.GetColumn(2).magnitude);
            doorWidth = profile.DoorWidth * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));

            RoadSurfaceSampler.TryConformToGround(start, out start);
            Vector3 lead = start + forward * EntranceLeadLength;
            RoadSurfaceSampler.TryConformToGround(lead, out lead);

            if (!RoadConnectionUtility.TryFindNearestRoad(
                    lead,
                    null,
                    MaximumConnectionDistance,
                    out RoadSnapResult nearest)) return false;
            roadConnection = nearest;

            Vector3 roadEdge = RoadConnectionUtility.GetRoadEdgePoint(lead, nearest);
            RoadSurfaceSampler.TryConformToGround(roadEdge, out roadEdge);
            if (Vector3.Distance(start, roadEdge) < 0.5f) return false;

            List<Vector3> controls = new() { start, lead };
            if (Vector3.Distance(lead, roadEdge) >= 0.25f) controls.Add(roadEdge);
            path = RoadSurfaceSampler.BuildConformedCenterLine(controls);
            return path.Count >= 2;
        }
    }
}
