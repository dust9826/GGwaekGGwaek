# 눈덩이 던지는 아이들 — 구현 계획

> 폴더: `Assets/Game/InGame/Interaction/Npc/Children/`
> 브랜치: `/main/interaction-children`

**목표**: 맵에 놓으면 아이 3~4명이 눈덩이를 조금씩 쌓아 두고, 다가온 플레이어에게 던지고,
한 명이라도 맞거나 부딪히면 그룹 전원이 도망치면서 **쌓아 둔 재고를 그 자리에 떨군다.**
이 기능의 목적은 위협이 아니라 **눈덩이 수급처**다 — 아이를 쫓아내는 것이 곧 보상이다.

**접근**: `Snow/` 를 한 줄도 참조하지 않는다. `Interaction/` 의 기존 계약(부딪히는 쪽에 요구하는
것은 `Rigidbody` + trigger 아닌 `Collider` 뿐)을 그대로 지키면, 눈 구현이 어떻게 확정되든 이
폴더는 안 바뀐다. 던지는 눈덩이는 이 폴더가 소유하는 **가벼운 투사체**이지 `SnowBallCarrier` 가
아니다.

---

## 전역 제약

- 네임스페이스 `PPack` 하나. 비공개 필드 `_camelCase`, enum `E` 접두사, 직렬화 Unity Object 는 `== null`.
- **`Snow/` 의 어떤 타입도 등장하지 않는다.** `SnowBallCarrier`·`SnowCpuStage`·`SnowField` 금지.
- 권위는 서버 하나. `Runner.IsServer` 밖에서는 판정하지 않고, 클라는 `[Networked]` 상태를 읽어 그린다.
- **GPU 를 전제하지 않는다.** VFX·사운드는 `SystemInfo.graphicsDeviceType == Null` 로 건너뛴다
  (`ImpactBreakable` 과 같은 패턴).
- 애니메이션은 이번 범위 밖이다. 상태(`EChildState`)만 복제하고, 표현 계층이 나중에 그 상태를 읽는다.

### 왜 던지는 눈덩이가 `SnowBallCarrier` 가 아닌가

`SnowCpuStage` 는 `TotalHeightMm + BallHeldMm + UnaccountedOutMm == 초기 총량` 을 불변식으로 갖고
테스트가 이를 검사한다. 아이들은 **주위 눈을 쓰지 않고** 눈덩이를 만들므로, 그 공을 눈 시스템의
공으로 만들면 원장에 없는 **유입항**이 생겨 보존 검사가 조용히 깨진다. 던지는 공을 순수
`Rigidbody` 투사체로 두면 이 문제 자체가 발생하지 않는다.

덤: 투사체가 실제 질량을 가진 `Rigidbody` 이므로 **`ImpactDoor`·`ImpactBreakable` 이 공짜로 반응한다** —
아이가 던진 눈덩이가 문을 덜컹거리게 하고 상자를 부순다. 폴더 계약을 지킨 대가가 아니라 보상이다.

⚠ 투사체는 `collisionDetectionMode = ContinuousDynamic` 이어야 한다. 이건 취향이 아니라
`Interaction/AGENTS.md` 가 실측으로 기록한 요구사항이다(빠른 물체가 문 콜라이더를 관통하면
겹침 해소가 폭발한다).

---

## 폴더 구조

```
Interaction/Npc/Children/
  Scripts/
    ChildBrain.cs         ← 순수 C#, UnityEngine 참조 0 (DoorSwing 과 같은 규약)
    ChildBallistics.cs    ← 순수 C#, 포물선 발사각 역산
    Child.cs              ← NetworkBehaviour. 한 명
    ChildGroup.cs         ← NetworkBehaviour. 3~4명 + 그룹 도망
    ChildSnowball.cs      ← NetworkBehaviour. 투사체
  Prefabs/
    PF_ChildGroup.prefab, PF_Child.prefab, PF_ChildSnowball.prefab
  Tests/
    Interaction_Children_Test.unity
    EditMode/ChildBrainTests.cs, ChildBallisticsTests.cs
```

`Core/Hit/` 에 피격 계약 둘을 새로 만든다 — 아래 태스크 1.

---

## 태스크 1 — 피격 시스템 (효과는 아직 정하지 않는다)

`Core/Hit/SnowballHit.cs`, `Core/Hit/ISnowballHittable.cs`.

`Core/` 인 이유: 던지는 쪽은 `InGame/Interaction/`, 맞는 쪽은 `InGame/Penguin/` 이다. **첫날부터
소비자가 둘**이라 "두 번째 호출부에서 승격" 규칙을 이미 만족한다.

```csharp
public readonly struct SnowballHit
{
    public Vector3 Point;            // 첫 접촉점
    public Vector3 Normal;           // 접촉 법선
    public Vector3 Direction;        // 날아온 방향(정규화)
    public float   MomentumKgMps;    // ImpactMomentum 과 같은 척도
    public GameObject Source;        // 던진 주체. 없을 수 있다
}

public interface ISnowballHittable { void OnSnowballHit(in SnowballHit hit); }
```

