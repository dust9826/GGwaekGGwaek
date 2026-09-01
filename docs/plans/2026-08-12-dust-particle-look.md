# 먼지 청소 파티클 룩 — 구현 계획

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 청소 파티클이 3인칭 거리에서 먼지 알갱이로 읽히게 만든다. 내장 파티클 룩 랩으로 모양을 찾고, 답을 VFX Graph로 이관하면서 룩 노브를 Exposed로 뺀다.

**Architecture:** 파이프라인(지운 양 RT, 마스크, 스탬프)은 한 줄도 안 고친다. 새 검증 씬 하나에 내장 `ParticleSystem`을 놓고 `DustPadSweep`의 패드를 따라다니게 한 뒤, Unity CLI로 값을 바꿔가며 스크린샷을 찍는다. 이긴 룩을 세 `.vfx`에 손으로 옮기고 그 김에 파라미터로 노출한다.

**Tech Stack:** Unity 6000.6.0b7 · URP 17.6.0 · VFX Graph 17.6.0 · Unity CLI (`~/.unity/bin/unity`) · Plastic SCM · `gpt-image-1`

스펙: [../specs/2026-08-12-dust-particle-look.md](../specs/2026-08-12-dust-particle-look.md)

## Global Constraints

- **워크스페이스는 `/Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB`, 브랜치는 `/main/dust-particle-look` (cs:99에서 분기).**
- ⚠ **`--project-path`를 모든 `unity cmd`에 명시한다.** 다른 워크스페이스(`~/Documents/UnityProjects/PPackPPack_v2`)의 에디터가 포트 7800에 이미 떠 있다. 그 인스턴스는 **건드리지 않는다.** `unity status`에 branchB 인스턴스가 안 보이면 다른 데로 라우팅하지 말고 중단·보고한다.
- **파이프라인 파일 수정 금지** — `DustPaintTarget.cs`, `DustCleanVfx.cs`, `BrushPad.cs`, `DustMousePainter.cs`, `Shaders/`. `DustPadSweep.cs`만 예외이고 Task 2의 한 줄뿐이다.
- **남의 피처 수정 금지** — `Vehicle/`, `Player/`, `Vacuum/`.
- 네임스페이스는 `PPack` 하나. private 필드 `_camelCase`, 타입·메서드 `PascalCase`. 직렬화된 Unity Object 필드는 `== null` / `!= null`.
- `Dust_Look_Test.unity`는 **읽기 전용으로 취급한다** — Task 9의 회귀 측정에서만 열고, 저장하지 않는다.
- **`capture_game_view --save_path`는 오써링 루트 안에 가둔다.** 리포지토리의 `docs/`에 쓰려고 하면 `Assets/docs/`가 만들어진다. 절대 경로가 필요하면 `screenshot --output`을 쓴다.
- **스크린샷은 오래된 프레임을 돌려줄 수 있다.** `set_autotick --enable true`를 켜두고, 연속 두 장의 바이트 수가 같으면 갱신 실패로 본다.
- ⚠ **`set_component_properties`와 `set_transform`은 플레이 모드에서 거부된다** (`This cannot be used during play mode`, 2026-08-12 실측). 값을 바꾸려면 **에디트 모드에서 바꾸고 저장한 뒤 플레이**해야 한다. 그래서 Task 7의 A/B 한 사이클은 "정지 → 세팅 → 플레이 → 캡처"가 되고, 하네스가 그 순서를 지켜야 한다.
- **실측한 CLI 인자 이름** (계획 초안이 틀렸던 것들):

  | 명령 | 인자 |
  |---|---|
  | `set_authoring_root` | `--root` (`--path` 아님) |
  | `get_component_properties` · `set_component_properties` · `set_transform` | `--target` (`--gameobject` 아님) |
  | `copy_asset` | `--asset` · `--destination` (`--path`/`--new_path` 아님) |
  | `screenshot` | `--view game|scene` · `--output <절대경로>` — 오써링 루트 밖으로 나갈 수 있는 유일한 캡처 |
- 검증 스크린샷은 `docs/images/verify/`에 `particle_*.png`로 모은다 (셸에서 `mv`, Unity 밖이므로 `.meta` 없음).

---

## 파일 구조

| 파일 | 책임 | 상태 |
|---|---|---|
| `Assets/Game/InGame/Dust/Scripts/DustPadSweep.cs` | 결정론적 패드 스윕. **`CurrentPad` 프로퍼티 한 줄만 추가** | 수정 |
| `Assets/Game/InGame/Dust/Tests/DustParticleLookLab.cs` | 패드를 따라 `ParticleSystem`을 옮기고 방출. 마스크는 안 본다 | 신규 |
| `Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity` | 룩 랩 씬. `Dust_Look_Test` 복제 + VFX 끄기 + 고정 카메라 | 신규 |
| `Assets/Game/InGame/Dust/Textures/T_DustGrain_Particle_*.png` | 알갱이 스프라이트 후보 | 신규 |
| `Assets/Game/InGame/Dust/Materials/M_DustParticle.mat` | 랩 파티클 머티리얼 (URP Particles Unlit) | 신규 |
| `Assets/Game/InGame/Dust/VFX/VFX_*.vfx` | 이긴 룩 반영 + 룩 노브 Exposed | 수정 |
| `Assets/Game/InGame/Dust/AGENTS.md` | 결정 기록 | 수정 |

**테스트 프레임워크를 안 쓴다.** 이 브랜치의 산출물은 "어떻게 보이는가"이고 그건 assert로 못 잡는다. 대신 두 가지가 검증을 진다 — (1) 고정 프레임 스크린샷 A/B, (2) `DustPadSweep`으로 잰 살아있는 파티클 수(선행 브랜치가 112→1을 잰 바로 그 방법). 한 줄짜리 프로퍼티를 위해 EditMode 테스트 인프라를 세우는 것은 `AGENTS.md` §2에 어긋난다.

---

## 체크인 계획 (5개)

| | 내용 | 태스크 |
|---|---|---|
| 1 | 설계 — 스펙 + 계획 | (이 문서) |
| 2 | 룩 랩 — `CurrentPad`, 씬, 스크립트, 하네스 | 2·3·4·5 |
| 3 | 스프라이트와 탐색 결과 | 6·7 |
| 4 | VFX Graph 이관 + Exposed | 8 |
| 5 | 문서 — `AGENTS.md`, `INDEX.md`, 세션 요약 | 10 |

---

### Task 1: 에디터를 띄우고 현재 룩을 증거로 남긴다 — **게이트**

스펙 §3은 "구형의 원인이 스프라이트"라고 **추정만** 했다. 이 태스크가 그것을 사실로 바꾸거나 기각한다. 여기서 결론이 "Mesh 출력이라 스프라이트와 무관하다"로 나오면 §3과 Task 6·7의 방향이 통째로 바뀌므로 **멈추고 재설계한다.**

