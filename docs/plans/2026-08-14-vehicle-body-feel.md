# 차량 차체 필링 — 구현 계획

> 스펙: [2026-08-14-vehicle-body-feel.md](../specs/2026-08-14-vehicle-body-feel.md)
> 브랜치: `/main/vehicle-body-feel` (cs:161 기준)

**목표**: 비주얼 차체가 가속·드리프트·충돌에 반응한다. 주행 모델은 한 줄도 안 바뀐다.

**접근**: `BodyPivot`을 새로 끼워 트랜스폼 채널을 가른다 — 우리 스크립트가 피벗의 회전·위치를,
Feel(`MMF_Player`)이 `Body`의 스케일을 소유한다. 상시 성분은 스프링으로 적분하고, 충돌 순간만
Feel이 친다.

**검증 수단**: 유닛 테스트가 아니라 **플레이 모드 실측**이다. 이 프로젝트에는 테스트 어셈블리가
없고, `Tests/` 폴더는 씬을 담는다. 값은 `unity cmd eval`로 읽는다.

---

## 전역 제약

스펙에서 그대로 가져온다. 모든 태스크에 적용된다.

- **네임스페이스는 `PPack` 하나다.** 폴더·어셈블리를 따르지 않는다
- 비공개 필드는 `_camelCase`, 타입·메서드는 `PascalCase`, 열거형은 `E` 접두사
- 직렬화된 Unity Object 필드는 `== null` / `!= null` (fake-null). 그 외에는 `is null`
- `[SerializeField]`를 public 필드보다 우선
- **`VehicleController.cs`·`VehicleCamera.cs`를 한 줄도 고치지 않는다.** 순수 프레젠테이션 레이어다
- **`Assets/Game/InGame/Mop/` 는 건드리지 않는다**
- **차량 루트의 스케일은 `(1,1,1)`을 유지한다.** `BodyPivot`도 `(1,1,1)`이다
- 새 씬을 만들지 않는다. 기존 `Vehicle_Prototype_Test.unity`를 쓰고 **Build Settings에 넣지 않는다**
- 에셋 이동·삭제는 Unity Project 창(또는 CLI)에서만. `.meta`는 자산과 함께 다닌다
- **`PPack.InGame`에서 `MMF_Player`를 타입으로 부르지 않는다.** 컴파일 에러다

### 정규화 기준 — 프리팹에서 읽은 값

`VehicleController`의 튜닝값이다. C# 기본값과 프리팹 값이 **일치하는 것을 확인했다.**

| 필드 | 값 | 쓰는 곳 |
|---|---|---|
| `_accel` | `20` | 피치 정규화 상한 |
| `_brakeDecel` | `30` | 피치 정규화 하한 |
| `_coastDecel` | `12` | (참고) |
| `_baseMaxSpeed` | `12` | (참고) |
| `_boostMaxSpeed` | `16` | 충돌 세기 상한의 근거 |

`Vehicle/AGENTS.md`가 "튜닝 수치는 프리팹에서 읽어라, C# 기본값으로 계산해 정반대 결론을 낸 적이
있다"고 경고한다. 위 표가 그 확인이다.

---

## 파일 구조

| 파일 | 책임 |
|---|---|
| `Vehicle/Scripts/VehicleBodyMotion.cs` (신규) | 상시 스프링 — 피치·롤·킥. `BodyPivot`의 회전·위치만 쓴다 |
| `Vehicle/Scripts/VehicleImpactRelay.cs` (신규) | 충돌 감지 → 킥 호출 + `UnityEvent` 발사. 루트에 붙는다 |
| `Vehicle/Prefabs/PF_VehicleProto.prefab` (수정) | `BodyPivot` 삽입, 컴포넌트 부착, `MMF_Player` 구성, 이벤트 배선 |
| `Vehicle/AGENTS.md` (수정) | 채널 소유 규칙과 Feel 제약을 폴더 규칙으로 남긴다 |
| `docs/INDEX.md` (수정) | 현재 상태 갱신 |
| `docs/Session_Summary_20260814_vehicle-body-feel.md` (신규) | 세션 기록 |

---

## Task 1: `VehicleBodyMotion`

**Files**
- Create: `Assets/Game/InGame/Vehicle/Scripts/VehicleBodyMotion.cs`