- `Penguin/Scripts/PenguinSnowballHit.cs` — 수신기. **게임플레이 효과 0.** 서버가
  `[Networked] NetHitCount` 를 올리고, 각 피어가 그 카운터가 변한 것을 보고 자기 쪽에서 연출을
  재생한다(`ImpactDoor` 의 히트 카운터 폴링과 같은 패턴). `LastHit`(로컬)과 `event HitReceived`
  를 공개해 넉백·감속·스턴 중 무엇을 붙이든 **여기 한 곳만 구독**하면 되게 둔다.
- `Child` 도 같은 인터페이스를 구현한다 — 플레이어가 던진(또는 굴린) 것에 맞는 경로가 곧 도망 트리거다.

검증: 컴파일 + 태스크 5 의 씬에서 히트 카운터가 오르는지 실측.

## 태스크 2 — 순수 로직과 EditMode 테스트

**`ChildBrain`** — `UnityEngine` 참조 0. 데디 서버와 EditMode 양쪽에서 돈다.

| 입력 | 출력 |
|---|---|
| `dt`, `targetDistanceM`(없으면 `float.PositiveInfinity`), `hit`(bool) | `EChildState`, `Stock`, `ThrowRequested` |

- 재고는 `_secondsPerBall` 마다 1 씩 늘고 `_stockMax`(기본 3) 에서 멈춘다. **주위 눈을 소비하지 않는다.**
- `Idle`: 사거리 밖.
- `Throw`: 사거리 안 + 재고 ≥ 1 + 쿨다운 만료 → `ThrowRequested` 한 번, 재고 −1.
- `Taunt`: 사거리 안 + 재고 0. (애니메이션은 추후 — 상태만 낸다)
- `Flee`: 되돌릴 수 없다. 한 번 들어가면 `hit` 이 없어도 유지된다(상자의 "부서짐 1비트" 와 같은 성질).

**`ChildBallistics.TrySolve(from, to, speedMps, gravity, out Vector3 velocity)`** — 발사 속력을
고정하고 **각도를 역산**한다(낮은 궤적 해). 판별식이 음수면 `false` — 그때는 던지지 않는다.
속도를 그때그때 지어내면 가까운 표적에 총알처럼 날아가 "포물선" 이 사라진다.

검증(EditMode):
- `ChildBrainTests` — 재고가 상한에서 멈춘다 / 던지면 1 줄어든다 / 재고 0 이면 `Taunt` /
  `Flee` 는 되돌아오지 않는다 / 사거리 밖이면 `ThrowRequested` 가 안 뜬다.
- `ChildBallisticsTests` — 해가 있는 거리에서 적분하면 표적을 지난다(허용 오차 0.15m) /
  사거리 밖은 `false` / 같은 표적에서 낮은 해를 고른다.

## 태스크 3 — 투사체

`ChildSnowball.cs` + `PF_ChildSnowball.prefab`.

- `Rigidbody`(mass 0.5kg, `useGravity`, **`ContinuousDynamic`**) + `SphereCollider`(trigger 아님, r≈0.12).
- 서버가 `Runner.Spawn` 하고 `ChildBallistics` 가 준 속도를 한 번 대입한다. 이후는 그냥 물리다 —
  유도하지 않는다(사용자 요구: 던지는 **순간**의 플레이어를 향한다).
- `OnCollisionEnter`(서버만): `ImpactMomentum.TryCompute` 로 `SnowballHit` 을 만들고
  `GetComponentInParent<ISnowballHittable>()` 에 전달 → 즉시 despawn.
- 수명 타임아웃(기본 6초) despawn. 맞든 안 맞든 재고로 남지 않는다.
- **아이는 서로의 눈덩이에 안 맞는다** — 발사 직후 같은 그룹 콜라이더와 `Physics.IgnoreCollision`.
  (레이어를 새로 만들지 않는다. 문·상자가 문턱값만으로 거르고 있는 것과 같은 선택이다)

## 태스크 4 — 아이와 그룹

`Child.cs`

- kinematic `Rigidbody` + `CapsuleCollider`. kinematic 이어도 **다이내믹 바디와의 충돌 콜백은 온다** —
  펭귄(mass 30 다이내믹)이 뛰어들면 `OnCollisionEnter` 가 뜨고, `ImpactMomentum` 문턱을 넘으면 도망이다.
  부서지기 전 `ImpactBreakable` 과 같은 몸가짐이다(약한 접촉에 밀려다니지 않는다).
- `[Networked]`: `NetState`(`EChildState`), `NetStock`(int), `NetFleeDirXZ`. 표현 계층이 읽는 전부.
- 표적 선정: `_engageRangeM`(기본 12m) 안의 **가장 가까운 `PenguinSnowballHit`**. 시야 차폐 판정은
  넣지 않는다 — 벽 너머로 던지는 게 실제로 문제가 되면 그때 레이캐스트를 붙인다.
