# 보행자 NPC 기본 시스템 계획

> 소유 폴더: `Assets/Game/InGame/Interaction/Npc/Pedestrian/`

## 결정

눈덩이를 만들어 던지는 `Children`을 NPC의 첫 구현으로 확장하지 않는다. 해당 기능은 제거하고,
GTA·Goat Simulator·Amazing Frog처럼 평소에는 가볍게 배회하다 물리 사건에 반응하는 보행자를 첫
구체 NPC로 삼는다. 처음부터 많은 행동을 만들지 않고 아래 세로 조각만 완성한다.

```
Idle ↔ Wander
Impact → Ragdoll → GettingUp → Timid:Flee / Aggressive:Attack
Witness                         → Timid:Flee / Aggressive:Attack
```

## 소유권

| 소유자 | 책임 |
|---|---|
| Behavior Designer 트리 | 행동 우선순위, 전이, 성격에 따른 다음 반응 |
| `PedestrianContext` | 충격·사건 입력, 회복 시간, 현재 실행 출력과 완료 통지 |
| 보행자 실행기 | NavMesh 목적지, 추적, 밀치기, 서버 루트 Rigidbody |
| 래그돌 표현 | Animator·IK 중지, 클라이언트 뼈 물리, 일어나기 정렬 |
| 사건 허브 | 충격 사건을 반경 내 후보에게 한 번 전달 |
| 애니메이터 드라이버 | 실제 루트 변위와 상태 시퀀스를 애니메이션으로 표현 |

판단과 실행은 분리하지만 첫 소비자 하나뿐인 인터페이스나 베이스 클래스는 만들지 않는다.
처음부터 Behavior Designer 트리가 정책을 소유한다. 컨텍스트는 사실과 실행 결과만 보관하고,
숨은 상태 기계로 다음 행동을 선택하지 않는다.

## 구현 순서와 검증

1. ✅ 반응형 Selector 그래프와 `Ragdoll/GettingUp/Flee/Attack` Task, 두 성격, 컨텍스트 계약을 만들고
   EditMode에서 그래프 역직렬화·노드 연결·약한 충격·강한 충격·회복·반응 완료를 검증한다.
2. NavMesh 배회·도망·추적을 붙이고 막힌 목적지 재표본과 행동 종료를 검증한다.
3. 서버 루트 Rigidbody 비행·정착·회복을 붙이고 최소/최대 회복 시간과 추가 충격을 검증한다.
4. Humanoid 뼈 래그돌 표현과 Getting Up 전환을 붙여 시각 검증한다.
5. 사건 허브에 거리·시야각·차폐·중복 필터를 붙여 벽 뒤 NPC와 연쇄 반응을 검증한다.
6. 공격을 추적+짧은 물리 밀치기로 완성하고 체력 없이도 상태가 정상 종료되는지 검증한다.
7. Fusion 서버 권위 상태·충격 원인·반응 위치·시퀀스를 복제하고 뼈 Transform이 복제되지 않는지
   확인한다.
8. `-batchmode -nographics`에서 판단·충돌·회복이 Animator와 그래픽 장치 없이 도는지 확인한다.

검증 씬은 `Pedestrian/Tests/Npc_ImpactReaction_Test.unity`에 두며 Build Settings에는 넣지 않는다.

2026-08-25: 1단계 완료. Pedestrian EditMode 11/11 통과. 그래프는 YAML을 손대지 않고 Behavior
Designer `Subtree.AddNode`·`Serialize` API로 생성했으며, 생성 전용 Editor 코드는 제거했다.

## 이번에 하지 않는 것

인구 디렉터·풀링·AI LOD·스케줄·기억·활동 지점·다수 성격 수치·체력·사망·정밀한 네트워크 뼈
래그돌은 첫 세로 조각이 실제 플레이에서 성립한 뒤 추가한다.
