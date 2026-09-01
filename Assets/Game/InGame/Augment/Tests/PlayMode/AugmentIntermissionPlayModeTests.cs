using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>
    /// 쉬는 시간이 <see cref="RequestDirector"/> 의 무엇을 멈추고 무엇을 안 멈추는지 본다.
    /// 스폰과 TTL 은 멈추고 완료는 통과한다 — 쉬는 중에 배달을 마치는 것을 막을 이유가 없다.
    /// </summary>
    public sealed class AugmentIntermissionPlayModeTests
    {
        private GameObject _root;
        private readonly List<Object> _spawned = new();
        private readonly List<DeliveryHouse> _houses = new();

        [UnitySetUp]
        public IEnumerator SetUp()
        {
            _root = new GameObject("__TEST__AugmentIntermission");
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            if (_root != null) Object.Destroy(_root);
            foreach (Object spawned in _spawned)
                if (spawned != null) Object.Destroy(spawned);
            _spawned.Clear();
            _houses.Clear();
            yield return null;
        }

        [UnityTest]
        public IEnumerator NoNewRequestsDuringIntermission()
        {
            (RequestDirector director, AugmentSelectionDirector selection) = Build(ttlBase: 100f);
            yield return null;

            selection.OpenForTest();
            int before = director.ActiveRequests.Count;
            for (int i = 0; i < 120; i++) yield return new WaitForFixedUpdate();

            Assert.That(director.ActiveRequests.Count, Is.EqualTo(before),
                "쉬는 시간 동안 의뢰가 새로 나왔다");
        }

        [UnityTest]
        public IEnumerator RequestTtlIsFrozenDuringIntermission()
        {
            (RequestDirector director, AugmentSelectionDirector selection) = Build(ttlBase: 100f);
            yield return null;

            GiftRequest request = director.SpawnRequest(0, EGiftBoxKind.Red);
            Assert.That(request, Is.Not.Null, "테스트 리그가 의뢰를 만들지 못했다");

            selection.OpenForTest();
            yield return new WaitForFixedUpdate();
            float remaining = request.RemainingSeconds;
            for (int i = 0; i < 60; i++) yield return new WaitForFixedUpdate();

            Assert.That(request.RemainingSeconds, Is.EqualTo(remaining).Within(0.0001f));
        }

        [UnityTest]
        public IEnumerator TtlResumesAfterIntermission()
        {
            (RequestDirector director, AugmentSelectionDirector selection) = Build(ttlBase: 100f);
            yield return null;

            GiftRequest request = director.SpawnRequest(0, EGiftBoxKind.Red);
            selection.OpenForTest();
            yield return new WaitForFixedUpdate();

            // 1인이라 클릭이 곧 전원 투표다 — 타이머를 기다리지 않고 닫힌다.
            selection.Confirm(selection.Cards[0]);
            Assert.That(selection.IsOpen, Is.False);

            float remaining = request.RemainingSeconds;
            for (int i = 0; i < 30; i++) yield return new WaitForFixedUpdate();

            Assert.That(request.RemainingSeconds, Is.LessThan(remaining));
        }

        [UnityTest]
        public IEnumerator CompletionStillLandsDuringIntermission()
        {
            (RequestDirector director, AugmentSelectionDirector selection) = Build(ttlBase: 100f);
            bool completed = false;
            director.RequestCompleted += _ => completed = true;
            yield return null;

            director.SpawnRequest(0, EGiftBoxKind.Red);
            selection.OpenForTest();
            SpawnGiftInHouse(0, EGiftBoxKind.Red);

            for (int i = 0; i < 20 && !completed; i++) yield return new WaitForFixedUpdate();

            Assert.That(completed, Is.True, "쉬는 시간에도 배달 완료는 통과해야 한다");
        }

        // --- rig ---

        private (RequestDirector, AugmentSelectionDirector) Build(float ttlBase)
        {
            var config = ScriptableObject.CreateInstance<StageBalanceConfig>();
            config.TtlBase = ttlBase;
            config.MaxActiveRequests = 10;
            config.FirstSpawnDelaySeconds = 0.05f;
            config.SpawnIntervalMin = 0.05f;
            config.SpawnIntervalMax = 0.05f;
            _spawned.Add(config);

            var baseObject = new GameObject("__TEST__Base");
            baseObject.transform.SetParent(_root.transform);

            for (int index = 0; index < 4; index++)
            {
                var houseObject = new GameObject($"__TEST__House{index}");
                houseObject.transform.SetParent(_root.transform);
                houseObject.transform.position = new Vector3(20f + index * 15f, 0f, 0f);
                GiftDropZone zone = houseObject.AddComponent<GiftDropZone>();
                DeliveryHouse house = houseObject.AddComponent<DeliveryHouse>();
                house.Configure(null, null, zone);
                _houses.Add(house);
            }

            var rigObject = new GameObject("__TEST__AugmentRig");
            rigObject.transform.SetParent(_root.transform);
            AugmentLoadout loadout = rigObject.AddComponent<AugmentLoadout>();

            var pool = ScriptableObject.CreateInstance<AugmentPool>();
            pool.Entries = new[] { Make("a"), Make("b"), Make("c"), Make("d") };
            _spawned.Add(pool);

            AugmentSelectionDirector selection = rigObject.AddComponent<AugmentSelectionDirector>();
            selection.ConfigureForTest(loadout, pool, cardCount: 3, seed: 7);

            var directorObject = new GameObject("__TEST__RequestDirector");
            directorObject.transform.SetParent(_root.transform);
            directorObject.SetActive(false);
            RequestDirector director = directorObject.AddComponent<RequestDirector>();
            director.Configure(null, baseObject.transform, null, _houses, config, null);
            director.SetIntermission(selection);
            directorObject.SetActive(true);
            director.Begin();
            return (director, selection);
        }

        private AugmentDefinition Make(string id)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Weight = 1f;
            definition.Benefits = new[]
                { new AugmentEffect { Stat = EAugmentStat.Reward, Value = 0.4f } };
            _spawned.Add(definition);
            return definition;
        }

        private Gift SpawnGiftInHouse(int houseIndex, EGiftBoxKind kind)
        {
            var giftObject = new GameObject($"__TEST__Gift_{kind}");
            giftObject.transform.SetParent(_root.transform);
            giftObject.transform.position = _houses[houseIndex].DoorPosition;
            Gift gift = giftObject.AddComponent<Gift>();
            gift.SetKind(kind);
            return gift;
        }
    }
}
