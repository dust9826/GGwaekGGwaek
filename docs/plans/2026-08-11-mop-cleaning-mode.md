# 대걸레 청소 조작 구현 계획

> **에이전트용:** 태스크 단위로 실행한다. 스텝은 `- [ ]` 체크박스다.
> **스펙:** `docs/specs/2026-08-11-mop-cleaning-mode.md` — 결정의 근거는 전부 거기 있다.

**목표:** 좌클릭으로 청소 모드에 들어가 탑다운 시점·탱크 조작으로 밀고 다니면 지나간 자리가 닦인다.

**아키텍처:** 양 끝(`Dust`의 붓질 API, `Player`의 로코모션·카메라)은 이미 있다. 이번에 만드는 것은 **이음매**뿐이다. 청소 모드는 팀원 컴포넌트 셋을 끄고 우리 셋을 켜는 것으로 성립하며, 팀원 파일은 한 줄도 고치지 않는다.

**기술 스택:** Unity 6000.6.0b7 · URP 17.6.0 · Input System 1.20.0 · Plastic SCM · Unity CLI(`~/.unity/bin/unity`)

## 전역 제약

- **브랜치**: `/main/mop-cleaning-mode`. `/main`에서 작업하지 않는다
- **버전 관리는 Plastic이다. git 명령을 쓰지 않는다.** 체크인 **전에** 경로 목록을 완성한다 — 디렉터리 경로는 안의 `Changed`를 건너뛰고 `.meta`는 에셋을 따라가지 않는다(루트 `AGENTS.md`)
- **체크인은 딜리버러블 단위다.** 이 계획은 태스크 6개지만 체크인은 **3번**이다 — 아래 각 태스크의 "체크인 묶음" 표시를 따른다
- **`Assets/Game/InGame/Player/` 아래 파일을 수정하지 않는다.** 이것이 설계 목표다. 고쳐야 할 것 같으면 멈추고 보고한다
- **`.unity` / `.prefab` / `.asset` / `.meta` YAML을 손으로 편집하지 않는다.** Unity 에디터나 Unity CLI로 만든다
- 네임스페이스는 평평한 `PPack` 하나. private 필드 `_camelCase`, 열거형만 `E` 접두사
- 직렬화 Unity 오브젝트 필드는 `== null` / `!= null`(가짜 null)
- 테스트 씬은 `Mop/Tests/`에 두고 **Build Settings에 넣지 않는다**
- **작업 전 에디터 상태를 기록하고 끝나면 복원한다**(루트 `AGENTS.md` §5)

## 사전 확인

`Mop/`은 `Assets/Game/InGame/` 아래라 `PPack.InGame.asmdef`가 이미 덮는다. **asmdef 변경이 없다.** 확인:

```bash
ls Assets/Game/InGame/PPack.InGame.asmdef && ls -d Assets/Game/InGame/Mop
```

---

## 이미 있는 API — 이 계획이 호출하는 것 전부

건드리지 않고 부르기만 한다.

```csharp
// InGame/Dust/Scripts/BrushPad.cs
public readonly struct BrushPad
{
    public BrushPad(Vector3 position, Quaternion rotation, Vector2 halfExtents,
                    float thickness, float feather, float strength,
                    float unevenness, float unevennessScale);
}

// InGame/Dust/Scripts/DustPaintTarget.cs
public void Paint(in BrushPad pad);
public void CaptureErased(RenderTexture target, in BrushPad pad);

// InGame/Dust/Scripts/DustCleanVfx.cs
public RenderTexture ErasedMap { get; }
public void BeginFrame();
public void Play(Vector3 padCenter, Vector3 travelDirection);
```

`Play`는 **인자가 둘**이다. 하나로 부르면 컴파일되지 않는다.

## 파일 구조

