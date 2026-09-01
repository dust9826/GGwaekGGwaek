using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 슬라이드 발 밀기의 표현. 물리 위상은 <see cref="PenguinLocomotion"/>이 FixedUpdate에서
    /// 결정하며, 이 컴포넌트는 Animator가 포즈를 쓴 뒤 발가락 본에 회전 델타만 더한다.
    /// </summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(110)]
    public sealed class PenguinSlideKick : MonoBehaviour
    {
        [SerializeField] private PenguinLocomotion _locomotion;
        [SerializeField] private Transform _leftToe;
        [SerializeField] private Transform _rightToe;
        [SerializeField, Range(0f, 60f)] private float _kickAngleDeg = 30f;
        [SerializeField, Min(1f)] private float _returnDegPerSecond = 360f;

        private float _leftAngleDeg;
        private float _rightAngleDeg;

        private void Reset()
        {
            _locomotion = GetComponentInParent<PenguinLocomotion>();
        }

        private void Awake()
        {
            if (_locomotion == null) _locomotion = GetComponentInParent<PenguinLocomotion>();
            if (_leftToe == null) _leftToe = FindChild("DEF-toe.L");
            if (_rightToe == null) _rightToe = FindChild("DEF-toe.R");
        }

        private void LateUpdate()
        {
            int activeFoot = _locomotion != null ? _locomotion.ActiveSlideKickFoot : -1;
            float stroke01 = _locomotion != null ? _locomotion.SlideKickStroke01 : 0f;
            float strokeAngle = Mathf.Sin(Mathf.Clamp01(stroke01) * Mathf.PI) * _kickAngleDeg;

            float leftTarget = activeFoot == 0 ? strokeAngle : 0f;
            float rightTarget = activeFoot == 1 ? strokeAngle : 0f;
            float maxDelta = _returnDegPerSecond * Time.deltaTime;
            _leftAngleDeg = Mathf.MoveTowards(_leftAngleDeg, leftTarget, maxDelta);
            _rightAngleDeg = Mathf.MoveTowards(_rightAngleDeg, rightTarget, maxDelta);

            if (_leftToe != null)
                _leftToe.localRotation *= Quaternion.AngleAxis(_leftAngleDeg, Vector3.right);
            if (_rightToe != null)
                _rightToe.localRotation *= Quaternion.AngleAxis(_rightAngleDeg, Vector3.right);
        }

        private Transform FindChild(string childName)
        {
            foreach (Transform child in GetComponentsInChildren<Transform>(true))
                if (child.name == childName) return child;
            return null;
        }
    }
}
