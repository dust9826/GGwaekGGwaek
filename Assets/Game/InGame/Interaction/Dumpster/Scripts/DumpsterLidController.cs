using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class DumpsterLidController : MonoBehaviour
    {
        [SerializeField] private Transform _leftLid;
        [SerializeField] private Transform _rightLid;
        [SerializeField, Range(15f, 120f)] private float _openAngleDeg = 82f;
        [SerializeField, Min(10f)] private float _angularSpeedDegPerSecond = 150f;

        private float _angleDeg;
        private bool _isOpen;

        public bool IsOpen => _isOpen;
        public float OpenAmount => _openAngleDeg > 0f ? _angleDeg / _openAngleDeg : 0f;

        public void Configure(Transform leftLid, Transform rightLid)
        {
            _leftLid = leftLid;
            _rightLid = rightLid;
            ApplyPose();
        }

        public void Toggle() => SetOpen(!_isOpen);

        public void SetOpen(bool open) => _isOpen = open;

        private void Update()
        {
            float target = _isOpen ? _openAngleDeg : 0f;
            _angleDeg = Mathf.MoveTowards(
                _angleDeg,
                target,
                _angularSpeedDegPerSecond * Time.deltaTime);
            ApplyPose();
        }

        private void ApplyPose()
        {
            if (_leftLid != null) _leftLid.localRotation = Quaternion.Euler(_angleDeg, 0f, 0f);
            if (_rightLid != null) _rightLid.localRotation = Quaternion.Euler(_angleDeg, 0f, 0f);
        }
    }
}
