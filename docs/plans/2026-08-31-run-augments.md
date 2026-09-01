# 런 증강 구현 계획

> **에이전트 작업자에게:** 이 계획은 태스크 단위로 실행한다. 체크박스(`- [ ]`)로 진행을 표시한다.

**Goal:** 하루가 넘어갈 때 증강 3장을 띄우고 하나를 고르게 하며, 고른 효과가 다섯 소비처에 실제로 걸리게 한다.

**Architecture:** 증강 하나는 SO 한 장이고, 효과는 `EAugmentStat` 별 **가산 누적**이다. `AugmentLoadout`(판에
하나인 MonoBehaviour)이 합산값을 들고, 소비처가 자기 계산에 그 값을 곱하거나 확률로 쓴다. 추첨과 확정은
`StageSession.IsAuthority` 에서만 돈다 — 싱글에서는 항상 참이라 지금 동작 그대로이고, 멀티가 붙는 날 게이트가
이미 서 있다.

**Tech Stack:** Unity 6000.6.0b7 · UI Toolkit · Unity Test Framework(EditMode/PlayMode) · Plastic SCM

**Spec:** `docs/specs/2026-08-31-run-augments.md`

## Global Constraints

- **git 을 쓰지 않는다.** 버전 관리는 Plastic SCM (`cm`). `git` 명령은 이 저장소에서 금지다.
- **체크인은 태스크마다 하지 않는다.** 이 계획은 **딜리버러블 4개**로 묶는다 — 설계 / 증강 코어 / 배선·UI / 문서.
- **네임스페이스는 `PPack` 하나.** 폴더나 어셈블리를 따라가지 않는다.
- private 필드는 `_camelCase`, 타입·메서드는 `PascalCase`, enum 타입명은 `E` 접두.
- **직렬화된 Unity Object 필드만 `== null` / `!= null`**, 나머지는 `is null` / `is not null`.
- **표시 문자열은 영어**(스펙 §9). 이득·패널티의 스탯 이름 표기도 UI 에서는 영어다.
- **`Id` 는 안정 키다.** 문구가 바뀌어도 바꾸지 않는다.
- 씬 편집은 언제나 `SinglePlay.unity` 에서. `MultiPlay.unity` 는 빌더 산출물이다.
- **테스트 씬을 Build Settings 에 넣지 않는다.**
- 테스트 후 유니티 원복: Play Mode off, 원래 씬 활성, dirty 없음, `(Clone)`·`__TEST__` 잔여물 없음.

### CLI

```bash
U=/Users/dust9826/.unity/bin/unity
P=/Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB
```

**판정 순서를 지킨다** — `recompile` → `recompile_status` 가 `completed` + `failed:false` → 그제서야 테스트 결과를
믿는다. 비포커스 에디터는 낡은 어셈블리로 통과시킨다(`Core/Multiplay/AGENTS.md` 실측).

---

## 파일 구조

| 경로 | 책임 |
|---|---|
| `Assets/Game/InGame/Augment/Scripts/EAugmentStat.cs` (신규) | 효과 축 다섯 |
| `Assets/Game/InGame/Augment/Scripts/AugmentEffect.cs` (신규) | `{ Stat, Value }` 직렬화 struct |
| `Assets/Game/InGame/Augment/Scripts/AugmentDefinition.cs` (신규) | 증강 한 장. 이득·패널티·가중치 |
| `Assets/Game/InGame/Augment/Scripts/AugmentPool.cs` (신규) | 추첨 후보 목록 |
| `Assets/Game/InGame/Augment/Scripts/AugmentLoadout.cs` (신규) | 획득 목록 + 스탯 합산. **판에 하나** |
| `Assets/Game/InGame/Augment/Scripts/AugmentDraft.cs` (신규) | 가중치 추첨. 순수 static |
| `Assets/Game/InGame/Augment/Scripts/AugmentSelectionDirector.cs` (신규) | 트리거·게이트·정지·확정. **정지 호출이 여기에만 있다** |
| `Assets/Game/InGame/UI/AugmentSelect/Scripts/AugmentSelectionView.cs` (신규) | 카드 3장 표시와 클릭 |
| `Assets/Game/InGame/UI/AugmentSelect/AugmentSelect.uxml` `.uss` `PanelSettings` (신규) | UI Toolkit 자산 |
| `Assets/Game/InGame/Augment/Data/*.asset` (신규) | 카드 6장 + 풀 |
| `Assets/Game/InGame/Cleanliness/Scripts/GameManager.cs` (수정) | `Reward` · `ClearTimeBonus` |
| `Assets/Game/InGame/Delivery/Scripts/RequestDirector.cs` (수정) | `RequestTtl` |
| `Assets/Game/InGame/Penguin/Scripts/PenguinLocomotion.cs` (수정) | `WalkSpeed` |
| `Assets/Game/InGame/Map/WinterVillage/Scripts/SnowGiftMachinePresentation.cs` (수정) | `ExtraGiftChance` |
| `Assets/Game/InGame/Augment/Tests/EditMode/` (신규) | 합산·추첨 |
| `Assets/Game/InGame/Augment/Tests/PlayMode/` (신규) | 정지·확정·반영 |
| `Assets/Game/InGame/Augment/Tests/Augment_Selection_Test.unity` (신규) | 검증 씬. **Build Settings 금지** |
| `Assets/Game/InGame/Augment/AGENTS.md` (신규) | 폴더 규칙 |

---

### Task 1: 데이터 타입과 로드아웃 합산

