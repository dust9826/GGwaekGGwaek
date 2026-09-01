using System.Collections;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// TutorialPlay의 비차단형 길라잡이. 퀘스트 시작 때 짧게 말하고, 같은 단계에서
    /// 오래 머무르면 한 번만 간단한 힌트를 다시 보여 준다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(UIDocument))]
    public sealed class TutorialSantaGuide : MonoBehaviour
    {
        [SerializeField] private Texture2D _portrait;
        [SerializeField] private AudioSource _voiceSource;
        [SerializeField] private AudioClip[] _stepVoiceClips = new AudioClip[8];
        [SerializeField] private AudioClip _completeVoiceClip;
        [SerializeField, Min(1f)] private float _messageDurationSeconds = 6f;
        [SerializeField, Min(5f)] private float _stallReminderDelaySeconds = 18f;

        private UIDocument _document;
        private VisualElement _card;
        private VisualElement _portraitElement;
        private Label _eyebrow;
        private Label _title;
        private Label _message;
        private Coroutine _hideRoutine;
        private Coroutine _reminderRoutine;
        private int _revision;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            TryBind();
        }

        private void OnDisable()
        {
            _revision++;
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            if (_reminderRoutine != null) StopCoroutine(_reminderRoutine);
            _hideRoutine = null;
            _reminderRoutine = null;
        }

        public void Configure(Texture2D portrait, AudioSource voiceSource)
        {
            _portrait = portrait;
            _voiceSource = voiceSource;
        }

        public void ShowStep(int stepIndex, string title, string message, string reminder)
        {
            int revision = ++_revision;
            StopPresentationCoroutines();
            StartCoroutine(PresentWhenReady(revision, "SANTA'S GUIDE", title, message,
                StepClip(stepIndex), _messageDurationSeconds));
            _reminderRoutine = StartCoroutine(RemindAfterDelay(
                revision, title, reminder, _stallReminderDelaySeconds));
        }

        public void ShowComplete(string message)
        {
            int revision = ++_revision;
            StopPresentationCoroutines();
            StartCoroutine(PresentWhenReady(revision, "DELIVERY COMPLETE", "훌륭하구나!", message,
                _completeVoiceClip, _messageDurationSeconds + 2f));
        }

        private IEnumerator PresentWhenReady(int revision, string eyebrow, string title,
            string message, AudioClip voiceClip, float duration)
        {
            while (!TryBind()) yield return null;
            if (revision != _revision) yield break;

            _eyebrow.text = eyebrow;
            _title.text = title;
            _message.text = message;
            _card.AddToClassList("santa-guide--visible");
            PlayVoice(voiceClip);
            _hideRoutine = StartCoroutine(HideAfterDelay(revision, duration));
        }

        private IEnumerator RemindAfterDelay(int revision, string title, string reminder, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (revision != _revision || string.IsNullOrWhiteSpace(reminder)) yield break;
            yield return PresentWhenReady(revision, "SANTA'S HINT", title, reminder, null,
                _messageDurationSeconds);
        }

        private IEnumerator HideAfterDelay(int revision, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (revision == _revision && _card != null)
                _card.RemoveFromClassList("santa-guide--visible");
            _hideRoutine = null;
        }

        private bool TryBind()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) return false;

            _card = root.Q<VisualElement>("santa-guide");
            _portraitElement = root.Q<VisualElement>("santa-portrait");
            _eyebrow = root.Q<Label>("santa-eyebrow");
            _title = root.Q<Label>("santa-title");
            _message = root.Q<Label>("santa-message");
            if (_card == null || _portraitElement == null || _eyebrow == null ||
                _title == null || _message == null)
                return false;

            if (_portrait != null)
                _portraitElement.style.backgroundImage = new StyleBackground(_portrait);
            return true;
        }

        private void StopPresentationCoroutines()
        {
            if (_hideRoutine != null) StopCoroutine(_hideRoutine);
            if (_reminderRoutine != null) StopCoroutine(_reminderRoutine);
            _hideRoutine = null;
            _reminderRoutine = null;
            if (_card != null) _card.RemoveFromClassList("santa-guide--visible");
            if (_voiceSource != null) _voiceSource.Stop();
        }

        private AudioClip StepClip(int stepIndex)
        {
            return _stepVoiceClips != null && stepIndex >= 0 && stepIndex < _stepVoiceClips.Length
                ? _stepVoiceClips[stepIndex]
                : null;
        }

        private void PlayVoice(AudioClip clip)
        {
            if (_voiceSource == null || clip == null) return;
            _voiceSource.Stop();
            _voiceSource.clip = clip;
            _voiceSource.Play();
        }
    }
}
