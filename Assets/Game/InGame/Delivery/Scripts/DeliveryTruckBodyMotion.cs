using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차체 비주얼이 주행에 반응한다 — 가감속에 피치, 선회에 롤.
    ///
    /// <b><c>BodyPivot</c> 에 붙고 그 로컬 회전만 쓴다.</b> 스케일은 Feel(<c>MMF_Player</c>)이 자식
    /// <c>Body</c> 에서 쓰므로 여기서 건드리지 않는다 — 채널 하나에 주인 하나다
    /// (<c>../Vehicle/VehicleBodyMotion</c> 과 같은 규약).
    ///
    /// 순수 연출이다. 이 컴포넌트가 없어도, GPU 가 없어도 주행 자체는 똑같이 돈다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class DeliveryTruckBodyMotion : MonoBehaviour
    {
        [SerializeField] private DeliveryTruck _truck;

        [Header("피치 — 가속·제동")]
        [SerializeField, Min(0f)] private float _maxPitchDeg = 3f;
        [Tooltip("이 가속도에서 최대 피치가 된다. 트럭의 제동 감속도와 같은 값이 기준이다.")]
        [SerializeField, Min(0.01f)] private float _accelReferenceMps2 = 4f;

        [Header("롤 — 선회")]
        [SerializeField, Min(0f)] private float _maxRollDeg = 3.5f;
        [SerializeField, Min(1f)] private float _yawRateReferenceDegPerSecond = 70f;

        [SerializeField, Min(0.01f)] private float _responseSeconds = 0.12f;

        private float _pitchDeg;
        private float _pitchRate;
        private float _rollDeg;
        private float _rollRate;
        private float _previousYawDeg;

        /// <summary>지금 적용 중인 값. 검증이 읽는다.</summary>
        public float PitchDeg => _pitchDeg;

        /// <summary>지금 적용 중인 값. 검증이 읽는다.</summary>
        public float RollDeg => _rollDeg;

        private void Reset() => _truck = GetComponentInParent<DeliveryTruck>();

        private void Awake()
        {
            if (_truck == null) _truck = GetComponentInParent<DeliveryTruck>();
            if (_truck != null) _previousYawDeg = _truck.transform.eulerAngles.y;
        }

        // 트럭이 Update 에서 자기 트랜스폼을 쓰므로 그 뒤에 읽는다.
        private void LateUpdate()
        {
            if (_truck == null) return;
            float deltaSeconds = Time.deltaTime;
            if (deltaSeconds <= 0f) return;

            float yawDeg = _truck.transform.eulerAngles.y;
            float yawRate = Mathf.DeltaAngle(_previousYawDeg, yawDeg) / deltaSeconds;
            _previousYawDeg = yawDeg;

            // 제동(음수 가속)이면 앞으로 처박고, 가속이면 뒤로 젖힌다.
            float targetPitch = -Mathf.Clamp(_truck.CurrentAcceleration / _accelReferenceMps2, -1f, 1f) * _maxPitchDeg;
            // 오른쪽으로 돌면 차체는 바깥인 왼쪽으로 기운다.
            float targetRoll = Mathf.Clamp(yawRate / _yawRateReferenceDegPerSecond, -1f, 1f) * _maxRollDeg;

            _pitchDeg = Mathf.SmoothDamp(_pitchDeg, targetPitch, ref _pitchRate, _responseSeconds);
            _rollDeg = Mathf.SmoothDamp(_rollDeg, targetRoll, ref _rollRate, _responseSeconds);
            transform.localRotation = Quaternion.Euler(_pitchDeg, 0f, _rollDeg);
        }
    }
}
