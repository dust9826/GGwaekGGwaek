# 인게임 멀티 재구축 — 제설차를 버리고 펭귄으로

`/main/multiplay-penguin` · 2026-08-24

로비–접속 세션 계층만 남기고 **인게임 멀티를 통째로 다시 만든다.** 플레이어는 눈덩이를 굴리는
펭귄이고, 네트워크로 가는 것은 **원인(액터 포즈·입력)뿐**이며 눈 필드는 각 피어가 스스로 시뮬한다.

전제는 `2026-08-23-snow-regions.md` 가 확정했다. 이 문서는 그 §6 을 **실제 코드에 어떻게 앉히는가**만
다룬다. 눈 자체의 전제(소모·재생·구역)를 다시 논하지 않는다.

---

## 1. 왜 고치는 게 아니라 버리는가

지금 멀티는 **눈이 게임에 아무 영향을 주지 않는 상태로 죽어 있다.** 실측 사슬:

1. `MultiplayPlowVehicle` 은 최고속 배수와 제설 저항을 `SnowPileFieldV7` 에서 읽는다
   (`:190`, `:201`, `:207-210`).
2. `MP_Gameplay.unity` 에는 **`SnowV7MapRig` 도 `SnowPileFieldV7` 도 없다**(스크립트 GUID 대조).
   씬에 있는 것은 `SnowCpuStage` 와 `SnowDisplaceView` 뿐이다.
3. 따라서 `_rig == null` → `SnowSpeedFactor()` 는 항상 `1f`, `ApplySnowDrag()` 는 speed 를 그대로
   반환한다. 눈이 60 cm 든 맨바닥이든 차는 똑같이 달린다.

이건 배선 실수 하나가 아니라 **규칙/시각 분리 위반**이다(`snow-v7-confirmed-rule-visual-split`).
v7 은 클라 전용 시각 표현인데 거기서 게임 값을 읽고, 그 값이 `[Networked] ForwardSpeedMps` 로
들어간다. 리그를 다시 붙이면 데디 서버에는 GPU 가 없어 리그도 없으므로 **서버는 배수 1, 클라는
저항 적용** — 영구 예측 불일치가 된다. 지금 안 터지는 유일한 이유가 양쪽 다 죽어 있어서다.

고치려면 저항의 데이터 출처를 바꾸고, 차량을 권위 격자에 다시 묶고, 셀 델타 복제를 걷어내야
한다. 그건 남는 코드가 거의 없는 수술이다. **게다가 플레이어가 제설차가 아니라 펭귄으로 바뀐다.**

---

## 2. 버리는 것 · 남기는 것

### 버린다 — `Assets/Game/InGame/Multiplay/` 전체

| | |
|---|---|
| `Scripts/` | `MultiplayPlowVehicle` · `MultiplayPenguin` · `MultiplayAvatar` · `MultiplayAvatarChoice` · `MultiplayChaseCamera` |
| `Resources/` | `PF_MultiplayPlow` · `PF_MultiplayPenguin` · `PF_MultiplayAvatar` |
| `Scenes/` | `MP_Gameplay.unity` |
| `Materials/` `Preview/` | 부속 머티리얼 2 · 스크린샷 5 |

`PF_SessionLobby.prefab` 은 이 폴더 안에 있지만 **로비라서 남긴다** — `Core/Multiplay/Resources/` 로 옮긴다.
`SessionLauncher:71` 이 `Resources.Load("PF_SessionLobby")` 로 이름만 보므로 폴더가 바뀌어도
`Resources` 아래이기만 하면 된다.

### 남긴다 — `Assets/Game/Core/Multiplay/` 세션 계층 전부

`SessionLauncher` · `SessionLobby` · `ESessionPhase` · `NetworkInputData` ·
`MultiplayerRoleBootstrap` · `Editor/MultiplayScenarioTools`.

로비 → 매치메이킹 → 로딩 → 게임플레이 단계 기계와 `GameMode.Server` 규약은 그대로다.
인게임과의 결합은 **문자열 두 개와 타입 하나**뿐이라 싸게 끊긴다:

- `SessionLauncher:31` `GameplayScenePath` 상수
- `SessionLauncher:51` 기본 아바타 `"PF_MultiplayPlow"`
- `MultiplayAvatarChoice` 가 넣는 `SceneAvatarResource`(`:56`)

