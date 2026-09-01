# 펭귄 체력과 점프 쿨타임 — 설계

> 2026-08-26 · `/main/multiplay-delivery-mission` 위에서 작업
> 선행: [멀티 펭귄 재구축](2026-08-24-multiplay-penguin-rebuild.md) · [배달 미션 멀티플레이](2026-08-26-multiplay-delivery-mission.md)

## 목표

두 가지를 넣는다.

1. **체력** — Shift 달리기가 소모하고, 달리지 않으면 찬다. 다 쓰면 걷기로 내려가고, 일정
   량까지 차야 다시 달릴 수 있다. 좌하단 HUD 바로 보인다.
2. **점프 쿨타임** — 착지 즉시 재점프가 되는 현행을 막는다. **UI 없이 내부적으로만** 존재한다.

**범위 밖**: 체력을 쓰는 다른 행동(점프 비용·슬라이딩 비용), 체력 회복 아이템, 탈진 연출·사운드,
남의 펭귄 체력 표시.

## 지금 구조 (측정된 사실)

| 요소 | 상태 |
|---|---|
| Shift | **달리기**다. `_walkSpeed 3.5` → `_sprintSpeed 5.5` (`PenguinLocomotion.cs:522`). 슬라이딩 진입·발 킥도 겸하는 과부하 키 |
| 체력 | **없다.** `PenguinLocomotion.cs:60` 툴팁이 *"체력 제한 없이 유지된다"* 라고 현행을 서술 |
| 점프 진입점 | **4개** — `ApplyJump`(보행) · `Jump()`(운반 중 눈 위) · `JumpFromSlide()`(슬라이딩) · `BareHop()`(맨바닥) |
| 점프 조건 | 전부 `input.JumpPressed && grounded` **뿐**. 쿨타임이 없어 착지 즉시 재점프가 된다 |
| 점프 수렴점 | 4개 모두 `_jumpCount++; Jumped?.Invoke();` 로 끝난다 |
| `Step(dt, input)` | 이동 본문 하나. 싱글은 `FixedUpdate`, 멀티는 `PenguinNetAvatar.FixedUpdateNetwork` 가 부른다 |
| 예측 | **안 켰다.** 그래서 멀티에서 `Step` 은 **서버에서만** 돈다 |
| 연출 복제 | `PenguinPresentation` → `[Networked]` → `Render()` 의 `ApplyPresentation` 경로가 이미 있다 |

## 핵심 관찰 — 서버 권위가 공짜다

예측을 안 켰으므로 `Step` 은 권위 피어에서만 돈다. **`Step` 안에서 깎는 체력은 그 자체로 이미
서버 권위다.** 권위를 위한 별도 코드가 없다.

내보내는 길도 이미 있다. `Speed`·`Grounded` 가 쓰는 `PenguinPresentation` → `[Networked]` →
`ApplyPresentation` 경로에 칸 하나만 더하면 된다. **새 패턴이 없다.**

그 결과 UI 쪽이 특히 단순해진다 — 권위 피어는 `Step` 이 값을 채우고, 비권위 피어는 `Render()`
의 `ApplyPresentation` 이 채운다. 그래서 **모든 피어에서 `PenguinLocomotion.Stamina01` 하나만
읽으면 맞다.** HUD 에 싱글/멀티 분기가 생기지 않는다.

## 설계

### 1. `PenguinStaminaState` (신규, 순수 struct)

`Penguin/Scripts/PenguinStaminaState.cs`. `Tick(dt, wantsSprint)` 이 **달려도 되는지** 를 돌려준다.

`StageMetrics`·`PenguinMoveInput` 과 같은 패턴이다. 순수 값이라 **씬도 GameObject 도 없이
EditMode 에서 검증**된다 — 이것이 로코모션 인라인(1078줄 파일이 더 커지고 씬 없이는 못 짠다)
대신 이쪽을 고른 이유다. 별도 MonoBehaviour 도 아니다: 자기 `Update` 로 돌면 서버 틱과 어긋나
결국 `Step` 이 틱해야 하므로, 컴포넌트로 만들면 배선만 늘고 얻는 것이 없다.

- `Value01` (0~1), `Exhausted` (bool 래치).
- 탈진하면 문턱을 넘을 때까지 `wantsSprint` 를 **계속 거절한다.** 이게 없으면 0 근처에서 끝없이
  끊기는 "딱딱이 달리기" 가 된다 — 지금 연속 점프와 같은 종류의 문제라 같은 방식으로 막는다.
- 회복은 지연 뒤에 시작한다. Shift 를 톡톡 눌러 무한 질주하는 것을 막는다.

### 2. `PenguinLocomotion`

- struct 를 필드로 들고 `Step` 에서 틱, `522` 행의 `moveSpeed` 선택을 게이트한다.
- `Stamina01` 을 공개 속성으로 낸다.
- **점프 쿨타임 타이머 하나.** 4개 진입점이 전부 `_jumpCount++; Jumped?.Invoke();` 로 수렴하므로
  그 둘을 `MarkJumped()` 로 뽑아 타이머를 찍고, 조건 3곳(`ApplyJump` · 운반 · 슬라이딩)에
  `CanJump` 게이트를 건다.
