# Core/Multiplay — 세션 계층 (Fusion 2, UI Host Mode)

`/main/multiplay/fusion-session` 에서 만들었다. 로비 → 매치메이킹 → 로딩 → 게임플레이를 **한 줄기의
단계 기계**로 들고 있고, 그 단계를 UI(`OutGame`)와 아바타(`InGame`)가 각자 읽는다.

여기가 `Core` 인 이유는 **경계 때문**이다. `InGame` 과 `OutGame` 은 서로를 참조할 수 없다(어셈블리가
그것을 컴파일 에러로 만든다). 로비가 세션을 시작하고 게임플레이가 그 세션 위에서 도는 이상, 둘이 같이
보는 타입은 `Core` 에 있어야 한다.

## 있는 것

| | |
|---|---|
| `ESessionPhase.cs` | `Offline · Lobby · Matchmaking · Loading · Playing`. **UI 가 읽는 유일한 상태** |
| `NetworkInputData.cs` | 클라이언트가 매 틱 보내는 입력. `Move` + `Buttons`(`EInputButton`) |
| `SessionLauncher.cs` | 피어 하나의 전부 — 시작, 단계 전이, 씬 로드, 아바타 스폰, 입력 수집 |
| `MultiplayerRoleBootstrap.cs` | MPPM 인스턴스별 역할 자동 결정(메인=서버, 추가=클라) |
| `Editor/MultiplayScenarioTools.cs` | 시나리오 창 열기·시작·정지·상태를 메뉴로. CLI 에서도 부를 수 있다 |

로비 상태 오브젝트는 `Resources/PF_SessionLobby.prefab` — **이 폴더 안에 있다.** 로비는 세션 계층의
것이므로 자리도 여기다.

### ⚠ 인게임 아바타와 게임플레이 씬은 여기 없다 (2026-08-24)

`SessionLauncher.GameplayScenePath` 와 `SessionLauncher.SceneAvatarResource` 는 **비어 있는 채로
시작하고, 게임플레이 씬의 부트스트랩이 넣는다.** 둘 다 비어 있으면 각각 로딩과 스폰에서 멈추고
에러를 남긴다.

전에는 씬 경로가 `const` 였고 아바타 기본값이 `"PF_MultiplayPlow"` 였다. 그것은 `Core` 가 `InGame` 의
파일 경로를 아는 것이고, **어셈블리로 막아 둔 경계를 문자열로 우회하는 것**이다. 게다가 씬이
아무것도 넣지 않으면 조용히 제설차가 스폰되어 원인이 씬에서 멀리 보였다.

**구 인게임 멀티(`InGame/Multiplay/`)는 2026-08-24 에 통째로 지웠다** — 제설차 계열 스크립트, 아바타
프리팹 셋, `MP_Gameplay` 씬 전부. 저항이 v7 시각 필드에 물려 있는데 그 필드가 씬에 없어서 눈이
차 속도에 아무 영향을 주지 않는 상태로 죽어 있었고, 리그를 다시 붙여도 데디 서버에는 GPU 가 없어
영구 예측 불일치가 된다. 플레이어는 눈덩이를 굴리는 펭귄으로 다시 만든다.
**이 계층은 그 교체와 무관하게 그대로다.**
설계 `docs/specs/2026-08-24-multiplay-penguin-rebuild.md`.

## ⚠ 새 네트워크 프리팹을 만들면 프리팹 표를 다시 굽는다 (2026-08-24 실측)

`NetworkObject` 를 가진 프리팹을 새로 만들면 **`Tools/Fusion/Rebuild Prefab Table` 을 한 번
돌려야 한다.** 안 돌리면 이렇게 죽는다:

```
InvalidOperationException: Prefab PF_PenguinNet (Fusion.NetworkObject) has been baked with a
guid 70a6696d-..., but such guid failed to be translated into a ...
```

**컴파일도 EditMode 도 전부 통과하고 런타임 스폰에서만 죽는다.** 표는 GUID 목록이 아니라
임포트 시점 스캔으로 만들어지므로 파일을 봐도 무엇이 빠졌는지 보이지 않는다.

## ⚠ 아바타는 스스로 `SetPlayerObject` 를 불러야 한다 (2026-08-20 실측 · 2026-08-24 재발)

새 아바타 컴포넌트를 만들 때 이것을 빠뜨리면 **증상이 네트워킹처럼 안 보인다.**
`SnowCpuStage` 의 관심 반경 판정이 `TryGetPlayerObject` 로 각 플레이어를 찾고 못 찾으면
건너뛴다 — 아무도 못 찾으면 어떤 청크도 stale 로 표시되지 않아 **"서버는 눈을 깎는데 화면에선
안 파인다"** 가 된다.

2026-08-24 에 `PenguinNetAvatar` 를 새로 만들면서 정확히 이것을 다시 겪었다. 이 문서에 이미
적혀 있었는데 새 컴포넌트를 쓰면서 다시 읽지 않은 것이 원인이다. 지금은 그 경고가
`PenguinNetAvatar.Spawned` 의 주석에도 있다.

## ⚠ 테스트 통과만으로 "컴파일된다" 고 말하지 않는다 (2026-08-24 실측)

Unity 가 비포커스일 때는 AssetDatabase 가 편집을 바로 보지 않는다. 그 상태에서 `run_tests` 는
**낡은 어셈블리로 돌아 통과한다.** 실제로 컴파일되지 않는 코드를 그 통과만 믿고 체크인했다
(`cs:650`).

판정 순서를 지킨다: `recompile` 을 걸고 → `recompile_status` 가 `compiling` 을 거쳐 `completed`
가 되는 것을 보고 → **`failed:false` 를 확인한 뒤에** 테스트 결과를 믿는다.

## ⚠ MPPM 시나리오가 고착되면 에디터를 재시작한다 (2026-08-24 실측)

`state=Running` 인데 메인 에디터가 Play 에 못 들어가고 클론도 하나만 뜨는 상태가 된다.
시나리오 정지·클론 닫기로도 풀리지 않는다. 에디터가 오래 떠 있을수록(실측 21시간) 잘 생긴다.
**에디터를 껐다 켜면 첫 시도에 정상으로 돌아온다.**

## ⚠ 클론이 붙지 못하는 것은 대개 Fusion 설정 아티팩트가 낡은 것이다 (2026-08-29 실측)

