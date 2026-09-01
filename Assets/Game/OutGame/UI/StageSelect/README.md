# Stage Select

`MainMenu` 씬 안의 `view-stage-select` 패널이다. 별도 Unity 씬을 로드하지 않으며 Home의
`SINGLEPLAYER`에서 짧은 페이드로 열린다. 현재 실제로 연결된 `WINTER VILLAGE`만 선택 가능하고,
뒤의 두 지점은 진행 방향만 보여 주는 잠금 노드다. `DEPLOY SNOW CREW`를 눌렀을 때만
`LoadingScreen` 씬을 거쳐 `SinglePlay` 씬으로 넘어간다.

## 디자인 근거

- Overcooked 2의 오버월드처럼 스테이지를 독립 카드 목록이 아니라 하나의 이동 경로 위 노드로 보인다.
- Super Mario 3D World의 맵 화면처럼 현재 코스와 잠금/클리어 상태를 노드 가까이에 붙여 읽힌다.
- 선택한 노드와 임무 브리핑을 좌우로 분리해, 경로를 훑고 세부 내용을 확인하는 순서를 만든다.
- 공통 제설 팔레트의 밤하늘 남색, 산타 레드, 솔잎 초록, 랜턴 골드, 흰 외곽선과 눈색 패널을 쓴다.
- 런타임 래스터 이미지는 없다. 겨울 마을 미리보기는 USS 도형으로 구성해 확대해도 흐려지지 않는다.

참고 자료:

- https://www.nintendo.com/us/store/products/overcooked-2-switch/
- https://www.team17.com/news/the-a-z-of-overcooked-2
- https://www.nintendo.com/eu/media/downloads/games_8/emanuals/wii_u_6/super_mario_3d_world_3/ElectronicManual_WiiU_SuperMario3DWorld_EN.pdf
- https://docs.unity3d.com/6000.0/Documentation/Manual/UIE-faq-event-and-input-system.html

## 동작

- 패널 진입 시 `WINTER VILLAGE` 버튼에 포커스를 준다.
- 선택 상태는 `PPack.SelectedStage = winter-village`로 저장한다.
- 잠긴 노드는 `Button`이 아닌 `VisualElement`라 마우스와 키보드 선택 대상이 아니다.
- 패널 전환은 기존 `OutGameTransitionWipe`의 제자리 페이드만 사용한다.

## 에셋

- 폰트: 공용 `LilitaOne-Regular.ttf`
- 텍스처/Material: 없음
- 래스터 확대: 없음
- 기준 해상도: 1600 x 900
- 시각 검수: `Preview/StageSelectPreview.png`
