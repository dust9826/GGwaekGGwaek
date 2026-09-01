# Mission HUD

우측 상단에 표시되는 필수 미션 전용 UI Toolkit HUD입니다. 미션 판정은 하지 않고, 외부 미션/이벤트 시스템이 전달한 목표와 진행도만 표시합니다.

## Runtime API

- `SetMissions(IReadOnlyList<MissionHUDItem>)`: 표시할 필수 미션 목록을 교체합니다.
- `ReceiveMissions(IReadOnlyList<MissionHUDItem>)`: 이벤트 수락 시 목표를 순차적으로 추가하며 카드를 늘립니다.
- `SetProgress(string missionId, int current, int target)`: 개별 미션 진행도를 갱신합니다.
- `CompleteAndRemoveMission(string missionId)`: 완료 표시 후 해당 행을 접어 제거합니다.
- `SetVisible(bool)`: HUD 노출을 제어합니다.
- `ClearMissions()`: 목록을 비우고 HUD를 숨깁니다.

`PF_MissionHUD.prefab`을 인게임 UI 씬에 배치하고, 실제 미션 시스템의 상태 변경 시 위 API를 호출합니다.

## Preview

`Tests/MissionHUD_RequiredMissions_Test.unity`를 실행하면 토끼 당근 트랩 이벤트의 3개 목표가 표시됩니다.

- `E`: 토끼 트랩 이벤트 받기
- `Space`: 다음 미션 완료 후 제거
- `R`: 이벤트가 없는 초기 상태로 복귀

`WinterVillage_ConceptMap`의 프리뷰는 시작 시 HUD가 숨겨져 있다가 1.2초 뒤 이벤트를 자동으로 받습니다.
실제 게임에서는 미션 시스템이 `ReceiveMissions`와 `CompleteAndRemoveMission`을 호출합니다.

## Feel

프리팹에는 `Feel_MissionReceived`, `Feel_MissionCleared` 두 `MMF_Player`가 들어 있습니다.
컨트롤러의 직렬화된 UnityEvent가 `PlayFeedbacks`를 호출하고, Feel이 움직인 `FeelScaleDriver`를
UI Toolkit 카드 스케일에 반영합니다. 행 높이 증감은 DOTween이 담당해 레이아웃이 자연스럽게 늘고 줄어듭니다.
Feel 스쿼시는 항상 `1` 스케일에서 시작하고 끝나며 중첩 재생하지 않습니다. HUD를 숨기거나 다시 표시할 때
컨트롤러가 드라이버, 카드, 행의 일시적인 스케일을 기준값으로 복원하므로 반복 노출로 높이가 누적 축소되지 않습니다.

테스트 씬은 빌드 설정에 추가하지 않습니다.

## Visual assets

- 영문 라벨: 프로젝트 공용 `Lilita One`
- 한글 본문: `Noto Sans KR` variable font
- 아이콘/배경/테두리: USS 도형으로 제작, 별도 래스터 이미지 없음
- `Fonts/OFL.txt`: Noto Sans KR의 SIL Open Font License 1.1
