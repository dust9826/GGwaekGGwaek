using Fusion;
using UnityEngine;
using UnityEngine.Serialization;

namespace PPack
{
    public enum EThiefAction
    {
        Waiting,
        ApproachingSite,
        ApproachingGift,
        LiftingGift,
        Escaping,
        ExitCountdown,
        ImpactReaction,
    }

    public enum EThiefTaskResult
    {
        Failure,
        Running,
        Success,
    }

    public enum EThiefLiftPhase
    {
        None,
        PrepareCrouch,
        ReachFloor,
        Grip,
        LiftToChest,
        StandAndOverhead,
        Carrying,
    }

    public enum EThiefImpactPhase
    {
        None,
        Falling,
        Down,
        GettingUp,
    }

    [DisallowMultipleComponent]
    [RequireComponent(typeof(ThiefMovement))]
    public sealed class ThiefActor : MonoBehaviour
    {
        private const float GrabPositionToleranceM = 0.08f;
        private const float GrabFacingToleranceDeg = 8f;
        private const float GrabTurnSpeedDegPerS = 360f;

        [SerializeField] private ThiefMovement _movement;
        [SerializeField] private CapsuleCollider _bodyCollider;
        [SerializeField] private Transform _carryAnchor;
        [SerializeField, Min(0f)] private float _grabSurfaceClearanceM = 0.25f;
        [SerializeField, Min(0f)] private float _prepareCrouchSeconds = 0.45f;
        [SerializeField, Min(0.05f)] private float _reachFloorSeconds = 0.55f;
        [SerializeField, Min(0.01f)] private float _gripSeconds = 0.12f;
        [SerializeField, Min(0.05f)] private float _liftToChestSeconds = 0.4f;
        [FormerlySerializedAs("_liftSeconds")]
        [SerializeField, Min(0.05f)] private float _standAndOverheadSeconds = 0.65f;
        [SerializeField, Min(0f)] private float _liftArcHeightM = 0.35f;
        [SerializeField, Min(0.1f)] private float _chestHeightM = 1.15f;
        [SerializeField, Min(0f)] private float _chestBodyClearanceM = 0.15f;
        [SerializeField, Min(0f)] private float _overheadClearanceM = 0.08f;
        [SerializeField, Min(0f)] private float _emptySearchSeconds = 1.5f;
        [SerializeField, Min(0.1f)] private float _escapeArrivalM = 1.2f;
        [SerializeField, Min(0f)] private float _exitCountdownSeconds = 5f;

        [Header("충격 반응")]
        [SerializeField, Min(0.05f)] private float _fallingSeconds = 0.8f;
        [SerializeField, Min(0f)] private float _downSeconds = 1f;
        [SerializeField, Min(0.05f)] private float _gettingUpSeconds = 1f;
        [SerializeField, Min(0f)] private float _dropGiftMaxDeltaVMps = 3f;

        private ThiefRaidSite _raidSite;
        private EGiftBoxKind _preferredKind;
        private Vector3 _home;
        private Gift _claimedGift;
        private Rigidbody _giftBody;
        private Collider[] _giftColliders;
        private bool[] _giftColliderStates;
        private bool _giftWasKinematic;
        private bool _giftUsedGravity;
        private Vector3 _liftStartPosition;
        private Quaternion _liftStartRotation;
        private Vector3 _liftChestPosition;
        private Quaternion _liftChestRotation;
        private ThiefGiftGeometry _giftGeometry;
        private bool _hasGiftGeometry;
        private float _liftPhaseElapsed;
        private float _liftPhaseProgress01;
        private float _searchElapsed;
        private EThiefLiftPhase _liftPhase;
        private bool _giftAttached;
        private bool _initialized;
        private bool _hasCargo;
        private bool _isEscaping;
        private bool _isDespawning;
        private float _exitCountdownRemaining;
        private EThiefImpactPhase _impactPhase;
        private float _impactPhaseElapsed;
        private float _impactPhaseProgress01;

