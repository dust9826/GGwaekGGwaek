using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class BlizzardEventPlayModeTests
    {
        private readonly List<GameObject> _owned = new List<GameObject>();

        [TearDown]
        public void TearDown()
        {
            for (int index = _owned.Count - 1; index >= 0; index--)
            {
                if (_owned[index] != null) UnityEngine.Object.DestroyImmediate(_owned[index]);
            }
            _owned.Clear();
        }

        [Test]
        public void 경로는_초기눈에서_현재눈을_뺀_합이_큰_두_구역을_지난다()
        {
            var geo = new SnowFieldGeometry(24f, 16f, 0f, 0f, 0f);
            var field = new SnowHeightFieldCpu(geo, 300);
            RemoveDisc(field, new Vector2(5f, 8f), 2.5f, 260);
            RemoveDisc(field, new Vector2(17f, 8f), 1.8f, 220);

            bool planned = BlizzardRoutePlanner.TryPlan(
                field, 300, 1.5f, 0.5f, 6f, 11, out BlizzardRoutePlan route);

            Assert.IsTrue(planned);
            Assert.That(Vector2.Distance(route.Start, new Vector2(5f, 8f)), Is.LessThan(1.2f));
            Assert.That(Vector2.Distance(route.SecondRegion, new Vector2(17f, 8f)), Is.LessThan(1.2f));
            Assert.That(Vector2.Dot(route.Direction, (route.SecondRegion - route.Start).normalized),
                Is.GreaterThan(0.999f));

            Vector2 exit = route.Start + route.Direction * route.TravelDistance;
            Assert.That(exit.x, Is.GreaterThan(geo.OriginXM + geo.ResX * SnowFieldGeometry.CellSizeM));
        }

        [Test]
        public void 같은_구역에_옮겨_쌓은_눈은_사용량을_상쇄한다()
        {
            var geo = new SnowFieldGeometry(24f, 16f, 0f, 0f, 0f);
            var field = new SnowHeightFieldCpu(geo, 300);

            RemoveDisc(field, new Vector2(5f, 8f), 2f, 180);
            AddDisc(field, new Vector2(5f, 8f), 1f, 720);
            RemoveDisc(field, new Vector2(17f, 8f), 2f, 120);
            RemoveDisc(field, new Vector2(11f, 8f), 1.5f, 80);

            bool planned = BlizzardRoutePlanner.TryPlan(
                field, 300, 1.5f, 0.5f, 5f, 17, out BlizzardRoutePlan route);

            Assert.IsTrue(planned);
            Assert.That(Vector2.Distance(route.Start, new Vector2(17f, 8f)), Is.LessThan(1.2f),
                "옮겨 쌓은 눈을 무시하면 왼쪽 구역이 잘못 1순위가 된다.");
        }

        [UnityTest]
        public IEnumerator 눈폭풍은_한_셀에_120만_더하고_300을_넘기지_않는다()
        {
            SnowCpuStage stage = CreateStage(8f, 8f, 0);
            yield return null;
            Assert.IsNotNull(stage.Field);

            byte[] exposure = new byte[stage.Field.HeightMm.Length];
            var start = new Vector2(1f, 2f);
            var end = new Vector2(6f, 2f);
            stage.ApplyBlizzardSweep(start, end, 0.5f, 1f, 120, 300, 7, exposure);

            int centerCell = CellAt(stage.Field.Geo, 3f, 2f);
            Assert.AreEqual(120, stage.Field.GetAt(centerCell));

            stage.ApplyBlizzardSweep(start, end, 0.5f, 1f, 120, 300, 7, exposure);
            Assert.AreEqual(120, stage.Field.GetAt(centerCell), "겹치는 스윕이 같은 이벤트의 120 mm 예산을 다시 쓰면 안 된다.");

            int cappedCell = CellAt(stage.Field.Geo, 3f, 5f);
            stage.Field.Set(cappedCell % stage.Field.Geo.ResX, cappedCell / stage.Field.Geo.ResX, 250);
            byte[] secondExposure = new byte[stage.Field.HeightMm.Length];
            stage.ApplyBlizzardSweep(new Vector2(1f, 5f), new Vector2(6f, 5f),
                0.5f, 1f, 120, 300, 9, secondExposure);
            Assert.AreEqual(300, stage.Field.GetAt(cappedCell));
        }

        [UnityTest]
        public IEnumerator 눈폭풍은_경고_이동_회복을_거쳐_직선_눈길을_남긴다()
        {
            SnowCpuStage stage = CreateStage(12f, 8f, 0);
            yield return null;

            GameObject eventObject = Own("__TEST__MovingBlizzard");
            eventObject.SetActive(false);
            BlizzardEvent weatherEvent = eventObject.AddComponent<BlizzardEvent>();
            SetPrivate(weatherEvent, "_snowStage", stage);
            SetPrivate(weatherEvent, "_warningDuration", 0.02f);
            SetPrivate(weatherEvent, "_recoveryDuration", 0.02f);
            SetPrivate(weatherEvent, "_moveSpeedMps", 80f);
            SetPrivate(weatherEvent, "_coreRadiusM", 0.5f);
            SetPrivate(weatherEvent, "_edgeFeatherM", 0.5f);

            var phases = new List<EBlizzardEventPhase>();
            weatherEvent.PhaseChanged += phases.Add;
            eventObject.SetActive(true);
            Assert.IsTrue(weatherEvent.Trigger(new BlizzardRoutePlan(
                new Vector2(1f, 4f), new Vector2(6f, 4f), Vector2.right, 12f)));

            yield return WaitUntil(() => weatherEvent.Phase == EBlizzardEventPhase.Idle, 2f);

            Assert.Contains(EBlizzardEventPhase.Warning, phases);
            Assert.Contains(EBlizzardEventPhase.Active, phases);
            Assert.Contains(EBlizzardEventPhase.Recovery, phases);
            Assert.Greater(stage.Field.GetAt(CellAt(stage.Field.Geo, 6f, 4f)), 0);
            Assert.AreEqual(0, stage.Field.GetAt(CellAt(stage.Field.Geo, 6f, 7f)));
        }

        [UnityTest]
        public IEnumerator 예약된_날짜만_한_번_눈폭풍을_시작한다()
        {
            SnowCpuStage stage = CreateStage(20f, 20f, 300);
            yield return null;

            GameObject eventObject = Own("__TEST__ScheduledBlizzard");
            eventObject.SetActive(false);
            BlizzardEvent weatherEvent = eventObject.AddComponent<BlizzardEvent>();
            ScheduledBlizzardDirector scheduler = eventObject.AddComponent<ScheduledBlizzardDirector>();
            SetPrivate(weatherEvent, "_snowStage", stage);
            SetPrivate(weatherEvent, "_coreRadiusM", 1f);
            SetPrivate(weatherEvent, "_edgeFeatherM", 1f);
            SetPrivate(weatherEvent, "_minimumRegionSeparationM", 5f);
            SetPrivate(scheduler, "_event", weatherEvent);
            SetPrivate(scheduler, "_blizzardDayIndices", new[] { 7 });
            eventObject.SetActive(true);

            Assert.IsFalse(scheduler.NotifyDateStarted(6));
            Assert.IsTrue(scheduler.NotifyDateStarted(7));
            Assert.IsFalse(scheduler.NotifyDateStarted(7));
            Assert.AreEqual(7, scheduler.LastTriggeredDayIndex);
        }

        [UnityTest]
        public IEnumerator 스테이지_시작과_종료가_날짜와_예약_눈폭풍을_제어한다()
        {
            SnowCpuStage stage = CreateStage(20f, 20f, 300);
            yield return null;
            RemoveDisc(stage.Field, new Vector2(5f, 10f), 2f, 240);
            RemoveDisc(stage.Field, new Vector2(15f, 10f), 1.5f, 180);

            GameObject eventObject = Own("__TEST__DateBlizzard");
            eventObject.SetActive(false);
            BlizzardEvent weatherEvent = eventObject.AddComponent<BlizzardEvent>();
            ScheduledBlizzardDirector scheduler = eventObject.AddComponent<ScheduledBlizzardDirector>();
            SetPrivate(weatherEvent, "_snowStage", stage);
            SetPrivate(weatherEvent, "_coreRadiusM", 1f);
            SetPrivate(weatherEvent, "_edgeFeatherM", 1f);
            SetPrivate(weatherEvent, "_candidateStepM", 0.5f);
            SetPrivate(weatherEvent, "_minimumRegionSeparationM", 5f);
            SetPrivate(scheduler, "_event", weatherEvent);
            SetPrivate(scheduler, "_blizzardDayIndices", new[] { 1 });
            eventObject.SetActive(true);

            TimeOfDayConfig timeConfig = ScriptableObject.CreateInstance<TimeOfDayConfig>();
            timeConfig.ApplyDefaultSky();
            timeConfig.SecondsPerDay = 1f;
            timeConfig.StartTimeOfDay = 0.99f;
            GameObject timeObject = Own("__TEST__TimeOfDay");
            timeObject.SetActive(false);
            TimeOfDayDirector timeOfDay = timeObject.AddComponent<TimeOfDayDirector>();
            SetPrivate(timeOfDay, "_config", timeConfig);
            SetPrivate(timeOfDay, "_autoStart", false);
            timeObject.SetActive(true);

            StageBalanceConfig balance = ScriptableObject.CreateInstance<StageBalanceConfig>();
            balance.StartSeconds = 0.2f;
            GameObject managerObject = Own("__TEST__DateGameManager");
            GameManager manager = managerObject.AddComponent<GameManager>();
            manager.Configure(balance, null);

            GameObject coordinatorObject = Own("__TEST__StageDateCoordinator");
            StageDateCoordinator coordinator = coordinatorObject.AddComponent<StageDateCoordinator>();
            coordinator.Configure(manager, timeOfDay, scheduler);

            manager.BeginPlaying();

            Assert.IsTrue(timeOfDay.IsRunning);
            Assert.AreEqual(int.MinValue, scheduler.LastTriggeredDayIndex, "예약되지 않은 0일에는 시작하면 안 된다");

            for (int index = 0; index < 30 && scheduler.LastTriggeredDayIndex != 1; index++)
                yield return null;

            Assert.AreEqual(1, scheduler.LastTriggeredDayIndex);
            Assert.AreEqual(EBlizzardEventPhase.Warning, weatherEvent.Phase);

            for (int index = 0; index < 30 && manager.Phase != EGamePhase.Ended; index++)
                yield return new WaitForFixedUpdate();

            Assert.AreEqual(EGamePhase.Ended, manager.Phase);
            Assert.IsFalse(timeOfDay.IsRunning);
        }

        private SnowCpuStage CreateStage(float widthM, float depthM, int initialDepthMm)
        {
            GameObject stageObject = Own("__TEST__BlizzardSnowCpuStage");
            SnowCpuStage stage = stageObject.AddComponent<SnowCpuStage>();
            SetPrivate(stage, "_originXZ", Vector2.zero);
            SetPrivate(stage, "_sizeMeters", new Vector2(widthM, depthM));
            SetPrivate(stage, "_initialDepthMm", initialDepthMm);
            return stage;
        }

        private GameObject Own(string name)
        {
            var gameObject = new GameObject(name);
            _owned.Add(gameObject);
            return gameObject;
        }

        private static int CellAt(SnowFieldGeometry geo, float x, float z)
        {
            int cx = Mathf.Clamp(Mathf.FloorToInt((x - geo.OriginXM) / SnowFieldGeometry.CellSizeM), 0, geo.ResX - 1);
            int cz = Mathf.Clamp(Mathf.FloorToInt((z - geo.OriginZM) / SnowFieldGeometry.CellSizeM), 0, geo.ResZ - 1);
            return geo.CellIndex(cx, cz);
        }

        private static void RemoveDisc(SnowHeightFieldCpu field, Vector2 center, float radiusM, int amountMm)
        {
            field.BeginStep();
            for (int z = 0; z < field.Geo.ResZ; z++)
            {
                for (int x = 0; x < field.Geo.ResX; x++)
                {
                    Vector2 point = new Vector2(
                        field.Geo.OriginXM + (x + 0.5f) * SnowFieldGeometry.CellSizeM,
                        field.Geo.OriginZM + (z + 0.5f) * SnowFieldGeometry.CellSizeM);
                    if (Vector2.Distance(point, center) <= radiusM) field.Add(x, z, -amountMm);
                }
            }
        }

        private static void AddDisc(SnowHeightFieldCpu field, Vector2 center, float radiusM, int amountMm)
        {
            SnowFieldGeometry geo = field.Geo;
            for (int z = 0; z < geo.ResZ; z++)
            {
                for (int x = 0; x < geo.ResX; x++)
                {
                    Vector2 point = new Vector2(
                        geo.OriginXM + (x + 0.5f) * SnowFieldGeometry.CellSizeM,
                        geo.OriginZM + (z + 0.5f) * SnowFieldGeometry.CellSizeM);
                    if (Vector2.Distance(point, center) > radiusM) continue;
                    field.Add(x, z, amountMm);
                }
            }
        }

        private static IEnumerator WaitUntil(Func<bool> condition, float timeoutSeconds)
        {
            float deadline = Time.realtimeSinceStartup + timeoutSeconds;
            while (!condition() && Time.realtimeSinceStartup < deadline) yield return null;
            Assert.IsTrue(condition(), "상태 전이가 제한 시간 안에 끝나지 않았다.");
        }

        private static void SetPrivate<T>(object target, string fieldName, T value)
        {
            FieldInfo field = target.GetType().GetField(fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(field, $"Missing private field: {target.GetType().Name}.{fieldName}");
            field.SetValue(target, value);
        }
    }
}
