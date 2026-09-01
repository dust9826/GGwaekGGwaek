using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 주행 튜닝용 계측기. 손맛은 주관적이지만 "0 → 최고속 몇 초"는 아니다. 튜닝을 반복할 때
    /// 같은 조건을 재현하기 위한 장치이므로 체크인한다.
    ///
    /// <b>속도 문턱은 정규화 값이 아니라 m/s 다.</b> <see cref="VehicleController.CurrentSpeed01"/>
    /// 는 부스트 최고속을 1 로 잡으므로 부스트를 안 누르면 0.75 에서 천장을 친다 — 정규화 값으로
    /// 문턱을 걸면 기본 주행 측정이 영원히 안 걸린다.
    /// </summary>
    [RequireComponent(typeof(VehicleController))]
    public sealed class VehicleDriveProbe : MonoBehaviour
    {
        /// <summary>드리프트를 놓고 그립이 다시 물기까지 기다리는 시간(초). 이 사이의 슬립은
        /// 평소 주행이 아니라 드리프트의 꼬리라 평소 최대치에 넣으면 계측기가 거짓말을 한다.</summary>
        private const float DriftSettleTime = 0.5f;

        [SerializeField] private VehicleController _vehicle;
        [SerializeField] private Rigidbody _rigidbody;

        [Tooltip("최고속에 닿았다고 볼 평면 속력 (m/s). 기본 최고속의 95% 정도로 둔다 — " +
                 "마찰과 스텝 이산화 때문에 정확히 최고속에는 잘 안 닿는다.")]
        [SerializeField, Min(0.1f)] private float _topSpeed = 11.4f;
        [Tooltip("정지로 간주할 속력 (m/s).")]
        [SerializeField, Min(0.01f)] private float _stopSpeed = 0.1f;

        private float _accelStartTime = -1f;
        private float _coastStartTime = -1f;
        private Vector3 _restAnchor;
        private bool _hasRestAnchor;
        private float _maxDriftSlipSeen;
        private float _maxCruiseSlipSeen;
        private float _driftReleaseTime = -99f;
        private bool _wasAboveTopSpeed;

        private void Reset()
        {
            _vehicle = GetComponent<VehicleController>();
            _rigidbody = GetComponent<Rigidbody>();
        }

        private void FixedUpdate()
        {
            if (_vehicle == null || _rigidbody == null) return;

            float speed = new Vector3(_rigidbody.linearVelocity.x, 0f, _rigidbody.linearVelocity.z).magnitude;

            MeasureAcceleration(speed);
            MeasureCoast(speed);
            MeasureSlip(_vehicle.CurrentSpeed01);
            MeasureRestTurn(speed);
        }

        // 기준 1: 0 → 기본 최고속 0.6 ± 0.1 초
        private void MeasureAcceleration(float speed)
        {
            if (speed <= _stopSpeed)
            {
                _accelStartTime = Time.time;
                return;
            }

            if (_accelStartTime < 0f) return;

            if (speed >= _topSpeed)
            {
                Debug.Log($"[VehicleDriveProbe] 0 → 최고속 {Time.time - _accelStartTime:F2}s (기준 0.6 ± 0.1)");
                _accelStartTime = -1f;
            }
        }

        // 기준 2: 최고속에서 손 떼면 1.5 초 내 정지
        private void MeasureCoast(float speed)
        {
            if (speed >= _topSpeed)
            {
                _wasAboveTopSpeed = true;
                _coastStartTime = Time.time;
                return;
            }

            if (!_wasAboveTopSpeed || _coastStartTime < 0f) return;

            if (speed <= _stopSpeed)
            {
                Debug.Log($"[VehicleDriveProbe] 최고속 → 정지 {Time.time - _coastStartTime:F2}s (기준 1.5 이내)");
                _wasAboveTopSpeed = false;
                _coastStartTime = -1f;
            }
        }

        // 기준 3: 드리프트 중 고속 슬립각 30° 이상 / 기준 6: 저속에서 5° 미만
        // 기준 7: 드리프트를 안 눌렀으면 고속에서도 슬립이 거의 없어야 한다 — "그냥 커브"
        //
        // 슬립각 하나에 기준 둘이 걸리므로, 눌렀는지 아닌지로 갈라 재지 않으면 평소 주행이
        // 미끄럽다는 것과 드리프트가 잘 미끄러진다는 것을 계측기가 구별하지 못한다.
        private void MeasureSlip(float speed01)
        {
            float slip = _vehicle.SlipAngle;

            if (_vehicle.IsDrifting)
            {
                _driftReleaseTime = Time.time;
                if (speed01 >= 0.4f && slip > _maxDriftSlipSeen)
                {
                    _maxDriftSlipSeen = slip;
                    Debug.Log($"[VehicleDriveProbe] 드리프트 최대 슬립각 {slip:F1}° (기준 30 이상)");
                }
                return;
            }

            if (Time.time - _driftReleaseTime < DriftSettleTime) return;

            if (speed01 >= 0.4f && slip > _maxCruiseSlipSeen)
            {
                _maxCruiseSlipSeen = slip;
                Debug.Log($"[VehicleDriveProbe] 평소 주행 최대 슬립각 {slip:F1}° (기준 5 미만)");
            }

            if (speed01 > 0.05f && speed01 < 0.2f && slip > 5f)
            {
                Debug.LogWarning($"[VehicleDriveProbe] 저속에서 슬립각 {slip:F1}° — 기준 5 미만을 넘었다");
            }
        }

        // 기준 4: 정지 상태 조향 → 위치 이동 0.1 m 이내
        //
        // 정지 판정에 정규화 속도를 쓰면 안 된다. 관성의 꼬리가 회전이 민 것처럼 잡힌다.
        // 실제 속력으로 판정해야 재는 값이 "회전이 만든 이동"이 된다.
        private void MeasureRestTurn(float speed)
        {
            if (speed > _stopSpeed)
            {
                _hasRestAnchor = false;
                return;
            }

            if (!_hasRestAnchor)
            {
                _restAnchor = transform.position;
                _hasRestAnchor = true;
                return;
            }

            // 정지 판정이 평면 속력이므로 재는 것도 평면이어야 한다. 수직은 따로 찍어 원인을
            // 가른다 — 바닥에 정착하며 오르내리는 것과 회전이 미는 것은 다른 문제다.
            Vector3 delta = transform.position - _restAnchor;
            float planarDrift = new Vector3(delta.x, 0f, delta.z).magnitude;

            if (planarDrift > 0.1f)
            {
                Debug.LogWarning($"[VehicleDriveProbe] 제자리 회전이 평면으로 {planarDrift:F2} m 밀렸다 " +
                                 $"(수직 {delta.y:F2} m) — 기준 0.1 이내");
                _hasRestAnchor = false;
            }
        }
    }
}
