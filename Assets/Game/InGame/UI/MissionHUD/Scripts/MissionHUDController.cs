using System;
using System.Collections.Generic;
using DG.Tweening;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UIElements;

namespace PPack
{
    public readonly struct MissionHUDItem
    {
        public MissionHUDItem(string id, string description, int current, int target)
        {
            Id = id;
            Description = description;
            Current = current;
            Target = target;
        }

        public string Id { get; }
        public string Description { get; }
        public int Current { get; }
        public int Target { get; }
    }

    [RequireComponent(typeof(UIDocument))]
    public sealed class MissionHUDController : MonoBehaviour
    {
        private const string CompleteClass = "mission-row-complete";
        private const float RowHeight = 58f;

        [Header("Feel Feedback Hooks")]
        [Tooltip("Feel이 스케일을 움직이는 중립 Transform입니다. UI Toolkit 카드 스케일로 전달됩니다.")]
        [SerializeField] private Transform _feelScaleDriver;
        [Tooltip("Inspector에서 이벤트 수락용 MMF_Player.PlayFeedbacks를 연결합니다.")]
        [SerializeField] private UnityEvent _missionReceivedFeedback = new UnityEvent();
        [Tooltip("Inspector에서 미션 완료용 MMF_Player.PlayFeedbacks를 연결합니다.")]
        [SerializeField] private UnityEvent _missionClearedFeedback = new UnityEvent();

        private sealed class MissionRow
        {
            public string Id;
            public VisualElement Root;
            public Label Progress;
            public int Current;
            public int Target;
        }

        private readonly Dictionary<string, MissionRow> _rows = new Dictionary<string, MissionRow>();
        private VisualElement _hudRoot;
        private VisualElement _card;
        private VisualElement _missionList;
        private Label _overallProgress;
        private Sequence _introSequence;
        private int _receiveGeneration;
        private Vector3 _feelScaleBase = Vector3.one;
        private bool _feelScaleBaseCaptured;

        public bool IsVisible => _hudRoot != null && _hudRoot.resolvedStyle.display != DisplayStyle.None;

        private void OnEnable()
        {
            VisualElement root = GetComponent<UIDocument>().rootVisualElement;
            _hudRoot = root.Q<VisualElement>("mission-hud-root");
            _card = root.Q<VisualElement>("mission-card");
            _missionList = root.Q<VisualElement>("mission-list");
            _overallProgress = root.Q<Label>("overall-progress");

            CaptureFeelScaleBase();
            ResetTransientVisualState();
            SetVisible(false);
        }

        private void OnDisable()
        {
            _introSequence?.Kill();
            DOTween.Kill(this);
            DOTween.Kill(_card);

            foreach (MissionRow row in _rows.Values)
            {
                DOTween.Kill(row.Root);
                DOTween.Kill(row.Progress);
            }

            ResetTransientVisualState();
        }

        private void Update()
        {
            if (_feelScaleDriver == null || _card == null)
            {
                return;
            }

            Vector3 feelScale = _feelScaleDriver.localScale;
            float baseX = Mathf.Abs(_feelScaleBase.x) < 0.0001f ? 1f : _feelScaleBase.x;
            float baseY = Mathf.Abs(_feelScaleBase.y) < 0.0001f ? 1f : _feelScaleBase.y;
            _card.style.scale = new Scale(new Vector2(feelScale.x / baseX, feelScale.y / baseY));
        }

        public void SetMissions(IReadOnlyList<MissionHUDItem> missions)
        {
            if (_missionList == null)
            {
                return;
            }

            _receiveGeneration++;
            ClearRows();

            if (missions == null || missions.Count == 0)
            {
                SetVisible(false);
                return;
            }

            for (int index = 0; index < missions.Count; index++)
            {
                MissionHUDItem item = missions[index];
                AddMissionInternal(item, false, index == missions.Count - 1);
            }

            RefreshOverallProgress();
            SetVisible(true);
            _hudRoot.schedule.Execute(PlayIntro).StartingIn(1);
        }

