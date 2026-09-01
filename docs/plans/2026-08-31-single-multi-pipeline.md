# 싱글·멀티 파이프라인 규약 구현 계획

> **에이전트 작업자에게:** 이 계획은 태스크 단위로 실행한다. 체크박스(`- [ ]`)로 진행을 표시한다.

**Goal:** 세션 판정을 `StageSession` 하나로 모으고, 세 상태 게이트 규약을 문서로 굳힌다.

**Architecture:** 판정 로직은 Fusion 을 모르는 순수 함수(`Resolve`)가 갖고, Fusion 결선(`For`)은
러너 조회와 씬 경로 대조만 하는 얇은 껍데기다. 기존 컴포넌트를 게이트로 옮기는 이관 작업은 없다 —
빌더의 끄기 목록이 이미 no-op 이기 때문이다(스펙 §2).

**Tech Stack:** Unity 6000.6.0b7 · Photon Fusion 2 (`Fusion.Runtime.dll`) · Unity Test Framework(EditMode) · Plastic SCM

**Spec:** `docs/specs/2026-08-31-single-multi-pipeline.md`

## Global Constraints

- **git 을 쓰지 않는다.** 버전 관리는 Plastic SCM (`cm`). `git` 명령은 이 저장소에서 금지다.
- **체크인은 태스크마다 하지 않는다.** 프로젝트 규칙은 *딜리버러블당 하나*이고 피처 브랜치당 4~6개가
  건강한 수다(루트 `AGENTS.md`). 이 계획은 체크인을 **3개**로 묶는다 — 설계 / 구현 / 문서.
  태스크 끝마다 커밋하라는 일반 지침은 여기서 따르지 않는다.
- **네임스페이스는 `PPack` 하나.** 폴더나 어셈블리를 따라가지 않는다.
- **private 필드는 `_camelCase`**, 타입·메서드는 `PascalCase`, enum 타입명은 `E` 접두.
- **직렬화된 Unity Object 필드만 `== null` / `!= null`**, 나머지는 `is null` / `is not null`.
- `Core` 는 우리 것 중 아무것도 참조하지 않는다. `PPack.Core` 는 `InGame`·`OutGame` 을 모른다.
- 씬 편집은 언제나 `SinglePlay.unity` 에서. `MultiPlay.unity` 는 빌더 산출물이다.
- 테스트 후 유니티 원복: Play Mode off, 원래 씬 활성, dirty 없음, `__TEST__` 잔여물 없음.

---

## 파일 구조

| 경로 | 책임 |
|---|---|
| `Assets/Game/Core/Multiplay/Scripts/StageSession.cs` (신규) | 세션 세 상태와 인원 수를 답한다. 순수 판정 + 얇은 Fusion 결선 |
| `Assets/Game/Core/Multiplay/Tests/EditMode/PPack.Multiplay.EditModeTests.asmdef` (신규) | Core/Multiplay 의 EditMode 테스트 어셈블리. 지금은 PlayMode 것만 있다 |
| `Assets/Game/Core/Multiplay/Tests/EditMode/StageSessionTests.cs` (신규) | `Resolve` 와 씬 경로 대조의 EditMode 커버 |
| `Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs` (수정) | standalone 판정을 `StageSession` 으로 교체 |
| `Assets/Game/Core/Multiplay/AGENTS.md` (수정) | 세 상태 게이트 규약 |
| `Assets/Game/InGame/Cleanliness/AGENTS.md` (수정) | 종료 흐름 확정, 빌더 수정 기준 |
| `docs/INDEX.md` (수정) | 현재 상태 한 줄 |

---

### Task 1: `StageSession` 의 순수 판정과 EditMode 어셈블리

**Files:**
- Create: `Assets/Game/Core/Multiplay/Scripts/StageSession.cs`
- Create: `Assets/Game/Core/Multiplay/Tests/EditMode/PPack.Multiplay.EditModeTests.asmdef`
- Create: `Assets/Game/Core/Multiplay/Tests/EditMode/StageSessionTests.cs`

