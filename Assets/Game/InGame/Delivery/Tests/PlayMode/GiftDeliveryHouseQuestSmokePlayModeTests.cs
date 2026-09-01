using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class GiftDeliveryHouseQuestSmokePlayModeTests
    {
        [UnityTest]
        public IEnumerator 퀘스트_집_굴뚝_연기는_지붕색과_동기화되고_원래_설정으로_복원된다()
        {
            var smokeObject = new GameObject("QuestSmokeTest");

            try
            {
                ParticleSystem smoke = smokeObject.AddComponent<ParticleSystem>();
                smoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);

                ParticleSystem.MainModule main = smoke.main;
                main.maxParticles = 18;
                main.startColor = Color.white;
                main.startSize = new ParticleSystem.MinMaxCurve(0.48f, 0.82f);

                ParticleSystem.EmissionModule emission = smoke.emission;
                emission.rateOverTime = new ParticleSystem.MinMaxCurve(1.15f, 1.65f);

                smoke.Emit(1);
                Color roofColor = new Color(0.68f, 0.82f, 1f, 1f);
                GiftDeliveryHouseQuestSmoke effect =
                    smokeObject.AddComponent<GiftDeliveryHouseQuestSmoke>();
                effect.Configure(smoke, roofColor);
                yield return null;

                Assert.That(effect.IsApplied, Is.True);
                Assert.That(effect.HighlightColor.r, Is.EqualTo(roofColor.r).Within(0.001f));
                Assert.That(smoke.main.startColor.colorMin.r, Is.EqualTo(roofColor.r).Within(0.001f));
                Assert.That(smoke.main.startSize.constantMin, Is.EqualTo(0.528f).Within(0.001f));
                Assert.That(smoke.main.startSize.constantMax, Is.EqualTo(0.902f).Within(0.001f));
                Assert.That(smoke.emission.rateOverTime.constantMin, Is.EqualTo(1.5525f).Within(0.001f));
                Assert.That(smoke.emission.rateOverTime.constantMax, Is.EqualTo(2.2275f).Within(0.001f));

                var living = new ParticleSystem.Particle[18];
                int livingCount = smoke.GetParticles(living);
                Assert.That(livingCount, Is.GreaterThan(0));
                Color livingColor = living[0].startColor;
                Assert.That(livingColor.r, Is.EqualTo(roofColor.r).Within(0.2f));

                effect.Restore();

                Assert.That(effect.IsApplied, Is.False);
                Assert.That(smoke.main.startColor.color, Is.EqualTo(Color.white));
                Assert.That(smoke.main.startSize.constantMin, Is.EqualTo(0.48f).Within(0.001f));
                Assert.That(smoke.main.startSize.constantMax, Is.EqualTo(0.82f).Within(0.001f));
                Assert.That(smoke.emission.rateOverTime.constantMin, Is.EqualTo(1.15f).Within(0.001f));
                Assert.That(smoke.emission.rateOverTime.constantMax, Is.EqualTo(1.65f).Within(0.001f));
            }
            finally
            {
                Object.Destroy(smokeObject);
            }
        }
    }
}
