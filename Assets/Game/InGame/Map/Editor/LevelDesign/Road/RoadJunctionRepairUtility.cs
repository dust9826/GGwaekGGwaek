using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class RoadJunctionRepairUtility
    {
        private const float ConnectionTolerance = 0.22f;

        [MenuItem("PPack/Level Design/Repair All Road Junctions")]
        public static void RepairAllFromMenu()
        {
            RoadPath[] roads = Object.FindObjectsByType<RoadPath>();
            if (roads.Length == 0)
            {
                Debug.Log("Road junction repair: no RoadPath components were found in the loaded scenes.");
                return;
            }

            Undo.IncrementCurrentGroup();
            int undoGroup = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Repair Road Junctions");
            int repaired = 0;

            for (int i = 0; i < roads.Length; i++)
            {
                RoadPath road = roads[i];
                if (road == null || road.LocalCenterPoints.Count < 2) continue;

                Vector3 start = road.transform.TransformPoint(road.LocalCenterPoints[0]);
                Vector3 end = road.transform.TransformPoint(road.LocalCenterPoints[^1]);
                float startRadius = FindRequiredRadius(road, start, roads, i);
                float endRadius = FindRequiredRadius(road, end, roads, i);
                if (Mathf.Approximately(road.StartJunctionRadius, startRadius)
                    && Mathf.Approximately(road.EndJunctionRadius, endRadius)) continue;

                Undo.RecordObject(road, "Repair Road Junctions");
                road.SetJunctionRadii(startRadius, endRadius);
                EditorUtility.SetDirty(road);
                repaired++;
            }

            Undo.CollapseUndoOperations(undoGroup);
            SceneView.RepaintAll();
            Debug.Log($"Road junction repair: updated {repaired} of {roads.Length} roads. Scene was not saved.");
        }

        internal static float FindRequiredRadius(
            RoadPath source,
            Vector3 endpoint,
            RoadPath[] roads,
            int sourceIndex)
        {
            float bestRadius = 0f;
            for (int roadIndex = 0; roadIndex < roads.Length; roadIndex++)
            {
                RoadPath target = roads[roadIndex];
                if (target == null || target == source || target.LocalCenterPoints.Count < 2) continue;

                for (int segment = 0; segment < target.LocalCenterPoints.Count - 1; segment++)
                {
                    Vector3 start = target.transform.TransformPoint(target.LocalCenterPoints[segment]);
                    Vector3 end = target.transform.TransformPoint(target.LocalCenterPoints[segment + 1]);
                    Vector3 closest = RoadConnectionUtility.ClosestPointOnSegmentXZ(endpoint, start, end);
                    float planarDistance = Vector2.Distance(
                        new Vector2(endpoint.x, endpoint.z),
                        new Vector2(closest.x, closest.z));
                    if (planarDistance > ConnectionTolerance || Mathf.Abs(endpoint.y - closest.y) > 0.25f) continue;

                    bool targetEndpoint = Vector3.Distance(closest, start) <= ConnectionTolerance
                                          || Vector3.Distance(closest, end) <= ConnectionTolerance;
                    // When two endpoints meet, only one road owns the cap to avoid coplanar discs.
                    if (targetEndpoint && sourceIndex < roadIndex) continue;

                    bestRadius = Mathf.Max(bestRadius, Mathf.Max(source.Width, target.Width) * 0.56f);
                }
            }
            return bestRadius;
        }
    }
}
