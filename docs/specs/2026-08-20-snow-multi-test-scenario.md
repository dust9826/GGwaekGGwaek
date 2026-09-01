# 눈 멀티 테스트 — MPPM Play Mode Scenario

날짜: 2026-08-20 · 시나리오: `Assets/Game/InGame/Snow/Tests/SnowMulti.asset`

**결론부터**: 씬 열고, Play 드롭다운에서 `SnowMulti` 고르고, **Play.** 나머지는 코드가 한다.

---

## 0. 무엇이 무엇을 하는가

|조각|역할|
|---|---|
`SnowMulti.asset`|시나리오. 메인 에디터 1(태그 `server`) + 추가 에디터 1(태그 `client`)|
`MultiplayerRoleBootstrap`|**태그를 읽어 역할을 정한다.** `server` 면 방을 열고, `client` 면 붙는다|
`SessionLauncher`|실제 포톤 세션. 서버는 `GameMode.Server`, 클라는 `GameMode.Client`|

**태그가 이 흐름의 핵심이다.** 부트스트랩은 **태그가 없으면 아무것도 하지 않는다** - 그것이
"사람이 로비 UI 로 직접 방을 만드는" 시나리오이고, 태그 없는 클론이 자동 접속을 시도하다
`GameNotFound` 로 죽은 실측 때문에 그렇게 정해졌다.

|태그|무엇|
|---|---|
`server`|이 인스턴스가 방을 연다. **플레이어가 들어오면 매치를 자동 시작한다**(START 클릭 없음)|
`client`|이 인스턴스가 방에 붙는다|
`room:XXXX`|방 코드를 바꾼다. 없으면 `MPPMDEV`|

태그 정의는 `ProjectSettings/VirtualProjectsConfig.json` 에 이미 있다(`server`, `client`).
새로 만들려면 **Edit > Project Settings > Multiplayer > PlayMode > Player Tags**.

---

## 1. 왜 MPPM 인가 — 워크스페이스 둘을 안 띄운다

눈은 **두 프로세스**에서만 진짜로 검증된다. 자동 테스트는 `PeerMode = Multiple` 로 한 프로세스에
피어를 여럿 띄우고, 그 경로에는 포톤 릴레이도 직렬화도 왕복도 없다.

MPPM 의 **추가 에디터 인스턴스도 진짜 자식 프로세스**다. 그래서 같은 조건을 만족하면서 워크스페이스는
하나면 된다 - 에셋·코드는 공유하고 인스턴스별 설정만 `Library/VP/` 아래 따로 생긴다.

---

## 2. 하는 법

1. **Window > Play Mode > Scenarios** 에서 `SnowMulti` 가 보이는지 확인한다.
   - 메인: 태그 `server` · 추가 에디터 1개: 태그 `client`
2. 아무 씬이나 열어도 되지만 **`MainMenu`** 를 권한다 - 로비 UI 가 있어 상태가 보인다.
3. Play 드롭다운에서 `SnowMulti` 를 고른다.
4. **Play.**

그 다음 자동으로:

```
메인 에디터(server 태그) -> 방 MPPMDEV 열기 -> 플레이어 들어오면 매치 시작 -> MP_Gameplay 로드
추가 에디터(client 태그) -> 방 MPPMDEV 에 붙기 -> 서버가 올린 씬을 따라 로드
```

콘솔에 이렇게 남는다:

```
[MPPM] 이 인스턴스는 서버다 - 방 MPPMDEV 을 연다.
[MPPM] 플레이어 1명 - 매치를 시작한다.
```

### 조작

|키|무엇|
|---|---|
`W/A/S/D`|이동|
`Shift`|달리기|
**`Space` 또는 좌클릭**|뭉치기 / 놓기|

추가 에디터 창으로 이동: **Cmd+F10**(Player 2), Cmd+F11(Player 3).

---

## 3. 무엇을 보면 되는가

|보는 것|정상|
|---|---|
두 창의 눈 표면|같은 자리에 같은 자국|
공|양쪽에서 같은 크기로 자란다(질량이 `[Networked]`)|
클라 화면의 남의 자국|서버가 깎은 자리가 몇 프레임 안에 클라에도 파인다|

숫자로 보고 싶으면 `SnowMultiProbe` 를 두 인스턴스의 씬에 붙인다 - 1.5 초마다 고정 좌표의 깊이와
서버 대기열, 보낸 셀 수를 콘솔에 남긴다.

**실측 기준선**(2026-08-20): 같은 차선의 표본이 모든 점에서 일치, 필드 총량 차이 0.0014%,
32 초 유지. 자세한 것은 `Snow/AGENTS.md`.

---

## 4. 함정

- **태그가 없으면 아무 일도 안 일어난다.** 의도된 것이다. 시나리오 창에서 인스턴스마다 태그가
  붙어 있는지 확인한다.
