# Stage Outro UI

스테이지 종료 시 겨울 노선 결과를 보여 주는 UI Toolkit 결과 화면이다. Intro와 동일한 Winter District 출동 보드 스타일을 사용한다.

## 점수와 최고 점수

- `StageOutroController.SetResult(score, highScore, isNewRecord, clearPercent, timeText)`로 결과를 표시한다.
- 카드 가운데에 `ROUTE SCORE` + 이번 판 점수(금색), 그 아래 `BEST <최고 점수>`가 온다.
- 점수는 0에서 결과값까지 굴러 올라간 뒤 한 번 튄다.
- 신기록이면 `NEW RECORD!` 배지가 뜬다. 신기록이 아니어도 **배지 자리는 그대로 비워 둔다** —
  켜질 때 카드 높이가 바뀌면 아래 통계와 버튼이 통째로 밀린다.
- 화면에 뜨는 `BEST`는 이번 점수보다 낮아질 수 없다(`SetResult`가 표시 단계에서 막는다).
- 테스트 씬에서는 숫자 키 `1`(기록 못 깬 판), `2`(신기록)로 두 상태를 다시 재생할 수 있다.

⚠ **별은 UI에서만 사라졌다.** `StageStarRubric`·`StageResult`는 `../../Cleanliness/`에 그대로
살아 있고 멀티에서는 `MissionNetHub`가 별 개수를 계속 복제한다. 이 화면이 안 그릴 뿐이다 —
근거는 `../../Cleanliness/AGENTS.md`의 2026-08-26 항목.

## 최고 점수의 주인

`Cleanliness/Scripts/StageHighScore.cs`(`PlayerPrefs`, 키 `PPack.HighScore.<stageId>`)다. 이 화면은
읽지도 쓰지도 않는다 — 제출과 조회는 아래 두 접착부가 맡고, 컨트롤러는 받은 숫자를 그리기만 한다.
기록은 **기기마다 자기 것**이라 멀티에서도 공유되지 않는다.

## 연결 지점

프로덕션 `SinglePlay.unity`에서는 `RequestStageFlowPresenter`가 `GameManager.GameEnded`를 구독해
`SetResult(...)`를 부르고 아래 UnityEvent를 연결한다. 옛 `StageOutroPresenter`는
`SinglePlayDirector` 기반 씬을 위한 호환 코드이며 현재 SinglePlay 씬에는 배치하지 않는다.

- `_retryRequested`: `RequestStageFlowPresenter.OnRetryRequested` — `SinglePlay.unity` 재로드
- `_continueRequested`: `RequestStageFlowPresenter.OnContinueRequested` — `MainMenu.unity` 로드
- `_outroShown`: 아직 연결 안 함(연출 완료 시점에 추가로 뭔가 해야 하면 여기에 배선)

`RequestStageFlowPresenter`(`../StageHUD/Scripts/`)도 같은 화면을 쓴다 — 싱글/호스트는
`GameManager.Score`, 클라이언트는 복제된 `MissionNetHub.Score`를 넣는다. 최고 점수 제출은
**결과 화면이 없어도** 일어난다(기록은 UI 상태가 아니라 게임 상태다).

## 확인 방법

`Tests/StageOutro_Result_Test.unity`를 열고 Play한다. 숫자 키 `1`로 기록을 못 깬 판, `2`로 신기록
상태를 확인한다.
