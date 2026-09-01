# InGame/Interaction — 충격에 반응하는 프롭과 행위자

굴러오는 눈덩이(`../Snow/`)나 차량이 부딪히면 반응하는 프롭들(`Door/`·`Box/`), 그리고 맵에서
독립적으로 행동하는 행위자 기반(`Npc/`). 프롭과 NPC 모두 Snow의 구현 세부사항을 참조하지 않고
Unity 기본 물리 계약만 본다:

| 부딪히는 쪽이 지켜야 할 계약 | 이유 |
|---|---|
| 서버에서 non-kinematic `Rigidbody`, 실제 kg `mass` | 세기 계산이 `collision.rigidbody.mass` 에서 나온다 |
| trigger 아닌 `Collider` | `OnCollisionEnter` 가 안 뜬다 |

`Door/`·`Box/`·`Npc/`는 Snow 성장 공식이 바뀌어도 영향을 받지 않는다.

## 폴더 구조

프롭별로 `Door/`·`Box/` 하위 폴더를 나누고, 각각 `Prefabs`·`Scripts`·`Tests`를 갖는다(프로젝트
루트 규칙의 "폴더 구조" 절과 같은 모양 — 피처가 자기 것을 전부 들고 있는다). **두 프롭이 같이
쓰는 것만 `Interaction/` 바로 아래에 남는다** — `ImpactMomentum.cs`(운동량 계산),
`ImpactReceiver.cs`(충돌 콜백 밖의 직접 공격·임펄스 전달)와
`TestImpactLauncher.cs`(검증 씬 전용 키 입력 헬퍼). 셋째 프롭이 생겨서 셋 다 쓰는 게 늘어나기
전엔 이 둘을 각 폴더로 복제하거나 억지로 나누지 않는다.

```
Interaction/
  AGENTS.md
  Scripts/ImpactMomentum.cs        ← Door·Box·Thief 공용
  Scripts/ImpactReceiver.cs        ← Penguin·Thief 공용 외부 충격 진입점
  Tests/TestImpactLauncher.cs      ← Door·Box 공용, 검증 씬 전용(프로덕션 아님)
  Door/
    Prefabs/PF_DoubleDoor.prefab
    Scripts/DoorSwing.cs, ImpactDoor.cs
    Tests/Interaction_ImpactDoor_Test.unity, EditMode/DoorSwingTests.cs
  Box/
    Prefabs/PF_BreakableCrate.prefab
    Scripts/ImpactBreakable.cs
    Tests/Interaction_BreakableCrate_Test.unity
  Npc/                               ← 독립 NPC 공통 기반과 선택적 Behavior Designer 연동
    Scripts/NpcGroupContext.cs, NpcGroupMember.cs, NpcSpawnPoint.cs
    Behavior/NpcBehaviorTreeAuthority.cs, Tasks/...
    Pedestrian/                      ← 배회·충격·목격 반응 행동 트리를 갖는 첫 구체 NPC
  Traffic/                           ← Delivery 도로 그래프를 읽는 주변 차량과 플레이어 충돌 임펄스
```

## 지금 있는 것

