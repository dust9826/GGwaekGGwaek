using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class DeliveryRouteTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 세그먼트_경계는_방향을_한번에_꺾지_않고_호로_돈다()
        {
            DeliveryRoadNode a = Node("A", new Vector3(0f, 0f, 0f));
            DeliveryRoadNode b = Node("B", new Vector3(10f, 0f, 0f));
            DeliveryRoadNode c = Node("C", new Vector3(10f, 0f, 10f));
            DeliveryRoadSegment ab = Segment("AB", a, b, cornerDistance: 3f);
            DeliveryRoadSegment bc = Segment("BC", b, c, cornerDistance: 3f);
            var route = new DeliveryRoute(new[]
            {
                new DeliveryRoadTraversal(ab, false),
                new DeliveryRoadTraversal(bc, false),
            });

            Assert.That(route.Evaluate(3f).Forward, Is.EqualTo(Vector3.right));
            Assert.That(route.Evaluate(17f).Forward, Is.EqualTo(Vector3.forward));

            bool foundDiagonalTangent = false;
            for (float distance = 7f; distance <= 13f; distance += 0.25f)
            {
                Vector3 forward = route.Evaluate(distance).Forward;
                if (Mathf.Abs(forward.x) > 0.05f && Mathf.Abs(forward.z) > 0.05f) foundDiagonalTangent = true;
            }
            Assert.IsTrue(foundDiagonalTangent, "노드를 지날 때 대각 접선을 가진 호를 그려야 한다");
        }

        [Test]
        public void 경로의_시작과_끝은_이웃이_없어_블렌딩하지_않는다()
        {
            DeliveryRoadNode a = Node("A", new Vector3(0f, 0f, 0f));
            DeliveryRoadNode b = Node("B", new Vector3(10f, 0f, 0f));
            DeliveryRoadSegment ab = Segment("AB", a, b, cornerDistance: 3f);
            var route = new DeliveryRoute(new[] { new DeliveryRoadTraversal(ab, false) });

            Assert.That(route.Evaluate(0f).Forward, Is.EqualTo(Vector3.right));
            Assert.That(route.Evaluate(10f).Forward, Is.EqualTo(Vector3.right));
        }

        [Test]
        public void 보행로는_차선_오프셋이_항상_0이다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 10f));
            DeliveryRoadSegment sidewalk = Segment("Sidewalk", a, b, isSidewalk: true);
            var route = new DeliveryRoute(new[] { new DeliveryRoadTraversal(sidewalk, false) });

            Assert.That(DeliveryRoutePose.PreferredLaneOffset(route.Evaluate(5f)), Is.EqualTo(0f));
        }

        /// <summary>
        /// 2026-08-18: 오프셋 자체는 더 이상 방향에 따라 부호를 뒤집지 않는다(우측통행이라는 같은
        /// 물리적 의미만 갖는다) — 대신 <see cref="DeliveryRoutePose.SegmentRight"/> 가 항상
        /// <see cref="DeliveryRoutePose.Forward"/> 기준 진짜 오른쪽이므로, "오프셋 크기는 방향과
        /// 무관하게 같지만 실제로 쏠리는 물리적 쪽(월드 좌표)은 서로 반대"가 맞는 계약이다.
        /// 예전 계약(오프셋이 스스로 부호를 뒤집고 <c>SegmentRight</c>는 역주행을 반영하지 않음)은
        /// 인접 세그먼트의 Reverse 가 서로 다른 교차로에서 <c>right</c> 부호가 경계마다 뒤집혀
        /// 표시 좌표가 순간이동하는 버그의 원인이었다.
        /// </summary>
        [Test]
        public void 도로는_역방향일때_차선_오프셋이_반대쪽_월드_위치로_쏠린다()
        {
            DeliveryRoadNode a = Node("A", Vector3.zero);
            DeliveryRoadNode b = Node("B", new Vector3(0f, 0f, 10f));
            DeliveryRoadSegment road = Segment("Road", a, b);
            var forwardRoute = new DeliveryRoute(new[] { new DeliveryRoadTraversal(road, false) });
            var reverseRoute = new DeliveryRoute(new[] { new DeliveryRoadTraversal(road, true) });

            DeliveryRoutePose forwardPose = forwardRoute.Evaluate(5f);
            DeliveryRoutePose reversePose = reverseRoute.Evaluate(5f);

            float forwardOffset = DeliveryRoutePose.PreferredLaneOffset(forwardPose);
            float reverseOffset = DeliveryRoutePose.PreferredLaneOffset(reversePose);
            Assert.That(forwardOffset, Is.EqualTo(reverseOffset).Within(0.001f));
            Assert.That(forwardOffset, Is.Not.Zero);

            Vector3 forwardFinal = forwardPose.Position + forwardPose.SegmentRight * forwardOffset;
            Vector3 reverseFinal = reversePose.Position + reversePose.SegmentRight * reverseOffset;
            Assert.That(forwardFinal.x, Is.EqualTo(-reverseFinal.x).Within(0.001f),
                "정방향·역방향은 같은 위치를 지나지만 우측통행이 서로 반대쪽 월드 좌표를 가리켜야 한다");
        }

        [Test]
        public void 역방향_경로도_코너_블렌딩_구간에서_차선_오프셋이_튀지_않는다()
        {
            DeliveryRoadNode a = Node("A", new Vector3(0f, 0f, 0f));
            DeliveryRoadNode b = Node("B", new Vector3(10f, 0f, 0f));
            DeliveryRoadNode c = Node("C", new Vector3(10f, 0f, 10f));
            DeliveryRoadSegment ab = Segment("AB", a, b, cornerDistance: 3f);
            DeliveryRoadSegment bc = Segment("BC", b, c, cornerDistance: 3f);
            // C -> B -> A, 즉 두 세그먼트 모두 원본(Start->End)의 반대 방향으로 주행한다.
            var route = new DeliveryRoute(new[]
            {
                new DeliveryRoadTraversal(bc, true),
                new DeliveryRoadTraversal(ab, true),
            });

            Vector3 previousFinal = default;
            bool first = true;
            for (float distance = 0f; distance <= route.Length; distance += 0.25f)
            {
                DeliveryRoutePose pose = route.Evaluate(distance);
                // 실제로 그려지는 값(PreferredLateralOffset)을 검사한다 — 정적 PreferredLaneOffset()은
                // 계약상 블렌딩되지 않은 원래 트래버설 값을 그대로 돌려주므로 이 테스트의 의도(렌더링에
                // 쓰이는 값이 튀지 않는지)와 안 맞는다.
                float offset = pose.PreferredLateralOffset;
                Vector3 final = pose.Position + pose.SegmentRight * offset;
                if (!first)
                {
                    float step = Vector3.Distance(previousFinal, final);
                    Assert.That(step, Is.LessThan(1f),
                        $"거리 {distance}m 부근에서 차선 오프셋이 불연속으로 튀었다(스텝 {step}m)");
                }
                previousFinal = final;
                first = false;
            }
        }

        private DeliveryRoadNode Node(string id, Vector3 position)
        {
            GameObject gameObject = NewObject(id);
            gameObject.transform.position = position;
            DeliveryRoadNode node = gameObject.AddComponent<DeliveryRoadNode>();
            node.Configure(id);
            return node;
        }

        private DeliveryRoadSegment Segment(string name, DeliveryRoadNode start, DeliveryRoadNode end,
                                            float width = 6f, float cornerDistance = 0f, bool isSidewalk = false)
        {
            DeliveryRoadSegment segment = NewObject(name).AddComponent<DeliveryRoadSegment>();
            segment.Configure(start, end, null, width, cornerDistance, 0.25f, isSidewalk);
            return segment;
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
