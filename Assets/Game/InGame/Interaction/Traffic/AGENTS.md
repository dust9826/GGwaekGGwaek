# InGame/Interaction/Traffic — 주변 차량 상호작용

도로 위를 자율 주행하며 플레이어와 충돌하면 Rigidbody 임펄스를 주는 주변 차량을 소유한다.
차량 모델은 `../../map asset/Low Poly Locations Ultimate Pack`의 벤더 프리팹을 참조만 하고 수정하지
않는다. 런타임에 프로젝트 소유 루트(`Rigidbody` + 단순 `BoxCollider`) 아래 비주얼로 인스턴스한다.

## 경계

- `TrafficLaneNetwork`가 주변 차량 전용 방향성 차선 그래프를 소유한다. Delivery의 도로·주문·공장·
  트럭 타입은 런타임과 씬 조립 모두에서 사용하지 않는다.
- 도로 한 줄마다 서로 반대 방향인 차선 두 개를 만들고, 맵 경계의 다섯 포털 사이를 최단 경로로
  이동한다. 목적지 포털에 도착하면 풀로 회수한 뒤 다른 포털에서 다시 생성한다. 같은 폐곡선을
  계속 도는 차량은 없다.
- `AmbientTrafficVehicle`은 kinematic Rigidbody의 `MovePosition`/`MoveRotation`으로 경로를 따라간다.
  플레이어를 피하지 않으며, 교차로 앞뒤 3m는 이차 곡선으로 이어 방향 전환을 부드럽게 만든다.
  포털 재스폰은 비활성 상태에서 자세를 먼저 옮기고 한 물리 스텝 뒤 콜라이더를 켠다. 차량의 CCD는
  `Discrete`다. `ContinuousSpeculative`는 이전 포털→새 포털 순간이동을 속도로 추정해 맵 전체에
  가짜 접촉을 만들고, 동적 플레이어를 수천 m/s로 발사하므로 사용하지 않는다.
- Enter Play Mode 옵션은 Domain Reload만 끄고 Scene Reload는 유지한다. 씬 재로드까지 끄면 풀 차량의
  경로·물리 캐시와 플레이어 충격 상태가 이전 Play에서 이어져 새 실행의 초깃값을 보장할 수 없다.
- `AmbientTrafficWorld`가 같은 차선의 시간 간격과 교차로 통행권을 한곳에서 계산한다. 차량끼리의
  PhysX 충돌은 끄고, 한 교차로에는 한 차량만 진입시킨다. 차체끼리 밀어 한곳에 쌓이는 문제를
  개별 센서나 물리 회피로 해결하지 않는다.
- 플레이어 충돌은 먼저 `PenguinImpactRelay.ReceiveExternalImpulse`에 알린 뒤 펭귄 루트 Rigidbody에
  `ForceMode.Impulse`를 한 번 적용한다. 기본 최대값은 180N·s, 위쪽 비율은 0.2다. 이 순서여야 큰
  충격의 회전 제약이 임펄스보다 먼저 풀려 같은 접촉이 공중 회전을 만든다. 이어지는 펭귄 쪽
  `OnCollisionEnter`는 이미 큰 충격 중이면 무시하므로 피드백은 두 번 재생되지 않는다.
- 차량 콜라이더의 상단 0.2m에서 수직 법선으로 만나고 펭귄 무게중심이 접점보다 위에 있으면 차량
  충돌이 아니라 지붕 착지다. 이 접촉에는 임펄스와 큰 충격을 보내지 않아 점프로 차 위에 올라갈 때
  회전 제약이 풀리고 kinematic 차체 모서리에 끼는 현상을 막는다. 앞·옆 충돌은 기존대로 처리한다.
- 스폰 수·모델 선택·씬 배선은 `Editor/AmbientTrafficSceneRigBuilder.cs`가 소유한다.
  2026-08-28부터 SnowDelivery 흐름으로 승격된 SinglePlay에는 주변 차량을 설치하지 않는다.

## 폴더

```
Traffic/
  Scripts/    방향성 차선 그래프, 포털 경로, 중앙 교통 조정, 풀 스폰, 차량 주행·충돌
  Editor/     교통 리그 조립과 벤더 차량 참조
  Tests/      포털 연결/U턴 방지/목적지 다양성, 교차로 통행권, 플레이어 임펄스·지붕 착지 제외
```

테스트 씬을 추가한다면 `Tests/` 아래에 두고 Build Settings에는 넣지 않는다.
