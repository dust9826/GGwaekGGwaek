using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 물리 펭귄이 굴러도 수평을 유지하는 3인칭 카메라. 카메라까지 같이 넘어지면 병맛이 아니라
    /// 멀미가 되므로, 위치만 따라가고 요/피치는 월드 기준으로 따로 계산한다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class PenguinChaosCamera : MonoBehaviour
    {
        [SerializeField] private Transform _target;
        [SerializeField, Min(0.1f)] private float _distance = 7.5f;
        [SerializeField, Min(0f)] private float _targetHeight = 1.1f;
        [SerializeField, Min(0f)] private float _positionSmoothTime = 0.08f;
        [SerializeField] private float _yawSensitivity = 0.18f;
        [SerializeField] private float _pitchSensitivity = 0.15f;
        [SerializeField] private float _initialPitch = 18f;
        [SerializeField] private float _minPitch = -8f;
        [SerializeField] private float _maxPitch = 62f;
        [SerializeField] private LayerMask _collisionMask = -1;

        private float _yaw;
        private float _pitch;
        private Vector3 _velocity;
        private readonly RaycastHit[] _collisionHits = new RaycastHit[12];

        private void Awake()
        {
            _yaw = _target != null ? _target.eulerAngles.y : transform.eulerAngles.y;
            _pitch = _initialPitch;
        }

        private void OnEnable()
        {
            if (!Application.isPlaying) return;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
        }

        private void OnDisable()
        {
            if (!Application.isPlaying) return;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (_target == null) return;

            Mouse mouse = Mouse.current;
            if (mouse != null && Cursor.lockState == CursorLockMode.Locked)
            {
                Vector2 delta = mouse.delta.ReadValue();
                _yaw += delta.x * _yawSensitivity;
                _pitch = Mathf.Clamp(_pitch - delta.y * _pitchSensitivity, _minPitch, _maxPitch);
            }

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.escapeKey.wasPressedThisFrame)
            {
                Cursor.lockState = CursorLockMode.None;
                Cursor.visible = true;
            }
            else if (mouse != null && mouse.leftButton.wasPressedThisFrame && Cursor.lockState != CursorLockMode.Locked)
            {
                Cursor.lockState = CursorLockMode.Locked;
                Cursor.visible = false;
            }

            Vector3 pivot = _target.position + Vector3.up * _targetHeight;
            Quaternion orbit = Quaternion.Euler(_pitch, _yaw, 0f);
            Vector3 desired = pivot + orbit * new Vector3(0f, 0f, -_distance);

            Vector3 ray = desired - pivot;
            float rayLength = ray.magnitude;
            if (rayLength > 0.001f)
            {
                Vector3 direction = ray / rayLength;
                int count = Physics.SphereCastNonAlloc(
                    pivot, 0.24f, direction, _collisionHits, rayLength,
                    _collisionMask, QueryTriggerInteraction.Ignore);
                float nearest = rayLength;

                for (int i = 0; i < count; i++)
                {
                    Collider hitCollider = _collisionHits[i].collider;
                    if (hitCollider == null || hitCollider.transform == _target ||
                        hitCollider.transform.IsChildOf(_target))
                    {
                        continue;
                    }

                    nearest = Mathf.Min(nearest, _collisionHits[i].distance);
                }

                if (nearest < rayLength)
                    desired = pivot + direction * Mathf.Max(0.6f, nearest - 0.18f);
            }

            transform.position = Vector3.SmoothDamp(
                transform.position, desired, ref _velocity, _positionSmoothTime);
            transform.rotation = Quaternion.LookRotation(pivot - transform.position, Vector3.up);
        }
    }
}
