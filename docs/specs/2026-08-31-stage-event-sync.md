# 스테이지 이벤트 동기화 — 시간과 날씨

날짜: 2026-08-31 · 브랜치: `/main/multiplay-gift-machine`

## 1. 무엇이 문제인가 (실측)

### 시간은 동기화되는 것이 아니라 **우연히 나란히 도는** 것이다

`TimeOfDayDirector` 는 네트워크를 전혀 모른다(`[Networked]` 0개, `Runner` 참조 0개).
자기 `Update()` 에서 로컬 시계로 굴린다.

```csharp
NormalizedTime = Mathf.Repeat(previous + (delta / _config.SecondsPerDay), 1f);
```

모든 피어가 같은 `StartTimeOfDay` 에서 출발하고 서버가 씬을 동시에 올리므로 **맞아 보인다.**
실제로는 각자 굴러서 어긋나고, **늦게 들어온 사람은 아침부터 시작한다.**

### 눈보라는 클라이언트에서 아예 실행되지 않는다

`BlizzardEvent.Trigger()` 가 권위를 요구한다.

```csharp
if (_snowStage == null || _snowStage.Field == null || !_snowStage.HasSimulationAuthority)
{
    Debug.LogWarning("권위 CPU 눈 필드가 준비되지 않아 시작하지 못했다.");
    return false;   // ← 클라이언트는 항상 여기서 나간다
}
```

`HasSimulationAuthority` 는 서버에서만 참이다. 게다가 시작을 부르는 쪽도 각 피어의 **로컬 날짜
카운터**다(`TimeOfDayDirector.DayAdvanced` → `StageDateCoordinator` → `ScheduledBlizzardDirector`).
**눈보라는 호스트에서만 돌고 클라이언트는 아무것도 보지 못한다.**

## 2. 이미 있는 방식을 따른다 — 발명하지 않는다

`ThiefNetworkHub` 가 같은 문제를 이미 풀었다. 그 모양이 이 프로젝트의 정답이다.

- 로컬 시스템(`ThiefActor`)은 **네트워크를 모른 채** 둔다.
- 옆에 붙은 `NetworkBehaviour` 가 **관찰 가능한 원인만** `[Networked]` 로 복제한다.
- `PresentedX` 속성이 갈라 준다 — **클라이언트는 복제값, 서버는 로컬값**을 읽는다.

```csharp
public EThiefAction PresentedAction => Object != null && Object.IsValid && !Object.HasStateAuthority
    ? (EThiefAction)NetAction : _actor.CurrentAction;
```

`MissionNetHub` 도 같다. 그리고 그것은 씬에 놓인 것이 아니라 `PF_MissionHub` 프리팹으로
**런타임에 스폰된다**(`MissionNetSpawner`). 그래서 여기에 필드를 더하는 데 **씬 수정이 필요 없다.**

## 3. 확장 가능해야 한다 — 그러나 프레임워크는 만들지 않는다

앞으로 이벤트가 더 붙는다. 그래서 **새 이벤트를 더할 때 중앙 파일을 고치지 않아도 되는** 모양이어야
한다. 다만 루트 규약은 "추상은 두 번째 호출부가 확인된 뒤" 이고, 지금 그 조건이 성립한다.

**굳힐 것은 딱 하나다 — 이 프로젝트가 이미 세 번 손으로 반복하고 매번 주석으로 경고한 그 패턴.**
`NetJumpCount`, `NetAttackCount`, `NetClosedTicket` 이 전부 같은 모양이다:

```csharp
[Networked] private byte NetAttackCount { get; set; }   // 펄스가 아니라 계수기다
...
_renderedAttackCount = NetAttackCount;                  // Spawned 에서 프라이밍
...
if (NetAttackCount != _renderedAttackCount) { ... }      // Render 에서 비교
```

세 조각을 매번 손으로 쓰는데, **하나라도 빠지면 증상이 네트워킹처럼 보이지 않는다** — 펄스로 쓰면
그 틱을 못 본 피어가 이벤트를 통째로 놓치고, 프라이밍을 빠뜨리면 늦게 들어온 사람이 **과거 이벤트를
전부 재생한다.**

### 뼈대 — `StageEventNetBehaviour`

```csharp
/// 한 번 일어나는 스테이지 이벤트를 복제하는 공통 뼈대.
/// 페이로드는 파생 클래스가 자기 [Networked] 필드로 선언한다 — 여기서 다루지 않는다.
public abstract class StageEventNetBehaviour : NetworkBehaviour
{
    [Networked] protected byte Ticket { get; private set; }

    private byte _seen;

    /// 서버가 아닌 피어. 파생 클래스가 "복제값을 읽을 것인가" 를 판단할 때 쓴다.
    protected bool IsFollower => Object != null && Object.IsValid && !Object.HasStateAuthority;

    public override void Spawned() => _seen = Ticket;   // 늦게 들어온 사람은 과거를 재생하지 않는다

    /// 서버가 이벤트를 낸다. <b>페이로드를 먼저 쓰고</b> 이것을 부른다.
    protected void RaiseOnServer()
    {
        if (Object.HasStateAuthority) Ticket = unchecked((byte)(Ticket + 1));
    }

    public override void Render()
    {
        if (!IsFollower || _seen == Ticket) return;
        _seen = Ticket;
        OnEventRaised();
    }

    /// 복제된 페이로드를 읽어 이 피어에서 이벤트를 재생한다.
    protected abstract void OnEventRaised();
}
```

