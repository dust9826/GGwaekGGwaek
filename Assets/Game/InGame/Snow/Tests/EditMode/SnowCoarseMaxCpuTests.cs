using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    /// <summary>
    /// coarse-max 는 <b>상한</b>이다. 이 파일이 지키는 것은 성능이 아니라 그 성질 하나다 —
    /// 상한이 한 텍셀이라도 깨지면 마칭이 그 자리에서 표면을 뚫고 화면에 구멍이 뚫린다.
    /// </summary>
    public sealed class SnowCoarseMaxCpuTests
    {
        private static SnowHeightFieldCpu Field(int depthMm = 0)
            => new SnowHeightFieldCpu(new SnowFieldGeometry(16f, 16f, 0f, 0f), depthMm);

        private static void FillRandom(SnowHeightFieldCpu f, int seed)
        {
            var rng = new System.Random(seed);
            for (int cz = 0; cz < f.Geo.ResZ; cz++)
            for (int cx = 0; cx < f.Geo.ResX; cx++)
                f.Set(cx, cz, (ushort)rng.Next(0, 3000));
        }

        /// <summary>텍셀의 상한이 그 안전반경 안의 모든 셀보다 크거나 같은지 전수로 확인한다.</summary>
        private static void AssertBoundHolds(SnowHeightFieldCpu f, SnowCoarseMaxCpu c, string when)
        {
            int reach = (SnowCoarseMaxCpu.DilateTexels + 1) * SnowCoarseMaxCpu.BlockCells - 1;
            for (int tz = 0; tz < c.ResZ; tz++)
            for (int tx = 0; tx < c.ResX; tx++)
            {
                int bound = c.MaxMm[tz * c.ResX + tx];
                int cx0 = tx * SnowCoarseMaxCpu.BlockCells;
                int cz0 = tz * SnowCoarseMaxCpu.BlockCells;

                // 이 텍셀이 덮는 블록 + 다일레이트 반경 안의 셀은 전부 상한 이하여야 한다.
                for (int cz = cz0 - reach + SnowCoarseMaxCpu.BlockCells - 1;
                         cz <= cz0 + reach; cz++)
                for (int cx = cx0 - reach + SnowCoarseMaxCpu.BlockCells - 1;
                         cx <= cx0 + reach; cx++)
                {
                    if (!f.Geo.InBounds(cx, cz)) continue;
                    // 안전반경 안인지 텍셀 거리로 판정한다.
                    int dtx = System.Math.Abs(cx / SnowCoarseMaxCpu.BlockCells - tx);
                    int dtz = System.Math.Abs(cz / SnowCoarseMaxCpu.BlockCells - tz);
                    if (dtx > SnowCoarseMaxCpu.DilateTexels || dtz > SnowCoarseMaxCpu.DilateTexels) continue;

                    Assert.LessOrEqual(f.Get(cx, cz), bound,
                        $"{when}: 텍셀 {tx},{tz} 의 상한 {bound} 이 셀 {cx},{cz} 의 높이보다 낮다");
                }
            }
        }

        [Test]
        public void Bound_HoldsOverEveryCellInTheSafeRadius_OnRandomTerrain()
        {
            var f = Field();
            FillRandom(f, 4242);
            var c = new SnowCoarseMaxCpu(f);
            AssertBoundHolds(f, c, "RebuildAll");
        }

        [Test]
        public void Bound_StillHoldsAfterAChunkRebuild()
        {
            var f = Field(300);
            var c = new SnowCoarseMaxCpu(f);

            // 한 청크 안에 탑을 세우고 그 청크만 다시 굽는다.
            f.Set(20, 20, 5000);
            f.Set(21, 21, 4200);
            int ci = f.Geo.ChunkIndex(f.Geo.ChunkOfCellX(20), f.Geo.ChunkOfCellZ(20));
            c.RebuildChunks(new List<int> { ci });

            AssertBoundHolds(f, c, "RebuildChunks");
        }

        [Test]
        public void ChunkRebuild_MatchesAFullRebuild()
        {
            var f = Field();
            FillRandom(f, 7);
            var incremental = new SnowCoarseMaxCpu(f);

            var rng = new System.Random(11);
            var touched = new List<int>();
            for (int i = 0; i < 40; i++)
            {
                int cx = rng.Next(f.Geo.ResX), cz = rng.Next(f.Geo.ResZ);
                f.Set(cx, cz, (ushort)rng.Next(0, 9000));
                int ci = f.Geo.ChunkIndex(f.Geo.ChunkOfCellX(cx), f.Geo.ChunkOfCellZ(cz));
                if (!touched.Contains(ci)) touched.Add(ci);
            }
            touched.Sort();
            incremental.RebuildChunks(touched);

            var full = new SnowCoarseMaxCpu(f);
            CollectionAssert.AreEqual(full.MaxMm, incremental.MaxMm,
                "증분 재빌드가 전체 재빌드와 갈라졌다 - 다일레이트 이웃까지 다시 굽지 않은 것이다");
        }

        [Test]
        public void BlockSize_DividesTheChunkExactly()
        {
            // 나눠 떨어져야 청크 하나가 coarse 텍셀 정수 개를 덮고 경계 처리가 없다.
            Assert.AreEqual(0, SnowFieldGeometry.ChunkCells % SnowCoarseMaxCpu.BlockCells);
        }

        [Test]
        public void SafeRadius_IsAtLeastTheDilatedBlockExtent()
        {
            var c = new SnowCoarseMaxCpu(Field());
            float minimum = SnowCoarseMaxCpu.BlockCells * 0.5f * SnowFieldGeometry.CellSizeM;
            Assert.GreaterOrEqual(c.SafeRadiusM, minimum,
                "안전반경이 블록 반폭보다 작으면 마칭이 자기 텍셀 안에서도 뛰어넘을 수 있다");
        }

        [Test]
        public void EmptyField_HasAZeroBound()
        {
            var c = new SnowCoarseMaxCpu(Field(0));
            foreach (var v in c.MaxMm) Assert.AreEqual(0, v);
        }
    }
}
