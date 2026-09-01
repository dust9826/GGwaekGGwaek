# 인게임 멀티 재구축 — 1단계: 해체 구현 계획

> **에이전트 작업자에게:** 이 계획은 `superpowers:subagent-driven-development` 또는
> `superpowers:executing-plans` 로 태스크 단위로 실행한다. 단계는 체크박스(`- [ ]`)로 추적한다.

**목표:** 구 인게임 멀티(제설차 계열)를 완전히 제거하고, 권위 눈 격자와 세션 계층이
**컴파일되고 EditMode 테스트가 통과하는 상태**로 남긴다.

**접근:** 결합을 먼저 끊고(수정) → 로비 프리팹을 옮기고(이동) → 구 멀티를 지운다(삭제).
이 순서여야 모든 중간 체인지셋이 빌드된다. 반대로 하면 삭제 직후가 컴파일 에러다.

**기술 스택:** Unity 6000.6.0b7 · URP 17.6.0 · Photon Fusion 2.1.1 · Plastic SCM · Unity CLI
(`~/.unity/bin/unity`)

**스펙:** `docs/specs/2026-08-24-multiplay-penguin-rebuild.md` (§2 · §4 · §12)

**브랜치:** `/main/multiplay-penguin` (이미 전환됨, `cs:618` 기준)

## 전역 제약

- **git 을 쓰지 않는다.** 버전 관리는 Plastic SCM 이다 — `cm status` · `cm checkout` · `cm add` ·
  `cm move` · `cm remove` · `cm ci --commentsfile=<file>`.
- **삭제 경로와 수정 경로는 같은 `cm ci` 에 들어갈 수 없다.** 섞으면 체크인 전체가 무관한 파일을
  지목하며 실패하고 아무것도 커밋되지 않는다.
- **수정된 파일은 `cm ci` 전에 `cm checkout` 해야 한다.** 에디터 밖에서 편집한 파일은 `Changed`
  상태로 남고, 그 상태로는 명시적으로 이름을 대도 체크인이 거부된다.
- **`.meta` 는 에셋을 따라가지 않는다.** 에셋과 `.meta` 를 둘 다 이름 대거나 폴더를 대고 수정
  파일을 따로 추가한다.
- **에셋 이동·삭제는 Unity 를 통해서만 한다.** Finder 나 셸로 하면 `.meta` GUID 가 흔들린다.
  Unity CLI 의 `delete_asset` 은 `AssetDatabase` 를 거치므로 이 조건을 만족한다.
- **`cm add -R` 를 저장소 루트에 쓰지 않는다.** `.omc` 같은 개인 도구 산출물을 쓸어담는다.
  경로를 명시한다.
- **네임스페이스는 `PPack` 하나다.** 폴더를 따라가지 않는다.
- **새 추상은 두 번째 호출 지점이 확인된 뒤에 만든다.** 이 계획에서는 인터페이스를 새로 만들지
  않는다 — 지우기만 한다.
- 비공개 필드는 `_camelCase`, 타입·메서드는 `PascalCase`, enum 타입명은 `E` 접두사.

## 파일 구조

| 파일 | 이 계획에서의 책임 |
|---|---|
| `Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs` | 블레이드(차량) 경로를 들어낸다. 아바타·눈덩이 경로는 손대지 않는다. 클라 이완의 앵커를 차량에서 눈덩이로 옮긴다 |
| `Assets/Game/InGame/UI/SnowballCoopPush/Scripts/SnowballCoopTimingHud.cs` | `MultiplayPenguin` 오버로드와 필드를 제거한다. 싱글(`PenguinSnowball`) 경로는 그대로 |
| `Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs` | 인게임 씬 경로·기본 아바타 문자열을 지운다. 단계 기계는 손대지 않는다 |
| `Assets/Game/InGame/Multiplay/Resources/PF_SessionLobby.prefab` | `Core/Multiplay/Resources/` 로 이동 |
| `Assets/Game/InGame/Multiplay/**` | 삭제 |
| `Assets/Game/InGame/Snow/Tests/PlayMode/{SnowCpuSyncTests,SnowSyncScaleTests,SnowCpuStageMultiplayTests,SnowPlowPrefabTests}.cs` | **먼저** 삭제(Task 2) |

체크인은 다섯이다 — **회귀 확인(체크인 없음) → 죽은 테스트 삭제 → 수정만 → 이동만 →
구 멀티 삭제 → 문서.** 삭제가 앞뒤로 두 번 나뉘는 이유는 §Task 2 에 있다.
태스크마다 커밋하지 않는다. 루트 `AGENTS.md` 가 *"One check-in per deliverable, not per task"*
라고 못박았고, 위의 삭제/수정 분리 제약이 경계를 이미 정해 놓았다.

---

## Task 1: Raymarch 얼룩말 회귀 확인 (게이트)

`cs:616` 이 `_residueMm` 을 0 으로 내렸다. `snow-regions` §7 이 **잔설 0 이 Raymarch 도랑 안쪽의
얼룩말 아티팩트를 깨운다**고 경고했고 아직 눈으로 보지 않았다. `Snow_Slope_Test` 는 Displace
경로라 이 회귀를 보여주지 않는다.

**이건 게이트다.** 아티팩트가 나오면 여기서 멈추고 사람에게 보고한다 — 룩 수정이 멀티 해체보다
먼저다.

**Files:** 없음(확인만)

**Interfaces:**
- Consumes: 없음
- Produces: 게이트 판정 하나 — 진행 / 중단