**이것이 전부다.** 이벤트 목록도, 페이로드 직렬화도, 등록 절차도 없다 — 그런 것을 만들면
`[Networked]` 의 타입 안전을 버리고 디버깅만 어려워진다.

### 새 이벤트를 더하는 법

1. `StageEventNetBehaviour` 를 상속한 클래스를 만든다.
2. 필요한 페이로드를 **자기 `[Networked]` 필드로** 선언한다.
3. 서버 경로에서 페이로드를 쓰고 `RaiseOnServer()` 를 부른다.
4. `OnEventRaised()` 에서 그 페이로드로 로컬 시스템을 돌린다.
5. `PF_MissionHub` 프리팹에 컴포넌트로 붙인다.

**중앙 파일을 고칠 일이 없다.** 5번은 프리팹 한 번이고, 그 프리팹은 이미 런타임에 스폰된다
(`MissionNetSpawner`) — 씬 수정도 없다.

## 4. 이번에 만드는 둘

### `BlizzardNetHub : StageEventNetBehaviour` — 한 번 일어나는 이벤트

| 필드 | 형 |
|---|---|
| `NetStart` · `NetSecondRegion` · `NetDirection` | `Vector2` |
| `NetTravel` | `float` |

서버의 `StageDateCoordinator` 가 날이 바뀌면 `BlizzardRoutePlanner.TryPlan` 으로 경로를 정하고,
그 넷을 쓴 뒤 `RaiseOnServer()`. 클라이언트의 `OnEventRaised()` 는 **권위를 요구하지 않는**
`BlizzardEvent.Trigger(BlizzardRoutePlan)` 오버로드를 부른다(이미 있다).

`BlizzardRoutePlan` 은 `Vector2` 셋 + `float` 하나뿐이라 복제 비용이 사실상 없다.

### `TimeOfDayNetHub : NetworkBehaviour` — 계속 흐르는 상태

**뼈대를 쓰지 않는다.** 시간은 "일어나는 사건" 이 아니라 이어지는 값이라 티켓이 맞지 않는다.
억지로 같은 추상에 넣으면 둘 다 이상해진다.

| 필드 | 형 |
|---|---|
| `NetNormalizedTime` | `float` |

서버가 매 틱 `TimeOfDayDirector` 에서 복사하고, 클라이언트는 `SetNormalizedTime(net)` 로 따라간다.

**`DayIndex` 는 복제하지 않는다(구현하며 뺐다).** 읽는 곳이 `StageDateCoordinator` 하나뿐이고 그쪽이
서버 전용이 되므로, 클라이언트에서 쓰이지 않는다. 쓰지 않는 값을 실어 보내지 않는다.

## 5. 흐름

```
서버                                        클라이언트
  TimeOfDayDirector (로컬 시계)
      ↓ 매 틱 복사
  TimeOfDayNetHub.NetNormalizedTime  ──────>  SetNormalizedTime(net)

  StageDateCoordinator (서버 전용이 된다)
      ↓ 날이 바뀌면
  BlizzardRoutePlanner.TryPlan()
      ↓ 경로를 쓰고
  BlizzardNetHub.RaiseOnServer()     ──────>  Ticket 이 오르면
                                              BlizzardEvent.Trigger(route)
```

**`StageDateCoordinator` 의 트리거는 서버 전용이 된다.** 지금은 모든 피어가 각자 부른다.

## 6. 결정과 기각

- **`TimeOfDayDirector` 와 `BlizzardEvent` 는 네트워크를 모르는 채로 둔다.** `ThiefActor` 선례이고,
  그래야 싱글플레이가 지금과 똑같이 돈다.
- **허브는 `PF_MissionHub` 에 컴포넌트로 붙인다.** 새 `NetworkObject` 를 이벤트마다 만들지 않는다 —
  스폰·수명·프리팹 표 등록을 한 벌씩 더 만드는 값을 하지 못한다.
- **시간은 매 틱 덮어쓴다.** `SecondsPerDay` 가 분 단위라 틱당 변화가 미세해서 튀지 않는다.
  보간·예측은 넣지 않는다.
- **기각 — 이벤트 id 와 공용 페이로드를 가진 이벤트 버스.** `[Networked]` 는 고정 용량이고 타입이
  있다. 공용 페이로드로 만들면 blob 직렬화와 캐스팅이 생기고, 잘못 넣어도 컴파일이 통과한다.
  **파생 클래스가 자기 필드를 선언하는 쪽이 더 안전하고 더 짧다.**
- **기각 — 각 피어가 같은 시드로 같은 경로를 계산하기.** 경로는 **현재 눈 필드 상태**를 읽어 정하는데
  (`TryPlan(field, ...)`), 필드는 피어마다 자기가 시뮬한 결과라 완전히 같다는 보장이 없다. 같은 시드가
  같은 결과를 준다는 가정이 조용히 깨지는 자리다.
