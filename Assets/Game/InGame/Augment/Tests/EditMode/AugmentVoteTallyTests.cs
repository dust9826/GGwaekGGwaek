using System;
using NUnit.Framework;

namespace PPack
{
    public sealed class AugmentVoteTallyTests
    {
        private static readonly bool[] AllPresent = { true, true, true, true };

        private static int Resolve(int[] votes, int cardCount = 3, int seed = 1234,
            bool[] eligible = null)
            => AugmentVoteTally.Resolve(votes, eligible ?? AllPresent, cardCount,
                new Random(seed));

        [Test]
        public void HighestVoteCountWins()
        {
            int[] votes = { 2, 2, 0, AugmentVoteTally.NoVote };
            Assert.That(Resolve(votes), Is.EqualTo(2));
        }

        [Test]
        public void AbstainedSlotsAreNotCounted()
        {
            int[] votes = { 1, AugmentVoteTally.NoVote, AugmentVoteTally.NoVote,
                AugmentVoteTally.NoVote };
            Assert.That(Resolve(votes), Is.EqualTo(1));
        }

        [Test]
        public void OutOfRangePicksAreIgnored()
        {
            int[] votes = { 7, -9, 0, AugmentVoteTally.NoVote };
            Assert.That(Resolve(votes), Is.EqualTo(0));
        }

        [Test]
        public void VotesFromAbsentPlayersAreIgnored()
        {
            int[] votes = { 2, 2, 0, 0 };
            bool[] eligible = { false, false, true, true };
            Assert.That(Resolve(votes, eligible: eligible), Is.EqualTo(0));
        }

        [Test]
        public void AllAbstainingStillYieldsACard()
        {
            int[] votes = { AugmentVoteTally.NoVote, AugmentVoteTally.NoVote,
                AugmentVoteTally.NoVote, AugmentVoteTally.NoVote };
            Assert.That(Resolve(votes), Is.InRange(0, 2));
        }

        [Test]
        public void TiesResolveAmongTiedCardsOnly()
        {
            int[] votes = { 0, 1, AugmentVoteTally.NoVote, AugmentVoteTally.NoVote };
            for (int seed = 0; seed < 50; seed++)
            {
                int picked = Resolve(votes, seed: seed);
                Assert.That(picked, Is.EqualTo(0).Or.EqualTo(1), $"seed={seed} 에서 2가 나왔다");
            }
        }

        [Test]
        public void SameSeedGivesSameResult()
        {
            int[] votes = { 0, 1, AugmentVoteTally.NoVote, AugmentVoteTally.NoVote };
            Assert.That(Resolve(votes, seed: 77), Is.EqualTo(Resolve(votes, seed: 77)));
        }

        [Test]
        public void NoCardsGivesNoVote()
        {
            int[] votes = { 0, 0, 0, 0 };
            Assert.That(Resolve(votes, cardCount: 0), Is.EqualTo(AugmentVoteTally.NoVote));
        }

        [Test]
        public void NullVotesDoesNotThrow()
        {
            Assert.That(AugmentVoteTally.Resolve(null, AllPresent, 3, new Random(1)),
                Is.InRange(0, 2));
        }
    }
}
