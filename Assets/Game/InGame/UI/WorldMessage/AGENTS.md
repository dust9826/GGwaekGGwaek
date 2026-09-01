# InGame/UI/WorldMessage — 게임 중 짧게 뜨는 알림

화면 위쪽에 한 줄씩 잠깐 뜨는 토스트. **무엇을 알릴지는 이 폴더가 정하지 않는다.**

```csharp
WorldMessagePresenter.Post("PLAYER 2 LEFT");
```

## 소유하는 것

| | |
|---|---|
| `Scripts/WorldMessageQueue.cs` | 순서와 시간만. **유니티도 UI 도 모른다** — EditMode 로 전부 덮는다 |
| `Scripts/WorldMessagePresenter.cs` | 그리는 쪽. 정적 `Post` 창구를 연다 |
| `Scripts/PlayerLeftAnnouncer.cs` | 첫 소비자. 세션의 "빠졌다" 를 문장으로 바꾼다 |
| `WorldMessage.uxml` · `.uss` · `WorldMessagePanelSettings.asset` | `sortingOrder = 20` — HUD(0) 위, 결과 화면(30) 아래 |

씬에는 `SnowDeliveryRig/WorldMessageUI` 로 `SnowDeliverySceneBuilder` 가 붙인다. **손으로 넣지
않는다** — 그 빌더가 `SinglePlay` 를 매번 다시 만들므로 손으로 넣은 것은 다음 실행에 사라진다.

## 경계 — `Core` 는 사실만 알린다

```
SessionLauncher.PlayerLeft (Core)  →  PlayerLeftAnnouncer (InGame)  →  Post()  →  Presenter
```

`Core` 는 `InGame` 을 참조할 수 없지만, 그 전에 **세션 계층이 화면을 알아야 할 이유가 없다.**
"빠졌다" 는 사실이고 그것을 토스트로 볼지는 읽는 쪽이 정한다 — `PhaseChanged` 와 같은 방향이고,
`StageOutroPresenter` 가 `Cleanliness` 를 구독할 뿐 반대가 아닌 것과 같다.

## 복제하지 않는다

Fusion 의 `OnPlayerLeft` 는 **서버만이 아니라 전 피어에서 불린다.** 그래서 각자 자기 화면에
띄우면 되고, `[Networked]` 도 `NetworkObject` 도 필요 없다 — `MultiPlaySceneBuilder` 도 안 건드린다.

서버만 아는 사실을 알려야 하는 메시지가 나중에 생기면 그때는 `StageEventNetBehaviour` 로 복제해
그 결과를 `Post` 하면 된다. **복제는 부르는 쪽의 문제이고 이 버스는 그것을 모른다.**

## 한 번에 하나만, 큐로 줄을 세운다

두 줄이 동시에 뜨면 어느 것이 방금 일어난 일인지 알 수 없고 자리도 겹친다. 시간은
`Time.unscaledTime` 이다 — 일시정지 중에도 흘러야 한다. 멈춘 화면에 토스트가 박제되면 그것대로
이상하다.

## ⚠ 정적 상태를 조심한다

`Post` 는 정적이고 `PlayerLeft` 도 정적 이벤트다. PlayMode 배치는 `DisableSceneReload` 라
**지난 판의 구독과 등록이 살아남는다.**

- 프레젠터는 `OnDisable` 에서 **자기가 등록한 경우에만** 자리를 비운다(남이 이미 가져갔으면 둔다).
- `PlayerLeftAnnouncer` 는 `OnDisable` 에서 반드시 구독을 해지한다.
- 프레젠터가 없으면 `Post` 는 조용히 버린다. **알림이 없다고 게임이 멈추면 안 된다.**

## 눈보라 경고는 아직 옮기지 않았다

`BlizzardAlertPresenter` 가 같은 모양의 토스트를 따로 그린다. 기능 차이는 사실상 **문구가 uxml 에
고정이라는 것과, 큐를 안 탄다는 것** 둘뿐이다.

옮기지 않은 이유는 **큐** 다. 경고가 다른 메시지 뒤에서 3초를 기다릴 수 있는데 **경고는 늦으면
경고가 아니다.** 우선순위를 넣으면 풀리지만 지금 필요 없는 복잡도다. 세 번째 메시지가 생기거나
두 토스트의 모양이 갈라지는 시점에 우선순위와 함께 다시 본다.

## 이름은 아직 번호다

`PLAYER 2 LEFT` 로 뜬다. 프로젝트에 **복제되는 닉네임이 없기 때문**이다 — 입력 UI 와 값은
`MainMenu` 에 있지만 그 프로세스 밖으로 나가지 않는다(`OutGameScreenController` 의 2026-08-24
기록: *"닉네임은 복제되지 않는다… 없는 정보를 만들어 내지 않는다"*).

닉네임을 복제하게 되면 **`PlayerLeftAnnouncer.Describe` 한 곳만 바꾸면 된다.** 그 작업은
로비 명단까지 같이 좋아지므로 별도로 잡는다.

## 검증

- EditMode `WorldMessageQueueTests` 7개 — 순서·만료·큐·페이드·빈 문구·비우기.
- 그리는 것과 세션 배선은 Play 로 본다. `UIDocument` 는 패널 없이 트리를 만들지 않고, **실제 피어가
  나가는 것은 인스턴스 둘이 필요하다** — 그 끝단만 검증 강도가 한 단계 낮다.
- ⚠ 화면 확인은 `eval` 에서 `ScreenCapture.CaptureScreenshot` 을 부른다.
  `screenshot --view Game` 은 카메라 렌더라 UI Toolkit 오버레이를 담지 못한다.