증상은 **"호스트는 되는데 참가만 안 된다"** 이고, 화면에는 아무 말도 안 뜬다. 클론 로그에만 남는다:

```
InvalidOperationException: Failed to load NetworkProjectConfigAsset
  at PPack.SessionLauncher.StartPeer (...)  ← NetworkProjectConfig.Global
  at PPack.OutGameScreenController.JoinRoomAsync (...)
```

**MPPM 클론은 자기 `ArtifactDB` 가 없다.** `Library/VP/mppmXXXX/Library/` 에 `Artifacts` 도
`SourceAssetDB` 도 없고, 심링크된 것은 `Assets` 와 `ProjectSettings` 뿐이다. 클론은 본체의
아티팩트를 **읽기 전용**으로 읽는다.

그런데 Fusion 의 `FusionCustomDependency.Update()` 는 **클론에서도 돈다**. 그것이 부르는
`RegisterCustomDependencyWithMppmWorkaround`(`Fusion.Unity.Editor.cs:3218`)는

```csharp
FusionMppm.MainEditor?.Send(...);                    // 클론에선 null → 건너뜀
AssetDatabase.RegisterCustomDependency(dep, hash);   // 클론에서도 무조건 실행
```

두 번째 줄이 클론의 읽기 전용 AssetDatabase 에 의존성을 등록해 `NetworkProjectConfig.fusion`
아티팩트를 낡은 것으로 만든다. 그리고 클론은 그것을 다시 굽지 못한다 — 로그에 그대로 나온다:

```
Asset Database is set to Read Only, but it has found out-of-date assets. This should not happen!
Imports: total=0 (actual=0, local cache=0, cache server=0)
```

**왜 간헐적인가.** Fusion 이 등록하는 의존성은 넷이다 — `Fusion.PrefabsDependency`,
`Fusion.ScriptOrderDependency`, `Fusion/NetworkObjectPostprocessor`,
`FusionILWeaverTriggerImporter/ConfigHash`. 해시는 **재컴파일하거나 네트워크 프리팹 표를 건드릴
때마다** 바뀐다. 즉 클론이 떠 있는 동안 본체가 컴파일하면 **그 클론은 그때부터 못 붙는다.**
네트워크 프리팹을 자주 만지는 시기일수록 자주 난다.

**증상의 정체는 `Resources.Load` 다 (2026-08-30, 클론 안에서 계측).** 클론 부트스트랩에
진단을 심어 두 클론을 나란히 찍었더니 이렇게 갈렸다:

```
mppmc98a64d1 : Resources.Load('NetworkProjectConfig') = NULL  → Global THREW
mppmcfbb519f : Resources.Load('NetworkProjectConfig') = 있음   → Global OK PeerMode=Single
```

같이 확인된 것: 클론에서 `Application.dataPath` 는 심링크를 풀지 않고
`Library/VP/mppmXXXX/Assets` 를 돌려주며, 그래서 `FusionMppm.Status = VirtualInstance` 로
**올바르게** 판정된다(`MainEditor` 는 null). 즉 MPPM 판정이 틀린 것이 아니라, 그 클론의
**리소스 색인에 자산이 없다.**

**대처 — 본체 에디터를 재시작한다.** 이것이 실제로 듣는 유일한 것이다.

| 시도 | 결과 |
|---|---|
| 클론만 다시 띄우기 | 낫지 않는다(같은 세션에서 20초 재시도 80번 전부 실패) |
| 클론 폴더(`Library/VP/mppmXXXX`, 441MB) 삭제 후 재생성 | 낫지 않는다 |
| **본체 에디터 재시작 후 시나리오 재실행** | 나은 적도 있고(클론 둘 다 정상) 아닌 적도 있다 |
| **`client` 태그를 다른 인스턴스로 옮기기** | **그 자리가 살아 있으면 바로 붙는다** |

**어느 클론이 실패하는지는 실행마다 바뀐다.** 한동안 `Player 2` 자리만 실패해서 슬롯을 타는
것으로 적었는데(2026-08-29), 계측 실행에서 반대로 뒤집혔다. **슬롯 고정이 아니다.** 한 세션
안에서는 실패한 클론이 계속 실패하지만, 본체를 다시 띄우면 재추첨된다.

**재시작이 항상 듣는 것은 아니다(2026-08-30 추가 실측).** 방금 재시작한 에디터에서도 `client` 태그를
단 클론이 그대로 실패했고, **그 태그를 다른 인스턴스로 옮기자 즉시 붙었다.** 그러니 순서는 이렇다 —
① `client` 태그를 다른 인스턴스로 옮겨 본다(가장 싸다) ② 그래도 안 되면 본체 에디터를 재시작한다.
**어느 쪽이든 클론 폴더를 지우는 것은 낭비다** — 재생성해도 같은 실패를 한다.

**이것은 에디터 도구 문제이지 게임 버그가 아니다.** 빌드는 이 경로를 타지 않는다 — 자산이
`Resources` 에 구워져 들어가고 임포터가 돌지 않는다. 진짜 접속 검증이 필요하면 빌드로 한다.

**우리 쪽에서 고친 것은 원인이 아니라 진단 가능성이다.** `StartPeer` 는 `NetworkProjectConfig.Global`
을 피어를 만들기 **전에** 읽고, 실패하면 로그를 남기고 `false` 를 돌려준다. 전에는 이 읽기가
`StartGameArgs` 초기화 안에 있어서 예외가 `Task<bool>` 계약을 깨고 빠져나갔고, 호출부가
`async void` 라 **상태 라벨조차 안 바뀌었다** — 그래서 화면에 단서가 하나도 없었다.
`OutGameScreenController` 의 `HostRoomAsync`/`JoinRoomAsync` 도 몸통을 감쌌다.

> `async void` UI 콜백은 예외를 삼킨다. 그 안에서 부르는 것이 `Task<bool>` 을 돌려주더라도,
> **던질 수 있으면 감싸야 한다.** 아니면 실패가 화면에서 사라진다.

## ⚠ `클론 닫기` 뒤에 MPPM 이 모달을 띄운다 — 그 순간 에디터 전체가 멎는다 (2026-08-25 실측)

`클론 닫기` 는 `pkill -f scenarioClone` 이라 MPPM 쪽에서는 클론이 **죽은 것**으로 보인다.
그러면 시나리오 매니저가 클론마다 알림 창을 하나씩 띄운다:

