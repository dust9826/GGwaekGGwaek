# 싱글·멀티 개발 파이프라인 — 씬 스탬핑을 유지하되 차이를 런타임으로 옮긴다

날짜: 2026-08-31 · 브랜치: 미생성(작성 시점 `/main`)

증강 시스템과 일차별 이벤트를 설계하기 전에, 두 모드의 현재 관계를 확정한다.

## 1. 전제 — 두 모드는 같은 게임이다

사용자 확인(2026-08-31): **싱글과 멀티는 둘 다 정식 모드이고, 종료 흐름과 게임 흐름이 같다.**
차이는 **인원 수와 그에 따른 밸런스**뿐이며, 증강처럼 인원을 조건으로 갈리는 것이 더해질 수 있다.

이 전제는 `Cleanliness/AGENTS.md` 의 **"멀티플레이의 종료 흐름은 아직 정하지 않았다"** 를 대체한다.
그 항목은 미정이 아니라 **싱글과 같다**로 확정한다.

## 2. 지금 관계 (실측)

### 씬은 찍어 낸다

`MultiPlaySceneBuilder` 가 `SinglePlay.unity` 를 열어 `MultiPlay.unity` 경로로 저장한 뒤 네 가지를 한다.
두 씬은 114,686 / 114,835 줄로 99.9% 동일하다.

| 단계 | 성격 |
|---|---|
| `RemoveLocalPlayer` — 씬의 펭귄 제거 | 네트워크 배선 |
| `BuildSpawnPoints` · `BuildMissionSpawner` · `BuildGiftSupplier` · `BuildBootstrap` | 네트워크 배선 |
| `ConfigureSnow` | 네트워크 배선 |
| `DisableSinglePeerRigs` — 피어별로 갈릴 것 끄기 | **런타임 사실을 에디터가 표현하고 있다** |

### 게임플레이 본체는 한 벌이다

`GameManager`(단계·점수·종료) · `RequestDirector`(의뢰) · `RequestStageFlowPresenter`(Intro/HUD/Outro)
가 양쪽에서 같은 코드로 돈다. 멀티는 그 위에 창구만 얹는다 — `MissionNetHub` 가 서버의 두 디렉터
상태를 `[Networked]` 로 복사하고, 클라이언트에서는 두 디렉터를 끈다. `PenguinNetAvatar` 가
`PenguinLocomotion` 을 감싸는 것과 같다. *"같은 게임을 두 벌 만들지 않는다."*

### `DisableSinglePeerRigs()` 는 현재 no-op 이다 (실측)

| 항목 | SinglePlay | MultiPlay | 빌더의 실제 효과 |
|---|---|---|---|
| `GiftDeliveryDirector` | 있으나 이미 `m_Enabled: 0` | 0 | 없음 |
| `GiftSpawner` | 없음 | 없음 | 없음 — `_Recovery/0.unity` 와 `Delivery_RequestFlow_Test` 에만 존재 |
| `RequestGameDebugHud` | 없음 | 없음 | 없음 — 어느 씬·프리팹에도 없다 |
| `RequestCompletionCondition` 파생 | 없음 | 없음 | 없음 — 유일한 파생이 `Tests/` 이고 미배치 |

죽은 코드가 아니라 **아직 아무것도 걸리지 않은 보험**이다. 누군가 SinglePlay 에 디버그 HUD 를
놓는 날 작동한다.

### 인원 수는 밸런스에 들어가 있지 않다 (실측)

`StageBalanceConfig` 에 인원 항이 하나도 없다. `PlayerCount` 를 읽는 곳은 로비 표시와 매치 시작
판정뿐이고, **게임플레이 밸런스에 인원이 들어가는 코드는 0건**이다. 지금 2인 플레이는 1인과
완전히 같은 난이도로 돈다. "인원이 늘면 밸런스가 달라진다"는 아직 어디에도 표현돼 있지 않다.

### 씬에 놓인 `NetworkObject` 는 0개다 (실측)

