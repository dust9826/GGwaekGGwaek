# 인게임 멀티 재구축 — 2A: 펭귄이 멀티에서 걷는다

> **에이전트 작업자에게:** `superpowers:executing-plans` 로 태스크 단위로 실행한다.

**목표:** 두 피어가 접속해 **서로의 펭귄이 걷는 것을 본다.** 눈 복제는 이 계획 밖(2B)이다.

**접근:** 싱글 펭귄(`PF_Penguin`)을 기준으로, `PenguinLocomotion` 의 물리 스텝을 `dt` 와 입력을
인자로 받게 고치고(본문 하나), 네트워크 진입점은 **변형 프리팹에만 붙는 작은 `NetworkBehaviour`**
가 맡는다. 서버만 로코모션을 돌리고 클라이언트는 `NetworkRigidbody` 보정을 받아 그린다.

**스펙:** `docs/specs/2026-08-24-multiplay-penguin-rebuild.md` — §6-B (a)(b)(c)(d) · §7 · §9

**브랜치:** `/main/multiplay-penguin` (`cs:628` 기준)

## 전역 제약

- **git 을 쓰지 않는다.** Plastic 이다. 삭제 경로와 수정 경로는 같은 `cm ci` 에 못 들어간다.
  수정 파일은 `cm ci` 전에 `cm checkout`. 새 파일은 `cm add`(경로 명시, `-R` 를 루트에 쓰지 않는다).
- **에셋 생성·이동·삭제는 Unity 를 거친다.** Unity CLI 의 `create_scene` · `create_prefab_variant` ·
  `delete_asset` 은 `AssetDatabase` 를 지나므로 조건을 만족한다.
- **네임스페이스는 `PPack` 하나.** 비공개 필드 `_camelCase`, enum 타입명 `E` 접두사.
- **`EInputButton` 의 비트 값을 다시 매기지 않는다.** 비트가 곧 와이어 포맷이다. 쓰지 않게 된
  비트는 구멍으로 남기고 새 버튼은 뒤에 붙인다.
- **`Core` 는 `InGame` 을 참조할 수 없다.** 어셈블리가 컴파일 에러로 만든다. 경계를 넘는 값은
  `Core` 가 정적 훅을 열고 `InGame` 이 채운다(기존 선례: `SceneAvatarResource`·`TestInputSource`).
- **PlayMode 테스트는 CLI 에서 세션당 한 번만 돈다.** 판정은 EditMode 로 최대한 내린다.
- **Play Mode 중 CLI 는 변형을 전부 거부한다.** 씬·프리팹 편집은 반드시 Edit Mode 에서.

## 파일 구조

| 파일 | 책임 |
|---|---|
| `Core/Multiplay/Scripts/NetworkInputData.cs` | 차량 비트 넷 제거, `Jump`(11) 추가, `Sprint`→`Slide` 개명(값 0 유지), `CameraYawDeg` 필드 추가 |
| `Core/Multiplay/Scripts/SessionLauncher.cs` | `OnInput` 이 키보드 대신 정적 훅 `LocalInputSource` 를 읽는다 |
| `InGame/Penguin/Scripts/PenguinLocomotion.cs` | 물리 스텝을 `Step(dt, in PenguinMoveInput)` 로 인자화. **수치와 규칙은 전부 여기 하나** |
| `InGame/Penguin/Scripts/PenguinMoveInput.cs` (신규) | 로코모션이 받는 입력 struct. 싱글·멀티 공용 |
| `InGame/Penguin/Scripts/PenguinNetAvatar.cs` (신규) | 변형에만 붙는 `NetworkBehaviour`. `FixedUpdateNetwork` 진입점 + 로컬 입력 공급 |
| `InGame/Penguin/Resources/PF_PenguinNet.prefab` (신규) | `PF_Penguin` 의 **변형**. `NetworkObject`·`NetworkRigidbody`·`PenguinNetAvatar` 추가 |
| `InGame/Cleanliness/Scenes/MultiPlay.unity` (신규) | 게임플레이 씬. `SinglePlay.unity` 옆 |
| `InGame/Cleanliness/Scripts/MultiPlayBootstrap.cs` (신규) | 씬이 `GameplayScenePath`·`SceneAvatarResource` 를 채운다 |
| `ProjectSettings/EditorBuildSettings.asset` | `MultiPlay.unity` 등록 |

체크인 넷 — **입력 계약 → 로코모션 인자화 → 네트워크 아바타·프리팹·씬 → 문서.**

---

