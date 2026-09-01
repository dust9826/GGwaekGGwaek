# Vehicle Route Minimap

차량 주행 중 실제 맵의 배치와 다음 청소 구간을 빠르게 읽게 하는 탑다운 미니맵이다. Map의 `MapMinimapBounds`를 기준으로 차량, 장애물과 Trash 프롭의 X/Z 좌표를 동일 비율로 투영한다.

## 테스트 씬

- `../Scenes/VehicleRouteMinimap_UI_Test.unity`
- `Vehicle/Tests/Vehicle_Prototype_Test.unity`를 복제해 차량, 카메라, 먼지 바닥과 흡입 프롭을 그대로 유지하고 UI Toolkit HUD만 추가했다.
- `WASD`: 주행, `Shift`: 부스트, `Space`: 점프, 마우스: 카메라, `R`: 카메라 리셋

## 표시 규칙

- 평소에는 미니맵과 전용 렌더 카메라를 숨기고 비활성화한다.
- `Tab`을 누르고 있는 동안 화면 중앙에 큰 지도를 표시한다. Tab을 놓으면 즉시 접힌다.

- 민트 경로: 실제 쓰레기 위치를 Z 순서로 연결한 테스트용 추천 경로
- 노란 다이아몬드: 실제 Trash 프리팹의 `TrashMapTarget` 위치. 런타임 생성/비활성화 이벤트로 즉시 추가·완료 처리된다.
- 코랄 직사각형: 실제 `JumpObstacle` Collider의 위치와 크기
- 코랄 차량: 실제 `VehicleController`의 X/Z 위치와 Y 회전을 표시
- 반투명 민트 면적: `VehicleMopPad`가 실제 `DustPaintTarget`에 전달한 회전 사각 붓 영역을 96×96 월드 그리드에 누적
- WinterVillage에서는 `SnowVehiclePad.SnowCleared`가 전달한 실제 제설 영역을 같은 민트 면적으로 누적하고 표기를 `SNOW ROUTE / SNOW / DONE`으로 전환
- 민트 원: 수거 완료된 쓰레기의 마지막 월드 위치. 추천 경로에서는 즉시 제외되므로 다시 유도하지 않는다.

진행률은 차량 시작점에서 움직인 최대 거리와 실제 추천 경로의 총 길이를 비교한다. 뒤로 가더라도 이미 확보한 최고 진행률은 감소하지 않는다.

씬 오브젝트가 이동하면 마커도 갱신된다. 쓰레기가 수거되어 제거되면 노란 마커는 민트 완료 마커로 바뀌고 추천 경로에서 빠진다. 바닥의 민트 영역은 차량이 실제로 청소 패드를 접촉시킨 장소만 누적되므로 플레이어가 이미 지나간 구간을 피할 수 있다. 정식 스테이지에서는 오브젝트 이름 검색 대신 Map/Cleanliness가 Bounds와 목표 목록을 전달하도록 교체해야 한다.

## 실제 기능 연결

- `MapMinimapBounds`: Map이 투영할 실제 월드 Bounds를 제공한다. `PF_TestMap`, `PF_TestMapWithoutCar`와 UI 테스트 씬의 Ground에 연결되어 있다.
- `TrashMapTarget`: Trash가 위치·크기와 등록/해제 이벤트를 제공한다. `Trash/Prefabs`의 8종 루트에 모두 부착되어 있다.
- 런타임에 Trash 프리팹을 생성하면 등록 이벤트로 마커가 즉시 추가된다.
- 수거 로직이 Trash 오브젝트를 비활성화하거나 제거하면 완료 마커로 전환되고 남은 목표 경로가 즉시 다시 계산된다.
- UI는 수거 판정, Destroy 또는 점수 상태를 직접 변경하지 않는다.

## UI와 모션

- UI Toolkit의 `Painter2D`로 런타임 경로를 그리므로 경로 텍스처나 Material을 사용하지 않는다.
- 384×384 정사영 카메라가 `MapMinimapBounds` 전체를 위에서 렌더링해 실제 도로·집·자연물 배치를 배경으로 보여준다. HUD가 꺼질 때 카메라와 RenderTexture도 함께 해제된다.
- `VehicleRouteMinimapController`가 차량 상태를 읽고 `generateVisualContent`만 다시 그린다.
- 먼지 맵은 `MopPad.SurfacePainted`, 겨울 맵은 `SnowVehiclePad.SnowCleared`를 읽으므로 판정·눈 격자·렌더링 권위는 건드리지 않는다.
- Tab 지도 열기/닫기와 20% 단위 차량 마커 펀치는 DOTween을 사용한다. 미니맵 경로 자체는 반복 이동하지 않는다.
- 기준 해상도는 1920 x 1080 `Scale With Screen Size`다.

## 에셋과 라이선스

- 폰트: `Assets/Game/Core/UI/Fonts/LilitaOne-Regular.ttf`
- 라이선스: SIL Open Font License 1.1, `Assets/Game/Core/UI/Fonts/OFL-LilitaOne.txt`
- UI Texture: 없음
- Unity Material: 없음