**Files:**
- Read: `Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity`
- Create: `docs/images/verify/particle_before_closeup.png`, `docs/images/verify/particle_before_thirdperson.png`

**Interfaces:**
- Consumes: 없음
- Produces: 이후 모든 A/B의 **"before" 기준 이미지 2장.** Task 7이 이것과 비교한다

- [ ] **Step 1: branchB 워크스페이스로 에디터를 연다**

```bash
~/.unity/bin/unity open /Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB
```

첫 오픈은 임포트 때문에 몇 분 걸린다. 완료 확인:

```bash
~/.unity/bin/unity status
```

기대: `PPackPPack_v2-branchB` 행이 `ready`로 뜬다. **7800 포트의 `PPackPPack_v2`(branchB 아님)와 헷갈리지 말 것.** 이후 모든 명령에 `--project-path`를 붙인다.

- [ ] **Step 2: 오써링 루트와 autotick을 세팅한다**

```bash
P=/Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB
~/.unity/bin/unity cmd set_authoring_root --path Assets --project-path $P
~/.unity/bin/unity cmd set_autotick --enable true --project-path $P
```

- [ ] **Step 3: 검증 씬을 열고 스윕 파라미터를 읽는다**

```bash
~/.unity/bin/unity cmd open_scene --path Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity --project-path $P
~/.unity/bin/unity cmd find_gameobjects --name DustPainter --project-path $P
~/.unity/bin/unity cmd get_component_properties --type DustPadSweep --gameobject <handle> --project-path $P
```

**`_center` · `_radius` · `_lapSeconds`를 받아 적는다.** Task 3의 카메라 위치를 이 값에서 계산한다. 스크립트 기본값은 `_center (-5, 0, -0.5)` · `_radius 2.8` · `_lapSeconds 8`이지만 **씬이 덮어썼을 수 있으므로 읽은 값이 진실이다.**

- [ ] **Step 4: `DustPadSweep`을 켜고 플레이한다**

```bash
~/.unity/bin/unity cmd set_component_properties --type DustPadSweep --gameobject <handle> \
  --properties '{"m_Enabled":true}' --project-path $P
~/.unity/bin/unity cmd editor_play --project-path $P
```

`OnEnable`이 마스크를 리셋하므로 첫 바퀴가 더러운 구간이다. **2~3초 안에 찍어야** 파티클이 실제로 뜨는 구간을 잡는다.

- [ ] **Step 5: 근접 스크린샷 — 모양을 본다**

Scene 뷰를 파티클 가까이 붙여서 찍는다. 목적은 **한 알의 실루엣**을 보는 것이다.

```bash
~/.unity/bin/unity cmd screenshot --view scene --output /tmp/particle_before_closeup.png --project-path $P
```

판정할 것 — 아래 셋 중 어느 쪽인가:

| 관찰 | 뜻 | 다음 |
|---|---|---|
| 납작하고 부드러운 원, 항상 카메라를 향함 | **빌보드 + 기본 블롭 텍스처.** §3이 맞다 | 계획대로 Task 2로 |
| 음영이 있는 입체 구, 회전해도 구 | **Mesh 출력.** §3이 틀렸다 | **멈춘다.** 스프라이트로는 안 고쳐진다 |
| 판별 불가 | 그래프를 직접 봐야 한다 | Step 6 |

- [ ] **Step 6: (Step 5가 판별 불가일 때만) 그래프를 눈으로 연다**

CLI로는 `.vfx` 내부가 안 보인다(스펙 §2). `editor_focus`로 에디터를 앞에 놓고 `computer-use` 스킬로 Project 창에서 `VFX_DustPuff`를 더블클릭해 출력 컨텍스트와 텍스처 슬롯을 읽는다.

- [ ] **Step 7: 3인칭 스크린샷 — before 기준을 남긴다**

```bash
~/.unity/bin/unity cmd screenshot --view game --output /tmp/particle_before_thirdperson.png --project-path $P
~/.unity/bin/unity cmd editor_stop --project-path $P
mkdir -p docs/images/verify
mv /tmp/particle_before_*.png docs/images/verify/
```

**두 장의 바이트 수가 같으면 프레임이 안 갱신된 것이다** — `set_autotick`을 확인하고 다시 찍는다.

- [x] **Step 8: 게이트 판정을 기록한다** — **통과 (2026-08-12)**

**Mesh 출력이 아니다. 전부 카메라를 향한 납작한 원이다. 스프라이트로 고칠 수 있다 — §3의 방향은 맞다.** 계획대로 진행한다.

**다만 원인이 하나가 아니고, 세 그래프가 서로 다른 문제를 갖고 있다.** 셋을 하나씩만 켜고 찍어서 갈랐다(`particle_before_iso_*.png`):

| 그래프 | 관찰 | 읽히는 것 |
|---|---|---|
| **퍼프** | 불투명한 황갈색 원판. **가장자리가 딱 떨어지고 그라데이션이 없다.** 여러 개가 바닥 평면에 **반원으로 잘려** 있다 | 먼지가 아니라 **스티커·동전** |
| **밀림** | 퍼프와 같은 원판. 개수만 적다(2개) | 같음. 문서의 "퍼프의 15%"와 일치 |
| **반짝** | 크고 **밝은 연노랑 소프트 원**. 개수가 많고 타일 한 칸에 육박한다 | **비눗방울·보케** |

**정정: 세 개가 같은 문제가 아니다.** 합성 근접 컷에서 보이는 밝은 소프트 블롭은 **반짝**이고, 퍼프·밀림은 소프트가 아니라 하드 엣지 불투명 원판이다. 처음 이 자리에 "전부 소프트 블롭"이라고 적었던 것은 반짝이 나머지를 덮어서 생긴 오독이다.

**3인칭에서 안 보였던 이유도 갈린다:**

- **반짝** — 밝기가 바닥의 스페큘러 하이라이트와 같은 값이라 **그 위에서 사라진다.** `Dust/AGENTS.md`가 경고한 바로 그것
- **퍼프·밀림** — 색은 바닥과 대비되지만 **개수가 적고**(밀림은 2개) 모양이 평평해서 눈에 안 걸린다

`Dust/AGENTS.md`가 §8.5의 원인으로 적어둔 "크기와 개수"는 퍼프·밀림에는 절반쯤 맞고 반짝에는 틀렸다.

**따라서 Task 7의 축 순서를 바꾼다.** 원래 색을 마지막(축 5)에 뒀는데 밝기가 반짝의 주범이므로 **색을 축 2로 올린다.** 순서: 렌더모드 → **색** → 스프라이트 → 회전 → 크기·개수.