```
Assets/Game/InGame/Mop/
  AGENTS.md                          수정 — 좌클릭 토글 결정, "hold" 문장 정정
  Input/
    MopControls.inputactions      신규 — Cleaning 맵 + ToolToggle 맵
    MopControls.cs                생성물 — Generate C# Class 산출
  Scripts/
    MopMode.cs                    신규 — 토글, 컴포넌트 교대
    MopLocomotion.cs              신규 — W 전진 / AD 회전
    MopCamera.cs                  신규 — 탑다운 추적
    MopPad.cs                    신규 — 패드 생성과 붓질
  Tests/
    Mop_Cleaning_Test.unity       신규 — PF_Player + 오염 바닥
```

---

## Task 1: 입력 자산 — `MopControls`

**체크인 묶음 A** (Task 1·2·3 을 한 번에 체크인)

**Files:**
- Create: `Assets/Game/InGame/Mop/Input/MopControls.inputactions`

**Interfaces:**
- Consumes: 없음
- Produces: 생성 클래스 `PPack.MopControls`. 접근 경로는 `controls.Cleaning.Move`(Vector2), `controls.ToolToggle.Toggle`(Button). 각 맵은 `controls.Cleaning.Enable()/Disable()`

- [ ] **Step 1: 작업 전 에디터 상태를 기록한다**

```bash
~/.unity/bin/unity status
~/.unity/bin/unity cmd list_open_scenes --project-path .
```

열린 씬·활성 씬·dirty 여부·Play Mode를 적어둔다. Task 6 끝에서 이 상태로 되돌린다.

- [ ] **Step 2: Project 창에서 입력 자산을 만든다**

`Assets/Game/InGame/Mop/Input/` 폴더를 만들고 우클릭 → Create → Input Actions → 이름 `MopControls`.

- [ ] **Step 3: 맵과 액션을 정확히 이대로 넣는다**

| 맵 | 액션 | 타입 | 바인딩 |
|---|---|---|---|
| `Cleaning` | `Move` | Value / Vector2 | 2D Vector 컴포짓 — Up `<Keyboard>/w`, Down `<Keyboard>/s`, Left `<Keyboard>/a`, Right `<Keyboard>/d` |
| `ToolToggle` | `Toggle` | Button | `<Mouse>/leftButton` |

맵 이름을 정확히 `Cleaning`·`ToolToggle`로 둔다 — 생성 클래스의 프로퍼티 이름이 여기서 나오고 Task 2가 그 이름을 부른다.

- [ ] **Step 4: C# 생성을 켠다**

자산을 선택하고 Inspector에서 **Generate C# Class** 체크 → Apply. `MopControls.cs`가 옆에 생긴다.

- [ ] **Step 5: 컴파일과 이름을 확인한다**

```bash
~/.unity/bin/unity cmd recompile --project-path . --focus true
~/.unity/bin/unity cmd recompile_status --project-path .
grep -nE "public .*(Cleaning|ToolToggle)Actions|struct .*Actions" Assets/Game/InGame/Mop/Input/MopControls.cs | head
```

기대: 에러 0건, `CleaningActions`와 `ToolToggleActions`가 보인다. 안 보이면 맵 이름 오타다.

---

## Task 2: 모드 전환 — `MopMode`

**체크인 묶음 A**

**Files:**
- Create: `Assets/Game/InGame/Mop/Scripts/MopMode.cs`

**Interfaces:**
- Consumes: `PPack.MopControls` (Task 1)
- Produces: `MopMode.IsCleaning` (bool), `MopMode.Controls` (`MopControls`, 청소 모드 컴포넌트가 `Cleaning.Move`를 읽는 통로)

