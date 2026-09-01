# 증강 투표와 쉬는 시간 — 2026-09-01

멀티에서 증강이 **호스트에게만** 뜬다. 이 문서는 그것을 전원 투표로 바꾸고, 그 과정에서
`Time.timeScale = 0` 정지를 **싱글에서도** 걷어내 "쉬는 시간" 으로 대체하는 설계다.

선행: `2026-08-31-run-augments.md`(증강 자체) · `2026-08-31-single-multi-pipeline.md`(세션 규약).
폴더 규칙: `Assets/Game/InGame/Augment/AGENTS.md`.

---

## 1. 왜 호스트에게만 뜨는가 — 게이트가 네 겹이다

하나가 아니라서 한 줄로 못 고친다.

| 층 | 지금 | 근거 |
|---|---|---|
| 트리거 | `TimeOfDayNetHub` 가 `DayIndex` 를 **의도적으로 복제 안 함** | `Augment/AGENTS.md` |
| 열기 | `if (!Session.IsAuthority) return;` | `AugmentSelectionDirector.cs:75` |
| 정지 | `Time.timeScale = 0` — **피어 로컬** | `AugmentSelectionDirector.cs:137` |
| 효과 | `AugmentLoadout` 참조가 **씬 참조** | `SnowDeliverySceneBuilder.cs:951` |

넷째는 이 스펙 범위 밖이다 — `/main/multiplay-penguin-parity` 가 `PenguinNetAvatar.Spawned()` 에서
런타임 재바인딩으로 이미 닫았다.

---

## 2. 정지를 버린다 — 싱글도 같이

**`timeScale` 은 프로세스 전역이라 서버가 남의 피어 것을 0으로 만들 수 없다.** 서버가 할 수 있는
것은 "지금 투표 중" 이라는 플래그를 복제하는 것까지고, 그것을 받아 `timeScale = 0` 을 거는 것은
각 피어다. 그러면 원래 문제로 되돌아온다.

이것은 추측이 아니라 **이 프로젝트가 이미 실측한 것**이다. 일시정지 세션(2026-08-31)이
*"멀티에서 시간을 멈추면 그 피어만 멈추고 세션은 계속 돌아 재개하는 순간 자기 화면만 과거에 있다"*
를 확인하고 "멀티는 화면만 뜬다" 로 갔다. 증강도 같은 벽에 부딪힌다.

그래서 **`AugmentSelectionDirector.Pause()` / `Resume()` 를 통째로 지운다.** `Time.timeScale` 도
`PenguinInputReader.enabled` 도 건드리지 않는다. 싱글도 같다 — 두 모드가 같은 코드로 돈다는 규약이
정지 하나 때문에 깨져 있었고, 폴더 `AGENTS.md` 가 그것을 "갚아야 할 빚" 으로 적어 뒀다.

### 대가와 그것을 메우는 방법

정지가 하던 일은 둘이었다 — **압박을 없애고**, **고르는 동안 손을 멈추게** 하는 것. 첫째는 아래
쉬는 시간이 대신한다. 둘째는 포기한다: 쉬는 시간에는 계속 움직일 수 있고, 그것이 "쉬는 시간" 이라는
말과 더 맞는다.

### 부수 효과 — 정지 소유권 충돌이 사라진다

cs:917 이 "증강과 일시정지가 둘 다 `timeScale` 과 입력을 잡는다" 를 조율하는 데 쓰였다. 증강이
둘 다 안 잡으면 그 충돌은 **원인째** 없어지고 정지 주체가 다시 하나(일시정지 메뉴)가 된다.

⚠ **`PauseMenuController.cs:90` 의 가드(`_augmentSelection.IsOpen` 이면 ESC 를 안 연다)는 남긴다.**
이유가 바뀐다. 전에는 `timeScale` 소유권 때문이었고, 지금은 **커서** 때문이다 — 투표 중에는 증강이
커서를 풀어 두는데, 그 위에서 일시정지를 열었다 닫으면 그쪽이 커서를 다시 잠가 **투표 화면에서
커서를 잃는다.** 가드를 지우려면 커서 소유권을 먼저 세워야 한다.