**Interfaces:**
- Consumes: `SessionLauncher.ExpectedPlayerCount` (static int), `SessionLauncher.GameplayScenePath` (static string) — 둘 다 이미 있다.
- Produces: `StageSession.Resolve(bool, bool, int)` · `StageSession.SceneOwnsSession(string, string)` ·
  읽기 속성 `Runner` `IsAuthority` `IsFollower` `PlayerCount`. Task 2 와 3 이 이 이름들에 의존한다.
  `Runner` 는 `Resolve` 경로에서 언제나 `null` 이고 Task 2 의 `For` 만 채운다.

- [ ] **Step 1: EditMode 테스트 어셈블리를 만든다**

`Assets/Game/Core/Multiplay/Tests/EditMode/PPack.Multiplay.EditModeTests.asmdef`:

```json
{
    "name": "PPack.Multiplay.EditModeTests",
    "rootNamespace": "PPack",
    "references": [
        "PPack.Core",
        "Fusion.Unity",
        "UnityEngine.TestRunner",
        "UnityEditor.TestRunner"
    ],
    "includePlatforms": ["Editor"],
    "excludePlatforms": [],
    "allowUnsafeCode": false,
    "overrideReferences": true,
    "precompiledReferences": [
        "nunit.framework.dll",
        "Fusion.Runtime.dll"
    ],
    "autoReferenced": false,
    "defineConstraints": ["UNITY_INCLUDE_TESTS"],
    "versionDefines": [],
    "noEngineReferences": false
}
```

옆의 `Tests/PlayMode/PPack.Multiplay.PlayModeTests.asmdef` 와 같은 참조에 `includePlatforms: ["Editor"]`
와 `UnityEditor.TestRunner` 만 더한 형태다.

- [ ] **Step 2: 실패하는 테스트를 쓴다**

`Assets/Game/Core/Multiplay/Tests/EditMode/StageSessionTests.cs`:

```csharp
using NUnit.Framework;

namespace PPack
{
    public sealed class StageSessionTests
    {
        [Test]
        public void 세션이_없으면_싱글이고_권위이며_인원은_1이다()
        {
            StageSession session = StageSession.Resolve(hasSession: false, isServer: false, expectedPlayerCount: 4);

            Assert.That(session.IsAuthority, Is.True, "싱글은 권위다 — 서버와 같은 코드가 돌아야 한다");
            Assert.That(session.IsFollower, Is.False);
            Assert.That(session.PlayerCount, Is.EqualTo(1), "세션이 없으면 인원은 언제나 1이다");
        }

        [Test]
        public void 세션이_있고_서버면_권위이고_인원은_기대값이다()
        {
            StageSession session = StageSession.Resolve(hasSession: true, isServer: true, expectedPlayerCount: 3);

            Assert.That(session.IsAuthority, Is.True);
            Assert.That(session.IsFollower, Is.False);
            Assert.That(session.PlayerCount, Is.EqualTo(3));
        }

        [Test]
        public void 세션이_있고_서버가_아니면_팔로워다()
        {
            StageSession session = StageSession.Resolve(hasSession: true, isServer: false, expectedPlayerCount: 3);

            Assert.That(session.IsAuthority, Is.False, "클라이언트는 판정하지 않는다");
            Assert.That(session.IsFollower, Is.True);
            Assert.That(session.PlayerCount, Is.EqualTo(3));
        }

        [Test]
        public void 기대_인원이_0이어도_인원은_1_아래로_내려가지_않는다()
        {
            StageSession session = StageSession.Resolve(hasSession: true, isServer: true, expectedPlayerCount: 0);

            Assert.That(session.PlayerCount, Is.EqualTo(1),
                "StartMatch 전에는 ExpectedPlayerCount 가 0이다 — 밸런스가 0으로 나눠지면 안 된다");
        }

        [Test]
        public void 씬_경로가_같으면_이_씬의_세션이다()
        {
            Assert.That(
                StageSession.SceneOwnsSession(
                    "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity",
                    "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity"),
                Is.True);
        }

        [Test]
        public void 씬_경로가_어긋나면_남의_세션이다()
        {
            // 2026-08-31 회귀: MPPM host 태그를 단 채 SinglePlay 에 들어가면 러너가 따라오고
            // GetRunnerForScene 가 그것을 돌려준다. 경로 대조가 유일한 방어다.
            Assert.That(
                StageSession.SceneOwnsSession(
                    gameplayScenePath: "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity",
                    ownerScenePath: "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity"),
                Is.False);
        }

        [Test]
        public void 게임플레이_씬_경로가_비어_있으면_남의_세션으로_친다()
        {
            Assert.That(
                StageSession.SceneOwnsSession(null, "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity"),
                Is.False);
            Assert.That(
                StageSession.SceneOwnsSession("", "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity"),
                Is.False);
        }
    }
}
```

