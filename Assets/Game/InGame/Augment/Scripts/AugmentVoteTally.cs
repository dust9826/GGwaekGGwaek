using System;
using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 표를 세어 이긴 카드의 인덱스를 고른다. <b>순수 static</b> — 규칙 전체가 EditMode 로 덮인다.
    ///
    /// <para>동점은 <paramref name="random"/> 이 푼다. 이것을 부르는 쪽이 <b>권위 하나뿐</b>이라
    /// 결정론이 깨지지 않는다 — 뽑는 것은 서버이고 결과만 복제된다
    /// (<c>docs/specs/2026-09-01-augment-vote-and-intermission.md</c> §8).</para>
    /// </summary>
    public static class AugmentVoteTally
    {
        /// <summary>아직 안 골랐다.</summary>
        public const int NoVote = -1;

        /// <summary>
        /// <paramref name="votes"/> 는 PlayerId 색인이고 값은 카드 인덱스 또는 <see cref="NoVote"/>.
        /// <paramref name="eligible"/> 가 거짓인 자리는 <b>지금 판에 없는 사람</b>이라 세지 않는다.
        ///
        /// <para>유효표가 하나도 없으면 무작위 한 장을 돌려준다 — <b>"의뢰를 하나도 못 해도 반드시
        /// 한 번은 받는다"</b> 가 설계 의도이고(<c>Augment/AGENTS.md</c>), 기권으로 그것을 깨지 않는다.</para>
        /// </summary>
        public static int Resolve(IReadOnlyList<int> votes, IReadOnlyList<bool> eligible,
            int cardCount, Random random) =>
            Resolve(votes, eligible, cardCount, random, out _);

        /// <summary>
        /// 같은 판정에 <paramref name="wasTie"/> 를 얹은 것. <b>규칙은 하나도 바꾸지 않는다</b> —
        /// 화면이 "동점이라 무작위로 골랐다" 를 밝히려면 결과만으로는 알 수가 없어서 덧붙였다.
        ///
        /// <para>표가 하나도 없어 무작위로 한 장을 뽑는 경우도 동점으로 본다. 플레이어 입장에서는
        /// 둘 다 "내가 정하지 않았는데 정해졌다" 이고, 그것을 말해 주지 않는 것이 문제였다.</para>
        /// </summary>
        public static int Resolve(IReadOnlyList<int> votes, IReadOnlyList<bool> eligible,
            int cardCount, Random random, out bool wasTie)
        {
            wasTie = false;
            if (cardCount <= 0) return NoVote;
            if (random is null) throw new ArgumentNullException(nameof(random));

            var counts = new int[cardCount];
            int cast = 0;
            int slots = votes is null ? 0 : votes.Count;
            for (int index = 0; index < slots; index++)
            {
                if (eligible is not null && (index >= eligible.Count || !eligible[index])) continue;
                int pick = votes[index];
                if (pick < 0 || pick >= cardCount) continue;
                counts[pick]++;
                cast++;
            }

            if (cast == 0)
            {
                wasTie = true;
                return random.Next(cardCount);
            }

            int highest = 0;
            for (int index = 1; index < cardCount; index++)
                if (counts[index] > counts[highest]) highest = index;

            // 동점자를 모아 그중에서 뽑는다. 가장 낮은 인덱스를 쓰면 첫 카드가 구조적으로 유리해진다.
            var tied = new List<int>();
            for (int index = 0; index < cardCount; index++)
                if (counts[index] == counts[highest]) tied.Add(index);

            if (tied.Count == 1) return highest;

            wasTie = true;
            return tied[random.Next(tied.Count)];
        }
    }
}
