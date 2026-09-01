using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    /// <summary>증강이 소비처에 실제로 걸리는지 본다. 로드아웃이 비면 기존 동작 그대로여야
    /// 한다는 것도 함께 잰다 — 그것이 기존 씬·테스트를 안 건드린다는 근거다.</summary>
    public sealed class AugmentEffectPlayModeTests
    {
        private readonly List<Object> _spawned = new();

        [TearDown]
        public void TearDown()
        {
            foreach (Object spawned in _spawned)
                if (spawned != null) Object.Destroy(spawned);
            _spawned.Clear();
        }

        private StageBalanceConfig Config()
        {
            var config = ScriptableObject.CreateInstance<StageBalanceConfig>();
            config.StartSeconds = 100f;
            config.MaxSeconds = 0f;
            _spawned.Add(config);
            return config;
        }

        private static GiftRequest Request(int reward, float timeBonus) =>
            new GiftRequest(1, 0, EGiftBoxKind.Red, 10f,
                new RequestBalanceResult(1f, reward, 30f, timeBonus));

        private (GameManager manager, AugmentLoadout loadout) BuildManager(string name)
        {
            var host = new GameObject(name);
            _spawned.Add(host);
            AugmentLoadout loadout = host.AddComponent<AugmentLoadout>();
            GameManager manager = host.AddComponent<GameManager>();
            manager.Configure(Config(), null);
            manager.SetAugments(loadout);
            return (manager, loadout);
        }

        private AugmentDefinition Definition(string id, EAugmentStat benefitStat, float benefit,
            EAugmentStat penaltyStat, float penalty)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Benefits = new[] { new AugmentEffect { Stat = benefitStat, Value = benefit } };
            definition.Penalties = new[] { new AugmentEffect { Stat = penaltyStat, Value = penalty } };
            _spawned.Add(definition);
            return definition;
        }

        [UnityTest]
        public IEnumerator RewardAndTimeBonusScaleWithLoadout()
        {
            (GameManager manager, AugmentLoadout loadout) = BuildManager("__TEST__AugmentEffects");
            manager.BeginPlaying();
            yield return null;

            loadout.Add(Definition("reward_up", EAugmentStat.Reward, 0.5f,
                EAugmentStat.ClearTimeBonus, -0.5f));

            int scoreBefore = manager.Score;
            float timeBefore = manager.RemainingSeconds;
            manager.NotifyRequestCompleted(Request(reward: 10, timeBonus: 20f));

            Assert.That(manager.Score - scoreBefore, Is.EqualTo(15), "보상 10 x 1.5");
            Assert.That(manager.RemainingSeconds - timeBefore, Is.EqualTo(10f).Within(0.05f),
                "추가시간 20 x 0.5");
        }

        [UnityTest]
        public IEnumerator EmptyLoadoutLeavesRewardUntouched()
        {
            (GameManager manager, _) = BuildManager("__TEST__AugmentEffectsEmpty");
            manager.BeginPlaying();
            yield return null;

            int scoreBefore = manager.Score;
            float timeBefore = manager.RemainingSeconds;
            manager.NotifyRequestCompleted(Request(reward: 10, timeBonus: 20f));

            Assert.That(manager.Score - scoreBefore, Is.EqualTo(10));
            Assert.That(manager.RemainingSeconds - timeBefore, Is.EqualTo(20f).Within(0.05f));
        }

        [UnityTest]
        public IEnumerator NoLoadoutAtAllLeavesRewardUntouched()
        {
            var host = new GameObject("__TEST__AugmentEffectsNull");
            _spawned.Add(host);
            GameManager manager = host.AddComponent<GameManager>();
            manager.Configure(Config(), null);
            manager.BeginPlaying();
            yield return null;

            int scoreBefore = manager.Score;
            manager.NotifyRequestCompleted(Request(reward: 10, timeBonus: 20f));

            Assert.That(manager.Score - scoreBefore, Is.EqualTo(10));
        }

        [UnityTest]
        public IEnumerator StackedRewardPenaltiesNeverPayNegative()
        {
            (GameManager manager, AugmentLoadout loadout) = BuildManager("__TEST__AugmentEffectsFloor");
            manager.BeginPlaying();
            yield return null;

            for (int index = 0; index < 6; index++)
                loadout.Add(Definition($"down{index}", EAugmentStat.WalkSpeed, 0f,
                    EAugmentStat.Reward, -0.2f));

            int scoreBefore = manager.Score;
            manager.NotifyRequestCompleted(Request(reward: 10, timeBonus: 0f));

            Assert.That(manager.Score - scoreBefore, Is.EqualTo(0), "0 에서 멈추고 음수로 안 간다");
        }
    }
}
