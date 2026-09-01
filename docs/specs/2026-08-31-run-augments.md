# 런 증강 — 일차마다 3장 중 1장

날짜: 2026-08-31 · 브랜치: `/main/run-augments`

일차가 넘어갈 때 증강 3장을 띄우고 하나를 고르게 한다. 고른 효과는 그 판이 끝날 때까지 누적된다.
이 문서는 **시스템**을 정한다 — 카드 목록은 첫 여섯 장만 두고, 늘리는 것은 데이터 작업이다.

`docs/specs/2026-08-31-single-multi-pipeline.md` 의 규약 위에 선다. 그 문서 §11 의 열린 결정 1번
**"증강이 per-player 인가 파티 공용인가"** 를 **파티 공용**으로 닫는 것이 이 문서다.

## 1. 범위

| | |
|---|---|
| 모드 | `SinglePlay` 에서 돈다. 멀티 배선(투표·복제)은 이번 범위 밖 |
| 시점 | **매 일차 시작마다** — 하루가 넘어갈 때마다. 첫 회는 2일차 시작(§3) |
| 형태 | 3장 중 1장. 고르는 동안 완전 정지 |
| 효과 | 대표 5개 스탯 |
| 소유 | **판 하나** — 팀 공유 |

## 2. 소유와 권위 — 왜 팀 공유인가

사용자 결정(2026-08-31): **팀 공유 증강, 멀티에서는 투표형.** 근거는 취향만이 아니라 구조다.

### 의뢰에는 완료자가 없다 (실측)

`RequestDirector.TickCompletions()` 는 매 틱 **"집 앞 존에 맞는 상자가 놓여 있는가"** 를 폴링해
완료로 친다. `GiftRequest` 에도 `RequestCompleted` 이벤트에도 배달한 사람이 없고, 상자를 거기
누가 뒀는지 기록하는 곳도 없다.

따라서 **per-player 로 두면 "보상 +40%" 를 계산할 수 없다** — 누구 배율을 곱할지 물어볼 대상이
없다. 가능하게 하려면 상자에 최종 운반자를 달아 완료 판정까지 끌고 가야 하고, 그것은 증강 작업이
아니라 배송 시스템 수술이다.

### 고른 효과의 소비처가 이미 판 단위다 (실측)

| 스탯 | 소비처 | 소유 단위 |
|---|---|---|
| `Reward` · `ClearTimeBonus` | `GameManager` | 판 하나 |
| `RequestTtl` | `RequestDirector` | 판 하나 |
| `ExtraGiftChance` | `SnowGiftMachinePresentation` | 월드 오브젝트 하나 |
| `WalkSpeed` | `PenguinLocomotion` | 플레이어마다 |

5개 중 4개가 판/월드 단위다. 데이터가 팀 공유를 가리킨다.

### 권위는 `StageSession` 이 답한다

**추첨과 확정은 `StageSession.IsAuthority` 에서만 돈다.** 싱글에서는 항상 참이라 지금 동작은
그대로이고, 멀티가 붙는 날 게이트가 이미 맞게 서 있다. 파이프라인 규약 §3 의 "신규 코드는 규약을
따른다" 를 지키는 형태다. `StageSession.For(gameObject)` 는 한 번 받아 캐시한다.

### 빌더 수정은 0이다

팀 공유이므로 `AugmentLoadout` 과 `AugmentSelectionDirector` 는 **`SinglePlay` 씬의 일반
컴포넌트**다. 파이프라인 규약 §6 에 따라 빌더 코드 수정 없이 스탬핑으로 `MultiPlay` 에 따라온다.
빌더에 한 줄이 필요해지는 것은 나중에 `[Networked]` 복제 리그를 세울 때다.

## 3. 트리거

`TimeOfDayDirector.DayAdvanced(int dayIndex)` 를 구독한다. **모든 `DayAdvanced` 가 대상이고 인덱스
조건은 없다.**

**`DayIndex` 는 0에서 시작한다**(`ResetToStart`). 첫 넘어감이 `DayAdvanced(1)` 이고, 그것이 화면상
2일차의 시작이다. `dayIndex >= 2` 같은 조건을 걸면 **첫 증강을 통째로 건너뛴다.**

### 첫 증강은 언제 오는가 (실측)

하루 경계는 `NormalizedTime` 이 되감기는 순간이다. `StartTimeOfDay` = 0.28, `SecondsPerDay` = 120
이므로 **첫 넘어감은 시작 86.4초 뒤**이고, 그 뒤로는 120초마다다.