```
Player 2 unexpectedly stopped
It appears that Player 2 has unexpectedly stopped. Do you want to restart it?   [No] [Yes]
```

**이것은 모달이라 에디터 메인 스레드를 잡는다.** 그때부터 CLI 의 모든 호출이
`Main thread operation timed out after 60000ms` 로 죽는다 — `open_scene` 도 `quit` 도 안 되고,
프로세스는 CPU 0.5% 로 멀쩡히 살아 있어서 **행에 걸린 것처럼 보이지 않는다.** 로그에도
모달이 떴다는 말은 없고 타임아웃만 쌓인다.

창이 화면 밖이거나 가려져 있으면 눈으로도 안 보인다. 확인과 해소를 스크립트로 하려면:

```bash
osascript -e 'tell application "System Events" to tell process "Unity" \
  to get value of every static text of (first window whose value of attribute "AXModal" is true)'
osascript -e 'tell application "System Events" to tell process "Unity" \
  to click button "No" of (first window whose value of attribute "AXModal" is true)'
```

클론 수만큼 뜨므로 `count of (every window whose AXModal is true)` 가 0 이 될 때까지 반복한다.
**Yes 를 누르면 클론이 되살아난다** — 정리하려던 참이면 반드시 No 다.

## 수동으로 HOST / JOIN 을 확인하는 법 — 태그 없이도 항상 된다

**태그는 자동화 편의일 뿐이다.** 로비 UI 의 HOST / JOIN 버튼은 태그와 무관하게 동작하고, 태그가
하는 일은 그 버튼을 대신 눌러 주는 것뿐이다(`MultiplayerRoleBootstrap`). 태그를 전부 비우면
부트스트랩이 빠지고 사람이 로비를 그대로 밟는다 — 그것이 `2PlayerLobbyFlow` 다.

문제는 **JOIN 을 누르는 쪽이 MPPM 클론일 때**다. 위 절의 결함이 수동 경로에도 똑같이 걸린다
(스택은 `JoinLobby → JoinRoomAsync → JoinRoom → StartPeer` 로 다를 뿐 원인은 같다).

### 코드로는 못 고친다 — 확인했고 기각한다 (2026-08-30)

Fusion 의 에러 문구가 `FusionGlobalScriptableObjectAttribute` 로 로딩을 대신하라고 안내하므로
그 길을 재 봤다. **두 번 다 막힌다.**

1. **커스텀 로더로 애셋을 찾는 길** — 실패하는 클론에서는 `Resources.Load` 도
   `AssetDatabase.LoadAssetAtPath` 도 **둘 다 null 이다**(실측). 로더를 붙여도 불러올 대상이 없다.
2. **`.fusion` 을 디스크에서 직접 읽는 길** — 파일은 평범한 JSON 이고 임포터도
   `File.ReadAllText` + `EditorJsonUtility.FromJsonOverwrite` 로 읽으니 될 것 같다. 그런데 임포터가
   만드는 것은 JSON 만이 아니다:

   ```csharp
   root.Config  = config;                       // 디스크에서 읽을 수 있다
   root.Prefabs = DiscoverPrefabs(ctx);         // 애셋 데이터베이스를 훑어야 만든다
   root.BehaviourMeta = CreateBehaviourMeta(ctx);
   ```

   프리팹 표가 빈 채로 만들어지면 피어는 뜨지만 `Runner.Spawn` 이 죽는다. **시끄러운 실패를
   조용한 실패로 바꾸는 것이라 더 나쁘다.**

### 그래서 수동 확인은 이 셋 중 하나로 한다

| 방법 | 비용 | 신뢰도 |
|---|---|---|
| 본체 에디터 HOST + **두 번째 워크스페이스** 에디터 JOIN | 워크스페이스 하나(이미 `PPackPPack_v2_request_hud` 가 있다) | **높다 — MPPM 결함을 안 탄다** |
| 본체 에디터 HOST + **개발 빌드** JOIN | 빌드 시간 | 높다. 출시 형태와 가장 가깝다 |
| MPPM 클론으로 JOIN | 가장 싸다 | 클론이 간헐적으로 못 붙는다(위 절) |

일상 확인은 MPPM 으로 하고, **클론이 못 붙는 날 두 번째 워크스페이스로 넘어간다.** 워크스페이스
둘을 두는 것은 이 프로젝트가 이미 브랜치 전환 때문에 쓰는 방식이다(루트 `AGENTS.md`).

## 호스트 모드로 검증하기 — `host` 태그 (2026-08-30)

부트스트랩 태그는 셋이다.

| 태그 | 진입점 | GameMode | 그 화면에 펭귄이 |
|---|---|---|---|
| `host` | `SessionLauncher.HostRoom` | `Host` | **있다** — 방장이 곧 플레이어 |
| `server` | `SessionLauncher.HostServerOnly` | `Server` | 없다 — 데디케이티드 |
| `client` | `SessionLauncher.JoinRoom` | `Client` | 있다 |

`host` 와 `server` 를 같이 달면 에러를 내고 멈춘다. 하나는 데디이고 하나는 방장이 플레이어라
동시에 성립하지 않는다.

**호스트는 기대 인원에서 자기를 뺀다.** `ExpectedClientCount()` 는 "이 시나리오가 띄우는
클라이언트 수" 인데 호스트 인스턴스 자신도 `PlayerCount` 에 들어간다. 빼지 않으면 한 명을
영원히 더 기다린다.

실측(2026-08-30): 본체 `host` + 클론 `client` 로 띄우니
`[MPPM] 이 인스턴스는 호스트다` → `adding player [Player:1]`(호스트) →
`adding player [Player:2]`(클론) → 매치 시작 → `Phase=Playing`. 게임플레이 씬에서
`PenguinNetAvatar` 2개, 그중 하나가 `InputAuthority=True`(호스트가 직접 조작), 활성 카메라 1개
(원격 아바타의 로컬 전용 부품은 꺼졌다), `SnowCpuStage` 권위 True.

### ⚠ 태그의 진짜 출처는 시나리오 애셋이다 — `SystemData.json` 을 손으로 고치면 덮인다

