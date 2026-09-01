# 차량 차체 필링 — 설계

> **작성일** 2026-08-14 · **브랜치** `/main/vehicle-body-feel` · **피처** `Assets/Game/InGame/Vehicle/`

차량의 **비주얼 차체**가 주행에 반응하게 한다. 가속하면 뒤로 눌리고, 드리프트하면 바깥으로
기울고, 벽에 박으면 찌그러졌다 돌아온다. 레퍼런스는 크레이지 택시(Sega, 1999)다.

선행: [2026-08-12-mop-driving-feel.md](2026-08-12-mop-driving-feel.md) — 주행 모델 자체는 거기서
만들어져 `Vehicle/`로 이식됐다. 그 문서가 "다음 단계"로 미뤄둔 연출 중 **차체 부분만** 이 문서가
맡는다. 화면·소리·FOV는 범위 밖이다.

---

## 0. 범위

| | |
|---|---|
| **하는 것** | 차체 트랜스폼이 가속·드리프트·충돌에 반응한다 |
| **안 하는 것** | 카메라, 화면 효과, 오디오, 주행 모델 변경 |

범위를 차체로 좁힌 것은 사용자 결정이다. 크레이지 택시 속도감의 절반이 카메라와 소리에서
나온다는 것은 선행 문서 §0에 이미 적혀 있고 여전히 사실이다 — 이 문서를 다 구현해도 그 절반은
비어 있다. 실패가 아니라 순서다.

**주행 모델은 한 줄도 건드리지 않는다.** 이것은 순수 프레젠테이션 레이어다. 판정 기준은 §6에
있다 — 실측 주행 수치가 변하면 그것은 이 작업이 선을 넘었다는 뜻이다.

---

## 1. 제약 — 측정으로 확인한 것

설계가 여기서 갈리므로 먼저 적는다. 전부 이번에 파일을 열어 확인했다.

### ~~Feel은 우리 코드에서 호출할 수 없다~~ — 철회 (2026-08-14)

**이 절의 원래 주장은 틀렸다.** 아래에 원문을 남기는 것은 그 위에서 내린 설계 결정 하나
(§4.3 의 "세기 비례 성분은 위치 킥이 맡는다")가 이 전제에서 나왔기 때문이다.

> `Assets/Feel/MMFeedbacks/`에 `.asmdef`가 없다. 따라서 `MMF_Player`는 `Assembly-CSharp`에
> 들어가고, `PPack.InGame` 안에 있는 우리 스크립트는 **그 타입을 이름으로 부를 수 없다.**
> 루트 `AGENTS.md`에 적힌 그대로고, 실제로 그렇다.

**실제로는 부를 수 있다.** `MMFeedbacks/` 에 `.asmdef` 가 없는 것은 맞지만 **`.asmref` 가 있다** —
`Assets/Feel/MMFeedbacks/MMFeedbacks/MoreMountains.Feedbacks.asmref` 가 그 폴더를 기존
`MoreMountains.Tools` 어셈블리에 편입시킨다. 유니티 자신이 그렇게 직렬화한다 — 프리팹이
`MMF_Player` 를 `MoreMountains.Feedbacks.MMF_Player, MoreMountains.Tools` 로 적는다. 그리고
`PPack.InGame.asmdef` 는 **이미 `MoreMountains.Tools` 를 참조 목록에 갖고 있다.**
`PPack.InGame` 안에 `MMF_Player` 필드를 두고 `PlayFeedbacks(position, intensity)` 를 부르는
스크립트를 컴파일해 확인했다 — 에러 0.

`*.asmdef` 만 검색하고 **`*.asmref` 를 검색하지 않은 것**이 원인이다. 루트 `AGENTS.md` 에 같은
내용이 적혀 있어서 확인했다고 여겼지만, 확인한 것이 아니라 반복한 것이었다.

**여전히 맞는 것:** `UnityEvent` 인스펙터 바인딩으로는 세기를 못 넘긴다. `FeedbacksIntensity` 는
프로퍼티가 아니라 필드이고, 세기를 받는 오버로드는 인자가 4개다. 틀린 것은 "그래서 UnityEvent
밖에 방법이 없다"는 결론이다.

→ **구현은 `MMF_Player` 를 직접 부른다.** 스쿼시도 위치 킥과 같이 충돌 세기에 비례한다.
자세한 것은 §4.3 과 `InGame/Vehicle/AGENTS.md`.

### 그 연결은 실제로 가능하다

```csharp
// MMF_Player.cs:330
public override void PlayFeedbacks()
{
    PlayFeedbacksInternal(this.transform.position, FeedbacksIntensity);
}
```