**Interfaces**
- Consumes: `VehicleController.IsDrifting`(bool), `VehicleController.IsGrounded`(bool), `Rigidbody.linearVelocity`
- Produces: `public void AddImpulse(float strength01, Vector3 worldDirection)` — Task 2가 호출한다

### Step 1: 파일을 쓴다

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차체 비주얼이 주행에 반응한다 — 가속에 피치, 드리프트에 롤, 충돌에 킥.
    ///
    /// <b>BodyPivot 에 붙고 그 로컬 회전·위치만 쓴다.</b> 스케일은 Feel(<c>MMF_Player</c>)이
    /// 자식 <c>Body</c> 에서 쓰므로 여기서 건드리지 않는다 — 채널 하나에 주인 하나다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleBodyMotion : MonoBehaviour
    {
        [SerializeField] private VehicleController _controller;
        [SerializeField] private Rigidbody _rigidbody;

        [Header("피치 — 가속·제동")]
        [SerializeField] private float _maxPitchDeg = 5f;

        /// <summary><c>VehicleController._accel</c> 과 같은 값이어야 한다.</summary>
        [SerializeField, Min(0.01f)] private float _accelReference = 20f;

        /// <summary><c>VehicleController._brakeDecel</c> 과 같은 값이어야 한다.</summary>
        [SerializeField, Min(0.01f)] private float _brakeReference = 30f;

        [Header("롤 — 드리프트")]
        [SerializeField] private float _maxRollDeg = 8f;

        /// <summary>실측된 드리프트 슬립각. <c>Vehicle/AGENTS.md</c> 참조.</summary>
        [SerializeField, Min(0.01f)] private float _slipReferenceDeg = 45f;

        [SerializeField, Min(0f)] private float _driftRollGain = 1.3f;

        [Header("충돌 킥")]
        [SerializeField, Min(0f)] private float _maxKickDistance = 0.25f;

        [Header("스프링")]
        [SerializeField, Min(0.01f)] private float _frequency = 9f;
        [SerializeField, Range(0f, 2f)] private float _damping = 0.7f;

        private Vector3 _previousVelocity;
        private float _targetPitchDeg;
        private float _targetRollDeg;

        private float _pitchDeg;
        private float _pitchRate;
        private float _rollDeg;
        private float _rollRate;
        private Vector3 _kick;
        private Vector3 _kickRate;

        /// <summary>지금 적용 중인 값. 검증이 읽는다.</summary>
        public float PitchDeg => _pitchDeg;

        /// <summary>지금 적용 중인 값. 검증이 읽는다.</summary>
        public float RollDeg => _rollDeg;

        private void Reset()
        {
            _rigidbody = GetComponentInParent<Rigidbody>();
            _controller = GetComponentInParent<VehicleController>();
        }

        private void Awake()
        {
            if (_rigidbody == null) _rigidbody = GetComponentInParent<Rigidbody>();
            if (_controller == null) _controller = GetComponentInParent<VehicleController>();
            _previousVelocity = _rigidbody != null ? _rigidbody.linearVelocity : Vector3.zero;
        }

        /// <summary>충돌이 준 한 방.</summary>
        /// <param name="strength01">0~1 로 정규화된 세기.</param>
        /// <param name="worldDirection">차체가 밀려야 하는 월드 방향.</param>
        public void AddImpulse(float strength01, Vector3 worldDirection)
        {
            if (worldDirection.sqrMagnitude < 0.0001f) return;

            Vector3 local = transform.parent == null
                ? worldDirection
                : transform.parent.InverseTransformDirection(worldDirection);

            _kickRate += local.normalized * (strength01 * _maxKickDistance * _frequency);
        }

        private void FixedUpdate()
        {
            if (_rigidbody == null) return;

            Vector3 velocity = _rigidbody.linearVelocity;
            Transform root = _rigidbody.transform;
            Vector3 planar = new Vector3(velocity.x, 0f, velocity.z);

            float forwardSpeed = Vector3.Dot(planar, root.forward);
            float previousForwardSpeed = Vector3.Dot(_previousVelocity, root.forward);
            float lateralSpeed = Vector3.Dot(planar, root.right);

            // 충돌 해소가 넣은 속도는 모델의 rate 셋과 무관하게 한 스텝에 크게 튄다. 자르지
            // 않으면 벽에 닿는 순간 피치가 통째로 꺾여, VehicleImpactRelay 의 킥과 같은 사건에
            // 두 번 반응한다.
            float accel = (forwardSpeed - previousForwardSpeed) / Time.fixedDeltaTime;
            accel = Mathf.Clamp(accel, -_brakeReference, _accelReference);
            float accel01 = accel >= 0f ? accel / _accelReference : accel / _brakeReference;

            // +X 회전은 코가 내려가는 방향이다. 가속하면 앞이 들려야 하므로 부호를 뒤집는다.
            _targetPitchDeg = -_maxPitchDeg * accel01;

            // VehicleController.SlipAngle 은 Vector3.Angle 이라 부호가 없다. 어느 쪽으로
            // 미끄러지는지를 모르면 롤 방향을 정할 수 없으므로 여기서 직접 구한다.
            float signedSlipDeg = Mathf.Atan2(lateralSpeed, Mathf.Abs(forwardSpeed)) * Mathf.Rad2Deg;
            float slip01 = Mathf.Clamp(signedSlipDeg / _slipReferenceDeg, -1f, 1f);
            float gain = _controller != null && _controller.IsDrifting ? _driftRollGain : 1f;

            // 바깥으로 기운다. +Z 회전은 위가 왼쪽으로 가는 방향이므로, 오른쪽으로 미끄러질 때
            // (slip01 > 0) 음수여야 오른쪽으로 기운다.
            _targetRollDeg = -_maxRollDeg * slip01 * gain;

            if (_controller != null && !_controller.IsGrounded)
            {
                _targetPitchDeg = 0f;
                _targetRollDeg = 0f;
            }

            _previousVelocity = velocity;
        }

        private void LateUpdate()
        {
            // 물리는 50Hz 인데 화면은 훨씬 빠르다(이 프로젝트 실측 451fps). 물리 값을 그대로
            // 쓰면 한 값을 아홉 프레임 붙잡고 있다가 튄다 — 스프링은 프레임률로 적분한다.
            float dt = Mathf.Min(Time.deltaTime, 0.05f);

            _pitchDeg = Spring(_pitchDeg, _targetPitchDeg, ref _pitchRate, dt);
            _rollDeg = Spring(_rollDeg, _targetRollDeg, ref _rollRate, dt);
            _kick = Spring(_kick, Vector3.zero, ref _kickRate, dt);
            _kick = Vector3.ClampMagnitude(_kick, _maxKickDistance);

            transform.localRotation = Quaternion.Euler(_pitchDeg, 0f, _rollDeg);
            transform.localPosition = _kick;
        }

        private float Spring(float current, float target, ref float rate, float dt)
        {
            float accel = (target - current) * (_frequency * _frequency)
                          - rate * (2f * _damping * _frequency);
            rate += accel * dt;
            return current + rate * dt;
        }

        private Vector3 Spring(Vector3 current, Vector3 target, ref Vector3 rate, float dt)
        {
            Vector3 accel = (target - current) * (_frequency * _frequency)
                            - rate * (2f * _damping * _frequency);
            rate += accel * dt;
            return current + rate * dt;
        }
    }
}
```

### Step 2: 컴파일을 확인한다

```bash
U=~/.unity/bin/unity
P=/Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB
$U cmd --project-path "$P" recompile --no-banner
$U cmd --project-path "$P" recompile_status --no-banner
```

기대: `{"status":"completed","failed":false,"errors":[]}`

---

## Task 2: `VehicleImpactRelay`

**Files**
- Create: `Assets/Game/InGame/Vehicle/Scripts/VehicleImpactRelay.cs`

**Interfaces**
- Consumes: `VehicleBodyMotion.AddImpulse(float, Vector3)` (Task 1)
- Produces: 직렬화된 `UnityEvent _onImpact` — Task 3이 인스펙터에서 `MMF_Player.PlayFeedbacks()`에 건다

### Step 1: 파일을 쓴다

```csharp
using UnityEngine;
using UnityEngine.Events;

