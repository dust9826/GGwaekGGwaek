using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈덩이의 권위 계약. 가중치 1에서는 <c>필드 + 공 = 초기</c>를 지키고, 생산 가중치 0.5에서는
    /// 실제 수확량 중 절반만 공 질량으로 취득하는지 확인한다.
    /// </summary>
    public sealed class SnowBallCpuTests
    {
        private static SnowHeightFieldCpu Field(int depthMm = 300)
            => new SnowHeightFieldCpu(new SnowFieldGeometry(24f, 24f, 0f, 0f), depthMm);

        private static SnowBallCpu Ball(SnowHeightFieldCpu f, int residueMm = 0)
            => new SnowBallCpu(f, residueMm);

        [Test]
        public void SnowBallPrefab_ClearsEveryCellToGround()
        {
            SnowBallCarrier prefab = Resources.Load<SnowBallCarrier>("PF_SnowBall");

            Assert.IsNotNull(prefab, "Resources/PF_SnowBall 프리팹이 없다");
            Assert.AreEqual(0, prefab.ResidueMm,
                            "런타임 공의 직렬화 잔량이 0이 아니면 코드 기본값과 무관하게 눈이 남는다");
            Assert.AreEqual(SnowBallCpu.DefaultGrowthWeightPermille, prefab.GrowthWeightPermille,
                            "생산 공의 성장 가중치는 0.5여야 한다");
        }

        [Test]
        public void GrowthHudMetrics_FollowTheAuthoritativeRadiusQuarters()
        {
            SnowBallCarrier prefab = Resources.Load<SnowBallCarrier>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            SnowBallCarrier ball = Object.Instantiate(prefab);

            try
            {
                ball.ServerApplyMass(0);
                Assert.AreEqual(ESnowBallGrowthStage.Seed, ball.GrowthStage);
                Assert.AreEqual(SnowBallCpu.SeedRadiusM * 2f, ball.DiameterM, 0.001f);
                Assert.Greater(ball.RemainingDiameterToNextGrowthTargetM, 0f);
                Assert.That(ball.GrowthStageProgress01, Is.InRange(0f, 0.001f));

                ball.ServerApplyMass(ball.MassMmForRadius(
                    SnowballStageModel.Stage1StartRadiusM) + 1L);
                Assert.AreEqual(ESnowBallGrowthStage.Stage1, ball.GrowthStage);
                Assert.That(ball.GrowthStageProgress01, Is.InRange(0f, 0.01f));
                Assert.Greater(ball.NextGrowthTargetDiameterM, ball.DiameterM);

                ball.ServerApplyMass(ball.MassMmForRadius(
                    SnowballStageModel.Stage2StartRadiusM) + 1L);
                Assert.AreEqual(ESnowBallGrowthStage.Stage2, ball.GrowthStage);
                Assert.That(ball.GrowthStageProgress01, Is.InRange(0f, 0.01f));
                Assert.Greater(ball.NextGrowthTargetDiameterM, ball.DiameterM);

                ball.ServerApplyMass(ball.VisibleMaxMassMm);
                Assert.AreEqual(ESnowBallGrowthStage.Stage4, ball.GrowthStage);
                Assert.IsTrue(ball.IsVisibleGrowthComplete);
                Assert.AreEqual(1f, ball.GrowthStageProgress01, 0.001f);
                Assert.AreEqual(0f, ball.RemainingDiameterToNextGrowthTargetM, 0.001f);
            }
            finally
            {
                Object.DestroyImmediate(ball.gameObject);
            }
        }

        [Test]
        public void MassZeroBall_StillHasAFootprint()
        {
            // 반지름 0 이면 걷을 셀이 없어 영원히 0 이다. 씨앗 반지름이 그 교착을 푼다.
            Assert.AreEqual(SnowBallCpu.SeedRadiusM, Ball(Field()).RadiusM);
        }

        [Test]
        public void Rolling_GrowsTheBallAndTakesItFromTheField()
        {
            var f = Field(300);
            var ball = Ball(f);
            long before = f.TotalHeightMm;

            long cut = ball.Harvest(4f, 12f, 9f, 12f);

            Assert.Greater(cut, 0, "처녀설을 5 m 굴렀으면 걷은 것이 있어야 한다");
            Assert.AreEqual(cut, ball.MassMm, "공이 든 양은 걷은 양과 같다");
            Assert.AreEqual(before - f.TotalHeightMm, ball.MassMm, "필드에서 빠진 양과 공이 든 양이 같다");
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm, "증분 원장이 갈라지지 않았다");
            Assert.Greater(ball.RadiusM, SnowBallCpu.SeedRadiusM, "질량이 늘면 반지름이 늘어야 한다");
        }

        [Test]
        public void StandingStill_HarvestsNothing()
        {
            var f = Field(300);
            var ball = Ball(f);
            long before = f.TotalHeightMm;

            Assert.AreEqual(0, ball.Harvest(8f, 8f, 8f, 8f), "제자리에서 커지면 서서 무한정 자란다");
            Assert.AreEqual(before, f.TotalHeightMm);
        }

        /// <summary>
        /// 한 번의 호출이 몇 미터를 덮어도 <b>줄무늬가 남지 않아야 한다.</b> 세그먼트 상한을 넘는 거리를
        /// 한 스윕으로 넘기면 세그먼트 사이가 벌어져 안 걷은 셀이 남는다 — 경사를 굴러 내려가는 공은
        /// 한 틱에 몇 미터를 가므로 이것이 실전 결함이다(첫 구현이 여기서 걸렸다).
        /// </summary>
        [Test]
        public void Harvest_LeavesNoStripes_EvenWhenItCoversMetresInOneCall()
        {
            var f = Field(300);
            var ball = Ball(f, residueMm: 0);

            ball.Harvest(4f, 12f, 9f, 12f);

            for (float x = 4.2f; x <= 8.8f; x += 0.125f)
            {
                Assert.IsTrue(f.Geo.TryWorldToCell(x, 12f, out int cx, out int cz));
                Assert.AreEqual(0, f.Get(cx, cz), $"x={x:F3} 에 걷지 않은 줄무늬가 남았다");
            }
        }

        [Test]
        public void SecondPassOverTheSameLane_DoesNotYieldTheSameSnowTwice()
        {
            var f = Field(300);
            var ball = Ball(f, residueMm: 0);
            long initial = f.TotalHeightMm;

            ball.Harvest(4f, 12f, 9f, 12f);
            ball.Harvest(4f, 12f, 9f, 12f);

            Assert.IsTrue(f.Geo.TryWorldToCell(6.5f, 12f, out int cx, out int cz));
            Assert.AreEqual(0, f.Get(cx, cz), "이미 맨땅인 셀은 두 번째에도 맨땅이다");
            Assert.AreEqual(initial - f.TotalHeightMm, ball.MassMm,
                            "두 번 지나가도 필드 + 공 = 초기");
        }

        [Test]
        public void Residue_StillControlsHowMuchSnowIsActuallyHarvested()
        {
            var fast = Field(300);
            var slow = Field(300);

            long fastCut = Ball(fast, residueMm: 0).Harvest(4f, 12f, 9f, 12f);
            long slowCut = Ball(slow, residueMm: 250).Harvest(4f, 12f, 9f, 12f);

            Assert.Greater(fastCut, slowCut, "잔량을 남기면 같은 거리에서 덜 걷는다");

            Assert.IsTrue(slow.Geo.TryWorldToCell(6.5f, 12f, out int cx, out int cz));
            Assert.AreEqual(250, slow.Get(cx, cz), "지나간 셀에 잔량이 정확히 남는다");
            Assert.AreEqual(0, fast.Get(cx, cz), "잔량 0 이면 맨땅이 된다");
        }

        [Test]
        public void GrowthWeight_OnlyCreditsItsShareOfTheHarvestedSnow()
        {
            var normalField = Field(300);
            var slowField = Field(300);
            var normal = new SnowBallCpu(normalField, 0, SnowBallCpu.GrowthWeightScale);
            var slow = new SnowBallCpu(slowField, 0, SnowBallCpu.DefaultGrowthWeightPermille);

            long normalCut = normal.Gather(12f, 12f, 1f, 0);
            long slowCut = slow.Gather(12f, 12f, 1f, 0);

            Assert.AreEqual(normalCut, slowCut, "같은 패치에서 필드가 줄어드는 양은 같아야 한다");
            Assert.AreEqual(normalCut, normal.MassMm, "가중치 1은 걷은 양 전부를 가져간다");
            Assert.AreEqual(slowCut / 2, slow.MassMm, "가중치 0.5는 걷은 양의 절반만 가져간다");
            Assert.Less(slow.RadiusM, normal.RadiusM, "적게 가져온 질량만큼 성장도 느려야 한다");
            Assert.Greater(Field(300).TotalHeightMm - slowField.TotalHeightMm, slow.MassMm,
                "가중치로 제외한 양은 공의 질량 장부에 남기지 않는다");
            Assert.AreEqual(slowField.RecomputeTotalHeightMm(), slowField.TotalHeightMm);
        }

        [Test]
        public void VisibleMaximum_KeepsHarvestingAsCompressedMass()
        {
            var f = Field(300);
            var ball = new SnowBallCpu(f, 0, SnowBallCpu.DefaultGrowthWeightPermille);
            long initial = f.TotalHeightMm;

            for (float z = 2f; z <= 22f && !ball.IsOverSizeThreshold; z += 1f)
            {
                ball.Harvest(2f, z, 22f, z);
            }

            Assert.IsTrue(ball.IsOverSizeThreshold, "충분한 처녀설을 지나면 보이는 크기 상한에 도달해야 한다");
            long massAtThresholdMm = ball.MassMm;
            long extraCutMm = ball.Harvest(2f, 23f, 22f, 23f);

            Assert.Greater(extraCutMm, 0, "1.5 m 이후에도 눈을 계속 수확해야 한다");
            Assert.Greater(ball.MassMm, massAtThresholdMm, "초과 수확량이 압축 질량으로 누적되지 않았다");
            Assert.AreEqual(SnowBallCpu.MaxRadiusM, ball.RadiusM, 0.0001f);
            Assert.Greater(ball.EquivalentRadiusM, SnowBallCpu.MaxRadiusM,
                "초과 질량을 환산한 반지름은 1.5 m보다 커야 한다");

            long removedMm = initial - f.TotalHeightMm;
            Assert.Greater(removedMm, ball.MassMm, "가중치에서 제외된 양은 공 질량에 들어가지 않는다");
            Assert.AreEqual(ball.MassMm, removedMm / 2, 200,
                "전체 수확량의 약 절반이 공 질량이어야 한다");
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Burst_PutsAGiantBallBackWithoutLoss()
        {
            // 터짐은 놓기와 같은 경로다 - 링 수만 크다. 큰 공에서도 잔량이 남지 않는 것이 터뜨려도
            // 되는 근거이고, 남는다면 터짐이 눈을 먹는다는 뜻이다.
            var f = Field(300);
            var ball = Ball(f);
            long initial = f.TotalHeightMm;

            ball.Harvest(2f, 12f, 22f, 12f);
            ball.Harvest(2f, 8f, 22f, 8f);
            ball.Harvest(2f, 16f, 22f, 16f);
            Assert.Greater(ball.RadiusM, 1f, "세 줄을 굴렸으면 큰 공이다");

            long unplaced = ball.Release(12f, 12f, capMm: 1600, spreadRings: 24);

            Assert.AreEqual(0, unplaced, "마지막 패스가 상한을 무시하므로 잔량이 남을 이유가 없다");
            Assert.AreEqual(0, ball.MassMm, "터졌으면 공은 비어 있다");
            Assert.AreEqual(initial, f.TotalHeightMm, "필드 + 공 = 초기");
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Release_PutsEverythingBack()
        {
            var f = Field(300);
            var ball = Ball(f);
            long initial = f.TotalHeightMm;

            ball.Harvest(4f, 12f, 9f, 12f);
            Assert.Greater(ball.MassMm, 0);

            long unplaced = ball.Release(9f, 12f, capMm: 2000, spreadRings: 2);

            Assert.AreEqual(0, unplaced, "마지막 패스는 상한을 무시하므로 잔량이 남을 이유가 없다");
            Assert.AreEqual(0, ball.MassMm, "놓았으면 공은 비어 있다");
            Assert.AreEqual(initial, f.TotalHeightMm, "필드 + 공 = 초기");
            Assert.AreEqual(f.RecomputeTotalHeightMm(), f.TotalHeightMm);
        }

        [Test]
        public void Release_KeepsWhatItCouldNotPlace()
        {
            var f = Field(300);
            var ball = Ball(f);
            ball.Harvest(4f, 12f, 9f, 12f);

            // 필드 밖에 놓으라고 하면 놓을 셀이 없다. 그때 질량을 버리면 그것이 누출이다.
            long unplaced = ball.Release(-500f, -500f, capMm: 2000, spreadRings: 0);

            Assert.Greater(unplaced, 0);
            Assert.AreEqual(unplaced, ball.MassMm, "놓지 못한 양은 공에 남는다");
        }

        [Test]
        public void RadiusAndMass_RoundTrip()
        {
            long mass = SnowBallCpu.MassMmForRadius(0.75f);
            Assert.AreEqual(0.75f, SnowBallCpu.RadiusFromMassMm(mass), 0.01f);
        }

        [Test]
        public void GrowthWeight_DoesNotAlterTheMassToRadiusFormula()
        {
            long mass = SnowBallCpu.MassMmForRadius(1.25f);
            Assert.AreEqual(1.25f, SnowBallCpu.RadiusFromMassMm(mass), 0.01f);
        }

        [Test]
        public void MeasuredScale_OneMetreBallIsAboutFiveHundredLitres()
        {
            // 설계 문서의 수치를 코드가 지키는지 본다 — 지름 1 m = 524 L.
            long mass = SnowBallCpu.MassMmForRadius(0.5f);
            double litres = mass * 1e-3 * SnowBallCpu.CellAreaM2 * 1000.0;
            Assert.AreEqual(524.0, litres, 2.0);
        }

        private static int CellX(SnowHeightFieldCpu f, float worldX)
        {
            f.Geo.TryWorldToCell(worldX, 0f, out int cx, out _);
            return cx;
        }

        private static int CellZ(SnowHeightFieldCpu f, float worldZ)
        {
            f.Geo.TryWorldToCell(0f, worldZ, out _, out int cz);
            return cz;
        }
    }
}
