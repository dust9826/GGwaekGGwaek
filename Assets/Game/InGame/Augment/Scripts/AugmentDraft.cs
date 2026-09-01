using System.Collections.Generic;

namespace PPack
{
    /// <summary>증강 추첨. RNG·씬·GPU 와 무관한 순수 계산이라 EditMode 로 전부 덮인다 —
    /// 시드는 호출자가 넣는다(<see cref="RequestBalance"/> 와 같은 이유).
    ///
    /// <para><see cref="AugmentLoadout"/> 이 아니라 <see cref="IReadOnlyList{T}"/> 를 받는 것은
    /// 의도적이다. MonoBehaviour 를 인자로 받으면 순수 함수가 아니게 되고 EditMode 로 덮기
    /// 어려워진다 — <c>StageSession.Resolve</c> 와 같은 이유다.</para></summary>
    public static class AugmentDraft
    {
        private static readonly List<AugmentDefinition> Candidates = new();

        /// <summary>후보에서 가중치로 <paramref name="count"/> 장을 뽑아
        /// <paramref name="results"/> 에 채운다. 이미 가진 것과 가중치 0은 빠지고,
        /// 후보가 모자라면 있는 만큼만 준다.</summary>
        public static void Draw(IReadOnlyList<AugmentDefinition> pool, IReadOnlyList<AugmentDefinition> owned,
            int count, System.Random random, List<AugmentDefinition> results)
        {
            if (results is null) return;

            results.Clear();
            if (pool is null || count <= 0 || random is null) return;

            Candidates.Clear();
            for (int index = 0; index < pool.Count; index++)
            {
                AugmentDefinition candidate = pool[index];
                if (candidate == null || candidate.Weight <= 0f) continue;
                if (owned is not null && Contains(owned, candidate)) continue;
                Candidates.Add(candidate);
            }

            while (results.Count < count && Candidates.Count > 0)
            {
                int picked = PickWeighted(random);
                results.Add(Candidates[picked]);
                Candidates.RemoveAt(picked);
            }

            Candidates.Clear();
        }

        private static bool Contains(IReadOnlyList<AugmentDefinition> list, AugmentDefinition value)
        {
            for (int index = 0; index < list.Count; index++)
                if (list[index] == value) return true;
            return false;
        }

        private static int PickWeighted(System.Random random)
        {
            float total = 0f;
            for (int index = 0; index < Candidates.Count; index++) total += Candidates[index].Weight;

            float roll = (float)random.NextDouble() * total;
            for (int index = 0; index < Candidates.Count; index++)
            {
                roll -= Candidates[index].Weight;
                if (roll <= 0f) return index;
            }

            return Candidates.Count - 1;
        }
    }
}
