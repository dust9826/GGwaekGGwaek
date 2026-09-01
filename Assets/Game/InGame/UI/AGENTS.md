# InGame UI 제작 기준

이 파일의 지침은 `Assets/Game/InGame/UI/` 아래의 HUD, 상호작용 안내, 결과 화면에 적용한다. 특별한 기획 요구가 없다면 `VacuumModeSwitch/`를 인게임 HUD의 시각·구조·모션 기준 구현으로 사용한다.

Boundary: 게임 상태의 권위는 각 인게임 기능이 소유한다. UI는 `Cleanliness`, `Vacuum` 등에서 전달받은 상태를 그리며 흡입 판정이나 점수 규칙을 직접 소유하지 않는다.

## 기준 파일

- 레이아웃: `VacuumModeSwitch/VacuumModeSwitch.uxml`
- 스타일과 색상 토큰: `VacuumModeSwitch/VacuumModeSwitch.uss`
- 도구 데이터: `VacuumModeSwitch/Data/VacuumToolModeCatalog.asset`
- DOTween 전환: `VacuumModeSwitch/Scripts/VacuumModeSwitchController.cs`
- 화면 스케일 기준: `VacuumModeSwitch/VacuumModePanelSettings.asset` (1920 x 1080, Scale With Screen Size)
- 시각 검수: `VacuumModeSwitch/Preview/VacuumModeSwitchDustPreview.png`, `VacuumModeSwitchPropPreview.png`, `VacuumModeSwitchTransitionPreview.png`

새 HUD를 만들기 전에 위 파일과 `Assets/Game/OutGame/UI/AGENTS.md`를 함께 확인한다. OutGame의 팔레트와 굵은 외곽선 문법은 공유하지만, 인게임에서는 플레이 공간을 가리지 않는 작은 정보 단위로 축약한다.

## 시각 방향

- 목표는 값싼 도형 모음이 아니라 **장난감 청소 도구에 붙은 두꺼운 스티커 UI**다.
- 주요 컨트롤은 `짙은 깊이 그림자 → 흰색 스티커 외곽 → 주색 면 → 기능을 보여 주는 내부 형태` 순으로 최소 3단 이상의 깊이를 만든다.
- 도구 전환 HUD의 궤도는 완전한 원형 다이얼로 만들지 않는다. 화살표 없이 명암이 비대칭인 단일 반투명 타원만 두고, 방향은 실제 전환 모션으로 전달한다.
- 단일 타원은 정적인 배경 가이드로만 사용한다. 화살촉, 두 번째 링, 반사점, 회전·이동·펄스 애니메이션을 추가하지 않는다.
- 아이콘은 점이나 글자만으로 대체하지 않는다. 화면 안의 실제 도구를 단순화한 실루엣을 사용해 먼지 청소기와 프롭 청소기를 형태만으로 구분한다.
- 한 컴포넌트 안에서 핵심색은 주색, 보조색, 강조색의 최대 세 가지로 제한한다. 흰색과 남색은 외곽·텍스트용 중립색으로 본다.
- 글로우, 유리 블러, 사실적 금속 그라디언트, 얇은 1px 선은 기본 표현으로 사용하지 않는다.
- 화면 가장자리 여백을 유지하고, 인게임 월드나 조준점을 가리지 않는다. 청소기 모드 HUD는 오른쪽 하단만 사용하며 왼쪽 보조 HUD를 만들지 않는다.

## 폰트

- 짧은 영문 제목, 모드명, 키캡은 `Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf`의 **Lilita One**을 사용한다.
- 라이선스는 SIL Open Font License 1.1이며 `Assets/Game/Core/UI/Fonts/OFL-LilitaOne.txt`에 유지한다.
- 긴 설명과 한글에는 별도의 굵은 산세리프를 사용하되 한 화면의 폰트 패밀리는 최대 두 개로 제한한다.
- 모드 라벨은 `DUST`, `PROP`처럼 짧게 쓰고, 동일한 의미를 긴 문장과 아이콘으로 반복하지 않는다.

