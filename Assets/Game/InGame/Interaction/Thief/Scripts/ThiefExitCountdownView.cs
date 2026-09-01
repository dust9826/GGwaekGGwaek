using UnityEngine;

namespace PPack
{
    /// <summary>퇴장 지점에 도착한 도둑의 남은 시간을 각 클라이언트에서 표시한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefExitCountdownView : MonoBehaviour
    {
        [SerializeField] private ThiefActor _actor;
        [SerializeField] private ThiefNetworkHub _networkHub;
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private TextMesh _label;
        [SerializeField] private TextMesh _shadow;

        private Camera _camera;
        private int _presentedSeconds = -1;

        private void Awake()
        {
            if (_actor == null) _actor = GetComponent<ThiefActor>();
            if (_networkHub == null) _networkHub = GetComponent<ThiefNetworkHub>();
            SetVisible(false);
        }

        private void LateUpdate()
        {
            EThiefAction action = _networkHub != null
                ? _networkHub.PresentedAction
                : _actor != null ? _actor.CurrentAction : EThiefAction.Waiting;
            float remaining = _networkHub != null
                ? _networkHub.PresentedExitCountdownRemaining
                : _actor != null ? _actor.ExitCountdownRemaining : 0f;
            bool visible = action == EThiefAction.ExitCountdown && remaining > 0f;
            SetVisible(visible);
            if (!visible) return;

            int seconds = Mathf.Max(1, Mathf.CeilToInt(remaining));
            if (_presentedSeconds != seconds)
            {
                string value = seconds.ToString();
                if (_label != null) _label.text = value;
                if (_shadow != null) _shadow.text = value;
                _presentedSeconds = seconds;
            }

            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera != null && _visualRoot != null)
                _visualRoot.rotation = Quaternion.LookRotation(
                    _visualRoot.position - _camera.transform.position, Vector3.up);
        }

        private void SetVisible(bool visible)
        {
            if (_visualRoot != null && _visualRoot.gameObject.activeSelf != visible)
                _visualRoot.gameObject.SetActive(visible);
            if (!visible) _presentedSeconds = -1;
        }
    }
}