모든 복제 상태는 런타임 스폰이다. 이유는 `MissionNetSpawner` 주석에 실측으로 남아 있다 —
로비 단계에서 스폰하면 뒤이은 `StartMatch` 의 씬 로드가 삼키고 콘솔에는 아무것도 남지 않는다.

## 3. 결정 — 스탬핑은 유지하고, 차이를 런타임으로 옮긴다

빌더의 diff 를 **네트워크 배선만**으로 유지한다. "피어마다 갈릴 것"은 에디터가 끄는 대신
컴포넌트가 스스로 판단한다.

**이관 작업량은 0이다.** §2에서 보듯 끄기 목록이 이미 아무것도 잡고 있지 않으므로, 이 결정은
*고치는 일*이 아니라 **지금 상태를 규약으로 굳혀서 증강·일차별 이벤트가 그것을 깨지 않게 하는 일**이다.

### 실제로 할 일

1. `StageSession` 신설 (§5) — 유일한 신규 코드
2. `SnowCpuStage` 의 `NetworkRunner.Instances.Count == 0` 판정을 `StageSession` 으로 교체 —
   누수 방어가 실제로 붙는 곳이자 §9 사고의 진원
3. 세 상태 게이트 규약을 `Core/Multiplay/AGENTS.md` 에, 빌더 수정 기준(§6)을
   `Cleanliness/AGENTS.md` 에 적는다
4. `Cleanliness/AGENTS.md` 의 "멀티플레이의 종료 흐름은 아직 정하지 않았다"를 결정으로 바꾼다 (§1)

기존 컴포넌트를 게이트로 옮기는 작업은 **없다.** 신규 코드만 규약을 따른다.

### 채택하지 않은 것

- **씬을 하나로 합치기.** `MultiPlay.unity` 를 없애고 런타임에 네트워크 리그를 붙이는 안. 두 씬이
  갈릴 여지가 사라지지만, 잘 도는 파이프라인을 크게 흔들고 씬에서 보이던 배선이 코드로 숨는다.
  스탬핑을 유지해도 이득의 대부분을 얻으므로 지금 시점에 과하다.
- **지금 그대로.** 기능마다 "멀티에선 이걸 끈다"가 한 줄씩 붙어, 그 목록이 곧 제일 읽기 어려운
  파일이 된다.

## 4. 규약 — 세 상태 게이트

```
Runner 없음              → 싱글.        그대로 돈다
Runner 있음 + IsServer   → 권위(호스트).  그대로 돈다   ← 위와 같은 코드
Runner 있음 + !IsServer  → 클라이언트.    판정하지 않고 복제값만 읽는다
```

**첫째와 둘째가 같은 코드라는 것이 이 규약의 전부다.** 그것이 "흐름이 같다"를 코드로 표현한 형태다.

셋째 상태에서 무엇을 읽을지는 이미 이 프로젝트의 답이 있다 — `ThiefNetworkHub.PresentedAction`
(`docs/specs/2026-08-31-stage-event-sync.md`)의 `PresentedX` 패턴을 따른다. 로컬 시스템은
네트워크를 모른 채 두고, 옆에 붙은 `NetworkBehaviour` 가 관찰 가능한 원인만 복제한다.

## 5. `StageSession` — 판정을 한 곳에 모은다

`Core/Multiplay/` 에 둔다. `SessionLauncher`·`SessionLobby`·`StageEventNetBehaviour` 가 있는
자리이고, 인원 수의 출처인 `ExpectedPlayerCount` 도 거기 있다. 소비자가 `Snow`·`Cleanliness`·
`Delivery`·`Interaction/Thief`·신규 증강으로 이미 여럿이라 "두 번째 소비자에서 승격" 규칙을 넘겼다.

```csharp
public readonly struct StageSession   // Core/Multiplay/
{
    public NetworkRunner Runner   { get; }  // null = 싱글
    public bool IsAuthority       { get; }  // 싱글이거나 서버
    public bool IsFollower        { get; }  // 세션 있고 서버 아님
    public int  PlayerCount       { get; }  // 싱글 = 1, 멀티 = ExpectedPlayerCount

    public static StageSession For(GameObject owner);
}
```

