using UnityEngine;

namespace PPack
{
    /// <summary>
    /// F로 가까운 눈덩이·선물을 고르면 물체의 가까운 면으로 걸어가 등을 보인 뒤 메고 내려놓는다.
    /// E는 <see cref="PenguinSnowball"/> 밀기만 소유하므로 두 의도가 겹치지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PenguinInputReader))]
    [RequireComponent(typeof(PenguinControlState))]
    [RequireComponent(typeof(PenguinLocomotion))]
    [RequireComponent(typeof(Rigidbody))]
    [DefaultExecutionOrder(-150)]
    public sealed class PenguinCarry : MonoBehaviour
    {
        private enum ECarryPhase
        {
            None,
            Approaching,
            Mounting,
            Holding,
            Placing
        }

        [Header("접근")]
        [SerializeField, Min(0.1f)] private float _searchSurfaceRadiusM = 2.5f;
        [SerializeField, Min(0.1f)] private float _approachSpeedMps = 2.8f;
        [SerializeField, Min(0.1f)] private float _approachAccelerationMps2 = 35f;
        [SerializeField, Min(0.01f)] private float _approachPositionToleranceM = 0.12f;
        [SerializeField, Range(1f, 30f)] private float _approachAngleToleranceDeg = 7f;
        [SerializeField, Min(0.1f)] private float _approachTimeoutSeconds = 3.5f;
        [SerializeField, Min(0f)] private float _approachGapM = 0.08f;

        [Header("동작")]
        [SerializeField, Min(0.05f)] private float _mountSeconds = 0.5f;
        [SerializeField, Min(0.05f)] private float _placeSeconds = 0.45f;
        [SerializeField, Min(0f)] private float _mountArcHeightM = 0.45f;
        [SerializeField, Min(0f)] private float _placeArcHeightM = 0.2f;
        [SerializeField, Min(0f)] private float _carryDeckHeightM = 0.38f;
        [SerializeField] private float _carryBackOffsetM = 0.12f;
        [SerializeField, Min(0.1f)] private float _dropBehindM = 0.8f;
        [SerializeField, Min(0f)] private float _dropBackSpeedMps = 0.6f;

        [Header("무게 반응")]
        [Tooltip("x=운반물 질량/펭귄 기본 질량, y=연출 세기. 최대 반지름과 독립이다.")]
        [SerializeField] private AnimationCurve _massRatioToStrength = new AnimationCurve(
            new Keyframe(0f, 0f), new Keyframe(0.1f, 0.18f),
            new Keyframe(1f, 0.65f), new Keyframe(4f, 1f));
        [SerializeField, Range(0.5f, 1f)] private float _fullLoadVerticalScale = 0.78f;
        [SerializeField, Range(1f, 1.5f)] private float _fullLoadHorizontalScale = 1.08f;
        [SerializeField, Min(0.01f)] private float _scaleBlendSeconds = 0.12f;

        private readonly RaycastHit[] _lineHits = new RaycastHit[16];
        private readonly RaycastHit[] _groundHits = new RaycastHit[16];

        private PenguinInputReader _input;
        private PenguinControlState _controlState;
        private PenguinSnowball _snowball;
        private PenguinImpactRelay _impactRelay;
        private PenguinBodyMotion _bodyMotion;
        private PenguinLocomotion _locomotion;
        private Rigidbody _playerBody;
        private float _basePlayerMass;

        private Component _cargo;
        private SnowBallCarrier _snowballCargo;
        private Gift _giftCargo;

        /// <summary>멀티에서는 서버가 이 컴포넌트를 돌린다. 켜지면 로컬 <see cref="FixedUpdate"/> 는
        /// 물러난다 — 피어마다 자기 키보드로 돌리면 남의 화물을 만진다.</summary>
        public bool NetworkDriven { get; set; }

