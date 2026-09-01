# 대걸레 청소 조작 — 설계

> **작성일** 2026-08-11 · **브랜치** `/main/mop-cleaning-mode` · **피처** `Assets/Game/InGame/Mop/`

좌클릭으로 **청소 모드**에 들어가면 시점이 탑다운으로 바뀌고 조작이 탱크식으로 바뀐다. 그 상태로 밀고 다니면 지나간 자리의 먼지가 닦인다.

선행: [2026-08-11-dust-clean-vfx.md](2026-08-11-dust-clean-vfx.md) — 도구가 스팀청소기 계열이라는 것, `BrushPad` 규약, 붓질 호출 순서가 거기 있다. 그 문서가 *"도구 자체(조작·모델·온오프)는 `InGame/Mop/` 소관이고 이번 브랜치에서 만들지 않는다"*고 지목해둔 작업이 이것이다.

---

## 0. 전제 — 이미 있는 것

이번에 만드는 것은 **이음매뿐**이다. 양쪽 끝은 이미 있다.

| 있는 것 | 어디 |
|---|---|
| `BrushPad` · `DustPaintTarget.Paint/CaptureErased` · `DustCleanVfx` | `InGame/Dust/` (cs:65) |
| `PlayerState` · `InputReader` · `PlayerAnimationController` · `PlayerCameraController` | `InGame/Player/` (cs:67) |

플레이어 프리팹 구조가 설계를 결정한다:

```
PF_Player                [PlayerState, PlayerToolSwitcher]
  GnomeCharacter         [CharacterController, InputReader,
                          PlayerAnimationController, Animator]
  PF_SyntyCamera         [PlayerCameraController]
    MainCamera           [Camera, AudioListener]
  FrontRayPos / RearRayPos
```

끌 컴포넌트 셋이 서로 다른 오브젝트에 있고, 몰 대상(`CharacterController`, `MainCamera`)도 제자리에 있다.

---

## 1. 범위

**한다**

| | |
|---|---|
| 모드 전환 | 좌클릭 토글. 진입/이탈 시 컴포넌트와 액션 맵 교대 |
| 카메라 | 청소 중 탑다운 추적. 피치 기본 **65도**, 직렬화 범위 45~80 |
| 조작 | W 전진, A/D 회전 (탱크식) |
| 청소 | 플레이어 앞 고정 오프셋의 패드가 바닥에 붙어 먼지를 지움 |
| 검증 씬 | `Mop/Tests/Mop_Cleaning_Test.unity` |

**안 한다**

| 안 하는 것 | 이유 / 소관 |
|---|---|
| 청소기 모델·애니메이션 | 아트가 없다. 패드는 디버그 표시로만 보인다 |
| 대걸레(`Mop`) 조작 | `InGame/Mop/`. `PlayerToolSwitcher`가 이미 F키로 상태만 바꾼다 |
| 청소도 집계 | `InGame/Cleanliness/` |
| 흡입 궤적 (프롭이 빨려들어오는 연출) | 먼지가 아니라 `Trash` 대상이다 |
| 사운드·진동 | 도구 모델이 생긴 뒤 |
| Fusion 코드 | §5 참조. 이번 산출물은 전부 클라이언트 로컬이다 |
| 팀원 파일 수정 | §3 참조. 한 줄도 고치지 않는 것이 설계 목표다 |

---

## 2. 구성

`Assets/Game/InGame/Mop/`에 다섯 개.

| 파일 | 책임 | 의존 |
|---|---|---|
| `Scripts/MopMode.cs` | 좌클릭 토글, 모드 상태, 전환 시 켜고 끄기 | 나머지 넷을 조율 |
| `Scripts/MopLocomotion.cs` | W 전진 / A·D 회전 | `CharacterController` |
| ~~`Scripts/MopCamera.cs`~~ | ~~탑다운 추적~~ | **구현 중 Cinemachine으로 대체 — §3 참조** |
| `Scripts/MopPad.cs` | 패드를 만들어 `Dust`에 건넴 | `BrushPad`, `DustPaintTarget`, `DustCleanVfx` |
| `Input/MopControls.inputactions` | 청소 모드 입력 | — |

`MopMode`가 `PF_Player` 루트에 붙는다 — `PlayerState`·`PlayerToolSwitcher`와 같은 자리다. 나머지 셋은 각자 몰 대상 옆에 붙는다.

**경계**: `Mop`은 마스크를 모른다. `BrushPad`를 채워 건넬 뿐이고 지우는 것은 `Dust`가 한다. `Mop/AGENTS.md`가 정한 경계 — 바닥 오염은 여기, 빨려드는 물건은 `Vacuum` — 가 그대로 유지된다.

