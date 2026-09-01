using UnityEngine;
using UnityEngine.UIElements;

namespace PPack
{
    /// <summary>
    /// TutorialPlay 전용 소형 퀘스트 기록 UI. 화면을 가리는 훈련 카드 대신 현재 배송 사이클과
    /// 완료한 작업만 조용히 기록한다.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public sealed class TutorialQuestJournal : MonoBehaviour
    {
        private const int StepCount = 8;

        private static readonly string[] StepNames =
        {
            "마을길 걷기",
            "달려서 이동하기",
            "눈길 슬라이딩",
            "눈덩이 굴리기",
            "제작기에 눈 넣기",
            "우편 단말기로 보내기",
            "창고에서 택배 꺼내기",
            "옆집 앞마당에 배달하기"
        };

        private UIDocument _document;
        private Label _counter;
        private Label _currentTitle;
        private Label _currentHint;
        private Label _progressText;
        private VisualElement _progressFill;
        private VisualElement _journal;
        private Label[] _marks;
        private Label[] _labels;
        private int _currentIndex = -1;

        private void Awake()
        {
            _document = GetComponent<UIDocument>();
        }

        private void OnEnable()
        {
            TryBind();
        }

        public void ShowStep(int index, string title, string key, string instruction)
        {
            if (!TryBind())
            {
                StartCoroutine(ShowStepWhenReady(index, title, key, instruction));
                return;
            }

            _currentIndex = Mathf.Clamp(index, 0, StepCount - 1);
            _journal.RemoveFromClassList("quest-journal--complete");
            _counter.text = $"DELIVERY LOG  {_currentIndex + 1:00} / {StepCount:00}";
            _currentTitle.text = title;
            _currentHint.text = string.IsNullOrWhiteSpace(key)
                ? instruction
                : $"{key}  ·  {instruction}";

            for (int rowIndex = 0; rowIndex < StepCount; rowIndex++)
            {
                bool done = rowIndex < _currentIndex;
                bool active = rowIndex == _currentIndex;
                _marks[rowIndex].text = done ? "✓" : active ? "●" : "○";
                _labels[rowIndex].text = StepNames[rowIndex];
                _labels[rowIndex].EnableInClassList("quest-label--done", done);
                _labels[rowIndex].EnableInClassList("quest-label--active", active);
                _marks[rowIndex].EnableInClassList("quest-mark--done", done);
                _marks[rowIndex].EnableInClassList("quest-mark--active", active);
            }
        }

        public void SetProgress(float progress01, string text)
        {
            if (!TryBind()) return;
            _progressFill.style.width = Length.Percent(Mathf.Clamp01(progress01) * 100f);
            _progressText.text = text;
        }

        public void ShowSuccess()
        {
            if (!TryBind() || _currentIndex < 0) return;
            _marks[_currentIndex].text = "✓";
            _marks[_currentIndex].AddToClassList("quest-mark--done");
            _labels[_currentIndex].AddToClassList("quest-label--done");
            _progressFill.style.width = Length.Percent(100f);
            _progressText.text = "완료";
        }

        public void ShowComplete()
        {
            if (!TryBind()) return;
            _journal.AddToClassList("quest-journal--complete");
            _counter.text = "DELIVERY LOG  COMPLETE";
            _currentTitle.text = "배송 한 사이클 완료";
            _currentHint.text = "눈을 선물로 만들어 창고를 거쳐 이웃에게 전달했습니다.";
            _progressFill.style.width = Length.Percent(100f);
            _progressText.text = "마을 배송 완료";
            for (int index = 0; index < StepCount; index++)
            {
                _marks[index].text = "✓";
                _marks[index].AddToClassList("quest-mark--done");
                _marks[index].RemoveFromClassList("quest-mark--active");
                _labels[index].AddToClassList("quest-label--done");
                _labels[index].RemoveFromClassList("quest-label--active");
            }
        }

        private bool TryBind()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            VisualElement root = _document != null ? _document.rootVisualElement : null;
            if (root == null) return false;

            _journal = root.Q<VisualElement>("quest-journal");
            _counter = root.Q<Label>("quest-counter");
            _currentTitle = root.Q<Label>("quest-current-title");
            _currentHint = root.Q<Label>("quest-current-hint");
            _progressText = root.Q<Label>("quest-progress-text");
            _progressFill = root.Q<VisualElement>("quest-progress-fill");
            _marks = new Label[StepCount];
            _labels = new Label[StepCount];
            for (int index = 0; index < StepCount; index++)
            {
                _marks[index] = root.Q<Label>($"quest-mark-{index}");
                _labels[index] = root.Q<Label>($"quest-label-{index}");
            }

            if (_journal == null || _counter == null || _currentTitle == null ||
                _currentHint == null || _progressText == null || _progressFill == null)
                return false;
            for (int index = 0; index < StepCount; index++)
                if (_marks[index] == null || _labels[index] == null) return false;
            return true;
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
