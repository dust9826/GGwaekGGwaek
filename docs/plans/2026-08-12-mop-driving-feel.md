# 대걸레 주행 필링 — 구현 계획

> 스펙: [2026-08-12-mop-driving-feel.md](../specs/2026-08-12-mop-driving-feel.md)
> 브랜치: `/main/mop-driving-feel` (cs:99 기준)

**목표**: 대걸레를 탈것으로 바꾸고, 관성과 드리프트가 있는 아케이드 주행을 넣는다.

**접근**: `Rigidbody`의 `linearVelocity`를 덮어쓰지 않고 정면/횡으로 분해해 각각 다른 마찰을
먹인 뒤 되돌려쓴다. 조향 각속도와 횡 그립은 속도의 함수다.

**검증 수단**: 유닛 테스트가 아니라 **플레이 모드 실측**이다. `MopDriveProbe`가 네 수치를
`Debug.Log`로 뱉고, `unity cmd get_console_logs`로 읽는다.

---

## 전역 제약

스펙에서 그대로 가져온다. 모든 태스크에 적용된다.

- **네임스페이스는 `PPack` 하나다.** 폴더·어셈블리를 따르지 않는다
- **타입 이름이 팀원 것과 겹치면 안 된다.** `Vehicle*`은 `InGame/Vehicle/`가 쓴다. 접두사는 `Mop`
- **`Assets/Game/InGame/Vehicle/` 아래 파일은 한 줄도 고치지 않는다**
- **`PF_VehicleProto.prefab` · `Vehicle_Prototype_Test.unity`를 열지도 저장하지도 않는다.** YAML은 머지가 안 된다
- 비공개 필드는 `_camelCase`, 타입·메서드는 `PascalCase`, 열거형은 `E` 접두사
- 직렬화된 Unity Object 필드는 `== null` / `!= null` (fake-null)
- 주행 적분은 전부 `FixedUpdate`. 붓질(`MopPad`)은 `Update`에 그대로 둔다
- 확정된 튜닝값은 **코드 기본값으로 구워 넣는다.** 프리팹에 오버라이드를 남기지 않는다
- 새 씬을 **Build Settings에 넣지 않는다**
- 에셋 이동·삭제는 Unity Project 창에서만. `.meta`는 자산과 함께 다닌다

### 위임 분담

셰이더 때 정한 선례를 따른다 — **텍스트는 위임 가능, YAML과 검증은 직접.**

