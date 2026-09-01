# InGame/Interaction/Npc/Pedestrian — 보행자 NPC

Creative Characters 외형으로 맵에서 서 있거나 배회하다가 큰 충격에는 피격 애니메이션을 재생하고,
성격에 따라 도망치거나 공격하는 첫 구체 NPC다. 다른 보행자는 사건을 목격하면 같은 성격 규칙으로
반응한다. 래그돌과 Getting Up은 현재 범위에 없다.

## 첫 행동 트리

```
Repeat
└─ Reactive Selector
   ├─ StrongImpact → HitReaction
   ├─ Incident → Aggressive:Attack / Timid:Flee
   └─ NormalBehavior
```

- 성격은 `Timid`와 `Aggressive` 둘뿐이다.
- 공격은 사건 위치까지 접근한 뒤 짧은 공격 애니메이션을 재생한다. 피해·사망은 아직 없다.
- 배회는 스폰 위치 주변의 NavMesh 표본을 사용한다.
- 사건은 위치와 목격 반경으로 한 번 전파한다. 시야각·차폐·위협 대상 추적은 이후 범위다.

## 지금 있는 것

```
Behavior/PedestrianBehaviorTree.asset
Behavior/PedestrianConditionTasks.cs
Behavior/PedestrianActionTasks.cs
Appearance/NpcAppearanceCatalog.asset
Animations/AC_Pedestrian.controller
Prefabs/PF_Pedestrian.prefab
Scripts/NpcProfileData.cs, NpcAppearanceCatalog.cs, NpcAppearanceGenerator.cs
Scripts/NpcProfileRegistry.cs, PedestrianNetworkHub.cs, PedestrianAppearance.cs
Scripts/PedestrianContext.cs, PedestrianIncidentSystem.cs
Scripts/PedestrianBehaviorExecutor.cs, PedestrianAnimator.cs, PedestrianImpactReceiver.cs
Editor/CreativePedestrianAssetBuilder.cs
Tests/EditMode/Pedestrian*Tests.cs, NpcProfileTests.cs
```

2026-08-28부터 SnowDelivery 흐름으로 승격된 SinglePlay에는 보행자 리그를 설치하지 않는다.
기존 `Cleanliness/Scenes/SinglePlay/NavMesh-PedestrianSidewalks.asset`은 Recovery 씬 참조 때문에
즉시 삭제하지 않았으며, 프로덕션 SinglePlay는 이를 참조하지 않는다.

`PedestrianBehaviorTree`가 행동 우선순위와 전이를 소유한다. `PedestrianContext`는 충격·사건 같은
입력 사실과 현재 실행 출력만 보관하며 다음 행동을 선택하지 않는다. 피격·공격·도망 Task는
`PedestrianBehaviorExecutor`의 완료 통지를 기다린다.

`NpcProfileData`는 고유 `NpcId`, 생성 seed, 성격, 여덟 외형 슬롯 ID를 함께 보관한다. 미션은 외형
정보를 단서로 보여 주되 성공 판정은 `NpcId`로 해야 한다. 활성 프로필은 `NpcProfileRegistry`에서
조회할 수 있다. 외형 ID는 에셋 GUID에서 만든 안정적인 값이며 카탈로그 배열 순서를 저장하지 않는다.

## 물리와 네트워크

- 서버는 프로필 생성, 판단, NavMesh와 행동 상태를 소유한다.
- `PedestrianNetworkHub` 한 곳이 프로필 슬롯과 현재 행동을 복제한다. 클라이언트는 복제된 프로필로
  메시를 조립하고 Animator를 표현한다.
- 큰 충격은 `Interaction/Scripts/ImpactMomentum.cs`의 kg·m/s 값을 기준으로 판정한다.

## 자산

- 모델·외형 변형·애니메이션은 새 Creative Characters 자산만 참조한다. Synty와 Mixamo Getting Up은
  이 프리팹에 쓰지 않는다.
- 프로젝트 소유 `AC_Pedestrian`은 `Idle_Relaxed`, `Walk_Forward`, `Run_Forward`,
  `Hit_Reaction_Heavy`, `Attack_Punch` 다섯 클립만 참조하며 root motion은 끈다.
- `CreativePedestrianAssetBuilder`는 벤더 프리팹을 수정하지 않고 카탈로그·컨트롤러·트리·래퍼
  프리팹을 재생성한다.

## 아직 없는 것

- 시야각·차폐가 있는 감지, 실제 공격 대상/피해, 미션 UI·저장 파일.
- 래그돌, 인구 디렉터, 풀링, AI LOD, 일정·기억·활동 지점, 체력·사망.