`Library/VP/SystemData.json` 의 `Tags` 를 고치고 에디터를 재시작해도, **시나리오를 실행하는 순간
그 값이 애셋의 `m_Settings.PlayerTag` 로 덮인다**(`SetupEditorTagsNode`). 실제로 `host` 로 써 두고
재시작했는데 시나리오가 뜨면서 `server` 로 돌아갔고, 본체는 데디로 떴다.

- 사람이 쓸 때는 **Scenarios 창에서 인스턴스의 태그를 바꾼다.** 그것이 애셋에 저장된다.
- 코드로 띄울 때는 `GetAllInstances()` 로 받은 항목마다 `m_Settings` 를 꺼내 `PlayerTag` 를 채우고
  **박스를 되돌려 쓴 다음** `CreateScenario` 를 부른다(값형일 수 있다). 그러면 그 값이
  `SystemData.json` 에 써지고 부트스트랩이 그것을 읽는다.

### 호스트로 옮겨도 헤드리스 게이트는 남긴다

에디터의 호스트 피어에는 **GPU 가 있다.** 데디 서버가 못 하는 코드를 그대로 실행하므로, 호스트로만
검증하면 그 의존을 영영 못 잡는다(루트 `AGENTS.md` 의 근거). `MultiplayHeadlessTests` 와
`MultiPlaySceneHeadlessTests` 가 그 구멍을 대신 지키므로 **호스트로 옮긴다고 그 둘을 지우지 않는다.**

## 클론에도 CLI 로 붙을 수 있다 (2026-08-31 실측)

**클론도 파이프라인 서버를 띄운다** — 본체가 7800, 클론이 7801 이다. `unity status` 는 본체만
보여 주지만, 프로젝트 경로를 클론 폴더로 주면 그대로 붙는다.

```
unity command eval_file <스크립트> --project-path <프로젝트>/Library/VP/mppmXXXXXXXX
```

포트는 클론 프로세스에서 확인한다: `lsof -nP -iTCP -sTCP:LISTEN -a -p <pid>`.

**이제까지 클라이언트를 로그로만 봤는데 그럴 필요가 없었다.** 클라이언트 쪽 상태를 직접 읽을 수
있으면 "복제가 되는가" 를 로그 문구에 의존하지 않고 값으로 판정한다.

**⚠ 두 피어는 같은 순간에 재야 한다.** 서버와 클론을 몇 초 간격으로 찍으면 그 사이에 상태가
바뀌어 동기화가 깨진 것처럼 보인다 — 실제로 눈보라 검증에서 서버 `Active`, 클론 `Idle` 을 보고
"복제가 안 된다" 로 오독했다. 같은 순간에 재니 둘 다 `Active` 였다.

## 클라이언트 하나로 매치를 띄우는 법 (2026-08-30 실측)

클론 둘 중 하나가 못 붙는 문제(바로 위 절) 때문에 2인 시나리오는 로비에서 멈춘다 —
`MultiplayerRoleBootstrap.HostAndWaitForPlayers` 가 **기대 인원이 다 찰 때까지** 매치를 시작하지
않기 때문이다. 한 명으로 돌리는 절차는 이렇다.

1. `Library/VP/SystemData.json` 에서 **잘 붙는 슬롯에만** `Tags: ["client"]` 를 주고 나머지
   클라 슬롯은 비운다. `Main Editor` 는 `["server"]`.
2. 에디터를 **`PPACK_MPPM_CLIENTS=1` 로 시작**한다. 이 값은 `ExpectedClientCount()` 가 읽고,
   기본값 2 그대로면 한 명으로는 영원히 로비에 머문다.

   ```
   PPACK_MPPM_CLIENTS=1 ~/.unity/bin/unity open <프로젝트>
   ```
3. 태그는 **프로세스 시작 시점에 읽힌다**(위의 함정 2). 그래서 1번을 먼저 하고 2번을 한다.
4. 시나리오를 띄우면 서버가 방을 열고, 클라가 붙고, 매치가 시작돼 게임플레이 씬이 올라온다.

실측 진행: `Offline → Matchmaking → Playing` 까지 약 20초, 그 뒤 `MultiPlay` 씬이 로드되고
`SnowCpuStage`(권위), `GiftNetSpawner` 1개, `SnowGiftMachine` 1개, `PenguinNetAvatar` 1개,
`MissionNetHub`(Playing) 가 모두 섰다.

**`ScenarioRunner.StopScenario()` 는 클론 프로세스를 끝내지 않는다.** Play 를 빠져나가고 시나리오
상태도 정리되는데 클론 프로세스는 1분을 기다려도 살아 있었다. 검증이 끝나면 `pkill -f scenarioClone`
로 직접 내린다 — 안 그러면 다음 실행에서 클론이 겹친다.

## 세션 판정은 `StageSession` 하나로 한다 (2026-08-31)

싱글과 멀티는 **같은 게임이다** — 종료 흐름도 게임 흐름도 같고, 다른 것은 인원 수와 그에 따른
밸런스뿐이다. 그것을 코드로 표현한 것이 세 상태 게이트다.

```
Runner 없음              → 싱글.        그대로 돈다
Runner 있음 + IsServer   → 권위(호스트).  그대로 돈다   ← 위와 같은 코드
Runner 있음 + !IsServer  → 클라이언트.    판정하지 않고 복제값만 읽는다
```

**첫째와 둘째가 같은 코드라는 것이 이 규약의 전부다.** 컴포넌트는 `StageSession.IsAuthority`
하나만 보면 두 모드에서 같은 판정을 돌린다.

- **`StageSession.For(gameObject)` 를 쓴다. `NetworkRunner.Instances.Count` 를 세지 않는다.**
  그 판정은 남의 세션이 하나라도 살아 있으면 뒤집힌다 — MPPM `host` 태그가 남은 채 SinglePlay 에
  들어가면 러너가 `DontDestroyOnLoad` 로 따라오고, `NetworkRunner.GetRunnerForScene` 는 그 씬에서도
  **그 러너를 그대로 돌려준다**(2026-08-31 실측). `StageSession` 이 `SessionLauncher.GameplayScenePath`
  와 소유자 씬 경로를 대조해 그것을 걸러 낸다.
- **프레임마다 부르지 않는다.** 러너 조회가 들어 있다. 한 번 받아 캐시한다.
- **인원 수도 여기서 읽는다.** `PlayerCount` 는 싱글이면 1, 멀티면 `ExpectedPlayerCount` 다.
  현재 접속 인원(`Runner.ActivePlayers`)이 아닌 이유는 그것이 판 도중에 변해 밸런스를 흔들기
  때문이다 — `MissionNetHub` 가 같은 이유로 이미 그렇게 한다.
