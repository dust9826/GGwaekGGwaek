# Loading Screen

MainMenu와 게임 플레이 씬 사이에서 동작하는 UI Toolkit 기반 제설 출동 화면입니다.

## 화면 구조

- 기준 해상도: MainMenu와 동일한 `1600 x 900` Panel Settings
- 배경: 밤하늘 남색 `#17384C`, 아래쪽 눈 둔덕, 낮은 대비의 수평 바람선, 작은 눈 결정과 랜턴 골드 포인트
- 진행 표현: 눈덩이가 왼쪽에서 오른쪽으로 굴러가며 점점 커지고, 뒤에 어두운 도로와 중앙 골드 표식을 드러냅니다.
- 제설 피드백: 눈덩이 앞에서 눈 조각과 얼음색 바람 호가 짧게 흩어지고, 열린 도로에는 소수의 작은 반짝임이 남습니다.
- 하단 HUD: `SNOWBALL EXPRESS`와 `ROLLING TO THE ROUTE`를 표시하고, 고정 폭 마침표 영역만 `없음 → . → .. → ...` 순서로 반복합니다.
- 배경과 제설 진행은 기존 Feel 중립 Transform 연결을 유지하며, 화면을 가로지르는 별도 와이프는 없습니다.
- 로딩: 메뉴에서 전달받은 목적지 씬을 비동기로 로드하고 최소 연출 시간이 지난 뒤 활성화합니다. 목적지가 없으면 `SnowDelivery_RequestFlow_Test`를 기본값으로 사용합니다.
- 로딩: `SnowDelivery_RequestFlow_Test` 씬을 비동기로 로드하고 최소 연출 시간이 지난 뒤 활성화합니다.
- 로딩: `SinglePlay` 씬을 비동기로 로드하고 최소 연출 시간이 지난 뒤 활성화합니다.

## 런타임 에셋

- 현재 런타임 표현은 폰트와 UI Toolkit 도형만 사용합니다. 별도 Unity Material은 없습니다.
- `Textures/LoadingVacuum*.png`, `LoadingDustCloud.png`, `DustPatch.png`, `DustCleanVfx.png`와 `Textures/Source/`는 과거 청소 테마 비교 자료이며 현재 UXML이나 컨트롤러에서 참조하지 않습니다.
- Preview PNG는 검수용 기록이며 런타임 에셋이 아닙니다.

## 씬 흐름

`MainMenu -> LoadingScreen -> SnowDelivery_RequestFlow_Test | PenguinTutorial`
`MainMenu -> LoadingScreen -> SnowDelivery_RequestFlow_Test`
`MainMenu -> LoadingScreen -> SinglePlay`

MainMenu의 스테이지 시작 버튼은 `SnowDelivery_RequestFlow_Test`, Tutorial의 `START TRAINING`은 `PenguinTutorial`을 목적지로 전달합니다. `LoadingScreenController`가 해당 씬의 `AsyncOperation`을 관리하며, LoadingScreen을 직접 실행하면 자동 전환하지 않고 프리뷰 루프를 유지합니다.
MainMenu Stage Select의 `DEPLOY SNOW CREW` 또는 레거시 Singleplayer 패널의 `DEPLOY SNOWPLOW`를 누르면 LoadingScreen을 열며, `LoadingScreenController`가 `SnowDelivery_RequestFlow_Test` 씬의 `AsyncOperation`을 관리합니다. LoadingScreen을 직접 실행하면 자동 전환하지 않고 프리뷰 루프를 유지합니다.
MainMenu Stage Select의 `DEPLOY SNOW CREW` 또는 레거시 Singleplayer 패널의 `DEPLOY SNOWPLOW`를 누르면 LoadingScreen을 열며, `LoadingScreenController`가 `SinglePlay` 씬의 `AsyncOperation`을 관리합니다. LoadingScreen을 직접 실행하면 자동 전환하지 않고 프리뷰 루프를 유지합니다.

`LoadingScreenFeel`은 기존 씬 연결을 보존하기 위해 이름을 유지한 `VacuumMotionDriver`의 로컬 X를 0에서 1까지 선형 이동시킵니다. `VacuumScrubFeel`은 눈덩이의 짧은 전후 움직임, `BackgroundDecorFeel`은 바람선과 눈 결정의 느린 호흡을 담당합니다.

에디터에서 LoadingScreen 씬을 직접 열거나 Play하면 눈덩이가 굴러가며 커지고 도로를 여는 루프를 확인할 수 있습니다.
