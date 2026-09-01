using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 강의 물 표면 아래 트리거. 플레이어 차량이 물로 떨어졌을 때 테스트 씬의
    /// <see cref="DeliveryPlayerSpawn"/> 자세로 되돌린다.
    /// </summary>
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public sealed class DeliveryRiverRespawnVolume : VehicleRespawnVolume
    {
    }
}