- [ ] **Step 1: 스크립트를 쓴다**

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 청소 모드의 유일한 진실. 좌클릭으로 토글하고, 평상시 컴포넌트와 청소용 컴포넌트를 교대시킨다.
    ///
    /// 팀원 파일(<c>InGame/Player/</c>)을 고치지 않기 위해 통째로 끄고 우리가 몬다.
    /// <see cref="InputReader"/> 를 끄면 그 OnDisable 이 Player 액션 맵까지 함께 끈다.
    ///
    /// Fusion 이 오면 <see cref="IsCleaning"/> 이 [Networked] 로 승격될 자리다 — 다른
    /// 컴포넌트는 이것을 읽기만 한다.
    /// </summary>
    public sealed class MopMode : MonoBehaviour
    {
        [Header("평상시 — 청소 중에는 끈다")]
        [SerializeField] private InputReader _inputReader;
        [SerializeField] private PlayerAnimationController _playerLocomotion;
        [SerializeField] private PlayerCameraController _playerCamera;

        [Header("청소 중 — 평상시에는 끈다")]
        [SerializeField] private MopLocomotion _mopLocomotion;
        [SerializeField] private MopCamera _cleaningCameraRig;
        [SerializeField] private MopPad _mopPad;

        private MopControls _controls;

        public bool IsCleaning { get; private set; }

        /// <summary>청소용 컴포넌트가 Cleaning 맵을 읽는 통로.</summary>
        public MopControls Controls => _controls;

        private void Awake()
        {
            _controls = new MopControls();
            SetCleaning(false);
        }

        private void OnEnable()
        {
            _controls.ToolToggle.Enable();
            _controls.ToolToggle.Toggle.performed += OnTogglePerformed;
        }

        private void OnDisable()
        {
            _controls.ToolToggle.Toggle.performed -= OnTogglePerformed;
            _controls.ToolToggle.Disable();
            _controls.Cleaning.Disable();
        }

        private void OnDestroy()
        {
            _controls.Dispose();
        }

        private void OnTogglePerformed(UnityEngine.InputSystem.InputAction.CallbackContext context)
        {
            SetCleaning(!IsCleaning);
        }

        private void SetCleaning(bool cleaning)
        {
            IsCleaning = cleaning;

            // 평상시 쪽. InputReader 를 끄면 Player 액션 맵도 같이 꺼진다.
            if (_inputReader != null) _inputReader.enabled = !cleaning;
            if (_playerLocomotion != null) _playerLocomotion.enabled = !cleaning;
            if (_playerCamera != null) _playerCamera.enabled = !cleaning;

            // 청소 쪽.
            if (_mopLocomotion != null) _mopLocomotion.enabled = cleaning;
            if (_cleaningCameraRig != null) _cleaningCameraRig.enabled = cleaning;
            if (_mopPad != null) _mopPad.enabled = cleaning;

            if (cleaning) _controls.Cleaning.Enable();
            else _controls.Cleaning.Disable();
        }
    }
}
```

- [ ] **Step 2: 컴파일을 확인한다**

```bash
~/.unity/bin/unity cmd recompile --project-path . --focus true
~/.unity/bin/unity cmd recompile_status --project-path .
```

기대: `MopLocomotion`·`MopCamera`·`MopPad` 이 아직 없으므로 **CS0246 세 건**. 정상이다. Task 3~5가 채운다.

---

## Task 3: 탱크 조작 — `MopLocomotion`

**체크인 묶음 A**

**Files:**
- Create: `Assets/Game/InGame/Mop/Scripts/MopLocomotion.cs`

**Interfaces:**
- Consumes: `MopMode.Controls.Cleaning.Move` (Vector2)
- Produces: 없음 (`CharacterController` 를 직접 움직인다)

- [ ] **Step 1: 스크립트를 쓴다**

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 청소 모드의 탱크 조작. W/S 가 전후진, A/D 가 제자리 회전이다.
    ///
    /// 평상시 로코모션(<c>PlayerAnimationController</c>)은 카메라 기준으로 움직이지만 청소 중에는
    /// 시점이 탑다운이라 카메라 기준이 성립하지 않는다. 그래서 캐릭터 자기 정면을 기준으로 민다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class MopLocomotion : MonoBehaviour
    {
        [SerializeField] private MopMode _mode;

        [Tooltip("전진 속도. m/s.")]
        [SerializeField, Min(0f)] private float _moveSpeed = 2.2f;
        [Tooltip("제자리 회전 속도. 도/초.")]
        [SerializeField, Min(0f)] private float _turnSpeed = 140f;
        [Tooltip("접지 유지용 하향 가속. 없으면 경사면에서 뜬다.")]
        [SerializeField] private float _gravity = -20f;

        private CharacterController _controller;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void Update()
        {
            if (_mode == null) return;

            Vector2 move = _mode.Controls.Cleaning.Move.ReadValue<Vector2>();

            transform.Rotate(Vector3.up, move.x * _turnSpeed * Time.deltaTime, Space.World);

            _verticalVelocity = _controller.isGrounded
                ? -1f                                        // 접지 시 살짝 눌러 붙인다
                : _verticalVelocity + _gravity * Time.deltaTime;

            Vector3 velocity = transform.forward * (move.y * _moveSpeed);
            velocity.y = _verticalVelocity;

            _controller.Move(velocity * Time.deltaTime);
        }
    }
}
```