**Files:**
- Create: `Assets/Game/InGame/Augment/Scripts/EAugmentStat.cs`
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentEffect.cs`
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentDefinition.cs`
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentLoadout.cs`
- Create: `Assets/Game/InGame/Augment/Tests/EditMode/PPack.Augment.EditModeTests.asmdef`
- Test: `Assets/Game/InGame/Augment/Tests/EditMode/AugmentLoadoutTests.cs`

**Interfaces:**
- Produces: `EAugmentStat`, `AugmentEffect{Stat,Value}`, `AugmentDefinition`(public 필드 `Id/DisplayName/Description/Benefits/Penalties/Weight`), `AugmentLoadout.Add(AugmentDefinition)` · `.GetValue(EAugmentStat)` · `.GetMultiplier(EAugmentStat)` · `.Has(...)` · `.Owned` · `.Changed`

> **`AugmentDefinition` 이 public 필드인 이유:** 이 프로젝트의 밸런스 SO 선례가 그렇다
> (`StageBalanceConfig` 는 전 필드가 public). 인스펙터 튜닝이 목적이고 런타임 변경 지점이 없다.
> 루트 AGENTS.md 의 "`[SerializeField]` 선호" 보다 같은 종류의 기존 자산과 맞추는 쪽을 택한다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`Assets/Game/InGame/Augment/Tests/EditMode/PPack.Augment.EditModeTests.asmdef`:

```json
{
    "name": "PPack.Augment.EditModeTests",
    "rootNamespace": "PPack",
    "references": ["PPack.InGame", "UnityEngine.TestRunner", "UnityEditor.TestRunner"],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": ["nunit.framework.dll"],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

`AugmentLoadoutTests.cs`:

```csharp
using NUnit.Framework;
using UnityEngine;

namespace PPack
{
    public sealed class AugmentLoadoutTests
    {
        private GameObject _host;
        private AugmentLoadout _loadout;

        [SetUp]
        public void SetUp()
        {
            _host = new GameObject("__TEST__AugmentLoadout");
            _loadout = _host.AddComponent<AugmentLoadout>();
        }

        [TearDown]
        public void TearDown() => Object.DestroyImmediate(_host);

        private static AugmentDefinition Make(string id, EAugmentStat stat, float benefit,
            EAugmentStat penaltyStat, float penalty)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Benefits = new[] { new AugmentEffect { Stat = stat, Value = benefit } };
            definition.Penalties = new[] { new AugmentEffect { Stat = penaltyStat, Value = penalty } };
            definition.Weight = 1f;
            return definition;
        }

        [Test]
        public void EmptyLoadoutIsZero()
        {
            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.EqualTo(0f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1f));
        }

        [Test]
        public void BenefitAndPenaltyBothAccumulate()
        {
            _loadout.Add(Make("a", EAugmentStat.Reward, 0.4f, EAugmentStat.RequestTtl, -0.2f));

            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.EqualTo(0.4f).Within(1e-5f));
            Assert.That(_loadout.GetValue(EAugmentStat.RequestTtl), Is.EqualTo(-0.2f).Within(1e-5f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1.4f).Within(1e-5f));
        }

        [Test]
        public void StacksAndCancels()
        {
            _loadout.Add(Make("a", EAugmentStat.WalkSpeed, 0.25f, EAugmentStat.Reward, -0.2f));
            _loadout.Add(Make("b", EAugmentStat.WalkSpeed, 0.10f, EAugmentStat.Reward, 0.2f));

            Assert.That(_loadout.GetValue(EAugmentStat.WalkSpeed), Is.EqualTo(0.35f).Within(1e-5f));
            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.EqualTo(0f).Within(1e-5f));
        }

        [Test]
        public void MultiplierNeverGoesNegative()
        {
            for (int index = 0; index < 6; index++)
                _loadout.Add(Make($"p{index}", EAugmentStat.WalkSpeed, 0f, EAugmentStat.Reward, -0.2f));

            Assert.That(_loadout.GetValue(EAugmentStat.Reward), Is.LessThan(-1f));
            Assert.That(_loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(0f));
        }

        [Test]
        public void AddRaisesChangedAndTracksOwned()
        {
            int raised = 0;
            _loadout.Changed += () => raised++;
            AugmentDefinition definition = Make("a", EAugmentStat.Reward, 0.4f, EAugmentStat.RequestTtl, -0.2f);

            _loadout.Add(definition);

            Assert.That(raised, Is.EqualTo(1));
            Assert.That(_loadout.Has(definition), Is.True);
            Assert.That(_loadout.Owned.Count, Is.EqualTo(1));
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
```

기대: 컴파일 에러 — `AugmentLoadout` · `AugmentDefinition` · `EAugmentStat` 없음.

- [ ] **Step 3: 최소 구현**

`EAugmentStat.cs`:

```csharp
namespace PPack
{
    /// <summary>증강 효과가 꽂히는 축. 값은 언제나 가산 누적이고, 배율로 쓰는 쪽이 1 + 값 으로 읽는다.</summary>
    public enum EAugmentStat
    {
        ClearTimeBonus = 0,
        RequestTtl = 1,
        Reward = 2,
        WalkSpeed = 3,
        ExtraGiftChance = 4,
    }
}
```

`AugmentEffect.cs`:

```csharp
using System;

namespace PPack
{
    /// <summary>스탯 하나에 더할 값. 배율 축은 0.4 가 +40%, 확률 축은 0.3 이 30%p 다.</summary>
    [Serializable]
    public struct AugmentEffect
    {
        public EAugmentStat Stat;
        public float Value;
    }
}
```

`AugmentDefinition.cs`:

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>증강 한 장. <b><see cref="Id"/> 는 안정 키다</b> — 이름·설명은 튜닝 대상이지만
    /// 이것은 아니다. 나중 로컬라이제이션 테이블이 이 값을 건다(스펙 §9).</summary>
    [CreateAssetMenu(menuName = "PPack/Augment/Augment Definition")]
    public sealed class AugmentDefinition : ScriptableObject
    {
        [Tooltip("안정 키. 문구가 바뀌어도 바꾸지 않는다.")]
        public string Id;

        [Tooltip("카드에 뜨는 이름. 영어로 쓴다(스펙 §9).")]
        public string DisplayName;

        [TextArea, Tooltip("카드 한 줄 설명. 영어로 쓴다.")]
        public string Description;

        public AugmentEffect[] Benefits = new AugmentEffect[0];
        public AugmentEffect[] Penalties = new AugmentEffect[0];

        [Tooltip("추첨 가중치. 0이면 안 나온다.")]
        [Min(0f)] public float Weight = 1f;
    }
}
```

`AugmentLoadout.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>이 판이 얻은 증강 전부와 스탯별 합산값.
    ///
    /// <para><b>static 이 아니다.</b> 지금은 판에 하나지만, 소유가 per-player 로 뒤집혀도 붙이는
    /// 위치만 바뀌고 코드는 그대로다(스펙 §2·§5).</para></summary>
    [DisallowMultipleComponent]
    public sealed class AugmentLoadout : MonoBehaviour
    {
        private static readonly int StatCount = Enum.GetValues(typeof(EAugmentStat)).Length;

        private readonly List<AugmentDefinition> _owned = new();
        private readonly float[] _values = new float[StatCount];

        public IReadOnlyList<AugmentDefinition> Owned => _owned;

        /// <summary>획득 목록이 바뀌었다. 소비처는 매번 읽어도 되므로 구독은 선택이다.</summary>
        public event Action Changed;

        /// <summary>가산 합계. 확률 축은 이 값을 그대로 쓴다.</summary>
        public float GetValue(EAugmentStat stat) => _values[(int)stat];

        /// <summary>배율 축이 쓰는 값. <b>0 아래로 내려가지 않는다</b> — 패널티가 겹쳐도 보상이
        /// 음수가 되지는 않는다.</summary>
        public float GetMultiplier(EAugmentStat stat) => Mathf.Max(0f, 1f + _values[(int)stat]);

        public bool Has(AugmentDefinition definition) => definition != null && _owned.Contains(definition);

        public void Add(AugmentDefinition definition)
        {
            if (definition == null) return;

            _owned.Add(definition);
            Accumulate(definition.Benefits);
            Accumulate(definition.Penalties);
            Changed?.Invoke();
        }

        /// <summary>판이 다시 시작할 때 비운다.</summary>
        public void Clear()
        {
            if (_owned.Count == 0) return;
            _owned.Clear();
            Array.Clear(_values, 0, _values.Length);
            Changed?.Invoke();
        }

        private void Accumulate(AugmentEffect[] effects)
        {
            if (effects is null) return;
            for (int index = 0; index < effects.Length; index++)
                _values[(int)effects[index].Stat] += effects[index].Value;
        }
    }
}
```

- [ ] **Step 4: 통과를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter AugmentLoadoutTests
```