| | 누가 |
|---|---|
| Task 1·2 (C# 두 개) | Codex 위임 가능 |
| Task 3·4 (프리팹·씬) | 직접. Unity CLI로 만든다 |
| Task 5 (튜닝·판정) | 직접. 손맛 판정은 사용자가 한다 |
| Task 6 (문서·체크인) | 직접 |

---

## 파일 구조

| 파일 | 책임 | 상태 |
|---|---|---|
| `Assets/Game/InGame/Mop/Scripts/MopVehicle.cs` | 주행 전부 — 속도 분해, 조향 곡선, 그립 곡선, 입력 맵 수명 | 신규 |
| `Assets/Game/InGame/Mop/Scripts/MopDriveProbe.cs` | 검증 수치 넷을 재서 로그로 뱉는다 | 신규 |
| `Assets/Game/InGame/Mop/Prefabs/PF_MopVehicle.prefab` | 탈것 | 신규 |
| `Assets/Game/InGame/Mop/Tests/Mop_Driving_Test.unity` | 검증 코스 | 신규 |
| `Assets/Game/InGame/Mop/Input/MopControls.inputactions` | 기존 자산. **고치지 않는다** | 그대로 |
| `Assets/Game/InGame/Mop/Scripts/MopPad.cs` | 기존. **고치지 않는다** — 프리팹에 붙이기만 한다 | 그대로 |
| `Assets/Game/InGame/Mop/Scripts/MopMode.cs` · `MopLocomotion.cs` | 도보 청소 모드. **그대로 둔다** | 그대로 |

`MopVehicle`과 `MopDriveProbe`를 나눈 이유: 프로브는 검증 장치이고 주행 모델의 일부가 아니다.
`DustPadSweep`을 검증 도구로 남긴 선례와 같다.

---

## Task 1: `MopVehicle` — 주행 코어

**파일**
- 생성: `Assets/Game/InGame/Mop/Scripts/MopVehicle.cs`

**인터페이스**
- 소비: `MopControls.inputactions`의 `Cleaning` 맵, `Move` 액션 (`Vector2`)
- 생산: `public float Speed01 { get; }` — 0~1 정규화 속력. Task 2와 2단계 연출이 읽는다
- 생산: `public float SlipAngle { get; }` — 진행 방향과 정면이 벌어진 각도(도). Task 2가 읽는다

- [ ] **Step 1: 파일을 만든다**

```csharp
using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 아케이드 주행. <see cref="Rigidbody.linearVelocity"/> 를 덮어쓰지 않고 정면/횡으로
    /// 분해해 각각 다른 마찰을 먹인 뒤 되돌려쓴다.
    ///
    /// 횡 성분을 <b>버리지 않고 감쇠시키는</b> 것이 이 클래스의 전부다. 거기서 드리프트(몸이
    /// 돌아도 속도가 안 따라온다)와 벽 튕김(충돌이 준 횡속도가 살아남는다)이 함께 나온다.
    /// 팀원의 <c>InGame/Vehicle/VehicleController</c> 는 같은 자리에서 횡속도를 0 으로 만든다.
    ///
    /// 조향 각속도와 그립은 속도의 함수다. 속도 0 에서도 각속도가 살아 있으므로 제자리 회전이
    /// 분기 없이 나온다 — 레이싱에는 없는 예외지만 구석을 닦으려면 필요하다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    public sealed class MopVehicle : MonoBehaviour
    {
        private const string CleaningMapName = "Cleaning";
        private const string MoveActionName = "Move";

        [Tooltip("Cleaning 맵을 담은 자산. MopMode 없이 단독으로 도는 프리팹이라 직접 켠다.")]
        [SerializeField] private InputActionAsset _controls;
        [SerializeField] private Rigidbody _rigidbody;

        [Header("속도 (m/s, m/s²)")]
        [SerializeField, Min(0.1f)] private float _maxSpeed = 12f;
        [Tooltip("후진이 느려야 전진이 빨라 보인다.")]
        [SerializeField, Min(0.1f)] private float _reverseMaxSpeed = 4f;
        [Tooltip("0 → 최고속 0.6 초.")]
        [SerializeField, Min(0f)] private float _accel = 20f;
        [Tooltip("반대 입력. 코스팅보다 확실히 빨라야 브레이크로 읽힌다.")]
        [SerializeField, Min(0f)] private float _brakeDecel = 30f;
        [Tooltip("무입력. 손 떼면 1.5 초간 미끄러져 멈춘다.")]
        [SerializeField, Min(0f)] private float _coastDecel = 8f;

        [Header("조향 (도/초) — 속도 0 → 최고속")]
        [Tooltip("속도 0 에서의 각속도. 이 값이 곧 제자리 회전 속도다.")]
        [SerializeField, Min(0f)] private float _turnRateAtRest = 120f;
        [Tooltip("최고속에서의 각속도. 낮을수록 무겁고 차 같다.")]
        [SerializeField, Min(0f)] private float _turnRateAtTopSpeed = 70f;

        [Header("횡 그립 (m/s²) — 속도 0 → 최고속")]
        [Tooltip("초당 죽이는 횡속도. 낮을수록 크게 미끄러진다. 튜닝 1순위.")]
        [SerializeField, Min(0f)] private float _gripAtRest = 40f;
        [SerializeField, Min(0f)] private float _gripAtTopSpeed = 15f;

        private InputActionMap _cleaningMap;
        private InputAction _moveAction;

        /// <summary>0(정지) ~ 1(최고속)로 정규화한 평면 속력.</summary>
        public float Speed01 { get; private set; }

        /// <summary>진행 방향과 정면이 벌어진 각도(도). 드리프트 판정은 이걸 읽는다.</summary>
        public float SlipAngle { get; private set; }

        private void Reset()
        {
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponent<Rigidbody>();

            if (_controls == null)
            {
                Debug.LogError($"{nameof(MopVehicle)}: 입력 자산이 비어 있다.", this);
                enabled = false;
                return;
            }

            // 자산을 인스턴스로 복제한다. 원본을 직접 Enable 하면 씬을 나가도 활성인 채로 남는다.
            _controls = Instantiate(_controls);
            _cleaningMap = _controls.FindActionMap(CleaningMapName, true);
            _moveAction = _cleaningMap.FindAction(MoveActionName, true);
        }

        private void OnEnable()
        {
            if (_cleaningMap != null) _cleaningMap.Enable();
        }

        private void OnDisable()
        {
            if (_cleaningMap != null) _cleaningMap.Disable();
        }

        private void FixedUpdate()
        {
            if (_moveAction == null) return;

            Vector2 move = _moveAction.ReadValue<Vector2>();
            float dt = Time.fixedDeltaTime;

            Vector3 velocity = _rigidbody.linearVelocity;
            float vertical = velocity.y;                       // 중력·충돌의 수직 성분은 물리에 맡긴다
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);

            Speed01 = Mathf.Clamp01(planar.magnitude / _maxSpeed);

            // 조향. 회전 결과를 직접 계산해서 쓴다 — MoveRotation 직후의 transform.forward 는
            // 물리 스텝 전이라 아직 옛 값이다.
            float turnRate = Mathf.Lerp(_turnRateAtRest, _turnRateAtTopSpeed, Speed01);
            Quaternion rotation = _rigidbody.rotation * Quaternion.Euler(0f, move.x * turnRate * dt, 0f);
            _rigidbody.MoveRotation(rotation);

            Vector3 forward = rotation * Vector3.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 1e-6f) return;          // 완전히 수직으로 뒤집힌 프레임
            forward.Normalize();

            // 정면 성분은 스로틀이, 횡 성분은 그립이 다룬다. 이 두 줄이 설계 전부다.
            float forwardSpeed = Vector3.Dot(planar, forward);
            Vector3 lateral = planar - forward * forwardSpeed;

            float targetSpeed = move.y >= 0f ? move.y * _maxSpeed : move.y * _reverseMaxSpeed;
            bool noThrottle = Mathf.Abs(move.y) < 0.01f;
            bool braking = !noThrottle && forwardSpeed * move.y < 0f;
            float rate = noThrottle ? _coastDecel : (braking ? _brakeDecel : _accel);
            forwardSpeed = Mathf.MoveTowards(forwardSpeed, targetSpeed, rate * dt);

            float grip = Mathf.Lerp(_gripAtRest, _gripAtTopSpeed, Speed01);
            lateral = Vector3.MoveTowards(lateral, Vector3.zero, grip * dt);

            Vector3 result = forward * forwardSpeed + lateral;
            _rigidbody.linearVelocity = new Vector3(result.x, vertical, result.z);

            SlipAngle = result.sqrMagnitude < 0.01f
                ? 0f
                : Vector3.Angle(result, forwardSpeed >= 0f ? forward : -forward);
        }
    }
}
```

- [ ] **Step 2: 컴파일을 확인한다**

```bash
U=/Users/dust9826/.unity/bin/unity
P=/Users/dust9826/Documents/UnityProjects/PPackPPack_v2
$U cmd --project-path "$P" recompile --no-banner
$U cmd --project-path "$P" recompile_status --no-banner
```

기대: `{"status":"completed","failed":false,"errors":[]}`

- [ ] **Step 3: 이름 충돌이 없는지 확인한다**

```bash
grep -rn "class MopVehicle\|class MopDriveProbe" /Users/dust9826/Documents/UnityProjects/PPackPPack_v2/Assets/Game --include=*.cs
```

기대: 각 1건. 2건이면 `PPack` 네임스페이스가 하나뿐이라 컴파일이 깨진다.

---

## Task 2: `MopDriveProbe` — 검증 수치를 재는 장치

스펙 §8의 기준 1·2·3·4·6을 사람 눈이 아니라 로그로 판정하기 위한 것이다. 체크인한다 —
`DustPadSweep`과 같은 성격이고, 튜닝을 반복할 때마다 같은 조건을 재현해야 한다.

**파일**
- 생성: `Assets/Game/InGame/Mop/Scripts/MopDriveProbe.cs`

**인터페이스**
- 소비: `MopVehicle.Speed01`, `MopVehicle.SlipAngle`, `Rigidbody.linearVelocity`, `Transform.position`
- 생산: 없음. `Debug.Log`만 낸다

- [ ] **Step 1: 파일을 만든다**

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 주행 튜닝용 계측기. 스펙 §8 의 기준을 눈이 아니라 로그로 판정한다.
    ///
    /// 손맛은 주관적이지만 "0 → 최고속 몇 초"는 아니다. 튜닝을 반복할 때 같은 조건을
    /// 재현하기 위한 장치이므로 체크인한다 — <see cref="DustPadSweep"/> 와 같은 성격이다.
    /// </summary>
    [RequireComponent(typeof(MopVehicle))]
    public sealed class MopDriveProbe : MonoBehaviour
    {
        [SerializeField] private MopVehicle _vehicle;
        [SerializeField] private Rigidbody _rigidbody;

        [Tooltip("최고속으로 간주할 Speed01. 1.0 은 마찰 때문에 잘 안 닿는다.")]
        [SerializeField, Range(0.8f, 1f)] private float _topSpeedThreshold = 0.95f;
        [Tooltip("정지로 간주할 속력 (m/s).")]
        [SerializeField, Min(0.01f)] private float _stopSpeed = 0.1f;
        [Tooltip("제자리 회전으로 간주할 Speed01 상한.")]
        [SerializeField, Range(0f, 0.2f)] private float _restSpeed01 = 0.05f;

        private float _accelStartTime = -1f;
        private float _coastStartTime = -1f;
        private Vector3 _restAnchor;
        private bool _hasRestAnchor;
        private float _maxSlipSeen;
        private bool _wasAboveTopSpeed;

        private void Reset()
        {
            _vehicle = GetComponent<MopVehicle>();
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_vehicle == null || _rigidbody == null) return;

            float speed01 = _vehicle.Speed01;
            float speed = new Vector3(_rigidbody.linearVelocity.x, 0f, _rigidbody.linearVelocity.z).magnitude;

            MeasureAcceleration(speed01, speed);
            MeasureCoast(speed01, speed);
            MeasureSlip(speed01);
            MeasureRestTurn(speed01);
        }

        // 기준 1: 0 → 최고속 0.6 ± 0.1 초
        private void MeasureAcceleration(float speed01, float speed)
        {
            if (speed <= _stopSpeed)
            {
                _accelStartTime = Time.time;
                return;
            }

            if (_accelStartTime < 0f) return;

            if (speed01 >= _topSpeedThreshold)
            {
                Debug.Log($"[MopDriveProbe] 0 → 최고속 {Time.time - _accelStartTime:F2}s (기준 0.6 ± 0.1)");
                _accelStartTime = -1f;
            }
        }

        // 기준 2: 최고속에서 손 떼면 1.5 초 내 정지
        private void MeasureCoast(float speed01, float speed)
        {
            bool aboveTop = speed01 >= _topSpeedThreshold;

            if (aboveTop)
            {
                _wasAboveTopSpeed = true;
                _coastStartTime = Time.time;
                return;
            }

            if (!_wasAboveTopSpeed || _coastStartTime < 0f) return;

            if (speed <= _stopSpeed)
            {
                Debug.Log($"[MopDriveProbe] 최고속 → 정지 {Time.time - _coastStartTime:F2}s (기준 1.5 이내)");
                _wasAboveTopSpeed = false;
                _coastStartTime = -1f;
            }
        }

        // 기준 3: 최고속에서 슬립각 30° 이상 / 기준 6: 저속에서 5° 미만
        private void MeasureSlip(float speed01)
        {
            float slip = _vehicle.SlipAngle;

            if (speed01 >= 0.5f && slip > _maxSlipSeen)
            {
                _maxSlipSeen = slip;
                Debug.Log($"[MopDriveProbe] 고속 최대 슬립각 {slip:F1}° (기준 30 이상)");
            }

            if (speed01 > _restSpeed01 && speed01 < 0.25f && slip > 5f)
            {
                Debug.LogWarning($"[MopDriveProbe] 저속에서 슬립각 {slip:F1}° — 기준 5 미만을 넘었다");
            }
        }

        // 기준 4: 정지 상태 조향 → 위치 이동 0.1 m 이내
        private void MeasureRestTurn(float speed01)
        {
            if (speed01 > _restSpeed01)
            {
                _hasRestAnchor = false;
                return;
            }

            if (!_hasRestAnchor)
            {
                _restAnchor = transform.position;
                _hasRestAnchor = true;
                return;
            }

            float drift = Vector3.Distance(transform.position, _restAnchor);
            if (drift > 0.1f)
            {
                Debug.LogWarning($"[MopDriveProbe] 제자리 회전이 {drift:F2} m 밀렸다 — 기준 0.1 이내");
                _hasRestAnchor = false;
            }
        }
    }
}
```

- [ ] **Step 2: 컴파일을 확인한다** — Task 1 Step 2와 같은 명령. 기대: 에러 0

- [ ] **Step 3: 체크인한다 (구현 1/2)**

```bash
cd /Users/dust9826/Documents/UnityProjects/PPackPPack_v2
cm add -R Assets/Game/InGame/Mop/Scripts
cm status --private
```

`.cs`와 `.cs.meta`가 모두 `Added`인지 확인한 뒤 체크인한다. **`.meta`는 자산을 따라가지 않는다.**

---

## Task 3: `PF_MopVehicle` — 탈것 프리팹

**파일**
- 생성: `Assets/Game/InGame/Mop/Prefabs/PF_MopVehicle.prefab`

**구조**

```
PF_MopVehicle            [Rigidbody, BoxCollider, MopVehicle, MopDriveProbe, MopPad]
  Body                   [MeshFilter(Cube), MeshRenderer]  ← 임시 표시. 아트 아님
  CamTarget              [Transform]                        ← Cinemachine Follow/LookAt 대상