---

## 3. 모드 전환 — 끄고 켜고 되돌리기

### 팀원 파일을 고치지 않는다

`PlayerCameraController`의 피치는 `_cameraTiltBounds = (-10, 45)`로 잘려 있고 관련 필드가 전부 private이라 밖에서 60~70도를 만들 수 없다. 그렇다고 그 파일에 공개 API를 더하면, 그쪽이 계속 튜닝 중인 파일(cs:40·42가 카메라 프레이밍 조정이었다)이라 머지가 비싸진다.

**대신 통째로 끄고 우리가 몬다.** 이탈하면 되돌린다.

| | 평상시 | 청소 중 |
|---|---|---|
| `InputReader` | 켬 | **끔** |
| `PlayerAnimationController` | 켬 | **끔** |
| `PlayerCameraController` | 켬 | **끔** |
| `MopLocomotion` · `MopCamera` · `MopPad` | 끔 | **켬** |

### 액션 맵도 교대한다

`InputReader.OnEnable()`이 `_controls.Player.Enable()`, `OnDisable()`이 `Disable()`이다. 즉 **컴포넌트를 끄면 `Player` 액션 맵이 통째로 꺼진다.** 별도 조치가 필요 없다.

청소 모드 입력은 `Mop`이 자기 자산으로 갖는다. 팀원의 `Controls.inputactions`에 좌클릭이 비어 있지만 그건 그쪽 자산이고, 루트 `AGENTS.md`의 *"피처는 필요한 걸 전부 소유한다"*에도 우리 것을 갖는 쪽이 맞다.

```
MopControls.inputactions
  ├ Cleaning    Move (WASD 2D)      — 청소 모드에서만 Enable
  └ ToolToggle  Toggle (좌클릭)      — 항상 Enable
```

**`ToolToggle`을 별도 맵으로 뺀 이유**: 진입은 평상시에, 이탈은 청소 중에 눌러야 하는데 두 맵은 배타적이다. 항상 켜진 얇은 맵을 위에 두면 그 아래에서 `Player` ↔ `Cleaning`이 교대한다.

`.inputactions`는 "Generate C# Class"를 켜 `MopControls.cs`를 생성한다. 팀원 쪽 `Controls.cs`와 같은 성격의 생성물이므로 그 선례대로 체크인한다.

### 토글이다 — `Mop/AGENTS.md`의 "hold"를 뒤집는다

`Mop/AGENTS.md`는 *"Left-click hold pulls in whatever is ahead and releasing stops it immediately"*라고 적고 있다. **이 결정이 그것을 뒤집는다.**

이유는 좌클릭이 이제 흡입 스위치가 아니라 **모드 스위치**이기 때문이다. 홀드로 두면 손을 뗄 때마다 카메라가 3인칭으로 돌아오고 조작이 다시 뒤바뀐다. 바닥 한 줄 닦는 동안 시점이 여러 번 뒤집히면 조작이 성립하지 않는다.

`Mop/AGENTS.md`의 그 문장을 이번 작업에서 고친다. 흡입이 홀드라는 원래 의도는 **쓰레기**(`Trash`)를 빨아들일 때 살아남을 수 있다 — 그건 모드 전환 없이 그 자리에서 하는 동작이므로 홀드가 맞다. 즉 "좌클릭 홀드 = 흡입"과 "좌클릭 토글 = 청소 모드"는 나중에 도구가 갈릴 때 다시 만난다.

### 구현 후 정정 — 카메라는 Cinemachine이 몬다 (2026-08-11)

이 절은 원래 `MopCamera`가 카메라 트랜스폼을 직접 계산하는 것으로 썼다. 만들어 돌려보니 **A/D를 누를 때 멀미가 났다.** 원인은 카메라 요를 캐릭터 요에 그대로 물린 것 — 회전이 즉각적이라 세상이 튀듯 돌았다.

세상이 도는 것 자체는 의도가 맞다. 없던 것은 **보간**이었다. 그래서 직접 짠 추적을 버리고 Cinemachine으로 옮겼다(이미 6.6.0을 쓰고 `PPack.InGame.asmdef`도 `Unity.Cinemachine`을 참조하고 있어 추가 비용이 없었다).

