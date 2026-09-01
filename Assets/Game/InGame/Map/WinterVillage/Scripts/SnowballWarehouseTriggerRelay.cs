using UnityEngine;

namespace PPack
{
    public enum EWarehouseTriggerKind
    {
        Approach,
        GiftStorage
    }

    [DisallowMultipleComponent]
    public sealed class SnowballWarehouseTriggerRelay : MonoBehaviour
    {
        [SerializeField] private SnowballWarehouseStorage _warehouse;
        [SerializeField] private EWarehouseTriggerKind _kind;

        public void Configure(SnowballWarehouseStorage warehouse, EWarehouseTriggerKind kind)
        {
            _warehouse = warehouse;
            _kind = kind;
        }

        private void OnTriggerEnter(Collider other)
        {
            _warehouse?.NotifyTrigger(_kind, other, true);
        }

        private void OnTriggerExit(Collider other)
        {
            _warehouse?.NotifyTrigger(_kind, other, false);
        }
    }
}