### 펭귄은 왜 되살리지 않고 새로 만드는가

**`MultiplayPenguin` 에는 독립적인 기술적 근거가 없었다.** 차량을 버린 실측 사슬(§1)이 펭귄에는
적용되지 않는다 — 그 파일은 `SnowPileFieldV7` 도 `SnowV7Resistance` 도 `_rig` 도 블레이드 개념도
쓰지 않는 깨끗한 `NetworkBehaviour` 였고, 클래스 주석이 이 문서와 같은 말을 하고 있었다
("권위는 전부 서버다 … 클라이언트는 원인만 보낸다"). 폴더를 통째로 지우는 데 딸려 나간 것이고,
항목별로 근거를 나누지 않은 것이 판단 실수다. 기록해 둔다.

**그런데도 되살리지 않는 이유는 프리팹이다.** 두 펭귄이 참조하는 에셋 집합이 **하나도 겹치지
않는다**(GUID 대조):

| | `PF_MultiplayPenguin`(삭제됨) | `PF_Penguin`(싱글) |
|---|---|---|
| 외형 | `M_PenguinProto` · `M_PenguinProtoBeak` — **프로토 재질** | 실제 메시 + `AC_Penguin.controller` |
| 입력 | 코드에서 직접 | `PenguinControls.inputactions` |
| 연출 | 없음 | `MMF_Player`(Feel) |
| 물리 재질 | 없음 | `PM_Frictionless` |
| 스크립트 | `MultiplayPenguin` 하나(404줄) | 9개 |

되살리면 **프로토 아바타 전제가 따라온다.** 같은 게임에 펭귄이 두 종류 있게 되고, 그것은 그
파일 자신이 경고한 *"수치와 규칙이 어긋나면 같은 게임이 두 개가 된다"* 와 정확히 같은 실패다.

**그러므로 기준은 `PF_Penguin` 이다.** 사용자 결정(2026-08-24): 팀원의 싱글 작업을 기준으로
멀티를 만든다. 삭제된 404줄은 `cs:626` 에 남아 있으므로 필요하면 참고할 수 있다 — 되살리는 것이
아니라 읽는 것이다.

### 연쇄로 손대야 하는 것

| 파일 | 왜 |
|---|---|
| **`SnowCpuStage.cs`** `:145,148,393,778` | 권위 격자가 `List<MultiplayPlowVehicle>` 을 직접 들고 있다. §4 |
| `UI/SnowballCoopPush/SnowballCoopTimingHud.cs` `:15,27` | `MultiplayPenguin` 을 필드로 들고 있다 |
| `Snow/Tests/PlayMode/` 4개 | `SnowCpuSyncTests` · `SnowSyncScaleTests` · `SnowCpuStageMultiplayTests` · `SnowPlowPrefabTests` — 지워지는 타입과 셀 델타 복제 위에 서 있다 |

---

## 3. 구조 — 원인만 복제하고 각 피어가 시뮬한다

`2026-08-23-snow-regions.md` §6 의 불변식을 그대로 가져온다:

> **클라 격자를 읽고 정해지는 게임 값이 하나도 없어야 한다.**

| 값 | 주인 | 클라가 얻는 법 |
|---|---|---|
| 눈덩이 질량 · 반지름 | 서버 스칼라 | `[Networked]` 직접 복제 — **이미 맞음** |
| 눈 깊이 격자 | 서버 권위 | 각 피어가 **복제된 명령으로 재유도** |
| 화면의 자국 | 클라 전용 | 자기 격자에서 |
| 마찰 · 가속 | 서버 권위 + 클라 예측 | 클라가 자기 격자를 읽는다 — **유일한 누수 경로**, §7 |

눈덩이 질량이 이미 안전한 근거: `SnowBallCarrier.cs:86` `[Networked] NetMassMm`, 쓰기는
`HasStateAuthority` 게이트(`:234`), 클라는 읽기 전용(`:587`,`:599`), 반지름은 질량에서 유도(`:145`).

액터 포즈는 어차피 렌더링 때문에 복제 중이므로 격자 재유도의 추가 대역폭은 0 이고, **팀원 자국도
자동으로 보인다.**

