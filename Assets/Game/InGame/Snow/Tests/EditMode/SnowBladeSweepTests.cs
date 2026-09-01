using NUnit.Framework;

namespace PPack
{
    public sealed class SnowBladeSweepTests
    {
        private static SnowHeightFieldCpu Field(int depthMm = 300)
            => new SnowHeightFieldCpu(new SnowFieldGeometry(16f, 16f, 0f, 0f), depthMm);

        private static SnowBladePose Pose(float x, float z)
            => new SnowBladePose { CenterX = x, CenterZ = z, ForwardX = 0f, ForwardZ = 1f };

        private static SnowBladeShape Shape => SnowBladeShape.Default;

        [Test]
        public void Cut_TakesTheWholeColumn_NotASkin()
        {
            var f = Field(300);
            long cut = SnowBladeSweep.Cut(f, Pose(8f, 8f), Pose(8f, 8f), Shape, 1, 0);
            Assert.Greater(cut, 0);
            f.Geo.TryWorldToCell(8f, 8f, out int cx, out int cz);
            Assert.AreEqual(0, f.Get(cx, cz), "스윕 박스 안은 전량 제거다");
        }

        [Test]
        public void Cut_ReturnsExactlyWhatItRemovedFromTheField()
        {
            var f = Field(300);
            long before = f.TotalHeightMm;
            long cut = SnowBladeSweep.Cut(f, Pose(8f, 6f), Pose(8f, 8f), Shape, 3, 0);
            Assert.AreEqual(before - f.TotalHeightMm, cut);
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Cut_LeavesTheResidueWhenOneIsAsked()
        {
            var f = Field(300);
            SnowBladeSweep.Cut(f, Pose(8f, 8f), Pose(8f, 8f), Shape, 1, 30);
            f.Geo.TryWorldToCell(8f, 8f, out int cx, out int cz);
            Assert.AreEqual(30, f.Get(cx, cz));
        }

        [Test]
        public void Cut_TouchesNothingOutsideTheSweptBox()
        {
            var f = Field(300);
            SnowBladeSweep.Cut(f, Pose(8f, 8f), Pose(8f, 8f), Shape, 1, 0);
            Assert.AreEqual(300, f.Get(2, 2), "블레이드에서 6 m 떨어진 셀이 변했다");
            f.Geo.TryWorldToCell(8f + 1.15f + 0.5f, 8f, out int ox, out int oz);
            Assert.AreEqual(300, f.Get(ox, oz), "블레이드 끝단 바깥 0.5 m 가 깎였다");
        }

        [Test]
        public void Cut_OfAnEmptyFieldReturnsZeroAndChangesNothing()
        {
            var f = Field(0);
            Assert.AreEqual(0, SnowBladeSweep.Cut(f, Pose(8f, 6f), Pose(8f, 8f), Shape, 3, 0));
            Assert.AreEqual(0, f.TotalHeightMm);
        }

        [Test]
        public void Cut_OverALongSweepClearsTheWholeLaneWithoutGaps()
        {
            var f = Field(300);
            // 두께 0.35 m 블레이드가 한 스텝에 2 m 를 간다. 세그먼트가 충분하면 구멍이 없다.
            SnowBladeSweep.Cut(f, Pose(8f, 6f), Pose(8f, 8f), Shape, 8, 0);
            for (float z = 6.1f; z < 7.9f; z += 0.1f)
            {
                Assert.IsTrue(f.Geo.TryWorldToCell(8f, z, out int cx, out int cz));
                Assert.AreEqual(0, f.Get(cx, cz), $"차선 z={z:0.0} 에 눈이 남았다");
            }
        }

        [Test]
        public void Cut_WakesTheChunksItTouched()
        {
            var f = Field(300);
            f.Geo.TryWorldToCell(8f, 8f, out int cx, out int cz);
            int home = f.Geo.ChunkIndex(f.Geo.ChunkOfCellX(cx), f.Geo.ChunkOfCellZ(cz));
            Assert.IsFalse(f.IsChunkAwake(home));
            SnowBladeSweep.Cut(f, Pose(8f, 8f), Pose(8f, 8f), Shape, 1, 0);
            Assert.IsTrue(f.IsChunkAwake(home));
        }

        [Test]
        public void Deposit_PlacesEverythingItWasGiven()
        {
            var f = Field(0);
            long before = f.TotalHeightMm;
            long unplaced = SnowBladeSweep.Deposit(f, Pose(8f, 8f), Shape, 1.0f, 0.1f, 500000);
            Assert.AreEqual(0, unplaced, "놓지 못한 잔량이 있으면 그만큼이 장부 없이 사라진다");
            Assert.AreEqual(500000, f.TotalHeightMm - before);
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Deposit_PutsMoreSnowNearTheBladeThanAtTheFarEdgeOfTheBand()
        {
            var f = Field(0);
            SnowBladeSweep.Deposit(f, Pose(8f, 8f), Shape, 1.0f, 0.1f, 500000);
            f.Geo.TryWorldToCell(8f, 8f + 0.175f + 0.1f, out int nx, out int nz);
            f.Geo.TryWorldToCell(8f, 8f + 0.175f + 0.9f, out int fx, out int fz);
            Assert.Greater(f.Get(nx, nz), f.Get(fx, fz));
        }

        [Test]
        public void Deposit_LandsInFrontOfTheBladeNotBehindIt()
        {
            var f = Field(0);
            SnowBladeSweep.Deposit(f, Pose(8f, 8f), Shape, 1.0f, 0.1f, 500000);
            f.Geo.TryWorldToCell(8f, 8f - 0.5f, out int bx, out int bz);
            Assert.AreEqual(0, f.Get(bx, bz), "블레이드 뒤에 눈이 놓였다");
        }

        [Test]
        public void Deposit_RotatesWithTheBlade()
        {
            var f = Field(0);
            var east = new SnowBladePose { CenterX = 8f, CenterZ = 8f, ForwardX = 1f, ForwardZ = 0f };
            SnowBladeSweep.Deposit(f, east, Shape, 1.0f, 0.1f, 400000);
            f.Geo.TryWorldToCell(8f + 0.5f, 8f, out int ex, out int ez);
            Assert.Greater(f.Get(ex, ez), 0, "동쪽을 보는 블레이드는 동쪽에 부어야 한다");
            f.Geo.TryWorldToCell(8f, 8f + 0.5f, out int nx, out int nz);
            Assert.AreEqual(0, f.Get(nx, nz));
        }

        [Test]
        public void MakeBarrier_CoversTheBladeFootprintAndNothingElse()
        {
            var b = SnowBladeSweep.MakeBarrier(Pose(8f, 8f), Shape);
            Assert.IsTrue(b.Active);
            Assert.IsTrue(b.Contains(8f, 8f));
            Assert.IsTrue(b.Contains(8f + 1.1f, 8f));
            Assert.IsFalse(b.Contains(8f + 1.3f, 8f), "끝단 바깥은 벽이 아니다 - 둔덕이 여기로 빠진다");
            Assert.IsFalse(b.Contains(8f, 8f + 0.5f));
        }
    }
}