        public void ReceiveMissions(IReadOnlyList<MissionHUDItem> missions, int staggerMilliseconds = 120)
        {
            _receiveGeneration++;
            int generation = _receiveGeneration;
            ClearRows();

            if (missions == null || missions.Count == 0)
            {
                SetVisible(false);
                return;
            }

            SetVisible(true);
            _hudRoot.schedule.Execute(PlayIntro).StartingIn(1);

            for (int index = 0; index < missions.Count; index++)
            {
                MissionHUDItem item = missions[index];
                bool isLast = index == missions.Count - 1;
                _hudRoot.schedule.Execute(() =>
                {
                    if (generation == _receiveGeneration)
                    {
                        AddMissionInternal(item, true, isLast);
                    }
                }).StartingIn(Mathf.Max(0, staggerMilliseconds) * index);
            }
        }

        public bool CompleteAndRemoveMission(string missionId, int removeDelayMilliseconds = 320)
        {
            if (string.IsNullOrWhiteSpace(missionId) || !_rows.TryGetValue(missionId, out MissionRow row))
            {
                return false;
            }

            SetProgress(missionId, row.Target, row.Target);
            _hudRoot.schedule.Execute(() => RemoveMission(missionId)).StartingIn(Mathf.Max(0, removeDelayMilliseconds));
            return true;
        }

        public bool RemoveMission(string missionId)
        {
            if (string.IsNullOrWhiteSpace(missionId) || !_rows.TryGetValue(missionId, out MissionRow row))
            {
                return false;
            }

            _missionClearedFeedback.Invoke();
            DOTween.Kill(row.Root);
            row.Root.style.overflow = Overflow.Hidden;
            row.Root.style.minHeight = 0f;

            float height = Mathf.Max(RowHeight, row.Root.resolvedStyle.height);
            float opacity = row.Root.resolvedStyle.opacity;
            Vector2 position = row.Root.resolvedStyle.translate;

            DOTween.Sequence()
                .SetTarget(row.Root)
                .SetUpdate(true)
                .Append(DOTween.To(() => height, value =>
                {
                    height = value;
                    row.Root.style.height = value;
                }, 0f, 0.24f).SetEase(Ease.InBack))
                .Join(DOTween.To(() => opacity, value =>
                {
                    opacity = value;
                    row.Root.style.opacity = value;
                }, 0f, 0.16f).SetEase(Ease.InQuad))
                .Join(DOTween.To(() => position, value =>
                {
                    position = value;
                    row.Root.style.translate = new Translate(value.x, value.y, 0f);
                }, new Vector2(24f, 0f), 0.20f).SetEase(Ease.InQuad))
                .OnComplete(() => FinishRemovingRow(missionId, row));
            return true;
        }

        public bool SetProgress(string missionId, int current, int target)
        {
            if (string.IsNullOrWhiteSpace(missionId) || !_rows.TryGetValue(missionId, out MissionRow row))
            {
                return false;
            }

            int safeTarget = Mathf.Max(1, target);
            int safeCurrent = Mathf.Clamp(current, 0, safeTarget);
            bool wasComplete = row.Current >= row.Target;

            row.Current = safeCurrent;
            row.Target = safeTarget;
            row.Progress.text = $"{safeCurrent}/{safeTarget}";
            ApplyCompletionState(row);
            RefreshOverallProgress();
            PlayProgressPunch(row, !wasComplete && safeCurrent >= safeTarget);
            return true;
        }

        public void SetVisible(bool visible)
        {
            if (_hudRoot != null)
            {
                ResetTransientVisualState();
                _hudRoot.style.display = visible ? DisplayStyle.Flex : DisplayStyle.None;
            }
        }

        public void ClearMissions()
        {
            _receiveGeneration++;
            ClearRows();
            SetVisible(false);
        }