## Task 0: 미검증 항목을 먼저 재는다 (게이트)

스펙 §확인 못 한 것에 남은 것 중 **코드를 쓰기 전에 답이 있어야 하는 둘**만 잰다. 나머지는
구현 중에 자연히 드러난다.

**Files:** 없음(측정만)

- [ ] **Step 1: `FixedUpdateNetwork` 안에서 `Time.deltaTime` 이 무엇인가**

`PenguinLocomotion:690` 과 `PenguinSnowball:254` 가 `Time.deltaTime` 을 쓴다. 지금은
`FixedUpdate` 안이라 `Time.fixedDeltaTime` 과 같지만 `FixedUpdateNetwork` 에서도 같은지는
**확인 못 했다.** 임시 로그로 한 번 잰다.

기대와 무관하게 **`Step(dt, ...)` 안에서는 `Time.deltaTime` 을 아예 쓰지 않고 인자 `dt` 로
바꾼다** — 재는 것은 "지금 싱글에서 값이 바뀌는가" 를 알기 위해서다. 같으면 싱글 동작이
그대로이고, 다르면 그 차이를 세션 요약에 남긴다.

- [ ] **Step 2: 씬에 배치되지 않은 프리팹 변형이 싱글을 건드리지 않는지 확인**

`PF_PenguinNet` 은 `Resources` 에 있고 씬에 놓이지 않는다(런타임 스폰). `PF_SnowBall` 이 같은
모양이므로 선례가 있다. **`PF_Penguin` 자체는 손대지 않는다** — 변형을 만든 뒤 싱글 씬 셋
(`Penguin_Locomotion_Test` · `Penguin_SnowballPush_Test` · `Snow_Slope_Test`)에서 EditMode 가
그대로 통과하는지 Task 3 끝에서 확인한다.

---

## Task 1: 입력 계약 + 체크인 A

**Files:**
- Modify: `Assets/Game/Core/Multiplay/Scripts/NetworkInputData.cs`
- Modify: `Assets/Game/Core/Multiplay/Scripts/SessionLauncher.cs`

**Interfaces:**
- Produces: `NetworkInputData { Vector2 Move; float CameraYawDeg; NetworkButtons Buttons; }`.
  `EInputButton` = `Slide`(0) · `Action`(1) · [2~5 구멍] · `RequestStartMatch`(6) · `Burst`(7) ·
  `CreateSnowball`(8) · `CoopShoveSuccess`(9) · `CoopShoveFailure`(10) · `Jump`(11).
  `SessionLauncher.LocalInputSource` — `InGame` 이 채우는 정적 델리게이트.

- [ ] **Step 1: `EInputButton` 을 고친다**

`Sprint`(0)를 `Slide` 로 개명한다 — **값은 0 그대로다.** 싱글의 `Slide` 액션과 이름을 맞추는 것이고
와이어 포맷은 변하지 않는다. `BladeToggle`(2)·`AngleLeft`(3)·`AngleStraight`(4)·`AngleRight`(5)를
지우고 **구멍으로 남긴다**(주석으로 그렇게 적는다). 끝에 `Jump = 11` 을 붙인다.

- [ ] **Step 2: `CameraYawDeg` 를 추가한다**

```csharp
        /// <summary>
        /// 이동 기준이 되는 카메라의 요(도). <b>월드 방향 벡터를 보내지 않는 이유가 여기 있다</b> —
        /// 방향을 보내면 <c>PenguinLocomotion.CameraRelativeDirection</c> 의 크기 클램프를 서버가
        /// 강제할 수 없고(클라가 길이 3 짜리 벡터를 보내면 그대로 믿는다), 전후·좌우 성분을 서버가
        /// 다시 분해할 수도 없다. 각도는 그 조작 여지가 구조적으로 없다.
        /// </summary>
        public float CameraYawDeg;
```

- [ ] **Step 3: `SessionLauncher` 에 정적 훅을 연다**

`TestInputSource` 옆에 같은 모양으로 둔다:

```csharp
        /// <summary>
        /// 로컬 플레이어의 입력을 만드는 곳. <b><c>InGame</c> 이 채운다</b> — <c>Core</c> 는 펭귄의
        /// 입력 액션을 알 수 없다(어셈블리가 막는다). 비어 있으면 입력이 나가지 않는다.
        /// </summary>
        public static System.Func<NetworkInputData> LocalInputSource { get; set; }
```

- [ ] **Step 4: `OnInput` 이 키보드 대신 그 훅을 읽게 한다**

