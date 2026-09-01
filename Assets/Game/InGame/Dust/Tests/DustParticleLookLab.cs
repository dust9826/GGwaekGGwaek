using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 파티클 룩을 눈으로 비교하기 위한 랩. <see cref="DustPadSweep"/> 가 도는 패드에
    /// <see cref="ParticleSystem"/> 을 붙여두고 계속 방출한다.
    ///
    /// <b>마스크를 안 본다.</b> 깨끗한 바닥에서도 뜨는데 의도한 것이다 — 답하려는 질문이
    /// "알갱이로 읽히는가"라서 어디서 뜨는지는 상관없다. 실제 판정(깨끗한 자리에선 안 난다)은
    /// VFX Graph 쪽이 이미 통과했고 이 랩은 그것을 대체하지 않는다. 내장 파티클이 그 판정에서
    /// 진 기록은 <c>Dust/AGENTS.md</c> 의 A/B 표에 있다.
    ///
    /// <b>왜 이 스크립트가 룩 노브를 직접 들고 있나 (2026-08-12 실측).</b>
    /// Unity CLI 의 <c>set_component_properties</c> 로 <see cref="ParticleSystem"/> 을 읽으면
    /// <c>InitialModule</c> · <c>ShapeModule</c> · <c>EmissionModule</c> · <c>SizeModule</c> ·
    /// <c>ColorModule</c> 이 전부 <c>&lt;unsupported:Generic&gt;</c> 으로 나온다. 크기 · 색 ·
    /// 개수 · 모양이 전부 그 안에 있어서 <b>CLI 로는 하나도 못 만진다.</b> 닿는 것은
    /// <c>lengthInSec</c> 같은 최상위 스칼라뿐이고 그중 룩 노브는 없다.
    ///
    /// 그래서 노브를 이 컴포넌트의 평범한 직렬화 필드로 올리고, 여기서 C# API 로 모듈에
    /// 밀어넣는다. <c>float</c> · <c>Color</c> · <c>enum</c> 은 CLI 가 잘 쓰므로 이걸로
    /// 값 세팅 - 플레이 - 스크린샷 루프가 성립한다.
    ///
    /// 여기 있는 노브 목록이 곧 VFX Graph 에 만들 Exposed 파라미터의 사양이다.
    /// 근거는 <c>docs/specs/2026-08-12-dust-particle-look.md</c> §2 · §5.
    ///
    /// 검증 씬 전용이다. 프로덕션 경로에 넣지 않는다.
    /// </summary>
    [RequireComponent(typeof(ParticleSystem))]
    public sealed class DustParticleLookLab : MonoBehaviour
    {
        [SerializeField] private DustPadSweep _sweep;

        [Tooltip("패드 위로 띄우는 높이. 바닥과 같은 평면에 두면 깜빡인다.")]
        [SerializeField] private float _lift = 0.05f;

        [Header("Look — CLI 가 만지는 값들")]
        [SerializeField] private ParticleSystemRenderMode _renderMode = ParticleSystemRenderMode.Billboard;

        [Tooltip("StretchedBillboard 에서만 의미가 있다. 속도 방향으로 늘어뜨리는 양.")]
        [SerializeField] private float _velocityScale;

        [SerializeField] private Color _color = new Color(0.72f, 0.66f, 0.55f, 1f);
        [SerializeField] private float _sizeMin = 0.04f;
        [SerializeField] private float _sizeMax = 0.10f;
        [SerializeField] private float _rate = 200f;
        [SerializeField] private float _lifetime = 0.7f;
        [SerializeField] private float _speed = 0.6f;

        [Tooltip("끄면 같은 스프라이트가 같은 각도로만 찍혀 도장처럼 보인다.")]
        [SerializeField] private bool _randomRotation = true;

        [Tooltip("방출 상자. 패드(반쪽 0.5 x 0.15)를 덮도록 기본값을 잡았다.")]
        [SerializeField] private Vector3 _emitBox = new Vector3(1f, 0.02f, 0.3f);

        private ParticleSystem _particles;
        private ParticleSystemRenderer _particleRenderer;

        private void Awake() => Apply();

        private void OnValidate() => Apply();

        private void Apply()
        {
            if (_particles == null) _particles = GetComponent<ParticleSystem>();
            if (_particleRenderer == null) _particleRenderer = GetComponent<ParticleSystemRenderer>();
            if (_particles == null || _particleRenderer == null) return;

            ParticleSystem.MainModule main = _particles.main;
            main.startColor = _color;
            main.startSize = new ParticleSystem.MinMaxCurve(_sizeMin, _sizeMax);
            main.startLifetime = _lifetime;
            main.startSpeed = _speed;
            main.startRotation = _randomRotation
                ? new ParticleSystem.MinMaxCurve(0f, Mathf.PI * 2f)
                : new ParticleSystem.MinMaxCurve(0f);

            // 월드 시뮬레이션이어야 한다. 로컬이면 파티클이 움직이는 패드에 붙어 따라다녀서
            // 지나간 자리에 남지 않는다 — 흡입이 아니라 도구에 매달린 장식으로 보인다.
            main.simulationSpace = ParticleSystemSimulationSpace.World;

            ParticleSystem.EmissionModule emission = _particles.emission;
            emission.rateOverTime = _rate;

            ParticleSystem.ShapeModule shape = _particles.shape;
            shape.shapeType = ParticleSystemShapeType.Box;
            shape.scale = _emitBox;

            _particleRenderer.renderMode = _renderMode;
            _particleRenderer.velocityScale = _velocityScale;
        }

        // DustPadSweep.Update 가 CurrentPad 를 채운 뒤에 읽어야 한 프레임 뒤처지지 않는다.
        private void LateUpdate()
        {
            // enabled 까지 보는 이유: 스윕이 꺼져 있으면 Update 가 안 돌아 CurrentPad 가
            // default 로 남고, 그 WorldToPad 는 영행렬이라 inverse 가 쓰레기를 뱉는다.
            if (_sweep == null || !_sweep.enabled) return;

            // BrushPad 는 포즈를 WorldToPad 하나로만 들고 있다. 스케일이 one 인 순수 강체
            // 변환이라 역행렬에서 위치와 회전을 그대로 꺼낼 수 있다.
            Matrix4x4 padToWorld = _sweep.CurrentPad.WorldToPad.inverse;
            transform.SetPositionAndRotation(padToWorld.GetPosition() + Vector3.up * _lift,
                                             padToWorld.rotation);
        }
    }
}