**새 항목 하나가 생겼다 — 바닥 클리핑.** 퍼프 파티클이 바닥과 만나는 자리에서 반원으로 잘린다. 소프트 파티클(뎁스 페이드)이 안 켜져 있다는 뜻이고, 이건 스프라이트를 바꿔도 남는다. Task 7의 축으로 추가한다.

---

### Task 2: `DustPadSweep.CurrentPad` 한 줄

**Files:**
- Modify: `Assets/Game/InGame/Dust/Scripts/DustPadSweep.cs:39` 부근 (`Laps` 프로퍼티 옆)

**Interfaces:**
- Consumes: `BrushPad` (기존 구조체, `Dust/Scripts/BrushPad.cs`)
- Produces: `public BrushPad CurrentPad { get; private set; }` — Task 4의 `DustParticleLookLab`이 매 프레임 읽는다

- [ ] **Step 1: 프로퍼티를 추가한다**

`Laps` 선언 바로 아래:

```csharp
        /// <summary>이번 프레임의 패드. 룩 랩이 파티클을 여기 붙인다 — <c>Tests/DustParticleLookLab.cs</c>.</summary>
        public BrushPad CurrentPad { get; private set; }
```

- [ ] **Step 2: `Update`에서 채운다**

`BrushPad pad = new BrushPad(...)` 생성 직후, `_vfx` 블록보다 **먼저** 한 줄:

```csharp
            CurrentPad = pad;
```

먼저인 이유: `_vfx`가 null이면 그 블록을 건너뛰므로 뒤에 두면 VFX 없는 씬에서 안 채워진다. 랩 씬이 정확히 그 경우다.

- [ ] **Step 3: 컴파일 확인**

```bash
~/.unity/bin/unity cmd recompile --project-path $P
~/.unity/bin/unity cmd recompile_status --project-path $P   # completed 될 때까지
~/.unity/bin/unity cmd get_console_logs --severity Error --project-path $P
```

기대: 에러 0건.

- [ ] **Step 4: 기존 동작이 안 깨진 것 확인**

`Dust_Look_Test`에서 `DustPadSweep`을 켜고 플레이해 먼지가 여전히 지워지는지 본다. 추가한 것은 대입 한 줄이므로 안 깨지는 게 정상이고, **깨졌다면 순서를 잘못 넣은 것이다.**

---

### Task 3: 랩 씬 만들기

**Files:**
- Create: `Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity` (`Dust_Look_Test` 복제)

**Interfaces:**
- Consumes: Task 1 Step 3에서 읽은 `_center` · `_radius`
- Produces: 고정 카메라를 가진 랩 씬. Task 4가 여기에 `ParticleSystem`을 붙인다

- [ ] **Step 1: 씬을 복제한다**

```bash
~/.unity/bin/unity cmd copy_asset \
  --path Assets/Game/InGame/Dust/Tests/Dust_Look_Test.unity \
  --new_path Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity --project-path $P
~/.unity/bin/unity cmd open_scene --path Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity --project-path $P
```

`copy_asset`은 새 GUID를 준다 — 원본과 독립이라는 뜻이고 여기서는 그게 맞다.

- [ ] **Step 2: VFX 세 개를 끈다**

```bash
~/.unity/bin/unity cmd find_gameobjects --component VisualEffect --project-path $P
# 나온 핸들마다:
~/.unity/bin/unity cmd set_active --gameobject <handle> --active false --project-path $P
```

`DustCleanVfx`가 `DustPadSweep`의 `_vfx` 슬롯에 물려 있으면 그 참조도 비운다:

```bash
~/.unity/bin/unity cmd set_component_properties --type DustPadSweep --gameobject <DustPainter handle> \
  --properties '{"_vfx":null}' --project-path $P
```

랩은 VFX를 안 쓴다. 켜둔 채로 비교하면 무엇이 무엇인지 구분이 안 된다.

- [ ] **Step 3: 카메라를 청소 리그 자세로 고정한다**

**따라다니지 않는 고정 카메라**를 쓴다. A/B는 같은 프레임이어야 비교가 되고, 따라다니는 카메라는 매 캡처마다 각도가 조금씩 다르다.

각도 0(패드가 원의 +X 지점, 진행 방향 +Z)일 때를 기준으로 잡는다. Task 1에서 읽은 값을 `C = _center`, `R = _radius`라 하면:

```
padPos    = (C.x + R, 0, C.z)
travel    = (0, 0, 1)
cameraPos = padPos + (0, 5.362, 0) + (-4.5 × travel) = (C.x + R, 5.362, C.z - 4.5)
lookAt    = padPos + (0, 0.6, 0) + (0.8 × travel)    = (C.x + R, 0.6,   C.z + 0.8)
```

기본값(`C = (-5, 0, -0.5)`, `R = 2.8`)이면:

| | |
|---|---|
| position | `(-2.2, 5.362, -5.0)` |
| rotation (euler) | `(41.94, 0, 0)` |

회전은 `atan2(5.362 − 0.6, 4.5 + 0.8) = atan2(4.762, 5.3) = 41.94°`다. **50°가 아닌 이유**: `FollowOffset (0, 5.362, -4.5)`은 팔로우 타깃에서 7.0m 거리의 50° 방향이고(`atan(5.362/4.5) = 50.0°`), 거기에 `LookAtOffset (0, 0.6, 0.8)`이 조준점을 앞·위로 밀어 최종 피치가 42°가 된다. 청소 리그가 실제로 만드는 프레이밍이 이쪽이다.

```bash
~/.unity/bin/unity cmd find_gameobjects --component Camera --project-path $P
~/.unity/bin/unity cmd set_transform --gameobject <handle> \
  --position '[-2.2, 5.362, -5.0]' --rotation '[41.94, 0, 0]' --project-path $P
```

씬에 `CinemachineBrain`이 있으면 그 컴포넌트를 꺼야 트랜스폼이 덮어써지지 않는다:

```bash
~/.unity/bin/unity cmd set_component_properties --type CinemachineBrain --gameobject <handle> \
  --properties '{"m_Enabled":false}' --project-path $P
```

- [ ] **Step 4: 스페큘러 하이라이트가 프레임 안에 있는지 확인한다**

스펙 §8.2가 요구한다. 플레이 없이 Game 뷰를 찍어 바닥의 밝은 반사가 패드 궤적과 겹치는지 본다.

```bash
~/.unity/bin/unity cmd capture_game_view --save_path Game/InGame/Dust/Tests/__framing.png --project-path $P
```

겹치지 않으면 **카메라가 아니라 라이트를 돌린다** — 카메라는 청소 리그 수치라 건드리면 판정 근거가 사라진다. 확인 후 임시 파일을 지운다:

