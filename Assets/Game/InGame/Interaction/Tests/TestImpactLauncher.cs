using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 검증 씬 전용 — Play 모드에서 손으로 공을 밀 방법이 없어서 만든 헬퍼. 프로덕션 코드가 아니다.
    ///
    /// <para><b>1</b> = 약한 충격(래치를 못 이겨야 정상 — 문은 안 열리고 덜컹, 상자는 안 부서짐).
    /// <b>2</b> = 강한 충격(열리거나 부서져야 정상). <b>R</b> = 시작 위치로 되돌린다.</para>
    /// </summary>
    public sealed class TestImpactLauncher : MonoBehaviour
    {
        [SerializeField] private Rigidbody _target;
        [SerializeField] private Vector3 _weakVelocity = new Vector3(0f, 0f, 1f);
        [SerializeField] private Vector3 _strongVelocity = new Vector3(0f, 0f, 6f);

        private Vector3 _startPosition;

        private void Awake()
        {
            if (_target != null) _startPosition = _target.position;
        }

        private void Update()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null || _target == null) return;

            if (keyboard.digit1Key.wasPressedThisFrame) Launch(_weakVelocity);
            else if (keyboard.digit2Key.wasPressedThisFrame) Launch(_strongVelocity);
            else if (keyboard.rKey.wasPressedThisFrame) ResetTarget();
        }

        private void Launch(Vector3 velocity)
        {
            _target.linearVelocity = velocity;
            _target.angularVelocity = Vector3.zero;
        }

        private void ResetTarget()
        {
            _target.position = _startPosition;
            _target.linearVelocity = Vector3.zero;
            _target.angularVelocity = Vector3.zero;
        }
    }
}
