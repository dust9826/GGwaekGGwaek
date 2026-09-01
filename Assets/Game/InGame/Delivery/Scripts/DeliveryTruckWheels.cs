using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 바퀴가 실제 속도만큼 구르고, 앞바퀴는 선회하는 만큼 꺾인다. 순수 연출이라 주행 판정에
    /// 아무 영향이 없다 — 값을 읽기만 한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeliveryTruckWheels : MonoBehaviour
    {
        /// <summary>원통 메시의 축은 로컬 Y 다. 이만큼 굴려야 축이 좌우(X)를 향한다.</summary>
        private const float AxleRollDeg = 90f;

        [SerializeField] private DeliveryTruck _truck;
        [SerializeField] private Transform[] _frontWheels;
        [SerializeField] private Transform[] _rearWheels;
        [SerializeField, Min(0.05f)] private float _wheelRadius = 0.35f;
        [SerializeField, Min(0f)] private float _maxSteerDeg = 22f;
        [SerializeField, Min(1f)] private float _yawRateReferenceDegPerSecond = 70f;
        [SerializeField, Min(0.01f)] private float _steerResponseSeconds = 0.1f;

        private float _spinDeg;
        private float _steerDeg;
        private float _steerRate;
        private float _previousYawDeg;

        public void Configure(DeliveryTruck truck, Transform[] frontWheels, Transform[] rearWheels)
        {
            _truck = truck;
            _frontWheels = frontWheels;
            _rearWheels = rearWheels;
        }

        private void Awake()
        {
            if (_truck == null) _truck = GetComponentInParent<DeliveryTruck>();
            if (_truck != null) _previousYawDeg = _truck.transform.eulerAngles.y;
        }

        private void LateUpdate()
        {
            if (_truck == null) return;
            float deltaSeconds = Time.deltaTime;
            if (deltaSeconds <= 0f) return;

            float yawDeg = _truck.transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_previousYawDeg, yawDeg) / deltaSeconds;
            _previousYawDeg = yawDeg;

            _spinDeg += _truck.CurrentSpeed / _wheelRadius * Mathf.Rad2Deg * deltaSeconds;
            float targetSteer = Mathf.Clamp(yawRate / _yawRateReferenceDegPerSecond, -1f, 1f) * _maxSteerDeg;
            _steerDeg = Mathf.SmoothDamp(_steerDeg, targetSteer, ref _steerRate, _steerResponseSeconds);

            Quaternion roll = Quaternion.AngleAxis(AxleRollDeg, Vector3.forward)
                              * Quaternion.AngleAxis(_spinDeg, Vector3.up);
            Apply(_rearWheels, roll);
            Apply(_frontWheels, Quaternion.AngleAxis(_steerDeg, Vector3.up) * roll);
        }

        private static void Apply(Transform[] wheels, Quaternion rotation)
        {
            if (wheels == null) return;
            foreach (Transform wheel in wheels)
            {
                if (wheel != null) wheel.localRotation = rotation;
            }
        }
    }
}