`StageBalanceConfig.StartSeconds` = 120초, `MaxSeconds` = 0(상한 없음). 따라서:

- **첫 증강은 의뢰를 하나도 못 해도 받는다** (86.4초 < 120초). 튜토리얼처럼 반드시 한 번은 본다.
- **두 번째는 206.4초라 시간을 벌어야 도달한다.** 증강이 보상이 되는 것은 여기서부터다.

`StartTimeOfDay` 를 바꾸면 첫 증강 시점이 함께 움직인다. 하늘 연출 값이 증강 페이싱을 정하고
있다는 뜻이므로, 어느 한쪽을 튜닝할 때 다른 쪽을 확인한다.

⚠ **`TimeOfDayNetHub` 는 `DayIndex` 를 의도적으로 복제하지 않는다.** 읽는 곳이
`StageDateCoordinator` 하나뿐이고 서버 전용이라서다(파이프라인 규약 §7). 증강이 두 번째
서버 전용 소비자가 되는 것뿐이라 그 결정은 그대로 유효하다. **클라이언트가 "3일차" 를 화면에서
봐야 하는 요구가 생기면 그때 복제 결정을 뒤집어야 하고, 근거를 남겨야 한다.**

배선은 `AugmentSelectionDirector` 가 `TimeOfDayDirector` 를 직렬화 참조로 받아 구독한다. `Map` 은
`Augment` 를 모르므로 단방향이고, `Cleanliness` 는 건드리지 않는다. 기존 `StageDateCoordinator` 와
같은 모양이되 관심사가 달라 파일을 나눈다.

## 4. 데이터

`Assets/Game/InGame/Augment/`

```
EAugmentStat        ClearTimeBonus · RequestTtl · Reward · WalkSpeed · ExtraGiftChance
AugmentEffect       { EAugmentStat Stat, float Value }        순수 struct
AugmentDefinition   SO: Id, DisplayName, Description, Benefits[], Penalties[], Weight
AugmentPool         SO: AugmentDefinition[]
```

**모든 값은 가산 누적이다.** `Loadout.GetValue(stat)` 은 0에서 시작해 획득한 증강의 값을 전부
더한다. 배율로 쓰는 쪽은 `1f + GetValue(...)`, 확률로 쓰는 쪽은 `GetValue(...)` 를 그대로 쓴다.
스택도 패널티도 덧셈 하나로 끝나 별도 구조가 없다.

**이득과 패널티를 배열 둘로 나눈 이유는 UI 다.** 부호로 판정하려면 "이 스탯은 높을수록 좋다"
테이블이 따로 필요한데, 데이터 작성자가 명시하면 그 테이블이 통째로 사라진다.

## 5. 런타임

| 타입 | 책임 |
|---|---|
| `AugmentLoadout` (MonoBehaviour) | 획득 목록 소유, `GetValue(stat)` 합산, `Changed` 이벤트 |
| `AugmentDraft` (순수 static) | 가중치 추첨 3장. 보유분 제외, 시드 주입 가능 |
| `AugmentSelectionDirector` (MonoBehaviour) | `DayAdvanced` 구독 → 게이트 → 추첨 → 정지 → UI → 확정 → 재개 |

`AugmentLoadout` 은 **static 이 아니다.** 지금은 판에 하나지만, 그 결정이 뒤집혀도 붙이는 위치만
바뀌고 코드는 그대로다.

풀이 모자라면 있는 만큼만 띄운다. 0장이면 화면을 열지 않고 그냥 넘어간다.

## 6. 정지

`Time.timeScale = 0f`.

실측으로 확인했다 — `GameManager` 는 `FixedUpdate`, `RequestDirector` 는 `Time.fixedDeltaTime`,
`TimeOfDayDirector` 는 `Time.deltaTime` 이라 셋 다 스케일을 탄다. 물리도 함께 멈춘다. 입력은 안
멈추므로 `PenguinInputReader` 를 선택 중 끈다.

⚠ **이것은 갚아야 할 빚이다.** `timeScale` 은 피어 로컬이라 멀티에서 그대로 쓰면 "싱글과 권위가
같은 코드로 돈다" 가 깨진다. 대안은 각 디렉터의 `Pause()` 인데(`TimeOfDayDirector.Pause()` 는
이미 있다) 그러면 물리가 안 멈춰 미끄러지던 펭귄이 계속 미끄러진다 — 사용자가 고른 "완전 정지"
가 아니게 된다.
**정지·재개 호출을 `AugmentSelectionDirector` 한 파일에만 두어, 멀티 전환이 그 파일 하나
교체가 되게 한다.**