호출부는 프레임마다 조회하지 않고 한 번 받아 캐시한다.

**판정 로직은 `For` 가 아니라 순수 함수 `Resolve` 가 갖는다** — Fusion 상태를 EditMode 에서
만들어 낼 수 없기 때문이다. 분리 이유와 신호 형태는 §10.

**`PlayerCount` 를 `ExpectedPlayerCount` 에서 가져오는 것은 의도적이다.** 그 값은 매치 시작
시점에 잠긴다. 현재 접속 인원(`Runner.ActivePlayers`)을 쓰면 중간에 나간 사람 때문에 밸런스가
판 도중에 흔들린다. `MissionNetHub` 가 같은 이유로 이미 그렇게 하고 있다.

### 누수 방어를 여기에 한 번만 넣는다

`NetworkRunner.GetRunnerForScene` 는 막아 주지 않는다 — 2026-08-31 실측으로, MPPM `host` 태그를
단 채 SinglePlay 에 들어가면 그 씬에서도 `SessionPeer_Host` 를 그대로 돌려준다. 그래서 한 겹 더 본다.

```
SessionLauncher.GameplayScenePath != owner.scene.path  →  Runner = null (싱글로 취급)
```

⚠ 이것은 **방어이지 증명이 아니다.** 근본 원인은 태그 위생이다(§9). 다만 이 결정이 판정을 훨씬
많은 컴포넌트로 퍼뜨리므로, 틀렸을 때 고칠 자리를 하나로 두는 것 자체에 값이 있다.

## 6. 빌더를 언제 고치는가

| 새 기능이 필요로 하는 것 | 해야 할 일 |
|---|---|
| 일반 컴포넌트 (씬 오브젝트·UI·연출·판정) | SinglePlay 에 넣는다. **빌더 코드 수정 없음.** 빌드만 다시 |
| `[Networked]` 복제 상태 | NetworkObject 프리팹 + 서버 스폰 리그 → **빌더에 한 줄** |

**씬 편집은 언제나 SinglePlay 에서 한다.** `MultiPlay.unity` 를 직접 고치면 다음 빌드에서 사라진다.

## 7. 증강·일차별 이벤트가 얹히는 자리

이 문서는 두 시스템을 설계하지 않는다. 규약 위에서 어디에 붙는지만 고정한다.

- **일차별 이벤트** — 스케줄러는 `ScheduledBlizzardDirector` 처럼 씬 오브젝트라 빌더 수정 0.
  "사건이 일어났다"는 `StageEventNetBehaviour` 파생으로 알린다(`BlizzardNetHub` 가 선례).
  ⚠ **`TimeOfDayNetHub` 는 `DayIndex` 를 의도적으로 복제하지 않는다** — 읽는 곳이
  `StageDateCoordinator` 하나뿐이고 그쪽이 서버 전용이라서다. 클라이언트가 날짜를 알아야 하는
  요구(예: "3일차" HUD)가 생기면 그 결정을 뒤집어야 하고, 근거를 남겨야 한다.
- **증강** — per-player 면 아바타 프리팹에 붙고 아바타는 이미 서버가 스폰하므로 빌더 수정 0.
  파티 공용이면 허브가 하나 필요해 빌더에 한 줄. **아직 정해지지 않았다**(§11).
- **인원 조건과 밸런스** — 둘 다 `StageSession.PlayerCount` 를 읽는다. 실제 수치 튜닝은 하지 않는다.

## 8. 이번 범위 밖으로 둔 것

`SnowGiftMachinePresentation` 이 선물 생성 경로를 두 벌 하드코딩하고 있다 — `_isNetworkConversion`
이면 `GiftNetSpawner`, 아니면 로컬 `Instantiate`. 2026-08-31 의 "네트워크 선물을 생성하지 못했다"
에러가 그 분기에서 났다. 이 결정이 없애려는 모양 그 자체라 `StageSession` 위에서 한 경로로 합칠
후보지만, 지금 건드리면 범위가 번진다. **기록만 남기고 손대지 않는다.**

