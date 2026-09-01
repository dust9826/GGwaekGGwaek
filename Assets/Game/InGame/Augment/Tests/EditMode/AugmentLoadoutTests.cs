using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class AugmentLoadoutTests
    {
        private GameObject _host;
        private AugmentLoadout _loadout;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("__TEST__AugmentLoadout");
            _loadout = _host.AddComponent<AugmentLoadout>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        private static AugmentDefinition Make(string id, EAugmentStat stat, float benefit,
            EAugmentStat penaltyStat, float penalty)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Benefits = new[] { new AugmentEffect { Stat = stat, Value = benefit } };
            definition.Penalties = new[] { new AugmentEffect { Stat = penaltyStat, Value = penalty } };
            definition.Weight = 1f;
            return definition;
        }

        [Test]
        public void EmptyLoadoutIsZero()
        {
            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.EqualTo(0f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1f));
        }

        [Test]
        public void BenefitAndPenaltyBothAccumulate()
        {
            _loadout.Add(Make("a", EAugmentStat.Reward, 0.4f, EAugmentStat.RequestTtl, -0.2f));

            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(_loadout.GetValue(EAugmentStat.RequestTtl), Is.EqualTo(-0.2f).Within(1e-5f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1.4f).Within(1e-5f));
        }

        [Test]
        public void StacksAndCancels()
        {
            _loadout.Add(Make("a", EAugmentStat.WalkSpeed, 0.25f, EAugmentStat.Reward, -0.2f));
            _loadout.Add(Make("b", EAugmentStat.WalkSpeed, 0.10f, EAugmentStat.Reward, 0.2f));

            Assert.That(_loadout.GetValue(EAugmentStat.WalkSpeed), Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void MultiplierNeverGoesNegative()
        {
            for (int index = 0; index < 6; index++)
                _loadout.Add(Make($"p{index}", EAugmentStat.WalkSpeed, 0f, EAugmentStat.Reward, -0.2f));

            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.LessThan(-1f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(0f));
        }

        [Test]
        public void AddRaisesChangedAndTracksOwned()
        {
            int raised = 0;
            _loadout.Changed += () => raised++;
            AugmentDefinition definition = Make("a", EAugmentStat.Reward, 0.4f, EAugmentStat.RequestTtl, -0.2f);

            _loadout.Add(definition);

            Assert.That(raised, Is.EqualTo(1));
            Assert.That(_loadout.Has(definition), Is.True);
            Assert.That(_loadout.Owned.Count, Is.EqualTo(1));
        }

        [Test]
        public void ClearResetsValuesAndOwned()
        {
            _loadout.Add(Make("a", EAugmentStat.WalkSpeed, 0.25f, EAugmentStat.Reward, -0.2f));

            _loadout.Clear();

            Assert.That(_loadout.Owned.Count, Is.EqualTo(0));
            Assert.That(_loadout.GetValue(EAugmentStat.WalkSpeed), Is.EqualTo(0f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1f));
        }
    }
}
