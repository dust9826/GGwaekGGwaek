using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 세 동사 중 <b>사람이 누르는 두 개</b>를 <see cref="SnowPlowBlade"/> 에 넣는다.
    /// 세 번째(전진/후진)는 키가 없다 — 날은 <b>실제 전진 속도</b>로 붙었는지를 판정하므로
    /// 후진은 이미 주행 입력이고, 그것에 별 키를 주면 두 개의 진실이 생긴다.
    ///
    /// <b>권위가 아니다.</b> 블레이드는 입력을 모르고 <c>SetBladeDown</c>·<c>SetAngle</c> 만 받는다.
    /// 그래서 데디 서버에서 이 컴포넌트가 아예 없어도 시뮬레이션이 돈다 — 루트 <c>AGENTS.md</c> 의
    /// "로컬 플레이어·입력 장치·카메라·UI 가 있다고 가정하지 마라"가 코드 모양으로 지켜진 자리다.
    ///
    /// <c>VehicleInput</c> 을 쓰지 않고 <see cref="Keyboard"/> 를 직접 읽는다.
    /// 동사 셋을 <c>VehicleControls.inputactions</c> 에 넣으려면 그 자산을 편집해야 하고,
    /// <c>.inputactions</c> 는 GUID 를 가진 자산이라 이 작업의 범위 밖이다. 액션이 생기면
    /// 여기만 갈아끼운다 — 블레이드는 바뀌지 않는다.
    /// </summary>
    public sealed class SnowPlowInput : MonoBehaviour
    {
        [SerializeField] private SnowPlowBlade _blade;

        [Header("키")]
        [Tooltip("날 내림/올림 토글. v7 과 같은 E 다.")]
        [SerializeField] private Key _bladeToggleKey = Key.E;
        [Tooltip("날을 왼쪽으로.")]
        [SerializeField] private Key _angleLeftKey = Key.Digit1;
        [Tooltip("날을 정면으로.")]
        [SerializeField] private Key _angleStraightKey = Key.Digit2;
        [Tooltip("날을 오른쪽으로.")]
        [SerializeField] private Key _angleRightKey = Key.Digit3;

        private void Awake()
        {
            if (_blade == null) _blade = GetComponentInParent<SnowPlowBlade>();

            // 배치 모드에는 키보드가 없다. 여기서 스스로 꺼져야 매 프레임 null 검사를 하지 않는다.
            if (_blade == null || Application.isBatchMode) enabled = false;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null) return;

            if (keyboard[_bladeToggleKey].wasPressedThisFrame) _blade.ToggleBlade();

            if (keyboard[_angleLeftKey].wasPressedThisFrame) _blade.SetAngle(EBladeAngle.Left);
            else if (keyboard[_angleStraightKey].wasPressedThisFrame) _blade.SetAngle(EBladeAngle.Straight);
            else if (keyboard[_angleRightKey].wasPressedThisFrame) _blade.SetAngle(EBladeAngle.Right);
        }
    }
}
