using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    public sealed class MissionHUDPreviewDriver : MonoBehaviour
    {
        [SerializeField] private MissionHUDController _missionHud;
        [SerializeField] private bool _autoReceiveAfterDelay;
        [SerializeField, Min(0f)] private float _autoReceiveDelay = 1.2f;

        private readonly MissionHUDItem[] _rabbitTrapMissions =
        {
            new MissionHUDItem("receive-carrot-trap", "당근 트랩 받기", 0, 1),
            new MissionHUDItem("place-trap-near-rabbit", "토끼 근처에 트랩 설치하기", 0, 1),
            new MissionHUDItem("turn-rabbit-into-gift", "토끼를 선물 상자로 만들기", 0, 1)
        };

        private int _previewStep;

        public void Configure(MissionHUDController missionHud, bool autoReceiveAfterDelay = false)
        {
            _missionHud = missionHud;
            _autoReceiveAfterDelay = autoReceiveAfterDelay;
        }

        private void Start()
        {
            if (_missionHud == null)
            {
                _missionHud = FindAnyObjectByType<MissionHUDController>();
            }

            ResetPreview();
            if (_autoReceiveAfterDelay)
            {
                Invoke(nameof(ReceiveRabbitEvent), _autoReceiveDelay);
            }
        }

        private void OnDisable()
        {
            CancelInvoke();
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (_missionHud == null || keyboard == null)
            {
                return;
            }

            if (keyboard.eKey.wasPressedThisFrame)
            {
                ReceiveRabbitEvent();
            }

            if (keyboard.spaceKey.wasPressedThisFrame)
            {
                AdvancePreview();
            }

            if (keyboard.rKey.wasPressedThisFrame)
            {
                ResetPreview();
            }
        }

        private void AdvancePreview()
        {
            bool advanced;
            switch (_previewStep)
            {
                case 0:
                    advanced = _missionHud.CompleteAndRemoveMission("receive-carrot-trap");
                    break;
                case 1:
                    advanced = _missionHud.CompleteAndRemoveMission("place-trap-near-rabbit");
                    break;
                case 2:
                    advanced = _missionHud.CompleteAndRemoveMission("turn-rabbit-into-gift");
                    break;
                default:
                    ReceiveRabbitEvent();
                    return;
            }

            if (advanced)
            {
                _previewStep++;
            }
        }

        private void ResetPreview()
        {
            _previewStep = 0;
            _missionHud?.ClearMissions();
        }

        private void ReceiveRabbitEvent()
        {
            CancelInvoke();
            _previewStep = 0;
            _missionHud?.ReceiveMissions(_rabbitTrapMissions);
        }
    }
}
