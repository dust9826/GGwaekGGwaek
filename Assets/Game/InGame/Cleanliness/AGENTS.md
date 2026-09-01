# InGame/Cleanliness — 스테이지 루프 오케스트레이션과 종료 판정

**2026-08-28부터 `Scenes/SinglePlay.unity`의 기준 게임은 SnowDelivery 의뢰 흐름이다.**
`GameManager`가 단계·점수·종료를, `RequestDirector`가 의뢰를, `RequestStageFlowPresenter`가
Intro/HUD/Outro 연결을 소유한다. `MultiPlaySceneBuilder`는 이 프로덕션 SinglePlay 씬을 열어
로컬 플레이어를 제거하고 네트워크 리그만 더한다. 별도의 테스트 씬을 원본으로 삼지 않는다.
스테이지 종류가 하나뿐인 동안은 별도의 `Flow/`·`Run/` 레이어를 만들지 않는다.

Boundary: 결과 표시는 `../UI/`가 소유한다. 이 기능은 `StageResult`를 만들어 이벤트로 흘려보낼 뿐,
결과 화면의 존재를 모른다 — UI가 아직 없어도 이 기능은 완전히 동작한다.

## 종료의 정체

종료는 화면을 띄우는 동작이 아니라 상태 전이다. 순서가 중요하다.

1. `Phase = Ended`로 즉시 전환한다. 재진입 가드다.
2. **판정 재료를 그 순간 스냅샷한다(`StageMetrics`).** `GiftDeliveryDirector`의 완료 수·실패 여부·
   점수와 `SnowCpuStage`의 남은 눈 총량을 값으로 굳힌다.
3. 스냅샷을 루브릭에 대조해 `StageResult`를 만든다. 순수 계산이며 씬·GPU와 무관해 EditMode에서
   전부 검증된다.
4. `PenguinInputReader`를 끈다. `OnDisable`이 이동·마우스·눈덩이 입력값을 전부 비우므로 마지막
   입력이 박제되어 계속 움직이지 않는다.
5. `GiftDeliveryDirector`를 꺼 주문 판정과 제한시간을 멈춘다. 주문 시간 초과의 `GameOver`도 같은
   종료 경로를 즉시 탄다.
6. `StageEnded` 이벤트를 발화한다. 구독자(결과 UI)가 없어도 정상 동작이다.

**종료에는** `Time.timeScale = 0`을 쓰지 않는다. 나중에 붙을 결과 화면의 DOTween 모션이 멈추고(`InGame/UI/AGENTS.md`가
`SetUpdate(true)`를 요구하는 이유가 이것과 같다), 물리 기반인 `SnowField`/`SnowPlowLedger` 원장이
어정쩡한 상태로 남으며, 헤드리스 테스트 사이로 전역 상태가 샌다.

⚠ **일시정지는 예외다 (2026-08-31).** 이 금지는 원래 *종료*를 두고 쓴 것이고, 그때는 판정이 끝난 뒤
화면이 계속 살아 있어야 하므로 옳다. **일시정지에서는 근거 셋 중 둘이 성립하지 않는다.**

- DOTween — `InGame/UI/AGENTS.md`가 이미 "일시정지 중에도 필요한 HUD 피드백은 `SetUpdate(true)`"를
  요구한다. 애초에 대비돼 있었다.
- 원장 — `timeScale = 0`은 `FixedUpdate`를 **아예 돌리지 않는다.** 종료 때와 달리 원장이 중간 상태에
  남지 않고, 재개하면 그대로 이어진다.
- **전역 누수 — 이것만 그대로 문다.** 그래서 `PauseMenuController`는 `OnDisable`에서도 되돌리고,
  씬을 바꾸기 전에 먼저 되돌린다. 안 되돌리면 메인메뉴가 얼어붙는다(실측으로 확인했다).

싱글만 멈춘다. 멀티에서 시간을 멈추면 그 피어만 멈추고 세션은 계속 돌아 재개하는 순간 자기 화면만
과거에 있다. 판정은 `StageSession`이 한다 — `InGame/UI/PauseMenu/AGENTS.md` 참고.

## 별 루브릭

