using System.Collections;

using UnityEngine.InputSystem;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class BlizzardEventTestController : MonoBehaviour
    {
        [SerializeField] private BlizzardEvent _event;
        [SerializeField] private SnowStage _snowStage;
        
        [SerializeField] private bool _clearSnowOnStart = true;
[SerializeField] private bool _triggerOnStart = true;

        private GUIStyle _titleStyle;
        private GUIStyle _phaseStyle;
        private GUIStyle _bodyStyle;
        private string _statusMessage = "Ready";

private IEnumerator Start()
        {
            ResolveReferences();
            yield return null;

            if (_clearSnowOnStart && _snowStage != null && _snowStage.Field != null)
            {
                _snowStage.Field.FillAll(0);
            }

            if (_triggerOnStart)
            {
                RestartEvent();
            }
        }

private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                TriggerEvent();
            }
            else if (keyboard.rKey.wasPressedThisFrame)
            {
                RestartEvent();
            }
            else if (keyboard.sKey.wasPressedThisFrame)
            {
                StopEvent();
            }
        }

        private void OnGUI()
        {
            ResolveReferences();
            EnsureStyles();

            const float width = 390f;
            Rect panel = new Rect(24f, 24f, width, 274f);
            GUI.Box(panel, GUIContent.none);

            GUI.Label(new Rect(44f, 40f, width - 40f, 34f), "BLIZZARD EVENT TEST", _titleStyle);

            EBlizzardEventPhase phase = _event != null ? _event.Phase : EBlizzardEventPhase.Idle;
            float intensity = _event != null ? _event.Intensity : 0f;
            float progress = _event != null ? _event.PhaseProgress : 0f;
            GUI.Label(new Rect(44f, 78f, width - 40f, 30f), $"PHASE  {phase}", _phaseStyle);

            int depth = 0;
            if (_event != null && _snowStage != null)
            {
                depth = _snowStage.DepthCmAtWorld(_event.EventCenter);
            }

            GUI.Label(
                new Rect(44f, 112f, width - 40f, 78f),
                $"Intensity      {intensity:0.00}\nPhase progress {progress * 100f:0}%\nSnow depth     {depth} cm\n{_statusMessage}",
                _bodyStyle);

            if (GUI.Button(new Rect(44f, 208f, 104f, 42f), "TRIGGER"))
            {
                TriggerEvent();
            }
            if (GUI.Button(new Rect(158f, 208f, 104f, 42f), "RESTART"))
            {
                RestartEvent();
            }
            if (GUI.Button(new Rect(272f, 208f, 104f, 42f), "STOP"))
            {
                StopEvent();
            }

            GUI.Label(
                new Rect(44f, 254f, width - 40f, 28f),
                "SPACE Trigger   R Restart   S Stop",
                _bodyStyle);
        }

        private void TriggerEvent()
        {
            ResolveReferences();
            if (_event == null)
            {
                _statusMessage = "BlizzardEvent is missing";
                return;
            }

            _statusMessage = _event.Trigger()
                ? "Manual trigger accepted"
                : $"Already running: {_event.Phase}";
        }

        private void RestartEvent()
        {
            ResolveReferences();
            if (_event == null)
            {
                _statusMessage = "BlizzardEvent is missing";
                return;
            }

            _event.Stop();
            _event.Trigger();
            _statusMessage = "Cycle restarted";
        }

        private void StopEvent()
        {
            ResolveReferences();
            if (_event == null)
            {
                _statusMessage = "BlizzardEvent is missing";
                return;
            }

            _event.Stop();
            _statusMessage = "Stopped";
        }

        private void ResolveReferences()
        {
            if (_event == null) _event = FindAnyObjectByType<BlizzardEvent>();
            if (_snowStage == null) _snowStage = FindAnyObjectByType<SnowStage>();
        }

        private void EnsureStyles()
        {
            if (_titleStyle != null)
            {
                return;
            }

            _titleStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 24,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _phaseStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 19,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(0.49f, 0.88f, 1f) }
            };
            _bodyStyle = new GUIStyle(GUI.skin.label)
            {
                fontSize = 15,
                normal = { textColor = new Color(0.9f, 0.96f, 1f) }
            };
        }
    }
}