```

- [ ] **Step 1: 빈 오브젝트로 조립한다**

Unity CLI로 만든다. `create_prefab`은 씬 오브젝트를 프리팹으로 굽는 명령이므로, 씬에서 조립한 뒤
굽는다. Task 4의 씬을 먼저 만들고 거기서 조립해도 된다 — **순서는 자유이나 프리팹이 씬보다
먼저 저장돼야 씬이 프리팹 인스턴스를 참조할 수 있다.**

- [ ] **Step 2: `Rigidbody`를 설정한다**

| 필드 | 값 | 이유 |
|---|---|---|
| `mass` | `100` | 소품에 밀리지 않을 정도 |
| `linearDamping` | `0` | **감속은 `_coastDecel`이 한다.** 댐핑을 걸면 튜닝값이 두 곳으로 갈린다 |
| `angularDamping` | `0.05` | 기본값 |
| `useGravity` | `true` | 수직 성분은 물리에 맡긴다 |
| ~~`constraints`~~ | ~~`FreezeRotationX \| FreezeRotationZ`~~ | **구현 중 코드로 옮겼다** — 아래 |
| `interpolation` | `Interpolate` | `FixedUpdate`로 움직이므로 없으면 화면이 떨린다 |
| `collisionDetectionMode` | `ContinuousDynamic` | 12 m/s에서 얇은 벽을 뚫는 것을 막는다 |

**정정 (구현 중, 2026-08-12): `constraints`는 코드로 옮겼다.** Unity CLI의
`set_component_properties`가 `m_Constraints`를 **성공으로 보고하면서 실제로는 무시한다** —
`get_serialized_fields`에도 안 나온다. 프리팹 설정에 맡기면 잘못 세팅됐을 때 조용히 깨지고,
주행 모델은 몸이 서 있는 것을 전제하므로 `MopVehicle.Awake()`에서 못 박는다.

```csharp
_rigidbody.constraints = RigidbodyConstraints.FreezeRotationX | RigidbodyConstraints.FreezeRotationZ;
```

- [ ] **Step 3: 콜라이더** — **정정: 루트에 `BoxCollider`를 따로 두지 않는다.** `Body` 큐브가
      이미 `BoxCollider`를 갖고 있고 로컬 스케일 `(1.2, 0.6, 2.0)`이 그대로 콜라이더 크기가
      된다. 자식 콜라이더는 부모의 `Rigidbody`에 붙으므로 동작이 같고, **보이는 것과 부딪히는
      것이 어긋날 수 없다**는 이점이 있다.

- [ ] **Step 4: `MopPad`를 붙이고 연결한다**

| 필드 | 값 |
|---|---|
| `_origin` | 루트 트랜스폼 (자기 자신) |
| `_vfx` | 비워둔다 — VFX는 이번 범위가 아니고, `MopPad`는 없어도 붓질한다 |
| `_localOffset` | `(0, 0, 1.3)` — 차체 반쪽 길이 1.0 보다 앞 |

나머지 붓 파라미터는 **기본값 그대로 둔다.** 손맛이 이미 맞춰진 값이다.

- [ ] **Step 5: `MopVehicle`·`MopDriveProbe`를 붙이고 연결한다**

`MopVehicle._controls` ← `Assets/Game/InGame/Mop/Input/MopControls.inputactions`
나머지 참조는 `Reset()`이 자동으로 채운다. 채워졌는지 확인한다.

- [ ] **Step 6: 프리팹으로 굽는다**

```bash
$U cmd --project-path "$P" create_prefab --target /PF_MopVehicle \
   --path Assets/Game/InGame/Mop/Prefabs/PF_MopVehicle.prefab --no-banner