- **도메인 리로드가 꺼져 있다**(`DisableDomainReload`). 그래서 `SessionLauncher` 의 static 이 지난
  Play 의 값을 들고 있었고, 두 번째 Play 부터 "이미 서버 피어가 있다" 로 방을 못 만들었다 -
  증상이 "포톤 연결 실패" 처럼 보인다. `RuntimeInitializeOnLoadMethod` 로 Play 시작마다 비우게
  고쳤다(cs:476). **이 함정은 도메인 리로드를 끈 프로젝트의 모든 static 에 해당한다.**
- **추가 에디터가 처음 뜰 때 느리다.** `Library/VP/` 를 처음 채우기 때문이고, 두 번째부터 빠르다.
  Active Scenario 창의 **Keep Active** 를 켜면 Play 를 끊어도 프로세스가 남아 더 빠르다.
- **`MP_Gameplay` 가 빌드 세팅에 있어야 한다.** 서버가 `LoadScene` 으로 올린다.
- **시나리오 에셋은 코드로 만들었다.** `OrchestratedScenario` 는 `ScriptableObject` 이고 필드가
  구체적이지만 **내부 API** 다 - 유니티 버전이 바뀌면 여기가 먼저 깨진다. 창에 안 보이면 손으로
  만들면 되고 결과물은 같다: `+` -> 이름 -> **Editor** 체크 -> **Additional Editor Instances** `+`
  -> 각각 태그를 `server` / `client` 로.
- **Scenarios 창의 *local instance* 는 쓰지 않는다.** 실제 빌드를 만들어 돌리는 것이라 Build Profile
  과 `com.unity.dedicated-server` 패키지를 요구한다(우리에게 없다).

## 시나리오는 코드로 띄울 수 있다 (2026-08-20 실측)

"MPPM 은 GUI 라서 자동화가 안 된다" 는 앞 절의 전제가 틀렸다. 실제로 띄웠다.

```
ScenarioFactory.CreateScenario(OrchestratedScenario owner, IEnumerable<IInstanceItem> items)
ScenarioRunner.LoadScenario(scenario)      // static
ScenarioRunner.StartScenario()             // static
ScenarioRunner.StopScenario()              // static
```

`items` 는 `OrchestratedScenario.GetAllInstances()` 가 준다. 확인한 결과: 메인 에디터가 Play 로
들어가고 `Library/VP/` 에 추가 인스턴스 폴더가 생기고 그 프로세스가 실제로 뜬다.

`CreateScenario` 의 두 번째 인자는 **정확한 제네릭 배열**이어야 한다 - `List<object>` 를 넘기면
조용히 실패한다. `IInstanceItem[]` 로 만들어 넘긴다.

### 함정 1 — `m_PlayerTag` 는 태그가 아니다

에셋 YAML 에 `m_PlayerTag: server` 가 보이고 리플렉션으로 쓸 수도 있는데, **그 필드는 읽히지
않는다.** 실제로 태그를 정하는 것은 인스턴스 항목의 `m_Settings.PlayerTag` 다
(`MainEditorController.InstanceSettings` / `CloneEditorController.InstanceSettings`).

`GetAllInstances()` 로 받은 항목마다 `m_Settings` 를 꺼내 `PlayerTag` 를 채우고 **박스를 되돌려
쓴 다음**(값형일 수 있다) `CreateScenario` 를 부르면, 시나리오의 `SetupEditorTagsNode` 가 그것을
`Library/VP/SystemData.json` 의 `Tags` 로 써 넣는다. 그 파일이 `CurrentPlayer.ReadOnlyTags()` 의
실제 출처다. 이름이 비슷한 두 필드를 구별하는 데 이 세션에서 가장 오래 걸렸다.

`SystemDataStore.GetMain()` + `TryLoadPlayerJson`/`SavePlayerJson` 으로 그 파일을 직접 쓸 수도
있다. `Tags` 는 자동 속성이라 `GetField("Tags")` 로는 안 잡히고 `<Tags>k__BackingField` 다.

### 함정 2 — 태그는 프로세스가 뜰 때 읽힌다

세션 중에 `SystemData.json` 을 고쳐도 **이미 돌고 있는 에디터에는 반영되지 않는다.**
`CurrentPlayer.ReadOnlyTags()` 는 프로세스 시작 시점의 값을 들고 있어서, 태그를 심은 직후 같은
세션에서 시나리오를 띄우면 `MultiplayerRoleBootstrap` 이 여전히 "태그가 없다" 로 빠진다.

그래서 순서가 중요하다: **태그가 파일에 있는 상태에서 에디터를 시작**해야 한다. 한 번 GUI 로
태그를 붙여 두거나, 코드로 심은 뒤 에디터를 재시작한다. 시나리오를 코드로 돌리는 것 자체는
그 뒤로 반복 가능하다.

**아직 못 한 것:** 이 경로로 서버·클라가 실제로 붙은 상태의 대역폭 측정. 함정 2 를 만난 시점에
에디터 재시작이 필요해졌고 그 뒤로 임포트가 길어졌다. 다음 세션은 태그가 이미 파일에 있으므로
`mppm_final` 절차(설정 태그 주입 -> CreateScenario -> Load -> Start)부터 바로 시작하면 된다.
