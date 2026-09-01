using System;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 폭 있는 곡선 도로에서 트럭 차체가 들어갈 연속된 눈 없는 오프셋을 찾는다.
    /// 텍스처나 물리를 읽지 않고 월드 위치의 CPU 눈 깊이 함수만 사용한다.
    /// </summary>
    public static class DeliverySnowClearance
    {
        public static bool TryFindOffset(DeliveryRoute route,
                                         float routeDistance,
                                         float preferredOffset,
                                         float lookAheadDistance,
                                         float truckHalfLength,
                                         float truckHalfWidth,
                                         float safetyMargin,
                                         int blockingDepthCm,
                                         float longitudinalSpacing,
                                         float lateralSpacing,
                                         Func<Vector3, int> depthAtWorld,
                                         out float offset,
                                         float footprintExclusionStart = float.NegativeInfinity)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            if (depthAtWorld == null)
            {
                offset = 0f;
                return true;
            }

            DeliveryRoutePose currentPose = route.Evaluate(routeDistance);
            float maxOffset = currentPose.Segment.DrivableWidth * 0.5f - truckHalfWidth - safetyMargin;
            if (maxOffset < 0f)
            {
                offset = 0f;
                return false;
            }

            longitudinalSpacing = Mathf.Max(0.1f, longitudinalSpacing);
            lateralSpacing = Mathf.Max(0.1f, lateralSpacing);
            float bestScore = float.PositiveInfinity;
            float bestOffset = 0f;
            bool found = false;

            int steps = Mathf.Max(1, Mathf.CeilToInt(maxOffset * 2f / lateralSpacing));
            for (int index = 0; index <= steps; index++)
            {
                float candidate = Mathf.Lerp(-maxOffset, maxOffset, index / (float)steps);
                if (!IsOffsetClear(route, routeDistance, candidate, lookAheadDistance,
                                   truckHalfLength, truckHalfWidth, safetyMargin,
                                   blockingDepthCm, longitudinalSpacing, depthAtWorld,
                                   footprintExclusionStart)) continue;

                float score = Mathf.Abs(candidate - preferredOffset) + Mathf.Abs(candidate) * 0.05f;
                if (score >= bestScore) continue;
                bestScore = score;
                bestOffset = candidate;
                found = true;
            }

            offset = bestOffset;
            return found;
        }

        public static bool IsOffsetClear(DeliveryRoute route,
                                         float routeDistance,
                                         float offset,
                                         float lookAheadDistance,
                                         float truckHalfLength,
                                         float truckHalfWidth,
                                         float safetyMargin,
                                         int blockingDepthCm,
                                         float longitudinalSpacing,
                                         Func<Vector3, int> depthAtWorld,
                                         float footprintExclusionStart = float.NegativeInfinity)
        {
            if (depthAtWorld == null) return true;
            float window = WindowEnd(route, routeDistance, lookAheadDistance) - routeDistance;
            float clear = ClearDistance(route, routeDistance, offset, lookAheadDistance,
                                        truckHalfLength, truckHalfWidth, safetyMargin,
                                        blockingDepthCm, longitudinalSpacing, depthAtWorld,
                                        footprintExclusionStart);
            return clear >= window - 0.001f;
        }

        /// <summary>
        /// 이 오프셋으로 지금 자리에서 앞으로 몇 미터까지 차체가 들어가는지. 막힌 지점 직전까지의
        /// 거리이므로 그 자리가 곧 정지선이다 — 트럭은 이 값으로 눈 앞에서 감속해 선다.
        /// 전부 뚫려 있으면 검사 창의 길이를 그대로 돌려준다.
        /// </summary>
        public static float ClearDistance(DeliveryRoute route,
                                          float routeDistance,
                                          float offset,
                                          float lookAheadDistance,
                                          float truckHalfLength,
                                          float truckHalfWidth,
                                          float safetyMargin,
                                          int blockingDepthCm,
                                          float longitudinalSpacing,
                                          Func<Vector3, int> depthAtWorld,
                                          float footprintExclusionStart = float.NegativeInfinity)
        {
            if (route == null) throw new ArgumentNullException(nameof(route));
            float end = WindowEnd(route, routeDistance, lookAheadDistance);
            if (depthAtWorld == null) return end - routeDistance;

            float centerStep = Mathf.Max(0.1f, longitudinalSpacing);

            // 트럭이 지금 서 있는 자리는 이미 자기 차체가 덮고 있어 플레이어가 제설을 못 넣는다.
            // footprintExclusionStart 아래로는 절대 안 내려가게 잘라서, "내 발밑·후미가 아직
            // 눈이다" 라는 이유만으로 영원히 SnowBlocked 에 갇히는 걸 막는다. 기본값(-무한대)은
            // 예전 그대로 0부터 검사한다 — 후진 양보처럼 트럭이 아직 없는 자리를 검사할 때는
            // 그 자리 전체가 실제로 비어 있어야 하므로 제외하면 안 된다.
            float footprintMin = Mathf.Max(0f, footprintExclusionStart);
            float clear = 0f;

            for (float centerDistance = routeDistance; centerDistance <= end + 0.001f; centerDistance += centerStep)
            {
                if (!IsCenterClear(route, centerDistance, offset, truckHalfLength, truckHalfWidth,
                                   safetyMargin, blockingDepthCm, depthAtWorld, footprintMin))
                {
                    return clear;
                }

                clear = centerDistance - routeDistance;
                if (centerDistance >= end) break;
            }

            return end - routeDistance;
        }

        private static float WindowEnd(DeliveryRoute route, float routeDistance, float lookAheadDistance)
            => Mathf.Min(route.Length, routeDistance + Mathf.Max(0f, lookAheadDistance));

        private static bool IsCenterClear(DeliveryRoute route, float centerDistance, float offset,
                                          float truckHalfLength, float truckHalfWidth, float safetyMargin,
                                          int blockingDepthCm, Func<Vector3, int> depthAtWorld,
                                          float footprintMin)
        {
            DeliveryRoutePose centerPose = route.Evaluate(centerDistance);
            float usableHalfWidth = centerPose.Segment.DrivableWidth * 0.5f - safetyMargin;
            if (Mathf.Abs(offset) + truckHalfWidth > usableHalfWidth) return false;

            for (int lengthIndex = -1; lengthIndex <= 1; lengthIndex++)
            {
                float footprintDistance = Mathf.Clamp(centerDistance + lengthIndex * truckHalfLength,
                                                      footprintMin, route.Length);
                DeliveryRoutePose pose = route.Evaluate(footprintDistance);
                float poseUsableHalfWidth = pose.Segment.DrivableWidth * 0.5f - safetyMargin;
                if (Mathf.Abs(offset) + truckHalfWidth > poseUsableHalfWidth) return false;

                for (int widthIndex = -1; widthIndex <= 1; widthIndex++)
                {
                    float lateral = offset + widthIndex * truckHalfWidth;
                    Vector3 world = pose.Position + pose.SegmentRight * lateral;
                    if (depthAtWorld(world) >= blockingDepthCm) return false;
                }
            }

            return true;
        }
    }
}