```

- [ ] **Step 7: 직렬화 값을 되읽어 확인한다**

```bash
$U cmd --project-path "$P" get_serialized_fields --target /PF_MopVehicle --component MopVehicle --no-banner
```

기대: `_controls`가 비어 있지 않고, 수치가 Task 1의 기본값과 같다.

---

## Task 4: `Mop_Driving_Test` — 검증 코스

**파일**
- 생성: `Assets/Game/InGame/Mop/Tests/Mop_Driving_Test.unity`

**Build Settings에 넣지 않는다.**

- [ ] **Step 1: 씬을 만든다**

```bash
$U cmd --project-path "$P" create_scene --path Assets/Game/InGame/Mop/Tests/Mop_Driving_Test.unity --no-banner
```

- [ ] **Step 2: 코스를 놓는다**

12 m/s면 16 m를 1.3초에 지난다. 직선 60 m 이상과 90° 코너 두 개가 필요하다.

| 오브젝트 | 위치 | 스케일 | 비고 |
|---|---|---|---|
| `Floor` | `(0, 0, 0)` | Plane `(10, 1, 10)` = 100×100 m | 무지 바닥. 먼지 없음 |
| `Wall_A` | `(0, 1, 35)` | Cube `(30, 2, 1)` | 직선 끝의 벽 — 코너 1 |
| `Wall_B` | `(15, 1, 20)` | Cube `(1, 2, 30)` | 코너 2 |
| `Wall_C` | `(-20, 1, 0)` | Cube `(1, 2, 40)` | 기준 5(튕김)용 비스듬한 충돌 대상 |
| `DustPanel` | `(0, 0.02, 10)` | 16×16 m 평면 | `M_Dust` + `DustPaintTarget`(마스크 2048) |

`DustPanel`을 코스 전체가 아니라 한 구간에만 까는 이유: 100×100 m에 마스크 하나를 씌우면
텍셀이 5 cm를 넘어 붓 경계가 사라진다(2026-08-12 실측, `InGame/Map/AGENTS.md`). **드리프트
자국을 눈으로 보는 것이 목적이지 전면 청소가 아니다.**

- [ ] **Step 3: 탈것을 놓는다** — `PF_MopVehicle`을 `(0, 0.5, -25)`에 인스턴스로 놓는다

- [ ] **Step 4: 카메라를 놓는다**

`CinemachineBrain`을 `MainCamera`에, `CinemachineCamera` + `CinemachineFollow` +
`CinemachineHardLookAt`을 별도 오브젝트에 둔다. Follow/LookAt은 탈것의 `CamTarget`.

| 필드 | 값 | 근거 |
|---|---|---|
| `BindingMode` | `LockToTargetWithWorldUp` | 차 요를 따라 궤도를 돈다 |
| `FollowOffset` | `(0, 4.5, -8)` | 거리 9, 피치 약 29° — 탑다운이던 청소 모드보다 눕는다 |
| `PositionDamping` | `(0.8, 0.4, 1.2)` | **Z를 가장 늦춰 가속 시 카메라가 처지게 한다.** Y는 조여 출렁임을 막는다 |
| `RotationDamping` | `(0, 2.0, 0)` | Y축만 지연 = 회전 딜레이 |

`Mop/AGENTS.md`가 남긴 결정을 그대로 가져온다 — 직접 짠 추적은 A/D에서 멀미가 났고, 없던 것은
보간이었다. **속도 반응(FOV·거리)은 이번에 넣지 않는다.**

- [ ] **Step 5: 씬을 저장하고 플레이한다**

```bash
$U cmd --project-path "$P" editor_play --no-banner
```

기대: WASD로 차가 움직인다. 콘솔에 `[MopDriveProbe]` 로그가 뜬다.

- [ ] **Step 6: 체크인한다 (구현 2/2)**

프리팹·씬·`.meta`를 전부 이름으로 나열한다. 디렉터리 경로는 `Changed` 파일을 건너뛴다.

---

## Task 5: 튜닝과 판정

여기가 이 브랜치의 본체다. 앞의 넷은 이걸 하기 위한 준비다.

- [ ] **Step 1: 기준 1·2를 잰다**

플레이 모드에서 정지 → W 끝까지 → 최고속 도달 → 손 떼기. 콘솔을 읽는다.

```bash
$U cmd --project-path "$P" get_console_logs --severity Log --limit 30 --no-banner
```

| 기준 | 목표 | 안 맞으면 |
|---|---|---|
| 0 → 최고속 | `0.6 ± 0.1s` | `_accel`을 조정 |
| 최고속 → 정지 | `1.5s` 이내 | `_coastDecel`을 조정 |

- [ ] **Step 2: 기준 3·6을 잰다 (그립 곡선)**

최고속으로 달리다 A를 끝까지 꺾는다. 그 다음 저속에서 같은 것을 한다.

| 기준 | 목표 | 안 맞으면 |
|---|---|---|
| 고속 최대 슬립각 | **30° 이상** | `_gripAtTopSpeed`를 낮춘다 |
| 저속 슬립각 | **5° 미만** | `_gripAtRest`를 높인다 |

**둘은 한 쌍이다.** 하나만 통과하면 곡선의 기울기가 틀린 것이지 값 하나가 틀린 게 아니다.

- [ ] **Step 3: 기준 4를 잰다** — 정지 상태에서 A/D. 경고 로그가 안 뜨면 통과

- [ ] **Step 4: 기준 5를 본다** — `Wall_C`에 비스듬히 들이받는다. 튕겨 나가고 계속 가면 통과.
      멈춰 서면 `linearVelocity`를 어딘가에서 덮어쓰고 있다는 뜻이다

- [ ] **Step 5: 손맛을 판정한다 — 사용자가 한다**

수치가 다 통과해도 크레이지 택시 쪽이 아니면 그립·조향 곡선을 다시 잡는다. **이 판정만은
자동화할 수 없고, 이 브랜치의 진짜 합격 기준이다.**

- [ ] **Step 6: 확정된 값을 코드 기본값으로 구워 넣는다**

인스펙터에서 맞춘 값을 `MopVehicle.cs`의 `[SerializeField]` 초기값에 옮기고, 씬 인스턴스와
프리팹의 오버라이드를 **되돌린다.** 프리팹에 오버라이드를 남기지 않는 이유는 스펙 §0 —
`.prefab`은 머지가 안 되고, 팀원이 같은 시기에 작업 중이다.

- [ ] **Step 7: 리컴파일하고 다시 한 번 잰다** — 기본값으로 구운 뒤에도 같은 수치가 나오는지.
      안 나오면 오버라이드가 남아 있는 것이다

- [ ] **Step 8: 체크인한다 (튜닝)**

---

## Task 6: 문서와 마무리

- [ ] **Step 1: `Mop/AGENTS.md`를 갱신한다**

더할 결정 셋:
1. **대걸레가 탈것이 됐다** — 무엇이 남고(도보 `MopMode`·`MopLocomotion`) 무엇이 새로 왔는지
2. **횡속도를 버리지 않고 감쇠시킨다** — 드리프트·벽 튕김이 같은 줄에서 나오는 이유, 팀원 `VehicleController`와 갈리는 지점
3. **제자리 회전은 의도한 예외다** — 크레이지 택시엔 없지만 구석 청소 때문에 남겼다

`Open`에 더할 것:
- **빠르게 달리면 청소 자국에 줄무늬가 생긴다** (스펙 §7). 다음 단계에서 서브스텝으로 고친다
- **드리프트 중 먼지 밀림 방향이 정면 기준이라 틀린다** (스펙 §7)
- **`MopMode`와 `MopVehicle`이 같은 입력 맵을 각자 켠다.** 한 씬에 같이 두면 안 된다

- [ ] **Step 2: `docs/INDEX.md`에 스펙·계획·세션 요약을 등록하고 현재 상태를 갱신한다**

- [ ] **Step 3: 세션 요약을 쓴다** — `docs/Session_Summary_20260812_mop-driving-feel.md`

남겨야 할 것: 크레이지 택시가 레퍼런스라는 것, 팀원 `Vehicle`이 같은 날 들어와 설계 대상이
한 번 바뀐 것, 따로 간 이유가 `.prefab` 머지 불가라는 것, 합치는 방향이 "주행만 이식"이라는 것.

- [ ] **Step 4: 체크인한다 (문서)**

- [ ] **Step 5: `cm status`로 남은 게 없는지 확인한다**

남은 것이 있으면 **체크인을 하나 더 만들지 말고** 왜 빠뜨렸는지 본다 — 루트 `AGENTS.md`가
"자기 체크인을 고치는 데 체크인을 쓰지 말라"고 정해뒀다.

---

## 체크인 단위

딜리버러블 단위다. 태스크 단위가 아니다.

| # | 내용 | 태스크 |
|---|---|---|
| 1 | 설계 — 스펙 + 이 계획 | — |
| 2 | 주행 코어와 계측기 (C# 둘) | 1, 2 |
| 3 | 탈것과 검증 코스 (프리팹·씬) | 3, 4 |
| 4 | 손맛 확정 (튜닝값) | 5 |
| 5 | 문서 | 6 |

다섯 개. 루트 `AGENTS.md`가 말하는 "4~6이 건강하다"에 든다.