        public EThiefAction CurrentAction { get; private set; } = EThiefAction.Waiting;
        public EThiefGait CurrentGait => _movement != null ? _movement.CurrentGait : EThiefGait.Idle;
        public bool HasClaimedGift => _claimedGift != null && _claimedGift.IsClaimedBy(this);
        public bool HasCargo => _hasCargo;
        public bool IsEscaping => _isEscaping;
        public bool IsInitialized => _initialized;
        public EThiefLiftPhase LiftPhase => _liftPhase;
        public float LiftPhaseProgress01 => _liftPhaseProgress01;
        public Gift CurrentLiftGift => _claimedGift;
        public float ExitCountdownRemaining => _exitCountdownRemaining;
        public bool IsImpactReacting => _impactPhase != EThiefImpactPhase.None;
        public EThiefImpactPhase ImpactPhase => _impactPhase;
        public float ImpactPhaseProgress01 => _impactPhaseProgress01;
        public bool IsSpotted => !HasClaimedGift && !_isEscaping &&
            _movement != null && _movement.AwarenessStage == EThiefAwarenessStage.Spotted;

        private void Awake()
        {
            if (_movement == null) _movement = GetComponent<ThiefMovement>();
            if (_bodyCollider == null) _bodyCollider = GetComponent<CapsuleCollider>();
        }

        private void LateUpdate()
        {
            if (!_hasCargo || _claimedGift == null || _carryAnchor == null) return;
            GetOverheadPose(out Vector3 position, out Quaternion rotation);
            MoveGift(position, rotation);
        }

        private void OnDisable()
        {
            if (!_isDespawning) ReleaseGift();
        }

        public void Initialize(ThiefRaidSite raidSite, EGiftBoxKind preferredKind, Vector3 home)
        {
            if (!HasBehaviorAuthority) return;
            _raidSite = raidSite;
            _preferredKind = preferredKind;
            _home = home;
            _initialized = raidSite != null;
            CurrentAction = EThiefAction.ApproachingSite;
        }

        public EThiefTaskResult TickAcquireOrApproach(float deltaSeconds)
        {
            if (!_initialized || _raidSite == null || _isEscaping) return EThiefTaskResult.Failure;
            if (!_raidSite.Contains(transform.position))
            {
                _searchElapsed = 0f;
                CurrentAction = EThiefAction.ApproachingSite;
                Vector3 approach = _raidSite.ClosestApproachPoint(transform.position);
                return _movement.MoveTo(approach, false)
                    ? EThiefTaskResult.Success : EThiefTaskResult.Running;
            }

            _movement.Stop();
            if (_raidSite.TryFindGift(transform.position, _preferredKind, out Gift gift) &&
                gift.TryClaim(this))
            {
                _claimedGift = gift;
                _searchElapsed = 0f;
                CurrentAction = EThiefAction.ApproachingGift;
                return EThiefTaskResult.Success;
            }

            _searchElapsed += deltaSeconds;
            if (_searchElapsed < _emptySearchSeconds) return EThiefTaskResult.Running;
            BeginEscape();
            return EThiefTaskResult.Success;
        }

        public bool BeginSpottedRetreat()
        {
            if (!IsSpotted) return false;
            _searchElapsed = 0f;
            BeginEscape();
            return true;
        }

        public EThiefTaskResult TickSteal(float deltaSeconds)
        {
            if (_claimedGift == null || !_claimedGift.IsClaimedBy(this))
            {
                CancelLift();
                return EThiefTaskResult.Failure;
            }
            if (_hasCargo || _isEscaping) return EThiefTaskResult.Failure;

            if (CurrentAction != EThiefAction.LiftingGift)
            {
                CurrentAction = EThiefAction.ApproachingGift;
                if (!ThiefGiftGeometry.TryCreate(_claimedGift, out ThiefGiftGeometry geometry))
                    return EThiefTaskResult.Failure;

                Vector3 grabPosition = geometry.GrabStandPosition(
                    transform.position, BodyRadiusM(), _grabSurfaceClearanceM);
                if (!_movement.MoveTo(grabPosition, false, GrabPositionToleranceM))
                    return EThiefTaskResult.Running;

                Vector3 toGift = geometry.WorldCenter - transform.position;
                toGift.y = 0f;
                if (toGift.sqrMagnitude > 0.001f)
                {
                    Quaternion targetRotation = Quaternion.LookRotation(toGift.normalized, Vector3.up);
                    float angle = Quaternion.Angle(transform.rotation, targetRotation);
                    if (angle > GrabFacingToleranceDeg)
                    {
                        transform.rotation = Quaternion.RotateTowards(transform.rotation,
                            targetRotation, GrabTurnSpeedDegPerS * Mathf.Max(0f, deltaSeconds));
                        return EThiefTaskResult.Running;
                    }
                }
                BeginLift();
            }

            return AdvanceLift(Mathf.Max(0f, deltaSeconds));
        }

