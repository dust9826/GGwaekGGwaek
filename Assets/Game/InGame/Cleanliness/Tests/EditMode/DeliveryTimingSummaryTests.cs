using NUnit.Framework;

namespace PPack
{
    public sealed class DeliveryTimingSummaryTests
    {
        [Test]
        public void 요약은_왕복과_TTL_여유를_한줄로_접는다()
        {
            var summary = new DeliveryTimingSummary();
            // 직선 왕복 120m 를 132m 걸어갔고 19초 중 16초만 움직였다 -> 우회 1.10x, 8.25m/s
            summary.Add(1, 2, 60f, 1f, 35f, 10d, 9d, 132d, 16d);
            summary.Add(2, 3, 90f, 1.5f, 50f, 15d, 12d, 180d, 27d);

            string line = summary.ToLogLine();

            StringAssert.Contains("samples=2", line);
            StringAssert.Contains("roundTrip=23.00/23.00/27.00s", line);
            StringAssert.Contains("ttlMargin=30.00/25.00s", line);
        }

        [Test]
        public void 우회와_정지시간은_직선거리와_흐른시간에서_분리된다()
        {
            var summary = new DeliveryTimingSummary();
            // 직선 왕복 120m 인데 132m 를 걸었고, 19초 중 16초만 실제로 움직였다.
            summary.Add(1, 2, 60f, 1f, 35f, 10d, 9d, 132d, 16d);

            DeliveryTimingSummary.Sample sample = summary.Samples[0];

            Assert.That(sample.DetourRatio, Is.EqualTo(1.10d).Within(0.001d));
            Assert.That(sample.SpeedMps, Is.EqualTo(8.25d).Within(0.001d));
            Assert.That(sample.IdleSeconds, Is.EqualTo(3d).Within(0.001d));
        }

        [Test]
        public void 서_있기만_하면_속도는_0이고_직선거리가_0이면_우회도_0이다()
        {
            var summary = new DeliveryTimingSummary();
            summary.Add(1, 2, 0f, 1f, 35f, 10d, 9d, 0d, 0d);

            DeliveryTimingSummary.Sample sample = summary.Samples[0];

            Assert.That(sample.SpeedMps, Is.EqualTo(0d));
            Assert.That(sample.DetourRatio, Is.EqualTo(0d));
        }
    }
}