---

## 3. 쉬는 시간 (Intermission)

정지 대신 **별도 타이머**를 둔다. 일차가 넘어가면 시작하고, 끝나면 투표를 집계하고 닫는다.
길이는 `AugmentSelectionDirector` 의 `[SerializeField]`, 기본 **20초**. `StageBalanceConfig` 에
넣지 않는다 — 인원 밸런스가 아니라 UI 리듬이다.

| 쉬는 시간 동안 | | 이유 |
|---|---|---|
| `RequestDirector.TickSpawns` | **안 돈다** | 새 의뢰가 안 나온다 — 이것이 "텀" 이다 |
| `RequestDirector.TickRequests` | **안 돈다** | 카드 읽는 사이에 의뢰가 죽으면 억울하다 |
| `RequestDirector.TickCompletions` | 돈다 | 쉬는 중에 배달을 마치는 것은 허용한다 |
| `TimeOfDayDirector` | 돈다 | 하늘이 멈추면 이상하다. 20초 < 하루 120초라 경계가 겹치지 않는다 |
| 펭귄 이동·물리 | 돈다 | 쉬는 시간이지 정지가 아니다 |

`RequestDirector.FixedUpdate()` 가 이미 셋으로 갈라져 있어(`:83-91`) 이음매가 깨끗하다.
게이트는 `RequestDirector` 가 `AugmentSelectionDirector` 를 **읽는** 방향이다 — 반대로 하면
증강이 배송 시스템을 조작하게 된다. 참조는 `[SerializeField]` 이고 `SnowDeliverySceneBuilder`
`BuildAugmentRig()` 가 채운다. `_augments` 를 물리는 그 줄(`:947`) 바로 옆이다.
**비어 있으면 게이트가 없는 것과 같다** — 증강을 안 놓은 씬과 테스트가 영향받지 않는 근거다.

⚠ **클라이언트에서는 이 게이트가 아예 필요 없다.** `MissionNetHub.Spawned()` 가 클라에서
`_director.enabled = false` · `_manager.enabled = false` 로 **둘을 끈다**(`MissionNetHub.cs:170` 부근).
그래서 쉬는 시간은 순수하게 서버·싱글의 문제다.

---

## 4. 누가 고르는가 — 전원 투표, 다수결

증강은 **판 하나에 붙는다**(팀 공유). 근거는 취향이 아니라 코드다 — `RequestDirector.TickCompletions()`
가 존 폴링이라 의뢰에 완료자 개념이 없어 per-player 로는 "보상 +40%" 를 곱할 대상이 없다
(`Augment/AGENTS.md`). 소유가 팀이므로 **선택도 팀이 한다.**

호스트 단독 선택이 더 싸지만, 그러면 나머지 셋은 자기 판의 규칙이 정해지는 것을 구경만 한다.

---

## 5. 구조 — `AugmentNetHub`

`AugmentSelectionDirector` 가 **권위에서 전부 굴리고**(추첨 · 쉬는 시간 타이머 · 표 집계),
새 `AugmentNetHub` 가 그 상태를 `[Networked]` 로 복사한다. 클라이언트는 허브만 읽는다.
`MissionNetHub` 의 문장을 그대로 따른다 — *"서버가 그대로 굴리고, 이 컴포넌트는 그 상태를
`[Networked]`로 복사한다. 클라이언트는 여기만 읽는다."*

**싱글은 허브 없이 디렉터만 돈다.** `StageSession.IsAuthority` 가 싱글에서 항상 참이라 같은 코드가
그대로 돈다. 허브가 없으면 디렉터가 자기 상태를 자기 뷰에 바로 먹인다.

허브를 따로 두는 것은 이 프로젝트의 관례다 — `TimeOfDayNetHub` · `BlizzardNetHub` · `GiftNetState`
가 전부 피처별 허브다. 루트 `AGENTS.md` 의 *"상태를 허브에 모아라"* 는 **오브젝트마다 흩지 마라**
라는 뜻이지 허브가 하나여야 한다는 뜻이 아니다.