        /// <summary>이번 스텝의 델타. 서버 틱과 로컬 <c>fixedDeltaTime</c> 이 다를 수 있어 인자로 받는다.</summary>
        private float _stepDeltaSeconds = 0.02f;
        private Rigidbody _cargoBody;
        private Collider[] _cargoColliders;
        private bool[] _colliderEnabled;
        private bool _cargoWasKinematic;
        private bool _cargoUsedGravity;
        private bool _cargoDetectedCollisions;
        private RigidbodyConstraints _cargoConstraints;
        private RigidbodyInterpolation _cargoInterpolation;
        private Vector3 _cargoLinearVelocity;
        private Vector3 _cargoAngularVelocity;
        private Vector3 _cargoStartPosition;
        private Quaternion _cargoStartRotation;
        private Vector3 _placeTargetPosition;
        private Quaternion _placeTargetRotation;
        private float _cargoMassKg;
        private float _cargoHalfHeightM;
        private Vector3 _cargoBoundsSize;
        private float _loadStrength01;
        private bool _docked;
        private Component _nearbyCandidate;
        private Vector3 _approachOutward;
        private Vector3 _approachTargetPosition;

        private GameObject _proxyRoot;
        private SphereCollider _sphereProxy;
        private BoxCollider _boxProxy;
        private ECarryPhase _phase;
        private float _phaseElapsed;

        public Component Cargo => _cargo;
        public bool IsApproaching => _phase == ECarryPhase.Approaching;
        public bool IsCarrying => _phase is ECarryPhase.Mounting or ECarryPhase.Holding
            or ECarryPhase.Placing;
        public bool IsHolding => _phase == ECarryPhase.Holding;
        public float LoadStrength01 => _loadStrength01;
        public bool CanApproachCargo => _phase == ECarryPhase.None && _nearbyCandidate != null;

        public float CarryPoseWeight
        {
            get
            {
                float duration = _phase == ECarryPhase.Placing ? _placeSeconds : _mountSeconds;
                float t = Mathf.Clamp01(_phaseElapsed / Mathf.Max(0.01f, duration));
                return _phase switch
                {
                    ECarryPhase.Mounting => Smooth(t),
                    ECarryPhase.Holding => 1f,
                    ECarryPhase.Placing => 1f - Smooth(t),
                    _ => 0f
                };
            }
        }

        public Vector3 CargoCenter => _cargoBody != null ? _cargoBody.position : transform.position;
        public float CargoRadiusM => _snowballCargo != null ? _snowballCargo.RadiusM : 0f;
        public bool CargoIsSnowball => _snowballCargo != null;

        private void Awake()
        {
            _input = GetComponent<PenguinInputReader>();
            _controlState = GetComponent<PenguinControlState>();
            _snowball = GetComponent<PenguinSnowball>();
            _impactRelay = GetComponent<PenguinImpactRelay>();
            _bodyMotion = GetComponentInChildren<PenguinBodyMotion>(true);
            _locomotion = GetComponent<PenguinLocomotion>();
            _playerBody = GetComponent<Rigidbody>();
            _basePlayerMass = _playerBody.mass;
            CreateCollisionProxy();
        }

        private void FixedUpdate()
        {
            if (NetworkDriven) return;
            Step(Time.fixedDeltaTime, _input != null && _input.PickupPressedThisFrame);
        }

        /// <summary>
        /// 한 스텝 굴린다. 싱글은 <see cref="FixedUpdate"/> 가, 멀티는 서버의
        /// <see cref="PenguinNetAvatar"/> 가 부른다 — <see cref="PenguinLocomotion.Step"/> 과 같은 모양이다.
        /// 읽는 규칙이 두 곳에 살면 싱글과 멀티의 조작이 조용히 갈린다.
        /// </summary>
        public void Step(float deltaSeconds, bool pickupPressed)
        {
            _stepDeltaSeconds = deltaSeconds;

            if (_cargo == null && _phase != ECarryPhase.None)
            {
                FinishWithoutCargo();
                return;
            }

            if (pickupPressed) HandleInteract();

            switch (_phase)
            {
                case ECarryPhase.Approaching:
                    TickApproaching();
                    break;
                case ECarryPhase.Mounting:
                    TickMounting();
                    break;
                case ECarryPhase.Holding:
                    MoveCargo(CarryPosition(), CarryRotation());
                    break;
                case ECarryPhase.Placing:
                    TickPlacing();
                    break;
            }
        }