`VacuumToolModeCatalog` 패턴을 그대로 따른다 — 새 도구가 컨트롤러 수정 없이 카탈로그 항목만
늘리듯, 별 개수도 `StageStarRubric` 에셋의 행을 늘리는 것만으로 확장된다. 별마다 클래스를 만드는
추상화는 쓰지 않는다(두 번째 호출부가 없는 상태에서 새 계층을 만들지 않는다는 루트 규칙).

- `EStarMetric` — 지금 읽을 수 있는 지표: `DeliveriesCompleted`, `SnowClearedPercent`,
  `DeliveriesCancelled`, `TotalPoints`. 새 지표 종류가 필요하면 enum 케이스 1개 + 평가 switch
  분기 1개를 추가한다.
- `StageStarRubric`(ScriptableObject) — `StageStarEntry { EStarMetric, ComparisonMode, threshold,
  label }`의 리스트. 별 3개는 `SinglePlayStarRubric.asset`의 행 3개일 뿐 코드가 아니다.
- 먼지(청결도 %)는 기준에 넣지 않았다 — `Neighborhood_ConceptMap`은 이미 dust에서 snow로
  교체됐고(`Map/AGENTS.md`), 먼지 마스크는 `RenderTexture`라 CPU 대표값을 새로 만들어야 별
  기준으로 쓸 수 있다. 필요해지면 별도 스펙에서 다룬다.
- `TotalPoints`는 여전히 계산되고(`DeliveryDirector.TotalPoints`) 루브릭 enum에도 남아 있지만,
  2026-08-18부로 어느 별 조건에도 쓰이지 않는다 — 아래 Decisions 참고.

## 씬 리그 재사용

`Delivery/Editor/DeliverySceneRigBuilder.cs`가 `WinterVillage_ConceptMap` 사본에 도로망·플레이어·
리스폰 볼륨·배송 기반을 얹는다. `Cleanliness/Editor/SnowDeliverySceneBuilder.cs`가 그 위에
`GameManager`·`RequestDirector`·눈 선물 기계·날씨·UI를 조립해 `SinglePlay.unity`를 만든다.
도둑 리그는 이 최종 씬의 눈 선물 기계 배출 지점을 중심으로 배치하되 보관소 프리팹을 참조하지 않고,
`RequestDirector.RequestExpired`를 실패 소스로 사용한다.

⚠ **`SinglePlay.unity`와 `MultiPlay.unity`는 Build Settings가 GUID로 참조한다.** 두 빌더 모두
원본 씬을 대상 경로로 저장해 씬 파일 내용만 교체하고 기존 `.meta`는 유지한다. `CopyAsset`으로
매번 새 GUID를 만들거나 씬 YAML을 직접 편집하지 않는다.

## 경계

- `../Delivery/`의 도로·트럭·점수 규칙은 바꾸지 않는다. 읽기만 한다.
- `../Snow/`의 `SnowCpuStage.TotalHeightMm`을 직접 읽는다. 렌더러나 GPU 텍스처는 판정에 쓰지 않는다.
- `../Vehicle/`의 입력 게이팅 수정(`VehicleInput.OnDisable`에서 값 비우기)은 이 기능이 요구했지만
  코드는 `Vehicle/`이 소유한다.
- Fusion이 오기 전에는 `NetworkBehaviour`를 추가하지 않는다. 서버 권위가 될 로직(단계 전환, 타이머,
  루브릭 평가)은 로컬 입력·카메라·GPU 없이 그대로 실행할 수 있어야 한다.

## Decisions

- **2026-08-31 — 검증한 눈덩이 관성·성장 HUD를 `SinglePlay`에 적용했다.**
  `SnowDeliverySceneBuilder`는 기본 `PF_Penguin` 대신 `PF_Penguin_MomentumHandling` Variant를
  생성하고, 플레이 카메라를 따라가는 성장 HUD와 런타임 성장 컨트롤러를 `SnowDeliveryRig` 아래에
  조립한다. HUD는 눈덩이를 밀거나 운반할 때만 표시되며 내려놓으면 숨는다.