### 스폰

`MissionNetSpawner` 를 그대로 본뜬 `AugmentNetSpawner` — 씬에 놓인 `MonoBehaviour` 가
`NetworkObject` 프리팹을 들고, `SessionLauncher.Phase == ESessionPhase.Playing` 이고 서버일 때
**한 번만** 스폰한다. 그 파일의 함정 둘이 그대로 적용된다:

- ⚠ **매치 시작 뒤에만 스폰한다.** 러너는 방을 연 순간부터 서버라, 그 조건만 보면 로비에서
  스폰하고 곧이어 씬 로드가 삼킨다 — 콘솔에 아무 오류도 안 남는다(2026-08-26 실측).
- ⚠ **반환값이 아니라 요청 자체로 한 번을 보장한다.** 스폰이 큐를 거치면 반환값이 null 이다.

씬 배선은 `MultiPlaySceneBuilder.BuildMissionSpawner()` 옆에 `BuildAugmentSpawner()` 로 넣는다.
**손으로 씬에 놓지 않는다** — 빌더가 씬을 매번 다시 조립하므로 다음 실행에 사라진다(cs:913 의 교훈).

---

## 6. 복제하는 것 — 그리고 안 하는 것

| 복제한다 | 타입 | 왜 |
|---|---|---|
| 투표 열림 | `NetworkBool` | 각 피어가 화면을 켜는 신호 |
| 카드 3장 | `NetworkArray<int>` (풀 인덱스) | 아래 참고 |
| 종료 틱 | `int` (`Runner.Tick` 기준) | 남은 시간 표시 |
| 표 | `NetworkArray<int>` (PlayerId 색인, -1 = 미투표) | 남이 뭘 골랐는지 보여 준다 |

용량은 `SessionLauncher.MaxPlayers`(4). `[Networked]` 배열 용량은 컴파일 타임 상수이고,
PlayerId 로 색인하는 것은 `SessionLobby` 의 닉네임 배열이 이미 쓰는 방식이다 — **슬롯 순서로
담으면 누가 나갈 때 나머지가 밀려 표가 어긋난다.**
| 결과 | `int` + 티켓 `byte` | 투표하고 아무것도 안 보이면 안 된다 |

**카드는 시드가 아니라 인덱스다.** 시드로 하면 같은 결과가 나오려면 `owned` 목록까지 같아야 하고,
그것도 복제해야 한다. `int` 세 개가 더 싸고 확실하다.

### 로드아웃은 복제하지 않는다

소비처 넷이 **전부 서버에서만 읽힌다**:

| 소비처 | 클라에서 도는가 |
|---|---|
| `GameManager` | ✗ `MissionNetHub` 가 끈다 |
| `RequestDirector` | ✗ `MissionNetHub` 가 끈다 |
| `PenguinLocomotion` | ✗ `PenguinNetAvatar.FixedUpdateNetwork` 가 `HasStateAuthority` 로 막는다 |
| `SnowGiftMachinePresentation` | 서버 경로 — 확인 항목으로 남긴다 |

루트 규약이 *"실제로 다른 플레이어가 관측하는 것만 동기화한다"* 이고, 누적 로드아웃은 아무도 안
본다. **팀이 지금까지 뭘 모았는지 보여 주는 화면이 생기면 그때 복제한다** — 그것이 그 결정을
뒤집을 트리거다.

---

## 7. 입력 — 마우스 클릭, 비트 셋

`EInputButton` 에 **뒤에 붙인다**: `AugmentPick0 = 14` · `AugmentPick1 = 15` · `AugmentPick2 = 16`.

⚠ **기존 값을 재사용하지 않는다.** 비트가 곧 와이어 포맷이라 다시 쓰면 옛 빌드와 의미가 어긋난다
(그 파일 `:44-46` 의 제설차 구멍 2~5 가 같은 이유로 비어 있다).

