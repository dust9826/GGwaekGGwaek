using PPack;   // ISnowBladeState 가 여기로 옮겨졌다(평평한 이름공간 규칙)
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Rendering;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// v7 필드·마처를 <b>이 프로젝트의 실제 맵</b>에 붙이는 어댑터.
    ///
    /// v7 의 부트스트랩(<c>SnowFakeV7Bootstrap</c>)은 스파이크용이라 자기 차·카메라·바닥을 직접 만든다.
    /// 실제 맵에는 이미 차량(<c>PF_VehicleProto</c>)과 카메라가 있으므로 그것들을 다시 만들면 안 되고,
    /// 필요한 것은 <b>블레이드 자세를 필드에 넘기는 한 층</b>뿐이다 — 그게 이 컴포넌트다.
    ///
    /// 넘기는 값은 <see cref="SnowPileSweepV7"/> 이고, 스텝 시작·끝의 블레이드 선 중심과 진행 방향,
    /// 부호 있는 속도, 동사 셋(내림 / 각도 / 붙음)이다. 차량이 그것을 결정하고 필드는 실행한다 —
    /// v7 의 원래 분담과 같다.
    ///
    /// ⚠ <b>이 프로젝트의 CPU 눈(<c>PPack.SnowStage</c> 계열)과 배타적이다.</b> 둘을 같이 켜면 같은 자리에
    /// 두 개의 눈이 그려지고, 차량의 CPU 패드가 자기 격자를 따로 깎는다. <see cref="_disablePPackSnow"/>
    /// 가 켜져 있으면 씬의 그 컴포넌트들을 런타임에 끈다.
    /// </summary>
    [RequireComponent(typeof(SnowPileFieldV7), typeof(SnowRaymarchRendererV7))]
    public sealed class SnowV7MapRig : MonoBehaviour, ISnowBladeState
    {
        [Header("격자 (맵에 맞춘다)")]
        [Tooltip("격자 원점의 월드 XZ. WinterVillage 는 (-60, -55).")]
        [SerializeField] private Vector2 _originXZ = new Vector2(-60f, -55f);
        [Tooltip("격자 크기(m). WinterVillage 는 120 × 110.")]
        [SerializeField] private Vector2 _sizeMeters = new Vector2(120f, 110f);
        [SerializeField, Min(0.02f)] private float _cellSizeM = 0.125f;
        [Tooltip("눈이 얹히는 바닥의 월드 Y.")]
        [SerializeField] private float _groundY;

        [Header("차량 접점")]
        [Tooltip("비면 씬에서 Rigidbody 를 가진 첫 차량을 찾는다.")]
        [SerializeField] private Transform _vehicle;
        [Tooltip("블레이드 선의 중심이 차량 원점에서 앞으로 이만큼 떨어져 있다.")]
        [SerializeField] private float _bladeAheadM = 1.1f;
        [Tooltip("이 속도 이상으로 전진할 때만 날이 더미를 받친다(히스테리시스는 절반값).")]
        [SerializeField, Min(0.05f)] private float _attachSpeedMps = 0.6f;
        [Tooltip("스텝의 호를 이 개수의 스윕 박스로 나눈다. 1 이면 현(chord).")]
        [SerializeField, Range(1, 8)] private int _segments = 3;

        [Header("입력")]
        [SerializeField] private Key _bladeToggleKey = Key.E;
        [SerializeField] private Key _angleLeftKey = Key.Digit1;
        [SerializeField] private Key _angleStraightKey = Key.Digit2;
        [SerializeField] private Key _angleRightKey = Key.Digit3;

        [Header("배타")]
        [Tooltip("씬의 PPack CPU 눈(SnowStage·패널·차량 패드)을 런타임에 끈다.")]
        [SerializeField] private bool _disablePPackSnow = true;

        private SnowPileFieldV7 _field;
        private SnowRaymarchRendererV7 _renderer;
        private Rigidbody _body;

        private bool _bladeDown;
        private int _angleState;
        private bool _attached;
        private Vector2 _lastCenter;
        private Vector2 _lastForward;
        private bool _hasLast;
        private bool _initialized;
        private bool _externallyDriven;

        /// <summary>이 리그가 구동하는 필드. 감속(<see cref="SnowV7VehicleFeel"/>)이 깊이를 읽는다.</summary>
        public SnowPileFieldV7 Field => _field;

        /// <summary>블레이드가 내려가 있는가. <b>제설 저항은 이것이 참일 때만</b> 붙는다(v7 규약).</summary>
        public bool BladeDown => _bladeDown;

        /// <summary>날이 더미를 받치고 있는가. 전진 0.6 m/s 이상에서 켜지고 절반값에서 풀린다.</summary>
        public bool BladeAttached => _attached;

        /// <summary>블레이드 선 중심이 차량 원점에서 앞으로 떨어진 거리. 깊이 샘플 지점이기도 하다.</summary>
        public float BladeAheadM => _bladeAheadM;

        /// <summary>
        /// 배출 방향. <b>-1 좌 · 0 정면 · +1 우</b> 이고 이 셋뿐이다(v7 규약 — 연속 각도가 아니다).
        /// 시각물(<see cref="SnowV7BladeVisual"/>)이 이 값으로 날을 요잉한다.
        /// </summary>
        public int AngleState => _angleState;

        /// <summary>
        /// 이 리그가 따라갈 차량을 런타임에 물린다. <b>멀티에서 필요하다</b> — 플레이어 차량은 세션이
        /// 시작된 뒤 스폰되므로 씬에서 미리 참조를 걸어 둘 수 없다. 스폰된 차량이 자기 것이라고
        /// 판단했을 때(입력 권한 보유) 스스로 이 메서드를 부른다.
        /// </summary>
        /// <summary>
        /// 블레이드 상태를 밖에서 정한다. <b>멀티에서 필요하다</b> — 네트워크 차량의 블레이드는 복제된
        /// 상태(<c>MultiplayPlowVehicle</c> 의 <c>[Networked]</c> 값)가 진실이고, 리그가 키보드를 따로
        /// 읽으면 <b>화면의 날과 깎이는 눈이 어긋난다.</b> 싱글에서는 아무도 부르지 않으므로 리그가
        /// 계속 자기 키 입력을 쓴다.
        /// </summary>
        public void SetBladeState(bool down, int angleState)
        {
            _bladeDown = down;
            _angleState = Mathf.Clamp(angleState, -1, 1);
            _externallyDriven = true;
        }

        public void SetVehicle(Transform vehicle)
        {
            _vehicle = vehicle;
            _body = vehicle == null ? null : vehicle.GetComponent<Rigidbody>();

            // 이전 차량의 마지막 위치를 그대로 쓰면 첫 스텝의 스윕이 맵을 가로지르는 선이 되어
            // 그 선을 따라 눈이 통째로 깎인다. 새 차량의 위치에서 다시 시작한다.
            _hasLast = false;
        }

        private void Awake()
        {
            _field = GetComponent<SnowPileFieldV7>();
            _renderer = GetComponent<SnowRaymarchRendererV7>();

            // 권위가 GPU 컴퓨트에 있는 스파이크 구성이라 그래픽이 없으면 아예 돌 수 없다.
            // 데디 서버에서 이 리그를 쓰지 않는다는 것을 코드로 못 박아 둔다.
            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
            {
                enabled = false;
                return;
            }

            if (_vehicle == null)
            {
                foreach (var rb in FindObjectsByType<Rigidbody>(FindObjectsSortMode.None))
                {
                    if (rb.isKinematic) continue;
                    _vehicle = rb.transform;
                    break;
                }
            }

            if (_vehicle != null) _vehicle.TryGetComponent(out _body);
        }

        private void OnEnable()
        {
            if (!_disablePPackSnow) return;

            // 같은 자리에 두 개의 눈이 그려지는 것을 막는다. 컴포넌트만 끄고 오브젝트는 남긴다 —
            // 씬의 다른 배선(예: SnowDriveSweep 의 참조)이 끊기지 않게.
            foreach (var mono in FindObjectsByType<MonoBehaviour>(FindObjectsInactive.Include,
                                                                 FindObjectsSortMode.None))
            {
                string type = mono.GetType().FullName;
                if (type == null || !type.StartsWith("PPack.Snow", System.StringComparison.Ordinal)) continue;
                if (mono is SnowV7MapRig) continue;

                mono.enabled = false;
                if (mono.GetType().Name == "SnowPanelBuilder") mono.gameObject.SetActive(false);
            }
        }

        private void Start()
        {
            _field.ApplyGeometryOverrides(_originXZ.x, _originXZ.y, _sizeMeters.x, _sizeMeters.y,
                                          _cellSizeM, -1);
            _field.EnsureResources();
            _renderer.Initialize(_field);
            _initialized = true;
        }

        private void Update()
        {
            // 밖에서 상태를 넣어 주는 동안(멀티)에는 키 입력을 읽지 않는다 - 두 주인이 생긴다.
            if (_externallyDriven) return;

            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[_bladeToggleKey].wasPressedThisFrame) _bladeDown = !_bladeDown;
            if (keyboard[_angleLeftKey].wasPressedThisFrame) _angleState = -1;
            else if (keyboard[_angleStraightKey].wasPressedThisFrame) _angleState = 0;
            else if (keyboard[_angleRightKey].wasPressedThisFrame) _angleState = 1;
        }

        private void FixedUpdate()
        {
            if (!_initialized || _vehicle == null) return;

            float dt = Time.fixedDeltaTime;

            Vector3 forward3 = _vehicle.forward;
            forward3.y = 0f;
            if (forward3.sqrMagnitude < 1e-6f) return;
            forward3.Normalize();

            var forward = new Vector2(forward3.x, forward3.z);
            Vector3 bladePos = _vehicle.position + forward3 * _bladeAheadM;
            var center = new Vector2(bladePos.x, bladePos.z);

            if (!_hasLast)
            {
                _lastCenter = center;
                _lastForward = forward;
                _hasLast = true;
            }

            // 부호 있는 속도는 물리에서 읽는다. 트랜스폼 차분은 스텝 사이에 물리가 끼어들면 튄다.
            float signedSpeed = _body != null
                ? Vector3.Dot(_body.linearVelocity, forward3)
                : Vector2.Dot(center - _lastCenter, forward) / dt;

            // 붙음 판정은 히스테리시스다 — 멈추기만 해도 놓는다(v7 규약).
            float attachOn = Mathf.Max(0.05f, _attachSpeedMps);
            float attachOff = attachOn * 0.5f;
            _attached = _bladeDown && (_attached ? signedSpeed > attachOff : signedSpeed > attachOn);

            var sweep = new SnowPileSweepV7
            {
                StartCenter = _lastCenter,
                StartForward = _lastForward,
                EndCenter = center,
                EndForward = forward,
                SignedSpeed = signedSpeed,
                Segments = _segments,
                BladeDown = _bladeDown,
                AngleState = _angleState,
                BladeAttached = _attached,
                Push01 = _attached ? Mathf.Clamp01(signedSpeed / 4f) : 0f,
            };

            _field.Step(dt, sweep);

            _lastCenter = center;
            _lastForward = forward;
        }

        private void LateUpdate()
        {
            if (_initialized) _renderer.UpdateUniforms(Time.deltaTime);
        }
    }
}