```bash
~/.unity/bin/unity cmd delete_asset --path Assets/Game/InGame/Dust/Tests/__framing.png --confirm true --project-path $P
```

(셸 `rm` 금지 — `.meta`가 남는다.)

- [ ] **Step 5: 저장한다**

```bash
~/.unity/bin/unity cmd save_scene --path Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity --project-path $P
```

---

### Task 4: `DustParticleLookLab.cs`와 파티클 오브젝트

**Files:**
- Create: `Assets/Game/InGame/Dust/Tests/DustParticleLookLab.cs`
- Create: `Assets/Game/InGame/Dust/Materials/M_DustParticle.mat`
- Modify: `Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity`

**Interfaces:**
- Consumes: `DustPadSweep.CurrentPad` (Task 2), `BrushPad`의 `Position`·`Rotation` 멤버
- Produces: 씬 오브젝트 `ParticleLab` (`ParticleSystem` + `DustParticleLookLab`). Task 5의 하네스가 이 이름으로 찾는다

- [x] **Step 1: `BrushPad`의 실제 멤버 이름을 확인한다** — **완료 (2026-08-12)**

```bash
grep -n "public" Assets/Game/InGame/Dust/Scripts/BrushPad.cs
```

**결과: `Position`·`Rotation`이 없다.** `BrushPad`는 포즈를 `WorldToPad` 행렬 하나로만 들고 있다:

```csharp
WorldToPad = Matrix4x4.TRS(position, rotation, Vector3.one).inverse;
```

스케일이 `one`인 **순수 강체 변환**이므로 역행렬에서 위치와 회전을 그대로 꺼낼 수 있다. Step 2의 코드는 이 사실에 맞춰 쓰여 있다. (계획 초안은 `pad.Position`을 가정했고 그게 틀렸다 — 이 스텝이 잡으려던 것이 정확히 이것이다.)

- [ ] **Step 2: 스크립트를 만든다**

```bash
~/.unity/bin/unity cmd create_script --path Assets/Game/InGame/Dust/Tests/DustParticleLookLab.cs \
  --name DustParticleLookLab --project-path $P
```

그 다음 파일 내용을 통째로 쓴다:

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 파티클 룩을 눈으로 비교하기 위한 랩. <see cref="DustPadSweep"/>가 도는 패드에
    /// <see cref="ParticleSystem"/>을 붙여두고 계속 방출한다.
    ///
    /// <b>마스크를 안 본다.</b> 깨끗한 바닥에서도 뜨는데 의도한 것이다 — 답하려는 질문이
    /// "알갱이로 읽히는가"라서 어디서 뜨는지는 상관없다. 실제 판정(깨끗한 자리에선 안 난다)은
    /// VFX Graph 쪽이 이미 통과했고 이 랩은 그것을 대체하지 않는다.
    /// 자세한 근거는 <c>docs/specs/2026-08-12-dust-particle-look.md</c> §2.
    ///
    /// 검증 씬 전용이다. 프로덕션 경로에 넣지 않는다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DustParticleLookLab : MonoBehaviour
    {
        [SerializeField] private DustPadSweep _sweep;

        [Tooltip("패드 위로 띄우는 높이. 바닥과 같은 평면에 두면 깜빡인다.")]
        [SerializeField] private float _lift = 0.05f;

        private ParticleSystem _particles;

        private void Awake()
        {
            _particles = GetComponent<ParticleSystem>();
        }

        private void LateUpdate()
        {
            // enabled 까지 보는 이유: 스윕이 꺼져 있으면 Update 가 안 돌아 CurrentPad 가
            // default 로 남고, 그 WorldToPad 는 영행렬이라 inverse 가 NaN 을 뱉는다.
            if (_sweep == null || !_sweep.enabled) return;

            Matrix4x4 padToWorld = _sweep.CurrentPad.WorldToPad.inverse;
            transform.SetPositionAndRotation(padToWorld.GetPosition() + Vector3.up * _lift,
                                             padToWorld.rotation);
        }
    }
}
```

`LateUpdate`인 이유: `DustPadSweep.Update`가 `CurrentPad`를 채운 뒤에 읽어야 한 프레임 뒤처지지 않는다.

- [ ] **Step 3: 컴파일하고 확인한다**

```bash
~/.unity/bin/unity cmd recompile --project-path $P
~/.unity/bin/unity cmd recompile_status --project-path $P
~/.unity/bin/unity cmd get_console_logs --severity Error --project-path $P
```

기대: 에러 0건. `CurrentPad`를 못 찾는다는 에러가 나면 Task 2가 저장되지 않은 것이다.

- [ ] **Step 4: 파티클 머티리얼을 만든다**

```bash
~/.unity/bin/unity cmd list_shaders --project-path $P | grep -i "Particles"
```

`Universal Render Pipeline/Particles/Unlit`을 쓴다. Lit이 아닌 이유: 조명을 받으면 밝기가 프레임마다 달라져 A/B가 흔들리고, 먼지 알갱이는 자체 음영이 필요 없다.

```bash
~/.unity/bin/unity cmd create_asset --path Assets/Game/InGame/Dust/Materials/M_DustParticle.mat \
  --type Material --project-path $P
~/.unity/bin/unity cmd set_material_properties --path Assets/Game/InGame/Dust/Materials/M_DustParticle.mat \
  --shader "Universal Render Pipeline/Particles/Unlit" --project-path $P
```

- [ ] **Step 5: 씬에 파티클 오브젝트를 만든다**

```bash
~/.unity/bin/unity cmd create_gameobjects --count 1 --name ParticleLab --project-path $P
~/.unity/bin/unity cmd add_component --gameobject <handle> --type ParticleSystem --project-path $P
~/.unity/bin/unity cmd attach_script --gameobject <handle> --type DustParticleLookLab --project-path $P
~/.unity/bin/unity cmd set_component_properties --type DustParticleLookLab --gameobject <handle> \
  --properties '{"_sweep":{"gameObject":"<DustPainter handle>","type":"DustPadSweep"}}' --project-path $P
```

`ParticleSystemRenderer`에 머티리얼을 물린다:

```bash
~/.unity/bin/unity cmd set_component_properties --type ParticleSystemRenderer --gameobject <handle> \
  --properties '{"m_Materials":["Assets/Game/InGame/Dust/Materials/M_DustParticle.mat"]}' --project-path $P
