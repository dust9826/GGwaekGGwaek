# 싱글플레이 게임 루프 — 구현 계획

> 스펙: [2026-08-18-singleplay-game-loop.md](../specs/2026-08-18-singleplay-game-loop.md)
> 브랜치: `/main/singleplay-game-loop`

**목표**: MainMenu → Loading → 3초 카운트다운 → 5분 플레이 → 자동 종료 → 별 등급 로그가 처음부터
끝까지 한 번에 돈다. 결과 UI는 범위 밖.

**접근**: 새 코드는 `InGame/Cleanliness/`에만 넣는다. 맵·차량·눈·배송·카운트다운은 기존 기능을
그대로 조립한다.

---

## 전역 제약

- 네임스페이스는 `PPack` 하나.
- 비공개 필드 `_camelCase`, 타입·메서드 `PascalCase`, enum은 `E` 접두사.
- 직렬화된 Unity Object 필드는 `== null`/`!= null`, 그 외는 `is null`.
- `Delivery/Scripts/`, `Delivery/Prefabs/`, `Map/WinterVillage/`, `Snow/Scripts/`, `Vehicle/Scripts/`의
  기존 판정 로직은 건드리지 않는다. `Vehicle/`에 필요한 한 곳(입력 게이팅)만 예외.
- `WinterVillage_ConceptMap.unity`는 열지도 저장하지도 않는다 — `AssetDatabase.CopyAsset`만.
- `SinglePlay.unity`는 Build Settings가 참조하므로 있으면 재사용, 없을 때만 새로 만든다.
- Feel은 `PPack.InGame`에서 `MMF_Player`를 직접 참조할 수 있다(루트 `AGENTS.md` 2026-08-14 정정).
  단, 이번 작업에서 Feel 신규 배선은 없다.

---

## 태스크 1 — Cleanliness 런타임 스크립트

파일 (전부 `Cleanliness/Scripts/`, 새 asmdef 없음 — `PPack.InGame`에 자동 편입):

- `EStagePhase.cs` — `{ Intro, Playing, Ended }`
- `EStarMetric.cs` — `{ DeliveriesCompleted, SnowClearedPercent, DeliveriesCancelled, TotalPoints }`
- `StageMetrics.cs` — readonly struct. `DeliveriesCompleted`, `DeliveriesCancelled`, `TotalPoints`,
  `SnowClearedPercent01`을 담는다. `static StageMetrics Capture(DeliveryDirector, SnowField field,
  long initialTotalDepthCm)` 팩토리.
- `StageStarEntry.cs` — `[Serializable] class { EStarMetric metric; float threshold; string label; }`
- `StageStarRubric.cs` — `[CreateAssetMenu] ScriptableObject`. `List<StageStarEntry>` +
  `StageResult Evaluate(StageMetrics)`.
- `StageResult.cs` — readonly struct. `int StarCount`, `IReadOnlyList<(string label, bool passed)>`.

검증: 이 태스크만으로 컴파일이 통과해야 한다(씬 배선 없이도 독립 컴파일).

## 태스크 2 — SinglePlayDirector + Vehicle 입력 게이팅

- `Cleanliness/Scripts/SinglePlayDirector.cs`(MonoBehaviour):
  - `[SerializeField]` 참조: `StageIntroController`, `DeliveryDirector`, `SnowStage`,
    `VehicleInput`, `SnowPlowInput`(옵션, null 허용), `StageStarRubric`.
  - `[SerializeField] float _playSeconds = 300f;`
  - `[SerializeField] float _autoReturnToMenuSeconds = 3f;`
  - `[SerializeField] int _mainMenuBuildIndex = 0;`
  - `public EStagePhase Phase { get; private set; }`
  - `public StageResult Result { get; private set; }`
  - `public event Action<StageResult> StageEnded;`
  - `[SerializeField] UnityEvent<StageResult> _stageEndedFeedback;` — 씬 배선용, 결과 UI가 없어도
    비워 두면 무해하다.
  - `OnEnable`: 입력 게이팅 켜기(비활성화), `Phase = Intro`, `_introController.Play()` 구독.
  - `_introController`의 `_introCompleted` UnityEvent에 `OnIntroFinished` 연결(인스펙터 배선,
    코드에서 강제 구독하지 않음 — `StageIntroController`가 이미 UnityEvent로 노출).
  - `OnIntroFinished()`: 입력 게이팅 해제(값이 이미 비어 있으므로 `enabled = true`만),
    `Phase = Playing`, `_remainingSeconds = _playSeconds`, `_initialTotalDepthCm` 캡처.
  - `Update()`: `Phase == Playing`일 때만 `_remainingSeconds -= Time.deltaTime`; `<= 0`이면 `EndStage()`.
  - `EndStage()`: 스펙의 7단계 순서 그대로 구현. 재진입 가드로 시작.
  - `ReturnToMenuAfterDelay()`: 코루틴, `_autoReturnToMenuSeconds` 뒤 `SceneManager.LoadScene(_mainMenuBuildIndex)`.

