# Main Menu

OutGame 기능을 `MainMenu` 단일 씬과 단일 `UIDocument` 안에서 전환하는 화면입니다. Kimohoh의 `MainMenuScene`처럼 HOST/JOIN/LOBBY를 씬 로드가 아닌 패널 상태로 관리합니다.

- 첫 화면에서 `HOST`, `JOIN`, `CHOOSE ROUTE`, `TUTORIAL`, `SETTINGS`, `EXIT`를 선택합니다.
- HOST/JOIN/LOBBY/STAGE SELECT/TUTORIAL/SETTINGS는 `MainMenu.uxml`의 `menu-view` 패널입니다. Home의 `CHOOSE ROUTE`는 Stage Select을 열고, Tutorial 패널의 `START TRAINING`은 `LoadingScreen`을 거쳐 플레이 가능한 `PenguinTutorial` 씬으로 진입합니다.
- 패널은 위치 이동이나 청소 띠 없이 짧게 페이드 전환하며 씬을 로드하지 않습니다.
- Stage Select의 `DEPLOY SNOW CREW`, 레거시 Singleplayer 패널의 `DEPLOY SNOWPLOW`, Tutorial의 `START TRAINING`은 별도 `LoadingScreen` 씬을 열고, 로딩 화면이 선택한 목적지 씬을 비동기로 준비합니다.
- Stage Select의 `DEPLOY SNOW CREW`와 레거시 Singleplayer 패널의 `DEPLOY SNOWPLOW`는 같은 진입 함수를 사용해 별도 `LoadingScreen` 씬을 열고, 로딩 화면이 `SnowDelivery_RequestFlow_Test` 씬을 비동기로 준비합니다.
- Stage Select의 `DEPLOY SNOW CREW`와 레거시 Singleplayer 패널의 `DEPLOY SNOWPLOW`는 같은 진입 함수를 사용해 별도 `LoadingScreen` 씬을 열고, 로딩 화면이 `SinglePlay` 씬을 비동기로 준비합니다.
- 실제 네트워크 세션을 통한 게임 플레이 진입은 아직 연결하지 않습니다.
- 빌드 첫 씬은 `Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity`입니다.

## 오디오

- `Audio/BGM_CleanUpDrive.mp3`를 메뉴 전용 2D BGM으로 반복 재생합니다.
- 볼륨은 UI 호버음을 가리지 않도록 `0.32`로 설정합니다.
- BGM은 `MainMenu` 씬 오브젝트에 붙어 있으며 영구 오브젝트로 승격하지 않습니다. 따라서 LoadingScreen이나 게임 플레이 씬으로 넘어가면 즉시 종료됩니다.
- 긴 음원을 메모리에 전부 올리지 않도록 Unity 임포트 설정은 `Streaming`을 사용합니다. 세부 출처와 임포트 설정은 `Audio/README.md`에 기록합니다.

## 마우스 커서

- `Cursors/`의 64px 원본을 Unity에서 32px로 임포트해 사용하며 로딩 화면 청소기와 같은 굵은 남색 외곽선, 흰색·코랄·회색의 단순 면으로 구성합니다.
- 세 상태 모두 `Cursor_Default`의 동일한 32px 픽셀과 불투명 영역을 사용합니다. 일반 상태는 흰색, 버튼 호버는 하늘색 `#55BFE2`, 클릭은 파랑 `#2F83CF`으로만 바뀝니다. UI Toolkit 소프트웨어 커서 대신 OS 커서를 사용해 이동 잔상과 클릭 시 크기 차이를 방지합니다.
- 클릭 시 별도 노란 반짝이나 파티클은 생성하지 않습니다.
- 세 상태의 핫스팟은 `(1.5, 1.5)`로 고정해 상태가 바뀌어도 클릭 위치가 움직이지 않습니다.
- 상태 Tint는 즉시 교체합니다. 커서 크기·스케일·회전 애니메이션은 사용하지 않습니다.
- `MainMenuCursor`는 MainMenu가 비활성화될 때 OS 기본 커서를 복원하므로 LoadingScreen과 게임 플레이에는 영향을 주지 않습니다.

## 비주얼 에셋

- 펭귄 야간 베이스캠프를 기준으로 밤바다 남색, 빙판 아이스블루, 배의 흰색, 부리 오렌지를 UI 계층에 사용합니다.
- 좌우 배경의 눈 덮인 침엽수 군락과 눈사람 마스코트는 확대해도 선명한 UI Toolkit 도형으로 유지합니다. 작은 화면에서는 콘텐츠 가독성을 위해 둘 다 자동으로 숨깁니다.
- 산타 레드와 크리스마스 장식은 사용하지 않고, 아이스블루는 주 행동, 부리 오렌지는 선택 강조에만 사용합니다.
- 균등한 타일 격자, 비눗방울, 화면을 가로지르는 닦기 장식은 사용하지 않습니다.
- 걸레·청소기 같은 별도 청소 도구 이미지는 사용하지 않습니다.
- 별도 Unity Material은 사용하지 않습니다.
- `Preview/`의 기존 이미지는 이전 산타 팔레트 비교 자료입니다. 펭귄 UI의 최종 캡처는 MainMenu Scene을 1600 x 900과 3840 x 2160에서 실행해 다시 검수합니다.

## UI 효과

- 버튼 호버에는 모서리에서 짧게 사라지는 작은 글린트를 사용합니다. 클릭에는 별도 파티클·버블·오버슈트 없이 짧은 눌림과 복귀만 남깁니다.
- 패널 전환은 별도 도착 파티클 없이 페이드만 사용하며, 화면을 좌우로 닦거나 패널을 미는 효과도 사용하지 않습니다.
- 배경의 작은 흰색 눈 결정은 서로 다른 박자로 밝기와 크기가 잔잔하게 변합니다.
- 효과는 외부 텍스처 없이 UI Toolkit 도형과 DOTween으로 생성되며 약 `0.2~0.4초` 뒤 자동 제거됩니다.
- 효과 검수 이미지는 호버 글린트 기준의 `Preview/MainMenuEffectsPreview.png`, 고해상도 검수 이미지 `Preview/MainMenuEffects_4K.png`입니다.