- **슬라이딩 점프도 같은 타이머를 공유한다.** 따로 두면 슬라이딩으로 우회해 연타된다.
- 쿨타임은 **복제하지 않는다** — `Step` 이 서버 전용이라 타이머도 하나뿐이고, UI 도 없다.
- `60` 행 툴팁의 *"체력 제한 없이 유지된다"* 를 고친다.

### 3. `PenguinPresentation` · `PenguinNetAvatar`

`Stamina01` 한 칸 추가. `PublishPresentation` 과 `Render` 에 한 줄씩. `NetSpeed` 와 같은 길이다.

`PenguinPresentation` 의 주석이 "여기 없는 것" 으로 이동 규칙 값들을 배제하는데, 체력은 그
경계의 어느 쪽인지 명시해 둔다: **이동 규칙이 만들고 UI 가 읽는 값이라 `Speed` 와 같은 자리다.**
비권위 피어가 이동을 계산하지 않는다는 사실은 여기서도 그대로다 — 그래서 복제한다.

로컬 플레이어의 바가 RTT 만큼 늦는 것은 **의도된 결과다.** 예측을 안 켰으므로 자기 펭귄의
속도·접지도 이미 서버가 정한 값을 그린다. 체력만 로컬로 앞서 그리면 그 하나만 화면의 나머지와
어긋난다.

### 4. HUD

- `StageHUD.uxml`/`.uss` 에 **좌하단** 체력 바. 좌상단은 의뢰 보드, 중상단은 시계, 우상단은 점수
  칩, 중하단은 상호작용 프롬프트(`F CARRY`)라 좌하단이 비어 있다.
- `StageHUDController.SetStamina01(value01, exhausted)`. 탈진 중에는 색이 바뀐다.
- `RequestHudPresenter` 가 `_player` 에서 `PenguinLocomotion` 을 집어 매 프레임 넣는다 —
  점수 칩과 같은 모양의 한 줄.

### 5. 수치 (전부 `[SerializeField]`, 인스펙터에서 조정)

| 값 | 시작점 | 근거 |
|---|---|---|
| 최대 지속 | 6초 전력 질주 | 집 사이를 한 번에 질주할 정도 |
| 회복 | 4.5초에 만충 | 달린 시간보다 조금 짧게 — 기다림이 벌처럼 느껴지지 않게 |
| 회복 지연 | 0.6초 | Shift 톡톡 눌러 무한 질주 방지 |
| 탈진 해제 문턱 | 30% | |
| 점프 쿨타임 | 0.35초 | 착지 연타는 막고, 조작이 굼뜨게 느껴지지는 않는 선 |

## 검증

- **EditMode 신규** — 소모 / 회복 / 회복 지연 / 탈진 래치(0 에서 바로 못 달리고, 문턱을 넘어야
  풀린다). 씬 불필요.
- **PlayMode 신규** — 착지 직후 재점프가 쿨타임 동안 거절되고 그 뒤 통과한다. 슬라이딩 점프도
  같은 타이머를 공유한다. 기존 `PenguinActionTests` 옆.
- **회귀** — Penguin PlayMode 38개를 **이름 집합**으로 비교한다(개수 아님 —
  `playmode-red-baseline` 참고).
- 테스트 뒤 Unity 를 원상복구한다(루트 `AGENTS.md` §5).

## 결정하지 않은 것

- **점프는 체력을 안 쓴다.** 쿨타임만으로 연타를 막는다. 둘 다 걸면 원인이 겹쳐 튜닝이 어려워진다.
- **슬라이딩은 체력을 안 쓴다.** Shift 가 달리기·슬라이딩 진입·발 킥을 겸하는데, 이번엔 달리기
  속도 선택 하나만 게이트한다. 슬라이딩까지 묶는 것은 활강 필을 바꾸는 별도 결정이다.

  ⚠ **그 결과 실전에서는 체력이 거의 깎이지 않는다. 알고 남긴 것이지 누락이 아니다.**
  2026-08-26 에 `SnowDelivery_RequestFlow_Test` 에서 실측했다 — Shift 를 쥐고 달리면
  `controlState=Sliding · grounded=False · speed=6.55m/s · stamina=1.00 · sprintedLast=False`.
  `!grounded && !IsSliding && SprintHeld → EnterSliding()` 이라 눈밭에서 잠깐만 공중에 떠도
  슬라이딩으로 들어가고, 움직이는 동안에는 자동 종료(접선 속도 1.0m/s 이하 0.2초)도 안 걸려
  계속 거기 머문다. 평지 큐브를 깔고 다시 세워도 같았다.
  대안 두 가지를 함께 제시했고 **"그대로 둔다" 를 골랐다**: (A) 슬라이딩도 체력을 쓰게 한다,
  (B) 슬라이딩 진입 조건을 좁힌다(공중 Shift 자동 진입 제거). 체력을 실제로 체감시켜야 할 때
  꺼내 쓸 선택지는 이 둘이다.
- **남의 펭귄 체력은 안 보여 준다.** 값은 복제되지만 HUD 는 로컬 것만 그린다.