---

## 4. `SnowCpuStage` — 블레이드 경로를 **지운다**, 추상화하지 않는다

지금 권위 격자가 구체 차량 타입을 안다(`:145` `List<MultiplayPlowVehicle>`). 그게 이번 삭제를
컴파일 에러로 만드는 지점이다.

**첫 판단은 "인터페이스 뒤로 숨긴다" 였고 그것은 틀렸다.** `HeightCpu/ISnowBladeState.cs` 가 이미
있지만 멤버가 `BladeDown` 과 `AngleState` 둘뿐이고 **둘 다 블레이드 개념**이다 — 펭귄에는 날도
배출 방향도 없다. 재사용하면 차량 개념이 펭귄으로 끌려온다. 포즈·속도를 담는 새 인터페이스를
만드는 것도 안 된다: 차량을 버리면 **구현체가 펭귄 하나뿐**이고, 루트 `AGENTS.md` 가 *"새
추상은 두 번째 호출 지점이 확인된 뒤에"* 라고 못박았다.

실제로는 스테이지에 액터 경로가 이미 **셋**이고, 그중 하나만 차량을 안다:

| 경로 | 무엇을 보나 | 차량 결합 |
|---|---|---|
| 블레이드 | `List<MultiplayPlowVehicle>`(`:393`, `:778`), 독립 실행용 `_localBody`/`_bladeState`(`:314`, `:694-755`) | **있음 — 지운다** |
| 아바타(뭉치기) | `Runner.TryGetPlayerObject(player, out NetworkObject)`(`:858`, `:940`, `:998`) | 없음 — **이미 구체 타입을 안 본다** |
| 눈덩이 | `ball.transform.position`(`:495`, `:514`) | 없음 |

**펭귄은 아바타 경로를 그대로 쓴다.** `NetworkObject` 만 보므로 새 플레이어 타입이 무엇이든
꽂힌다. 그러므로 이 작업은 추상을 더하는 것이 아니라 블레이드 경로와 그 부속
(`_vehicles` · `_prevPose` · `_localBody` · `_bladeState` · `_legacyBlade` · `_standalonePrev`)을
**빼는 것**이다. `ISnowBladeState` 자체는 남는다 — `V7Spike` 의 `SnowV7MapRig` 와
`SnowV7BladeVisual` 이 아직 쓴다.

나중에 차량이 돌아오면 그때가 두 번째 호출 지점이고, 그때 추상을 만든다.

---

## 5. 이미 있는 부품 — 배선만 하면 된다

`/main/snow-quadtree-commands` 스파이크(2026-08-21)가 실험 셋을 통과시키고 머지를 보류해 뒀는데,
**세 파일이 이미 현재 브랜치에 들어와 있다.** 다만 **테스트 말고는 아무도 안 쓴다.**

| 파일 | 무엇 | 실험 결과 |
|---|---|---|
| `HeightCpu/SnowSweepInt.cs` | 정수 스윕 | float 과 **같은 셀**, 차이 0 |
| `HeightCpu/SnowHeightQuadtree.cs` | 높이 접기 | 최악에서도 **15.6배**(33.6 KB vs 524 KB) |
| `HeightCpu/SnowCommandWire.cs` | 명령 와이어 | **6.8배** 작고 어긋난 셀 **0 / 262,144** |

`SnowCommandWire` 의 표면이 이미 펭귄 경로를 덮는다 — `ESnowCommandKind` 에
`BallHarvest` · `BallRelease` · `BallBurst` · `Gather` 가 있다(`BladeCut` 은 차량 것이라 안 쓴다).
`SnowCommand` 는 32 바이트 고정이고 **모든 값이 정수**라 보간된 원격 트랜스폼을 읽지 않는다.
`Write` / `Read` / `Apply(field, cmd, scratch)` 가 다 있다.

### ⚠ "원인 복제는 폐기됐다" 는 문장과 충돌하지 않는다

읽다 보면 반드시 걸리는 자리가 있다. `SnowBallCarrier.cs:18-19` 가 이렇게 적어 놨다:

> 이 프로젝트는 이미 **서버 권위 + 결과 복제**이므로(원인 복제는 실측으로 폐기됐다 —
> `AGENTS.md` 전파 절) 구조가 어긋나지 않는다.

