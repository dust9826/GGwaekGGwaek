# InGame/UI/PauseMenu — 게임 중 ESC 일시정지

게임 도중 ESC 로 여는 오버레이. **계속하기와 나가기 둘뿐이다.**

## 소유하는 것

| | |
|---|---|
| `Scripts/PauseMenuController.cs` | ESC 토글, 열고 닫기, 입력·카메라 게이팅, `timeScale` |
| `PauseMenu.uxml` · `PauseMenu.uss` | 카드 오버레이 레이아웃과 스타일 |
| `PauseMenuPanelSettings.asset` | `sortingOrder = 40` — HUD(0)와 결과 화면(30) 위에 뜬다 |

씬에서는 `SnowDeliveryRig/PauseMenuUI` 에 붙는다. `_playerInput`·`_cameraOrbit`·`_stageFlow` 셋을
인스펙터로 물린다.

## ⚠ 멀티에서는 인스펙터 참조가 비어 있다 — 아바타가 넣어 준다

`MultiPlaySceneBuilder` 가 씬의 펭귄을 지우므로, 그 펭귄을 가리키던 `_playerInput` 과
`_cameraOrbit` 은 **MultiPlay 에서 `NULL` 이다**(2026-08-31 실측). 그대로 두면 멀티에서 메뉴를 열어도
입력과 카메라가 계속 돌고 **커서가 잠긴 채라 버튼을 누를 수 없다.** 씬 파일에 컴포넌트가 들어간
것만 보고 "멀티도 된다" 고 넘기면 놓친다 — 런타임으로 확인해야 잡힌다.

`PenguinNetAvatar` 가 `HasInputAuthority` 일 때 `BindLocalPlayer(input, orbit)` 를 부른다.
`RequestStageFlowPresenter.BindLocalCameraOrbit` 과 같은 자리, 같은 이유다 — *"씬에는 플레이어가
없으므로 아바타가 스스로 자기를 넣는다."*

**여기서 씬을 뒤져 찾지 않는다.** 찾기로 때우면 "누가 로컬인가" 를 이 클래스가 추측하게 되고,
4인이면 첫 번째로 걸린 남의 아바타를 끌 수 있다. 그런 폴백이 조용히 틀렸을 때 증상이 원인에서
멀어진다는 것을 이 프로젝트가 이미 겪었다(2026-08-31 눈덩이 교환기).

## 싱글은 멈추고 멀티는 안 멈춘다

```
StageSession.For(gameObject).Runner == null  →  싱글  →  Time.timeScale = 0
그 외                                        →  멀티  →  시간은 그대로, 화면만 뜬다
```

멀티에서 시간을 멈추면 **그 피어만 멈추고 세션은 계속 돈다** — 재개하는 순간 자기 화면만 과거에
있다. 판정을 직접 하지 않고 `StageSession` 에 맡기는 이유는 `NetworkRunner.Instances.Count` 같은
자체 판정이 남의 세션에 뒤집히기 때문이다(`Core/Multiplay/AGENTS.md`).

**입력은 두 모드 모두 끈다.** 메뉴 뒤로 조작이 새면 보이지 않는 채로 움직이게 된다. 멀티에서는
그동안 펭귄이 그 자리에 서 있게 되는데, 그것이 이 결정의 비용이고 받아들인 것이다.

## 판이 끝나면 열리지 않는다

`RequestStageFlowPresenter.IsOutroShown` 이 참이면 `Open()` 이 그냥 돌아간다. 끝난 판은 멈출 것이
없고, 결과 화면의 RETRY/CONTINUE 를 가리기만 한다.

**종료 판정을 여기서 다시 하지 않는다.** 싱글은 `GameManager`, 멀티는 복제된 `MissionNetHub.Phase`
로 종료를 아는데, **그 둘을 이미 아는 것은 결과 화면을 켜는 쪽**이다. 그래서 그쪽에 한 줄짜리
속성을 두고 여기서는 그것만 읽는다 — 이 클래스는 모드를 몰라도 된다.

**열린 채로 판이 끝나는 경우도 막는다.** 멀티는 멈추지 않으므로 메뉴를 연 동안 시간이 흘러 판이
끝날 수 있고, 그러면 결과 화면이 이 메뉴 밑에 깔린다. `Update` 가 그때 스스로 닫는다.
싱글에서는 시간이 멈춰 있어 일어나지 않는다.

## ⚠ 정지의 주인이 둘이다 — 증강 위에는 열리지 않는다 (2026-09-01)

`AugmentSelectionDirector` 도 `Time.timeScale` 과 `PenguinInputReader.enabled` 를 잡는다.
그 위에서 이 메뉴를 열었다 닫으면 **`Close()` 가 입력을 무조건 켜서** 증강 화면이 떠 있는데 조작이
살아난다. `timeScale` 은 옳게 0 으로 복원되므로 물리는 멈춰 있고, 그래서 **눈에 잘 안 띈다.**

`AugmentSelectionDirector.IsOpen` 이 참이면 `Open()` 이 그냥 돌아간다. 결과 화면을 막는 것과 같은
방식이다 — **여는 것을 막으면 "닫을 때 내가 끈 것만 되돌린다" 같은 소유권 추적이 필요 없다.**
증강은 몇 초짜리 강제 선택이라 고르고 나서 멈추면 된다는 판단이다.

