using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 물리 샌드박스용 펭귄 조작. 정확한 보행보다 "원하는 곳으로는 가지만 몸은 말을 덜 듣는"
    /// 조작감을 목표로 한다. 평소에는 약한 자세 복원 토크로 서고, 배밀이와 몸 던지기에서는
    /// Rigidbody 회전을 그대로 열어 충돌이 다음 웃긴 상황을 만든다.
    /// </summary>
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PenguinChaosController : MonoBehaviour
    {
        [Header("접점")]
        [Tooltip("이동 방향 기준. 보통 별도 추적 카메라다.")]
        [SerializeField] private Transform _cameraBasis;

        [Header("미끄러운 이동")]
        [SerializeField, Min(0f)] private float _groundAcceleration = 31f;
        [SerializeField, Min(0f)] private float _airAcceleration = 8f;
        [SerializeField, Min(0.1f)] private float _maxGroundSpeed = 8.5f;
        [SerializeField, Min(0.1f)] private float _maxSlideSpeed = 13f;
        [SerializeField, Min(0f)] private float _overspeedBrake = 5f;
        [SerializeField, Min(0f)] private float _driveForceHeight = 0.42f;
        [SerializeField, Min(0f)] private float _normalDamping = 0.45f;
        [SerializeField, Min(0f)] private float _slideDamping = 0.08f;

        [Header("흔들리는 자세")]
        [SerializeField, Min(0f)] private float _poseSpring = 34f;
        [SerializeField, Min(0f)] private float _poseDamping = 6.2f;
        [SerializeField, Range(0f, 89f)] private float _slidePitchDegrees = 68f;
        [SerializeField, Min(0f)] private float _flopSeconds = 1.65f;
        [SerializeField, Min(0f)] private float _flopSpin = 7.5f;

        [Header("점프와 공중 묘기")]
        [SerializeField, Min(0f)] private float _jumpVelocity = 6.8f;
        [SerializeField, Range(0, 3)] private int _extraAirJumps = 2;
        [SerializeField, Min(0f)] private float _airTrickTorque = 22f;

        [Header("박치기")]
        [SerializeField, Min(0f)] private float _headbuttVelocity = 6.5f;
        [SerializeField, Min(0f)] private float _headbuttLift = 1.35f;
        [SerializeField, Min(0f)] private float _headbuttSeconds = 0.34f;
        [SerializeField, Min(0f)] private float _propLaunchVelocity = 7.5f;
        [SerializeField, Min(0f)] private float _propLaunchLift = 3.2f;

        public float Speed { get; private set; }
        public bool IsGrounded { get; private set; }
        public bool IsSliding { get; private set; }
        public bool IsFlopped => Time.time < _flopUntil;
        public bool HeadbuttActive => Time.time < _headbuttUntil;

        private Rigidbody _body;
        private Vector2 _moveInput;
        private Vector3 _lastHeading = Vector3.forward;
        private float _lastGroundContactTime = float.NegativeInfinity;
        private float _flopUntil;
        private float _headbuttUntil;
        private int _airJumpsRemaining;
        private bool _jumpRequested;
        private bool _flopRequested;
        private bool _headbuttRequested;
        private bool _resetRequested;
        private bool _slideHeld;
        private bool _airTrickHeld;
        private Vector3 _spawnPosition;
        private Quaternion _spawnRotation;

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            _body.maxAngularVelocity = 24f;
            _body.centerOfMass = new Vector3(0f, 0.62f, 0.03f);

            _spawnPosition = transform.position;
            _spawnRotation = transform.rotation;
            _lastHeading = Flatten(transform.forward, Vector3.forward);
            _airJumpsRemaining = _extraAirJumps;
        }

        private void Update()
        {
            ReadInput();

            if (_resetRequested || transform.position.y < -12f)
            {
                ResetToSpawn();
            }
        }

        private void FixedUpdate()
        {
            IsGrounded = Time.time - _lastGroundContactTime < 0.12f;
            if (IsGrounded) _airJumpsRemaining = _extraAirJumps;

            if (_flopRequested)
            {
                if (IsFlopped) RecoverNow();
                else EnterFlop();
                _flopRequested = false;
            }

            if (_headbuttRequested)
            {
                StartHeadbutt();
                _headbuttRequested = false;
            }

            if (_jumpRequested)
            {
                TryJump();
                _jumpRequested = false;
            }

            Vector3 heading = CameraRelativeHeading(_moveInput);
            if (heading.sqrMagnitude > 0.0001f) _lastHeading = heading;

            IsSliding = _slideHeld && IsGrounded && !IsFlopped;
            _body.linearDamping = IsSliding ? _slideDamping : _normalDamping;

            ApplyDrive(heading);
            ApplyPose(heading);
            ApplyAirTrick();
            LimitHorizontalSpeed(IsSliding ? _maxSlideSpeed : _maxGroundSpeed);

            Vector3 horizontal = _body.linearVelocity;
            horizontal.y = 0f;
            Speed = horizontal.magnitude;
        }

        private void ReadInput()
        {
            Keyboard keyboard = Keyboard.current;
            Mouse mouse = Mouse.current;

            if (keyboard == null)
            {
                _moveInput = Vector2.zero;
                _slideHeld = false;
                _airTrickHeld = false;
                return;
            }

            float x = 0f;
            float y = 0f;
            if (keyboard.dKey.isPressed) x += 1f;
            if (keyboard.aKey.isPressed) x -= 1f;
            if (keyboard.wKey.isPressed) y += 1f;
            if (keyboard.sKey.isPressed) y -= 1f;
            _moveInput = Vector2.ClampMagnitude(new Vector2(x, y), 1f);

            _jumpRequested |= keyboard.spaceKey.wasPressedThisFrame;
            _flopRequested |= keyboard.qKey.wasPressedThisFrame;
            _resetRequested = keyboard.rKey.wasPressedThisFrame;
            _slideHeld = keyboard.leftShiftKey.isPressed;
            _airTrickHeld = mouse != null && mouse.rightButton.isPressed;
            _headbuttRequested |= mouse != null && mouse.leftButton.wasPressedThisFrame;
        }

        private void ApplyDrive(Vector3 heading)
        {
            if (heading.sqrMagnitude < 0.0001f || IsFlopped) return;

            float acceleration = IsGrounded ? _groundAcceleration : _airAcceleration;
            if (IsSliding) acceleration *= 1.35f;

            Vector3 forcePoint = _body.worldCenterOfMass + Vector3.up * _driveForceHeight;
            _body.AddForceAtPosition(heading * acceleration, forcePoint, ForceMode.Acceleration);
        }

        private void ApplyPose(Vector3 heading)
        {
            if (IsFlopped) return;

            Vector3 facing = heading.sqrMagnitude > 0.0001f ? heading : _lastHeading;
            Quaternion target = Quaternion.LookRotation(facing, Vector3.up);
            if (IsSliding) target *= Quaternion.Euler(_slidePitchDegrees, 0f, 0f);

            Quaternion error = target * Quaternion.Inverse(_body.rotation);
            error.ToAngleAxis(out float angleDegrees, out Vector3 axis);
            if (angleDegrees > 180f) angleDegrees -= 360f;
            if (!float.IsFinite(axis.x) || axis.sqrMagnitude < 0.0001f) return;

            float spring = IsGrounded ? _poseSpring : _poseSpring * 0.2f;
            float damping = IsGrounded ? _poseDamping : _poseDamping * 0.35f;
            Vector3 torque = axis.normalized * (angleDegrees * Mathf.Deg2Rad * spring)
                             - _body.angularVelocity * damping;
            _body.AddTorque(torque, ForceMode.Acceleration);
        }

        private void ApplyAirTrick()
        {
            if (!_airTrickHeld || IsGrounded) return;

            Transform basis = _cameraBasis != null ? _cameraBasis : transform;
            Vector3 right = Flatten(basis.right, transform.right);
            Vector3 forward = Flatten(basis.forward, transform.forward);
            Vector3 torque = right * (-_moveInput.y * _airTrickTorque)
                             + forward * (-_moveInput.x * _airTrickTorque);
            _body.AddTorque(torque, ForceMode.Acceleration);
        }

        private void LimitHorizontalSpeed(float maxSpeed)
        {
            Vector3 horizontal = _body.linearVelocity;
            horizontal.y = 0f;
            float speed = horizontal.magnitude;
            if (speed <= maxSpeed || speed < 0.001f) return;

            _body.AddForce(-horizontal.normalized * ((speed - maxSpeed) * _overspeedBrake),
                ForceMode.Acceleration);
        }

        private void TryJump()
        {
            bool canJump = IsGrounded || _airJumpsRemaining > 0;
            if (!canJump) return;

            if (!IsGrounded) _airJumpsRemaining--;
            if (IsFlopped) RecoverNow();

            Vector3 launch = Vector3.up * _jumpVelocity;
            Vector3 offCenter = _body.worldCenterOfMass + transform.forward * 0.22f;
            _body.AddForceAtPosition(launch, offCenter, ForceMode.VelocityChange);
            _body.AddTorque(transform.right * Random.Range(-1.2f, 1.2f), ForceMode.VelocityChange);
        }

        private void StartHeadbutt()
        {
            if (IsFlopped) return;

            Vector3 direction = _lastHeading.sqrMagnitude > 0.0001f ? _lastHeading : transform.forward;
            _headbuttUntil = Time.time + _headbuttSeconds;
            _body.AddForce(direction * _headbuttVelocity + Vector3.up * _headbuttLift,
                ForceMode.VelocityChange);
            _body.AddTorque(transform.right * -2.2f, ForceMode.VelocityChange);
        }

        private void EnterFlop()
        {
            _flopUntil = Time.time + _flopSeconds;
            _body.AddForce(Vector3.up * 1.25f + _lastHeading * 1.6f, ForceMode.VelocityChange);
            Vector3 chaos = new Vector3(
                Random.Range(-1f, 1f),
                Random.Range(-0.5f, 0.5f),
                Random.Range(-1f, 1f)).normalized;
            _body.AddTorque(chaos * _flopSpin, ForceMode.VelocityChange);
        }

        private void RecoverNow()
        {
            _flopUntil = 0f;
            _body.angularVelocity *= 0.4f;
        }

        private void ResetToSpawn()
        {
            _resetRequested = false;
            _flopUntil = 0f;
            _headbuttUntil = 0f;
            _body.linearVelocity = Vector3.zero;
            _body.angularVelocity = Vector3.zero;
            _body.position = _spawnPosition;
            _body.rotation = _spawnRotation;
            _lastHeading = Flatten(_spawnRotation * Vector3.forward, Vector3.forward);
            _body.PublishTransform();
        }

        private Vector3 CameraRelativeHeading(Vector2 input)
        {
            if (input.sqrMagnitude < 0.0001f) return Vector3.zero;

            Transform basis = _cameraBasis != null ? _cameraBasis : transform;
            Vector3 forward = Flatten(basis.forward, transform.forward);
            Vector3 right = Flatten(basis.right, transform.right);
            return (forward * input.y + right * input.x).normalized;
        }

        private static Vector3 Flatten(Vector3 value, Vector3 fallback)
        {
            value.y = 0f;
            if (value.sqrMagnitude > 0.0001f) return value.normalized;

            fallback.y = 0f;
            return fallback.sqrMagnitude > 0.0001f ? fallback.normalized : Vector3.forward;
        }

        private void OnCollisionEnter(Collision collision)
        {
            RegisterGroundContact(collision);

            if (!HeadbuttActive || collision.rigidbody == null) return;

            Vector3 away = collision.rigidbody.worldCenterOfMass - _body.worldCenterOfMass;
            away.y = 0f;
            if (away.sqrMagnitude < 0.0001f) away = _lastHeading;
            away.Normalize();

            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : collision.rigidbody.worldCenterOfMass;
            collision.rigidbody.AddForceAtPosition(
                away * _propLaunchVelocity + Vector3.up * _propLaunchLift,
                point,
                ForceMode.VelocityChange);
            collision.rigidbody.AddTorque(Random.onUnitSphere * 3.5f, ForceMode.VelocityChange);

            _body.AddForce(-away * 1.1f + Vector3.up * 0.35f, ForceMode.VelocityChange);
            _headbuttUntil = 0f;
        }

        private void OnCollisionStay(Collision collision) => RegisterGroundContact(collision);

        private void RegisterGroundContact(Collision collision)
        {
            for (int i = 0; i < collision.contactCount; i++)
            {
                if (Vector3.Dot(collision.GetContact(i).normal, Vector3.up) < 0.45f) continue;
                _lastGroundContactTime = Time.time;
                return;
            }
        }

        private void OnGUI()
        {
            const float width = 540f;
            GUI.Box(new Rect(20f, 20f, width, 178f), string.Empty);

            GUIStyle title = new GUIStyle(GUI.skin.label)
            {
                fontSize = 28,
                fontStyle = FontStyle.Bold,
                normal = { textColor = new Color(1f, 0.85f, 0.32f) }
            };
            GUIStyle body = new GUIStyle(GUI.skin.label)
            {
                fontSize = 18,
                normal = { textColor = Color.white }
            };

            string state = IsFlopped ? "FLOP MODE" : IsSliding ? "BELLY SLIDE" : IsGrounded ? "WOBBLE WALK" : "AIRBORNE";
            GUI.Label(new Rect(38f, 30f, width - 30f, 38f), "PENGUIN CHAOS PLAYGROUND", title);
            GUI.Label(new Rect(38f, 72f, width - 30f, 104f),
                "WASD  move     SPACE  triple jump     SHIFT  belly slide\n" +
                "LMB  headbutt     Q  flop / recover     RMB + WASD  air trick\n" +
                $"R  reset                          {state}  |  {Speed:0.0} m/s",
                body);
        }
    }
}
