using System;
using System.Collections.Generic;
using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>일차가 넘어갈 때 증강을 띄우고 <b>표를 걷어</b> 확정한다.
    ///
    /// <para><b>아무것도 멈추지 않는다</b>(2026-09-01). 이 클래스는 <c>Time.timeScale = 0</c> 과
    /// <c>PenguinInputReader.enabled = false</c> 를 둘 다 잡고 있었고, 폴더 <c>AGENTS.md</c> 가
    /// 그것을 "갚아야 할 빚" 으로 적어 뒀다 — <c>timeScale</c> 은 <b>프로세스 전역</b>이라 서버가
    /// 남의 피어 것을 0으로 만들 수 없기 때문이다. 플래그를 복제해 각자 걸면 원래 문제로 돌아온다:
    /// 그 피어만 멈추고 세션은 계속 돌아 재개하는 순간 자기 화면만 과거에 있다(2026-08-31 실측,
    /// 일시정지 메뉴가 같은 벽에 부딪혔다).
    ///
    /// <para><b>대신 쉬는 시간을 둔다.</b> 새 의뢰가 안 나오고 기존 의뢰의 제한시간도 멈추지만,
    /// 완료는 되고 펭귄은 계속 움직인다 — 정지가 하던 일의 본질은 압박을 없애는 것이었고 그것은
    /// 타이머로 된다. <b>싱글도 같다</b>(스펙 §2·§3·§10).</para></para>
    ///
    /// <para><b>추첨과 확정은 권위에서만 돈다.</b> 싱글에서는 <see cref="StageSession.IsAuthority"/>
    /// 가 항상 참이라 같은 코드가 그대로 돈다(스펙 §2).</para>
    ///
    /// <para><b>창은 둘 중 먼저 오는 쪽에 닫힌다</b> — 전원이 표를 냈거나, 쉬는 시간이 끝났거나.
    /// 그래서 싱글에서는 클릭이 곧 확정이고 이것은 예전 동작 그대로다(스펙 §8).</para></summary>
    [DisallowMultipleComponent]
    public sealed class AugmentSelectionDirector : MonoBehaviour
    {
        [SerializeField] private TimeOfDayDirector _timeOfDay;
        [SerializeField] private AugmentLoadout _loadout;
        [SerializeField] private AugmentPool _pool;
        [SerializeField] private AugmentSelectionView _view;
        [SerializeField] private PenguinInputReader _input;

        [Tooltip("판이 끝난 뒤에는 열지 않는다. 비어 있으면 그 가드가 없는 것과 같다.")]
        [SerializeField] private GameManager _manager;

        [Tooltip("한 번에 보여 줄 카드 수. 풀이 모자라면 있는 만큼만 나온다. " +
                 "⚠ 3이 상한이다 — EInputButton 의 AugmentPick 비트가 셋뿐이라 넷째 카드는 " +
                 "멀티에서 고를 수 없다.")]
        [SerializeField, Range(1, 3)] private int _cardCount = 3;

        [Tooltip("증강을 고르는 쉬는 시간(초). 이 동안 새 의뢰가 안 나오고 기존 의뢰의 제한시간도 " +
                 "멈춘다. 전원이 표를 내면 그전에 끝난다.")]
        [SerializeField, Min(1f)] private float _intermissionSeconds = 20f;

        /// <summary>전원이 표를 낸 뒤 남기는 유예. 이 동안에는 표를 바꿀 수 있다.</summary>
        [SerializeField, Min(0f)] private float _voteGraceSeconds = 3f;

        /// <summary>이긴 카드를 보여주는 시간. 이것이 끝나야 판이 다시 돈다.</summary>
        [SerializeField, Min(0f)] private float _revealSeconds = 1.5f;

        private readonly List<AugmentDefinition> _cards = new();
        private System.Random _random;
        private StageSession _session;
        private bool _sessionResolved;
        private PenguinCameraOrbit _heldOrbit;

        // ⚠ 용량은 MaxPlayers(4)가 아니라 MaxPlayerId(8)다. PlayerId 는 사람이 나가도 재사용되지
        // 않으므로 동시 인원보다 커질 수 있고, 4로 잡으면 그 표가 조용히 버려진다.
        private readonly int[] _votes = new int[SessionLauncher.MaxPlayerId];
        private readonly bool[] _eligible = new bool[SessionLauncher.MaxPlayerId];

        private bool _replicatedOpen;

        /// <summary>머리말에만 쓴다. 판정은 이 값을 보지 않는다.</summary>
        private int _dayIndex;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<AugmentDefinition> Cards => _cards;

        /// <summary>남은 쉬는 시간(초). 닫혀 있으면 0. 화면과 <c>RequestDirector</c> 가 읽는다.</summary>
        public float IntermissionRemainingSeconds { get; private set; }

        /// <summary>닫힌 뒤 실제로 이긴 카드의 인덱스. 아직 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int WinningCardIndex { get; private set; } = AugmentVoteTally.NoVote;

        /// <summary>이긴 카드가 <b>동점을 무작위로 푼 결과</b>인가. 화면이 그것을 밝혀야 한다.</summary>
        public bool WinnerWasTie { get; private set; }

        /// <summary>
        /// 이긴 카드를 보여주는 동안 남은 시간. <b>권위에서만 흐른다</b> —
        /// 클라이언트는 <c>NetOpen</c> 이 꺼질 때까지 화면을 띄우고 있으면 되므로
        /// 같은 시계를 따로 돌릴 이유가 없다.
        /// </summary>
        public float RevealRemainingSeconds { get; private set; }

        /// <summary>결과를 보여주는 중인가. 이때는 표를 더 받지 않는다.</summary>
        public bool IsRevealing => RevealRemainingSeconds > 0f;

        /// <summary>이번에 제시한 카드 중 몇 번인가. 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int CardIndexOf(AugmentDefinition definition)
        {
            int index = definition == null ? -1 : _cards.IndexOf(definition);
            return index < 0 ? AugmentVoteTally.NoVote : index;
        }

        /// <summary>그 자리의 표. 안 냈으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int VoteOf(int slot) =>
            slot < 0 || slot >= _votes.Length ? AugmentVoteTally.NoVote : _votes[slot];

        /// <summary>머리말이 읽는 일차. 허브가 복제한다.</summary>
        public int DayIndexForDisplay => _dayIndex;

        /// <summary>
        /// 이번에 제시한 <paramref name="card"/> 번째 카드가 풀에서 몇 번인가. 없으면 -1.
        /// <b>카드는 시드가 아니라 인덱스로 복제한다</b> — 시드로 하면 결과가 같으려면 <c>owned</c>
        /// 목록까지 같아야 하고 그것도 복제해야 한다(스펙 §6).
        /// </summary>
        public int PoolIndexOfCard(int card)
        {
            if (_pool == null || card < 0 || card >= _cards.Count) return -1;
            return Array.IndexOf(_pool.Entries, _cards[card]);
        }

        /// <summary>
        /// <b>복제된 상태를 화면에 옮긴다 — 클라이언트 전용이고 판정하지 않는다.</b>
        /// 카드는 풀 인덱스로 오므로 여기서 되살린다. 커서도 여기서 잡고 푼다 — 화면을 여는 곳이
        /// 하나여야 커서 소유자도 하나다.
        /// </summary>
        public void PresentReplicated(bool open, IReadOnlyList<int> poolIndices, int dayIndex)
        {
            if (open == _replicatedOpen) return;
            _replicatedOpen = open;

            if (!open)
            {
                IsOpen = false;
                IntermissionRemainingSeconds = 0f;
                _cards.Clear();
                if (_view != null) _view.Hide();
                HoldCursor(false);
                return;
            }

            _cards.Clear();
            if (_pool != null && poolIndices is not null)
            {
                for (int index = 0; index < poolIndices.Count; index++)
                {
                    int poolIndex = poolIndices[index];
                    if (poolIndex < 0 || poolIndex >= _pool.Entries.Length) continue;
                    AugmentDefinition definition = _pool.Entries[poolIndex];
                    if (definition != null) _cards.Add(definition);
                }
            }

            if (_cards.Count == 0)
            {
                _replicatedOpen = false;
                return;
            }

            IsOpen = true;
            _dayIndex = dayIndex;
            HoldCursor(true);
            if (_view != null) _view.Show(_cards, _dayIndex, OnPicked);
        }

        /// <summary>
        /// 표 집계를 화면으로 넘긴다. <b>표시 전용이고 판정에는 쓰지 않는다</b> —
        /// 이기는 카드는 여전히 권위가 <see cref="AugmentVoteTally.Resolve"/> 로 정한다.
        /// </summary>
        public void PresentVotes(in AugmentVoteDisplay display)
        {
            if (_view != null) _view.SetVotes(display);
        }

        public event Action Opened;
        public event Action Closed;

        /// <summary>테스트가 씬 배선 없이 세운다. 시드를 받아 추첨을 결정론으로 만든다.</summary>
        public void ConfigureForTest(AugmentLoadout loadout, AugmentPool pool, int cardCount, int seed)
        {
            _loadout = loadout;
            _pool = pool;
            _cardCount = cardCount;
            _random = new System.Random(seed);
        }

        /// <summary>테스트가 쉬는 시간을 짧게 줄여 만료를 본다.</summary>
        public void SetIntermissionSecondsForTest(float seconds) => _intermissionSeconds = seconds;

        /// <summary>테스트가 일차 넘어감 없이 연다.</summary>
        public void OpenForTest() => Open();

        private void OnEnable()
        {
            Subscribe();
            SessionLauncher.PlayerLeft -= OnPlayerLeft;
            SessionLauncher.PlayerLeft += OnPlayerLeft;
        }

        private void OnDisable()
        {
            Unsubscribe();
            SessionLauncher.PlayerLeft -= OnPlayerLeft;
        }

        /// <summary>나간 사람은 분모에서 뺀다. 안 빼면 남은 사람이 타이머를 다 기다린다.</summary>
        private void OnPlayerLeft(PlayerRef player)
        {
            if (!IsOpen) return;

            int slot = player.PlayerId;
            if (slot < 0 || slot >= _eligible.Length) return;

            _eligible[slot] = false;
            _votes[slot] = AugmentVoteTally.NoVote;
            if (Session.IsAuthority && AllEligibleVoted()) OnEveryoneVoted();
        }

        private void Subscribe()
        {
            if (_timeOfDay == null) return;
            _timeOfDay.DayAdvanced -= OnDayAdvanced;
            _timeOfDay.DayAdvanced += OnDayAdvanced;
        }

        private void Unsubscribe()
        {
            if (_timeOfDay == null) return;
            _timeOfDay.DayAdvanced -= OnDayAdvanced;
        }

        /// <summary>인덱스 조건이 없다. <c>DayIndex</c> 는 0에서 시작하므로 첫 넘어감이 1이고,
        /// 그것이 화면상 2일차의 시작이다 — <c>&gt;= 2</c> 를 걸면 첫 증강을 통째로
        /// 건너뛴다(스펙 §3).</summary>
        private void OnDayAdvanced(int dayIndex)
        {
            if (!Session.IsAuthority) return;

            // 결과 화면 위로 카드가 뜨면 정렬 순서와 커서가 동시에 엉킨다.
            // MissionNetHub.PollRestartRequests() 가 Ended 를 확인하는 것과 같은 종류의 가드다.
            if (_manager != null && _manager.Phase != EGamePhase.Playing) return;

            _dayIndex = dayIndex;
            Open();
        }

        /// <summary>러너 조회가 들어 있어 한 번만 받고 캐시한다.</summary>
        private StageSession Session
        {
            get
            {
                if (_sessionResolved) return _session;
                _session = StageSession.For(gameObject);
                _sessionResolved = true;
                return _session;
            }
        }

        private void Open()
        {
            if (IsOpen) return;

            _random ??= new System.Random(Environment.TickCount);
            AugmentDraft.Draw(
                _pool != null ? _pool.Entries : null,
                _loadout != null ? _loadout.Owned : null,
                _cardCount, _random, _cards);

            if (_cards.Count == 0) return;

            IsOpen = true;
            WinningCardIndex = AugmentVoteTally.NoVote;
            WinnerWasTie = false;
            RevealRemainingSeconds = 0f;
            IntermissionRemainingSeconds = _intermissionSeconds;
            for (int slot = 0; slot < _votes.Length; slot++)
            {
                _votes[slot] = AugmentVoteTally.NoVote;
                _eligible[slot] = false;
            }

            MarkEligibleSlots();
            HoldCursor(true);
            if (_view != null) _view.Show(_cards, _dayIndex, OnPicked);
            Opened?.Invoke();
        }

        /// <summary>표를 셀 자리를 표시한다. 싱글은 0번 하나, 멀티는 지금 접속한 사람 전부.</summary>
        private void MarkEligibleSlots()
        {
            NetworkRunner runner = Session.Runner;
            if (runner is null)
            {
                _eligible[0] = true;
                return;
            }

            foreach (PlayerRef player in runner.ActivePlayers)
            {
                int slot = player.PlayerId;
                if (slot >= 0 && slot < _eligible.Length) _eligible[slot] = true;
            }
        }

        /// <summary>
        /// 쉬는 시간을 흘린다. <b><c>Time.deltaTime</c>(스케일 적용)을 쓴다</b> — 싱글에서 ESC 로
        /// 멈추면 쉬는 시간도 같이 멈추는 것이 일관적이다. 지금은
        /// <c>PauseMenuController</c> 의 가드가 그 경우를 막지만, 가드가 사라져도 망가지지 않게 둔다.
        /// </summary>
        private void Update()
        {
            if (!IsOpen) return;

            if (IsRevealing)
            {
                RevealRemainingSeconds = Mathf.Max(0f, RevealRemainingSeconds - Time.deltaTime);
                if (RevealRemainingSeconds > 0f) return;
                if (Session.IsAuthority) Close();
                return;
            }

            IntermissionRemainingSeconds =
                Mathf.Max(0f, IntermissionRemainingSeconds - Time.deltaTime);
            if (IntermissionRemainingSeconds > 0f) return;
            if (Session.IsAuthority) ResolveAndClose();
        }

        /// <summary>
        /// 한 자리의 표를 기록한다. <b>권위만 부른다</b> — 싱글은 뷰의 콜백이, 멀티는 서버가 입력
        /// 비트를 읽어서 부른다. 창이 닫힐 때까지 몇 번이든 바꿀 수 있다.
        /// </summary>
        public void SubmitVote(int slot, int cardIndex)
        {
            if (!IsOpen || IsRevealing) return;
            if (slot < 0 || slot >= _votes.Length) return;
            if (cardIndex < 0 || cardIndex >= _cards.Count) return;

            _votes[slot] = cardIndex;
            _eligible[slot] = true;
            if (AllEligibleVoted()) OnEveryoneVoted();
        }

        /// <summary>
        /// 전원이 표를 냈다. <b>바로 닫지 않고 타이머를 유예까지 당긴다</b> — 그 동안 표를 바꿀 수
        /// 있고, 무엇보다 결과가 나오기 전에 남들이 뭘 골랐는지 볼 틈이 생긴다.
        /// 2026-09-01 전까지는 여기서 곧바로 확정하고 닫았다.
        ///
        /// <para><b>혼자 하는 판은 예외다.</b> 거기서 "전원 투표" 는 자기가 카드를 누른 그 순간이라,
        /// 방금 누른 것을 3초 기다렸다 다시 보여줄 이유가 없다. 유예도 공개도 두지 않는다.</para>
        /// </summary>
        private void OnEveryoneVoted()
        {
            if (EligibleCount() < 2)
            {
                ResolveAndClose();
                return;
            }

            IntermissionRemainingSeconds =
                Mathf.Min(IntermissionRemainingSeconds, _voteGraceSeconds);
        }

        private int EligibleCount()
        {
            int count = 0;
            for (int slot = 0; slot < _eligible.Length; slot++)
                if (_eligible[slot]) count++;
            return count;
        }

        private bool AllEligibleVoted()
        {
            bool any = false;
            for (int slot = 0; slot < _votes.Length; slot++)
            {
                if (!_eligible[slot]) continue;
                any = true;
                if (_votes[slot] == AugmentVoteTally.NoVote) return false;
            }

            return any;
        }

        /// <summary>
        /// 표를 세어 확정하고, 보여줄 시간이 있으면 그만큼 열어 둔 채로 둔다.
        /// <b>권위만 부른다.</b>
        /// </summary>
        private void ResolveAndClose()
        {
            if (!IsOpen) return;

            _random ??= new System.Random(Environment.TickCount);
            int winner = AugmentVoteTally.Resolve(
                _votes, _eligible, _cards.Count, _random, out bool wasTie);
            WinningCardIndex = winner;
            WinnerWasTie = wasTie;
            if (winner >= 0 && winner < _cards.Count && _loadout != null)
                _loadout.Add(_cards[winner]);

            // 확정한 카드는 NetWinningCard 로 이미 복제된다. 그래서 공개 단계에 새 상태가
            // 필요 없다 — 화면은 "열려 있는데 승자가 정해졌다" 를 결과 단계로 읽으면 된다.
            RevealRemainingSeconds = EligibleCount() >= 2 ? _revealSeconds : 0f;
            if (!IsRevealing) Close();
        }

        /// <summary>테스트와 뷰가 쓰는 진입점. 이번에 제시하지 않은 것은 무시한다.</summary>
        public void Confirm(AugmentDefinition picked) => OnPicked(picked);

        private void OnPicked(AugmentDefinition definition)
        {
            int cardIndex = CardIndexOf(definition);
            if (cardIndex == AugmentVoteTally.NoVote) return;

            // 싱글은 러너가 없으므로 바로 기록한다. 멀티는 <b>호스트도 이 길로 간다</b> —
            // 서버가 자기 입력도 TryGetInputForPlayer 로 읽으므로 표를 걷는 경로가 하나로 유지된다.
            if (Session.Runner is null)
            {
                SubmitVote(LocalVoteSlot, cardIndex);
                return;
            }

            AugmentPickInputRelay.Queue(cardIndex);
        }

        /// <summary>이 피어의 표 자리. 싱글은 0이다.</summary>
        private int LocalVoteSlot
        {
            get
            {
                NetworkRunner runner = Session.Runner;
                return runner is null ? 0 : runner.LocalPlayer.PlayerId;
            }
        }

        private void Close()
        {
            IsOpen = false;
            IntermissionRemainingSeconds = 0f;
            RevealRemainingSeconds = 0f;
            if (_view != null) _view.Hide();
            _cards.Clear();
            HoldCursor(false);
            Closed?.Invoke();
        }

        /// <summary>
        /// 고르는 동안 커서를 푼다. <b>커서를 직접 만지지 않는다</b> —
        /// <see cref="PenguinCameraOrbit"/> 를 끄면 그쪽 <c>OnDisable</c> 이 잠금을 푼다(cs:907).
        /// 대가는 그동안 카메라 회전이 죽는 것이고, 쉬는 시간이라 받아들인다(스펙 §9).
        ///
        /// <para>⚠ <b><c>_input.enabled = false</c> 를 되살리지 마라.</b> 쉬는 시간에는 계속 움직일
        /// 수 있어야 한다(스펙 §2). 2026-09-01 전까지 이 클래스는 <c>Time.timeScale = 0</c> 과 입력
        /// 차단을 둘 다 했는데, 전자가 피어 로컬이라 멀티에서 못 쓰는 것이 근본 문제였다.</para>
        /// </summary>
        private void HoldCursor(bool hold)
        {
            if (hold)
            {
                _heldOrbit = FindFirstObjectByType<PenguinCameraOrbit>(FindObjectsInactive.Exclude);
                if (_heldOrbit != null) _heldOrbit.enabled = false;
                return;
            }

            if (_heldOrbit != null) _heldOrbit.enabled = true;
            _heldOrbit = null;
        }
    }
}
