using System;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class StageIntroController : MonoBehaviour
    {
        [Header("Stage Copy")]
        [SerializeField] private string _stageLabel = "DELIVERY RUN";
        [SerializeField] private string _stageTitle = "PENGUIN EXPRESS";
        [SerializeField] private string _stageSubtitle = "ROLL OUT • DELIVER THE GIFTS";

        [Header("Timing")]
        [SerializeField, Range(0.75f, 2.5f)] private float _timingScale = 1.45f;

        [Header("Preview")]
        [SerializeField] private bool _playOnEnable = true;
        [SerializeField] private bool _spaceToReplay = true;
        [SerializeField] private bool _showPreviewHint;

        [Header("Feedback Hooks")]
        [SerializeField] private UnityEvent _countdownTickFeedback = new UnityEvent();
        [SerializeField] private UnityEvent _cleanSignalFeedback = new UnityEvent();
        [SerializeField] private UnityEvent _introCompleted = new UnityEvent();

        private VisualElement _root;
        private VisualElement _backdrop;
        private VisualElement _card;
        private VisualElement _objectiveRow;
        private VisualElement _countdown;
        private VisualElement _countdownFace;
        private VisualElement _countdownPenguin;
        private VisualElement _countdownGift;
        private VisualElement _readyRow;
        private VisualElement _cleanSignal;
        private VisualElement _signalGift;
        private VisualElement _previewHint;
        private Label _stageLabelElement;
        private Label _stageTitleElement;
        private Label _stageSubtitleElement;
        private Label _countdownLabel;
        private VisualElement[] _objectiveChips;
        private Sequence _sequence;

        public bool IsPlaying => _sequence is { active: true };
        public event Action Completed;

        private void OnEnable()
        {
            _root = GetComponent<UIDocument>().rootVisualElement;
            CacheElements();
            ApplyCopy();
            ResetVisuals();

            if (_playOnEnable)
            {
                _root.schedule.Execute(Play).StartingIn(450);
            }
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (_spaceToReplay && keyboard != null && keyboard.spaceKey.wasPressedThisFrame)
            {
                Play();
            }
        }

        private void OnDisable()
        {
            _sequence?.Kill();
            DOTween.Kill(this);
            KillElementTweens();
        }

        public void Play() => Play(0f);

        /// <summary>인트로를 틀되, <paramref name="elapsedSeconds"/> 만큼 이미 흘러간 것으로 치고 그 지점부터 재생한다.
        ///
        /// <para>멀티에서 피어마다 시작 신호를 받는 프레임이 다르기 때문이다. 늦게 받은 피어가 그만큼
        /// 앞서 감면 카운트다운이 모두에게 같은 순간에 끝난다. 싱글은 0 이라 예전과 같다.</para></summary>
        public void Play(float elapsedSeconds)
        {
            if (_root == null)
            {
                return;
            }

            _sequence?.Kill();
            KillElementTweens();
            ResetVisuals();

            _sequence = DOTween.Sequence()
                .SetTarget(this)
                .SetUpdate(true);

            _sequence.Append(FadeTo(_backdrop, 1f, T(0.22f)).SetEase(Ease.OutSine));
            _sequence.Join(MoveTo(_card, Vector2.zero, T(0.32f)).SetEase(Ease.OutCubic));
            _sequence.Join(ScaleTo(_card, 1f, T(0.28f)).SetEase(Ease.OutQuad));
            _sequence.Join(FadeTo(_card, 1f, T(0.18f)).SetEase(Ease.OutSine));

            for (int index = 0; index < _objectiveChips.Length; index++)
            {
                VisualElement chip = _objectiveChips[index];
                float insertTime = T(0.20f + index * 0.07f);
                _sequence.Insert(insertTime, FadeTo(chip, 1f, T(0.16f)).SetEase(Ease.OutSine));
                _sequence.Insert(insertTime, ScaleTo(chip, 1f, T(0.18f)).SetEase(Ease.OutQuad));
            }

            _sequence.AppendInterval(T(0.16f));
            _sequence.Append(ShowCountdown(3, "countdown-red"));
            _sequence.AppendInterval(T(0.40f));
            _sequence.Append(ShowCountdown(2, "countdown-yellow"));
            _sequence.AppendInterval(T(0.40f));
            _sequence.Append(ShowCountdown(1, "countdown-mint"));
            _sequence.AppendInterval(T(0.34f));
            _sequence.AppendCallback(ShowCleanSignal);
            _sequence.AppendInterval(T(0.50f));
            _sequence.Append(FadeTo(_cleanSignal, 0f, T(0.20f)).SetEase(Ease.InSine));
            _sequence.Join(MoveTo(_cleanSignal, new Vector2(0f, -18f), T(0.22f)).SetEase(Ease.InCubic));
            _sequence.Join(ScaleTo(_cleanSignal, 0.98f, T(0.20f)));
            _sequence.Join(FadeTo(_backdrop, 0f, T(0.26f)));
            _sequence.AppendCallback(FinishIntro);

            // 이미 지난 분은 건너뛴다. 전체 길이를 넘기면 그대로 끝까지 감아 즉시 완료된다 —
            // 너무 늦게 합류한 피어가 인트로에 갇히지 않는다.
            if (elapsedSeconds > 0f) _sequence.Goto(elapsedSeconds, true);
        }

        public void SetStageCopy(string stageLabel, string stageTitle, string stageSubtitle)
        {
            _stageLabel = stageLabel;
            _stageTitle = stageTitle;
            _stageSubtitle = stageSubtitle;
            ApplyCopy();
        }

        private void CacheElements()
        {
            _backdrop = _root.Q<VisualElement>("backdrop");
            _card = _root.Q<VisualElement>("intro-card");
            _objectiveRow = _root.Q<VisualElement>("objective-row");
            _countdown = _root.Q<VisualElement>("countdown");
            _countdownFace = _root.Q<VisualElement>("countdown-face");
            _countdownPenguin = _root.Q<VisualElement>(className: "countdown-penguin");
            _countdownGift = _root.Q<VisualElement>(className: "countdown-gift");
            _readyRow = _root.Q<VisualElement>("ready-row");
            _cleanSignal = _root.Q<VisualElement>("clean-signal");
            _signalGift = _root.Q<VisualElement>(className: "signal-gift");
            _previewHint = _root.Q<VisualElement>("preview-hint");
            _stageLabelElement = _root.Q<Label>("stage-label");
            _stageTitleElement = _root.Q<Label>("stage-title");
            _stageSubtitleElement = _root.Q<Label>("stage-subtitle");
            _countdownLabel = _root.Q<Label>("countdown-label");
            _objectiveChips = new[]
            {
                _root.Q<VisualElement>("floor-chip"),
                _root.Q<VisualElement>("trash-chip"),
                _root.Q<VisualElement>("perfect-chip")
            };
        }

        private void ApplyCopy()
        {
            if (_stageLabelElement == null)
            {
                return;
            }

            _stageLabelElement.text = _stageLabel;
            _stageTitleElement.text = _stageTitle;
            _stageSubtitleElement.text = _stageSubtitle;
        }

        private void ResetVisuals()
        {
            _backdrop.style.opacity = 0f;
            _card.style.display = DisplayStyle.Flex;
            _card.style.opacity = 0f;
            _card.style.translate = new Translate(0f, -28f, 0f);
            _card.style.scale = new Scale(new Vector2(0.98f, 0.98f));
            _objectiveRow.style.opacity = 1f;

            foreach (VisualElement chip in _objectiveChips)
            {
                chip.style.opacity = 0f;
                chip.style.scale = new Scale(new Vector2(0.96f, 0.96f));
            }

            _countdown.style.display = DisplayStyle.None;
            _countdown.style.opacity = 0f;
            _countdown.style.scale = new Scale(new Vector2(0.90f, 0.90f));
            _countdownPenguin.style.translate = new Translate(0f, 0f, 0f);
            _countdownGift.style.scale = new Scale(Vector2.one);
            _readyRow.style.opacity = 0f;
            _cleanSignal.style.display = DisplayStyle.None;
            _cleanSignal.style.opacity = 0f;
            _cleanSignal.style.translate = new Translate(0f, 18f, 0f);
            _cleanSignal.style.scale = new Scale(new Vector2(0.96f, 0.96f));
            _previewHint.style.opacity = _spaceToReplay && _showPreviewHint ? 0.88f : 0f;
        }

        private Tween ShowCountdown(int value, string colorClass)
        {
            Sequence beat = DOTween.Sequence()
                .SetTarget(_countdown)
                .SetUpdate(true);

            beat.AppendCallback(() =>
            {
                _countdownFace.RemoveFromClassList("countdown-red");
                _countdownFace.RemoveFromClassList("countdown-yellow");
                _countdownFace.RemoveFromClassList("countdown-mint");
                _countdownFace.AddToClassList(colorClass);
                _countdownLabel.text = value.ToString();
                _countdown.style.display = DisplayStyle.Flex;
                _countdown.style.opacity = 1f;
                _countdown.style.scale = new Scale(new Vector2(0.90f, 0.90f));
                _countdownPenguin.style.translate = new Translate(-6f, 7f, 0f);
                _countdownGift.style.scale = new Scale(new Vector2(0.86f, 0.86f));
                _readyRow.style.opacity = 1f;
                _countdownTickFeedback.Invoke();
            });
            beat.Append(ScaleTo(_countdown, 1.04f, T(0.12f)).SetEase(Ease.OutQuad));
            beat.Join(MoveTo(_countdownPenguin, Vector2.zero, T(0.12f)).SetEase(Ease.OutBack));
            beat.Join(ScaleTo(_countdownGift, 1.08f, T(0.12f)).SetEase(Ease.OutBack));
            beat.Append(ScaleTo(_countdown, 1f, T(0.12f)).SetEase(Ease.InOutSine));
            beat.Join(ScaleTo(_countdownGift, 1f, T(0.12f)).SetEase(Ease.InOutSine));
            return beat;
        }

        private void ShowCleanSignal()
        {
            _countdown.style.display = DisplayStyle.None;
            _card.style.display = DisplayStyle.None;
            _card.style.opacity = 0f;
            _readyRow.style.opacity = 0f;
            _cleanSignal.style.display = DisplayStyle.Flex;
            _cleanSignal.style.opacity = 1f;
            _cleanSignal.style.translate = new Translate(0f, 18f, 0f);
            _cleanSignal.style.scale = new Scale(new Vector2(0.96f, 0.96f));
            _signalGift.style.scale = new Scale(new Vector2(0.86f, 0.86f));
            _cleanSignalFeedback.Invoke();

            DOTween.Sequence()
                .SetTarget(_cleanSignal)
                .SetUpdate(true)
                .Append(MoveTo(_cleanSignal, Vector2.zero, T(0.18f)).SetEase(Ease.OutCubic))
                .Join(ScaleTo(_cleanSignal, 1f, T(0.18f)).SetEase(Ease.OutQuad))
                .Join(ScaleTo(_signalGift, 1.08f, T(0.18f)).SetEase(Ease.OutBack))
                .Append(ScaleTo(_signalGift, 1f, T(0.10f)).SetEase(Ease.InOutSine));
        }

        private float T(float seconds)
        {
            return seconds * Mathf.Max(0.01f, _timingScale);
        }

        private void FinishIntro()
        {
            _cleanSignal.style.display = DisplayStyle.None;
            _backdrop.style.opacity = 0f;
            Completed?.Invoke();
            _introCompleted.Invoke();
        }

        private void KillElementTweens()
        {
            DOTween.Kill(_backdrop);
            DOTween.Kill(_card);
            DOTween.Kill(_countdown);
            DOTween.Kill(_countdownPenguin);
            DOTween.Kill(_countdownGift);
            DOTween.Kill(_cleanSignal);
            DOTween.Kill(_signalGift);

            if (_objectiveChips == null)
            {
                return;
            }

            foreach (VisualElement chip in _objectiveChips)
            {
                DOTween.Kill(chip);
            }
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