파라미터 없는 public 메서드라 `UnityEvent` 인스펙터 드롭다운에 잡힌다.

### 하지만 세기는 못 넘긴다

```csharp
// MMF_Player.cs:40
public float FeedbacksIntensity;
```

**프로퍼티가 아니라 필드다.** `UnityEvent`의 인스펙터 바인딩은 메서드와 프로퍼티 setter만
노출하고 필드는 노출하지 않는다. 세기를 인자로 받는 오버로드
(`PlayFeedbacks(Vector3, float, bool, Dictionary)`)는 인자가 4개라 역시 바인딩되지 않는다.

→ **충돌 세기에 비례하는 성분은 Feel이 아니라 우리 스크립트가 맡는다** (§4.3).

### 스쿼시는 차체 치수를 보존한다

```csharp
// MMF_SquashAndStretch.cs:109 (Initialization) 및 :207 (재생 시)
_initialScale = SquashAndStretchTarget.localScale;
```

캡처한 초기 스케일에서 상대적으로 움직이므로, `Body`의 `(1.8, 0.9, 4)`를 `(1,1,1)`로 날리지
않는다. 초기 검토 때 반대로 판단했다가 소스를 읽고 정정했다. **그래도 §6에서 복귀를 숫자로
확인한다** — 누적 변형은 조용히 일어나고 한참 뒤에 발견된다.

### 지금 있는 것

`VehicleController`가 이미 내보내는 값 — `IsGrounded`(:79), `CurrentSpeed01`(:90),
`SlipAngle`(:93), `IsDrifting`(:96).

없는 것 — 충돌 이벤트, 가속도.

`Body`는 유니티 기본 Cube다. `localScale (1.8, 0.9, 4)`, 컴포넌트는 Transform · MeshFilter ·
MeshRenderer 셋뿐이고 콜라이더도 자식도 없다. **프로젝트에 차량 메시가 없다** — 스쿼시가
박스에서 얼마나 읽히는지는 §8에 열린 항목으로 둔다.

---

## 2. 계층 — 채널 하나에 주인 하나

```
PF_VehicleProto      scale 1 · Rigidbody · BoxCollider(PM_Frictionless)
├─ BodyPivot         scale 1 · rot 0 · pos 0        ← VehicleBodyMotion 이 소유
│   └─ Body          scale (1.8, 0.9, 4)            ← MMF_Player 가 소유
└─ Nozzle            (형제, 변경 없음)
```

**`BodyPivot`을 새로 끼운다.** 우리 스크립트는 피벗의 회전·위치만 쓰고, `Body`는 Feel에게
통째로 넘긴다. 그래야 앞으로 `MMF_Position`이든 `MMF_RotationShake`든 인스펙터에서 얹기만 하면
되고 코드를 다시 열 필요가 없다. 사용자 결정이다.

한 트랜스폼에 둘을 얹는 안(우리가 회전, Feel이 스케일)도 검토했다. 채널이 갈려 있어 당장은
동작하고 계층 변경도 없지만, Feel이 위치나 회전을 원하는 순간 충돌한다. 루트 `AGENTS.md`의
"레이어 분리는 두 번째 소비자가 확인된 뒤"에는 그쪽이 맞으나, **Feel에게 자유를 주는 것이 이
작업의 요청 자체**이므로 지금 나눈다.

세 가지가 안전한 것을 확인했다.

- **피벗이 스케일 1이라 `Body`의 치수가 그대로 내려간다.** 루트에 차체 스케일을 걸었다가
  `Nozzle` 오프셋이 4배로 부풀었던 사고(`Vehicle/AGENTS.md`)와 같은 형태가 아니다 —
  `Nozzle`은 피벗의 자식이 아니라 형제다.
- **부모 회전 + 자식 비균등 스케일은 전단을 만들지 않는다.** 전단이 나는 것은 반대 순서
  (부모가 비균등 스케일, 자식이 회전)이고 이 배치는 그 형태가 아니다.
- **`Body` 트랜스폼을 참조하는 것이 없다.** 프리팹 안에서 루트의 자식 목록에만 등장하고,
  스크립트 어디서도 잡지 않는다. `VehicleVacuum.Body`는 흡입 대상의 `Rigidbody`를 담는 무관한
  필드다.

---

## 3. 컴포넌트 둘

### `VehicleBodyMotion` — `BodyPivot`에 붙는다

읽는다: `VehicleController`의 `IsDrifting` · `IsGrounded`, 그리고 루트
`Rigidbody.linearVelocity`.