namespace PPack
{
    /// <summary>
    /// 벽 충돌을 차체 반응으로 넘긴다. <b>차량 루트에 붙는다</b> — <c>OnCollisionEnter</c> 는
    /// 콜라이더를 가진 오브젝트에서 불리고, <c>Body</c> 에는 콜라이더가 없다.
    ///
    /// 스케일 스쿼시는 Feel 이 <see cref="OnImpact"/> 를 받아 친다. <c>PPack.InGame</c> 에서는
    /// <c>MMF_Player</c> 를 타입으로 부를 수 없다 — <c>Assets/Feel/MMFeedbacks/</c> 에 asmdef 가
    /// 없어 <c>Assembly-CSharp</c> 에 들어가기 때문이다. 그래서 인스펙터에서 건다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class VehicleImpactRelay : MonoBehaviour
    {
        [SerializeField] private VehicleBodyMotion _bodyMotion;

        [Header("문턱 (법선 방향 상대속도, m/s)")]
        [SerializeField, Min(0f)] private float _minImpactSpeed = 3f;
        [SerializeField, Min(0.01f)] private float _maxImpactSpeed = 12f;

        [Header("Feel")]
        [SerializeField] private UnityEvent _onImpact = new UnityEvent();

        /// <summary>충돌이 문턱을 넘으면 발사. 프리팹에서 <c>MMF_Player.PlayFeedbacks()</c> 에 건다.</summary>
        public UnityEvent OnImpact => _onImpact;

        private void Reset()
        {
            _bodyMotion = GetComponentInChildren<VehicleBodyMotion>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision.contactCount == 0) return;

            ContactPoint contact = collision.GetContact(0);

            // 바닥 접촉과 착지는 이번 범위가 아니다(스펙 §8). 법선이 거의 수직이면 바닥이다.
            if (Mathf.Abs(contact.normal.y) > 0.7f) return;

            float closingSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (closingSpeed < _minImpactSpeed) return;

            float strength01 = Mathf.InverseLerp(_minImpactSpeed, _maxImpactSpeed, closingSpeed);

            // 법선은 벽에서 우리 쪽을 가리킨다. 차체는 관성으로 벽 쪽에 계속 밀렸다가 돌아오므로
            // 반대 방향이다.
            if (_bodyMotion != null) _bodyMotion.AddImpulse(strength01, -contact.normal);

            _onImpact.Invoke();
        }
    }
}
```

### Step 2: 컴파일을 확인한다

Task 1 Step 2와 같은 명령. 기대도 같다.

---

## Task 3: 프리팹 수술

**Files**
- Modify: `Assets/Game/InGame/Vehicle/Prefabs/PF_VehicleProto.prefab`

**왜 `eval_file` 인가.** `BodyPivot` 삽입 · 컴포넌트 부착 · `MMF_Player` 구성 · `UnityEvent`
배선이 한 트랜잭션이어야 한다. 그리고 **`eval` 은 asmdef 밖에서 Roslyn 으로 컴파일되므로
`MMF_Player` 를 직접 참조할 수 있다** — 우리 스크립트가 못 하는 일을 에디터 스크립트는 한다.

### Step 1: 수술 스크립트를 쓴다

`$CLAUDE_JOB_DIR/tmp/body_pivot_surgery.cs` 에 쓴다. **프로젝트 안에 두지 않는다** — 일회성이다.

```csharp
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using UnityEngine.Events;
using UnityEditor;
using UnityEditor.Events;
using MoreMountains.Feedbacks;
using PPack;

