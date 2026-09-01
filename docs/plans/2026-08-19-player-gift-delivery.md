# 플레이어 선물 배달 루프 — 구현 계획

> 스펙: [2026-08-19-player-gift-delivery.md](../specs/2026-08-19-player-gift-delivery.md)
> 브랜치: `/main/player-gift-delivery`

**목표**: 집 앞 선물 수령 판정과 무한 주문 루프를 `Delivery/`에 얹는다. 눈덩이·눈밭은 다른 작업이
소유하므로 건드리지 않고, 그쪽 구현 없이도 `GiftSpawner`로 루프 전체를 검증한다.

**접근**: 새 코드는 전부 `Delivery/Scripts/`에만 넣는다. 도로망(`DeliveryRoadNetwork` 등)은
재사용하고, 트럭 시스템은 코드를 남긴 채 씬 빌더에서 비활성화한다.

---

## 전역 제약

- 네임스페이스 `PPack` 하나. 비공개 필드 `_camelCase`, 타입·메서드 `PascalCase`, enum `E` 접두사.
- 눈덩이 쪽에 어떤 호출 규약도 요구하지 않는다 — `Gift`/`GiftDropZone`만으로 판정한다.
- `DeliveryFactory`, 트럭 5종, `DeliveryTrafficController`, `DeliveryYield*`, `DeliverySnowClearance`는
  손대지 않는다. `Snow/`, `Player/`, `Vehicle/`도 건드리지 않는다.
- 멀티플레이 전환(공유 주문 N건, 실패 시 전원 종료)에 맞춰 `ActiveOrders`는 처음부터 목록,
  `GiftDeliveryOrder`는 오브젝트 참조 없이 값 타입만.

---

## 태스크 1 — 판정 원자 단위

- `Gift.cs` — 값어치·`Id`·`Gift.All` 정적 레지스트리.
- `GiftEntry`/`GiftAcceptance.cs` — 정원 초과 시 값어치 내림차순 + `Id` 오름차순 결정론적 선별.
- `GiftDropZone.cs` — 방향 있는 박스 범위, `Evaluate()`가 초과분을 `Destroy`.
- `DeliveryRoadNetwork.FindNearestNode(Vector3)` 추가.

검증: `GiftAcceptanceTests`(EditMode) — 정원 이하 전부 수용 / 초과 시 값어치 상위만 / 동점 결정론.

## 태스크 2 — 주문·난이도·집 선정

- `DeliveryHouse.cs` — 배달 목적지, `DeliveryFactory`와 같은 오브젝트에 공존.
- `GiftDeliveryOrder.cs` — `HouseIndex`(int) 기반, `Tick`/`TryComplete`/`Fail`.
- `GiftDeliveryDifficulty.cs` — 완료 수 → 목표 길이·시간 여유·요구 선물 수·값어치 순수 함수.
- `GiftDeliveryHouseSelector.cs` — 참가자 최단 경로 기준 목표 길이에 가장 가까운 집 선정, 진행
  중·최근 사용 집 제외.

검증(EditMode): `GiftDeliveryOrderTests`, `GiftDeliveryDifficultyTests`, `GiftDeliveryHouseSelectorTests`,
`DeliveryRoadNetworkTests`.

## 태스크 3 — 루프 허브 + 테스트용 공급

- `GiftDeliveryDirector.cs` — `EGiftDeliveryPhase{Idle,Running,GameOver}`, `FixedUpdate` 틱,
  `Start()`에서 자동 `Begin()`(직렬화 필드는 도메인 리로드를 넘기지만 `Phase`는 아니므로).
  `_wrongHouseFails` 기본 `false`.
- `GiftSpawner.cs` — 테스트용 최소 선물 스포너.

검증(PlayMode): `GiftDeliveryLoopPlayModeTests` — 성공→스태거→다른 집, 시간 초과→게임오버 1회,
정원 초과 소멸, 오배달 토글 온/오프, 참가자 2명 동시 주문 2건, 동시 주문 중 하나만 실패해도
게임오버.

## 태스크 4 — 씬 빌더 통합

- `DeliverySceneRigBuilder.BuildFactories`가 `DeliveryHouse`+`GiftDropZone`도 붙이도록 확장
  (`GiftZoneSize`/`GiftZoneCapacity` 공유 상수).
- `BuildRig`에서 NPC `DeliveryDirector` 비활성화(`_maxConcurrentTrucks=0`, `enabled=false`),
  `GiftDeliveryDirector` 생성·설정, `GiftSpawner` 테스트 리그 추가.
- 메뉴 `PPack/Delivery/Build Request Flow Test Scene` 재실행으로 `Delivery_RequestFlow_Test.unity`
  갱신 — Delivery/AGENTS.md 규칙대로 손으로 고치지 않는다.

검증: 씬 재생성 후 Play 모드로 실제 루프 확인(주문 선정 → 선물 배치 → 완료 → 다음 집).

## 태스크 5 — 문서

- `Delivery/AGENTS.md`에 트럭→플레이어 전환, 눈덩이 무규약 원칙, 멀티 대비 데이터 모양,
  임시 밸런싱 값, 알려진 한계(오배달 유예 없음) 기록.
- `docs/Glossary.md`에 신규 용어 등록, `docs/INDEX.md`에 한 줄 hook.

---

## 검증 요약

- EditMode: 기존 6종 + 신규 5종, 전체 통과.
- PlayMode: 기존 3종 + 신규 1종(7개 테스트 케이스), 전체 통과.
- 통합 씬 재생성 + Play Mode 실측: 주문이 도로망 기준으로 뜨고, 선물 배치 시 완료되며, 최근 집을
  제외하고 다음 주문이 다른 집으로 이동하는 것을 확인.
