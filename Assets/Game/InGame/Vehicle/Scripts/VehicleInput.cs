using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// VehicleControls.inputactions의 Driving 맵을 감싼다. Mop/MopMode.cs와 같은 패턴 —
    /// 자산을 인스턴스로 복제해 씬을 나가도 활성 상태가 남지 않게 한다. 생성된 래퍼 클래스
    /// 없이 FindActionMap/FindAction 문자열 조회로 액션을 찾는다(MopControls와 동일).
    /// </summary>
    public sealed class VehicleInput : MonoBehaviour
    {
        private const string DrivingMapName = "Driving";
        private const string MoveActionName = "Move";
        private const string DriftActionName = "Drift";
        private const string AccelerateActionName = "Accelerate";
        private const string PullFrontActionName = "PullFront";
        private const string PullLeftActionName = "PullLeft";
        private const string PullRightActionName = "PullRight";
        private const string PullCancelActionName = "PullCancel";

        [Tooltip("Driving 맵을 담은 자산.")]
        [SerializeField] private InputActionAsset _controls;

        private InputActionMap _drivingMap;
        private InputAction _moveAction;
        private InputAction _driftAction;
        private InputAction _accelerateAction;
        private InputAction _pullFrontAction;
        private InputAction _pullLeftAction;
        private InputAction _pullRightAction;
        private InputAction _pullCancelAction;

        public Vector2 Move { get; private set; }
        public bool AccelerateHeld { get; private set; }
        public bool PullFrontHeld { get; private set; }
        public bool PullLeftHeld { get; private set; }
        public bool PullRightHeld { get; private set; }
        public bool PullFrontReleased { get; private set; }
        public bool PullLeftReleased { get; private set; }
        public bool PullRightReleased { get; private set; }
        public bool PullLeftPressed { get; private set; }
        public bool PullRightPressed { get; private set; }
        /// <summary>부채꼴을 조준(방향키를 누르고 있는) 중일 때만 의미가 있다 — 아래쪽 화살표로 취소.</summary>
        public bool PullCancelPressed { get; private set; }

        /// <summary>드리프트는 홀드다. 누른 순간이 아니라 누르고 있는 동안 그립이 낮아진다.</summary>
        public bool DriftHeld { get; private set; }

        private void Awake()
        {
            if (_controls == null)
            {
                Debug.LogError($"{nameof(VehicleInput)}: 입력 자산이 비어 있다.", this);
                enabled = false;
                return;
            }

            _controls = Instantiate(_controls);
            _drivingMap = _controls.FindActionMap(DrivingMapName, true);
            _moveAction = _drivingMap.FindAction(MoveActionName, true);
            _driftAction = _drivingMap.FindAction(DriftActionName, true);
            _accelerateAction = _drivingMap.FindAction(AccelerateActionName, true);
            _pullFrontAction = _drivingMap.FindAction(PullFrontActionName, true);
            _pullLeftAction = _drivingMap.FindAction(PullLeftActionName, true);
            _pullRightAction = _drivingMap.FindAction(PullRightActionName, true);
            _pullCancelAction = _drivingMap.FindAction(PullCancelActionName, true);
        }

        private void OnEnable()
        {
            if (_drivingMap == null) return;

            _drivingMap.Enable();
        }

        private void OnDisable()
        {
            // 입력맵을 끄는 것만으로는 부족하다 — 프로퍼티는 Update가 마지막으로 쓴 값에
            // 박제된 채 남는다. 가속 키를 누른 채로 게이팅되면 그 값 그대로 차가 계속
            // 움직인다(InGame/Cleanliness/AGENTS.md 종료 시퀀스가 이 게이팅에 의존한다).
            Move = Vector2.zero;
            AccelerateHeld = false;
            DriftHeld = false;
            PullFrontHeld = false;
            PullLeftHeld = false;
            PullRightHeld = false;
            PullFrontReleased = false;
            PullLeftReleased = false;
            PullRightReleased = false;
            PullLeftPressed = false;
            PullRightPressed = false;
            PullCancelPressed = false;

            if (_drivingMap == null) return;

            _drivingMap.Disable();
        }

        private void Update()
        {
            Move = _moveAction.ReadValue<Vector2>();
            AccelerateHeld = _accelerateAction.IsPressed();
            DriftHeld = _driftAction.IsPressed();
            PullFrontHeld = _pullFrontAction.IsPressed();
            PullLeftHeld = _pullLeftAction.IsPressed();
            PullRightHeld = _pullRightAction.IsPressed();
            PullFrontReleased = _pullFrontAction.WasReleasedThisFrame();
            PullLeftReleased = _pullLeftAction.WasReleasedThisFrame();
            PullRightReleased = _pullRightAction.WasReleasedThisFrame();
            PullLeftPressed = _pullLeftAction.WasPressedThisFrame();
            PullRightPressed = _pullRightAction.WasPressedThisFrame();
            PullCancelPressed = _pullCancelAction.WasPressedThisFrame();
        }
    }
}