`DisableSinglePeerRigs()` 는 비용이 0이고 잡을 날이 오므로 **보험으로 남긴다.**

## 9. 배경 — 2026-08-31 의 누수 사고

MPPM `host` 태그가 메인 에디터에 남아 있으면 MainMenu 에서 Fusion Host 세션이 자동으로 열린다.
러너는 `DontDestroyOnLoad` 라 그 뒤 SinglePlay 에 들어가도 따라온다. 그러면 SinglePlay 인데도
`SnowCpuStage` 가 standalone 을 끄고(`NetworkRunner.Instances.Count == 0` 판정) 눈덩이가
`NetworkObject` 로 스폰되고, 눈덩이 교환기가 네트워크 경로를 타서 `GiftNetSpawner` 를 찾는다.
그것은 `MultiPlay.unity` 에만 있으므로 매번 실패한다.

일차 조치는 태그 위생(검증 끝나면 태그를 뗀다)이고, `StageSession` 의 씬 경로 대조는 이차 방어다.

## 10. 검증

**EditMode 에서 `NetworkRunner` 의 상태를 만들어 낼 수 없다.** 컴포넌트를 붙여도 `IsRunning` 이
false 이고 `IsServer` 는 세션이 실제로 시작돼야 참이 된다. 둘 다 세터가 없다. 그래서 판정 로직을
Fusion 에서 떼어 낸다.

```csharp
// 순수 함수 — Fusion 을 모른다. EditMode 로 전부 덮는다.
internal static StageSession Resolve(bool hasSession, bool isServer, int expectedPlayerCount);

// 얇은 결선 — 러너 조회와 씬 경로 대조만 한다.
public static StageSession For(GameObject owner);
```

`For` 는 러너를 찾고, `GameplayScenePath` 와 소유자 씬 경로를 대조해
`hasSession = runner != null && runner.IsRunning && 경로일치` 를 만든 뒤 `Resolve` 에 넘긴다.
**분기 로직은 전부 `Resolve` 에 있고 `For` 에는 없다.**

EditMode 로 덮는 것:

- `Resolve` 의 세 상태 — 세션 없음 / 세션+서버 / 세션+클라이언트.
- 세션이 없으면 `PlayerCount == 1`.
- 세션이 있으면 `PlayerCount == expectedPlayerCount`. 인자로 받으므로 판 도중 접속 인원이
  변해도 흔들리지 않는 것이 구조로 보장된다.
- **회귀:** 경로가 어긋나면 호출자가 `hasSession = false` 를 만들어 싱글로 떨어진다.
  씬 경로 대조 자체를 순수 함수로 따로 뽑아 이것도 EditMode 로 덮는다 — §9 사고의 재발 방지이고,
  이 항목이 없으면 방어가 조용히 풀린다.

`For` 의 Fusion 결선(러너 조회 5줄)은 테스트하지 않는다. 그 경로는 멀티를 실제로 띄울 때 드러난다.

`SnowCpuStage` 교체는 기존 `SnowHeadlessTests` 가 회귀를 잡는다. 새 테스트는 더하지 않는다.

PlayMode 는 쓰지 않는다 — CLI 수집기가 세션당 한 번만 돌고, 이 범위는 EditMode 로 충분하다.

## 11. 열린 결정

1. **증강이 per-player 인가 파티 공용인가.** 복제 모델과 빌더 수정 여부가 여기서 갈린다.
2. **`StageSession` 이라는 이름.** `Stage` 가 `Cleanliness` 쪽 단어라 `Core` 에 두기엔 어색할 수
   있다. `SessionScope` · `NetSession` 이 대안이다.
3. **인원별 밸런스를 테이블로 둘지 배율로 둘지.** 이번에는 `PlayerCount` 를 읽을 수 있게만 하고
   수치는 정하지 않는다.
