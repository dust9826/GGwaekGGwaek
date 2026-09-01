using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 청소 모드의 탱크 조작. W/S 가 전후진, A/D 가 제자리 회전이다.
    ///
    /// 평상시 로코모션(<c>PlayerAnimationController</c>)은 카메라 기준으로 움직이지만 청소 중에는
    /// 시점이 탑다운이라 카메라 기준이 성립하지 않는다. 그래서 캐릭터 자기 정면을 기준으로 민다.
    /// </summary>
    [RequireComponent(typeof(CharacterController))]
    public sealed class MopLocomotion : MonoBehaviour
    {
        [SerializeField] private MopMode _mode;

        [Tooltip("전진 속도. m/s.")]
        [SerializeField, Min(0f)] private float _moveSpeed = 2.2f;
        [Tooltip("제자리 회전 속도. 도/초. 140 은 한 바퀴가 2.6초라 청소 도구치고 빨랐다.")]
        [SerializeField, Min(0f)] private float _turnSpeed = 90f;
        [Tooltip("접지 유지용 하향 가속. 없으면 경사면을 내려갈 때 뜬다.")]
        [SerializeField] private float _gravity = -20f;

        private CharacterController _controller;
        private float _verticalVelocity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
        }

        private void OnEnable()
        {
            // 모드가 바뀌는 순간 지난 프레임의 낙하 속도가 남아 있으면 한 번 튄다.
            _verticalVelocity = 0f;
        }

        private void Update()
        {
            if (_mode == null || _mode.MoveAction == null) return;

            Vector2 move = _mode.MoveAction.ReadValue<Vector2>();

            transform.Rotate(Vector3.up, move.x * _turnSpeed * Time.deltaTime, Space.World);

            _verticalVelocity = _controller.isGrounded
                ? -1f                                     // 접지 시 살짝 눌러 붙인다
                : _verticalVelocity + _gravity * Time.deltaTime;

            Vector3 velocity = transform.forward * (move.y * _moveSpeed);
            velocity.y = _verticalVelocity;

            _controller.Move(velocity * Time.deltaTime);
        }
    }
}
