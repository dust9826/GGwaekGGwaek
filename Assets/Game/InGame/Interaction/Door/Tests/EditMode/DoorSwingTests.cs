using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// <see cref="DoorSwing"/> 는 순수 C# 이라 Unity 없이도 검증할 수 있다. 여기서는 래치·양방향·
    /// 가역성·덜컹 감쇠 네 가지 규약만 잰다 — 나머지(지렛대 계산·충돌 판정)는 <c>ImpactDoor</c> 쪽
    /// PlayMode 테스트 몫이다.
    /// </summary>
    public sealed class DoorSwingTests
    {
        private static DoorSwing NewSwing() =>
            new DoorSwing(maxAngleDeg: 90f, inertiaKgM2: 6f, angularDampingPerSecond: 2.5f,
                bounce01: 0.15f, latchAngleDeg: 3f, latchBreakL: 40f, rattleMaxDeg: 1.5f,
                rattleFrequencyHz: 9f);

        private static void StepFor(DoorSwing swing, float seconds, float dt = 1f / 60f)
        {
            for (float t = 0f; t < seconds; t += dt) swing.Step(dt);
        }

        [Test]
        public void 래치를_못_이기면_열리지_않는다()
        {
            var swing = NewSwing();

            bool opened = swing.TryApplyHit(10f, out float blocked01);

            Assert.IsFalse(opened);
            Assert.Greater(blocked01, 0f);
            Assert.Less(blocked01, 1f);
            Assert.AreEqual(0f, swing.AngVelDegPerS);
        }

        [Test]
        public void 래치를_이기면_열린다()
        {
            var swing = NewSwing();

            bool opened = swing.TryApplyHit(80f, out _);
            StepFor(swing, 1f);

            Assert.IsTrue(opened);
            Assert.Greater(swing.AngleDeg, 0f);
        }

        [Test]
        public void 반대쪽에서_밀어도_같은_문턱으로_열린다()
        {
            var swing = NewSwing();

            bool opened = swing.TryApplyHit(-80f, out _);
            StepFor(swing, 1f);

            Assert.IsTrue(opened);
            Assert.Less(swing.AngleDeg, 0f);
        }

        [Test]
        public void 열린_문을_반대로_밀면_되돌아온다()
        {
            var swing = NewSwing();
            swing.TryApplyHit(80f, out _);
            StepFor(swing, 1.5f);
            float openedAngle = swing.AngleDeg;
            Assert.Greater(openedAngle, 20f);

            swing.TryApplyHit(-80f, out _);
            StepFor(swing, 1.5f);

            Assert.Less(swing.AngleDeg, openedAngle);
        }

        [Test]
        public void 각도는_범위를_벗어나지_않는다()
        {
            var swing = NewSwing();
            swing.TryApplyHit(100000f, out _);
            StepFor(swing, 3f);

            Assert.LessOrEqual(swing.AngleDeg, 90f);
            Assert.GreaterOrEqual(swing.AngleDeg, -90f);
        }

        [Test]
        public void 덜컹은_시간이_지나면_0으로_돌아오고_문은_안_열린다()
        {
            var swing = NewSwing();
            swing.TryApplyHit(10f, out float blocked01);
            swing.Kick(blocked01);

            StepFor(swing, 1f);

            Assert.AreEqual(0f, swing.AngleDeg);
            Assert.That(swing.RattleDeg, Is.EqualTo(0f).Within(0.01f));
        }
    }
}
