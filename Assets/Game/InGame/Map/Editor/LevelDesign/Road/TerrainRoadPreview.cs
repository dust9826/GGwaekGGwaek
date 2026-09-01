using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    internal static class TerrainRoadPreview
    {
        private static readonly Color CenterColor = new(0.12f, 0.9f, 1f, 0.95f);
        private static readonly Color EdgeColor = new(0.48f, 0.31f, 0.2f, 0.98f);
        private static readonly Color FeatherColor = new(1f, 1f, 1f, 0.38f);

        public static void Draw(IReadOnlyList<Vector3> points, float width, float edgeFeather, float borderWidth)
        {
            if (points == null || points.Count < 2) return;

            Vector3[] center = new Vector3[points.Count];
            Vector3[] left = new Vector3[points.Count];
            Vector3[] right = new Vector3[points.Count];
            Vector3[] outerLeft = new Vector3[points.Count];
            Vector3[] outerRight = new Vector3[points.Count];
            float halfWidth = width * 0.5f;

            for (int i = 0; i < points.Count; i++)
            {
                Vector3 direction;
                if (i == 0) direction = points[1] - points[0];
                else if (i == points.Count - 1) direction = points[^1] - points[^2];
                else direction = points[i + 1] - points[i - 1];
                direction.y = 0f;
                Vector3 side = direction.sqrMagnitude < 0.0001f
                    ? Vector3.right
                    : Vector3.Cross(Vector3.up, direction.normalized);
                Vector3 lifted = points[i] + Vector3.up * 0.07f;
                center[i] = lifted;
                left[i] = lifted - side * halfWidth;
                right[i] = lifted + side * halfWidth;
                outerLeft[i] = lifted - side * (halfWidth + borderWidth + edgeFeather);
                outerRight[i] = lifted + side * (halfWidth + borderWidth + edgeFeather);
            }

            Handles.color = FeatherColor;
            Handles.DrawAAPolyLine(1.5f, outerLeft);
            Handles.DrawAAPolyLine(1.5f, outerRight);
            Handles.color = EdgeColor;
            Handles.DrawAAPolyLine(3f, left);
            Handles.DrawAAPolyLine(3f, right);
            Handles.color = CenterColor;
            Handles.DrawAAPolyLine(2f, center);
        }
    }
}