| | |
|---|---|
| `MainCamera` | `CinemachineBrain` (씬 오버라이드 — 프리팹은 안 건드린다) |
| `PF_Player/CleaningCam` | `CinemachineCamera` + `CinemachineFollow` + `CinemachineHardLookAt` |
| `BindingMode` | `LockToTargetWithWorldUp` — 캐릭터 요를 따라 궤도를 돈다 |
| `FollowOffset` | `(0, 5.362, -4.500)` — 피치 **50도**, 거리 7 |
| `PositionDamping` | `(0.8, 0.4, 1.2)` |
| `RotationDamping` | `(0, 2.0, 0)` — Y축만 지연 = 회전 딜레이 |
| `LookAtOffset` | `(0, 0.6, 0.8)` — 패드를 겨누고 캐릭터를 화면 63%로 |

`MopMode`는 컴포넌트가 아니라 **GameObject**를 껐다 켠다. vcam이 비활성이어야 Brain이 카메라를 놓고 `PlayerCameraController`가 되찾는다. 이 구조의 가장 큰 위험이었고 플레이 모드에서 통과를 확인했다.

`MopCamera.cs`는 삭제했다. 스크립트가 넷에서 셋으로 줄었다.

### 알려진 위험 셋

이번 단계에서 감수하되 검증 항목으로 둔다.

1. **카메라 복귀 시 튐.** `PlayerCameraController`가 `_lastAngleX`·`_lastAngleY`·`_lastPosition`을 내부에 들고 있어, 다시 켤 때 그 값에서 보간을 재개한다. 청소 중에 플레이어가 많이 움직였으면 화면이 튈 수 있다. **튀면 대안은 그 파일에 공개 메서드 하나를 더하는 것이고, 그때는 팀원과 합의한다.**
2. **애니메이션 정지.** `PlayerAnimationController`를 끄면 `Animator`가 그 자세로 멈춘다. 청소 애니메이션이 아직 없으므로 감수한다. 알려진 공백으로 기록한다.
3. **입력 잔류값.** `InputReader`를 꺼도 `_moveComposite`는 마지막 값을 유지한다. W를 누른 채 좌클릭하면 그 값이 박제되고 모드에서 나올 때 한 프레임 흘러갈 수 있다.

---

## 4. 청소 파이프라인

### 패드의 위치와 자세

패드는 플레이어 **앞 고정 오프셋**에 둔다. 캐릭터가 청소기를 앞으로 밀고 다니는 모양이다. 기본값은 로컬 `(0, 0, 0.8)` — 패드 길이(반쪽 0.15)와 캐릭터 반지름을 감안해 발밑을 벗어나되 팔이 닿는 거리다. 직렬화 필드로 두어 눈으로 맞춘다.

자세는 표면이 정한다 — 오프셋 지점에서 아래로 레이캐스트해 맞은 표면의 노멀을 패드의 위쪽으로, 플레이어 정면을 그 노멀에 투영한 것을 패드의 진행 방향으로 삼는다. `DustMousePainter.UpdatePadRotation`과 같은 방식이고, 경사면에서도 패드가 뜨지 않는다.

레이캐스트가 아무것도 맞지 않으면(공중, 구멍) 그 프레임은 붓질하지 않는다.

### 호출 순서는 계약이다

```csharp
_vfx.BeginFrame();
target.CaptureErased(_vfx.ErasedMap, pad);   // 빼기 전 마스크를 읽어야 한다
target.Paint(pad);
_vfx.Play(travelDirection);
```

`CaptureErased`가 `Paint`보다 **먼저**여야 한다. 빼고 나면 지워진 양을 알 수 없다. 선행 스펙이 이 순서를 이유와 함께 못 박아뒀다.

**이 묶음은 프레임당 한 번 도는 자리에 있어야 한다.** Fusion이 오면 그대로 `Render()`로 옮겨간다 — `FixedUpdateNetwork`에 두면 재시뮬레이션마다 중복으로 지워진다.

붓은 **맞은 콜라이더가 속한 `DustPaintTarget`만** 지운다. 벽 너머가 지워지지 않게 하는 규칙이고 `DustMousePainter`가 이미 그렇게 한다.

### 패드 파라미터

`DustMousePainter`의 튜닝값을 그대로 가져온다. 이미 손맛이 맞춰진 값이고, 도구가 바뀌었다고 붓이 달라질 이유가 없다.

| | 값 |
|---|---|
| 반쪽 크기 | `(0.5, 0.15)` |
| 두께 | `0.25` |
| 페더 | `0.06` |
| 세기 | `0.35` |
| 울퉁불퉁함 / 결 크기 | `0.55` / `6` |
| 세기로 고르게 | `0.65` |

