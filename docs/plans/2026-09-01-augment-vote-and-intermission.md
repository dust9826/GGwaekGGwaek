# 증강 투표와 쉬는 시간 구현 계획

> **에이전트 작업자에게:** 이 계획은 태스크 단위로 실행한다. 체크박스(`- [ ]`)로 진행을 표시한다.

**Goal:** 증강이 호스트에게만 뜨던 것을 전원 투표(다수결)로 바꾸고, 그 과정에서 `Time.timeScale = 0`
정지를 **싱글에서도** 걷어내 "쉬는 시간" 으로 대체한다.

**Architecture:** `AugmentSelectionDirector` 가 권위에서 전부 굴리고(추첨 · 쉬는 시간 타이머 · 표
집계), 새 `AugmentNetHub` 가 그 상태를 `[Networked]` 로 복사한다. 클라이언트는 허브만 읽는다 —
`MissionNetHub` 와 같은 문장이다. 싱글은 허브 없이 디렉터만 돌고, 그 분기는 `StageSession.IsAuthority`
가 이미 갖고 있다. 집계 규칙은 **순수 static** 으로 떼어 EditMode 로 전부 덮는다.

**Tech Stack:** Unity 6000.6.0b7 · Photon Fusion 2 (Host Mode) · UI Toolkit ·
Unity Test Framework(EditMode/PlayMode) · Plastic SCM

**Spec:** `docs/specs/2026-09-01-augment-vote-and-intermission.md`

## Global Constraints

- **git 을 쓰지 않는다.** 버전 관리는 Plastic SCM (`cm`). `git` 명령은 이 저장소에서 금지다.
- **체크인은 태스크마다 하지 않는다.** 이 계획은 **딜리버러블 4개**로 묶는다 — 아래 "체크인 계획" 참고.
- **네임스페이스는 `PPack` 하나.** 폴더나 어셈블리를 따라가지 않는다.
- private 필드는 `_camelCase`, 타입·메서드는 `PascalCase`, enum 타입명은 `E` 접두.
- **직렬화된 Unity Object 필드만 `== null` / `!= null`**, 나머지는 `is null` / `is not null`.
- **새 추상화는 두 번째 호출처가 확인된 뒤에만.** 이 계획에는 인터페이스를 만들지 않는다.
- **`EInputButton` 의 기존 비트 값을 재사용하지 않는다.** 새 버튼은 뒤에 붙인다 — 비트가 곧 와이어 포맷.
- **RPC 금지.** Fusion 위버가 심는 internal 호출 때문에 런타임 `MethodAccessException` 이 난다(실측).
- 씬 편집은 언제나 `SinglePlay.unity` 에서. `MultiPlay.unity` 는 빌더 산출물이다.
- **씬에 손으로 놓지 않는다.** `SnowDeliverySceneBuilder` / `MultiPlaySceneBuilder` 가 씬을 매번 다시
  조립하므로 손으로 놓은 것은 다음 빌더 실행에 사라진다(cs:913 의 교훈).
- **`.asset` / `.prefab` / `.unity` YAML 을 직접 편집하지 않는다.** 에디터나 `eval` 로 만들고 되읽어 확인.
- **테스트 씬을 Build Settings 에 넣지 않는다.**
- 테스트 후 유니티 원복: Play Mode off, 원래 씬 활성, dirty 없음, `(Clone)`·`__TEST__` 잔여물 없음.

### CLI

```bash
U=/Users/dust9826/.unity/bin/unity
P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2   # 작업 워크스페이스 경로로 바꿔 쓴다
```

**판정 순서를 지킨다** — `recompile` → `recompile_status` 가 `completed` + `failed:false` → 그제서야
테스트 결과를 믿는다. 비포커스 에디터는 낡은 어셈블리로 통과시킨다.

⚠ **`eval` / `eval_file` 은 `using` 디렉티브를 못 받는다.** 완전 수식 이름을 쓴다
(`UnityEditor.AssetDatabase` 등).

⚠ **PlayMode CLI 수집기는 에디터 세션당 한 번**이다. 그 뒤로는 0건이 나온다. 배치를 아껴 짜고,
예산을 다 쓰면 에디터를 재시작한다.

---

## 파일 구조

| 경로 | 책임 |
|---|---|
| `Assets/Game/InGame/Augment/Scripts/AugmentVoteTally.cs` (신규) | 표 집계. **순수 static** |
| `Assets/Game/InGame/Augment/Scripts/AugmentSelectionDirector.cs` (수정) | 정지 제거 · 쉬는 시간 타이머 · 표 수집 · 확정 |
| `Assets/Game/InGame/Augment/Scripts/AugmentNetHub.cs` (신규) | 디렉터 상태를 `[Networked]` 로 복사 |
| `Assets/Game/InGame/Augment/Scripts/AugmentNetSpawner.cs` (신규) | 서버가 허브를 한 번 스폰 |
| `Assets/Game/InGame/Augment/Scripts/AugmentPickInputRelay.cs` (신규) | 클릭 → 입력 비트 펄스 |
| `Assets/Game/InGame/Augment/Prefabs/PF_AugmentHub.prefab` (신규) | 허브 `NetworkObject` |
| `Assets/Game/Core/Multiplay/Scripts/NetworkInputData.cs` (수정) | `AugmentPick0/1/2` = 14/15/16 |
| `Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs` (수정) | 펄스를 비트로 싣는다 |
| `Assets/Game/InGame/Delivery/Scripts/RequestDirector.cs` (수정) | 쉬는 시간 게이트 |
| `Assets/Game/InGame/Cleanliness/Editor/SnowDeliverySceneBuilder.cs` (수정) | 게이트 참조 배선 |
| `Assets/Game/InGame/Cleanliness/Editor/MultiPlaySceneBuilder.cs` (수정) | `BuildAugmentSpawner()` |
| `Assets/Game/InGame/Augment/Tests/EditMode/AugmentVoteTallyTests.cs` (신규) | 집계 규칙 전부 |
| `Assets/Game/InGame/Augment/Tests/PlayMode/AugmentSelectionPlayModeTests.cs` (수정) | 정지 단언 → 쉬는 시간 단언 |
| `Assets/Game/InGame/Augment/AGENTS.md` (수정) | 폴더 규칙 갱신 |
| `docs/INDEX.md` (수정) | 현재 상태 한 줄 |

