using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// Joins two selected road ends and distributes the direction change across both source splines.
    /// Generated road meshes remain rebuild products and are never edited directly.
    /// </summary>
    internal static class RoadEndpointConnector
    {
        private const float DefaultMaximumGap = 10f;
        private const float MaximumHeightGap = 2.5f;
        private const float MinimumInteriorOpening = 35f;
        private const float MinimumAlignmentLength = 0.75f;
        private const float MaximumAlignmentLength = 4.5f;
        private const float MinimumKnotSeparation = 0.35f;

        [MenuItem("PPack/Level Design/Roads/Connect Selected Road Ends Smoothly")]
        public static void ConnectSelectedFromMenu()
        {
            TerrainRoadPath[] selected = Selection.gameObjects
                .Select(selectedObject => selectedObject.GetComponentInParent<TerrainRoadPath>())
                .Where(path => path != null)
                .Distinct()
                .ToArray();
            if (selected.Length != 2)
            {
                Debug.LogWarning("[RoadConnector] Select exactly two TerrainRoadPath source objects.");
                return;
            }

            TerrainRoadPath first = selected[0];
            TerrainRoadPath second = selected[1];
            if (first.gameObject.scene != second.gameObject.scene)
            {
                Debug.LogWarning("[RoadConnector] Both roads must belong to the same loaded Scene.");
                return;
            }

            List<Vector3> firstBefore = new();
            List<Vector3> secondBefore = new();
            first.GetWorldControlPoints(firstBefore);
            second.GetWorldControlPoints(secondBefore);
            if (!TryBuildSmoothedConnection(
                    firstBefore,
                    first.Width,
                    secondBefore,
                    second.Width,
                    out List<Vector3> firstAfter,
                    out List<Vector3> secondAfter,
                    out RoadEnd firstEnd,
                    out RoadEnd secondEnd,
                    out string error))
            {
                Debug.LogWarning("[RoadConnector] " + error);
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Connect Selected Road Ends Smoothly");
            Undo.RegisterFullObjectHierarchyUndo(first.gameObject, "Connect Selected Road Ends Smoothly");
            Undo.RegisterFullObjectHierarchyUndo(second.gameObject, "Connect Selected Road Ends Smoothly");

            ConfigureFrom(first, firstAfter);
            ConfigureFrom(second, secondAfter);
            EditorUtility.SetDirty(first);
            EditorUtility.SetDirty(second);
            Scene scene = first.gameObject.scene;
            EditorSceneManager.MarkSceneDirty(scene);

            bool rebuilt = TryRebuildEasyRoadSources(scene, first, second);
            Undo.CollapseUndoOperations(undoGroup);
            Selection.objects = new UnityEngine.Object[] { first.gameObject, second.gameObject };
            SceneView.RepaintAll();

            string rebuildMessage = rebuilt
                ? "The EasyRoads meshes and colliders were rebuilt."
                : "The source splines were updated; run this Scene's road rebuild command to refresh generated meshes and colliders.";
            Debug.Log(
                $"[RoadConnector] Connected {first.name} ({firstEnd}) and {second.name} ({secondEnd}). " +
                rebuildMessage);
        }

        internal static bool TryBuildSmoothedConnection(
            IReadOnlyList<Vector3> first,
            float firstWidth,
            IReadOnlyList<Vector3> second,
            float secondWidth,
            out List<Vector3> firstResult,
            out List<Vector3> secondResult,
            out RoadEnd firstEnd,
            out RoadEnd secondEnd,
            out string error,
            float maximumGap = DefaultMaximumGap)
        {
            firstResult = first == null ? new List<Vector3>() : new List<Vector3>(first);
            secondResult = second == null ? new List<Vector3>() : new List<Vector3>(second);
            firstEnd = RoadEnd.Start;
            secondEnd = RoadEnd.Start;
            error = string.Empty;

            if (firstResult.Count < 2 || secondResult.Count < 2)
            {
                error = "Both roads need at least two spline control points.";
                return false;
            }

            FindClosestEnds(firstResult, secondResult, out firstEnd, out secondEnd, out float gap);
            float allowedGap = Mathf.Max(
                Mathf.Max(firstWidth, secondWidth) * 1.5f,
                Mathf.Max(0.5f, maximumGap));
            if (gap > allowedGap)
            {
                error = $"The closest endpoints are {gap:F2} m apart; allowed distance is {allowedGap:F2} m.";
                return false;
            }

            Vector3 firstPoint = GetEndpoint(firstResult, firstEnd);
            Vector3 secondPoint = GetEndpoint(secondResult, secondEnd);
            if (Mathf.Abs(firstPoint.y - secondPoint.y) > MaximumHeightGap)
            {
                error =
                    $"The closest endpoints differ by {Mathf.Abs(firstPoint.y - secondPoint.y):F2} m in height. " +
                    "Grade the terrain or road profiles before connecting them.";
                return false;
            }

            Vector3 firstInward = GetInwardDirection(firstResult, firstEnd);
            Vector3 secondInward = GetInwardDirection(secondResult, secondEnd);
            if (firstInward.sqrMagnitude < 0.0001f || secondInward.sqrMagnitude < 0.0001f)
            {
                error = "A road endpoint has no usable horizontal direction.";
                return false;
            }

            float interiorOpening = Vector3.Angle(firstInward, secondInward);
            if (interiorOpening < MinimumInteriorOpening)
            {
                error =
                    $"The two road interiors open only {interiorOpening:F1}°. " +
                    "They form a branch or U-turn, not an end-to-end connection.";
                return false;
            }

            Vector3 join = (firstPoint + secondPoint) * 0.5f;
            Vector3 firstOutward = -firstInward;
            Vector3 secondOutward = -secondInward;
            Vector3 sharedAxis = Vector3.ProjectOnPlane(firstOutward - secondOutward, Vector3.up);
            if (sharedAxis.sqrMagnitude < 0.0001f)
                sharedAxis = Vector3.ProjectOnPlane(secondPoint - firstPoint, Vector3.up);
            if (sharedAxis.sqrMagnitude < 0.0001f)
                sharedAxis = firstOutward;
            sharedAxis.Normalize();
            if (Vector3.Dot(sharedAxis, firstOutward) < 0f) sharedAxis = -sharedAxis;

            ApplyEndpointBlend(firstResult, firstEnd, join, sharedAxis, firstWidth);
            ApplyEndpointBlend(secondResult, secondEnd, join, -sharedAxis, secondWidth);
            return true;
        }

        private static void FindClosestEnds(
            IReadOnlyList<Vector3> first,
            IReadOnlyList<Vector3> second,
            out RoadEnd firstEnd,
            out RoadEnd secondEnd,
            out float bestDistance)
        {
            firstEnd = RoadEnd.Start;
            secondEnd = RoadEnd.Start;
            bestDistance = float.PositiveInfinity;
            foreach (RoadEnd candidateFirst in Enum.GetValues(typeof(RoadEnd)))
            {
                foreach (RoadEnd candidateSecond in Enum.GetValues(typeof(RoadEnd)))
                {
                    Vector3 a = GetEndpoint(first, candidateFirst);
                    Vector3 b = GetEndpoint(second, candidateSecond);
                    float distance = HorizontalDistance(a, b);
                    if (distance >= bestDistance) continue;
                    bestDistance = distance;
                    firstEnd = candidateFirst;
                    secondEnd = candidateSecond;
                }
            }
        }

        private static void ApplyEndpointBlend(
            List<Vector3> points,
            RoadEnd end,
            Vector3 join,
            Vector3 desiredOutward,
            float width)
        {
            int endpointIndex = end == RoadEnd.Start ? 0 : points.Count - 1;
            int neighborIndex = end == RoadEnd.Start ? 1 : points.Count - 2;
            Vector3 neighbor = points[neighborIndex];
            points[endpointIndex] = join;

            float available = HorizontalDistance(join, neighbor);
            if (available < MinimumKnotSeparation) return;

            float desiredLength = Mathf.Clamp(
                Mathf.Max(MinimumAlignmentLength, width * 0.72f),
                MinimumAlignmentLength,
                MaximumAlignmentLength);
            float alignmentLength = Mathf.Min(desiredLength, available * 0.58f);
            if (alignmentLength < MinimumKnotSeparation) return;

            Vector3 alignment = join - desiredOutward * alignmentLength;
            float heightT = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(alignmentLength / available));
            alignment.y = Mathf.Lerp(join.y, neighbor.y, heightT);

            if (HorizontalDistance(alignment, neighbor) < MinimumKnotSeparation)
                points[neighborIndex] = alignment;
            else if (end == RoadEnd.Start)
                points.Insert(1, alignment);
            else
                points.Insert(points.Count - 1, alignment);
        }

        private static Vector3 GetEndpoint(IReadOnlyList<Vector3> points, RoadEnd end)
        {
            return points[end == RoadEnd.Start ? 0 : points.Count - 1];
        }

        private static Vector3 GetInwardDirection(IReadOnlyList<Vector3> points, RoadEnd end)
        {
            int endpointIndex = end == RoadEnd.Start ? 0 : points.Count - 1;
            int neighborIndex = end == RoadEnd.Start ? 1 : points.Count - 2;
            return Vector3.ProjectOnPlane(points[neighborIndex] - points[endpointIndex], Vector3.up).normalized;
        }

        private static float HorizontalDistance(Vector3 first, Vector3 second)
        {
            return Vector2.Distance(new Vector2(first.x, first.z), new Vector2(second.x, second.z));
        }

        private static void ConfigureFrom(TerrainRoadPath path, IReadOnlyList<Vector3> controls)
        {
            path.Configure(
                controls,
                path.Width,
                path.EdgeFeather,
                path.BorderWidth,
                path.BorderFeather,
                path.Terrain,
                path.RoadLayer,
                path.BorderLayer);
        }

        private static bool TryRebuildEasyRoadSources(
            Scene scene,
            TerrainRoadPath first,
            TerrainRoadPath second)
        {
            EasyRoadSource firstSource = first.GetComponent<EasyRoadSource>();
            EasyRoadSource secondSource = second.GetComponent<EasyRoadSource>();
            if (firstSource == null || secondSource == null) return false;

            EasyRoadTemplate fallback = firstSource.Template ??
                                        secondSource.Template ??
                                        EasyRoadBuilderPreferences.Template;
            if (fallback == null || fallback.RoadMaterial == null) return false;
            return EasyRoadAuthoring.RebuildScene(
                       scene,
                       fallback,
                       "Connect Selected Road Ends Smoothly",
                       true) > 0;
        }

        internal enum RoadEnd
        {
            Start,
            End
        }
    }
}