- **2026-08-31 — 멀티의 종료 흐름은 싱글과 같다. 두 모드는 같은 게임이다.**
  이 항목은 이 문서가 2026-08-31 까지 "미정" 으로 두었던 것을 대체한다 — 멀티가 주문 실패 자동
  종료를 따를지 예전의 수동 종료 요청으로 남을지가 열려 있었다. **싱글과 같은 규칙을 따른다.**
  두 모드는 종료 흐름도 게임 흐름도 같고, 다른 것은 **인원 수와 그에 따른 밸런스**뿐이다.
  증강처럼 인원을 조건으로 갈리는 것이 나중에 더해질 수 있다.

  따라오는 것 셋:

  - **인원 수는 `StageSession.PlayerCount` 로 읽는다.** 싱글은 1이다. `StageBalanceConfig` 에는
    아직 인원 항이 없다 — 인원별 테이블이나 배율은 실측 뒤에 넣는다.
  - **빌더를 언제 고치는가.** ⚠ **2026-09-01 에 이 항목을 고쳤다 — 원문이 틀렸다.**
    원래는 "일반 컴포넌트는 `SinglePlay.unity` 에 넣고 `Build MultiPlay Scene` 을 다시 돌리면 끝,
    빌더 코드는 안 고친다" 였는데, 그것은 **아무도 `Build SinglePlay Scene` 을 안 돌릴 때만** 참이다.
    `SnowDeliverySceneBuilder.Build()` 는 그 씬을 **매번 처음부터 조립하므로 손으로 넣은 것은 다음
    실행에 사라진다.** 일시정지 메뉴와 증강이 실제로 그렇게 놓였다가 옮겨졌다.
    **씬에 남아야 하는 것은 `SnowDeliverySceneBuilder` 가 조립한다.**
    `[Networked]` 복제 상태가 필요할 때만 NetworkObject 프리팹과 서버 스폰 리그를 빌더에 더한다.
    이 프로젝트에는 **씬에 놓인 `NetworkObject` 가 0개다**(전부 런타임 스폰). 씬 편집은 언제나
    `SinglePlay.unity` 에서 한다 — `MultiPlay.unity` 를 직접 고치면 다음 빌드에서 사라진다.
  - **`MultiPlaySceneBuilder.DisableSinglePeerRigs()` 는 현재 no-op 이다**(실측). 네 항목 모두
    프로덕션 씬에서 아무것도 잡지 않는다 — `GiftDeliveryDirector` 는 `SinglePlay` 에서 이미
    꺼져 있고, 나머지 셋은 어느 씬에도 없다. **죽은 코드가 아니라 보험이므로 남긴다.**
    "피어마다 갈릴 것"은 앞으로 빌더가 끄지 않고 컴포넌트가 `StageSession` 으로 스스로 판정한다.

  설계 근거: `docs/specs/2026-08-31-single-multi-pipeline.md` ·
  게이트 규약: `Core/Multiplay/AGENTS.md`

- **2026-08-28 — SnowDelivery 요청 흐름을 `SinglePlay.unity`로 승격하고 MultiPlay의 유일한 원본으로 삼았다.**
  기존 `SnowDelivery_RequestFlow_Test.unity`를 Unity AssetDatabase로
  `Cleanliness/Scenes/SinglePlay.unity`에 이동했다. MainMenu의 LoadingScreen과 결과 Retry는 모두
  프로덕션 경로를 직접 로드한다. `MultiPlaySceneBuilder`도 맵이나 테스트 씬이 아니라 SinglePlay를
  복사한 뒤 네트워크 차이만 적용한다. 테스트는 프로덕션 SinglePlay를 직접 검증하며 테스트 씬은
  Build Settings와 프로젝트에서 제거했다. 이 결정은 바로 아래 2026-08-26의 임시 테스트 씬 직행을
  대체한다.

- **2026-08-26 — MainMenu의 싱글 출동 목적지는 `SnowDelivery_RequestFlow_Test`다.**
  `OutGame/UI/LoadingScreen/Scripts/LoadingScreenController.cs`가 로딩 연출 뒤 해당 씬 경로를 직접
  비동기 로드한다. 씬은 Build Settings에 활성 등록되어 있다. 아래 2026-08-18의 `SinglePlay` 직행
  결정은 이 요청으로 대체됐다. SnowDelivery 흐름이 프로덕션 씬으로 승격되기 전까지 메인 메뉴의
  DEPLOY 경로를 임의로 `SinglePlay.unity`로 되돌리지 않는다.

