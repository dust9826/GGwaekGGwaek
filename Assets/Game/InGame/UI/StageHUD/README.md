# Stage HUD

집별 선물 의뢰를 왼쪽 위에 겹쳐 꽂힌 작은 주문서로 표시한다. 평소에는 단색 견본과 시간 숫자만 보이고, 펭귄이 같은 색 선물을 들었을 때만 상대 방향과 직선거리가 펼쳐진다.

## Visual implementation

- 런타임 UI는 `StageHUD.uxml`, `StageHUD.uss`와 UI Toolkit 도형만 사용한다.
- 팔레트와 깊이는 인게임 공통 규칙인 `#0B2638` 그림자, `#FFFFFF` 외곽, `#F8F5EA` 종이, `#153950` 잉크를 따른다.
- 집 번호, 주문 번호, 선물 영문명과 개수는 표시하지 않는다. 하단 반복 다이아/톱니 장식도 사용하지 않는다.
- 선물 색은 색인 띠와 상자 본체에 사용하며, 상자·뚜껑·작은 매듭의 실루엣으로 색상표와 구분한다.
- 얇은 이중 테두리, 접힌 종이 모서리와 길 안내가 열릴 때 나타나는 짧은 절취선으로 주문서 인상을 만든다.
- 폰트는 `Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf`이며 라이선스는 `Assets/Game/Core/UI/Fonts/OFL-LilitaOne.txt`에 있다.
- 런타임 래스터, Material, 래스터 확대 없음. `Preview/GiftDeliveryTicketConcept.png`는 제작 참고용이며 UI에서 로드하지 않는다.
- `Preview/GiftDeliveryHudPreview.png`는 기본 상태, `Preview/GiftDeliveryHudNavigationPreview.png`는 같은 색 선물을 든 상태의 1920×1080 검수 캡처다.

## Runtime wiring

- `StageHUDController`는 `StageHudOrderView`만 받아 그리는 View다. 배달 도메인 타입을 직접 참조하지 않는다.
- 현재 점수는 오른쪽 위 `score-chip`에 뜬다(`SetScore`). 왼쪽 위는 의뢰 보드, 가운데 위는 시계이고
  **화면 아래쪽은 비워 둔다 — 펭귄 체력 바가 앉을 자리다.** 점수를 다시 띄운 이유는
  `../../Cleanliness/AGENTS.md`의 2026-08-26 항목에 있다(결과 화면이 점수를 그리므로, 플레이 중에
  안 보이면 마지막 숫자의 출처를 알 수 없다). 점수가 오를 때의 연출은 아직 없다.
- 달리기 체력은 **왼쪽 아래** `stamina-bar` 에 뜬다(`SetStamina01`). 중하단은 상호작용 프롬프트가
  쓰므로 왼쪽이다. 탈진 중에는 채움 색이 붉어진다 — 바가 조금 차 있는데도 Shift 가 안 먹는
  구간이 있어서 그것을 설명해 주는 것이 색의 일이다.
- **체력은 싱글·멀티가 같은 곳에서 읽는다** — `RequestHudPresenter` 가 로컬 펭귄의
  `PenguinLocomotion.Stamina01` 하나만 본다. 권위 피어는 `Step` 이, 비권위 피어는
  `PenguinNetAvatar.Render` 의 `ApplyPresentation` 이 같은 필드를 채워 두기 때문이다.
  규칙과 근거는 `../../Penguin/AGENTS.md` 의 "달리기 체력과 점프 쿨타임".
- `GiftDeliveryHudPresenter`는 기존 `GiftDeliveryDirector`, `RequestHudPresenter`는 새 `RequestDirector`를 같은 View 모델로 변환한다.
- `RequestStageFlowPresenter`가 `StageIntroController.Completed`와 `GameManager.GameEnded`를 구독해 인트로 → 플레이 → 결과 화면과 플레이어 입력을 연결한다.
- 주문 추가·제거·완료·만료와 스테이지 시작·종료 효과는 각 컴포넌트의 직렬화된 `UnityEvent`에 연결한다. UI 상태 전환 로직을 수정하지 않고 Feel 효과를 교체할 수 있다.

## Concept prompt

Built-in ImageGen의 `ui-mockup` 경로로 현재 HUD를 참고해 생성했다. 핵심 요청은 크림 종이, 남색 외곽과 그림자, 코랄 타이머, 집 색 탭을 유지하고 반복 다이아·톱니 하단을 제거한 작은 선물 주문 영수증 네 장이다.