- **판정은 `Resolve` 가 갖고 `For` 는 갖지 않는다.** `NetworkRunner.IsRunning` 과 `IsServer` 는
  세터가 없어 EditMode 에서 만들어 낼 수 없다. 분기를 순수 함수로 떼어 놓아야 전부 테스트할 수
  있고, `For` 는 Fusion 에서 신호 셋을 뽑아 넘기는 껍데기로 남는다.
  `Resolve` 와 `SceneOwnsSession` 은 `internal` 이고 `Core/AssemblyInfo.cs` 의 `InternalsVisibleTo`
  로 `PPack.Multiplay.EditModeTests` 에만 열려 있다 — 호출부는 `For` 만 쓴다.
- **클라이언트가 무엇을 읽을지는 `PresentedX` 패턴을 따른다** — `ThiefNetworkHub.PresentedAction`
  이 선례다. 로컬 시스템은 네트워크를 모른 채 두고, 옆에 붙은 `NetworkBehaviour` 가 관찰 가능한
  원인만 복제한다.

설계 근거: `docs/specs/2026-08-31-single-multi-pipeline.md`

## 닉네임 — 복제되고, 표시는 `이름#id` (2026-09-01)

**2026-08-24 의 "닉네임은 복제되지 않는다" 를 뒤집었다.** 그때는 이름을 나를 곳이 없어
`OutGameScreenController` 가 "없는 정보를 만들어 내지 않는다" 로 번호만 보여 줬다. 이제
`SessionLobby` 가 나른다.

- **전송은 신뢰 채널이다** (`SessionLobby.NicknameKey`). RPC 는 이 프로젝트에서 못 쓰고,
  입력 구조체에 싣지 않은 이유는 다르다 — 시작 요청은 한 틱짜리 사건이지만 **이름은 한 번 정해지면
  안 바뀌는 값**이라 매 틱 보낼 이유가 없다. 눈 격자 델타가 같은 채널을 쓴다.
- ⚠ **호스트는 보내지 않고 직접 적는다.** 자기 자신에게 보낸 신뢰 데이터는 돌아오지 않아
  `OnReliableDataReceived` 가 안 불린다(실측). 그대로 두면 **방장 이름만 비어** `#1` 로 뜬다.
- **인덱스는 `PlayerId` 다.** 슬롯 순서로 담으면 누가 나갈 때 나머지가 밀려 이름이 어긋난다.
- 여기서만 `[Networked]` 컬렉션을 쓴다. 위의 비트마스크가 컬렉션을 피한 이유는 "정수 하나로
  끝나서" 였고, 이름은 정수로 담기지 않는다.

### ⚠ `SessionLobby` 는 게임플레이 씬에서 사라진다 (실측)

`StartMatch` 의 씬 로드 뒤에 `SessionLobby.Instance` 가 **`null`** 이다. 그래서 **인게임에서 로비를
읽으면 안 된다** — 이름이 필요한 곳(나감 알림)이 바로 거기라 그냥 두면 조용히 빈 문자열이 온다.

`SessionLauncher` 가 로비가 살아 있는 동안 이름을 베껴 둔다(`RememberPlayerName` / `NameOf`).
런처는 `DontDestroyOnLoad` 라 씬을 넘어 살고, `ExpectedPlayerCount` 가 같은 이유로 거기 있다 —
**"이 판의 사람들" 은 세션 계층의 사실이고 씬보다 오래 산다.**

거울은 **전 피어**가 돌린다. 복제된 값을 각자 자기 사본으로 만드는 것이라 복제가 늘지 않는다.

### 표시는 `SessionLobby.Format` 하나로

`이름#id` — `PENGUIN#2`. 이름이 없으면 `#2`.

접미사가 둘을 동시에 푼다. **이름이 겹쳐도**(둘 다 `PENGUIN`) 구분되고, **이름을 못 받았거나 이미
사라진 뒤에도** 누군지 특정된다. 그래서 이름을 모르는 경우가 "무의미한 문장" 이 아니라 "덜 친절한
문장" 으로 끝난다 — 캐시는 정확성의 전제가 아니라 친절함의 장치다.

## 규약

- **현재 UI 출시 경로는 `GameMode.Host` 다 (2026-08-26, 임시 전환).** "방 만들기"(`HostRoom`)는
  방을 만든 사람이 서버 권위와 플레이어 역할을 함께 맡는다. 데디 서버 운영을 다시 준비할 때까지
  UI 는 이 경로만 쓴다. `HostServerOnly`의 `GameMode.Server` 경로는 삭제하지 않는다 — 헤드리스 검증,
  MPPM 서버 역할과 향후 데디 운영이 직접 사용한다. 호스트에서도 서버 권위 로직은 계속
  `Runner.IsServer`로 게이트하고, GPU·카메라·로컬 입력을 권위 상태의 전제로 삼지 않는다.
- **한 프로세스에 피어 하나다.** `PeerMode = Single`. 검증은 MPPM 인스턴스를 여러 개 띄워서 한다.
- **피어 하나에 런처 하나.** UI 가 보는 것은 `SessionLauncher.Local`(클라이언트 또는 호스트)이고,
  플레이어 피어가 없으면 `LocalServer` 의 단계를 준다 — 데디 서버만 띄운 인스턴스에서 로비 UI 가
  영원히 `Offline` 을 읽지 않게.
- **입력은 원인만 보낸다.** 위치도 효과도 보내지 않는다. 토글 판정도 아바타가
  `[Networked] PreviousButtons` 와 비교해서 한다 — 로컬 변수로 두면 재시뮬레이션에서 토글이
  두 번 먹거나 씹힌다.
- **`EInputButton` 의 비트 값을 다시 매기지 않는다.** 비트가 곧 와이어 포맷이라 피어 사이에서
  의미가 어긋난다. 쓰지 않게 된 비트는 구멍으로 남기고 새 버튼은 뒤에 붙인다.
- **게임플레이 씬은 경로로 찾는다** — `SceneUtility.GetBuildIndexByScenePath`. 인덱스를 직렬화해 두면
  팀원이 빌드 세팅을 건드릴 때 조용히 어긋난다.