- [ ] **Step 1: 테스트 전 에디터 상태를 기록한다**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" list_open_scenes --no-banner
```

`name` · `isDirty` · `isActive` 를 적어 둔다. **`isDirty` 가 참인 씬이 있으면 그 씬을 다시 열거나
버리지 않는다** — 그때는 사람에게 묻는다.

- [ ] **Step 2: Raymarch 경로를 쓰는 씬을 연다**

```bash
$U cmd --project-path "$P" open_scene --path "Assets/Game/InGame/Snow/Tests/Snow_Raymarch_Test.unity" --no-banner
```

- [ ] **Step 3: 눈덩이가 판 도랑을 만든다**

Play Mode 에 들어가 눈덩이를 굴린다.

```bash
$U cmd --project-path "$P" set_autotick --enable true --interval_ms 33 --no-banner
$U cmd --project-path "$P" editor_play --no-banner
```

⚠ **Unity CLI 는 Play Mode 중 `set_component_properties` · `set_active` · `delete_gameobject` 를
모두 거부한다.** 실측으로 확인됐다. 따라서 공을 굴리는 조작은 사람이 키보드로 해야 한다.
사람에게 "눈덩이를 한 줄 굴려 달라"고 요청하고 기다린다.

- [ ] **Step 4: 도랑 안쪽을 캡처한다**

```bash
$U cmd --project-path "$P" screenshot --view Game --output /tmp/raymarch_residue0.png \
      --width 1600 --height 900 --no-banner --timeout 60
```

캡처를 눈으로 본다. **찾는 것:** 도랑 바닥에 밝고 어두운 띠가 번갈아 나타나는 무늬(얼룩말).
높이가 정확히 0 인 넓은 면에서 나온다.

- [ ] **Step 5: 에디터를 원상 복구한다**

```bash
$U cmd --project-path "$P" editor_stop --no-banner
$U cmd --project-path "$P" open_scene --path "<Step 1 에서 적어 둔 원래 씬>" --no-banner
$U cmd --project-path "$P" list_open_scenes --no-banner   # isDirty 가 false 인지 확인
$U cmd --project-path "$P" clear_console --no-banner
```

`/tmp` 밖에 캡처를 남기지 않는다. 프로젝트 안에 `__TEST__` 이름이 남지 않았는지 확인한다.

- [ ] **Step 6: 판정**

- 얼룩말이 **없다** → Task 2 로 간다.
- 얼룩말이 **있다** → **여기서 멈춘다.** 캡처를 첨부해 사람에게 보고하고 룩 수정 여부를 묻는다.
  이건 예상된 회귀이지 놀랄 일이 아니다(`snow-regions` §7).

---

## Task 2: 죽은 PlayMode 테스트 삭제 + 체크인 A (삭제만)

**왜 이것이 먼저인가.** 이 넷은 `MultiplayPlowVehicle` · `MultiplayPenguin` ·
`SnowCpuStage.SteppedVehiclesLastTick` 을 읽는다. 결합 해체(Task 3)가
`SteppedVehiclesLastTick` 을 지우는 순간 이 파일들이 컴파일되지 않으므로, **해체보다 먼저**
치워야 중간 체인지셋이 빌드된다. 그리고 삭제는 수정과 같은 체크인에 못 들어가므로 별도다.

셋은 셀 델타 복제를 검증하는데 그 복제 자체가 2단계에서 사라진다. 대체 테스트는 명령 복제와
함께 새로 쓴다 — 고쳐 쓸 것이 남지 않는다.

**Files:**
- Delete: `Assets/Game/InGame/Snow/Tests/PlayMode/SnowCpuSyncTests.cs`
- Delete: `Assets/Game/InGame/Snow/Tests/PlayMode/SnowSyncScaleTests.cs`
- Delete: `Assets/Game/InGame/Snow/Tests/PlayMode/SnowCpuStageMultiplayTests.cs`
- Delete: `Assets/Game/InGame/Snow/Tests/PlayMode/SnowPlowPrefabTests.cs`

**Interfaces:**
- Consumes: Task 1 의 게이트 통과
- Produces: `SteppedVehiclesLastTick` 을 읽는 코드가 프로젝트에 하나도 없다.
  `PPack.Snow.PlayModeTests.asmdef` 와 `SnowHeadlessTests.cs` 는 **남는다** — 지워질 타입을
  참조하지 않는 것을 실측으로 확인했다.

- [ ] **Step 1: 남는 테스트가 안전한지 확인한다**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
grep -n "MultiplayPlowVehicle\|MultiplayPenguin\|MultiplayAvatar\|SteppedVehiclesLastTick\|MP_Gameplay\|PF_Multiplay" \
  Assets/Game/InGame/Snow/Tests/PlayMode/SnowHeadlessTests.cs \
  Assets/Game/Core/Multiplay/Tests/PlayMode/MultiplayHeadlessTests.cs
```

기대: **출력 없음.** 무언가 나오면 그 파일도 이 태스크의 대상이므로 목록에 넣고 사람에게 알린다.

- [ ] **Step 2: 지울 목록을 먼저 미리보기로 확인한다**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
for a in SnowCpuSyncTests SnowSyncScaleTests SnowCpuStageMultiplayTests SnowPlowPrefabTests; do
  $U cmd --project-path "$P" delete_asset \
        --asset "Assets/Game/InGame/Snow/Tests/PlayMode/$a.cs" --dry_run true --no-banner