`SnowCpuStage.cs:12-16` 도 같은 말을 한다 — 원인 복제로 해 봤는데 **실측에서 분포가 갈렸다**고.

**폐기된 것은 "원인 복제" 라는 발상이 아니라 그 첫 구현이다.** `SnowCpuStage` 가 든 실패 원인은
정확히 둘이었다: (1) 원격 액터의 트랜스폼이 클라에서 **표시용으로 보간된 값**이고, (2) 틱 지연이
있어 같은 입력이라도 스윕이 다른 자세로 찍힌다.

`SnowCommandWire` 는 그 둘을 **직접 겨냥해서** 만들어졌다. 파일 주석이 그렇게 말한다:

> 모든 값이 정수다. **자세를 명령이 싣고 다니므로 보간된 원격 트랜스폼을 읽지 않고**, 정수라서
> 플랫폼을 넘어 같은 셀 집합이 나온다(실험 1). **그 둘이 2026-08-18 에 이 방향을 폐기시킨 두
> 원인이었다.**

그리고 실험 결과가 어긋난 셀 **0 / 262,144** 다. 그러므로 이 문서가 원인 복제로 돌아가는 것은
폐기 결정을 무시하는 것이 아니라, **폐기 사유가 해소된 뒤에 다시 여는 것**이다.

**단, 눈덩이의 운동은 예외다.** 그건 맵 콜라이더 위의 Unity 물리라 정수 결정론 밖이고
(`SnowBallCarrier.cs:14-20`), 앞으로도 **서버 권위 + 결과 복제**로 남는다. 원인 복제로 돌아가는
것은 **눈 격자**이지 강체 운동이 아니다. 펭귄의 이동도 같은 이유로 강체 쪽이다 — §6 참조.

**그러므로 이 작업의 대부분은 새로 쓰는 것이 아니라 `SnowCpuStage.SendDelta`(`:896`) 를 걷어내고
그 자리에 명령 방송을 넣는 것이다.**

---

## 6. 예측과 재시뮬레이션 — 눈은 확정 틱에서만 바뀐다

이게 구조를 결정하는 제약이다.

Fusion 2 는 예측 틱을 **재시뮬레이션**한다. `FixedUpdateNetwork` 안에서 눈을 깎으면 같은 자리를
여러 번 깎는다. 그런데 320×320 `ushort` 격자는 틱마다 되감을 수 없다 — 롤백 버퍼를 두려면
틱당 200 KB 다.

**따라서 눈 필드는 예측 레이어에 두지 않는다.** 명령의 적용은 확정된 틱에서 한 번만 일어난다.
`SnowCommand.Tick` 이 이미 필드로 있으므로 `(tick, actor, kind)` 로 idempotency 를 지킨다 —
폴더 `AGENTS.md` 가 *"재시뮬레이션 idempotency 는 셀이 아니라 `(tick, stampId)` 단위로 지킨다"*
고 이미 못박아 둔 규약과 같다.

결과로 눈은 **입력보다 몇 틱 늦게** 깎인다. 그 지연이 화면에서 보이면 자국 VFX 를 로컬 즉시
재생하고 격자는 뒤늦게 따라오게 한다 — 원인은 복제하고 연출은 예측하는 통상 패턴이다.

---

## 6-B. 펭귄 — 결정 셋 (2026-08-24 확정)

### (a) 한 파일 · 진입점 둘. 새 래퍼를 만들지 않는다

`PenguinLocomotion` 자체를 고쳐 싱글과 멀티를 모두 지원한다. **본문 하나 · 진입점 둘 ·
`dt` 와 입력은 인자.** `SnowBallCarrier.cs:332-357` 이 이미 그 형태다.

얇은 래퍼 안은 **전제부터 성립하지 않는다.** 물리 애드온이 `Physics.simulationMode = Script` 로
바꿔도 **Unity 는 `FixedUpdate` 를 계속 부른다**(`SnowBallCarrier.cs:336-339`). 래퍼를 붙여도
`PenguinLocomotion.FixedUpdate` 는 여전히 자기 클럭으로 힘을 쌓으므로 그 진입점을 어차피 손대야
하고, 그러면 한 파일 안의 시그니처 변경을 똑같이 치른 위에 새 파일과 상태 이중화가 얹힌다.

