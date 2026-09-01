using UnityEngine;
using UnityEngine.InputSystem;

namespace PPack
{
    /// <summary>테스트 씬에서 단계 경계와 연출을 반복해서 확인하기 위한 입력 드라이버.</summary>
    [DisallowMultipleComponent]
    public sealed class SnowballGrowthTestDriver : MonoBehaviour
    {
        [SerializeField] private SnowballStagePrototypeActor _actor;
        [SerializeField] private bool _autoGrow = true;
        [SerializeField, Min(0.01f)] private float _growthRateMPerSecond = 0.22f;
        [SerializeField, Min(0f)] private float _maximumHoldSeconds = 0.8f;

        private float _maximumHoldTimer;

        public void Configure(SnowballStagePrototypeActor actor)
        {
            _actor = actor;
        }

        private void Update()
        {
            if (_actor == null) return;

            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                if (keyboard.digit0Key.wasPressedThisFrame) SetStage(ESnowBallGrowthStage.Seed);
                if (keyboard.digit1Key.wasPressedThisFrame) SetStage(ESnowBallGrowthStage.Stage1);
                if (keyboard.digit2Key.wasPressedThisFrame) SetStage(ESnowBallGrowthStage.Stage2);
                if (keyboard.digit3Key.wasPressedThisFrame) SetStage(ESnowBallGrowthStage.Stage3);
                if (keyboard.digit4Key.wasPressedThisFrame) SetStage(ESnowBallGrowthStage.Stage4);
                if (keyboard.gKey.wasPressedThisFrame) _autoGrow = !_autoGrow;
                if (keyboard.rKey.wasPressedThisFrame)
                    _actor.SetRawRadiusM(SnowballStageModel.MinRadiusM, false);
            }

            bool manualGrow = keyboard != null && keyboard.spaceKey.isPressed;
            if (!_autoGrow && !manualGrow) return;

            if (_actor.RawRadiusM >= SnowballStageModel.MaxRadiusM - 0.0001f)
            {
                _maximumHoldTimer += Time.deltaTime;
                if (_maximumHoldTimer >= _maximumHoldSeconds)
                {
                    _maximumHoldTimer = 0f;
                    _actor.SetRawRadiusM(SnowballStageModel.MinRadiusM, false);
                }
                return;
            }

            _maximumHoldTimer = 0f;
            _actor.AddRawRadiusM(_growthRateMPerSecond * Time.deltaTime);
        }

        private void SetStage(ESnowBallGrowthStage stage)
        {
            SnowballStageModel.GetStageRange(stage, out float startRadiusM, out _);
            _actor.SetRawRadiusM(startRadiusM, stage > _actor.GrowthStage);
        }
    }
}
