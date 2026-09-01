using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 패드를 원형 궤적으로 일정하게 쓸어 청소를 재현한다. <b>기본은 꺼져 있고</b>, 인스펙터에서
    /// 켜면 마우스 대신 이것이 붓질을 민다.
    ///
    /// 왜 필요한가: 마우스로 문지르면 매번 속도와 경로가 달라서 "더러울 때"와 "이미 깨끗할 때"를
    /// 같은 조건으로 비교할 수 없다. 청소 VFX 의 성공 기준은 그 둘의 차이(<c>Dust/AGENTS.md</c>
    /// 의 A/B 표)라서, 판정하려면 결정론적인 붓질이 필요하다. 원형인 이유는 첫 바퀴가 더러운
    /// 자리를 지나고 몇 바퀴 뒤에는 같은 궤적이 이미 깨끗해지기 때문이다 — 한 번 돌리면 두 조건이
    /// 다 나온다.
    ///
    /// <see cref="DustMousePainter"/> 와 같이 켜둬도 된다. 마우스 쪽은 좌클릭 중에만 붓질하므로
    /// 클릭하지 않으면 서로 간섭하지 않는다.
    /// </summary>
    public sealed class DustPadSweep : MonoBehaviour
    {
        [SerializeField] private DustPaintTarget _target;
        [SerializeField] private DustCleanVfx _vfx;

        [Header("Path")]
        [SerializeField] private Vector3 _center = new Vector3(-5f, 0f, -0.5f);
        [SerializeField, Min(0.1f)] private float _radius = 2.8f;
        [Tooltip("한 바퀴에 걸리는 시간. 길게 잡으면 첫 바퀴의 '더러운 구간'을 오래 관찰할 수 있다.")]
        [SerializeField, Min(0.5f)] private float _lapSeconds = 8f;

        [Header("Pad")]
        [SerializeField] private Vector2 _halfExtents = new Vector2(0.5f, 0.15f);
        [SerializeField, Min(0.01f)] private float _thickness = 0.25f;
        [SerializeField, Min(0.001f)] private float _feather = 0.06f;
        [Tooltip("0.002 아래는 8비트 마스크의 반올림이 0 이라 아무 일도 일어나지 않는다.")]
        [SerializeField, Range(0.002f, 1f)] private float _strength = 0.35f;
        [SerializeField, Range(0f, 1f)] private float _unevenness = 0.35f;
        [SerializeField, Min(0.01f)] private float _unevennessScale = 6f;

        /// <summary>몇 바퀴째인지. 판정 시점을 특정하는 데 쓴다.</summary>
        public float Laps { get; private set; }

        /// <summary>이번 프레임의 패드. 룩 랩이 파티클을 여기 붙인다 — <c>Tests/DustParticleLookLab.cs</c>.</summary>
        public BrushPad CurrentPad { get; private set; }

        private float _startTime;

        /// <summary>켤 때마다 마스크를 되돌린다. 그래야 매번 같은 조건에서 시작한다.</summary>
        private void OnEnable()
        {
            _startTime = Time.time;
            Laps = 0f;
            if (_target != null) _target.ResetMask();
        }

        private void Update()
        {
            if (_target == null) return;

            Laps = (Time.time - _startTime) / _lapSeconds;
            float angle = Laps * Mathf.PI * 2f;

            Vector3 position = _center + new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * _radius;
            Vector3 travel = new Vector3(-Mathf.Sin(angle), 0f, Mathf.Cos(angle));

            BrushPad pad = new BrushPad(position, Quaternion.LookRotation(travel, Vector3.up),
                                        _halfExtents, _thickness, _feather, _strength,
                                        _unevenness, _unevennessScale);

            // _vfx 블록보다 먼저다. 뒤에 두면 VFX 가 없는 씬 — 룩 랩이 그 경우다 — 에서 안 채워진다.
            CurrentPad = pad;

            // DustMousePainter 와 같은 순서. CaptureErased 가 Paint 보다 먼저다.
            if (_vfx != null)
            {
                _vfx.BeginFrame();
                _target.CaptureErased(_vfx.ErasedMap, pad);
            }

            _target.Paint(pad);

            if (_vfx != null) _vfx.Play(pad, travel);
        }
    }
}