규칙으로도 한 파일이 맞다. 이중 진입점은 이 저장소에서 **호출 지점이 이미 둘**이다 —
`SnowBallCarrier`(`:341-351`)와 `PenguinSnowballHit`(`:27-28` `IsNetworked`/`IsAuthority`).
따라서 새 추상이 아니라 **확립된 관례를 세 번째로 쓰는 것**이다. 래퍼가 오히려 구현체 하나짜리
새 계층이고, §4 가 `SnowCpuStage` 에서 똑같은 이유로 기각한 모양이다.

#### 네트워크 진입점은 어디에 두는가 — 변형 위의 작은 컴포넌트

`PenguinLocomotion` **자체를 `NetworkBehaviour` 로 만들지는 않는다.** 그러면 `PF_Penguin` 에
`NetworkObject` 가 필요해지는데, **이 프로젝트에는 씬에 배치된 `NetworkObject` 가 하나도 없다**
(전 씬 grep). `PF_SnowBall` 은 `NetworkObject` 를 갖지만 씬에 놓이지 않고 런타임에 스폰되므로
선례가 되지 못한다. 싱글 씬 셋이 쓰는 프리팹에 미검증 변경을 얹지 않는다.

대신 이렇게 나눈다:

| | |
|---|---|
| `PenguinLocomotion`(기존 파일, `MonoBehaviour` 유지) | **본문 하나.** 물리 스텝을 `Step(dt, input)` 로 인자화한다. 수치와 규칙이 전부 여기 있다 |
| `FixedUpdate`(기존 진입점) | 런너가 없을 때만 `Step(Time.fixedDeltaTime, 로컬입력)` |
| 새 작은 `NetworkBehaviour`(변형에만 붙는다) | `FixedUpdateNetwork` 에서 `Step(Runner.DeltaTime, 네트워크입력)` |

**이것은 얇은 래퍼 안(기각됨)이 아니다.** 기각한 이유는 그 안이 *"싱글 파일을 안 건드린다"* 를
전제로 상태와 수치를 두 곳에 두기 때문이었다. 여기서는 `PenguinLocomotion` 을 인자화로 **직접
고치고**, 새 컴포넌트는 `dt` 와 입력만 골라 넘기는 진입점이다 — 본문도 수치도 복제되지 않는다.
`SnowBallCarrier` 가 같은 파일 안에서 하는 일을, 프리팹 층의 제약 때문에 컴포넌트 둘로 나눈 것뿐이다.

### (b) 클라이언트 예측은 **처음에 켜지 않는다**

서버만 로코모션을 돌리고(`Object.HasStateAuthority` 게이트), 클라이언트는 `NetworkRigidbody` 의
보정을 받아 그린다. **대가는 자기 입력이 1 RTT 늦게 보이는 것이다.**

이유는 재시뮬레이션 위험이 통째로 사라지기 때문이다. 서버에서는 `Runner.IsForward` 가 항상 참이라
아래가 전부 무해해진다 (전부 실측 확인):

| 위험 | 위치 |
|---|---|
| `_snowCoverage01` `MoveTowards` 누적 | `:244`, `:689` |
| `_slideExitControlRemaining` 카운트다운 | `:247`, `:370-371` |
| `_slideSlipDeg`/`_slideSlipVelocity` `SmoothDamp` | `:248-249`, `:506-508` |
| **`PenguinSlideKick.Phase` — 위상이 힘을 정한다** | `SlideKick:88,128,166` |
| `_wasSliding` 에지 래치 — 2회차엔 진입 처리를 건너뛴다 | `:246`, `:464-470` |
| `_capsule.sharedMaterial` 스왑 — 스냅샷 대상이 아니라 **되감기지 않는다** | `:477,485` |
| `Jumped?.Invoke()` → `SetTrigger` 중복 발동 | `:644` |

**이건 되돌릴 수 있는 결정이다.** 손맛이 부족하면 게이트를 풀고 위 일곱을 롤백 상태로 올리면 된다.
이 프로젝트는 눈 격자에 대해 이미 같은 판단을 내렸다(§6: *"눈 필드는 예측 레이어에 두지 않는다"*).
**먼저 붙이고, 느껴지면 그때 켠다.**

