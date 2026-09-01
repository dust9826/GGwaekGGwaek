using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 투표 화면이 <b>지금 이 프레임에 그려야 할 것 전부</b>. 값만 담고 판정하지 않는다.
    ///
    /// <para><b>왜 묶었는가.</b> 이 값들은 <c>AugmentNetHub</c> → <c>AugmentSelectionDirector</c> →
    /// <c>AugmentSelectionView</c> 로 세 겹을 그대로 지나간다. 낱개로 두면 같은 인자 일곱 개가
    /// 세 군데에 늘어서고, 하나 늘 때마다 세 곳을 고쳐야 한다. 두 번째 소비처가 확인된 뒤에
    /// 묶는다는 규약(루트 AGENTS)에 맞고, 여기서는 그 소비처가 이미 셋이다.</para>
    ///
    /// <para>전부 <b>이미 복제된 값</b>에서 나온다. 이 구조체가 새 권위를 갖지 않는다.</para>
    /// </summary>
    public readonly struct AugmentVoteDisplay
    {
        /// <summary>카드별 득표. 길이는 제시된 카드 수다.</summary>
        public readonly IReadOnlyList<int> PerCard;

        /// <summary>표를 낸 사람 수.</summary>
        public readonly int Voted;

        /// <summary>지금 판에 있는 사람 수. <b>2 미만이면 투표 표시를 전부 감춘다.</b></summary>
        public readonly int Total;

        /// <summary>이 화면을 보는 사람이 고른 카드. 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public readonly int LocalPick;

        /// <summary>확정된 카드. 아직이면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public readonly int Winner;

        /// <summary>그 카드가 <b>동점을 무작위로 푼 결과</b>인가. 화면이 그 사실을 밝혀야 한다.</summary>
        public readonly bool WinnerWasTie;

        /// <summary>투표에 남은 시간. 결과 단계에서는 0이다.</summary>
        public readonly float SecondsLeft;

        public AugmentVoteDisplay(IReadOnlyList<int> perCard, int voted, int total,
            int localPick, int winner, bool winnerWasTie, float secondsLeft)
        {
            PerCard = perCard;
            Voted = voted;
            Total = total;
            LocalPick = localPick;
            Winner = winner;
            WinnerWasTie = winnerWasTie;
            SecondsLeft = secondsLeft;
        }
    }
}