| | |
|---|---|
| `Scripts/ImpactMomentum.cs` | 충돌 하나에서 운동량을 뽑는 공용 유틸리티. `ImpactDoor`·`ImpactBreakable` 둘 다 쓴다 |
| `Scripts/ImpactReceiver.cs` | 직접 공격·외부 임펄스를 원인과 함께 전달하는 공용 진입점. Penguin·Thief가 구현한다 |
| `Tests/TestImpactLauncher.cs` | 검증 씬 전용 키 입력 헬퍼(1=약, 2=강, R=리셋) — 프로덕션 코드 아님 |
| `Door/Scripts/DoorSwing.cs` | 문 한 짝의 각도 적분기. 순수 C#, `UnityEngine` 참조 0 — 데디 서버·EditMode 양쪽에서 돈다 |
| `Door/Scripts/ImpactDoor.cs` | 충돌 → 각운동량 → `DoorSwing`, 복제, MMF 피드백. `NetworkBehaviour` |
| `Box/Scripts/ImpactBreakable.cs` | 충돌 → 운동량 문턱 → 부서짐(1비트), 파편은 로컬 전용. `NetworkBehaviour` |
| `Door/Prefabs/PF_DoubleDoor.prefab` | 두 짝 문. 각 짝이 독립된 `ImpactDoor` — 서로 참조하지 않는다 |
| `Box/Prefabs/PF_BreakableCrate.prefab` | 부서지는 상자. `ImpactBreakable` 한 개 |
| `Door/Tests/Interaction_ImpactDoor_Test.unity` | 문 검증 씬. **Build Settings 에 넣지 않는다** |
| `Box/Tests/Interaction_BreakableCrate_Test.unity` | 상자 검증 씬. **Build Settings 에 넣지 않는다** |
| `Door/Tests/EditMode/DoorSwingTests.cs` | `DoorSwing` 순수 로직 6종 |
| `Npc/` | 독립 NPC 공통 기반·선택적 Behavior Designer 연동. 자세한 내용은 [폴더 규칙](Npc/AGENTS.md) |
| `Npc/Pedestrian/` | 충격·래그돌·회복·목격 반응 우선순위를 Behavior Designer 트리로 갖는 첫 구체 NPC |
| `Traffic/` | 도로별로 다른 목적지를 이어 달리는 주변 차량. 펭귄 충돌 시 Rigidbody 임펄스를 적용한다 |

## 문 — 상태는 각도 하나

열림/닫힘 bool 은 없다. `AngleDeg` 가 `0`이면 닫힘, `-MaxAngleDeg..MaxAngleDeg` 어디든 갈 수 있다.
**양방향이다** — 밀어도 당겨도(어느 면에서 부딪히든) 0을 중심으로 반대쪽까지 열린다. 되돌릴 수
있다 — 열린 문을 반대로 밀면 닫히는 쪽으로 돌아온다.

**두 짝이 각자 독립된 힌지다.** `PF_DoubleDoor` 는 빈 부모고, `Leaf_Left`·`Leaf_Right` 가 각각 자기
`transform.position`을 힌지 축의 한 점, `transform.up`을 힌지 축으로 쓴다(`ImpactDoor.cs` 의
"이 오브젝트가 곧 힌지다" 절). `Leaf_Right`는 `Leaf_Left`를 복제해 Y로 180도 돌려 만들었다 — 같은
로컬 오프셋 값(`box.center.x = +0.45`)이 반대쪽에서는 자동으로 도어웨이 중심을 향하므로, 두 짝이
서로 다른 컴포넌트 값을 가질 필요가 없다.

**래치는 근접 판정이다.** 각도가 0에 `_latchAngleDeg`(기본 3도) 만큼 가까울 때만 걸린다. 이때
충격의 크기(부호 무관)가 `_latchBreakL` 을 못 넘으면 각속도를 전혀 주지 않는다 — 문이 이미 래치
밖으로 나가 있으면 래치가 없다(살짝 밀면 살짝 움직인다). **`_latchBreakL` 은 실측 전 자리값이다.**
"씨앗 크기 눈덩이(반지름 0.18m, ≈60kg)로는 못 열고 한 번 굴려 키워야 열린다"가 기준 — 실제
게임플레이 씬에서 눈덩이로 부딪혀 보고 정할 것.

**지렛대가 물리에서 그냥 나온다.** 충돌 지점과 힌지 축 사이의 외적으로 각운동량을 계산하므로
(`ImpactDoor.OnCollisionEnter`), 경첩 바로 옆을 치면 같은 운동량으로도 안 열리고 손잡이 쪽을 치면
열린다 — 문턱값을 따로 튜닝할 필요가 없다.

**막힌 충돌의 피드백은 각도로 표현하고 복제하지 않는다.** `RattleDeg` 가 `AngleDeg` 에 더해져
`DisplayAngleDeg` 를 만든다(문은 실제로 덜컹거리지만 열리지는 않는다). 복제되는 것은 "히트
카운터 + 열렸는지 + 세기"뿐이고, 각 피어가 그 원인을 보고 자기 쪽에서 `Kick()`(덜컹)과
`MMF_Player`(소리·발광)를 재생한다 — 결과 자체(덜컹의 실제 파형, 소리 재생)는 복제하지 않는다.
루트 `AGENTS.md` 의 눈 전파 원칙과 같은 이유다.

