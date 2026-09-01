# 접속 중 화면 — 로비로 가기 전에 한 단계

날짜: 2026-08-30 · 브랜치: `/main/multiplay-gift-machine`

## 1. 무엇을 만드는가

HOST 또는 JOIN 을 누른 뒤 **로비로 바로 넘어가지 않고** "방을 만드는 중 / 들어가는 중" 을 보여 주는
화면을 하나 끼운다. 성공하면 로비로 넘어가고, 실패하면 그 화면에 실패와 이유가 남는다.

## 2. 지금 무엇이 문제인가

`OutGameScreenController` 는 결과를 알기 **전에** 화면을 옮긴다.

```csharp
SwitchView("lobby");     // 먼저 넘어가고
JoinRoomAsync(code);     // 그 다음에 붙어 본다
```

그래서 붙는 데 실패해도 사람은 **로비 화면에 앉아 있다.** 방 코드가 보이고 슬롯이 보이는데 아무도
안 들어온다 — 실패한 것인지 기다리는 것인지 화면으로 구분할 수 없다. 2026-08-30 에 보고된
"가끔 접속이 안 된다" 의 절반은 이 화면 설계였다(나머지 절반인 단발 시도는 `cs:874` 에서 고쳤다).

실패 문구가 나갈 자리도 사실상 없다. `SetStatus` 는 **활성 뷰 안의** `.status-label` 을 찾으므로,
로비에 앉은 채로 받은 실패 메시지는 로비의 라벨에 적힌다 — 로비에 있다는 사실 자체가 이미
"성공했다" 는 신호라 서로 어긋난다.

## 3. 화면 흐름

```
view-join ──JOIN──┐                        ┌── 성공 ──> view-lobby
                  ├──> view-connecting ────┤
view-host ──HOST──┘                        └── 실패 ──> view-connecting 에 머무름
                                                        (실패 문구 + BACK)
```

- **BACK 은 실패한 뒤에만 보인다.** 시도 중에는 없다(§6 참조).
- **BACK TO MENU 는 메인 화면(`view-home`)으로 간다**(2026-08-30 결정). 처음에는 왔던 화면으로
  돌려보내 코드를 다시 안 치게 했는데, 실패한 뒤에는 코드가 맞는지 자체가 의심스러우므로
  메인에서 다시 고르는 쪽이 맞다.
- 성공 경로는 `view-lobby` 다. 메인(`view-home`)이 아니다.

## 4. 문구 (영어)

| 상태 | 문구 |
|---|---|
| 방 만드는 중 | `CREATING ROOM...` |
| 들어가는 중 | `JOINING ROOM...` |
| 들어가는 중(재시도) | `JOINING ROOM - {n}/8` |
| 방 만들기 실패 | `COULD NOT CREATE ROOM` + 아래 이유 |
| 들어가기 실패 | `COULD NOT JOIN ROOM` + 아래 이유 |

이유는 `SessionLauncher.LastStartFailure`(`cs:874`)를 사람이 읽는 문장으로 옮긴다. **원인마다 다음에
할 일이 다르므로 뭉뚱그리지 않는다.**

| `LastStartFailure` | 화면 문구 |
|---|---|
| `GameNotFound` | `ROOM NOT FOUND - CHECK THE CODE OR WAIT FOR THE HOST` |
| `Fusion 설정을 읽지 못했다` | `EDITOR ISSUE - SEE CONSOLE` |
| 그 밖 | 값을 그대로 붙인다 |

## 5. 어디에 만드는가

**`MainMenu.uxml` 안에 `view-connecting` 을 하나 더 만든다.** 뷰는
`OutGameScreenController` 가 `view-` 로 시작하는 자식을 훑어 자동 등록하므로(`ViewElementPrefix`),
UXML 에 넣는 것만으로 `SwitchView("connecting")` 이 동작한다. 등록 코드는 건드리지 않는다.

**필수 요소 둘.**

- `.status-label` 클래스를 가진 라벨 — 없으면 `SetStatus` 가 **조용히 아무 일도 안 한다**(활성 뷰
  안에서만 찾는다). 이 스펙의 문구가 전부 여기로 나간다.
- `name="action-connecting-back"` 버튼 — 실패 전에는 `display: none`.

