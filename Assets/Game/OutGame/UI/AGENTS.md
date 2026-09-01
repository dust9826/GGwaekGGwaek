# OutGame UI 제작 기준

이 파일의 지침은 `Assets/Game/OutGame/UI/` 아래에서 새로 만들거나 수정하는 모든 UI에 적용한다. OutGame 흐름은 `MainMenu` 한 씬과 한 `UIDocument` 안에서 기능 패널을 전환하는 구성을 기준으로 한다. 실제 게임 진입처럼 로딩이 필요한 경계만 별도 씬을 사용한다.

## 기준 파일

- 통합 레이아웃: `MainMenu/MainMenu.uxml`
- 공통 스타일: `Shared/SnowRemovalTheme.uss`, `Shared/OutGameFlow.uss`
- 메뉴별 최종 오버라이드: `MainMenu/MainMenu.uss`
- 패널 제어와 상호작용: `Shared/Scripts/OutGameScreenController.cs`
- 같은 씬 페이드 전환: `Shared/Scripts/OutGameTransitionWipe.cs`
- 화면 스케일 기준: 1600 x 900, Scale With Screen Size
- 시각 검수 기준: `MainMenu/Preview/MainMenuPreview.png`

Host, Join, Lobby, StageSelect, Singleplayer, Tutorial, Settings는 독립 씬이 아니라 `MainMenu.uxml`의 `menu-view` 패널이다. 기능별 USS와 README는 각 하위 폴더에 유지하되, 씬 전환 API로 연결하지 않는다.

## 폰트

- 기본 영문 디스플레이 폰트는 `Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf`의 **Lilita One**이다.
- 제목, 큰 수치, 메뉴 버튼처럼 짧고 굵은 영문에 사용한다. 본문이나 긴 문장에는 과도하게 사용하지 않는다.
- 라이선스는 SIL Open Font License 1.1이며 원문은 `Assets/Game/Core/UI/Fonts/OFL-LilitaOne.txt`에 유지한다. 폰트 파일을 다른 위치로 복제하지 않는다.
- Lilita One에는 한글 글리프가 없으므로 한글 UI에는 한글을 지원하는 굵은 산세리프/디스플레이 폰트를 별도로 지정하되, 굵은 획과 둥근 인상을 맞춘다. 새 폰트를 추가하면 라이선스 파일과 출처를 같은 기능 폴더에 포함한다.
- 화면 전체의 폰트 혼용은 최대 두 패밀리로 제한한다.

## 산타 마을 제설 아트 디렉션

OutGame UI는 일반 청소 회사가 아니라 **산타 마을의 야간 제설대 출동 화면**으로 읽혀야 한다. 기능을 새로 만들거나 기존 화면을 수정할 때 다음 팔레트와 의미를 유지한다.

- 밤하늘 남색: `#17384C`
- 깊은 그림자: `#0D2637`
- 눈 카드: `#EEF5F4` / 외곽선 `#F9FFFF`
- 얼음 테두리: `#B9D5DA`
- 산타 레드: `#C73542` / 깊이 레드 `#8F2330`
- 솔잎 초록: `#2F7C66` / 깊이 초록 `#1D5647`
- 랜턴 골드: `#F1C969`
- 본문 남색: `#173247`
- 보조 본문: `#557381`

- 남색은 바깥 환경, 눈색은 정보 카드, 산타 레드는 주 행동과 출동 신호, 솔잎 초록은 협동/확정, 랜턴 골드는 선택/주의에만 사용한다. 모든 버튼을 크리스마스색으로 번갈아 칠하지 않는다.
- 배경은 큰 원형 링이나 비눗방울 대신 화면 아래의 겹친 눈 둔덕, 낮은 대비의 수평 바람선, 소수의 작은 눈 결정, 멀리 보이는 따뜻한 빛 한두 개로 구성한다. 장식은 비대칭이고 성기게 두어 정보 카드보다 먼저 보이지 않게 한다.
- 카드와 버튼은 눈에 덮인 제설대 표지판처럼 직선 정렬, 두꺼운 흰 가장자리, 짧은 아래 그림자를 사용한다. 사탕 지팡이 줄무늬, 과도한 리본, 장난감 장식은 사용하지 않는다.
- 문구는 `CLEANER`, `CLEANING`, `MESS`, `VACUUM`, `MOP` 대신 `DRIVER`, `SNOW CREW`, `ROUTE`, `PLOW`, `DEPLOY`, `BLIZZARD`를 우선한다. 실내 청소 콘텐츠를 설명해야 하는 별도 기능이 아니라면 먼지·얼룩·걸레·청소기 아이콘을 OutGame에 다시 넣지 않는다.
- 산타 느낌은 레드/그린의 양보다 `NORTH POLE`, 제설대 출동 문구, 눈길·등불·겨울 야간 대비로 만든다. 장식 밀도를 높여 산만하게 만들지 않는다.
- 흐릿한 유리 효과, 사실적 광택, 무거운 그라디언트는 기본 표현으로 사용하지 않는다. 텍스트 대비와 클릭 영역을 우선하며 장식 때문에 텍스트가 작아지거나 잘리지 않게 한다.

