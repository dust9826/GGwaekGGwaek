using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class PenguinTutorialHud : MonoBehaviour
    {
        public const int StepCount = 8;

        [SerializeField] private MMF_Player _stepCompleteFeedbacks;
        [SerializeField] private MMF_Player _nextStepFeedbacks;

        private UIDocument _document;
        private Label _stepCounter;
        private Label _title;
        private Label _key;
        private VisualElement _keyShadow;
        private Label _instruction;
        private Label _progressText;
        private VisualElement _progressFill;
        private VisualElement _card;
        private VisualElement _complete;
        private VisualElement _clearBadge;
        private VisualElement[] _stepDots;

        public bool IsReady => TryBind();

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            TryBind();
        }

        private bool TryBind()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) return false;

            _stepCounter = root.Q<Label>("step-counter");
            _title = root.Q<Label>("step-title");
            _key = root.Q<Label>("key-badge");
            _keyShadow = root.Q<VisualElement>("key-shadow");
            _instruction = root.Q<Label>("instruction");
            _progressText = root.Q<Label>("progress-text");
            _progressFill = root.Q<VisualElement>("progress-fill");
            _card = root.Q<VisualElement>("tutorial-card");
            _complete = root.Q<VisualElement>("complete-card");
            _clearBadge = root.Q<VisualElement>("clear-badge");
            _stepDots = new VisualElement[StepCount];
            for (int index = 0; index < StepCount; index++)
                _stepDots[index] = root.Q<VisualElement>($"step-dot-{index}");

            if (_card == null || _complete == null || _clearBadge == null || _progressFill == null ||
                _stepCounter == null || _title == null || _key == null || _keyShadow == null ||
                _instruction == null || _progressText == null)
                return false;

            for (int index = 0; index < _stepDots.Length; index++)
                if (_stepDots[index] == null) return false;
            return true;
        }

        public void ShowStep(int index, string title, string key, string instruction)
        {
            if (!TryBind())
            {
                StartCoroutine(ShowStepWhenReady(index, title, key, instruction));
                return;
            }

            _card.style.display = DisplayStyle.Flex;
            _complete.style.display = DisplayStyle.None;
            _clearBadge.style.display = DisplayStyle.None;
            _card.RemoveFromClassList("tutorial-card--success");
            _stepCounter.text = $"TRAINING  {index + 1} / {StepCount}";
            _title.text = title;
            _key.text = key;
            _instruction.text = instruction;

            bool usesWideKeyBadge = key != null && key.Length > 8;
            _keyShadow.EnableInClassList("key-shadow--wide", usesWideKeyBadge);
            _key.EnableInClassList("key-badge--wide", usesWideKeyBadge);

            for (int dotIndex = 0; dotIndex < _stepDots.Length; dotIndex++)
            {
                _stepDots[dotIndex].EnableInClassList("step-dot--done", dotIndex < index);
                _stepDots[dotIndex].EnableInClassList("step-dot--active", dotIndex == index);
            }

            if (_nextStepFeedbacks != null) _nextStepFeedbacks.PlayFeedbacks();
        }

        public void SetProgress(float progress01, string text)
        {
            if (_progressFill != null) _progressFill.style.width = Length.Percent(Mathf.Clamp01(progress01) * 100f);
            if (_progressText != null) _progressText.text = text;
        }

        public void SetInstruction(string instruction)
        {
            if (TryBind()) _instruction.text = instruction;
        }

        public void SetKeyAttention(bool active)
        {
            if (!TryBind()) return;
            _keyShadow.EnableInClassList("key-shadow--attention", active);
            _key.EnableInClassList("key-badge--attention", active);
        }

        public void ShowSuccess()
        {
            if (!TryBind()) return;
            _card.AddToClassList("tutorial-card--success");
            _progressText.text = "CLEAR!";
            _clearBadge.style.display = DisplayStyle.Flex;
            if (_stepCompleteFeedbacks != null) _stepCompleteFeedbacks.PlayFeedbacks();
        }

        public void ShowComplete()
        {
            if (!TryBind()) return;
            _card.style.display = DisplayStyle.None;
            _complete.style.display = DisplayStyle.Flex;
            for (int index = 0; index < _stepDots.Length; index++)
            {
                _stepDots[index].RemoveFromClassList("step-dot--active");
                _stepDots[index].AddToClassList("step-dot--done");
            }
        }

        private System.Collections.IEnumerator ShowStepWhenReady(
            int index,
            string title,
            string key,
            string instruction)
        {
            while (!TryBind()) yield return null;
            ShowStep(index, title, key, instruction);
        }
    }
}
