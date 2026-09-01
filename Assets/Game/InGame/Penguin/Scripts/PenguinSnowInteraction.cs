using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 펭귄과 CPU 눈의 접점. 눈 권위 변경은 <see cref="SnowCpuStage"/>에 등록해 맡기고, 이 컴포넌트는
    /// 접지 정보와 발자국 치수 제공 및 비주얼 높이 보정만 소유한다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PenguinLocomotion))]
    [RequireComponent(typeof(PenguinControlState))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PenguinSnowInteraction : MonoBehaviour
    {
        public const float VisualHeightFraction = 0.5f;
        private const float SupportFloorToleranceM = 0.12f;

        [SerializeField] private Transform _snowHeightPivot;
        [SerializeField, Min(0.01f)] private float _heightFollowSeconds = 0.18f;

        private PenguinLocomotion _locomotion;
        private PenguinControlState _controlState;
        private CapsuleCollider _capsule;
        private SnowCpuStage _stage;
        private Vector3 _pivotRestLocalPosition;
        private float _visualOffsetM;
        private float _visualOffsetVelocity;
        private float _nextStageSearchTime;

        public bool HasSnowContact => _locomotion != null
                                      && _locomotion.IsGrounded
                                      && _controlState != null
                                      && _controlState.Current != EPenguinControlState.SnowballTop
                                      && TrySupportedSnowDepth(out _);

        public Vector3 ContactWorldPosition
        {
            get
            {
                if (_capsule == null) return transform.position;
                Bounds bounds = _capsule.bounds;
                return new Vector3(bounds.center.x, bounds.min.y + 0.05f, bounds.center.z);
            }
        }

        public float FootprintRadiusM
        {
            get
            {
                if (_capsule == null) return 0.4f;
                Vector3 scale = _capsule.transform.lossyScale;
                return _capsule.radius * Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            }
        }

        public float VisualOffsetM => _visualOffsetM;

        private void Awake()
        {
            _locomotion = GetComponent<PenguinLocomotion>();
            _controlState = GetComponent<PenguinControlState>();
            _capsule = GetComponent<CapsuleCollider>();

            if (_snowHeightPivot == null)
            {
                Transform candidate = transform.Find("SnowHeightPivot");
                if (candidate != null) _snowHeightPivot = candidate;
            }

            if (_snowHeightPivot != null) _pivotRestLocalPosition = _snowHeightPivot.localPosition;
        }

        private void Start() => BindStage();

        private void OnEnable()
        {
            if (_stage != null) _stage.RegisterPenguin(this);
        }

        private void OnDisable()
        {
            if (_stage != null) _stage.UnregisterPenguin(this);
            if (_snowHeightPivot != null) _snowHeightPivot.localPosition = _pivotRestLocalPosition;
            _visualOffsetM = 0f;
            _visualOffsetVelocity = 0f;
        }

        private void LateUpdate()
        {
            if (_stage == null && Time.unscaledTime >= _nextStageSearchTime) BindStage();
            if (_snowHeightPivot == null) return;

            float targetOffsetM = TargetVisualOffsetM();
            _visualOffsetM = Mathf.SmoothDamp(_visualOffsetM, targetOffsetM,
                ref _visualOffsetVelocity, _heightFollowSeconds, Mathf.Infinity, Time.deltaTime);

            Vector3 local = _pivotRestLocalPosition;
            local.y += _visualOffsetM;
            _snowHeightPivot.localPosition = local;
        }

        private float TargetVisualOffsetM()
        {
            if (_stage == null || _stage.Field == null) return 0f;
            if (_controlState != null && _controlState.Current == EPenguinControlState.SnowballTop) return 0f;

            // 공중에서는 마지막 설면 오프셋을 유지한다. 점프 도중 아래 지형을 따라가면 몸과 카메라가
            // 별도의 수직 궤적을 만들어 실제 Rigidbody 점프와 어긋난다.
            if (_locomotion == null || !_locomotion.IsGrounded) return _visualOffsetM;
            return TrySupportedSnowDepth(out float depthM)
                ? depthM * VisualHeightFraction
                : 0f;
        }

        private bool TrySupportedSnowDepth(out float depthM)
        {
            depthM = 0f;
            return _stage != null && _stage.TryDepthAtSupport(ContactWorldPosition,
                SupportFloorToleranceM, out depthM);
        }

        private void BindStage()
        {
            if (_stage != null) return;
            _nextStageSearchTime = Time.unscaledTime + 1f;

            foreach (SnowCpuStage candidate in FindObjectsByType<SnowCpuStage>())
            {
                if (candidate.gameObject.scene != gameObject.scene) continue;
                _stage = candidate;
                _stage.RegisterPenguin(this);
                return;
            }
        }
    }
}