**팀원의 `LoadingScreen.unity` 를 쓰지 않는다.** `Core/Multiplay/AGENTS.md` 에 이미 적힌 결정이다 —
그 씬은 `SceneManager.LoadScene` 으로 도는 싱글플레이 연출이라 네트워크 씬 권위와 싸운다. 여기서
필요한 것은 씬이 아니라 **메인 메뉴 안의 한 화면**이다.

## 6. 기각한 것

- **시도 중 취소 버튼.** 넣지 않는다. 최악이 6초 남짓(재시도 8회 × 0.7초)이고, 취소는 반쯤 시작된
  러너를 내리는 경로를 새로 만든다 — 세션 버그가 사는 자리다. 길어지면 그때 다시 본다.
- **진행 바.** 넣지 않는다. 얼마나 남았는지 모르면서 아는 척하는 것이다.
- ~~**로딩 스피너.**~~ **2026-08-30 에 넣기로 바꿨다.** 처음에는 진행 바와 묶어 기각했는데 그것이
  틀렸다 — 스피너는 **남은 양을 말하지 않는다.** "아직 살아 있다" 만 알리므로 거짓말을 하지 않고,
  그 정보는 재시도 횟수가 대신 주지 못한다(횟수는 2초에 한 번 바뀌어서 그 사이 화면이 멎어 보인다).
  UI Toolkit 에는 `@keyframes` 가 없으므로 USS 는 모양만 주고 회전은 코드가 돌린다.
- **`ESessionPhase.Matchmaking` 을 구독해서 화면을 바꾸는 것.** 매력적이지만 안 한다 — 그 단계는
  자동 접속(MPPM 태그)에서도 지나가므로, 화면 전환을 거기 걸면 사람이 안 누른 경우에도 화면이
  움직인다. **버튼을 누른 흐름만 이 화면을 띄운다.**

## 7. 합격 기준

1. HOST 를 누르면 로비가 아니라 `view-connecting` 이 먼저 뜨고 `CREATING ROOM...` 이 보인다.
2. JOIN 을 누르면 `JOINING ROOM {CODE}...` 가 보이고, 재시도마다 `{n}/8` 이 올라간다.
3. 성공하면 `view-lobby` 로 넘어간다.
4. 실패하면 `view-connecting` 에 남고, 실패 문구와 §4 의 이유가 함께 보이며, BACK 이 나타나
   왔던 화면(`view-join` / `view-host`)으로 돌아간다.
5. `OutGameUiContractTests` 에 새 요소 둘(`.status-label`, `action-connecting-back`)을 더하고
   통과한다 — 그 테스트가 "코드가 이름으로 찾는 요소는 UXML 에 다 있다" 를 지킨다.

4 번은 없는 방 코드를 넣어 재현할 수 있다(`cs:874` 검증에서 쓴 방법 그대로).

## 8. 이후 변경 (2026-08-30)

- **방 코드를 문구에서 뺐다.** 코드는 사람이 방금 입력한 값이라 다시 보여 줄 이유가 약하고,
  `JOINING ROOM...` 쪽이 짧아 읽힌다.
- **도는 고리를 넣었다.** §6 의 기각을 뒤집은 것이며 근거는 그 항목에 적었다. 실패하면 멈추고
  감춘다 — 도는 채로 두면 아직 시도 중이라는 뜻이 된다.

## 9. 실패 표시 (2026-08-30 추가)

"BACK 버튼만 보인다" 는 보고를 받고 고쳤다. 실패 이유가 배너 부제로만 나가서 사람이 그냥
지나쳤다 — 평소 안내와 같은 모양이면 실패가 안내처럼 읽힌다.

- 이유를 **상태 라벨에도** 적고 `.status-label.error` 로 붉게 만든다.
- 배너 부제는 "TAP BACK TO MENU AND TRY AGAIN" 으로 다음 행동을 말한다.
- 버튼 문구는 `BACK` → `BACK TO MENU`.

**그리고 시도 중 상태 줄이 제목과 어긋나던 것을 고쳤다.** `OnSessionPhaseChanged` 가 상태 줄을
덮어써서, 제목이 `JOINING ROOM - 2/8` 인데 아래에 `SESSION CLOSED` 가 떴다. 재시도가 매번 러너를
세웠다 내리므로 `Offline` 이 정상적으로 여러 번 지나가는데, 그 문구는 사람에게 "끝났다" 로 읽힌다.
접속 중 화면이 떠 있는 동안에는 그 핸들러가 상태 줄을 건드리지 않는다.