- `Vehicle/Scripts/VehicleInput.cs` 수정 — `OnDisable`에 추가:
  ```csharp
  Move = Vector2.zero;
  AccelerateHeld = DriftHeld = false;
  PullFrontHeld = PullLeftHeld = PullRightHeld = false;
  PullFrontReleased = PullLeftReleased = PullRightReleased = false;
  PullLeftPressed = PullRightPressed = PullCancelPressed = false;
  ```
  `_drivingMap.Disable()` 앞에 둔다(순서 무관하지만 읽는 쪽 기준을 명확히 하려면 값 비우기 먼저).
  `Vehicle/AGENTS.md`에 이 결정과 근거(함정: `OnDisable`이 입력맵만 껐고 프로퍼티는 마지막 값에
  박제됐다) 한 단락 추가.

검증: `SnowPlowInput`은 현재 토글 방식(`E` 키로 블레이드 on/off)이라 완전히 끄면 배송 판정에
관여하는 제설이 멈춘다 — **게이팅 대상에서 제외**하고 `VehicleInput`만 게이팅한다. Intro 중에는
차량이 안 움직이면 충분하다.

## 태스크 3 — EditMode 테스트

`Cleanliness/Tests/EditMode/`, 새 asmdef `PPack.Cleanliness.EditModeTests`
(`Delivery/Tests/EditMode/PPack.Delivery.EditModeTests.asmdef`를 참고 템플릿으로, references에
`PPack.InGame` + `UnityEngine.TestRunner` + `UnityEditor.TestRunner`, `defineConstraints:
["UNITY_INCLUDE_TESTS"]`, `includePlatforms: ["Editor"]`).

- `StageStarRubricTests.cs` — 경계값(정확히 임계값, 임계값 미만/초과), 별 0개/일부/전체, 빈 루브릀,
  지표 확장(새 `EStarMetric` 케이스를 추가해도 기존 항목 평가가 그대로인지).
- `StageMetricsTests.cs` — `SnowClearedPercent01` 계산(초기 깊이 0 나눗셈 가드 포함).

이 테스트는 씬·MonoBehaviour 없이 순수 데이터로 검증한다 — `SinglePlayDirector`는 PlayMode로 미룬다.

## 태스크 4 — Delivery 리그 공용부 추출

**리스크가 가장 큰 태스크.** 690줄 `DeliveryTestSceneBuilder.cs`를 건드리므로, 기능 동작이
바뀌지 않는지 매 단계 컴파일 + 기존 `PPack/Delivery/Build Request Flow Test Scene` 메뉴 재실행으로
확인한다.

1. `Delivery/Editor/DeliverySceneRigBuilder.cs` 신규. `DeliveryTestSceneBuilder`에서 다음을
   `internal static` 메서드로 그대로 옮긴다(로직 변경 없이 이동만): `CopyMapSceneAndOpen`,
   `FindMapSnowStage`, `FlushCurbCollidersToRoadTop`, `BuildCurbRamps`, `BuildNodes`, `BuildSegments`,
   `BuildFactories`, `FindHouseTransforms`, `BuildPlayerVehicle`, `BuildRiverRespawnVolumes`,
   `BuildFallbackVehicle`, `BuildFallbackCamera`, `BuildTruckPrefab`(트럭 프리팹 자체는 두 씬이
   공유), 관련 상수(`RoadSurfaceY`, `NodeSpecs`, `RoadSpecs`, `HouseSpecs`, `VehicleRoadWidth`,
   `PromenadeWidth`, 트럭 치수, 커브 램프 상수).
   씬 경로·`PlayerStart`처럼 씬마다 다른 값은 매개변수로 받는다.
2. `DeliveryTestSceneBuilder.Build()`가 새 클래스를 호출하도록 바꾼다. **한 줄도 다른 동작을
   하면 안 된다** — 리팩터는 이동이지 개선이 아니다.
3. Unity MCP로 컴파일 확인(`read_console`) 후 `PPack/Delivery/Build Request Flow Test Scene`
   메뉴를 재실행해 `Delivery_RequestFlow_Test.unity`가 이전과 동일하게 생성되는지 확인.
4. 기존 PlayMode 테스트(`Delivery/Tests/PlayMode/`)가 여전히 통과하는지 헤드리스로 확인.

## 태스크 5 — SinglePlay 씬 빌더 + 씬 + Build Settings

