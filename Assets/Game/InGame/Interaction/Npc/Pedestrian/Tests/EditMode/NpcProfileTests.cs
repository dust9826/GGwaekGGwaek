using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class NpcProfileTests
    {
        private NpcAppearanceCatalog _catalog;

        [SetUp]
        public void SetUp()
        {
            _catalog = ScriptableObject.CreateInstance<NpcAppearanceCatalog>();
            List<NpcAppearanceCatalog.Entry> entries = new List<NpcAppearanceCatalog.Entry>();
            foreach (ENpcAppearanceSlot slot in System.Enum.GetValues(typeof(ENpcAppearanceSlot))) {
                entries.Add(new NpcAppearanceCatalog.Entry(slot, 100 + (int)slot, "A", null));
                entries.Add(new NpcAppearanceCatalog.Entry(slot, 200 + (int)slot, "B", null));
            }
            _catalog.SetEntriesForEditor(entries.ToArray());
        }

        [TearDown]
        public void TearDown()
        {
            Object.DestroyImmediate(_catalog);
        }

        [Test]
        public void 같은_시드는_같은_외형을_만든다()
        {
            NpcAppearanceData first = NpcAppearanceGenerator.Generate(1234, _catalog, null);
            NpcAppearanceData second = NpcAppearanceGenerator.Generate(1234, _catalog, null);
            Assert.That(first, Is.EqualTo(second));
        }

        [Test]
        public void 활성_NPC와_완전히_같은_후보보다_다른_후보를_선택한다()
        {
            NpcAppearanceData existing = NpcAppearanceGenerator.Generate(1234, _catalog, null);
            NpcProfileData[] active = { new NpcProfileData(1, 1234, ENpcTemperament.Timid, existing) };
            NpcAppearanceData generated = NpcAppearanceGenerator.Generate(1234, _catalog, active);

            Assert.That(generated, Is.Not.EqualTo(existing));
            Assert.That(NpcAppearanceGenerator.WeightedDistance(generated, existing), Is.GreaterThan(0));
        }

        [Test]
        public void 랜덤_외형은_서로_겹치는_의상과_머리_조합을_만들지_않는다()
        {
            for (int seed = 0; seed < 128; seed++) {
                NpcAppearanceData appearance = NpcAppearanceGenerator.Generate(seed, _catalog, null);
                bool hasOutfit = appearance.GetId(ENpcAppearanceSlot.Top) != 0;
                bool hasSeparates = appearance.GetId(ENpcAppearanceSlot.Coat) != 0 ||
                    appearance.GetId(ENpcAppearanceSlot.Pants) != 0;
                bool hasHairAndHat = appearance.GetId(ENpcAppearanceSlot.Hair) != 0 &&
                    appearance.GetId(ENpcAppearanceSlot.Hat) != 0;

                Assert.That(hasOutfit && hasSeparates, Is.False, $"seed: {seed}");
                Assert.That(hasHairAndHat, Is.False, $"seed: {seed}");
            }
        }

        [Test]
        public void 고유_ID로_프로필을_조회할_수_있다()
        {
            NpcProfileData profile = new NpcProfileData(771, 42, ENpcTemperament.Aggressive, default);
            NpcProfileRegistry.Register(profile);
            try {
                Assert.That(NpcProfileRegistry.TryGet(771, out NpcProfileData found), Is.True);
                Assert.That(found.Temperament, Is.EqualTo(ENpcTemperament.Aggressive));
            }
            finally {
                NpcProfileRegistry.Unregister(771);
            }
        }
    }
}