const string PrefabPath = "Assets/Game/InGame/Vehicle/Prefabs/PF_VehicleProto.prefab";

GameObject root = PrefabUtility.LoadPrefabContents(PrefabPath);
try
{
    Transform body = root.transform.Find("Body");
    if (body == null) { Debug.LogError("[surgery] Body 없음"); return; }

    // 1. BodyPivot 을 루트와 Body 사이에 끼운다. 피벗은 항상 스케일 1 이다.
    Transform pivot = root.transform.Find("BodyPivot");
    if (pivot == null)
    {
        var pivotGo = new GameObject("BodyPivot");
        pivot = pivotGo.transform;
        pivot.SetParent(root.transform, false);
        pivot.SetSiblingIndex(body.GetSiblingIndex());
    }
    pivot.localPosition = Vector3.zero;
    pivot.localRotation = Quaternion.identity;
    pivot.localScale = Vector3.one;

    Vector3 bodyScale = body.localScale;
    body.SetParent(pivot, false);
    body.localPosition = Vector3.zero;
    body.localRotation = Quaternion.identity;
    body.localScale = bodyScale;   // (1.8, 0.9, 4) 를 보존한다

    // 2. 우리 컴포넌트 둘
    var motion = pivot.GetComponent<VehicleBodyMotion>() ?? pivot.gameObject.AddComponent<VehicleBodyMotion>();
    var relay = root.GetComponent<VehicleImpactRelay>() ?? root.AddComponent<VehicleImpactRelay>();

    var motionSo = new SerializedObject(motion);
    motionSo.FindProperty("_controller").objectReferenceValue = root.GetComponent<VehicleController>();
    motionSo.FindProperty("_rigidbody").objectReferenceValue = root.GetComponent<Rigidbody>();
    motionSo.ApplyModifiedPropertiesWithoutUndo();

    var relaySo = new SerializedObject(relay);
    relaySo.FindProperty("_bodyMotion").objectReferenceValue = motion;
    relaySo.ApplyModifiedPropertiesWithoutUndo();

    // 3. Feel — Body 에 MMF_Player 와 스쿼시 하나
    var player = body.GetComponent<MMF_Player>() ?? body.gameObject.AddComponent<MMF_Player>();
    if (player.FeedbacksList == null) player.FeedbacksList = new List<MMF_Feedback>();
    if (!player.FeedbacksList.Any(f => f is MMF_SquashAndStretch))
    {
        var squash = new MMF_SquashAndStretch
        {
            SquashAndStretchTarget = body,
            Mode = MMF_SquashAndStretch.Modes.Absolute,
            Axis = MMF_SquashAndStretch.PossibleAxis.ZtoXY,
            AnimateScaleDuration = 0.2f,
        };
        player.AddFeedback(squash);
    }

    // 4. 배선 — 여기가 asmdef 를 건너뛰는 지점이다.
    //    먼저 비운다. 이 스크립트는 여러 번 돌 수 있고, 안 비우면 리스너가 쌓여
    //    한 번 박을 때 스쿼시가 두 번 세 번 터진다.
    var evt = relay.OnImpact;
    for (int i = evt.GetPersistentEventCount() - 1; i >= 0; i--)
    {
        UnityEventTools.RemovePersistentListener(evt, i);
    }
    UnityEventTools.AddVoidPersistentListener(evt, new UnityAction(player.PlayFeedbacks));
    EditorUtility.SetDirty(relay);

    PrefabUtility.SaveAsPrefabAsset(root, PrefabPath);
    Debug.Log($"[surgery] 완료 — Body.localScale={body.localScale}, persistent={evt.GetPersistentEventCount()}");
}
finally
{
    PrefabUtility.UnloadPrefabContents(root);
}
```

### Step 2: 실행한다

```bash
U=~/.unity/bin/unity
P=/Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB
$U cmd --project-path "$P" eval_file --file "$CLAUDE_JOB_DIR/tmp/body_pivot_surgery.cs" --timeout 120 --no-banner
```

기대: `[surgery] 완료 — Body.localScale=(1.80, 0.90, 4.00), persistent=1`

**`persistent=1` 이 아니면 배선이 안 된 것이다.** 그러면 충돌해도 스쿼시가 안 뜬다.

### Step 3: 저장된 결과를 다시 읽어 확인한다

```bash
$U cmd --project-path "$P" eval --timeout 60 --no-banner --code '
var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/InGame/Vehicle/Prefabs/PF_VehicleProto.prefab");
var pivot = go.transform.Find("BodyPivot");
var body = pivot.Find("Body");
Debug.Log($"pivot scale={pivot.localScale} body scale={body.localScale} " +
          $"motion={pivot.GetComponent<PPack.VehicleBodyMotion>()!=null} " +
          $"relay={go.GetComponent<PPack.VehicleImpactRelay>()!=null} " +
          $"player={body.GetComponent<MoreMountains.Feedbacks.MMF_Player>()!=null} " +
          $"listeners={go.GetComponent<PPack.VehicleImpactRelay>().OnImpact.GetPersistentEventCount()}");
