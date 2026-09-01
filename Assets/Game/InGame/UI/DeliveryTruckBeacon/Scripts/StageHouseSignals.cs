using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>집 위에 무엇을 띄울지 정하는 데 필요한 전부. 어느 주문 모델에서 왔는지는 담지 않는다.</summary>
    public readonly struct StageHouseSignal
    {
        public StageHouseSignal(int houseIndex, Color color)
        {
            HouseIndex = houseIndex;
            Color = color;
        }

        public int HouseIndex { get; }
        public Color Color { get; }
    }

    /// <summary>HELP 말풍선·지붕색·바닥 표식이 구독하는 집 단위 신호원.
    ///
    /// <para>표시 컴포넌트는 이제 배달 도메인을 모르고 이 신호만 읽는다. 어느 주문 모델을 쓰든
    /// 자기 Presenter가 <see cref="Open"/>과 <see cref="Close"/>로 밀어 넣는다 —
    /// <see cref="GiftDeliveryHouseSignalPresenter"/>와 <see cref="RequestHouseSignalPresenter"/>가
    /// 그 역할이다. 세 번째 주문원이 생겨도 표시는 손대지 않는다.</para></summary>
    [DisallowMultipleComponent]
    public sealed class StageHouseSignals : MonoBehaviour
    {
        private readonly List<StageHouseSignal> _active = new List<StageHouseSignal>();
        private DeliveryHouse[] _houses = Array.Empty<DeliveryHouse>();

        public event Action<StageHouseSignal> HouseOpened;
        /// <summary>두 번째 인자는 배달로 닫혔는지(true) 만료로 닫혔는지(false). 완료 연출은
        /// 이 구분이 있어야 낼 수 있다.</summary>
        public event Action<int, bool> HouseClosed;

        /// <summary>컴포넌트가 늦게 켜져도 현재 열린 집을 복원할 수 있게 목록을 열어 둔다.</summary>
        public IReadOnlyList<StageHouseSignal> Active => _active;

        public int HouseCount => _houses.Length;

        public DeliveryHouse HouseAt(int index) =>
            index >= 0 && index < _houses.Length ? _houses[index] : null;

        public void SetHouses(IReadOnlyList<DeliveryHouse> houses)
        {
            if (houses == null)
            {
                _houses = Array.Empty<DeliveryHouse>();
                return;
            }

            _houses = new DeliveryHouse[houses.Count];
            for (int index = 0; index < houses.Count; index++) _houses[index] = houses[index];
        }

        /// <summary>같은 집이 이미 열려 있으면 색만 갱신하고 이벤트는 다시 쏘지 않는다.
        /// 표시 쪽 등장 연출이 두 번 돌지 않게 하려는 것이다.</summary>
        public void Open(int houseIndex, Color color)
        {
            if (houseIndex < 0) return;

            for (int index = 0; index < _active.Count; index++)
            {
                if (_active[index].HouseIndex != houseIndex) continue;
                _active[index] = new StageHouseSignal(houseIndex, color);
                return;
            }

            var signal = new StageHouseSignal(houseIndex, color);
            _active.Add(signal);
            HouseOpened?.Invoke(signal);
        }

        public void Close(int houseIndex, bool completed = false)
        {
            for (int index = 0; index < _active.Count; index++)
            {
                if (_active[index].HouseIndex != houseIndex) continue;
                _active.RemoveAt(index);
                HouseClosed?.Invoke(houseIndex, completed);
                return;
            }
        }

        public void CloseAll()
        {
            for (int index = _active.Count - 1; index >= 0; index--)
            {
                int houseIndex = _active[index].HouseIndex;
                _active.RemoveAt(index);
                HouseClosed?.Invoke(houseIndex, false);
            }
        }
    }
}
