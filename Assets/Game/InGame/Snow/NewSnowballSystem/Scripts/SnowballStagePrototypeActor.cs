using System;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 테스트 씬 전용 눈덩이. 실제 눈 수확이나 네트워크 상태 대신 반지름 입력만 받아 단계 UX를 검증한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowballStagePrototypeActor : MonoBehaviour, ISnowballGrowthDisplay
    {
        private const float SnowDensityKgPerM3 = 400f;

        [SerializeField] private Transform _stageSizePivot;
        [SerializeField] private Transform _stagePopFeedbackPivot;
        [SerializeField] private SphereCollider _sphereCollider;
        [SerializeField] private Rigidbody _body;
        [SerializeField] private MMF_Player _stageUpFeedback;
        [SerializeField, Range(SnowballStageModel.MinRadiusM, SnowballStageModel.MaxRadiusM)]
        private float _rawRadiusM = SnowballStageModel.MinRadiusM;
        [SerializeField] private bool _keepBottomOnGround = true;
        [SerializeField] private float _groundY;

        private bool _initialized;
        private ESnowBallGrowthStage _growthStage;
        private float _displayRadiusM;

        public event Action<ESnowBallGrowthStage> StageChanged;

        public float RawRadiusM => _rawRadiusM;
        public float DisplayRadiusM => _displayRadiusM;
        public float StageProgress01 => _growthStage == ESnowBallGrowthStage.Stage4
            ? 1f
            : SnowballStageModel.GetStageProgress01(_rawRadiusM);
        public ESnowBallGrowthStage GrowthStage => _growthStage;
        public Vector3 WorldCenter => transform.position;
        public int FeedbackPlayCount { get; private set; }

        private void Awake()
        {
            ApplyCurrentRadius(false);
        }

        private void OnDisable()
        {
            if (_stageUpFeedback != null) _stageUpFeedback.StopFeedbacks();
            if (_stagePopFeedbackPivot != null) _stagePopFeedbackPivot.localScale = Vector3.one;
        }

        public void Configure(Transform stageSizePivot, Transform stagePopFeedbackPivot,
            SphereCollider sphereCollider, Rigidbody body, MMF_Player stageUpFeedback)
        {
            _stageSizePivot = stageSizePivot;
            _stagePopFeedbackPivot = stagePopFeedbackPivot;
            _sphereCollider = sphereCollider;
            _body = body;
            _stageUpFeedback = stageUpFeedback;
            _initialized = false;
            ApplyCurrentRadius(false);
        }

        public void SetRawRadiusM(float radiusM, bool playFeedback = true)
        {
            _rawRadiusM = SnowballStageModel.ClampRadius(radiusM);
            ApplyCurrentRadius(playFeedback);
        }

        public void AddRawRadiusM(float deltaRadiusM)
        {
            SetRawRadiusM(_rawRadiusM + deltaRadiusM);
        }

        public void RefreshWithoutFeedback()
        {
            ApplyCurrentRadius(false);
        }

        private void ApplyCurrentRadius(bool playFeedback)
        {
            ESnowBallGrowthStage nextStage = SnowballStageModel.GetStage(_rawRadiusM);
            bool stageChanged = _initialized && nextStage != _growthStage;
            bool stageIncreased = stageChanged && nextStage > _growthStage;

            _growthStage = nextStage;
            _displayRadiusM = _rawRadiusM;

            if (_stageSizePivot != null)
                _stageSizePivot.localScale = Vector3.one * (_displayRadiusM * 2f);
            if (_sphereCollider != null)
                _sphereCollider.radius = _displayRadiusM;
            if (_body != null)
            {
                float volumeM3 = 4f / 3f * Mathf.PI * _rawRadiusM * _rawRadiusM * _rawRadiusM;
                _body.mass = Mathf.Max(1f, volumeM3 * SnowDensityKgPerM3);
            }

            if (_keepBottomOnGround)
            {
                Vector3 position = transform.position;
                position.y = _groundY + _displayRadiusM;
                transform.position = position;
            }

            if (stageChanged) StageChanged?.Invoke(nextStage);
            if (stageIncreased && playFeedback) PlayStageUpFeedback();
            _initialized = true;
        }

        private void PlayStageUpFeedback()
        {
            if (_stagePopFeedbackPivot != null) _stagePopFeedbackPivot.localScale = Vector3.one;
            if (_stageUpFeedback == null) return;

            _stageUpFeedback.StopFeedbacks();
            _stageUpFeedback.PlayFeedbacks();
            FeedbackPlayCount++;
        }
    }
}