'
```

기대: `pivot scale=(1.00, 1.00, 1.00) body scale=(1.80, 0.90, 4.00) motion=True relay=True player=True listeners=1`

**디스크에서 다시 읽는 것이 요점이다.** 메모리의 객체는 저장 실패해도 멀쩡해 보인다 — 이
프로젝트에 그 전례가 있다(`AudioSource.m_audioClip` 이 성공을 반환하고 값은 `null` 로 남은 건).

### Step 4: 노즐이 안 밀렸는지 확인한다

```bash
$U cmd --project-path "$P" eval --timeout 60 --no-banner --code '
var go = UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Game/InGame/Vehicle/Prefabs/PF_VehicleProto.prefab");
var n = go.transform.Find("Nozzle");
Debug.Log($"nozzle localPos={n.localPosition} parent={n.parent.name}");
'
```

기대: `parent=PF_VehicleProto`. **노즐은 형제로 남아야 한다.** 피벗의 자식이 되면 차체가
기울 때마다 흡입구가 같이 흔들리고, 원뿔 판정이 연출을 따라간다.

---

## Task 4: 플레이 모드 검증

**Files**: 없음 (읽기만)

### Step 1: 씬을 열고 플레이한다

```bash
$U cmd --project-path "$P" open_scene --path "Assets/Game/InGame/Vehicle/Tests/Vehicle_Prototype_Test.unity" --no-banner
$U cmd --project-path "$P" editor_play --no-banner
```

### Step 2: 정지 상태의 기준값을 읽는다

```bash
$U cmd --project-path "$P" eval --timeout 60 --no-banner --code '
var m = Object.FindFirstObjectByType<PPack.VehicleBodyMotion>();
Debug.Log($"[rest] pitch={m.PitchDeg:F3} roll={m.RollDeg:F3} pos={m.transform.localPosition} " +
          $"bodyScale={m.transform.GetChild(0).localScale}");