`TestInputSource` 분기는 그대로 두고, 그 아래의 `Keyboard.current` 블록 전체를 훅 호출로 바꾼다.
`RequestStartMatch` 와 `CoopShove*` 는 지금처럼 런처가 얹는다 — 그 둘은 펭귄이 아니라 UI·릴레이의
것이다.

```csharp
            var data = LocalInputSource != null ? LocalInputSource() : default;

            data.Buttons.Set((int)EInputButton.CoopShoveSuccess, CoopShoveInputRelay.SuccessActive);
            data.Buttons.Set((int)EInputButton.CoopShoveFailure, CoopShoveInputRelay.FailureActive);
            data.Buttons.Set((int)EInputButton.RequestStartMatch,
                             Time.realtimeSinceStartup < _startRequestUntil);
            input.Set(data);
```

**왜 뒤집는가:** 지금은 키 바인딩이 `PenguinControls.inputactions` 와 `OnInput` **두 곳**에 산다
(`OnInput` 이 WASD 를 하드코딩한다). 그러면 싱글과 멀티의 조작이 조용히 갈린다.

⚠ `using UnityEngine.InputSystem` 이 더 필요 없어지면 지운다. 남아 있으면 경고가 난다.

- [ ] **Step 5: 컴파일 + EditMode**

```bash
U=~/.unity/bin/unity; P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" recompile --no-banner
until $U cmd --project-path "$P" recompile_status --no-banner | grep -qE '"status":"(completed|up_to_date)"'; do :; done
$U cmd --project-path "$P" get_console_logs --severity Error --limit 20 --no-banner
$U cmd --project-path "$P" run_tests --mode EditMode --timeout 900 --no-banner
```

기대: 에러 0, EditMode 전부 통과(현재 기준 207).

- [ ] **Step 6: 체크인 A (수정만)**

---

## Task 2: `PenguinLocomotion` 인자화 + 체크인 B

**이 태스크가 이 계획의 핵심이고 가장 위험하다.** 741줄 파일의 물리 스텝을 건드린다.
**수치는 한 줄도 바꾸지 않는다** — 시간축과 입력 출처만 인자로 뺀다.

**Files:**
- Create: `Assets/Game/InGame/Penguin/Scripts/PenguinMoveInput.cs`
- Modify: `Assets/Game/InGame/Penguin/Scripts/PenguinLocomotion.cs`

**Interfaces:**
- Produces: `PenguinMoveInput` struct · `PenguinLocomotion.Step(float dt, in PenguinMoveInput input)`
  public. 기존 `FixedUpdate` 는 런너가 없을 때만 그것을 부른다.

- [ ] **Step 1: 입력 struct 를 만든다**

```csharp
namespace PPack
{
    /// <summary>
    /// <see cref="PenguinLocomotion"/> 이 한 스텝에 쓰는 입력. <b>싱글과 멀티가 같은 것을 넘긴다</b> —
    /// 싱글은 <see cref="PenguinInputReader"/> 에서, 멀티는 <c>NetworkInputData</c> 에서 채운다.
    ///
    /// <para><b>카메라 요가 여기 있는 이유.</b> 이동 방향이 카메라 기준인데 데디 서버에는 원격
    /// 플레이어의 카메라가 없다. 방향 벡터가 아니라 각도를 넘겨야 크기 클램프를 서버가 강제한다.</para>
    /// </summary>
    public struct PenguinMoveInput
    {
        public Vector2 Move;
        public float CameraYawDeg;
        public bool SlideHeld;
        public bool JumpPressed;
    }
}
```

- [ ] **Step 2: `CameraRelativeDirection` 이 요를 쓰게 한다**

`:648-656`. `_cameraPivot.forward`/`.right` 대신 `Quaternion.Euler(0, yaw, 0)` 에서 낸다.
**`:655` 의 `sqrMagnitude > 1f ? normalized : dir` 규칙은 그대로 둔다** — 그게 서버가 강제해야
하는 것이다. 싱글은 `_cameraPivot.eulerAngles.y` 를 요로 넣으므로 동작이 같다.

- [ ] **Step 3: `FixedUpdate` 본문을 `Step(dt, input)` 으로 옮긴다**

`:263-391` 을 `public void Step(float dt, in PenguinMoveInput input)` 으로 만들고,
`FixedUpdate` 는 이렇게만 남긴다:

