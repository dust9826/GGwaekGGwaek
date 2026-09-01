using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class StageOutroController : MonoBehaviour
    {
        // 별 3개를 0.18초 간격으로 점등하던 구간을 그대로 물려받은 길이다. 카드 등장부터
        // 버튼이 눌리기까지의 호흡이 이전과 같아야 한다.
        private const float ScoreRevealSeconds = 0.34f;
        private const float CountUpSeconds = 0.52f;

        [Header("Result Copy")]
        [SerializeField] private string _stageLabel = "STAGE 01";
        [SerializeField] private string _stageTitle = "WINTER VILLAGE";
        [SerializeField] private string _resultSubtitle = "SNOW ROUTE SERVICE REPORT";

        [Header("Result")]
        [SerializeField, Min(0)] private int _score = 240;
        [SerializeField, Min(0)] private int _highScore = 310;
        [SerializeField] private bool _isNewRecord;
        [SerializeField, Range(0, 100)] private int _clearPercent = 92;
        [SerializeField] private string _timeText = "03:42";

        [Header("Timing")]
        [SerializeField, Range(0.75f, 2.5f)] private float _timingScale = 1.55f;

        [Header("Preview")]
        [SerializeField] private bool _playOnEnable = true;
        [SerializeField] private bool _numberKeysPreview = true;
        [SerializeField] private bool _showPreviewHint;

        [Header("Action Hooks")]
        [SerializeField] private UnityEvent _retryRequested = new UnityEvent();
        [SerializeField] private UnityEvent _continueRequested = new UnityEvent();
        [SerializeField] private UnityEvent _outroShown = new UnityEvent();

        private VisualElement _root;
        private VisualElement _backdrop;
        private VisualElement _card;
        private VisualElement _statsRow;
        private VisualElement _actionRow;
        private VisualElement _previewHint;
        private VisualElement _scoreBlock;
        private VisualElement _newRecord;
        private Label _stageLabelElement;
        private Label _stageTitleElement;
        private Label _resultSubtitleElement;
        private Label _scoreValue;
        private Label _highScoreLabel;
        private Label _clearValue;
        private Label _timeValue;
        private Button _retryButton;
        private Button _continueButton;
        private Sequence _sequence;

        public bool IsPlaying => _sequence is { active: true };

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            CacheElements();
            BindButtons();
            ApplyResultCopy();
            ResetVisuals();

            if (_playOnEnable)
            {
                _root.schedule.Execute(Play).StartingIn(350);
            }
        }

        private void Update()
        {
            if (!_numberKeysPreview || Keyboard.current == null)
            {
                return;
            }

            // 1 = 기록을 못 깬 판, 2 = 신기록. 결과 화면이 갈라지는 지점은 이 둘뿐이다.
            if (Keyboard.current.digit1Key.wasPressedThisFrame)
            {
                SetResult(_score, _highScore, false, _clearPercent, _timeText);
            }
            else if (Keyboard.current.digit2Key.wasPressedThisFrame)
            {
                SetResult(_score, _score, true, _clearPercent, _timeText);
            }
        }

        private void OnDisable()
        {
            _sequence?.Kill();
            KillElementTweens();
            UnbindButtons();
        }

        public void Play()
        {
            if (_root == null)
            {
                return;
            }

            _sequence?.Kill();
            KillElementTweens();
            ApplyResultCopy();
            ResetVisuals();

            _sequence = DOTween.Sequence()
                .SetTarget(this)
                .SetUpdate(true);

            _sequence.Append(FadeTo(_backdrop, 1f, T(0.22f)).SetEase(Ease.OutSine));
            _sequence.Join(FadeTo(_card, 1f, T(0.20f)).SetEase(Ease.OutSine));
            _sequence.Join(MoveTo(_card, Vector2.zero, T(0.30f)).SetEase(Ease.OutCubic));
            _sequence.Join(ScaleTo(_card, 1f, T(0.26f)).SetEase(Ease.OutQuad));
            _sequence.Insert(T(0.20f), FadeTo(_statsRow, 1f, T(0.18f)).SetEase(Ease.OutSine));

            _sequence.Insert(T(ScoreRevealSeconds), FadeTo(_scoreBlock, 1f, T(0.16f)).SetEase(Ease.OutSine));
            _sequence.Insert(T(ScoreRevealSeconds), CountUpScore(T(CountUpSeconds)));

            // 숫자가 멈추는 순간 한 번 튄다. 카운트업만 있으면 어디서 끝났는지 눈이 못 잡는다.
            float finishTime = T(ScoreRevealSeconds + CountUpSeconds);
            _sequence.Insert(finishTime, ScaleTo(_scoreValue, 1.08f, T(0.12f)).SetEase(Ease.OutQuad));
            _sequence.Insert(finishTime + T(0.12f), ScaleTo(_scoreValue, 1f, T(0.12f)).SetEase(Ease.InOutSine));
            _sequence.Insert(finishTime, FadeTo(_highScoreLabel, 1f, T(0.18f)).SetEase(Ease.OutSine));

            if (_isNewRecord)
            {
                _sequence.Insert(finishTime + T(0.06f), FadeTo(_newRecord, 1f, T(0.14f)).SetEase(Ease.OutSine));
                _sequence.Insert(finishTime + T(0.06f), ScaleTo(_newRecord, 1.12f, T(0.16f)).SetEase(Ease.OutBack));
                _sequence.Insert(finishTime + T(0.22f), ScaleTo(_newRecord, 1f, T(0.12f)).SetEase(Ease.InOutSine));
            }

            _sequence.Insert(finishTime + T(0.12f), FadeTo(_actionRow, 1f, T(0.20f)).SetEase(Ease.OutSine));
            _sequence.InsertCallback(finishTime + T(0.14f), () => _outroShown.Invoke());
        }

        private float T(float seconds)
        {
            return seconds * Mathf.Max(0.01f, _timingScale);
        }

        public void SetResult(int score, int highScore, bool isNewRecord, int clearPercent, string timeText,
            bool replay = true)
        {
            _score = Mathf.Max(0, score);
            // 화면의 BEST 가 방금 띄운 점수보다 낮으면 그건 버그로 읽힌다. 표시 단계에서 막는다.
            _highScore = Mathf.Max(_score, highScore);
            _isNewRecord = isNewRecord;
            _clearPercent = Mathf.Clamp(clearPercent, 0, 100);
            _timeText = string.IsNullOrWhiteSpace(timeText) ? "--:--" : timeText;
            ApplyResultCopy();

            if (replay)
            {
                Play();
            }
        }

        public void SetStageCopy(string stageLabel, string stageTitle, string resultSubtitle)
        {
            _stageLabel = stageLabel;
            _stageTitle = stageTitle;
            _resultSubtitle = resultSubtitle;
            ApplyResultCopy();
        }

        private void CacheElements()
        {
            _backdrop = _root.Q<VisualElement>("backdrop");
            _card = _root.Q<VisualElement>("result-card");
            _statsRow = _root.Q<VisualElement>("stats-row");
            _actionRow = _root.Q<VisualElement>("action-row");
            _previewHint = _root.Q<VisualElement>("preview-hint");
            _stageLabelElement = _root.Q<Label>("stage-label");
            _stageTitleElement = _root.Q<Label>("stage-title");
            _resultSubtitleElement = _root.Q<Label>("result-subtitle");
            _scoreBlock = _root.Q<VisualElement>("score-block");
            _newRecord = _root.Q<VisualElement>("new-record");
            _scoreValue = _root.Q<Label>("score-value");
            _highScoreLabel = _root.Q<Label>("high-score");
            _clearValue = _root.Q<Label>("clear-value");
            _timeValue = _root.Q<Label>("time-value");
            _retryButton = _root.Q<Button>("retry-button");
            _continueButton = _root.Q<Button>("continue-button");
        }

        private void BindButtons()
        {
            _retryButton.clicked += OnRetryClicked;
            _continueButton.clicked += OnContinueClicked;
        }

        private void UnbindButtons()
        {
            if (_retryButton != null)
            {
                _retryButton.clicked -= OnRetryClicked;
            }

            if (_continueButton != null)
            {
                _continueButton.clicked -= OnContinueClicked;
            }
        }

        private void OnRetryClicked()
        {
            _retryRequested.Invoke();
        }

        private void OnContinueClicked()
        {
            _continueRequested.Invoke();
        }

        private void ApplyResultCopy()
        {
            if (_stageLabelElement == null)
            {
                return;
            }

            _stageLabelElement.text = _stageLabel;
            _stageTitleElement.text = _stageTitle;
            _resultSubtitleElement.text = _resultSubtitle;
            _clearValue.text = $"{_clearPercent}%";
            _timeValue.text = _timeText;
            _scoreValue.text = _score.ToString("N0");
            _highScoreLabel.text = $"BEST  {_highScore:N0}";
        }

        private void ResetVisuals()
        {
            _backdrop.style.opacity = 0f;
            _card.style.opacity = 0f;
            _card.style.translate = new Translate(0f, 24f, 0f);
            _card.style.scale = new Scale(new Vector2(0.98f, 0.98f));
            _statsRow.style.opacity = 0f;
            _scoreBlock.style.opacity = 0f;
            _scoreValue.style.scale = new Scale(Vector2.one);
            _highScoreLabel.style.opacity = 0f;
            _newRecord.style.opacity = 0f;
            _newRecord.style.scale = new Scale(new Vector2(0.8f, 0.8f));
            _actionRow.style.opacity = 0f;
            _previewHint.style.opacity = _numberKeysPreview && _showPreviewHint ? 0.88f : 0f;
        }

        /// <summary>0에서 결과 점수까지 굴린다. 대상을 <c>_scoreValue</c>로 잡아 두면
        /// <see cref="KillElementTweens"/>가 다른 트윈과 같은 방식으로 거둬 간다.</summary>
        private Tweener CountUpScore(float duration)
        {
            float value = 0f;
            _scoreValue.text = "0";
            return DOTween.To(() => value, next =>
            {
                value = next;
                _scoreValue.text = Mathf.RoundToInt(next).ToString("N0");
            }, _score, duration).SetTarget(_scoreValue).SetEase(Ease.OutCubic);
        }

        private void KillElementTweens()
        {
            DOTween.Kill(this);
            DOTween.Kill(_backdrop);
            DOTween.Kill(_card);
            DOTween.Kill(_statsRow);
            DOTween.Kill(_actionRow);
            DOTween.Kill(_scoreBlock);
            DOTween.Kill(_scoreValue);
            DOTween.Kill(_highScoreLabel);
            DOTween.Kill(_newRecord);
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
