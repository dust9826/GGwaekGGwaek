using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 테스트 씬에서 생성된 실제 SnowBallCarrier의 표시 크기와 단계 상승 연출을 HUD에 제공한다.
    /// </summary>
    // SnowballGrowthStageTimer(50)와 SnowballMomentumMass(75)가 이번 물리 틱의 최종
    // 반지름·질량을 확정한 뒤 렌더 보간 표본을 잡는다.
    [DefaultExecutionOrder(1200)]
    [DisallowMultipleComponent]
    public sealed class SnowballGrowthPlayablePresentation : MonoBehaviour, ISnowballGrowthDisplay
    {
        private SnowBallCarrier _carrier;
        private SnowballGrowthStageTimer _stageTimer;
        private MeshRenderer _sourceRenderer;
        private Transform _sizeCorrectionPivot;
        private Transform _popFeedbackPivot;
        private MMF_Player _stageUpFeedback;
        private ESnowBallGrowthStage _growthStage;
        private float _previousPhysicsRadiusM;
        private float _currentPhysicsRadiusM;
        private float _displayRadiusM;
        private bool _initialized;

        public Vector3 WorldCenter => transform.position;
        public float DisplayRadiusM => _displayRadiusM;
        public float StageProgress01 => _growthStage == ESnowBallGrowthStage.Stage4
            ? 1f
            : _stageTimer != null
                ? _stageTimer.StageProgress01
                : _carrier == null ? 0f : SnowballStageModel.GetStageProgress01(_carrier.RadiusM);
        public ESnowBallGrowthStage GrowthStage => _growthStage;
        public SnowBallCarrier Carrier => _carrier;
        public int FeedbackPlayCount { get; private set; }

        public void ConfigureStageTimer(SnowballGrowthStageTimer stageTimer)
        {
            _stageTimer = stageTimer;
        }

        public void Initialize(SnowBallCarrier carrier)
        {
            if (_initialized || carrier == null) return;

            _carrier = carrier;
            _sourceRenderer = carrier.GetComponent<MeshRenderer>();
            MeshFilter sourceFilter = carrier.GetComponent<MeshFilter>();
            if (_sourceRenderer == null || sourceFilter == null) return;

            _sizeCorrectionPivot = CreateChild(transform, "StageSizeCorrectionPivot");
            _popFeedbackPivot = CreateChild(_sizeCorrectionPivot, "StagePopFeedbackPivot");
            _stageUpFeedback = CreateStageUpFeedback(_popFeedbackPivot);

            GameObject meshObject = new GameObject("GrowthSnowballMesh");
            meshObject.transform.SetParent(_popFeedbackPivot, false);
            meshObject.AddComponent<MeshFilter>().sharedMesh = sourceFilter.sharedMesh;
            MeshRenderer proxyRenderer = meshObject.AddComponent<MeshRenderer>();
            proxyRenderer.sharedMaterials = _sourceRenderer.sharedMaterials;
            proxyRenderer.shadowCastingMode = _sourceRenderer.shadowCastingMode;
            proxyRenderer.receiveShadows = _sourceRenderer.receiveShadows;

            _sourceRenderer.enabled = false;
            _growthStage = SnowballStageModel.GetStage(carrier.RadiusM);
            _previousPhysicsRadiusM = carrier.RadiusM;
            _currentPhysicsRadiusM = carrier.RadiusM;
            _displayRadiusM = carrier.RadiusM;
            ApplyDisplayScale();
            _initialized = true;
        }

        private void FixedUpdate()
        {
            if (!_initialized || _carrier == null) return;

            _previousPhysicsRadiusM = _currentPhysicsRadiusM;
            _currentPhysicsRadiusM = _carrier.RadiusM;
        }

        private void LateUpdate()
        {
            if (!_initialized || _carrier == null) return;

            float physicalRadiusM = _carrier.RadiusM;
            if (Mathf.Abs(physicalRadiusM - _currentPhysicsRadiusM) > 0.000001f)
            {
                // 에디터 도구나 테스트가 FixedUpdate 밖에서 크기를 바꿔도 한 렌더 프레임에
                // 튀지 않도록 같은 보간 경로에 넣는다.
                _previousPhysicsRadiusM = _currentPhysicsRadiusM;
                _currentPhysicsRadiusM = physicalRadiusM;
            }

            ESnowBallGrowthStage nextStage = SnowballStageModel.GetStage(physicalRadiusM);
            bool stageIncreased = nextStage > _growthStage;
            _growthStage = nextStage;
            float renderAlpha = Time.fixedDeltaTime > 0f
                ? Mathf.Clamp01((Time.time - Time.fixedTime) / Time.fixedDeltaTime)
                : 1f;
            _displayRadiusM = Mathf.Lerp(
                _previousPhysicsRadiusM, _currentPhysicsRadiusM, renderAlpha);
            ApplyDisplayScale();

            if (stageIncreased) PlayStageUpFeedback();
        }

        private void OnDestroy()
        {
            if (_sourceRenderer != null) _sourceRenderer.enabled = true;
        }

        private void ApplyDisplayScale()
        {
            if (_sizeCorrectionPivot == null || _carrier == null) return;

            float physicalDiameterM = Mathf.Max(0.001f, _carrier.DiameterM);
            float displayDiameterM = _displayRadiusM * 2f;
            _sizeCorrectionPivot.localScale = Vector3.one * (displayDiameterM / physicalDiameterM);
        }

        private void PlayStageUpFeedback()
        {
            if (_popFeedbackPivot != null) _popFeedbackPivot.localScale = Vector3.one;
            if (_stageUpFeedback == null) return;

            _stageUpFeedback.StopFeedbacks();
            _stageUpFeedback.PlayFeedbacks();
            FeedbackPlayCount++;
        }

        private static Transform CreateChild(Transform parent, string name)
        {
            GameObject child = new GameObject(name);
            child.transform.SetParent(parent, false);
            return child.transform;
        }

        private static MMF_Player CreateStageUpFeedback(Transform target)
        {
            MMF_Player player = target.gameObject.AddComponent<MMF_Player>();
            player.AddFeedback(new MMF_SquashAndStretch
            {
                SquashAndStretchTarget = target,
                Mode = MMF_SquashAndStretch.Modes.Absolute,
                Axis = MMF_SquashAndStretch.PossibleAxis.YtoXZ,
                AnimateScaleDuration = 0.35f,
                RemapCurveZero = 1f,
                RemapCurveOne = 1.28f,
                DetermineScaleOnPlay = false,
                AllowAdditivePlays = false,
                AnimateCurve = new AnimationCurve(
                    new Keyframe(0f, 0f),
                    new Keyframe(0.14f, -0.28f),
                    new Keyframe(0.42f, 1f),
                    new Keyframe(0.72f, -0.16f),
                    new Keyframe(1f, 0f))
            });
            player.Initialization();
            return player;
        }
    }
}
