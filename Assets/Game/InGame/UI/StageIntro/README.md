# Stage Intro UI

인게임 진입 직후 펭귄 배달 임무를 보여 준 뒤 `3 → 2 → 1 → DELIVER!`로 출발시키는 UI Toolkit 오버레이다. 설백색 배달 명세표, 펭귄 우편 배지, 선물 상자와 눈 굴림 아이콘으로 게임의 핵심 행동을 바로 전달한다.

## 재생 흐름

1. 겨울 남색 딤과 펭귄 우편국의 설백색 배달 명세표가 짧게 페이드 인한다.
2. `ROLL`, `GIFTS`, `DELIVER` 행동이 눈덩이·선물·도착지 아이콘과 함께 차례로 나타난다.
3. 펭귄 배달원과 선물 태그가 있는 카운트다운이 `3` 코랄, `2` 노랑, `1` 민트로 바뀐다.
4. 마지막에 카드가 사라지고 선물 상자와 `DELIVER! / GIFTS ON THE MOVE` 출발 신호가 나타났다 퇴장한다.

`StageIntroController.Play()`로 재생하며 `SetStageCopy(stageLabel, stageTitle, stageSubtitle)`로 스테이지 문구를 바꾼다. 실제 스테이지 오케스트레이션에서는 `_introCompleted`에 차량 입력 잠금 해제를 연결한다.

## Feel과 DOTween

- DOTween은 카드 진입, 목표 칩, 숫자 강조, 펭귄의 짧은 출발 바운스와 `DELIVER!` 진입·퇴장을 담당한다.
- `_countdownTickFeedback`에는 Feel의 짧은 UI Scale/Audio feedback을 연결한다.
- `_cleanSignalFeedback`에는 출발 시 오디오나 차량 준비 피드백을 연결한다.
- `_introCompleted`는 게임플레이 입력을 푸는 시점이다.
- `PPack.InGame`은 Feel을 코드로 참조하지 않으므로 `MMF_Player.PlayFeedbacks` 연결은 Inspector의 UnityEvent에서 한다.

## 확인 방법

- `Tests/StageIntro_Countdown_Test.unity`를 열고 Play한다.
- 진입 시 자동 재생되며 `Space`를 누르면 언제든 처음부터 다시 재생된다.
- 테스트 씬의 `SPACE / REPLAY START` 힌트는 `_showPreviewHint`가 켜진 테스트 전용 표시다. 실제 게임 기본값은 꺼져 있다.
- 테스트 씬은 Build Settings에 추가하지 않는다.

## 화질과 에셋

- `StageIntroPanelSettings.asset`: `Scale With Screen Size`, 기준 `1920 x 1080`, Match `0.5`.
- 모든 패널, 아이콘, 테두리와 하이라이트는 UXML/USS 도형이다. 런타임 래스터 확대 없음.
- 폰트: `Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf` (SIL OFL 1.1).
- UI Texture: 사용하지 않음.
- Unity Material: 사용하지 않음.
- Preview PNG는 시각 검수 전용이며 런타임에서 참조하지 않는다.
- 최대 펀치 scale은 `1.08`이며 부모·자식 scale을 장시간 중첩하지 않는다.

## 파일

- `StageIntro.uxml`: 카드, 목표 칩, 카운트다운, 출발 신호 구조
- `StageIntro.uss`: 인게임 스티커 색·두께·아이콘 도형
- `Scripts/StageIntroController.cs`: 문구 주입, DOTween 시퀀스, Feel용 UnityEvent
- `Tests/StageIntro_Countdown_Test.unity`: 단독 실행 프리뷰 씬
- `Preview/StageIntroCountdownPreview.png`: 카운트다운 프레임
- `Preview/StageIntroCleanSignalPreview.png`: `DELIVER!` 출발 프레임