기대: `recompile_status` 가 `completed` · `failed:false`, 테스트 5/5 통과.
`run_tests` 인자 이름이 다르면 일부러 틀린 인자를 줘 에러 메시지로 알아낸다(이 프로젝트 CLI 관례).

---

### Task 2: 추첨

**Files:**
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentPool.cs`
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentDraft.cs`
- Test: `Assets/Game/InGame/Augment/Tests/EditMode/AugmentDraftTests.cs`

**Interfaces:**
- Consumes: Task 1 의 `AugmentDefinition`
- Produces: `AugmentPool.Entries` (`AugmentDefinition[]`), `AugmentDraft.Draw(IReadOnlyList<AugmentDefinition> pool, IReadOnlyList<AugmentDefinition> owned, int count, System.Random random, List<AugmentDefinition> results)`

> `Draw` 가 `AugmentLoadout` 이 아니라 `IReadOnlyList` 를 받는 이유: MonoBehaviour 를 인자로 받으면
> 순수 함수가 아니게 되고 EditMode 로 덮기 어려워진다. `StageSession.Resolve` 와 같은 이유다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`AugmentDraftTests.cs`:

```csharp
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
            foreach (AugmentDefinition definition in _created) UnityEngine.Object.DestroyImmediate(definition);
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
            pool.Add(Make("never", 0f));

            for (int seed = 0; seed < 20; seed++)
            {
                AugmentDraft.Draw(pool, Array.Empty<AugmentDefinition>(), 3, new System.Random(seed), _results);
                CollectionAssert.DoesNotContain(_results, pool[3]);
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
```

- [ ] **Step 2: 실패를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
```

기대: 컴파일 에러 — `AugmentDraft` 없음.

- [ ] **Step 3: 최소 구현**

`AugmentPool.cs`:

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>추첨 후보 전부. 프리셋을 나누고 싶으면 에셋을 하나 더 만든다 —
    /// .asset 은 YAML 이라 Plastic 이 병합하지 못한다.</summary>
    [CreateAssetMenu(menuName = "PPack/Augment/Augment Pool")]
    public sealed class AugmentPool : ScriptableObject
    {
        public AugmentDefinition[] Entries = new AugmentDefinition[0];
    }
}
```

`AugmentDraft.cs`:

