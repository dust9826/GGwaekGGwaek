using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// 타일 분할의 계산만 검증한다. <b>틀리면 화면에 실금이 가는 부분이 전부 여기</b>이고,
    /// 순수 계산이라 그래픽 장치 없이 전수로 돌릴 수 있다.
    /// </summary>
    public sealed class SnowPanelTilingTests
    {
        private const float Spacing = 0.25f;

        [Test]
        public void LatticeCount_MatchesTheOldSinglePanelFormula()
        {
            // 120 m / 0.25 = 480 구간 → 481 정점. 지금 BuildGrid 가 쓰는 식과 같아야 한다.
            Assert.AreEqual(481, SnowPanelTiling.LatticeCount(120f, Spacing));
            Assert.AreEqual(441, SnowPanelTiling.LatticeCount(110f, Spacing));
            Assert.AreEqual(2, SnowPanelTiling.LatticeCount(0.01f, Spacing), "최소 2 정점");
        }

        [Test]
        public void 이웃_타일이_공유하는_모서리_정점은_비트단위로_같다()
        {
            // 이 테스트가 이 파일의 존재 이유다. 타일마다 로컬 원점으로 계산하면
            // 부동소수 결과가 갈려 타일 경계에 실금이 간다.
            const float min = -60f, size = 120f;
            int count = SnowPanelTiling.LatticeCount(size, Spacing);
            int quads = SnowPanelTiling.QuadsPerTile(16f, Spacing);
            int tiles = SnowPanelTiling.TileCountOnAxis(count, quads);

            for (int t = 0; t < tiles - 1; t++)
            {
                SnowPanelTiling.TileVertexRange(count, quads, t, out _, out int hi);
                SnowPanelTiling.TileVertexRange(count, quads, t + 1, out int nextLo, out _);
                Assert.AreEqual(hi, nextLo, $"타일 {t} 의 끝과 {t + 1} 의 시작이 같은 정점이어야 한다");

                float a = SnowPanelTiling.LatticePos(min, size, count, hi);
                float b = SnowPanelTiling.LatticePos(min, size, count, nextLo);
                Assert.IsTrue(a.Equals(b), $"공유 정점이 다르다: {a:R} vs {b:R}");
            }
        }

        [Test]
        public void 타일들이_격자를_빠짐없이_한_번씩_덮는다()
        {
            foreach (float tileM in new[] { 8f, 16f, 20f, 30f, 500f })
            {
                int count = SnowPanelTiling.LatticeCount(120f, Spacing);
                int quads = SnowPanelTiling.QuadsPerTile(tileM, Spacing);
                int tiles = SnowPanelTiling.TileCountOnAxis(count, quads);

                int covered = 0;
                int expectedLo = 0;
                for (int t = 0; t < tiles; t++)
                {
                    SnowPanelTiling.TileVertexRange(count, quads, t, out int lo, out int hi);
                    Assert.AreEqual(expectedLo, lo, $"타일 {t} 이 앞 타일과 안 붙는다 (tile {tileM} m)");
                    Assert.Greater(hi, lo, $"타일 {t} 에 quad 가 없다 (tile {tileM} m)");
                    covered += hi - lo;
                    expectedLo = hi;
                }

                Assert.AreEqual(count - 1, covered, $"quad 합이 전역과 다르다 (tile {tileM} m)");
                Assert.AreEqual(count - 1, expectedLo, $"마지막 타일이 끝에 안 닿는다 (tile {tileM} m)");
            }
        }

        [Test]
        public void TileCountOnAxis_IsAtLeastOne_EvenWhenTheTileIsBiggerThanTheField()
        {
            int count = SnowPanelTiling.LatticeCount(6f, Spacing);         // 25 정점
            int quads = SnowPanelTiling.QuadsPerTile(16f, Spacing);        // 64 quad
            Assert.AreEqual(1, SnowPanelTiling.TileCountOnAxis(count, quads));
            SnowPanelTiling.TileVertexRange(count, quads, 0, out int lo, out int hi);
            Assert.AreEqual(0, lo);
            Assert.AreEqual(count - 1, hi, "한 장짜리 타일은 격자 전체를 덮어야 한다");
        }

        [Test]
        public void TryDirtyCellRect_ReturnsFalse_WhenNothingIsDirty()
        {
            var geo = new SnowFieldGeometry(16f, 16f, 0f, 0f);
            Assert.IsFalse(SnowPanelTiling.TryDirtyCellRect(geo, new List<int>(),
                                                            out _, out _, out _, out _));
        }

        [Test]
        public void TryDirtyCellRect_CoversExactlyOneChunk()
        {
            var geo = new SnowFieldGeometry(16f, 16f, 0f, 0f);   // 128x128 셀, 8x8 청크
            int chunk = geo.ChunkIndex(3, 5);
            Assert.IsTrue(SnowPanelTiling.TryDirtyCellRect(geo, new List<int> { chunk },
                                                           out int cx0, out int cz0,
                                                           out int cx1, out int cz1));
            Assert.AreEqual(3 * 16, cx0);
            Assert.AreEqual(5 * 16, cz0);
            Assert.AreEqual(3 * 16 + 15, cx1);
            Assert.AreEqual(5 * 16 + 15, cz1);
        }

        [Test]
        public void TryDirtyCellRect_SpansTwoDistantChunks()
        {
            var geo = new SnowFieldGeometry(16f, 16f, 0f, 0f);
            var dirty = new List<int> { geo.ChunkIndex(1, 1), geo.ChunkIndex(6, 4) };
            Assert.IsTrue(SnowPanelTiling.TryDirtyCellRect(geo, dirty,
                                                           out int cx0, out int cz0,
                                                           out int cx1, out int cz1));
            Assert.AreEqual(1 * 16, cx0);
            Assert.AreEqual(1 * 16, cz0);
            Assert.AreEqual(6 * 16 + 15, cx1);
            Assert.AreEqual(4 * 16 + 15, cz1);
        }

        [Test]
        public void StagingSizeFor_RoundsUpToAPowerOfTwo_AndRefusesWhatIsTooBig()
        {
            Assert.AreEqual(16, SnowPanelTiling.StagingSizeFor(1, 1, 256));
            Assert.AreEqual(16, SnowPanelTiling.StagingSizeFor(16, 9, 256));
            Assert.AreEqual(32, SnowPanelTiling.StagingSizeFor(17, 9, 256));
            Assert.AreEqual(64, SnowPanelTiling.StagingSizeFor(40, 64, 256));
            Assert.AreEqual(256, SnowPanelTiling.StagingSizeFor(160, 160, 256));
            Assert.AreEqual(0, SnowPanelTiling.StagingSizeFor(257, 4, 256), "넘으면 0 = 전체 업로드");
            Assert.AreEqual(0, SnowPanelTiling.StagingSizeFor(4, 300, 256));
        }
    }
}