        public EThiefTaskResult TickEscape(float deltaSeconds)
        {
            if (!_isEscaping) return EThiefTaskResult.Failure;
            if (CurrentAction == EThiefAction.ExitCountdown)
            {
                _movement.Stop();
                _exitCountdownRemaining = Mathf.Max(0f,
                    _exitCountdownRemaining - Mathf.Max(0f, deltaSeconds));
                if (_exitCountdownRemaining > 0f) return EThiefTaskResult.Running;
                DespawnRaid();
                return EThiefTaskResult.Success;
            }

            CurrentAction = EThiefAction.Escaping;
            if (!_movement.MoveTo(_home, true)) return EThiefTaskResult.Running;
            if ((transform.position - _home).sqrMagnitude > _escapeArrivalM * _escapeArrivalM)
                return EThiefTaskResult.Running;
            BeginExitCountdown();
            return EThiefTaskResult.Running;
        }

        public bool TryBeginImpact(ImpactHit hit)
        {
            if (!HasBehaviorAuthority || !_initialized || _isDespawning || IsImpactReacting) return false;
            if (CurrentAction == EThiefAction.ExitCountdown) return false;

            _movement.Stop();
            DropGift(hit);
            _isEscaping = false;
            _exitCountdownRemaining = 0f;
            CurrentAction = EThiefAction.ImpactReaction;
            EnterImpactPhase(EThiefImpactPhase.Falling);
            return true;
        }

        public EThiefTaskResult TickImpactReaction(float deltaSeconds)
        {
            if (!IsImpactReacting) return EThiefTaskResult.Failure;
            _movement.Stop();

            float remaining = Mathf.Max(0f, deltaSeconds);
            int transitionGuard = 0;
            while (IsImpactReacting && transitionGuard++ < 4)
            {
                float duration = ImpactPhaseDuration(_impactPhase);
                float available = Mathf.Max(0f, duration - _impactPhaseElapsed);
                float consumed = Mathf.Min(remaining, available);
                _impactPhaseElapsed += consumed;
                remaining -= consumed;
                _impactPhaseProgress01 = duration <= 0f
                    ? 1f : Mathf.Clamp01(_impactPhaseElapsed / duration);

                if (_impactPhaseProgress01 < 1f) break;
                CompleteImpactPhase();
                if (remaining <= 0f) break;
            }

            return IsImpactReacting ? EThiefTaskResult.Running : EThiefTaskResult.Success;
        }

        public void ApplyReplicatedState(EThiefAction action, EThiefGait gait, bool hasCargo,
            EThiefLiftPhase liftPhase, float liftPhaseProgress01, float exitCountdownRemaining,
            EThiefImpactPhase impactPhase, float impactPhaseProgress01)
        {
            if (HasBehaviorAuthority) return;
            CurrentAction = action;
            _hasCargo = hasCargo;
            _liftPhase = liftPhase;
            _liftPhaseProgress01 = Mathf.Clamp01(liftPhaseProgress01);
            _exitCountdownRemaining = Mathf.Max(0f, exitCountdownRemaining);
            _impactPhase = impactPhase;
            _impactPhaseProgress01 = Mathf.Clamp01(impactPhaseProgress01);
        }

        public bool HasBehaviorAuthority
        {
            get
            {
                NetworkObject networkObject = GetComponent<NetworkObject>();
                return networkObject == null || !networkObject.IsValid || networkObject.HasStateAuthority;
            }
        }