### (c) 카메라 요를 보낸다. 월드 방향 벡터가 아니다

`PenguinLocomotion` 은 이동 방향을 카메라 기준으로 만드는데(`:292,355` → `:648-656`) 데디 서버에는
원격 플레이어의 카메라가 없다. **`NetworkInputData` 에 요(yaw) 하나를 싣는다.**

월드 벡터를 보내면 안 되는 이유:

1. **`CameraRelativeDirection:648-656` 의 본문이 죽는다.** 요를 받으면 `_cameraPivot.forward` 를
   `Quaternion.Euler(0, yaw, 0) * Vector3.forward` 로 바꾸는 두 줄 교체로 끝나고 규칙은 한 곳에 남는다.
2. **`:655` 의 `sqrMagnitude > 1f ? normalized : dir` 를 서버가 강제할 수 없게 된다.** 클라가 길이
   3짜리 벡터를 보내면 그대로 믿는다. 요는 각도라 그 조작 여지가 구조적으로 없다.
3. **`:311` 의 옆붙기 경로가 `fwd` 만 쓰고 `right` 는 `PenguinSnowball.TickSideOrbit:253` 이 따로
   해석한다.** 방향 하나로 합치면 서버가 W/S 와 A/D 를 다시 분해할 수 없다.
4. 매 틱 나가는 구조체에서 각도 하나가 벡터보다 작다.

⚠ **요는 입력이지 물리 상태가 아니다. `[Networked]` 로 들지 않는다.** 틱마다 입력으로 오고
재시뮬에서도 같은 값이 다시 오므로 그 자체로 재시뮬 안전하다. `PenguinCameraOrbit` 이 이미
`_yaw` 필드를 들고 있어 새로 계산할 것도 없다.

### (d) 프리팹은 **변형(variant)** 으로 만든다. `PF_Penguin` 을 개조하지 않는다

`PF_Penguin` 을 `NetworkObject` 로 바꾸면 싱글 씬 셋(`Penguin_Locomotion_Test` ·
`Penguin_SnowballPush_Test` · `Snow_Slope_Test`)과 PlayMode 테스트 둘이 그 변경을 함께 받는다.

대신 `PF_Penguin` 의 **프리팹 변형**을 `Resources` 아래에 만들고 거기에만 `NetworkObject` ·
`NetworkRigidbody` · 네트워크 구동부를 얹는다. 변형은 값을 상속하므로 **수치가 갈리지 않는다** —
"한 곳에서 오는 수치" 규칙을 프리팹 층에서도 지키는 유일한 방법이다. 오버라이드는 명시적으로 남는다.

`Resources` 여야 하는 이유는 `SessionLauncher:507` 이 `Resources.Load` 로 아바타를 찾기 때문이다.

⚠ **컴포넌트 순서** — 네트워크 구동부가 `NetworkRigidbody` 보다 **앞**에 있어야 한다.
`Spawned()` 에서 물리 바디에 자세를 먼저 밀어 넣지 않으면 원점으로 순간이동한다
(`SnowBallCarrier.cs:565-576` 실측: `(-2,1,8)` 에 만들었는데 `(0,6,0)` 에서 떨어졌다).
그리고 **`NetworkTransform` 을 같이 붙이지 않는다** — `NetworkRigidbody` 가 `NetworkTRSP` 를
상속해 트랜스폼 복제를 겸하므로 둘 다 두면 싸운다(`:584-585`).

---

## 7. 마찰 — 싱글이 이미 풀어 놨다. 새로 정하지 않는다

**이 절은 2026-08-24 에 뒤집혔다.** 처음에는 "처녀설 / 다진눈 / 맨바닥 3단계로 양자화하고
경계값 둘을 사람이 정한다" 였는데, 싱글 펭귄을 열어 보니 **이미 그보다 나은 것을 하고 있었다.**

```csharp
// PenguinLocomotion.cs:709
private bool IsSnowCovered(Vector3 worldPosition)
{
    if (_snowCpuStage != null && _snowCpuStage.Field != null)
        return _snowCpuStage.HeightAtM(worldPosition.x, worldPosition.z) >= _snowThresholdCm * 0.01f;
    ...
}
```

