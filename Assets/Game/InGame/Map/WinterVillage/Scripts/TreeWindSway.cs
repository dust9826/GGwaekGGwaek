using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class TreeWindSway : MonoBehaviour
    {
        [SerializeField] private Transform _visualPivot;
        [SerializeField] private Vector2 _windDirection = new Vector2(0.85f, 0.35f);
        [SerializeField, Range(0f, 5f)] private float _ambientAngle = 1.65f;
        [SerializeField, Min(0.01f)] private float _windSpeed = 0.62f;
        [SerializeField, Range(0f, 1f)] private float _gustAmount = 0.38f;
        [SerializeField, Range(1f, 2.5f)] private float _visualWindMultiplier = 1.55f;
        [SerializeField, Range(0f, 1f)] private float _sharedGustResponse = 0.42f;
        [SerializeField, Range(0f, 0.5f)] private float _gustFlutterAmount = 0.16f;
        [SerializeField, Range(0.05f, 0.6f)] private float _motionSmoothTime = 0.16f;
        [SerializeField, Range(0f, 12f)] private float _collisionImpulse = 5.8f;
        [SerializeField, Range(0f, 20f)] private float _springStrength = 16f;
        [SerializeField, Range(0f, 20f)] private float _springDamping = 2.1f;
        [SerializeField, Range(1f, 12f)] private float _maximumAngle = 6.5f;

        [Header("Impact Response")]
        [SerializeField, Min(0f)] private float _minimumImpactSpeed = 1.25f;
        [SerializeField, Min(0.01f)] private float _fullImpactSpeed = 9f;
        [SerializeField, Range(1f, 3f)] private float _impactImpulseMultiplier = 1.7f;
        [SerializeField, Range(1f, 12f)] private float _impactKickAngle = 4.2f;
        [SerializeField, Range(1f, 16f)] private float _impactMaximumAngle = 10f;
        [SerializeField, Range(0.05f, 1f)] private float _impactCooldown = 0.35f;
        [SerializeField, Range(0.5f, 0.95f)] private float _canopyHeight = 0.78f;

        [Header("Impact Scale Spring")]
        [SerializeField, Range(0f, 0.15f)] private float _impactScaleKick = 0.08f;
        [SerializeField, Range(1f, 100f)] private float _scaleSpringStrength = 48f;
        [SerializeField, Range(0f, 20f)] private float _scaleSpringDamping = 5f;
        [SerializeField, Range(0f, 0.2f)] private float _maximumScaleOffset = 0.12f;

        private Quaternion _restRotation;
        private Vector3 _restScale;
        private Vector2 _ambientAngleState;
        private Vector2 _ambientVelocity;
        private Vector2 _collisionAngle;
        private Vector2 _collisionVelocity;
        private float _scaleOffset;
        private float _scaleVelocity;
        private double _windTime;
        private float _seed;
        private Vector3 _canopyPointInPivotSpace;
        private float _canopyRadius = 1f;
        private float _nextImpactTime;

        public Vector2 CurrentAngle { get; private set; }
        public float CurrentScaleMultiplier { get; private set; } = 1f;
        public float PeakImpactAngle { get; private set; }
        public float LastImpactStrength { get; private set; }
        public int ImpactCount { get; private set; }

        private void Awake()
        {
            if (_visualPivot == null)
            {
                _visualPivot = transform;
            }

            _restRotation = _visualPivot.localRotation;
            _restScale = _visualPivot.localScale;
            uint entityHash = unchecked((uint)GetEntityId().GetHashCode());
            _seed = (entityHash & 0x00FFFFFFu) / 16777215f;
            _windTime = _seed * 37.0;
            _windDirection = _windDirection.sqrMagnitude > 0.001f ? _windDirection.normalized : Vector2.right;
            CacheCanopyBounds();
        }

        private void LateUpdate()
        {
            if (_visualPivot == null)
            {
                return;
            }

            float deltaTime = Mathf.Min(Time.deltaTime, 0.05f);
            _windTime += deltaTime * _windSpeed;

            float phase = (float)_windTime;
            float forwardWave = Mathf.Sin(phase) + Mathf.Sin(phase * 2.17f + 1.3f) * 0.22f;
            float sideWave = Mathf.Cos(phase * 0.83f + _seed * 4.9f) * 0.22f
                + Mathf.Sin(phase * 1.41f + 2.4f) * 0.08f;
            float gustNoise = Mathf.Clamp01(Mathf.PerlinNoise(_seed * 31.73f, phase * 0.28f));
            float localGust = Mathf.Lerp(1f - _gustAmount, 1f + _gustAmount, gustNoise);
            float sharedGust = WinterWindGustVFX.SharedGustStrength;
            float gust = localGust * (1f + sharedGust * _sharedGustResponse);

            Vector2 windDirection = ResolveWindDirection();
            Vector2 sideDirection = new Vector2(-windDirection.y, windDirection.x);
            float flutter = (Mathf.Sin(phase * 4.2f + _seed * 8.1f) * 0.7f
                + Mathf.Sin(phase * 6.7f + 0.8f) * 0.3f)
                * (_gustFlutterAmount * sharedGust);
            Vector2 ambientTarget = (windDirection * (forwardWave + flutter) + sideDirection * sideWave)
                * (_ambientAngle * _visualWindMultiplier * gust);
            _ambientAngleState = Vector2.SmoothDamp(
                _ambientAngleState,
                ambientTarget,
                ref _ambientVelocity,
                Mathf.Max(_motionSmoothTime, 0.01f),
                float.PositiveInfinity,
                deltaTime);

            Vector2 acceleration = -_collisionAngle * _springStrength - _collisionVelocity * _springDamping;
            _collisionVelocity += acceleration * deltaTime;
            _collisionAngle += _collisionVelocity * deltaTime;
            _collisionAngle = Vector2.ClampMagnitude(_collisionAngle, _impactMaximumAngle);

            float scaleAcceleration = -_scaleOffset * _scaleSpringStrength
                - _scaleVelocity * _scaleSpringDamping;
            _scaleVelocity += scaleAcceleration * deltaTime;
            _scaleOffset += _scaleVelocity * deltaTime;
            _scaleOffset = Mathf.Clamp(_scaleOffset, -_maximumScaleOffset, _maximumScaleOffset);
            CurrentScaleMultiplier = 1f + _scaleOffset;

            CurrentAngle = Vector2.ClampMagnitude(
                _ambientAngleState + _collisionAngle,
                Mathf.Max(_maximumAngle, _impactMaximumAngle));
            if (Time.time < _nextImpactTime)
            {
                PeakImpactAngle = Mathf.Max(PeakImpactAngle, CurrentAngle.magnitude);
            }
            _visualPivot.localRotation = _restRotation * Quaternion.Euler(CurrentAngle.y, 0f, -CurrentAngle.x);
            _visualPivot.localScale = _restScale * CurrentScaleMultiplier;
        }

        private void CacheCanopyBounds()
        {
            Renderer[] renderers = _visualPivot.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length == 0)
            {
                _canopyPointInPivotSpace = Vector3.up * 2f;
                _canopyRadius = 1f;
                return;
            }

            Bounds bounds = renderers[0].bounds;
            for (int i = 1; i < renderers.Length; i++)
            {
                bounds.Encapsulate(renderers[i].bounds);
            }

            Vector3 canopyPoint = new Vector3(
                bounds.center.x,
                Mathf.Lerp(bounds.min.y, bounds.max.y, _canopyHeight),
                bounds.center.z);
            _canopyPointInPivotSpace = _visualPivot.InverseTransformPoint(canopyPoint);
            _canopyRadius = Mathf.Max(0.45f, Mathf.Max(bounds.extents.x, bounds.extents.z) * 0.72f);
        }

        private Vector2 ResolveWindDirection()
        {
            Vector2 worldDirection = WinterWindGustVFX.SharedWindDirection;
            if (worldDirection.sqrMagnitude < 0.001f)
            {
                return _windDirection;
            }

            Vector3 localDirection = transform.InverseTransformDirection(
                new Vector3(worldDirection.x, 0f, worldDirection.y));
            Vector2 resolved = new Vector2(localDirection.x, localDirection.z);
            return resolved.sqrMagnitude > 0.001f ? resolved.normalized : _windDirection;
        }

        private void OnDisable()
        {
            _ambientAngleState = Vector2.zero;
            _ambientVelocity = Vector2.zero;
            _collisionAngle = Vector2.zero;
            _collisionVelocity = Vector2.zero;
            _scaleOffset = 0f;
            _scaleVelocity = 0f;
            CurrentAngle = Vector2.zero;
            CurrentScaleMultiplier = 1f;
            if (_visualPivot != null)
            {
                _visualPivot.localRotation = _restRotation;
                _visualPivot.localScale = _restScale;
            }
        }

        private void OnTriggerEnter(Collider other)
        {
            if (other == null || other.transform.IsChildOf(transform) || other.attachedRigidbody == null)
            {
                return;
            }

            Vector3 closestPoint = other.ClosestPoint(transform.position);
            Vector3 away = transform.position - closestPoint;
            if (away.sqrMagnitude < 0.001f)
            {
                away = transform.position - other.transform.position;
            }

            float approachSpeed = Mathf.Max(0f, Vector3.Dot(other.attachedRigidbody.linearVelocity, away.normalized));
            float strength = Mathf.InverseLerp(0f, _fullImpactSpeed, approachSpeed);
            if (approachSpeed >= _minimumImpactSpeed && Time.time >= _nextImpactTime)
            {
                TriggerImpact(away, approachSpeed, closestPoint);
            }
            else
            {
                AddImpulse(away, Mathf.Lerp(0.12f, 0.35f, strength));
            }
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (collision == null || collision.contactCount == 0 || Time.time < _nextImpactTime)
            {
                return;
            }

            ContactPoint contact = collision.GetContact(0);
            if (Mathf.Abs(contact.normal.y) > 0.7f)
            {
                return;
            }

            float closingSpeed = Mathf.Abs(Vector3.Dot(collision.relativeVelocity, contact.normal));
            if (closingSpeed < _minimumImpactSpeed)
            {
                return;
            }

            Vector3 away = transform.position - collision.transform.position;
            away.y = 0f;
            if (away.sqrMagnitude < 0.001f)
            {
                away = -contact.normal;
                away.y = 0f;
            }

            TriggerImpact(away, closingSpeed, contact.point);
        }

        private void TriggerImpact(Vector3 away, float closingSpeed, Vector3 impactPosition)
        {
            float strength = Mathf.InverseLerp(_minimumImpactSpeed, _fullImpactSpeed, closingSpeed);
            _nextImpactTime = Time.time + _impactCooldown;
            LastImpactStrength = strength;
            PeakImpactAngle = 0f;
            ImpactCount++;
            AddImpactImpulse(away, strength);

            Vector3 canopyPosition = _visualPivot.TransformPoint(_canopyPointInPivotSpace);
            WinterTreeImpactFeedback.TryPlay(
                canopyPosition,
                impactPosition,
                _canopyRadius,
                away,
                strength);
        }

        public void AddImpulse(Vector3 worldDirection, float normalizedStrength = 1f)
        {
            Vector3 localDirection = transform.InverseTransformDirection(worldDirection);
            Vector2 direction = new Vector2(localDirection.x, localDirection.z);
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector2.right;
            }

            direction.Normalize();
            _collisionVelocity += direction * (_collisionImpulse * Mathf.Clamp01(normalizedStrength));
            _collisionVelocity = Vector2.ClampMagnitude(_collisionVelocity, _maximumAngle * 2f);
        }

        private void AddImpactImpulse(Vector3 worldDirection, float normalizedStrength)
        {
            Vector3 localDirection = transform.InverseTransformDirection(worldDirection);
            Vector2 direction = new Vector2(localDirection.x, localDirection.z);
            if (direction.sqrMagnitude < 0.001f)
            {
                direction = Vector2.right;
            }

            direction.Normalize();
            float strength = Mathf.Lerp(0.45f, 1f, Mathf.Clamp01(normalizedStrength));
            _collisionAngle += direction * (_impactKickAngle * strength);
            _collisionAngle = Vector2.ClampMagnitude(_collisionAngle, _impactMaximumAngle);
            _collisionVelocity += direction * (_collisionImpulse * _impactImpulseMultiplier * strength);
            _collisionVelocity = Vector2.ClampMagnitude(_collisionVelocity, _impactMaximumAngle * 2.2f);
            _scaleOffset += _impactScaleKick * strength;
            _scaleOffset = Mathf.Clamp(_scaleOffset, -_maximumScaleOffset, _maximumScaleOffset);
        }
    }
}