        private void Update()
        {
            _nearbyCandidate = _phase == ECarryPhase.None && _controlState != null &&
                (_controlState.Current == EPenguinControlState.Normal || _controlState.IsSnowballState)
                ? FindNearbyCargo()
                : null;
        }

        private void LateUpdate()
        {
            if (_bodyMotion == null) return;

            float strength = _docked ? _loadStrength01 : 0f;
            Vector3 target = new Vector3(
                Mathf.Lerp(1f, _fullLoadHorizontalScale, strength),
                Mathf.Lerp(1f, _fullLoadVerticalScale, strength),
                Mathf.Lerp(1f, _fullLoadHorizontalScale, strength));
            float step = _scaleBlendSeconds <= 0f
                ? float.MaxValue
                : Time.deltaTime / _scaleBlendSeconds;
            _bodyMotion.transform.localScale = Vector3.MoveTowards(
                _bodyMotion.transform.localScale, target, step);
        }

        private void OnDisable()
        {
            if (Application.isPlaying && _phase != ECarryPhase.None) ForceRelease();
        }

        private void HandleInteract()
        {
            if (_phase != ECarryPhase.None)
            {
                if (_phase == ECarryPhase.Approaching) CancelApproach();
                if (_phase == ECarryPhase.Holding) TryBeginPlacing();
                return;
            }

            Component cargo = null;
            if (_controlState != null && _controlState.IsSnowballState && _snowball != null)
            {
                cargo = _snowball.Held;
                _snowball.Release();
            }
            else if (_controlState == null || _controlState.Current != EPenguinControlState.Normal)
                return;

            if (cargo == null) cargo = _nearbyCandidate != null ? _nearbyCandidate : FindNearbyCargo();
            if (cargo != null) BeginApproach(cargo);
        }

        private Component FindNearbyCargo()
        {
            Component best = null;
            float bestSurface = float.MaxValue;

            foreach (SnowBallCarrier ball in FindObjectsByType<SnowBallCarrier>())
            {
                if (ball.gameObject.scene != gameObject.scene) continue;
                Consider(ball, ball.GetComponent<Rigidbody>(), ball.RadiusM, ref best, ref bestSurface);
            }

            foreach (Gift gift in Gift.All)
            {
                if (gift == null || !gift.isActiveAndEnabled || gift.IsCarried) continue;
                Consider(gift, gift.GetComponent<Rigidbody>(), 0f, ref best, ref bestSurface);
            }

            return best;
        }

        private void Consider(Component candidate, Rigidbody body, float radius,
            ref Component best, ref float bestSurface)
        {
            if (candidate == null || body == null || body.isKinematic) return;

            Vector3 center = body.worldCenterOfMass;
            Vector3 flat = center - transform.position;
            flat.y = 0f;
            float centerDistance = flat.magnitude;
            if (centerDistance <= 0.001f) return;

            float surface = radius > 0f
                ? centerDistance - radius
                : SurfaceDistance(candidate, transform.position);
            if (surface > _searchSurfaceRadiusM || !HasLineOfSight(candidate, center)) return;
            if (surface >= bestSurface) return;

            bestSurface = surface;
            best = candidate;
        }

        private static float SurfaceDistance(Component candidate, Vector3 point)
        {
            Collider[] colliders = candidate.GetComponentsInChildren<Collider>(true);
            float best = float.MaxValue;
            for (int i = 0; i < colliders.Length; i++)
            {
                Collider collider = colliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                best = Mathf.Min(best, Vector3.Distance(point, collider.ClosestPoint(point)));
            }
            return best;
        }