        private void BeginLift()
        {
            bool approachedCrouched = _movement != null && _movement.CurrentGait == EThiefGait.Crouch;
            _movement.Stop();
            CurrentAction = EThiefAction.LiftingGift;
            _liftPhaseElapsed = 0f;
            _liftPhaseProgress01 = 0f;
            _giftAttached = false;
            _hasGiftGeometry = ThiefGiftGeometry.TryCreate(_claimedGift, out _giftGeometry);
            _giftBody = _claimedGift.GetComponent<Rigidbody>();
            _giftColliders = _claimedGift.GetComponentsInChildren<Collider>(true);
            _giftColliderStates = new bool[_giftColliders.Length];
            for (int index = 0; index < _giftColliders.Length; index++)
            {
                Collider collider = _giftColliders[index];
                _giftColliderStates[index] = collider != null && collider.enabled;
            }

            _liftStartPosition = _claimedGift.transform.position;
            _liftStartRotation = _claimedGift.transform.rotation;
            GetChestPose(out _liftChestPosition, out _liftChestRotation);
            if (_giftBody != null)
            {
                _giftWasKinematic = _giftBody.isKinematic;
                _giftUsedGravity = _giftBody.useGravity;
            }
            EnterLiftPhase(approachedCrouched
                ? EThiefLiftPhase.ReachFloor : EThiefLiftPhase.PrepareCrouch);
        }

        private EThiefTaskResult AdvanceLift(float deltaSeconds)
        {
            float remaining = deltaSeconds;
            int transitionGuard = 0;
            while (_liftPhase is not EThiefLiftPhase.Carrying and not EThiefLiftPhase.None &&
                   transitionGuard++ < 8)
            {
                float duration = LiftPhaseDuration(_liftPhase);
                float available = Mathf.Max(0f, duration - _liftPhaseElapsed);
                float consumed = Mathf.Min(remaining, available);
                _liftPhaseElapsed += consumed;
                remaining -= consumed;
                _liftPhaseProgress01 = duration <= 0f
                    ? 1f : Mathf.Clamp01(_liftPhaseElapsed / duration);
                UpdateGiftForLiftPhase(_liftPhaseProgress01);

                if (_liftPhaseProgress01 < 1f) break;
                CompleteLiftPhase();
                if (remaining <= 0f) break;
            }

            return _liftPhase == EThiefLiftPhase.Carrying
                ? EThiefTaskResult.Success : EThiefTaskResult.Running;
        }

        private void CompleteLiftPhase()
        {
            switch (_liftPhase)
            {
                case EThiefLiftPhase.PrepareCrouch:
                    EnterLiftPhase(EThiefLiftPhase.ReachFloor);
                    break;
                case EThiefLiftPhase.ReachFloor:
                    EnterLiftPhase(EThiefLiftPhase.Grip);
                    break;
                case EThiefLiftPhase.Grip:
                    EnterLiftPhase(EThiefLiftPhase.LiftToChest);
                    break;
                case EThiefLiftPhase.LiftToChest:
                    if (_claimedGift != null)
                    {
                        _liftChestPosition = _claimedGift.transform.position;
                        _liftChestRotation = _claimedGift.transform.rotation;
                    }
                    else
                    {
                        GetChestPose(out _liftChestPosition, out _liftChestRotation);
                    }
                    EnterLiftPhase(EThiefLiftPhase.StandAndOverhead);
                    break;
                case EThiefLiftPhase.StandAndOverhead:
                    _hasCargo = true;
                    EnterLiftPhase(EThiefLiftPhase.Carrying);
                    BeginEscape();
                    break;
            }
        }

        private void EnterLiftPhase(EThiefLiftPhase phase)
        {
            _liftPhase = phase;
            _liftPhaseElapsed = 0f;
            _liftPhaseProgress01 = 0f;
            if (phase == EThiefLiftPhase.Grip) AttachGiftForLift();
            if (phase == EThiefLiftPhase.Carrying && _carryAnchor != null)
            {
                GetOverheadPose(out Vector3 position, out Quaternion rotation);
                MoveGift(position, rotation);
            }
        }

        private float LiftPhaseDuration(EThiefLiftPhase phase)
        {
            return phase switch
            {
                EThiefLiftPhase.PrepareCrouch => _prepareCrouchSeconds,
                EThiefLiftPhase.ReachFloor => _reachFloorSeconds,
                EThiefLiftPhase.Grip => _gripSeconds,
                EThiefLiftPhase.LiftToChest => _liftToChestSeconds,
                EThiefLiftPhase.StandAndOverhead => _standAndOverheadSeconds,
                _ => 0f,
            };
        }