RPC 는 **못 쓴다** — 위버가 사용자 어셈블리에 `Fusion.Runtime` 의 internal `CheckInvokeRpc` 호출을
심는데 `InternalsVisibleTo` 에 우리가 없어 런타임에 `MethodAccessException` 이 난다(실측).

클릭 → 입력 비트로 옮기는 것은 **짧은 펄스**다. `CoopShoveInputRelay` 가 정확히 같은 모양의
선례다(0.2초 펄스, `NetworkInputData.cs:93`). `AugmentSelectionView` 의 `onPick` 콜백이 펄스를
세우고, `SessionLauncher.OnInput` 이 그것을 비트로 싣는다.

서버는 `Runner.TryGetInputForPlayer` 로 매 틱 표를 모은다 — `MissionNetHub.PollRestartRequests()`
와 같은 모양이다.

⚠ **비트가 셋이므로 카드 수는 3으로 고정된다.** `AugmentSelectionDirector._cardCount` 는 지금
`[SerializeField, Min(1)]` 라 4를 넣을 수 있는데, 그러면 넷째 카드에 실을 비트가 없어 **멀티에서만
조용히 못 고르는 카드**가 생긴다. 상한을 3으로 좁히고 그 이유를 필드 툴팁에 적는다. 늘리려면
비트를 뒤에 더 붙이는 것이지 기존 셋을 재해석하는 것이 아니다.

**싱글은 입력 비트를 타지 않는다.** 러너가 없어 `TryGetInputForPlayer` 가 아예 없다. 뷰의 `onPick`
콜백이 디렉터를 직접 부른다 — 지금 동작 그대로다. 즉 **비트는 클라이언트가 서버에게 말하는
경로일 뿐**이고, 권위가 표를 받는 자리는 싱글·멀티가 같은 함수여야 한다.

---

## 8. 집계 규칙

- **최다 득표.** 동점이면 **서버가 무작위로 고른다.** 결정론이 깨지지 않는다 — 뽑는 것은 서버뿐이고
  결과만 복제되기 때문이다.
- **미투표는 분모에서 뺀다.** 기다리지 않는다.
- **전원 기권이면 무작위 1장을 준다.** *"의뢰를 하나도 못 해도 반드시 한 번은 받는다"* 가 현재
  설계 의도다(`Augment/AGENTS.md`). 기권으로 그것을 깨지 않는다.
- **중간에 나간 사람의 표는 뺀다.** `Runner.ActivePlayers` 에 없으면 안 센다.
- **창은 둘 중 먼저 오는 쪽에 닫힌다** — 전원이 표를 냈거나, 쉬는 시간이 끝났거나.
  그래서 **싱글에서는 클릭이 곧 확정**이고 이것은 지금 동작 그대로다. 멀티에서는 마지막 사람이
  낼 때까지 표를 **바꿀 수 있다** — 첫 클릭으로 잠그면 오클릭이 판을 정한다.
  ⚠ 전원 투표로 닫히면 **쉬는 시간도 같이 끝난다.** 쉬는 시간은 고르는 시간이지 별도의 휴식이 아니다.
- **판이 끝났으면 열지 않는다.** `GameManager.Phase` 가 진행 중일 때만 연다. 결과 화면 위로 카드가
  뜨면 정렬 순서와 커서가 동시에 엉킨다 — `MissionNetHub.PollRestartRequests()` 가 `Ended` 를
  확인하는 것과 같은 종류의 가드다. 이미 열려 있는데 판이 끝나면 **집계하지 않고 닫는다.**

---

## 9. 커서와 화면

투표가 열리는 동안 **`PenguinCameraOrbit` 을 끈다.** 그쪽 `OnDisable` 이 커서 잠금을 푼다 —
일시정지 메뉴가 쓰는 그 수단이고, 커서를 직접 만지지 않는 이유도 같다(cs:907).

대가: 커서가 풀린 동안 **카메라 회전이 죽는다.** 이동은 살아 있다. 쉬는 시간이라 받아들인다.