## UI 텍스처와 Material

- 패널, 배너, 버튼, 그림자, 링, 스트라이프는 가능한 한 USS의 색상·외곽선·반경·변형으로 만든다.
- 현재 MainMenu 배경은 외부 래스터 스프라이트나 Unity Material을 사용하지 않는다.
- LoadingScreen의 제설차, 눈더미, 드러나는 도로와 눈 분사도 UI Toolkit 도형으로 만든다. 검수용 이미지나 과거 청소기 텍스처를 런타임에 다시 연결하지 않는다.
- Preview 이미지는 검수용 캡처이며 런타임 배경이나 스프라이트로 참조하지 않는다.
- Material이 꼭 필요한 경우 UI Toolkit에서 필요한 이유와 셰이더 의존성을 README에 기록하고, 프로젝트 전역 Material을 임의로 수정하지 않는다.

## UI Toolkit 구조

- 신규 OutGame UI는 UI Toolkit(UXML + USS)을 기본으로 한다.
- `MainMenu.uxml` 안의 각 기능 화면은 `menu-view` 클래스를 사용하고 이름은 `view-<기능>` 형식을 따른다.
- 화면 버튼은 `view-<기능>` 이름으로 같은 문서의 패널을 연다. Home의 `view-stage-select`은 같은 씬의 스테이지 선택 패널을 열고, 선택 화면의 `action-stage-start`만 제설차가 도로를 여는 `LoadingScreen` 씬으로 진입한다. Back, Leave, Got It은 `view-home`으로 돌아간다.
- StageSelect은 현재 선택 가능한 스테이지, 잠금 상태와 위협 정보를 한 화면에서 읽게 한다. 잠긴 스테이지를 선택 가능한 버튼처럼 보이게 만들지 않으며, 실제로 연결된 스테이지만 시작 버튼을 활성화한다.
- 기능별 폴더에는 해당 기능 USS, README, Preview를 두고 독립 씬은 만들지 않는다.
- 공통 스타일을 바꿀 때는 Home과 모든 기능 패널의 회귀를 함께 확인한다.
- 기준 해상도는 1600 x 900이며, 다른 종횡비에서도 핵심 버튼과 텍스트가 잘리지 않도록 최대/최소 크기와 유연한 정렬을 사용한다.
- 씬과 UXML/PanelSettings 연결은 GUID가 보존되도록 Unity Editor 또는 Unity MCP로 이동·수정한다. `.meta`, `.unity`, `.asset` YAML을 직접 편집하지 않는다.
- C#은 프로젝트의 단일 `PPack` 네임스페이스 규칙을 따르고, OutGame 어셈블리에서 InGame 어셈블리를 참조하지 않는다.

## 확대 화질 기준

Main Menu의 현재 구성을 모든 OutGame UI의 확대 화질 기준으로 사용한다.

- `StartScreenPanelSettings.asset`처럼 `Scale With Screen Size`, 기준 `1600 x 900`, `Match Width Or Height`, `Match = 0.5`를 사용한다.
- 확대되는 버튼·장식·아이콘은 우선 UI Toolkit 텍스트, USS 도형과 `Painter2D`로 만든다. Main Menu가 런타임 래스터 배경 없이 확대 화질을 유지하는 핵심 방식이다.
- Preview PNG, 콘셉트 이미지와 화면 캡처를 런타임 요소로 확대하지 않는다.
- 래스터가 필요한 경우 원본 픽셀은 `레이아웃 표시 크기 × 애니메이션 최대 scale` 이상이어야 하며 가능하면 2배 원본을 사용한다. Import는 `Compression = None`을 우선하고, 부드러운 UI는 Bilinear, 픽셀 아트만 Point를 사용한다.
- 부모와 자식에 scale을 중복 적용하지 않고 호버는 현재 기준인 `1.04~1.06` 범위를 유지한다. 확대 종료 후 scale과 translate를 정확히 기본값으로 돌린다.
- 전체 UI가 흐리면 이미지 교체 전에 PanelSettings와 Game View의 실제 출력 해상도·축소 미리보기 상태를 확인한다.
- 래스터를 사용한 기능 README에는 `원본 크기 / Import Max Size / Filter Mode / Compression / 최대 scale`을 기록한다. 래스터가 없으면 `래스터 확대 없음`이라고 기록한다.