- **2026-08-26 — 결과 화면에서 별을 빼고 점수·최고 점수를 띄운다. HUD에도 현재 점수를 되살렸다.**
  이것은 아래 **2026-08-18 "HUD에서 포인트 표시를 뺐다"를 의도적으로 뒤집는 결정이다.** 그때는
  결과의 화폐가 별이었고 포인트는 UI에 안 나오는 내부 값이었다. 지금은 반대다 — 결과 화면이
  점수를 그리므로, 플레이 중에 점수가 어디에도 안 보이면 마지막에 튀어나온 숫자의 출처를 알 수 없다.
  `StageHUD`의 점수 칩은 그래서 장식이 아니라 결과 화면의 전제다.
  - **판정 코드는 그대로 둔다.** `StageStarRubric`·`StageResult`·`EStarMetric`·`MissionNetHub.NetStarCount`는
    살아 있고 `RequestStageFlowPresenter`는 계속 루브릭을 평가해 별을 복제한다. 없앤 것은 **UI 표시**뿐이다
    — 2026-08-18이 포인트에 했던 것과 같은 방식이라, 되돌리기가 싸다.
  - 최고 점수는 `Cleanliness/Scripts/StageHighScore.cs`(`PlayerPrefs`, 키 `PPack.HighScore.<stageId>`)가
    가진다. **기기마다 자기 기록**이라 멀티에서도 피어끼리 공유하지 않는다 — 공유 기록판은 서버가
    필요하고 지금은 없다. 동점은 갱신이 아니다(첫 판 0점에 NEW RECORD 가 뜨지 않게).
  - `StageOutroController.SetResult`의 시그니처가 바뀌었다:
    `(score, highScore, isNewRecord, clearPercent, timeText)`. 호출부는 `StageOutroPresenter`와
    `RequestStageFlowPresenter`(싱글/호스트 경로 + 클라이언트 경로) 셋이다.
  - 점수 상승 연출(펀치·팝)은 **넣지 않았다.** 요청 범위 밖이고, `stage-clock-gain`의 떠오르는 팝이
    그대로 얹힐 자리라 지금 만들면 추측 설계가 된다.
  - `StageHUD`의 점수 칩은 **오른쪽 위**에 둔다(왼쪽 위는 의뢰 보드, 가운데 위는 시계).
    **화면 아래쪽은 비워 뒀다 — 펭귄 체력 바가 앉을 자리다.**

- **2026-08-25 — SinglePlay 펭귄 시작점은 `(-4, 0.31, -12)` 남쪽 공터다.** 예전
  `(-8, 0.31, -9)`는 주변 차량의 대각선 차선 위라 입력 없이도 약 8초 뒤 충돌했다.
  `SinglePlaySceneBuilder`는 새 리그를 안전 좌표에 만들고, 기존 씬도 `SinglePlayDirector.Start()`가
  Penguin과 연결된 `PlayerSpawn` 위치 및 Rigidbody 속도를 맞춰 같은 시작점을 보장한다. 기존 씬에서
  옮길 때는 한 물리 스텝 동안 Rigidbody 충돌을 꺼 `ContinuousDynamic`이 이동 거리를 속도로 추정하는
  스폰 스윕도 막는다.

- **2026-08-25 — 5분 고정 스테이지 타이머를 제거했다.** `SinglePlayDirector`는 경과 시간만 기록하고
  자체적으로 종료하지 않는다. 종료 권위는 `GiftDeliveryDirector`의 주문 시간 초과 하나이며,
  성공하면 다음 주문으로 계속 이어진다. 2026-08-18의 5분 자동 종료 결정은 이 결정으로 대체됐다.

