using System;
using System.Collections;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Rigidbody))]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class AmbientTrafficVehicle : MonoBehaviour
    {
        private const float JunctionTurnDistanceM = 3f;

        [SerializeField, Min(0.1f)] private float _cruiseSpeedMps = 6f;
        [SerializeField, Min(0.1f)] private float _accelerationMps2 = 3f;
        [SerializeField, Min(0.1f)] private float _brakingMps2 = 9f;
        [SerializeField, Min(0f)] private float _roadSurfaceY = 0.31f;
        [SerializeField, Min(0f)] private float _playerImpactImpulseNs = 180f;
        [SerializeField, Range(0f, 1f)] private float _impactLiftRatio = 0.2f;

        private AmbientTrafficWorld _world;
        private AmbientTrafficSpawner _spawner;
        private TrafficRoute _route;
        private Rigidbody _rigidbody;
        private BoxCollider _collider;
        private int _routeIndex;
        private float _laneDistance;
        private float _currentSpeedMps;
        private float _trafficTargetSpeedMps;
        private bool _waitingForJunction;
        private Coroutine _enableCollisionRoutine;

        public int VehicleId { get; private set; }
        public TrafficRoute Route => _route;
        public TrafficLane CurrentLane => _route == null ? null : _route.Lanes[_routeIndex];
        public TrafficLane NextLane => _route != null && _routeIndex + 1 < _route.Lanes.Count
            ? _route.Lanes[_routeIndex + 1]
            : null;
        public float LaneDistance => _laneDistance;
        public float CruiseSpeedMps => _cruiseSpeedMps;
        public float CurrentSpeedMps => _currentSpeedMps;
        public float TrafficTargetSpeedMps => _trafficTargetSpeedMps;
        public float BrakingMps2 => _brakingMps2;
        public float HalfLength => _collider == null ? 0f : _collider.size.z * 0.5f;
        public float JunctionWaitSeconds { get; private set; }
        public float TotalDistanceTravelled { get; private set; }
        public int PlayerImpactCount { get; private set; }

        private void Awake()
        {
            _rigidbody = GetComponent<Rigidbody>();
            _collider = GetComponent<BoxCollider>();
            _rigidbody.isKinematic = true;
            _rigidbody.interpolation = RigidbodyInterpolation.Interpolate;
            // 차량은 한 포털에서 회수된 뒤 다른 포털로 순간 이동한다. Speculative CCD는
            // 비활성 중의 그 이동까지 속도로 추정해 맵 전체에 가짜 접촉을 만들 수 있다.
            // 7m/s에서 한 스텝 이동은 최대 0.14m라 Discrete로도 실제 접촉은 충분히 잡힌다.
            _rigidbody.collisionDetectionMode = CollisionDetectionMode.Discrete;
        }

        public void Initialize(AmbientTrafficWorld world, AmbientTrafficSpawner spawner,
            TrafficRoute route, float speedMps, int vehicleId, float roadSurfaceY)
        {
            if (world == null) throw new ArgumentNullException(nameof(world));
            if (route == null || route.Lanes.Count == 0) throw new ArgumentException(nameof(route));
            _world = world;
            _spawner = spawner;
            _route = route;
            _cruiseSpeedMps = Mathf.Max(0.1f, speedMps);
            _roadSurfaceY = Mathf.Max(0f, roadSurfaceY);
            VehicleId = vehicleId;
            _routeIndex = 0;
            _laneDistance = 0f;
            _currentSpeedMps = _cruiseSpeedMps;
            _trafficTargetSpeedMps = _cruiseSpeedMps;
            JunctionWaitSeconds = 0f;
            TotalDistanceTravelled = 0f;
            _collider.enabled = false;
            ApplyPose(EvaluateRoutePose(), immediate: true);
            _world.Register(this);
            if (isActiveAndEnabled) BeginCollisionDelay();
        }

        private void FixedUpdate()
        {
            if (_route == null || _rigidbody == null || _world == null) return;

            JunctionWaitSeconds = _waitingForJunction
                ? JunctionWaitSeconds + Time.fixedDeltaTime
                : 0f;
            float rate = _trafficTargetSpeedMps < _currentSpeedMps
                ? _brakingMps2
                : _accelerationMps2;
            _currentSpeedMps = Mathf.MoveTowards(_currentSpeedMps, _trafficTargetSpeedMps,
                rate * Time.fixedDeltaTime);
            float movement = _currentSpeedMps * Time.fixedDeltaTime;
            _laneDistance += movement;
            TotalDistanceTravelled += movement;

            while (_laneDistance >= CurrentLane.Length)
            {
                float carry = _laneDistance - CurrentLane.Length;
                if (_routeIndex + 1 >= _route.Lanes.Count)
                {
                    _spawner?.Release(this);
                    return;
                }
                _routeIndex++;
                _laneDistance = Mathf.Min(carry, CurrentLane.Length);
            }

            ApplyPose(EvaluateRoutePose(), immediate: false);
        }

        internal void SetTrafficTargetSpeed(float speedMps)
            => _trafficTargetSpeedMps = Mathf.Clamp(speedMps, 0f, _cruiseSpeedMps);

        internal void SetWaitingForJunction(bool waiting)
            => _waitingForJunction = waiting;

        private TrafficLanePose EvaluateRoutePose()
        {
            TrafficLane current = CurrentLane;
            if (NextLane != null)
            {
                float turn = TurnDistance(current, NextLane);
                float remaining = current.Length - _laneDistance;
                if (remaining < turn)
                    return EvaluateJunction(current, NextLane,
                        0.5f - remaining / turn * 0.5f, turn);
            }

            if (_routeIndex > 0)
            {
                TrafficLane previous = _route.Lanes[_routeIndex - 1];
                float turn = TurnDistance(previous, current);
                if (_laneDistance < turn)
                    return EvaluateJunction(previous, current,
                        0.5f + _laneDistance / turn * 0.5f, turn);
            }

            return current.Evaluate(_laneDistance);
        }

        private static float TurnDistance(TrafficLane before, TrafficLane after)
            => Mathf.Min(JunctionTurnDistanceM, before.Length * 0.25f, after.Length * 0.25f);

        private static TrafficLanePose EvaluateJunction(TrafficLane before, TrafficLane after,
            float t, float turnDistance)
        {
            Vector3 start = before.Evaluate(before.Length - turnDistance).Position;
            Vector3 control = before.To.Position;
            Vector3 end = after.Evaluate(turnDistance).Position;
            float u = 1f - t;
            Vector3 position = u * u * start + 2f * u * t * control + t * t * end;
            Vector3 derivative = 2f * u * (control - start) + 2f * t * (end - control);
            return new TrafficLanePose(position, derivative.normalized);
        }

        private void ApplyPose(TrafficLanePose pose, bool immediate)
        {
            Vector3 position = pose.Position;
            position.y += _roadSurfaceY;
            Quaternion rotation = Quaternion.LookRotation(pose.Forward, Vector3.up);
            if (immediate)
            {
                _rigidbody.position = position;
                _rigidbody.rotation = rotation;
                _rigidbody.PublishTransform();
                return;
            }

            _rigidbody.MovePosition(position);
            _rigidbody.MoveRotation(rotation);
        }

        private void OnEnable()
        {
            if (_route != null) BeginCollisionDelay();
        }

        private void BeginCollisionDelay()
        {
            if (_enableCollisionRoutine != null) StopCoroutine(_enableCollisionRoutine);
            _enableCollisionRoutine = StartCoroutine(EnableCollisionAfterSpawn());
        }

        private IEnumerator EnableCollisionAfterSpawn()
        {
            yield return new WaitForFixedUpdate();
            yield return null;
            if (_collider != null) _collider.enabled = true;
            _enableCollisionRoutine = null;
        }

        private void OnDisable()
        {
            if (_enableCollisionRoutine != null)
            {
                StopCoroutine(_enableCollisionRoutine);
                _enableCollisionRoutine = null;
            }
            if (_world != null) _world.Unregister(this);
        }

        private void OnCollisionEnter(Collision collision)
        {
            Rigidbody otherBody = collision.rigidbody;
            if (otherBody == null || otherBody.GetComponent<PenguinLocomotion>() == null) return;
            if (IsPenguinLandingOnRoof(collision, otherBody)) return;

            Vector3 horizontalForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up).normalized;
            Vector3 direction = (horizontalForward + Vector3.up * _impactLiftRatio).normalized;
            float speedScale = Mathf.InverseLerp(1f, Mathf.Max(1.01f, _cruiseSpeedMps),
                _currentSpeedMps);
            Vector3 impulse = direction * (_playerImpactImpulseNs * Mathf.Lerp(0.4f, 1f, speedScale));
            Vector3 point = collision.contactCount > 0
                ? collision.GetContact(0).point
                : otherBody.worldCenterOfMass;

            // 회전 제약을 먼저 풀어야 같은 임펄스가 공중 회전까지 만든다. 충돌 콜백 뒤에
            // 알리면 PhysX가 이미 잠긴 축의 각운동량을 버린 다음이라 늦다.
            PenguinImpactRelay impactRelay = otherBody.GetComponent<PenguinImpactRelay>();
            if (impactRelay != null) impactRelay.ReceiveExternalImpulse(impulse, point);
            otherBody.AddForceAtPosition(impulse, point, ForceMode.Impulse);
            PlayerImpactCount++;
        }

        private bool IsPenguinLandingOnRoof(Collision collision, Rigidbody penguinBody)
        {
            if (_collider == null) return false;

            Bounds vehicleBounds = _collider.bounds;
            float roofBandBottom = vehicleBounds.max.y - Mathf.Min(0.2f, vehicleBounds.extents.y);
            for (int index = 0; index < collision.contactCount; index++)
            {
                ContactPoint contact = collision.GetContact(index);
                if (contact.point.y < roofBandBottom) continue;
                if (penguinBody.worldCenterOfMass.y <= contact.point.y) continue;
                if (Mathf.Abs(contact.normal.y) < 0.65f) continue;

                return true;
            }

            return false;
        }
    }
}
