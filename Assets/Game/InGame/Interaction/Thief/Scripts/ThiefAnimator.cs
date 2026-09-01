using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>Synty 샘플 컨트롤러의 입력 계약을 권위 도둑 상태로 변환한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefAnimator : MonoBehaviour
    {
        private static readonly int MovementInputTapped = Animator.StringToHash("MovementInputTapped");
        private static readonly int MovementInputPressed = Animator.StringToHash("MovementInputPressed");
        private static readonly int MovementInputHeld = Animator.StringToHash("MovementInputHeld");
        private static readonly int MoveSpeed = Animator.StringToHash("MoveSpeed");
        private static readonly int CurrentGait = Animator.StringToHash("CurrentGait");
        private static readonly int InclineAngle = Animator.StringToHash("InclineAngle");
        private static readonly int LocomotionStartDirection = Animator.StringToHash("LocomotionStartDirection");
        private static readonly int StrafeDirectionX = Animator.StringToHash("StrafeDirectionX");
        private static readonly int StrafeDirectionZ = Animator.StringToHash("StrafeDirectionZ");
        private static readonly int ForwardStrafe = Animator.StringToHash("ForwardStrafe");
        private static readonly int IsStrafing = Animator.StringToHash("IsStrafing");
        private static readonly int IsStopped = Animator.StringToHash("IsStopped");
        private static readonly int IsTurningInPlace = Animator.StringToHash("IsTurningInPlace");
        private static readonly int IsStarting = Animator.StringToHash("IsStarting");
        private static readonly int IsWalking = Animator.StringToHash("IsWalking");
        private static readonly int IsCrouching = Animator.StringToHash("IsCrouching");
        private static readonly int IsJumping = Animator.StringToHash("IsJumping");
        private static readonly int IsGrounded = Animator.StringToHash("IsGrounded");
        private static readonly int LeanValue = Animator.StringToHash("LeanValue");
        private static readonly int HeadLookX = Animator.StringToHash("HeadLookX");
        private static readonly int HeadLookY = Animator.StringToHash("HeadLookY");
        private static readonly int BodyLookX = Animator.StringToHash("BodyLookX");
        private static readonly int BodyLookY = Animator.StringToHash("BodyLookY");
        private static readonly int LiftPhase = Animator.StringToHash("LiftPhase");
        private static readonly int HasCargo = Animator.StringToHash("HasCargo");
        private static readonly int ImpactPhase = Animator.StringToHash("ImpactPhase");

        [SerializeField] private Animator _animator;
        [SerializeField] private ThiefNetworkHub _networkHub;
        [SerializeField] private ThiefActor _actor;
        [SerializeField, Min(0.01f)] private float _speedDampingSeconds = 0.12f;
        [SerializeField, Min(0.01f)] private float _inputPressedSeconds = 0.15f;

        private readonly HashSet<int> _parameterHashes = new HashSet<int>();
        private float _moveInputElapsed;
        private bool _wasMoving;
        private Vector3 _previousForward;

        private void Awake()
        {
            if (_animator == null) _animator = GetComponentInChildren<Animator>(true);
            if (_networkHub == null) _networkHub = GetComponent<ThiefNetworkHub>();
            if (_actor == null) _actor = GetComponent<ThiefActor>();
            if (_animator == null) return;
            _animator.applyRootMotion = false;
            CacheParameters();
            _previousForward = transform.forward;
        }

        private void Update()
        {
            if (_animator == null || _animator.runtimeAnimatorController == null) return;

            EThiefAction action = _networkHub != null
                ? _networkHub.PresentedAction : _actor != null ? _actor.CurrentAction : EThiefAction.Waiting;
            EThiefGait gait = _networkHub != null
                ? _networkHub.PresentedGait : _actor != null ? _actor.CurrentGait : EThiefGait.Idle;
            bool hasCargo = _networkHub != null
                ? _networkHub.PresentedHasCargo : _actor != null && _actor.HasCargo;
            EThiefLiftPhase liftPhase = _networkHub != null
                ? _networkHub.PresentedLiftPhase : _actor != null ? _actor.LiftPhase : EThiefLiftPhase.None;
            EThiefImpactPhase impactPhase = _networkHub != null
                ? _networkHub.PresentedImpactPhase : _actor != null
                    ? _actor.ImpactPhase : EThiefImpactPhase.None;

            bool lifting = liftPhase is > EThiefLiftPhase.None and < EThiefLiftPhase.Carrying;
            bool impactReacting = impactPhase != EThiefImpactPhase.None;
            bool moving = !lifting && !impactReacting && gait != EThiefGait.Idle;
            bool crouching = gait == EThiefGait.Crouch ||
                              liftPhase is >= EThiefLiftPhase.PrepareCrouch and <= EThiefLiftPhase.LiftToChest;
            float targetSpeed = gait switch
            {
                EThiefGait.Run => 2.5f,
                EThiefGait.Crouch => 1.2f,
                EThiefGait.Walk => 2.5f,
                _ => 0f,
            };
            if (lifting || impactReacting) targetSpeed = 0f;

            UpdateMovementInput(moving);
            SetFloat(MoveSpeed, targetSpeed, _speedDampingSeconds);
            SetInteger(CurrentGait, gait == EThiefGait.Crouch ? 1 : moving ? 2 : 0);
            SetBool(IsStopped, !moving);
            SetBool(IsWalking, gait == EThiefGait.Crouch);
            SetBool(IsCrouching, crouching);
            SetBool(IsGrounded, true);
            SetBool(IsJumping, false);
            SetBool(IsTurningInPlace, false);
            SetBool(IsStarting, moving && _moveInputElapsed < _inputPressedSeconds);
            SetFloat(IsStrafing, 0f);
            SetFloat(StrafeDirectionX, 0f);
            SetFloat(StrafeDirectionZ, 1f);
            SetFloat(ForwardStrafe, 1f);
            SetFloat(InclineAngle, 0f);
            SetFloat(HeadLookX, 0f);
            SetFloat(HeadLookY, 0f);
            SetFloat(BodyLookX, 0f);
            SetFloat(BodyLookY, 0f);
            SetInteger(LiftPhase, (int)liftPhase);
            SetBool(HasCargo, hasCargo);
            SetInteger(ImpactPhase, (int)impactPhase);

            float turn = Vector3.SignedAngle(_previousForward, transform.forward, Vector3.up);
            float lean = lifting || impactReacting || Time.deltaTime <= 0f
                ? 0f : Mathf.Clamp(turn / (180f * Time.deltaTime), -1f, 1f);
            SetFloat(LeanValue, lean, 0.1f);
            _previousForward = transform.forward;

            if (action == EThiefAction.Waiting && liftPhase == EThiefLiftPhase.None)
                SetBool(IsStopped, true);
        }

        private void UpdateMovementInput(bool moving)
        {
            bool tapped = moving && !_wasMoving;
            if (tapped) _moveInputElapsed = 0f;
            else if (moving) _moveInputElapsed += Time.deltaTime;
            else _moveInputElapsed = 0f;

            bool pressed = moving && !tapped && _moveInputElapsed < _inputPressedSeconds;
            bool held = moving && _moveInputElapsed >= _inputPressedSeconds;
            SetBool(MovementInputTapped, tapped);
            SetBool(MovementInputPressed, pressed);
            SetBool(MovementInputHeld, held);
            if (tapped) SetFloat(LocomotionStartDirection, 0f);
            _wasMoving = moving;
        }

        private void CacheParameters()
        {
            _parameterHashes.Clear();
            foreach (AnimatorControllerParameter parameter in _animator.parameters)
                _parameterHashes.Add(parameter.nameHash);
        }

        private void SetBool(int hash, bool value)
        {
            if (_parameterHashes.Contains(hash)) _animator.SetBool(hash, value);
        }

        private void SetInteger(int hash, int value)
        {
            if (_parameterHashes.Contains(hash)) _animator.SetInteger(hash, value);
        }

        private void SetFloat(int hash, float value)
        {
            if (_parameterHashes.Contains(hash)) _animator.SetFloat(hash, value);
        }

        private void SetFloat(int hash, float value, float dampTime)
        {
            if (_parameterHashes.Contains(hash))
                _animator.SetFloat(hash, value, dampTime, Time.deltaTime);
        }
    }
}
