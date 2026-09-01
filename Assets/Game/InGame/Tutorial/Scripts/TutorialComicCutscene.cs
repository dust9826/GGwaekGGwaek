using System;
using System.Collections;
using DG.Tweening;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>튜토리얼 조작 전에 세계관 만화를 클릭할 때마다 한 장씩 보여준다.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TutorialComicCutscene : MonoBehaviour
    {
        public enum ComicDialogueStyle
        {
            Penguin,
            Fairy,
            Santa,
            SoundEffect,
            MagicEffect,
            WhooshEffect
        }

        [Serializable]
        public struct ComicDialogueLine
        {
            public string Speaker;
            [TextArea] public string Text;
            public Vector2 PositionPercent;
            public bool TailPointsRight;
            public ComicDialogueStyle Style;

            public ComicDialogueLine(string speaker, string text, Vector2 positionPercent,
                bool tailPointsRight, ComicDialogueStyle style)
            {
                Speaker = speaker;
                Text = text;
                PositionPercent = positionPercent;
                TailPointsRight = tailPointsRight;
                Style = style;
            }
        }

        private const int MaxCardSlotCount = 8;

        [SerializeField] private Texture2D[] _cardTextures;
        [SerializeField, Range(0.5f, 2f)] private float _timingScale = 1f;
        [SerializeField] private bool _showStartPrompt;
        [Header("Feel Card Reveals")]
        [SerializeField] private MMF_Player _leftRevealFeedbacks;
        [SerializeField] private MMF_Player _rightRevealFeedbacks;
        [Header("Feel Speech Bubble Reveals")]
        [SerializeField] private MMF_Player _leftBubbleRevealFeedbacks;
        [SerializeField] private MMF_Player _rightBubbleRevealFeedbacks;
        [Header("Dialogue")]
        [SerializeField] private ComicDialogueLine[] _dialogueLines;
        [Header("SFX")]
        [SerializeField] private AudioClip _cardRevealSfx;
        [SerializeField] private AudioClip _bubbleRevealSfx;
        [SerializeField] private AudioClip _magicRevealSfx;
        [SerializeField] private AudioClip _whooshRevealSfx;
        [SerializeField] private AudioSource _sfxSource;
        [SerializeField, Range(0f, 1f)] private float _cardRevealSfxVolume = 0.5f;
        [SerializeField, Range(0f, 1f)] private float _bubbleRevealSfxVolume = 0.34f;

        private VisualElement _container;
        private VisualElement _stage;
        private VisualElement _startPrompt;
        private VisualElement _startPromptFace;
        private VisualElement _startMouseButton;
        private VisualElement _skipHint;
        private Label _nextHint;
        private VisualElement[] _cards;
        private readonly VisualElement[] _speechBubbles = new VisualElement[2];
        private readonly VisualElement[] _speechBubbleTails = new VisualElement[2];
        private readonly Label[] _speechBubbleSpeakers = new Label[2];
        private readonly Label[] _speechBubbleTexts = new Label[2];
        private Action<MeshGenerationContext>[] _cardRenderers;
        private Sequence _sequence;
        private Sequence _startPromptLoop;
        private int _currentCardIndex = -1;
        private readonly int[] _feelDrivenCardIndices = { -1, -1 };
        private readonly bool[] _feelDriverActive = new bool[2];
        private readonly bool[] _bubbleFeelDriverActive = new bool[2];
        private readonly bool[] _bubbleVisible = new bool[2];
        private bool _isTransitioning;
        private bool _isAwaitingStart;
        private bool _isPlaying;
        private bool _isComplete;

        public bool IsPlaying => _isPlaying;
        public bool IsComplete => _isComplete;
        public int CurrentCardIndex => _currentCardIndex;
        public int CardCount => _cardTextures?.Length ?? 0;
        public int DialogueCount => _dialogueLines?.Length ?? 0;

        public void ConfigureDialogue(ComicDialogueLine[] lines)
        {
            _dialogueLines = lines == null ? Array.Empty<ComicDialogueLine>() :
                (ComicDialogueLine[])lines.Clone();
        }

        private void OnEnable()
        {
            ConfigureSfxSource();
            CacheElements();
            RegisterCardRenderers();
            ApplyTextures();
            ResetVisuals();
            _stage?.RegisterCallback<ClickEvent>(OnClick);
        }

        private void Update()
        {
            UpdateFeelDrivers();
            UpdateBubbleFeelDrivers();

            Keyboard keyboard = Keyboard.current;
            if (_isPlaying && keyboard != null && keyboard.escapeKey.wasPressedThisFrame) Skip();

            Mouse mouse = Mouse.current;
            if (_isPlaying && mouse != null && mouse.leftButton.wasPressedThisFrame) Advance();
        }

        private void OnDisable()
        {
            _stage?.UnregisterCallback<ClickEvent>(OnClick);
            UnregisterCardRenderers();
            _sequence?.Kill();
            _startPromptLoop?.Kill();
            KillCardTweens();
            StopFeelReveals();
            StopBubbleReveals();
            if (_isPlaying)
            {
                _isPlaying = false;
                _isComplete = true;
            }
        }

        public IEnumerator PlayAndWait()
        {
            Play();
            while (!_isComplete && isActiveAndEnabled) yield return null;
        }

        public void Play()
        {
            // UIDocument가 런타임에 패널을 다시 붙인 경우까지 포함해 매 재생마다
            // 최신 VisualElement를 다시 잡는다. 씬 진입 직후 컷신이 조용히 건너뛰는 것을 막는다.
            UnregisterCardRenderers();
            CacheElements();
            RegisterCardRenderers();
            ApplyTextures();
            if (!HasValidCards())
            {
                Debug.LogError(
                    $"{nameof(TutorialComicCutscene)}: 만화 카드는 2~{MaxCardSlotCount}장의 짝수 묶음이어야 하며 모두 연결되어야 한다. 현재 {CardCount}장.",
                    this);
                _isComplete = true;
                return;
            }

            _sequence?.Kill();
            KillCardTweens();
            ResetVisuals();
            _isPlaying = true;
            _isComplete = false;
            _isTransitioning = true;
            _isAwaitingStart = _showStartPrompt;
            _currentCardIndex = -1;
            _container.style.display = DisplayStyle.Flex;

            _sequence = DOTween.Sequence().SetTarget(this).SetUpdate(true);
            _sequence.Append(FadeTo(_stage, 1f, T(0.22f)).SetEase(Ease.OutSine));
            if (_showStartPrompt)
            {
                _startPrompt.style.display = DisplayStyle.Flex;
                _startPrompt.style.opacity = 0f;
                _startPrompt.style.scale = new Scale(Vector2.one * 0.9f);
                _sequence.Append(FadeTo(_startPrompt, 1f, T(0.16f)).SetEase(Ease.OutSine));
                _sequence.Join(ScaleTo(_startPrompt, Vector2.one, T(0.24f)).SetEase(Ease.OutBack));
                _sequence.OnComplete(() =>
                {
                    _isTransitioning = false;
                    StartPromptLoop();
                });
                return;
            }

            // 튜토리얼에 들어오자마자 이야기의 첫 장면을 보여 준다. 시작 버튼을 한 번 더
            // 요구하면 첫 클릭의 의미가 '시작'인지 '다음 컷'인지 모호해진다.
            _sequence.AppendCallback(() => _currentCardIndex = 0);
            _sequence.Append(ShowCard(0));
            _sequence.OnComplete(() =>
            {
                _isTransitioning = false;
                ShowNextHint();
            });
        }

        public void Advance()
        {
            if (!_isPlaying || _isTransitioning) return;

            if (_isAwaitingStart)
            {
                BeginComicAnimation();
                return;
            }

            if (_currentCardIndex >= _cards.Length - 1)
            {
                _isTransitioning = true;
                SetNextHintVisible(false);
                _sequence = DOTween.Sequence().SetTarget(this).SetUpdate(true)
                    .Append(HideSpeechBubbles())
                    .Append(FadeTo(_stage, 0f, T(0.20f)).SetEase(Ease.InSine))
                    .AppendCallback(Finish);
                return;
            }

            _isTransitioning = true;
            SetNextHintVisible(false);
            int nextCardIndex = _currentCardIndex + 1;
            _sequence = DOTween.Sequence().SetTarget(this).SetUpdate(true);
            if (nextCardIndex % 2 == 0)
                _sequence.Append(HidePair(_currentCardIndex - 1, _currentCardIndex));

            _sequence.AppendCallback(() => _currentCardIndex = nextCardIndex);
            _sequence.Append(ShowCard(nextCardIndex));
            _sequence.OnComplete(() =>
            {
                _isTransitioning = false;
                ShowNextHint();
            });
        }

        private void BeginComicAnimation()
        {
            _isAwaitingStart = false;
            _isTransitioning = true;
            _startPromptLoop?.Kill();
            _startPromptLoop = null;

            _sequence = DOTween.Sequence().SetTarget(this).SetUpdate(true);
            _sequence.Append(ScaleTo(_startPrompt, Vector2.one * 0.94f, T(0.08f)).SetEase(Ease.InQuad));
            _sequence.Join(FadeTo(_startPrompt, 0f, T(0.12f)).SetEase(Ease.InSine));
            _sequence.AppendCallback(() =>
            {
                _startPrompt.style.display = DisplayStyle.None;
                _currentCardIndex = 0;
            });
            _sequence.Append(ShowCard(0));
            _sequence.OnComplete(() =>
            {
                _isTransitioning = false;
                ShowNextHint();
            });
        }

        private void StartPromptLoop()
        {
            _startPromptLoop?.Kill();
            _startPromptLoop = DOTween.Sequence().SetTarget(_startPrompt).SetUpdate(true);
            _startPromptLoop.Append(ScaleTo(_startPrompt, Vector2.one * 1.035f, T(0.34f)).SetEase(Ease.OutSine));
            _startPromptLoop.Join(MoveTo(_startPromptFace, new Vector2(0f, -3f), T(0.34f)).SetEase(Ease.OutSine));
            _startPromptLoop.Join(ScaleTo(_startMouseButton, new Vector2(1f, 0.76f), T(0.16f)).SetEase(Ease.InQuad));
            _startPromptLoop.Append(ScaleTo(_startMouseButton, Vector2.one, T(0.16f)).SetEase(Ease.OutBack));
            _startPromptLoop.Append(ScaleTo(_startPrompt, Vector2.one, T(0.34f)).SetEase(Ease.InOutSine));
            _startPromptLoop.Join(MoveTo(_startPromptFace, Vector2.zero, T(0.34f)).SetEase(Ease.InOutSine));
            _startPromptLoop.AppendInterval(T(0.32f));
            _startPromptLoop.SetLoops(-1, LoopType.Restart);
        }

        public void Skip()
        {
            if (!_isPlaying) return;

            _sequence?.Kill();
            _startPromptLoop?.Kill();
            KillCardTweens();
            StopBubbleReveals();
            _isTransitioning = true;
            SetNextHintVisible(false);
            _sequence = DOTween.Sequence()
                .SetTarget(this)
                .SetUpdate(true)
                .Append(HideSpeechBubbles())
                .Append(FadeTo(_stage, 0f, T(0.16f)).SetEase(Ease.InSine))
                .AppendCallback(Finish);
        }

        private Sequence ShowCard(int index)
        {
            bool isLeftCard = index % 2 == 0;
            float duration = isLeftCard ? 0.58f : 0.54f;
            VisualElement card = _cards[index];
            Sequence reveal = DOTween.Sequence().SetTarget(card).SetUpdate(true);
            reveal.AppendCallback(() =>
            {
                card.style.display = DisplayStyle.Flex;
                card.style.opacity = 0f;
                card.style.translate = new Translate(0f, 0f, 0f);
                card.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
                card.style.scale = new Scale(Vector2.one);
                PlayCardRevealSfx();
                StartFeelReveal(index, duration);
            });
            reveal.Append(FadeTo(card, 1f, T(0.30f)).SetEase(Ease.OutQuad));
            reveal.AppendInterval(Mathf.Max(0f, T(duration) - T(0.30f)));
            if (HasDialogueLine(index))
            {
                reveal.AppendInterval(T(0.18f));
                reveal.Append(ShowSpeechBubble(index));
            }
            return reveal;
        }

        private void ConfigureSfxSource()
        {
            if (_sfxSource == null) _sfxSource = GetComponent<AudioSource>();
            if (_sfxSource == null) _sfxSource = gameObject.AddComponent<AudioSource>();
            _sfxSource.playOnAwake = false;
            _sfxSource.loop = false;
            _sfxSource.spatialBlend = 0f;
            _sfxSource.dopplerLevel = 0f;
            _sfxSource.ignoreListenerPause = true;
        }

        private void PlayCardRevealSfx()
        {
            if (_sfxSource == null || _cardRevealSfx == null) return;
            _sfxSource.pitch = 1f;
            _sfxSource.PlayOneShot(_cardRevealSfx, _cardRevealSfxVolume);
        }

        private void PlayDialogueSfx(ComicDialogueStyle style)
        {
            if (_sfxSource == null) return;
            AudioClip clip = style switch
            {
                ComicDialogueStyle.MagicEffect => _magicRevealSfx != null ? _magicRevealSfx : _bubbleRevealSfx,
                ComicDialogueStyle.WhooshEffect => _whooshRevealSfx != null ? _whooshRevealSfx : _bubbleRevealSfx,
                _ => _bubbleRevealSfx
            };
            if (clip == null) return;

            _sfxSource.pitch = style == ComicDialogueStyle.SoundEffect ? 1.04f : 1f;
            _sfxSource.PlayOneShot(clip, _bubbleRevealSfxVolume);
        }

        private void StartFeelReveal(int cardIndex, float duration)
        {
            int side = cardIndex % 2;
            MMF_Player player = side == 0 ? _leftRevealFeedbacks : _rightRevealFeedbacks;
            if (player == null) return;

            player.StopFeedbacks();
            ConfigureFeelTiming(player, T(duration));

            Transform driver = player.transform;
            driver.localPosition = side == 0 ? new Vector3(-38f, 4f, 0f) : new Vector3(44f, -3f, 0f);
            driver.localScale = Vector3.one * (side == 0 ? 0.982f : 0.978f);
            driver.localRotation = Quaternion.Euler(0f, 0f, side == 0 ? -0.8f : 0.9f);

            _feelDrivenCardIndices[side] = cardIndex;
            _feelDriverActive[side] = true;
            ApplyFeelDriver(side);
            player.PlayFeedbacks();
        }

        private void UpdateFeelDrivers()
        {
            for (int side = 0; side < _feelDriverActive.Length; side++)
            {
                if (!_feelDriverActive[side]) continue;

                MMF_Player player = side == 0 ? _leftRevealFeedbacks : _rightRevealFeedbacks;
                ApplyFeelDriver(side);
                if (player != null && player.HasFeedbackStillPlaying()) continue;

                ResetFeelDrivenCard(side);
            }
        }

        private void ApplyFeelDriver(int side)
        {
            int cardIndex = _feelDrivenCardIndices[side];
            MMF_Player player = side == 0 ? _leftRevealFeedbacks : _rightRevealFeedbacks;
            if (player == null || _cards == null || cardIndex < 0 || cardIndex >= _cards.Length) return;

            Transform driver = player.transform;
            Vector3 position = driver.localPosition;
            Vector3 scale = driver.localScale;
            float angle = NormalizeSignedAngle(driver.localEulerAngles.z);
            VisualElement card = _cards[cardIndex];
            card.style.translate = new Translate(position.x, position.y, 0f);
            card.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            card.style.scale = new Scale(new Vector2(scale.x, scale.y));
        }

        private void ResetFeelDrivenCard(int side)
        {
            int cardIndex = _feelDrivenCardIndices[side];
            if (_cards != null && cardIndex >= 0 && cardIndex < _cards.Length)
            {
                VisualElement card = _cards[cardIndex];
                card.style.translate = new Translate(0f, 0f, 0f);
                card.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
                card.style.scale = new Scale(Vector2.one);
            }

            MMF_Player player = side == 0 ? _leftRevealFeedbacks : _rightRevealFeedbacks;
            if (player != null)
            {
                player.transform.localPosition = Vector3.zero;
                player.transform.localScale = Vector3.one;
                player.transform.localRotation = Quaternion.identity;
            }

            _feelDrivenCardIndices[side] = -1;
            _feelDriverActive[side] = false;
        }

        private void StopFeelReveals()
        {
            _leftRevealFeedbacks?.StopFeedbacks();
            _rightRevealFeedbacks?.StopFeedbacks();
            ResetFeelDrivenCard(0);
            ResetFeelDrivenCard(1);
        }

        private static void ConfigureFeelTiming(MMF_Player player, float duration)
        {
            if (player.FeedbacksList == null) return;
            foreach (MMF_Feedback feedback in player.FeedbacksList)
            {
                switch (feedback)
                {
                    case MMF_Position position:
                        position.AnimatePositionDuration = duration * 0.86f;
                        break;
                    case MMF_Scale scale:
                        scale.AnimateScaleDuration = duration;
                        break;
                    case MMF_Rotation rotation:
                        rotation.AnimateRotationDuration = duration * 0.92f;
                        break;
                }
            }
        }

        private bool HasDialogueLine(int cardIndex)
        {
            return _dialogueLines != null && cardIndex >= 0 && cardIndex < _dialogueLines.Length &&
                   !string.IsNullOrWhiteSpace(_dialogueLines[cardIndex].Text);
        }

        private Sequence ShowSpeechBubble(int cardIndex)
        {
            int side = cardIndex % 2;
            VisualElement bubble = _speechBubbles[side];
            ComicDialogueLine line = _dialogueLines[cardIndex];
            MMF_Player feelPlayer = side == 0 ? _leftBubbleRevealFeedbacks : _rightBubbleRevealFeedbacks;
            bool hasFeel = feelPlayer != null;
            float opacity = 0f;
            Vector2 fallbackOffset = BubbleInitialOffset(side, line.Style);
            Vector2 fallbackScale = Vector2.one * 0.82f;
            float fallbackAngle = BubbleInitialAngle(side, line.Style);

            Sequence reveal = DOTween.Sequence().SetTarget(bubble).SetUpdate(true);
            reveal.AppendCallback(() =>
            {
                PrepareSpeechBubble(side, line);
                PlayDialogueSfx(line.Style);
                if (hasFeel)
                    StartBubbleFeelReveal(side, line.Style);
                else
                {
                    bubble.style.translate = new Translate(fallbackOffset.x, fallbackOffset.y, 0f);
                    bubble.style.scale = new Scale(fallbackScale);
                    bubble.style.rotate = new Rotate(new Angle(fallbackAngle, AngleUnit.Degree));
                }
            });
            reveal.Append(DOTween.To(() => opacity, next =>
            {
                opacity = next;
                bubble.style.opacity = next;
            }, 1f, T(0.16f)).SetTarget(bubble).SetEase(Ease.OutQuad));

            if (!hasFeel)
            {
                reveal.Join(DOTween.To(() => fallbackOffset, next =>
                {
                    fallbackOffset = next;
                    bubble.style.translate = new Translate(next.x, next.y, 0f);
                }, Vector2.zero, T(0.28f)).SetTarget(bubble).SetEase(Ease.OutCubic));
                reveal.Join(DOTween.To(() => fallbackScale, next =>
                {
                    fallbackScale = next;
                    bubble.style.scale = new Scale(next);
                }, Vector2.one, T(0.28f)).SetTarget(bubble).SetEase(Ease.OutBack));
                reveal.Join(DOTween.To(() => fallbackAngle, next =>
                {
                    fallbackAngle = next;
                    bubble.style.rotate = new Rotate(new Angle(next, AngleUnit.Degree));
                }, 0f, T(0.25f)).SetTarget(bubble).SetEase(Ease.OutCubic));
            }

            reveal.AppendInterval(T(0.12f));
            return reveal;
        }

        private void PrepareSpeechBubble(int side, ComicDialogueLine line)
        {
            VisualElement bubble = _speechBubbles[side];
            Label speaker = _speechBubbleSpeakers[side];
            Label text = _speechBubbleTexts[side];

            bubble.style.display = DisplayStyle.Flex;
            bubble.style.opacity = 0f;
            bubble.style.left = Length.Percent(Mathf.Clamp(line.PositionPercent.x, 2f, 70f));
            bubble.style.top = Length.Percent(Mathf.Clamp(line.PositionPercent.y, 4f, 78f));
            speaker.text = line.Speaker ?? string.Empty;
            text.text = line.Text ?? string.Empty;
            ApplyDialogueClasses(bubble, line);
            _bubbleVisible[side] = true;
        }

        private static void ApplyDialogueClasses(VisualElement bubble, ComicDialogueLine line)
        {
            bubble.EnableInClassList("speech-bubble--penguin", line.Style == ComicDialogueStyle.Penguin);
            bubble.EnableInClassList("speech-bubble--fairy", line.Style == ComicDialogueStyle.Fairy);
            bubble.EnableInClassList("speech-bubble--santa", line.Style == ComicDialogueStyle.Santa);
            bool isSoundEffect = line.Style is ComicDialogueStyle.SoundEffect or
                ComicDialogueStyle.MagicEffect or ComicDialogueStyle.WhooshEffect;
            bubble.EnableInClassList("speech-bubble--sfx", isSoundEffect);
            bubble.EnableInClassList("speech-bubble--magic", line.Style == ComicDialogueStyle.MagicEffect);
            bubble.EnableInClassList("speech-bubble--whoosh", line.Style == ComicDialogueStyle.WhooshEffect);
            bubble.EnableInClassList("speech-bubble--tail-right", line.TailPointsRight);
            bubble.EnableInClassList("speech-bubble--tail-left", !line.TailPointsRight);
        }

        private void StartBubbleFeelReveal(int side, ComicDialogueStyle style)
        {
            MMF_Player player = side == 0 ? _leftBubbleRevealFeedbacks : _rightBubbleRevealFeedbacks;
            if (player == null) return;

            player.StopFeedbacks();
            ConfigureFeelTiming(player, T(0.30f));
            Transform driver = player.transform;
            Vector2 offset = BubbleInitialOffset(side, style);
            driver.localPosition = new Vector3(offset.x, offset.y, 0f);
            driver.localScale = Vector3.one * 0.82f;
            driver.localRotation = Quaternion.Euler(0f, 0f, BubbleInitialAngle(side, style));
            _bubbleFeelDriverActive[side] = true;
            ApplyBubbleFeelDriver(side);
            player.PlayFeedbacks();
        }

        private static Vector2 BubbleInitialOffset(int side, ComicDialogueStyle style)
        {
            if (style == ComicDialogueStyle.WhooshEffect) return new Vector2(36f, 0f);
            return side == 0 ? new Vector2(-16f, 8f) : new Vector2(16f, 8f);
        }

        private static float BubbleInitialAngle(int side, ComicDialogueStyle style)
        {
            float angle = style is ComicDialogueStyle.SoundEffect or ComicDialogueStyle.MagicEffect or
                ComicDialogueStyle.WhooshEffect ? 2.4f : 1.5f;
            return side == 0 ? -angle : angle;
        }

        private void UpdateBubbleFeelDrivers()
        {
            for (int side = 0; side < _bubbleFeelDriverActive.Length; side++)
            {
                if (!_bubbleFeelDriverActive[side]) continue;
                MMF_Player player = side == 0 ? _leftBubbleRevealFeedbacks : _rightBubbleRevealFeedbacks;
                ApplyBubbleFeelDriver(side);
                if (player != null && player.HasFeedbackStillPlaying()) continue;
                ResetBubbleFeelDriver(side, false);
            }
        }

        private void ApplyBubbleFeelDriver(int side)
        {
            MMF_Player player = side == 0 ? _leftBubbleRevealFeedbacks : _rightBubbleRevealFeedbacks;
            VisualElement bubble = _speechBubbles[side];
            if (player == null || bubble == null) return;

            Transform driver = player.transform;
            Vector3 position = driver.localPosition;
            Vector3 scale = driver.localScale;
            float angle = NormalizeSignedAngle(driver.localEulerAngles.z);
            bubble.style.translate = new Translate(position.x, position.y, 0f);
            bubble.style.rotate = new Rotate(new Angle(angle, AngleUnit.Degree));
            bubble.style.scale = new Scale(new Vector2(scale.x, scale.y));
        }

        private void ResetBubbleFeelDriver(int side, bool hideBubble)
        {
            MMF_Player player = side == 0 ? _leftBubbleRevealFeedbacks : _rightBubbleRevealFeedbacks;
            if (player != null)
            {
                player.transform.localPosition = Vector3.zero;
                player.transform.localScale = Vector3.one;
                player.transform.localRotation = Quaternion.identity;
            }

            VisualElement bubble = _speechBubbles[side];
            if (bubble != null)
            {
                bubble.style.translate = new Translate(0f, 0f, 0f);
                bubble.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
                bubble.style.scale = new Scale(Vector2.one);
                if (hideBubble)
                {
                    bubble.style.opacity = 0f;
                    bubble.style.display = DisplayStyle.None;
                    _bubbleVisible[side] = false;
                }
            }
            _bubbleFeelDriverActive[side] = false;
        }

        private void StopBubbleReveals()
        {
            _leftBubbleRevealFeedbacks?.StopFeedbacks();
            _rightBubbleRevealFeedbacks?.StopFeedbacks();
            ResetBubbleFeelDriver(0, true);
            ResetBubbleFeelDriver(1, true);
        }

        private Sequence HideSpeechBubbles()
        {
            Sequence hide = DOTween.Sequence().SetTarget(this).SetUpdate(true);
            bool hasVisibleBubble = false;
            for (int side = 0; side < _speechBubbles.Length; side++)
            {
                if (!_bubbleVisible[side] || _speechBubbles[side] == null) continue;
                VisualElement bubble = _speechBubbles[side];
                Tweener fade = FadeTo(bubble, 0f, T(0.13f)).SetEase(Ease.InSine);
                if (!hasVisibleBubble)
                {
                    hide.Append(fade);
                    hasVisibleBubble = true;
                }
                else
                {
                    hide.Join(fade);
                }
                hide.Join(ScaleTo(bubble, Vector2.one * 0.94f, T(0.14f)).SetEase(Ease.InQuad));
            }

            if (!hasVisibleBubble) hide.AppendInterval(0f);
            hide.AppendCallback(() =>
            {
                _leftBubbleRevealFeedbacks?.StopFeedbacks();
                _rightBubbleRevealFeedbacks?.StopFeedbacks();
                ResetBubbleFeelDriver(0, true);
                ResetBubbleFeelDriver(1, true);
            });
            return hide;
        }

        private static float NormalizeSignedAngle(float angle)
        {
            return angle > 180f ? angle - 360f : angle;
        }

        private Sequence HidePair(int leftCardIndex, int rightCardIndex)
        {
            VisualElement leftCard = _cards[leftCardIndex];
            VisualElement rightCard = _cards[rightCardIndex];
            Sequence hide = DOTween.Sequence().SetTarget(this).SetUpdate(true);
            hide.Append(HideSpeechBubbles());
            hide.Append(FadeTo(rightCard, 0f, T(0.24f)).SetEase(Ease.InSine));
            hide.Join(FadeTo(leftCard, 0f, T(0.30f)).SetEase(Ease.InSine));
            hide.Join(MoveTo(rightCard, new Vector2(30f, -8f), T(0.30f)).SetEase(Ease.InQuad));
            hide.Join(MoveTo(leftCard, new Vector2(-24f, 6f), T(0.30f)).SetEase(Ease.InQuad));
            hide.AppendCallback(() =>
            {
                rightCard.style.display = DisplayStyle.None;
                leftCard.style.display = DisplayStyle.None;
            });
            return hide;
        }

        private void CacheElements()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _container = root.Q<VisualElement>("cutscene-root");
            _stage = root.Q<VisualElement>("comic-stage");
            _startPrompt = root.Q<VisualElement>("start-prompt");
            _startPromptFace = root.Q<VisualElement>("start-prompt-face");
            _startMouseButton = root.Q<VisualElement>("start-mouse-button");
            _skipHint = root.Q<VisualElement>("skip-hint");
            _nextHint = root.Q<Label>("next-hint");
            for (int side = 0; side < _speechBubbles.Length; side++)
            {
                string suffix = side == 0 ? "left" : "right";
                _speechBubbles[side] = root.Q<VisualElement>($"speech-bubble-{suffix}");
                _speechBubbleTails[side] = root.Q<VisualElement>($"speech-bubble-{suffix}-tail");
                _speechBubbleSpeakers[side] = root.Q<Label>($"speech-bubble-{suffix}-speaker");
                _speechBubbleTexts[side] = root.Q<Label>($"speech-bubble-{suffix}-text");
            }
            int cardCount = Mathf.Clamp(CardCount, 0, MaxCardSlotCount);
            _cards = new VisualElement[cardCount];
            for (int index = 0; index < _cards.Length; index++)
                _cards[index] = root.Q<VisualElement>($"comic-card-{index}");
        }

        private void ApplyTextures()
        {
            if (!HasValidCards()) return;
            for (int index = 0; index < _cards.Length; index++)
                _cards[index].MarkDirtyRepaint();
        }

        private bool HasValidCards()
        {
            if (_container == null || _stage == null || _startPrompt == null || _startPromptFace == null ||
                _startMouseButton == null || _skipHint == null ||
                _nextHint == null ||
                _cardTextures == null || _cardTextures.Length < 2 ||
                _cardTextures.Length > MaxCardSlotCount || _cardTextures.Length % 2 != 0 ||
                _cards == null || _cards.Length != _cardTextures.Length)
                return false;

            for (int side = 0; side < _speechBubbles.Length; side++)
                if (_speechBubbles[side] == null || _speechBubbleTails[side] == null ||
                    _speechBubbleSpeakers[side] == null || _speechBubbleTexts[side] == null)
                    return false;

            for (int index = 0; index < _cardTextures.Length; index++)
                if (_cards[index] == null || _cardTextures[index] == null) return false;
            return true;
        }

        private void ResetVisuals()
        {
            if (_container == null || _stage == null) return;
            _container.style.display = DisplayStyle.None;
            _stage.style.opacity = 0f;
            _startPrompt.style.display = DisplayStyle.None;
            _startPrompt.style.opacity = 0f;
            _startPrompt.style.translate = new Translate(0f, 0f, 0f);
            _startPrompt.style.scale = new Scale(Vector2.one);
            _startPromptFace.style.translate = new Translate(0f, 0f, 0f);
            _startMouseButton.style.scale = new Scale(Vector2.one);
            _skipHint.style.opacity = 0.88f;
            SetNextHintVisible(false);
            _currentCardIndex = -1;
            _isTransitioning = false;
            _isAwaitingStart = false;
            _startPromptLoop?.Kill();
            _startPromptLoop = null;
            StopFeelReveals();
            StopBubbleReveals();
            if (_cards == null) return;

            foreach (VisualElement card in _cards)
            {
                if (card == null) continue;
                card.style.display = DisplayStyle.None;
                card.style.opacity = 0f;
                card.style.translate = new Translate(0f, 0f, 0f);
                card.style.rotate = new Rotate(new Angle(0f, AngleUnit.Degree));
                card.style.scale = new Scale(Vector2.one);
            }
        }

        private void Finish()
        {
            _isPlaying = false;
            _isComplete = true;
            _isTransitioning = false;
            _isAwaitingStart = false;
            _startPromptLoop?.Kill();
            _startPromptLoop = null;
            StopBubbleReveals();
            _stage.style.opacity = 0f;
            SetNextHintVisible(false);
            _container.style.display = DisplayStyle.None;
        }

        private void ShowNextHint()
        {
            if (!_isPlaying) return;
            SetNextHintVisible(true);
            _nextHint.text = _currentCardIndex >= _cards.Length - 1 ? "CLICK  ▶  CLOSE" : "CLICK  ▶  NEXT";
        }

        private void SetNextHintVisible(bool visible)
        {
            if (_nextHint == null) return;
            _nextHint.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            _nextHint.style.opacity = visible ? 1f : 0f;
        }

        private void OnClick(ClickEvent clickEvent)
        {
            Advance();
        }

        private void RegisterCardRenderers()
        {
            UnregisterCardRenderers();
            if (_cards == null) return;

            _cardRenderers = new Action<MeshGenerationContext>[_cards.Length];
            for (int index = 0; index < _cards.Length; index++)
            {
                int cardIndex = index;
                _cardRenderers[index] = context => DrawCard(context, cardIndex);
                _cards[index].generateVisualContent += _cardRenderers[index];
            }
        }

        private void UnregisterCardRenderers()
        {
            if (_cards == null || _cardRenderers == null) return;

            int count = Mathf.Min(_cards.Length, _cardRenderers.Length);
            for (int index = 0; index < count; index++)
            {
                if (_cards[index] != null && _cardRenderers[index] != null)
                    _cards[index].generateVisualContent -= _cardRenderers[index];
            }
            _cardRenderers = null;
        }

        private void DrawCard(MeshGenerationContext context, int cardIndex)
        {
            Texture2D texture = _cardTextures != null && cardIndex >= 0 && cardIndex < _cardTextures.Length
                ? _cardTextures[cardIndex]
                : null;

            Rect rect = _cards[cardIndex].contentRect;
            float width = rect.width;
            float height = rect.height;
            if (width <= 1f || height <= 1f) return;

            bool isRightCard = cardIndex % 2 == 1;
            const float rightTopInset = 0.11017f;
            Vector2[] outer = isRightCard
                ? new[]
                {
                    new Vector2(0f, height),
                    new Vector2(width * rightTopInset, 0f),
                    new Vector2(width, 0f),
                    new Vector2(width, height)
                }
                : new[]
                {
                    new Vector2(0f, height),
                    new Vector2(0f, 0f),
                    new Vector2(width, 0f),
                    new Vector2(width * 0.90f, height)
                };

            Vector2[] inner = isRightCard
                ? new[]
                {
                    new Vector2(12f, height - 10f),
                    new Vector2(width * rightTopInset + 12f, 10f),
                    new Vector2(width - 10f, 10f),
                    new Vector2(width - 10f, height - 10f)
                }
                : new[]
                {
                    new Vector2(10f, height - 10f),
                    new Vector2(10f, 10f),
                    new Vector2(width - 10f, 10f),
                    new Vector2(width * 0.90f - 12f, height - 10f)
                };

            DrawAntialiasedPolygon(context, outer, Color.white);
            if (texture != null)
            {
                const float imageZoom = 1f;
                float effectiveWidth = width * 0.95f;
                const float verticalCropOffset = 0f;
                float sourceAspect = (float)texture.width / texture.height;
                float panelAspect = effectiveWidth / height;
                Vector2[] imagePoints = sourceAspect > panelAspect
                    ? ScalePoints(inner, 1f, imageZoom, width, height)
                    : ScalePoints(inner, imageZoom, 1f, width, height);
                Rect uvRect = GetCropUvRect(texture, effectiveWidth, height, verticalCropOffset, imageZoom);
                DrawQuad(context, imagePoints, texture, uvRect, Color.white);
                DrawAntialiasedInnerSeam(context, imagePoints, isRightCard);
            }
            else
            {
                DrawQuad(context, inner, null, new Rect(0f, 0f, 1f, 1f),
                    new Color(0.95f, 0.97f, 0.98f, 1f));
                DrawAntialiasedInnerSeam(context, inner, isRightCard);
            }
        }

        private static void DrawAntialiasedPolygon(MeshGenerationContext context,
            Vector2[] points, Color color)
        {
            Painter2D painter = context.painter2D;
            painter.fillColor = color;
            painter.BeginPath();
            painter.MoveTo(points[0]);
            for (int index = 1; index < points.Length; index++)
                painter.LineTo(points[index]);
            painter.ClosePath();
            painter.Fill();
        }

        private static void DrawAntialiasedInnerSeam(MeshGenerationContext context,
            Vector2[] points, bool isRightCard)
        {
            Vector2 start = isRightCard ? points[0] : points[2];
            Vector2 end = isRightCard ? points[1] : points[3];
            Painter2D painter = context.painter2D;
            painter.strokeColor = Color.white;
            painter.lineWidth = 2.5f;
            painter.BeginPath();
            painter.MoveTo(start);
            painter.LineTo(end);
            painter.Stroke();
        }

        private static void DrawQuad(MeshGenerationContext context, Vector2[] points,
            Texture texture, Rect uvRect, Color tint)
        {
            Vertex[] vertices = new Vertex[4];
            for (int index = 0; index < vertices.Length; index++)
            {
                vertices[index].position = new Vector3(points[index].x, points[index].y, Vertex.nearZ);
                vertices[index].tint = tint;
            }
            vertices[0].uv = new Vector2(uvRect.xMin, uvRect.yMin);
            vertices[1].uv = new Vector2(uvRect.xMin, uvRect.yMax);
            vertices[2].uv = new Vector2(uvRect.xMax, uvRect.yMax);
            vertices[3].uv = new Vector2(uvRect.xMax, uvRect.yMin);

            ushort[] indices = { 0, 1, 2, 2, 3, 0 };
            MeshWriteData mesh = context.Allocate(vertices.Length, indices.Length, texture);
            mesh.SetAllVertices(vertices);
            mesh.SetAllIndices(indices);
        }

        private static Vector2[] ScalePoints(Vector2[] points, float scaleX, float scaleY,
            float panelWidth, float panelHeight)
        {
            Vector2 center = new Vector2(panelWidth * 0.5f, panelHeight * 0.5f);
            Vector2[] scaled = new Vector2[points.Length];
            for (int index = 0; index < points.Length; index++)
            {
                Vector2 offset = points[index] - center;
                scaled[index] = center + new Vector2(offset.x * scaleX, offset.y * scaleY);
            }
            return scaled;
        }

        private static Rect GetCropUvRect(Texture2D texture, float panelWidth, float panelHeight,
            float verticalOffset = 0f, float zoom = 1f)
        {
            float sourceAspect = (float)texture.width / texture.height;
            float panelAspect = panelWidth / panelHeight;
            if (sourceAspect > panelAspect)
            {
                float visibleWidth = Mathf.Min(1f, panelAspect / sourceAspect / zoom);
                return new Rect((1f - visibleWidth) * 0.5f, 0f, visibleWidth, 1f);
            }

            float visibleHeight = Mathf.Min(1f, sourceAspect / panelAspect / zoom);
            float centeredY = (1f - visibleHeight) * 0.5f;
            float y = Mathf.Clamp(centeredY + verticalOffset, 0f, 1f - visibleHeight);
            return new Rect(0f, y, 1f, visibleHeight);
        }

        private void KillCardTweens()
        {
            if (_stage != null) DOTween.Kill(_stage);
            foreach (VisualElement bubble in _speechBubbles)
                if (bubble != null) DOTween.Kill(bubble);
            if (_cards == null) return;
            foreach (VisualElement card in _cards)
                if (card != null) DOTween.Kill(card);
        }

        private float T(float seconds)
        {
            return seconds * Mathf.Max(0.01f, _timingScale);
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

        private static Tweener ScaleTo(VisualElement element, Vector2 endValue, float duration)
        {
            Vector2 value = element.resolvedStyle.scale.value;
            if (value == Vector2.zero) value = Vector2.one;
            return DOTween.To(() => value, next =>
            {
                value = next;
                element.style.scale = new Scale(next);
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

        private static Tweener RotateTo(VisualElement element, float endValue, float duration)
        {
            float value = element.resolvedStyle.rotate.angle.value;
            return DOTween.To(() => value, next =>
            {
                value = next;
                element.style.rotate = new Rotate(new Angle(next, AngleUnit.Degree));
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