- [ ] **Step 3: 컴파일 실패를 확인한다**

```bash
U=/Users/dust9826/.unity/bin/unity
P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 20
```

기대: `StageSession` 이 없어 컴파일 에러. 이 단계에서 테스트는 아직 못 돈다.

- [ ] **Step 4: 최소 구현을 쓴다**

`Assets/Game/Core/Multiplay/Scripts/StageSession.cs`:

```csharp
using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// "지금 이 씬은 싱글인가, 권위인가, 팔로워인가" 를 한 곳에서 답한다.
    ///
    /// <para><b>싱글과 권위는 같은 코드가 돈다.</b> 그것이 이 구조체의 존재 이유다 — 싱글과 멀티는
    /// 흐름이 같고 인원과 밸런스만 다르다(<c>docs/specs/2026-08-31-single-multi-pipeline.md</c>).
    /// 컴포넌트가 <c>IsAuthority</c> 하나만 보면 두 모드에서 같은 판정을 돌릴 수 있다.</para>
    ///
    /// <para><b>판정은 <see cref="Resolve"/> 가 갖고 <see cref="For"/> 는 갖지 않는다.</b>
    /// <c>NetworkRunner.IsRunning</c> 과 <c>IsServer</c> 는 세터가 없어 EditMode 에서 만들어 낼 수
    /// 없다. 분기를 순수 함수로 떼어 놓아야 전부 테스트할 수 있다.</para>
    /// </summary>
    public readonly struct StageSession
    {
        /// <summary>이 씬의 러너. 싱글이거나 남의 세션이면 <c>null</c>.</summary>
        public NetworkRunner Runner { get; }

        /// <summary>싱글이거나 서버다. <b>판정과 스폰을 돌려도 되는 쪽.</b></summary>
        public bool IsAuthority { get; }

        /// <summary>세션이 있고 서버가 아니다. <b>판정하지 않고 복제값만 읽는 쪽.</b></summary>
        public bool IsFollower { get; }

        /// <summary>이 판의 인원. 싱글은 언제나 1이다.</summary>
        public int PlayerCount { get; }

        private StageSession(NetworkRunner runner, bool isAuthority, bool isFollower, int playerCount)
        {
            Runner = runner;
            IsAuthority = isAuthority;
            IsFollower = isFollower;
            PlayerCount = playerCount;
        }

        /// <summary>순수 판정. 러너 없이 상태만 정한다 — 테스트가 부르는 것이 이것이다.</summary>
        internal static StageSession Resolve(bool hasSession, bool isServer, int expectedPlayerCount) =>
            ResolveWith(null, hasSession, isServer, expectedPlayerCount);

        /// <summary>같은 판정에 러너를 실어 준다. <see cref="For"/> 만 부른다.</summary>
        private static StageSession ResolveWith(
            NetworkRunner runner, bool hasSession, bool isServer, int expectedPlayerCount)
        {
            if (!hasSession) return new StageSession(null, true, false, 1);

            // StartMatch 전에는 ExpectedPlayerCount 가 0이다. 밸런스가 이 값으로 나누므로 1로 막는다.
            int players = Mathf.Max(1, expectedPlayerCount);
            return new StageSession(runner, isServer, !isServer, players);
        }

        /// <summary>
        /// 지금 이 씬이 그 세션의 게임플레이 씬인가.
        ///
        /// <para><b>왜 필요한가</b>(2026-08-31 실측). 러너는 <c>DontDestroyOnLoad</c> 라 멀티 세션이
        /// 살아 있는 채로 SinglePlay 에 들어가면 따라온다. 그때
        /// <c>NetworkRunner.GetRunnerForScene</c> 는 그 러너를 <b>그대로 돌려준다</b> — 막아 주지
        /// 않는다. 경로 대조가 유일한 방어다.</para>
        /// </summary>
        internal static bool SceneOwnsSession(string gameplayScenePath, string ownerScenePath)
        {
            if (string.IsNullOrEmpty(gameplayScenePath)) return false;
            return string.Equals(gameplayScenePath, ownerScenePath, System.StringComparison.Ordinal);
        }
    }
}
```