- [ ] **Step 2: 컴파일을 확인한다**

```bash
~/.unity/bin/unity cmd recompile --project-path . --focus true
~/.unity/bin/unity cmd recompile_status --project-path .
```

기대: 남은 에러가 `MopCamera`·`MopPad` 두 건으로 줄었다.

---

## Task 4: 탑다운 카메라 — `MopCamera`

**체크인 묶음 B** (Task 4·5 를 한 번에 체크인)

**Files:**
- Create: `Assets/Game/InGame/Mop/Scripts/MopCamera.cs`

**Interfaces:**
- Consumes: 없음
- Produces: 없음 (카메라 트랜스폼을 직접 움직인다)

- [ ] **Step 1: 스크립트를 쓴다**

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 청소 모드의 탑다운 카메라. 대상 위에서 내려다보며 따라간다.
    ///
    /// 팀원의 <c>PlayerCameraController</c> 는 피치가 (-10, 45) 로 잘려 있고 관련 필드가 전부
    /// private 이라 밖에서 60~70 도를 만들 수 없다. 그 파일을 고치는 대신 청소 중에만 그것을 끄고
    /// 같은 카메라 트랜스폼을 여기서 몬다.
    ///
    /// 요 는 절대 회전을 매 프레임 새로 쓴다는 것이다 — 그래야 평상시 카메라가 남긴 각도가 섞이지
    /// 않는다.
    /// </summary>
    public sealed class MopCamera : MonoBehaviour
    {
        [Tooltip("따라갈 대상. 보통 캐릭터 트랜스폼.")]
        [SerializeField] private Transform _target;
        [Tooltip("움직일 카메라. PF_SyntyCamera 아래의 MainCamera.")]
        [SerializeField] private Transform _camera;

        [Tooltip("내려다보는 각도. 90 이 완전한 수직이다.")]
        [SerializeField, Range(45f, 80f)] private float _pitch = 65f;
        [Tooltip("대상에서 카메라까지 거리.")]
        [SerializeField, Min(1f)] private float _distance = 7f;
        [Tooltip("대상 기준 상하 오프셋. 캐릭터를 화면 아래쪽에 두려면 올린다.")]
        [SerializeField] private float _heightOffset = 0.5f;
        [Tooltip("따라붙는 부드러움. 클수록 즉각적이다.")]
        [SerializeField, Min(0.01f)] private float _followSharpness = 12f;

        private void OnEnable()
        {
            // 켜지는 순간 목표 지점으로 즉시 이동한다. 보간으로 들어오면 첫 프레임에 화면이 쓸린다.
            if (_target == null || _camera == null) return;
            Apply(1f);
        }

        private void LateUpdate()
        {
            if (_target == null || _camera == null) return;
            Apply(1f - Mathf.Exp(-_followSharpness * Time.deltaTime));
        }

        private void Apply(float t)
        {
            Quaternion rotation = Quaternion.Euler(_pitch, _target.eulerAngles.y, 0f);
            Vector3 focus = _target.position + Vector3.up * _heightOffset;
            Vector3 desired = focus - rotation * Vector3.forward * _distance;

            _camera.position = Vector3.Lerp(_camera.position, desired, t);
            _camera.rotation = Quaternion.Slerp(_camera.rotation, rotation, t);
        }
    }
}
```

- [ ] **Step 2: 컴파일을 확인한다**

기대: 남은 에러가 `MopPad` 한 건이다.

---

## Task 5: 패드와 청소 — `MopPad`

**체크인 묶음 B**

**Files:**
- Create: `Assets/Game/InGame/Mop/Scripts/MopPad.cs`

**Interfaces:**
- Consumes: `BrushPad`, `DustPaintTarget.CaptureErased/Paint`, `DustCleanVfx.BeginFrame/ErasedMap/Play`
- Produces: 없음

- [ ] **Step 1: 스크립트를 쓴다**

```csharp
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 청소 패드. 플레이어 앞 고정 오프셋에서 바닥으로 레이캐스트해 표면에 붙은 사각 패드를 만들고,
    /// 그 패드로 <see cref="DustPaintTarget"/> 을 지운다.
    ///
    /// 여기는 마스크를 모른다 — <see cref="BrushPad"/> 를 채워 건넬 뿐이다.
    /// (<c>Mop/AGENTS.md</c>: 흡입은 여기, 무엇이 빨려드는지는 Dust/Trash/Insects)
    ///
    /// <see cref="DustCleanVfx"/> 를 소유하는 쪽이 붓질을 미는 쪽이어야 Fusion 이 왔을 때 원격
    /// 플레이어의 도구도 자기 RT 를 갖는다.
    /// </summary>
    public sealed class MopPad : MonoBehaviour
    {
        [Tooltip("패드를 매달 기준. 보통 캐릭터 트랜스폼.")]
        [SerializeField] private Transform _origin;
        [Tooltip("청소 VFX. 붙어 있지 않아도 붓질은 동작한다.")]
        [SerializeField] private DustCleanVfx _vfx;

        [Header("패드 위치")]
        [Tooltip("기준 기준 로컬 오프셋. 발밑을 벗어나되 팔이 닿는 거리.")]
        [SerializeField] private Vector3 _localOffset = new Vector3(0f, 0f, 0.8f);
        [Tooltip("바닥을 찾는 레이의 시작 높이와 길이.")]
        [SerializeField, Min(0.1f)] private float _rayUp = 1f;
        [SerializeField, Min(0.1f)] private float _rayLength = 3f;
        [SerializeField] private LayerMask _layers = ~0;

        [Header("붓 — DustMousePainter 의 튜닝값")]
        [SerializeField] private Vector2 _halfExtents = new Vector2(0.5f, 0.15f);
        [SerializeField, Min(0.01f)] private float _thickness = 0.25f;
        [SerializeField, Min(0.001f)] private float _feather = 0.06f;
        [SerializeField, Range(0.002f, 1f)] private float _strength = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _unevenness = 0.55f;
        [SerializeField, Min(0.01f)] private float _unevennessScale = 6f;
        [SerializeField, Range(0f, 1f)] private float _evenOutWithStrength = 0.65f;

        private void Update()
        {
            if (_origin == null) return;

            Vector3 padXZ = _origin.TransformPoint(_localOffset);
            Ray ray = new Ray(padXZ + Vector3.up * _rayUp, Vector3.down);

            if (!Physics.Raycast(ray, out RaycastHit hit, _rayUp + _rayLength, _layers))
            {
                return;   // 공중이거나 구멍. 이 프레임은 붓질하지 않는다.
            }

            // 패드 평면은 표면 노멀에 눕고, 진행 방향은 캐릭터 정면을 그 평면에 투영한 것이다.
            Vector3 forward = Vector3.ProjectOnPlane(_origin.forward, hit.normal);
            if (forward.sqrMagnitude < 1e-6f) return;
            Quaternion padRotation = Quaternion.LookRotation(forward.normalized, hit.normal);

            // 약하게 밀면 얼룩이 남고 세게 밀면 고르게 닦인다.
            float unevenness = _unevenness * Mathf.Lerp(1f, 1f - _evenOutWithStrength, _strength);

            BrushPad pad = new BrushPad(hit.point, padRotation, _halfExtents,
                                        _thickness, _feather, _strength,
                                        unevenness, _unevennessScale);

            // 이 묶음이 프레임당 한 번 도는 자리에 있어야 한다. Fusion 이 오면 그대로 Render() 로
            // 옮겨간다 — FixedUpdateNetwork 에 두면 재시뮬레이션마다 중복으로 지워진다.
            if (_vfx != null) _vfx.BeginFrame();

            // 붓은 맞은 콜라이더가 속한 대상만 지운다. 벽 너머가 지워지지 않는다.
            if (hit.collider.TryGetComponent(out DustPaintTarget target))
            {
                // 순서가 중요하다. CaptureErased 는 빼기 전 마스크를 읽어야 한다.
                if (_vfx != null) target.CaptureErased(_vfx.ErasedMap, pad);
                target.Paint(pad);
            }

            if (_vfx != null) _vfx.Play(hit.point, forward.normalized);
        }
    }
}
```

- [ ] **Step 2: 컴파일을 확인한다**

```bash
~/.unity/bin/unity cmd recompile --project-path . --focus true
~/.unity/bin/unity cmd recompile_status --project-path .
~/.unity/bin/unity cmd get_console_logs --project-path . --severity Error --limit 20
```

기대: **에러 0건.** 네 스크립트가 다 있으므로 Task 2의 CS0246이 사라진다.

---

## Task 6: 테스트 씬 · 검증 · 문서

**체크인 묶음 C**

**Files:**
- Create: `Assets/Game/InGame/Mop/Tests/Mop_Cleaning_Test.unity`
- Modify: `Assets/Game/InGame/Mop/AGENTS.md`
- Modify: `docs/INDEX.md`

**Interfaces:**
- Consumes: Task 1~5 전부
- Produces: 없음

- [ ] **Step 1: 씬을 만들고 오염 바닥을 깐다**

```bash
~/.unity/bin/unity cmd create_scene --project-path . --path "Game/InGame/Mop/Tests/Mop_Cleaning_Test.unity"
```

빈 씬으로 생성되므로 카메라·조명이 없다. 조명을 만들 때 **`add_component --type Light` 는 Point 라이트를 만든다** — 이름만 Directional로 짓지 말고 타입을 바꾼다:

```bash
U=~/.unity/bin/unity
$U cmd create_gameobject --project-path . --name "Directional Light"
$U cmd add_component --project-path . --target "/Directional Light" --type Light
$U cmd set_component_properties --project-path . --target "/Directional Light" --type Light \
  --properties '{"m_Type":"Directional","m_Intensity":1}'
