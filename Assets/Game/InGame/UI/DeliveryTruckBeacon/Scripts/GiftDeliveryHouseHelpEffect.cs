using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.UI;

namespace PPack
{
    /// <summary>
    /// 주문 집 위에 붙는 평면 월드 UI. 서로 다른 문구의 말풍선 두 장을 집 주변에서 순차 등장시키고
    /// Feel로 반복 펄스를 재생한다. 집 주변 오라나 3D 메시 표지는 만들지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryHouseHelpEffect : MonoBehaviour
    {
        private static readonly Color InkColor = new Color32(27, 57, 76, 255);
        private static readonly Color PaperColor = new Color32(255, 247, 221, 255);
        private static readonly Color ShadowColor = new Color32(13, 31, 45, 92);

        private static readonly Vector2 LeftBubblePosition = new Vector2(-190f, 10f);
        private static readonly Vector2 RightBubblePosition = new Vector2(190f, 90f);

        private static readonly Vector2 CardSize = new Vector2(300f, 132f);

        private const float CardStaggerSeconds = 0.9f;
        private const float CardEnterSeconds = 0.45f;
        private const float StackHoldSeconds = 1.8f;
        private const float StackFadeSeconds = 0.72f;
        private const float CycleRestSeconds = 0.9f;
        private const float IdlePulseSeconds = 1.05f;
        private const float IdleLoopStartDelaySeconds = 2.8f;
        private const float IdleLoopPauseSeconds = 2.4f;
        private const int SingleCardCycleInterval = 3;
        private const float SpawnBelowDistance = 58f;
        private const float RootBobHeight = 0.11f;

        private readonly List<Texture2D> _runtimeTextures = new List<Texture2D>();
        private readonly List<Sprite> _runtimeSprites = new List<Sprite>();
        private readonly List<HelpCardMotion> _cardMotions = new List<HelpCardMotion>();

        private Transform _anchor;
        private Transform _primaryVisual;
        private Transform _secondaryVisual;
        private MMF_Player _entranceFeedback;
        private MMF_Player _idleFeedback;
        private Camera _camera;
        private Vector3 _localAnchorPosition;
        private float _baseScale;
        private float _animationTime;
        private int _patternSeed;
        private int _lastPatternCycle;
        private bool _singleCardCycle;
        private int _singleCardSlotIndex;

        public void Configure(
            Transform anchor,
            Color accentColor,
            Font font,
            Vector3 localAnchorPosition,
            float scale,
            MMF_Player entranceFeedback,
            MMF_Player idleFeedback)
        {
            _anchor = anchor;
            _localAnchorPosition = localAnchorPosition;
            _baseScale = scale;
            _camera = Camera.main;
            if (_camera != null) transform.rotation = _camera.transform.rotation;
            _animationTime = 0f;
            _patternSeed = anchor != null ? anchor.GetEntityId().GetHashCode() : 0;
            _lastPatternCycle = -1;
            _singleCardCycle = false;
            _singleCardSlotIndex = 1;

            BuildVisual(accentColor, font);
            BindAndPlayFeel(entranceFeedback, idleFeedback);
            UpdatePose();
        }

        private void LateUpdate()
        {
            if (_anchor == null)
            {
                Destroy(gameObject);
                return;
            }

            UpdatePose();
            UpdateContinuousAnimation();
        }

        private void UpdatePose()
        {
            float bob = Mathf.Sin(_animationTime * Mathf.PI * 0.72f) * RootBobHeight;
            transform.position = _anchor.TransformPoint(_localAnchorPosition) + Vector3.up * bob;

            if (_camera == null || !_camera.isActiveAndEnabled) _camera = Camera.main;
            if (_camera != null)
            {
                // 카메라를 즉시 복사하지 않고 감쇠시켜 월드에 떠 있는 스티커처럼 부드럽게 따라본다.
                float rotationBlend = 1f - Mathf.Exp(-12f * Time.unscaledDeltaTime);
                transform.rotation = Quaternion.Slerp(
                    transform.rotation,
                    _camera.transform.rotation,
                    rotationBlend);
            }

            transform.localScale = Vector3.one * _baseScale;
        }

        private void UpdateContinuousAnimation()
        {
            if (_cardMotions.Count == 0) return;

            _animationTime += Time.unscaledDeltaTime;
            float lastSpawnTime = CardStaggerSeconds * (_cardMotions.Count - 1);
            float fadeStartTime = lastSpawnTime + CardEnterSeconds + StackHoldSeconds;
            float cycleDuration = fadeStartTime + StackFadeSeconds + CycleRestSeconds;
            float cycleTime = Mathf.Repeat(_animationTime, cycleDuration);
            int patternCycle = Mathf.FloorToInt(_animationTime / cycleDuration);
            if (patternCycle != _lastPatternCycle)
            {
                // 한 장 패턴이 연달아 나오지 않게 하여 두 장 반복 사이에 가끔만 섞는다.
                bool previousCycleWasSingle = _singleCardCycle;
                _singleCardCycle = !previousCycleWasSingle && ShouldUseSingleCard(patternCycle);
                if (_singleCardCycle)
                {
                    int positiveSeed = _patternSeed & int.MaxValue;
                    _singleCardSlotIndex =
                        (positiveSeed + patternCycle / SingleCardCycleInterval) % _cardMotions.Count;
                }
                _lastPatternCycle = patternCycle;
            }

            float fadeOut = 1f - Smooth01(Mathf.InverseLerp(
                fadeStartTime,
                fadeStartTime + StackFadeSeconds,
                cycleTime));

            for (int index = 0; index < _cardMotions.Count; index++)
            {
                HelpCardMotion card = _cardMotions[index];
                // 한 장 변주에서는 이전 요청대로 HELP! 카드만 남긴다.
                bool isSingleVisibleCard = index == 0;
                if (_singleCardCycle && !isSingleVisibleCard)
                {
                    card.Group.alpha = 0f;
                    continue;
                }

                int sequenceIndex = _singleCardCycle ? 0 : index;
                float spawnTime = sequenceIndex * CardStaggerSeconds;
                float age = cycleTime - spawnTime;
                if (age < 0f)
                {
                    card.Group.alpha = 0f;
                    continue;
                }

                float enter = Smooth01(Mathf.InverseLerp(0f, CardEnterSeconds, age));
                int slotIndex = _singleCardCycle ? _singleCardSlotIndex : index;
                GetBubbleSlot(slotIndex, out Vector2 bubblePosition, out float bubbleRotation);
                Vector2 spawnPosition = bubblePosition + Vector2.down * SpawnBelowDistance;
                float idleSway = Mathf.Sin(_animationTime * 1.5f + index * 1.4f) * 2.2f * enter;
                float idleBob = Mathf.Sin(_animationTime * 1.8f + index * 0.9f) * 1.5f * enter;
                card.Rect.anchoredPosition = Vector2.Lerp(spawnPosition, bubblePosition, enter) +
                                             new Vector2(idleSway, idleBob);

                float pop = Mathf.Sin(enter * Mathf.PI) * 0.1f;
                float entranceScale = Mathf.Lerp(0.82f, 1f, enter);
                float exitScale = Mathf.Lerp(0.96f, 1f, fadeOut);
                card.Rect.localScale = Vector3.one * (entranceScale * exitScale * (1f + pop));
                float entranceRotation = Mathf.Lerp(-4f, bubbleRotation, enter);
                float wobble = Mathf.Sin(_animationTime * 1.35f + index) * 0.8f * enter;
                card.Rect.localRotation = Quaternion.Euler(0f, 0f, entranceRotation + wobble);
                card.Group.alpha = enter * fadeOut;
            }
        }

        private bool ShouldUseSingleCard(int cycleIndex)
        {
            int houseOffset = (_patternSeed & int.MaxValue) % SingleCardCycleInterval;
            return (cycleIndex + houseOffset) % SingleCardCycleInterval == 0;
        }

        private static void GetBubbleSlot(int slotIndex, out Vector2 position, out float rotation)
        {
            if (slotIndex <= 0)
            {
                position = LeftBubblePosition;
                rotation = -3f;
                return;
            }

            position = RightBubblePosition;
            rotation = 2.5f;
        }

        private static float Smooth01(float value)
        {
            value = Mathf.Clamp01(value);
            return value * value * (3f - 2f * value);
        }

        private void BuildVisual(Color accentColor, Font font)
        {
            Sprite panelSprite = CreateRoundedPanelSprite();

            GameObject canvasObject = new GameObject(
                "HelpWorldCanvas",
                typeof(RectTransform),
                typeof(Canvas),
                typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);

            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = _camera;
            canvas.overrideSorting = true;
            canvas.sortingOrder = 80;

            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 12f;

            RectTransform canvasRect = canvasObject.GetComponent<RectTransform>();
            canvasRect.sizeDelta = new Vector2(760f, 330f);
            canvasRect.localScale = Vector3.one * 0.01f;

            _primaryVisual = CreateCard(
                canvasRect,
                "HelpCard_Primary",
                "HELP!",
                panelSprite,
                accentColor,
                font,
                true,
                1f);

            _secondaryVisual = CreateCard(
                canvasRect,
                "HelpCard_Secondary",
                "OVER HERE!",
                panelSprite,
                accentColor,
                font,
                false,
                -1f);
        }

        private Transform CreateCard(
            RectTransform parent,
            string objectName,
            string label,
            Sprite panelSprite,
            Color accentColor,
            Font font,
            bool createAccentRays,
            float tailSide)
        {
            GameObject cardObject = new GameObject(objectName, typeof(RectTransform), typeof(CanvasGroup));
            RectTransform cardRect = cardObject.GetComponent<RectTransform>();
            cardRect.SetParent(parent, false);
            cardRect.anchorMin = cardRect.anchorMax = new Vector2(0.5f, 0.5f);
            cardRect.pivot = new Vector2(0.5f, 0.5f);
            GetBubbleSlot(_cardMotions.Count, out Vector2 initialPosition, out _);
            cardRect.anchoredPosition = initialPosition + Vector2.down * SpawnBelowDistance;
            cardRect.sizeDelta = CardSize;
            cardRect.localScale = Vector3.one * 0.82f;
            cardRect.localRotation = Quaternion.Euler(0f, 0f, -4f);
            // 겹치지는 않지만 등장 순서와 Feel 대상 순서를 동일하게 유지한다.
            cardRect.SetSiblingIndex(_cardMotions.Count);
            CanvasGroup canvasGroup = cardObject.GetComponent<CanvasGroup>();
            canvasGroup.alpha = 0f;

            _cardMotions.Add(new HelpCardMotion(cardRect, canvasGroup));

            GameObject visualObject = new GameObject("Visual", typeof(RectTransform));
            RectTransform visualRect = visualObject.GetComponent<RectTransform>();
            visualRect.SetParent(cardRect, false);
            Stretch(visualRect);

            if (createAccentRays) CreateAccentRays(visualRect, accentColor, CardSize);
            CreateTail(visualRect, CardSize, tailSide);

            GameObject panelObject = new GameObject("Paper", typeof(RectTransform), typeof(Image), typeof(Shadow));
            RectTransform panelRect = panelObject.GetComponent<RectTransform>();
            panelRect.SetParent(visualRect, false);
            Stretch(panelRect);

            Image panel = panelObject.GetComponent<Image>();
            panel.sprite = panelSprite;
            panel.type = Image.Type.Sliced;
            panel.color = Color.white;
            panel.raycastTarget = false;

            Shadow shadow = panelObject.GetComponent<Shadow>();
            shadow.effectColor = ShadowColor;
            shadow.effectDistance = new Vector2(5f, -6f);
            shadow.useGraphicAlpha = true;

            GameObject textObject = new GameObject("Label", typeof(RectTransform), typeof(Text));
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.SetParent(visualRect, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(14f, 8f);
            textRect.offsetMax = new Vector2(-14f, -8f);

            Text text = textObject.GetComponent<Text>();
            text.font = font;
            text.text = label;
            text.alignment = TextAnchor.MiddleCenter;
            text.fontStyle = FontStyle.Bold;
            int maximumFontSize = label.Length > 6 ? 48 : 70;
            text.fontSize = maximumFontSize;
            text.color = InkColor;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 28;
            text.resizeTextMaxSize = maximumFontSize;

            return visualRect;
        }

        private static void CreateTail(RectTransform parent, Vector2 cardSize, float side)
        {
            Vector2 tailPosition = new Vector2(cardSize.x * 0.18f * side, -cardSize.y * 0.43f);
            CreateSolidImage(parent, "Tail_Outline", tailPosition, new Vector2(38f, 38f), InkColor, 45f);
            CreateSolidImage(parent, "Tail_Paper", tailPosition + new Vector2(0f, 3f),
                new Vector2(27f, 27f), PaperColor, 45f);
        }

        private static void CreateAccentRays(RectTransform parent, Color accentColor, Vector2 cardSize)
        {
            CreateSolidImage(parent, "AccentRay_Top", new Vector2(cardSize.x * 0.28f, cardSize.y * 0.57f),
                new Vector2(9f, 31f), accentColor, -8f);
            CreateSolidImage(parent, "AccentRay_RightTop", new Vector2(cardSize.x * 0.47f, cardSize.y * 0.44f),
                new Vector2(9f, 31f), accentColor, -42f);
            CreateSolidImage(parent, "AccentRay_Right", new Vector2(cardSize.x * 0.56f, cardSize.y * 0.18f),
                new Vector2(9f, 28f), accentColor, -70f);
        }

        private static void CreateSolidImage(
            RectTransform parent,
            string objectName,
            Vector2 position,
            Vector2 size,
            Color color,
            float rotation)
        {
            GameObject imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.SetParent(parent, false);
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            rect.localRotation = Quaternion.Euler(0f, 0f, rotation);

            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;
        }

        private void BindAndPlayFeel(MMF_Player entranceFeedback, MMF_Player idleFeedback)
        {
            _entranceFeedback = entranceFeedback;
            _idleFeedback = idleFeedback;

            if (_entranceFeedback != null)
            {
                _entranceFeedback.RestoreInitialValuesOnDisable = false;
                int scaleIndex = 0;
                for (int index = 0; index < _entranceFeedback.FeedbacksList.Count; index++)
                {
                    if (!(_entranceFeedback.FeedbacksList[index] is MMF_Scale scaleFeedback)) continue;
                    Transform target = GetVisualForFeedback(scaleIndex);
                    scaleFeedback.Active = target != null;
                    if (target == null)
                    {
                        scaleIndex++;
                        continue;
                    }

                    scaleFeedback.Timing.InitialDelay = scaleIndex * CardStaggerSeconds;
                    scaleFeedback.AnimateScaleDuration = CardEnterSeconds + scaleIndex * 0.04f;
                    scaleFeedback.AnimateScaleTarget = target;
                    scaleIndex++;
                }

                _entranceFeedback.StopFeedbacks();
                _entranceFeedback.ResetFeedbacks();
                _entranceFeedback.Initialization(true);
                _entranceFeedback.PlayFeedbacks(transform.position);
            }

            if (_idleFeedback != null)
            {
                _idleFeedback.RestoreInitialValuesOnDisable = false;
                int scaleIndex = 0;
                for (int index = 0; index < _idleFeedback.FeedbacksList.Count; index++)
                {
                    MMF_Feedback feedback = _idleFeedback.FeedbacksList[index];
                    if (feedback is MMF_LooperStart loopStart)
                    {
                        loopStart.PauseDuration = IdleLoopStartDelaySeconds;
                    }
                    else if (feedback is MMF_Scale scaleFeedback)
                    {
                        Transform target = GetVisualForFeedback(scaleIndex);
                        scaleFeedback.Active = target != null;
                        if (target == null)
                        {
                            scaleIndex++;
                            continue;
                        }

                        scaleFeedback.Timing.InitialDelay = scaleIndex * CardStaggerSeconds;
                        scaleFeedback.AnimateScaleDuration = IdlePulseSeconds;
                        scaleFeedback.AnimateScaleTarget = target;
                        scaleIndex++;
                    }
                    else if (feedback is MMF_Pause pause && !(feedback is MMF_Looper))
                    {
                        pause.PauseDuration = IdleLoopPauseSeconds;
                    }
                }

                _idleFeedback.StopFeedbacks();
                _idleFeedback.ResetFeedbacks();
                _idleFeedback.Initialization(true);
                _idleFeedback.PlayFeedbacks(transform.position);
            }
        }

        private Transform GetVisualForFeedback(int index)
        {
            if (index == 0) return _primaryVisual;
            if (index == 1) return _secondaryVisual;
            return null;
        }

        private Sprite CreateRoundedPanelSprite()
        {
            const int width = 192;
            const int height = 96;
            const int border = 7;
            const int radius = 22;

            var texture = new Texture2D(width, height, TextureFormat.RGBA32, false)
            {
                name = "HelpPanel_RuntimeTexture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };

            var pixels = new Color[width * height];
            for (int y = 0; y < height; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    bool outer = IsInsideRoundedRect(x, y, width, height, radius);
                    bool inner = IsInsideRoundedRect(
                        x - border,
                        y - border,
                        width - border * 2,
                        height - border * 2,
                        radius - border);
                    pixels[y * width + x] = !outer ? Color.clear : inner ? PaperColor : InkColor;
                }
            }

            texture.SetPixels(pixels);
            texture.Apply(false, false);
            _runtimeTextures.Add(texture);

            Sprite sprite = Sprite.Create(
                texture,
                new Rect(0f, 0f, width, height),
                new Vector2(0.5f, 0.5f),
                100f,
                0,
                SpriteMeshType.FullRect,
                new Vector4(radius, radius, radius, radius));
            sprite.name = "HelpPanel_RuntimeSprite";
            _runtimeSprites.Add(sprite);
            return sprite;
        }

        private static bool IsInsideRoundedRect(int x, int y, int width, int height, int radius)
        {
            if (x < 0 || y < 0 || x >= width || y >= height) return false;
            if (x >= radius && x < width - radius) return true;
            if (y >= radius && y < height - radius) return true;

            int centerX = x < radius ? radius : width - radius - 1;
            int centerY = y < radius ? radius : height - radius - 1;
            int deltaX = x - centerX;
            int deltaY = y - centerY;
            return deltaX * deltaX + deltaY * deltaY <= radius * radius;
        }

        private static void Stretch(RectTransform rect)
        {
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
        }

        private void OnDestroy()
        {
            if (_entranceFeedback != null) _entranceFeedback.StopFeedbacks();
            if (_idleFeedback != null) _idleFeedback.StopFeedbacks();

            for (int index = 0; index < _runtimeSprites.Count; index++)
                if (_runtimeSprites[index] != null) Destroy(_runtimeSprites[index]);
            _runtimeSprites.Clear();

            for (int index = 0; index < _runtimeTextures.Count; index++)
                if (_runtimeTextures[index] != null) Destroy(_runtimeTextures[index]);
            _runtimeTextures.Clear();
        }

        private sealed class HelpCardMotion
        {
            public HelpCardMotion(RectTransform rect, CanvasGroup group)
            {
                Rect = rect;
                Group = group;
            }

            public RectTransform Rect { get; }
            public CanvasGroup Group { get; }
        }
    }
}
