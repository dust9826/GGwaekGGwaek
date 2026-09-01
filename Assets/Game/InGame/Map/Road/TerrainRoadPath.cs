using System;
using System.Collections.Generic;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Serialization;
using UnityEngine.Splines;

namespace PPack
{
    /// <summary>
    /// Editable spline and style data for a road baked into a Terrain.
    /// The Terrain owns rendering and collision; this object is the rebuildable source of truth.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SplineContainer))]
    public sealed class TerrainRoadPath : MonoBehaviour
    {
        private const float MinimumWidth = 0.25f;
        private const float DefaultSampleSpacing = 0.2f;

        [SerializeField] private Terrain _terrain;
        [SerializeField] private TerrainLayer _roadLayer;
        [SerializeField] private TerrainLayer _borderLayer;
        [FormerlySerializedAs("_localCenterPoints")]
        [SerializeField, HideInInspector] private List<Vector3> _legacyLocalCenterPoints = new();
        [SerializeField, Min(MinimumWidth)] private float _width = 2.5f;
        [SerializeField, Min(0.02f)] private float _edgeFeather = 0.12f;
        [SerializeField, Min(0.1f)] private float _borderWidth = 0.55f;
        [SerializeField, Min(0.02f)] private float _borderFeather = 0.08f;

        public Terrain Terrain => _terrain;
        public TerrainLayer RoadLayer => _roadLayer;
        public TerrainLayer BorderLayer => _borderLayer;
        public IReadOnlyList<Vector3> LegacyLocalCenterPoints => _legacyLocalCenterPoints;
        public float Width => _width;
        public float EdgeFeather => _edgeFeather;
        public float BorderWidth => _borderWidth;
        public float BorderFeather => _borderFeather;
        public SplineContainer SplineContainer => GetComponent<SplineContainer>();
        public bool HasEditableSpline => TryGetSpline(out Spline spline) && spline.Count >= 2;

        public void Configure(
            IReadOnlyList<Vector3> worldControlPoints,
            float width,
            float edgeFeather,
            float borderWidth,
            float borderFeather,
            Terrain terrain,
            TerrainLayer roadLayer,
            TerrainLayer borderLayer)
        {
            if (worldControlPoints == null) throw new ArgumentNullException(nameof(worldControlPoints));
            if (worldControlPoints.Count < 2)
                throw new ArgumentException("A terrain road requires at least two control points.", nameof(worldControlPoints));
            if (terrain == null || terrain.terrainData == null)
                throw new ArgumentNullException(nameof(terrain));
            if (roadLayer == null) throw new ArgumentNullException(nameof(roadLayer));
            if (borderLayer == null) throw new ArgumentNullException(nameof(borderLayer));

            transform.SetPositionAndRotation(worldControlPoints[0], Quaternion.identity);
            transform.localScale = Vector3.one;

            List<float3> localKnots = new(worldControlPoints.Count);
            for (int i = 0; i < worldControlPoints.Count; i++)
            {
                Vector3 local = transform.InverseTransformPoint(worldControlPoints[i]);
                localKnots.Add(new float3(local.x, local.y, local.z));
            }

            SplineContainer container = GetComponent<SplineContainer>();
            if (container == null) container = gameObject.AddComponent<SplineContainer>();
            container.Spline = new Spline(localKnots, TangentMode.AutoSmooth);
            _legacyLocalCenterPoints.Clear();

            _width = Mathf.Max(MinimumWidth, width);
            _edgeFeather = Mathf.Clamp(edgeFeather, 0.02f, 0.35f);
            _borderWidth = Mathf.Max(0.1f, borderWidth);
            _borderFeather = Mathf.Max(0.02f, borderFeather);
            _terrain = terrain;
            _roadLayer = roadLayer;
            _borderLayer = borderLayer;
        }

        public void GetWorldCenterPoints(List<Vector3> destination, float sampleSpacing = DefaultSampleSpacing)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();

            if (TryGetSpline(out Spline spline) && spline.Count >= 2)
            {
                SplineContainer container = SplineContainer;
                float length = Mathf.Max(0.01f, container.CalculateLength());
                int steps = Mathf.Max(1, Mathf.CeilToInt(length / Mathf.Max(0.2f, sampleSpacing)));
                for (int i = 0; i <= steps; i++)
                {
                    float3 evaluated = container.EvaluatePosition(i / (float)steps);
                    destination.Add(new Vector3(evaluated.x, evaluated.y, evaluated.z));
                }
                return;
            }

            for (int i = 0; i < _legacyLocalCenterPoints.Count; i++)
                destination.Add(transform.TransformPoint(_legacyLocalCenterPoints[i]));
        }

        public void GetWorldControlPoints(List<Vector3> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            destination.Clear();
            if (!TryGetSpline(out Spline spline))
            {
                for (int i = 0; i < _legacyLocalCenterPoints.Count; i++)
                    destination.Add(transform.TransformPoint(_legacyLocalCenterPoints[i]));
                return;
            }

            for (int i = 0; i < spline.Count; i++)
            {
                float3 local = spline[i].Position;
                destination.Add(transform.TransformPoint(new Vector3(local.x, local.y, local.z)));
            }
        }

        private bool TryGetSpline(out Spline spline)
        {
            SplineContainer container = GetComponent<SplineContainer>();
            spline = container == null ? null : container.Spline;
            return spline != null;
        }

        private void OnValidate()
        {
            _width = Mathf.Max(MinimumWidth, _width);
            _edgeFeather = Mathf.Clamp(_edgeFeather, 0.02f, 0.35f);
            _borderWidth = Mathf.Max(0.1f, _borderWidth);
            _borderFeather = Mathf.Max(0.02f, _borderFeather);
        }

        private void OnDrawGizmosSelected()
        {
            List<Vector3> points = new();
            GetWorldCenterPoints(points, 1f);
            if (points.Count < 2) return;

            Gizmos.color = new Color(0.12f, 0.9f, 1f, 0.9f);
            for (int i = 0; i < points.Count - 1; i++)
                Gizmos.DrawLine(points[i] + Vector3.up * 0.08f, points[i + 1] + Vector3.up * 0.08f);
        }
    }
}
