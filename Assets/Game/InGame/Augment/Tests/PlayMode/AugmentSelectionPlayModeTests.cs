using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class AugmentSelectionPlayModeTests
    {
        private readonly List<Object> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object spawned in _spawned)
                if (spawned != null) Object.Destroy(spawned);
            _spawned.Clear();
        }

        private AugmentDefinition Make(string id)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Weight = 1f;
            definition.Benefits = new[] { new AugmentEffect { Stat = EAugmentStat.Reward, Value = 0.4f } };
            _spawned.Add(definition);
            return definition;
        }

        private (AugmentSelectionDirector director, AugmentLoadout loadout) Build()
        {
            var host = new GameObject("__TEST__AugmentRig");
            _spawned.Add(host);

            AugmentLoadout loadout = host.AddComponent<AugmentLoadout>();

            var pool = ScriptableObject.CreateInstance<AugmentPool>();
            pool.Entries = new[] { Make("a"), Make("b"), Make("c"), Make("d") };
            _spawned.Add(pool);

            AugmentSelectionDirector director = host.AddComponent<AugmentSelectionDirector>();
            director.ConfigureForTest(loadout, pool, cardCount: 3, seed: 7);
            return (director, loadout);
        }

        [UnityTest]
        public IEnumerator OpeningStartsIntermissionAndOffersThreeCards()
        {
            (AugmentSelectionDirector director, _) = Build();

            director.OpenForTest();
            yield return null;

            Assert.That(director.IsOpen, Is.True);
            Assert.That(director.Cards.Count, Is.EqualTo(3));

            // 정지 대신 쉬는 시간이 선다. 이것이 이 작업의 핵심 단언이다.
            Assert.That(director.IntermissionRemainingSeconds, Is.GreaterThan(0f));
            Assert.That(Time.timeScale, Is.EqualTo(1f), "증강은 더 이상 시간을 멈추지 않는다");
        }

        [UnityTest]
        public IEnumerator ConfirmAddsToLoadoutAndEndsIntermission()
        {
            (AugmentSelectionDirector director, AugmentLoadout loadout) = Build();

            director.OpenForTest();
            yield return null;
            AugmentDefinition picked = director.Cards[0];
            director.Confirm(picked);
            yield return null;

            Assert.That(director.IsOpen, Is.False);
            Assert.That(loadout.Has(picked), Is.True);
            Assert.That(loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1.4f).Within(1e-5f));

            // 1인이라 클릭이 곧 전원 투표다 — 타이머를 기다리지 않고 쉬는 시간이 끝난다.
            Assert.That(director.IntermissionRemainingSeconds, Is.EqualTo(0f));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator SecondOpenExcludesWhatWasTaken()
        {
            (AugmentSelectionDirector director, _) = Build();

            director.OpenForTest();
            yield return null;
            AugmentDefinition picked = director.Cards[0];
            director.Confirm(picked);
            yield return null;

            director.OpenForTest();
            yield return null;

            CollectionAssert.DoesNotContain(director.Cards, picked);
        }

        [UnityTest]
        public IEnumerator ConfirmingUnofferedCardIsIgnored()
        {
            (AugmentSelectionDirector director, AugmentLoadout loadout) = Build();

            director.OpenForTest();
            yield return null;
            AugmentDefinition outsider = Make("outsider");
            director.Confirm(outsider);
            yield return null;

            Assert.That(loadout.Has(outsider), Is.False);
            Assert.That(director.IsOpen, Is.True);

            director.Confirm(director.Cards[0]);
            yield return null;
        }

        [UnityTest]
        public IEnumerator ExhaustedPoolDoesNotOpenOrStartIntermission()
        {
            var host = new GameObject("__TEST__AugmentRigEmpty");
            _spawned.Add(host);
            AugmentLoadout loadout = host.AddComponent<AugmentLoadout>();
            var pool = ScriptableObject.CreateInstance<AugmentPool>();
            pool.Entries = new AugmentDefinition[0];
            _spawned.Add(pool);

            AugmentSelectionDirector director = host.AddComponent<AugmentSelectionDirector>();
            director.ConfigureForTest(loadout, pool, cardCount: 3, seed: 7);

            director.OpenForTest();
            yield return null;

            Assert.That(director.IsOpen, Is.False);
            Assert.That(director.IntermissionRemainingSeconds, Is.EqualTo(0f));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator IntermissionExpiryPicksSomethingWithoutAVote()
        {
            (AugmentSelectionDirector director, AugmentLoadout loadout) = Build();
            director.SetIntermissionSecondsForTest(0.2f);

            director.OpenForTest();
            yield return new WaitForSeconds(0.4f);

            // 아무도 안 골랐지만 "반드시 한 번은 받는다" 가 설계 의도다.
            Assert.That(director.IsOpen, Is.False);
            Assert.That(loadout.Owned.Count, Is.EqualTo(1));
        }
    }
}