```csharp
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>증강 추첨. RNG·씬과 무관한 순수 계산이라 EditMode 로 전부 덮인다 —
    /// 시드는 호출자가 넣는다(<see cref="RequestBalance"/> 와 같은 이유).</summary>
    public static class AugmentDraft
    {
        private static readonly List<AugmentDefinition> Candidates = new();

        /// <summary>후보에서 가중치로 <paramref name="count"/> 장을 뽑아
        /// <paramref name="results"/> 에 채운다. 이미 가진 것과 가중치 0은 빠지고,
        /// 후보가 모자라면 있는 만큼만 준다.</summary>
        public static void Draw(IReadOnlyList<AugmentDefinition> pool, IReadOnlyList<AugmentDefinition> owned,
            int count, System.Random random, List<AugmentDefinition> results)
        {
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
```

- [ ] **Step 4: 통과를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter AugmentDraftTests
```

기대: `failed:false`, 테스트 6/6 통과.

- [ ] **Step 5: 체크인 (딜리버러블 2 — 증강 코어)**

```bash
cm add -R Assets/Game/InGame/Augment
cm status --changed --private
cm ci Assets/Game/InGame/Augment --commentsfile=/tmp/ci-augment-core.txt
```

코멘트에 담을 것: 가산 누적을 고른 이유(스택·패널티가 덧셈 하나로 끝난다), `GetMultiplier` 가 0에서
멈추는 이유, `Draw` 가 `AugmentLoadout` 대신 `IReadOnlyList` 를 받는 이유.

---

### Task 3: 선택 디렉터 — 트리거·게이트·정지

**Files:**
- Create: `Assets/Game/InGame/Augment/Scripts/AugmentSelectionDirector.cs`
- Test: `Assets/Game/InGame/Augment/Tests/PlayMode/PPack.Augment.PlayModeTests.asmdef`
- Test: `Assets/Game/InGame/Augment/Tests/PlayMode/AugmentSelectionPlayModeTests.cs`

**Interfaces:**
- Consumes: Task 1·2 전부, `TimeOfDayDirector.DayAdvanced` / `.DayIndex`, `StageSession.For(GameObject)`
- Produces: `AugmentSelectionDirector.IsOpen` · `.Cards` · `.Confirm(AugmentDefinition)` · `.Opened` · `.Closed`

> **`Confirm` 이 public 인 이유:** UI 가 부르고 PlayMode 테스트도 부른다. UI 없이 흐름을 검증할 수
> 있어야 해서 화면과 확정을 분리한다.

- [ ] **Step 1: 실패하는 테스트를 쓴다**

`PPack.Augment.PlayModeTests.asmdef` — Task 1 의 EditMode asmdef 와 같되 `name` 은
`PPack.Augment.PlayModeTests` 이고 `includePlatforms` 는 `[]`(빈 배열)다.

`AugmentSelectionPlayModeTests.cs`:

```csharp
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class AugmentSelectionPlayModeTests
    {
        private readonly List<Object> _spawned = new();
        private float _timeScaleBefore;

        [SetUp]
        public void SetUp() => _timeScaleBefore = Time.timeScale;

        [TearDown]
        public void TearDown()
        {
            Time.timeScale = _timeScaleBefore;
            foreach (Object spawned in _spawned) if (spawned != null) Object.Destroy(spawned);
            _spawned.Clear();
        }

        private AugmentDefinition Make(string id)
        {
            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = id;
            definition.DisplayName = id;
            definition.Weight = 1f;
            definition.Benefits = new[] { new AugmentEffect { Stat = EAugmentStat.Reward, Value = 0.4f } };
            _spawned.Add(definition);
            return definition;
        }

        private (AugmentSelectionDirector director, AugmentLoadout loadout) Build()
        {
            var host = new GameObject("__TEST__AugmentRig");
            _spawned.Add(host);

            AugmentLoadout loadout = host.AddComponent<AugmentLoadout>();
            var pool = ScriptableObject.CreateInstance<AugmentPool>();
            pool.Entries = new[] { Make("a"), Make("b"), Make("c"), Make("d") };
            _spawned.Add(pool);

            AugmentSelectionDirector director = host.AddComponent<AugmentSelectionDirector>();
            director.ConfigureForTest(loadout, pool, cardCount: 3, seed: 7);
            return (director, loadout);
        }

        [UnityTest]
        public IEnumerator OpeningStopsTimeAndOffersThreeCards()
        {
            (AugmentSelectionDirector director, _) = Build();

            director.OpenForTest();
            yield return null;

            Assert.That(director.IsOpen, Is.True);
            Assert.That(director.Cards.Count, Is.EqualTo(3));
            Assert.That(Time.timeScale, Is.EqualTo(0f));
        }

        [UnityTest]
        public IEnumerator ConfirmAddsToLoadoutAndRestoresTime()
        {
            (AugmentSelectionDirector director, AugmentLoadout loadout) = Build();

            director.OpenForTest();
            yield return null;
            AugmentDefinition picked = director.Cards[0];
            director.Confirm(picked);
            yield return null;

            Assert.That(director.IsOpen, Is.False);
            Assert.That(loadout.Has(picked), Is.True);
            Assert.That(loadout.GetMultiplier(EAugmentStat.Reward), Is.EqualTo(1.4f).Within(1e-5f));
            Assert.That(Time.timeScale, Is.EqualTo(1f));
        }

        [UnityTest]
        public IEnumerator SecondOpenExcludesWhatWasTaken()
        {
            (AugmentSelectionDirector director, _) = Build();

            director.OpenForTest();
            yield return null;
            AugmentDefinition picked = director.Cards[0];
            director.Confirm(picked);
            yield return null;

            director.OpenForTest();
            yield return null;

            CollectionAssert.DoesNotContain(director.Cards, picked);
        }

        [UnityTest]
        public IEnumerator ConfirmingUnofferedCardIsIgnored()
        {
            (AugmentSelectionDirector director, AugmentLoadout loadout) = Build();

            director.OpenForTest();
            yield return null;
            AugmentDefinition outsider = Make("outsider");
            director.Confirm(outsider);
            yield return null;

            Assert.That(loadout.Has(outsider), Is.False);
            Assert.That(director.IsOpen, Is.True);
        }
    }
}
```

- [ ] **Step 2: 실패를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
```