        private bool HasLineOfSight(Component candidate, Vector3 center)
        {
            Vector3 origin = transform.position + Vector3.up * 0.65f;
            Vector3 delta = center - origin;
            float distance = delta.magnitude;
            if (distance <= 0.001f) return true;

            int count = Physics.RaycastNonAlloc(origin, delta / distance, _lineHits, distance,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                Collider hit = _lineHits[i].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                if (hit.GetComponentInParent(candidate.GetType()) == candidate) continue;
                return false;
            }
            return true;
        }

        private void BeginApproach(Component cargo)
        {
            Rigidbody body = cargo.GetComponent<Rigidbody>();
            Gift gift = cargo as Gift;
            if (body == null || (gift != null && !gift.TryClaim(this))) return;
            if (!_controlState.TryTransitionTo(EPenguinControlState.CarryApproach))
            {
                gift?.ReleaseClaim(this);
                return;
            }

            _cargo = cargo;
            _snowballCargo = cargo as SnowBallCarrier;
            _giftCargo = gift;
            _cargoBody = body;
            _cargoMassKg = Mathf.Max(0.01f, body.mass);
            float ratio = _cargoMassKg / Mathf.Max(0.01f, _basePlayerMass);
            _loadStrength01 = Mathf.Clamp01(_massRatioToStrength.Evaluate(ratio));
            _cargoStartPosition = body.position;
            _cargoStartRotation = body.rotation;
            _cargoWasKinematic = body.isKinematic;
            _cargoUsedGravity = body.useGravity;
            _cargoDetectedCollisions = body.detectCollisions;
            _cargoConstraints = body.constraints;
            _cargoLinearVelocity = body.linearVelocity;
            _cargoAngularVelocity = body.angularVelocity;

            _cargoColliders = cargo.GetComponentsInChildren<Collider>(true);
            _colliderEnabled = new bool[_cargoColliders.Length];
            for (int i = 0; i < _cargoColliders.Length; i++)
            {
                _colliderEnabled[i] = _cargoColliders[i] != null && _cargoColliders[i].enabled;
            }

            _cargoInterpolation = body.interpolation;

            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.useGravity = false;
            body.isKinematic = true;

            // <b>운반 중에는 보간을 켠다.</b> 화물은 kinematic 이 되어 물리 스텝(50Hz)마다
            // <see cref="MoveCargo"/> 가 위치를 옮기는데, 보간이 꺼져 있으면 렌더 프레임이 그보다
            // 촘촘한 만큼 위치가 계단으로 뛴다 — 그것이 등에 멘 눈덩이의 떨림으로 보인다.
            // 펭귄 본체는 이미 보간되고 있어서 화물만 따라오지 못하면 둘이 어긋나 더 도드라진다.
            body.interpolation = RigidbodyInterpolation.Interpolate;
            if (_giftCargo != null) NormalizeGiftForApproach();
            CacheCargoBounds();

            Vector3 center = body.worldCenterOfMass;
            _approachOutward = transform.position - center;
            _approachOutward.y = 0f;
            if (_approachOutward.sqrMagnitude < 0.001f) _approachOutward = -transform.forward;
            _approachOutward.Normalize();
            float cargoExtent = _snowballCargo != null
                ? _snowballCargo.RadiusM
                : Mathf.Max(_cargoBoundsSize.x, _cargoBoundsSize.z) * 0.5f;
            CapsuleCollider capsule = GetComponent<CapsuleCollider>();
            float playerRadius = capsule != null ? capsule.radius * Mathf.Max(
                Mathf.Abs(transform.lossyScale.x), Mathf.Abs(transform.lossyScale.z)) : 0.4f;
            _approachTargetPosition = center + _approachOutward *
                (cargoExtent + playerRadius + _approachGapM);
            _approachTargetPosition.y = _playerBody.position.y;

            _docked = false;
            _phase = ECarryPhase.Approaching;
            _phaseElapsed = 0f;

            // 눈덩이는 <b>잡혔다는 사실을 복제한다</b>. 위에서 건 kinematic · 중력 · 충돌 설정은
            // 이 컴포넌트가 서버에서만 돌기 때문에(NetworkDriven) 클라이언트 사본에 닿지 않는다.
            // 근거와 증상은 SnowBallCarrier.BeginExternalMotion 에 적어 뒀다.
            // 접근 단계부터 거는 이유: 이때 이미 서버의 공은 kinematic 이라, 클라 사본만 동적으로
            // 두면 접근하는 동안에도 떨어지며 어긋난다.
            if (_snowballCargo != null) _snowballCargo.BeginExternalMotion();
        }

