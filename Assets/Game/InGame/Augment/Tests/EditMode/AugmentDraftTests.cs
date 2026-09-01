using System;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class AugmentDraftTests
    {
        private readonly List<AugmentDefinition> _created = new();
        private readonly List<AugmentDefinition> _results = new();

        [TearDown]
        public void TearDown()
        {
            foreach (AugmentDefinition definition in _created)
                if (definition != null) UnityEngine.Object.DestroyImmediate(definition);
            _created.Clear();
            _results.Clear();
        }

        private AugmentDefinition Make(string id, float weight = 1f)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Weight = weight;
            _created.Add(definition);
            return definition;
        }

        private List<AugmentDefinition> Pool(int count)
        {
            var pool = new List<AugmentDefinition>();
            for (int index = 0; index < count; index++) pool.Add(Make($"a{index}"));
            return pool;
        }

        [Test]
        public void DrawsRequestedCountWithoutDuplicates()
        {
            AugmentDraft.Draw(Pool(6), Array.Empty<AugmentDefinition>(), 3, new System.Random(1), _results);

            Assert.That(_results.Count, Is.EqualTo(3));
            CollectionAssert.AllItemsAreUnique(_results);
        }

        [Test]
        public void ExcludesOwned()
        {
            List<AugmentDefinition> pool = Pool(4);
            var owned = new List<AugmentDefinition> { pool[0], pool[1] };

            AugmentDraft.Draw(pool, owned, 3, new System.Random(1), _results);

            Assert.That(_results.Count, Is.EqualTo(2));
            CollectionAssert.DoesNotContain(_results, pool[0]);
            CollectionAssert.DoesNotContain(_results, pool[1]);
        }

        [Test]
        public void ExhaustedPoolGivesWhatIsLeft()
        {
            AugmentDraft.Draw(Pool(2), Array.Empty<AugmentDefinition>(), 3, new System.Random(1), _results);

            Assert.That(_results.Count, Is.EqualTo(2));
        }

        [Test]
        public void ZeroWeightNeverDrawn()
        {
            List<AugmentDefinition> pool = Pool(3);
            AugmentDefinition never = Make("never", 0f);
            pool.Add(never);

            for (int seed = 0; seed < 20; seed++)
            {
                AugmentDraft.Draw(pool, Array.Empty<AugmentDefinition>(), 3, new System.Random(seed), _results);
                CollectionAssert.DoesNotContain(_results, never);
            }
        }

        [Test]
        public void SameSeedGivesSameResult()
        {
            List<AugmentDefinition> pool = Pool(8);
            var first = new List<AugmentDefinition>();

            AugmentDraft.Draw(pool, Array.Empty<AugmentDefinition>(), 3, new System.Random(42), first);
            AugmentDraft.Draw(pool, Array.Empty<AugmentDefinition>(), 3, new System.Random(42), _results);

            CollectionAssert.AreEqual(first, _results);
        }

        [Test]
        public void NullPoolGivesEmpty()
        {
            AugmentDraft.Draw(null, Array.Empty<AugmentDefinition>(), 3, new System.Random(1), _results);

            Assert.That(_results.Count, Is.EqualTo(0));
        }
    }
}
