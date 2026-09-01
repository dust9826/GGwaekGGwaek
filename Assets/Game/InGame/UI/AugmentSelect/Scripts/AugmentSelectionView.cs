using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>증강 카드 세 장을 띄우고 고른 것을 돌려준다.
    ///
    /// <para><b>판정하지 않는다.</b> 확정은 <see cref="AugmentSelectionDirector.Confirm"/> 가
    /// 하고, 이 화면은 무엇을 눌렀는지만 알린다. 그래서 화면 없이도 흐름을 테스트할 수 있다.</para>
    ///
    /// <para>표시 문자열은 영어다 — <c>StageHUD</c>(SCORE·ORDERS)와 <c>StageOutro</c>
    /// (ROUTE COMPLETE)가 이미 그렇다(스펙 §9).</para></summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class AugmentSelectionView : MonoBehaviour
    {
        private VisualElement _root;
        private VisualElement _cardRow;
        private Label _dayLabel;
        private Label _headerTitle;
        private Label _headerNote;
        private VisualElement _timerGroup;
        private Label _timerLabel;
        private System.Action<AugmentDefinition> _onPick;
        private int _dayIndex;

        // 카드마다 하나씩. 다시 그리지 않고 숫자만 갈아 끼우려고 들고 있는다.
        private readonly List<VisualElement> _voteGroups = new List<VisualElement>();
        private readonly List<Label> _voteLabels = new List<Label>();
        private readonly List<VisualElement> _cardElements = new List<VisualElement>();

        private void OnEnable()
        {
            EnsureRoot();
            HideRoot();
        }

        /// <summary>카드를 그리고 화면을 켠다. <paramref name="dayIndex"/> 는 머리말에만 쓴다.</summary>
        public void Show(IReadOnlyList<AugmentDefinition> cards, int dayIndex,
            System.Action<AugmentDefinition> onPick)
        {
            EnsureRoot();
            if (_root is null || cards is null) return;

            _onPick = onPick;
            _dayIndex = dayIndex;
            if (_dayLabel is not null) _dayLabel.text = $"DAY {dayIndex + 1}";

            _voteGroups.Clear();
            _voteLabels.Clear();
            _cardElements.Clear();
            _cardRow.Clear();
            for (int index = 0; index < cards.Count; index++)
            {
                AugmentDefinition definition = cards[index];
                if (definition == null) continue;
                _cardRow.Add(BuildCard(definition, index));
            }

            _root.style.display = DisplayStyle.Flex;
        }

        /// <summary>
        /// 표를 그린다. <b>2인 이상일 때만 보인다</b> — 혼자 하는 판에는 표라는 개념이 없고,
        /// 빈 배지가 남아 있으면 "아직 아무도 안 골랐다" 로 잘못 읽힌다.
        ///
        /// <para><b>새 상태를 만들지 않는다.</b> 값은 전부 <c>AugmentNetHub</c> 가 이미 복제해 둔
        /// 표를 센 결과다 — 루트 규약의 "연출은 실제로 적용된 복제 상태에서 끌어낸다".</para>
        ///
        /// <para><c>LocalPick</c> 은 <b>이 화면을 보는 사람</b>의 표이고 노란 테두리로 표시한다.
        /// <c>Winner</c> 가 정해지면(0 이상) 결과 단계로 넘어가 이긴 카드만 남기고 나머지를 흐리며,
        /// <c>WinnerWasTie</c> 면 무작위로 골랐다는 사실을 머리말에 밝힌다.</para>
        ///
        /// <para>0표인 카드는 배지를 띄우지 않는다. 세 장 중 둘에 <c>0</c> 이 붙어 있으면
        /// 정보가 아니라 잡음이고, 몇 명이 아직 안 골랐는지는 머리말이 이미 말한다.</para>
        /// </summary>
        public void SetVotes(in AugmentVoteDisplay display)
        {
            bool multiplayer = display.Total >= 2;
            bool decided = display.Winner >= 0;
            IReadOnlyList<int> perCard = display.PerCard;

            for (int index = 0; index < _voteGroups.Count; index++)
            {
                int count = multiplayer && perCard is not null && index < perCard.Count
                    ? perCard[index]
                    : 0;

                _voteGroups[index].style.display = count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
                if (count > 0) _voteLabels[index].text = count.ToString();

                VisualElement card = _cardElements[index];

                // 내 표는 결과가 나오기 전까지만 표시한다. 결과가 나온 뒤에도 남기면
                // "내가 고른 것" 과 "이긴 것" 두 테두리가 동시에 켜져 무엇이 결과인지 흐려진다.
                card.EnableInClassList("is-mypick", !decided && index == display.LocalPick);
                card.EnableInClassList("is-winner", decided && index == display.Winner);
                card.EnableInClassList("is-loser", decided && index != display.Winner);
            }

            // 타이머는 여럿이 고르는 동안에만 뜻이 있다. 혼자면 기다리는 사람이 없고,
            // 결과가 나온 뒤에는 셀 것이 없다.
            bool showTimer = multiplayer && !decided;
            if (_timerGroup is not null)
                _timerGroup.style.display = showTimer ? DisplayStyle.Flex : DisplayStyle.None;
            if (showTimer && _timerLabel is not null)
                _timerLabel.text = Mathf.CeilToInt(Mathf.Max(0f, display.SecondsLeft)).ToString();

            // 결과가 나왔는데 제목이 "고르라" 로 남아 있으면 화면이 스스로와 모순된다.
            if (_headerTitle is not null)
                _headerTitle.text = decided ? "AUGMENT PICKED" : "CHOOSE AN AUGMENT";

            if (_dayLabel is not null)
            {
                _dayLabel.text = !decided && multiplayer
                    ? $"DAY {_dayIndex + 1}  ·  {display.Voted} / {display.Total} VOTED"
                    : $"DAY {_dayIndex + 1}";
            }

            if (_headerNote is null) return;

            // 동점을 무작위로 푼 것은 <b>반드시 말해 준다.</b> 말하지 않으면 표를 세는 규칙이
            // 틀린 것처럼 보인다 — 두 장이 같은 표인데 한 장이 이겼으니까.
            if (decided && display.WinnerWasTie) _headerNote.text = "TIED — PICKED AT RANDOM";
            else if (decided) _headerNote.text = "MOST VOTES WINS";
            else _headerNote.text = HeaderNoteDefault;
        }

        private const string HeaderNoteDefault = "THE GAME IS PAUSED WHILE YOU CHOOSE";

        public void Hide()
        {
            _onPick = null;
            HideRoot();
        }

        private void HideRoot()
        {
            if (_root is null) return;
            _root.style.display = DisplayStyle.None;
            _voteGroups.Clear();
            _voteLabels.Clear();
            _cardElements.Clear();
            _cardRow?.Clear();
        }

        private void EnsureRoot()
        {
            if (_root is not null) return;

            UIDocument document = GetComponent<UIDocument>();
            if (document == null) return;

            _root = document.rootVisualElement?.Q<VisualElement>("augment-select-root");
            if (_root is null) return;

            _cardRow = _root.Q<VisualElement>("card-row");
            _dayLabel = _root.Q<Label>("day-label");
            _headerTitle = _root.Q<Label>("header-title");
            _headerNote = _root.Q<Label>("header-note");
            _timerGroup = _root.Q<VisualElement>("vote-timer");
            _timerLabel = _root.Q<Label>("vote-timer-label");
        }

        /// <summary>
        /// 카드 한 장. <b>보이는 것은 전부 자식이고 이 <c>Button</c> 자신은 껍데기다.</b>
        ///
        /// <para>USS 에 <c>box-shadow</c> 가 없어서, <c>InGame/UI/AGENTS.md</c> 가 요구하는
        /// "최소 3단 깊이" 를 요소를 겹쳐서 만든다 — 그림자 면 → 흰 스티커 외곽 → 크림 면 →
        /// 왼쪽 강조 바. <c>StageIntro</c> 가 같은 방식이고 클래스 이름도 맞춰 뒀다.</para>
        ///
        /// <para><b>루트는 계속 <c>Button</c> 이고 이름도 그대로다.</b>
        /// <c>AugmentSelectionPlayModeTests</c> 가 <c>augment-card-{id}</c> 로 찾아
        /// <c>ClickEvent</c> 를 보내므로, 속을 아무리 바꿔도 이 둘은 건드리지 않는다.</para>
        /// </summary>
        private VisualElement BuildCard(AugmentDefinition definition, int index)
        {
            var card = new Button { name = $"augment-card-{definition.Id}" };
            card.AddToClassList("augment-card");

            // `clicked` 가 아니라 ClickEvent 를 듣는다. 둘 다 실제 클릭에서 동작하지만 `clicked` 는
            // Clickable 매니퓰레이터가 PointerDown/Up 으로만 구동해서 **합성 이벤트로 검증할 수가
            // 없다.** 이쪽이면 같은 동작을 유지하면서 테스트가 이 경로를 실제로 지날 수 있다.
            card.RegisterCallback<ClickEvent>(_ => _onPick?.Invoke(definition));

            card.Add(Div("card-shadow"));

            var outline = Div("card-outline");
            var face = Div("card-face");
            outline.Add(face);
            card.Add(outline);

            // 강조 바는 외곽 위에 얹는다 — 세 장을 눈으로 가르는 유일한 신호다.
            card.Add(Div("card-accent", AccentClass(index)));

            // 번호 배지. 그림자를 별도 요소로 두고 아래로 내리는 것이 StageIntro 의 처리다.
            card.Add(Div("card-tab-shadow"));
            var tab = Div("card-tab");
            var tabLabel = new Label($"#{index + 1:00}");
            tabLabel.AddToClassList("card-tab__label");
            tab.Add(tabLabel);
            card.Add(tab);

            // 표 배지. 왼쪽 번호 탭과 같은 스티커 처리이고 색만 중립 남색이다 —
            // "한 컴포넌트의 핵심색은 셋까지" 라 새 주색을 만들지 않는다(InGame/UI/AGENTS.md).
            // 처음에는 숨겨 두고 SetVotes 가 켠다. 혼자 하는 판에서는 끝까지 안 켜진다.
            var voteGroup = Div("vote-group");
            voteGroup.Add(Div("vote-badge-shadow"));
            var voteBadge = Div("vote-badge");
            var voteLabel = new Label("0");
            voteLabel.AddToClassList("vote-badge__label");
            voteBadge.Add(voteLabel);
            voteGroup.Add(voteBadge);
            voteGroup.style.display = DisplayStyle.None;
            card.Add(voteGroup);
            _voteGroups.Add(voteGroup);
            _voteLabels.Add(voteLabel);
            _cardElements.Add(card);

            // Lilita One 은 표시용 대문자 서체이고 USS 에는 text-transform 이 없다.
            var name = new Label(definition.DisplayName.ToUpperInvariant());
            name.AddToClassList("augment-card__name");
            face.Add(name);

            var description = new Label(definition.Description);
            description.AddToClassList("augment-card__desc");
            face.Add(description);

            face.Add(Div("card-divider"));

            var effects = new VisualElement();
            effects.AddToClassList("augment-card__effects");
            AddEffects(effects, definition.Benefits, isBenefit: true);
            AddEffects(effects, definition.Penalties, isBenefit: false);
            face.Add(effects);

            return card;
        }

        private static VisualElement Div(params string[] classes)
        {
            var element = new VisualElement { pickingMode = PickingMode.Ignore };
            for (int index = 0; index < classes.Length; index++) element.AddToClassList(classes[index]);
            return element;
        }

        private static string AccentClass(int index) => (index % 3) switch
        {
            0 => "card-accent-a",
            1 => "card-accent-b",
            _ => "card-accent-c",
        };

        /// <summary>
        /// 한 줄을 화살표·이름·값 <b>셋으로 나눠 담는다.</b> 전에는 한 문자열이었는데,
        /// 그러면 값이 이름 길이를 따라 흔들려 카드 세 장을 세로로 비교할 수가 없다.
        /// USS 에 <c>tabular-nums</c> 가 없으므로 값 칸의 폭을 USS 에서 직접 잡는다.
        /// </summary>
        private static void AddEffects(VisualElement parent, AugmentEffect[] effects, bool isBenefit)
        {
            if (effects is null) return;

            for (int index = 0; index < effects.Length; index++)
            {
                var row = new VisualElement { pickingMode = PickingMode.Ignore };
                row.AddToClassList("effect");
                row.AddToClassList(isBenefit ? "is-benefit" : "is-penalty");

                var arrow = new Label(isBenefit ? "\u25B2" : "\u25BC");
                arrow.AddToClassList("effect__arrow");
                row.Add(arrow);

                var label = new Label(Label(effects[index].Stat));
                label.AddToClassList("effect__label");
                row.Add(label);

                var value = new Label(Amount(effects[index]));
                value.AddToClassList("effect__value");
                row.Add(value);

                parent.Add(row);
            }
        }

        /// <summary>부호까지 포함한 값 한 덩이. 화살표가 방향을 이미 말하므로 여기서는 크기만 쓴다.</summary>
        private static string Amount(AugmentEffect effect)
        {
            float percent = effect.Value * 100f;
            string sign = percent >= 0f ? "+" : "\u2212";
            string unit = IsChance(effect.Stat) ? "%p" : "%";
            return $"{sign}{Mathf.Abs(percent):0.#}{unit}";
        }

        /// <summary>확률 축은 배율이 아니라 퍼센트 포인트다.</summary>
        private static bool IsChance(EAugmentStat stat) => stat == EAugmentStat.ExtraGiftChance;

        private static string Label(EAugmentStat stat) => stat switch
        {
            EAugmentStat.ClearTimeBonus => "Clear time bonus",
            EAugmentStat.RequestTtl => "Order time",
            EAugmentStat.Reward => "Reward",
            EAugmentStat.WalkSpeed => "Walk speed",
            EAugmentStat.ExtraGiftChance => "Extra gift chance",
            _ => stat.ToString(),
        };
    }
}