패널 정렬은 증강 50 · 일시정지 40 그대로다(cs:917).

---

## 10. 싱글에서 무엇이 바뀌는가

| | 전 | 후 |
|---|---|---|
| 시간 | `timeScale = 0` 으로 물리까지 정지 | 계속 돈다 |
| 입력 | `PenguinInputReader.enabled = false` | 계속 받는다 |
| 의뢰 | 정지에 묶여 같이 멈춤 | 스폰·TTL 만 멈추고 완료는 된다 |
| 고르기 | 무제한 | 20초 뒤 자동 집계(1인이므로 자기 표가 곧 결과) |

**싱글에서 20초 안에 안 고르면 무작위 1장**이 된다. 이것이 이 스펙에서 싱글 플레이어가 체감하는
가장 큰 변화이고, 의도한 것이다 — 두 모드가 같은 코드로 돌게 하는 값이 그 비용보다 크다.

---

## 11. 검증

| | |
|---|---|
| EditMode | 집계를 **순수 static** 으로 빼서 전부 덮는다 — 최다 · 동점 · 기권 · 전원 기권 · 이탈자 제외 · 표 변경 |
| EditMode | 쉬는 시간 타이머(시작·만료·이중 시작 방지) |
| PlayMode | 쉬는 시간 동안 스폰 0 · TTL 불변 · 완료는 통과 |
| 실측 2인스턴스 | 양쪽에 같은 카드 3장이 뜨는가 · 다수결이 먹는가 · 결과가 양쪽에 보이는가 · 커서가 풀리고 다시 잠기는가 |

⚠ **PlayMode CLI 수집기는 에디터 세션당 한 번**이다. 배치를 아껴 짠다.
⚠ **MPPM `host` 태그를 지우고 끝낸다.** 남으면 러너가 `DontDestroyOnLoad` 라 SinglePlay 까지
세션이 따라오고 증상이 한 씬 떨어져 나온다.

---

## 12. 이 스펙이 깨뜨리는 것

정지를 걷어내면 **그것을 단언하는 기존 테스트가 깨진다.** 미리 세어 둔다 —
`Assets/Game/InGame/Augment/Tests/PlayMode/AugmentSelectionPlayModeTests.cs`:

| 줄 | 지금 단언 | 어떻게 바꾸나 |
|---|---|---|
| `:63` | 열면 `timeScale == 0f` | **쉬는 시간이 시작됐는가**로 바꾼다 |
| `:80` | 확정하면 `timeScale == 1f` | **쉬는 시간이 끝났는가**로 바꾼다 |
| `:135` | 같음 | 같음 |
| `:12,15,20` | `_timeScaleBefore` 저장·복원 | 지운다 — 아무도 `timeScale` 을 안 만진다 |

⚠ **이 셋은 "고장난 테스트" 가 아니라 "낡은 전제 위의 테스트" 다.** 지우지 말고 새 전제로 다시
쓴다 — 정지가 하던 일을 쉬는 시간이 대신하는지 확인하는 것이 이 작업의 핵심 단언이다.

`PenguinInputReader.enabled` 를 단언하는 곳이 있으면 같이 본다. 정지가 사라지면 입력은 계속 켜진다.

---

## 13. 범위 밖 · 열린 것

- **중간 참가.** `MissionNetHub` 가 `ExpectedPlayerCount` 를 매치 시작에 잠그는 전제와 정면으로
  부딪힌다. 투표 중에 들어온 사람을 어떻게 셀지는 그 설계가 선 뒤에 정한다.
- **누적 로드아웃 화면.** 만들면 §6 의 "복제하지 않는다" 를 뒤집는다.
- **선물(`Gift`) 운반 지터.** `/main/snowball-net-carry` 의 형제 문제이고 이 스펙과 무관하다.
- **`SnowGiftMachinePresentation` 이 서버에서만 도는지 미확인.** `Augment/AGENTS.md` 가 이미
  적어 둔 항목이다. 이 작업 중에 확인해서 결론을 그 파일에 남긴다.
