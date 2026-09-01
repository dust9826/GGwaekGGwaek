using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public readonly struct DeliveryRoadTraversal
    {
        public DeliveryRoadTraversal(DeliveryRoadSegment segment, bool reverse)
        {
            Segment = segment;
            Reverse = reverse;
        }

        public DeliveryRoadSegment Segment { get; }
        public bool Reverse { get; }
    }

    public readonly struct DeliveryRoutePose
    {
        public DeliveryRoutePose(Vector3 position, Vector3 forward, Vector3 segmentRight,
                                 float preferredLateralOffset,
                                 DeliveryRoadSegment segment, float segmentDistance,
                                 bool reverse, int traversalIndex)
        {
            Position = position;
            Forward = forward;
            SegmentRight = segmentRight;
            PreferredLateralOffset = preferredLateralOffset;
            Segment = segment;
            SegmentDistance = segmentDistance;
            Reverse = reverse;
            TraversalIndex = traversalIndex;
        }

        public Vector3 Position { get; }
        public Vector3 Forward { get; }
        public Vector3 SegmentRight { get; }

        /// <summary>
        /// 이 지점의 선호 차선 오프셋(<see cref="PreferredLaneOffset"/>과 같은 값). 세그먼트
        /// 경계(교차로) 부근에서는 <see cref="DeliveryRoute.Evaluate"/> 가 앞뒤 세그먼트의 값을
        /// 위치·방향과 같은 창에서 부드럽게 블렌딩해 준다 — 그래서 정적 메서드를 다시 부르지 않고
        /// 이 값을 그대로 쓰면 오프셋이 경계에서 순간이동하지 않는다(2026-08-18, 실측: 도로 1.125m
        /// ↔ 보행로 0m 전환이 0.4m 샘플 간격에서 1.1m 이상 튀던 문제).
        /// </summary>
        public float PreferredLateralOffset { get; }

        public DeliveryRoadSegment Segment { get; }
        public float SegmentDistance { get; }
        public bool Reverse { get; }
        public int TraversalIndex { get; }

        /// <summary>
        /// 보행로는 정중앙(0), 일반 도로는 우측 통행이 기본 주행 위치다. 세그먼트 경계 부근에서
        /// 부드러운 값이 필요하면 이 정적 메서드 대신 <see cref="PreferredLateralOffset"/> 을 쓴다 —
        /// 이 메서드는 <see cref="Segment"/> 가 애초에 블렌딩되지 않는다는 계약(Evaluate 문서 참고)을
        /// 그대로 따르므로, 블렌딩된 포즈에 불러도 항상 그 트래버설의 원래(안 블렌딩된) 값을
        /// 돌려준다.
        ///
        /// <b>부호을 뒤집지 않는다(2026-08-18).</b> 예전엔 역주행이면 음수를 돌려줬는데, 그건
        /// <see cref="SegmentRight"/>/<see cref="Evaluate"/> 가 반대로 "역주행을 반영하지 않는
        /// 원래 방향의 오른쪽"을 돌려준다는 전제와 짝을 이루던 보정이었다. 이제
        /// <see cref="Evaluate"/> 가 <c>right</c> 를 (블렌딩 여부와 무관하게) 항상 <c>Forward</c>
        /// 기준 진짜 오른쪽으로 계산하므로, 오프셋은 방향과 무관하게 항상 "우측 통행"이라는 같은
        /// 물리적 의미만 가지면 된다 — 부호 짝맞추기가 아예 필요 없어졌다. 이 정정 전에는 인접한
        /// 두 세그먼트의 <c>Reverse</c> 플래그가 서로 다르면(예: 도로는 정방향, 다음 보행로는
        /// 역방향으로 편입된 경로) 위치·오프셋이 각각 연속인데도 <c>right</c> 부호만 경계에서
        /// 뒤집혀 최종 표시 좌표가 순간이동했다(실측: 0.4m 스텝에서 1.2m 튐, `req9`의 90° 교차로).
        /// </summary>
        public static float PreferredLaneOffset(DeliveryRoutePose pose) => ComputePreferredOffset(pose.Segment);

        internal static float ComputePreferredOffset(DeliveryRoadSegment segment)
            => segment.IsSidewalk ? 0f : segment.DrivableWidth * 0.25f;
    }

    public sealed class DeliveryRoute
    {
        /// <summary>
        /// 교차로 필릿의 목표 회전 반경(m). 도로 폭(4.5m)에 맞춘 값이라 트럭이 실제로 돌 수 있는
        /// 크기다. 세그먼트 길이 예산이 부족하면(<see cref="JunctionCutDistance"/>) 이보다 작은
        /// 반경으로 잘리지만, 목표를 반경으로 두면 완만한 코너는 짧게·급한 코너는 길게 접선을
        /// 자동으로 잡아 모든 교차로가 최대한 같은 반경으로 돈다(2026-08-18) — 예전엔 코너 각도와
        /// 무관하게 접선 길이(`CornerDistance`, 세그먼트 내부 저작 코너용 값을 교차로에도 재사용)를
        /// 고정으로 줘서, 완만한 90° 코너는 반경이 남고 121°·150° 급커브는 반경이 0.2~0.7m 까지
        /// 쪼그라들었다(실측). 이 상수는 세그먼트별 `CornerDistance`와 별개다 — 그건 한 도로 안의
        /// 저작 제어점 꺾임(<see cref="DeliveryRoadCurve"/>)을 다듬는 값이고, 이건 도로가 바뀌는
        /// 교차로(<see cref="DeliveryRoadNetwork"/> 의 노드)를 다듬는 값이다.
        /// </summary>
        private const float JunctionRadius = 4f;

        private readonly DeliveryRoadTraversal[] _traversals;
        private readonly float[] _cumulativeLengths;

        public DeliveryRoute(IReadOnlyList<DeliveryRoadTraversal> traversals)
        {
            if (traversals == null) throw new ArgumentNullException(nameof(traversals));
            if (traversals.Count == 0) throw new ArgumentException("배송 경로는 도로를 하나 이상 가져야 한다", nameof(traversals));

            _traversals = new DeliveryRoadTraversal[traversals.Count];
            _cumulativeLengths = new float[traversals.Count + 1];
            for (int index = 0; index < traversals.Count; index++)
            {
                DeliveryRoadTraversal traversal = traversals[index];
                if (traversal.Segment == null) throw new ArgumentException("경로에 비어 있는 도로가 있다", nameof(traversals));
                _traversals[index] = traversal;
                _cumulativeLengths[index + 1] = _cumulativeLengths[index] + traversal.Segment.Length;
            }
        }

        public float Length => _cumulativeLengths[^1];
        public IReadOnlyList<DeliveryRoadTraversal> Traversals => _traversals;

        /// <summary>
        /// 세그먼트 경계(교차로) 부근에서는 위치·방향·<see cref="DeliveryRoutePose.PreferredLateralOffset"/>
        /// 을 이웃 트래버설과 원호 필릿(<see cref="DeliveryRoadCurve.EvaluateArc"/>)으로 블렌딩해,
        /// 방향이 한 스텝에 꺾이지 않고 목표 반경(<see cref="JunctionRadius"/>)의 호를 그리며 돌고
        /// 차선 오프셋도 도로↔보행로 전환에서 순간이동하지 않는다. <see cref="Segment"/>/
        /// <see cref="DeliveryRoutePose.SegmentDistance"/> 는 블렌딩하지 않고 원래 트래버설의 값을
        /// 그대로 돌려준다 — 거리 기반 판정(눈 치우기, 교통 양보, <see cref="CurvatureAt"/> 등)은
        /// 이 블렌딩과 무관하게 그대로 동작해야 하기 때문이다. <see cref="BoundaryTurnDegrees"/> 도
        /// 같은 이유로 여기를 거치지 않고 세그먼트 raw 탄젠트를 직접 본다 — 안 그러면 코너 감속
        /// 판정이 이미 블렌딩된(완만해진) 방향을 재서 급커브를 놓친다.
        /// </summary>
        public DeliveryRoutePose Evaluate(float routeDistance)
        {
            float clamped = Mathf.Clamp(routeDistance, 0f, Length);
            int traversalIndex = FindTraversal(clamped);
            DeliveryRoadTraversal traversal = _traversals[traversalIndex];
            float localDistance = clamped - _cumulativeLengths[traversalIndex];
            float segmentDistance = traversal.Reverse
                ? traversal.Segment.Length - localDistance
                : localDistance;
            DeliveryRoadPose roadPose = traversal.Segment.Evaluate(segmentDistance);
            Vector3 forward = traversal.Reverse ? -roadPose.Tangent : roadPose.Tangent;
            Vector3 position = roadPose.Position;
            float lateralOffset = DeliveryRoutePose.ComputePreferredOffset(traversal.Segment);

            if (traversalIndex > 0)
            {
                float cut = JunctionCutDistance(traversalIndex - 1, traversalIndex);
                if (cut > 0f && localDistance < cut)
                {
                    float t = 0.5f + (localDistance / cut) * 0.5f;
                    if (TryBlendJunction(traversalIndex - 1, traversalIndex, cut, t,
                                         out Vector3 blendedPosition, out Vector3 blendedForward, out float blendedOffset))
                    {
                        position = blendedPosition;
                        forward = blendedForward;
                        lateralOffset = blendedOffset;
                    }
                }
            }

            if (traversalIndex < _traversals.Length - 1)
            {
                float distanceToEnd = traversal.Segment.Length - localDistance;
                float cut = JunctionCutDistance(traversalIndex, traversalIndex + 1);
                if (cut > 0f && distanceToEnd < cut)
                {
                    float t = 0.5f - (distanceToEnd / cut) * 0.5f;
                    if (TryBlendJunction(traversalIndex, traversalIndex + 1, cut, t,
                                         out Vector3 blendedPosition, out Vector3 blendedForward, out float blendedOffset))
                    {
                        position = blendedPosition;
                        forward = blendedForward;
                        lateralOffset = blendedOffset;
                    }
                }
            }

            // right는 블렌딩 여부와 무관하게 항상 forward 기준 진짜 오른쪽이다 — 예전엔 블렌딩 안 된
            // 구간에서만 세그먼트 raw 접선 기준 right(역주행을 반영하지 않음)를 쓰고, 블렌딩된
            // 구간에서만 이걸 다시 계산+뒤집었다. 인접한 두 세그먼트의 Reverse 플래그가 다르면
            // 그 두 방식이 경계에서 정확히 반대 부호를 내 최종 표시 좌표가 순간이동했다(실측:
            // 2026-08-18, 90° 교차로에서 0.4m 샘플 스텝이 1.2m로 튐). forward 는 이미
            // traversal.Reverse 를 반영해 항상 실제 진행 방향이므로, 여기서 한 번만 계산하면
            // 블렌딩 여부·Reverse 조합과 무관하게 항상 일관된다.
            Vector3 right = Vector3.Cross(Vector3.up, forward).normalized;

            return new DeliveryRoutePose(position, forward, right, lateralOffset,
                                         traversal.Segment, segmentDistance,
                                         traversal.Reverse, traversalIndex);
        }

        /// <summary>
        /// 목표 반경(<see cref="JunctionRadius"/>)에서 역산한 접선 길이 — 완만한 코너는 짧게,
        /// 급한 코너는 길게 자동으로 잡힌다. 세그먼트 길이 예산(각 세그먼트 길이의 45%)을 넘지
        /// 않게 자르는데, 여기서 잘리면 <see cref="TryBlendJunction"/> 이 실제로 낸 반경은
        /// 목표보다 작아진다 — 접선 길이가 부족한데 무리하게 큰 반경을 요구하면 원호가 세그먼트
        /// 바깥으로 삐져나가기 때문이다(2026-08-18).
        /// </summary>
        private float JunctionCutDistance(int beforeIndex, int afterIndex)
        {
            DeliveryRoadTraversal before = _traversals[beforeIndex];
            DeliveryRoadTraversal after = _traversals[afterIndex];
            (Vector3 incoming, Vector3 outgoing, _) = JunctionDirections(before, after);
            float turnRadians = Mathf.Abs(Vector3.SignedAngle(incoming, outgoing, Vector3.up)) * Mathf.Deg2Rad;
            if (turnRadians < 1e-4f) return 0f;

            float idealCut = DeliveryRoadCurve.ArcCutDistance(JunctionRadius, turnRadians);
            return Mathf.Min(idealCut, before.Segment.Length * 0.45f, after.Segment.Length * 0.45f);
        }

        private bool TryBlendJunction(int beforeIndex, int afterIndex, float cut, float t,
                                      out Vector3 position, out Vector3 forward, out float lateralOffset)
        {
            DeliveryRoadTraversal before = _traversals[beforeIndex];
            DeliveryRoadTraversal after = _traversals[afterIndex];
            (Vector3 incoming, Vector3 outgoing, Vector3 corner) = JunctionDirections(before, after);

            float turnRadians = Mathf.Abs(Vector3.SignedAngle(incoming, outgoing, Vector3.up)) * Mathf.Deg2Rad;
            if (turnRadians < 1e-4f)
            {
                position = corner;
                forward = incoming;
                lateralOffset = DeliveryRoutePose.ComputePreferredOffset(before.Segment);
                return true;
            }

            // JunctionCutDistance 가 길이 예산에 맞춰 cut 을 줄였을 수 있다 — 그 cut 에서 실제
            // 반영되는 반지름을 역산해야 EvaluateArc 가 내부에서 다시 계산하는 cut 과 정확히
            // 일치한다. 안 맞으면 t=1 이 exit(코너에서 cut 만큼 나간 자리)에 정확히 닿지 않는다.
            float achievedRadius = cut / Mathf.Tan(turnRadians * 0.5f);
            DeliveryRoadCurve.EvaluateArc(corner, incoming, outgoing, achievedRadius, t, out position, out Vector3 tangent);
            if (tangent.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.zero;
                lateralOffset = 0f;
                return false;
            }
            forward = tangent.normalized;

            float beforeOffset = DeliveryRoutePose.ComputePreferredOffset(before.Segment);
            float afterOffset = DeliveryRoutePose.ComputePreferredOffset(after.Segment);
            lateralOffset = Mathf.Lerp(beforeOffset, afterOffset, t);
            return true;
        }

        private static (Vector3 incoming, Vector3 outgoing, Vector3 corner) JunctionDirections(
            DeliveryRoadTraversal before, DeliveryRoadTraversal after)
        {
            DeliveryRoadPose beforePose = EvaluateAtLocalDistance(before, before.Segment.Length);
            Vector3 incoming = before.Reverse ? -beforePose.Tangent : beforePose.Tangent;
            DeliveryRoadPose afterPose = EvaluateAtLocalDistance(after, 0f);
            Vector3 outgoing = after.Reverse ? -afterPose.Tangent : afterPose.Tangent;
            return (incoming, outgoing, afterPose.Position);
        }

        private static DeliveryRoadPose EvaluateAtLocalDistance(DeliveryRoadTraversal traversal, float localDistance)
        {
            float segmentDistance = traversal.Reverse
                ? traversal.Segment.Length - localDistance
                : localDistance;
            return traversal.Segment.Evaluate(segmentDistance);
        }

        public float TraversalStartDistance(int traversalIndex) => _cumulativeLengths[traversalIndex];
        public float TraversalEndDistance(int traversalIndex) => _cumulativeLengths[traversalIndex + 1];

        /// <summary>경로 거리에서의 도로 곡률(1/m). 진행 방향과 무관한 크기다.</summary>
        public float CurvatureAt(float routeDistance, float window)
        {
            float clamped = Mathf.Clamp(routeDistance, 0f, Length);
            int traversalIndex = FindTraversal(clamped);
            DeliveryRoadTraversal traversal = _traversals[traversalIndex];
            float localDistance = clamped - _cumulativeLengths[traversalIndex];
            float segmentDistance = traversal.Reverse
                ? traversal.Segment.Length - localDistance
                : localDistance;
            return traversal.Segment.Curve.CurvatureAt(segmentDistance, window);
        }

        /// <summary>도로가 바뀌는 노드의 수. 곡률은 도로 안에서만 이어지므로 노드는 따로 봐야 한다.</summary>
        public int BoundaryCount => _traversals.Length - 1;

        public float BoundaryDistance(int boundaryIndex) => _cumulativeLengths[boundaryIndex + 1];

        /// <summary>도로가 바뀌는 자리의 노드. 두 도로가 공유하는 그 노드가 곧 교차로다.</summary>
        public DeliveryRoadNode BoundaryNode(int boundaryIndex)
        {
            DeliveryRoadTraversal traversal = _traversals[boundaryIndex];
            return traversal.Reverse ? traversal.Segment.Start : traversal.Segment.End;
        }

        /// <summary>
        /// 노드에서 진행 방향이 꺾이는 각도(도). <see cref="Evaluate"/> 는 이 경계 부근에서 방향을
        /// 부드럽게 블렌딩하므로, 그 결과를 다시 여기서 재면 코너가 아무리 급해도 거의 0°로 나온다
        /// — 그래서 블렌딩 이전의 두 세그먼트 raw 탄젠트를 직접 비교한다. 곡선 평가는 도로 경계를
        /// 넘어 이어지지 않는다.
        /// </summary>
        public float BoundaryTurnDegrees(int boundaryIndex)
        {
            DeliveryRoadTraversal before = _traversals[boundaryIndex];
            DeliveryRoadTraversal after = _traversals[boundaryIndex + 1];

            DeliveryRoadPose beforePose = before.Segment.Evaluate(before.Reverse ? 0f : before.Segment.Length);
            Vector3 beforeForward = before.Reverse ? -beforePose.Tangent : beforePose.Tangent;

            DeliveryRoadPose afterPose = after.Segment.Evaluate(after.Reverse ? after.Segment.Length : 0f);
            Vector3 afterForward = after.Reverse ? -afterPose.Tangent : afterPose.Tangent;

            return Vector3.Angle(beforeForward, afterForward);
        }

        private int FindTraversal(float distance)
        {
            if (distance >= Length) return _traversals.Length - 1;
            int low = 0;
            int high = _traversals.Length - 1;
            while (low < high)
            {
                int middle = (low + high) / 2;
                if (_cumulativeLengths[middle + 1] <= distance) low = middle + 1;
                else high = middle;
            }
            return low;
        }
    }
}
