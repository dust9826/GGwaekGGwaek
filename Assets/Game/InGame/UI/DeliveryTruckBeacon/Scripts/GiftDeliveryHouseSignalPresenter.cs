using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>기존 싱글플레이 배달 흐름을 집 신호로 옮기는 어댑터.
    /// <see cref="RequestHouseSignalPresenter"/>와 같은 자리에 서는 반대편이다.</summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryHouseSignalPresenter : MonoBehaviour
    {
        [SerializeField] private GiftDeliveryDirector _director;
        [SerializeField] private StageHouseSignals _signals;

        public void Configure(GiftDeliveryDirector director, StageHouseSignals signals)
        {
            Unsubscribe();
            _director = director;
            _signals = signals;
            Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void OnDisable() => Unsubscribe();

        private void Subscribe()
        {
            if (_director == null || _signals == null) return;

            _director.OrderAnnounced -= HandleAnnounced;
            _director.OrderCompleted -= HandleEnded;
            _director.OrderFailed -= HandleFailed;
            _director.GameOver -= HandleGameOver;
            _director.OrderAnnounced += HandleAnnounced;
            _director.OrderCompleted += HandleEnded;
            _director.OrderFailed += HandleFailed;
            _director.GameOver += HandleGameOver;

            _signals.SetHouses(_director.Houses);

            IReadOnlyList<GiftDeliveryOrder> active = _director.ActiveOrders;
            for (int index = 0; index < active.Count; index++) HandleAnnounced(active[index]);
        }

        private void Unsubscribe()
        {
            if (_director == null) return;
            _director.OrderAnnounced -= HandleAnnounced;
            _director.OrderCompleted -= HandleEnded;
            _director.OrderFailed -= HandleFailed;
            _director.GameOver -= HandleGameOver;
        }

        private void HandleAnnounced(GiftDeliveryOrder order)
        {
            if (order == null || _signals == null) return;
            _signals.Open(order.HouseIndex, order.QuestColor);
        }

        private void HandleEnded(GiftDeliveryOrder order)
        {
            if (order == null || _signals == null) return;
            _signals.Close(order.HouseIndex, true);
        }

        private void HandleFailed(GiftDeliveryOrder order, EGiftDeliveryFailReason reason)
        {
            if (order == null || _signals == null) return;
            _signals.Close(order.HouseIndex, false);
        }

        private void HandleGameOver()
        {
            if (_signals != null) _signals.CloseAll();
        }
    }
}
