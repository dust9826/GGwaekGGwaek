using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>
    /// 마우스로 먼지를 지우는 임시 조작. 청소기·대걸레가 생기기 전까지 붓질을 손으로 확인하기 위한 것이고,
    /// 도구가 들어오면 이 컴포넌트는 사라진다. 진짜 조작은 <c>InGame/Vacuum</c>·<c>InGame/Mop</c> 소관이다.
    ///
    /// 도구는 표면에 붙어 움직이는 사각 패드(스팀청소기)라서, 여기서는 마우스 히트 지점을 위치로,
    /// 표면 노멀을 위로, 프레임간 이동 방향을 앞으로 삼아 패드를 흉내낸다.
    ///
    /// 프로젝트가 New Input System 전용(<c>activeInputHandler: 1</c>)이라 <c>Mouse.current</c> 를 쓴다.
    /// </summary>
    public sealed class DustMousePainter : MonoBehaviour
    {
        [SerializeField] private Camera _camera;
        [SerializeField] private Transform _brushIndicator;

        [Tooltip("청소 VFX. 붙어 있지 않아도 붓질은 동작한다.")]
        [SerializeField] private DustCleanVfx _vfx;

        [Header("Pad")]
        [Tooltip("패드의 반쪽 크기. x = 좌우 폭, y = 진행 방향 길이.")]
        [SerializeField] private Vector2 _halfExtents = new Vector2(0.5f, 0.15f);
        [Tooltip("패드가 닿는 두께. 이게 없으면 사각 기둥이 무한히 뻗어 아래층까지 지운다.")]
        [SerializeField, Min(0.01f)] private float _thickness = 0.25f;
        [Tooltip("경계가 풀어지는 폭. 월드 단위.")]
        [SerializeField, Min(0.001f)] private float _feather = 0.06f;
        [Tooltip("0.002 아래는 8비트 마스크의 반올림이 0 이라 아무 일도 일어나지 않는다.")]
        [SerializeField, Range(0.002f, 1f)] private float _strength = 0.35f;

        [Header("Unevenness")]
        [Tooltip("붓 자국의 울퉁불퉁함. 0 이면 완벽한 직사각형이 되어 스탬프처럼 보인다.")]
        [SerializeField, Range(0f, 1f)] private float _unevenness = 0.55f;
        [Tooltip("울퉁불퉁함의 결 크기. 클수록 잘다.")]
        [SerializeField, Min(0.01f)] private float _unevennessScale = 6f;
        [Tooltip("세게 밀수록 고르게 닦인다. 1 이면 세기와 무관하게 일정하게 울퉁불퉁하다.")]
        [SerializeField, Range(0f, 1f)] private float _evenOutWithStrength = 0.65f;

        [Header("Raycast")]
        [SerializeField] private float _maxDistance = 500f;
        [SerializeField] private LayerMask _layers = ~0;

        /// <summary>이 값보다 적게 움직였으면 직전 회전을 유지한다. 정지 상태에서 패드가 떠는 것을 막는다.</summary>
        private const float MinTravelSqr = 1e-6f;

        private Vector3 _lastHitPoint;
        private Quaternion _padRotation = Quaternion.identity;
        private bool _hasLastHit;

        private void Reset() => _camera = Camera.main;

        private void Awake()
        {
            if (_camera == null) _camera = Camera.main;
        }

        private void Update()
        {
            Mouse mouse = Mouse.current;
            if (mouse == null || _camera == null) return;

            // R 로 마스크를 되돌려 몇 번이고 다시 칠해볼 수 있게 한다.
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null && keyboard.rKey.wasPressedThisFrame)
            {
                foreach (DustPaintTarget target in FindObjectsByType<DustPaintTarget>(FindObjectsSortMode.None))
                    target.ResetMask();
            }

            Ray ray = _camera.ScreenPointToRay(mouse.position.ReadValue());
            if (!Physics.Raycast(ray, out RaycastHit hit, _maxDistance, _layers))
            {
                if (_brushIndicator != null) _brushIndicator.gameObject.SetActive(false);
                _hasLastHit = false;
                return;
            }

            UpdatePadRotation(hit);

            if (_brushIndicator != null)
            {
                _brushIndicator.gameObject.SetActive(true);
                _brushIndicator.SetPositionAndRotation(hit.point, _padRotation);
                _brushIndicator.localScale = new Vector3(_halfExtents.x * 2f, 0.02f, _halfExtents.y * 2f);
            }

            if (!mouse.leftButton.isPressed) return;

            // 약하게 문지르면 얼룩이 남고 세게 밀면 고르게 닦인다.
            // 여러 번 겹쳐 문지르면 결국 균일해지는데, 그 과정이 "닦는" 손맛이 된다.
            float unevenness = _unevenness * Mathf.Lerp(1f, 1f - _evenOutWithStrength, _strength);

            BrushPad pad = new BrushPad(hit.point, _padRotation, _halfExtents,
                                        _thickness, _feather, _strength, unevenness, _unevennessScale);

            // 이 묶음이 프레임당 한 번 도는 자리에 있어야 한다. Fusion 이 오면 그대로 Render() 로
            // 옮겨간다 — FixedUpdateNetwork 에 두면 재시뮬레이션마다 중복으로 지워진다.
            if (_vfx != null) _vfx.BeginFrame();

            // 붓은 맞은 콜라이더가 속한 대상만 지운다. 벽 너머가 지워지지 않는다.
            if (hit.collider.TryGetComponent(out DustPaintTarget paintTarget))
            {
                // 순서가 중요하다. CaptureErased 는 빼기 전 마스크를 읽어야 한다.
                if (_vfx != null) paintTarget.CaptureErased(_vfx.ErasedMap, pad);
                paintTarget.Paint(pad);
            }

            if (_vfx != null) _vfx.Play(pad, _padRotation * Vector3.forward);
        }

        /// <summary>진행 방향이 패드의 앞이 된다. 거의 안 움직였으면 직전 회전을 유지한다.</summary>
        private void UpdatePadRotation(in RaycastHit hit)
        {
            Vector3 forward = _hasLastHit
                ? Vector3.ProjectOnPlane(hit.point - _lastHitPoint, hit.normal)
                : Vector3.ProjectOnPlane(_camera.transform.forward, hit.normal);

            if (forward.sqrMagnitude > MinTravelSqr)
                _padRotation = Quaternion.LookRotation(forward.normalized, hit.normal);

            _lastHitPoint = hit.point;
            _hasLastHit = true;
        }
    }
}
