using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 월드 이동 결과를 Synty Animation Base Locomotion Male 컨트롤러 파라미터로 옮기는
    /// 공통 NPC 표현 컴포넌트. 이동 권위나 목적지 판단은 소유하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcMaleLocomotionAnimator : MonoBehaviour
    {
        [Tooltip("비우면 자식에서 찾는다.")]
        [SerializeField] private Animator _animator;
        [Tooltip("실제 이동하는 루트. 비우면 이 컴포넌트의 Transform을 쓴다.")]
        [SerializeField] private Transform _motionRoot;
        [SerializeField, Min(0f)] private float _walkSpeedMps = 1.4f;
        [SerializeField, Min(0f)] private float _runSpeedMps = 2.5f;
        [SerializeField, Min(0f)] private float _sprintSpeedMps = 7f;
        [SerializeField, Min(0f)] private float _movingThresholdMps = 0.05f;
        [SerializeField, Min(0f)] private float _speedDampSeconds = 0.1f;
        [SerializeField, Min(0f)] private float _startHoldSeconds = 0.2f;

        private static readonly int MovementInputTapped = Animator.StringToHash("MovementInputTapped");
        private static readonly int MovementInputPressed = Animator.StringToHash("MovementInputPressed");
        private static readonly int MovementInputHeld = Animator.StringToHash("MovementInputHeld");
        private static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int CurrentGait = Animator.StringToHash("CurrentGait");
        private static readonly int LocomotionStartDirection = Animator.StringToHash("LocomotionStartDirection");
        private static readonly int IsStopped = Animator.StringToHash("IsStopped");
        private static readonly int IsTurningInPlace = Animator.StringToHash("IsTurningInPlace");
        private static readonly int IsStarting = Animator.StringToHash("IsStarting");
        private static readonly int IsWalking = Animator.StringToHash("IsWalking");
        private static readonly int IsCrouching = Animator.StringToHash("IsCrouching");
        private static readonly int IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int IsGrounded = Animator.StringToHash("IsGrounded");

        private Vector3 _lastPosition;
        private bool _hasPositionSample;
        private bool _wasMoving;
        private bool _grounded = true;
        private float _startRemainingSeconds;
        private float _positionSampleSeconds;
        private float _stationarySeconds;

        public float CurrentSpeedMps { get; private set; }
        public bool IsMoving { get; private set; }

        private void Awake()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>();
            if (_motionRoot == null) _motionRoot = transform;
        }

        private void OnEnable()
        {
            ResetPositionSample();
            if (_animator == null) return;

            _animator.applyRootMotion = false;
            _animator.SetBool(IsGrounded, true);
            _animator.SetBool(IsStopped, true);
        }

        private void LateUpdate()
        {
            if (_animator == null || _motionRoot == null) return;

            Vector3 position = _motionRoot.position;
            if (!_hasPositionSample || Time.deltaTime <= 0f)
            {
                _lastPosition = position;
                _hasPositionSample = true;
                return;
            }

            _positionSampleSeconds += Time.deltaTime;
            Vector3 displacement = position - _lastPosition;
            displacement.y = 0f;
            if (displacement.sqrMagnitude > 0.000001f)
            {
                CurrentSpeedMps = displacement.magnitude / _positionSampleSeconds;
                IsMoving = CurrentSpeedMps >= _movingThresholdMps;
                _lastPosition = position;
                _positionSampleSeconds = 0f;
                _stationarySeconds = 0f;
            }
            else
            {
                _stationarySeconds += Time.deltaTime;
                if (_stationarySeconds >= Mathf.Max(Time.fixedDeltaTime * 2f, 0.05f))
                {
                    CurrentSpeedMps = 0f;
                    IsMoving = false;
                }
            }

            bool justStarted = IsMoving && !_wasMoving;
            if (justStarted)
            {
                _startRemainingSeconds = _startHoldSeconds;
                if (displacement.sqrMagnitude > 0f)
                {
                    float direction = Vector3.SignedAngle(_motionRoot.forward, displacement, Vector3.up);
                    _animator.SetFloat(LocomotionStartDirection, direction);
                }
            }

            if (_startRemainingSeconds > 0f) _startRemainingSeconds -= Time.deltaTime;

            int gait = ResolveGait(CurrentSpeedMps);
            _animator.SetFloat(MoveSpeed, CurrentSpeedMps, _speedDampSeconds, Time.deltaTime);
            _animator.SetInteger(CurrentGait, gait);
            _animator.SetBool(MovementInputTapped, justStarted);
            _animator.SetBool(MovementInputPressed, IsMoving && !justStarted && _startRemainingSeconds > 0f);
            _animator.SetBool(MovementInputHeld, IsMoving && _startRemainingSeconds <= 0f);
            _animator.SetBool(IsStopped, !IsMoving);
            _animator.SetBool(IsStarting, IsMoving && _startRemainingSeconds > 0f);
            _animator.SetBool(IsWalking, gait == 1);
            _animator.SetBool(IsTurningInPlace, false);
            _animator.SetBool(IsCrouching, false);
            _animator.SetBool(IsJumping, false);
            _animator.SetBool(IsGrounded, _grounded);

            _wasMoving = IsMoving;
        }

        /// <summary>구체 NPC의 물리 상태를 Synty Fall 전환에 연결한다.</summary>
        public void SetGrounded(bool grounded)
        {
            _grounded = grounded;
        }

        private int ResolveGait(float speedMps)
        {
            if (speedMps < _movingThresholdMps) return 0;
            if (speedMps < (_walkSpeedMps + _runSpeedMps) * 0.5f) return 1;
            if (speedMps < (_runSpeedMps + _sprintSpeedMps) * 0.5f) return 2;
            return 3;
        }

        private void ResetPositionSample()
        {
            _hasPositionSample = false;
            _wasMoving = false;
            CurrentSpeedMps = 0f;
            IsMoving = false;
            _startRemainingSeconds = 0f;
            _positionSampleSeconds = 0f;
            _stationarySeconds = 0f;
        }
    }
}
