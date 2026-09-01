using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    public sealed class SnowReposeRelaxTests
    {
        private static SnowRelaxBarrier NoBarrier => default;
        private static SnowMaterialCpu Mat => SnowMaterialCpu.Default;

        private static List<int> AllChunks(SnowFieldGeometry g)
        {
            var l = new List<int>(g.ChunkCount);
            for (int i = 0; i < g.ChunkCount; i++) l.Add(i);
            return l;
        }

        private static SnowHeightFieldCpu Field(float sizeM, int depthMm)
            => new SnowHeightFieldCpu(new SnowFieldGeometry(sizeM, sizeM, 0f, 0f), depthMm);

        [Test]
        public void Iterate_ConservesTotalVolumeExactly_OverAThousandIterationsOfRandomTerrain()
        {
            var f = Field(8f, 0);
            var rng = new System.Random(31337);
            for (int cz = 0; cz < f.Geo.ResZ; cz++)
            for (int cx = 0; cx < f.Geo.ResX; cx++)
                f.Set(cx, cz, (ushort)rng.Next(0, 4000));

            long before = f.TotalHeightMm;
            var chunks = AllChunks(f.Geo);
            for (int i = 0; i < 1000; i++)
            {
                SnowReposeRelax.Iterate(f, chunks, NoBarrier, Mat, out long clamped);
                Assert.AreEqual(0, clamped, $"반복 {i} 에서 {clamped} mm 가 잘려나갔다");
            }

            Assert.AreEqual(before, f.TotalHeightMm, "정수 이완은 정확히 보존해야 한다");
            Assert.AreEqual(before, f.RecomputeTotalHeightMm());
        }

        [Test]
        public void Iterate_EliminatesEveryOverSteepSlope_GivenEnoughIterations()
        {
            var f = Field(8f, 0);
            f.Set(32, 32, 20000);                       // 20 m 기둥 하나
            var chunks = AllChunks(f.Geo);
            for (int i = 0; i < 3000; i++) SnowReposeRelax.Iterate(f, chunks, NoBarrier, Mat, out _);

            int worst = 0;
            for (int cz = 1; cz < f.Geo.ResZ - 1; cz++)
            for (int cx = 1; cx < f.Geo.ResX - 1; cx++)
            {
                int h = f.Get(cx, cz);
                worst = System.Math.Max(worst, h - f.Get(cx + 1, cz) - Mat.MaxDropOrthoMm);
                worst = System.Math.Max(worst, h - f.Get(cx, cz + 1) - Mat.MaxDropOrthoMm);
                worst = System.Math.Max(worst, h - f.Get(cx + 1, cz + 1) - Mat.MaxDropDiagMm);
            }
            // 잔차 상한은 RelaxDenominator - 1 이다. 초과분이 그보다 작으면 정수 나눗셈이 0 이
            // 되어 움직이지 않고, 그 자리가 진짜 고정점이 된다. 178 mm 낙차의 4%.
            Assert.Less(worst, Mat.RelaxDenominator,
                        $"안식각을 {worst} mm 초과하는 경사가 남았다");
        }

        [Test]
        public void Iterate_IsAFixedPointOnAConeAtExactlyTheReposeAngle()
        {
            var f = Field(8f, 0);
            const int cx0 = 32, cz0 = 32, peak = 5000;
            for (int cz = 0; cz < f.Geo.ResZ; cz++)
            for (int cx = 0; cx < f.Geo.ResX; cx++)
            {
                // 체비셰프 거리 원뿔은 직교 낙차와 대각 낙차를 동시에 만족한다.
                int d = System.Math.Max(System.Math.Abs(cx - cx0), System.Math.Abs(cz - cz0));
                int h = peak - d * Mat.MaxDropOrthoMm;
                f.Set(cx, cz, (ushort)(h > 0 ? h : 0));
            }
            var snapshot = (ushort[])f.HeightMm.Clone();
            long moved = SnowReposeRelax.Iterate(f, AllChunks(f.Geo), NoBarrier, Mat, out _);

            Assert.AreEqual(0, moved, "안식각에 정확히 놓인 원뿔은 움직이면 안 된다 - 안 그러면 세계가 평면으로 녹는다");
            CollectionAssert.AreEqual(snapshot, f.HeightMm);
        }

        [Test]
        public void Iterate_IsIndependentOfChunkVisitOrder()
        {
            var a = Field(8f, 0);
            var b = Field(8f, 0);
            var rng = new System.Random(99);
            for (int cz = 0; cz < a.Geo.ResZ; cz++)
            for (int cx = 0; cx < a.Geo.ResX; cx++)
            {
                ushort v = (ushort)rng.Next(0, 6000);
                a.Set(cx, cz, v);
                b.Set(cx, cz, v);
            }

            var fwd = AllChunks(a.Geo);
            var rev = AllChunks(b.Geo);
            rev.Reverse();

            for (int i = 0; i < 40; i++)
            {
                SnowReposeRelax.Iterate(a, fwd, NoBarrier, Mat, out _);
                SnowReposeRelax.Iterate(b, rev, NoBarrier, Mat, out _);
            }
            CollectionAssert.AreEqual(a.HeightMm, b.HeightMm,
                "델타 버퍼가 제 역할을 하면 순회 순서가 결과를 바꿀 수 없다");
        }

        [Test]
        public void Iterate_NeverProducesANegativeHeight()
        {
            var f = Field(8f, 0);
            f.Set(10, 10, 60000);
            f.Set(11, 10, 1);
            for (int i = 0; i < 500; i++) SnowReposeRelax.Iterate(f, AllChunks(f.Geo), NoBarrier, Mat, out _);
            foreach (var h in f.HeightMm) Assert.GreaterOrEqual(h, 0);
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Iterate_WakesTheChunksItTouched()
        {
            var f = Field(8f, 0);
            f.Set(32, 32, 20000);
            int home = f.Geo.ChunkIndex(f.Geo.ChunkOfCellX(32), f.Geo.ChunkOfCellZ(32));
            Assert.IsFalse(f.IsChunkAwake(home), "필드는 잠든 채로 시작한다");

            SnowReposeRelax.Iterate(f, AllChunks(f.Geo), NoBarrier, Mat, out _);
            Assert.IsTrue(f.IsChunkAwake(home), "눈이 움직인 청크는 깨어 있어야 dirty 꼬리가 자란다");
        }

        [Test]
        public void Barrier_StopsSnowFromFlowingIntoTheBladeFootprint()
        {
            var f = Field(8f, 0);
            f.Set(32, 32, 20000);

            // x = 4.3125 m 는 셀 34 의 중심. 한 셀 폭 x z 전체 높이의 벽.
            var barrier = new SnowRelaxBarrier
            {
                Active = true,
                SegmentCount = 1,
                S0 = new SnowObb
                {
                    CenterX = 4.3125f, CenterZ = 4.0f,
                    RightX = 1f, RightZ = 0f,
                    ForwardX = 0f, ForwardZ = 1f,
                    HalfWidthM = 0.0624f,      // 셀 34 만 덮고 33 · 35 는 안 덮는다
                    HalfDepthM = 4f
                }
            };

            for (int i = 0; i < 800; i++) SnowReposeRelax.Iterate(f, AllChunks(f.Geo), barrier, Mat, out _);

            for (int cz = 0; cz < f.Geo.ResZ; cz++)
                Assert.AreEqual(0, f.Get(34, cz), $"배리어 셀 34,{cz} 이 눈을 받았다");
            Assert.Greater(f.Get(30, 32), 0, "배리어 반대편에는 눈이 쌓여 있어야 한다");
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Barrier_DoesNotBlockCellsBeyondItsEnds()
        {
            // 벽이 블레이드 폭에서 끝난다는 것이 둔덕의 원인이다. 그 성질을 고정한다.
            var b = new SnowRelaxBarrier
            {
                Active = true,
                SegmentCount = 1,
                S0 = new SnowObb
                {
                    CenterX = 0f, CenterZ = 0f,
                    RightX = 1f, RightZ = 0f,
                    ForwardX = 0f, ForwardZ = 1f,
                    HalfWidthM = 1.15f, HalfDepthM = 0.175f
                }
            };
            Assert.IsTrue(b.Contains(1.1f, 0f));
            Assert.IsFalse(b.Contains(1.2f, 0f), "끝단 바깥은 벽이 아니다 - 넘친 눈이 여기로 빠진다");
            Assert.IsFalse(b.Contains(0f, 0.2f));
        }
    }
}
