using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    [RequireComponent(typeof(StageHUDController))]
    public sealed class GiftDeliveryHudPresenter : MonoBehaviour
    {
        [SerializeField] private StageHUDController _hud;
        [SerializeField] private GiftDeliveryDirector _director;
        private readonly List<StageHudOrderView> _orders = new List<StageHudOrderView>();

        public void Configure(StageHUDController hud, GiftDeliveryDirector director)
        {
            _hud = hud;
            _director = director;
        }

        private void Update()
        {
            if (_hud == null || _director == null)
            {
                if (_hud != null) _hud.SetVisible(false);
                return;
            }

            bool running = _director.Phase == EGiftDeliveryPhase.Running;
            _hud.SetVisible(running);
            if (!running) return;

            Transform origin = null;
            for (int index = 0; index < _director.Participants.Count; index++)
            {
                if (_director.Participants[index] == null) continue;
                origin = _director.Participants[index];
                break;
            }

            EGiftBoxKind? heldGiftKind = null;
            PenguinCarry carry = origin != null ? origin.GetComponentInChildren<PenguinCarry>() : null;
            if (carry != null && carry.Cargo != null)
            {
                Gift heldGift = carry.Cargo as Gift;
                if (heldGift == null) heldGift = carry.Cargo.GetComponentInParent<Gift>();
                if (heldGift == null) heldGift = carry.Cargo.GetComponentInChildren<Gift>();
                if (heldGift != null) heldGiftKind = heldGift.Kind;
            }

            _orders.Clear();
            for (int index = 0; index < _director.ActiveOrders.Count; index++)
            {
                GiftDeliveryOrder order = _director.ActiveOrders[index];
                DeliveryHouse house = order.HouseIndex >= 0 && order.HouseIndex < _director.Houses.Count
                    ? _director.Houses[order.HouseIndex]
                    : null;
                Vector3 target = house != null ? house.DoorPosition : origin != null
                    ? origin.position + origin.forward * order.RouteLength
                    : Vector3.forward * order.RouteLength;
                _orders.Add(StageHudOrderView.FromWorldTarget(
                    order.Id, Gift.ColorForKind(order.GiftKind), order.RemainingSeconds,
                    origin, target, order.RouteLength,
                    heldGiftKind.HasValue && heldGiftKind.Value == order.GiftKind));
            }

            _hud.SetOrders(_orders);
        }
    }
}
