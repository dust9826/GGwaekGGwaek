# InGame/Augment — 런 증강

하루가 넘어갈 때 카드 3장을 띄우고 하나를 고르게 한다. 고른 효과는 그 판이 끝날 때까지 누적된다.

설계는 `docs/specs/2026-08-31-run-augments.md`(증강 자체)와
`docs/specs/2026-09-01-augment-vote-and-intermission.md`(투표와 쉬는 시간).
화면은 `InGame/UI/AugmentSelect/` 가 소유한다.

## 이 폴더가 가진 것

| | |
|---|---|
| `EAugmentStat` · `AugmentEffect` | 효과 축 다섯과 `{스탯, 값}` |
| `AugmentDefinition` · `AugmentPool` | 증강 한 장과 추첨 후보 목록 (SO) |
| `AugmentLoadout` | 이 판이 얻은 것 전부 + 스탯별 합산 |
| `AugmentDraft` | 가중치 추첨. 순수 static |
| `AugmentSelectionDirector` | 트리거 · 권위 게이트 · 쉬는 시간 · 표 · 확정 |
| `AugmentVoteTally` | 표 집계. 순수 static |
| `AugmentNetHub` · `AugmentNetSpawner` | 복제와 스폰 |
| `Data/` | 카드 6장과 기본 풀 |
| `Tests/` | EditMode 21 · PlayMode 14 · 검증 씬 |

## 값은 언제나 가산 누적이다

`GetValue(stat)` 은 0에서 시작해 획득한 증강의 값을 전부 더한다. 배율로 쓰는 쪽은
`GetMultiplier(stat)`(= `1 + 값`)을 읽고, **확률 축은 `GetValue` 를 그대로** 읽는다.

곱셈 누적이나 연산 종류를 데이터에 들고 다니는 방식을 쓰지 않았다. 덧셈 하나면 스택도 패널티도
같은 코드로 끝나고, 카드에 적힌 "+40%" 가 데이터의 `0.4` 와 그대로 대응한다.

**`GetMultiplier` 는 0에서 멈춘다.** 보상 −20% 짜리를 여섯 장 겹치면 −120% 가 되는데, 막지 않으면
의뢰를 완료할수록 점수가 깎인다.

## 이득과 패널티는 배열 둘로 나눠 적는다

부호로 좋고 나쁨을 추론하지 않는다. 그러려면 "이 스탯은 높을수록 좋다" 테이블이 따로 필요한데,
데이터 작성자가 `Benefits` / `Penalties` 로 명시하면 그 테이블이 통째로 사라진다. UI 는 색만 고른다.

## 팀 공유다 — 그리고 그것이 취향이 아닌 이유

증강은 **판 하나**에 붙는다(플레이어별이 아니다). 근거는 실측이다:

`RequestDirector.TickCompletions()` 는 매 틱 "집 앞 존에 맞는 상자가 놓여 있는가" 를 폴링해 완료로
친다. `GiftRequest` 에도 `RequestCompleted` 이벤트에도 **배달한 사람이 없다.** 그래서 per-player 로
두면 "보상 +40%" 를 곱할 대상이 없다. 가능하게 하려면 상자에 최종 운반자를 달아 완료 판정까지
끌고 가야 하고, 그것은 증강 작업이 아니라 배송 시스템 수술이다.

그럼에도 **`AugmentLoadout` 은 static 이 아니다.** 이 결정이 뒤집혀도 붙이는 위치만 바뀌고 코드는
그대로여야 한다. 같은 이유로 `AugmentDraft` 는 `AugmentLoadout` 이 아니라 `IReadOnlyList` 를 받는다 —
MonoBehaviour 를 인자로 받으면 순수 함수가 아니게 되고 EditMode 로 덮기 어려워진다.

## 트리거 — `DayIndex` 는 0에서 시작한다

`TimeOfDayDirector.DayAdvanced` 를 구독하고 **인덱스 조건은 없다.** `ResetToStart` 가 `DayIndex = 0`
으로 두므로 첫 넘어감이 `DayAdvanced(1)` 이고 그것이 화면상 2일차다. `>= 2` 를 걸면 첫 증강을
통째로 건너뛴다.

**첫 증강은 86.4초에 온다** — 하루 경계는 `NormalizedTime` 이 되감기는 순간이고
`StartTimeOfDay` 0.28 × `SecondsPerDay` 120 이다(실측으로 86초 확인). 시작 시간 풀이 120초이므로
**첫 장은 의뢰를 하나도 못 해도 받고**, 두 번째(206.4초)부터 시간을 벌어야 한다.
⚠ **하늘 연출 값이 증강 페이싱을 정하고 있다.** 한쪽을 튜닝하면 다른 쪽을 확인한다.

⚠ `TimeOfDayNetHub` 는 `DayIndex` 를 의도적으로 복제하지 않는다. 증강은 두 번째 서버 전용
소비자일 뿐이라 그 결정은 유효하다. 클라이언트가 "3일차" 를 화면에서 봐야 하는 요구가 생기면
그때 복제 결정을 뒤집고 근거를 남긴다.

