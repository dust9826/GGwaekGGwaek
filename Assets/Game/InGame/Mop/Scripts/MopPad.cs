using System;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 청소 패드. 플레이어 앞 고정 오프셋에서 바닥으로 레이캐스트해 표면에 붙은 사각 패드를 만들고,
    /// 그 패드로 <see cref="DustPaintTarget"/> 을 지운다.
    ///
    /// 여기는 마스크를 모른다 — <see cref="BrushPad"/> 를 채워 건넬 뿐이다.
    /// (<c>Mop/AGENTS.md</c>: 흡입은 여기, 무엇이 빨려드는지는 Dust/Trash/Insects)
    ///
    /// <see cref="DustCleanVfx"/> 를 소유하는 쪽이 붓질을 미는 쪽이어야 Fusion 이 왔을 때 원격
    /// 플레이어의 도구도 자기 렌더 텍스처를 갖는다.
    /// </summary>
    public sealed class MopPad : MonoBehaviour
    {
        [Tooltip("패드를 매달 기준. 보통 캐릭터 트랜스폼.")]
        [SerializeField] private Transform _origin;
        [Tooltip("청소 VFX. 붙어 있지 않아도 붓질은 동작한다.")]
        [SerializeField] private DustCleanVfx _vfx;

        [Header("패드 위치")]
        [Tooltip("기준 기준 로컬 오프셋. 발밑을 벗어나되 팔이 닿는 거리.")]
        [SerializeField] private Vector3 _localOffset = new Vector3(0f, 0f, 0.8f);
        [Tooltip("바닥을 찾는 레이의 시작 높이.")]
        [SerializeField, Min(0.1f)] private float _rayUp = 1f;
        [Tooltip("시작 높이 아래로 훑는 길이.")]
        [SerializeField, Min(0.1f)] private float _rayLength = 3f;
        [SerializeField] private LayerMask _layers = ~0;

        [Header("붓")]
        [Tooltip("좌우 반쪽 폭, 진행 방향 반쪽 길이. 폭은 차체(1.2 m)에 맞춘다.\n" +
                 "진행 방향 길이는 _feather 보다 짧으면 안 된다 — 짧으면 패드 한가운데조차 " +
                 "최대 세기에 도달하지 못해 아래의 '한 번 지나가면 얼마나' 산수가 깨진다.")]
        [SerializeField] private Vector2 _halfExtents = new Vector2(0.6f, 0.3f);
        [SerializeField, Min(0.01f)] private float _thickness = 0.25f;
        [Tooltip("경계가 풀어지는 폭. 겹쳐 찍혀도 살아남을 만큼 넓어야 자국이 너덜너덜해진다.")]
        [SerializeField, Min(0.001f)] private float _feather = 0.3f;

        [Tooltip("한 번 지나가면 지워지는 양. 1 이면 한 번에 다 닦이고, 1/n 이면 대충 n 번 " +
                 "왕복해야 다 닦인다.\n" +
                 "프레임마다 찍는 양이 아니라 '지나간 결과'다 — 이 프레임에 패드가 실제로 " +
                 "움직인 거리로 나눠 넣으므로 속도와 프레임률이 달라져도 같은 결과가 나온다. " +
                 "프레임당 값으로 두면 빠를수록 덜 닦이거나 프레임률에 따라 결과가 갈린다.")]
        [SerializeField, Range(0.02f, 1f)] private float _cleanPerPass = 1f;

        [SerializeField, Range(0f, 1f)] private float _unevenness = 0.55f;
        [SerializeField, Min(0.01f)] private float _unevennessScale = 6f;
        [SerializeField, Range(0f, 1f)] private float _evenOutWithStrength = 0.65f;

        /// <summary>실제로 <see cref="DustPaintTarget"/> 에 전달한 붓의 월드 위치·회전·반크기.
        /// 미니맵이 이미 청소한 영역을 칠하는 데 구독한다.
        ///
        /// 프레임당 한 번만 쏜다 — 패드가 이음매에 걸치면 대상은 여럿이지만 붓은 하나다.
        /// 청소 판정과 마스크의 권위는 여전히 이쪽에 있고, 이 이벤트는 읽기 전용 표시용이다.</summary>
        public event Action<Vector3, Quaternion, Vector2> SurfacePainted;

        /// <summary>지난 프레임 패드 위치. 이번 프레임에 지나간 거리를 재는 데만 쓴다.</summary>
        private Vector3 _lastPadPosition;
        private bool _hasLastPadPosition;

        /// <summary>겹침 질의 결과 버퍼. 패드가 한 번에 걸칠 수 있는 바닥 조각 수의 상한이다 —
        /// 16 m 패널 격자에서 폭 1 m 패드가 닿을 수 있는 것은 많아야 넷이고, 나머지는 여유다.</summary>
        private readonly Collider[] _overlap = new Collider[8];

        /// <summary>레이 결과 버퍼. 자기 차체를 걸러내야 하므로 첫 히트만으로는 부족하다.</summary>
        private readonly RaycastHit[] _rayHits = new RaycastHit[8];

        private void OnEnable()
        {
            // 껐다 켜는 사이에 차가 움직였을 수 있다. 그 거리를 한 프레임에 지운 것으로 치면
            // 켜는 순간 큰 자국이 찍힌다.
            _hasLastPadPosition = false;
        }

        private void Update()
        {
            if (_origin == null) return;

            // 붓질하지 않는 프레임에도 비운다. 이 호출이 아래 조기 return 들 뒤에 있었을 때는,
            // 차가 멈춰 travel 이 0 이 되는 순간 **마지막에 지운 내용이 맵에 그대로 굳었다.**
            // 그래프는 그 맵을 계속 읽으므로 파티클이 영원히 뜬다. 서서히 멈추면 남는 값이 작아
            // 문턱에 걸러지지만, 최고속으로 벽에 박아 즉시 멈추면 0.095 가 남은 채로 굳는다.
            if (_vfx != null) _vfx.BeginFrame();

            Vector3 padXZ = _origin.TransformPoint(_localOffset);
            Ray ray = new Ray(padXZ + Vector3.up * _rayUp, Vector3.down);

            // 공중이거나 구멍 위다. 이 프레임은 붓질하지 않는다.
            if (!TryFindSurface(ray, _rayUp + _rayLength, out RaycastHit hit)) return;

            // 패드 평면은 표면 노멀에 눕고, 진행 방향은 캐릭터 정면을 그 평면에 투영한 것이다.
            // 경사면에서도 패드가 뜨지 않는 이유가 이것이다.
            Vector3 forward = Vector3.ProjectOnPlane(_origin.forward, hit.normal);
            if (forward.sqrMagnitude < 1e-6f) return;
            forward.Normalize();

            Quaternion padRotation = Quaternion.LookRotation(forward, hit.normal);

            // 이 프레임에 찍을 양은 "지나간 거리 / 패드 길이" 만큼이다.
            //
            // 프레임당 고정값으로 두면 한 텍셀이 몇 번 찍히는지가 속도와 프레임률에 좌우된다 —
            // 12 m/s @60fps 면 여덟 겹이지만 24 m/s 나 30fps 면 절반이다. 그러면 "몇 번 왕복하면
            // 다 닦이나"가 상황마다 달라진다. 거리로 나누면 한 번 지나간 결과가 _cleanPerPass 로
            // 고정된다.
            //
            // 멈춰 있으면 아무것도 안 지운다. 제자리에서 문지르면 페더까지 전부 0 이 되어 자국
            // 끝이 자로 잰 듯 잘렸는데, 그 인공물도 같이 사라진다.
            float travel = _hasLastPadPosition ? Vector3.Distance(hit.point, _lastPadPosition) : 0f;
            _lastPadPosition = hit.point;
            _hasLastPadPosition = true;

            // 나누는 길이는 패드 길이가 아니라 **유효 길이**다. 붓은 중앙선에서만 최대 세기이고
            // 앞뒤로 페더만큼 0 까지 떨어지므로, 한 텍셀이 패드 밑을 지나며 받는 총량은 그 프로파일의
            // 적분이지 길이 전체가 아니다. 길이로 나누면 실제로 지워지는 양이 절반쯤 나온다 —
            // _cleanPerPass 를 1 로 두고도 한 번에 다 안 닦였던 것이 이것이었다.
            float padLength = _halfExtents.y * 2f;
            float effectiveLength = Mathf.Max(padLength - _feather, padLength * 0.25f, 1e-4f);
            float strength = Mathf.Clamp01(_cleanPerPass * (travel / effectiveLength));

            // 8비트 마스크는 0.002 아래를 0 으로 반올림한다. 기어가듯 느리게 가면 프레임당 양이
            // 그 아래로 내려가 영원히 안 닦이므로, 움직이고 있다면 바닥값을 준다.
            if (travel > 0f) strength = Mathf.Max(strength, 0.004f);
            if (strength <= 0f) return;

            // 약하게 밀면 얼룩이 남고 세게 밀면 고르게 닦인다.
            float unevenness = _unevenness * Mathf.Lerp(1f, 1f - _evenOutWithStrength, _cleanPerPass);

            BrushPad pad = new BrushPad(hit.point, padRotation, _halfExtents,
                                        _thickness, _feather, strength,
                                        unevenness, _unevennessScale);

            // 이 묶음이 프레임당 한 번 도는 자리에 있어야 한다. Fusion 이 오면 그대로 Render() 로
            // 옮겨간다 — FixedUpdateNetwork 에 두면 재시뮬레이션마다 중복으로 지워진다.

            // 패드가 닿는 **모든** 대상을 지운다. 레이가 맞은 하나만 지우면, 바닥이 여러 장으로
            // 나뉘어 있을 때 패드가 이음매에 걸치는 순간 붓의 절반이 통째로 버려진다. 지나간 자리
            // 한쪽이 자로 잰 듯 곧게 잘리는 것이 그 증상이다 — 붓이 아니라 대상 선택의 문제다.
            //
            // 벽 너머가 지워지지 않는 것은 여전히 지켜진다. 상자가 패드 두께만큼 얇아서 같은 면에
            // 놓인 바닥만 걸리고, 걸리더라도 붓 셰이더가 두께 밖 텍셀을 다시 잘라낸다.
            Vector3 halfBox = new Vector3(_halfExtents.x, _thickness, _halfExtents.y);
            int hitCount = Physics.OverlapBoxNonAlloc(hit.point, halfBox, _overlap, padRotation, _layers);

            bool paintedAnything = false;
            for (int i = 0; i < hitCount; i++)
            {
                if (!_overlap[i].TryGetComponent(out DustPaintTarget target)) continue;

                // 순서가 중요하다. CaptureErased 는 빼기 전 마스크를 읽어야 한다.
                if (_vfx != null) target.CaptureErased(_vfx.ErasedMap, pad);
                target.Paint(pad);
                paintedAnything = true;
            }

            if (paintedAnything) SurfacePainted?.Invoke(hit.point, padRotation, _halfExtents);
            if (_vfx != null) _vfx.Play(pad, forward);
        }

        /// <summary>
        /// 패드 아래 표면을 찾는다. <b>자기 차체는 건너뛴다.</b>
        ///
        /// 패드가 차체 발자국 안으로 들어오면 레이 시작점이 차체 위가 되어, 첫 히트가 바닥이 아니라
        /// 자기 <c>Body</c> 콜라이더가 된다. 거기엔 <see cref="DustPaintTarget"/> 이 없으므로 청소가
        /// 통째로 멈춘다 — 패드를 차 크기로 키우려던 첫 시도(2026-08-13)가 되돌려진 것이 이것이다.
        ///
        /// 레이어로는 못 가른다. 차체와 바닥이 둘 다 <c>Default</c> 라 <see cref="_layers"/> 로 하나를
        /// 빼면 다른 하나도 빠지고, 레이어를 새로 파는 것은 <c>TagManager.asset</c> 을 건드리는 일이라
        /// 브랜치마다 같은 줄에서 충돌한다.
        ///
        /// 차체에 클리어런스가 없다는 것도 짚어둔다. <c>Body</c> 콜라이더가 y 0 까지 내려와 바닥에
        /// 닿으므로, 바닥과 차체 사이에서 레이를 시작하는 방법은 아예 존재하지 않는다.
        /// </summary>
        private bool TryFindSurface(Ray ray, float distance, out RaycastHit surface)
        {
            int count = Physics.RaycastNonAlloc(ray, _rayHits, distance, _layers);

            surface = default;
            float nearest = float.PositiveInfinity;

            for (int i = 0; i < count; i++)
            {
                if (_rayHits[i].distance >= nearest) continue;
                if (_rayHits[i].collider.transform.IsChildOf(_origin)) continue;

                nearest = _rayHits[i].distance;
                surface = _rayHits[i];
            }

            return nearest < float.PositiveInfinity;
        }
    }
}