## DOTween 모션

- 진입 모션은 카드 → 타이틀 → 설명 → 버튼 순으로 짧게 스태거한다.
- 패널 전환은 씬 로드 없이 짧은 페이드로 교체한다. 패널 자체 이동, 화면을 가로지르는 닦기/밀기 띠, 눈보라 와이프는 사용하지 않는다.
- 버튼 효과는 호버의 짧은 글린트와 오버슈트 없는 눌림/복귀 모션으로 제한한다. 클릭과 패널 도착에는 버스트나 파티클을 사용하지 않고, 방향성 있는 화면 와이프도 사용하지 않는다.
- 런타임 효과 요소는 `PickingMode.Ignore`로 생성하고 Tween 완료 또는 `OnDisable`에서 모두 제거한다. 버튼 호버의 작은 흰색/얼음색 결정 두 개를 넘는 버스트는 만들지 않는다.
- 기본 등장 이징은 `Ease.OutBack`, 보조 이동은 `Ease.OutQuad`, 잔잔한 반복은 `Ease.InOutSine`을 사용한다.
- 호버는 약 `1.04~1.06` 배 확대하고, 클릭은 `0.97 → 1.0`의 짧은 눌림/복귀만 사용한다.
- 메뉴 모션은 일시정지 상태에서도 동작하도록 필요 시 `SetUpdate(true)`를 사용한다.
- Tween에는 대상을 지정하고 `OnDisable`에서 모두 `Kill`하여 전환 뒤 Tween이 남지 않게 한다.
- 모션은 피드백을 강화하는 용도로만 사용하며 클릭을 지연시키거나 반복 애니메이션이 가독성을 방해하지 않게 한다.

## UI 사운드

- Main Menu의 현재 기본 호버는 `MainMenu/Audio/UI/UI_Hover_Dustyroom.wav`, 일반 클릭은 `UI_Click_Dustyroom.wav`를 사용한다. 같은 Dustyroom 팩의 인접 변형이라 기본 상호작용의 음색이 이어진다.
- 같은 문서 안의 패널 이동과 `BACK/LEAVE/GOT IT`은 `UI_Click_Casual.ogg`, 실제 세션 생성·참가·게임 시작·제설대 출동은 더 짧고 분명한 `UI_Click_Confirm.wav`를 사용한다. 단순 설정 변경과 스테이지 선택에는 기본 클릭음을 유지한다.
- 호버 사운드는 시각적 확대가 시작되는 순간 재생하고, 길이는 0.18초 호버 Tween보다 짧게 유지한다.
- 기본 볼륨은 호버 `0.18`, 일반 클릭 `0.30`, 패널 이동 `0.24`, 확정 행동 `0.34`를 기준으로 낮게 유지한다. 같은 샘플을 반복할 때는 버튼 의미에 따라 `0.94~1.06` 범위의 작은 피치 변화만 허용한다.
- 포인터가 경계를 빠르게 오갈 때 소리가 겹치지 않도록 짧은 재생 제한 시간을 둔다.
- UI 음소거 설정을 존중하고, 비활성화 시 재생 중인 UI 사운드와 AudioSource 대상 Tween을 정리한다.
- 새 사운드를 추가하면 원본 파일명, 출처, 라이선스, 길이와 선택 이유를 해당 화면의 `Audio/README.md`에 기록한다.

## 완료 조건

- Unity 컴파일 오류와 UI Toolkit 경고가 없다.
- Home → 각 기능 → Home, Host/Join → Lobby가 Play Mode에서 작동하며 활성 씬이 계속 `MainMenu`인지 확인한다. 모든 화면에서 청소 전용 문구나 민트 청소 배경이 다시 노출되지 않는지도 함께 확인한다.
- 페이드 전환이 입력을 중복 처리하지 않고 짧게 끝나며, 패널의 가로 위치가 바뀌지 않는지 확인한다.
- 1600 x 900 기준 미리보기를 Home과 각 기능의 `Preview` 폴더에 갱신한다.
- 1600 x 900 정지 화면과 최대 호버 확대 프레임에서 텍스트·아이콘·둥근 테두리가 선명한지 확인하고, 1280 x 720과 2560 x 1440에서도 회귀 검수한다.
- 사용한 폰트, 텍스처, Material, 라이선스와 의도적인 스타일 예외를 화면별 README에 기록한다.
