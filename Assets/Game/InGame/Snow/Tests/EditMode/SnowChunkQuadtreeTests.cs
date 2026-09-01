using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    public sealed class SnowChunkQuadtreeTests
    {
        private static SnowFieldGeometry Geo() => new SnowFieldGeometry(128f, 128f, -64f, -64f);

        private static List<int> BruteForceDirty(bool[] dirty)
        {
            var r = new List<int>();
            for (int i = 0; i < dirty.Length; i++) if (dirty[i]) r.Add(i);
            return r;
        }

        [Test]
        public void QueryDirty_MatchesBruteForce_OverAThousandRandomMarkAndClearOperations()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            var shadow = new bool[geo.ChunkCount];
            var rng = new System.Random(20260818);
            var got = new List<int>();

            for (int op = 0; op < 1000; op++)
            {
                int ci = rng.Next(geo.ChunkCount);
                if (rng.Next(2) == 0) { tree.MarkDirty(ci); shadow[ci] = true; }
                else                  { tree.ClearDirty(ci); shadow[ci] = false; }

                if (op % 37 != 0) continue;
                tree.QueryDirty(got);
                CollectionAssert.AreEqual(BruteForceDirty(shadow), got, $"op {op} 에서 갈라졌다");
            }
        }

        [Test]
        public void DirtyCount_MatchesTheNumberOfDirtyLeaves()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            tree.MarkDirty(5);
            tree.MarkDirty(5);          // 두 번 켜도 하나다
            tree.MarkDirty(9);
            Assert.AreEqual(2, tree.DirtyCount);
            tree.ClearDirty(5);
            tree.ClearDirty(5);         // 두 번 꺼도 하나다
            Assert.AreEqual(1, tree.DirtyCount);
        }

        [Test]
        public void QueryDirty_ReturnsAscendingChunkIndices()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            var rng = new System.Random(7);
            for (int i = 0; i < 200; i++) tree.MarkDirty(rng.Next(geo.ChunkCount));

            var got = new List<int>();
            tree.QueryDirty(got);
            Assert.Greater(got.Count, 0);
            for (int i = 1; i < got.Count; i++)
                Assert.Less(got[i - 1], got[i], "결정론은 순회 순서가 고정일 때만 성립한다");
        }

        [Test]
        public void QueryDirty_IsEmptyWhenNothingIsDirty()
        {
            var tree = new SnowChunkQuadtree(Geo());
            var got = new List<int> { 999 };
            tree.QueryDirty(got);
            Assert.AreEqual(0, got.Count);
        }

        [Test]
        public void ClearDirty_CollapsesAncestorsOnlyWhenEverySiblingIsClean()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            int a = geo.ChunkIndex(0, 0);
            int b = geo.ChunkIndex(1, 0);   // 같은 부모
            tree.MarkDirty(a);
            tree.MarkDirty(b);
            tree.ClearDirty(a);

            var got = new List<int>();
            tree.QueryDirty(got);
            CollectionAssert.AreEqual(new List<int> { b }, got, "형제가 아직 더러운데 부모를 접었다");
        }

        [Test]
        public void QueryRect_MatchesBruteForce_OverAHundredRandomRects()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            var rng = new System.Random(4242);
            var got = new List<int>();

            for (int t = 0; t < 100; t++)
            {
                int cx0 = rng.Next(geo.ResX), cx1 = rng.Next(geo.ResX);
                int cz0 = rng.Next(geo.ResZ), cz1 = rng.Next(geo.ResZ);
                if (cx1 < cx0) { int s = cx0; cx0 = cx1; cx1 = s; }
                if (cz1 < cz0) { int s = cz0; cz0 = cz1; cz1 = s; }

                var want = new List<int>();
                for (int ci = 0; ci < geo.ChunkCount; ci++)
                {
                    geo.ChunkCellBounds(ci, out int bx0, out int bz0, out int bx1, out int bz1);
                    if (bx1 >= cx0 && bx0 <= cx1 && bz1 >= cz0 && bz0 <= cz1) want.Add(ci);
                }
                tree.QueryRect(cx0, cz0, cx1, cz1, got);
                CollectionAssert.AreEqual(want, got, $"rect {cx0},{cz0}..{cx1},{cz1}");
            }
        }

        [Test]
        public void ActiveSet_IsTheSortedDeduplicatedUnionOfBothQueries()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            tree.MarkDirty(geo.ChunkIndex(0, 0));      // rect 와 겹친다
            tree.MarkDirty(geo.ChunkIndex(40, 40));    // rect 밖이다

            var set = new SnowActiveSet(512);
            set.Build(tree, 0, 0, 31, 31, 512);        // 2 x 2 청크

            var expected = new List<int>
            {
                geo.ChunkIndex(0, 0), geo.ChunkIndex(1, 0),
                geo.ChunkIndex(0, 1), geo.ChunkIndex(1, 1),
                geo.ChunkIndex(40, 40)
            };
            expected.Sort();
            CollectionAssert.AreEqual(expected, set.Chunks);
            Assert.AreEqual(0, set.DroppedByCap);
        }

        [Test]
        public void ActiveSet_AppliesTheCapDeterministicallyAndReportsWhatItDropped()
        {
            var geo = Geo();
            var tree = new SnowChunkQuadtree(geo);
            for (int i = 0; i < 100; i++) tree.MarkDirty(i * 7 % geo.ChunkCount);

            var a = new SnowActiveSet(16);
            var b = new SnowActiveSet(16);
            a.Build(tree, 0, 0, 15, 15, 16);
            b.Build(tree, 0, 0, 15, 15, 16);

            Assert.AreEqual(16, a.Chunks.Count);
            Assert.Greater(a.DroppedByCap, 0, "상한이 실제로 걸렸어야 한다");
            CollectionAssert.AreEqual(a.Chunks, b.Chunks, "같은 입력에 같은 출력이 아니면 피어가 갈라진다");
        }
    }
}
