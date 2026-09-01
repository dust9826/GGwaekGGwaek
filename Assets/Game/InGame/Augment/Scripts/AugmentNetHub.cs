using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 증강 투표 상태를 복제한다. <b>서버가 <see cref="AugmentSelectionDirector"/> 로 그대로 굴리고,
    /// 이 컴포넌트는 그 상태를 <c>[Networked]</c> 로 복사한다. 클라이언트는 여기만 읽는다</b> —
    /// <see cref="MissionNetHub"/> 와 같은 문장이다.
    ///
    /// <para><b>로드아웃은 복제하지 않는다.</b> 소비처 넷이 전부 서버에서만 읽히기 때문이다 —
    /// <c>GameManager</c> 와 <c>RequestDirector</c> 는 <c>MissionNetHub</c> 가 클라에서 끄고,
    /// <c>PenguinLocomotion.Step</c> 은 <c>HasStateAuthority</c> 로 막혀 있다. 팀이 지금까지 뭘
    /// 모았는지 보여 주는 화면이 생기면 그때 이 결정을 뒤집는다
    /// (<c>docs/specs/2026-09-01-augment-vote-and-intermission.md</c> §6).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AugmentNetHub : NetworkBehaviour
    {
        /// <summary>비트가 셋이라 카드도 셋이다(<see cref="EInputButton.AugmentPick0"/>).</summary>
        public const int MaxCards = 3;

        [Networked] private NetworkBool NetOpen { get; set; }
        [Networked] private int NetDayIndex { get; set; }
        [Networked, Capacity(MaxCards)] private NetworkArray<int> NetCards { get; }

        /// <summary>⚠ 색인은 <c>PlayerId</c> 이고 용량은 <see cref="SessionLauncher.MaxPlayerId"/> 다.
        /// <c>MaxPlayers</c> 로 잡으면 늦게 들어온 사람의 표가 조용히 버려진다.</summary>
        [Networked, Capacity(SessionLauncher.MaxPlayerId)]
        private NetworkArray<int> NetVotes { get; }

        [Networked] private int NetWinningCard { get; set; }

        /// <summary>이긴 카드가 동점 추첨 결과인가. 화면이 그 사실을 밝히므로 복제한다.</summary>
        [Networked] private NetworkBool NetWinnerWasTie { get; set; }

        /// <summary>
        /// 투표에 남은 시간. <b>복제해야 한다</b> — 전원이 표를 내면 권위가 이 값을 유예까지
        /// 당기는데(<see cref="AugmentSelectionDirector"/>), 각 피어가 자기 시계를 따로 세면
        /// 그 순간부터 호스트와 클라이언트에 다른 숫자가 보인다.
        /// </summary>
        [Networked] private float NetVoteSeconds { get; set; }

        private AugmentSelectionDirector _selection;
        private readonly int[] _cardBuffer = new int[MaxCards];
        private readonly int[] _voteBuffer = new int[MaxCards];

        public override void Spawned()
        {
            _selection = FindAnyObjectByType<AugmentSelectionDirector>(FindObjectsInactive.Include);
            if (_selection == null)
            {
                Debug.LogError($"{nameof(AugmentNetHub)}: 씬에 {nameof(AugmentSelectionDirector)} 가 " +
                               "없다. 증강이 아무에게도 안 뜬다.");
                return;
            }

            Debug.Log($"[{nameof(AugmentNetHub)}] 붙었다 — 권위={Object.HasStateAuthority}");
        }

        /// <summary>
        /// 허브가 사라지면 클라이언트의 화면도 닫는다. 안 닫으면 카드가 뜬 채로 남는다 —
        /// 판정은 서버가 갖고 있으므로 클라이언트에는 스스로 닫을 근거가 없다.
        /// </summary>
        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_selection == null) return;
            if (Object != null && Object.IsValid && Object.HasStateAuthority) return;
            _selection.PresentReplicated(false, null, 0);
        }

        /// <summary>
        /// 서버가 표를 걷고 상태를 복제한다. <b>모든 피어가 들어오지만 쓰는 것은 서버뿐이다.</b>
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _selection == null) return;

            CollectVotes();

            NetOpen = _selection.IsOpen;
            NetDayIndex = _selection.DayIndexForDisplay;
            NetWinningCard = _selection.WinningCardIndex;
            NetWinnerWasTie = _selection.WinnerWasTie;
            NetVoteSeconds = _selection.IntermissionRemainingSeconds;

            for (int card = 0; card < MaxCards; card++)
                NetCards.Set(card, _selection.PoolIndexOfCard(card));
            for (int slot = 0; slot < SessionLauncher.MaxPlayerId; slot++)
                NetVotes.Set(slot, _selection.VoteOf(slot));
        }

        /// <summary>
        /// 입력 비트에서 표를 읽는다. 같은 표가 여러 틱 들어와도 해롭지 않다 —
        /// <see cref="AugmentSelectionDirector.SubmitVote"/> 가 같은 값을 덮어쓸 뿐이다.
        /// </summary>
        private void CollectVotes()
        {
            if (!_selection.IsOpen || Runner == null) return;

            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                if (!Runner.TryGetInputForPlayer(player, out NetworkInputData input)) continue;

                int pick = AugmentVoteTally.NoVote;
                if (input.Buttons.IsSet((int)EInputButton.AugmentPick0)) pick = 0;
                else if (input.Buttons.IsSet((int)EInputButton.AugmentPick1)) pick = 1;
                else if (input.Buttons.IsSet((int)EInputButton.AugmentPick2)) pick = 2;
                if (pick == AugmentVoteTally.NoVote) continue;

                _selection.SubmitVote(player.PlayerId, pick);
            }
        }

        /// <summary>
        /// <b>복제 값이 사본을 먹인다 — 이 방향뿐이다.</b> 권위는 자기 상태를 이미 갖고 있으므로
        /// 여기서 아무것도 하지 않는다.
        /// </summary>
        public override void Render()
        {
            if (_selection == null) return;

            // 표 집계는 권위도 그린다. 호스트 모드에서는 서버가 곧 플레이어라
            // 여기서 같이 빠지면 방장 화면에만 표가 안 보인다.
            PresentVotes();

            if (Object != null && Object.IsValid && Object.HasStateAuthority) return;

            for (int card = 0; card < MaxCards; card++) _cardBuffer[card] = NetCards.Get(card);
            _selection.PresentReplicated(NetOpen, _cardBuffer, NetDayIndex);
        }

        /// <summary>
        /// 복제된 표를 세어 화면으로 넘긴다. <b>새 네트워크 상태를 만들지 않는다</b> —
        /// <see cref="NetVotes"/> 는 이미 모두에게 가 있으므로 세는 일은 각 피어가 하면 된다.
        ///
        /// <para>인원은 <c>Runner.ActivePlayers</c> 로 센다. 나간 사람 자리에 남은 옛 표를
        /// 세지 않으려면 <see cref="NetVotes"/> 를 훑는 것이 아니라 지금 있는 사람을 훑어야 한다 —
        /// <see cref="AugmentVoteTally.Resolve"/> 가 <c>eligible</c> 을 받는 이유와 같다.</para>
        /// </summary>
        private void PresentVotes()
        {
            if (Runner == null) return;

            for (int card = 0; card < MaxCards; card++) _voteBuffer[card] = 0;

            int total = 0;
            int voted = 0;
            foreach (PlayerRef player in Runner.ActivePlayers)
            {
                total++;
                int pick = VoteOf(player.PlayerId);
                if (pick < 0 || pick >= MaxCards) continue;
                voted++;
                _voteBuffer[pick]++;
            }

            // 내 표는 이 피어의 PlayerId 로 읽는다. 데디케이티드 서버에는 로컬 플레이어가
            // 없으므로(SessionLauncher.HostServerOnly) 그때는 표시할 내 표도 없다.
            int localPick = Runner.LocalPlayer.IsRealPlayer
                ? VoteOf(Runner.LocalPlayer.PlayerId)
                : AugmentVoteTally.NoVote;

            _selection.PresentVotes(new AugmentVoteDisplay(
                _voteBuffer, voted, total, localPick,
                NetWinningCard, NetWinnerWasTie, NetVoteSeconds));
        }

        /// <summary>그 자리가 낸 표. 화면이 "누가 뭘 골랐나" 를 그릴 때 읽는다.</summary>
        public int VoteOf(int slot) =>
            slot < 0 || slot >= SessionLauncher.MaxPlayerId
                ? AugmentVoteTally.NoVote
                : NetVotes.Get(slot);

        /// <summary>이긴 카드. 아직 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int WinningCard => NetWinningCard;
    }
}