**`AugmentSelectionView` 는 안 고친다.** 클릭 콜백(`onPick`)의 시그니처가 그대로이고, 그 콜백이
싱글이면 확정으로, 클라이언트면 펄스로 갈리는 것은 **디렉터가 정한다.** 뷰는 판정하지 않는다는
기존 규약 그대로다.

## 체크인 계획

| # | 딜리버러블 | 태스크 |
|---|---|---|
| A | 설계 — 스펙과 계획 | (이 문서 + 스펙) |
| B | 쉬는 시간 — 싱글에서 완결된다 | 1 · 2 · 3 |
| C | 멀티 투표 — 복제와 입력 | 4 · 5 · 6 |
| D | 문서 | 7 |

**B 하나로 싱글이 완결된다**는 것이 이 순서의 이유다. C 가 없어도 게임은 돌고, 정지가 사라진 자리를
쉬는 시간이 메운 것을 혼자 확인할 수 있다.

---

### Task 1: 표 집계를 순수 함수로

**Files:**
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentVoteTally.cs`
- Test: `Assets/Game/InGame/Augment/Tests/EditMode/AugmentVoteTallyTests.cs`

**Interfaces:**
- Consumes: 없음 (순수 static, 프로젝트 타입에 의존하지 않는다)
- Produces: `AugmentVoteTally.NoVote` (const int = -1) ·
  `AugmentVoteTally.Resolve(IReadOnlyList<int> votes, IReadOnlyList<bool> eligible, int cardCount, System.Random random) -> int`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/Game/InGame/Augment/Tests/EditMode/AugmentVoteTallyTests.cs`:

```csharp
using System;
using System.Collections.Generic;
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
            int picked = Resolve(votes);
            Assert.That(picked, Is.InRange(0, 2));
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
```

- [ ] **Step 2: 실패하는 것을 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
# recompile_status 폴링 → completed
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter AugmentVoteTally --async_tests true
```

기대: 컴파일 에러 — `AugmentVoteTally` 가 없다.

- [ ] **Step 3: 최소 구현을 쓴다**

`Assets/Game/InGame/Augment/Scripts/AugmentVoteTally.cs`:

```csharp
using System;
using System.Collections.Generic;

namespace PPack
{
    /// <summary>
    /// 표를 세어 이긴 카드의 인덱스를 고른다. <b>순수 static</b> — 규칙 전체가 EditMode 로 덮인다.
    ///
    /// <para>동점은 <paramref name="random"/> 이 푼다. 이것을 부르는 쪽이 <b>권위 하나뿐</b>이라
    /// 결정론이 깨지지 않는다 — 뽑는 것은 서버이고 결과만 복제된다(스펙 §8).</para>
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
        /// 한 번은 받는다"</b> 가 설계 의도이고, 기권으로 그것을 깨지 않는다.</para>
        /// </summary>
        public static int Resolve(IReadOnlyList<int> votes, IReadOnlyList<bool> eligible,
            int cardCount, Random random)
        {
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

            if (cast == 0) return random.Next(cardCount);

            int highest = 0;
            for (int index = 1; index < cardCount; index++)
                if (counts[index] > counts[highest]) highest = index;

            // 동점자를 모아 그중에서 뽑는다. 가장 낮은 인덱스를 쓰면 첫 카드가 구조적으로 유리해진다.
            var tied = new List<int>();
            for (int index = 0; index < cardCount; index++)
                if (counts[index] == counts[highest]) tied.Add(index);

            return tied.Count == 1 ? highest : tied[random.Next(tied.Count)];
        }
    }
}
```

- [ ] **Step 4: 통과하는 것을 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
# recompile_status → completed / failed:false
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter AugmentVoteTally --async_tests true
# test_status 폴링
```

기대: **9/9 Passed.**

- [ ] **Step 5: 체크인하지 않는다** — 딜리버러블 B 의 일부다. Task 3 까지 끝내고 한 번에 넣는다.

---

### Task 2: 정지를 걷어내고 쉬는 시간을 넣는다

**Files:**
- Modify: `Assets/Game/InGame/Augment/Scripts/AugmentSelectionDirector.cs`
- Modify: `Assets/Game/InGame/Augment/Tests/PlayMode/AugmentSelectionPlayModeTests.cs:12,15,20,63,80,135`

**Interfaces:**
- Consumes: `AugmentVoteTally.Resolve(...)` · `AugmentVoteTally.NoVote` (Task 1) ·
  `StageSession.For(GameObject) -> StageSession` · `SessionLauncher.MaxPlayers` (const int = 4)
- Produces:
  - `AugmentSelectionDirector.IsOpen -> bool` (기존, 유지)
  - `AugmentSelectionDirector.IntermissionRemainingSeconds -> float`
  - `AugmentSelectionDirector.SubmitVote(int slot, int cardIndex) -> void`
  - `AugmentSelectionDirector.VoteOf(int slot) -> int`
  - `AugmentSelectionDirector.CardIndexOf(AugmentDefinition) -> int`
  - `AugmentSelectionDirector.WinningCardIndex -> int` (닫힌 뒤의 결과, 없으면 `NoVote`)
  - 삭제: `Pause()` · `Resume()` · `_resumeTimeScale`

**⚠ 이 태스크가 기존 테스트 셋을 깬다.** 지우지 말고 **새 전제로 다시 쓴다** — 정지가 하던 일을
쉬는 시간이 대신하는지가 이 작업의 핵심 단언이다.

- [ ] **Step 1: 깨질 테스트를 먼저 새 전제로 다시 쓴다**

`AugmentSelectionPlayModeTests.cs` 에서 `_timeScaleBefore` 필드(`:12`)와 `SetUp`(`:15`) ·
`TearDown`(`:20`) 의 `Time.timeScale` 저장·복원을 **지운다.** 아무도 `timeScale` 을 안 만지므로
저장할 것이 없다. 그리고 세 단언을 바꾼다:

```csharp
// :63 이었던 것 — "열면 멈춘다" → "열면 쉬는 시간이 시작된다"
Assert.That(director.IsOpen, Is.True);
Assert.That(director.IntermissionRemainingSeconds, Is.GreaterThan(0f));
Assert.That(Time.timeScale, Is.EqualTo(1f), "증강은 더 이상 시간을 멈추지 않는다");

// :80 이었던 것 — "확정하면 풀린다" → "확정하면 쉬는 시간이 끝난다"
Assert.That(director.IsOpen, Is.False);
Assert.That(director.IntermissionRemainingSeconds, Is.EqualTo(0f));

// :135 도 같은 모양으로 바꾼다.
```

