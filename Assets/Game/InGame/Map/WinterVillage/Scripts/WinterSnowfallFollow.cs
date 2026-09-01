using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Keeps the atmospheric snowfall volume centered over the gameplay camera while particles
    /// remain in world space. This provides steady coverage across the map without filling the
    /// entire level with particles.
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class WinterSnowfallFollow : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField, Min(0f)] private float _height = 9f;
        [SerializeField] private float _forwardOffset = 4f;

        private void Awake()
        {
            ResolveTarget();
            FollowTarget();
        }

        private void LateUpdate()
        {
            ResolveTarget();
            FollowTarget();
        }

        private void ResolveTarget()
        {
            if (_target != null) return;

            Camera mainCamera = Camera.main;
            if (mainCamera != null) _target = mainCamera.transform;
        }

        private void FollowTarget()
        {
            if (_target == null) return;

            Vector3 forward = Vector3.ProjectOnPlane(_target.forward, Vector3.up);
            if (forward.sqrMagnitude < 0.001f) forward = Vector3.forward;
            else forward.Normalize();

            Vector3 position = _target.position + Vector3.up * _height + forward * _forwardOffset;
            transform.SetPositionAndRotation(position, Quaternion.identity);
        }
    }
}