```csharp
        private void FixedUpdate()
        {
            // 런너가 있으면 네트워크 진입점(PenguinNetAvatar)이 돌린다. 여기서 또 돌리면 힘을
            // 두 시계로 쌓는다 — 물리 애드온이 simulationMode 를 Script 로 바꿔도 Unity 는
            // FixedUpdate 를 계속 부르기 때문이다(SnowBallCarrier.cs:336-339 실측).
            if (_networkDriven) return;
            Step(Time.fixedDeltaTime, ReadLocalInput());
        }

        /// <summary>네트워크 진입점이 켠다. 켜지면 이 컴포넌트는 자기 클럭으로 돌지 않는다.</summary>
        public bool NetworkDriven { set => _networkDriven = value; }
```

`ReadLocalInput()` 은 `_input`(`PenguinInputReader`)과 `_cameraPivot` 에서 `PenguinMoveInput` 을
채우는 private 헬퍼다.

- [ ] **Step 4: `dt` 를 15군데에 꽂는다**

`Time.fixedDeltaTime` 15군데(`:371` `:404` `:426` `:433` `:453` `:508` `:519`×2 `:521` `:532`
`:538` `:578` `:592` `:616` `:628`)와 **`Time.deltaTime` 1군데(`:690`)** 를 인자 `dt` 로 바꾼다.
호출되는 메서드 16개의 시그니처에 `dt` 를 넘긴다.

⚠ **`:592` `desiredCancelForce = -lateralVel * mass / dt` 는 그립력이 `dt` 에 반비례한다.**
값을 바꾸지 않아야 `AGENTS.md` 가 실측한 포화값 `μ_lat×g = 11.772` 가 유지된다.

- [ ] **Step 5: `_input` 직접 참조 10군데를 인자로 바꾼다**

`:292` `:294` `:312` `:313` `:321` `:355` `:425` `:504` `:574` `:638`.

- [ ] **Step 6: 컴파일 + EditMode + 싱글 육안 확인**

EditMode 통과 후 `Penguin_Locomotion_Test` 를 열어 Play 로 **걷기·점프·슬라이딩이 그대로인지
사람이 확인한다.** 수치를 안 건드렸으므로 달라 보이면 인자화가 틀린 것이다.

- [ ] **Step 7: 체크인 B (수정 + 추가)**

---

## Task 3: 네트워크 아바타 · 변형 프리팹 · 씬 + 체크인 C

**Files:**
- Create: `Assets/Game/InGame/Penguin/Scripts/PenguinNetAvatar.cs`
- Create: `Assets/Game/InGame/Penguin/Resources/PF_PenguinNet.prefab` (변형)
- Create: `Assets/Game/InGame/Cleanliness/Scripts/MultiPlayBootstrap.cs`
- Create: `Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity`
- Modify: `ProjectSettings/EditorBuildSettings.asset`

- [ ] **Step 1: `PenguinNetAvatar` 를 쓴다**

핵심만:

```csharp
    [RequireComponent(typeof(NetworkRigidbody))]
    [DisallowMultipleComponent]
    public sealed class PenguinNetAvatar : NetworkBehaviour
    {
        [SerializeField] private PenguinLocomotion _locomotion;

        public override void Spawned()
        {
            // ⚠ 스폰 자세를 물리 바디에 먼저 밀어 넣는다. 안 하면 원점으로 순간이동한다
            //   (SnowBallCarrier.cs:565-576 실측). 그래서 이 컴포넌트가 NetworkRigidbody 보다
            //   앞에 있어야 한다.
            var body = GetComponent<Rigidbody>();
            if (body != null) { body.position = transform.position; body.rotation = transform.rotation; }

            _locomotion.NetworkDriven = true;

            // 로컬 플레이어만 입력을 만든다. 남의 아바타에 카메라·입력이 붙으면 안 된다.
            if (HasInputAuthority) SessionLauncher.LocalInputSource = ReadLocalNetInput;
            else DisableLocalOnly();
        }

        public override void FixedUpdateNetwork()
        {
            // 예측은 아직 켜지 않는다(스펙 §6-B(b)). 서버만 로코모션을 돌린다 -
            // 그러면 IsForward 가 항상 참이라 재시뮬 위험 일곱 개가 통째로 사라진다.
            if (!Object.HasStateAuthority) return;
            if (!GetInput(out NetworkInputData net)) return;

            _locomotion.Step(Runner.DeltaTime, new PenguinMoveInput
            {
                Move = net.Move,
                CameraYawDeg = net.CameraYawDeg,
                SlideHeld = net.Buttons.IsSet((int)EInputButton.Slide),
                JumpPressed = net.Buttons.IsSet((int)EInputButton.Jump),
            });
        }
    }
```

