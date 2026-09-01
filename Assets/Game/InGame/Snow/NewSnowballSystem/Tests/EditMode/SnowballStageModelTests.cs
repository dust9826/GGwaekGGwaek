using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class SnowballStageModelTests
    {
        [Test]
        public void 반지름_경계를_네_구간으로_정확히_나눈다()
        {
            const float epsilon = 0.0001f;

            Assert.AreEqual(0.51f, SnowballStageModel.Stage1StartRadiusM, epsilon);
            Assert.AreEqual(0.84f, SnowballStageModel.Stage2StartRadiusM, epsilon);
            Assert.AreEqual(1.17f, SnowballStageModel.Stage3StartRadiusM, epsilon);
            Assert.AreEqual(1.5f, SnowballStageModel.Stage4StartRadiusM, epsilon);

            Assert.AreEqual(ESnowBallGrowthStage.Seed,
                SnowballStageModel.GetStage(SnowballStageModel.Stage1StartRadiusM - epsilon));
            Assert.AreEqual(ESnowBallGrowthStage.Stage1,
                SnowballStageModel.GetStage(SnowballStageModel.Stage1StartRadiusM));
            Assert.AreEqual(ESnowBallGrowthStage.Stage1,
                SnowballStageModel.GetStage(SnowballStageModel.Stage2StartRadiusM - epsilon));
            Assert.AreEqual(ESnowBallGrowthStage.Stage2,
                SnowballStageModel.GetStage(SnowballStageModel.Stage2StartRadiusM));
            Assert.AreEqual(ESnowBallGrowthStage.Stage2,
                SnowballStageModel.GetStage(SnowballStageModel.Stage3StartRadiusM - epsilon));
            Assert.AreEqual(ESnowBallGrowthStage.Stage3,
                SnowballStageModel.GetStage(SnowballStageModel.Stage3StartRadiusM));
            Assert.AreEqual(ESnowBallGrowthStage.Stage3,
                SnowballStageModel.GetStage(SnowballStageModel.Stage4StartRadiusM - epsilon));
            Assert.AreEqual(ESnowBallGrowthStage.Stage4,
                SnowballStageModel.GetStage(SnowballStageModel.Stage4StartRadiusM));
        }

        [Test]
        public void 진행도는_각_단계에서_영부터_일로_다시_채워진다()
        {
            SnowballStageModel.GetStageRange(ESnowBallGrowthStage.Stage2,
                out float startRadiusM, out float endRadiusM);

            Assert.AreEqual(0f, SnowballStageModel.GetStageProgress01(startRadiusM), 0.0001f);
            Assert.AreEqual(0.5f,
                SnowballStageModel.GetStageProgress01((startRadiusM + endRadiusM) * 0.5f),
                0.0001f);
            Assert.AreEqual(1f,
                SnowballStageModel.GetStageProgress01(SnowballStageModel.MaxRadiusM),
                0.0001f);
        }

        [Test]
        public void 단계별_대표_반지름은_각_구간의_중앙이다()
        {
            for (int value = 0; value <= SnowballStageModel.StageCount; value++)
            {
                var stage = (ESnowBallGrowthStage)value;
                SnowballStageModel.GetStageRange(stage, out float startRadiusM,
                    out float endRadiusM);
                float representative = SnowballStageModel.GetStageRepresentativeRadius(stage);

                Assert.AreEqual((startRadiusM + endRadiusM) * 0.5f, representative, 0.0001f);
                Assert.AreEqual(stage, SnowballStageModel.GetStage(representative));
            }
        }

        [Test]
        public void 조작용_유효질량은_균일밀도_구체로_영점오일팔사에서_삼백킬로그램까지_증가한다()
        {
            Assert.AreEqual(0.5184f,
                SnowballStageModel.GetEffectiveHandlingMassKg(SnowballStageModel.MinRadiusM),
                0.0001f);
            Assert.AreEqual(300f,
                SnowballStageModel.GetEffectiveHandlingMassKg(SnowballStageModel.MaxRadiusM),
                0.001f);

            float previousMassKg = 0f;
            for (int value = 0; value <= SnowballStageModel.StageCount; value++)
            {
                float radiusM = SnowballStageModel.GetStageRepresentativeRadius(
                    (ESnowBallGrowthStage)value);
                float massKg = SnowballStageModel.GetEffectiveHandlingMassKg(radiusM);
                Assert.Greater(massKg, previousMassKg);
                previousMassKg = massKg;
            }
        }

        [Test]
        public void 모든_승급의_기본_활성수확시간은_칠초다()
        {
            Assert.AreEqual(7f, SnowballStageModel.DefaultStageDurationSeconds, 0.0001f);
        }

        [Test]
        public void 단계_필요량은_삼백밀리미터_처녀설과_기준시간으로_고정된다()
        {
            long sevenSeconds = SnowballStageModel.CalculateRequiredHarvestMm(
                ESnowBallGrowthStage.Stage2, 7f, 4.1f,
                SnowballStageModel.DefaultContinuousGrowthShare);
            long fourteenSeconds = SnowballStageModel.CalculateRequiredHarvestMm(
                ESnowBallGrowthStage.Stage2, 14f, 4.1f,
                SnowballStageModel.DefaultContinuousGrowthShare);
            long halfDepth = SnowballStageModel.CalculateRequiredHarvestMm(
                ESnowBallGrowthStage.Stage2, 7f, 4.1f,
                SnowballStageModel.DefaultContinuousGrowthShare, 150);

            Assert.LessOrEqual(System.Math.Abs(sevenSeconds * 2L - fourteenSeconds), 1L);
            Assert.LessOrEqual(System.Math.Abs(sevenSeconds / 2L - halfDepth), 1L);
        }

        [Test]
        public void 표시크기와_콜라이더는_모든_단계에서_실제반지름을_따른다()
        {
            GameObject root = new GameObject("__TEST__SnowballGrowthPrototype");
            try
            {
                GameObject sizePivotObject = new GameObject("StageSizePivot");
                sizePivotObject.transform.SetParent(root.transform, false);
                GameObject feedbackPivotObject = new GameObject("StagePopFeedbackPivot");
                feedbackPivotObject.transform.SetParent(sizePivotObject.transform, false);
                SphereCollider sphereCollider = root.AddComponent<SphereCollider>();
                Rigidbody body = root.AddComponent<Rigidbody>();
                body.isKinematic = true;
                body.useGravity = false;
                SnowballStagePrototypeActor actor = root.AddComponent<SnowballStagePrototypeActor>();
                actor.Configure(sizePivotObject.transform, feedbackPivotObject.transform,
                    sphereCollider, body, null);
                SnowballStageModel.GetStageRange(ESnowBallGrowthStage.Seed,
                    out float startRadiusM, out float endRadiusM);
                float rawRadiusM = Mathf.Lerp(startRadiusM, endRadiusM, 0.75f);
                actor.SetRawRadiusM(rawRadiusM, false);

                Assert.AreEqual(rawRadiusM, actor.DisplayRadiusM, 0.0001f);
                Assert.AreEqual(rawRadiusM, sphereCollider.radius, 0.0001f);

                actor.SetRawRadiusM(endRadiusM - 0.0001f, false);
                actor.SetRawRadiusM(endRadiusM, false);

                Assert.AreEqual(ESnowBallGrowthStage.Stage1, actor.GrowthStage);
                Assert.AreEqual(endRadiusM, actor.DisplayRadiusM, 0.0001f);
                Assert.AreEqual(endRadiusM, sphereCollider.radius, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void 사단계에_진입하면_HUD_진행도는_가득_찬다()
        {
            GameObject root = new GameObject("__TEST__SnowballGrowthPrototype");
            try
            {
                SnowballStagePrototypeActor actor = root.AddComponent<SnowballStagePrototypeActor>();
                actor.SetRawRadiusM(SnowballStageModel.Stage4StartRadiusM, false);

                Assert.AreEqual(ESnowBallGrowthStage.Stage4, actor.GrowthStage);
                Assert.AreEqual(1f, actor.StageProgress01, 0.0001f);
                Assert.AreEqual(SnowballStageModel.MaxRadiusM, actor.DisplayRadiusM, 0.0001f);
            }
            finally
            {
                Object.DestroyImmediate(root);
            }
        }

        [Test]
        public void 제어반지름_단일스윕의_공_지름과_설면_제거폭은_한셀_오차안에서_같다()
        {
            var geometry = new SnowFieldGeometry(8f, 8f, -4f, -4f);
            var field = new SnowHeightFieldCpu(geometry, 300);
            const float radiusM = 0.84f;
            var ball = new SnowBallCpu(field, 0,
                SnowBallCpu.DefaultGrowthWeightPermille);

            long cutMm = ball.Harvest(-1f, 0f, 1f, 0f, radiusM);

            Assert.Greater(cutMm, 0L);
            float minimumZ = float.PositiveInfinity;
            float maximumZ = float.NegativeInfinity;
            for (int cz = 0; cz < geometry.ResZ; cz++)
            for (int cx = 0; cx < geometry.ResX; cx++)
            {
                geometry.CellCenterWorld(cx, cz, out float worldX, out float worldZ);
                if (Mathf.Abs(worldX) > 0.5f || field.Get(cx, cz) != 0) continue;
                minimumZ = Mathf.Min(minimumZ, worldZ);
                maximumZ = Mathf.Max(maximumZ, worldZ);
            }

            float removedWidthM = maximumZ - minimumZ + SnowFieldGeometry.CellSizeM;
            Assert.AreEqual(radiusM * 2f, removedWidthM,
                SnowFieldGeometry.CellSizeM,
                "격자 한 칸의 양자화보다 큰 폭 차이가 생기면 안 된다");
            Assert.LessOrEqual(System.Math.Abs(cutMm / 2L - ball.MassMm), 1L,
                "실제 제거량은 단계 진행에 쓰되 내부 취득 질량은 0.5 가중치를 유지해야 한다");
        }

        [Test]
        public void 같은경로의_실제잔설량만큼만_연속해서_수확한다()
        {
            var geometry = new SnowFieldGeometry(8f, 8f, -4f, -4f);
            var field = new SnowHeightFieldCpu(geometry, 300);
            var ball = new SnowBallCpu(field, 0,
                SnowBallCpu.DefaultGrowthWeightPermille);

            long firstCutMm = ball.Harvest(-1f, 0f, 1f, 0f, 0.84f);
            long secondCutMm = ball.Harvest(1f, 0f, -1f, 0f, 0.84f);

            Assert.Greater(firstCutMm, 0L);
            Assert.AreEqual(0L, secondCutMm,
                "첫 스윕에서 실제 눈을 모두 제거한 경로는 다시 지나도 성장량을 만들면 안 된다");

            var footprintField = new SnowHeightFieldCpu(geometry, 300);
            var footprint = new SnowFootprintCpu();
            footprint.Step(footprintField, -1f, 0f, 0.84f, true);
            footprint.Step(footprintField, 1f, 0f, 0.84f, true);
            var ballAfterFootprint = new SnowBallCpu(footprintField, 0,
                SnowBallCpu.DefaultGrowthWeightPermille);

            long afterFootprintCutMm = ballAfterFootprint.Harvest(
                -1f, 0f, 1f, 0f, 0.84f);

            Assert.Greater(afterFootprintCutMm, 0L);
            Assert.Less(afterFootprintCutMm, firstCutMm,
                "발자국으로 먼저 사라진 50 mm를 300 mm 처녀설처럼 다시 수확하면 안 된다");
        }

    }
}