## 색상 토큰

- 깊은 남색: `#0B2638`
- 본문·외곽 남색: `#153950`
- 스티커 흰색 / 종이 크림: `#FFFFFF` / `#F8F5EA`
- 민트 / 깊이 민트: `#63D6C1` / `#2AAE9A`
- 파랑 / 깊이 파랑: `#2F83CF` / `#1C62A5`
- 초록 / 깊이 초록: `#31B478` / `#208457`
- 강조 노랑: `#FFD35A`
- 프롭 코랄: `#F15B62`

OutGame과 색 이름은 공유하되 인게임 HUD에서는 배경색을 직접 깔지 않고, 게임 화면 위에서 읽히도록 흰 외곽과 남색 그림자를 함께 사용한다.

## UI Texture와 Material

- 기준 HUD는 외부 UI 텍스처와 Unity `Material` 없이 USS 도형으로 만든다.
- 원, 세그먼트, 키캡, 테두리, 얕은 하이라이트는 `background-color`, `border-*`, `border-radius`, `rotate`, `translate`, `opacity`로 구성한다.
- 텍스처가 반드시 필요하면 원본, 라이선스, 출처, 임포트 설정을 기능 README에 기록한다. 검수용 Preview 이미지를 런타임 스프라이트로 사용하지 않는다.
- 프로젝트 전역 Material이나 OutGame 전용 에셋을 InGame에서 직접 참조하지 않는다. 두 영역이 함께 쓰는 리소스는 `Core`의 공용 위치로 승격한다.

## UI Toolkit 구조

- 신규 HUD는 UI Toolkit(UXML + USS)을 기본으로 한다.
- 기능별 폴더에 `Scripts`, 필요 시 `Audio`, `Textures`, `Preview`를 둔다. 씬은 `UI/Scenes/`에 둔다.
- HUD 루트는 기본적으로 `picking-mode="Ignore"`를 사용해 게임 입력을 가로채지 않는다.
- 기준 해상도는 1920 x 1080이며 16:10, 울트라와이드에서도 가장자리 여백과 컴포넌트 비율이 유지되어야 한다.
- UI 전용 씬을 단독 실행할 때는 `Culling Mask = Nothing`, 가장 낮은 Depth의 폴백 카메라로 프레임 버퍼를 지운다. 카메라가 없으면 UI 애니메이션과 Game View 안내 문구의 이전 프레임이 누적될 수 있다.
- UXML과 PanelSettings 연결, 에셋 이동·삭제는 Unity Editor 또는 Unity MCP로 수행한다. `.meta`, `.unity`, `.asset` YAML을 직접 편집하지 않는다.
- 코드는 단일 `PPack` 네임스페이스를 사용하며 InGame과 OutGame 어셈블리를 서로 참조하지 않는다.

## 확대 시 화질 유지

Main Menu의 해결 방식을 인게임 UI에도 그대로 적용한다. 기준은 `Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity`의 `OutGameUI`다.