'
```

기대: `pitch≈0 roll≈0 pos≈(0,0,0) bodyScale=(1.80, 0.90, 4.00)`

### Step 3: 주행시켜 피치·롤을 확인한다

`VehicleDriveAutopilot`으로 가속·드리프트를 시킨 뒤 같은 값을 샘플링한다.

> **오토파일럿은 유니티 앱이 OS 최상위여야 동작한다.** `InputSettings.backgroundBehavior` 가
> `ResetAndDisableNonBackgroundDevices` 라, 포커스를 잃으면 합성 입력이 버려진다. 단계 로그는
> 다 찍히는데 차만 제자리여서 **모델이 고장난 것처럼 보인다** — 이 프로젝트에서 실제로 그렇게
> 오진한 적이 있다. 자동화가 안 되면 사용자에게 직접 몰아달라고 요청한다.

| 확인 | 통과 기준 |
|---|---|
| 가속 중 `PitchDeg` | 음수 (코가 들림), 정지하면 0으로 복귀 |
| 제동 중 `PitchDeg` | 양수 (코가 박힘) |
| 좌회전 드리프트 중 `RollDeg` | 한쪽 부호로 일관, 우회전에서 반대 부호 |
| `Body.localScale` | 항상 `(1.8, 0.9, 4)` — 충돌 전에는 안 변해야 한다 |

### Step 4: 벽에 박아 스쿼시를 확인한다

충돌 직후 몇 프레임을 샘플링해 `Body.localScale`이 변했다가 **정확히 `(1.8, 0.9, 4)`로 돌아오는지**
본다. 미세하게 안 돌아오면 충돌마다 누적되어 한참 뒤에 "차가 언제부터인가 납작하다"로 발견된다.

### Step 5: 주행 회귀를 확인한다 — 여기가 진짜 판정이다

`VehicleDriveProbe`로 재고 `get_console_logs`로 읽는다. **아래가 변하면 실패다.**

| | 기준값 |
|---|---|
| 0→최고속 | `0.58s` |
| 코스팅 정지 | `0.96s` |
| 평소 슬립각 | `0.0°` |
| 드리프트 슬립각 | `45.0°` |

순수 비주얼 레이어이므로 변할 이유가 없다. 변했다면 물리에 손댄 것이다.

### Step 6: 스크린샷

```bash
$U cmd --project-path "$P" screenshot --view Game --output "docs/images/verify/vehicle_body_feel.png" --no-banner
```

**플레이 모드에서만 찍는다.** 에디트 모드 Game 뷰는 리페인트가 안 걸려 캡처가 바이트 단위로
동일하게 나오고, 그 위의 결론이 전부 틀린다. md5 비교로도 안 걸러진다.

### Step 7: 에디터를 원상복구한다

루트 `AGENTS.md` §5. `editor_stop`, 원래 씬 복귀, 검증용 오브젝트 제거, dirty 없음 확인.

---

## Task 5: 튜닝

**Files**
- Modify: `Assets/Game/InGame/Vehicle/Scripts/VehicleBodyMotion.cs` (기본값만)

Task 4에서 숫자는 맞는데 **손맛이 안 나는** 부분을 잡는다. 판정은 사용자가 한다.

시작값과 만질 순서:

| 값 | 시작 | 증상 → 방향 |
|---|---|---|
| `_maxPitchDeg` | `5` | 밋밋하면 올리고, 배멀미 나면 내린다 |
| `_maxRollDeg` | `8` | 드리프트가 안 티나면 올린다 |
| `_frequency` | `9` | 낮추면 물렁하고, 올리면 딱딱하다 |
| `_damping` | `0.7` | 낮추면 출렁이며 여러 번 튄다 (크레이지 택시는 낮은 쪽) |
| `_maxKickDistance` | `0.25` | 충돌이 약하면 올린다 |

**확정된 값은 C# 기본값으로 굽는다.** 프리팹에 오버라이드를 남기면 다음 사람이 두 곳을 봐야
한다 — `mop-driving-feel` 계획이 정한 선례다.

---

## Task 6: 문서

**Files**
- Modify: `Assets/Game/InGame/Vehicle/AGENTS.md`
- Modify: `docs/INDEX.md`
- Create: `docs/Session_Summary_20260814_vehicle-body-feel.md`

`Vehicle/AGENTS.md`에 남길 것 — **다음 사람이 모르면 조용히 깨지는 것만** 적는다.

- `BodyPivot`은 회전·위치, `Body`는 스케일. **채널 하나에 주인 하나.** 우리 스크립트가 `Body`의
  스케일을 만지면 Feel의 `_initialScale` 캡처와 싸운다
- `MMF_Player`는 `Assembly-CSharp`이라 `PPack.InGame`에서 타입으로 부를 수 없다. `UnityEvent`로만 건다
- `VehicleController.SlipAngle`은 **부호가 없다.** 방향이 필요하면 속도에서 직접 구한다
- 노즐은 피벗의 자식이 아니라 형제다

---

## 체크인 계획

`AGENTS.md`의 "**체크인은 태스크가 아니라 딜리버러블 단위**"를 따른다. 이 스킬의 기본값인
"태스크마다 커밋"은 쓰지 않는다.

| # | 내용 | 시점 |
|---|---|---|
| 1 | **설계** — 스펙 + 계획 | 구현 전 (지금) |
| 2 | **구현** — 스크립트 둘 + 프리팹 + `.meta` | Task 5 통과 후 |
| 3 | **문서** — `Vehicle/AGENTS.md`, `INDEX.md`, 세션 요약 | 마지막 |

**경로 목록을 먼저 완성하고 한 번에 넣는다.** 두 함정이 첫 시도를 불완전하게 만든다.

- **디렉터리 경로는 안에 있는 `Changed` 파일을 건너뛴다.** 수정된 파일은 이름을 따로 대고,
  **먼저 `cm checkout` 해야 한다.** 에디터 밖에서 편집한 파일이 정확히 이 상태다
- **`.meta`는 자산을 따라가지 않는다.** 새 스크립트 둘은 `.cs`와 `.cs.meta`를 둘 다 댄다
- 유니티 밖에서 만든 파일은 `Private`이다. `cm add -R` 먼저, 그 다음 `cm status --private` 확인

**자기 체크인을 고치는 체크인을 쓰지 않는다.** `/main/contamination-variants`가 9개 중 2개를
그렇게 썼다 — 절차 오류지 이력이 아니다.