- [ ] **Step 5: 테스트가 통과하는지 본다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter StageSessionTests
```

기대: 7개 전부 PASS. 컴파일 에러 0.

`run_tests` 인자 이름이 다르면 일부러 틀린 인자를 줘서 에러 메시지로 알아낸다(이 프로젝트 CLI 관례).

---

### Task 2: Fusion 결선 — `For(GameObject)`

**Files:**
- Modify: `Assets/Game/Core/Multiplay/Scripts/StageSession.cs`

**Interfaces:**
- Consumes: Task 1 의 `Resolve` · `SceneOwnsSession`.
- Produces: `public static StageSession For(GameObject owner)`. Task 3 이 이것을 쓴다.

- [ ] **Step 1: `For` 를 더한다**

`StageSession.cs` 의 `SceneOwnsSession` 아래에 넣는다. `using Fusion;` 은 Task 1 에서 이미 넣었다.

```csharp
        /// <summary>
        /// 이 오브젝트가 선 씬의 세션을 답한다. 세션이 없거나 남의 세션이면 싱글로 답한다.
        ///
        /// <para><b>프레임마다 부르지 않는다.</b> 러너 조회가 들어 있다. 한 번 받아 캐시한다.</para>
        /// </summary>
        public static StageSession For(GameObject owner)
        {
            if (owner == null) return Resolve(false, false, 0);

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(owner.scene);
            bool hasSession =
                runner != null &&
                runner.IsRunning &&
                SceneOwnsSession(SessionLauncher.GameplayScenePath, owner.scene.path);

            return ResolveWith(
                hasSession ? runner : null,
                hasSession,
                hasSession && runner.IsServer,
                SessionLauncher.ExpectedPlayerCount);
        }
```

`owner` 와 `runner` 는 Unity Object 이므로 `== null` / `!= null` 로 검사한다(fake-null).
남의 세션일 때 `Runner` 를 비우는 것이 요점이다 — 호출자가 `Runner` 로 싱글을 판정하기 때문이다.

- [ ] **Step 2: 컴파일과 기존 테스트를 확인한다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 20
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter StageSessionTests
```

기대: 컴파일 에러 0, Task 1 의 7개 여전히 PASS. `For` 자체는 테스트하지 않는다 —
Fusion 상태를 EditMode 에서 만들 수 없기 때문이고, 분기는 전부 `Resolve` 에 있다.

---

### Task 3: `SnowCpuStage` 의 standalone 판정 교체

**Files:**
- Modify: `Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs` (526행 근처 `EnsureRegistered`)

**Interfaces:**
- Consumes: Task 2 의 `StageSession.For(GameObject)`.
- Produces: 없음. 기존 `_standalone` 필드의 의미는 그대로다.

현재 코드는 이렇다:

```csharp
            if (NetworkRunner.Instances.Count == 0)
            {
                if (_field == null) Build();
                _standalone = true;
                return;
            }

            _standalone = false;

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null || !runner.IsRunning) return;
```

`Instances.Count == 0` 이 2026-08-31 사고의 진원이다 — 남의 세션이 하나라도 살아 있으면 거짓이 된다.

