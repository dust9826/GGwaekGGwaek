using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    [DisallowMultipleComponent]
    public sealed class BlizzardAlertPresenter : MonoBehaviour
    {
        [SerializeField] private BlizzardEvent _event;
        [SerializeField, Min(0.1f)] private float _visibleSeconds = 3f;
        [SerializeField, Min(0.01f)] private float _fadeSeconds = 0.25f;

        private VisualElement _alert;
        private float _shownAt = -1f;

        public void Configure(BlizzardEvent weatherEvent) => _event = weatherEvent;

        private void OnEnable()
        {
            ResolveAlert();
            BindEvent();
            HideImmediately();
        }

        private void OnDisable()
        {
            if (_event != null) _event.PhaseChanged -= OnPhaseChanged;
            HideImmediately();
        }

        private void Update()
        {
            if (_event == null) BindEvent();
            if (_shownAt < 0f || !ResolveAlert()) return;

            float elapsed = Time.unscaledTime - _shownAt;
            float opacity;
            if (elapsed < _fadeSeconds) opacity = elapsed / _fadeSeconds;
            else if (elapsed < _visibleSeconds - _fadeSeconds) opacity = 1f;
            else opacity = 1f - (elapsed - (_visibleSeconds - _fadeSeconds)) / _fadeSeconds;

            _alert.style.opacity = Mathf.Clamp01(opacity);
            if (elapsed >= _visibleSeconds) HideImmediately();
        }

        private void BindEvent()
        {
            if (_event == null) _event = FindAnyObjectByType<BlizzardEvent>();
            if (_event == null) return;
            _event.PhaseChanged -= OnPhaseChanged;
            _event.PhaseChanged += OnPhaseChanged;
        }

        private void OnPhaseChanged(EBlizzardEventPhase phase)
        {
            if (phase != EBlizzardEventPhase.Warning || !ResolveAlert()) return;
            _shownAt = Time.unscaledTime;
            _alert.style.display = DisplayStyle.Flex;
            _alert.style.opacity = 0f;
        }

        private bool ResolveAlert()
        {
            if (_alert != null) return true;
            UIDocument document = GetComponent<UIDocument>();
            _alert = document != null
                ? document.rootVisualElement?.Q<VisualElement>("blizzard-alert")
                : null;
            return _alert != null;
        }

        private void HideImmediately()
        {
            _shownAt = -1f;
            if (!ResolveAlert()) return;
            _alert.style.opacity = 0f;
            _alert.style.display = DisplayStyle.None;
        }

        private void OnValidate()
        {
            _visibleSeconds = Mathf.Max(0.1f, _visibleSeconds);
            _fadeSeconds = Mathf.Clamp(_fadeSeconds, 0.01f, _visibleSeconds * 0.5f);
        }
    }
}