        private void TickApproaching()
        {
            _phaseElapsed += _stepDeltaSeconds;
            if (_phaseElapsed >= _approachTimeoutSeconds)
            {
                CancelApproach();
                return;
            }

            Vector3 offset = _approachTargetPosition - _playerBody.position;
            offset.y = 0f;
            float distance = offset.magnitude;
            Vector3 horizontalVelocity = Vector3.ProjectOnPlane(_playerBody.linearVelocity, Vector3.up);
            Vector3 desiredForward = distance > _approachPositionToleranceM
                ? offset.normalized
                : _approachOutward;
            float yawErrorDeg = Vector3.SignedAngle(transform.forward, desiredForward, Vector3.up);
            if (distance <= _approachPositionToleranceM &&
                Mathf.Abs(yawErrorDeg) <= _approachAngleToleranceDeg &&
                horizontalVelocity.magnitude <= 0.35f)
            {
                BeginMounting();
                return;
            }

            float stoppingSpeed = Mathf.Sqrt(2f * _approachAccelerationMps2 * distance);
            Vector3 desiredVelocity = distance > 0.001f
                ? offset / distance * Mathf.Min(_approachSpeedMps, stoppingSpeed)
                : Vector3.zero;
            Vector3 acceleration = Vector3.ClampMagnitude(
                (desiredVelocity - horizontalVelocity) / _stepDeltaSeconds,
                _approachAccelerationMps2);
            _playerBody.AddForce(acceleration, ForceMode.Acceleration);

            float desiredYawRad = Mathf.Clamp(yawErrorDeg * Mathf.Deg2Rad / 0.12f, -7f, 7f);
            float yawAccel = Mathf.Clamp((desiredYawRad - _playerBody.angularVelocity.y) /
                _stepDeltaSeconds, -70f, 70f);
            _playerBody.AddTorque(Vector3.up * yawAccel, ForceMode.Acceleration);

        }

        private void BeginMounting()
        {
            if (!_controlState.TryTransitionTo(EPenguinControlState.Carrying))
            {
                CancelApproach();
                return;
            }

            _playerBody.linearVelocity = new Vector3(0f, _playerBody.linearVelocity.y, 0f);
            _playerBody.angularVelocity = Vector3.zero;
            StabilizePlayerPose();
            _cargoStartPosition = _cargoBody.position;
            _cargoStartRotation = _cargoBody.rotation;
            for (int i = 0; i < _cargoColliders.Length; i++)
                if (_cargoColliders[i] != null) _cargoColliders[i].enabled = false;
            _cargoBody.detectCollisions = false;
            _phase = ECarryPhase.Mounting;
            _phaseElapsed = 0f;
        }

        private void CancelApproach()
        {
            if (_phase != ECarryPhase.Approaching) return;
            if (_playerBody != null)
            {
                Vector3 velocity = _playerBody.linearVelocity;
                velocity.x = 0f;
                velocity.z = 0f;
                _playerBody.linearVelocity = velocity;
                _playerBody.angularVelocity = Vector3.zero;
            }
            RestoreCargo(_cargoStartPosition, _cargoStartRotation, _cargoLinearVelocity,
                _cargoAngularVelocity);
            StabilizePlayerPose();
            ClearCargo();
        }

