using NUnit.Framework;

namespace PPack
{
    public sealed class SnowPlowStepCpuTests
    {
        private const int InitialMm = 300;

        private static SnowHeightFieldCpu Field()
            => new SnowHeightFieldCpu(new SnowFieldGeometry(64f, 64f, -32f, -32f), InitialMm);

        private static SnowPlowStepInput Drive(float z0, float z1, bool down = true)
            => new SnowPlowStepInput
            {
                Prev = new SnowBladePose { CenterX = 0f, CenterZ = z0, ForwardX = 0f, ForwardZ = 1f },
                Now  = new SnowBladePose { CenterX = 0f, CenterZ = z1, ForwardX = 0f, ForwardZ = 1f },
                BladeDown = down,
                SignedSpeedMps = (z1 - z0) * 60f,
                DtSeconds = 1f / 60f
            };

        /// <summary>z0 에서 시작해 steps 번 전진한다. 끝난 뒤의 블레이드 z 를 돌려준다.</summary>
        private static float DriveForward(SnowPlowStepCpu sim, float z0, int steps, bool down = true,
                                          float perStep = 0.05f)
        {
            float z = z0;
            for (int i = 0; i < steps; i++) { sim.Step(Drive(z, z + perStep, down)); z += perStep; }
            return z;
        }

        private static int At(SnowHeightFieldCpu f, float wx, float wz)
        {
            Assert.IsTrue(f.Geo.TryWorldToCell(wx, wz, out int cx, out int cz), $"{wx},{wz} 가 필드 밖이다");
            return f.Get(cx, cz);
        }

        [Test]
        public void Step_ConservesMassExactly_OverAFiveHundredStepDrive()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            long before = f.TotalHeightMm;

            float z = -25f;
            for (int i = 0; i < 500; i++)
            {
                var st = sim.Step(Drive(z, z + 0.05f));
                Assert.AreEqual(0, st.ConservationErrorMm, $"스텝 {i} 에서 {st.ConservationErrorMm} mm 가 새거나 생겼다");
                Assert.AreEqual(0, st.UnplacedMm, $"스텝 {i} 에서 {st.UnplacedMm} mm 를 놓지 못했다");
                Assert.AreEqual(0, st.ClampedMm, $"스텝 {i} 에서 {st.ClampedMm} mm 가 잘려나갔다");
                z += 0.05f;
            }
            Assert.AreEqual(before, f.TotalHeightMm);
            Assert.AreEqual(before, f.RecomputeTotalHeightMm());
        }

        [Test]
        public void Step_IsBitwiseDeterministicAcrossTwoIdenticalRuns()
        {
            var a = Field(); var simA = new SnowPlowStepCpu(a);
            var b = Field(); var simB = new SnowPlowStepCpu(b);
            DriveForward(simA, -25f, 300);
            DriveForward(simB, -25f, 300);
            CollectionAssert.AreEqual(a.HeightMm, b.HeightMm);
        }

        [Test]
        public void Step_NeverWritesOutsideTheActiveRadius()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            var snapshot = (ushort[])f.HeightMm.Clone();

            sim.Step(Drive(-25f, -24.95f));