## 아무것도 멈추지 않는다 — 대신 쉬는 시간

**2026-09-01 에 뒤집혔다.** 이 폴더는 원래 *"정지는 `AugmentSelectionDirector` 만 한다"* 였고
`Time.timeScale = 0` 과 `PenguinInputReader.enabled = false` 를 둘 다 잡았다. 그 절은 스스로를
**"갚아야 할 빚"** 으로 적어 뒀는데, 이제 갚았다.

**갚은 이유.** `timeScale` 은 프로세스 전역이라 서버가 남의 피어 것을 0으로 만들 수 없다. 서버가
할 수 있는 것은 "지금 투표 중" 플래그를 복제하는 것까지이고, 그것을 받아 각자 0을 걸면 원래
문제로 돌아온다 — 그 피어만 멈추고 세션은 계속 돌아 **재개하는 순간 자기 화면만 과거에 있다**
(2026-08-31 실측, 일시정지 메뉴가 같은 벽에 부딪혀 "멀티는 화면만 뜬다" 로 갔다).

**대신 쉬는 시간.** 별도 타이머(`_intermissionSeconds`, 기본 20초)를 두고 그동안:

| | |
|---|---|
| `RequestDirector.TickSpawns` | **안 돈다** — 새 의뢰가 없다. 이것이 "텀" 이다 |
| `RequestDirector.TickRequests` | **안 돈다** — TTL 정지 |
| `RequestDirector.TickCompletions` | 돈다 — 쉬는 중에 배달을 마치는 것은 허용한다 |
| `TimeOfDayDirector` · 펭귄 물리 | 돈다 |

게이트는 `RequestDirector` 가 이 디렉터를 **읽는** 방향이다. 참조가 비어 있으면 게이트가 없는
것과 같아서 증강을 안 놓은 씬과 테스트가 영향받지 않는다.

⚠ **클라이언트에서는 그 게이트가 아예 안 돈다** — `MissionNetHub.Spawned()` 가 클라에서
`RequestDirector` 와 `GameManager` 를 통째로 끈다. 쉬는 시간은 서버·싱글의 문제다.

**커서만 잡는다.** `PenguinCameraOrbit` 을 끄면 그쪽 `OnDisable` 이 잠금을 푼다 — 커서를 직접
만지지 않는 이유는 일시정지 메뉴와 같다. 대가는 그동안 카메라 회전이 죽는 것이다.

⚠ **`PauseMenuController` 의 "증강이 열려 있으면 ESC 를 안 연다" 가드는 남는다.** 이유가 바뀐다 —
전에는 `timeScale` 소유권이었고 지금은 **커서**다. 투표 중에 증강이 커서를 풀어 뒀는데 일시정지를
열었다 닫으면 그쪽이 다시 잠가 카드를 못 누른다.

## 투표 — 전원이 고르고 다수결

증강은 판 하나에 붙으므로(팀 공유) **선택도 팀이 한다.** 창은 **둘 중 먼저 오는 쪽**에 닫힌다 —
전원이 표를 냈거나, 쉬는 시간이 끝났거나. 그래서 **싱글에서는 클릭이 곧 확정**이고 이것은 예전
동작 그대로다. 멀티에서는 마지막 사람이 낼 때까지 바꿀 수 있다.

집계는 `AugmentVoteTally` 가 갖는다 — **순수 static 이라 규칙 전체가 EditMode 로 덮인다.**
최다 득표 · 미투표 제외 · 범위 밖 값 무시 · 나간 사람 제외 · 전원 기권이면 무작위 한 장 ·
동점이면 동점자 중에서 무작위. 동점을 무작위로 푸는 것이 결정론을 안 깨는 이유는 **부르는 쪽이
권위 하나뿐이고 결과만 복제되기** 때문이다. 가장 낮은 인덱스를 쓰지 않은 것은 그러면 첫 카드가
구조적으로 유리해져서다.

⚠ **카드는 3장이 상한이다.** `EInputButton` 의 `AugmentPick0/1/2` 비트가 셋뿐이라 넷째 카드는
**멀티에서만 조용히 못 고르게** 된다. 늘리려면 비트를 뒤에 더 붙이는 것이지 기존 셋을 재해석하는
것이 아니다. `_cardCount` 는 `Range(1, 3)` 으로 막아 뒀다.

## 로드아웃은 복제하지 않는다

`AugmentNetHub` 가 복제하는 것은 **열림 · 카드 3장의 풀 인덱스 · 표 · 이긴 카드**뿐이다.
누적 로드아웃은 안 보낸다 — 소비처 넷이 전부 서버에서만 읽히기 때문이다:

| 소비처 | 클라에서 도는가 |
|---|---|
| `GameManager` · `RequestDirector` | ✗ `MissionNetHub` 가 끈다 |
| `PenguinLocomotion` | ✗ `PenguinNetAvatar.FixedUpdateNetwork` 가 `HasStateAuthority` 로 막는다 |
| `SnowGiftMachinePresentation` | 서버 경로 |

**뒤집을 트리거는 하나다** — 팀이 지금까지 뭘 모았는지 보여 주는 화면이 생기면 그때 복제한다.