속도 하나에서 **종방향 가속도**(미분, §4.1)와 **부호 있는 슬립각**(§4.2) 둘 다 나온다.
`SlipAngle`은 부호가 없어 쓰지 않는다.

쓴다: `BodyPivot`의 `localRotation`, `localPosition`. **스케일은 건드리지 않는다.**

**가속도를 컨트롤러에서 받지 않고 속도에서 미분하는 이유가 있다.** `VehicleController`를 수정할
필요가 없고, 무엇보다 **속도는 관찰 가능한 값**이다. Fusion이 들어오면 원격 클라이언트가 복제된
속도로 같은 계산을 그대로 한다. 스로틀 입력을 읽으면 내 차만 반응하고 팀메이트 차는 뻣뻣하게
간다 — 루트 `AGENTS.md`가 "VFX를 로컬 입력이 아니라 실제로 적용된 복제 상태로 구동하라"고 적은
것과 같은 함정이다.

**갱신 시점을 나눈다.** 가속도 샘플링은 `FixedUpdate`(물리 50Hz), 스프링 적분과 트랜스폼 쓰기는
`LateUpdate`(`Time.deltaTime`). 이 프로젝트는 물리 50Hz에 화면 451fps를 실측했고, 보간 없이
물리 값을 그대로 쓰면 한 값을 아홉 프레임 붙잡고 있다가 튄다는 것을 이미 겪었다. 같은 실수를
비주얼 레이어에서 반복하지 않는다.

### `VehicleImpactRelay` — 루트에 붙는다

`OnCollisionEnter`는 콜라이더를 가진 오브젝트에서 불린다. 콜라이더는 루트에 있고 `Body`에는
없으므로 여기여야 한다.

- 충격 세기 = 충돌 법선 방향의 상대 속도
- 임계값을 넘으면 ① `VehicleBodyMotion.AddImpulse(세기, 방향)` 직접 호출 ② `UnityEvent OnImpact` 발사

같은 어셈블리라 ①은 그냥 메서드 호출이다. ②만 인스펙터를 거친다.

프리팹 안에서 자기 자식(`Body`의 `MMF_Player`)을 참조하는 것이라, `MopPad._vfx`가 겪었던
**프리팹은 씬 오브젝트를 참조할 수 없다** 문제는 생기지 않는다. 이 연결은 프리팹에 저장되고
씬마다 다시 물릴 필요가 없다.

---

## 4. 세 가지 동작

### 4.1 가속·제동 → 피치

전진 방향 가속도를 X축 회전으로. 가속하면 앞이 들리고, 제동하면 코가 박힌다.