- `PanelSettings`는 `Scale With Screen Size`를 사용한다. Main Menu 기준값은 `1600 x 900`, `Match Width Or Height`, `Match = 0.5`이며 인게임은 현재 기준인 `1920 x 1080`을 유지하되 같은 스케일 방식을 사용한다. `Constant Pixel Size` 상태에서 루트 전체를 임의 배율로 키워 해상도를 맞추지 않는다.
- 확대될 버튼, 아이콘, 링, 테두리, 진행 바는 Main Menu처럼 UI Toolkit 텍스트·USS 도형 또는 `Painter2D` 경로로 만든다. `background-color`, `border-*`, `border-radius`와 벡터 폰트는 확대 시 다시 그려지므로 저해상도 PNG를 늘린 것처럼 깨지지 않는다.
- 검수용 Preview PNG, 콘셉트 아트, 화면 캡처를 런타임 UI에 넣고 확대하지 않는다. 이 이미지는 제작 참고용일 뿐 UI 에셋이 아니다.
- 래스터 텍스처가 꼭 필요한 그림은 **애니메이션 최대 표시 크기**를 기준으로 원본 픽셀을 준비한다. 예를 들어 200px 아이콘을 최대 `1.10`까지 확대한다면 최소 220px 이상을 제공하고, 가능하면 2배 원본을 사용한다. 작은 이미지를 Import의 `Max Size`나 런타임 `scale`만 키워 복구하려 하지 않는다.
- 래스터 UI 텍스처 Import는 손실 압축을 피하고(`Compression = None` 우선), 알파가 있는 UI는 투명 경계 번짐을 확인한다. 부드러운 일러스트는 Bilinear, 의도적인 픽셀 아트만 Point를 사용한다. Texture가 동적 아틀라스의 최대 서브텍스처 크기를 넘거나 축소되지 않는지도 PanelSettings와 Import 설정에서 확인한다.
- 확대 애니메이션은 Main Menu처럼 기본 크기에서 짧게 `1.04~1.10` 범위로 제한한다. 긴 시간 큰 배율로 유지하거나 부모와 자식에 `scale`을 중복 적용하지 않는다. 부모·자식 배율이 곱해져 예상 최대 크기와 래스터 해상도를 초과할 수 있다.
- 텍스트는 이미지로 굽지 않고 `.ttf`/폰트 에셋을 사용한다. 글자가 흐리면 폰트 PNG를 교체하는 것이 아니라 PanelSettings, Game View 해상도, 실제 font-size와 중첩 scale부터 확인한다.
- 흐림 현상을 볼 때 먼저 Game View의 `Low Resolution Aspect Ratios`, 축소된 미리보기 배율, 캡처 리사이즈 여부를 배제한다. 에디터 Game View가 1920 x 1080을 작은 창에 축소 표시하면 실제 빌드보다 흐려 보일 수 있다.
- 새 UI에 래스터 에셋을 사용했다면 기능 README에 `원본 크기 / Import Max Size / Filter Mode / Compression / 애니메이션 최대 scale`을 기록한다. USS·폰트·Painter2D만 사용했다면 `래스터 확대 없음`이라고 명시한다.

### 화질 검수 순서

1. PanelSettings가 `Scale With Screen Size`이고 기준 해상도와 `Match` 값이 의도대로인지 확인한다.
2. 1920 x 1080 Game View에서 정지 상태와 애니메이션 최대 확대 프레임을 각각 캡처한다.
3. 1280 x 720과 2560 x 1440에서도 텍스트, 둥근 외곽, 아이콘 경계가 깨지거나 흐려지지 않는지 확인한다.
4. 문제가 래스터 하나에서만 발생하면 원본·Import 설정을 수정한다. 전체 UI가 흐리면 개별 이미지를 키우기 전에 PanelSettings와 Game View 출력 해상도를 수정한다.
5. 확대 종료 후 `scale = 1`과 정수 기준 레이아웃 위치로 돌아오는지 확인한다. 작은 잔여 scale과 반 픽셀 translate를 남기지 않는다.

## DOTween 모션

- 도구 전환은 0.34초 이내로 끝낸다. 현재 도구가 뒤로 이동하며 축소되고 다음 도구가 앞으로 이동하며 확대되는 교차 모션을 사용한다.
- 인게임 시작 연출은 `StageIntro/`를 기준으로 `펭귄 배달 명세표 → 3·2·1 → DELIVER! → HUD/차량 입력 활성화` 순서를 사용한다. 명세표에는 `ROLL / GIFTS / DELIVER` 행동과 펭귄 우편 배지를 보여 주고, 카운트는 코랄·노랑·민트 선물 태그로 구분한다. 출발 감각은 펭귄과 선물의 짧은 바운스 및 마지막 배달 신호로만 표현한다.
- 시작 카드는 월드를 완전히 가리는 로딩 화면으로 만들지 않는다. 차량과 진행 방향이 보이도록 상단 중앙에 두고, 종료 즉시 딤과 카드의 opacity를 0으로 복귀시킨다.
- 기본 이징은 등장 `Ease.OutBack`, 교체 `Ease.InOutBack`, 보조 페이드 `Ease.OutQuad`를 사용한다.
- 키 입력 피드백은 키캡을 약 `0.90`까지 눌렀다가 복귀시키며, 반복 애니메이션은 사용하지 않는다.
- 일시정지 중에도 필요한 HUD 피드백은 `SetUpdate(true)`를 사용한다.
- Tween에는 대상을 지정하고 `OnDisable`에서 모두 `Kill`한다.

