using System.Collections.Generic;
using NUnit.Framework;

namespace PPack
{
    public sealed class GiftAcceptanceTests
    {
        [Test]
        public void 정원_이하면_전부_수용된다()
        {
            var entries = new List<GiftEntry> { new GiftEntry(0, 3), new GiftEntry(1, 1) };
            var accepted = new List<int>();
            var overflow = new List<int>();

            GiftAcceptance.Select(entries, 3, accepted, overflow);

            Assert.That(accepted.Count, Is.EqualTo(2));
            Assert.That(overflow.Count, Is.EqualTo(0));
        }

        [Test]
        public void 초과시_값어치_상위만_수용한다()
        {
            var entries = new List<GiftEntry> { new GiftEntry(0, 1), new GiftEntry(1, 5), new GiftEntry(2, 3) };
            var accepted = new List<int>();
            var overflow = new List<int>();

            GiftAcceptance.Select(entries, 2, accepted, overflow);

            Assert.That(accepted, Is.EquivalentTo(new[] { 1, 2 }));
            Assert.That(overflow, Is.EquivalentTo(new[] { 0 }));
        }

        [Test]
        public void 동점이면_Id_오름차순으로_결정론적이다()
        {
            var entries = new List<GiftEntry> { new GiftEntry(2, 5), new GiftEntry(0, 5), new GiftEntry(1, 5) };
            var accepted = new List<int>();
            var overflow = new List<int>();

            GiftAcceptance.Select(entries, 2, accepted, overflow);

            // entries[1] (Id=0) 과 entries[2] (Id=1) 가 뽑히고, entries[0] (Id=2) 가 초과된다.
            Assert.That(accepted, Is.EqualTo(new[] { 1, 2 }));
            Assert.That(overflow, Is.EqualTo(new[] { 0 }));
        }
    }
}
