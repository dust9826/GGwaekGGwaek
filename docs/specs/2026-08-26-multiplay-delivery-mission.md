# 배달 미션 멀티플레이 — 설계

> 2026-08-26 · `/main/multiplay-delivery-mission` (`/main` `cs:757`에서 분기)
> 선행: [멀티 펭귄 재구축](2026-08-24-multiplay-penguin-rebuild.md) · [선물 배달](2026-08-19-player-gift-delivery.md)

## 목표

의뢰 게임(`RequestDirector` + `GameManager`)을 **멀티플레이에서 플레이 가능하게** 만든다.
두 사람이 같은 마을에 들어와 같은 의뢰 목록을 보고, 각자 선물을 옮겨 같은 집에 배달하면
양쪽 화면에서 같은 결과가 나오는 것까지가 이 스펙의 범위다.

**범위 밖**: 난이도 곡선, 결과 화면의 멀티 규칙, 데디케이티드 서버 운영, NPC·차량 복제.
동기화 버벅임은 원인 규명이 아니라 **간단한 A/B 측정**까지만 한다.

## 지금 구조 (측정된 사실, `cs:757`)

| 요소 | 상태 |
|---|---|
| `RequestDirector`·`GameManager` | 순수 `MonoBehaviour`, `FixedUpdate`, GPU·카메라·로컬 입력 의존 없음 |
| `GiftRequest` | 권위값이 `Id`·`HouseIndex`(int)·`WantedKind`·`RemainingSeconds`뿐 — `[Networked]`로 옮기기 쉽게 이미 설계됨 |
| 완료 판정 | `Gift.All`에서 종류가 맞는 선물을 집 앞 `GiftDropZone` 안에서 찾아 `Destroy` |
| 선물 공급 | `GiftSpawner`가 `Instantiate`, Rigidbody 2kg |
| 운반 | `PenguinCarry`가 화물을 `isKinematic = true`로 만들어 등에 얹고, 내려놓을 때 되돌린다 |
| 세션 | Fusion 2 설치됨. `SessionLauncher`가 런타임에 러너 + `RunnerSimulatePhysics`를 만든다 |
| 아바타 | 서버가 `Resources/PF_PenguinNet`을 스폰. 위치는 **원점 반경 3m 원** — 마을 좌표를 모른다 |
| 씬에 배치된 `NetworkObject` | 프로젝트 전체에 **0개**. 전부 서버가 `Runner.Spawn` 한다 |
| 미션 씬 | `Cleanliness/Scenes/SinglePlay.unity` (SnowDelivery 빌더 산출물, 2026-08-28 승격) |
| `MultiPlay.unity` | Ground·Sun·SnowCpuStage·MultiPlayBootstrap 4개뿐인 빈 샌드박스. **Build Settings에 이미 등록돼 있다** |

## 채택 — A. 미러 허브

서버가 `MissionNetHub`(`NetworkBehaviour`) 프리팹을 스폰한다. 서버는 기존 `RequestDirector`·
`GameManager`를 **손대지 않은 채** 그대로 굴리고, 허브가 그 상태를 `[Networked]`로 복사한다.
클라이언트는 허브만 읽는다.

`PenguinNetAvatar`가 `PenguinLocomotion`을 감싸는 방식 그대로다 — "본문은 여기 없다".
게임 규칙이 두 벌로 갈라지지 않고, 싱글플레이와 기존 테스트는 그대로 돈다.

### 복제 계약

`MissionNetHub`가 드는 것 — **결과가 아니라 원인**:

| 값 | 형태 | 이유 |
|---|---|---|
| 활성 의뢰 배열 | `[Networked, Capacity(N)] NetworkArray<NetRequest>` | HUD·집 표지가 그린다. `NetRequest`는 `Id`·`HouseIndex`·`Kind`·`RemainingSeconds` |
| 전역 남은 시간 | `[Networked] float` | 팀 공용 자원. 화면에 계속 뜬다 |
| 점수 | `[Networked] int` | 공용 |
| 페이즈 | `[Networked] EGamePhase` | Intro→Playing→Ended |

**집·도로망·구역은 복제하지 않는다.** 모든 피어가 같은 씬을 로드하므로 씬 안의 정적 배치는
이미 같다. 의뢰가 `DeliveryHouse` 참조 대신 `HouseIndex`를 드는 것이 이것을 성립시킨다 —
인덱스는 씬에 직렬화된 배열 순서이므로 피어마다 동일하다.

### HUD가 읽는 자리

