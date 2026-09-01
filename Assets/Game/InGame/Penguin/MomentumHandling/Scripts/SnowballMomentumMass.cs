using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 관성 실험 씬에서만 실제 반지름을 조작용 유효 질량으로 바꾼다. 눈 수확량을 나타내는
    /// SnowBallCarrier의 내부 질량은 건드리지 않고 Rigidbody 관성만 덮어쓴다.
    /// </summary>
    // SnowballGrowthStageTimer(50)가 제어 반지름을 확정한 뒤, SnowBallCarrier(100)가
    // 기존 SubmitPush를 적용하기 전에 실험용 유효 질량을 확정한다.
    [DefaultExecutionOrder(75)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody), typeof(SnowBallCarrier))]
    public sealed class SnowballMomentumMass : MonoBehaviour
    {
        private const float ReleaseCoastStopSpeedMps = 0.05f;

        [SerializeField, Min(0.01f)] private float _maximumMassKg =
            SnowballStageModel.DefaultMaximumHandlingMassKg;
        [Tooltip("설면 위 구름 저항 계수. 질량과 수직항력에 비례한다.")]
        [SerializeField, Min(0f)] private float _rollingResistanceCoefficient = 0.015f;
        [SerializeField, Min(0f)] private float _airDensityKgPerM3 = 1.225f;
        [SerializeField, Min(0f)] private float _dragCoefficient = 0.47f;
        [SerializeField, Min(0f)] private float _angularDamping = 0.05f;
        [Tooltip("구름 각속도 상한을 계산할 때 가정하는 최대 접선 속도.")]
        [SerializeField, Min(0.1f)] private float _maximumRollingSpeedMps = 12f;

        private Rigidbody _body;
        private SnowBallCarrier _carrier;
        private PenguinSnowball _owner;
        private PenguinMomentumHandling _handling;
        private float _appliedMassKg = -1f;
        private float _appliedRadiusM = -1f;
        private bool _wasBoundForPush;
        private bool _releaseCoasting;

        public float EffectiveMassKg => SnowballStageModel.GetEffectiveHandlingMassKg(
            _carrier == null ? SnowballStageModel.MinRadiusM : _carrier.RadiusM,
            _maximumMassKg);

        public float Load01 => _carrier == null
            ? 0f
            : SnowballStageModel.GetVolumeProgress01(_carrier.RadiusM);

        private void Awake()
        {
            CacheComponents();
            RefreshMass();
        }

        private void FixedUpdate()
        {
            RefreshMass();
            UpdateReleaseCoast();
            ApplyPushDirectionSteering(Time.fixedDeltaTime);
            ApplyResistance(Time.fixedDeltaTime);
        }

        public void Configure(float maximumMassKg,
            float rollingResistanceCoefficient = 0.015f,
            float dragCoefficient = 0.47f)
        {
            _maximumMassKg = Mathf.Max(0.01f, maximumMassKg);
            _rollingResistanceCoefficient = Mathf.Max(0f, rollingResistanceCoefficient);
            _dragCoefficient = Mathf.Max(0f, dragCoefficient);
            RefreshMass();
        }

        public void Bind(PenguinSnowball owner, PenguinMomentumHandling handling)
        {
            _owner = owner;
            _handling = handling;
            if (owner == null) return;

            _wasBoundForPush = true;
            _releaseCoasting = false;
        }

        public void RefreshMass()
        {
            CacheComponents();
            if (_body == null || _carrier == null) return;

            float massKg = EffectiveMassKg;
            float radiusM = _carrier.RadiusM;
            _body.linearDamping = 0f;
            _body.angularDamping = _angularDamping;
            _body.maxAngularVelocity = Mathf.Max(8f,
                _maximumRollingSpeedMps / Mathf.Max(0.01f, radiusM));
            _body.mass = massKg;

            if (Mathf.Abs(_appliedMassKg - massKg) < 0.0001f &&
                Mathf.Abs(_appliedRadiusM - radiusM) < 0.0001f) return;

            _body.ResetInertiaTensor();
            _appliedMassKg = massKg;
            _appliedRadiusM = radiusM;
        }

        private void ApplyResistance(float dt)
        {
            if (_body == null || _carrier == null || _body.isKinematic || dt <= 0f) return;

            Vector3 velocity = MotionVelocity();
            float speedMps = velocity.magnitude;
            if (speedMps <= 0.0001f) return;

            float radiusM = _carrier.RadiusM;
            float areaM2 = Mathf.PI * radiusM * radiusM;
            float dragForceN = 0.5f * _airDensityKgPerM3 * _dragCoefficient * areaM2 *
                               speedMps * speedMps;
            float rollingForceN = 0f;
            if (_carrier.HasSupport)
            {
                float normalAcceleration = Mathf.Abs(Vector3.Dot(
                    Physics.gravity, _carrier.SupportNormal.normalized));
                rollingForceN = _rollingResistanceCoefficient * _body.mass * normalAcceleration;
            }

            float requestedResistanceN = rollingForceN + dragForceN;
            bool heldCoast = _owner != null && _owner.Held == _carrier &&
                             !_owner.IsPushing && !_owner.IsMomentumBraking;
            if (_handling != null && (heldCoast || _releaseCoasting))
            {
                requestedResistanceN = Mathf.Max(requestedResistanceN,
                    _handling.SnowballCoastResistanceForceN(radiusM));
            }

            float stoppingForceN = speedMps * _body.mass / dt;
            float resistanceForceN = Mathf.Min(stoppingForceN, requestedResistanceN);
            if (resistanceForceN > 0f)
                _body.AddForce(-velocity.normalized * resistanceForceN, ForceMode.Force);
        }

        private void UpdateReleaseCoast()
        {
            if (_body == null || _carrier == null) return;

            bool stillHeld = _owner != null && _owner.Held == _carrier;
            if (_wasBoundForPush && !stillHeld)
            {
                _wasBoundForPush = false;
                _releaseCoasting = MotionVelocity().magnitude > ReleaseCoastStopSpeedMps;
            }

            if (!_releaseCoasting) return;
            if (_body.isKinematic || MotionVelocity().magnitude <= ReleaseCoastStopSpeedMps)
            {
                _releaseCoasting = false;
                _owner = null;
                _handling = null;
                return;
            }

            // SnowBallCarrier의 기존 평지 정지는 입력이 없으면 수평·회전 속도를 즉시 지운다.
            // 해제된 공이 자연 저항으로 충분히 느려질 때까지만 그 보정을 우회한다.
            _carrier.SubmitMomentumCoast();
        }

        private Vector3 MotionVelocity() => _carrier.HasSupport
            ? Vector3.ProjectOnPlane(_body.linearVelocity, _carrier.SupportNormal)
            : _body.linearVelocity;

        private void ApplyPushDirectionSteering(float dt)
        {
            if (_body == null || _carrier == null || _owner == null ||
                _handling == null || !_owner.IsPushing || _owner.IsMomentumBraking ||
                _body.isKinematic || dt <= 0f) return;

            Vector3 supportNormal = _carrier.HasSupport
                ? _carrier.SupportNormal
                : Vector3.up;
            if (supportNormal.sqrMagnitude < 0.0001f) supportNormal = Vector3.up;
            supportNormal.Normalize();

            Vector3 velocity = Vector3.ProjectOnPlane(_body.linearVelocity, supportNormal);
            float speedMps = velocity.magnitude;
            if (speedMps <= 0.05f) return;

            CapsuleCollider capsule = _owner.GetComponent<CapsuleCollider>();
            Vector3 pusherCenter = capsule != null
                ? _owner.transform.TransformPoint(capsule.center)
                : _owner.transform.position;
            Vector3 desiredDirection = Vector3.ProjectOnPlane(
                _body.worldCenterOfMass - pusherCenter, supportNormal);
            if (desiredDirection.sqrMagnitude < 0.0001f) return;
            desiredDirection.Normalize();

            PenguinLocomotion locomotion = _owner.GetComponent<PenguinLocomotion>();
            float walkSpeedMps = locomotion != null ? locomotion.WalkSpeedMps : 3.5f;
            float bodyRadiusM = capsule != null
                ? capsule.radius * Mathf.Max(
                    Mathf.Abs(_owner.transform.lossyScale.x),
                    Mathf.Abs(_owner.transform.lossyScale.z))
                : 0.4f;
            float orbitRadiusM = Mathf.Max(0.1f,
                _carrier.RadiusM + bodyRadiusM + 0.02f);
            float maximumTurnRad = walkSpeedMps / orbitRadiusM * dt;
            Vector3 steeredDirection = Vector3.RotateTowards(
                velocity / speedMps, desiredDirection, maximumTurnRad, 0f);
            Vector3 targetVelocity = steeredDirection * speedMps;
            Vector3 deltaVelocity = targetVelocity - velocity;
            if (deltaVelocity.sqrMagnitude <= 0.000001f) return;

            Vector3 requiredForceN = deltaVelocity * (_body.mass / dt);
            float availableForceN = _handling.SnowballSteerForceN(_carrier.RadiusM);
            _body.AddForce(Vector3.ClampMagnitude(requiredForceN, availableForceN),
                ForceMode.Force);
        }

        private void CacheComponents()
        {
            if (_body == null) _body = GetComponent<Rigidbody>();
            if (_carrier == null) _carrier = GetComponent<SnowBallCarrier>();
        }

#if UNITY_EDITOR
        private void OnValidate()
        {
            _maximumMassKg = Mathf.Max(0.01f, _maximumMassKg);
            _rollingResistanceCoefficient = Mathf.Max(0f, _rollingResistanceCoefficient);
            _airDensityKgPerM3 = Mathf.Max(0f, _airDensityKgPerM3);
            _dragCoefficient = Mathf.Max(0f, _dragCoefficient);
            _angularDamping = Mathf.Max(0f, _angularDamping);
            _maximumRollingSpeedMps = Mathf.Max(0.1f, _maximumRollingSpeedMps);
            if (!Application.isPlaying) RefreshMass();
        }
#endif
    }
}