        private void UpdateGiftForLiftPhase(float ratio)
        {
            if (!_giftAttached || _claimedGift == null) return;
            float smooth = ratio * ratio * (3f - 2f * ratio);
            switch (_liftPhase)
            {
                case EThiefLiftPhase.Grip:
                    MoveGift(_liftStartPosition, _liftStartRotation);
                    break;
                case EThiefLiftPhase.LiftToChest:
                {
                    GetChestPose(out Vector3 chest, out Quaternion chestRotation);
                    Vector3 position = Vector3.Lerp(_liftStartPosition, chest, smooth);
                    position += Vector3.up * Mathf.Sin(Mathf.PI * ratio) * (_liftArcHeightM * 0.35f);
                    MoveGift(position, Quaternion.Slerp(_liftStartRotation, chestRotation, smooth));
                    break;
                }
                case EThiefLiftPhase.StandAndOverhead:
                {
                    GetOverheadPose(out Vector3 overhead, out Quaternion rotation);
                    Vector3 position = Vector3.Lerp(_liftChestPosition, overhead, smooth);
                    position += Vector3.up * Mathf.Sin(Mathf.PI * ratio) * _liftArcHeightM;
                    MoveGift(position, Quaternion.Slerp(_liftChestRotation, rotation, smooth));
                    break;
                }
            }
        }

        private void GetChestPose(out Vector3 rootPosition, out Quaternion rotation)
        {
            rotation = transform.rotation;
            if (!_hasGiftGeometry)
            {
                rootPosition = transform.position + Vector3.up * _chestHeightM + transform.forward * 0.75f;
                return;
            }

            float giftRadius = _giftGeometry.SupportRadius(transform.forward, rotation);
            Vector3 desiredRoot = transform.position + Vector3.up * _chestHeightM;
            Vector3 desiredCenter = _giftGeometry.CenterAt(desiredRoot, rotation);
            desiredCenter += transform.forward * (BodyRadiusM() + giftRadius + _chestBodyClearanceM);
            rootPosition = _giftGeometry.RootPositionForCenter(desiredCenter, rotation);
        }

        private void GetOverheadPose(out Vector3 rootPosition, out Quaternion rotation)
        {
            rootPosition = _carryAnchor != null
                ? _carryAnchor.position : transform.position + Vector3.up * 2f;
            rotation = _carryAnchor != null ? _carryAnchor.rotation : transform.rotation;
            if (!_hasGiftGeometry) return;

            Vector3 center = _giftGeometry.CenterAt(rootPosition, rotation);
            float minimumCenterY = BodyTopY() +
                                   _giftGeometry.SupportRadius(Vector3.up, rotation) + _overheadClearanceM;
            if (center.y < minimumCenterY) rootPosition += Vector3.up * (minimumCenterY - center.y);
        }

