using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class DeliverySnowClearanceTests
    {
        private readonly List<GameObject> _objects = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            foreach (GameObject gameObject in _objects) Object.DestroyImmediate(gameObject);
            _objects.Clear();
        }

        [Test]
        public void 넓은_도로의_오른쪽만_치우면_오른쪽_통로를_선택한다()
        {
            DeliveryRoute route = MakeStraightRoute(6f);
            var field = new SnowField(-4f, -2f, 8f, 14f, 0.25f, 30);
            field.FillAll(30);
            var clearRight = new SnowStampArea(1.5f, 5f, 0f, 1f, 7f, 1.1f);
            field.ApplyStamp(1, 1, clearRight, -30);

            bool found = DeliverySnowClearance.TryFindOffset(
                route, 0f, 0f, 6f, 1.5f, 0.75f, 0.2f,
                5, 0.5f, 0.25f,
                position => field.DepthCmAtWorld(position.x, position.z), out float offset);

            Assert.IsTrue(found);
            Assert.That(offset, Is.GreaterThan(0.5f));
        }

        [Test]
        public void 트럭보다_좁은_빈_띠는_통로가_아니다()
        {
            DeliveryRoute route = MakeStraightRoute(6f);
            var field = new SnowField(-4f, -2f, 8f, 14f, 0.25f, 30);
            field.FillAll(30);
            var narrowClear = new SnowStampArea(0f, 5f, 0f, 1f, 7f, 0.2f);
            field.ApplyStamp(1, 1, narrowClear, -30);

            bool found = DeliverySnowClearance.TryFindOffset(
                route, 0f, 0f, 6f, 1.5f, 1f, 0.2f,
                5, 0.5f, 0.25f,
                position => field.DepthCmAtWorld(position.x, position.z), out _);

            Assert.IsFalse(found);
        }

        [Test]
        public void 트럭_발밑은_제외하면_앞이_뚫려_있으면_통과한다()
        {
            // 트럭(halfLength 1.5)이 routeDistance 0 에 서 있다 — 자기 차체가 덮은 자리
            // [-1.5, 1.5] 는 플레이어가 못 치운다. 그 뒤(z<1.5)는 계속 30cm 눈으로 남겨두고
            // 앞 6m([1.5, 7.5])만 치운다.
            DeliveryRoute route = MakeStraightRoute(6f);
            var field = new SnowField(-4f, -2f, 8f, 14f, 0.25f, 30);
            field.FillAll(30);
            // 경계 셀 반올림 여유로 [1.5, 7.5] 보다 양쪽 0.5m 씩 더 넉넉히 치운다.
            var clearAhead = new SnowStampArea(0f, 4.5f, 0f, 1f, 3.5f, 3f);
            field.ApplyStamp(1, 1, clearAhead, -30);

            bool found = DeliverySnowClearance.TryFindOffset(
                route, 0f, 0f, 6f, 1.5f, 0.75f, 0.2f,
                5, 0.5f, 0.25f,
                position => field.DepthCmAtWorld(position.x, position.z), out _,
                footprintExclusionStart: 1.5f);

            Assert.IsTrue(found, "발밑을 제외했으니 앞 6m 가 뚫려 있으면 통과해야 한다");
        }

        [Test]
        public void 발밑을_제외하지_않으면_트럭_자신의_눈에_영원히_막힌다()
        {
            // 같은 조건인데 제외를 안 넘긴 옛 호출부 — 트럭 자신이 서 있는 자리(0 근방)가
            // 계속 30cm 눈이라 앞이 뚫려 있어도 절대 통과하지 못한다. 고치기 전 실제 버그였다.
            DeliveryRoute route = MakeStraightRoute(6f);
            var field = new SnowField(-4f, -2f, 8f, 14f, 0.25f, 30);
            field.FillAll(30);
            var clearAhead = new SnowStampArea(0f, 4.5f, 0f, 1f, 3f, 3f);
            field.ApplyStamp(1, 1, clearAhead, -30);

            bool found = DeliverySnowClearance.TryFindOffset(
                route, 0f, 0f, 6f, 1.5f, 0.75f, 0.2f,
                5, 0.5f, 0.25f,
                position => field.DepthCmAtWorld(position.x, position.z), out _);

            Assert.IsFalse(found, "제외 없이는 트럭 자신의 발밑 눈 때문에 통과할 수 없다");
        }

        [Test]
        public void 초기_전방_4미터를_치우면_출발할_수_있다()
        {
            DeliveryRoute route = MakeStraightRoute(4.5f);
            var field = new SnowField(-4f, -2f, 8f, 12f, 0.125f, 30);
            field.FillAll(30);

            // 길이 4m 트럭의 앞끝은 z=2다. 12.5cm 셀의 대각선(약 17.7cm)을 더한
            // z=2.177부터 검사하므로, 패드 뒤끝도 그 위치에 맞춰 차체와 겹치지 않게 둔다.
            float start = 2f + 0.125f * Mathf.Sqrt(2f);
            var onePadAhead = new SnowStampArea(0f, start + 2f, 0f, 1f, 2f, 1.15f);
            field.ApplyStamp(1, 1, onePadAhead, -30);

            bool found = DeliverySnowClearance.TryFindOffset(
                route, 0f, 0f, 4f, 2f, 1f, 0.25f,
                5, 0.75f, 0.5f,
                position => field.DepthCmAtWorld(position.x, position.z), out _,
                footprintExclusionStart: start);

            Assert.IsTrue(found, "차체와 겹치지 않는 전방 패드 한 장으로 출발해야 한다");
        }

        [Test]
        public void 막힌_지점까지의_거리를_돌려준다()
        {
            // 앞 4m 만 치워두면 그 자리가 정지선이 된다 — 트럭은 이 거리로 미리 감속해 선다.
            DeliveryRoute route = MakeStraightRoute(6f);
            var field = new SnowField(-4f, -2f, 8f, 14f, 0.25f, 30);
            field.FillAll(30);
            var clearAhead = new SnowStampArea(0f, 2.5f, 0f, 1f, 3.5f, 3f);
            field.ApplyStamp(1, 1, clearAhead, -30);

            float clear = DeliverySnowClearance.ClearDistance(
                route, 0f, 0f, 8f, 1.5f, 0.75f, 0.2f,
                5, 0.5f,
                position => field.DepthCmAtWorld(position.x, position.z),
                footprintExclusionStart: 1.5f);

            Assert.That(clear, Is.GreaterThan(0f), "앞이 조금이라도 뚫려 있으면 그만큼은 갈 수 있어야 한다");
            Assert.That(clear, Is.LessThan(8f), "치운 구간 끝에서 막혀야 한다");
        }

        [Test]
        public void 전부_뚫려_있으면_검사_창_전체를_돌려준다()
        {
            DeliveryRoute route = MakeStraightRoute(6f);

            float clear = DeliverySnowClearance.ClearDistance(
                route, 0f, 0f, 6f, 1.5f, 0.75f, 0.2f, 5, 0.5f, null);

            Assert.That(clear, Is.EqualTo(6f).Within(0.001f));
        }

        [Test]
        public void 눈_시스템이_없는_씬은_중앙을_통과한다()
        {
            DeliveryRoute route = MakeStraightRoute(4f);

            bool found = DeliverySnowClearance.TryFindOffset(
                route, 0f, 1f, 5f, 1.5f, 0.75f, 0.2f,
                5, 0.5f, 0.25f, null, out float offset);

            Assert.IsTrue(found);
            Assert.That(offset, Is.Zero);
        }

        private DeliveryRoute MakeStraightRoute(float width)
        {
            DeliveryRoadNode start = NewObject("Start").AddComponent<DeliveryRoadNode>();
            start.transform.position = Vector3.zero;
            DeliveryRoadNode end = NewObject("End").AddComponent<DeliveryRoadNode>();
            end.transform.position = new Vector3(0f, 0f, 10f);
            DeliveryRoadSegment road = NewObject("Road").AddComponent<DeliveryRoadSegment>();
            road.Configure(start, end, null, width, 0f, 0.25f);
            return new DeliveryRoute(new[] { new DeliveryRoadTraversal(road, false) });
        }

        private GameObject NewObject(string name)
        {
            var gameObject = new GameObject($"__TEST__{name}");
            _objects.Add(gameObject);
            return gameObject;
        }
    }
}