**세 번째 주인이 생기면 그때가 참조 계수 게이트를 만들 자리다.** 지금은 둘이라 이르다.

패널 정렬도 갈랐다 — 증강 **50**, 일시정지 **40**(결과 화면 30, 월드 메시지 20). 전에는 증강과
일시정지가 **둘 다 40** 이라 그리는 순서가 정의돼 있지 않았다.

같이 고친 것: `AugmentSelectionDirector.Pause()` 가 `Time.timeScale > 0f ? Time.timeScale : 1f` 로
직전 값을 저장하고 있었다. **이미 멈춰 있을 때 0 대신 1 을 기억해** 확정할 때 남이 멈춰 둔 게임을
풀어 버린다. 지금은 도달 불가능하지만(시간이 멈추면 일차도 안 넘어가고, 위 가드가 막는다) 정지
주체가 둘이 된 이상 방어적으로 둔다.

## ⚠ 커서를 직접 만지지 않는다

`PenguinCameraOrbit` 을 끄면 그쪽 `OnDisable` 이 `Cursor.lockState = None` 과 `visible = true` 를
이미 한다. 여기서 또 만지면 두 곳이 같은 전역 상태를 다투고, 어느 쪽이 마지막이었는지로 버그가
갈린다. **카메라를 끄는 것으로 커서까지 해결된다** — 실측으로 확인했다(열면 `Locked → None`,
닫으면 `None → Locked`).

## ⚠ `timeScale` 은 세 곳에서 되돌린다

`Cleanliness/AGENTS.md` 가 `timeScale = 0` 을 금지하는 근거 중 **전역 누수만 일시정지에도 그대로
문다.** PlayMode 배치는 `DisableSceneReload` 라 테스트끼리 상태를 공유하므로, 한 번 새면 그 뒤가
통째로 이상해진다. 그래서:

1. `Close()` — 평범한 재개
2. `Quit()` — **씬을 바꾸기 전에.** 안 되돌리면 메인메뉴가 얼어붙는다
3. `OnDisable()` — 열린 채 꺼지거나 파괴돼도

직전 값을 기억해서 되돌린다(0 을 "직전 값" 으로 덮어쓰지 않게 `_timeScaleHeld` 로 가드).

## 나가기는 새로 만들지 않는다

`RequestStageFlowPresenter.OnContinueRequested()` 를 그대로 부른다. 멀티에서 세션을 먼저 닫는 것과
`Leave()` 를 기다리지 않는 이유가 이미 거기 주석에 있다. 두 벌 만들면 그 판단이 갈린다.

## 스타일

같은 성격의 화면인 `StageOutro` 와 같은 조형·색을 쓴다 — 그림자 카드, 흰 아웃라인(`#f9ffff` /
`#173247`), 옅은 청록 면(`#eef5f4`), 왼쪽 붉은 액센트(`#c73542`), 아래쪽이 두꺼운 버튼.
문구는 전부 대문자 영문이다.

**OutGame 테마(`SnowRemovalTheme.uss`)를 참조하지 않는다.** 어셈블리는 코드만 막지만 에셋 경계도
손으로 지키는 규칙이고(루트 `AGENTS.md`), 필요한 값이 `StageOutro` 와 같은 것뿐이라 이 폴더에 둔다.

## 설정은 아직 없다

버튼 목록이 `pause-actions` 컨테이너 하나라, 사운드·마이크 설정을 넣을 때 **행만 늘리면 되고
컨트롤러 구조는 바뀌지 않는다.** 그때 `OutGame` 의 설정 로직과 `PlayerPrefs` 키를 공유해야 하면
그 키를 `Core` 로 올린다 — 지금은 두 번째 소비자가 없다.

## 씬 배선 — 빌더는 고치지 않는다

일반 컴포넌트라 `SinglePlay.unity` 에 넣고 `PPack/Cleanliness/Build MultiPlay Scene` 을 다시 돌리면
멀티에도 그대로 간다. `MultiPlaySceneBuilder` 는 손대지 않았다 — 2026-08-31 에 정한 기준의 첫
적용 사례다(`docs/specs/2026-08-31-single-multi-pipeline.md` §6).

## 검증

- EditMode `PauseMenuControllerTests` 4개 — 열고 닫을 때의 `timeScale`, 토글, 중복 `Open` 가드,
  `OnDisable` 복원.
- ⚠ **EditMode 는 `OnDisable`/`OnDestroy` 를 부르지 않는다**(`[ExecuteAlways]` 없는 MonoBehaviour).
  실측으로 확인했다 — `DestroyImmediate` 뒤에도 `timeScale` 이 0 으로 남아 처음 작성한 테스트가
  실패했다. 그래서 그 테스트는 **배선이 아니라 메서드 몸통**을 리플렉션으로 부른다. 콜백이 실제로
  불리는지는 Play 로 본다.
- 멀티 경로는 EditMode 로 못 덮는다(`NetworkRunner` 상태를 만들 수 없다). Play 로 확인한다.
- ⚠ **`screenshot --view Game` 은 UI Toolkit 오버레이를 담지 못한다.** 카메라 렌더 기반이라
  HUD 도 같이 빠진다. 화면을 눈으로 확인하려면 `eval` 에서 `ScreenCapture.CaptureScreenshot` 을
  부른다 — 그쪽은 백버퍼를 찍어 UI 가 들어온다.