기대: 컴파일 에러 — `AugmentSelectionDirector` 없음.

- [ ] **Step 3: 최소 구현**

`AugmentSelectionDirector.cs`:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>일차가 넘어갈 때 증강을 띄우고 확정한다.
    ///
    /// <para><b>정지 호출이 이 파일에만 있다.</b> <c>Time.timeScale</c> 은 피어 로컬이라 멀티에서
    /// 그대로 쓰면 "싱글과 권위가 같은 코드로 돈다" 가 깨진다 — 갚아야 할 빚이고, 갚을 때
    /// 고칠 자리를 하나로 두려고 여기 모았다(스펙 §6).</para>
    ///
    /// <para><b>추첨과 확정은 권위에서만 돈다.</b> 싱글에서는 <see cref="StageSession.IsAuthority"/>
    /// 가 항상 참이라 지금 동작 그대로다(스펙 §2).</para></summary>
    [DisallowMultipleComponent]
    public sealed class AugmentSelectionDirector : MonoBehaviour
    {
        [SerializeField] private TimeOfDayDirector _timeOfDay;
        [SerializeField] private AugmentLoadout _loadout;
        [SerializeField] private AugmentPool _pool;
        [SerializeField] private AugmentSelectionView _view;
        [SerializeField] private PenguinInputReader _input;
        [SerializeField, Min(1)] private int _cardCount = 3;

        private readonly List<AugmentDefinition> _cards = new();
        private System.Random _random;
        private StageSession _session;
        private bool _sessionResolved;
        private float _resumeTimeScale = 1f;

        public bool IsOpen { get; private set; }
        public IReadOnlyList<AugmentDefinition> Cards => _cards;

        public event Action Opened;
        public event Action Closed;

        /// <summary>테스트가 씬 배선 없이 세운다.</summary>
        public void ConfigureForTest(AugmentLoadout loadout, AugmentPool pool, int cardCount, int seed)
        {
            _loadout = loadout;
            _pool = pool;
            _cardCount = cardCount;
            _random = new System.Random(seed);
        }

        /// <summary>테스트가 일차 넘어감 없이 연다.</summary>
        public void OpenForTest() => Open();

        private void OnEnable() => Subscribe();
        private void OnDisable() => Unsubscribe();

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
        /// 그것이 화면상 2일차의 시작이다 — <c>&gt;= 2</c> 를 걸면 첫 증강을 통째로 건너뛴다(스펙 §3).</summary>
        private void OnDayAdvanced(int dayIndex)
        {
            if (!Session.IsAuthority) return;
            Open();
        }

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
            AugmentDraft.Draw(_pool != null ? _pool.Entries : null,
                _loadout != null ? _loadout.Owned : null, _cardCount, _random, _cards);
            if (_cards.Count == 0) return;

            IsOpen = true;
            Pause();
            if (_view != null) _view.Show(_cards, Confirm);
            Opened?.Invoke();
        }

        /// <summary>고른 것을 확정한다. 이번에 제시하지 않은 것은 무시한다.</summary>
        public void Confirm(AugmentDefinition picked)
        {
            if (!IsOpen || picked == null || !_cards.Contains(picked)) return;

            if (_loadout != null) _loadout.Add(picked);
            Close();
        }

        private void Close()
        {
            IsOpen = false;
            _cards.Clear();
            if (_view != null) _view.Hide();
            Resume();
            Closed?.Invoke();
        }

        private void Pause()
        {
            _resumeTimeScale = Time.timeScale > 0f ? Time.timeScale : 1f;
            Time.timeScale = 0f;
            if (_input != null) _input.enabled = false;
        }

        private void Resume()
        {
            Time.timeScale = _resumeTimeScale;
            if (_input != null) _input.enabled = true;
        }
    }
}
```

> Task 5 전까지 `AugmentSelectionView` 가 없으므로, 이 태스크에서는 그 필드와 두 호출
> (`_view.Show` · `_view.Hide`)을 **주석 처리하고** Task 5 에서 되살린다. 그래야 이 태스크가
> 혼자 컴파일되고 테스트가 돈다.

- [ ] **Step 4: 통과를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter AugmentSelectionPlayModeTests
```

기대: `failed:false`, 테스트 4/4 통과.
⚠ PlayMode 는 이 프로젝트에서 **빨간 15개가 기본선**이다. 개수가 아니라 **이름 집합**을 기준선과 비교한다.

---

### Task 4: 효과 다섯 군데 배선

**Files:**
- Modify: `Assets/Game/InGame/Cleanliness/Scripts/GameManager.cs:88-104` (`NotifyRequestCompleted`)
- Modify: `Assets/Game/InGame/Delivery/Scripts/RequestDirector.cs:101-102`
- Modify: `Assets/Game/InGame/Penguin/Scripts/PenguinLocomotion.cs:180,585,946,982`
- Modify: `Assets/Game/InGame/Map/WinterVillage/Scripts/SnowGiftMachinePresentation.cs:328`
- Test: `Assets/Game/InGame/Augment/Tests/PlayMode/AugmentEffectPlayModeTests.cs`

