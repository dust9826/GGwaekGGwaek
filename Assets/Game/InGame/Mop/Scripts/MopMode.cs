using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 청소 모드의 유일한 진실. 좌클릭으로 토글하고, 평상시 컴포넌트와 청소용 컴포넌트를 교대시킨다.
    ///
    /// 팀원 파일(<c>InGame/Player/</c>)을 고치지 않기 위해 통째로 끄고 우리가 몬다.
    /// <see cref="InputReader"/> 를 끄면 그 <c>OnDisable</c> 이 Player 액션 맵까지 함께 끈다 —
    /// 맵을 따로 만질 필요가 없다.
    ///
    /// Fusion 이 오면 <see cref="IsCleaning"/> 이 <c>[Networked]</c> 로 승격될 자리다. 다른
    /// 컴포넌트는 이것을 읽기만 한다.
    /// </summary>
    public sealed class MopMode : MonoBehaviour
    {
        private const string CleaningMapName = "Cleaning";
        private const string ToggleMapName = "ToolToggle";
        private const string MoveActionName = "Move";
        private const string ToggleActionName = "Toggle";

        [Tooltip("Cleaning 맵과 ToolToggle 맵을 담은 자산.")]
        [SerializeField] private InputActionAsset _controls;

        [Header("평상시 — 청소 중에는 끈다")]
        [SerializeField] private InputReader _inputReader;
        [SerializeField] private PlayerAnimationController _playerLocomotion;
        [SerializeField] private PlayerCameraController _playerCamera;

        [Header("청소 중 — 평상시에는 끈다")]
        [SerializeField] private MopLocomotion _mopLocomotion;
        [SerializeField] private MopPad _mopPad;

        [Tooltip("청소용 Cinemachine 카메라. 오브젝트를 껐다 켜면 Brain 이 알아서 블렌드한다. " +
                 "컴포넌트가 아니라 GameObject 를 받는 이유는 vcam 이 비활성일 때만 " +
                 "PlayerCameraController 가 카메라를 되찾기 때문이다.")]
        [SerializeField] private GameObject _cleaningCameraRig;

        private InputActionMap _cleaningMap;
        private InputAction _toggleAction;

        public bool IsCleaning { get; private set; }

        /// <summary>청소 모드의 이동 입력. 모드가 꺼져 있으면 값이 들어오지 않는다.</summary>
        public InputAction MoveAction { get; private set; }

        private void Awake()
        {
            if (_controls == null)
            {
                Debug.LogError($"{nameof(MopMode)}: 입력 자산이 비어 있다.", this);
                enabled = false;
                return;
            }

            // 자산을 인스턴스로 복제한다. 원본을 직접 Enable 하면 씬을 나가도 활성인 채로 남는다.
            _controls = Instantiate(_controls);

            _cleaningMap = _controls.FindActionMap(CleaningMapName, true);
            MoveAction = _cleaningMap.FindAction(MoveActionName, true);
            _toggleAction = _controls.FindActionMap(ToggleMapName, true)
                                     .FindAction(ToggleActionName, true);

            SetCleaning(false);
        }

        private void OnEnable()
        {
            if (_toggleAction == null) return;

            _toggleAction.performed += OnTogglePerformed;
            _toggleAction.Enable();
        }

        private void OnDisable()
        {
            if (_toggleAction == null) return;

            _toggleAction.performed -= OnTogglePerformed;
            _toggleAction.Disable();
            _cleaningMap.Disable();
        }

        private void OnTogglePerformed(InputAction.CallbackContext context)
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
            if (_mopPad != null) _mopPad.enabled = cleaning;
            if (_cleaningCameraRig != null) _cleaningCameraRig.SetActive(cleaning);

            if (cleaning) _cleaningMap.Enable();
            else _cleaningMap.Disable();
        }
    }
}