## Feel 피드백

- Feel은 프로젝트 코드에서 직접 참조하지 않는다. 씬의 `MMF_Player`를 컨트롤러의 직렬화된 `UnityEvent`에 연결하고, 이벤트가 `PlayFeedbacks`를 호출하게 한다.
- UI Toolkit 대상은 UXML의 안정적인 `name`으로 질의한다. 기준 HUD는 `rotate-arrow`, `keycap`, `mode-switch`를 사용한다.
- DOTween은 도구 슬롯 교체, Feel은 키캡 입력 반응·강조 펀치를 담당한다. 타원 링에는 모션 피드백을 연결하지 않는다.
- DOTween 전환에서는 뒤 도구를 약 60%로 줄이고 흐리게, 앞 도구를 약 110%까지 키운다. 전환 중간에 `VisualElement` 형제 순서도 교체해 크기 변화와 실제 겹침 순서가 일치해야 한다.
- 두 도구의 시작·종료 좌석은 타원의 대각선 반대편에 두고, 하나는 아래쪽 전경 호, 다른 하나는 위쪽 후경 호를 따라 이동한다. 중앙에서 직선으로 교차하거나 종료 직전에 위치가 순간 이동하지 않도록 양쪽 이동 오프셋은 실제 좌석 간 거리와 정확히 일치시킨다.
- `R` 피드백은 약 0.28초 안에 끝나며 키캡 상판이 받침 쪽으로 5~7px 내려가면서 Y축이 살짝 눌렸다 튀어 오르고, HUD 전체가 3% 안쪽으로 펀치 후 원래 크기로 돌아온다. 키캡은 동일 비율로만 축소하지 않는다.
- 빠른 재입력에서도 피드백이 중첩되지 않게 현재 키캡 피드백을 재시작하고, 종료 후 이동값과 스케일이 초기값으로 반드시 복귀해야 한다.

## 청소 도구 전환 HUD 결정

- 오른쪽 하단의 작은 전환 표시만 사용한다. 펼쳐지는 방사형 메뉴, 큰 원형 다이얼, 모드명 패널, 왼쪽 보조 UI는 제외한다.
- 현재 도구 하나를 앞쪽에 크게, 카탈로그의 다음 도구 하나를 뒤쪽에 작고 흐리게 표시한다.
- `R`은 `VacuumToolModeCatalog`의 순서대로 순환한다. 도구가 추가되어도 컨트롤러와 기준 UXML은 수정하지 않고, 아이콘 UXML·정의 에셋·카탈로그 항목만 추가한다.
- UI는 표시 상태만 소유한다. 실제 도구 권위와 흡입 대상 필터링은 `../Vacuum/`이 소유한다.

## 완료 조건

- Unity 컴파일 오류와 UI Toolkit 경고가 없다.
- 진입, `R` 전환, 현재·다음 도구 순환을 Play Mode에서 확인한다.
- 1920 x 1080의 현재/다음 순서 Preview를 해당 기능의 `Preview/`에 갱신한다.
- 작은 화면에서도 라벨과 키캡이 잘리지 않고 게임 입력을 가로채지 않는다.
- 1920 x 1080의 애니메이션 최대 확대 프레임에서 텍스트·아이콘·테두리 화질이 유지되고, 1280 x 720 및 2560 x 1440에서도 같은 항목을 확인한다.
- 사용한 폰트, 텍스처, Material, 라이선스와 의도적 예외를 기능 README에 기록한다.