```

- [ ] **Step 6: 파티클이 패드를 따라가는지 확인한다**

`DustPadSweep`을 켜고 플레이해 Game 뷰를 찍는다. 파티클이 **원을 그리며 돌면** 배선이 맞다. 한 자리에 고여 있으면 `_sweep` 참조가 안 물린 것이고, 원과 어긋난 곳에 있으면 `BrushPad` 멤버 이름을 잘못 짚은 것이다(Step 1).

```bash
~/.unity/bin/unity cmd editor_play --project-path $P
~/.unity/bin/unity cmd screenshot --view game --output /tmp/lab_wiring.png --project-path $P
~/.unity/bin/unity cmd editor_stop --project-path $P
```

- [ ] **Step 7: 저장한다**

```bash
~/.unity/bin/unity cmd save_scene --path Assets/Game/InGame/Dust/Tests/Dust_ParticleLook_Test.unity --project-path $P
```

---

### Task 5: CLI 튜닝 하네스

스펙 §8.5(랩이 CLI만으로 돌아간다)를 채우는 태스크다. 이게 없으면 Task 7이 다시 수동 GUI 작업이 되고, 그러면 이 브랜치가 존재할 이유가 없다.

**Files:**
- Create: `<scratchpad>/particle_variant.sh` — **리포지토리 밖.** 탐색 도구지 산출물이 아니다

**Interfaces:**
- Consumes: Task 4의 `ParticleLab` 오브젝트
- Produces: 변형 이름 하나를 받아 값 세팅 → 플레이 → 캡처 → 정지까지 하는 셸 함수. Task 7이 반복 호출한다

⚠ **실측 정정 (2026-08-12): `ParticleSystem`의 모듈은 CLI로 못 만진다.** `InitialModule` ·
`ShapeModule` · `EmissionModule` · `SizeModule` · `ColorModule` · `RotationModule`이 전부
`<unsupported:Generic>`으로 나온다. 그래서 하네스는 **`ParticleSystem`이 아니라
`DustParticleLookLab`의 필드**를 세팅한다. 아래 스크립트의 `--type`이 그것이다.

**또 하나: 값 세팅은 에디트 모드에서만 된다.** `set_component_properties`는 플레이 중 거부되므로
한 사이클이 "세팅 → 저장 → 플레이 → 캡처 → 정지"가 된다.

- [ ] **Step 1: 한 변형을 끝까지 도는 스크립트를 쓴다**

```bash
#!/usr/bin/env bash
# 사용법: particle_variant.sh <이름> <ParticleSystem 프로퍼티 JSON>
set -euo pipefail
P=/Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB
U=~/.unity/bin/unity
NAME="$1"; PROPS="${2:-{\}}"
GO=$($U cmd find_gameobjects --name ParticleLab --project-path $P --format json | python3 -c 'import sys,json; print(json.load(sys.stdin)[0]["handle"])')

# 프로퍼티가 비면 세팅을 건너뛴다 — 축 2(스프라이트)는 머티리얼만 바꾸고 파티클은 그대로 둔다
if [ "$PROPS" != "{}" ]; then
  $U cmd set_component_properties --type ParticleSystem --gameobject "$GO" --properties "$PROPS" --project-path $P
fi
$U cmd editor_play --project-path $P
sleep 3   # 첫 바퀴의 더러운 구간
$U cmd screenshot --view game --output "/tmp/particle_${NAME}.png" --project-path $P
$U cmd editor_stop --project-path $P
echo "/tmp/particle_${NAME}.png"
```

`find_gameobjects`의 JSON 필드 이름은 첫 호출에서 실제 응답을 보고 맞춘다 — `handle`이 아니면 그 이름으로 고친다.

- [ ] **Step 2: 한 번 돌려서 하네스를 검증한다**

```bash
bash <scratchpad>/particle_variant.sh sanity '{"startSize":0.05}'
```

기대: 경로가 출력되고 그 PNG가 존재한다. **연속 두 번 돌려 바이트 수가 다른지 확인한다** — 같으면 프레임이 안 갱신되는 것이고 `set_autotick`부터 고친다.

- [ ] **Step 3: 사람 손이 안 드는지 확인한다**

Step 2를 에디터 창을 **포커스하지 않은 채로** 돌린다. 스펙 §8.5의 조건이 그것이다. 포커스가 필요하면 `set_autotick`이 안 켜진 것이다.

---

### Task 6: 알갱이 스프라이트 생성

**Files:**
- Create: `Assets/Game/InGame/Dust/Textures/T_DustGrain_Particle_A.png` … `_D.png`

**Interfaces:**
- Consumes: 기존 아트 타겟 (`docs/images/`의 `dust-target-*.png`)
- Produces: Task 7이 `M_DustParticle`의 `_BaseMap`에 갈아끼울 후보 4장

- [ ] **Step 1: 후보를 생성한다**

`openai-image-gen` 스킬로 `gpt-image-1`(`/v1/images/edits`)에 기존 아트 타겟을 레퍼런스로 붙여 4장을 만든다. **실루엣이 서로 달라야 한다** — 같은 블롭의 밝기 변주는 스펙 §3의 가설을 검증하지 못한다.

| | 방향 |
|---|---|
| A | 불규칙한 알갱이 뭉치. 가장자리가 각지고 부서진 |
| B | 가늘고 긴 섬유·보풀 조각 |
| C | 알갱이 여러 개가 흩어진 클러스터 (한 알에 여러 점) |
| D | 부드러운 연기 퍼프 — **대조군.** 지금 것과 같은 계열이 정말 문제인지 확인용 |

배경은 투명, 정사각형, 단색 실루엣에 가깝게. **3인칭 거리에서 한 알은 몇 픽셀이다** — 디테일이 많으면 그 거리에서 다시 블롭이 된다.

- [ ] **Step 2: 임포트한다**

```bash
~/.unity/bin/unity cmd import_asset --source /tmp/T_DustGrain_Particle_A.png \
  --path Assets/Game/InGame/Dust/Textures/T_DustGrain_Particle_A.png --project-path $P
