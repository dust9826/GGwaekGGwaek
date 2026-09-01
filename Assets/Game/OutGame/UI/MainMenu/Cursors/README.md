# Main Menu Cursor

`LoadingScreen`의 손그림풍 청소기와 동일한 시각 언어로 제작한 MainMenu 전용 커서입니다.

- `Cursor_Default.png`: 흰색 포인터 + 코랄 패널 + 회색 청소 헤드
- `Cursor_Hover.png`: `Cursor_Default`와 같은 흰 포인터 형태의 폴백 복사본
- `Cursor_Click.png`: `Cursor_Default`와 같은 흰 포인터 형태의 폴백 복사본
- 원본 크기: 64 × 64 px
- Unity 임포트 크기: 32 × 32 px
- 실제 보이는 크기: 약 21 × 21 px
- 핫스팟: `(1.5, 1.5)` — 세 상태에서 동일
- Unity Texture Type: Cursor

원본 래스터는 프로젝트 전용으로 제작했으며 외부 에셋을 사용하지 않았습니다. `MainMenuCursor`는 `Cursor_Default`의 동일한 32px 픽셀과 알파 영역으로 런타임 호버·클릭 색상본을 한 번 만들고, OS `Cursor.SetCursor`로 기본 흰색 → 호버 하늘색 `#55BFE2` → 클릭 파랑 `#2F83CF`을 즉시 교체합니다. UI Toolkit 위에 별도 커서를 그리지 않으므로 이동 잔상이나 프레임 지연이 없고, 상태별 크기와 실루엣도 같습니다. 크기·스케일·회전 애니메이션과 클릭 반짝이·파티클은 사용하지 않으며, MainMenu를 벗어나면 OS 기본 커서로 복원합니다.