- 아바타 프리팹은 `Resources` 에 있다. 런처가 런타임에 생겨서 인스펙터로 물릴 수 없기 때문이다.

## MPPM 으로 검증하는 법 (2026-08-18)

`com.unity.multiplayer.playmode 3.0.0` 은 Unity 6000.6 **빌트인**이고, MPPM 2.0 부터 구현이 엔진으로
들어갔다(패키지 폴더에는 문서만 있다). ⚠ **Fusion 2.1.1 의 MPPM 통합은 켜지지 않는다** —
`FUSION_ENABLE_MPPM` 의 versionDefine 이 `"0.6"` 이고 우리는 3.0.0 이며, 그 코드는 옛 MPPM 의
`UnityEditor.MPE` + 네임드 파이프용이다. 벤더 파일을 고쳐 켤 이유가 없다 — 우리가 쓰는 것은 인스턴스
분리뿐이다.

쓰는 API는 둘이다.

| | |
|---|---|
| 런타임 | `Unity.Multiplayer.PlayMode.CurrentPlayer` — `IsMainEditor`, `ReadOnlyTags()` |
| 에디터 | `Unity.PlayMode.Editor.PlayModeScenarioManager` — `ActiveScenario`, `State`, `Start()`, `Stop()` |

**시나리오 구성(인스턴스 추가·태그)은 공개 API 가 없다.** `Window > Play Mode > Scenarios` 에서 한 번
해야 하고, 그 설정은 `ProjectSettings` 에 저장되므로 한 번이면 된다. 시작·정지는 위 API 라
`PPack/Multiplay/시나리오 시작` 메뉴(그리고 CLI 의 `menu` 명령)로 반복할 수 있다.

역할은 태그로 정한다 — `server` · `client` · `room:CODE`. 태그가 없으면 메인 에디터는 아무것도 하지
않고(사람이 로비를 쓴다) 추가 에디터는 `MultiplayerRoleBootstrap.DevRoomCode`(`MPPMDEV`)로 자동 접속한다.

## 함정 — 여기서 실제로 시간을 쓴 것들

### 눈은 피어마다 자기 것이다 — 그리고 v7 필드는 인스턴스 안전하지 않았다 (2026-08-18)

`MP_Gameplay` 에 v7 눈 리그가 들어 있고, 플레이어는 <b>제설차</b>(`PF_MultiplayPlow`)로 스폰된다.
멀티피어에서는 씬 사본이 피어마다 있으니 <b>필드도 피어마다 하나</b>다. 스폰된 차량은 입력 권한을 가진
쪽에서만, 그리고 <b>자기와 같은 씬의</b> 리그에 자기를 문다 — 전역에서 아무 리그나 찾으면 남의 피어
화면의 눈이 깎인다.

⚠ **v7 필드는 프로세스에 하나뿐이라고 가정하고 있었다.** `EnsureResources` 가
`Resources.Load<ComputeShader>` 로 받은 **에셋을 그대로** 썼고, `ComputeShader` 의 파라미터는 에셋 전역
상태다. 그래서 필드 셋(서버+클라2)이 서로의 파라미터를 덮어써 **한 필드만 시뮬레이션되고 나머지는
`HeightAt` 이 전부 0** 이 됐다(어느 피어가 죽는지는 실행마다 바뀌고 콘솔 에러는 0). `Instantiate` 로
필드마다 인스턴스를 갖게 고쳤다. 새로 GPU 자원을 쓰는 시스템을 멀티피어에 올릴 때 같은 함정을 의심해라.

실측(각자 블레이드 내리고 전진):

| 피어 | 자기 차선 | 남의 차선 | 옆 | 날에 실린 눈 |
|---|---|---|---|---|
| Client1 (차 x=3) | **0.000** | 0.401 | 0.400 | 985 kg |
| Client2 (차 x=0) | 0.393 | **0.005** | 0.400 | 1360 kg |
| Server | 0.400 | 0.400 | 0.400 | 0 |

즉 **각 피어는 자기 차가 깎은 자리만 본다.** 서버는 리그에 차량이 물리지 않아 눈을 아예 모른다.
협동 제설(남이 치운 길이 내 화면에도 보이는 것)은 **블레이드 자세를 복제해 각 클라이언트가 모든 차의
스윕을 재현**해야 하고, 그것이 눈 권위 결정(D4)의 실제 작업이다.

⚠ **주행 중에 `eval` 을 부르지 마라.** Roslyn 컴파일 스톨이 Photon 연결을 끊어 세션이 조용히 내려간다
(런너 0, 에러 0). "차가 안 움직인다"로 보였던 증상이 실제로는 이것이었다. 입력 주입과 판독을 각각
한 번의 호출로 묶고, 그 사이에는 쉘에서만 기다려라.

### `RunnerEnableVisibility` 는 **로비 씬**까지 물어서 로비 카메라를 끈다 (2026-08-18)

증상: `CREW LOBBY` 로 들어가면 UI 는 뜨는데 그 뒤가 **`Display 1 / No cameras rendering`** 이다.

그 컴포넌트의 `OnSceneLoadDone` 은 `runner.SimulationUnityScene` 의 **루트를 전부** 가시성 노드로
등록한다(SDK 소스에 그대로 있다). 로비 단계에서는 그 씬이 아직 로비 씬이므로 로비 카메라와 UI 가 피어의
노드로 잡히고, 서버 피어를 감추는 `runner.SetVisible(false)` 가 **로비 카메라를 같이 끈다.**

그래서 **가시성 등록과 `SetVisible` 은 `StartMatch` 에서** 한다. 피어별 가시성이 실제로 필요해지는
순간은 게임플레이 씬이 피어마다 사본으로 올라갈 때뿐이다 — 로비에는 감출 것이 없다.

함께 고친 것: 세션을 여는 순간의 씬 로드(빈 세션 루트)를 "로딩"으로 세면 로비에
`LOADING GAMEPLAY...` 가 뜬다. `OnSceneLoadStart` 는 단계가 **이미 `Lobby` 일 때만** `Loading` 으로
넘어가고, `OnSceneLoadDone` 은 **로비 씬 밖에 카메라가 생겼을 때만** `Playing` 으로 넘어간다.
그 판정을 씬 이름·인덱스로 하지 않는 이유는 멀티피어에서 게임플레이 사본이 피어 씬으로 옮겨져 이름이
달라지기 때문이고, 실제로 알아야 하는 것은 "렌더링을 넘겨줄 카메라가 생겼는가" 하나다.