- 던지기: 손 위치(`_throwOrigin`)에서 표적 캡슐 중심으로 `TrySolve`. 실패하면 그 틱은 던지지 않는다.
- 도망: `NetFleeDirXZ` 방향으로 `_fleeSpeedMps` 등속, 아래로 레이캐스트해 지면에 붙는다.
  `_fleeSeconds`(기본 4초) 또는 `_fleeDistanceM` 이후 despawn.
  **경로 탐색은 없다.** NavMesh 도 회피도 넣지 않는다 — 직선으로 뛰다 사라진다. 실제로 벽에
  박히는 배치가 나오면 그때 측정하고 검토한다.
- 재고 떨구기: 도망을 시작하는 **그 한 프레임**에 `NetStock` 개의 `_dropPrefab` 을 발밑에 흩뿌린다
  (작은 랜덤 임펄스). 서버만. 상자의 파편과 달리 이건 **게임플레이 산출물이라 복제한다.**

`ChildGroup.cs`

- `_memberCount`(`Range(1,8)`, 기본 4 — 사용자 요구대로 바뀔 수 있는 수치) 만큼 `Spawned()` 에서
  서버가 반경 `_ringRadiusM` 원에 `PF_Child` 를 스폰한다. 씬에는 그룹 하나만 놓는다.
- `ServerBeginFlee(Vector3 threatPos)` — **전원**이 각자 `(자기 위치 − threatPos)` 수평 방향으로
  흩어져 도망친다. 한 점으로 모이지 않게 각자 자기 방향을 쓴다.
- 멤버가 전부 despawn 하면 그룹도 despawn.

## 태스크 5 — 검증 씬과 실측

`Tests/Interaction_Children_Test.unity` — **Build Settings 에 넣지 않는다.**

- 바닥 + `PF_ChildGroup` 하나 + `TestImpactLauncher` 계열 키 헬퍼(1 = 눈덩이 발사, 2 = 몸통 돌진
  프록시, R = 리셋). 기존 헬퍼가 문·상자 전용이면 그대로 두고 씬 전용 스크립트를 하나 더 만든다.
- 아이 모델은 임시 캡슐이다. 문·상자와 같은 규약 — 루트(물리 바디 + 로직)와 `Visual` 자식을
  분리해 메시만 갈아 끼우면 되게 둔다.

실측할 것:
1. 4명이 각자 재고를 쌓고 상한에서 멈추는가 (`NetStock` 로그).
2. 던진 궤적이 실제로 포물선인가, 그리고 **던진 순간의 위치**로 가는가(움직이는 표적은 빗나가야 정상).
3. 한 명을 맞혔을 때 4명 전원이 흩어지고, 떨군 개수 합 == 도망 시점 재고 합.
4. 헤드리스(`-batchmode -nographics`) 한 번. 아이·투사체 어느 쪽도 GPU 를 요구하지 않아야 한다.

## 태스크 6 — 문서

- `Interaction/Npc/Children/AGENTS.md` 신설. `Interaction/AGENTS.md` 의 폴더 지도와 "지금 있는 것" 표에 추가.
- ⚠ `Interaction/AGENTS.md` 의 **"눈덩이 구현을 참조하지 않는다"** 는 계약은 **그대로 유지된다** —
  Children 도 `Snow/` 를 안 본다. 다만 "이 폴더는 프롭만 있다" 는 인상은 바뀌므로, 폴더 소개
  문장에 "충격에 반응하는 **행위자**" 를 포함하도록 고친다.
- `docs/INDEX.md` 현재 상태에 한 줄, 세션 요약 하나.

---

## 체크인 계획 (4개)

| # | 내용 |
|---|---|
| 1 | 이 계획 문서 |
| 2 | 피격 계약(`Core/Hit/`) + `PenguinSnowballHit` + 순수 로직 2종 + EditMode 테스트 |
| 3 | `Child`·`ChildGroup`·`ChildSnowball` + 프리팹 3종 + 검증 씬 |
| 4 | `AGENTS.md` 2종 + `docs/INDEX.md` + 세션 요약 |

에디터 밖에서 만든 파일은 `Private` 로 뜬다 — 체크인 전에 `cm add -R`, 그리고 `cm status --private`
로 확인한다. 프리팹·씬은 Unity 에디터(MCP)로 만들고 YAML 을 손으로 편집하지 않는다.

---

## 정하지 않고 남기는 것

- **떨구는 것의 정체.** `_dropPrefab` 은 직렬화 필드로 비워 둔다. 눈 구현이 확정되면 그때 배선한다.
  ⚠ 만약 그것이 `SnowBallCarrier` 가 되면 **`SnowCpuStage` 원장에 유입항(`ExternalInMm`)을 먼저
  추가해야 한다** — 안 그러면 보존 테스트가 깨진다. 지금은 프로토 큐브를 물려 개수만 검증한다.
- 놀리기·던지기·도망 애니메이션. 상태는 이미 복제되므로 표현만 얹으면 된다.
- 플레이어가 맞았을 때의 효과(넉백/감속/무효). 계약과 수신기만 만들고 비워 둔다.
- 아이의 시야 차폐, 장애물 회피, 재등장(리스폰). 전부 실측으로 필요해질 때까지 안 만든다.
- 문턱값 전부 자리값이다 — `_engageRangeM`, `_secondsPerBall`, `_throwSpeedMps`, 도망 문턱 운동량.