- [ ] **Step 1: 판정을 `StageSession` 으로 바꾼다**

```csharp
            // 남의 세션이 살아 있어도 이 씬의 세션이 아니면 싱글로 돈다.
            // 전에는 NetworkRunner.Instances.Count 를 셌는데, MPPM host 태그가 남은 채 SinglePlay 에
            // 들어가면 그 판정이 조용히 뒤집혔다(2026-08-31 실측).
            StageSession session = StageSession.For(gameObject);
            if (session.Runner == null)
            {
                if (_field == null) Build();
                _standalone = true;
                return;
            }

            _standalone = false;

            NetworkRunner runner = session.Runner;
```

이 아래 `if (runner == null || !runner.IsRunning) return;` 줄은 **지운다** — `StageSession.For` 가
이미 그 둘을 봤고, 남의 세션까지 걸러 준다. 그 아래 `_registeredWith = runner; runner.AddGlobal(this); Build();`
는 그대로 둔다.

- [ ] **Step 2: 컴파일하고 눈 회귀 테스트를 돌린다**

```bash
$U cmd --project-path "$P" --no-banner recompile
$U cmd --project-path "$P" --no-banner recompile_status
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 20
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter StageSessionTests
$U cmd --project-path "$P" --no-banner run_tests --mode EditMode --filter Snow
```

기대: 컴파일 에러 0. `StageSessionTests` 7개 PASS. 눈 EditMode 테스트가 **이 변경 전과 같은
결과**여야 한다.

⚠ 루트 `AGENTS.md` 가 기록한 기존 실패가 있다 — `SnowHeadlessTests` 의 눈덩이 성장 테스트는
`/main` 에서 이미 실패한다(`HasSupport` 게이트와 테스트가 어긋남). **그것이 여전히 실패하는 것은
이 작업의 회귀가 아니다.** 변경 전 결과를 먼저 찍어 두고 비교한다.

- [ ] **Step 3: 싱글 씬에서 눈이 서는지 눈으로 본다**

MPPM 태그가 **비어 있는지 먼저 확인한다**(`Library/VP/SystemData.json` 의 Main Editor 행).
태그가 남아 있으면 이 검증 자체가 거짓이 된다.

```bash
$U cmd --project-path "$P" --no-banner list_open_scenes     # 현재 씬과 dirty 상태를 기록
$U cmd --project-path "$P" --no-banner open_scene --path Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity
$U cmd --project-path "$P" --no-banner editor_play
# 잠시 뒤
$U cmd --project-path "$P" --no-banner get_console_logs --severity Error --limit 20
$U cmd --project-path "$P" --no-banner editor_stop
```

기대: 에러 0. 눈이 정상적으로 선다(`_standalone = true` 경로).
**열려 있던 씬이 dirty 였다면 열지 말고 멈춘다** — 루트 `AGENTS.md` §5.
끝나면 원래 씬으로 돌아가고 dirty 를 남기지 않는다.

---

### Task 4: 규약을 문서로 굳힌다

**Files:**
- Modify: `Assets/Game/Core/Multiplay/AGENTS.md`
- Modify: `Assets/Game/InGame/Cleanliness/AGENTS.md`
- Modify: `docs/INDEX.md`

**Interfaces:** 없음. 코드 변경 없음.

- [ ] **Step 1: `Core/Multiplay/AGENTS.md` 에 세 상태 게이트를 적는다**

담을 것 — 스펙 §4·§5 에서 옮긴다:

- 세 상태 표(싱글 / 권위 / 팔로워)와 **싱글과 권위가 같은 코드**라는 이유.
- `StageSession.For` 를 쓰고 `NetworkRunner.Instances.Count` 를 세지 말 것. 왜 그런지(2026-08-31 사고).
- 판정은 `Resolve` 가 갖고 `For` 는 갖지 않는다 — EditMode 테스트 가능성 때문.
- 팔로워가 무엇을 읽는지는 `ThiefNetworkHub.PresentedAction` 의 `PresentedX` 패턴을 따른다.
- 스펙 링크: `docs/specs/2026-08-31-single-multi-pipeline.md`.

