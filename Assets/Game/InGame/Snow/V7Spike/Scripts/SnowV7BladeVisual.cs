using PPack;   // ISnowBladeState 가 여기로 옮겨졌다(평평한 이름공간 규칙)
using UnityEngine;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// 제설 블레이드의 <b>시각물</b>. 눈을 깎는 것은 <see cref="SnowPileFieldV7"/> 이고 이 컴포넌트는
    /// 그 상태를 사람이 볼 수 있게 그리는 층뿐이다 — <b>콜라이더가 없다.</b>
    ///
    /// <para>콜라이더를 안 붙인 이유가 설계다. 눈과의 상호작용은 필드가 격자에서 계산하고(어디를 깎고
    /// 얼마를 쌓았는지), 차량이 받는 저항은 <see cref="SnowV7VehicleFeel"/> 이 그 격자를 읽어 준다.
    /// 여기에 물리 콜라이더까지 두면 <b>같은 저항을 두 곳이 계산</b>하고, 더미를 밀 때 콜라이더가 지형
    /// 프롭에 걸려 차가 튄다.</para>
    ///
    /// <para><b>치수는 시뮬레이션에서 가져온다.</b> 날의 폭은 <see cref="SnowPileFieldV7.BladeWidthM"/>
    /// 이고, 배치는 <see cref="SnowV7MapRig.BladeAheadM"/> 다. 인스펙터에 폭을 따로 적어 두면 그림과
    /// 실제로 깎이는 폭이 조용히 어긋난다 — 그건 "보이는 것과 판정이 다르다"는 최악의 버그다.</para>
    ///
    /// <para>계층: 이 컴포넌트가 붙은 오브젝트(회전 중심) → <c>Yaw</c>(배출 방향) → <c>Lift</c>(내림/올림)
    /// → 판·날끝. 회전 축을 하나에 몰지 않고 나눈 이유는 <b>두 동작이 독립</b>이기 때문이다. 날을 올린
    /// 상태에서도 각도를 바꿀 수 있고, 각도를 준 상태에서 내릴 수 있다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowV7BladeVisual : MonoBehaviour
    {
        [Header("연결")]
        [Tooltip("비면 부모에서 찾고, 없으면 씬에서 찾는다. 멀티에서는 네트워크 차량이 주인이다.")]
        [SerializeField] private MonoBehaviour _stateSource;
        [Tooltip("배출 방향으로 요잉하는 노드.")]
        [SerializeField] private Transform _yaw;
        [Tooltip("내림/올림으로 피치하는 노드.")]
        [SerializeField] private Transform _lift;
        [Tooltip("폭을 필드의 날 폭에 맞출 판. 로컬 X 스케일이 폭이다.")]
        [SerializeField] private Transform _plate;

        [Header("각도")]
        [Tooltip("배출 방향으로 돌리는 각도. 필드의 _bladeAngleDeg 와 같아야 그림과 판정이 맞는다.")]
        [SerializeField, Range(0f, 60f)] private float _yawDeg = 30f;
        [Tooltip("내렸을 때의 피치. 0 이면 판이 수직이다.")]
        [SerializeField, Range(-30f, 30f)] private float _downPitchDeg = 8f;
        [Tooltip("올렸을 때의 피치. 날끝이 지면에서 떨어질 만큼 젖힌다.")]
        [SerializeField, Range(0f, 80f)] private float _upPitchDeg = 42f;
        [Tooltip("각도 전이 속도 (도/초). 즉시 꺾이면 기계가 아니라 스위치로 읽힌다.")]
        [SerializeField, Min(1f)] private float _degreesPerSecond = 180f;

        private ISnowBladeState _state;
        private float _pitch;
        private float _yawCurrent;
        private bool _widthApplied;
        private Vector3 _restLocalPosition;

        /// <summary>
        /// 차가 눈 위로 올라탄 높이(m). <see cref="SnowV7VehicleFeel"/> 가 밀어 넣는다.
        ///
        /// <b>제설 모드에서는 0 이다.</b> 날을 내리면 그것은 바닥을 긁고 있다는 뜻이므로 눈 높이를
        /// 따라 뜨면 안 된다 — 날이 뜨는 것은 올렸을 때뿐이고, 그때는 차에 실려 같이 올라간다.
        /// 그 판단은 넣는 쪽이 하고 여기서는 받은 값을 그대로 쓴다.
        /// </summary>
        public float RideOffsetY { get; set; }

        private void Awake()
        {
            // 부모(차량)가 상태를 갖고 있으면 그것이 우선이다 - 멀티에서는 복제된 값이 진실이고,
            // 씬 전역에서 리그를 찾으면 남의 피어 상태를 그릴 위험이 있다.
            _state = _stateSource as ISnowBladeState
                  ?? GetComponentInParent<ISnowBladeState>()
                  ?? FindAnyObjectByType<SnowV7MapRig>() as ISnowBladeState;

            _restLocalPosition = transform.localPosition;

            // 시작 자세는 "올림"이다. 리그도 그 상태로 시작하므로 첫 프레임에 그림이 튀지 않는다.
            _pitch = _upPitchDeg;
            ApplyPose();
        }

        private void LateUpdate()
        {
            if (_state == null) return;

            ApplySimulationWidthOnce();

            float targetPitch = _state.BladeDown ? _downPitchDeg : _upPitchDeg;
            float targetYaw = _state.AngleState * _yawDeg;
            float step = _degreesPerSecond * Time.deltaTime;

            _pitch = Mathf.MoveTowards(_pitch, targetPitch, step);
            _yawCurrent = Mathf.MoveTowards(_yawCurrent, targetYaw, step);

            ApplyPose();
        }

        private void ApplyPose()
        {
            if (_yaw != null) _yaw.localRotation = Quaternion.Euler(0f, _yawCurrent, 0f);
            if (_lift != null) _lift.localRotation = Quaternion.Euler(-_pitch, 0f, 0f);
            transform.localPosition = _restLocalPosition + new Vector3(0f, RideOffsetY, 0f);
        }

        /// <summary>
        /// 필드가 준비된 첫 프레임에 판의 폭을 시뮬레이션 폭으로 맞춘다. <c>Awake</c> 에서 못 하는 이유는
        /// 필드가 <c>Start</c> 에서 리소스를 만들기 때문이다.
        /// </summary>
        private void ApplySimulationWidthOnce()
        {
            if (_widthApplied || _plate == null) return;

            // 폭은 필드에서 읽는다. 상태 주인이 차량일 때는 필드를 모르므로 씬의 리그에서 찾는다.
            var rig = _state as SnowV7MapRig ?? FindAnyObjectByType<SnowV7MapRig>();
            SnowPileFieldV7 field = rig == null ? null : rig.Field;
            if (field == null) return;

            Vector3 scale = _plate.localScale;
            scale.x = Mathf.Max(0.3f, field.BladeWidthM);
            _plate.localScale = scale;
            _widthApplied = true;
        }
    }
}