`DisableLocalOnly()` 는 원격 아바타에서 `PenguinCameraOrbit`·`PenguinInputReader`·`Camera`·
`AudioListener` 를 끈다. **4인이면 클라 한 대에 카메라 4개·오디오리스너 4개가 스폰되고
`PenguinCameraOrbit.OnEnable:104` 가 각각 커서를 잠근다** — 이걸 안 끄면 그 증상으로 나타난다.

`ReadLocalNetInput()` 은 `PenguinInputReader` 와 `PenguinCameraOrbit` 에서 `NetworkInputData` 를
만든다. 점프는 래치라 **읽는 순간 소비된다** — `Core/Multiplay/AGENTS.md` 규약대로 눌림 상태만
보내고 에지 판정은 받는 쪽이 한다.

- [ ] **Step 2: 변형 프리팹을 만든다**

```bash
$U cmd --project-path "$P" create_prefab_variant --source "Assets/Game/InGame/Penguin/Prefabs/PF_Penguin.prefab" \
      --path "Assets/Game/InGame/Penguin/Resources/PF_PenguinNet.prefab" --confirm true --no-banner
```

그 위에 `NetworkObject` · `NetworkRigidbody` · `PenguinNetAvatar` 를 붙인다.
⚠ **`NetworkTransform` 을 붙이지 않는다** — `NetworkRigidbody` 가 `NetworkTRSP` 를 상속해 트랜스폼
복제를 겸한다. 둘 다 두면 싸운다(`SnowBallCarrier.cs:584-585`).
⚠ **컴포넌트 순서**: `PenguinNetAvatar` 가 `NetworkRigidbody` 보다 앞.

- [ ] **Step 3: 씬과 부트스트랩**

`MultiPlay.unity` 를 만들고 조명·`SnowCpuStage`·바닥을 넣는다(`SinglePlay.unity` 를 참고하되
복사하지 않는다). `MultiPlayBootstrap` 이 `Awake` 에서:

```csharp
            SessionLauncher.GameplayScenePath = "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity";
            SessionLauncher.SceneAvatarResource = "PF_PenguinNet";
```

- [ ] **Step 4: 빌드 세팅에 등록**

```bash
$U cmd --project-path "$P" add_scene_to_build --path "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity" --enabled true --no-banner
```

⚠ 이건 `EditorBuildSettings.asset` 수정이라 **삭제와 같은 체크인에 넣지 않는다.**

- [ ] **Step 5: 컴파일 + EditMode**

- [ ] **Step 6: 체크인 C**

---

## Task 4: MPPM 2인 실행 검증

- [ ] **Step 1: 시나리오를 돌린다**

```bash
$U cmd --project-path "$P" clear_console --no-banner
$U cmd --project-path "$P" menu --path "PPack/Multiplay/2인 자동 시작 (서버+클라2)" --no-banner --timeout 300
$U cmd --project-path "$P" menu --path "PPack/Multiplay/시나리오 상태 보기" --no-banner
```

`state=Running` 을 기다린 뒤 콘솔을 읽는다. **찾는 것:** `게임플레이 씬 경로가 정해지지 않았다`
에러가 **사라지고** 씬 로드로 넘어가는가.

- [ ] **Step 2: 사람에게 조작을 요청한다**

⚠ **Play Mode 중 CLI 는 변형을 전부 거부하므로 펭귄은 사람이 몬다.** 클론 창에서 WASD 로
걸어 달라고 요청하고, 양쪽 화면을 캡처해 **남의 펭귄이 같이 움직이는지** 판정한다.

- [ ] **Step 3: 정리**

시나리오 정지 → 클론 닫기 → `editor_stop` → 원래 씬 복귀 → `cm status` 확인.

---

## Task 5: 문서 + 체크인 D

`Penguin/AGENTS.md` 에 멀티 진입점 절 추가 · `Core/Multiplay/AGENTS.md` 의 아바타·씬 서술 갱신 ·
`docs/INDEX.md` · 세션 요약. **세션 요약은 체크인까지가 한 덩어리다.**

---

## 2B (이 계획 밖)

`SendDelta`(`SnowCpuStage:896`) 제거 → `SnowCommandWire` 배선 → 확정 틱 적용 → 접속 스냅샷.
검증은 "두 피어의 격자가 셀 단위로 같은가" 이고, 그건 `TestInputSource` 주입으로 자동화한다.
