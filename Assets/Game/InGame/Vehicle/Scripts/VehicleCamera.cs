using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차량 뒤에 상시 고정되는 3인칭 카메라. 마우스 입력을 읽지 않으며, yaw는 매 프레임 차량
    /// heading을 그대로 따른다. 속도에 비례해 높이/거리/피치/FOV/전방 여백을 보간하고,
    /// 수직 위치만 별도로 더 무겁게 감쇠해 점프·착지 출렁임을 줄인다.
    /// </summary>
    [RequireComponent(typeof(Camera))]
    public sealed class VehicleCamera : MonoBehaviour
    {
        [SerializeField] private Transform _vehicle;
        [SerializeField] private VehicleController _controller;

        [Header("저속/고속 보간")]
        [SerializeField] private float _heightLow = 1.5f;
        [SerializeField] private float _heightHigh = 3.5f;
        [SerializeField] private float _distanceLow = 4f;
        [SerializeField] private float _distanceHigh = 7f;
        [SerializeField] private float _pitchOffsetHigh = -6f;
        [SerializeField] private float _fovLow = 60f;
        [SerializeField] private float _fovHigh = 75f;
        [SerializeField] private float _lookaheadLow = 0f;
        [SerializeField] private float _lookaheadHigh = 3f;
        [Tooltip("속도 변화가 카메라 값에 반영되는 시간. 0.3~0.5초 범위에서 테스트.")]
        [SerializeField] private float _speedTransitionTime = 0.4f;

        [Header("드리프트")]
        [Tooltip("이 슬립각부터 킥이 들어가기 시작한다. 평소 주행 슬립각이 0°라 오검출은 없다.")]
        [SerializeField, Min(0f)] private float _driftSlipAngle = 15f;
        [Tooltip("킥이 최대가 되는 슬립각. 이 차가 실제로 내는 최대치에 맞춘다 — 실측 45°.")]
        [SerializeField, Min(1f)] private float _driftFullSlipAngle = 45f;
        [Tooltip("드리프트 중 추가로 넓히는 FOV. 미끄러지는 순간을 화면이 알아채게 한다.")]
        [SerializeField, Range(0f, 20f)] private float _driftFovKick = 6f;

        [Header("수직 위치 — 착지 출렁임 완화")]
        [SerializeField] private float _horizontalSmoothTime = 0.08f;
        [SerializeField] private float _verticalSmoothTime = 0.25f;

        private float _smoothedSpeed01;
        private float _speed01Velocity;
        private float _drift;
        private float _driftVelocity;
        private Vector3 _smoothedPosition;
        private Vector3 _positionVelocity;
        private Camera _camera;
        private bool _initialized;

        private void Awake()
        {
            _camera = GetComponent<Camera>();
        }

        private void OnEnable()
        {
            // 커서를 잠그지 않으면 OS 포인터가 화면 경계에 부딪히거나 게임 뷰 밖으로 나갈 때마다
            // 원시 델타가 튀어 카메라가 흔들린다 — Player/AGENTS.md에 기록된 것과 같은 문제.
            //
            // 웹은 제외한다. 브라우저는 포인터 잠금을 DOM 이벤트 핸들러 안에서만 허용하는데
            // 씬 로드는 그 안이 아니라 NotAllowedError 가 캔버스에 빨간 배너로 뜼다.
#if !UNITY_WEBGL || UNITY_EDITOR
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
#endif

            // 켜지는 순간 드리프트 킥은 0 에서 시작한다. 남겨두면 첫 프레임에 FOV 가 튄다.
            _drift = 0f;
            _driftVelocity = 0f;
        }

        private void OnDisable()
        {
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void LateUpdate()
        {
            if (_vehicle == null || _controller == null) return;

            if (!_initialized)
            {
                _smoothedPosition = ComputeDesiredPosition(_controller.CurrentSpeed01);
                _initialized = true;
            }

            float targetSpeed01 = _controller.CurrentSpeed01;
            float smoothingRate = _speedTransitionTime > 0f ? 1f - Mathf.Exp(-Time.deltaTime / _speedTransitionTime) : 1f;
            _smoothedSpeed01 = Mathf.Lerp(_smoothedSpeed01, targetSpeed01, smoothingRate);

            // 드리프트 킥. 속도 연출만으로는 미끄러지기 시작한 순간이 화면에 안 잡힌다 —
            // 총 속력은 드리프트 중에도 최고속 근처라 FOV 가 거의 안 움직이기 때문이다.
            // 끝점은 둥근 숫자가 아니라 이 차가 실제로 내는 최대 슬립각(45°)이다. 전에 한 번
            // 30°→45° 로 옮긴 적이 있는데, 모델이 안 내는 값까지 범위를 잡으면 가장 격한
            // 순간에 효과가 절반만 뜬다.
            float targetDrift = Mathf.InverseLerp(_driftSlipAngle, _driftFullSlipAngle, _controller.SlipAngle);
            _drift = Mathf.SmoothDamp(_drift, targetDrift, ref _driftVelocity, _speedTransitionTime);

            Vector3 desiredPosition = ComputeDesiredPosition(_smoothedSpeed01);

            float smoothedX = Mathf.SmoothDamp(_smoothedPosition.x, desiredPosition.x, ref _positionVelocity.x, _horizontalSmoothTime);
            float smoothedZ = Mathf.SmoothDamp(_smoothedPosition.z, desiredPosition.z, ref _positionVelocity.z, _horizontalSmoothTime);
            float smoothedY = Mathf.SmoothDamp(_smoothedPosition.y, desiredPosition.y, ref _positionVelocity.y, _verticalSmoothTime);
            _smoothedPosition = new Vector3(smoothedX, smoothedY, smoothedZ);

            transform.position = _smoothedPosition;
            transform.LookAt(LookTarget(_smoothedSpeed01));

            _camera.fieldOfView = Mathf.Lerp(_fovLow, _fovHigh, _smoothedSpeed01) + _driftFovKick * _drift;
        }

        private Vector3 Pivot()
        {
            return _vehicle.position + Vector3.up * 1f;
        }

        private Vector3 LookTarget(float speed01)
        {
            float lookahead = Mathf.Lerp(_lookaheadLow, _lookaheadHigh, speed01);
            return Pivot() + _vehicle.forward * lookahead;
        }

        private Vector3 ComputeDesiredPosition(float speed01)
        {
            float distance = Mathf.Lerp(_distanceLow, _distanceHigh, speed01);
            float height = Mathf.Lerp(_heightLow, _heightHigh, speed01);
            float pitch = Mathf.Lerp(0f, _pitchOffsetHigh, speed01);

            Quaternion orbitRotation = Quaternion.Euler(pitch, _vehicle.eulerAngles.y, 0f);
            Vector3 orbitOffset = orbitRotation * new Vector3(0f, 0f, -distance);

            return Pivot() + orbitOffset + Vector3.up * height;
        }
    }
}
