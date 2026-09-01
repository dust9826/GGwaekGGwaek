using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class GiftDeliveryTerminalTrigger : MonoBehaviour
    {
        [SerializeField] private GiftDeliveryTerminal _terminal;

        private void Reset()
        {
            _terminal = GetComponentInParent<GiftDeliveryTerminal>();
            BoxCollider trigger = GetComponent<BoxCollider>();
            trigger.isTrigger = true;
        }

        private void OnTriggerEnter(Collider other) => TryAccept(other);
        private void OnTriggerStay(Collider other) => TryAccept(other);

        private void TryAccept(Collider other)
        {
            if (_terminal == null) _terminal = GetComponentInParent<GiftDeliveryTerminal>();
            Gift gift = other.GetComponentInParent<Gift>();
            if (gift != null) _terminal?.TryAccept(gift);
        }
    }
}
