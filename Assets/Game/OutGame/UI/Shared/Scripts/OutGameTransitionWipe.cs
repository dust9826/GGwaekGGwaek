using DG.Tweening;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    public static class OutGameTransitionWipe
    {
        private const float StandardOutDuration = 0.12f;
        private const float StandardInDuration = 0.16f;
        private const float StandardOverlapAt = 0.045f;
        private const float CurtainCoverDuration = 0.11f;
        private const float CurtainRevealDuration = 0.18f;
        private const float CurtainOpacity = 0.78f;

        private static Sequence _sequence;
        private static bool _isTransitioning;
        private static VisualElement _curtain;

        public static bool IsTransitioning => _isTransitioning;

        public static bool SwitchView(
            VisualElement root,
            VisualElement currentView,
            VisualElement nextView,
            bool useCurtain,
            System.Action onMidpoint,
            System.Action onComplete)
        {
            if (_isTransitioning || root == null || currentView == null || nextView == null)
            {
                return false;
            }

            _isTransitioning = true;
            _sequence?.Kill();
            _curtain = root.Q<VisualElement>("transition-curtain");

            float currentOpacity = currentView.resolvedStyle.opacity;
            float nextOpacity = 0f;

            nextView.style.display = DisplayStyle.Flex;
            nextView.style.opacity = 0f;
            nextView.style.translate = new Translate(0f, 0f, 0f);

            if (_curtain != null)
            {
                _curtain.style.display = DisplayStyle.Flex;
                _curtain.style.opacity = 0f;
            }

            _sequence = DOTween.Sequence()
                .SetUpdate(true)
                .SetTarget(root);

            if (useCurtain && _curtain != null)
            {
                float curtainOpacity = 0f;
                _sequence
                    .Insert(0f, DOTween.To(
                            () => curtainOpacity,
                            value =>
                            {
                                curtainOpacity = value;
                                _curtain.style.opacity = value;
                            },
                            CurtainOpacity,
                            CurtainCoverDuration)
                        .SetEase(Ease.InOutQuad))
                    .Insert(0f, DOTween.To(
                        () => currentOpacity,
                        value =>
                        {
                            currentOpacity = value;
                            currentView.style.opacity = value;
                        },
                        0f,
                        CurtainCoverDuration)
                    .SetEase(Ease.OutSine))
                    .InsertCallback(CurtainCoverDuration, () => onMidpoint?.Invoke())
                    .InsertCallback(CurtainCoverDuration, () => currentView.style.display = DisplayStyle.None)
                    .Insert(CurtainCoverDuration, DOTween.To(
                            () => curtainOpacity,
                            value =>
                            {
                                curtainOpacity = value;
                                _curtain.style.opacity = value;
                            },
                            0f,
                            CurtainRevealDuration)
                        .SetEase(Ease.InOutSine))
                    .Insert(CurtainCoverDuration, DOTween.To(
                        () => nextOpacity,
                        value =>
                        {
                            nextOpacity = value;
                            nextView.style.opacity = value;
                        },
                        1f,
                        CurtainRevealDuration)
                    .SetEase(Ease.OutSine));
            }
            else
            {
                _sequence
                    .Insert(0f, DOTween.To(
                            () => currentOpacity,
                            value =>
                            {
                                currentOpacity = value;
                                currentView.style.opacity = value;
                            },
                            0f,
                            StandardOutDuration)
                        .SetEase(Ease.OutSine))
                    .InsertCallback(StandardOverlapAt, () => onMidpoint?.Invoke())
                    .Insert(StandardOverlapAt, DOTween.To(
                            () => nextOpacity,
                            value =>
                            {
                                nextOpacity = value;
                                nextView.style.opacity = value;
                            },
                        1f,
                        StandardInDuration)
                        .SetEase(Ease.OutSine))
                    .InsertCallback(StandardOutDuration, () => currentView.style.display = DisplayStyle.None);
            }

            _sequence
                .OnComplete(() =>
                {
                    currentView.style.opacity = 1f;
                    currentView.style.translate = new Translate(0f, 0f, 0f);
                    currentView.EnableInClassList("menu-view-active", false);
                    currentView.EnableInClassList("menu-view-hidden", true);
                    nextView.style.opacity = 1f;
                    nextView.style.translate = new Translate(0f, 0f, 0f);
                    nextView.EnableInClassList("menu-view-hidden", false);
                    nextView.EnableInClassList("menu-view-active", true);

                    if (_curtain != null)
                    {
                        _curtain.style.opacity = 0f;
                        _curtain.style.display = DisplayStyle.None;
                    }

                    _sequence = null;
                    _isTransitioning = false;
                    onComplete?.Invoke();
                });

            return true;
        }

        public static void Cancel()
        {
            _sequence?.Kill();

            if (_curtain != null)
            {
                _curtain.style.opacity = 0f;
                _curtain.style.display = DisplayStyle.None;
            }

            _sequence = null;
            _curtain = null;
            _isTransitioning = false;
        }
    }
}
