using NUnit.Framework;

namespace PPack
{
    public sealed class SnowHeightFieldCpuTests
    {
        private static SnowHeightFieldCpu Small(int depthMm = 300)
            => new SnowHeightFieldCpu(new SnowFieldGeometry(8f, 8f, 0f, 0f), depthMm);

        [Test]
        public void Construction_FillsEveryCellWithTheInitialDepth()
        {
            var f = Small();
            for (int i = 0; i < f.HeightMm.Length; i++) Assert.AreEqual(300, f.HeightMm[i]);
            Assert.AreEqual((long)f.Geo.CellCount * 300, f.TotalHeightMm);
        }

        [Test]
        public void TotalHeightMm_TracksEveryWriteIncrementally()
        {
            var f = Small();
            f.Set(3, 4, 1200);
            f.Add(5, 6, -100);
            f.Add(7, 8, 50);
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm, "증분 원장이 실제 합과 갈라졌다");
        }

        [Test]
        public void Add_ClampsAtZero_AndReportsWhatItActuallyApplied()
        {
            var f = Small(300);
            int applied = f.Add(1, 1, -1000);
            Assert.AreEqual(-300, applied, "300 mm 밖에 없었으므로 300 만 빠져야 한다");
            Assert.AreEqual(0, f.Get(1, 1));
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Add_ClampsAtTheCeiling_AndReportsWhatItActuallyApplied()
        {
            var f = Small(300);
            int applied = f.Add(1, 1, 100000);
            Assert.AreEqual(SnowHeightFieldCpu.MaxHeightMm - 300, applied);
            Assert.AreEqual(SnowHeightFieldCpu.MaxHeightMm, f.Get(1, 1));
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void DeltaBuffer_StartsZero_AndApplyDeltaReturnsItToZero()
        {
            var f = Small();
            int i = f.Geo.CellIndex(2, 2);
            Assert.AreEqual(0, f.DeltaMm[i]);
            f.DeltaMm[i] = 77;
            f.ApplyDelta(2, 2);
            Assert.AreEqual(377, f.Get(2, 2));
            Assert.AreEqual(0, f.DeltaMm[i], "델타는 반영 후 반드시 0 이어야 다음 스텝이 오염되지 않는다");
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void TotalVolume_MatchesTheHandComputedSlab()
        {
            var f = new SnowHeightFieldCpu(new SnowFieldGeometry(128f, 128f, -64f, -64f), 300);
            Assert.AreEqual(4915.2, f.TotalVolumeM3, 1e-6);   // 128 x 128 m 에 0.30 m
        }

        [Test]
        public void ChunkRest_MarksDirtyOnWake_AndSleepsOnlyAfterTheThreshold()
        {
            var f = Small();
            Assert.IsFalse(f.IsChunkAwake(0), "필드는 잠든 채로 시작한다");
            f.WakeChunk(0);
            Assert.IsTrue(f.IsChunkAwake(0));
            for (int i = 0; i < SnowHeightFieldCpu.RestStepsToSleep - 1; i++)
            {
                f.RestChunk(0);
                Assert.IsTrue(f.IsChunkAwake(0), $"{i + 1} 스텝만에 잠들면 안 된다");
            }
            f.RestChunk(0);
            Assert.IsFalse(f.IsChunkAwake(0));
        }
        [Test]
        public void RenderDirty_누적된다_BeginStep이_비우지_않는다()
        {
            // 한 프레임에 FixedUpdate 가 여러 번 돌 수 있다. 스텝마다 비우면 렌더는
            // 마지막 스텝 것만 보게 되고, 그러면 앞 스텝이 바꾼 셀이 화면에 안 올라간다.
            var f = Small();
            f.BeginStep();
            f.WakeChunkOfCell(0, 0);
            f.BeginStep();
            f.WakeChunkOfCell(f.Geo.ResX - 1, f.Geo.ResZ - 1);

            Assert.AreEqual(2, f.RenderDirtyChunks.Count, "두 스텝의 청크가 둘 다 남아 있어야 한다");
            Assert.AreEqual(1, f.ChangedChunks.Count,
                            "ChangedChunks 는 스텝마다 비워지는 그대로여야 한다");
        }

        [Test]
        public void RenderDirty_같은_청크를_여러_번_깨워도_한_번만_들어간다()
        {
            var f = Small();
            f.BeginStep();
            for (int i = 0; i < 10; i++) f.WakeChunkOfCell(1, 1);
            Assert.AreEqual(1, f.RenderDirtyChunks.Count);
        }

        [Test]
        public void RenderDirty_ClearRenderDirty만_비운다()
        {
            var f = Small();
            f.BeginStep();
            f.WakeChunkOfCell(0, 0);
            Assert.AreEqual(1, f.RenderDirtyChunks.Count);

            f.ClearRenderDirty();
            Assert.AreEqual(0, f.RenderDirtyChunks.Count);

            // 비운 뒤에는 다시 들어갈 수 있어야 한다(플래그가 안 남아 있어야 한다).
            f.WakeChunkOfCell(0, 0);
            Assert.AreEqual(1, f.RenderDirtyChunks.Count);
        }

        [Test]
        public void RenderDirty_아무것도_안_건드리면_비어_있다()
        {
            var f = Small();
            f.BeginStep();
            Assert.AreEqual(0, f.RenderDirtyChunks.Count);
        }
    }
}
