# Main Menu Audio

- `BGM_CleanUpDrive.mp3`
  - 원본 파일명: `Snowball Delivery.mp3`
  - 출처: 프로젝트 소유자가 직접 제공한 파일
  - 라이선스: 프로젝트 소유자가 사용 권한을 보유한 제공 에셋
  - 길이: 약 174.8초
  - 형식: MP3, 스테레오
  - 용도: `MainMenu` 씬에서만 반복 재생되는 2D 배경 음악
  - 선택 이유: 평화롭고 신나는 선물 배달 분위기를 전달하는 전용 로비 BGM
  - Unity 임포트: `Streaming`, Vorbis 품질 0.7, Background Loading, Preload 비활성화

이 BGM 오브젝트는 `DontDestroyOnLoad`를 사용하지 않습니다. 따라서 MainMenu를 떠나 LoadingScreen 또는 게임 플레이 씬으로 전환하면 음악도 함께 종료됩니다.

## UI 버튼 SFX

- `UI/UI_Click_Dustyroom.wav`
  - 원본 파일명: `DM-CGS-21.wav`
  - 출처: Dustyroom `FREE Casual Game SFX Pack`
  - 라이선스: CC0
  - 길이: 약 0.121초
  - 용도: 설정 토글·코드 복사·종료 등 일반 Main Menu 기능 클릭
  - 재생 볼륨: `0.30`
  - 선택 이유: 짧고 둥근 팝 계열이라 반복되는 일반 조작에 부담이 적음
- `UI/UI_Hover_Dustyroom.wav`
  - 원본 파일명: `DM-CGS-20.wav`
  - 출처: Dustyroom `FREE Casual Game SFX Pack`
  - 라이선스: CC0
  - 길이: 약 0.097초
  - 용도: 모든 Main Menu `.flow-button` 포인터 진입
  - 재생 볼륨: `0.18`
  - 선택 이유: 클릭음과 같은 팩의 인접 변형이라 음색은 유지하면서 더 가볍게 들림

- `UI/UI_Click_Casual.ogg`
  - 원본 파일명: `confirmation_001.ogg`
  - 출처: Kenney `Interface Sounds`
  - 라이선스: CC0
  - 길이: 약 0.290초
  - 용도: 같은 문서 안의 패널 이동과 `BACK`, `LEAVE`, `GOT IT`
  - 재생 볼륨: `0.24`
  - 선택 이유: 기능 실행음보다 부드러워 화면 탐색과 실행을 귀로 구분할 수 있음

- `UI/UI_Click_Confirm.wav`
  - 원본 파일명: `click3.wav`
  - 출처: Kenney `UI SFX`
  - 라이선스: CC0
  - 길이: 약 0.086초
  - 용도: 방 생성·참가, 게임 시작, 제설대 출동처럼 상태를 실제로 진행하는 행동
  - 재생 볼륨: `0.34`
  - 선택 이유: 짧은 고음 어택으로 실행 확정을 분명하게 전달하면서 전환을 지연시키지 않음

두 파일 모두 Unity Asset Store의 [Dustyroom FREE Casual Game SFX Pack](https://assetstore.unity.com/packages/audio/sound-fx/free-casual-game-sfx-pack-54116)에 포함된 같은 팝 계열의 인접 변형입니다. 제작자 공식 배포 페이지에서는 팩 전체를 CC0로 제공합니다. 원본 라이선스와 파일 대응은 `UI/LICENSE-Dustyroom-Free-Casual-Game-SFX.txt`, `UI/LICENSE-Dustyroom-Free-Casual-Game-SFX.pdf`에 보존했습니다.

`UI_Hover_Short.wav`와 `UI_Hover_Casual.ogg`는 비교 및 롤백용으로 유지하며 Main Menu 런타임에서는 사용하지 않습니다. Kenney 클릭 두 종은 탐색과 확정 행동에만 제한적으로 사용해 Dustyroom 기본 음색을 덮지 않습니다.

호버는 포인터 진입 시 한 번만 재생하며 기존 0.05초 재생 제한과 버튼별 피치 변화를 유지합니다. 클릭과 호버 모두 `PPack.UiSoundEnabled` 설정을 따릅니다.