## 7. 효과가 꽂히는 곳 — 5군데

| 스탯 | 지점 | 적용 시점 |
|---|---|---|
| `RequestTtl` | `RequestDirector` — `RequestBalance.Evaluate` 결과를 스케일 | 의뢰 스폰 시 |
| `Reward` | `GameManager.NotifyRequestCompleted` | 완료 시 |
| `ClearTimeBonus` | 〃 | 완료 시 |
| `WalkSpeed` | `PenguinLocomotion` | 매 스텝 |
| `ExtraGiftChance` | `SnowGiftMachinePresentation.SpawnGift()` | 변환 시 |

규칙은 **"값이 실제로 쓰이는 순간에 적용"** 하나다. TTL 은 타이머가 그때 시작하므로 스폰 시,
보상과 추가시간은 그때 지급되므로 완료 시다. 그래서 증강을 고르면 **이미 떠 있는 의뢰의 보상도
즉시 오른다.**

각 소비처는 `AugmentLoadout` 을 직렬화 필드로 받는다. **비어 있으면 효과가 없고 기존 동작 그대로**
이므로, 증강을 놓지 않은 씬과 테스트가 영향받지 않는다.

### 건드리지 않는 것들

- **`RequestBalance` 는 그대로 둔다.** "여기는 결정론적이다" 라는 문서화된 계약이 있다. 곱하는
  것은 호출자 쪽 한 줄로 한다.
- ⚠ **`SnowGiftMachinePresentation` 의 `_isNetworkConversion` 분기는 건드리지 않는다.**
  파이프라인 규약 §8 이 "두 경로를 합치는 것은 범위가 번지므로 기록만 남기고 손대지 않는다" 로
  못 박은 자리다. 증강은 **`SpawnGift()` 호출 횟수만 늘린다** — 확률에 걸리면 한 번 더 부른다.
  분기 구조를 그대로 두므로 나중에 합칠 때 이 코드는 따라올 필요가 없다.
- ⚠ **`PenguinLocomotion._speedBoostMultiplier` 를 재사용하지 않는다.** 소유자가
  `PenguinBoostReceiver`(부스트 패드)이고 만료 때 `1f` 로 되돌리므로 증강 값이 지워진다. `[1,3]`
  클램프라 감속 패널티도 표현되지 않는다. **별도 승수 필드를 하나 더 둔다.**

## 8. UI

`Assets/Game/InGame/UI/AugmentSelect/` — 형제인 `StageHUD`·`StageOutro` 와 같은 **UI Toolkit**
구성(`.uxml` + `.uss` + `PanelSettings` + `Scripts` + `Tests` + `README.md`).

카드 3장에 이름·설명·이득(초록)·패널티(빨강). 클릭으로 확정한다.

## 9. 표시 문자열은 영어다

사용자 결정(2026-08-31): **카드에 뜨는 이름·설명은 영어로 쓴다.**

기존 UI 가 이미 그렇다(실측) — `StageHUD` 는 `SCORE` · `ORDERS` · `NEXT REQUEST...`,
`StageOutro` 는 `ROUTE COMPLETE` · `WINTER VILLAGE` · `SNOW ROUTE SERVICE REPORT` 다. 한국어는
눈폭풍 경고 한 줄뿐이다. 영어로 쓰는 것이 새 규칙이 아니라 **이미 있는 규약을 따르는 것**이다.

### 로컬라이제이션은 이번 범위 밖이다

`com.unity.localization` 은 **설치돼 있지 않다**(실측). 지금 층을 세우면 소비자가 증강 하나뿐인
채로 만들어진다 — 루트 AGENTS.md 의 "두 번째 호출 지점 확인 전에는 추상화 금지" 에 걸린다.

**대신 나중에 막히지 않을 것만 지금 지킨다:**

- **`Id` 는 표시 문자열과 분리된 안정 키다.** 나중 로컬라이제이션 테이블이 이것을 건다.
- **`Id` 는 문구가 바뀌어도 바꾸지 않는다.** 이름·설명은 튜닝 대상이지만 `Id` 는 아니다.
- 표시 문자열은 `AugmentDefinition` 필드에 그대로 둔다. 테이블 조회로 바꾸는 날 필드 둘이
  키로 바뀌고, 그 변경은 이 SO 한 곳에서 끝난다.

