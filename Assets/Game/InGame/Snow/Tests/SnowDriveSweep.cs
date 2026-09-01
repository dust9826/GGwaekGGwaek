using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 차를 정해진 원 궤도로 몰아 제설 자국을 재현 가능하게 만든다.
    ///
    /// 먼지에서 <c>DustPadSweep</c> 을 만든 것과 같은 이유다 — 판정 기준이 <b>차이</b>(처음 지나는 눈 vs
    /// 이미 치운 길)이고, 손으로 몬 주행은 두 조건을 같게 재현하지 못한다. 한 바퀴는 눈을 밀고
    /// 두 바퀴째는 같은 자리를 다시 지나므로 한 번의 실행으로 두 값을 얻는다.
    ///
    /// 입력 합성(<c>VehicleDriveAutopilot</c>)을 쓰지 않는 이유는 그것이 <b>OS 포커스를 요구</b>하기
    /// 때문이다 — 유니티가 최상위가 아니면 입력 장치가 리셋돼 합성 입력이 버려진다(루트 <c>AGENTS.md</c>).
    ///
    /// 씬에 <b>꺼둔 채로</b> 두고 필요할 때 켠다. 켜면 <see cref="VehicleController"/> 를 끈다 —
    /// 둘이 같은 <c>Rigidbody</c> 를 쓰면 서로 속도를 덮어쓴다.
    /// </summary>
    public sealed class SnowDriveSweep : MonoBehaviour
    {
        [SerializeField] private Transform _vehicle;
        [SerializeField] private VehicleController _controller;
        [SerializeField] private SnowVehiclePad _pad;
        [SerializeField] private SnowVehicleDrag _drag;

        [Header("궤도")]
        [SerializeField, Min(1f)] private float _radius = 12f;
        [SerializeField, Min(0.5f)] private float _speed = 8f;
        [Tooltip("차체가 바닥에서 뜨는 높이. 프리팹 기준값을 그대로 쓴다.")]
        [SerializeField] private float _height = 0.6f;

        [Header("로그")]
        [Tooltip("이 간격(초)마다 덮인 비율·최고속 배율·제거량을 찍는다. 0 이면 안 찍는다.")]
        [SerializeField, Min(0f)] private float _logInterval = 1f;

        private float _angle;
        private float _logTimer;
        private int _removedThisInterval;

        private Rigidbody _body;
        private bool _bodyWasKinematic;

        private void OnEnable()
        {
            // 같은 Rigidbody 를 두 곳이 쓰면 안 된다.
            if (_controller != null) _controller.enabled = false;

            // 물리가 스텝 사이에 차를 끌어내리고 우리가 다시 올려놓는 싸움을 없앤다.
            if (_vehicle != null && _vehicle.TryGetComponent(out _body))
            {
                _bodyWasKinematic = _body.isKinematic;
                _body.isKinematic = true;
            }

            _angle = 0f;
            _logTimer = 0f;
            _removedThisInterval = 0;
        }

        private void OnDisable()
        {
            if (_controller != null) _controller.enabled = true;
            if (_body != null) _body.isKinematic = _bodyWasKinematic;
        }

        /// <summary>
        /// 운동은 <b>렌더 레이트</b>에서 돈다. <c>FixedUpdate</c> 에서 <c>transform</c> 을 쓰면
        /// Rigidbody 입장에서 매 물리 스텝 <b>텔레포트</b>가 되어 보간 버퍼를 건너뛰고, 렌더가
        /// 물리보다 빠른 순간 위치가 계단으로 보인다 — 카메라가 그걸 그대로 따라가 떨린다.
        /// 이 프로젝트는 같은 원인으로 이미 한 번 물렸다(<c>MoveRotation</c> 이 텔레포트로 취급되는 건).
        ///
        /// 이 도구는 물리 스텝 격자에 위치를 맞출 이유가 없다. 스탬프만 <c>FixedUpdate</c> 에 남긴다 —
        /// 패드가 2.4m 라 스탬프 위치가 스텝 단위로 양자화돼도 자국은 이어진다.
        /// </summary>
        private void Update()
        {
            if (_vehicle == null) return;

            _angle += _speed / _radius * Time.deltaTime;

            Vector3 position = new Vector3(Mathf.Cos(_angle) * _radius, _height,
                                           Mathf.Sin(_angle) * _radius);
            Vector3 tangent = new Vector3(-Mathf.Sin(_angle), 0f, Mathf.Cos(_angle));
            _vehicle.SetPositionAndRotation(position, Quaternion.LookRotation(tangent, Vector3.up));
        }

        private void FixedUpdate()
        {
            if (_vehicle == null) return;

            float dt = Time.fixedDeltaTime;
            if (_pad != null) _removedThisInterval += _pad.LastRemovedCm;

            if (_logInterval <= 0f) return;
            _logTimer += dt;
            if (_logTimer < _logInterval) return;
            _logTimer = 0f;

            float covered = _drag != null ? _drag.Covered : -1f;
            float factor = _controller != null ? _controller.GroundSpeedFactor : -1f;
            float laps = _angle / (Mathf.PI * 2f);

            // 필드를 직접 찍는다 — 화면이 이상할 때 "데이터가 낮은가 / 업로드가 깨졌나 / 셰이더가 깨졌나"를
            // 갈라내는 첫 질문이 이것이다. 궤도 밖(원점)과 궤도 위 두 곳을 본다.
            SnowStage stage = _drag != null ? GetComponent<SnowStage>() : null;
            string field = "n/a";
            if (stage?.Field != null)
            {
                int offTrack = stage.DepthCmAtWorld(Vector3.zero);
                int onTrack = stage.DepthCmAtWorld(new Vector3(_radius, 0f, 0f));
                field = $"depthCm(center)={offTrack} depthCm(track)={onTrack}";
            }

            Debug.Log($"[SnowDriveSweep] lap={laps:F2} removedCm={_removedThisInterval} " +
                      $"covered={covered:F2} groundSpeedFactor={factor:F2} {field}");
            _removedThisInterval = 0;
        }
    }
}
