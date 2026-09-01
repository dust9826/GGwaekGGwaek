using UnityEngine;

namespace PPack
{
    /// <summary>테스트 씬 전용: 플레이어가 의뢰 집 반경에 닿으면 완료 조건을 만족한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DebugProximityRequestCompletionCondition : RequestCompletionCondition
    {
        [SerializeField, Min(0.5f)] private float _radius = 6f;

        public override bool IsSatisfied(Transform player, RequestDirector director, GiftRequest request)
        {
            if (player == null || director == null || request == null) return false;

            DeliveryHouse house = director.HouseAt(request.HouseIndex);
            if (house == null || house.Zone == null) return false;

            Vector3 playerPosition = player.position;
            Vector3 housePosition = house.Zone.transform.position;
            playerPosition.y = 0f;
            housePosition.y = 0f;
            return Vector3.Distance(playerPosition, housePosition) <= _radius;
        }
    }
}