```

네 장 모두. `import_asset`을 쓰는 이유: Finder나 셸로 옮기면 `.meta` GUID가 흔들린다(루트 `AGENTS.md` 자산 편집 절).

- [ ] **Step 3: 알파가 살아있는지 확인한다**

```bash
~/.unity/bin/unity cmd get_import_settings --path Assets/Game/InGame/Dust/Textures/T_DustGrain_Particle_A.png --project-path $P
```

기대: `alphaSource`가 텍스처 알파를 쓰고 `alphaIsTransparency`가 켜져 있다. 꺼져 있으면 사각형 판때기로 렌더된다.

- [ ] **Step 4: 라이선스를 기록한다**

`Dust/Textures/AGENTS.md`에 네 장이 **생성물**이라고 적는다. 그 문서에 Megascans 미확인 건이 둘 있으므로 새로 들어온 것의 출처를 분명히 해둔다.

---

### Task 7: A/B 탐색

**Files:**
- Create: `docs/images/verify/particle_ab_*.png`

**Interfaces:**
- Consumes: Task 5의 하네스, Task 6의 스프라이트
- Produces: **이긴 룩의 값 표.** Task 8이 이것을 VFX Graph에 옮긴다

**한 번에 한 축만 바꾼다.** 스펙 §9의 마지막 항목이 그것을 요구한다 — 모양과 크기·개수를 같이 바꾸면 무엇이 효과였는지 모른다.

- [ ] **Step 1: 축 1 — `renderMode`**

스프라이트는 기본 그대로 두고 렌더 모드만 돌린다. 스펙 §5가 가장 의심스럽다고 지목한 축이다.

```bash
bash <scratchpad>/particle_variant.sh rm_billboard '{"renderMode":"Billboard"}'
bash <scratchpad>/particle_variant.sh rm_stretched '{"renderMode":"StretchedBillboard","velocityScale":0.3}'
bash <scratchpad>/particle_variant.sh rm_hbillboard '{"renderMode":"HorizontalBillboard"}'
```

Task 1의 `particle_before_thirdperson.png`와 나란히 본다. **판정 질문은 하나다 — 구슬로 보이는가?**

- [ ] **Step 2: 축 2 — 스프라이트**

Step 1의 승자를 고정하고 텍스처만 바꾼다.

```bash
for V in A B C D; do
  ~/.unity/bin/unity cmd set_material_properties --path Assets/Game/InGame/Dust/Materials/M_DustParticle.mat \
    --properties "{\"_BaseMap\":\"Assets/Game/InGame/Dust/Textures/T_DustGrain_Particle_$V.png\"}" --project-path $P
  bash <scratchpad>/particle_variant.sh "sprite_$V" '{}'
done
```

**D(연기 퍼프)가 이기면 스펙 §3이 기각된 것이다.** 모양이 원인이 아니라는 뜻이므로 §9의 셋째 항목대로 크기·개수로 넘어간다.

- [ ] **Step 3: 축 3 — 회전 랜덤**

같은 스프라이트라도 회전이 고정이면 도장처럼 보인다.

```bash
bash <scratchpad>/particle_variant.sh rot_fixed  '{"startRotation":0}'
bash <scratchpad>/particle_variant.sh rot_random '{"startRotation3D":false,"startRotation":{"mode":"TwoConstants","constantMin":0,"constantMax":6.283}}'
```

`startRotation`은 라디안이다. 곡선/랜덤 프로퍼티의 JSON 모양은 첫 호출에서 `get_component_properties`로 실제 형식을 읽어 맞춘다.

- [ ] **Step 4: 축 4 — 크기와 개수**

여기까지 와서도 약하면 그때 건드린다. 스펙 §9의 셋째 항목이 이 순서를 요구한다 — 모양을 먼저 보고, 부족할 때 양을 건드린다.

```bash
bash <scratchpad>/particle_variant.sh size_s '{"startSize":0.03}'
bash <scratchpad>/particle_variant.sh size_m '{"startSize":0.08}'
bash <scratchpad>/particle_variant.sh size_l '{"startSize":0.15}'
bash <scratchpad>/particle_variant.sh rate_lo '{"emission":{"rateOverTime":50}}'
bash <scratchpad>/particle_variant.sh rate_md '{"emission":{"rateOverTime":200}}'
bash <scratchpad>/particle_variant.sh rate_hi '{"emission":{"rateOverTime":600}}'
```

중첩 모듈(`emission`)의 JSON 모양은 첫 호출에서 `get_component_properties --type ParticleSystem`으로 실제 형식을 읽어 맞춘다. 평탄한 `rateOverTime`을 받는다면 그쪽으로 쓴다.

- [ ] **Step 5: 축 5 — 색**

승자를 고정하고 색만 바꾼다. **스페큘러 하이라이트 위에서 읽히는지**가 판정이다(스펙 §8.2). 선행 브랜치가 "파티클 색이 밝으면 하이라이트 위에서 안 보인다"에 실제로 걸렸다.

```bash
for C in "0.72,0.66,0.55,1" "0.45,0.40,0.34,1" "0.28,0.25,0.21,1"; do
  ~/.unity/bin/unity cmd set_material_properties --path Assets/Game/InGame/Dust/Materials/M_DustParticle.mat \
    --properties "{\"_BaseColor\":[${C}]}" --project-path $P
  bash <scratchpad>/particle_variant.sh "color_${C%%,*}"
