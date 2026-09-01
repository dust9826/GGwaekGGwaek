using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 펭귄 입력. <c>PenguinControls.inputactions</c>의 <c>Gameplay</c> 맵을 감싼다.
    /// <c>VehicleInput</c>과 같은 방식으로 자산을 인스턴스 복제하고, 생성된 C# 래퍼 없이
    /// <c>FindActionMap</c>/<c>FindAction</c> 문자열 조회로 액션을 찾는다.
    ///
    /// <b>순간 입력(눌린 프레임)은 <c>Update</c>에서 래치해 <c>FixedUpdate</c>에 넘긴다
    /// (2026-08-22, Rigidbody 재작성).</b> <c>PenguinLocomotion</c>이 <c>FixedUpdate</c>로
    /// 내려가면서, 래치 없이 <c>wasPressedThisFrame</c>을 매 <c>Update</c>마다 덮어쓰는 예전
    /// 방식은 문제가 된다 — 120fps 대 50Hz 물리 스텝 기준으로 <b>점프 같은 순간 입력의 약
    /// 58%가 조용히 사라진다.</b> 여러 <c>Update</c> 프레임에 걸쳐 눌림을 <c>_pending</c>
    /// 플래그로 모았다가, 다음 <c>FixedUpdate</c> 한 번에만 공개 프로퍼티를 참으로 만들고
    /// 즉시 비운다. <see cref="PenguinLocomotion"/>과 <see cref="PenguinSnowball"/> 둘 다
    /// <c>FixedUpdate</c>에서 같은 프레임에 이 값을 읽으므로, 이 스크립트가 그 둘보다 먼저
    /// 실행되도록 <see cref="DefaultExecutionOrder"/>를 더 낮게 잡아야 한다 —
    /// <c>PenguinSnowball</c>이 이미 -100이라 여기서는 -200으로 확실히 앞선다.
    ///
    /// <c>MoveInput</c>·<c>SprintHeld</c>·<c>PackSnowHeld</c>는 <b>래치하지 않는다</b> — 순간
        /// 이벤트가 아니라 "지금 눌려 있는가"를 묻는 상태값이라, 매 프레임 액션 상태를 그대로
    /// 읽어도 스텝 사이에서 잃어버릴 정보가 없다.
    /// </summary>
    [DefaultExecutionOrder(-200)]
    public sealed class PenguinInputReader : MonoBehaviour
    {
        private const string GameplayMapName = "Gameplay";
        private const string MoveActionName = "Move";
        private const string LookActionName = "Look";
        private const string JumpActionName = "Jump";
        private const string SprintActionName = "Sprint";
        private const string PackSnowActionName = "PackSnow";
        private const string CreateSnowballActionName = "CreateSnowball";
        private const string CoopShoveActionName = "CoopShove";
        private const string BurstActionName = "Burst";
        private const string PickupActionName = "Pickup";

        [Tooltip("Gameplay 맵을 담은 PenguinControls 입력 자산.")]
        [SerializeField] private InputActionAsset _controls;

        private InputActionMap _gameplayMap;
        private InputAction _moveAction;
        private InputAction _lookAction;
        private InputAction _jumpAction;
        private InputAction _sprintAction;
        private InputAction _packSnowAction;
        private InputAction _createSnowballAction;
        private InputAction _coopShoveAction;
        private InputAction _burstAction;
        private InputAction _pickupAction;

        /// <summary>x = 오른쪽(D), y = 앞(W). 정규화하지 않은 -1..1 값이다.</summary>
        public Vector2 MoveInput { get; private set; }

        /// <summary>이번 프레임의 마우스 델타.</summary>
        public Vector2 LookDelta { get; private set; }

        /// <summary>이번 프레임에 스페이스바가 눌렸는가.</summary>
        public bool JumpPressedThisFrame { get; private set; }

        /// <summary>Shift를 누르고 있는 동안 true. 일반 이동에서는 달리기, 공중에서는 슬라이드 전환이다.</summary>
        public bool SprintHeld { get; private set; }

        /// <summary>좌클릭을 누르고 있는 동안 true. 눈덩이가 없을 때의 공격 입력이다.</summary>
        public bool PackSnowHeld { get; private set; }

        /// <summary>좌클릭을 누른 순간. 눈덩이가 없을 때 공격으로 소비한다.</summary>
        public bool PrimaryActionPressedThisFrame { get; private set; }

        /// <summary>E — 전방 눈덩이를 선택해 밀거나 발밑 눈으로 새 눈덩이를 만든다.</summary>
        public bool CreateSnowballPressedThisFrame { get; private set; }

        /// <summary>기존 눈덩이 접촉 코드도 같은 E 입력을 읽는다.</summary>
        public bool GrabPressedThisFrame => CreateSnowballPressedThisFrame;

        /// <summary>우클릭 — 함께 밀기 타이밍을 입력한다.</summary>
        public bool CoopShovePressedThisFrame { get; private set; }

        /// <summary>Q — 붙어 있는 눈덩이를 터뜨린다.</summary>
        public bool BurstPressedThisFrame { get; private set; }

        /// <summary>F — 가까운 눈덩이·선물을 등에 메거나 내려놓고, 접근 중이면 취소한다.</summary>
        public bool PickupPressedThisFrame { get; private set; }

        private bool _jumpPending;
        private bool _createSnowballPending;
        private bool _coopShovePending;
        private bool _burstPending;
        private bool _pickupPending;
        private bool _primaryActionPending;

        private void Awake()
        {
            if (_controls == null)
            {
                Debug.LogError($"{nameof(PenguinInputReader)}: 입력 자산이 비어 있다.", this);
                enabled = false;
                return;
            }

            _controls = Instantiate(_controls);
            _gameplayMap = _controls.FindActionMap(GameplayMapName, true);
            _moveAction = _gameplayMap.FindAction(MoveActionName, true);
            _lookAction = _gameplayMap.FindAction(LookActionName, true);
            _jumpAction = _gameplayMap.FindAction(JumpActionName, true);
            _sprintAction = _gameplayMap.FindAction(SprintActionName, true);
            _packSnowAction = _gameplayMap.FindAction(PackSnowActionName, true);
            _createSnowballAction = _gameplayMap.FindAction(CreateSnowballActionName, true);
            _coopShoveAction = _gameplayMap.FindAction(CoopShoveActionName, true);
            _burstAction = _gameplayMap.FindAction(BurstActionName, true);
            _pickupAction = _gameplayMap.FindAction(PickupActionName, true);
        }

        private void OnEnable()
        {
            if (_gameplayMap == null) return;

            _gameplayMap.Enable();
        }

        private void Update()
        {
            MoveInput = _moveAction.ReadValue<Vector2>();
            LookDelta = _lookAction.ReadValue<Vector2>();
            SprintHeld = _sprintAction.IsPressed();
            PackSnowHeld = _packSnowAction.IsPressed();

            // 순간 입력은 지우지 않고 모은다 — 다음 FixedUpdate 가 가져가면서 비운다.
            _jumpPending |= _jumpAction.WasPressedThisFrame();
            _createSnowballPending |= _createSnowballAction.WasPressedThisFrame();
            _coopShovePending |= _coopShoveAction.WasPressedThisFrame();
            _burstPending |= _burstAction.WasPressedThisFrame();
            _pickupPending |= _pickupAction.WasPressedThisFrame();
            _primaryActionPending |= _packSnowAction.WasPressedThisFrame();
        }

        /// <summary>
        /// 모아 둔 순간 입력을 이번 물리 스텝에만 참으로 공개하고 비운다. 실행 순서가
        /// <see cref="PenguinLocomotion"/>·<see cref="PenguinSnowball"/>보다 앞서야 하므로
        /// <see cref="DefaultExecutionOrder"/>가 이 클래스에 걸려 있다.
        /// </summary>
        private void FixedUpdate()
        {
            JumpPressedThisFrame = _jumpPending;
            CreateSnowballPressedThisFrame = _createSnowballPending;
            CoopShovePressedThisFrame = _coopShovePending;
            BurstPressedThisFrame = _burstPending;
            PickupPressedThisFrame = _pickupPending;
            PrimaryActionPressedThisFrame = _primaryActionPending;

            _jumpPending = false;
            _createSnowballPending = false;
            _coopShovePending = false;
            _burstPending = false;
            _pickupPending = false;
            _primaryActionPending = false;
        }

        private void OnDisable()
        {
            MoveInput = Vector2.zero;
            LookDelta = Vector2.zero;
            SprintHeld = false;
            PackSnowHeld = false;

            JumpPressedThisFrame = false;
            CreateSnowballPressedThisFrame = false;
            CoopShovePressedThisFrame = false;
            BurstPressedThisFrame = false;
            PickupPressedThisFrame = false;
            PrimaryActionPressedThisFrame = false;

            _jumpPending = false;
            _createSnowballPending = false;
            _coopShovePending = false;
            _burstPending = false;
            _pickupPending = false;
            _primaryActionPending = false;

            if (_gameplayMap == null) return;

            _gameplayMap.Disable();
        }
    }
}