세 가지가 이미 맞다.

1. **권위 격자를 직접 읽는다.** `SnowCpuStage.HeightAtM` 이다 — v7 이 아니다. 제설차를 죽인
   §1 의 위반이 여기엔 없다.
2. **연속값이 아니라 불리언이다.** 샘플 지점들에서 "덮였나" 를 세어 비율을 낸다
   (`PenguinLocomotion.cs:700-707`). 양자화가 이미 돼 있다.
3. **단계가 셋이 아니라 하나다** — 임계 `_snowThresholdCm`(기본 2 cm, `:83`). 경계가 하나뿐이면
   격자가 한두 셀 어긋나도 같은 답이 나올 확률이 3단계보다 **높다**. 3단계 제안은 경계를 둘로
   늘려 어긋날 자리를 하나 더 만드는 것이었다.

**그러므로 경계값을 새로 정하지 않는다. 싱글의 임계 방식을 그대로 쓴다.** 사용자 결정
(2026-08-24): 펭귄의 수치와 규칙은 전부 싱글 기준으로 맞춘다.

`SnowV7Resistance` 는 여전히 쓰지 않는다 — v7 시각 필드에 물려 있다.

---

## 8. 중간 접속 스냅샷 — 선택이 아니다

재생이 **국소·수동**이라(`snow-regions` §1) 늦게 들어온 클라의 격자는 저절로 수렴하지 않는다.
이미 깎인 땅은 다시 안 깎이므로 명령만 받아서는 영원히 어긋난 채로 남는다.

비용은 걸림돌이 아니다 — 40×40 m · 12.5 cm 면 320×320 = 102,400 셀, `ushort` 그대로 200 KB,
1 m 다운샘플이면 1,600 B. **틱당이 아니라 접속당 1 회다.**

`SnowHeightQuadtree` 가 여기 쓰인다(접힘 15.6배).

---

## 9. 입력 계약 — 4 비트만 뺀다

`Core/Multiplay/NetworkInputData.cs` 는 이미 대부분 펭귄 모양이다. `EInputButton` 에서 **차량
전용 비트 넷만** 뺀다:

| 뺀다 | 남는다 |
|---|---|
| `BladeToggle`(2) · `AngleLeft`(3) · `AngleStraight`(4) · `AngleRight`(5) | `Sprint`(0) · `Action`(1) · `RequestStartMatch`(6) · `Burst`(7) · `CreateSnowball`(8) · `CoopShoveSuccess`(9) · `CoopShoveFailure`(10) |

⚠ **비트 값을 다시 매기지 않는다.** 주석이 *"순서를 바꾸면 안 된다 — 비트가 곧 와이어 포맷"*
이라고 못박아 뒀다. 뺀 자리는 구멍으로 남기고 새 버튼은 11 부터 붙인다.

---

## 10. 검증

- **EditMode** — 명령 라운드트립(`Write`→`Read`→`Apply`)과 결정성은 이미 테스트가 있다
  (`SnowCommandWireTests` · `SnowSweepIntTests` · `SnowHeightQuadtreeTests`). 새 배선이 그것을
  실제로 통과하는지 스테이지 수준에서 한 번 더 건다.
- **PlayMode** — 지워지는 4개를 대체하는 최소 세트를 새로 쓴다: 2 피어에서 같은 명령열을 받은
  두 격자가 **셀 단위로 같은가**, 중간 접속 클라가 스냅샷 후 수렴하는가.
  ⚠ 메모리 `playmode-cli-one-run-per-session` · `playmode-batch-shares-session-state` —
  PlayMode 는 세션당 한 번만 돌고 배치에서 서로 오염된다. 판별은 EditMode 로 최대한 내린다.
- **화면** — 검증 씬은 `InGame/Multiplay/Tests/` 에 새로 만든다. 씬 하나, 빌드 세팅에 넣지 않는다.

### 먼저 확인할 회귀 하나 — **확인함 (2026-08-24)**

`cs:616` 에서 `_residueMm` 을 0 으로 내렸고, `snow-regions` §7 이 **잔설 0 이 Raymarch 도랑
안쪽의 얼룩말 아티팩트를 깨운다**고 경고해 두었다. `Snow_BallPush_Test`(`SnowCpuStage` + 펭귄,
`_look = Raymarch`)에서 눈덩이 도랑을 내고 육안으로 확인했다 — **재현되지 않았다.**

