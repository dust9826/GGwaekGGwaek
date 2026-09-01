# Lobby

Host/Join 화면에서 전달한 닉네임과 방 코드를 보여 주는 로컬 대기실입니다. 최대 네 명의 자리와 Host/Guest 상태를 시각화합니다.

- `COPY CODE`는 시스템 클립보드에 코드를 복사합니다.
- `START GAME` 또는 Guest의 `READY`는 상태 문구만 갱신합니다.
- `LEAVE`는 같은 씬의 `view-home`으로 돌아갑니다.
- 실제 게임 플레이 및 네트워크 씬 전환은 의도적으로 연결하지 않았습니다.
- 독립 씬은 없으며 레이아웃은 `MainMenu/MainMenu.uxml`에 포함됩니다.
