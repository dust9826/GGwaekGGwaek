using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(MeshFilter), typeof(MeshRenderer), typeof(MeshCollider))]
    public sealed class RoadPath : MonoBehaviour
    {
        private const float MinimumWidth = 0.25f;
        private const float MaximumMiterScale = 2.4f;
        private const float JunctionSurfaceLift = 0.008f;
        private const int JunctionSegments = 20;

        [SerializeField] private List<Vector3> _localCenterPoints = new();
        [SerializeField, Min(MinimumWidth)] private float _width = 2.5f;
        [SerializeField] private Material _material;
        [SerializeField, Min(0f)] private float _startJunctionRadius;
        [SerializeField, Min(0f)] private float _endJunctionRadius;

        [NonSerialized] private Mesh _generatedMesh;

        public IReadOnlyList<Vector3> LocalCenterPoints => _localCenterPoints;
        public float Width => _width;
        public Mesh GeneratedMesh => _generatedMesh;
        public float StartJunctionRadius => _startJunctionRadius;
        public float EndJunctionRadius => _endJunctionRadius;

        public void Configure(IReadOnlyList<Vector3> worldCenterPoints, float width, Material material)
        {
            Configure(worldCenterPoints, width, material, 0f, 0f);
        }

        public void Configure(
            IReadOnlyList<Vector3> worldCenterPoints,
            float width,
            Material material,
            float startJunctionRadius,
            float endJunctionRadius)
        {
            if (worldCenterPoints == null) throw new ArgumentNullException(nameof(worldCenterPoints));
            if (worldCenterPoints.Count < 2)
                throw new ArgumentException("A road requires at least two center points.", nameof(worldCenterPoints));

            transform.SetPositionAndRotation(worldCenterPoints[0], Quaternion.identity);
            transform.localScale = Vector3.one;

            _localCenterPoints.Clear();
            for (int i = 0; i < worldCenterPoints.Count; i++)
                _localCenterPoints.Add(transform.InverseTransformPoint(worldCenterPoints[i]));

            _width = Mathf.Max(MinimumWidth, width);
            _material = material;
            _startJunctionRadius = Mathf.Max(0f, startJunctionRadius);
            _endJunctionRadius = Mathf.Max(0f, endJunctionRadius);
            Rebuild();
        }

        public void SetJunctionRadii(float startRadius, float endRadius)
        {
            _startJunctionRadius = Mathf.Max(0f, startRadius);
            _endJunctionRadius = Mathf.Max(0f, endRadius);
            Rebuild();
        }

        public void Rebuild()
        {
            EnsureComponents();
            ReleaseGeneratedMesh();

            if (_localCenterPoints == null || _localCenterPoints.Count < 2)
            {
                GetComponent<MeshFilter>().sharedMesh = null;
                GetComponent<MeshCollider>().sharedMesh = null;
                return;
            }

            _generatedMesh = BuildRibbonMesh(
                _localCenterPoints,
                _width,
                $"{name}_RoadMesh",
                _startJunctionRadius,
                _endJunctionRadius);
            _generatedMesh.hideFlags = HideFlags.DontSave;

            MeshFilter meshFilter = GetComponent<MeshFilter>();
            MeshRenderer meshRenderer = GetComponent<MeshRenderer>();
            MeshCollider meshCollider = GetComponent<MeshCollider>();
            meshFilter.sharedMesh = _generatedMesh;
            meshRenderer.sharedMaterial = _material;
            meshCollider.sharedMesh = null;
            meshCollider.sharedMesh = _generatedMesh;
        }

        public static Mesh BuildRibbonMesh(IReadOnlyList<Vector3> centerPoints, float width, string meshName)
        {
            return BuildRibbonMesh(centerPoints, width, meshName, 0f, 0f);
        }

        public static Mesh BuildRibbonMesh(
            IReadOnlyList<Vector3> centerPoints,
            float width,
            string meshName,
            float startJunctionRadius,
            float endJunctionRadius)
        {
            if (centerPoints == null) throw new ArgumentNullException(nameof(centerPoints));
            if (centerPoints.Count < 2)
                throw new ArgumentException("A road requires at least two center points.", nameof(centerPoints));

            float halfWidth = Mathf.Max(MinimumWidth, width) * 0.5f;
            List<Vector3> vertices = new(centerPoints.Count * 2 + JunctionSegments * 2 + 2);
            List<Vector2> uvs = new(vertices.Capacity);
            List<int> triangles = new((centerPoints.Count - 1) * 6 + JunctionSegments * 6);

            float distance = 0f;
            for (int i = 0; i < centerPoints.Count; i++)
            {
                if (i > 0) distance += Vector3.Distance(centerPoints[i - 1], centerPoints[i]);

                Vector3 joinOffset = CalculateJoinOffset(centerPoints, i, halfWidth);
                vertices.Add(centerPoints[i] - joinOffset);
                vertices.Add(centerPoints[i] + joinOffset);
                uvs.Add(new Vector2(0f, distance));
                uvs.Add(new Vector2(1f, distance));

                if (i >= centerPoints.Count - 1) continue;
                int vertex = i * 2;
                // Clockwise from above so the generated surface normals face upward.
                triangles.Add(vertex);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 1);
                triangles.Add(vertex + 1);
                triangles.Add(vertex + 2);
                triangles.Add(vertex + 3);
            }

            List<Vector3> junctionCenters = new();
            List<float> junctionRadii = new();
            CollectSelfJunctions(centerPoints, halfWidth * 1.12f, junctionCenters, junctionRadii);
            AddOrExpandJunction(centerPoints[0], startJunctionRadius, junctionCenters, junctionRadii);
            AddOrExpandJunction(centerPoints[^1], endJunctionRadius, junctionCenters, junctionRadii);
            for (int i = 0; i < junctionCenters.Count; i++)
                AppendJunctionCap(vertices, uvs, triangles, junctionCenters[i], junctionRadii[i]);

            Mesh mesh = new Mesh { name = string.IsNullOrEmpty(meshName) ? "Road Ribbon" : meshName };
            if (vertices.Count > ushort.MaxValue) mesh.indexFormat = IndexFormat.UInt32;
            mesh.SetVertices(vertices);
            mesh.SetUVs(0, uvs);
            mesh.SetTriangles(triangles, 0);
            mesh.RecalculateNormals();
            mesh.RecalculateTangents();
            mesh.RecalculateBounds();
            return mesh;
        }

        private static Vector3 CalculateJoinOffset(IReadOnlyList<Vector3> points, int index, float halfWidth)
        {
            if (index == 0)
                return CalculateRight(points[1] - points[0]) * halfWidth;
            if (index == points.Count - 1)
                return CalculateRight(points[index] - points[index - 1]) * halfWidth;

            Vector3 incomingRight = CalculateRight(points[index] - points[index - 1]);
            Vector3 outgoingRight = CalculateRight(points[index + 1] - points[index]);
            Vector3 miter = incomingRight + outgoingRight;
            if (miter.sqrMagnitude < 0.001f) return outgoingRight * halfWidth;

            miter.Normalize();
            float denominator = Mathf.Abs(Vector3.Dot(miter, outgoingRight));
            if (denominator < 0.1f) return outgoingRight * halfWidth;

            float scale = Mathf.Min(halfWidth / denominator, halfWidth * MaximumMiterScale);
            return miter * scale;
        }

        private static Vector3 CalculateRight(Vector3 direction)
        {
            direction.y = 0f;
            if (direction.sqrMagnitude < 0.001f) return Vector3.right;
            return Vector3.Cross(Vector3.up, direction.normalized).normalized;
        }

        private static void AppendJunctionCap(
            List<Vector3> vertices,
            List<Vector2> uvs,
            List<int> triangles,
            Vector3 center,
            float radius)
        {
            if (radius <= 0.01f) return;

            int centerIndex = vertices.Count;
            vertices.Add(center + Vector3.up * JunctionSurfaceLift);
            uvs.Add(new Vector2(0f, -1f));

            int ringStart = vertices.Count;
            for (int i = 0; i < JunctionSegments; i++)
            {
                float angle = -Mathf.PI * 2f * i / JunctionSegments;
                Vector3 offset = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * radius;
                vertices.Add(center + offset + Vector3.up * JunctionSurfaceLift);
                // Negative V marks radial junction geometry for the road shader.
                uvs.Add(new Vector2(1f, -1f));
            }

            for (int i = 0; i < JunctionSegments; i++)
            {
                triangles.Add(centerIndex);
                triangles.Add(ringStart + i);
                triangles.Add(ringStart + (i + 1) % JunctionSegments);
            }
        }

        private static void CollectSelfJunctions(
            IReadOnlyList<Vector3> points,
            float radius,
            List<Vector3> centers,
            List<float> radii)
        {
            for (int first = 0; first < points.Count - 1; first++)
            {
                for (int second = first + 2; second < points.Count - 1; second++)
                {
                    if (!TryGetPlanarIntersection(
                            points[first],
                            points[first + 1],
                            points[second],
                            points[second + 1],
                            out Vector3 intersection)) continue;
                    AddOrExpandJunction(intersection, radius, centers, radii);
                }
            }
        }

        private static bool TryGetPlanarIntersection(
            Vector3 firstStart,
            Vector3 firstEnd,
            Vector3 secondStart,
            Vector3 secondEnd,
            out Vector3 intersection)
        {
            Vector2 p = new(firstStart.x, firstStart.z);
            Vector2 r = new(firstEnd.x - firstStart.x, firstEnd.z - firstStart.z);
            Vector2 q = new(secondStart.x, secondStart.z);
            Vector2 s = new(secondEnd.x - secondStart.x, secondEnd.z - secondStart.z);
            float cross = Cross(r, s);
            if (Mathf.Abs(cross) < 0.0001f)
            {
                intersection = default;
                return false;
            }

            Vector2 qMinusP = q - p;
            float firstT = Cross(qMinusP, s) / cross;
            float secondT = Cross(qMinusP, r) / cross;
            const float endpointTolerance = 0.015f;
            if (firstT < -endpointTolerance || firstT > 1f + endpointTolerance
                || secondT < -endpointTolerance || secondT > 1f + endpointTolerance)
            {
                intersection = default;
                return false;
            }

            Vector3 firstPoint = Vector3.Lerp(firstStart, firstEnd, Mathf.Clamp01(firstT));
            Vector3 secondPoint = Vector3.Lerp(secondStart, secondEnd, Mathf.Clamp01(secondT));
            if (Mathf.Abs(firstPoint.y - secondPoint.y) > 0.25f)
            {
                intersection = default;
                return false;
            }

            intersection = (firstPoint + secondPoint) * 0.5f;
            return true;
        }

        private static float Cross(Vector2 left, Vector2 right)
        {
            return left.x * right.y - left.y * right.x;
        }

        private static void AddOrExpandJunction(
            Vector3 center,
            float radius,
            List<Vector3> centers,
            List<float> radii)
        {
            if (radius <= 0.01f) return;
            for (int i = 0; i < centers.Count; i++)
            {
                Vector2 existing = new(centers[i].x, centers[i].z);
                Vector2 candidate = new(center.x, center.z);
                if (Vector2.Distance(existing, candidate) > 0.18f) continue;
                radii[i] = Mathf.Max(radii[i], radius);
                return;
            }

            centers.Add(center);
            radii.Add(radius);
        }

        private void OnEnable()
        {
            Rebuild();
        }

        private void OnValidate()
        {
            _width = Mathf.Max(MinimumWidth, _width);
            _startJunctionRadius = Mathf.Max(0f, _startJunctionRadius);
            _endJunctionRadius = Mathf.Max(0f, _endJunctionRadius);
            Rebuild();
        }

        private void OnDestroy()
        {
            ReleaseGeneratedMesh();
        }

        private void EnsureComponents()
        {
            if (!TryGetComponent(out MeshFilter _)) gameObject.AddComponent<MeshFilter>();
            if (!TryGetComponent(out MeshRenderer _)) gameObject.AddComponent<MeshRenderer>();
            if (!TryGetComponent(out MeshCollider _)) gameObject.AddComponent<MeshCollider>();
        }

        private void ReleaseGeneratedMesh()
        {
            if (_generatedMesh == null) return;

            if (Application.isPlaying) Destroy(_generatedMesh);
            else DestroyImmediate(_generatedMesh);
            _generatedMesh = null;
        }
    }
}