- [ ] **Step 2: `Cleanliness/AGENTS.md` 의 "미정" 을 결정으로 바꾼다**

"## 미정" 절의 **"멀티플레이의 종료 흐름은 아직 정하지 않았다"** 문단을 지우고, "## Decisions" 에
2026-08-31 항목으로 넣는다:

- 멀티의 종료 흐름은 **싱글과 같다.** 두 모드는 흐름이 같고 인원과 밸런스만 다르다.
- 인원 수는 `StageSession.PlayerCount` 로 읽는다. 싱글은 1.
- 빌더 수정 기준(스펙 §6 표): 일반 컴포넌트는 SinglePlay 에 넣고 빌드만 다시,
  `[Networked]` 복제 상태가 필요할 때만 빌더에 스폰 리그를 더한다.
- `DisableSinglePeerRigs()` 는 현재 no-op 이지만 보험으로 남긴다.

- [ ] **Step 3: `docs/INDEX.md` 에 현재 상태 한 줄**

맨 위 "## 현재 상태" 에 2026-08-31 항목으로 더한다 — 싱글·멀티 관계를 규약으로 굳혔고
`StageSession` 이 세션 판정의 단일 창구이며, 스펙과 이 계획을 링크한다.

- [ ] **Step 4: 문서만 바뀌었는지 확인한다**

```bash
cm status
```

기대: 위 세 파일만 `Changed`. 코드 변경 없음.

---

## 체크인 (3회)

⚠ **체크인 전에 브랜치를 정한다.** 작성 시점 워크스페이스는 `/main` 이고 프로젝트 규칙은 피처
브랜치를 요구한다. 또한 내 것이 아닌 `Assets/_Recovery/0 (5).unity` 변경이 걸려 있다 — **그것을
같이 체크인하지 않는다.**

```bash
cm branch create /main/single-multi-pipeline -c "싱글·멀티 파이프라인 규약과 StageSession"
cm switch /main/single-multi-pipeline
```

체크인 절차(루트 `AGENTS.md`):

1. `cm status` 로 전체 경로 목록을 **먼저** 만든다. 나중에 빠진 것을 발견하면 체크인이 하나 더 든다.
2. 에디터 밖에서 만든 파일은 `Private` 다 → `cm add <경로>` 로 개별 지정한다.
   **`cm add -R` 을 쓰지 않는다** — `.omo/` 같은 개인 도구 산출물을 쓸어 담는다.
3. 수정된 파일은 `cm checkout <경로>` 를 먼저 해야 `cm ci` 가 받는다.
4. `.meta` 는 에셋을 따라가지 않는다. 새 `.cs` · `.asmdef` 는 `.meta` 도 같이 이름 짓는다.
5. 삭제와 수정은 같은 `cm ci` 에 못 섞는다.

| # | 딜리버러블 | 경로 |
|---|---|---|
| 1 | 설계 | `docs/specs/2026-08-31-single-multi-pipeline.md`, `docs/plans/2026-08-31-single-multi-pipeline.md` |
| 2 | 구현 | `StageSession.cs`(+`.meta`), `Tests/EditMode/` 전체(+`.meta`), `SnowCpuStage.cs` |
| 3 | 문서 | `Core/Multiplay/AGENTS.md`, `Cleanliness/AGENTS.md`, `docs/INDEX.md` |

체크인마다 `cm status` 로 남은 것이 없는지 확인한다.

---

## 완료 기준

- `StageSessionTests` 7개 PASS.
- 눈 EditMode 테스트가 변경 전과 같은 결과(기존 알려진 실패 제외).
- SinglePlay 를 Play 로 띄워 콘솔 에러 0, 눈이 정상적으로 선다.
- Play Mode off, 원래 씬 활성, dirty 없음, `__TEST__` 잔여물 없음.
- `cm status` 에 계획에 없는 경로가 남아 있지 않다.