        private void TickMounting()
        {
            _phaseElapsed += _stepDeltaSeconds;
            float t = Mathf.Clamp01(_phaseElapsed / Mathf.Max(0.01f, _mountSeconds));
            float eased = Smooth(t);
            Vector3 target = CarryPosition();
            Vector3 position = Vector3.Lerp(_cargoStartPosition, target, eased);
            position += Vector3.up * (Mathf.Sin(Mathf.PI * t) * _mountArcHeightM);

            Quaternion rotation = Quaternion.Slerp(_cargoStartRotation, CarryRotation(), eased);
            if (_snowballCargo != null)
            {
                float traveled = Vector3.Distance(_cargoStartPosition, position);
                float degrees = traveled / Mathf.Max(0.01f, _snowballCargo.RadiusM) * Mathf.Rad2Deg;
                rotation = Quaternion.AngleAxis(degrees, transform.right) * _cargoStartRotation;
            }
            MoveCargo(position, rotation);

            if (t >= 1f) DockCargo();
        }

        private void DockCargo()
        {
            _docked = true;
            _phase = ECarryPhase.Holding;
            _phaseElapsed = 0f;
            _playerBody.mass = _basePlayerMass + _cargoMassKg;
            ConfigureProxy();
            if (_impactRelay != null) _impactRelay.PlayCarryLoad(_loadStrength01);
        }

        private void TryBeginPlacing()
        {
            StabilizePlayerPose();
            if (!TryFindDropPose(out _placeTargetPosition, out _placeTargetRotation)) return;

            _cargoStartPosition = _cargoBody.position;
            _cargoStartRotation = _cargoBody.rotation;
            _phase = ECarryPhase.Placing;
            _phaseElapsed = 0f;
        }

        private void TickPlacing()
        {
            _phaseElapsed += _stepDeltaSeconds;
            float t = Mathf.Clamp01(_phaseElapsed / Mathf.Max(0.01f, _placeSeconds));
            float eased = Smooth(t);
            Vector3 position = Vector3.Lerp(_cargoStartPosition, _placeTargetPosition, eased);
            position += Vector3.up * (Mathf.Sin(Mathf.PI * t) * _placeArcHeightM);
            Quaternion rotation = Quaternion.Slerp(_cargoStartRotation, _placeTargetRotation, eased);

            if (_snowballCargo != null)
            {
                float traveled = Vector3.Distance(_cargoStartPosition, position);
                float degrees = traveled / Mathf.Max(0.01f, _snowballCargo.RadiusM) * Mathf.Rad2Deg;
                rotation = Quaternion.AngleAxis(-degrees, transform.right) * _cargoStartRotation;
            }
            MoveCargo(position, rotation);

            if (t >= 1f) ReleaseAt(_placeTargetPosition, _placeTargetRotation, true);
        }

