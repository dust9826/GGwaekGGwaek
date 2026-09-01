using System.Linq;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class ThiefPerceptionAndBoundaryTests
    {
        [Test]
        public void 시야는_거리와_각도를_모두_만족해야_한다()
        {
            Assert.That(ThiefPlayerSensor.IsInsideView(Vector3.forward,
                new Vector3(0f, 0f, 10f), 18f, 120f), Is.True);
            Assert.That(ThiefPlayerSensor.IsInsideView(Vector3.forward,
                new Vector3(10f, 0f, 0f), 18f, 120f), Is.False);
            Assert.That(ThiefPlayerSensor.IsInsideView(Vector3.forward,
                new Vector3(0f, 0f, 19f), 18f, 120f), Is.False);
        }

        [Test]
        public void 최대_시각_인지_거리는_10미터다()
        {
            Assert.That(ThiefPlayerSensor.IsInsideView(Vector3.forward,
                Vector3.forward * 10f, 10f, 120f), Is.True);
            Assert.That(ThiefPlayerSensor.IsInsideView(Vector3.forward,
                Vector3.forward * 10.01f, 10f, 120f), Is.False);
        }

        [Test]
        public void 습격_어댑터는_보관소_타입을_직접_참조하지_않는다()
        {
            string[] fieldTypes = typeof(ThiefRaidSite)
                .GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
                .Select(field => field.FieldType.FullName)
                .ToArray();

            Assert.That(fieldTypes.Any(type => type != null && type.Contains("Warehouse")), Is.False);
            Assert.That(fieldTypes.Any(type => type != null && type.Contains("Storage")), Is.False);
        }

        [Test]
        public void 근접_위협은_인지도를_즉시_최대로_만든다()
        {
            float next = ThiefPlayerSensor.NextAwareness01(0f, hasVisiblePlayer: true, isCloseThreat: true,
                deltaSeconds: 0f, waryFloor01: 0.3f, decayPerSecond: 2f);
            Assert.That(next, Is.EqualTo(1f));
        }

        [Test]
        public void 먼_시야는_인지도를_즉시_Wary_바닥값으로_올린다()
        {
            float next = ThiefPlayerSensor.NextAwareness01(0f, hasVisiblePlayer: true, isCloseThreat: false,
                deltaSeconds: 0f, waryFloor01: 0.3f, decayPerSecond: 2f);
            Assert.That(next, Is.EqualTo(0.3f));
        }

        [Test]
        public void 시야를_잃으면_인지도가_경과시간에_비례해_감쇠한다()
        {
            float next = ThiefPlayerSensor.NextAwareness01(1f, hasVisiblePlayer: false, isCloseThreat: false,
                deltaSeconds: 0.25f, waryFloor01: 0.3f, decayPerSecond: 2f);
            Assert.That(next, Is.EqualTo(0.5f).Within(0.0001f));
        }

        [Test]
        public void 인지도는_0과_1_사이로_클램프된다()
        {
            float decayedBelowZero = ThiefPlayerSensor.NextAwareness01(0.1f, false, false,
                deltaSeconds: 10f, waryFloor01: 0.3f, decayPerSecond: 2f);
            Assert.That(decayedBelowZero, Is.EqualTo(0f));
        }

        [Test]
        public void 먼_시야_상태에서_이미_인지도가_높으면_바닥값까지만_서서히_감쇠한다()
        {
            float next = ThiefPlayerSensor.NextAwareness01(1f, hasVisiblePlayer: true, isCloseThreat: false,
                deltaSeconds: 1f, waryFloor01: 0.3f, decayPerSecond: 2f);
            Assert.That(next, Is.EqualTo(0.3f).Within(0.0001f),
                "시야에 있는 한 Wary 바닥값 아래로는 내려가지 않아야 한다");
        }

        [Test]
        public void Spotted는_이탈_문턱_아래로_감쇠해야_Wary로_내려간다()
        {
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Spotted, 0.61f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Spotted), "0.6 초과이면 아직 Spotted를 유지해야 한다");
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Spotted, 0.6f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Wary), "이탈 문턱에 닿으면 Wary로 내려가야 한다");
        }

        [Test]
        public void Wary는_이탈_문턱_아래로_감쇠해야_Calm으로_내려간다()
        {
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Wary, 0.16f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Wary));
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Wary, 0.15f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Calm));
        }

        [Test]
        public void Calm에서는_진입_문턱을_넘어야_Wary가_된다()
        {
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Calm, 0.29f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Calm));
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Calm, 0.3f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Wary));
        }

        [Test]
        public void 인지도가_1에_도달하면_Calm에서도_바로_Spotted로_승격된다()
        {
            Assert.That(ThiefPlayerSensor.NextAwarenessStage(EThiefAwarenessStage.Calm, 1f,
                waryEnterThreshold: 0.3f, waryExitThreshold: 0.15f, spottedExitThreshold: 0.6f),
                Is.EqualTo(EThiefAwarenessStage.Spotted));
        }
    }
}