- [ ] **Step 2: 실패하는 것을 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter AugmentSelection --async_tests true
```

기대: 컴파일 에러 — `IntermissionRemainingSeconds` 가 없다.

- [ ] **Step 3: 디렉터를 고친다 — 정지 삭제**

`Pause()` · `Resume()` · `_resumeTimeScale` 필드를 **통째로 지운다.** `Open()` 의 `Pause();` 호출과
`Close()` 의 `Resume();` 호출도 지운다. `_input` 필드는 **남긴다** — 아래 Step 5 가 다시 쓴다.

- [ ] **Step 4: 쉬는 시간 타이머와 표를 넣는다**

`AugmentSelectionDirector` 에 추가한다:

```csharp
        [Tooltip("증강을 고르는 쉬는 시간(초). 이 동안 새 의뢰가 안 나오고 기존 의뢰의 제한시간도 멈춘다. " +
                 "전원이 표를 내면 그전에 끝난다.")]
        [SerializeField, Min(1f)] private float _intermissionSeconds = 20f;

        private readonly int[] _votes = new int[SessionLauncher.MaxPlayers];
        private readonly bool[] _eligible = new bool[SessionLauncher.MaxPlayers];

        /// <summary>남은 쉬는 시간(초). 닫혀 있으면 0. 화면과 <see cref="RequestDirector"/> 가 읽는다.</summary>
        public float IntermissionRemainingSeconds { get; private set; }

        /// <summary>닫힌 뒤 실제로 이긴 카드의 인덱스. 아직 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int WinningCardIndex { get; private set; } = AugmentVoteTally.NoVote;

        /// <summary>이번에 제시한 카드 중 몇 번인가. 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int CardIndexOf(AugmentDefinition definition)
        {
            int index = definition == null ? -1 : _cards.IndexOf(definition);
            return index < 0 ? AugmentVoteTally.NoVote : index;
        }

        /// <summary>그 자리의 표. 안 냈으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int VoteOf(int slot) =>
            slot < 0 || slot >= _votes.Length ? AugmentVoteTally.NoVote : _votes[slot];

        /// <summary>
        /// 한 자리의 표를 기록한다. <b>권위만 부른다</b> — 싱글은 뷰의 콜백이, 멀티는 서버가
        /// 입력 비트를 읽어서 부른다. 창이 닫힐 때까지 몇 번이든 바꿀 수 있다.
        /// </summary>
        public void SubmitVote(int slot, int cardIndex)
        {
            if (!IsOpen) return;
            if (slot < 0 || slot >= _votes.Length) return;
            if (cardIndex < 0 || cardIndex >= _cards.Count) return;

            _votes[slot] = cardIndex;
            _eligible[slot] = true;
            if (AllEligibleVoted()) ResolveAndClose();
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
```

`Open()` 을 고친다 — 표를 비우고 타이머를 세운다:

```csharp
            IsOpen = true;
            WinningCardIndex = AugmentVoteTally.NoVote;
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
```

`MarkEligibleSlots()` — 싱글은 자리 0 하나뿐이다. 멀티는 Task 6 이 접속자로 채운다:

```csharp
        /// <summary>표를 셀 자리를 표시한다. 싱글은 0번 하나다.</summary>
        private void MarkEligibleSlots()
        {
            _eligible[0] = true;
        }
```

타이머:

```csharp
        private void Update()
        {
            if (!IsOpen) return;

            IntermissionRemainingSeconds =
                Mathf.Max(0f, IntermissionRemainingSeconds - Time.deltaTime);
            if (IntermissionRemainingSeconds > 0f) return;
            if (Session.IsAuthority) ResolveAndClose();
        }
```

⚠ **`Time.deltaTime`(스케일 적용)을 쓴다.** 싱글에서 ESC 로 멈추면 쉬는 시간도 같이 멈추는 것이
일관적이다. 지금은 `PauseMenuController:90` 가드가 그 경우를 막지만, 가드가 사라져도 동작이
망가지지 않게 둔다.

집계와 닫기:

```csharp
        /// <summary>표를 세어 확정하고 닫는다. <b>권위만 부른다.</b></summary>
        private void ResolveAndClose()
        {
            if (!IsOpen) return;

            _random ??= new System.Random(Environment.TickCount);
            int winner = AugmentVoteTally.Resolve(_votes, _eligible, _cards.Count, _random);
            WinningCardIndex = winner;
            if (winner >= 0 && winner < _cards.Count && _loadout != null)
                _loadout.Add(_cards[winner]);
            Close();
        }
```

`Close()` 는 `Resume()` 대신 타이머와 커서를 정리한다:

```csharp
        private void Close()
        {
            IsOpen = false;
            IntermissionRemainingSeconds = 0f;
            if (_view != null) _view.Hide();
            _cards.Clear();
            HoldCursor(false);
            Closed?.Invoke();
        }
```

- [ ] **Step 5: 커서를 푼다**

`Confirm(AugmentDefinition)` 은 **공개 API 로 남긴다** — 테스트가 쓴다. 다만 몸통을 표 기록으로 바꾼다:

```csharp
        /// <summary>테스트와 뷰가 쓰는 진입점. 이번에 제시하지 않은 것은 무시한다.</summary>
        public void Confirm(AugmentDefinition picked) => OnPicked(picked);

        private void OnPicked(AugmentDefinition definition)
        {
            int cardIndex = CardIndexOf(definition);
            if (cardIndex == AugmentVoteTally.NoVote) return;
            SubmitVote(LocalVoteSlot, cardIndex);
        }

        /// <summary>이 피어의 표 자리. 싱글은 0이다. 멀티는 Task 6 이 PlayerId 로 바꾼다.</summary>
        private int LocalVoteSlot => 0;
```

커서는 **직접 만지지 않는다.** `PenguinCameraOrbit` 을 끄면 그쪽 `OnDisable` 이 잠금을 푼다 —
일시정지 메뉴가 쓰는 그 수단이다(cs:907).

```csharp
        private PenguinCameraOrbit _heldOrbit;

        /// <summary>
        /// 고르는 동안 커서를 푼다. <b>커서를 직접 만지지 않는다</b> —
        /// <see cref="PenguinCameraOrbit"/> 를 끄면 그쪽 <c>OnDisable</c> 이 잠금을 푼다(cs:907).
        /// 대가는 그동안 카메라 회전이 죽는 것이고, 쉬는 시간이라 받아들인다(스펙 §9).
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
```

⚠ **`_input.enabled = false` 를 되살리지 마라.** 쉬는 시간에는 계속 움직일 수 있어야 한다(스펙 §2).
`_input` 필드는 인스펙터 배선이 남아 있어 지우지 않지만, 이 클래스는 더 이상 그것을 끄지 않는다.

- [ ] **Step 6: 통과하는 것을 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile   # → completed / failed:false
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --async_tests true
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter Augment --async_tests true
```

기대: EditMode 전체 통과(회귀 없음) · `AugmentSelection` PlayMode 통과 ·
`Time.timeScale` 을 단언하는 곳이 남아 있지 않다.

- [ ] **Step 7: 체크인하지 않는다** — Task 3 까지가 딜리버러블 B 다.

---

### Task 3: 쉬는 시간이 의뢰를 멈춘다

**Files:**
- Modify: `Assets/Game/InGame/Delivery/Scripts/RequestDirector.cs:83-91`
- Modify: `Assets/Game/InGame/Cleanliness/Editor/SnowDeliverySceneBuilder.cs` (`BuildAugmentRig` 안, `:947` 옆)
- Test: `Assets/Game/InGame/Augment/Tests/PlayMode/AugmentIntermissionPlayModeTests.cs` (신규)

**Interfaces:**
- Consumes: `AugmentSelectionDirector.IsOpen` (Task 2)
- Produces: `RequestDirector.SetIntermission(AugmentSelectionDirector) -> void`

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/Game/InGame/Augment/Tests/PlayMode/AugmentIntermissionPlayModeTests.cs`:

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class AugmentIntermissionPlayModeTests
    {
        [UnityTest]
        public IEnumerator NoNewRequestsDuringIntermission()
        {
            AugmentIntermissionRig rig = AugmentIntermissionRig.Build();
            int before = rig.Director.ActiveCount;

            rig.Selection.OpenForTest();
            yield return new WaitForFixedUpdate();
            for (int i = 0; i < 200; i++) yield return new WaitForFixedUpdate();

            Assert.That(rig.Director.ActiveCount, Is.EqualTo(before),
                "쉬는 시간 동안 의뢰가 새로 나왔다");
            rig.Dispose();
        }

        [UnityTest]
        public IEnumerator RequestTtlIsFrozenDuringIntermission()
        {
            AugmentIntermissionRig rig = AugmentIntermissionRig.Build();
            GiftRequest request = rig.Director.SpawnRequest(0, EGiftBoxKind.Small);
            Assert.That(request, Is.Not.Null, "테스트 리그가 의뢰를 만들지 못했다");
            float remaining = request.RemainingSeconds;

            rig.Selection.OpenForTest();
            for (int i = 0; i < 100; i++) yield return new WaitForFixedUpdate();

            Assert.That(request.RemainingSeconds, Is.EqualTo(remaining).Within(0.001f));
            rig.Dispose();
        }

        [UnityTest]
        public IEnumerator TtlResumesAfterIntermission()
        {
            AugmentIntermissionRig rig = AugmentIntermissionRig.Build();
            GiftRequest request = rig.Director.SpawnRequest(0, EGiftBoxKind.Small);
            rig.Selection.OpenForTest();
            rig.Selection.Confirm(rig.Selection.Cards[0]);   // 1인이므로 전원 투표 = 즉시 닫힘
            float remaining = request.RemainingSeconds;

            for (int i = 0; i < 50; i++) yield return new WaitForFixedUpdate();

            Assert.That(request.RemainingSeconds, Is.LessThan(remaining));
            rig.Dispose();
        }
    }
}
```

⚠ **`AugmentIntermissionRig` 는 이 태스크가 같이 만드는 테스트 헬퍼다.** 기존
`AugmentSelectionPlayModeTests` 가 리그를 세우는 방식을 그대로 읽어서, `RequestDirector` ·
`GameManager` · `AugmentSelectionDirector` · `AugmentLoadout` · `AugmentPool` 을 코드로 세우고
`SetIntermission` 으로 물린 뒤 `Dispose()` 에서 전부 파괴하는 형태로 쓴다. **씬을 열지 않는다** —
그래야 "테스트 후 원복" 규칙에 걸릴 것이 없다.

- [ ] **Step 2: 실패하는 것을 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter AugmentIntermission --async_tests true
```

기대: 컴파일 에러 — `SetIntermission` 이 없다.

- [ ] **Step 3: 게이트를 넣는다**

`RequestDirector` 에 추가:

```csharp
        [Tooltip("증강 쉬는 시간. 열려 있는 동안 새 의뢰가 안 나오고 기존 의뢰의 제한시간도 멈춘다. " +
                 "비어 있으면 게이트가 없는 것과 같다 — 증강을 안 놓은 씬과 테스트가 영향받지 않는다.")]
        [SerializeField] private AugmentSelectionDirector _intermission;

        /// <summary>쉬는 시간 디렉터를 꽂는다. 씬에서는 빌더가 채우고, 테스트는 이것을 쓴다.</summary>
        public void SetIntermission(AugmentSelectionDirector intermission) =>
            _intermission = intermission;

        /// <summary>지금 쉬는 시간인가. 참조가 비어 있으면 언제나 거짓이다.</summary>
        private bool IsIntermission => _intermission != null && _intermission.IsOpen;
```

`FixedUpdate()` 를 고친다:

```csharp
        private void FixedUpdate()
        {
            if (!_running) return;
            float delta = Time.fixedDeltaTime;
            _elapsed += delta;

            // 쉬는 시간에는 새 의뢰가 안 나오고 제한시간도 안 준다. 완료는 계속 받는다 —
            // 쉬는 중에 배달을 마치는 것을 막을 이유가 없다(스펙 §3).
            if (!IsIntermission)
            {
                TickSpawns(delta);
                TickRequests(delta);
            }

            TickCompletions();
        }
```

⚠ **`_elapsed` 는 계속 는다.** 그것은 판 전체의 경과 시간이고 쉬는 시간도 판의 일부다.

⚠ **클라이언트에서는 이 게이트가 돌지 않는다.** `MissionNetHub.Spawned()` 가 클라에서
`_director.enabled = false` 로 이 컴포넌트를 끈다. 쉬는 시간은 서버·싱글의 문제다.

- [ ] **Step 4: 빌더가 배선하게 한다**

`SnowDeliverySceneBuilder.BuildAugmentRig()` 의 `SetSerialized(director, "_augments", loadout);`
(`:947`) 바로 아래에 한 줄 넣는다:

```csharp
            SetSerialized(director, "_intermission", selection);
```

⚠ `selection` 은 그 함수가 이미 만든 `AugmentSelectionDirector` 다. `return selection;` 앞에 넣는다.

- [ ] **Step 5: 통과하는 것을 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile   # → completed / failed:false
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter AugmentIntermission --async_tests true
```

기대: **3/3 Passed.**

- [ ] **Step 6: 씬을 다시 찍고 배선을 되읽어 확인한다**

```bash
$U cmd --project-path "$P" --no-banner menu --path "PPack/Cleanliness/Build SinglePlay Scene"
```

그리고 `eval` 로 `RequestDirector._intermission` 이 비어 있지 않은지 **되읽어 확인한다.**
빌더가 채웠다고 믿지 않는다.

- [ ] **Step 7: 체크인 B — "쉬는 시간"**

```bash
cm status                      # Changed 목록을 만든다
cm checkout <수정된 파일들>     # Changed 는 체크아웃해야 ci 가 받는다
cm ci --commentsfile=<파일>     # .meta 도 함께 명시한다
cm status                      # 남은 것이 없는지 확인
```

코멘트에 담을 것: 정지를 왜 버렸는지(피어 로컬 · 실측 근거), 쉬는 시간이 무엇을 멈추고 무엇을
안 멈추는지, 기존 테스트 셋을 왜 지우지 않고 다시 썼는지, 싱글의 체감 변화.

---

### Task 4: 클릭을 입력 비트로 싣는다

**Files:**
- Modify: `Assets/Game/Core/Multiplay/Scripts/NetworkInputData.cs:86` (enum 끝) 과 파일 끝(릴레이)
- Modify: `Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs:580-581` 옆

**Interfaces:**
- Produces: `EInputButton.AugmentPick0 = 14` · `AugmentPick1 = 15` · `AugmentPick2 = 16` ·
  `AugmentPickInputRelay.Queue(int cardIndex)` · `AugmentPickInputRelay.ActiveIndex -> int`
  (없으면 `-1`)

- [ ] **Step 1: 비트를 뒤에 붙인다**

`EInputButton` 의 `RequestRestartMatch = 13` **다음에** 넣는다:

```csharp
        /// <summary>
        /// 증강 카드 0·1·2 를 골랐다. <b>뒤에 붙였다</b> — 비트가 곧 와이어 포맷이라 기존 값 사이에
        /// 끼워 넣지 않는다.
        ///
        /// <para>⚠ <b>비트가 셋이므로 카드 수가 3으로 고정된다.</b>
        /// <c>AugmentSelectionDirector._cardCount</c> 를 4로 올리면 넷째 카드에 실을 비트가 없어
        /// <b>멀티에서만 조용히 못 고르는 카드</b>가 생긴다. 늘리려면 여기에 비트를 더 붙이는
        /// 것이지 기존 셋을 재해석하는 것이 아니다.</para>
        /// </summary>
        AugmentPick0 = 14,
        AugmentPick1 = 15,
        AugmentPick2 = 16,
```

- [ ] **Step 2: 카드 수 상한을 3으로 좁힌다**

`AugmentSelectionDirector._cardCount` 의 어트리뷰트를 바꾼다:

```csharp
        [Tooltip("한 번에 보여 줄 카드 수. 풀이 모자라면 있는 만큼만 나온다. " +
                 "⚠ 3이 상한이다 — EInputButton 의 AugmentPick 비트가 셋뿐이라 넷째 카드는 " +
                 "멀티에서 고를 수 없다.")]
        [SerializeField, Range(1, 3)] private int _cardCount = 3;
```

- [ ] **Step 3: 펄스 릴레이를 만든다**

`NetworkInputData.cs` 끝, `CoopShoveInputRelay` **바로 아래**에 둔다. 같은 종류의 것을 같은 곳에 둔다:

```csharp
    /// <summary>
    /// 증강 카드 클릭을 Fusion 입력 수집기로 넘기는 짧은 펄스. <see cref="CoopShoveInputRelay"/> 와
    /// 같은 모양이고 같은 이유다 — 클릭은 한 프레임짜리 사건인데 입력은 틱마다 모아 보내므로,
    /// 짧게 유지해서 다음 수집에 반드시 한 번 실리게 한다.
    ///
    /// <para>펄스가 여러 틱에 걸쳐 실려도 해롭지 않다 — 서버의
    /// <c>AugmentSelectionDirector.SubmitVote</c> 는 같은 표를 다시 받아도 같은 값을 덮어쓸 뿐이다.</para>
    /// </summary>
    public static class AugmentPickInputRelay
    {
        private const float PulseSeconds = 0.25f;
        private static float _activeUntil = -1f;
        private static int _index = -1;

        /// <summary>지금 실어야 할 카드 인덱스. 없으면 -1.</summary>
        public static int ActiveIndex => Time.unscaledTime <= _activeUntil ? _index : -1;

        public static void Queue(int cardIndex)
        {
            _index = cardIndex;
            _activeUntil = Time.unscaledTime + PulseSeconds;
        }

        /// <summary>도메인 리로드가 꺼져 있어 지난 Play 의 값이 살아남는다.</summary>
        public static void Reset()
        {
            _index = -1;
            _activeUntil = -1f;
        }
    }
```

⚠ **`SessionLauncher.ResetStatics` 에 `AugmentPickInputRelay.Reset()` 을 넣는다.**
`DisableDomainReload` 라 지난 Play 의 값이 살아남는다 — `PlayerLeft` 이벤트가 같은 이유로 거기 있다.

- [ ] **Step 4: 수집기가 비트를 세우게 한다**

`SessionLauncher.OnInput` 의 `CoopShove` 두 줄(`:580-581`) 아래에 넣는다:

```csharp
            int augmentPick = AugmentPickInputRelay.ActiveIndex;
            data.Buttons.Set((int)EInputButton.AugmentPick0, augmentPick == 0);
            data.Buttons.Set((int)EInputButton.AugmentPick1, augmentPick == 1);
            data.Buttons.Set((int)EInputButton.AugmentPick2, augmentPick == 2);
```

- [ ] **Step 5: 컴파일과 회귀를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile   # → completed / failed:false
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --async_tests true
```

기대: 컴파일 에러 0 · EditMode 회귀 없음. **이 태스크에는 새 테스트가 없다** — 순수 배선이고,
실제 단언은 Task 6 의 2인스턴스 실측이다.

- [ ] **Step 6: 체크인하지 않는다** — Task 6 까지가 딜리버러블 C 다.

---

### Task 5: 허브와 스포너

**Files:**
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentNetHub.cs`
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentNetSpawner.cs`
- Create: `Assets/Game/InGame/Augment/Prefabs/PF_AugmentHub.prefab`
- Modify: `Assets/Game/InGame/Cleanliness/Editor/MultiPlaySceneBuilder.cs` (`BuildMissionSpawner` 옆)

**Interfaces:**
- Consumes: `AugmentSelectionDirector.IsOpen` · `.IntermissionRemainingSeconds` · `.Cards` ·
  `.WinningCardIndex` · `.SubmitVote(int,int)` (Task 2) · `EInputButton.AugmentPick0/1/2` (Task 4)
- Produces: `AugmentNetHub.MaxCards` (const int = 3) · `AugmentNetSpawner` (씬 컴포넌트)

- [ ] **Step 1: 허브를 쓴다**

```csharp
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
    /// 모았는지 보여 주는 화면이 생기면 그때 이 결정을 뒤집는다(스펙 §6).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AugmentNetHub : NetworkBehaviour
    {
        /// <summary>비트가 셋이라 카드도 셋이다(<see cref="EInputButton.AugmentPick0"/>).</summary>
        public const int MaxCards = 3;

        [Networked] private NetworkBool NetOpen { get; set; }
        [Networked] private int NetDayIndex { get; set; }
        [Networked, Capacity(MaxCards)] private NetworkArray<int> NetCards { get; }
        [Networked, Capacity(SessionLauncher.MaxPlayers)] private NetworkArray<int> NetVotes { get; }
        [Networked] private int NetWinningCard { get; set; }

        private AugmentSelectionDirector _selection;
        private readonly int[] _cardBuffer = new int[MaxCards];

        public override void Spawned()
        {
            _selection = FindAnyObjectByType<AugmentSelectionDirector>(FindObjectsInactive.Include);
            if (_selection == null)
            {
                Debug.LogError($"{nameof(AugmentNetHub)}: 씬에 {nameof(AugmentSelectionDirector)} 가 없다. " +
                               "증강이 아무에게도 안 뜬다.");
                return;
            }

            _selection.BindNetHub(this);
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            if (_selection != null) _selection.BindNetHub(null);
        }

        /// <summary>
        /// 서버가 표를 걷고 상태를 복제한다. <b>모든 피어가 이 메서드에 들어오지만 쓰는 것은 서버뿐이다.</b>
        /// </summary>
        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _selection == null) return;

            CollectVotes();

            NetOpen = _selection.IsOpen;
            NetDayIndex = _selection.DayIndexForDisplay;
            NetWinningCard = _selection.WinningCardIndex;

            for (int card = 0; card < MaxCards; card++)
                NetCards.Set(card, _selection.PoolIndexOfCard(card));
            for (int slot = 0; slot < SessionLauncher.MaxPlayers; slot++)
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
            if (Object != null && Object.IsValid && Object.HasStateAuthority) return;

            for (int card = 0; card < MaxCards; card++) _cardBuffer[card] = NetCards.Get(card);
            _selection.PresentReplicated(NetOpen, _cardBuffer, NetDayIndex);
        }

        /// <summary>그 자리가 낸 표. 화면이 "누가 뭘 골랐나" 를 그릴 때 읽는다.</summary>
        public int VoteOf(int slot) =>
            slot < 0 || slot >= SessionLauncher.MaxPlayers
                ? AugmentVoteTally.NoVote
                : NetVotes.Get(slot);

        /// <summary>이긴 카드. 아직 없으면 <see cref="AugmentVoteTally.NoVote"/>.</summary>
        public int WinningCard => NetWinningCard;
    }
}
```

- [ ] **Step 2: 스포너를 쓴다**

`MissionNetSpawner` 를 그대로 본뜬다. **그 파일의 함정 둘이 그대로 적용된다** — 주석까지 옮겨 적는다.

```csharp
using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 게임플레이 씬이 <see cref="AugmentNetHub"/> 를 스폰하는 자리.
    /// <see cref="MissionNetSpawner"/> 와 같은 모양이고 같은 이유다 — <c>Core</c> 의 런처가 이
    /// 프리팹을 알면 <c>Core</c> 가 <c>InGame</c> 의 증강을 아는 것이 된다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AugmentNetSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject _hubPrefab;

        private bool _requested;

        private void Update()
        {
            if (_requested) return;

            // ⚠ <b>매치가 시작된 뒤에만 스폰한다</b>(2026-08-26 실측, MissionNetSpawner 와 같은 함정).
            // 러너는 방을 연 순간부터 서버이므로 그 조건만 보면 로비에서 스폰하고, 곧이어
            // StartMatch 의 씬 로드가 그것을 삼킨다 — 콘솔에 아무 오류도 안 남는다.
            if (SessionLauncher.Phase != ESessionPhase.Playing) return;

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;

            if (_hubPrefab == null)
            {
                _requested = true;
                Debug.LogError($"{nameof(AugmentNetSpawner)}: 허브 프리팹이 비어 있다. 증강이 안 뜬다.");
                return;
            }

            // 스폰이 큐를 거치면 반환값이 null 이므로, 반환값이 아니라 요청 자체로 한 번을 보장한다.
            _requested = true;
            runner.Spawn(_hubPrefab);
        }
    }
}
```

- [ ] **Step 3: 프리팹을 만든다**

⚠ **YAML 을 직접 쓰지 않는다.** `eval` 로 만들고 **되읽어 확인한다**:

```
GameObject "PF_AugmentHub" 하나
  + NetworkObject
  + AugmentNetHub