        private void AddMissionInternal(MissionHUDItem item, bool animate, bool isLast)
        {
            string id = string.IsNullOrWhiteSpace(item.Id) ? $"mission-{_rows.Count}" : item.Id;
            if (_rows.ContainsKey(id))
            {
                SetProgress(id, item.Current, item.Target);
                return;
            }

            foreach (MissionRow existing in _rows.Values)
            {
                existing.Root.RemoveFromClassList("mission-row-last");
            }

            MissionRow row = CreateRow(id, item.Description, item.Current, item.Target);
            if (isLast)
            {
                row.Root.AddToClassList("mission-row-last");
            }

            _rows[id] = row;
            _missionList.Add(row.Root);
            RefreshOverallProgress();
            _missionReceivedFeedback.Invoke();

            if (animate)
            {
                PlayRowAdded(row);
            }
        }

        private MissionRow CreateRow(string id, string description, int current, int target)
        {
            VisualElement rowRoot = new VisualElement
            {
                pickingMode = PickingMode.Ignore
            };
            rowRoot.AddToClassList("mission-row");

            VisualElement status = new VisualElement { pickingMode = PickingMode.Ignore };
            status.AddToClassList("mission-status");

            VisualElement dot = new VisualElement { pickingMode = PickingMode.Ignore };
            dot.AddToClassList("mission-status-dot");
            status.Add(dot);

            VisualElement checkShort = new VisualElement { pickingMode = PickingMode.Ignore };
            checkShort.AddToClassList("mission-check-short");
            status.Add(checkShort);

            VisualElement checkLong = new VisualElement { pickingMode = PickingMode.Ignore };
            checkLong.AddToClassList("mission-check-long");
            status.Add(checkLong);

            Label descriptionLabel = new Label(string.IsNullOrWhiteSpace(description) ? "미션 목표" : description)
            {
                pickingMode = PickingMode.Ignore
            };
            descriptionLabel.AddToClassList("mission-description");

            int safeTarget = Mathf.Max(1, target);
            int safeCurrent = Mathf.Clamp(current, 0, safeTarget);
            Label progressLabel = new Label($"{safeCurrent}/{safeTarget}")
            {
                pickingMode = PickingMode.Ignore
            };
            progressLabel.AddToClassList("mission-progress");

            rowRoot.Add(status);
            rowRoot.Add(descriptionLabel);
            rowRoot.Add(progressLabel);

            MissionRow row = new MissionRow
            {
                Id = id,
                Root = rowRoot,
                Progress = progressLabel,
                Current = safeCurrent,
                Target = safeTarget
            };
            ApplyCompletionState(row);
            return row;
        }

        private void ApplyCompletionState(MissionRow row)
        {
            row.Root.EnableInClassList(CompleteClass, row.Current >= row.Target);
        }

        private void RefreshOverallProgress()
        {
            int completed = 0;
            foreach (MissionRow row in _rows.Values)
            {
                if (row.Current >= row.Target)
                {
                    completed++;
                }
            }

            if (_overallProgress != null)
            {
                _overallProgress.text = $"{completed} / {_rows.Count}";
            }
        }

        private void PlayIntro()
        {
            if (_card == null || _hudRoot.resolvedStyle.display == DisplayStyle.None)
            {
                return;
            }

            _introSequence?.Kill();
            DOTween.Kill(_card);

            _card.style.opacity = 0f;
            _card.style.translate = new Translate(58f, 0f, 0f);

            _introSequence = DOTween.Sequence()
                .SetTarget(this)
                .SetUpdate(true)
                .Append(FadeTo(_card, 1f, 0.16f).SetEase(Ease.OutSine))
                .Join(MoveTo(_card, Vector2.zero, 0.32f).SetEase(Ease.OutBack));
        }

        private void PlayRowAdded(MissionRow row)
        {
            DOTween.Kill(row.Root);
            row.Root.style.overflow = Overflow.Hidden;
            row.Root.style.minHeight = 0f;
            row.Root.style.height = 0f;
            row.Root.style.opacity = 0f;
            row.Root.style.translate = new Translate(24f, 0f, 0f);

            float height = 0f;
            float opacity = 0f;
            Vector2 position = new Vector2(24f, 0f);
            DOTween.Sequence()
                .SetTarget(row.Root)
                .SetUpdate(true)
                .Append(DOTween.To(() => height, value =>
                {
                    height = value;
                    row.Root.style.height = value;
                }, RowHeight, 0.26f).SetEase(Ease.OutBack))
                .Join(DOTween.To(() => opacity, value =>
                {
                    opacity = value;
                    row.Root.style.opacity = value;
                }, 1f, 0.16f).SetEase(Ease.OutQuad))
                .Join(DOTween.To(() => position, value =>
                {
                    position = value;
                    row.Root.style.translate = new Translate(value.x, value.y, 0f);
                }, Vector2.zero, 0.24f).SetEase(Ease.OutBack))
                .OnComplete(() =>
                {
                    row.Root.style.minHeight = RowHeight;
                    row.Root.style.height = RowHeight;
                    row.Root.style.overflow = Overflow.Visible;
                });
        }

