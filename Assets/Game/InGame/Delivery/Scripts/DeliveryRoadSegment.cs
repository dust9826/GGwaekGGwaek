using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryRoadSegment : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNode _start;
        [SerializeField] private DeliveryRoadNode _end;
        [Tooltip("시작·끝 노드 사이의 월드 좌표 제어점. 빈 배열이면 직선이다.")]
        [SerializeField] private Vector3[] _intermediateWorldPoints = Array.Empty<Vector3>();
        [SerializeField, Min(0.5f)] private float _drivableWidth = 6f;
        [SerializeField, Min(0f)] private float _cornerDistance = 2f;
        [SerializeField, Min(0.05f)] private float _sampleSpacing = 0.25f;
        [Tooltip("보행로면 트럭이 정중앙을 달린다. 일반 도로는 우측 통행이 기본이다.")]
        [SerializeField] private bool _isSidewalk;

        private DeliveryRoadCurve _curve;

        public DeliveryRoadNode Start => _start;
        public DeliveryRoadNode End => _end;
        public float DrivableWidth => _drivableWidth;
        public float CornerDistance => _cornerDistance;
        public bool IsSidewalk => _isSidewalk;
        public float Length => Curve.Length;
        public DeliveryRoadCurve Curve => _curve ??= BuildCurve();

        public void Configure(DeliveryRoadNode start, DeliveryRoadNode end,
                              IReadOnlyList<Vector3> intermediateWorldPoints, float drivableWidth,
                              float cornerDistance = 2f, float sampleSpacing = 0.25f,
                              bool isSidewalk = false)
        {
            _start = start;
            _end = end;
            _intermediateWorldPoints = intermediateWorldPoints == null
                ? Array.Empty<Vector3>()
                : Copy(intermediateWorldPoints);
            _drivableWidth = drivableWidth;
            _cornerDistance = cornerDistance;
            _sampleSpacing = sampleSpacing;
            _isSidewalk = isSidewalk;
            _curve = null;
        }

        public DeliveryRoadNode Other(DeliveryRoadNode node)
        {
            if (node == _start) return _end;
            if (node == _end) return _start;
            return null;
        }

        public DeliveryRoadPose Evaluate(float distance) => Curve.Evaluate(distance);

        public bool TryValidate(out string error)
        {
            if (_start == null || _end == null)
            {
                error = $"{name}: 시작/끝 노드가 필요하다";
                return false;
            }
            if (_start == _end)
            {
                error = $"{name}: 같은 노드로 연결할 수 없다";
                return false;
            }
            if (_drivableWidth <= 0f)
            {
                error = $"{name}: 도로 폭은 0보다 커야 한다";
                return false;
            }

            try
            {
                _ = Curve.Length;
            }
            catch (Exception exception)
            {
                error = $"{name}: {exception.Message}";
                return false;
            }

            error = null;
            return true;
        }

        private DeliveryRoadCurve BuildCurve()
        {
            if (_start == null || _end == null)
                throw new InvalidOperationException($"{name}: 시작/끝 노드가 없다");

            var points = new List<Vector3>(_intermediateWorldPoints.Length + 2) { _start.transform.position };
            points.AddRange(_intermediateWorldPoints);
            points.Add(_end.transform.position);
            return new DeliveryRoadCurve(points, _cornerDistance, Mathf.Max(0.05f, _sampleSpacing));
        }

        private void OnValidate()
        {
            _curve = null;
            _drivableWidth = Mathf.Max(0.5f, _drivableWidth);
            _sampleSpacing = Mathf.Max(0.05f, _sampleSpacing);
        }

        private void OnDrawGizmos()
        {
            if (_start == null || _end == null) return;
            DeliveryRoadCurve curve;
            try { curve = BuildCurve(); }
            catch { return; }

            Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.9f);
            IReadOnlyList<Vector3> samples = curve.Samples;
            for (int index = 1; index < samples.Count; index++)
            {
                Gizmos.DrawLine(samples[index - 1] + Vector3.up * 0.05f, samples[index] + Vector3.up * 0.05f);
            }

            Gizmos.color = new Color(0.15f, 0.85f, 1f, 0.3f);
            float step = Mathf.Max(1f, curve.Length / 12f);
            for (float distance = 0f; distance <= curve.Length; distance += step)
            {
                DeliveryRoadPose pose = curve.Evaluate(distance);
                Gizmos.DrawLine(pose.Position - pose.Right * (_drivableWidth * 0.5f),
                                pose.Position + pose.Right * (_drivableWidth * 0.5f));
            }
        }

        private static Vector3[] Copy(IReadOnlyList<Vector3> source)
        {
            var copy = new Vector3[source.Count];
            for (int index = 0; index < source.Count; index++) copy[index] = source[index];
            return copy;
        }
    }
}