done
```

밝은 것부터 어두운 것 순이다. 하이라이트와 경쟁하지 않으려면 **어두운 쪽이 유리하지만**, 너무 어두우면 먼지가 아니라 그을음으로 보인다. 최종 색은 `DustSurface`의 `_DirtColor`와 같은 계열이어야 한다 — 선행 스펙 §4가 "색은 먼지 재질에서 가져온다"를 규칙으로 정했고, Task 8에서 그래프에 옮길 때 그 규칙으로 돌아간다.

#### 축 1~3 결과 (2026-08-12)

| 축 | 후보 | 결과 |
|---|---|---|
| 렌더모드 | Billboard | **채택** |
| | Stretch (`velocityScale 0.35`) | **기각.** 파티클이 뭉쳐 불투명한 페인트 자국이 된다 |
| | Horizontal / Vertical Billboard | 차이 없음 |
| 색 | 밝은 황갈색 `(0.72, 0.66, 0.55)` | **기각.** 3인칭에서 거의 안 보인다 |
| | 중간 갈색 `(0.45, 0.40, 0.34)` | **채택.** 하이라이트 옆에서 읽히고 알갱이가 구분된다 |
| | 어두운 `(0.28, 0.25, 0.21)` | 보이지만 그을음처럼 과하다 |
| 스프라이트 | 없음 | **기각.** `_BaseMap`이 비면 불투명한 사각형이라 무조건 덩어리가 된다 |
| | A 불규칙 알갱이 뭉치 | **채택** |
| | D 부드러운 퍼프 (대조군) | A와 거의 같다 |

**가장 중요한 결과: 스프라이트의 유무가 결정적이고 실루엣의 종류는 3인칭에서 거의 차이가 없다.**
A(불규칙)와 D(부드러운 퍼프)가 사실상 같게 읽힌다. 스펙 §7이 "3인칭 거리에서 한 알은 몇
픽셀이다"라고 경고한 그대로다. **스펙 §3의 "모양이 원인"은 절반만 맞았다** — 원인은 모양이
아니라 **알파**였다. 불투명한 판이 알파를 가진 무엇으로 바뀌는 순간 알갱이로 읽히기 시작하고,
그 무엇이 정확히 어떤 실루엣인지는 이 거리에서 덜 중요하다.

A를 채택하는 이유는 우세해서가 아니라 의도한 디자인이고 근접에서 더 낫기 때문이다.

**후보 C는 실패작이다.** 격리된 스프라이트가 아니라 레퍼런스의 타일 바닥을 그대로 따라 그렸다.
레퍼런스를 붙이는 조건부 생성의 대가다 — 톤은 이어지지만 구도까지 따라올 수 있다.

**체크인하지 않은 스크린샷** (전부 찍었고 판정에 썼지만, 결정적인 것과 거의 같아 뺐다):
`rm_hbillboard` · `rm_vbillboard` · `col_mid` · `sprite_B` · `sprite_C`. 장당 2MB라 12장으로
21MB다. 남긴 것은 진단 5장(before + 그래프별 분리 3장)과 판정이 갈린 7장이다.

**바닥 클리핑은 머티리얼에서 해결했다** — `_Surface 1`(Transparent) · `_Blend 0`(Alpha) ·
`_SoftParticlesEnabled 1` · `_SoftParticlesFarFadeDistance 0.4`. 반원으로 잘리던 것이 없어졌다.

- [ ] **Step 6: 결과를 표로 적는다**

이긴 조합의 값 전부를 이 계획 파일 아래에 표로 남긴다. Task 8이 이것만 보고 그래프를 고칠 수 있어야 한다.

```bash
mv /tmp/particle_*.png docs/images/verify/
```

**증거는 스크린샷이다.** 선행 브랜치가 `PS_DustPuff`를 지우면서 남긴 규칙이 그대로 적용된다.

---

### Task 8: VFX Graph 이관과 파라미터 노출

**수동 GUI 작업이다.** CLI로는 `.vfx` 내부를 못 만진다(스펙 §2). 이 태스크만 사람이 그래프를 연다.

⚠ **자동화를 시도했고 막혔다 (2026-08-12).** `computer-use`(Orca)로 에디터를 조작해보려 했으나 두 가지가 겹쳐 불가능하다:

- **유니티의 접근성 트리가 비어 있다.** 창 전체에서 노출되는 요소가 6개(닫기·전체화면·최소화 버튼, 제목, 아이콘)뿐이고 에디터 UI는 하나도 없다. IMGUI로 직접 그리기 때문이다. 의미 기반 조작(`click --element-index`, `set-value`)이 전부 불가능하다
- **좌표 클릭은 포커스 요건에 걸린다.** Orca는 대상 창이 최상위 포커스가 아니면 합성 클릭을 거부한다(`window_not_focused`). `editor_focus`로 앞에 올려도 시스템은 "no focused window"로 본다 — 터미널과 에디터가 포커스를 주고받는 구조라 긴 시퀀스가 성립하지 않는다

스크린샷은 읽힌다. 그래서 **진단은 자동화되지만 편집은 안 된다.** 이 비대칭이 이 브랜치 전체의 모양을 결정한다 — 랩이 존재하는 이유도 같은 것이다.

남은 자동화 후보는 `UnityEditor.VFX` 내부 API를 리플렉션으로 두드리는 에디터 스크립트뿐이다. 문서화되지 않은 internal API이고 베타 유니티라, 수동 30분짜리 작업을 위해 감수할 위험은 아니다. **한 번 수동으로 하고 나면 Exposed 파라미터 덕에 다음부터는 CLI로 돌아간다** — 그게 이 태스크를 수동으로 하는 것이 정당한 이유다.

**Files:**
- Modify: `Assets/Game/InGame/Dust/VFX/VFX_DustPuff.vfx`, `VFX_DustPush.vfx`, `VFX_CleanSparkle.vfx`

**Interfaces:**
- Consumes: Task 7의 값 표
- Produces: 그래프마다 Exposed 파라미터 — `ParticleSize` (float) · `ParticleRate` (float) · `ParticleColor` (Color) · `ParticleTexture` (Texture2D). Task 9가 이 이름으로 CLI 튜닝을 확인한다

- [ ] **Step 1: 퍼프부터 룩을 옮긴다**

`VFX_DustPuff`를 열고 Task 7의 승자를 반영한다 — 출력 컨텍스트의 텍스처, 크기, 개수, 색, 회전.

**수치가 아니라 방향이 옮겨간다**(스펙 §9). 내장 파티클과 VFX Graph는 크기·속도의 의미가 다르므로, 같은 숫자를 넣는 게 아니라 **같은 인상이 나올 때까지** 맞춘다.

- [ ] **Step 2: 룩 노브를 Exposed로 뺀다** — 사람이 하는 작업

파라미터 목록과 근거는 **스펙 §5의 "노출할 파라미터 — 확정"** 에 있다. 여기에는 손 순서만 적는다.

**세 그래프마다 반복한다.** `VFX_DustPuff` → `VFX_DustPush` → `VFX_CleanSparkle`.

1. 그래프를 연다 (Project 창에서 `.vfx` 더블클릭)
2. Blackboard 좌상단 **`+`** → 타입을 고른다 → 이름을 아래 표대로 적는다
3. 만든 항목을 **우클릭 → Exposed** 를 켠다 (안 켜면 코드·인스펙터에서 안 보인다)
4. Blackboard의 항목을 캔버스로 **드래그**해 노드를 만들고, 해당 슬롯에 연결한다

| 이름 | 타입 | 어디에 | 연결 방법 |
|---|---|---|---|
| `ParticleRate` | float | Spawn 컨텍스트의 `Constant Spawn Rate` → `Rate` | 기존 값과 `Multiply` |
| `ParticlePower` | float | Initialize의 속도 블록 (`Set Velocity` 계열) | 기존 값과 `Multiply` |
| `ParticleSize` | float | Initialize의 `Set Size` | 기존 값과 `Multiply` |
| `ParticleColor` | Color | Output 컨텍스트의 색 슬롯 | 직접 대입 |
| `ParticleTexture` | Texture2D | Output 컨텍스트의 `Main Texture` | 직접 대입 |

**배율에 `Multiply` 를 끼우는 이유**: 그래프 안에 이미 곡선과 랜덤 범위가 있고, 절대값으로 덮으면 그 변화가 죽어 파티클이 도장처럼 보인다. 기본값은 **1** 로 둔다 — 그래야 노출 직후의 룩이 지금과 같고, 회귀가 생기면 배선 실수라고 바로 알 수 있다.

**언더스코어를 쓰지 않는다.** `Dust/AGENTS.md:55`가 "VFX Graph property names — no underscore"를 규칙으로 적어뒀다.

- [ ] **Step 2b: 노출이 실제로 먹는지 CLI로 확인한다**

이게 이 태스크의 합격 기준이다. 그래프를 안 열고 값이 바뀌어야 한다.

```bash
~/.unity/bin/unity cmd find_gameobjects --name VFX_Puff --project-path $P
~/.unity/bin/unity cmd set_component_properties --type VisualEffect --target <handle> \
  --properties '{"ParticleRate":3.0}' --project-path $P