카드는 **시드가 아니라 인덱스**로 보낸다. 시드로 하면 결과가 같으려면 `owned` 목록까지 같아야
하고 그것도 복제해야 한다.

⚠ **표 배열의 색인은 `PlayerId` 이고 용량은 `SessionLauncher.MaxPlayerId`(8)다.**
`MaxPlayers`(4)가 아니다 — id 는 사람이 나가도 재사용되지 않아 동시 인원보다 커질 수 있고,
4로 잡으면 그 표가 **에러도 경고도 없이** 버려진다. `SessionLobby` 의 닉네임 배열이 같은 색인을
쓰고, 그래서 상수를 `SessionLauncher` 로 올렸다.

## 소비처는 넷이고 이 폴더 밖이다

| 스탯 | 어디서 | 언제 |
|---|---|---|
| `Reward` · `ClearTimeBonus` | `Cleanliness/GameManager` | 완료 시 |
| `RequestTtl` | `Delivery/RequestDirector` | 스폰 시 |
| `WalkSpeed` | `Penguin/PenguinLocomotion` | 매 스텝 |
| `ExtraGiftChance` | `Map/WinterVillage/SnowGiftMachinePresentation` | 변환 시 |

규칙은 **"값이 실제로 쓰이는 순간에 적용"** 하나다. 그래서 증강을 고르면 이미 떠 있던 의뢰의
보상도 즉시 오른다. 넷 다 `_augments` 가 비면 효과가 없고 **기존 동작 그대로**다 — 증강을 놓지
않은 씬과 테스트가 영향받지 않는 근거다.

### 건드리지 않기로 한 것 셋

- **`RequestBalance`** — "여기는 결정론적이다" 라는 문서화된 계약이 있다. 호출자가
  `RequestBalanceResult` 를 다시 만든다.
- **`SnowGiftMachinePresentation` 의 `_isNetworkConversion` 분기** — 파이프라인 스펙 §8 이 범위 밖으로
  미룬 자리다. `SpawnGift()` **호출 횟수만** 늘리므로 그 정리가 와도 이 코드는 따라올 필요가 없다.
- **`PenguinLocomotion._speedBoostMultiplier`** — `PenguinBoostReceiver` 가 만료 때 1로 되돌려 증강
  값을 지우고, `[1,3]` 클램프라 감속 패널티를 담지도 못한다. `SpeedMultiplier` 를 따로 뒀다.

⚠ **`WalkSpeed` 는 걷기·달리기에만 걸린다.** `SlideKickForceN` 과 `SlideTargetSpeedMps` 는 계속
부스트 패드 승수만 쓴다 — 카드가 약속하는 것이 "walk speed" 라서다. 이 게임은 슬라이딩이 주
이동이라 체감이 약할 수 있고, **활강까지 올리고 싶으면 그것은 별도 스탯이지 이 스탯의 확장이 아니다.**

## 표시 문자열은 영어다

`StageHUD`(SCORE·ORDERS)와 `StageOutro`(ROUTE COMPLETE)가 이미 그렇다. 로컬라이제이션 층은 세우지
않았다 — `com.unity.localization` 이 미설치이고 소비자가 증강 하나뿐이다. 대신 **`Id` 는 표시
문자열과 분리된 안정 키**이고 문구가 바뀌어도 바꾸지 않는다. 나중 테이블이 그것을 건다.

## 에셋을 만들 때

`.asset` YAML 을 직접 쓰지 않는다. `create_asset` 또는 `eval` 의 `AssetDatabase.CreateAsset` 으로
만들고 `SerializedObject` 로 값을 넣은 뒤 **되읽어 확인**한다.

## 검증 씬

`Tests/Augment_Selection_Test.unity` — 리그만 있고 `TimeOfDayDirector` 가 없다.
`OpenForTest()` 로 바로 띄운다. **Build Settings 에 넣지 않는다.**

⚠ **이 경고는 2026-09-01 에 근거가 사라졌다.** 원래는 *"`timeScale = 0` + 비포커스라 Game View 가
리페인트되지 않아 연속 캡처가 바이트까지 같다"* 였는데, 증강이 더 이상 시간을 멈추지 않는다.
비포커스 자체는 여전히 리페인트를 막을 수 있으므로, 캡처가 의심스러우면 유니티를 앞으로 올리거나
비주얼 트리를 직접 조회한다.

## 알려진 한계

- **2인스턴스 실측이 아직이다.** 구조와 배선은 되읽어 확인했지만 양쪽 화면에 같은 카드가 뜨는
  것을 눈으로 본 적은 없다.
- **중간 참가는 미정.** `MissionNetHub` 가 `ExpectedPlayerCount` 를 매치 시작에 잠그는 전제와
  부딪힌다. 투표 중에 들어온 사람을 어떻게 셀지는 그 설계가 선 뒤에 정한다.
- 추가 선물 두 개가 **같은 자리에서 나와** 물리로 서로 밀어낸다.
- 멀티에서 교환기의 그 자리가 서버에서만 도는지는 **확인되지 않았다.**