- **기각 — 눈보라 진행 중 상태까지 복제.** 이번에는 안 한다. 늦게 들어온 사람은 다음 눈보라부터 본다.
  진행 중 합류는 값이 더 필요하고 지금 그 근거가 없다.

## 7. 합격 기준

1. 클라이언트의 `TimeOfDayDirector.NormalizedTime` 이 서버 값과 **0.01 이내**로 맞는다.
2. **늦게 들어온 클라이언트**가 서버의 현재 시간대에서 시작한다(0에서 시작하지 않는다).
3. 서버에서 눈보라가 시작되면 **클라이언트에서도 시작하고, 경로가 같다**(`Start`·`Direction` 일치).
4. 클라이언트 콘솔에 `"권위 CPU 눈 필드가 준비되지 않아 시작하지 못했다"` 가 **더 이상 나오지 않는다.**
5. 싱글플레이는 변화가 없다 — `BlizzardEventPlayModeTests` 6개가 그대로 통과한다.

3·4 번은 MPPM 호스트+클라 세션에서 `ScheduledBlizzardDirector` 의 테스트 트리거로 재현한다.

6. **뼈대가 실제로 확장 가능한지**는 세 번째 이벤트를 붙일 때 드러난다. 그때 `StageEventNetBehaviour`
   를 고쳐야 한다면 이 설계가 틀린 것이므로, 그 사실을 여기 적고 다시 본다.

## 8. 도둑 — 세 번째 이벤트이자 이 설계의 판정 대상 (2026-08-31 조사)

도둑은 이미 **두 모양을 다 쓴다.** 그래서 §3·§4 가 갈래를 둘로 나눈 것이 맞는지 여기서 드러난다.

| 도둑의 조각 | 모양 | 지금 상태 |
|---|---|---|
| 습격 시작(의뢰 실패 → 8~25초 뒤 스폰) | **일회성 이벤트** | `ThiefDirector` 가 서버 권위로 스폰만 한다. 복제 없음 |
| 몸짓(동작·걸음·화물·드는 단계) | **이어지는 상태** | `ThiefNetworkHub` 가 이미 복제한다 |

**아직 스테이지에 연결되어 있지 않다.** `ThiefRaidSchedule` 을 부르는 프로덕션 코드가 없고
(테스트만 쓴다), `ThiefDirector` 도 `ThiefNetworkHub` 도 `MultiPlay`·`SinglePlay` 어느 씬에도 없다.

**판정 방법.** 도둑을 스테이지에 붙일 때 습격 시작을 `StageEventNetBehaviour` 를 상속한
`ThiefRaidNetHub` 로 얹는다. 그때 **뼈대 파일을 고쳐야 한다면 이 설계가 틀린 것이다.** 고치지 않고
얹히면 맞은 것이다. 어느 쪽이든 결과를 여기 적는다.

## 9. 검증 결과 (2026-08-31 실측, MPPM 호스트+클라)

서버와 클론을 **동시에** 재서 확인했다.

| | 서버 | 클론 | 차이 |
|---|---|---|---|
| 시각 | 0.0230 | 0.0259 | 0.0029 |
| | 0.0573 | **0.0573** | **0.0000** |
| | 0.0808 | **0.0808** | **0.0000** |
| | 0.1043 | 0.1072 | 0.0029 |
| 눈보라 | Warning → Active | Warning → Active | 같이 전이 |

남은 0.003 은 두 프로브를 순차로 찍는 사이에 실제로 흐른 시간이다(초당 약 0.006 진행).

- §7-1 **통과** — 0.01 기준보다 훨씬 안쪽이다.
- §7-3 **통과** — 서버가 낸 눈보라가 클라이언트에서도 같은 단계로 돈다.
- §7-4 **통과** — 클론 로그에 `권위 CPU 눈 필드가 준비되지 않아 시작하지 못했다` 가 0건이다.
- §7-5 **통과** — `BlizzardEventPlayModeTests` 6개 전부 통과(싱글 무회귀).
- §7-2 **미측정** — 진행 중 합류는 재현하지 못했다. 구조상 스폰 직후 첫 `Render` 에서
  `NetNormalizedTime` 을 받으므로 맞을 것으로 보이지만, **본 것은 아니다.**

### ⚠ 판정에 쓴 방법 — 클론에도 CLI 로 붙는다

이제까지 클라이언트는 로그로만 봤는데, **클론도 파이프라인 서버를 띄운다**(실측: 본체 7800,
클론 7801). `unity status` 는 본체만 보여 주지만 프로젝트 경로를 클론 폴더로 주면 붙는다.

```
unity command eval_file <스크립트> --project-path <프로젝트>/Library/VP/mppmXXXXXXXX
```

**이것이 없으면 이번 검증을 할 수 없었다.** 처음에 서버가 `Active` 일 때 클론이 `Idle` 로 보여
동기화가 안 되는 줄 알았는데, 두 프로브를 몇 초 간격으로 찍어서 생긴 착시였다 — 같은 순간에
재야 판정이 된다.