```

기대: 값이 먹고 화면에서 먼지가 눈에 띄게 많아진다. 안 먹으면 **Exposed 체크가 빠진 것**이다(3번 단계).

기본값 1에서 시작해 `ParticleRate`·`ParticlePower`를 올려가며 §8.5를 다시 판정한다.

- [ ] **Step 3: 밀림과 반짝에도 같은 작업**

`VFX_DustPush`, `VFX_CleanSparkle`. **셋의 파라미터 이름이 정확히 같아야** 한 코드로 셋을 다 만질 수 있다.

- [ ] **Step 4: `DustCleanVfx`가 안 깨졌는지 확인한다**

`DustCleanVfx.Bind`는 필수 두 개(`ErasedMap`, `ErasedThreshold`)에만 에러를 내고 나머지는 경고로 넘긴다. 파라미터를 **추가**하는 것이므로 안 깨지는 게 정상이다.

```bash
~/.unity/bin/unity cmd get_console_logs --severity Error --project-path $P
~/.unity/bin/unity cmd get_console_logs --severity Warning --project-path $P
```

기대: 에러 0건. 새 파라미터 이름에 대한 경고도 없어야 한다 — 있으면 `DustCleanVfx`가 그것을 바인딩하려 든다는 뜻이고, 그건 룩 노브를 코드가 덮어쓴다는 뜻이라 노출한 의미가 없어진다.

---

### Task 9: 회귀 측정과 최종 판정

**Files:**
- Create: `docs/images/verify/particle_after_thirdperson.png`

**Interfaces:**
- Consumes: Task 8의 그래프
- Produces: 스펙 §8의 7개 판정 결과

- [ ] **Step 1: 선행 판정을 다시 잰다** — 스펙 §8.4

`Dust_Look_Test`를 열고(**저장하지 않는다**) `DustPadSweep`을 켜서 6바퀴를 돌린 뒤 살아있는 파티클을 센다.

기대: 퍼프 112→1 · 밀림 17→1 · 반짝 43→0. **여기가 어긋나면 이관이 파이프라인을 건드린 것이다** — 룩만 바꿨어야 한다.

- [ ] **Step 2: 3인칭 after 스크린샷** — 스펙 §8.1·8.3

Task 1의 `particle_before_thirdperson.png`와 **같은 카메라, 같은 바퀴 수**로 찍는다.

```bash
~/.unity/bin/unity cmd screenshot --view game --output /tmp/particle_after_thirdperson.png --project-path $P
mv /tmp/particle_after_thirdperson.png docs/images/verify/
```

- [ ] **Step 3: 하이라이트 위 판정** — 스펙 §8.2

스페큘러 하이라이트와 겹치는 구간의 프레임을 따로 찍는다. 겹친 곳에서 안 보이면 색을 다시 잡는다.

- [ ] **Step 4: CLI 튜닝이 실제로 되는지 확인한다** — 스펙 §8.6

```bash
~/.unity/bin/unity cmd find_gameobjects --component VisualEffect --project-path $P
~/.unity/bin/unity cmd set_component_properties --type VisualEffect --gameobject <handle> \
  --properties '{"ParticleSize":0.2}' --project-path $P
```

기대: 값이 먹고 화면이 바뀐다. **이게 이번 브랜치의 오래 가는 산출물이다.**

- [ ] **Step 5: 마젠타와 콘솔** — 스펙 §8.7

```bash
~/.unity/bin/unity cmd get_console_logs --severity Error --project-path $P
```

기대: 0건. 스크린샷에 마젠타 없음.

- [ ] **Step 6: 씬을 원상복구한다**

루트 `AGENTS.md` §5. Play Mode 끄기, `Dust_Look_Test` 저장 안 함, 임시 오브젝트 없음, `(Clone)`·`__TEST__` 이름 없음.

```bash
~/.unity/bin/unity cmd editor_stop --project-path $P
~/.unity/bin/unity cmd list_open_scenes --project-path $P
```

기대: dirty 상태의 씬이 없다.

---

### Task 10: 문서와 체크인

**Files:**
- Modify: `Assets/Game/InGame/Dust/AGENTS.md`
- Modify: `docs/INDEX.md`
- Create: `docs/Session_Summary_20260812_dust-particle-look.md`

- [ ] **Step 1: `Dust/AGENTS.md`를 정정한다**

두 군데를 고친다:

- **"Still weak at third-person distance (2026-08-11)"** — 이 절이 원인을 "크기와 개수"로 적어뒀다. Task 1·7이 실제 원인을 밝혔으므로 그걸로 정정한다. 틀린 진단을 남겨두면 다음 사람이 같은 데를 판다
- **새 결정 하나** — 룩 노브가 Exposed라는 것과 그 이름 넷. 다음 튜닝이 CLI로 된다는 사실이 문서에 없으면 아무도 안 쓴다

- [ ] **Step 2: `docs/INDEX.md`를 갱신한다**

현재 상태의 "먼지 청소 VFX 완료 — 룩 튜닝만 남음" 줄을 이번 결과로 바꾸고, Specs·Plans·세션 로그에 세 줄을 넣는다.

- [ ] **Step 3: 세션 요약을 쓴다**

`wrap-session` 스킬 형식. 판정 표(§8의 7개)와 **틀린 것으로 밝혀진 가설**을 반드시 적는다 — 스펙 §3이 맞았는지 틀렸는지가 다음 사람에게 제일 쓸모 있다.

- [ ] **Step 4: 체크인한다**

Unity 밖에서 만든 파일은 `Private`이므로 먼저 추가하고, 수정된 파일은 `checkout` 없이는 `cm ci`가 거부한다:

```bash
cm status --private
cm add -R docs/specs/2026-08-12-dust-particle-look.md docs/plans/2026-08-12-dust-particle-look.md
cm add -R docs/images/verify docs/Session_Summary_20260812_dust-particle-look.md
cm checkout docs/INDEX.md Assets/Game/InGame/Dust/AGENTS.md Assets/Game/InGame/Dust/Scripts/DustPadSweep.cs
cm ci --commentsfile=/tmp/ci_comment.txt <경로 전부>
cm status
```

**함정 셋을 미리 처리한다**(루트 `AGENTS.md`):

- 디렉터리 경로는 안의 `Changed` 파일을 건너뛴다 → 수정된 파일을 이름으로 명시하고 `checkout`한다
- `.meta`는 자산을 안 따라간다 → `.png`·`.mat`·`.unity`·`.cs`마다 `.meta`를 같이 적는다
- 삭제 경로와 수정 경로를 같은 `ci`에 섞으면 전체가 중단된다 → 삭제는 따로

**체크인은 5개로 나눈다**(위 표). 한 번에 다 넣지 않는다.
