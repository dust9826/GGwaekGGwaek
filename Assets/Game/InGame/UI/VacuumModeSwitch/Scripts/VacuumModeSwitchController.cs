using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class VacuumModeSwitchController : MonoBehaviour
    {
        private static readonly Vector2 ActiveToNextOffset = new Vector2(104f, -60f);
        private static readonly Vector2 NextToActiveOffset = new Vector2(-104f, 60f);
        private static readonly Vector2 FrontArcBend = new Vector2(28f, 46f);
        private static readonly Vector2 RearArcBend = new Vector2(-28f, -46f);
        private const float DepthSwapTime = 0.115f;

        [SerializeField] private VacuumToolModeCatalog _catalog;
        [FormerlySerializedAs("_currentMode")]
        [SerializeField] private int _currentModeIndex;
        [SerializeField] private UnityEvent _modeChangedFeedback = new UnityEvent();
        [SerializeField] private UnityEvent<VacuumToolModeDefinition> _modeChanged = new UnityEvent<VacuumToolModeDefinition>();

        private VisualElement _root;
        private VisualElement _modeSwitch;
        private VisualElement _activeSlot;
        private VisualElement _nextSlot;
        private VisualElement _activeIconHost;
        private VisualElement _nextIconHost;
        private Sequence _transition;

        public int CurrentModeIndex => _currentModeIndex;
        public VacuumToolModeDefinition CurrentMode => _catalog == null
            ? null
            : _catalog.GetWrapped(_currentModeIndex);

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            CacheElements();
            RefreshVisuals();
            _root.schedule.Execute(PlayIntro).StartingIn(80);
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                CycleMode();
            }
        }

        private void OnDisable()
        {
            _transition?.Kill();
            DOTween.Kill(_modeSwitch);
            DOTween.Kill(_activeSlot);
            DOTween.Kill(_nextSlot);
        }

        public void CycleMode()
        {
            if (_catalog == null || _catalog.Count < 2 || _transition is { active: true })
            {
                return;
            }

            int nextModeIndex = (_currentModeIndex + 1) % _catalog.Count;

            KillSlotTweens();
            _modeChangedFeedback.Invoke();

            _transition = DOTween.Sequence()
                .SetTarget(this)
                .SetUpdate(true);
            _transition.Append(ArcMoveTo(
                _activeSlot,
                ActiveToNextOffset,
                FrontArcBend,
                0.23f).SetEase(Ease.InOutSine));
            _transition.Join(ScaleTo(_activeSlot, 0.58f, 0.23f).SetEase(Ease.InOutSine));
            _transition.Join(FadeTo(_activeSlot, 0.42f, 0.18f).SetEase(Ease.OutQuad));
            _transition.Join(ArcMoveTo(
                _nextSlot,
                NextToActiveOffset,
                RearArcBend,
                0.23f).SetEase(Ease.InOutSine));
            _transition.Join(ScaleTo(_nextSlot, 1.10f, 0.23f).SetEase(Ease.OutBack, 1.08f));
            _transition.Join(FadeTo(_nextSlot, 1f, 0.17f).SetEase(Ease.OutQuad));
            _transition.InsertCallback(DepthSwapTime, SwapDepthOrder);
            _transition.AppendCallback(() => CompleteModeChange(nextModeIndex));
            _transition.Append(ScaleTo(_activeSlot, 1f, 0.10f).SetEase(Ease.OutBack));
        }

        public void SetMode(string modeId)
        {
            if (_catalog == null)
            {
                return;
            }

            int index = _catalog.IndexOf(modeId);
            if (index < 0 || index == _currentModeIndex)
            {
                return;
            }

            _transition?.Kill();
            _currentModeIndex = index;
            RefreshVisuals();
            _modeChanged.Invoke(CurrentMode);
        }

        private void CacheElements()
        {
            _modeSwitch = _root.Q<VisualElement>("mode-switch");
            _activeSlot = _root.Q<VisualElement>("active-slot");
            _nextSlot = _root.Q<VisualElement>("next-slot");
            _activeIconHost = _root.Q<VisualElement>("active-icon-host");
            _nextIconHost = _root.Q<VisualElement>("next-icon-host");
        }

        private void CompleteModeChange(int nextModeIndex)
        {
            _currentModeIndex = nextModeIndex;
            RefreshVisuals();
            _activeSlot.style.scale = new Scale(new Vector2(0.86f, 0.86f));
            _modeChanged.Invoke(CurrentMode);
        }

        private void RefreshVisuals()
        {
            ResetSlotStyles();

            if (_catalog == null || _catalog.Count == 0)
            {
                _activeIconHost.Clear();
                _nextIconHost.Clear();
                return;
            }

            _currentModeIndex = ((_currentModeIndex % _catalog.Count) + _catalog.Count) % _catalog.Count;
            PopulateIcon(_activeIconHost, _catalog.GetWrapped(_currentModeIndex));
            PopulateIcon(_nextIconHost, _catalog.GetWrapped(_currentModeIndex + 1));
            _nextSlot.style.display = _catalog.Count > 1 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private static void PopulateIcon(VisualElement host, VacuumToolModeDefinition mode)
        {
            host.Clear();
            if (mode == null || mode.IconTemplate == null)
            {
                return;
            }

            mode.IconTemplate.CloneTree(host);
            host.tooltip = mode.DisplayName;
        }

        private void ResetSlotStyles()
        {
            _nextSlot.PlaceBehind(_activeSlot);
            _activeSlot.style.translate = new Translate(0f, 0f, 0f);
            _activeSlot.style.scale = new Scale(Vector2.one);
            _activeSlot.style.opacity = 1f;
            _nextSlot.style.translate = new Translate(0f, 0f, 0f);
            _nextSlot.style.scale = new Scale(new Vector2(0.62f, 0.62f));
            _nextSlot.style.opacity = 0.54f;
        }

        private void SwapDepthOrder()
        {
            _nextSlot.PlaceInFront(_activeSlot);
        }

        private void KillSlotTweens()
        {
            _transition?.Kill();
            DOTween.Kill(_activeSlot);
            DOTween.Kill(_nextSlot);
        }

        private void PlayIntro()
        {
            DOTween.Kill(_modeSwitch);
            _modeSwitch.style.opacity = 0f;
            _modeSwitch.style.translate = new Translate(62f, 0f, 0f);
            _modeSwitch.style.scale = new Scale(new Vector2(0.92f, 0.92f));

            DOTween.Sequence()
                .SetTarget(_modeSwitch)
                .SetUpdate(true)
                .Append(FadeTo(_modeSwitch, 1f, 0.16f))
                .Join(MoveTo(_modeSwitch, Vector2.zero, 0.36f).SetEase(Ease.OutBack, 1.08f))
                .Join(ScaleTo(_modeSwitch, 1f, 0.30f).SetEase(Ease.OutBack));
        }

        private static Tweener FadeTo(VisualElement element, float endValue, float duration)
        {
            float value = element.resolvedStyle.opacity;
            return DOTween.To(() => value, next =>
            {
                value = next;
                element.style.opacity = next;
            }, endValue, duration).SetTarget(element);
        }

        private static Tweener MoveTo(VisualElement element, Vector2 endValue, float duration)
        {
            Vector2 value = element.resolvedStyle.translate;
            return DOTween.To(() => value, next =>
            {
                value = next;
                element.style.translate = new Translate(next.x, next.y, 0f);
            }, endValue, duration).SetTarget(element);
        }

        private static Tweener ArcMoveTo(
            VisualElement element,
            Vector2 endValue,
            Vector2 bend,
            float duration)
        {
            Vector2 startValue = element.resolvedStyle.translate;
            float progress = 0f;
            return DOTween.To(() => progress, next =>
            {
                progress = next;
                Vector2 linear = Vector2.LerpUnclamped(startValue, endValue, next);
                Vector2 arc = bend * Mathf.Sin(Mathf.PI * next);
                Vector2 position = linear + arc;
                element.style.translate = new Translate(position.x, position.y, 0f);
            }, 1f, duration).SetTarget(element);
        }

        private static Tweener ScaleTo(VisualElement element, float endValue, float duration)
        {
            Vector2 value = element.resolvedStyle.scale.value;
            Vector2 target = new Vector2(endValue, endValue);
            return DOTween.To(() => value, next =>
            {
                value = next;
                element.style.scale = new Scale(next);
            }, target, duration).SetTarget(element);
        }

    }
}