- **2026-08-21 — SinglePlay의 플레이 주체를 차량에서 `PF_Penguin`으로 교체하고, 배달과 눈의 구세대를
  제거했다.** Penguin 프리팹 인스턴스를 그대로 써 애니메이션·카메라·입력·눈덩이 스택을 함께 받는다.
  배달은 `GiftDeliveryDirector`/`DeliveryHouse`/`GiftDropZone`/집 표지만 남기고 비활성 NPC
  `DeliveryDirector`·`DeliveryTrafficController`는 씬에 만들지 않는다. 눈은
  `Snow_BallPush_Test`와 같은 `SnowCpuStage`+`SnowCpuStageView`+`SnowSystem`+`SnowDisplaceView`
  스택 하나만 남기며, WinterVillage 전체를 덮도록 범위만 120×110m로 유지한다. 구 `SnowStage`·
  `SnowSurfaceRenderer`·`SnowPanelBuilder`는 파생 씬에서 제거한다.

- **2026-08-18 — 시간 제한 없음 / 수동 종료 요청 / 성공-실패 이분법을 폐기했다(싱글플레이 한정,
  2026-08-25의 무한 주문 루프로 대체됨).**
  이전 결정은 협동 판단 자체를 재미 요소로 뒀지만, 싱글플레이는 관찰할 팀원이 없어 그 재미가
  성립하지 않는다. 5분 고정 시간 + 자동 종료 + 별 등급(0~N개)으로 교체했다. 근거와 대안은
  `docs/specs/2026-08-18-singleplay-game-loop.md`.
- **2026-08-18 — HUD에서 포인트 표시를 뺐다.** `StageHUD`의 세 번째 세그먼트(별 아이콘 +
  `points-value`)를 지웠다 — 남은 두 세그먼트(시간, 성공한 트럭 대수)만 남는다.
  `StageHUDController.SetDeliveryStats`는 `completedCount`만 받는다. `DeliveryDirector.TotalPoints`
  등 포인트 계산 로직 자체는 그대로 둔다 — UI 표시만 없앤 것이다. 이에 맞춰 3성 조건
  (`SinglePlayStarRubric.asset`)도 `취소 0회 + TotalPoints≥300`에서 `성공한 트럭 10대 이상 +
  제설률 80% 이상 + 취소 0회`로 바꿨다 — 포인트에 기대지 않고도 "완벽 배송"을 판정할 수 있게.
- **2026-08-18 — `SinglePlay.unity`를 Build Settings에 등록하고 MainMenu Local Route와 연결했다.**
  `OutGame/UI/LoadingScreen`의 `LoadingScreenController`가 `buildIndex+1`로 다음 씬을 고르던
  것을 `SinglePlay.unity` 경로 직행으로 바꿨다 — Winter Village 선택 후 `action-stage-start`를
  누르면 실제로 이 씬에 도착한다(이전엔 index 산술 때문에 엉뚱한 씬으로 갔다). 멀티플레이는
  Fusion이 씬을 올려서 이 경로를 안 거치므로 충돌 없음을 확인했다.
- **2026-08-18 — `../UI/StageOutro/`의 결과 화면을 연결했다.** `StageOutroController`는 이미
  완성돼 있었지만 아무 데도 안 물려 있었다. 새 접착부 `InGame/UI/StageOutro/Scripts/StageOutroPresenter.cs`가
  `SinglePlayDirector.StageEnded`를 구독해 `SetResult(...)`를 부른다 — **경계 방향은 그대로다**,
  UI가 Cleanliness를 구독할 뿐 `SinglePlayDirector`는 이 스크립트의 존재를 모른다. 결과 화면이
  생기면서 종료 3초 뒤 무조건 메인메뉴로 돌아가던 `_autoReturnToMenuSeconds` 타이머가 결과 화면을
  볼 새도 없이 씬을 넘겨버리는 문제가 생겨, `SinglePlaySceneBuilder`가 이 값을 0으로 세팅해
  타이머를 끈다(0 = 자동 복귀 안 함, 코드 한 줄 추가). 대신 결과 화면의 RETRY/CONTINUE 버튼이
  `StageOutroPresenter`를 통해 씬 전환을 맡는다.
  ~~⚠ **알려진 한계**: `StageOutroController.SetResult`는 별점을 1~3으로 clamp한다 — 0성 판정도
  화면엔 1성으로 뜬다.~~ **2026-08-26 해소** — 결과 화면이 별을 안 그리므로 clamp 자체가 없어졌다.
  위 2026-08-26 항목 참고.