같은 세션에 사용자가 **레이마칭을 더 이상 쓰지 않기로 결정**했으므로 이 게이트는 무효가 됐다.
눈 렌더링은 Displace 로 통일했고(`cs:619`), 같은 도랑을 Displace 로 다시 보아 접합 곡선과 바닥
노이즈가 의도대로 나오는 것을 확인했다. 도랑 벽의 **12.5cm 격자 계단은 그대로 보인다** —
`snow-regions` §7 이 이미 기록한 알려진 특성이고 이 작업의 범위가 아니다.

레이마칭 코드는 지우지 않고 남겼다. 삭제하려면 씬 전부를 눈으로 확인해야 하고, 그것은 이
브랜치의 피처가 아니다.

(원문) 멀티 작업 전에 Raymarch 경로를 한 번 띄워 확인한다 — 나오면 거기서 멈추고 룩을 먼저 고친다
(`snow-regions` §9 채택 순서 2번).

---

## 11. 미정 — 사람이 정할 것

| | |
|---|---|
| **재생 구역 설계** | 개수 · 발동 방식 · 재생 속도. 성능이 아니라 **페이싱**을 정한다. 느리면 돌아다니고 빠르면 보급소에 눌러앉는다 |
| ~~마찰 3단계 경계값~~ | **해소(2026-08-24)** — §7. 싱글의 임계 방식을 그대로 쓴다 |
| ~~펭귄의 이동 감각~~ | **해소(2026-08-24)** — 싱글 값이 기준이다. 기준 프리팹은 `Penguin/Prefabs/PF_Penguin.prefab` |

`snow-regions` §8 의 (b) 터레인 유도 규칙과 (d) 높이 양자는 **이 작업의 범위 밖**이다 —
맵을 키울 때 재검토한다.

---

## 12. 작업 순서와 체크인 단위

**수정을 먼저 하고 삭제를 나중에 한다.** 루트 `AGENTS.md` 가 *"Deleted paths and modified paths
cannot go in the same `cm ci`"* 라고 못박았으므로 둘은 어차피 갈라야 하는데, 순서가 정해져 있다 —
결합을 먼저 끊어야 **삭제 직후에도 컴파일이 통과한다.** 반대로 하면 중간 체인지셋이 빌드되지 않는다.

| # | 체크인 | 종류 | 내용 |
|---|---|---|---|
| 1 | 설계 | 추가 | 이 문서 |
| 2 | 회귀 확인 | — | Raymarch 얼룩말 확인(§10). 나오면 여기서 룩 수정이 끼어든다 |
| 3 | 결합 해체 | **수정만** | `SnowCpuStage` 블레이드 경로 제거(§4), `SnowballCoopTimingHud` 의 `MultiplayPenguin` 결합 제거, `SessionLauncher` 의 씬 경로·기본 아바타 정리 |
| 4 | 로비 프리팹 이동 | **이동만** | `PF_SessionLobby` 를 `Core/Multiplay/Resources/` 로. Unity Project 창에서 옮겨 GUID 를 지킨다 |
| 5 | 구 멀티 제거 | **삭제만** | `InGame/Multiplay/` 전체 + 죽은 PlayMode 테스트 4개 |
| 6 | 명령 복제 | 수정 | `SendDelta` 제거, `SnowCommandWire` 배선, 확정 틱 적용(§6) |
| 7 | 펭귄 | 추가 | 새 플레이어 · 프리팹 · 게임플레이 씬 · 입력 비트 정리(§9) |
| 8 | 스냅샷 + 마찰 | 추가 | 접속 스냅샷(§8), 3단계 양자화(§7) |
| 9 | 문서 | 수정 | 폴더 `AGENTS.md` · `INDEX.md` · 세션 요약 |

3 → 5 사이에는 게임플레이 씬이 없는 구간이 생긴다. 컴파일은 통과하지만 **멀티는 7 번까지
플레이할 수 없다.** 의도된 것이고, 그 구간을 짧게 유지하는 것이 3~7 을 한 브랜치에 두는 이유다.