        private float BodyRadiusM()
        {
            if (_bodyCollider == null) return 0.3f;
            Vector3 scale = _bodyCollider.transform.lossyScale;
            float radialScale = _bodyCollider.direction == 0
                ? Mathf.Max(Mathf.Abs(scale.y), Mathf.Abs(scale.z))
                : _bodyCollider.direction == 1
                    ? Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z))
                    : Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y));
            return _bodyCollider.radius * radialScale;
        }

        private float BodyTopY()
        {
            if (_bodyCollider == null) return transform.position.y + 1.8f;
            return _bodyCollider.bounds.max.y;
        }

        private void AttachGiftForLift()
        {
            if (_giftAttached || _claimedGift == null || !_claimedGift.IsClaimedBy(this)) return;
            _giftAttached = true;
            if (_giftColliders != null)
            {
                foreach (Collider collider in _giftColliders)
                    if (collider != null) collider.enabled = false;
            }
            if (_giftBody == null) return;
            _giftBody.linearVelocity = Vector3.zero;
            _giftBody.angularVelocity = Vector3.zero;
            _giftBody.useGravity = false;
            _giftBody.isKinematic = true;
        }

        private void BeginEscape()
        {
            _isEscaping = true;
            _exitCountdownRemaining = 0f;
            CurrentAction = EThiefAction.Escaping;
        }

        private void BeginExitCountdown()
        {
            _movement.Stop();
            _exitCountdownRemaining = Mathf.Max(0f, _exitCountdownSeconds);
            CurrentAction = EThiefAction.ExitCountdown;
        }

        private void EnterImpactPhase(EThiefImpactPhase phase)
        {
            _impactPhase = phase;
            _impactPhaseElapsed = 0f;
            _impactPhaseProgress01 = 0f;
        }

        private void CompleteImpactPhase()
        {
            switch (_impactPhase)
            {
                case EThiefImpactPhase.Falling:
                    EnterImpactPhase(EThiefImpactPhase.Down);
                    break;
                case EThiefImpactPhase.Down:
                    EnterImpactPhase(EThiefImpactPhase.GettingUp);
                    break;
                case EThiefImpactPhase.GettingUp:
                    _impactPhase = EThiefImpactPhase.None;
                    _impactPhaseElapsed = 0f;
                    _impactPhaseProgress01 = 0f;
                    BeginEscape();
                    break;
            }
        }

        private float ImpactPhaseDuration(EThiefImpactPhase phase)
        {
            return phase switch
            {
                EThiefImpactPhase.Falling => _fallingSeconds,
                EThiefImpactPhase.Down => _downSeconds,
                EThiefImpactPhase.GettingUp => _gettingUpSeconds,
                _ => 0f,
            };
        }

        private void MoveGift(Vector3 position, Quaternion rotation)
        {
            if (_giftBody != null)
            {
                _giftBody.MovePosition(position);
                _giftBody.MoveRotation(rotation);
                return;
            }
            if (_claimedGift != null) _claimedGift.transform.SetPositionAndRotation(position, rotation);
        }

        private void ReleaseGift()
        {
            if (_claimedGift != null) _claimedGift.ReleaseClaim(this);
            if (_giftBody != null)
            {
                _giftBody.isKinematic = _giftWasKinematic;
                _giftBody.useGravity = _giftUsedGravity;
            }
            if (_giftColliders != null && _giftColliderStates != null)
            {
                for (int index = 0; index < _giftColliders.Length; index++)
                    if (_giftColliders[index] != null) _giftColliders[index].enabled = _giftColliderStates[index];
            }
            _claimedGift = null;
            _giftBody = null;
            _giftColliders = null;
            _giftColliderStates = null;
            _giftAttached = false;
            _hasGiftGeometry = false;
            _liftPhase = EThiefLiftPhase.None;
            _liftPhaseElapsed = 0f;
            _liftPhaseProgress01 = 0f;
            _hasCargo = false;
        }

        private void DropGift(ImpactHit hit)
        {
            Rigidbody droppedBody = _giftBody;
            bool wasAttached = _giftAttached || _hasCargo;
            ReleaseGift();
            if (!wasAttached || droppedBody == null || droppedBody.isKinematic ||
                _dropGiftMaxDeltaVMps <= 0f) return;

            Vector3 direction = hit.Direction;
            direction.y = Mathf.Max(direction.y, 0.35f);
            direction.Normalize();
            droppedBody.AddForce(direction * (droppedBody.mass * _dropGiftMaxDeltaVMps),
                ForceMode.Impulse);
        }

        private void CancelLift()
        {
            if (_liftPhase == EThiefLiftPhase.None && _claimedGift == null) return;
            ReleaseGift();
            CurrentAction = _initialized ? EThiefAction.ApproachingSite : EThiefAction.Waiting;
        }

        private void DespawnRaid()
        {
            _isDespawning = true;
            ConsumeGift();
            NetworkObject networkObject = GetComponent<NetworkObject>();
            if (networkObject != null && networkObject.IsValid && networkObject.Runner != null)
            {
                networkObject.Runner.Despawn(networkObject);
                return;
            }
            Destroy(gameObject);
        }

        private void ConsumeGift()
        {
            if (_claimedGift == null) return;
            NetworkObject giftNetworkObject = _claimedGift.GetComponent<NetworkObject>();
            if (giftNetworkObject != null && giftNetworkObject.IsValid && giftNetworkObject.Runner != null)
                giftNetworkObject.Runner.Despawn(giftNetworkObject);
            else
                Destroy(_claimedGift.gameObject);
            _claimedGift = null;
            _giftBody = null;
            _liftPhase = EThiefLiftPhase.None;
            _liftPhaseProgress01 = 0f;
        }
    }
}