        private bool TryFindDropPose(out Vector3 position, out Quaternion rotation)
        {
            Vector3 behind = transform.position - transform.forward * _dropBehindM;
            Vector3 origin = behind + Vector3.up * 3f;
            int count = Physics.RaycastNonAlloc(origin, Vector3.down, _groundHits, 8f,
                Physics.AllLayers, QueryTriggerInteraction.Ignore);

            float nearest = float.MaxValue;
            RaycastHit best = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform)) continue;
                if (_cargo != null && hit.collider.transform.IsChildOf(_cargo.transform)) continue;
                if (hit.distance >= nearest) continue;
                nearest = hit.distance;
                best = hit;
            }

            if (nearest == float.MaxValue)
            {
                position = default;
                rotation = default;
                return false;
            }

            position = best.point + Vector3.up * CargoHalfHeight();
            rotation = _snowballCargo != null ? _cargoBody.rotation : transform.rotation;
            return true;
        }

        private Vector3 CarryPosition()
        {
            return transform.position + Vector3.up * (_carryDeckHeightM + CargoHalfHeight())
                                      - transform.forward * _carryBackOffsetM;
        }

        private Quaternion CarryRotation() => _snowballCargo != null
            ? _cargoBody.rotation
            : transform.rotation;

        private float CargoHalfHeight()
        {
            return _snowballCargo != null ? _snowballCargo.RadiusM : _cargoHalfHeightM;
        }

        private void CacheCargoBounds()
        {
            if (_snowballCargo != null)
            {
                float diameter = _snowballCargo.RadiusM * 2f;
                _cargoBoundsSize = Vector3.one * diameter;
                _cargoHalfHeightM = _snowballCargo.RadiusM;
                return;
            }

            bool hasBounds = TryGetCargoBounds(out Bounds bounds);
            _cargoBoundsSize = hasBounds ? bounds.size : Vector3.one * 0.5f;
            _cargoHalfHeightM = Mathf.Max(0.1f, _cargoBoundsSize.y * 0.5f);
        }

        private void NormalizeGiftForApproach()
        {
            bool hasBounds = TryGetCargoBounds(out Bounds before);
            Transform cargoTransform = _cargoBody.transform;
            Vector3 targetPosition = _cargoBody.position;
            Quaternion targetRotation = Quaternion.Euler(0f, transform.eulerAngles.y, 0f);
            cargoTransform.SetPositionAndRotation(targetPosition, targetRotation);
            Physics.SyncTransforms();

            if (hasBounds && TryGetCargoBounds(out Bounds after))
                targetPosition += Vector3.up * (before.min.y - after.min.y);

            _cargoBody.position = targetPosition;
            _cargoBody.rotation = targetRotation;
            cargoTransform.SetPositionAndRotation(targetPosition, targetRotation);
            Physics.SyncTransforms();
        }

        private bool TryGetCargoBounds(out Bounds bounds)
        {
            bounds = default;
            bool hasBounds = false;
            for (int i = 0; i < _cargoColliders.Length; i++)
            {
                Collider collider = _cargoColliders[i];
                if (collider == null || !collider.enabled || collider.isTrigger) continue;
                if (!hasBounds) { bounds = collider.bounds; hasBounds = true; }
                else bounds.Encapsulate(collider.bounds);
            }
            return hasBounds;
        }

        private void MoveCargo(Vector3 position, Quaternion rotation)
        {
            if (_cargoBody == null) return;
            _cargoBody.MovePosition(position);
            _cargoBody.MoveRotation(rotation);
        }

        private void ConfigureProxy()
        {
            _proxyRoot.transform.localPosition = transform.InverseTransformPoint(CarryPosition());
            _proxyRoot.transform.localRotation = Quaternion.identity;
            PhysicsMaterial slidingMaterial = _locomotion != null
                ? _locomotion.SlidingMaterial
                : null;
            _sphereProxy.sharedMaterial = slidingMaterial;
            _boxProxy.sharedMaterial = slidingMaterial;

            if (_snowballCargo != null)
            {
                _sphereProxy.radius = _snowballCargo.RadiusM;
                _sphereProxy.enabled = true;
                _boxProxy.enabled = false;
                return;
            }

            _boxProxy.size = _cargoBoundsSize;
            _boxProxy.enabled = true;
            _sphereProxy.enabled = false;
        }

        private void CreateCollisionProxy()
        {
            _proxyRoot = new GameObject("CarryCollisionProxy");
            _proxyRoot.layer = gameObject.layer;
            _proxyRoot.transform.SetParent(transform, false);
            _sphereProxy = _proxyRoot.AddComponent<SphereCollider>();
            _boxProxy = _proxyRoot.AddComponent<BoxCollider>();
            _sphereProxy.enabled = false;
            _boxProxy.enabled = false;
        }

        public void ForceRelease()
        {
            if (_phase == ECarryPhase.None) return;
            if (_phase == ECarryPhase.Approaching)
            {
                CancelApproach();
                return;
            }
            StabilizePlayerPose();
            Vector3 position = _cargoBody != null ? _cargoBody.position : transform.position;
            Quaternion rotation = _cargoBody != null ? _cargoBody.rotation : transform.rotation;
            ReleaseAt(position, rotation, false);
        }

        private void ReleaseAt(Vector3 position, Quaternion rotation, bool playFeedback)
        {
            StabilizePlayerPose();
            if (_proxyRoot != null)
            {
                _sphereProxy.enabled = false;
                _boxProxy.enabled = false;
            }
            if (_playerBody != null) _playerBody.mass = _basePlayerMass;

            if (_cargoBody != null)
            {
                Vector3 releaseVelocity = _playerBody.linearVelocity
                    - transform.forward * _dropBackSpeedMps;
                RestoreCargo(position, rotation, releaseVelocity, Vector3.zero);
                if (!_cargoBody.isKinematic)
                    _cargoBody.WakeUp();
            }

            if (_cargoColliders != null)
            {
                for (int i = 0; i < _cargoColliders.Length; i++)
                    if (_cargoColliders[i] != null) _cargoColliders[i].enabled = _colliderEnabled[i];
            }
            if (_giftCargo != null) _giftCargo.ReleaseClaim(this);
            if (playFeedback && _impactRelay != null) _impactRelay.PlayCarryRelease(_loadStrength01);

            ClearCargo();
        }

        private void RestoreCargo(Vector3 position, Quaternion rotation, Vector3 velocity,
            Vector3 angularVelocity)
        {
            if (_cargoBody == null) return;
            _cargoBody.position = position;
            _cargoBody.rotation = rotation;
            _cargoBody.detectCollisions = _cargoDetectedCollisions;
            _cargoBody.useGravity = _cargoUsedGravity;
            _cargoBody.constraints = _cargoConstraints;
            _cargoBody.interpolation = _cargoInterpolation;
            _cargoBody.isKinematic = _cargoWasKinematic;
            if (!_cargoBody.isKinematic)
            {
                _cargoBody.linearVelocity = velocity;
                _cargoBody.angularVelocity = angularVelocity;
            }

            if (_cargoColliders != null)
            {
                for (int i = 0; i < _cargoColliders.Length; i++)
                    if (_cargoColliders[i] != null) _cargoColliders[i].enabled = _colliderEnabled[i];
            }
            if (_giftCargo != null) _giftCargo.ReleaseClaim(this);
        }

        private void ClearCargo()
        {
            // 해제 경로 셋(CancelApproach · ReleaseAt · FinishWithoutCargo)이 전부 여기로 모이므로
            // 복제 해제도 여기 한 곳이면 된다.
            if (_snowballCargo != null) _snowballCargo.EndExternalMotion();

            _docked = false;
            _phase = ECarryPhase.None;
            _phaseElapsed = 0f;
            _cargo = null;
            _snowballCargo = null;
            _giftCargo = null;
            _cargoBody = null;
            _cargoColliders = null;
            _colliderEnabled = null;
            _loadStrength01 = 0f;
            if (_controlState != null && _controlState.Current is
                EPenguinControlState.CarryApproach or EPenguinControlState.Carrying)
                _controlState.TryTransitionTo(EPenguinControlState.Normal);
        }

        private void FinishWithoutCargo()
        {
            if (_playerBody != null) _playerBody.mass = _basePlayerMass;
            if (_proxyRoot != null)
            {
                _sphereProxy.enabled = false;
                _boxProxy.enabled = false;
            }
            StabilizePlayerPose();
            ClearCargo();
        }

        private void StabilizePlayerPose()
        {
            if (_playerBody != null)
            {
                Vector3 forward = Vector3.ProjectOnPlane(
                    _playerBody.rotation * Vector3.forward, Vector3.up);
                if (forward.sqrMagnitude <= 0.0001f)
                    forward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
                if (forward.sqrMagnitude <= 0.0001f) forward = Vector3.forward;

                _playerBody.rotation = Quaternion.LookRotation(forward.normalized, Vector3.up);
                float yawVelocity = Vector3.Dot(_playerBody.angularVelocity, Vector3.up);
                _playerBody.angularVelocity = Vector3.up * yawVelocity;
            }

            if (_bodyMotion != null) _bodyMotion.ResetPose();
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