## 10. 첫 카드 여섯 장

**문구는 첫 안이고 구현 뒤에 다듬는다**(사용자 결정 2026-08-31). SO 데이터라 문구 수정에 코드
변경이 0 이므로, 실제로 화면에서 보고 고치는 편이 낫다. 아래는 그 출발점이다.

| `Id` | 표시 이름 | 설명 | 이득 | 패널티 |
|---|---|---|---|---|
| `rush_order` | Rush Order | Bigger payouts, tighter clocks. | 보상 +40% | 의뢰 제한시간 −20% |
| `slick_feet` | Slick Feet | Move faster, earn less time per delivery. | 걷기 속도 +25% | 클리어 추가시간 −15% |
| `extended_deadline` | Extended Deadline | More time per order, smaller payouts. | 의뢰 제한시간 +30% | 보상 −15% |
| `double_wrap` | Double Wrap | The machine sometimes pops out a second gift. | 추가 선물 확률 +30%p | 걷기 속도 −10% |
| `overtime` | Overtime | Deliveries pay in time instead of score. | 클리어 추가시간 +25% | 보상 −20% |
| `basic_training` | Basic Training | A small, clean speed bump. | 걷기 속도 +10% | 없음 |

숫자도 시작값이다. SO 라 플레이 중에도 만질 수 있고 그 값이 남는다.

## 11. 검증

**EditMode**

- 추첨 — 3장, 중복 없음, 보유분 제외, 풀 고갈 시 있는 만큼, 시드 고정 시 재현
- 합산 — 스택 누적, 패널티 상쇄, 빈 로드아웃이 0

**PlayMode**

- `DayAdvanced` 발생 → 화면이 뜨고 `Time.timeScale == 0`
- 확정 → `timeScale` 복구, 보상과 걷기 속도에 실제 반영

**테스트 씬** — `Assets/Game/InGame/Augment/Tests/Augment_Selection_Test.unity`.
**Build Settings 에 넣지 않는다.**

## 12. 채택하지 않은 것

- **전역 상태 하나(`RunModifiers` static).** 가장 짧지만 소유 결정이 뒤집히는 날 층을 통째로 다시
  쓴다. 아직 정하지 않아도 되는 것을 지금 정해 버리는 안이다.
- **증강마다 로직 스크립트(`Apply(owner)` 파생).** 확률형에는 자연스럽지만 스탯 합산형이
  어색해지고 증강 하나에 클래스가 하나씩 는다. 루트 AGENTS.md 의 "두 번째 호출 지점 확인 전에는
  추상화 금지" 에 걸린다.
- **스탯마다 소유자를 다르게(혼합).** 카드 한 장에 판 효과와 개인 효과가 섞이면 UI 도 규칙도
  복잡해진다. 같은 이유로 위와 함께 기각.
- **눈덩이 밀기 중 질주 확률.** 사용자가 예시로 든 효과지만 **밀기 중 질주라는 게임플레이가 아직
  없다** — 시스템 작업이 아니라 새 기능 제작이 된다. 시스템이 서고 나면 스탯 하나로 붙는다.

## 13. 열린 결정

1. **멀티 투표의 형태.** 과반인지 만장일치인지, 갈리면 어떻게 정하는지.
2. **선택 제한시간과 만료 처리.** 사용자 언급으로는 "제한시간이 지나면 랜덤 선택". 싱글은 완전
   정지라 필요가 없어 v1 에 넣지 않는다. 멀티 배선과 함께 정한다.
3. **인원 조건부 증강.** `StageSession.PlayerCount` 가 이미 답할 수 있다. 수치는 정하지 않는다.
4. **일차별 이벤트와의 관계.** 같은 `DayAdvanced` 를 두 시스템이 구독한다. 순서 보장이 필요해지면
   그때 정한다.
5. **로컬라이제이션 도입 시점.** 사용자 확인(2026-08-31): **설치는 할 예정이고, 지금은 아니다.**
   증강은 §9 대로 영어 문자열을 필드에 그대로 두고 간다. 들어오는 날 바꿀 것은
   `AugmentDefinition` 의 표시 필드 둘이 키가 되는 것뿐이고, `Id` 는 그때도 그대로다.

## 14. 용어

`docs/Glossary.md` 에 등록한다 — 증강 / `Augment`, 로드아웃 / `AugmentLoadout`,
증강 추첨 / `AugmentDraft`.
