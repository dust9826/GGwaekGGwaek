using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    [RequireComponent(typeof(PlayerState))]
    public class PlayerToolSwitcher : MonoBehaviour
    {
        private PlayerState _playerState;
        private InputAction _switchToolAction;

        private void Awake()
        {
            _playerState = GetComponent<PlayerState>();
            _switchToolAction = new InputAction("SwitchTool", InputActionType.Button, "<Keyboard>/f");
        }

        private void OnEnable()
        {
            _switchToolAction.performed += OnSwitchToolPerformed;
            _switchToolAction.Enable();
        }

        private void OnDisable()
        {
            _switchToolAction.performed -= OnSwitchToolPerformed;
            _switchToolAction.Disable();
        }

        private void OnDestroy()
        {
            _switchToolAction.Dispose();
        }

        private void OnSwitchToolPerformed(InputAction.CallbackContext context)
        {
            EPlayerState next = _playerState.Current == EPlayerState.Vacuum ? EPlayerState.Mop : EPlayerState.Vacuum;
            _playerState.SetState(next);
        }
    }
}