done
```

- [ ] **Step 3: Unity 를 거쳐 지운다**

```bash
for a in SnowCpuSyncTests SnowSyncScaleTests SnowCpuStageMultiplayTests SnowPlowPrefabTests; do
  $U cmd --project-path "$P" delete_asset \
        --asset "Assets/Game/InGame/Snow/Tests/PlayMode/$a.cs" --confirm true --no-banner
done
```

`.meta` 는 `delete_asset` 이 함께 처리한다 — 셸로 지우지 않는 이유가 이것이다.

- [ ] **Step 4: 컴파일하고 EditMode 를 돌린다**

```bash
$U cmd --project-path "$P" recompile --no-banner
until $U cmd --project-path "$P" recompile_status --no-banner | grep -qE "completed|up_to_date"; do :; done
$U cmd --project-path "$P" get_console_logs --severity Error --limit 30 --no-banner
$U cmd --project-path "$P" run_tests --mode EditMode --timeout 600 --no-banner
```

기대: 컴파일 에러 0건, EditMode 전부 통과.

- [ ] **Step 5: 체크인 A — 삭제만**

```bash
cm status   # Deleted 넷만 있어야 한다
```

코멘트(`/tmp/ci_delete_tests.txt`):

```
셀 델타 복제를 검증하던 PlayMode 테스트를 지운다

SnowCpuSyncTests · SnowSyncScaleTests · SnowCpuStageMultiplayTests ·
SnowPlowPrefabTests 넷이다. 지워지는 제설차 타입과 SteppedVehiclesLastTick 을
읽으므로 결합 해체보다 먼저 치워야 중간 체인지셋이 빌드된다.

고쳐 쓸 것이 남지 않아 지운다 - 셋이 검증하던 셀 델타 복제 자체가 사라지고
(4인 전달률 2.4% 로 무너졌고 눈이 점수가 아니게 되면서 존재 이유가 없어졌다),
대체 테스트는 2단계에서 명령 복제와 함께 새로 쓴다.

SnowHeadlessTests 와 asmdef 는 남는다 - 지워질 타입을 참조하지 않는 것을 확인했다.
설계: docs/specs/2026-08-24-multiplay-penguin-rebuild.md
```

```bash
cm ci "Assets/Game/InGame/Snow/Tests/PlayMode/SnowCpuSyncTests.cs" \
      "Assets/Game/InGame/Snow/Tests/PlayMode/SnowSyncScaleTests.cs" \
      "Assets/Game/InGame/Snow/Tests/PlayMode/SnowCpuStageMultiplayTests.cs" \
      "Assets/Game/InGame/Snow/Tests/PlayMode/SnowPlowPrefabTests.cs" \
      --commentsfile=/tmp/ci_delete_tests.txt
cm status
```

---

## Task 3: `SnowCpuStage` 블레이드 경로 제거

권위 격자에서 차량 결합을 들어낸다. 아바타 경로(`Runner.TryGetPlayerObject`)와 눈덩이 경로는
구체 타입을 보지 않으므로 **손대지 않는다**.

**Files:**
- Modify: `Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `SnowCpuStage` 가 `MultiplayPlowVehicle` · `SnowPlowBlade` · `ISnowBladeState` 를
  더 이상 참조하지 않는다. `public int SteppedVehiclesLastTick` 이 사라진다 — 이것을 읽던
  PlayMode 테스트 셋은 Task 2 에서 이미 지워졌으므로 남은 호출자가 없다(실측 확인).


- [ ] **Step 1: 지울 심볼 목록을 확정한다**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
grep -n "_vehicles\|_prevPose\|_localBody\|_bladeState\|_legacyBlade\|_standalonePrev\|_standaloneHasPrev\|MultiplayPlowVehicle\|SnowPlowBlade\|SteppedVehiclesLastTick\|_bladeAheadM" \
  Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs
```

기대: 40줄 안팎. 이 목록이 이 태스크의 작업 범위 전부다.

**남기는 것:** `_bladeWidthM` · `_bladeThicknessM` 은 지우지 않는다. `Build()` 가 이 둘로
`_sim.Shape` 를 만들고, 그 `_sim` 을 **눈덩이 경로가 공유한다**(`:543`, `:558`, `:576`, `:647`).
이름이 블레이드지만 지금은 플로우 스텝의 형상이다. 이름 정리는 이 계획의 범위 밖이다.

- [ ] **Step 2: `FixedUpdateNetwork` 의 차량 루프를 제거한다**

`:387`~`:442`. 서버 분기에서 차량을 열거하고 스윕하던 블록 전체를 지운다. 남는 서버 분기는
이렇게 된다:

```csharp
            StepBalls(Runner.DeltaTime);

            SendDelta();
        }