- `Cleanliness/Editor/PPack.InGame.Cleanliness.Editor.asmdef` 신규(`Delivery/Editor`의 asmdef를
  템플릿으로, references에 `PPack.InGame` + `Unity.RenderPipelines.Universal.Runtime`).
- `Cleanliness/Editor/SinglePlaySceneBuilder.cs`:
  - 메뉴 `PPack/Cleanliness/Build SinglePlay Scene`.
  - 씬이 없으면 `DeliverySceneRigBuilder`의 맵 복사부를 호출해 `SinglePlay.unity`를 만든다.
    있으면 씬을 열고 `SinglePlayRig` 루트만 찾아 삭제 후 재생성.
  - `DeliverySceneRigBuilder`의 나머지(노드·구간·공장·트럭·플레이어 차량·리스폰)를 호출.
  - `SinglePlayRig` 밑에 `SinglePlayDirector` GameObject 생성, 참조 배선(`DeliveryDirector`,
    `SnowStage`, `VehicleInput`, `StageStarRubric` 에셋).
  - `UI/StageIntro/StageIntro.uxml` + `StageIntroPanelSettings.asset`으로 `UIDocument`를 만들어
    `StageIntroController`를 붙이고 `SinglePlayDirector`에 배선.
  - `SinglePlayDirector._introController._introCompleted` UnityEvent를
    `SinglePlayDirector.OnIntroFinished`에 연결.
- `Cleanliness/Data/SinglePlayStarRubric.asset` — 스펙의 별 3개 행으로 생성.
- Unity MCP `manage_scene`으로 Build Settings에 `SinglePlay.unity`를 인덱스 2로 삽입
  (`Neighborhood_ConceptMap`은 3으로 밀림). **이 파일은 모든 브랜치가 공유하므로 이 체크인에서
  한 번만 확정.**

검증: 메뉴 실행 → 컴파일 → Play Mode에서 Intro 재생, 5분 대신 `_playSeconds`를 인스펙터에서
5초로 임시로 낮춰 종료까지 수동 확인 → 값 원복.

## 태스크 6 — PlayMode 테스트

`Cleanliness/Tests/PlayMode/`, 새 asmdef(`Delivery/Tests/PlayMode`를 템플릿으로).

- `SinglePlayDirectorPlayModeTests.cs`:
  - 씬을 로드하지 않고 코드로 최소 리그(더미 `DeliveryDirector`, `SnowStage`, `VehicleInput`,
    `StageIntroController`)를 만들어 검증(`Delivery/Tests/PlayMode/DeliveryDirectorPlayModeTests.cs`
    패턴).
  - Intro 동안 입력이 게이팅되는지, `_introCompleted` 발화 후 `Phase == Playing`으로 바뀌는지.
  - `_playSeconds`를 짧게(0.2f) 설정해 자동 `Ended` 전이 확인.
  - 종료 스냅샷 이후 `DeliveryDirector.RequestCompleted`를 강제로 더 발화시켜도 `Result`가
    바뀌지 않는지(스냅샷 불변성의 핵심 단정).
  - `VehicleInput` 값이 종료 후 0으로 비는지.
  - `[TearDown]`에서 생성한 GameObject 전부 파괴.

- 헤드리스 1회: 에디터 바이너리 직접 호출 `-batchmode -nographics -runTests -testPlatform
  PlayMode`(`unity run`이 아니라 — `-quit` 주입 문제, `Delivery/AGENTS.md` 선례).

## 태스크 7 — 문서 마무리

이미 반영: `Cleanliness/AGENTS.md`, `Delivery/AGENTS.md` 경계 절, `Game_Concept.md` §3/§7/§9,
`Glossary.md`, 이 스펙/계획.

남은 것:
- `docs/INDEX.md` "현재 상태"에 항목 추가, 커밋한 changeset 번호로 갱신.
- `Session_Summary_20260818_singleplay-game-loop.md` 신규(wrap-session 스킬 또는 수동).
- `Vehicle/AGENTS.md`에 입력 게이팅 결정 한 단락(태스크 2에서 이미 계획됨).

---

## 체크인 순서

1. **설계** — 이 스펙 + 계획 + `Cleanliness/AGENTS.md`/`Delivery/AGENTS.md`/`Game_Concept.md`/
   `Glossary.md` 개정. (진행 중)
2. **루프 권위** — 태스크 1·2·3.
3. **씬** — 태스크 4·5.
4. **테스트와 헤드리스 검증** — 태스크 6.
5. **문서 마무리** — 태스크 7.

각 체크인 전에 `cm status`로 전체 경로를 모으고, `.meta`를 빠뜨리지 않으며, 수정 파일은
`cm checkout` 후 체크인한다(루트 `AGENTS.md` 버전 관리 절).
