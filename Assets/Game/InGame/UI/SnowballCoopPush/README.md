# Snowball Coop Push HUD

같은 눈덩이를 미는 참가자 전원이 우클릭 타이밍을 맞출 때만 협동 Impulse가 발생한다. HUD는
`SnowBallCarrier`의 복제 상태를 읽어 표시하며 판정이나 물리 권위를 갖지 않는다.
하단 상호작용 안내는 로컬 `PenguinSnowball`과 `PenguinCarry`를 함께 읽는다. 전방 눈덩이는
`E · PUSH`, 가까운 눈덩이·선물은 `F · CARRY`, 둘 다 가능하면 `E / F · PUSH / CARRY`를 표시한다.
자동 접근 중에는 `F · CANCEL`, 등에 안착한 뒤에는 `F · DROP`으로 바뀐다.

눈덩이를 선택해 밀고 있는 동안에는 주문 보드 아래의 화면 왼쪽에 성장 카드가 나타난다. 상단 중앙의
스테이지 시간, 오른쪽 위 점수·미션, 왼쪽 아래 스태미나와 겹치지 않는 HUD 안전 영역이다. 카드에는 현재 지름,
현재 성장 단계, 다음 단계까지 남은 지름이 표시된다. 네 칸의 막대는 각 단계 진행을 뜻하며
파랑(Stage 1) → 초록(Stage 2) → 노랑(Stage 3) → 코랄(Stage 4) 순서다. 이 색 순서는 눈덩이를
선물로 바꿀 때의 선물 단계 색과 같다. Stage 4에서는 표시 크기 상한까지 남은 값을 보여 주고,
상한에 도달하면 `MAX SIZE`로 바뀐다.

- UI Toolkit UXML + USS, 1920×1080 기준 Scale With Screen Size
- Lilita One 공용 폰트 사용
- 래스터 이미지·Material·별도 텍스처 없음
- 모든 요소 `PickingMode.Ignore`; 마우스 입력을 가로막지 않음
