using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class StageHudOrderViewTests
    {
        [Test]
        public void 월드_목표를_UI용_거리와_방향으로_변환한다()
        {
            var originObject = new GameObject("__TEST__StageHudOrigin");
            try
            {
                originObject.transform.position = new Vector3(10f, 3f, 20f);
                originObject.transform.forward = Vector3.forward;

                StageHudOrderView view = StageHudOrderView.FromWorldTarget(
                    7, Color.red, 25f, originObject.transform,
                    new Vector3(20f, 99f, 30f), 100f, true);

                Assert.That(view.Id, Is.EqualTo(7));
                Assert.That(view.DistanceMeters, Is.EqualTo(Mathf.Sqrt(200f)).Within(0.001f));
                Assert.That(view.DirectionDegrees, Is.EqualTo(45f).Within(0.001f));
                Assert.IsTrue(view.ShowNavigation);
            }
            finally
            {
                Object.DestroyImmediate(originObject);
            }
        }

        [Test]
        public void 원점이_없으면_경로거리_폴백을_쓴다()
        {
            StageHudOrderView view = StageHudOrderView.FromWorldTarget(
                1, Color.blue, 10f, null, Vector3.one, 88f, false);

            Assert.That(view.DistanceMeters, Is.EqualTo(88f));
            Assert.That(view.DirectionDegrees, Is.Zero);
        }
    }
}
