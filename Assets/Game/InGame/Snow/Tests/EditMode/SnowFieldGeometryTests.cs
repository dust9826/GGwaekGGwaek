using NUnit.Framework;

namespace PPack
{
    public sealed class SnowFieldGeometryTests
    {
        private static SnowFieldGeometry Default() => new SnowFieldGeometry(128f, 128f, -64f, -64f);

        [Test]
        public void Resolution_Is1024Squared_ForA128MetreMap()
        {
            var g = Default();
            Assert.AreEqual(1024, g.ResX);
            Assert.AreEqual(1024, g.ResZ);
            Assert.AreEqual(1024 * 1024, g.CellCount);
        }

        [Test]
        public void ChunkGrid_Is64Squared_AndTilesTheFieldExactly()
        {
            var g = Default();
            Assert.AreEqual(64, g.ChunksX);
            Assert.AreEqual(64, g.ChunksZ);
            Assert.AreEqual(g.ChunksX * SnowFieldGeometry.ChunkCells, g.ResX);
            Assert.AreEqual(g.ChunksZ * SnowFieldGeometry.ChunkCells, g.ResZ);
        }

        [Test]
        public void Resolution_RoundsUpToWholeChunks_ForAnAwkwardMapSize()
        {
            // 101 m / 0.125 = 808 셀. 808 % 16 = 8 이므로 816 으로 올라간다.
            var g = new SnowFieldGeometry(101f, 101f, 0f, 0f);
            Assert.AreEqual(816, g.ResX);
            Assert.AreEqual(51, g.ChunksX);
            Assert.AreEqual(0, g.ResX % SnowFieldGeometry.ChunkCells);
        }

        [Test]
        public void ChunkSide_IsAMultipleOfTheNetworkBlock()
        {
            // 이 한 줄이 "리플리케이션 자리를 비워둔다"의 실질이다.
            Assert.AreEqual(0, SnowFieldGeometry.ChunkCells % SnowFieldGeometry.NetworkBlockCells);
        }

        [Test]
        public void QuadtreeSide_IsThePowerOfTwoCoveringTheChunkGrid()
        {
            Assert.AreEqual(64, Default().QuadtreeSide);
            Assert.AreEqual(6, Default().QuadtreeDepth);

            var g = new SnowFieldGeometry(101f, 101f, 0f, 0f);   // 51 x 51 청크
            Assert.AreEqual(64, g.QuadtreeSide);
            Assert.AreEqual(6, g.QuadtreeDepth);
        }

        [Test]
        public void WorldToCell_RoundTripsThroughCellCentre_ForEveryCellOnADiagonal()
        {
            var g = Default();
            for (int i = 0; i < g.ResX; i++)
            {
                g.CellCenterWorld(i, i, out float wx, out float wz);
                Assert.IsTrue(g.TryWorldToCell(wx, wz, out int cx, out int cz), $"cell {i} centre fell outside");
                Assert.AreEqual(i, cx, $"x at {i}");
                Assert.AreEqual(i, cz, $"z at {i}");
            }
        }

        [Test]
        public void WorldToCell_RejectsPointsOutsideTheField()
        {
            var g = Default();
            Assert.IsFalse(g.TryWorldToCell(-64.001f, 0f, out _, out _));
            Assert.IsFalse(g.TryWorldToCell(0f, 64.0f, out _, out _), "상단 경계는 배타적이어야 한다");
            Assert.IsTrue(g.TryWorldToCell(-64f, -64f, out int cx, out int cz));
            Assert.AreEqual(0, cx);
            Assert.AreEqual(0, cz);
        }

        [Test]
        public void ChunkCellBounds_PartitionTheFieldWithNoGapsAndNoOverlaps()
        {
            var g = Default();
            var seen = new byte[g.CellCount];
            for (int ci = 0; ci < g.ChunkCount; ci++)
            {
                g.ChunkCellBounds(ci, out int cx0, out int cz0, out int cx1, out int cz1);
                Assert.AreEqual(SnowFieldGeometry.ChunkCells, cx1 - cx0 + 1);
                Assert.AreEqual(SnowFieldGeometry.ChunkCells, cz1 - cz0 + 1);
                for (int cz = cz0; cz <= cz1; cz++)
                for (int cx = cx0; cx <= cx1; cx++)
                    seen[g.CellIndex(cx, cz)]++;
            }
            for (int i = 0; i < seen.Length; i++) Assert.AreEqual(1, seen[i], $"cell {i} covered {seen[i]} times");
        }

        [Test]
        public void ChunkOfCell_AgreesWithChunkCellBounds()
        {
            var g = Default();
            for (int cz = 0; cz < g.ResZ; cz += 7)
            for (int cx = 0; cx < g.ResX; cx += 7)
            {
                int ci = g.ChunkIndex(g.ChunkOfCellX(cx), g.ChunkOfCellZ(cz));
                g.ChunkCellBounds(ci, out int cx0, out int cz0, out int cx1, out int cz1);
                Assert.IsTrue(cx >= cx0 && cx <= cx1 && cz >= cz0 && cz <= cz1, $"cell {cx},{cz} not in chunk {ci}");
            }
        }

        [Test]
        public void WorldRectToCellRect_ClampsToTheFieldAndCoversTheRect()
        {
            var g = Default();
            Assert.IsTrue(g.TryWorldRectToCellRect(-70f, -70f, -63.9f, -63.9f,
                                                   out int cx0, out int cz0, out int cx1, out int cz1));
            Assert.AreEqual(0, cx0);
            Assert.AreEqual(0, cz0);
            Assert.AreEqual(0, cx1);
            Assert.AreEqual(0, cz1);

            Assert.IsFalse(g.TryWorldRectToCellRect(-200f, -200f, -100f, -100f, out _, out _, out _, out _),
                           "필드와 전혀 겹치지 않으면 false");
        }
    }
}