```

같은 메서드의 클라이언트 분기(`:378`~`:385`)에서 `SteppedVehiclesLastTick = 0;` 줄을 지운다.

- [ ] **Step 3: `RelaxOnly()` 의 앵커를 눈덩이로 옮긴다**

**이건 삭제가 아니라 동작 변경이다.** `RelaxOnly()` 는 클라이언트가 자기 격자에서 안식각 이완을
돌리는 경로인데, 지금 차량 주변만 활성 집합으로 잡는다(`:773`~`:790`). 차량이 사라지면 이완이
통째로 죽어 클라 화면에 수직 벽이 남는다.

눈덩이를 앵커로 바꾼다. `_balls` 목록과 열거 방식은 이미 있다(`:151`, `:465-466`):

```csharp
        private void RelaxOnly()
        {
            _balls.Clear();
            Runner.GetAllBehaviours(_balls);

            for (int i = 0; i < _balls.Count; i++)
            {
                SnowBallCarrier ball = _balls[i];
                if (ball == null) continue;

                Vector3 p = ball.transform.position;
                var pose = new SnowBladePose
                {
                    CenterX = p.x,
                    CenterZ = p.z,
                    ForwardX = 1f,
                    ForwardZ = 0f,
                };

                _sim.Step(new SnowPlowStepInput
                {
                    Prev = pose,
                    Now = pose,
                    BladeDown = false,
```

메서드의 나머지(스텝 입력의 뒷부분과 닫는 괄호)는 그대로 둔다. `BladeDown = false` 라 절삭은
일어나지 않는다 — 이 스텝이 하는 일은 공 주변을 활성 집합으로 잡는 것뿐이고, 그것이 서버의
눈덩이 경로가 하는 일과 같다(`:455-462` 주석).

`ForwardX = 1f` 는 임의 방향이다. `BladeDown = false` 이므로 방향이 결과에 들어가지 않는다.

- [ ] **Step 4: 독립 실행 경로에서 차량 탐색을 제거한다**

`FixedUpdate()` (`:686`). `StepBallsStandalone(Time.fixedDeltaTime);` 만 남기고 `:694` 부터
`:755` 까지의 `_localBody` 탐색·스윕 블록을 전부 지운다. 이어서 `:757`~`:758` 의
`_standalonePrev` · `_standaloneHasPrev` 필드 선언도 지운다.

메서드는 이렇게 남는다:

```csharp
        private void FixedUpdate()
        {
            if (!_standalone || _field == null) return;

            // <b>공은 차량이 없어도 굴러야 한다.</b> 펭귄이 미는 판에는 제설차가 없다.
            StepBallsStandalone(Time.fixedDeltaTime);
        }
```

- [ ] **Step 5: 남은 필드와 공개 속성을 지운다**

- `:145` `_vehicles`
- `:148-149` `_prevPose`
- `:170` `public int SteppedVehiclesLastTick { get; private set; }`
- `:314` `_bladeState`
- `:317` `_localBody`
- `:320` `_legacyBlade`
- `:46` `_bladeAheadM` (직렬화 필드 — 차량 앞 오프셋이라 남을 이유가 없다)

- [ ] **Step 6: 컴파일한다**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" recompile --no-banner
until $U cmd --project-path "$P" recompile_status --no-banner | grep -qE "completed|up_to_date"; do :; done
$U cmd --project-path "$P" get_console_logs --severity Error --limit 30 --no-banner
```

기대: **에러 0건.** `MultiplayPlowVehicle` 은 아직 존재하므로(Task 6 에서 지운다) 이 시점에
깨질 것은 없다.

에러가 나면 그 파일이 이 계획이 놓친 결합이다. 지우지 말고 사람에게 보고한다.

---

## Task 4: `SnowballCoopTimingHud` 의 `MultiplayPenguin` 결합 제거

**Files:**
- Modify: `Assets/Game/InGame/UI/SnowballCoopPush/Scripts/SnowballCoopTimingHud.cs`

**Interfaces:**
- Consumes: 없음
- Produces: `SnowballCoopTimingHud.Create(PenguinSnowball)` 만 남는다.
  `Create(MultiplayPenguin)` 오버로드가 사라진다. 유일한 호출자가
  `MultiplayPenguin.cs:133` 이고 그 파일은 Task 6 에서 지워진다.

- [ ] **Step 1: 필드를 지운다**

`:15` 의 `private MultiplayPenguin _networkOwner;` 를 지운다.

- [ ] **Step 2: 오버로드를 지운다**

`:27-31` 전체:

```csharp
        public static void Create(MultiplayPenguin owner)
        {
            SnowballCoopTimingHud hud = CreateHost(owner);
            if (hud != null) hud._networkOwner = owner;
        }
```

- [ ] **Step 3: 읽는 곳 두 줄을 단순화한다**

`:69-72` 를 이렇게 바꾼다:

```csharp
            SnowBallCarrier ball = _singleOwner != null ? _singleOwner.Held : null;
            Component owner = _singleOwner;
```

- [ ] **Step 4: 컴파일하고 콘솔을 확인한다**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" recompile --no-banner
until $U cmd --project-path "$P" recompile_status --no-banner | grep -qE "completed|up_to_date"; do :; done
$U cmd --project-path "$P" get_console_logs --severity Error --limit 30 --no-banner
```

기대: 에러 하나. `MultiplayPenguin.cs:133` 이 아직 `Create(this)` 를 부르는데, `this` 가
`MultiplayPenguin` 이고 그것은 `Component` 이므로 **`Create(PenguinSnowball)` 오버로드와 맞지
않아 에러가 난다.**

⚠ **그러므로 이 단계에서 에러가 하나 예상된다** — `MultiplayPenguin.cs(133)` 의
`CS1503` 또는 `CS1929`. 그것 **하나만** 나오면 정상이고 Step 5 가 해소한다.
다른 파일에서 에러가 나면 놓친 결합이니 보고한다.

- [ ] **Step 5: 그 하나를 임시로 끈다**

체크인 B 가 빌드되어야 하므로, `MultiplayPenguin.cs:133` 의 호출을 지운다:

```csharp
            // 협동 HUD 는 싱글 경로만 남았다. 이 파일 자체가 곧 삭제된다.
```

로 `if (HasInputAuthority) SnowballCoopTimingHud.Create(this);` 를 대체한다.

다시 컴파일해 **에러 0건**을 확인한다.

---

## Task 5: `SessionLauncher` 의 인게임 결합 정리 + 체크인 B (수정만)

**Files:**
- Modify: `Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs:31,39,51,54`

**Interfaces:**
- Consumes: Task 3 · Task 4 의 수정
- Produces: `SessionLauncher` 가 인게임 프리팹 이름과 씬 경로를 하드코딩하지 않는다.
  `AvatarResourcePath` 는 `AvatarResourceOverride ?? SceneAvatarResource` 이고 **둘 다 없으면
  스폰하지 않는다.** `GameplayScenePath` 는 `SceneGameplayPath` 로 대체되어 씬이 정한다.

- [ ] **Step 1: 씬 경로 상수를 정적 속성으로 바꾼다**

`:31` 의

```csharp
        public const string GameplayScenePath = "Assets/Game/InGame/Multiplay/Scenes/MP_Gameplay.unity";
```

를 지우고 그 자리에

```csharp
        /// <summary>
        /// 게임플레이 씬 경로. <b>Core 가 인게임 씬 이름을 알면 안 된다</b> — 인게임 쪽 부트스트랩이
        /// 넣는다. 비어 있으면 로딩 단계에서 멈추고 에러를 남긴다.
        /// </summary>
        public static string GameplayScenePath { get; set; }
```

- [ ] **Step 2: 기본 아바타 이름을 없앤다**

`:51` 의

```csharp
            => AvatarResourceOverride ?? SceneAvatarResource ?? "PF_MultiplayPlow";
```

를

```csharp
            => AvatarResourceOverride ?? SceneAvatarResource;
```

로 바꾼다. `:39` 의 캡슐·제설차를 설명하는 주석과 `:54` 의 `MultiplayAvatarChoice` 를 가리키는
`<see cref="..."/>` 도 함께 지운다 — 그 타입은 Task 6 에서 사라지므로 남겨 두면 XML 문서 경고가
난다.

- [ ] **Step 3: 아바타 이름이 없을 때 스폰을 건너뛴다**

`SpawnAvatar`(`:491`) 의 첫 줄에 가드를 넣는다:

```csharp
            string resource = AvatarResourcePath;
            if (string.IsNullOrEmpty(resource))
            {
                Debug.LogError($"{nameof(SessionLauncher)}: 아바타 리소스가 정해지지 않았다. " +
                               "씬이 SceneAvatarResource 를 넣어야 한다.");
                return;
            }
```

이어지는 `Resources.Load<NetworkObject>(AvatarResourcePath)` 를 `Resources.Load<NetworkObject>(resource)`
로 바꾼다.

- [ ] **Step 4: 씬 경로가 없을 때 로딩을 멈춘다**

`:301` 의 `SceneUtility.GetBuildIndexByScenePath(GameplayScenePath)` 앞에 가드를 넣는다:

```csharp
            if (string.IsNullOrEmpty(GameplayScenePath))
            {
                Debug.LogError($"{nameof(SessionLauncher)}: 게임플레이 씬 경로가 정해지지 않았다.");
                return;
            }
```

- [ ] **Step 5: 컴파일하고 EditMode 전체를 돌린다**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" recompile --no-banner
until $U cmd --project-path "$P" recompile_status --no-banner | grep -qE "completed|up_to_date"; do :; done
$U cmd --project-path "$P" get_console_logs --severity Error --limit 30 --no-banner
$U cmd --project-path "$P" run_tests --mode EditMode --timeout 600 --no-banner
```

기대: 컴파일 에러 0건, EditMode **전부 통과**.

⚠ PlayMode 는 여기서 돌리지 않는다. 이 세션에서 PlayMode 수집기는 한 번만 동작하고, 그 뒤로는
0건을 반환한다. 판별은 EditMode 로 내린다.

- [ ] **Step 6: 체크인 B — 수정만**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
cm status
cm checkout \
  "Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs" \
  "Assets/Game/InGame/UI/SnowballCoopPush/Scripts/SnowballCoopTimingHud.cs" \
  "Assets/Game/InGame/Multiplay/Scripts/MultiplayPenguin.cs" \
  "Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs"
```

코멘트 파일을 쓴다(`/tmp/ci_decouple.txt`):

```
권위 눈과 세션 계층에서 제설차 결합을 끊는다

SnowCpuStage 가 List<MultiplayPlowVehicle> 을 직접 들고 있어 구 멀티를 지우면
눈이 컴파일되지 않았다. 인터페이스 뒤로 숨기지 않고 블레이드 경로를 들어낸다 -
ISnowBladeState 는 멤버가 BladeDown 과 AngleState 뿐이고 둘 다 블레이드 개념이라
펭귄에 맞지 않고, 차량을 버리면 구현체가 하나뿐이라 새 인터페이스도 근거가 없다.
아바타 경로는 Runner.TryGetPlayerObject 로 이미 구체 타입을 안 보므로 그대로 둔다.

RelaxOnly 는 삭제가 아니라 앵커 교체다. 차량 주변만 활성 집합으로 잡고 있어서
그대로 지우면 클라이언트의 안식각 이완이 죽고 화면에 수직 벽이 남는다. 눈덩이를
앵커로 바꿨다 - BladeDown=false 라 절삭은 없고 하는 일은 서버의 눈덩이 경로와 같다.

_bladeWidthM 과 _bladeThicknessM 은 남긴다. Build() 가 이 둘로 만드는 _sim.Shape 를
눈덩이 경로가 공유한다. 이름이 블레이드지만 지금은 플로우 스텝의 형상이다.

SessionLauncher 에서 MP_Gameplay 경로 상수와 기본 아바타 "PF_MultiplayPlow" 를
없앴다. Core 가 인게임 씬 이름을 아는 것이 애초에 경계 위반이었다 - 씬이 넣는다.
```

```bash
cm ci \
  "Assets/Game/InGame/Snow/HeightCpu/SnowCpuStage.cs" \
  "Assets/Game/InGame/UI/SnowballCoopPush/Scripts/SnowballCoopTimingHud.cs" \
  "Assets/Game/InGame/Multiplay/Scripts/MultiplayPenguin.cs" \
  "Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs" \
  --commentsfile=/tmp/ci_decouple.txt
cm status   # 위 넷이 사라졌는지 확인
```

---

## Task 6: `PF_SessionLobby` 이동 + 체크인 C (이동만)

로비 프리팹이 버릴 폴더 안에 있다. `Resources` 아래이기만 하면 되므로 세션 계층 옆으로 옮긴다.

**Files:**
- Move: `Assets/Game/InGame/Multiplay/Resources/PF_SessionLobby.prefab`
  → `Assets/Game/Core/Multiplay/Resources/PF_SessionLobby.prefab`

**Interfaces:**
- Consumes: 체크인 B (수정만)
- Produces: `SessionLauncher:71` 의 `Resources.Load("PF_SessionLobby")` 가 계속 찾는다.
  GUID 가 보존되므로 프리팹을 참조하는 다른 에셋도 깨지지 않는다.

- [ ] **Step 1: 목적지 폴더를 만들고 Unity 로 옮긴다**

⚠ **Unity CLI 에 에셋 이동 명령이 없다.** 실측으로 확인했다 — `rename_asset` 은 있지만
*"keeps it in the same folder"* 라 폴더를 못 넘는다. 그러므로 **이 단계는 사람이 한다.**

사람에게 요청한다:

> Unity Project 창에서 `Assets/Game/InGame/Multiplay/Resources/PF_SessionLobby.prefab` 을
> `Assets/Game/Core/Multiplay/Resources/` 로 드래그해 주세요. (`Resources` 폴더가 없으면
> 먼저 만들어 주세요.) 한 번의 드래그입니다.

**셸의 `mv` 로 대신하지 않는다** — 루트 `AGENTS.md` 가 에셋 이동을 Project 창으로 한정한다.
이동 전 GUID 를 먼저 적어 둔다:

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
grep -m1 "^guid:" "Assets/Game/InGame/Multiplay/Resources/PF_SessionLobby.prefab.meta"
```

- [ ] **Step 2: GUID 가 보존됐는지 확인한다**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
grep -m1 "^guid:" "Assets/Game/Core/Multiplay/Resources/PF_SessionLobby.prefab.meta"
```

이동 전 GUID 와 같아야 한다. 이동 전에 미리 적어 둔다.

- [ ] **Step 3: 로비가 여전히 로드되는지 확인한다**

```bash
$U cmd --project-path "$P" find_assets --name "PF_SessionLobby" --no-banner
```

`Assets/Game/Core/Multiplay/Resources/` 아래에 하나만 나와야 한다.

- [ ] **Step 4: 체크인 C — 이동만**

```bash
cm status   # moved 로 잡히는지 확인
```

코멘트(`/tmp/ci_move_lobby.txt`):

```
로비 프리팹을 세션 계층 옆으로 옮긴다

PF_SessionLobby 는 로비라 남는데 곧 삭제될 InGame/Multiplay/Resources 안에 있었다.
Core/Multiplay/Resources 로 옮긴다 - SessionLauncher 는 Resources.Load 로 이름만
보므로 Resources 아래이기만 하면 된다. Unity 를 거쳐 옮겨 GUID 를 보존했다.
```

```bash
cm ci "Assets/Game/Core/Multiplay/Resources" \
      "Assets/Game/Core/Multiplay/Resources.meta" \
      --commentsfile=/tmp/ci_move_lobby.txt
cm status
```

⚠ 이동이 Plastic 에서 `Deleted` + `Added` 로 잡히면 **삭제 체크인과 섞이지 않도록 이 체크인만
따로** 낸다. 그것이 이 태스크를 독립시킨 이유다.

---

## Task 7: 구 멀티 삭제 + 체크인 D (삭제만)

**Files:**
- Delete: `Assets/Game/InGame/Multiplay/` 전체 (죽은 PlayMode 테스트는 Task 2 에서 이미 지웠다)

**Interfaces:**
- Consumes: 체크인 A · B · C
- Produces: 인게임 멀티가 없다. 컴파일은 통과하고 EditMode 는 전부 녹색이다.
  **멀티는 플레이할 수 없다** — 2단계 계획이 다시 만든다.

- [ ] **Step 1: 삭제 전 마지막 참조 확인**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
grep -rn "MultiplayPlowVehicle\|MultiplayPenguin\|MultiplayAvatar\|MultiplayChaseCamera\|MP_Gameplay\|PF_MultiplayPlow" \
  Assets/Game --include="*.cs" | grep -v "^Assets/Game/InGame/Multiplay/" \
  | grep -v "^Assets/Game/InGame/Snow/Tests/PlayMode/"
```

기대: **출력 없음.** 무언가 나오면 그것이 놓친 결합이다. 지우지 말고 Task 3~5 로 돌아간다.

- [ ] **Step 2: Unity 를 거쳐 지운다**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" delete_asset --asset "Assets/Game/InGame/Multiplay" --dry_run true --no-banner
$U cmd --project-path "$P" delete_asset --asset "Assets/Game/InGame/Multiplay" --confirm true --no-banner
```

위의 첫 줄이 미리보기다. 지워질 목록에 `PF_SessionLobby` 가 **없는지** 확인한다 — 있으면
Task 6 의 이동이 안 된 것이므로 멈춘다.

- [ ] **Step 3: 컴파일하고 EditMode 전체를 돌린다**

```bash
$U cmd --project-path "$P" recompile --no-banner
until $U cmd --project-path "$P" recompile_status --no-banner | grep -qE "completed|up_to_date"; do :; done
$U cmd --project-path "$P" get_console_logs --severity Error --limit 30 --no-banner
$U cmd --project-path "$P" run_tests --mode EditMode --timeout 600 --no-banner
```

기대: 컴파일 에러 0건, EditMode 전부 통과.

- [ ] **Step 4: 체크인 D — 삭제만**

```bash
cm status   # Deleted 만 있어야 한다. Changed 가 섞여 있으면 그것부터 따로 처리한다
```

코멘트(`/tmp/ci_delete_multi.txt`):

```
구 인게임 멀티를 제거한다

제설차 계열과 MP_Gameplay 를 통째로 지운다. 남길 이유가 없었다 - 저항이 v7 시각
필드에 물려 있고(MultiplayPlowVehicle:190,201) 그 필드가 MP_Gameplay 에 아예 없어서
눈이 차량 속도에 아무 영향을 주지 않는 상태로 죽어 있었다. 리그를 다시 붙여도
데디 서버에는 GPU 가 없어 서버는 배수 1, 클라는 저항 적용이 되어 영구 예측 불일치가
된다. 플레이어도 제설차가 아니라 펭귄으로 바뀐다.

로비-접속 세션 계층(Core/Multiplay)은 그대로 남고, 로비 프리팹은 이미 그쪽으로 옮겼다.
이 폴더를 읽던 PlayMode 테스트 넷은 앞선 체크인에서 지웠다.

멀티는 이 시점부터 2단계가 끝날 때까지 플레이할 수 없다. 의도된 것이다.
설계: docs/specs/2026-08-24-multiplay-penguin-rebuild.md
```

```bash
cm ci "Assets/Game/InGame/Multiplay" --commentsfile=/tmp/ci_delete_multi.txt
cm status
```

⚠ 디렉터리 삭제는 `cm remove <path>` 가 필요할 수 있다. 디스크에서만 지우면
`Removed locally` 로 붙박이가 되고 체크인이 "there are no changes" 로 실패한다. 그때
`cm undo` 는 상태를 지우는 게 아니라 저장소에서 디렉터리를 되살린다.

---

## Task 8: 문서 + 체크인 E

**Files:**
- Modify: `Assets/Game/Core/Multiplay/AGENTS.md`
- Modify: `docs/INDEX.md`
- Create: `docs/Session_Summary_<YYYYMMDD>.md` (`<YYYYMMDD>` 는 실행 당일 날짜)
- Add: `docs/specs/2026-08-24-multiplay-penguin-rebuild.md` · 이 계획 파일

**Interfaces:**
- Consumes: 체크인 A · B · C · D
- Produces: 없음

- [ ] **Step 1: `Core/Multiplay/AGENTS.md` 를 고친다**

지금 이렇게 적혀 있다 — 전부 틀리게 된다:

> 플레이어는 **제설차**(`InGame/Multiplay/Resources/PF_MultiplayPlow.prefab`)로 스폰되고,
> 게임플레이 씬은 `InGame/Multiplay/Scenes/MP_Gameplay.unity` 다. 캡슐 아바타
> (`PF_MultiplayAvatar`)는 이동 복제만 따로 확인할 때 쓰라고 남겨 뒀다.

이 문단을 "인게임 아바타와 게임플레이 씬은 **씬이 정해서 `SessionLauncher` 에 넣는다**. Core 는
이름을 모른다" 로 바꾸고, 2026-08-24 에 제설차 계열을 버린 이유를 한 문단으로 남긴다.

`NetworkInputData` 표의 `Buttons` 설명에서 블레이드·날 각도를 뺀다 — **비트 자체는 2단계에서
빼므로 여기서는 설명만 고친다.**

- [ ] **Step 2: `docs/INDEX.md` 상단에 현재 상태를 넣는다**

`## 현재 상태 (2026-08-24)` 절의 머리에 항목을 추가한다. 링크는 스펙과 이 계획.

- [ ] **Step 3: 세션 요약을 쓴다**

`wrap-session` 스킬을 쓴다. **요약을 쓰고 끝내지 않는다 — 체크인까지가 한 덩어리다.**
남겨 두면 `INDEX.md` 가 팀원과 정면 충돌한다(실제로 났다).

- [ ] **Step 4: 체크인 E**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
cm checkout "Assets/Game/Core/Multiplay/AGENTS.md" "docs/INDEX.md"
cm add "docs/specs/2026-08-24-multiplay-penguin-rebuild.md" \
       "docs/plans/2026-08-24-multiplay-penguin-teardown.md" \
       "docs/Session_Summary_<YYYYMMDD>.md"
cm ci "Assets/Game/Core/Multiplay/AGENTS.md" \
      "docs/INDEX.md" \
      "docs/specs/2026-08-24-multiplay-penguin-rebuild.md" \
      "docs/plans/2026-08-24-multiplay-penguin-teardown.md" \
      "docs/Session_Summary_<YYYYMMDD>.md" \
      --commentsfile=/tmp/ci_docs.txt
cm status --private   # 남은 것이 없는지
```

- [ ] **Step 5: 최종 확인**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" list_open_scenes --no-banner     # isDirty 전부 false
$U cmd --project-path "$P" get_console_logs --severity Error --limit 20 --no-banner
cm status                                                    # Private 외에 남은 것 없음
```

---

## 실행 기록 (2026-08-24, 완료)

체크인 순서와 결과. **계획대로 되지 않은 것 셋을 아래에 남긴다** — 다음 계획을 쓸 때 같은
자리에서 다시 틀리지 않으려는 것이다.

| 체인지셋 | 내용 |
|---|---|
| `cs:619` | 눈 렌더링을 Displace 로 통일(맵 셋 + `Snow_BallPush_Test`) |
| `cs:620` | 죽은 PlayMode 테스트 넷 삭제 |
| `cs:621` | 블레이드 경로 제거 · `RelaxOnly` 앵커 교체 · HUD·`SessionLauncher` 결합 해체 |
| `cs:623` · `cs:625` | 로비 프리팹을 `Core/Multiplay/Resources` 로 |
| `cs:626` | 빌드 세팅에서 `MP_Gameplay` 제거 |
| `cs:627` | `InGame/Multiplay/` 통째 삭제 |

매 단계 컴파일 에러 0건, **EditMode 207/207 통과**를 확인했다.

### 틀렸던 것 1 — 게이트 씬을 잘못 지목했다

Task 1 이 `Snow_Raymarch_Test.unity` 를 열라고 했는데 그 씬은 **구세대(`SnowStage` + 차량)** 라
`cs:616` 이 고친 `SnowRaymarchRendererCpu` 경로가 아니다. 맞는 씬은 `Snow_BallPush_Test` 다
(`SnowCpuStage` + 펭귄, `SnowSystem._look = Raymarch`). **`SnowRaymarchRendererCpu` 는 어떤 씬에도
배치돼 있지 않다** — `SnowCpuStageView` 가 런타임에 만든다. 파일 이름으로 씬을 고르지 말고
`SnowSystem._look` 과 뷰 컴포넌트로 골라야 한다.

### 틀렸던 것 2 — 삭제와 해체의 순서가 거꾸로였다

원안은 결합 해체(Task 3)에서 `SteppedVehiclesLastTick` 을 지우고 테스트는 나중에 지웠다. 그러면
그 사이 체인지셋이 컴파일되지 않는다 — 그 속성을 읽는 것이 지워질 테스트들이기 때문이다.
**죽은 테스트를 맨 앞으로 옮겼다.** 삭제/수정을 못 섞는 제약이 있으면 순서까지 따라온다.

### 틀렸던 것 3 — 테스트의 씬 의존을 놓쳤다

`MultiplayHeadlessTests` 를 "지워질 타입을 참조하는가" 로만 검사했고 통과했다. 그런데 그 파일의
두 번째 테스트는 `StartMatch()` 를 통해 **`MP_Gameplay` 씬에 의존**했다. `GameplayScenePath` 기본값을
없애는 순간 깨진다. 더 나아가 그 테스트는 **이미 실패 상태였다** — `rigsTotal > 0` 을 단언하는데
그 씬에 `SnowV7MapRig` 가 없다(GUID 대조).
**타입 참조만 보지 말고 "이 테스트가 무엇을 로드하는가" 를 같이 봐야 한다.**
그래픽 없는 서버에서 GPU 의존이 스스로 꺼지는지 지키는 게이트는 루트 `AGENTS.md` 가 요구하므로,
새 펭귄 씬이 생기면 **같은 검사를 그 씬으로 다시 지목해 되살린다.**

### 계획에 없었는데 필요했던 것

- **빌드 세팅에서 `MP_Gameplay` 제거**(`cs:626`). 안 하면 깨진 항목이 남는다. 수정이라 삭제와
  갈라야 해서 체크인이 하나 늘었다.
- **로비 프리팹 이동이 한 체크인으로 안 됐다.** Plastic 이 `Moved locally` 로만 잡고 체크인에
  싣지 못해 추가(`cs:625`)와 제거(`cs:627` 의 폴더 삭제에 포함)로 갈랐다. GUID 는 보존됐다.
- **Unity CLI 에 폴더를 넘는 에셋 이동 명령이 없다.** `rename_asset` 은 같은 폴더 전용이라
  사람이 Project 창에서 드래그해야 했다.

### 범위 밖이었지만 같이 한 것

레이마칭 폐기(`cs:619`). 사용자가 실행 중 결정했다. 씬의 `_look` 만 돌리고 코드
(`SnowCpuStageView` · `SnowRaymarchRendererCpu` · `SnowRaymarchCpu.hlsl/.shader` ·
`SnowCoarseMaxCpu` · `SnowSurfaceBakeCpu`)는 **남겼다** — 삭제는 씬 전부를 눈으로 확인해야 하는
별도 피처다. `MP_Gameplay` 는 어차피 지워지므로 건드리지 않았다.

---

## 2단계에서 하는 일 (이 계획 밖)

스펙 §12 의 6~8 번이다. **사람의 결정 둘을 기다린다** — 펭귄 이동 감각(가속·마찰·회전)과
마찰 3단계 경계값. 그것이 정해지면 계획을 따로 쓴다.

| | |
|---|---|
| 명령 복제 | `SendDelta`(`:896`) 제거, `SnowCommandWire` 배선, 확정 틱 적용 |
| 펭귄 | 새 플레이어 · 프리팹 · 게임플레이 씬 · `EInputButton` 비트 넷 제거 |
| 스냅샷 + 마찰 | 접속 시 1회 격자 스냅샷, 3단계 양자화 |