$U cmd set_transform --project-path . --target "/Directional Light" --position '[0,3,0]' --rotation '[50,-30,0]'
```

바닥은 `Dust_MaterialVariants_Test`와 같은 구성으로 만든다 — plane 프리미티브에 `M_Dust` 머티리얼과 `DustPaintTarget`:

```bash
$U cmd create_gameobject --project-path . --name "Floor_Dust" --primitive plane
$U cmd set_component_properties --project-path . --target "/Floor_Dust" --type MeshRenderer \
  --properties '{"m_Materials":["Assets/Game/InGame/Dust/Materials/M_Dust.mat"]}'
$U cmd add_component --project-path . --target "/Floor_Dust" --type DustPaintTarget
```

- [ ] **Step 2: 플레이어를 넣는다**

```bash
~/.unity/bin/unity cmd instantiate_prefab --project-path . \
  --prefab Assets/Game/InGame/Player/Prefabs/PF_Player.prefab --name PF_Player
```

카메라는 `PF_Player` 안에 있으므로 따로 만들지 않는다. 씬에 `Main Camera`를 추가하지 않는다 — `AudioListener`가 둘이 되면 경고가 뜬다.

- [ ] **Step 3: 컴포넌트를 붙이고 배선한다**

Project 창/Inspector에서 한다. 붙일 자리:

| 컴포넌트 | 붙일 오브젝트 |
|---|---|
| `MopMode` | `PF_Player` (루트) |
| `MopLocomotion` | `PF_Player/GnomeCharacter` (`CharacterController`가 여기 있다) |
| `MopCamera` | `PF_Player/PF_SyntyCamera` |
| `MopPad` | `PF_Player/GnomeCharacter` |
| `DustCleanVfx` | `PF_Player/GnomeCharacter` |

배선:

- `MopMode._inputReader` → `GnomeCharacter`의 `InputReader`
- `MopMode._playerLocomotion` → `GnomeCharacter`의 `PlayerAnimationController`
- `MopMode._playerCamera` → `PF_SyntyCamera`의 `PlayerCameraController`
- `MopMode._mopLocomotion` / `_cleaningCameraRig` / `_mopPad` → 각각 위에서 붙인 것
- `MopLocomotion._mode` → `PF_Player`의 `MopMode`
- `MopCamera._target` → `GnomeCharacter`, `_camera` → `PF_SyntyCamera/MainCamera`
- `MopPad._origin` → `GnomeCharacter`, `_vfx` → `GnomeCharacter`의 `DustCleanVfx`
- `DustCleanVfx._puff` → `Assets/Game/InGame/Dust/VFX/VFX_DustPuff.vfx` 를 붙인 `VisualEffect`

`VFX_DustPuff`를 쓰려면 자식 오브젝트에 `VisualEffect` 컴포넌트를 만들어 그 에셋을 물리고, 그것을 `DustCleanVfx._puff`에 넣는다.

- [ ] **Step 4: 씬을 저장하고 빌드 세팅에 없는지 확인한다**

```bash
~/.unity/bin/unity cmd save_scene --project-path .
grep -c "Mop_Cleaning_Test" ProjectSettings/EditorBuildSettings.asset
```

기대: `0`.

- [ ] **Step 5: 플레이 모드에서 8개를 확인한다**

```bash
~/.unity/bin/unity cmd editor_play --project-path .
```

스펙 §7의 목록이다. 하나씩 눈으로 본다.

1. 좌클릭에 시점이 탑다운으로 바뀐다
2. W로 전진, A/D로 제자리 회전
3. 지나간 자리가 닦이고 퍼프가 뜬다
4. 다시 좌클릭하면 원래 시점·조작으로 **튀지 않고** 돌아온다
5. 청소 중 WASD가 캐릭터를 평상시처럼 움직이지 않는다
6. W를 누른 채 진입·이탈해도 캐릭터가 흘러가지 않는다
7. 경사면에서 패드가 표면에 붙는다 (바닥을 기울여 확인)
8. 벽 너머의 바닥이 지워지지 않는다

```bash
~/.unity/bin/unity cmd editor_stop --project-path .
~/.unity/bin/unity cmd screenshot --project-path . --view Game \
  --output /Users/dust9826/Documents/UnityProjects-branchB/PPackPPack_v2-branchB/docs/images/verify/vacuum_cleaning.png \
  --width 1280 --height 720