⚠ 단계를 확인할 때는 **Play 를 새로 시작해라.** 한 Play 세션에서 방 만들기를 두 번 돌리면 앞선 피어가
남아 `Phase` 가 `Playing` 으로 읽히고, 그것을 버그로 오진하기 쉽다(실제로 한 번 그랬다).

### `PeerMode = Multiple` 은 `StartGameArgs.Scene` 을 요구한다 (2026-08-18)

"로비에 있는 동안은 씬을 바꾸지 않는다"는 뜻으로 `Scene = null` 을 넘겼더니, 피어마다
`NetworkSceneManagerDefault.Initialize` 가 에러를 던졌다:

```
[Fusion] PeerModes.Multiple requires a scene to be set in StartGameArgs.Scene.
```

**`StartGame` 자체는 성공한다.** 세션도 붙고 나중에 `LoadScene` 도 된다 — 그래서 기능 검증만 하면
지나치고, 콘솔에는 피어 수만큼 에러가 남는다. 실제로 그렇게 한 번 놓쳤다.

고친 방법은 **빈 씬을 넘기는 것**이다(`MP_SessionRoot.unity`, 빌드 세팅에 등록). 로비 씬을 넘기면 안
되는 이유가 위 절과 같다 — `StartGameArgs.Scene` 은 **피어마다 사본**이 올라가므로, 로비 씬을 주면
로비 UI 와 카메라가 피어 수만큼 복제된다. 게임플레이 씬을 주는 것도 답이 아니다: 그러면 방을 만드는
순간 게임이 시작돼 로비 단계가 사라진다.

씬은 경로로 넘긴다(`SceneUtility.GetBuildIndexByScenePath` → `SceneRef.FromIndex`). 경로가 빌드
세팅에 없으면 `BuildSceneInfo` 가 그 사실을 에러로 남기고 `null` 을 준다 — 그냥 `null` 을 넘기면
Fusion 이 "requires a scene to be set" 이라는 **딴 얘기로** 실패해 원인을 찾기 어렵다.

### 멀티피어에서 시작 씬은 언로드되지 않는다 (2026-08-18 실측)

`PeerMode = Multiple` 에서 Fusion 은 피어마다 게임플레이 씬 사본을 자기 `Scene`(`SessionPeer_...`)으로
올린다. **시작 씬(`MainMenu`)은 그대로 남는다.** 그래서 로비 카메라가 게임플레이 카메라보다 나중에
그려져 화면을 통째로 덮는다. 실측 증상은 "게임 뷰가 단색으로 가득 차고 아바타만 점처럼 남고 바닥은
아예 없다"였다 — 덮은 카메라가 y=0 에 있어서 바닥 평면이 정확히 edge-on 이었기 때문이다.

**바닥 렌더러를 끄고 다시 찍었는데 화면 색이 그대로였다**는 것이 결론을 갈랐다. 그러면 그 색은 바닥이
아니라 카메라 배경이다. `OnSceneLoadDone` 이 피어 씬 밖의 활성 카메라를 끄고 `Leave()` 가 되돌린다.
카메라 depth 를 조절하는 것으로는 부족하다 — 로비 UI 가 그대로 위에 뜬다.

### 팀원의 `LoadingScreen` 씬을 네트워크 로딩에 쓰지 않는다

`LoadingScreen.unity` 는 `SceneManager.LoadScene` 으로 도는 싱글플레이 연출이다. 네트워크 로딩은 씬
권위가 `MP_Gameplay` 를 Single 로 올리므로 로컬 씬 로드와 싸운다. 그래서 `Loading` 단계는 **로비 화면
위의 상태 표시**로 처리한다. 그 씬은 싱글플레이용으로 그대로 둔다.

### `INetworkRunnerCallbacks` 시그니처는 SDK 를 읽어서 맞춘다

Fusion 2.1.1 에서 `OnReliableDataReceived` 는 `ArraySegment<byte>` 가 아니라 `ReadOnlySpan<byte>` 를
받는다. 추측으로 고치지 말고 SDK 의 `release_history.txt` 와 참조 구현(`RunnerEnableVisibility.cs`)을
읽어라.

### `PPack.Core` 는 Fusion 을 명시적으로 참조해야 한다

`PPack.Core.asmdef` 의 `references` 가 비어 있어 `Fusion.Unity`·`Unity.InputSystem` 을 못 찾고
`CS0234` 가 났다. 둘을 추가했다. 그리고 `NetworkProjectConfig` 의 `AssembliesToWeave` 에 `PPack.Core` 를
넣었다 — 빠뜨리면 `[Networked]` 가 조용히 위빙되지 않고 런타임에 죽는다(루트 `AGENTS.md` 의 경고).

## 검증 (2026-08-18, cs:288 · cs:289)

한 에디터에서 서버 1 + 클라이언트 2:

| 조건 | 서버 | 클라이언트 | 차이 |
|---|---|---|---|
| 스폰 | (3.000, 0.000) · (0.000, 3.000) | 같음 | 0 |
| W 누른 상태 (6 m/s) | z=13.031 / 16.031 | z=12.707~13.274 / 15.708~16.257 | **≤ 0.33 m** (≈55ms 보간) |
| 키 뗀 뒤 2초 | z=15.844 / 18.656 | 소수점까지 같음 | 0 |

콘솔 에러 0, 위버 에러 0. 캡처는 `InGame/Multiplay/Preview/MP_TwoPeer_{Playing,Moving}.png`.

## 헤드리스 게이트 — `Tests/PlayMode/MultiplayHeadlessTests.cs` (2026-08-18, cs:350)

`-batchmode -nographics` 실행을 사람이 기억해야 하는 절차로 두지 않는다. 테스트 둘이 지킨다 —
**서버가 그래픽 없이 방을 열고 로비를 스폰하는가**, **게임플레이 씬을 올리는가**. `-nographics` 와
그래픽 있는 배치모드 양쪽에서 통과해야 한다(평소 실행은 배선 파손을, 헤드리스는 그 위에 GPU 의존을 잡는다).

씬 테스트는 비활성 컴포넌트까지 세어 **눈 리그 총계가 0 이 아님을 먼저 단정한다.** 그러지 않으면 씬에서
리그가 빠지는 순간 "켜진 리그 0" 이 저절로 참이 되어, 아무것도 지키지 않으면서 초록으로 남는다.

