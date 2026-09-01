using UnityEngine;

namespace PPack
{
    [RequireComponent(typeof(Rigidbody), typeof(CapsuleCollider))]
    [DisallowMultipleComponent]
    public sealed class RollingBarrel : MonoBehaviour
    {
        [SerializeField, Min(1f)] private float _massKg = 48f;
        [SerializeField, Min(0f)] private float _linearDamping = 0.08f;
        [SerializeField, Min(0f)] private float _angularDamping = 0.035f;
        [SerializeField, Min(0f)] private float _maxAngularSpeedRadPerSecond = 35f;

        private Rigidbody _body;

        public Rigidbody Body => _body != null ? _body : GetComponent<Rigidbody>();

        private void Awake()
        {
            _body = GetComponent<Rigidbody>();
            ApplyPhysicsSettings();
        }

        private void Reset()
        {
            _body = GetComponent<Rigidbody>();
            ApplyPhysicsSettings();
        }

        public void LayDownAndRoll(Vector3 position, Vector3 direction, float speedMps)
        {
            Rigidbody body = Body;
            transform.SetPositionAndRotation(position, Quaternion.Euler(90f, 0f, 0f));
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.AddForce(direction.normalized * speedMps, ForceMode.VelocityChange);
        }

        private void ApplyPhysicsSettings()
        {
            if (_body == null) return;
            _body.mass = _massKg;
            _body.linearDamping = _linearDamping;
            _body.angularDamping = _angularDamping;
            _body.maxAngularVelocity = _maxAngularSpeedRadPerSecond;
            _body.interpolation = RigidbodyInterpolation.Interpolate;
            _body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
        }
    }
}
