using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 테스트 씬에서 플레이어 차량이 서 있을 자리. <b>이 오브젝트 하나만 옮기면 된다</b> —
    /// 차량 프리팹 인스턴스를 직접 집을 필요가 없다.
    ///
    /// 동기화는 <b>에디트 모드에서만</b> 한다. 플레이 중에 위치를 덮어쓰지 않는 이유는
    /// <see cref="VehicleController"/> 가 <c>Awake</c> 에서 <c>_yaw = transform.eulerAngles.y</c> 로
    /// 방향을 캡처하기 때문이다 — 같은 프레임에 우리가 회전을 바꾸면 둘 중 어느 Awake 가 먼저
    /// 도는지에 따라 차가 스폰 방향이 아니라 이전 방향으로 튼다. 에디트 모드에서 미리 맞춰
    /// 씬에 저장해 두면 그 경합 자체가 없다.
    /// </summary>
    [ExecuteAlways]
    [DisallowMultipleComponent]
    public sealed class DeliveryPlayerSpawn : VehicleRespawnPoint
    {
    }
}
