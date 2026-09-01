using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UIElements;

namespace PPack
{
    [RequireComponent(typeof(UIDocument))]
    public sealed class CreativeCharacterCustomizationController : MonoBehaviour
    {
        private const string PreferencePrefix = "PPack.CreativeCharacter.";

        [Serializable]
        private sealed class MeshSlot
        {
            [SerializeField] private string _id;
            [SerializeField] private string _displayName;
            [SerializeField] private SkinnedMeshRenderer _renderer;
            [SerializeField] private Mesh[] _variants;
            [SerializeField] private bool _allowNone;
            [SerializeField] private int _defaultIndex;

            private int _index;

            public string Id => _id;
            public string DisplayName => _displayName;
            public int SelectionCount => _variants.Length + (_allowNone ? 1 : 0);

            public void Load()
            {
                int fallback = Mathf.Clamp(_defaultIndex, 0, Mathf.Max(0, SelectionCount - 1));
                _index = Mathf.Clamp(PlayerPrefs.GetInt(PreferencePrefix + _id, fallback), 0,
                                     Mathf.Max(0, SelectionCount - 1));
                Apply();
            }

            public void Cycle(int direction)
            {
                if (SelectionCount == 0) return;

                _index = (_index + direction + SelectionCount) % SelectionCount;
                Apply();
            }

            public void Randomize()
            {
                if (SelectionCount == 0) return;

                _index = UnityEngine.Random.Range(0, SelectionCount);
                Apply();
            }

            public void Save()
            {
                PlayerPrefs.SetInt(PreferencePrefix + _id, _index);
            }

            public string GetValueText()
            {
                if (_allowNone && _index == 0) return "NONE";

                int visualIndex = _allowNone ? _index : _index + 1;
                int visualCount = _variants.Length;
                return $"{visualIndex:00} / {visualCount:00}";
            }

            private void Apply()
            {
                if (_renderer == null) return;

                int meshIndex = _allowNone ? _index - 1 : _index;
                bool hasMesh = meshIndex >= 0 && meshIndex < _variants.Length && _variants[meshIndex] != null;
                _renderer.enabled = hasMesh;

                if (!hasMesh) return;

                Mesh mesh = _variants[meshIndex];
                _renderer.sharedMesh = mesh;
                _renderer.localBounds = mesh.bounds;
            }
        }

        [SerializeField] private UIDocument _document;
        [SerializeField] private Transform _characterRoot;
        [SerializeField] private List<MeshSlot> _slots = new();
        [SerializeField] private string _nextSceneName = "MainMenu";
        [SerializeField] private float _turnStep = 30f;
        [SerializeField] private float _dragSensitivity = 0.35f;

        private readonly List<Action> _unbindActions = new();
        private Label _statusLabel;
        private bool _isDragging;
        private int _pointerId;
        private Vector2 _lastPointerPosition;

        private void OnEnable()
        {
            if (_document == null) _document = GetComponent<UIDocument>();
            VisualElement root = _document.rootVisualElement;
            _statusLabel = root.Q<Label>("customization-status");

            foreach (MeshSlot slot in _slots)
            {
                slot.Load();
                BindSlot(root, slot);
                RefreshSlotLabel(root, slot);
            }

            BindButton(root, "action-randomize", Randomize);
            BindButton(root, "action-confirm-character", Confirm);
            BindButton(root, "action-skip-character", ContinueToMainMenu);
            BindButton(root, "action-turn-left", () => TurnCharacter(-_turnStep));
            BindButton(root, "action-turn-right", () => TurnCharacter(_turnStep));
            BindDragZone(root.Q<VisualElement>("preview-drag-zone"));

            SetStatus("DRAG TO TURN  ·  CHOOSE YOUR WINTER CREW LOOK");
        }

        private void OnDisable()
        {
            foreach (Action unbind in _unbindActions) unbind();
            _unbindActions.Clear();
            _isDragging = false;
        }

