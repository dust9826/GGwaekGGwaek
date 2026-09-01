# 플레이어 선물 배달 루프

## 목표

기존 NPC 배송 트럭(`Delivery/`)을 **플레이어가 직접 선물을 배달하는 무한 루프**로 바꾼다. 눈덩이가
선물을 굴려 지정된 집 앞까지 옮기면, 제한 시간 안에 도착해야 한다. 실패(시간 초과)는 **그 자리에서
게임 종료**다. 성공하면 짧은 대기 후 다음 집이 정해지며 무한 반복된다. 별도의 5분 스테이지 제한은
없으며, 주문 제한시간이 유일한 종료 시간 규칙이다.

## 경계 — 이번에 만드는 것과 다른 작업이 만드는 것

| 이번에 만드는 것 | 다른 작업 / 나중 |
|---|---|
| 집 앞 선물 수령 판정 (배치 범위, 정원, 초과분 소멸) | **눈덩이 전부** — 굴리기, 성장, 선물 흡수 |
| 주문 루프 (집 선정·난이도·제한시간·성공/실패/게임오버) | **눈밭(`SnowField`/`SnowStage`) 전부** |
| 도로망 기반 난이도 계산 | 별 루브릭·결과 UI 개편 |
| 트럭 시스템 **비활성화** (코드는 유지) | 트럭 코드 삭제 |

**눈덩이에는 아무 규약도 요구하지 않는다.** 이 기능은 눈덩이 구현을 전혀 모르고, 그 작업이
이 기능보다 우선이다. 판정은 `Gift` 컴포넌트가 붙은 활성 오브젝트가 좌표상 집 앞 범위 안에
있는가만 본다 — 등록 콜백도, 상태 API도, 인터페이스 요구도 없다. 규칙이 나중에 바뀌면
`GiftDropZone.Evaluate` 한 곳만 고친다.

## 도로망은 그대로 재사용한다

`DeliveryRoadNetwork`/`DeliveryRoadSegment`/`DeliveryRoadCurve`/`DeliveryRoutePlanner`/`DeliveryRoute`는
공장이 아니라 노드 기반이라 손대지 않고 재사용한다. `DeliveryFactory`와 트럭 관련 코드는 그대로
컴파일되게 유지하고 씬 빌더에서 비활성화만 한다 — 지우면 네트워크 검증(`TryValidate`)이 요구하는
"공장 최소 2곳" 제약과 얽힌 코드가 깨진다.

새로 만든 것 하나: `DeliveryRoadNetwork.FindNearestNode(Vector3)` — 참가자 위치에서 경로를 계획하기
위한 최근접 노드 조회.

## 새 컴포넌트

전부 `Assets/Game/InGame/Delivery/Scripts/`, 네임스페이스 `PPack`.

- **`Gift`** — 값어치(`Value`)와 안정적인 `Id`만 가진 마커. `Gift.All`로 자가 등록.
- **`GiftDropZone`** — 집 앞 선물 배치 범위(방향 있는 박스). `Evaluate()`가 정원 초과분을 값어치
  낮은 순으로 `Destroy`한다.
- **`GiftAcceptance`** — 정원 초과 시 무엇을 받고 무엇을 버릴지 결정하는 순수 함수. 값어치
  내림차순, 동점이면 `Id` 오름차순 — 결정론적.
- **`DeliveryHouse`** — 배달 목적지. 기존 `DeliveryFactory`(트럭용)와 같은 오브젝트에 공존한다.
- **`GiftDeliveryOrder`** — 한 집에 선물 몇 개·총 값어치 이상을 제한 시간 안에. `HouseIndex`(int)만
  들고 오브젝트 참조는 없다.
- **`GiftDeliveryDifficulty`** — 완료 수에 따라 목표 경로 길이·시간 여유·요구 선물 수·요구 값어치를
  계산하는 순수 함수. 초반엔 쉽고 점진적으로 오른다.
- **`GiftDeliveryHouseSelector`** — 목표 길이에 가장 가까운 집을 고른다. 진행 중·최근 사용 집은
  제외.
- **`GiftDeliveryDirector`** — 권위 허브. 주문 발행·판정·게임오버를 소유한다.
- **`GiftSpawner`** — 플레이어가 직접 밀 수 있는 동적 선물을 공급하는 스포너.
- **`GiftDeliveryHudPresenter`** — 현재 목표 집·남은 주문 시간·요구량·완료 수를 HUD에 연결.

## 멀티플레이 전환에 맞춘 데이터 모양

싱글플레이만 지금 구현하지만, 곧 멀티로 확장한다. 확정된 규칙: **주문은 전원이 공유하며 여러 건이
동시 진행**되고, **한 건이라도 실패하면 전원 게임 종료**. 이 규칙에 맞춰 처음부터:

- `ActiveOrders`는 목록이다. 동시 주문 수 = `clamp(참가자 수 × _ordersPerParticipant, 1, 집 수)`.
- 난이도 기준 거리는 **참가자 중 최단 경로**로 잰다 — 가장 가까운 사람이 그 집을 맡는다는 뜻.
- `GiftDeliveryOrder`는 오브젝트 참조 대신 `HouseIndex`(int)만 든다 — Fusion `[Networked]`가 담을
  수 있게.
- `FixedUpdate`/`Time.fixedDeltaTime`으로 돈다 — 나중에 `FixedUpdateNetwork`로 옮기기 쉽게.
- 이번 패스에는 `NetworkBehaviour`를 추가하지 않는다(`Delivery/AGENTS.md` 규칙).

## 미해결 항목

- **밸런싱은 눈덩이가 없는 상태의 추정치다.** `GiftDeliveryDifficultySettings.Default`의 모든 값,
  특히 `AssumedSpeedMps`(4.0)는 눈덩이 실제 이동 속도가 정해지면 다시 재야 한다.
- **오배달 판정(`_wrongHouseFails`)은 기본 꺼짐이다.** 방금 완료된 집에 남은 선물이 다음 틱에
  오배달로 오판정될 수 있는 한계가 있다(`Delivery/AGENTS.md` 참고) — 옵션을 켜는 시점에 다룬다.
- **결과 UI 개편은 아직 범위 밖이다.** 진행 HUD는 현재 플레이어 주문 정보를 표시한다.
- **`NetworkBehaviour` 전환 시점은 미정이다.** Fusion이 프로젝트에 안정적으로 설치된 뒤 결정한다.

## 검증

`Tests/EditMode/`에 순수 로직 테스트(정원 초과, 주문 완료 조건, 난이도 단조성, 집 선정, 최근접 노드),
`Tests/PlayMode/`에 통합 루프 테스트(주문 성공→다음 집 전환, 시간 초과→게임오버, 정원 초과 소멸,
오배달 토글, 멀티 대비 동시 주문). 통합 씬은 `Tests/Delivery_RequestFlow_Test.unity` — 메뉴
`PPack/Delivery/Build Request Flow Test Scene`으로 재생성한다.
