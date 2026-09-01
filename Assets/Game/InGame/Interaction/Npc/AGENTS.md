# InGame/Interaction/Npc — 독립 NPC 공통 기반

맵에 놓이는 NPC의 공통 기반. NPC의 판단과 실행은 각 구체 NPC가 소유하고,
`NpcGroupContext`는 선택적으로 이동 영역·멤버 목록·공유 신호만 더한다. 그룹이 멤버의 행동
메서드를 직접 호출하지 않는다.

## 구조

```
Npc/
  Scripts/
    NpcGroupContext.cs   ← 영역, 멤버 등록, 버전 있는 공유 신호
    NpcGroupMember.cs    ← NPC가 그룹 속성을 조회하고 신호를 발행하는 연결점
    NpcSpawnPoint.cs     ← 맵에 놓는 NPC 한 명의 권위 스폰 지점
    NpcMaleLocomotionAnimator.cs ← 기존 Synty NPC용 이동 표현(보행자는 사용하지 않음)
  Behavior/
    PPack.Npc.BehaviorDesigner.asmdef
    NpcBehaviorTreeAuthority.cs
    Tasks/               ← Behavior Designer Pro 3 커스텀 Task
  Tests/EditMode/
  Pedestrian/             ← 배회·충격·목격 반응을 갖는 첫 구체 NPC
    Scripts/PedestrianContext.cs
    Behavior/PedestrianBehaviorTree.asset, Pedestrian*Tasks.cs
    Tests/EditMode/PedestrianContextTests.cs, PedestrianBehaviorTreeAssetTests.cs
```

## 규칙

- NPC는 그룹 없이도 동작해야 한다. 그룹 관련 Conditional은 그룹이 없으면 실패한다.
- 그룹은 `Pedestrian`, `Penguin` 같은 구체 행위자 타입을 참조하지 않는다.
- 공유 신호는 명령이 아니라 관찰 가능한 그룹 상태다. 각 NPC가 자기 행동 트리에서 반응 여부를
  결정한다.
- 보행자 정책은 처음부터 Behavior Designer 트리가 소유한다. 컨텍스트와 실행기는 관찰 사실과
  물리·이동 결과만 제공하며 별도의 상태 기계로 다음 행동을 선택하지 않는다.
- 권위 판단·NavMesh 이동·충돌·목격 전파·공격 판정은 서버(또는 NetworkRunner가 없는 단독
  테스트)에서만 실행한다. 클라이언트는 복제된 원인과 상태를 표현한다.
- Opsive 패키지는 수정하지 않는다. 연동 코드는 별도 asmdef에 격리한다.
- Behavior Designer 그래프 에셋은 YAML로 손대지 않고 Behavior Designer Editor에서 저작한다.
- 구체 NPC는 이 폴더의 자식으로 둔다. 첫 소비자는 `Pedestrian/`이다.
- 구체 NPC는 자기 모델과 애니메이션 표현을 소유한다. Pedestrian은 Creative Characters 전용
  `AC_Pedestrian`을 사용하며 Synty 이동 표현을 사용하지 않는다.
- `NpcMaleLocomotionAnimator`는 목적지나 이동 권위를 소유하지 않는다. 루트의 실제 수평 변위를
  측정해 Synty의 이동 입력·속도·gait·grounded 파라미터만 구동하며 root motion은 끈다.
- Pedestrian 래그돌은 현재 범위에서 제외했다.
- 목격 반응은 매 프레임 주변 NPC를 스캔하지 않고 사건 하나를 반경 내 후보에게 한 번 전달한다.
  각 NPC는 성격과 현재 상태를 보고 도망 또는 공격을 선택한다.