**Interfaces:**
- Consumes: `AugmentLoadout.GetMultiplier` · `.GetValue`
- Produces: 네 소비처에 `[SerializeField] private AugmentLoadout _augments;` — **비면 기존 동작 그대로**

- [ ] **Step 1: 실패하는 테스트를 쓴다**

```csharp
using System.Collections;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace PPack
{
    public sealed class AugmentEffectPlayModeTests
    {
        private GameObject _host;

        [TearDown]
        public void TearDown() { if (_host != null) Object.Destroy(_host); }

        [UnityTest]
        public IEnumerator RewardAndTimeBonusScaleWithLoadout()
        {
            _host = new GameObject("__TEST__AugmentEffects");
            AugmentLoadout loadout = _host.AddComponent<AugmentLoadout>();
            GameManager manager = _host.AddComponent<GameManager>();

            var config = ScriptableObject.CreateInstance<StageBalanceConfig>();
            config.StartSeconds = 100f;
            config.MaxSeconds = 0f;
            manager.Configure(config, null);
            manager.SetAugmentsForTest(loadout);
            manager.BeginPlaying();
            yield return null;

            var definition = ScriptableObject.CreateInstance<AugmentDefinition>();
            definition.Id = "reward_up";
            definition.Benefits = new[] { new AugmentEffect { Stat = EAugmentStat.Reward, Value = 0.5f } };
            definition.Penalties = new[] { new AugmentEffect { Stat = EAugmentStat.ClearTimeBonus, Value = -0.5f } };
            loadout.Add(definition);

            int scoreBefore = manager.Score;
            float timeBefore = manager.RemainingSeconds;
            manager.NotifyRequestCompleted(
                new GiftRequest(1, 0, default, 10f, new RequestBalanceResult(1f, 10, 30f, 20f)));

            Assert.That(manager.Score - scoreBefore, Is.EqualTo(15));
            Assert.That(manager.RemainingSeconds - timeBefore, Is.EqualTo(10f).Within(0.2f));

            Object.DestroyImmediate(config);
            Object.DestroyImmediate(definition);
        }
    }
}
```

> `GiftRequest` 생성자 인자와 `EGiftBoxKind` 기본값은 실제 시그니처를 확인해 맞춘다
> (`Delivery/Scripts/GiftRequest.cs:18`). 시간 비교에 여유(`Within(0.2f)`)를 둔 것은
> `BeginPlaying` 뒤 한 프레임이 `FixedUpdate` 로 빠져나가기 때문이다.

- [ ] **Step 2: 실패를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
```

기대: 컴파일 에러 — `GameManager.SetAugmentsForTest` 없음.

- [ ] **Step 3: 네 소비처를 고친다**

`GameManager.cs` — 필드와 테스트 진입점을 더하고 `NotifyRequestCompleted` 안에서 곱한다:

```csharp
[SerializeField] private AugmentLoadout _augments;

/// <summary>테스트가 씬 배선 없이 로드아웃을 꽂는다.</summary>
public void SetAugmentsForTest(AugmentLoadout augments) => _augments = augments;
```

```csharp
// 기존: Score += request.Reward;
int reward = request.Reward;
float bonusSeconds = request.TimeBonusSeconds;
if (_augments != null)
{
    reward = Mathf.RoundToInt(reward * _augments.GetMultiplier(EAugmentStat.Reward));
    bonusSeconds *= _augments.GetMultiplier(EAugmentStat.ClearTimeBonus);
}

Score += reward;
ScoreChanged?.Invoke(Score);

float before = RemainingSeconds;
RemainingSeconds += bonusSeconds;   // 기존: request.TimeBonusSeconds
```

`RequestDirector.cs:101` — `RequestBalance` 는 건드리지 않고 결과만 스케일한다:

```csharp
RequestBalanceResult balance = RequestBalance.Evaluate(_config, kindWeight, distance, RollRatio(), _elapsed);
if (_augments != null)
    balance = new RequestBalanceResult(
        balance.Difficulty,
        balance.Reward,
        balance.TtlSeconds * _augments.GetMultiplier(EAugmentStat.RequestTtl),
        balance.TimeBonusSeconds);
var request = new GiftRequest(_nextId++, houseIndex, kind, distance, balance);
```

`PenguinLocomotion.cs` — **`_speedBoostMultiplier` 를 재사용하지 않는다**(부스트 패드 소유,
`[1,3]` 클램프라 감속 불가). 승수를 하나 더 두고, 기존 네 사용처(`180` · `585` · `946` · `982`)의
`_speedBoostMultiplier` 를 아래 프로퍼티로 바꾼다:

```csharp
[SerializeField] private AugmentLoadout _augments;

/// <summary>부스트 패드(<see cref="PenguinBoostReceiver"/>)와 증강은 서로 다른 주인이다.
/// 패드는 만료 때 자기 값을 1로 되돌리므로 한 필드를 나눠 쓰면 증강이 지워진다.</summary>
private float SpeedMultiplier =>
    _speedBoostMultiplier * (_augments != null ? _augments.GetMultiplier(EAugmentStat.WalkSpeed) : 1f);
```

`SnowGiftMachinePresentation.cs:328` — `_isNetworkConversion` 분기는 **건드리지 않고** 호출만 늘린다:

```csharp
SpawnGift();
if (_augments != null && UnityEngine.Random.value < _augments.GetValue(EAugmentStat.ExtraGiftChance))
    SpawnGift();
