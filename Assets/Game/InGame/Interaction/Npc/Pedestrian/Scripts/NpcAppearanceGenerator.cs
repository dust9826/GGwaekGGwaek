using System;
using System.Collections.Generic;

namespace PPack
{
    public static class NpcAppearanceGenerator
    {
        private const int CandidateCount = 32;
        private static readonly ENpcAppearanceSlot[] Slots =
            (ENpcAppearanceSlot[])Enum.GetValues(typeof(ENpcAppearanceSlot));

        public static NpcAppearanceData Generate(int seed, NpcAppearanceCatalog catalog,
            IReadOnlyList<NpcProfileData> activeProfiles)
        {
            if (catalog == null) return default;

            Random random = new Random(seed);
            NpcAppearanceData best = default;
            int bestDistance = int.MinValue;

            for (int index = 0; index < CandidateCount; index++) {
                NpcAppearanceData candidate = CreateCandidate(random, catalog);
                int distance = MinimumDistance(candidate, activeProfiles);
                if (distance <= bestDistance) continue;
                best = candidate;
                bestDistance = distance;
            }

            return best;
        }

        public static int WeightedDistance(NpcAppearanceData a, NpcAppearanceData b)
        {
            int distance = 0;
            foreach (ENpcAppearanceSlot slot in Slots) {
                if (a.GetId(slot) != b.GetId(slot)) distance += GetWeight(slot);
            }
            return distance;
        }

        private static NpcAppearanceData CreateCandidate(Random random, NpcAppearanceCatalog catalog)
        {
            NpcAppearanceData result = default;
            List<int> ids = new List<int>();
            foreach (ENpcAppearanceSlot slot in Slots) {
                catalog.GetIds(slot, ids);
                if (ids.Count == 0) {
                    result.SetId(slot, 0);
                    continue;
                }
                bool optional = IsOptional(slot);
                int optionCount = ids.Count + (optional ? 1 : 0);
                int choice = optionCount > 0 ? random.Next(optionCount) : 0;
                int id = optional && choice == 0 ? 0 : ids[choice - (optional ? 1 : 0)];
                result.SetId(slot, id);
            }

            ApplyCompatibilityRules(random, ref result);
            return result;
        }

        private static void ApplyCompatibilityRules(Random random, ref NpcAppearanceData appearance)
        {
            bool useOutfit = random.Next(2) == 0;
            if (useOutfit) {
                appearance.SetId(ENpcAppearanceSlot.Coat, 0);
                appearance.SetId(ENpcAppearanceSlot.Pants, 0);
            }
            else {
                appearance.SetId(ENpcAppearanceSlot.Top, 0);
            }

            bool useHat = appearance.GetId(ENpcAppearanceSlot.Hat) != 0 && random.Next(3) == 0;
            if (useHat) appearance.SetId(ENpcAppearanceSlot.Hair, 0);
            else appearance.SetId(ENpcAppearanceSlot.Hat, 0);
        }

        private static int MinimumDistance(NpcAppearanceData candidate,
            IReadOnlyList<NpcProfileData> activeProfiles)
        {
            if (activeProfiles == null || activeProfiles.Count == 0) return int.MaxValue;
            int minimum = int.MaxValue;
            for (int index = 0; index < activeProfiles.Count; index++) {
                int distance = WeightedDistance(candidate, activeProfiles[index].Appearance);
                if (distance < minimum) minimum = distance;
            }
            return minimum;
        }

        private static bool IsOptional(ENpcAppearanceSlot slot)
        {
            return slot == ENpcAppearanceSlot.Hair || slot == ENpcAppearanceSlot.Coat ||
                slot == ENpcAppearanceSlot.Hat;
        }

        private static int GetWeight(ENpcAppearanceSlot slot)
        {
            return slot switch {
                ENpcAppearanceSlot.Body => 3,
                ENpcAppearanceSlot.Hair => 3,
                ENpcAppearanceSlot.Coat => 3,
                ENpcAppearanceSlot.Hat => 3,
                ENpcAppearanceSlot.Top => 2,
                ENpcAppearanceSlot.Pants => 2,
                _ => 1,
            };
        }
    }
}
