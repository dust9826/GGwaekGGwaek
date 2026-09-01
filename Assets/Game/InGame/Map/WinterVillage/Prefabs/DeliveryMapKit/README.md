# Delivery Map Kit

`Delivery_RequestFlow_Test.unity`의 겨울 마을에 직접 배치된 프리팹을 맵 제작용으로 모아 둔 카탈로그다.

- `01_Buildings`: 주택과 주요 건물
- `02_Nature`: 나무, 풀, 바위, 동물
- `03_Props`: 가구, 표지판, 겨울 장식과 생활 소품
- `04_Vehicles`: 배치용 차량
- `05_Lighting_VFX`: 가로등과 환경 VFX
- `06_Gameplay`: 플레이어·눈처럼 맵에서 함께 확인할 게임플레이 프리팹

이 폴더의 프리팹은 원본을 상속하는 Variant다. 원본 에셋을 이동하거나 복제하지 않으므로 기존 씬의
참조를 깨뜨리지 않으며, Project 창에서 원하는 Variant를 맵으로 바로 드래그해 배치할 수 있다.

배송 씬의 맵 프리팹 구성이 바뀌면 메뉴 `PPack/Map/Winter Village/Build Delivery Map Prefab Kit`를
다시 실행한다. 현재 씬에서 사라진 항목은 카탈로그에서도 제거된다. Project 창에서 폴더를 바로
찾으려면 `PPack/Map/Winter Village/Open Delivery Map Prefab Kit`를 사용한다.