```

8개는 **사람이 플레이하며 봐야 한다.** 마우스와 키보드 입력이 필요하고 카메라 복귀가 튀는지는 눈으로만 판단된다. 에이전트는 씬을 띄우고 Play Mode를 켠 뒤 넘긴다.

`screenshot --output`은 절대 경로가 먹는다. `capture_game_view --save_path`는 경로를 `Assets/` 안으로 가두므로 쓰지 않는다.

**4·6이 실패하면** 원인과 함께 `Mop/AGENTS.md`에 기록하고 넘어간다 — 스펙 §7이 허용한 범위다. 1·2·3·5가 실패하면 멈추고 보고한다.

- [ ] **Step 6: `Mop/AGENTS.md`를 고친다**

현재 *"Left-click hold pulls in whatever is ahead and releasing stops it immediately"*를 정정한다. 담을 것:

- **좌클릭은 토글이고 흡입 스위치가 아니라 모드 스위치다 (2026-08-11).** 홀드로 두면 손을 뗄 때마다 카메라와 조작이 뒤바뀌어 바닥 한 줄을 닦는 동안 시점이 여러 번 뒤집힌다. 원래의 "홀드 = 흡입"은 모드 전환이 없는 `Trash` 흡입에서 다시 만난다
- **팀원 파일을 고치지 않기 위해 컴포넌트를 통째로 끈다 (2026-08-11).** `PlayerCameraController`의 피치가 (-10, 45)로 잘려 있고 필드가 private이다. 복귀 시 튐이 감당 안 되면 이 결정을 뒤집고 그 파일에 공개 API를 더한다 — 그때는 팀원과 합의한다
- **알려진 공백**: 청소 중 애니메이션이 정지한다(`PlayerAnimationController`를 끄므로). 청소 애니메이션이 생기면 해소된다
- Step 5의 4·6 결과

- [ ] **Step 7: `docs/INDEX.md`를 갱신한다**

Specs·Plans 절에 각각 한 줄, "현재 상태" 절에 대걸레 청소 조작이 들어왔다는 한 줄.

- [ ] **Step 8: 에디터 상태를 복원한다**

Task 1 Step 1에서 기록한 상태로 되돌린다. 확인할 것 — Play Mode 꺼짐, 원래 활성 씬, dirty 없음, `(Clone)`·`__TEST__` 잔해 없음.

---

## 체크인 세 번

루트 `AGENTS.md`의 "What goes in one check-in"을 따른다. 태스크는 실행 단위고 체크인은 딜리버러블 단위다.

| 체크인 | 태스크 | 딜리버러블 |
|---|---|---|
| **A** | 1·2·3 | 입력 자산과 모드 전환 골격 — 좌클릭에 컴포넌트가 교대하고 탱크 조작이 돈다 |
| **B** | 4·5 | 탑다운 카메라와 실제 청소 — 밀고 다니면 닦인다 |
| **C** | 6 | 테스트 씬·검증 결과·문서 |

각 체크인 전에 `cm status`로 경로 목록을 완성한다. `.meta`를 빠뜨리지 않는다.

---

## 완료 기준

- `Assets/Game/InGame/Player/` 아래 파일이 **하나도 수정되지 않았다** (`cm status`로 확인)
- 컴파일 에러 0건, 콘솔 에러 0건
- 스펙 §7의 1·2·3·5가 통과한다
- `Mop_Cleaning_Test.unity`가 Build Settings에 없다
- Play Mode 꺼짐, 원래 씬 활성, dirty 없음
- `cm status`가 깨끗하다 (기존 `ProjectSettings/Packages` Private 제외)

## 하지 않는 것

- 청소기 모델·애니메이션, 대걸레 조작, 청소도 집계, 사운드
- `Mop` 모드 — `PlayerToolSwitcher`가 F키로 `EPlayerState`만 바꾸고 이번 작업은 그것을 읽지 않는다
- Fusion 코드
- 팀원 파일 수정