        private void FinishRemovingRow(string missionId, MissionRow row)
        {
            if (!_rows.TryGetValue(missionId, out MissionRow current) || current != row)
            {
                return;
            }

            row.Root.RemoveFromHierarchy();
            _rows.Remove(missionId);

            MissionRow last = null;
            foreach (MissionRow remaining in _rows.Values)
            {
                remaining.Root.RemoveFromClassList("mission-row-last");
                last = remaining;
            }
            last?.Root.AddToClassList("mission-row-last");

            RefreshOverallProgress();
            if (_rows.Count == 0)
            {
                FadeOutEmptyCard();
            }
        }

        private void FadeOutEmptyCard()
        {
            DOTween.Kill(_card);
            float opacity = _card.resolvedStyle.opacity;
            Vector2 position = _card.resolvedStyle.translate;
            DOTween.Sequence()
                .SetTarget(_card)
                .SetUpdate(true)
                .Append(DOTween.To(() => opacity, value =>
                {
                    opacity = value;
                    _card.style.opacity = value;
                }, 0f, 0.18f).SetEase(Ease.InQuad))
                .Join(DOTween.To(() => position, value =>
                {
                    position = value;
                    _card.style.translate = new Translate(value.x, value.y, 0f);
                }, new Vector2(34f, 0f), 0.20f).SetEase(Ease.InQuad))
                .OnComplete(() => SetVisible(false));
        }

        private void PlayProgressPunch(MissionRow row, bool completedNow)
        {
            DOTween.Kill(row.Progress);
            DOTween.Kill(row.Root);

            row.Progress.style.scale = new Scale(Vector2.one);
            Sequence sequence = DOTween.Sequence()
                .SetTarget(row.Progress)
                .SetUpdate(true)
                .Append(ScaleTo(row.Progress, completedNow ? 1.16f : 1.10f, 0.10f).SetEase(Ease.OutQuad))
                .Append(ScaleTo(row.Progress, 1f, 0.13f).SetEase(Ease.OutBack));

            if (completedNow)
            {
                row.Root.style.scale = new Scale(Vector2.one);
                sequence.Join(ScaleTo(row.Root, 1.025f, 0.10f).SetEase(Ease.OutQuad));
                sequence.Append(ScaleTo(row.Root, 1f, 0.13f).SetEase(Ease.OutBack));
            }
        }

        private void ClearRows()
        {
            foreach (MissionRow row in _rows.Values)
            {
                DOTween.Kill(row.Root);
                DOTween.Kill(row.Progress);
            }

            _rows.Clear();
            _missionList?.Clear();
            if (_overallProgress != null)
            {
                _overallProgress.text = "0 / 0";
            }
        }

        private void CaptureFeelScaleBase()
        {
            if (_feelScaleBaseCaptured || _feelScaleDriver == null)
            {
                return;
            }

            _feelScaleBase = _feelScaleDriver.localScale;
            _feelScaleBaseCaptured = true;
        }

        private void ResetTransientVisualState()
        {
            CaptureFeelScaleBase();
            if (_feelScaleDriver != null)
            {
                _feelScaleDriver.localScale = _feelScaleBase;
            }

            if (_card != null)
            {
                _card.style.scale = new Scale(Vector2.one);
            }

            foreach (MissionRow row in _rows.Values)
            {
                row.Root.style.scale = new Scale(Vector2.one);
                row.Progress.style.scale = new Scale(Vector2.one);
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
