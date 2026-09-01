# 싱글플레이 게임 루프

## 목표

MainMenu의 싱글플레이 버튼을 누르면 전용 씬이 열려 3초 카운트다운 후 게임이 시작되고, 5분 뒤
자동으로 종료돼 별 0~3개 등급을 매긴다. 종료 버튼은 없다. 결과 화면 UI는 이번 범위에 없고
나중에 별도로 붙는다 — 이 스펙의 루프는 결과 화면 없이도 완결된다.

## 왜 새로 만들 것이 적은가

기존 기능을 그대로 조립한다.

- **씬 진입**: `OutGameScreenController.StartSinglePlayer()` → `LoadingScreen` → 빌드 인덱스 +1.
  코드 수정 없음.
- **3초 카운트다운**: `UI/StageIntro/StageIntroController`가 이미 `스테이지 카드 → 3·2·1 → CLEAN!`을
  구현하고 `_introCompleted` UnityEvent를 낸다. 소비자만 붙인다.
- **맵·차량·눈·배송**: `WinterVillage_ConceptMap`(팀원 소유, 직접 편집 금지) 위에
  `Delivery/Editor/DeliveryTestSceneBuilder.cs`가 이미 도로망·트럭·눈 연동 차량을 얹는 절차를
  검증해 뒀다. 이 스펙에서는 그 절차의 공용부를 뽑아 재사용한다.

## 새로 만드는 것

`InGame/Cleanliness/`가 처음으로 코드를 갖는다 — 루트 `AGENTS.md`가 이 폴더를 스테이지 오케스트레이션
자리로 예약해 뒀다.

### 단계와 타이머

`EStagePhase { Intro, Playing, Ended }`. `SinglePlayDirector`(MonoBehaviour)가 소유한다.

- `Intro`: `StageIntroController.Play()`를 부르고 `_introCompleted`를 기다린다. 입력은 이 구간
  내내 게이팅돼 있다.
- `Playing`: 입력 게이팅 해제, `_playSeconds`(기본 300, 테스트에서 직렬화 필드로 축소 가능)
  카운트다운 시작.
- `Ended`: "종료의 정체" 순서(아래)를 실행하고 이후 아무 것도 하지 않는다 — 재진입 가드는
  `Phase == Ended` 체크 하나.

### 종료의 정체

종료는 화면이 아니라 상태 전이 + 스냅샷이다. 순서가 정확성의 전부다.

1. `Phase = Ended`.
2. `StageMetrics` 스냅샷 — `DeliveryDirector.RequestCompleted`/`RequestCancelled`가 누적한 카운터,
   `DeliveryDirector.TotalPoints`, `SnowField.TotalDepthCm` 대 초기값. **이 시점 이후 완주하는
   트럭은 반영되지 않는다** — `DeliveryTruck.TickDriving`이 `DeliveryDirector`와 무관하게 자기
   `FixedUpdate`에서 계속 굴러가기 때문이다.
3. `StageStarRubric.Evaluate(metrics)` → `StageResult`.
4. 입력 게이팅. `VehicleInput`/`SnowPlowInput`을 끄기 **전에** 값을 비운다(아래 "입력 게이팅" 절).
5. `DeliveryDirector.SetMaxConcurrentTrucks(0)` — 신규 스폰만 막는다. 달리던 트럭은 계속 달린다.
6. `StageEnded` C# 이벤트 + `UnityEvent` 발화. 구독자가 없어도 정상.
7. `_autoReturnToMenuSeconds`(기본 3) 뒤 `SceneManager.LoadScene(0)`. 결과 UI가 오면 이 필드를
   0으로 두고 UI의 버튼이 같은 메서드를 부른다 — 지울 코드는 이 자동 복귀 한 곳뿐이다.

`Time.timeScale = 0`은 쓰지 않는다. 결과 화면 DOTween이 멈추고(`InGame/UI/AGENTS.md`의
`SetUpdate(true)` 요구와 같은 이유), `SnowField`/`SnowPlowLedger` 원장이 물리 틱 중간에 멈추며,
헤드리스 테스트 사이로 전역 상태가 샌다.

### 입력 게이팅 — Vehicle 최소 수정

`VehicleInput.OnDisable`은 지금 `_drivingMap.Disable()`만 하고 `Move`/`AccelerateHeld`
등 프로퍼티는 마지막 `Update` 값에 남는다. Intro 3초 동안, 또는 종료 후 가속 키를 누른 채
있으면 차가 계속 움직인다. `OnDisable`에서 모든 프로퍼티를 기본값으로 비우는 처리를 추가한다
(`Vehicle/AGENTS.md`에 기록, 코드는 `Vehicle/`이 소유).