**정규화 기준을 실측에서 역산하지 않는다. 모델이 이미 상수로 들고 있다.** `VehicleController`의
`Drive()`는 매 스텝 `MoveTowards(forwardSpeed, target, rate * dt)`를 부르고, 그 `rate`는 셋 중
하나다. 프리팹 값 기준(C# 기본값이 아니라 프리팹에서 읽었고, 둘은 일치한다):

| 상황 | 필드 | 값 |
|---|---|---|
| 스로틀 | `_accel` | `20 m/s²` |
| 제동 (스로틀이 진행 방향과 반대) | `_brakeDecel` | `30 m/s²` |
| 무입력 코스팅 | `_coastDecel` | `12 m/s²` |

이것이 종방향 가속도가 실제로 가질 수 있는 전 범위다. 피치는 측정한 가속도를
`[-_brakeDecel, +_accel]`로 정규화한다 — 튜닝할 상수가 하나(최대 각도)로 줄어든다.

**그리고 그 범위로 클램프해야 한다.** 우리는 `Rigidbody.linearVelocity`를 미분하는데, 충돌
해소가 넣은 속도는 이 세 rate와 무관하게 한 스텝에 크게 튄다. 클램프가 없으면 벽에 닿는 순간
피치가 통째로 꺾이고, §4.3의 충돌 킥과 **같은 사건에 두 번 반응**한다. 클램프하면 충돌 반응은
`VehicleImpactRelay` 한 곳에서만 나온다.

시작값 **최대 ±5°**. 튜닝 대상이다.

### 4.2 드리프트 → 롤

**미끄러지는 쪽으로** Z축 롤. 왼쪽으로 돌면 차는 바깥(오른쪽)으로 미끄러지고 차체도 오른쪽으로
기운다 — 무게가 바깥으로 쏠리기 때문이다. 안쪽으로 기울면 오토바이가 된다.

**`VehicleController.SlipAngle`을 그대로 쓸 수 없다. 부호가 없다.**

```csharp
// VehicleController.cs:214
SlipAngle = result.sqrMagnitude < 0.01f
    ? 0f
    : Vector3.Angle(result, forwardSpeed >= 0f ? forward : -forward);
```

`Vector3.Angle`은 항상 `0 ~ 180`을 준다. 얼마나 미끄러지는지는 알려주지만 **어느 쪽으로**
미끄러지는지는 알려주지 않으므로, 이 값만으로는 왼쪽 드리프트와 오른쪽 드리프트가 구별되지
않는다. 그대로 롤에 넣으면 양쪽 모두 같은 방향으로 기운다.

→ **부호 있는 슬립각을 `VehicleBodyMotion`이 직접 구한다.** 어차피 속도를 샘플링하고 있다.

```csharp
float lateralSpeed  = Vector3.Dot(planarVelocity, right);    // 부호 있음
float forwardSpeed  = Vector3.Dot(planarVelocity, forward);
float signedSlipDeg = Mathf.Atan2(lateralSpeed, Mathf.Abs(forwardSpeed)) * Mathf.Rad2Deg;
```

크기는 `SlipAngle`과 같으므로 **실측 기준은 그대로 살아 있다** — `Vehicle/AGENTS.md`의 평소
슬립각 `0.0°`, 드리프트 슬립각 `45.0°`. 이 두 점에 롤 각을 매핑한다.

`VehicleController`는 고치지 않는다. `SlipAngle`을 부호 있게 바꾸면 이 값을 이미 구독하는 쪽이
영향을 받고, 이 작업은 주행 모델을 건드리지 않기로 되어 있다(§0).

시작값 **슬립 45°에서 롤 8°**. 튜닝 대상이다.

`IsDrifting`은 게인 배수로만 쓴다. 드리프트 키를 놓아도 슬립이 남아 있는 동안 기울기가 유지돼야
하므로, 롤의 주 입력은 상태 플래그가 아니라 슬립각이다.

### 4.3 충돌 → 스쿼시 + 킥

두 성분으로 나눈다. **둘 다 충돌 세기에 비례한다.**

| 성분 | 주인 | 세기 |
|---|---|---|
| 스케일 스쿼시 | Feel `MMF_SquashAndStretch` | `RemapCurveOne` 을 보간 |
| 위치 킥 + 반동 | `VehicleBodyMotion` | 충돌 속도에 비례 |

> **원래 이 표의 스쿼시는 "고정"이었다.** §1 의 철회된 전제에서 나온 결정이다. `MMF_Player` 를
> 직접 부를 수 있게 되면서 스쿼시도 비례로 바뀌었다.

**세기를 `PlayFeedbacks` 의 intensity 인자로 넘기면 안 된다.** 그 값은 피드백의 remap 값에
**곱해진다**(`MMF_SquashAndStretch.cs:175`). 스쿼시는 배율이 1 미만이므로, 세기를 낮추면
`RemapCurveOne × intensity` 가 0 쪽으로 가서 **약한 충돌이 더 크게 눌린다** —
`intensity 0.5` 면 `Lerp(1, 0.425, 1.5) = 0.1375` 라 Z 가 `4.0m → 0.55m` 다.

배율 자체를 보간해야 한다.

```csharp
_squash.RemapCurveOne = Mathf.Lerp(1f, _squashRemapAtFullImpact, strength01);
_impactFeedback.PlayFeedbacks();
```

실측 — `strength 0.25 → Z 3.775`(5.6% 압축), `strength 1.00 → Z 3.100`(22.5% 압축). 예측과 일치한다.

살짝 긁으면 차체가 조금 밀렸다 돌아오고, 정면으로 박으면 크게 튕기면서 확실히 눌린다.

임계값 시작값 **법선 속도 3 m/s**, 최대 **12 m/s**에서 포화. 부스트 최고속이 16 m/s이므로
정면 충돌이 상단에 닿는다.

---

## 5. Feel 구성 (전부 인스펙터)

`Body`에 `MMF_Player` 하나, 안에 `MMF_SquashAndStretch`.

| 항목 | 값 |
|---|---|
| `SquashAndStretchTarget` | `Body` |
| `Mode` | `Absolute` |
| `Axis` | `ZtoXY` (Z가 눌리고 X·Y가 부푼다 — 정면 충돌 기준) |
| `AnimateScaleDuration` | 0.2s (기본값) |

**`Axis`가 고정이라 측면 충돌도 앞뒤로 눌린다.** 알고 받는 한계다. 방향별로 하려면 `MMF_Player`를
정면/측면 2개 두고 relay가 골라 쏘면 되는데, `VehicleImpactRelay`가 `UnityEvent`를 쏘는 구조라
**코드 변경 없이 인스펙터에서만** 늘릴 수 있다. 처음엔 하나로 간다.

---

## 6. 검증

씬은 이미 체크인된 `Vehicle/Tests/Vehicle_Prototype_Test.unity`를 쓴다. 새로 만들지 않는다.

**판정은 플레이 모드에서만 한다.** 에디트 모드 Game 뷰는 리페인트가 걸리지 않아 스크린샷 4장이
바이트 단위로 동일하게 나온 전례가 이 프로젝트에 있고, 그 위에서 내린 결론이 전부 틀렸다.
md5 비교로도 안 걸러진다 — 진짜 검은 프레임도 바이트가 같기 때문이다.

숫자로 볼 것:

| 확인 | 통과 기준 |
|---|---|
| 가속 중 `BodyPivot.localRotation.x` | 0이 아니고, 정지하면 0으로 복귀 |
| 드리프트 중 `BodyPivot.localRotation.z` | 슬립각과 같은 부호로 움직임 |
| 충돌 후 `Body.localScale` | **정확히 `(1.8, 0.9, 4)`로 복귀** |
| 콘솔 | 에러 0 |

셋째 줄이 이 표에서 제일 중요하다. 미세하게 안 돌아오면 충돌할 때마다 누적되어 한참 뒤에
"차가 언제부터인가 납작하다"로 발견된다.

회귀로 볼 것 — **아래 값이 변하면 실패다.** 순수 비주얼 레이어이므로 변할 이유가 없다.

| | 기준값 |
|---|---|
| 0→최고속 | `0.58s` |
| 코스팅 정지 | `0.96s` |
| 평소 슬립각 | `0.0°` |
| 드리프트 슬립각 | `45.0°` |

계측은 `VehicleDriveProbe`/`VehicleDriveAutopilot`을 쓴다. **오토파일럿은 유니티 앱이 OS 최상위여야
동작한다** — 포커스를 잃으면 합성 입력이 버려지고, 단계 로그는 다 찍히는데 차만 제자리여서
모델이 고장난 것처럼 보인다. 이 프로젝트에서 실제로 그렇게 오진한 적이 있다.

테스트가 끝나면 루트 `AGENTS.md` §5에 따라 플레이 모드를 끄고, 원래 씬으로 돌아가고, 검증용
오브젝트를 지운다.

---

## 7. 기각한 대안

- **`Body` 하나에 두 채널을 얹는다** — 회전/위치와 스케일은 안 싸우므로 계층 변경 없이 동작한다.
  Feel이 위치·회전 피드백을 원하는 순간 깨진다는 이유로 기각. §2 참조.
- **Feel 없이 전부 스크립트로** — 제어는 완전하지만 "Feel을 사용해서"라는 요청과 어긋나고,
  스쿼시 커브를 인스펙터에서 만지는 이점을 버린다.
- **`Assets/Feel/MMFeedbacks/`에 asmdef를 추가해 참조 가능하게 만든다** — 벤더 에셋 수정이라
  재임포트에 날아가고(루트 `AGENTS.md`), Feel 자체 데모 스크립트들이 `Assembly-CSharp`에서
  서로를 참조하고 있어 파급이 크다.
- **상시 기울기도 Feel의 무한 반복 피드백으로** — `MMF_Player`는 이벤트 도구다. 유지되는 상태를
  만들려면 무한 반복과 수동 중단이 필요하고, 그만큼 상태 관리가 지저분해진다.

---

## 8. 열린 것

- **`Body`가 기본 Cube라 스쿼시가 얼마나 읽힐지 모른다.** 프로젝트에 차량 메시가 없다. 곡선은
  피벗과 `MMF_Player`에 있으므로 진짜 메시가 오면 그대로 옮겨가지만, **박스에서 튜닝한 값이 차
  실루엣에서도 맞다는 보장은 없다.**
- **측면 충돌이 앞뒤로 눌린다** (§5). 인스펙터에서 늘릴 수 있게 열어둔다.
- **접지 상태를 쓸지 미정.** `IsGrounded`를 읽지만 착지 충격을 넣을지는 정하지 않았다. 점프가
  없어진 뒤 공중에 뜰 일이 드물다.
- 카메라·오디오·화면 효과는 범위 밖 (§0). 크레이지 택시 속도감의 나머지 절반이 거기 있다.
