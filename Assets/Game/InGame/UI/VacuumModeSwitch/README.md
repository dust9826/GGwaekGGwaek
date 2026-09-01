# Vacuum Tool Switch UI

오른쪽 하단에서 현재 청소 도구와 다음 청소 도구만 보여 주고 `R`로 순환하는 UI Toolkit HUD다. 큰 다이얼과 모드명 패널은 사용하지 않고, 실제 장난감 청소 도구의 실루엣·반투명 궤도·작은 키캡만 남긴다.

현재 구현은 UI 상태와 전환 연출만 담당한다. 실제 도구 권위와 흡입 대상 필터링은 `InGame/Vacuum`이 소유하며, 게임플레이에서는 `SetMode(string modeId)`와 `_modeChanged` 이벤트로 연결한다.

## 데이터 구조

- `Data/DustVacuumMode.asset`: `dust` 정의와 먼지 청소기 UXML
- `Data/PropVacuumMode.asset`: `prop` 정의와 프롭 청소기 UXML
- `Data/VacuumToolModeCatalog.asset`: `R` 입력으로 순환할 도구의 순서
- 화면에는 카탈로그의 **현재 항목과 다음 항목**만 그린다. 항목이 세 개 이상이어도 UXML 레이아웃은 바뀌지 않는다.

새 청소 도구를 추가할 때는 다음 순서만 따른다.

1. `Icons/`에 112 x 112 기준의 도구 UXML을 만든다. 루트 클래스는 `tool-template-root`, 외곽은 흰색 스티커와 남색 그림자를 사용한다.
2. `Vacuum Tool Mode Definition` ScriptableObject를 만들고 고유한 소문자 `Id`, 표시명, Icon Template을 지정한다.
3. `VacuumToolModeCatalog.asset`의 `Modes` 목록에 원하는 순서로 추가한다.

컨트롤러와 `VacuumModeSwitch.uxml`은 수정하지 않는다. 아이콘 고유 색상과 내부 도형만 `VacuumModeSwitch.uss`에 추가한다.

## 조작과 연출

- 입력: New Input System의 `Keyboard.current.rKey`
- DOTween: 현재 도구는 타원의 아래쪽 전경 호를 따라 뒤로 이동하며 58%까지 축소되고, 다음 도구는 위쪽 후경 호를 돌아 앞으로 오며 110%까지 커졌다가 안착한다. 두 도구는 정확히 반대편 좌석까지 이동하고, 전환 절반 지점에 UI Toolkit 형제 순서를 바꿔 실제 앞뒤 겹침도 함께 뒤집는다. 전체 전환은 0.33초 이내다.
- 궤도: 화살표 없이 명암이 한쪽만 살짝 강한 단일 반투명 타원을 정적인 배경 가이드로 사용한다. 링 자체에는 이동·회전·펄스 애니메이션을 적용하지 않는다.
- Feel `MMF_Player`: `keycap`, `mode-switch`에 각각 눌림·펀치를 재생한다. `keycap`은 상판이 받침 쪽으로 내려가며 Y축이 살짝 눌린 뒤 반발하도록 Translate와 Scale을 함께 사용한다.
- DOTween은 두 도구 슬롯의 곡선 이동, Feel은 키캡과 HUD 전체 촉감만 담당한다.
- 직렬화된 `_modeChangedFeedback`가 `MMF_Player.PlayFeedbacks`를 호출하므로 InGame 코드 어셈블리는 Feel을 직접 참조하지 않는다.
- UI는 `picking-mode="Ignore"`로 게임 입력을 가로채지 않는다.

## 에셋

- 폰트: `Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf` (`R` 키캡)
- 폰트 라이선스: SIL Open Font License 1.1, `Assets/Game/Core/UI/Fonts/OFL-LilitaOne.txt`
- UI Texture: 사용하지 않음
- Unity Material: 사용하지 않음
- Preview PNG: 시각 검수 전용이며 런타임에서 참조하지 않음

## 파일

- `VacuumModeSwitch.uxml`: 현재/다음 슬롯, 반투명 궤도, 키캡 레이아웃
- `VacuumModeSwitch.uss`: 공통 스티커 레이어, 도구 실루엣, 오른쪽 하단 배치
- `Icons/*.uxml`: 카탈로그 정의가 주입하는 도구별 시각 템플릿
- `Preview/VacuumModeSwitchTransitionPreview.png`: 뒤 도구는 작고 흐리며 앞 도구는 크게 겹치는 전환 중간 프레임
- `Preview/VacuumModeSwitchEllipseHUDPreview.png`: 두 도구가 타원의 앞·뒤 반대편에 놓이는 확장 궤도 검수 프레임
- `Scripts/VacuumToolModeDefinition.cs`: 도구 ID·표시명·아이콘 템플릿 정의
- `Scripts/VacuumToolModeCatalog.cs`: 순환 순서 데이터
- `Scripts/VacuumModeSwitchController.cs`: R 입력, 데이터 주입, DOTween 전환, Feel 호출용 UnityEvent
- `../../Scenes/UI.unity`: 독립 확인용 UI 씬. `UI Standalone Clear Camera`가 프레임 버퍼만 지워 카메라가 없는 Game View의 이전 프레임 누적을 막는다.
