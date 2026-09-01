using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 3인칭 카메라 궤도. <b>이 오브젝트의 회전만 바꾼다 — 캐릭터는 절대 건드리지 않는다.</b>
    /// 마우스로 카메라 각도만 도는 동안 펭귄은 이동 입력이 없으면 원래 향하던 방향을 그대로
    /// 유지해야 한다는 요구(놈 `PlayerCameraController`의 idle 케이스와 같은 이유)를 만족한다.
    ///
    /// Cinemachine 을 쓰지 않고 이 트랜스폼을 직접 돌린다 — Cinemachine 오브젝트 배선은
    /// 프리팹 계층을 요구해 에디터 세션이 필요하고, 이 스크립트만으로는 검증할 방법이 없다.
    /// 나중에 Cinemachine 리그로 바꾸더라도 이 스크립트의 자리(피벗 회전)는 그대로 필요하다.
    /// 카메라 자체는 이 피벗의 자식으로 두고 로컬 오프셋(거리)만 인스펙터에서 잡으면 된다.
    /// </summary>
    public sealed class PenguinCameraOrbit : MonoBehaviour
    {
        [SerializeField] private PenguinInputReader _input;

        [Tooltip("델타 1 당 요(좌우) 각도(도).")]
        [SerializeField] private float _yawSensitivity = 0.2f;
        [Tooltip("델타 1 당 피치(상하) 각도(도).")]
        [SerializeField] private float _pitchSensitivity = 0.2f;
        [SerializeField] private float _minPitch = -30f;
        [SerializeField] private float _maxPitch = 70f;
        [SerializeField] private float _initialPitch = 15f;

        [Tooltip("눈덩이에 붙어 있는 동안 유지할 세로 각도. 공 중심 높이를 따라가지 않아 크기가 바뀌어도 " +
                 "화면이 위아래로 흔들리지 않는다.")]
        [SerializeField, Range(-10f, 70f)] private float _snowballLockPitch = 20f;

        [Header("속도감 — 이 피벗의 자식 카메라에 적용")]
        [Tooltip("보간을 적용할 자식 카메라. 비면 속도 연출 전체가 꺼진다(마우스 궤도는 그대로 동작).")]
        [SerializeField] private Camera _camera;
        [Tooltip("속도를 읽을 대상. 비면 속도 연출이 꺼진다.")]
        [SerializeField] private PenguinLocomotion _locomotion;

        // 멈춰 있을 때의 거리. 얼굴이 보이는 프레이밍이 여기서 결정된다.
        // 측정값(fov 60): 1.8m은 가슴 위 클로즈업(몸 70%), 3.5m는 전신(36%).
        // 2.4m 가 그 사이의 상반신 샷으로, 얼굴이 읽히면서 주변도 보인다.
        [SerializeField] private float _distanceLow = 2.4f;
        [SerializeField] private float _distanceHigh = 6.5f;
        [SerializeField] private float _heightLow = 0f;
        [SerializeField] private float _heightHigh = 1f;
        [SerializeField] private float _fovLow = 60f;
        [SerializeField] private float _fovHigh = 78f;
        [Tooltip("빠를수록 진행 방향 앞을 본다(m). 화면 여백이 속도를 말해준다.")]
        [SerializeField] private float _lookaheadLow = 0f;
        [SerializeField] private float _lookaheadHigh = 3f;

        [Header("카메라 암")]
        [Tooltip("시야를 내릴수록 암이 짧아지는 정도. 0이면 길이가 고정된다.")]
        [SerializeField, Range(0f, 0.9f)] private float _armShortenWhenLow = 0.5f;

        [Tooltip("이 피치보다 아래로 내려갈 때부터 암이 줄기 시작한다. 평상 시선(약 0도)보다 " +
            "확실히 낮아야 한다 — 10도로 두었더니 가만히 서 있을 때도(피치 -2.8) 암이 " +
            "절반으로 줄어 의도치 않게 얼굴 클로즈업이 됐다.")]
        [SerializeField] private float _armShortenStartPitch = -12f;

        [Tooltip("피벗 위로 카메라가 지킬 최소 높이(m). 눈 아래가 보이는 것을 막는다.")]
        [SerializeField, Min(0f)] private float _minHeightAbovePivot = 0.3f;

        [Tooltip("암이 줄어들 수 있는 하한(m). 이보다 짧아지면 평귀 안으로 파고든다.")]
        [SerializeField, Min(0.1f)] private float _minArmDistance = 1.2f;

        [Tooltip("암 길이가 목표값을 따라가는 시간(초). 0이면 즉시 — 마우스를 따라 튀다.")]
        [SerializeField, Min(0f)] private float _armFollowSeconds = 0.35f;

        [Tooltip("가장 가까울 때 조준점을 이만큼 올린다(m, 피벗 기준). 피벗이 머리보다 위에 있어 음수다 — " +
            "측정값으로 눈이 피벗 기준 -0.59m에 있다.")]
        [SerializeField] private float _faceAimHeight = -0.59f;

        [Tooltip("가장 가까울 때 카메라 자체를 이만큼 내린다(m, 피벗 기준). 피벗이 머리보다 위라 " +
            "내리지 않으면 정수리만 찍힌다.")]
        [SerializeField] private float _zoomHeightOffset = -0.62f;

        [Tooltip("이 거리 이하면 완전히 당겨진 것으로 본다(m).")]
        [SerializeField, Min(0.1f)] private float _zoomNearDistance = 1.8f;

        [Tooltip("이 거리 이상이면 완전히 마른 것으로 본다(m). 손으로 정해야 한다 — " +
            "속도 거리 필드를 기준으로 쓰면 그 값을 바꿀 때 줄임 계수가 무너진다.")]
        [SerializeField, Min(0.2f)] private float _zoomFarDistance = 5.5f;
        [Tooltip("속도 변화가 카메라에 반영되는 시간(초). 없으면 킥 톱니마다 FOV 가 떤다.")]
        [SerializeField, Min(0f)] private float _speedTransitionTime = 0.4f;

        [Header("물리 루트 추적")]
        [Tooltip("루트 Rigidbody의 접촉 진동을 카메라 높이에 직접 복사하지 않도록 감쇠하는 시간(초).")]
        [SerializeField, Min(0f)] private float _verticalFollowSeconds = 0.08f;

        [Header("슬라이딩 자동 추적")]
        [Tooltip("슬라이딩 중 실제 이동 방향을 따라가는 시간(초). 차체가 옆을 봐도 진행 경로는 화면 중앙에 남는다.")]
        [SerializeField, Min(0.01f)] private float _slideFollowSeconds = 0.28f;
        [Tooltip("마우스로 만든 시점 편향이 진행 방향으로 돌아오는 시간(초).")]
        [SerializeField, Min(0.01f)] private float _slideLookBiasReturnSeconds = 1.2f;

        [Header("급선회 FOV 킥")]
        // 2026-08-22 Phase 5: PenguinLocomotion.LateralGripAccel(실제로 적용된 그립력의
        // 가속도)을 읽는다 — PenguinBodyMotion 의 기울기와 정확히 같은 소스다. 예전엔 v×ω
        // 추정치를 여기서 따로 계산했는데, 그립이 포화(드리프트)해도 v×ω는 계속 커질 수 있어
        // 카메라 킥과 몸통 기울기가 서로 다른 순간에 최대치를 찍을 수 있었다. 같은 값을 읽으면
        // 그 어긋남 자체가 구조적으로 없어진다.
        [Tooltip("이 원심 가속도(m/s²)부터 킥이 들어가기 시작한다.")]
        [SerializeField, Min(0f)] private float _turnKickStartLateralAccel = 3f;
        [Tooltip("킥이 최대가 되는 원심 가속도. 이 모델이 실제로 내는 값에 맞춘다 — CC 시절 실측 7.3 m/s²였으나 2026-08-22 Rigidbody 재작성으로 상한이 μ_lat×g(≈11.8)로 바뀌었다. 재측정 필요.")]
        [SerializeField, Min(0.1f)] private float _turnKickFullLateralAccel = 8f;
        [Tooltip("급선회 중 추가로 넓히는 FOV. 도는 순간을 화면이 알아채게 한다.")]
        [SerializeField, Range(0f, 20f)] private float _turnFovKick = 6f;

        private float _yaw;
        private float _pitch;
        private float _smoothedSpeed01;
        private float _turnKick01;
        private float _turnKickVelocity;

        /// <summary>현재 암 길이. 0이면 아직 초기화 전이라 다음 프레임에 목표값을 그대로 받는다 —
        /// 씨가 시작하자마자 길이가 기어 들어오는 것을 막는다.</summary>
        private float _armDistance;
        private float _armDistanceVelocity;
        private Vector3 _cameraRestLocalPosition;
        private float _baseFov;
        private bool _wasSlideFollowing;
        private float _slideYawBias;
        private float _slideYawBiasVelocity;
        private float _slideYawVelocity;
        private Vector3 _pivotRestLocalPosition;
        private float _smoothedPivotY;
        private float _pivotYVelocity;

        /// <summary>
        /// 비어 있지 않으면 마우스 궤도 입력 대신 이 대상을 계속 바라본다. 눈덩이에서 손을 떼면
        /// null로 돌아오고, 고정이 끝난 각도에서 마우스 조작이 이어진다.
        /// </summary>
        public Transform LookTarget { get; set; }

        private void Awake()
        {
            _pitch = _initialPitch;
            _yaw = transform.eulerAngles.y;
            _pivotRestLocalPosition = transform.localPosition;
            _smoothedPivotY = transform.position.y;

            if (_camera != null)
            {
                // 인스펙터에서 잡아 둔 거리·높이를 기준으로 삼는다. 코드가 절대값을 정하면
                // 프리팹에서 카메라를 옮겨도 반영이 안 된다.
                _cameraRestLocalPosition = _camera.transform.localPosition;
                _baseFov = _camera.fieldOfView;
            }
        }

        private void OnEnable()
        {
            Camera.onPreCull += HandleCameraPreCull;

            // 커서를 잠그지 않으면 OS 포인터가 화면 경계에 부딪히거나 게임 뷰 밖으로 나갈 때마다
            // 원시 델타가 튀어 카메라가 흔들린다 — Vehicle/Player 양쪽 AGENTS.md 에 기록된 문제다.
            //
            // 웹에서는 잠그지 않는다. 브라우저는 포인터 잠금을 실제 DOM 이벤트 핸들러 안에서만
            // 허용하는데, 유니티의 게임 루프는 requestAnimationFrame 이라 클릭한 프레임에 불러도
            // 핸들러 밖이다. itch 처럼 샌드박스 iframe 에 올리면 아예 차단되기도 한다.
            // 실패하면 NotAllowedError 가 캔버스에 빨간 배너로 뜼다. 시점 입력은 Look 액션의
            // 델타라 잠금 없이도 동작하므로 그냥 포기하는 편이 낫다.
#if !UNITY_WEBGL || UNITY_EDITOR
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
#endif

            // 켜지는 순간 킥은 0 에서 시작한다. 남겨두면 첫 프레임에 FOV 가 튄다.
            _turnKick01 = 0f;
            _turnKickVelocity = 0f;

            // 같은 이유로 암도 재상한다. 직전 길이를 들고 있으면 켜지자마자 그 길이에서 목표까지
            // 기어 들어온다.
            _armDistance = 0f;
            _armDistanceVelocity = 0f;
        }

        private void OnDisable()
        {
            Camera.onPreCull -= HandleCameraPreCull;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>
        /// 속도에 따라 <b>자식 카메라의 로컬 오프셋과 FOV</b>를 민다. 피벗의 회전은 건드리지
        /// 않는다 — 마우스 시점이 그대로 살아 있어야 하기 때문이다.
        ///
        /// <para><c>VehicleCamera</c> 를 그대로 옮기지 못한 지점이 여기다. 저쪽은 마우스를 읽지
        /// 않고 카메라의 <b>월드 위치를 직접 계산</b>하며 yaw 를 차량 heading 에 고정한다. 그걸
        /// 붙이면 펭귄의 마우스 궤도가 사라진다. 그래서 거리·높이·FOV·전방 여백만 떼어
        /// 자식 트랜스폼에 얹었다.</para>
        /// </summary>
        private void ApplySpeedFraming()
        {
            if (_camera == null || _locomotion == null) return;

            float dt = Time.deltaTime;
            float rate = _speedTransitionTime > 0f ? 1f - Mathf.Exp(-dt / _speedTransitionTime) : 1f;
            _smoothedSpeed01 = Mathf.Lerp(_smoothedSpeed01, _locomotion.CurrentSpeed01, rate);

            // 급선회 킥. 속도 연출만으로는 도는 순간이 화면에 안 잡힌다 — 선회 중에도 속력은
            // 거의 그대로라 FOV 가 움직이지 않기 때문이다.
            float lateralAccel = Mathf.Abs(_locomotion.LateralGripAccel);
            float turnTarget = Mathf.InverseLerp(_turnKickStartLateralAccel, _turnKickFullLateralAccel, lateralAccel);
            _turnKick01 = Mathf.SmoothDamp(_turnKick01, turnTarget, ref _turnKickVelocity,
                Mathf.Max(0.01f, _speedTransitionTime));

            float distance = Mathf.Lerp(_distanceLow, _distanceHigh, _smoothedSpeed01);
            float height = Mathf.Lerp(_heightLow, _heightHigh, _smoothedSpeed01);
            float lookahead = Mathf.Lerp(_lookaheadLow, _lookaheadHigh, _smoothedSpeed01);

            // rest 오프셋의 방향은 유지하고 거리만 속도로 민다.
            Vector3 rest = _cameraRestLocalPosition;
            float restDistance = new Vector2(rest.x, rest.z).magnitude;
            Vector3 back = restDistance > 0.001f
                ? new Vector3(rest.x, 0f, rest.z) / restDistance
                : Vector3.back;

            // 카메라 암은 시야를 내릴수록 짧아진다. 길게 둔 채로 내리면 카메라가 눈밭 아래로
            // 파고들어 지면 밑면이 보인다.
            // 설정값 자체를 부드럽게. 계단처럼 꺾이면 암이 줄기 시작하는 순간이 턴택 틀어진다.
            float low01 = 1f - Mathf.InverseLerp(_minPitch, _armShortenStartPitch, _pitch);
            low01 = low01 * low01 * (3f - 2f * low01);
            distance *= Mathf.Lerp(1f, 1f - _armShortenWhenLow, low01);

            // 그래도 모자라면 기하학적으로 잘라낸다. 카메라 높이는 암 길이에 대해 1차식이라
            // 반복 없이 한 번에 상한을 구할 수 있다.
            float armHeight = rest.y + height;
            Quaternion orbit = OrbitRotation();
            float risePerMeter = (orbit * back).y;
            if (risePerMeter < -0.001f)
            {
                float baseHeight = (orbit * Vector3.up).y * armHeight;
                float limit = (_minHeightAbovePivot - baseHeight) / risePerMeter;
                distance = Mathf.Min(distance, Mathf.Max(_minArmDistance, limit));
            }

            // 암은 목표 길이로 <b>따라간다</b>. 직접 대입하면 마우스 피치가 그대로 거리에 썧혀
            // 카메라가 튀고, 위의 기하학적 상한이 걸리는 순간에도 길이가 계단으로 뛰다.
            _armDistance = _armDistance > 0f
                ? Mathf.SmoothDamp(_armDistance, distance, ref _armDistanceVelocity,
                    Mathf.Max(0.0001f, _armFollowSeconds))
                : distance;

            // 줄임 정도를 먼저 구해서 카메라 높이와 조준점에 같이 쓴다. 둘이 같은 값을 보지 않으면
            // 카메라는 내려가는데 시선은 그대로여서 얼굴이 프레임 밖으로 밀린다.
            //
            // ⚠ 기준을 `_distanceHigh`로 잡으면 안 된다. 그 값이 같이 움직이는 순간
            // `InverseLerp`가 1을 돌려줘 zoom01이 0으로 고정되고, 얼굴 조준·높이가 통째로
            // 사라진다. 절대 거리로 재야 한다.
            float zoom01 = 1f - Mathf.InverseLerp(_zoomNearDistance, _zoomFarDistance, _armDistance);
            zoom01 = zoom01 * zoom01 * (3f - 2f * zoom01);

            _camera.transform.localPosition =
                back * _armDistance + Vector3.up * (armHeight + _zoomHeightOffset * zoom01);

            // ⚠ 전방 여백을 카메라 <b>위치</b>에 더하면 안 된다. 로컬 +Z 로 미는 것이라 뒤로 뺀
            // 거리를 그대로 상쇄한다 — 실측에서 거리 5.75 를 계산해 놓고 여백 2.25 가 깎아
            // 3.50(=rest 그대로)이 나왔다. 여백은 <b>보는 지점</b>을 앞으로 옮기는 것이므로
            // 카메라의 로컬 회전으로 준다.
            // 가까워질수록 얼굴을 본다. 멀리서는 지금처럼 몸 전체를 잡아야 진행 방향이 읽힌다.
            Vector3 aimTarget = Vector3.up * (rest.y + _faceAimHeight * zoom01)
                              + Vector3.forward * lookahead;

            Vector3 toTarget = aimTarget - _camera.transform.localPosition;
            _camera.transform.localRotation = toTarget.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(toTarget, Vector3.up)
                : Quaternion.identity;

            _camera.fieldOfView = Mathf.Lerp(_fovLow, _fovHigh, _smoothedSpeed01) + _turnFovKick * _turnKick01;
        }

        private void LateUpdate()
        {
            if (_input == null) return;

            StabilizePivotPosition(true);

            ApplySpeedFraming();

            if (LookTarget != null)
            {
                _wasSlideFollowing = false;
                Vector3 direction = LookTarget.position - transform.position;
                direction.y = 0f;
                if (direction.sqrMagnitude > 1e-6f)
                {
                    _yaw = Quaternion.LookRotation(direction.normalized, Vector3.up).eulerAngles.y;
                    _pitch = Mathf.Clamp(_snowballLockPitch, _minPitch, _maxPitch);
                    transform.rotation = OrbitRotation();
                }

                return;
            }

            var delta = _input.LookDelta;
            _pitch -= delta.y * _pitchSensitivity;
            _pitch = Mathf.Clamp(_pitch, _minPitch, _maxPitch);

            Vector3 velocityDir = _locomotion != null
                ? _locomotion.HorizontalVelocityDirection
                : Vector3.zero;
            bool followSlide = _locomotion != null
                               && _locomotion.UsesSlidingLocomotion
                               && velocityDir.sqrMagnitude > 0.0001f;

            if (followSlide)
            {
                float velocityYaw = Quaternion.LookRotation(velocityDir, Vector3.up).eulerAngles.y;
                if (!_wasSlideFollowing)
                {
                    // 진입 차이는 아래 SmoothDamp가 흡수한다. 그 차이를 마우스 편향으로 보존하면
                    // 자동 추적 시간이 아니라 편향 복귀 시간만큼 뒤늦게 따라가는 카메라가 된다.
                    _slideYawBias = 0f;
                    _slideYawBiasVelocity = 0f;
                    _slideYawVelocity = 0f;
                }

                _slideYawBias += delta.x * _yawSensitivity;
                _slideYawBias = Mathf.SmoothDampAngle(_slideYawBias, 0f,
                    ref _slideYawBiasVelocity, _slideLookBiasReturnSeconds);
                _yaw = Mathf.SmoothDampAngle(_yaw, velocityYaw + _slideYawBias,
                    ref _slideYawVelocity, _slideFollowSeconds);
            }
            else
            {
                _yaw += delta.x * _yawSensitivity;
                _slideYawVelocity = 0f;
            }

            _wasSlideFollowing = followSlide;

            transform.rotation = OrbitRotation();
        }

        private Quaternion OrbitRotation()
            => Quaternion.AngleAxis(_yaw, Vector3.up)
               * Quaternion.AngleAxis(_pitch, Vector3.right);

        private void HandleCameraPreCull(Camera renderedCamera)
        {
            if (renderedCamera != _camera || _locomotion == null) return;

            // Rigidbody 보간은 LateUpdate 뒤에도 부모 루트의 렌더 자세를 바꿀 수 있다. 실제
            // 렌더 직전에 한 번 더 월드 자세를 고정해 항상 자유로운 루트 회전이 카메라에 섞이지
            // 않게 한다.
            StabilizePivotPosition(false);
            transform.rotation = OrbitRotation();
        }

        private void StabilizePivotPosition(bool updateVerticalFollow)
        {
            if (transform.parent == null || _locomotion == null)
            {
                transform.localPosition = _pivotRestLocalPosition;
                _smoothedPivotY = transform.position.y;
                _pivotYVelocity = 0f;
                return;
            }

            Quaternion yawOnly = Quaternion.Euler(0f, _yaw, 0f);
            Vector3 followPosition = transform.parent.position;
            Vector3 target = followPosition + yawOnly * _pivotRestLocalPosition;
            if (updateVerticalFollow)
            {
                if (Mathf.Abs(target.y - _smoothedPivotY) > 2f)
                {
                    _smoothedPivotY = target.y;
                    _pivotYVelocity = 0f;
                }
                else
                {
                    _smoothedPivotY = Mathf.SmoothDamp(_smoothedPivotY, target.y,
                        ref _pivotYVelocity, _verticalFollowSeconds, Mathf.Infinity,
                        Time.deltaTime);
                }
            }

            transform.position = new Vector3(target.x, _smoothedPivotY, target.z);
        }
    }
}