`DustCleanVfx`는 **`MopPad`이 소유한다.** 선행 스펙이 *"붓질을 미는 쪽이 소유한다 — 지금은 `DustMousePainter`, 나중에 도구"*라고 적어둔 그대로다. 도구당 하나여야 Fusion이 왔을 때 원격 플레이어의 도구도 자기 것을 갖는다.

---

## 5. 멀티플레이 — 이번에도 코드는 없다

선행 스펙 §6의 결론이 그대로 적용된다. 복제되는 것은 **도구 포즈 + 켜짐/꺼짐 + 소유권**뿐이고, 붓질은 이벤트로 보내지 않는다. 각 클라이언트가 "패드가 켜져 있고 표면에 닿아 있으면 찍는다"를 스스로 실행한다.

이번 산출물이 지켜야 할 것은 두 줄이다.

1. **붓질을 프레임당 한 번 적용하는 자리에 둔다** — 나중에 `Render()`로 그대로 옮긴다
2. **모드 상태(`IsCleaning`)를 나중에 `[Networked]`로 승격할 수 있게 한 곳에 둔다** — `MopMode`가 유일한 진실이고, 다른 컴포넌트는 그것을 읽기만 한다

---

## 6. 기각한 대안

| 대안 | 기각 이유 |
|---|---|
| `PlayerCameraController`에 공개 API 추가 | 팀원이 계속 튜닝 중인 파일이라 머지가 비싸진다. 통째로 끄는 쪽이 파일을 안 건드린다. **복귀가 튀면 이 결정을 뒤집는다** |
| 청소 모드 전용 두 번째 `Camera` | 카메라가 둘이면 어느 쪽이 활성인지가 새 상태가 되고, `AudioListener`도 둘이 된다. 하나를 몰고 되돌리는 쪽이 상태가 적다 |
| 팀원 `Controls.inputactions`에 `Cleaning` 맵 추가 | 남의 자산이고, 피처가 자기 입력을 소유하는 편이 규칙에 맞다 |
| 입력을 재해석 (`_moveComposite.x`를 회전으로) | 액션 맵 전환이 Input System의 정석이고 의도가 자산에 드러난다. 재해석은 코드를 읽어야 조작을 알 수 있다 |
| 패드가 이동 주체, 캐릭터가 따라옴 | 청소 정밀도는 좋지만 캐릭터 로코모션과 이중 구조가 된다. 앞 오프셋이면 기존 `CharacterController` 하나로 끝난다 |
| 팀원 맵 씬(`Map_TestSceneWithoutCar`)에서 검증 | 그 씬 바닥은 `DustPaintTarget`이 아니고 tripo 변환 메시라 UV0 유니크 언랩을 기대할 수 없다. 남의 씬이기도 하다 |

---

## 7. 검증

`Mop/Tests/Mop_Cleaning_Test.unity` — `PF_Player` + 오염 바닥. 빌드 세팅에 넣지 않는다(루트 `AGENTS.md` §6).

플레이 모드에서 확인할 것:

1. 좌클릭에 시점이 탑다운으로 바뀐다
2. W로 전진하고 A/D로 제자리 회전한다
3. 지나간 자리가 닦이고 퍼프가 뜬다
4. 다시 좌클릭하면 원래 시점·조작으로 **튀지 않고** 돌아온다 (§3 위험 1)
5. 청소 중 WASD가 캐릭터를 평상시처럼 움직이지 않는다 (액션 맵이 실제로 교대했는지)
6. W를 누른 채 진입·이탈해도 캐릭터가 흘러가지 않는다 (§3 위험 3)
7. 경사면에서 패드가 표면에 붙는다
8. 벽 너머의 바닥이 지워지지 않는다

4·6번은 실패해도 이번 범위에서 고치지 않을 수 있다 — 그때는 원인과 함께 `Mop/AGENTS.md`에 기록하고 다음으로 넘긴다.

---

## 8. 뒤집는 조건

- **컴포넌트를 끄는 방식** — 복귀 시 튐이 감당 안 되거나, 청소 중에도 애니메이션이 필요해지면. 그때는 `PlayerCameraController`·`PlayerAnimationController`에 공개 API를 더하는 쪽으로 가고 팀원과 합의한다
- **패드가 플레이어 앞** — 청소 정밀도가 부족하다고 판단되면 패드를 이동 주체로 뒤집는다
- **입력 자산 분리** — `Mop` 말고 다른 피처도 자기 맵을 갖기 시작해 맵이 넷 이상이 되면, 프로젝트 하나로 합치는 쪽을 다시 따진다
