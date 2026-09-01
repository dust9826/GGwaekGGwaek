using UnityEngine;

namespace PPack
{
    /// <summary>연결된 플레이어가 들어오면 공용 리스폰 자세로 되돌리는 트리거다.</summary>
    [RequireComponent(typeof(BoxCollider))]
    [DisallowMultipleComponent]
    public class VehicleRespawnVolume : MonoBehaviour
    {
        [SerializeField] private VehicleRespawnPoint _spawn;

        public void Configure(VehicleRespawnPoint spawn) => _spawn = spawn;

        protected virtual void Reset()
        {
            BoxCollider collider = GetComponent<BoxCollider>();
            collider.isTrigger = true;
        }

        protected virtual void OnTriggerEnter(Collider other)
        {
            if (_spawn == null || _spawn.Player == null) return;

            Transform entered = other.transform;
            if (entered != _spawn.Player && !entered.IsChildOf(_spawn.Player)) return;
            _spawn.Respawn();
        }
    }
}