→ PrefabUtility.SaveAsPrefabAsset("Assets/Game/InGame/Augment/Prefabs/PF_AugmentHub.prefab")
```

되읽어 확인할 것: `GetComponent<NetworkObject>() != null` · `GetComponent<AugmentNetHub>() != null`.

- [ ] **Step 4: 빌더가 씬에 놓게 한다**

`MultiPlaySceneBuilder` 에 상수와 함수를 넣고 `Build()` 에서 `BuildMissionSpawner(...)` 바로 뒤에 부른다:

```csharp
        private const string AugmentHubPrefabPath =
            "Assets/Game/InGame/Augment/Prefabs/PF_AugmentHub.prefab";

        /// <summary>증강 허브를 스폰할 자리. 미션 허브와 같은 이유로 씬이 프리팹 참조를 든다.</summary>
        private static void BuildAugmentSpawner(Transform parent)
        {
            var spawnerObject = new GameObject("AugmentNetSpawner");
            spawnerObject.transform.SetParent(parent);
            AugmentNetSpawner spawner = spawnerObject.AddComponent<AugmentNetSpawner>();

            NetworkObject hub = AssetDatabase.LoadAssetAtPath<GameObject>(AugmentHubPrefabPath)
                ?.GetComponent<NetworkObject>();
            if (hub == null)
                throw new InvalidOperationException($"증강 허브 프리팹이 없다: {AugmentHubPrefabPath}");

            var serialized = new SerializedObject(spawner);
            serialized.FindProperty("_hubPrefab").objectReferenceValue = hub;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }
```

- [ ] **Step 5: 컴파일만 확인한다** — 배선의 진짜 단언은 Task 6 이다.

```bash
$U cmd --project-path "$P" --no-banner recompile   # → completed / failed:false
```

---

### Task 6: 디렉터를 멀티에 연결한다

**Files:**
- Modify: `Assets/Game/InGame/Augment/Scripts/AugmentSelectionDirector.cs`

**Interfaces:**
- Consumes: `AugmentNetHub` (Task 5) · `AugmentPickInputRelay.Queue(int)` (Task 4)
- Produces: `AugmentSelectionDirector.BindNetHub(AugmentNetHub)` ·
  `.PresentReplicated(bool, IReadOnlyList<int>, int)` · `.PoolIndexOfCard(int) -> int` ·
  `.DayIndexForDisplay -> int`

- [ ] **Step 1: 풀 인덱스 변환과 표시용 일차를 연다**

```csharp
        /// <summary>머리말이 읽는 일차. 허브가 복제한다.</summary>
        public int DayIndexForDisplay => _dayIndex;

        /// <summary>
        /// 이번에 제시한 <paramref name="card"/> 번째 카드가 풀에서 몇 번인가. 없으면 -1.
        /// <b>카드는 시드가 아니라 인덱스로 복제한다</b> — 시드로 하면 결과가 같으려면
        /// <c>owned</c> 목록까지 같아야 하고 그것도 복제해야 한다(스펙 §6).
        /// </summary>
        public int PoolIndexOfCard(int card)
        {
            if (_pool == null || card < 0 || card >= _cards.Count) return -1;
            return System.Array.IndexOf(_pool.Entries, _cards[card]);
        }