```

⚠ 두 선물이 같은 자리에서 나오므로 물리로 서로 밀어낸다. v1 에서는 그대로 둔다.
⚠ 멀티에서 이 자리가 서버에서만 도는지는 **확인되지 않았다.** v1 은 싱글 전용이라 범위 밖이고,
멀티 배선 때 확인한다.

- [ ] **Step 4: 통과를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter AugmentEffectPlayModeTests
```

기대: `failed:false`, 통과. 그리고 **기존 회귀가 늘지 않았는지** 확인한다:

```bash
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode
```

기준선과 **실패 이름 집합**을 비교한다. 개수는 신호가 아니다.

---

### Task 5: 선택 화면 (UI Toolkit)

**Files:**
- Create: `Assets/Game/InGame/UI/AugmentSelect/AugmentSelect.uxml`
- Create: `Assets/Game/InGame/UI/AugmentSelect/AugmentSelect.uss`
- Create: `Assets/Game/InGame/UI/AugmentSelect/AugmentSelectPanelSettings.asset`
- Create: `Assets/Game/InGame/UI/AugmentSelect/Scripts/AugmentSelectionView.cs`
- Create: `Assets/Game/InGame/UI/AugmentSelect/README.md`
- Modify: `Assets/Game/InGame/Augment/Scripts/AugmentSelectionDirector.cs` (Task 3 에서 주석 처리한 `_view` 되살리기)

**Interfaces:**
- Consumes: `AugmentSelectionDirector.Confirm`
- Produces: `AugmentSelectionView.Show(IReadOnlyList<AugmentDefinition> cards, Action<AugmentDefinition> onPick)` · `.Hide()`

**형제와 같은 구성으로 만든다** — `UI/StageHUD/`·`UI/StageOutro/` 가 `.uxml` + `.uss` +
`PanelSettings` + `Scripts` + `README.md` 다. 표시 문자열은 영어(`CHOOSE AN AUGMENT`).

- [ ] **Step 1: 뷰를 만든다**

카드 한 장의 구조(코드로 복제):

```
card (Button)
  card__name        Label   DisplayName
  card__desc        Label   Description
  card__benefit     Label   "▲ {스탯} {부호}{값}"   class: is-benefit
  card__penalty     Label   "▼ {스탯} {부호}{값}"   class: is-penalty
```

`AugmentSelectionView.cs` 핵심:

```csharp
using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>증강 카드 세 장을 띄우고 고른 것을 되돌려준다. 판정은 하지 않는다 —
    /// 확정은 <see cref="AugmentSelectionDirector.Confirm"/> 가 한다.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AugmentSelectionView : MonoBehaviour
    {
        [SerializeField] private UIDocument _document;
        [SerializeField] private VisualTreeAsset _cardTemplate;

        private VisualElement _root;
        private VisualElement _cardRow;
        private Action<AugmentDefinition> _onPick;

        public void Show(IReadOnlyList<AugmentDefinition> cards, Action<AugmentDefinition> onPick)
        {
            _onPick = onPick;
            EnsureRoot();
            _cardRow.Clear();

            for (int index = 0; index < cards.Count; index++)
            {
                AugmentDefinition definition = cards[index];
                VisualElement card = BuildCard(definition);
                card.RegisterCallback<ClickEvent>(_ => _onPick?.Invoke(definition));
                _cardRow.Add(card);
            }

            _root.style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            if (_root is null) return;
            _root.style.display = DisplayStyle.None;
            _cardRow?.Clear();
            _onPick = null;
        }
    }
}
```

> `EnsureRoot` 와 `BuildCard` 는 `StageOutro` 의 뷰가 쓰는 방식을 그대로 따른다 —
> 구현 전에 `UI/StageOutro/Scripts/` 를 읽고 이름과 구조를 맞춘다.

- [ ] **Step 2: 디렉터에서 `_view` 를 되살린다**

Task 3 에서 주석 처리한 필드와 `_view.Show(_cards, Confirm)` · `_view.Hide()` 두 줄을 되돌린다.

