using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Animator), typeof(CharacterController))]
    public sealed class WinterRabbitFlee : MonoBehaviour
    {
        private static readonly int IsRunning = Animator.StringToHash("isRunning");
        private static readonly int IsLookingOut = Animator.StringToHash("isLookingOut");

        [SerializeField] private Transform _threat;
        [SerializeField] private string _threatObjectName = "PlayerVehicle";
        [SerializeField, Min(0.5f)] private float _fleeDistance = 9f;
        [SerializeField, Min(0.5f)] private float _safeDistance = 15f;
        [SerializeField, Min(0.1f)] private float _runSpeed = 4.4f;
        [SerializeField, Min(30f)] private float _turnSpeed = 420f;
        [SerializeField, Min(1f)] private float _homeRadius = 15f;
        [SerializeField, Min(0.1f)] private float _obstacleProbeDistance = 1.15f;
        [SerializeField, Min(0.1f)] private float _gravity = 14f;

        private readonly RaycastHit[] _probeHits = new RaycastHit[12];
        private CharacterController _controller;
        private Animator _animator;
        private Vector3 _homePosition;
        private float _verticalSpeed;
        private float _minimumFleeTime;
        private float _lookOutTimer;
        private float _lookOutDuration;
        private float _sideSign;
        private uint _randomState;

        public bool IsFleeing { get; private set; }
        public float ThreatDistance { get; private set; } = float.PositiveInfinity;

        private void Awake()
        {
            _controller = GetComponent<CharacterController>();
            _animator = GetComponent<Animator>();
            _homePosition = transform.position;
            _randomState = unchecked((uint)GetEntityId().GetHashCode()) | 1u;
            _sideSign = Next01() < 0.5f ? -1f : 1f;
            ScheduleLookOut();
            ResolveThreat();
            SetAnimation(false, false);
        }

        private void Update()
        {
            ResolveThreat();
            UpdateThreatDistance();

            if (!IsFleeing && ThreatDistance <= _fleeDistance)
            {
                BeginFlee();
            }

            if (IsFleeing)
            {
                _minimumFleeTime -= Time.deltaTime;
                if (_minimumFleeTime <= 0f && ThreatDistance >= _safeDistance)
                {
                    EndFlee();
                }
                else
                {
                    MoveAway(Time.deltaTime);
                    return;
                }
            }

            UpdateIdle(Time.deltaTime);
        }

        private void ResolveThreat()
        {
            if (_threat != null || string.IsNullOrEmpty(_threatObjectName))
            {
                return;
            }

            GameObject target = GameObject.Find(_threatObjectName);
            if (target != null)
            {
                _threat = target.transform;
            }
        }

        private void UpdateThreatDistance()
        {
            if (_threat == null)
            {
                ThreatDistance = float.PositiveInfinity;
                return;
            }

            Vector3 offset = transform.position - _threat.position;
            offset.y = 0f;
            ThreatDistance = offset.magnitude;
        }

        private void BeginFlee()
        {
            IsFleeing = true;
            _minimumFleeTime = 1.15f;
            _lookOutDuration = 0f;
            SetAnimation(true, false);
            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.CrossFade("Rabbit_Run", 0.08f);
            }
        }

        private void EndFlee()
        {
            IsFleeing = false;
            ScheduleLookOut();
            SetAnimation(false, false);
            if (_animator != null && _animator.isActiveAndEnabled)
            {
                _animator.CrossFade("Rabbit_Idle", 0.12f);
            }
        }

        private void MoveAway(float deltaTime)
        {
            Vector3 away = _threat != null ? transform.position - _threat.position : transform.forward;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
            {
                away = transform.forward;
            }

            Vector3 direction = away.normalized;
            Vector3 toHome = _homePosition - transform.position;
            toHome.y = 0f;
            if (toHome.sqrMagnitude > _homeRadius * _homeRadius)
            {
                direction = Vector3.Slerp(direction, toHome.normalized, 0.78f).normalized;
            }

            direction = FindOpenDirection(direction);
            Quaternion targetRotation = Quaternion.LookRotation(direction, Vector3.up);
            transform.rotation = Quaternion.RotateTowards(transform.rotation, targetRotation, _turnSpeed * deltaTime);

            if (_controller.isGrounded && _verticalSpeed < 0f)
            {
                _verticalSpeed = -1f;
            }
            else
            {
                _verticalSpeed -= _gravity * deltaTime;
            }

            Vector3 motion = direction * _runSpeed + Vector3.up * _verticalSpeed;
            _controller.Move(motion * deltaTime);
            SetAnimation(true, false);
        }

        private Vector3 FindOpenDirection(Vector3 desired)
        {
            float[] angles = { 0f, 38f, -38f, 72f, -72f };
            for (int i = 0; i < angles.Length; i++)
            {
                float angle = angles[i] * _sideSign;
                Vector3 candidate = Quaternion.AngleAxis(angle, Vector3.up) * desired;
                if (!IsBlocked(candidate) && HasGroundAhead(candidate))
                {
                    return candidate.normalized;
                }
            }

            Vector3 home = _homePosition - transform.position;
            home.y = 0f;
            return home.sqrMagnitude > 0.001f ? home.normalized : -desired;
        }

        private bool IsBlocked(Vector3 direction)
        {
            Vector3 origin = transform.position + Vector3.up * 0.28f;
            int count = Physics.SphereCastNonAlloc(
                origin,
                0.12f,
                direction,
                _probeHits,
                _obstacleProbeDistance,
                ~0,
                QueryTriggerInteraction.Ignore);

            for (int i = 0; i < count; i++)
            {
                Collider hit = _probeHits[i].collider;
                if (hit != null && !hit.transform.IsChildOf(transform))
                {
                    return true;
                }
            }

            return false;
        }

        private bool HasGroundAhead(Vector3 direction)
        {
            Vector3 probePosition = transform.position + direction * 0.9f + Vector3.up * 1.4f;
            int count = Physics.RaycastNonAlloc(
                probePosition,
                Vector3.down,
                _probeHits,
                3.2f,
                ~0,
                QueryTriggerInteraction.Ignore);

            float bestDistance = float.PositiveInfinity;
            bool foundGround = false;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _probeHits[i];
                if (hit.collider == null || hit.collider.transform.IsChildOf(transform) || hit.normal.y < 0.65f)
                {
                    continue;
                }

                if (hit.distance < bestDistance && Mathf.Abs(hit.point.y - transform.position.y) <= 1.1f)
                {
                    bestDistance = hit.distance;
                    foundGround = true;
                }
            }

            return foundGround;
        }

        private void UpdateIdle(float deltaTime)
        {
            if (!_controller.isGrounded)
            {
                _verticalSpeed -= _gravity * deltaTime;
                _controller.Move(Vector3.up * (_verticalSpeed * deltaTime));
            }
            else
            {
                _verticalSpeed = -1f;
            }

            if (_lookOutDuration > 0f)
            {
                _lookOutDuration -= deltaTime;
                if (_lookOutDuration <= 0f)
                {
                    SetAnimation(false, false);
                    ScheduleLookOut();
                }
                return;
            }

            _lookOutTimer -= deltaTime;
            if (_lookOutTimer <= 0f)
            {
                _lookOutDuration = Mathf.Lerp(1.1f, 1.8f, Next01());
                SetAnimation(false, true);
            }
        }

        private void ScheduleLookOut()
        {
            _lookOutTimer = Mathf.Lerp(3.4f, 7.2f, Next01());
            _lookOutDuration = 0f;
        }

        private void SetAnimation(bool running, bool lookingOut)
        {
            if (_animator == null)
            {
                return;
            }

            _animator.SetBool(IsRunning, running);
            _animator.SetBool(IsLookingOut, lookingOut);
        }

        private float Next01()
        {
            _randomState ^= _randomState << 13;
            _randomState ^= _randomState >> 17;
            _randomState ^= _randomState << 5;
            return (_randomState & 0x00FFFFFFu) / 16777215f;
        }
    }
}