```

- [ ] **Step 2: 허브 바인딩과 클라이언트 표시를 넣는다**

```csharp
        private AugmentNetHub _netHub;
        private bool _replicatedOpen;

        /// <summary>허브가 스폰·디스폰될 때 자기를 넣고 뺀다. 없으면 싱글처럼 돈다.</summary>
        public void BindNetHub(AugmentNetHub hub) => _netHub = hub;

        /// <summary>
        /// <b>복제된 상태를 화면에 옮긴다 — 클라이언트 전용이고 판정하지 않는다.</b>
        /// 카드는 풀 인덱스로 오므로 여기서 되살린다. 커서도 여기서 잡고 푼다 —
        /// 화면을 여는 곳이 하나여야 커서 소유자도 하나다.
        /// </summary>
        public void PresentReplicated(bool open, IReadOnlyList<int> poolIndices, int dayIndex)
        {
            if (open == _replicatedOpen) return;
            _replicatedOpen = open;

            if (!open)
            {
                IsOpen = false;
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

            if (_cards.Count == 0) return;

            IsOpen = true;
            _dayIndex = dayIndex;
            HoldCursor(true);
            if (_view != null) _view.Show(_cards, _dayIndex, OnPicked);
        }
```

- [ ] **Step 3: 표 자리를 멀티로 넓힌다**

Task 2 의 `MarkEligibleSlots()` 와 `LocalVoteSlot` 을 바꾼다:

```csharp
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

        /// <summary>이 피어의 표 자리. 싱글은 0이다.</summary>
        private int LocalVoteSlot
        {
            get
            {
                NetworkRunner runner = Session.Runner;
                return runner is null ? 0 : runner.LocalPlayer.PlayerId;
            }
        }
```

⚠ **`PlayerId` 가 `MaxPlayers` 를 넘으면 그 표가 조용히 버려진다.** `SessionLobby` 의 닉네임 배열이
같은 색인을 쓰므로 그쪽 용량과 맞는지 **읽어서 확인하고**, 다르면 큰 쪽에 맞춘다.

- [ ] **Step 4: 클릭이 멀티에서는 펄스로 가게 한다**

Task 2 의 `OnPicked` 를 바꾼다:

```csharp
        private void OnPicked(AugmentDefinition definition)
        {
            int cardIndex = CardIndexOf(definition);
            if (cardIndex == AugmentVoteTally.NoVote) return;

            // 싱글은 러너가 없으므로 바로 기록한다. 멀티는 <b>호스트도 이 길로 간다</b> —
            // 서버가 자기 입력도 TryGetInputForPlayer 로 읽으므로 경로가 하나로 유지된다.
            if (Session.Runner is null)
            {
                SubmitVote(LocalVoteSlot, cardIndex);
                return;
            }

            AugmentPickInputRelay.Queue(cardIndex);
        }
```

- [ ] **Step 5: 나간 사람의 표를 뺀다**

```csharp
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
        private void OnPlayerLeft(int playerId)
        {
            if (!IsOpen) return;
            if (playerId < 0 || playerId >= _eligible.Length) return;

            _eligible[playerId] = false;
            _votes[playerId] = AugmentVoteTally.NoVote;
            if (Session.IsAuthority && AllEligibleVoted()) ResolveAndClose();
        }
```

⚠ **`SessionLauncher.PlayerLeft` 의 실제 시그니처를 읽고 맞춘다.** `PlayerRef` 를 넘길 수도 있다 —
`PlayerLeftAnnouncer` 가 그것을 구독하고 있으므로 거기서 확인한다.

- [ ] **Step 6: 게임이 끝났으면 열지 않는다**

`OnDayAdvanced` 에 가드를 더한다:

```csharp
        private void OnDayAdvanced(int dayIndex)
        {
            if (!Session.IsAuthority) return;
            if (_manager != null && _manager.Phase != EGamePhase.Playing) return;
            _dayIndex = dayIndex;
            Open();
        }
```

`_manager` 는 새 `[SerializeField] private GameManager _manager;` 이고 빌더가 채운다
(`SnowDeliverySceneBuilder.BuildAugmentRig` 는 이미 `manager` 를 인자로 받는다).
⚠ **`EGamePhase` 의 실제 이름을 읽어서 맞춘다** — `MissionNetHub` 가 `EGamePhase.Ended` 를 쓴다.

- [ ] **Step 7: 씬을 다시 찍고 실측한다**

```bash
$U cmd --project-path "$P" --no-banner menu --path "PPack/Cleanliness/Build SinglePlay Scene"
$U cmd --project-path "$P" --no-banner menu --path "PPack/Cleanliness/Build MultiPlay Scene"
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --async_tests true
```

그리고 **MPPM 2인스턴스 실측** — 이 태스크의 진짜 단언이다:

| 보는 것 | 통과 기준 |
|---|---|
| 일차가 넘어간다 | **양쪽 화면에 같은 카드 3장**이 뜬다 |
| 양쪽에서 커서 | 풀린다. 닫히면 다시 잠긴다 |
| 서로 다른 카드를 고른다 | 동점 → 둘 중 하나가 나온다(무작위) |
| 같은 카드를 고른다 | 그 카드가 이긴다 |
| 한쪽만 고른다 | **전원 투표가 아니므로 20초를 기다렸다가** 그 표로 확정된다 |
| 쉬는 시간 | 양쪽에서 새 의뢰가 안 나온다 · 기존 의뢰 시간이 안 준다 |
| 효과 | 서버에서 `WalkSpeed` · `Reward` 가 실제로 오른다 |

⚠ **끝나면 MPPM `host` 태그를 지운다.** 남으면 러너가 `DontDestroyOnLoad` 라 SinglePlay 까지
세션이 따라오고 증상이 한 씬 떨어져 나온다.

- [ ] **Step 8: 체크인 C — "멀티 투표"**

코멘트에 담을 것: 왜 로드아웃을 복제하지 않는지(소비처 넷이 전부 서버 전용 — 표로), 카드를 시드가
아니라 인덱스로 보내는 이유, 비트 셋이 카드 수를 3으로 고정한다는 것, 실측 결과.

---

### Task 7: 문서

**Files:**
- Modify: `Assets/Game/InGame/Augment/AGENTS.md`
- Modify: `docs/INDEX.md`
- Create: `docs/Session_Summary_20260901_augment-vote.md`

- [ ] **Step 1: 폴더 규칙을 갱신한다**

`Augment/AGENTS.md` 에서 **틀린 것이 된 문장을 고친다** — 지우지 말고 정정 이력을 남긴다:

- *"정지는 `AugmentSelectionDirector` 만 한다"* 절 → **"아무도 정지하지 않는다"** 로 바꾸고,
  `timeScale` 을 쓰던 이유와 왜 버렸는지, 쉬는 시간이 무엇을 대신하는지를 적는다.
- *"⚠ 이것은 갚아야 할 빚이다"* → **갚았다**고 적고 무엇으로 갚았는지 쓴다.
- 새 절: **"카드 수는 3이 상한이다"** — 입력 비트가 근거.
- 새 절: **"로드아웃은 복제하지 않는다"** — 소비처 넷의 표와, 이 결정을 뒤집을 트리거.
- 알려진 한계에서 *"멀티 배선은 범위 밖"* 을 지우고 실제 상태로 바꾼다.

- [ ] **Step 2: `docs/INDEX.md` 에 현재 상태 한 줄**

기존 증강 항목 **아래에** 새 항목을 넣는다. 링크는 스펙·계획·세션 요약·폴더 규칙 넷.

- [ ] **Step 3: 세션 요약을 쓴다**

`wrap-session` 스킬을 쓴다. ⚠ **요약만 쓰고 끝내지 않는다** — `docs/INDEX.md` 는 팀원과 정면으로
부딪히는 파일이라 오래 들고 있을수록 위험하다. 쓰고 바로 체크인한다.

- [ ] **Step 4: 체크인 D — "문서"**

---

## 자체 검토 결과

**스펙 커버리지** — §1 Task 6 Step 6 · §2 Task 2 · §3 Task 3 · §4 Task 1 · §5 Task 5 ·
§6 Task 5·6 · §7 Task 4 · §8 Task 1 · §9 Task 2 Step 5 · §10 Task 2 · §11 각 태스크의 마지막 단계 ·
§12 Task 2 Step 1 · §13 범위 밖. **빈 곳 없음.**

**타입 일관성** — `AugmentVoteTally.NoVote`(-1)를 디렉터·허브가 같은 이름으로 쓴다.
`MaxCards`(허브, 3) 와 `_cardCount`(디렉터, `Range(1,3)`) 와 `AugmentPick0/1/2`(비트 셋)가
같은 수를 가리킨다. `SessionLauncher.MaxPlayers`(4)가 `_votes` · `_eligible` · `NetVotes` 용량이다.

**읽어서 확인해야 할 것 셋** (계획이 단정하지 않는다):
1. `SessionLauncher.PlayerLeft` 의 시그니처 (`int` 인가 `PlayerRef` 인가) — Task 6 Step 5
2. `SessionLobby` 닉네임 배열의 용량 — Task 6 Step 3
3. `EGamePhase` 의 진행 중 상태 이름 — Task 6 Step 6