- [ ] **Step 3: 컴파일과 회귀를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner run_tests --mode PlayMode --filter Augment
```

기대: `failed:false`, Task 3·4 의 테스트가 그대로 통과(뷰가 비어도 흐름이 돌아야 한다).

---

### Task 6: 카드 여섯 장과 검증 씬

**Files:**
- Create: `Assets/Game/InGame/Augment/Data/Augment_RushOrder.asset` 외 5장
- Create: `Assets/Game/InGame/Augment/Data/AugmentPool_Default.asset`
- Create: `Assets/Game/InGame/Augment/Tests/Augment_Selection_Test.unity`
- Modify: `Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity` (증강 리그 배치)

스펙 §10 의 여섯 장을 그대로 만든다. **`Id` 는 표의 값 그대로** 넣는다.

| `Id` | DisplayName | Description | Benefits | Penalties |
|---|---|---|---|---|
| `rush_order` | Rush Order | Bigger payouts, tighter clocks. | Reward +0.4 | RequestTtl −0.2 |
| `slick_feet` | Slick Feet | Move faster, earn less time per delivery. | WalkSpeed +0.25 | ClearTimeBonus −0.15 |
| `extended_deadline` | Extended Deadline | More time per order, smaller payouts. | RequestTtl +0.3 | Reward −0.15 |
| `double_wrap` | Double Wrap | The machine sometimes pops out a second gift. | ExtraGiftChance +0.3 | WalkSpeed −0.1 |
| `overtime` | Overtime | Deliveries pay in time instead of score. | ClearTimeBonus +0.25 | Reward −0.2 |
| `basic_training` | Basic Training | A small, clean speed bump. | WalkSpeed +0.1 | (없음) |

- [ ] **Step 1: 에셋 6장 + 풀을 만든다** — `Assets > Create > PPack > Augment`. 풀에 여섯 장을 모두 넣는다.

- [ ] **Step 2: 검증 씬을 만든다**

`Augment_Selection_Test.unity` 에 `AugmentLoadout` + `AugmentSelectionDirector` + `AugmentSelectionView`
만 놓고, `TimeOfDayDirector` 없이 `OpenForTest()` 를 눌러 카드가 뜨는지 본다.
**Build Settings 에 넣지 않는다.**

- [ ] **Step 3: `SinglePlay.unity` 에 리그를 놓는다**

빈 오브젝트 `AugmentRig` 하나에 `AugmentLoadout` · `AugmentSelectionDirector` · `AugmentSelectionView`
를 붙이고, 디렉터에 씬의 `TimeOfDayDirector` · 풀 · `PenguinInputReader` 를 꽂는다.
네 소비처(`GameManager` · `RequestDirector` · `PenguinLocomotion` · `SnowGiftMachinePresentation`)의
`_augments` 필드에도 같은 로드아웃을 꽂는다.

⚠ **씬 편집은 `SinglePlay` 에서만.** `MultiPlay.unity` 는 빌더 산출물이라 직접 고치면 다음 빌드에서 사라진다.
빌더 코드 수정은 **필요 없다**(스펙 §2 — 일반 컴포넌트라 스탬핑으로 따라온다).

- [ ] **Step 4: 실제로 플레이해서 본다**

`SinglePlay` 를 열고 Play. **86.4초**에 첫 선택이 뜨는지, 고르는 동안 HUD 시계와 펭귄이 멈추는지,
확정 뒤 다시 도는지, 보상이 실제로 오르는지 본다.
급하면 `TimeOfDayConfig.SecondsPerDay` 를 20초로 낮춰 반복한다 — **SO 라 그 값이 남으므로 끝나고 120으로 되돌린다.**

- [ ] **Step 5: 유니티를 원복한다**

Play Mode off · 원래 씬 활성 · dirty 없음 · `__TEST__`·`(Clone)` 잔여물 없음.

- [ ] **Step 6: 체크인 (딜리버러블 3 — 배선과 UI)**

```bash
cm status --changed --private
cm add -R Assets/Game/InGame/UI/AugmentSelect
cm add -R Assets/Game/InGame/Augment/Data
cm checkout Assets/Game/InGame/Cleanliness/Scripts/GameManager.cs \
            Assets/Game/InGame/Delivery/Scripts/RequestDirector.cs \
            Assets/Game/InGame/Penguin/Scripts/PenguinLocomotion.cs \
            Assets/Game/InGame/Map/WinterVillage/Scripts/SnowGiftMachinePresentation.cs \
            Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity
cm ci <위 경로 전부 + 신규 폴더> --commentsfile=/tmp/ci-augment-wiring.txt
```

⚠ **경로 목록을 먼저 완성한다.** 디렉터리 경로는 그 안의 `Changed` 파일을 건너뛰고, `.meta` 는
자기 에셋을 따라가지 않는다. 삭제와 수정은 같은 체크인에 못 넣는다(루트 AGENTS.md).

---

### Task 7: 문서

**Files:**
- Create: `Assets/Game/InGame/Augment/AGENTS.md`
- Modify: `docs/INDEX.md` (현재 상태 한 줄)
- Create: `docs/Session_Summary_20260831_run-augments.md`

- [ ] **Step 1: `Augment/AGENTS.md` 를 쓴다**

담을 것: 이 폴더가 소유하는 것, **팀 공유를 고른 근거**(의뢰에 완료자가 없다), 가산 누적 규약,
`GetMultiplier` 가 0에서 멈춘다는 것, **`timeScale` 이 빚이라는 것**, 건드리지 않기로 한 셋
(`RequestBalance` · 교환기 분기 · `_speedBoostMultiplier`), `Id` 가 안정 키라는 것.

- [ ] **Step 2: `docs/INDEX.md` 현재 상태에 한 줄**

- [ ] **Step 3: 세션 요약**

실제로 측정한 것을 적는다 — 첫 선택이 뜬 시각, 보상 배율 전후, 테스트 통과 수, 기준선 대비 실패 이름 집합.

- [ ] **Step 4: 체크인 (딜리버러블 4 — 문서)**

---

## 자기 점검

**스펙 커버리지** — §1 범위(Task 3·6) · §2 소유와 권위(Task 1·3) · §3 트리거(Task 3) · §4 데이터(Task 1)
· §5 런타임(Task 1·2·3) · §6 정지(Task 3) · §7 효과 다섯(Task 4) · §8 UI(Task 5) · §9 영어 문자열(Task 6)
· §10 카드 여섯(Task 6) · §11 검증(Task 1·2·3·4) · §14 용어(이미 등록됨). 빠진 절 없음.

**타입 일관성** — `GetValue`/`GetMultiplier`/`Has`/`Owned`/`Add`/`Clear`(Task 1)가 Task 2·3·4 에서
같은 이름으로 쓰인다. `Draw(pool, owned, count, random, results)` 인자 순서가 Task 2·3 에서 같다.
`Show(cards, onPick)`/`Hide()` 가 Task 3 의 호출과 Task 5 의 정의에서 같다.

**알려진 순서 의존** — Task 3 은 Task 5 의 `AugmentSelectionView` 를 앞서 참조하므로, Task 3 에서는
그 필드와 두 호출을 주석 처리하고 Task 5 에서 되살린다. 이것이 유일한 전방 참조다.
