using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 테스트 전용 — 획득 방식 4개(부채꼴 조준 회전·직사각형 평행이동·큰 부채꼴+클릭·기존 방식)를
    /// 숫자키 1~4로 런타임 전환한다(2026-08-15). `PullMode_Comparison_Test.unity`에서만 쓰고
    /// `PF_VehicleProto` 프리팹에는 적용하지 않는다 — 넷 중 하나가 채택되면 이 스크립트와 나머지
    /// 셋은 통째로 지운다.
    ///
    /// 전환은 컴포넌트 하나만 켜고 나머지는 끄는 방식이다. 각 모드의 <c>OnDisable</c>이 하이라이트·
    /// 포획 상태를 정리하므로 여기서 따로 정리하지 않는다.
    /// </summary>
    public sealed class VehiclePullModeSwitcher : MonoBehaviour
    {
        [SerializeField] private VehiclePull_ConeAim _mode1ConeAim;
        [SerializeField] private VehiclePull_RectangleStrafe _mode2RectangleStrafe;
        [SerializeField] private VehiclePull_ClickCone _mode3ClickCone;
        [SerializeField] private VehiclePullAbility _mode4Existing;

        private MonoBehaviour[] _modes;
        private int _activeIndex;

        private void Awake()
        {
            _modes = new MonoBehaviour[] { _mode1ConeAim, _mode2RectangleStrafe, _mode3ClickCone, _mode4Existing };
        }

        private void Start()
        {
            SetActive(_activeIndex);
        }

        private void Update()
        {
            var keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) SetActive(0);
            else if (keyboard.digit2Key.wasPressedThisFrame) SetActive(1);
            else if (keyboard.digit3Key.wasPressedThisFrame) SetActive(2);
            else if (keyboard.digit4Key.wasPressedThisFrame) SetActive(3);
        }

        private void SetActive(int index)
        {
            _activeIndex = index;
            for (int i = 0; i < _modes.Length; i++)
            {
                if (_modes[i] != null) _modes[i].enabled = i == index;
            }

            Debug.Log($"[VehiclePullModeSwitcher] mode {index + 1} ({_modes[index]?.GetType().Name}) active.");
        }
    }
}