            // 블레이드는 (0, -25) 부근. 활성 반경 10 m 보다 훨씬 먼 곳은 한 셀도 변하면 안 된다.
            for (int cz = 0; cz < f.Geo.ResZ; cz++)
            for (int cx = 0; cx < f.Geo.ResX; cx++)
            {
                f.Geo.CellCenterWorld(cx, cz, out float wx, out float wz);
                float dx = wx - 0f, dz = wz - (-25f);
                if (dx * dx + dz * dz < 15f * 15f) continue;
                int i = f.Geo.CellIndex(cx, cz);
                Assert.AreEqual(snapshot[i], f.HeightMm[i], $"15 m 밖의 셀 {cx},{cz} 이 변했다");
            }
        }

        [Test]
        public void Step_BuildsAHeapInFrontOfTheBlade()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            float z = DriveForward(sim, -25f, 200);

            int heap = At(f, 0f, z + 0.5f);
            Assert.Greater(heap, InitialMm, $"블레이드 앞이 처녀설보다 높아지지 않았다 (측정 {heap} mm)");
        }

        [Test]
        public void Step_LeavesTheLaneBehindTheBladeCleared()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            DriveForward(sim, -25f, 200);

            int lane = At(f, 0f, -22f);
            Assert.Less(lane, InitialMm / 2, $"치운 차선에 {lane} mm 가 남았다");
        }

        [Test]
        public void Step_ThrowsSnowToTheSidesOnceTheHeapOverflows()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            float z = DriveForward(sim, -25f, 400);

            // 블레이드 끝단(1.15 m) 바깥, 이미 지나온 자리. 둔덕이 여기 있어야 한다.
            int berm = 0;
            for (float w = 1.2f; w <= 2.6f; w += 0.125f) berm = System.Math.Max(berm, At(f, w, z - 4f));
            Assert.Greater(berm, InitialMm, $"둔덕이 생기지 않았다 (끝단 밖 최대 {berm} mm)");
        }

        [Test]
        public void Step_WithTheBladeUpCutsNothingAndLeavesTheHeapStanding()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            float z = DriveForward(sim, -25f, 150);

            float heapZ = z + 0.5f;
            int heapBefore = At(f, 0f, heapZ);
            Assert.Greater(heapBefore, InitialMm, "먼저 더미가 있어야 이 테스트가 의미가 있다");

            for (int i = 0; i < 60; i++)
            {
                var st = sim.Step(Drive(z, z + 0.05f, down: false));
                Assert.AreEqual(0, st.CutMm, "블레이드를 들었는데 깎았다");
                z += 0.05f;
            }

            Assert.Greater(At(f, 0f, heapZ), InitialMm,
                           "블레이드를 들었더니 더미가 따라왔다 - 더미는 그 자리에 남아야 한다");
        }

        /// <summary>차선 뒤쪽 좌 / 우 옆구리에 쌓인, 처녀설을 넘는 양.</summary>
        private static long BermMass(SnowHeightFieldCpu f, float laneX, float z0, float z1, float from, float to)
        {
            long sum = 0;
            for (float z = z0; z <= z1; z += SnowFieldGeometry.CellSizeM)
            for (float w = from; w <= to; w += SnowFieldGeometry.CellSizeM)
            {
                if (!f.Geo.TryWorldToCell(laneX + w, z, out int cx, out int cz)) continue;
                int h = f.Get(cx, cz);
                if (h > InitialMm) sum += h - InitialMm;
            }
            return sum;
        }

        [Test]
        public void Step_WithABladeAngledRight_ThrowsMostOfTheSnowToTheRight()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            var v = new SnowBladeVehicleCpu(0f, -25f, 0f) { BladeAngleDeg = 30f };

            float z = -25f;
            for (int i = 0; i < 400; i++)
            {
                v.Integrate(new SnowVehicleInput { Throttle = 1f, Steer = 0f, BladeDown = true },
                            1f / 60f, f);
                var st = sim.Step(new SnowPlowStepInput
                {
                    Prev = v.PrevBladePose, Now = v.BladePose,
                    BladeDown = v.BladeDown, SignedSpeedMps = v.SpeedMps, DtSeconds = 1f / 60f
                });
                Assert.AreEqual(0, st.ConservationErrorMm, $"스텝 {i} 에서 질량이 샜다");
                z = v.PosZ;
            }

            long right = BermMass(f, 0f, -24f, z - 4f,  1.4f,  4f);
            long left  = BermMass(f, 0f, -24f, z - 4f, -4f,  -1.4f);

            Assert.Greater(right, left * 2,
                $"우향 블레이드인데 오른쪽 둔덕({right})이 왼쪽({left})의 두 배도 안 된다");
        }

        [Test]
        public void Step_WithABladeAngledLeft_MirrorsTheRightAngledCase()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            var v = new SnowBladeVehicleCpu(0f, -25f, 0f) { BladeAngleDeg = -30f };

            float z = -25f;
            for (int i = 0; i < 400; i++)
            {
                v.Integrate(new SnowVehicleInput { Throttle = 1f, Steer = 0f, BladeDown = true },
                            1f / 60f, f);
                sim.Step(new SnowPlowStepInput
                {
                    Prev = v.PrevBladePose, Now = v.BladePose,
                    BladeDown = v.BladeDown, SignedSpeedMps = v.SpeedMps, DtSeconds = 1f / 60f
                });
                z = v.PosZ;
            }

            long right = BermMass(f, 0f, -24f, z - 4f,  1.4f,  4f);
            long left  = BermMass(f, 0f, -24f, z - 4f, -4f,  -1.4f);

            Assert.Greater(left, right * 2,
                $"좌향 블레이드인데 왼쪽 둔덕({left})이 오른쪽({right})의 두 배도 안 된다");
        }

        [Test]
        public void Shape_IsSettableAtRuntimeAndWidensTheCut()
        {
            var narrow = Field();
            var wide = Field();
            var simN = new SnowPlowStepCpu(narrow) { Shape = new SnowBladeShape { HalfWidthM = 0.6f, HalfDepthM = 0.175f } };
            var simW = new SnowPlowStepCpu(wide)   { Shape = new SnowBladeShape { HalfWidthM = 2.4f, HalfDepthM = 0.175f } };

            var stN = simN.Step(Drive(-25f, -24.95f));
            var stW = simW.Step(Drive(-25f, -24.95f));

            Assert.Greater(stW.CutMm, stN.CutMm * 2,
                $"넓은 블레이드({stW.CutMm})가 좁은 것({stN.CutMm})의 두 배도 못 깎았다");
        }

        [Test]
        public void Step_ReportsTheActiveChunkCountAndStaysUnderTheCap()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            var st = sim.Step(Drive(-25f, -24.95f));
            Assert.Greater(st.ActiveChunks, 0);
            Assert.LessOrEqual(st.ActiveChunks, SnowPlowStepCpu.ActiveChunkCap);
            Assert.AreEqual(0, st.DroppedByCap, "이 규모에서 상한에 걸리면 안 된다");
        }

        [Test]
        public void Step_LetsChunksFallAsleepAfterTheBladeHasMovedOn()
        {
            var f = Field();
            var sim = new SnowPlowStepCpu(f);
            DriveForward(sim, -25f, 40);
            int early = sim.Tree.DirtyCount;

            // 블레이드를 들고 멀리 떨어진 곳에서 제자리 대기 - 지나온 자리는 정착해야 한다.
            for (int i = 0; i < 400; i++) sim.Step(Drive(20f, 20f, down: false));

            Assert.Less(sim.Tree.DirtyCount, early,
                        $"블레이드가 떠난 뒤에도 청크가 안 잠든다 (전 {early}, 후 {sim.Tree.DirtyCount})");
        }
    }
}