⚠ **덜컹 스프링은 닫힌 형식으로 적분한다 — 오일러가 아니다.** 임계 감쇠 스프링을 전진/semi-implicit
오일러로 풀면 `ω·dt` 가 1에 가까울 때 발산한다(실측: 9Hz 스프링을 60Hz 물리 틱으로 스텝하면 1초
만에 4×10⁸도까지 튄다 — `DoorSwingTests.덜컹은_시간이_지나면_0으로_돌아오고_문은_안_열린다` 가 이
회귀를 잡는다). 임계 감쇠는 선형 상미분방정식이라 닫힌 해가 있고 `dt` 가 얼마든 안정적이다.

⚠ **각속도 상한(`MaxAngVelDegPerS`, 180°/s)이 없으면 물리가 폭발한다.** 문은 kinematic
`Rigidbody.MoveRotation` 으로 도는데, 스윕 콜리전이 없어서 무거운 충격이 한 스텝에 문을 몇백 도
돌리면 겹쳐 있던 콜라이더를 그대로 관통하고, 겹침 해소가 폭발적인 속도로 물체를 날린다(실측: 씨앗
크기 눈덩이로도 발생, 좌표가 수백 단위로 튀었다). 상한을 걸고, **부딪히는 쪽(눈덩이·차량)의
`Rigidbody.collisionDetectionMode` 를 `ContinuousDynamic` 으로 두는 것도 같이 필요하다** —
테스트 씬의 `TestPusher` 가 그렇게 돼 있다. 새 프롭을 밀 물체를 추가할 때 이 설정을 빠뜨리면 같은
폭발이 재현된다.

## 상자 — 부서짐은 1비트다

각도처럼 이어지는 상태가 아니라 "부서졌는가" 하나뿐이고, 되돌릴 수 없다. 부서지기 전엔 문과
같은 이유로 kinematic이다(약한 충돌에 밀려다니지 않는다) — 다만 회전하지 않으므로 문의 각속도
상한·`MoveRotation` 스윕 불안정은 여기 해당하지 않는다: 부서지는 순간 콜라이더를 끄고 `Visual`을
숨길 뿐이라 폭발할 물리 상태 자체가 없다.

**문턱은 운동량 그대로다.** `ImpactMomentum.TryCompute` 가 뽑은 `kg·m/s` 값을 `_breakMomentumKgMps`
와 직접 비교한다 — 문과 달리 지렛대(각운동량)가 필요 없다, 상자는 돌지 않고 사라지기 때문이다.
**실측 전 자리값(90)이다.**

**파편은 복제하지 않는다.** 서버가 복제하는 것은 "부서졌는가" `[Networked] NetBroken` 한 비트뿐이고,
각 피어가 그 비트가 바뀐 것을 보고(`Update` 폴링, `ImpactDoor` 의 히트 카운터 폴링과 같은 패턴)
자기 쪽에서 `GameObject.CreatePrimitive(Cube)` 로 파편 조각을 즉석에서 만들어 날린다. 데디 서버는
`SystemInfo.graphicsDeviceType == Null` 검사로 파편 생성 자체를 건너뛴다 — 표현 계층이 없으니
만들 이유도 없다.

## 아직 없는 것

- `_latchBreakL`(문)·`_breakMomentumKgMps`(상자) 의 실측 확정. 둘 다 "씨앗 크기 눈덩이로는 안
  되고 한 번 굴려 키워야 된다"가 기준.
- 레이어 필터. 지금은 문턱값만으로 거른다 — 펭귄이 걸어와 부딪히는 게 실제로 문제가 되면 그때
  `LayerMask` 를 붙인다.
- 문·상자 모델은 임시 박스(`Cube`)다. 각 프리팹의 `Visual` 자식의 `MeshFilter.sharedMesh` 와
  콜라이더만 갈아 끼우면 되도록 루트(`Rigidbody`+`BoxCollider`+로직 컴포넌트)는 그대로 둔 채
  분리해 뒀다.
- 상자가 부서질 때 청결도·점수에 들어가는지는 이 폴더 밖 결정이다. 지금은 순수 연출이다.
