using UnityEngine;

namespace PPack
{
    /// <summary>곡선 HUD가 프로토타입 공과 실제 플레이 공을 같은 방식으로 읽는 최소 표시 계약.</summary>
    public interface ISnowballGrowthDisplay
    {
        Vector3 WorldCenter { get; }
        float DisplayRadiusM { get; }
        float StageProgress01 { get; }
        ESnowBallGrowthStage GrowthStage { get; }
    }
}