        private void BindSlot(VisualElement root, MeshSlot slot)
        {
            BindButton(root, $"previous-{slot.Id}", () =>
            {
                slot.Cycle(-1);
                RefreshSlotLabel(root, slot);
                SetStatus($"{slot.DisplayName.ToUpperInvariant()} UPDATED");
            });

            BindButton(root, $"next-{slot.Id}", () =>
            {
                slot.Cycle(1);
                RefreshSlotLabel(root, slot);
                SetStatus($"{slot.DisplayName.ToUpperInvariant()} UPDATED");
            });
        }

        private void BindButton(VisualElement root, string name, Action action)
        {
            Button button = root.Q<Button>(name);
            if (button == null)
            {
                Debug.LogWarning($"Character customization button is missing: {name}", this);
                return;
            }

            button.clicked += action;
            _unbindActions.Add(() => button.clicked -= action);
        }

        private void BindDragZone(VisualElement dragZone)
        {
            if (dragZone == null) return;

            EventCallback<PointerDownEvent> down = evt =>
            {
                if (_characterRoot == null || evt.button != 0) return;

                _isDragging = true;
                _pointerId = evt.pointerId;
                _lastPointerPosition = evt.position;
                dragZone.CapturePointer(_pointerId);
            };

            EventCallback<PointerMoveEvent> move = evt =>
            {
                if (!_isDragging || evt.pointerId != _pointerId || _characterRoot == null) return;

                float delta = evt.position.x - _lastPointerPosition.x;
                _lastPointerPosition = evt.position;
                TurnCharacter(-delta * _dragSensitivity);
            };

            EventCallback<PointerUpEvent> up = evt => EndDrag(dragZone, evt.pointerId);
            EventCallback<PointerCaptureOutEvent> captureOut = _ => _isDragging = false;

            dragZone.RegisterCallback(down);
            dragZone.RegisterCallback(move);
            dragZone.RegisterCallback(up);
            dragZone.RegisterCallback(captureOut);
            _unbindActions.Add(() => dragZone.UnregisterCallback(down));
            _unbindActions.Add(() => dragZone.UnregisterCallback(move));
            _unbindActions.Add(() => dragZone.UnregisterCallback(up));
            _unbindActions.Add(() => dragZone.UnregisterCallback(captureOut));
        }

        private void EndDrag(VisualElement dragZone, int pointerId)
        {
            if (!_isDragging || pointerId != _pointerId) return;

            _isDragging = false;
            if (dragZone.HasPointerCapture(pointerId)) dragZone.ReleasePointer(pointerId);
        }

        private void Randomize()
        {
            foreach (MeshSlot slot in _slots)
            {
                slot.Randomize();
                RefreshSlotLabel(_document.rootVisualElement, slot);
            }

            SetStatus("A NEW WINTER CREW LOOK IS READY");
        }

        private void Confirm()
        {
            foreach (MeshSlot slot in _slots) slot.Save();
            PlayerPrefs.SetInt(PreferencePrefix + "HasSelection", 1);
            PlayerPrefs.Save();

            SetStatus("CREW LOOK SAVED");
            ContinueToMainMenu();
        }

        private void ContinueToMainMenu()
        {
            if (!Application.CanStreamedLevelBeLoaded(_nextSceneName))
            {
                SetStatus("SAVED  ·  MAIN MENU IS NOT IN BUILD SETTINGS");
                return;
            }

            SceneManager.LoadScene(_nextSceneName);
        }

        private void TurnCharacter(float degrees)
        {
            if (_characterRoot == null) return;

            _characterRoot.Rotate(0f, degrees, 0f, Space.World);
        }

        private static void RefreshSlotLabel(VisualElement root, MeshSlot slot)
        {
            Label value = root.Q<Label>($"value-{slot.Id}");
            if (value != null) value.text = slot.GetValueText();
        }

        private void SetStatus(string message)
        {
            if (_statusLabel != null) _statusLabel.text = message;
        }
    }
}
