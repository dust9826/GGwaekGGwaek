using NUnit.Framework;

namespace PPack
{
    public sealed class SnowFootprintCpuTests
    {
        private static SnowHeightFieldCpu Field(int depthMm = 300)
            => new SnowHeightFieldCpu(new SnowFieldGeometry(24f, 24f, 0f, 0f), depthMm);

        private static long Step(SnowFootprintCpu footprint, SnowHeightFieldCpu field,
                                 float x, float z, bool grounded = true, float radiusM = 0.4f)
        {
            field.BeginStep();
            field.BeginCutPhase();
            long removed = footprint.Step(field, x, z, radiusM, grounded);
            field.EndCutPhase();
            return removed;
        }

        private static int HeightAt(SnowHeightFieldCpu field, float x, float z)
        {
            Assert.IsTrue(field.Geo.TryWorldToCell(x, z, out int cx, out int cz));
            return field.Get(cx, cz);
        }

        [Test]
        public void FirstGroundSample_SeedsContactWithoutDiggingWhileStanding()
        {
            SnowHeightFieldCpu field = Field();
            var footprint = new SnowFootprintCpu();

            Assert.AreEqual(0, Step(footprint, field, 4f, 12f));
            Assert.AreEqual(300, HeightAt(field, 4f, 12f));
            Assert.Greater(footprint.ContactCellCount, 0);
        }

        [Test]
        public void MovingIntoFreshCells_RemovesExactlyFiftyMillimetres()
        {
            SnowHeightFieldCpu field = Field();
            var footprint = new SnowFootprintCpu();
            Step(footprint, field, 4f, 12f);

            long removed = Step(footprint, field, 6f, 12f);

            Assert.Greater(removed, 0);
            Assert.AreEqual(250, HeightAt(field, 5f, 12f));
            Assert.Greater(field.CutCells.Count, 0, "발자국 절삭이 네트워크 CutCells에 기록돼야 한다");
        }

        [Test]
        public void StandingOnTheSameCells_DoesNotRemoveFiftyMillimetresEveryTick()
        {
            SnowHeightFieldCpu field = Field();
            var footprint = new SnowFootprintCpu();
            Step(footprint, field, 4f, 12f);
            Step(footprint, field, 6f, 12f);
            int before = HeightAt(field, 6f, 12f);

            long removed = Step(footprint, field, 6f, 12f);

            Assert.AreEqual(0, removed);
            Assert.AreEqual(before, HeightAt(field, 6f, 12f));
        }

        [Test]
        public void LeavingAndReturning_CountsAsASecondPass()
        {
            SnowHeightFieldCpu field = Field();
            var footprint = new SnowFootprintCpu();
            Step(footprint, field, 4f, 12f);
            Step(footprint, field, 6f, 12f);
            Assert.AreEqual(250, HeightAt(field, 6f, 12f));

            Step(footprint, field, 8f, 12f);
            Step(footprint, field, 6f, 12f);

            Assert.AreEqual(200, HeightAt(field, 6f, 12f));
        }

        [Test]
        public void Landing_RemovesOneFootprintButAirborneFramesRemoveNothing()
        {
            SnowHeightFieldCpu field = Field();
            var footprint = new SnowFootprintCpu();
            Step(footprint, field, 4f, 12f);

            Assert.AreEqual(0, Step(footprint, field, 4f, 12f, grounded: false));
            Assert.AreEqual(300, HeightAt(field, 4f, 12f));

            Assert.Greater(Step(footprint, field, 4f, 12f), 0);
            Assert.AreEqual(250, HeightAt(field, 4f, 12f));
            Assert.AreEqual(0, Step(footprint, field, 4f, 12f));
            Assert.AreEqual(250, HeightAt(field, 4f, 12f));
        }

        [Test]
        public void FastSweep_LeavesNoUncutStripes()
        {
            SnowHeightFieldCpu field = Field();
            var footprint = new SnowFootprintCpu();
            Step(footprint, field, 4f, 12f);

            Step(footprint, field, 9f, 12f);

            for (float x = 4.6f; x <= 8.8f; x += SnowFieldGeometry.CellSizeM)
                Assert.AreEqual(250, HeightAt(field, x, 12f), $"x={x:F3} 에 절삭되지 않은 줄이 남았다");
        }

        [Test]
        public void ShallowSnow_StopsAtZero()
        {
            SnowHeightFieldCpu field = Field(30);
            var footprint = new SnowFootprintCpu();
            Step(footprint, field, 4f, 12f);

            Step(footprint, field, 6f, 12f);

            Assert.AreEqual(0, HeightAt(field, 5f, 12f));
            Assert.AreEqual(field.RecomputeTotalHeightMm(), field.TotalHeightMm);
        }
    }
}
