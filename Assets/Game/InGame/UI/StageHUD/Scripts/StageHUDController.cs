using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>선물 색과 제한시간을 우선하고, 같은 색 선물을 들었을 때만 길 안내를 보여 주는 HUD.</summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class StageHUDController : MonoBehaviour
    {
        private const float WarningThresholdSeconds = 30f;
        // 주문 TTL 이 25~57초라 30초 경고는 가까운 집에선 태어나자마자 켜진다.
        // 실제로 "이제 못 간다" 를 알리는 구간은 따로 둔다.
        private const float OrderCriticalSeconds = 7f;
        private const float TicketStartScale = 0.92f;
        private const float ClockWarningSeconds = 30f;
        private const float ClockCriticalSeconds = 10f;
        private const float ClockBlinkPeriodSeconds = 0.5f;
        // 초가 바뀔 때 한 번 치는 킥. 짧아야 다음 초와 겹치지 않는다.
        private const float ClockTickPunchSeconds = 0.32f;
        private const float ClockTickPunchScale = 0.42f;
        private const float ClockTickShakePixels = 5f;
        private const float ClockTickShakeHz = 26f;
        private const float GainPopSeconds = 0.28f;
        private const float GainPopScale = 0.5f;
        private const float GainRisePixels = 14f;
        private const float ClockGainHoldSeconds = 0.9f;
        private const float ClockGainFadeSeconds = 0.5f;
        private const float TicketEnterFromRightPixels = 760f;
        private const float TicketLandingOvershootPixels = 14f;
        private const float TicketSlidePortion = 0.78f;
        private const float TicketFadeSeconds = 0.12f;
        private const float TicketStartRotation = 3.5f;

        [Header("Feel Feedback")]
        [Tooltip("Feel이 새 주문의 팝 스케일을 기록하는 중립 Transform입니다.")]
        [SerializeField] private Transform _orderAddedScaleDriver;
        [Tooltip("새 주문이 HUD 스택에 들어올 때 재생할 Feel 플레이어입니다.")]
        [SerializeField] private MMF_Player _orderAddedFeedback;
        [Tooltip("남은 시간이 10초 아래로 떨어지는 순간 한 번 재생한다. 진동은 여기에 MMF_Haptics 로 넣는다.")]
        [SerializeField] private MMF_Player _clockCriticalFeedback;
        [Tooltip("의뢰가 만료되는 순간 한 번 재생한다. 아무 신호 없이 사라지면 취소된 것처럼 느껴진다.")]
        [SerializeField] private MMF_Player _orderExpiredFeedback;
        [SerializeField, Min(0.1f)] private float _orderAddedAnimationSeconds = 0.68f;
        [SerializeField] private UnityEvent _orderAdded = new UnityEvent();
        [SerializeField] private UnityEvent _orderRemoved = new UnityEvent();

        private readonly List<int> _displayedOrderIds = new List<int>();
        private readonly List<OrderTicket> _tickets = new List<OrderTicket>();
        private VisualElement _hudRoot;
        private VisualElement _orderStack;
        private VisualElement _stageClock;
        private bool _isClockCritical;
        private int _lastShownSecond = -1;
        private float _clockTickStartedAt = -1f;
        private Label _stageClockTime;
        private Label _stageClockGain;
        private Label _scoreChipValue;
        private VisualElement _staminaFill;
        private float _clockGainShownAt = -1f;
        private VisualElement _waitingTicket;
        private VisualElement _animatedTicket;
        private float _orderAddedAnimationStartedAt;
        private bool _isOrderAddedAnimationPlaying;

        private void OnEnable()
        {
            // The scale driver is a scene-owned sibling and can be destroyed before Feel disables.
            // Restoring it from MMF_Player.OnDisable would then throw a MissingReferenceException.
            if (_orderAddedFeedback != null)
                _orderAddedFeedback.RestoreInitialValuesOnDisable = false;

            ResolveElements();
            SetVisible(false);
        }

        private void OnDisable()
        {
            FinishOrderAddedAnimation();
        }

        private void Update()
        {
            TickTimeGainFade();
            TickClockPunch();
            for (int index = 0; index < _tickets.Count; index++) _tickets[index].TickPunch();

            if (!_isOrderAddedAnimationPlaying || _animatedTicket == null) return;

            float elapsed = Time.unscaledTime - _orderAddedAnimationStartedAt;
            float normalized = Mathf.Clamp01(elapsed / _orderAddedAnimationSeconds);
            float horizontalPosition;
            if (normalized < TicketSlidePortion)
            {
                float slide = EaseOutCubic(normalized / TicketSlidePortion);
                horizontalPosition = Mathf.Lerp(
                    TicketEnterFromRightPixels,
                    -TicketLandingOvershootPixels,
                    slide);
            }
            else
            {
                float settle = Mathf.SmoothStep(
                    0f,
                    1f,
                    Mathf.InverseLerp(TicketSlidePortion, 1f, normalized));
                horizontalPosition = Mathf.Lerp(-TicketLandingOvershootPixels, 0f, settle);
            }

            float reveal = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(elapsed / TicketFadeSeconds));
            // 드라이버가 있으면 Feel 이 만든 스케일을, 없으면 같은 구간을 직접 이징한 값을 쓴다.
            float feelScale = _orderAddedScaleDriver != null
                ? _orderAddedScaleDriver.localScale.x
                : Mathf.Lerp(TicketStartScale, 1f, EaseOutCubic(normalized));

            _animatedTicket.style.opacity = reveal;
            _animatedTicket.style.translate = new Translate(horizontalPosition, 0f, 0f);
            _animatedTicket.style.rotate = new Rotate(TicketStartRotation * (1f - normalized));
            _animatedTicket.style.scale = new Scale(new Vector2(feelScale, feelScale));

            if (elapsed >= _orderAddedAnimationSeconds) FinishOrderAddedAnimation();
        }

        /// <summary>씬 빌더가 직렬화된 Feel 플레이어와 UI Toolkit 사이의 드라이버를 연결한다.</summary>
        public void ConfigureOrderAddedFeedback(Transform scaleDriver, MMF_Player feedback, float durationSeconds)
        {
            _orderAddedScaleDriver = scaleDriver;
            _orderAddedFeedback = feedback;
            _orderAddedAnimationSeconds = Mathf.Max(0.1f, durationSeconds);
        }

        /// <summary>UIDocument 가 트리를 만들기 전에 OnEnable 이 돌면 Q 가 전부 null 을 준다.
        /// 한 번 캐시하고 끝내면 그 판이 통째로 죽으므로, 비어 있을 때마다 다시 찾는다.</summary>
        private bool ResolveElements()
        {
            if (_hudRoot != null) return true;

            var document = GetComponent<UIDocument>();
            VisualElement documentRoot = document != null ? document.rootVisualElement : null;
            if (documentRoot == null) return false;

            _hudRoot = documentRoot.Q<VisualElement>("stage-hud-root");
            _orderStack = documentRoot.Q<VisualElement>("order-stack");
            _stageClock = documentRoot.Q<VisualElement>("stage-clock");
            _stageClockTime = documentRoot.Q<Label>("stage-clock-time");
            _stageClockGain = documentRoot.Q<Label>("stage-clock-gain");
            _waitingTicket = documentRoot.Q<VisualElement>("waiting-ticket");
            _scoreChipValue = documentRoot.Q<Label>("score-chip-value");
            _staminaFill = documentRoot.Q<VisualElement>("stamina-fill");
            return _hudRoot != null;
        }

        /// <summary>10초 경고에서 재생할 Feel 플레이어를 씬이 연결한다.</summary>
        public void ConfigureClockCriticalFeedback(MMF_Player feedback) => _clockCriticalFeedback = feedback;

        /// <summary>의뢰가 만료됐음을 알린다. 주문서는 이미 사라진 뒤라 여기서는 소리·진동만 친다.</summary>
        public void PlayOrderExpired()
        {
            if (_orderExpiredFeedback == null) return;
            _orderExpiredFeedback.StopFeedbacks();
            _orderExpiredFeedback.PlayFeedbacks(transform.position);
        }

        public void SetVisible(bool visible)
        {
            ResolveElements();
            if (_hudRoot == null) return;
            _hudRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
        }

        /// <summary>왼쪽 아래 달리기 체력을 갱신한다. 탈진 중에는 색이 갈린다 — 바가 조금
        /// 차 있는데도 Shift 가 안 먹는 구간이 있어서, 그걸 설명해 주는 것이 색의 일이다.</summary>
        public void SetStamina01(float value01, bool exhausted)
        {
            ResolveElements();
            if (_staminaFill == null) return;

            _staminaFill.style.width = Length.Percent(Mathf.Clamp01(value01) * 100f);
            _staminaFill.EnableInClassList("stamina-fill-exhausted", exhausted);
        }

        /// <summary>오른쪽 위 현재 점수를 갱신한다.</summary>
        public void SetScore(int score)
        {
            ResolveElements();
            if (_scoreChipValue == null) return;

            _scoreChipValue.text = score.ToString("N0");
        }

        /// <summary>가운데 위 남은 시간을 갱신한다.</summary>
        public void SetRemainingSeconds(float seconds)
        {
            ResolveElements();
            if (_stageClockTime == null) return;

            int total = Mathf.Max(0, Mathf.CeilToInt(seconds));
            _stageClockTime.text = $"{total / 60}:{total % 60:00}";
            _stageClockTime.EnableInClassList("stage-clock-warning", seconds <= ClockWarningSeconds);

            bool critical = seconds > 0f && seconds <= ClockCriticalSeconds;
            _stageClockTime.EnableInClassList("stage-clock-critical", critical);

            if (critical != _isClockCritical)
            {
                _isClockCritical = critical;

                // 문턱을 넘는 순간에만 한 번 친다. 매 프레임 치면 진동이 끊기지 않는다.
                if (critical && _clockCriticalFeedback != null)
                {
                    _clockCriticalFeedback.StopFeedbacks();
                    _clockCriticalFeedback.PlayFeedbacks(transform.position);
                }
                if (!critical) RestoreClockText();
            }

            // 남은 초가 실제로 바뀌는 순간에만 킥을 준다. 매 프레임 흔들면 그냥 떨리는 UI가 된다.
            if (critical && total != _lastShownSecond) _clockTickStartedAt = Time.unscaledTime;
            _lastShownSecond = total;

            if (!critical) return;

            // 깜빡임은 글자에만. 배경까지 깜빡이면 화면이 시끄럽다.
            bool on = Mathf.Repeat(Time.unscaledTime, ClockBlinkPeriodSeconds * 2f) < ClockBlinkPeriodSeconds;
            _stageClockTime.style.opacity = on ? 1f : 0.35f;
        }

        private void RestoreClockText()
        {
            if (_stageClockTime == null) return;
            _stageClockTime.style.opacity = 1f;
            _stageClockTime.style.scale = new Scale(Vector2.one);
            _stageClockTime.style.translate = new Translate(0f, 0f, 0f);
            _clockTickStartedAt = -1f;
        }

        /// <summary>초가 줄 때마다 글자를 한 번 키웠다 되돌리고 좌우로 떤다.
        /// 감쇠하는 사인이라 킥이 시작에서 가장 크고 금방 잦아든다.</summary>
        private void TickClockPunch()
        {
            if (_stageClockTime == null || _clockTickStartedAt < 0f) return;

            float elapsed = Time.unscaledTime - _clockTickStartedAt;
            if (elapsed >= ClockTickPunchSeconds)
            {
                _stageClockTime.style.scale = new Scale(Vector2.one);
                _stageClockTime.style.translate = new Translate(0f, 0f, 0f);
                _clockTickStartedAt = -1f;
                return;
            }

            float decay = 1f - elapsed / ClockTickPunchSeconds;
            float punch = 1f + ClockTickPunchScale * decay * decay;
            float shake = Mathf.Sin(elapsed * ClockTickShakeHz) * ClockTickShakePixels * decay;
            _stageClockTime.style.scale = new Scale(new Vector2(punch, punch));
            _stageClockTime.style.translate = new Translate(shake, 0f, 0f);
        }

        /// <summary>의뢰를 깨서 시간이 늘었을 때 시계 아래에 잠깐 띄운다.</summary>
        public void ShowTimeGain(float seconds)
        {
            ResolveElements();
            if (_stageClockGain == null || seconds <= 0f) return;

            _stageClockGain.text = $"+{Mathf.RoundToInt(seconds)}s";
            _stageClockGain.style.opacity = 1f;
            _stageClockGain.style.scale = new Scale(Vector2.one * (1f + GainPopScale));
            _stageClockGain.style.translate = new Translate(0f, 0f, 0f);
            _clockGainShownAt = Time.unscaledTime;
        }

        /// <summary>보너스 표시는 시간이 지나면 스스로 사라진다. 다음 완료가 겹쳐도
        /// 타이머만 새로 시작하면 되므로 코루틴을 쓰지 않는다.</summary>
        private void TickTimeGainFade()
        {
            if (_stageClockGain == null || _clockGainShownAt < 0f) return;

            float elapsed = Time.unscaledTime - _clockGainShownAt;

            // 뜨는 순간 크게 나왔다가 제 크기로 내려앉는다.
            float pop = Mathf.Clamp01(elapsed / GainPopSeconds);
            float scale = Mathf.Lerp(1f + GainPopScale, 1f, Mathf.SmoothStep(0f, 1f, pop));
            _stageClockGain.style.scale = new Scale(new Vector2(scale, scale));

            if (elapsed < ClockGainHoldSeconds)
            {
                _stageClockGain.style.opacity = 1f;
                return;
            }

            float fade = Mathf.InverseLerp(ClockGainHoldSeconds, ClockGainHoldSeconds + ClockGainFadeSeconds, elapsed);
            _stageClockGain.style.opacity = 1f - fade;
            // 사라지면서 위로 뜬다. 시계 쪽으로 흡수되는 인상을 준다.
            _stageClockGain.style.translate = new Translate(0f, -GainRisePixels * fade, 0f);
            if (fade >= 1f)
            {
                _clockGainShownAt = -1f;
                _stageClockGain.style.translate = new Translate(0f, 0f, 0f);
            }
        }

        public void SetOrders(IReadOnlyList<StageHudOrderView> orders)
        {
            ResolveElements();
            if (_orderStack == null) return;

            if (OrderSetChanged(orders)) RebuildTickets(orders);
            for (int index = 0; index < orders.Count; index++)
                _tickets[index].Update(orders[index]);

            bool waiting = orders.Count == 0;
            _orderStack.style.display = waiting ? DisplayStyle.None : DisplayStyle.Flex;
            _waitingTicket.style.display = waiting ? DisplayStyle.Flex : DisplayStyle.None;
        }

        private bool OrderSetChanged(IReadOnlyList<StageHudOrderView> orders)
        {
            if (_displayedOrderIds.Count != orders.Count) return true;
            for (int index = 0; index < orders.Count; index++)
                if (_displayedOrderIds[index] != orders[index].Id) return true;
            return false;
        }

        private void RebuildTickets(IReadOnlyList<StageHudOrderView> orders)
        {
            int addedOrderIndex = -1;
            bool removedOrder = false;
            for (int index = 0; index < _displayedOrderIds.Count; index++)
            {
                bool stillDisplayed = false;
                for (int next = 0; next < orders.Count; next++)
                {
                    if (_displayedOrderIds[index] != orders[next].Id) continue;
                    stillDisplayed = true;
                    break;
                }
                if (!stillDisplayed) removedOrder = true;
            }

            for (int index = 0; index < orders.Count; index++)
            {
                if (!_displayedOrderIds.Contains(orders[index].Id))
                {
                    addedOrderIndex = index;
                    break;
                }
            }

            FinishOrderAddedAnimation();
            _displayedOrderIds.Clear();
            _tickets.Clear();
            _orderStack.Clear();

            for (int index = 0; index < orders.Count; index++)
            {
                StageHudOrderView order = orders[index];
                var ticket = new OrderTicket(index);
                _displayedOrderIds.Add(order.Id);
                _tickets.Add(ticket);
                _orderStack.Add(ticket.Root);
            }

            if (addedOrderIndex >= 0 && addedOrderIndex < _tickets.Count)
            {
                PlayOrderAddedAnimation(_tickets[addedOrderIndex].Root);
                _orderAdded.Invoke();
            }
            if (removedOrder) _orderRemoved.Invoke();
        }

        private void PlayOrderAddedAnimation(VisualElement ticket)
        {
            _animatedTicket = ticket;
            _orderAddedAnimationStartedAt = Time.unscaledTime;
            _isOrderAddedAnimationPlaying = true;

            ApplyOrderAddedStartVisual();

            // Feel 은 스케일 곡선만 얹어 준다. 없으면 코드가 같은 구간을 직접 이징한다 —
            // 등장 연출이 씬 배선 유무에 걸려 사라지면 안 된다.
            if (_orderAddedScaleDriver == null || _orderAddedFeedback == null) return;

            _orderAddedScaleDriver.localScale = Vector3.one * TicketStartScale;
            _orderAddedFeedback.StopFeedbacks();
            for (int index = 0; index < _orderAddedFeedback.FeedbacksList.Count; index++)
            {
                if (!(_orderAddedFeedback.FeedbacksList[index] is MMF_Scale scaleFeedback)) continue;
                scaleFeedback.AnimateScaleTarget = _orderAddedScaleDriver;
                scaleFeedback.AnimateScaleDuration = _orderAddedAnimationSeconds;
                scaleFeedback.RemapCurveZero = TicketStartScale;
                scaleFeedback.RemapCurveOne = 1f;
            }
            _orderAddedFeedback.Initialization(true);
            _orderAddedFeedback.PlayFeedbacks(transform.position);
        }

        private void ApplyOrderAddedStartVisual()
        {
            if (_animatedTicket == null) return;
            _animatedTicket.style.opacity = 0f;
            _animatedTicket.style.translate = new Translate(TicketEnterFromRightPixels, 0f, 0f);
            _animatedTicket.style.rotate = new Rotate(TicketStartRotation);
            _animatedTicket.style.scale = new Scale(Vector2.one * TicketStartScale);
        }

        private static float EaseOutCubic(float value)
        {
            value = Mathf.Clamp01(value);
            float inverse = 1f - value;
            return 1f - inverse * inverse * inverse;
        }

        private void FinishOrderAddedAnimation()
        {
            if (_orderAddedFeedback != null && _isOrderAddedAnimationPlaying)
                _orderAddedFeedback.StopFeedbacks();

            if (_animatedTicket != null)
            {
                _animatedTicket.style.opacity = 1f;
                _animatedTicket.style.translate = new Translate(0f, 0f, 0f);
                _animatedTicket.style.rotate = new Rotate(0f);
                _animatedTicket.style.scale = new Scale(Vector2.one);
            }

            if (_orderAddedScaleDriver != null) _orderAddedScaleDriver.localScale = Vector3.one;
            _animatedTicket = null;
            _isOrderAddedAnimationPlaying = false;
        }

        private sealed class OrderTicket
        {
            private int _lastShownSecond = -1;
            private float _punchStartedAt = -1f;

            /// <summary>남은 초가 바뀔 때마다 숫자를 한 번 키웠다 되돌린다. 시계와 같은 감쇠식이라
            /// 두 경고가 같은 리듬으로 읽힌다.</summary>
            public void TickPunch()
            {
                if (Time == null || _punchStartedAt < 0f) return;

                float elapsed = UnityEngine.Time.unscaledTime - _punchStartedAt;
                if (elapsed >= ClockTickPunchSeconds)
                {
                    Time.style.scale = new Scale(Vector2.one);
                    _punchStartedAt = -1f;
                    return;
                }

                float decay = 1f - elapsed / ClockTickPunchSeconds;
                float punch = 1f + ClockTickPunchScale * decay * decay;
                Time.style.scale = new Scale(new Vector2(punch, punch));
            }

            public OrderTicket(int stackIndex)
            {
                Root = Element("order-ticket");
                if (stackIndex > 0) Root.AddToClassList("order-ticket-overlap");
                if ((stackIndex & 1) != 0) Root.AddToClassList("order-ticket-offset");
                Root.Add(Element("order-ticket-shadow"));

                VisualElement paper = Element("order-ticket-paper");
                Root.Add(paper);
                paper.Add(Element("order-ticket-frame"));
                GiftColor = Element("ticket-gift-color");
                paper.Add(GiftColor);

                VisualElement summary = Element("ticket-summary");
                VisualElement giftIcon = Element("ticket-gift-icon");
                giftIcon.Add(Element("ticket-gift-bow-left"));
                giftIcon.Add(Element("ticket-gift-bow-right"));
                GiftBox = Element("ticket-gift-body");
                giftIcon.Add(GiftBox);
                GiftLid = Element("ticket-gift-lid");
                giftIcon.Add(GiftLid);
                giftIcon.Add(Element("ticket-gift-knot"));
                summary.Add(giftIcon);

                VisualElement timeBlock = Element("ticket-time-block");
                Time = Text("--:--", "order-time");
                timeBlock.Add(Time);
                summary.Add(timeBlock);
                paper.Add(summary);

                Navigation = Element("ticket-navigation");
                AddPerforation(Navigation, 8);
                VisualElement route = Element("ticket-route-row");
                VisualElement directionPlate = Element("order-direction-plate");
                Direction = Element("order-direction");
                Direction.Add(Element("direction-arrow-head"));
                Direction.Add(Element("direction-arrow-stem"));
                directionPlate.Add(Direction);
                Distance = Text("-- m", "order-distance");
                route.Add(directionPlate);
                route.Add(Distance);
                Navigation.Add(route);
                paper.Add(Navigation);
                paper.Add(Element("order-ticket-fold"));
            }

            public VisualElement Root { get; }
            private VisualElement GiftColor { get; }
            private VisualElement GiftBox { get; }
            private VisualElement GiftLid { get; }
            private VisualElement Navigation { get; }
            private VisualElement Direction { get; }
            private Label Distance { get; }
            private Label Time { get; }

            public void Update(StageHudOrderView order)
            {
                GiftColor.style.backgroundColor = order.GiftColor;
                GiftBox.style.backgroundColor = order.GiftColor;
                GiftLid.style.backgroundColor = order.GiftColor;

                int seconds = Mathf.CeilToInt(Mathf.Max(0f, order.RemainingSeconds));
                Time.text = $"{seconds / 60}:{seconds % 60:00}";
                Time.EnableInClassList("order-time-warning", order.RemainingSeconds <= WarningThresholdSeconds);

                bool critical = order.RemainingSeconds > 0f && order.RemainingSeconds <= OrderCriticalSeconds;
                Time.EnableInClassList("order-time-critical", critical);
                if (critical && seconds != _lastShownSecond) _punchStartedAt = UnityEngine.Time.unscaledTime;
                _lastShownSecond = seconds;
                if (!critical)
                {
                    _punchStartedAt = -1f;
                    Time.style.opacity = 1f;
                    Time.style.scale = new Scale(Vector2.one);
                }
                else
                {
                    bool on = Mathf.Repeat(UnityEngine.Time.unscaledTime, ClockBlinkPeriodSeconds * 2f)
                              < ClockBlinkPeriodSeconds;
                    Time.style.opacity = on ? 1f : 0.35f;
                }

                Root.EnableInClassList("order-ticket-navigation-visible", order.ShowNavigation);
                Navigation.style.display = order.ShowNavigation ? DisplayStyle.Flex : DisplayStyle.None;
                if (!order.ShowNavigation) return;

                Distance.text = $"{order.DistanceMeters:0} m";
                Direction.style.rotate = new Rotate(new Angle(order.DirectionDegrees, AngleUnit.Degree));
            }

            private static VisualElement Element(string className)
            {
                var element = new VisualElement { pickingMode = PickingMode.Ignore };
                element.AddToClassList(className);
                return element;
            }

            private static Label Text(string text, string className)
            {
                var label = new Label(text) { pickingMode = PickingMode.Ignore };
                label.AddToClassList(className);
                return label;
            }

            private static void AddPerforation(VisualElement parent, int dashCount)
            {
                VisualElement perforation = Element("receipt-perforation");
                for (int index = 0; index < dashCount; index++) perforation.Add(Element("receipt-dash"));
                parent.Add(perforation);
            }
        }
    }
}