`RequestHudPresenter`는 지금 `(director, manager)`를 직접 받는다. 싱글은 그대로 두고,
읽기 인터페이스 하나를 만들어 **디렉터 쌍과 허브가 각각 구현**한다. 두 번째 호출부(클라이언트)가
확정된 시점이므로 루트 규칙의 "두 번째 호출부에서 추상화" 조건을 만족한다.

### 비채택

- **B. 디렉터 자체를 `NetworkBehaviour`로.** 진실 원본이 하나가 되는 대신 `RequestDirectorPlayModeTests`·
  `GameManagerPlayModeTests`·싱글 씬이 전부 러너를 요구하게 되고, 다른 워크스페이스에서 동시에
  편집 중인 파일과 정면으로 겹친다.
- **C. 시드만 동기화하고 피어마다 같은 시뮬.** 완료 판정이 물리(선물이 구역 안에 있는가)에 걸려
  있어 피어마다 갈린다.

## 단계

각 단계가 체크인 하나이고, 각 단계 끝에 MPPM 2인으로 눈으로 본다.

### 1. 씬 — 멀티 진입 경로가 마을에 도착한다

`MultiPlay.unity`를 빌더로 다시 찍어 마을 맵·도로망·집·눈 스택·의뢰 리그를 얹는다.
`SnowDeliverySceneBuilder`의 절차를 재사용하되 산출 경로만 다르다.

**`MultiPlay.unity`를 재사용하는 이유**: 이미 Build Settings에 있다. 새 씬을 등록하면
`ProjectSettings/EditorBuildSettings.asset`을 건드리게 되는데, 그 파일은 모든 브랜치가 같은 줄을
고치는 충돌 지뢰다(루트 `AGENTS.md`).

씬이 스폰 지점을 `SessionLauncher`에 알려 준다 — `MultiPlayBootstrap`이 씬 경로와 아바타 이름을
알려 주는 것과 같은 방식. Core가 마을 좌표를 아는 일은 없다.

**검증**: 2인이 마을 안 서로 다른 지점에 서고, 각자 눈밭이 돌고, 서로가 보인다.

### 2. 미션 권위 — 같은 의뢰를 같이 본다

`MissionNetHub` 추가. 서버만 `RequestDirector`·`GameManager`를 켜고(`Runner.IsServer` 게이트),
클라이언트에서는 꺼서 자기 난수로 자기 의뢰를 만들지 않게 한다. HUD는 허브를 읽는다.

**검증**: 두 화면의 의뢰 목록·남은 시간·점수가 같다. 클라이언트에서 시간이 따로 흐르지 않는다.

### 3. 선물 — 옮기고 배달하는 것이 양쪽에 보인다

- `PF_GiftBoxNet` — `NetworkObject` + `NetworkRigidbody` + `[Networked] Kind`·`Carried`
- `GiftSpawner`는 서버에서만 돌고 `Runner.Spawn` 한다
- 완료 판정의 `Destroy(gift)`는 `Runner.Despawn`으로 바뀐다
- `PenguinCarry`는 서버에서만 돈다. 운반 중 자세는 서버가 kinematic 바디를 움직이고
  `NetworkRigidbody`가 그것을 클라이언트에 그린다

**미측정 위험**: 운반 중 `isKinematic` 전환이 `NetworkRigidbody` 보정과 어떻게 맞물리는지 아직
모른다. 이 단계의 첫 검증 대상이며, 어긋나면 운반 중에는 소유권을 펭귄에 붙이는 쪽으로 바꾼다.

**검증**: 클라이언트가 선물을 메고 집 앞에 놓으면 서버·클라 양쪽에서 의뢰가 완료되고 상자가
사라진다.

### 4. 간단 측정과 플레이테스트

Host 버벅임을 `SnowDisplaceView` OFF → 눈 복제 OFF 순으로 A/B 하고 첫 로드 히치와 반복 버벅임을
구분한다. 원인 규명이 아니라 **어느 쪽이 지배적인지**까지만 본다. 그다음 2인 플레이.

## 미정

- **멀티의 종료 규칙.** 전역 시간 0이 전원 종료인지, 결과 화면을 누가 보는지. 2단계에서 정한다.
- **점수가 공용인지 개인인지.** 이 스펙은 공용으로 가정한다 — 협동 게임이고 시간 풀이 이미 공용이다.
- **의뢰 정원(`Capacity`).** `StageBalanceConfig.MaxActiveRequests`가 실시간으로 바뀌는데
  `[Networked]` 배열 용량은 컴파일 타임 상수다. 상한을 넉넉히 잡고 초과분은 서버가 자른다.