### 별 루브릭

`VacuumToolModeCatalog` 패턴. `StageStarRubric`(ScriptableObject) = `StageStarEntry` 리스트.
`EStarMetric`은 지금 CPU에서 읽을 수 있는 값만 취급한다.

| 별 | 지표 | 기준 |
|---|---|---|
| ★ | `DeliveriesCompleted` | ≥ 2건 |
| ★★ | `SnowClearedPercent` | ≥ 60% (`1 − TotalDepthCm / 초기 TotalDepthCm`) |
| ★★★ | `DeliveriesCancelled == 0` 이고 `TotalPoints` | 취소 0건 + 총점 ≥ N |

먼지 청결도는 기준에 넣지 않는다 — `Neighborhood_ConceptMap`은 dust에서 snow로 이미 교체됐고,
먼지 마스크는 `RenderTexture`라 CPU 대표값을 새로 만드는 별도 작업이 필요하다.

새 별 추가 = 에셋에 행 하나. 새 *지표 종류* 추가만 enum 케이스 + switch 분기가 필요하다.

### 씬 리그 — 중복 없는 재사용

`Delivery/Editor/DeliveryTestSceneBuilder.cs`(690줄)는 노드 21·구간 22·공장 11 좌표표, 갓돌 콜라이더
보정, 커브 램프 24개, 맵 `SnowStage`/차량/카메라 재사용, 강 리스폰 볼륨을 이미 검증했다. 이 좌표표를
두 번째 씬을 위해 복제하지 않는다.

- `Delivery/Editor/DeliverySceneRigBuilder.cs`(신규) — 위 절차 중 씬 리그를 만드는 부분(맵 복사,
  노드/구간/공장 생성, 갓돌·램프 보정, 트럭 프리팹 배치용 `DeliveryDirector` 구성, 차량·카메라
  재사용/폴백, 강 리스폰)을 공용 정적 메서드로 뽑는다. `DeliveryTestSceneBuilder`는 이 메서드를
  호출하도록 바뀌고 동작은 그대로다.
- `Cleanliness/Editor/SinglePlaySceneBuilder.cs`(신규) — `DeliverySceneRigBuilder`를 부른 뒤
  `SinglePlayRig` 루트에 `SinglePlayDirector` + `StageIntroController` 배선을 얹는다.
- 대상 씬: `Cleanliness/Scenes/SinglePlay.unity`.

⚠ **GUID 안정성.** 테스트 씬은 아무도 참조하지 않아 매 빌드 새 GUID여도 무방했지만, `SinglePlay`는
Build Settings가 GUID로 참조한다. 빌더는 씬이 없을 때만 `CopyAsset`하고, 있으면 `SinglePlayRig`
루트만 지우고 다시 얹는다. 맵 원본이 바뀌면 씬을 지우고 재실행 + Build Settings 재등록이 필요하다
— 이 비용은 감수한다.

### Build Settings

`LoadingScreen`이 `buildIndex + 1`을 로드하므로 `SinglePlay.unity`를 인덱스 2에 넣는다
(`Neighborhood_ConceptMap`은 3으로 밀린다). `LoadingScreenController`는 수정하지 않는다.

## 결정하지 않은 것

멀티플레이의 종료 흐름. `Game_Concept.md` §3/§7의 수동 종료 버튼 + 성공/실패 설명은 그대로 두고
멀티플레이 전용으로 라벨만 바꿨다. 멀티가 이 시간제 루프를 따를지는 §9에 미결로 남긴다.

## 테스트

- **EditMode** — `StageStarRubric` 평가(경계값, 별 0/일부/전체, 지표 확장 시나리오), 단계 전이
  타이밍(짧은 `_playSeconds`로), 스냅샷 이후 값 변화가 결과에 영향 없음을 가짜 델리게이트로 검증.
  씬·GPU 없이 전부 돈다.
- **PlayMode** — `SinglePlay` 씬 로드 → Intro 재생 → Playing 전이 → 입력 게이팅 확인 →
  강제 만료(`_playSeconds` 축소) → Ended 로그 → 자동 메뉴 복귀.
- **헤드리스** `-batchmode -nographics` 1회. `unity run`은 `-quit`를 주입해 `-runTests` 전에
  종료되므로 에디터 바이너리를 직접 호출한다.
