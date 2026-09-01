using System.Collections;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class SnowballGrowthPrototypeTests
    {
        [UnityTest]
        public IEnumerator 표시크기는_단계안과_경계에서_실제반지름을_연속해서_따른다()
        {
            GameObject root = new GameObject("__TEST__SnowballGrowthPrototype");
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
            actor.SetRawRadiusM(Mathf.Lerp(startRadiusM, endRadiusM, 0.25f), false);
            yield return null;
            float earlyDisplayRadiusM = actor.DisplayRadiusM;

            actor.SetRawRadiusM(Mathf.Lerp(startRadiusM, endRadiusM, 0.75f));
            yield return null;
            Assert.Greater(actor.DisplayRadiusM, earlyDisplayRadiusM);
            Assert.AreEqual(actor.RawRadiusM, actor.DisplayRadiusM, 0.0001f);
            Assert.AreEqual(0, actor.FeedbackPlayCount);

            actor.SetRawRadiusM(endRadiusM - 0.0001f, false);
            yield return null;
            float beforeStageUpRadiusM = actor.DisplayRadiusM;

            actor.SetRawRadiusM(endRadiusM, false);
            yield return null;
            Assert.AreEqual(ESnowBallGrowthStage.Stage1, actor.GrowthStage);
            Assert.AreEqual(endRadiusM, actor.DisplayRadiusM, 0.0001f);
            Assert.Greater(actor.DisplayRadiusM, beforeStageUpRadiusM);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 실제_눈덩이의_렌더표시는_물리반지름을_한틱안에_보간한다()
        {
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__PlayableSnowballGrowth";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.MassMmForRadius(0.49f));

            SnowballGrowthPlayablePresentation presentation =
                root.AddComponent<SnowballGrowthPlayablePresentation>();
            presentation.Initialize(carrier);
            yield return null;

            Assert.AreEqual(0.49f, carrier.RadiusM, 0.01f);
            Assert.AreEqual(ESnowBallGrowthStage.Seed, presentation.GrowthStage);
            Assert.AreEqual(carrier.RadiusM, presentation.DisplayRadiusM, 0.001f);
            Assert.IsFalse(root.GetComponent<MeshRenderer>().enabled);

            float previousDisplayRadiusM = presentation.DisplayRadiusM;
            carrier.ServerApplyMass(carrier.MassMmForRadius(0.60f));
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(ESnowBallGrowthStage.Stage1, presentation.GrowthStage);
            Assert.That(presentation.DisplayRadiusM,
                Is.InRange(previousDisplayRadiusM, carrier.RadiusM));
            Assert.AreEqual(1, presentation.FeedbackPlayCount);
            Transform correction = root.transform.Find("StageSizeCorrectionPivot");
            float visualRadiusM = root.transform.lossyScale.x * correction.localScale.x * 0.5f;
            Assert.AreEqual(presentation.DisplayRadiusM, visualRadiusM, 0.001f);

            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreEqual(carrier.RadiusM, presentation.DisplayRadiusM, 0.001f,
                "렌더 보간은 한 물리 틱 이상 실제 반지름보다 뒤처지면 안 된다");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 실제_생성반지름은_Seed_HUD의_영퍼센트가_된다()
        {
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__SnowballGrowthStageTimer";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.MassMmForRadius(0.33f));

            SnowballGrowthStageTimer timer = root.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(carrier);
            SnowballGrowthPlayablePresentation presentation =
                root.AddComponent<SnowballGrowthPlayablePresentation>();
            presentation.ConfigureStageTimer(timer);
            presentation.Initialize(carrier);
            yield return null;

            Assert.AreEqual(ESnowBallGrowthStage.Seed, timer.Stage);
            Assert.AreEqual(0f, presentation.StageProgress01, 0.0001f);
            Assert.AreEqual(carrier.RadiusM, presentation.DisplayRadiusM, 0.001f);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 실제수확량이_필요량을_채워야_승급한다()
        {
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__SnowballVariableHarvestTimer";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.MassMmForRadius(SnowballStageModel.MinRadiusM));

            SnowballGrowthStageTimer timer = root.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(carrier);
            timer.ConfigureDuration(0.1f);
            long requiredMm = timer.RequiredHarvestMm;
            timer.RecordHarvestedSnow(requiredMm / 4L);
            yield return new WaitForFixedUpdate();

            Assert.That(timer.StageProgress01, Is.InRange(0.24f, 0.26f));
            Assert.AreEqual(ESnowBallGrowthStage.Seed, timer.Stage);

            timer.RecordHarvestedSnow(requiredMm - timer.AccumulatedHarvestMm);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(ESnowBallGrowthStage.Stage1, timer.Stage,
                "실제 제거량이 고정 필요량을 채웠을 때만 승급해야 한다");
            Assert.AreEqual(0L, timer.AccumulatedHarvestMm);

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 한틱만_눈을_수확하면_눈이_없는_후속틱에는_성장이_멈춘다()
        {
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__SnowballNoSnowGrowth";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.MassMmForRadius(SnowballStageModel.MinRadiusM));

            SnowballGrowthStageTimer timer = root.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(carrier);
            timer.ConfigureDuration(0.1f);
            long harvestedMm = timer.RequiredHarvestMm / 5L;
            timer.RecordHarvestedSnow(harvestedMm);
            yield return new WaitForFixedUpdate();

            long accumulatedAfterHarvestMm = timer.AccumulatedHarvestMm;
            float radiusAfterHarvestM = timer.ControlledRadiusM;
            Assert.Greater(accumulatedAfterHarvestMm, 0L);

            for (int step = 0; step < 10; step++)
            {
                yield return new WaitForFixedUpdate();
            }

            Assert.AreEqual(ESnowBallGrowthStage.Seed, timer.Stage);
            Assert.AreEqual(accumulatedAfterHarvestMm, timer.AccumulatedHarvestMm,
                "실제 제거량이 없는 틱에는 누적량이 늘면 안 된다");
            Assert.AreEqual(radiusAfterHarvestM, timer.ControlledRadiusM, 0.0001f,
                "눈이 없는 구간에서 제어 반지름이 계속 커지면 안 된다");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 승급전에는_일부만_연속성장하고_승급순간_실제반지름이_점프한다()
        {
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__SnowballPhysicalStagePop";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.MassMmForRadius(SnowballStageModel.MinRadiusM));
            root.GetComponent<Rigidbody>().useGravity = false;

            SnowballGrowthStageTimer timer = root.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(carrier);
            timer.ConfigureDuration(0.1f);
            timer.ConfigureContinuousGrowthShare(
                SnowballStageModel.DefaultContinuousGrowthShare);
            SnowballGrowthPlayablePresentation presentation =
                root.AddComponent<SnowballGrowthPlayablePresentation>();
            presentation.ConfigureStageTimer(timer);
            presentation.Initialize(carrier);
            float initialContactY = root.transform.position.y - carrier.RadiusM;
            long requiredMm = timer.RequiredHarvestMm;

            for (int step = 0; step < 4; step++)
            {
                timer.RecordHarvestedSnow(requiredMm / 5L);
                yield return new WaitForFixedUpdate();
            }

            float beforeStageUpRadiusM = carrier.RadiusM;
            float continuousEndRadiusM = Mathf.Lerp(SnowballStageModel.MinRadiusM,
                SnowballStageModel.Stage1StartRadiusM,
                SnowballStageModel.DefaultContinuousGrowthShare);
            Assert.Less(beforeStageUpRadiusM, continuousEndRadiusM + 0.001f);
            Assert.That(timer.StageProgress01, Is.InRange(0.79f, 0.81f));

            timer.RecordHarvestedSnow(requiredMm - timer.AccumulatedHarvestMm);
            yield return new WaitForFixedUpdate();
            yield return null;

            Assert.AreEqual(ESnowBallGrowthStage.Stage1, timer.Stage);
            Assert.Greater(carrier.RadiusM - beforeStageUpRadiusM, 0.18f,
                "Feel 스케일만이 아니라 실제 물리 반지름이 승급 순간 커져야 한다");
            Assert.AreEqual(SnowballStageModel.Stage1StartRadiusM,
                carrier.RadiusM, 0.001f);
            Assert.AreEqual(initialContactY,
                root.transform.position.y - carrier.RadiusM, 0.001f,
                "반지름 성장분만큼 중심을 올려 지면 접점이 파고들지 않아야 한다");
            Assert.AreEqual(1, presentation.FeedbackPlayCount);

            yield return new WaitForFixedUpdate();
            yield return null;
            Assert.AreEqual(carrier.RadiusM, presentation.DisplayRadiusM, 0.001f,
                "승급 점프도 한 물리 틱 안에서 실제 크기에 도달해야 한다");

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        public IEnumerator 승급을_넘긴_수확량은_다음단계로_이월된다()
        {
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__SnowballHarvestOverflow";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.MassMmForRadius(SnowballStageModel.MinRadiusM));

            SnowballGrowthStageTimer timer = root.AddComponent<SnowballGrowthStageTimer>();
            timer.Initialize(carrier);
            timer.ConfigureDuration(0.1f);
            long seedRequirementMm = timer.RequiredHarvestMm;
            long stage1RequirementMm = SnowballStageModel.CalculateRequiredHarvestMm(
                ESnowBallGrowthStage.Stage1, 0.1f,
                SnowballStageModel.GetDefaultReferenceSpeedMps(
                    ESnowBallGrowthStage.Stage1),
                SnowballStageModel.DefaultContinuousGrowthShare);
            timer.RecordHarvestedSnow(seedRequirementMm + stage1RequirementMm / 2L);
            yield return new WaitForFixedUpdate();

            Assert.AreEqual(ESnowBallGrowthStage.Stage1, timer.Stage);
            Assert.That(timer.StageProgress01, Is.InRange(0.49f, 0.51f));

            Object.Destroy(root);
            yield return null;
        }

        [UnityTest]
        [Ignore("함께 밀기는 2026-09-01 에 껐다 — SnowBallCarrier.CoopPushEnabled. 되살리면 이 줄만 지우면 된다.")]
        public IEnumerator 격리씬은_기존_우클릭_협동타이밍과_보너스를_즉시_취소한다()
        {
            GameObject ground = GameObject.CreatePrimitive(PrimitiveType.Cube);
            ground.name = "__TEST__CoopTimingGround";
            ground.transform.position = new Vector3(0f, -0.5f, 0f);
            ground.transform.localScale = new Vector3(20f, 1f, 20f);

            GameObject participant = new GameObject("__TEST__CoopTimingParticipant");
            GameObject prefab = Resources.Load<GameObject>("PF_SnowBall");
            Assert.IsNotNull(prefab);
            GameObject root = Object.Instantiate(prefab);
            root.name = "__TEST__CoopTimingBall";
            SnowBallCarrier carrier = root.GetComponent<SnowBallCarrier>();
            carrier.ServerApplyMass(carrier.VisibleMaxMassMm);
            root.transform.position = new Vector3(0f, carrier.RadiusM + 0.02f, 0f);

            try
            {
                bool timingOpened = false;
                for (int step = 0; step < 120; step++)
                {
                    carrier.SubmitMomentumPush(participant.transform,
                        Vector3.forward * 8f, 1f);
                    yield return new WaitForFixedUpdate();
                    if (!carrier.TryGetCoopTiming(participant.transform,
                            out _, out _, out _)) continue;
                    timingOpened = true;
                    break;
                }

                Assert.IsTrue(timingOpened, "검증용으로 기존 협동 타이밍을 열지 못했다");
                int boostCount = carrier.CoopBoostCount;
                MethodInfo cancel = typeof(SnowballGrowthPlayableSceneController).GetMethod(
                    "CancelLegacyCoopTiming", BindingFlags.Static | BindingFlags.NonPublic);
                Assert.IsNotNull(cancel);
                cancel.Invoke(null, new object[] { carrier, participant.transform });

                Assert.IsFalse(carrier.TryGetCoopTiming(participant.transform,
                    out _, out _, out _));
                Assert.AreEqual(boostCount, carrier.CoopBoostCount,
                    "타이밍을 없애면서 협동 보너스까지 지급하면 안 된다");
            }
            finally
            {
                Object.DestroyImmediate(root);
                Object.DestroyImmediate(participant);
                Object.DestroyImmediate(ground);
            }

            yield return null;
        }
    }
}
