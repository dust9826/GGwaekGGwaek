using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public readonly struct DeliveryRoadPose
    {
        public DeliveryRoadPose(Vector3 position, Vector3 tangent, Vector3 right)
        {
            Position = position;
            Tangent = tangent;
            Right = right;
        }

        public Vector3 Position { get; }
        public Vector3 Tangent { get; }
        public Vector3 Right { get; }
    }

    /// <summary>
    /// 도로 중심선을 거리로 평가하는 불변 곡선. 제어점 사이 직선과 코너의 이차 베지어를 샘플해
    /// 직선·곡선·S자·ㄱ자를 같은 방식으로 다룬다.
    /// </summary>
    public sealed class DeliveryRoadCurve
    {
        private const float MinPointDistance = 0.001f;

        private readonly Vector3[] _samples;
        private readonly float[] _cumulativeLengths;

        /// <summary>
        /// 정점마다의 접선. 그 정점에 붙은 앞뒤 구간 방향의 평균이다.
        ///
        /// <b>구간별 상수 접선을 쓰면 방향이 계단으로 튄다.</b> 샘플 간격이 0.25m 라 5m/s 에서
        /// 초당 20번, 한 번에 최대 4° 씩 꺾인다(실측). 트럭의 요 각속도 제한(120°/s)은 점프
        /// 하나가 그보다 작아서 이걸 못 걸러낸다 — 교차로의 큰 점프만 걸러진다. 그래서 접선
        /// 자체를 이어지게 만든다.
        /// </summary>
        private readonly Vector3[] _vertexTangents;

        public DeliveryRoadCurve(IReadOnlyList<Vector3> controlPoints, float cornerDistance, float sampleSpacing)
        {
            if (controlPoints == null) throw new ArgumentNullException(nameof(controlPoints));
            if (controlPoints.Count < 2) throw new ArgumentException("도로 제어점은 최소 두 개가 필요하다", nameof(controlPoints));
            if (sampleSpacing <= 0f) throw new ArgumentOutOfRangeException(nameof(sampleSpacing));

            var samples = new List<Vector3>();
            AddSample(samples, Flatten(controlPoints[0]));

            for (int index = 1; index < controlPoints.Count - 1; index++)
            {
                Vector3 previous = Flatten(controlPoints[index - 1]);
                Vector3 corner = Flatten(controlPoints[index]);
                Vector3 next = Flatten(controlPoints[index + 1]);
                Vector3 incoming = corner - previous;
                Vector3 outgoing = next - corner;
                float incomingLength = incoming.magnitude;
                float outgoingLength = outgoing.magnitude;

                if (cornerDistance <= 0f || incomingLength < MinPointDistance || outgoingLength < MinPointDistance)
                {
                    AddLinearSamples(samples, corner, sampleSpacing);
                    continue;
                }

                incoming /= incomingLength;
                outgoing /= outgoingLength;
                float turnAngle = Vector3.Angle(incoming, outgoing);
                if (turnAngle < 0.5f)
                {
                    AddLinearSamples(samples, corner, sampleSpacing);
                    continue;
                }

                float cutDistance = Mathf.Min(cornerDistance, incomingLength * 0.45f, outgoingLength * 0.45f);
                Vector3 entry = corner - incoming * cutDistance;
                Vector3 exit = corner + outgoing * cutDistance;
                AddLinearSamples(samples, entry, sampleSpacing);

                int curveSteps = Mathf.Max(2, Mathf.CeilToInt((cutDistance * 2f) / sampleSpacing));
                for (int step = 1; step <= curveSteps; step++)
                {
                    float t = step / (float)curveSteps;
                    AddSample(samples, EvaluateQuadraticBezierPoint(entry, corner, exit, t));
                }
            }

            AddLinearSamples(samples, Flatten(controlPoints[controlPoints.Count - 1]), sampleSpacing);
            if (samples.Count < 2) throw new ArgumentException("서로 다른 도로 제어점이 필요하다", nameof(controlPoints));

            _samples = samples.ToArray();
            _cumulativeLengths = new float[_samples.Length];
            for (int index = 1; index < _samples.Length; index++)
            {
                _cumulativeLengths[index] = _cumulativeLengths[index - 1]
                                            + Vector3.Distance(_samples[index - 1], _samples[index]);
            }

            _vertexTangents = new Vector3[_samples.Length];
            for (int index = 0; index < _samples.Length; index++)
            {
                Vector3 incoming = index > 0
                    ? (_samples[index] - _samples[index - 1]).normalized
                    : Vector3.zero;
                Vector3 outgoing = index < _samples.Length - 1
                    ? (_samples[index + 1] - _samples[index]).normalized
                    : Vector3.zero;
                Vector3 averaged = incoming + outgoing;
                // 끝점은 한쪽만 있고, 180도 꺾인 자리는 합이 0 이 된다 — 둘 다 있는 쪽을 쓴다.
                _vertexTangents[index] = averaged.sqrMagnitude > MinPointDistance
                    ? averaged.normalized
                    : (outgoing.sqrMagnitude > 0f ? outgoing : incoming);
            }
        }

        public float Length => _cumulativeLengths[^1];
        public IReadOnlyList<Vector3> Samples => _samples;

        public DeliveryRoadPose Evaluate(float distance)
        {
            float clamped = Mathf.Clamp(distance, 0f, Length);
            int upper = FindUpperSample(clamped);
            int lower = Mathf.Max(0, upper - 1);
            float lowerDistance = _cumulativeLengths[lower];
            float segmentLength = _cumulativeLengths[upper] - lowerDistance;
            float t = segmentLength <= MinPointDistance ? 0f : (clamped - lowerDistance) / segmentLength;
            Vector3 position = Vector3.Lerp(_samples[lower], _samples[upper], t);
            // 정점 접선 사이를 보간한다 — 구간 안에서도 방향이 이어진다. 이웃 접선 사이 각이
            // 4도 이하라 Slerp 대신 Lerp + 정규화로 충분하다(오차 0.001도 미만).
            Vector3 tangent = Vector3.Lerp(_vertexTangents[lower], _vertexTangents[upper], t).normalized;
            Vector3 right = Vector3.Cross(Vector3.up, tangent).normalized;
            return new DeliveryRoadPose(position, tangent, right);
        }

        /// <summary>
        /// 거리 주변 <paramref name="window"/> 구간의 평균 곡률(1/m). 직선은 0이다.
        /// 접선이 샘플 구간마다 상수라 한 점에서 미분하면 계단이 나온다 — 창으로 평균 낸다.
        /// </summary>
        public float CurvatureAt(float distance, float window)
        {
            float half = Mathf.Max(0.05f, window * 0.5f);
            float from = Mathf.Clamp(distance - half, 0f, Length);
            float to = Mathf.Clamp(distance + half, 0f, Length);
            float span = to - from;
            if (span < MinPointDistance) return 0f;
            float turnDegrees = Vector3.Angle(Evaluate(from).Tangent, Evaluate(to).Tangent);
            return turnDegrees * Mathf.Deg2Rad / span;
        }

        private int FindUpperSample(float distance)
        {
            if (distance <= 0f) return 1;
            if (distance >= Length) return _samples.Length - 1;

            int low = 1;
            int high = _cumulativeLengths.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (_cumulativeLengths[middle] < distance) low = middle + 1;
                else high = middle;
            }
            return low;
        }

        private static void AddLinearSamples(List<Vector3> samples, Vector3 destination, float spacing)
        {
            Vector3 start = samples[^1];
            float distance = Vector3.Distance(start, destination);
            int steps = Mathf.Max(1, Mathf.CeilToInt(distance / spacing));
            for (int step = 1; step <= steps; step++)
            {
                AddSample(samples, Vector3.Lerp(start, destination, step / (float)steps));
            }
        }

        private static void AddSample(List<Vector3> samples, Vector3 point)
        {
            if (samples.Count > 0 && Vector3.Distance(samples[^1], point) < MinPointDistance) return;
            samples.Add(point);
        }

        private static Vector3 Flatten(Vector3 point) => new Vector3(point.x, 0f, point.z);

        /// <summary>진입점(entry)에서 정점(corner)을 거쳐 이탈점(exit)까지의 이차 베지어 위치. t는 0..1.</summary>
        public static Vector3 EvaluateQuadraticBezierPoint(Vector3 entry, Vector3 corner, Vector3 exit, float t)
        {
            float oneMinusT = 1f - t;
            return oneMinusT * oneMinusT * entry
                   + 2f * oneMinusT * t * corner
                   + t * t * exit;
        }

        /// <summary>같은 이차 베지어의 접선 방향(정규화 전). t는 0..1.</summary>
        public static Vector3 EvaluateQuadraticBezierTangent(Vector3 entry, Vector3 corner, Vector3 exit, float t)
        {
            return 2f * (1f - t) * (corner - entry) + 2f * t * (exit - corner);
        }

        /// <summary>
        /// 코너 정점을 반지름 <paramref name="radius"/>인 원호로 자르는 접선 길이(m). 표준 필릿
        /// 공식 t = R·tan(θ/2) — θ는 진행 방향이 꺾이는 각도(라디안, 0~π)다. 원호는 같은 접선
        /// 길이 예산에서 이차 베지어보다 곡률이 정점 쪽에 몰리지 않아 더 큰 실제 회전 반경을 낸다
        /// (2026-08-18 실측 비교: 150° 코너에서 베지어 R=0.40m 대 원호 R=1.53m, 같은 cut=5.6m).
        /// </summary>
        public static float ArcCutDistance(float radius, float turnRadians) => radius * Mathf.Tan(turnRadians * 0.5f);

        /// <summary>
        /// <paramref name="corner"/>에서 <paramref name="incomingDirection"/> 방향으로 들어와
        /// <paramref name="outgoingDirection"/> 방향으로 나가는 원호 필릿의 t 지점 위치와 접선.
        /// t=0 은 진입 접점(entry, <see cref="ArcCutDistance"/>만큼 코너 앞), t=1 은 이탈 접점
        /// (exit, 같은 거리만큼 코너 뒤)이다. 방향 벡터는 XZ 평면 단위 벡터여야 한다(y=0).
        ///
        /// 표준 접원(inscribed tangent circle) 기하: 두 방향이 이루는 진행 꺾임각 θ에서, 중심은
        /// 코너로부터 이등분선 방향으로 R/cos(θ/2) 떨어진 자리에 있고, 호의 전체 스윕 각도가
        /// 정확히 θ와 같다 — 그래서 접선이 t=0 에서 진입 방향, t=1 에서 이탈 방향으로 부드럽게
        /// 회전한다(자동차가 반지름 R로 정속 회전할 때와 같은 곡률 프로파일).
        /// </summary>
        public static void EvaluateArc(Vector3 corner, Vector3 incomingDirection, Vector3 outgoingDirection,
                                       float radius, float t, out Vector3 position, out Vector3 tangent)
        {
            float turnRadians = Vector3.SignedAngle(incomingDirection, outgoingDirection, Vector3.up) * Mathf.Deg2Rad;
            float absTurn = Mathf.Abs(turnRadians);
            if (absTurn < 1e-4f)
            {
                position = corner;
                tangent = incomingDirection;
                return;
            }

            float cut = ArcCutDistance(radius, absTurn);
            Vector3 entry = corner - incomingDirection * cut;
            Vector3 bisector = (-incomingDirection + outgoingDirection).normalized;
            float centerDistance = radius / Mathf.Cos(absTurn * 0.5f);
            Vector3 center = corner + bisector * centerDistance;

            // 부호: Vector3.SignedAngle 의 회전 방향과 아래 (x,z) 2D 회전 행렬의 방향이 서로 반대다
            // (Unity의 왼손 좌표계에서 up 축 기준 SignedAngle 부호가 이 행렬의 관례와 어긋난다) —
            // 실측으로 확인: 음수를 안 주면 호가 원의 반대편으로 돌아 t=1 에서 exit/outgoing이
            // 아니라 그 반대쪽에 닿는다(2026-08-18).
            float sweep = -turnRadians * t;
            float cosA = Mathf.Cos(sweep);
            float sinA = Mathf.Sin(sweep);

            Vector3 radial = entry - center;
            position = center + new Vector3(radial.x * cosA - radial.z * sinA, 0f, radial.x * sinA + radial.z * cosA);
            tangent = new Vector3(
                incomingDirection.x * cosA - incomingDirection.z * sinA, 0f,
                incomingDirection.x * sinA + incomingDirection.z * cosA);
        }
    }
}