테스트 어셈블리는 Fusion 을 **`precompiledReferences` 에 `Fusion.Runtime.dll`** 로 넣는다.
`references` 에 어셈블리 이름으로 넣으면 `NetworkBehaviour` 에서 `CS0012` 가 난다 — Fusion 은 사전컴파일
DLL 이고 `overrideReferences` 가 켜져 있기 때문이다.

서버에 눈이 생기면 이 게이트에 **차량이 눈을 깎는 것**까지 넣어라. 지금은 씬을 올리는 것까지만 지킨다.

## 아직 없는 것

- **카메라가 아바타를 따라가지 않는다.** 고정 카메라다.
- **차량은 네트워크로 옮기지 않았다.** `VehicleController` 가 Rigidbody 물리로 도는데, 멀티피어에서
  물리를 돌리려면 런너별 물리 씬(`RunnerSimulatePhysics3D`)이 필요하다.
- **눈은 서버에 없다.** v7 은 권위가 GPU 컴퓨트라 `-batchmode -nographics` 서버에서 리그가 스스로
  꺼진다(`InGame/Snow/AGENTS.md`). 서버 권위 눈은 CPU 블록 동기화 경로다.
- **`collab-proxy` 를 이 워크스페이스에서 제거했다.** Fusion 의 IL 위버가 그 어셈블리를 위빙하다
  죽기 때문이다(루트 `AGENTS.md` 의 롤백 기록). ⚠ **머지 전에 팀 합의가 필요하다** — 제거하면 에디터
  안 Plastic 통합이 사라진다.
- **`PhotonAppSettings.asset` 의 AppId 는 v1 프로젝트(KimOhOh)에서 가져온 팀 공용 값**이다. 이 파일을
  리포지토리에 두는 것이 맞는지는 팀이 정할 일이다.

## 아바타는 씬이 고른다 - 그리고 그 우선순위가 필요했다 (2026-08-20)

슬라이스가 둘이다. 차량 제설과 펭귄 눈덩이가 **같은 세션·같은 입력 계약**을 쓰고 아바타만 다르다.
그래서 `SessionLauncher.AvatarResourcePath` 는 상수가 아니라 세 단계 우선순위다:

```
AvatarResourceOverride  (명시 요구 - 테스트·툴)
  ?? SceneAvatarResource  (씬이 넣는다)
```

**2026-08-24 에 세 번째 단계(`?? "PF_MultiplayPlow"`)를 없앴다.** 기본값이 있으면 씬이 아무것도
넣지 않았을 때 조용히 제설차가 스폰되고 원인이 씬에서 멀리 보인다. 이제 둘 다 비면 스폰하지 않고
에러를 남긴다. 그 값을 넣던 `MultiplayAvatarChoice` 도 구 멀티와 함께 지웠으므로, 새 게임플레이 씬은
자기 부트스트랩에서 `SceneAvatarResource` 와 `GameplayScenePath` 를 직접 넣어야 한다.

**씬에 직렬화 필드로 둘 수 없다** - 런처는 `StartPeer` 가 코드로 만드는 오브젝트라 인스펙터가 없다.
그래서 선택을 씬에 있는 부트스트랩이 대신 들고 `Awake` 에서 넣는다.

**우선순위가 왜 필요했나 (실측).** 처음에는 단순 대입이었고, `MP_Gameplay` 가 펭귄을 넣자
`두_클라이언트가_밀면_서버와_같은_눈을_본다` 가 *"서버가 차량에 스텝을 주지 않았다 - 입력이
서버까지 오지 않았다"* 로 죽었다. 차량이 아예 없었던 것이다. 테스트가 먼저 정해도 서버가 씬을
로드한 뒤 `Awake` 가 덮어쓰기 때문에 순서로는 이길 수 없다. 그 검증의 전제는 "이 세션은 차량이다"
이므로, 전제를 명시로 요구할 수 있어야 한다.

> ⚠ **아래의 실측 기록들은 제설차와 `MP_Gameplay` 위에서 잰 것이다.** 둘 다 2026-08-24 에
> 사라졌으므로 수치를 그대로 재현할 수 없다. 세션 계층·복제 일반에 대한 결론은 여전히 유효하지만,
> 차량·씬 이름이 나오는 부분은 역사로 읽어야 한다.

## 플레이어 오브젝트 등록을 빠뜨리면 눈이 전파되지 않는다 (2026-08-20 실측)

`Spawned()` 에서 서버가 `Runner.SetPlayerObject(Object.InputAuthority, Object)` 를 불러야 한다.
런처가 `Spawn` 반환값으로 하지 않는 이유는 스폰이 큐를 거치면 그 반환값이 `null` 이기 때문이고,
아바타 자신이 하면 큐를 거쳐도 한 번만 등록된다.

**빠뜨렸을 때의 증상이 네트워킹처럼 안 보인다.** `SnowCpuStage` 의 관심 반경 판정이
`TryGetPlayerObject` 로 각 플레이어의 위치를 찾고 못 찾은 플레이어는 건너뛴다. 아무도 못 찾으면
**어떤 청크도 stale 로 표시되지 않고** 클라이언트는 깎인 자리를 영원히 못 본다.

새로 만든 `MultiplayPenguin` 이 이것을 빠뜨렸고, 그 결과가
`눈덩이를_굴리면_커지고_자국이_클라이언트에_보인다` 의 **최대차 0.580 m** 였다 - 깎인 깊이와 정확히
같은 값, 즉 "하나도 안 왔다". 진단에 두 번의 오진을 거쳤고 둘 다 값싸게 폐기했다:

|가설|반증|
|---|---|
|전파 대역폭이 부족하다|`MaxChunksPerPlayerTick` 을 2 -> 8 로 올려도 **0.580 그대로**|
|클라이언트의 relax 가 트렌치를 되메운다|클라 relax 를 끄고 같은 표본, **0.580 그대로**|

차이가 정확히 깎인 깊이와 같으면 "덜 왔다" 가 아니라 "안 왔다" 이고, 그때 볼 곳은 대역폭이 아니라
**보내는 조건**이다. 값이 노브와 같은 값일 때 그것을 신호로 읽는 것은 이 폴더에 이미 있던 교훈이다.
