using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <see cref="SnowCpuStage"/>의 단일 수확 스윕이 실제로 제거한 양을 단계 성장에 전달한다.
    /// 제거 폭은 스테이지가 제어 반지름으로 직접 정하므로 여기서는 설면을 다시 절삭하지 않는다.
    /// </summary>
    [DefaultExecutionOrder(25)]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SnowBallCarrier), typeof(SnowballGrowthStageTimer))]
    public sealed class SnowballGrowthFootprintSync : MonoBehaviour
    {
        private SnowBallCarrier _carrier;
        private SnowballGrowthStageTimer _timer;
        private SnowCpuStage _stage;

        public float LastControlledDiameterM { get; private set; }
        public float LastSourceDiameterM { get; private set; }

        public void Configure(SnowBallCarrier carrier, SnowballGrowthStageTimer timer,
            SnowCpuStage stage)
        {
            _carrier = carrier;
            _timer = timer;
            _stage = stage;
        }

        private void FixedUpdate()
        {
            if (_carrier == null) _carrier = GetComponent<SnowBallCarrier>();
            if (_timer == null) _timer = GetComponent<SnowballGrowthStageTimer>();
            if (_carrier == null || _timer == null || _stage == null ||
                _stage.Field == null || !_stage.HasSimulationAuthority) return;

            LastControlledDiameterM = _timer.ControlledRadiusM * 2f;
            LastSourceDiameterM = _carrier.RadiusM * 2f;
            _timer.RecordHarvestedSnow(_stage.LastBallHarvestMm(_carrier));
        }
    }
}
