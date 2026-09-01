using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 지금 배달 목표인 집 위에 <see cref="DeliveryTruckBeacon"/>(트럭 도착지에 쓰던 것과 같은 삼각형
    /// 핀)을 띄우고, 그 집의 <see cref="GiftDropZone"/> 범위를 <see cref="GiftDropZoneHighlight"/>로
    /// 눈 위에 그린다. <see cref="StageHouseSignals"/>의 집 신호만 구독해 표시만 하고, 주문
    /// 상태·판정은 전혀 바꾸지 않는다(Delivery/AGENTS.md "../UI/는 표시만 한다" 경계).
    ///
    /// 집 하나당 활성 주문은 최대 하나이므로(선정기가 진행 중인 집을 제외한다) 하우스 인덱스로
    /// 키를 잡아도 충돌하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryHouseBeaconDisplay : MonoBehaviour
    {
        [SerializeField] private StageHouseSignals _signals;

        [Tooltip("WinterVillage 집은 지붕까지 약 6m다(실측, VillageHouse_07 bounds). 트럭용 기본값(3m)을 " +
                 "그대로 쓰면 핀이 지붕 안에 파묻힌다 — 지붕 위로 확실히 뜨도록 더 높였다.")]
        [SerializeField, Min(0f)] private float _pinHeight = 7.5f;
        [SerializeField, Min(0.05f)] private float _pinSize = 0.7f;

        private readonly Dictionary<int, GameObject> _markersByHouseIndex = new Dictionary<int, GameObject>();

        public void Configure(StageHouseSignals signals) => _signals = signals;

        private void OnEnable()
        {
            if (_signals == null) return;
            _signals.HouseOpened += HandleHouseOpened;
            _signals.HouseClosed += HandleHouseClosed;

            IReadOnlyList<StageHouseSignal> active = _signals.Active;
            for (int index = 0; index < active.Count; index++) HandleHouseOpened(active[index]);
        }

        private void OnDisable()
        {
            if (_signals == null) return;
            _signals.HouseOpened -= HandleHouseOpened;
            _signals.HouseClosed -= HandleHouseClosed;
        }

        private void HandleHouseOpened(StageHouseSignal signal)
        {
            if (_markersByHouseIndex.ContainsKey(signal.HouseIndex)) return;

            DeliveryHouse house = _signals.HouseAt(signal.HouseIndex);
            if (house == null || house.Zone == null) return;

            var markerObject = new GameObject($"GiftHouseMarker_{house.name}");
            markerObject.transform.SetParent(transform);
            markerObject.SetActive(false);

            var pinObject = new GameObject("Pin");
            pinObject.transform.SetParent(markerObject.transform);
            pinObject.SetActive(false);
            DeliveryTruckBeacon beacon = pinObject.AddComponent<DeliveryTruckBeacon>();
            beacon.Configure(house.Zone.transform, signal.Color, _pinHeight, _pinSize);
            pinObject.SetActive(true);

            var zoneObject = new GameObject("ZoneHighlight");
            zoneObject.transform.SetParent(markerObject.transform);
            zoneObject.SetActive(false);
            GiftDropZoneHighlight highlight = zoneObject.AddComponent<GiftDropZoneHighlight>();
            Vector3 zoneSize = house.Zone.Size;
            highlight.Configure(
                house.Zone.transform,
                new Vector2(zoneSize.x, zoneSize.z),
                signal.Color);
            zoneObject.SetActive(true);

            markerObject.SetActive(true);
            _markersByHouseIndex[signal.HouseIndex] = markerObject;
        }

        // 배달로 닫혔는지 만료로 닫혔는지에 따라 마무리가 다르다. 완료면 표식이
        // 한 번 터진 뒤 사라지고, 만료면 조용히 없어진다.
        private void HandleHouseClosed(int houseIndex, bool completed) => RemoveMarker(houseIndex, completed);

        private void RemoveMarker(int houseIndex, bool completed = false)
        {
            if (!_markersByHouseIndex.TryGetValue(houseIndex, out GameObject markerObject)) return;
            _markersByHouseIndex.Remove(houseIndex);
            if (markerObject == null) return;

            if (completed)
            {
                DeliveryTruckBeacon beacon = markerObject.GetComponentInChildren<DeliveryTruckBeacon>(true);
                if (beacon != null) beacon.gameObject.SetActive(false);
                GiftDropZoneHighlight highlight = markerObject.GetComponentInChildren<GiftDropZoneHighlight>(true);
                if (highlight != null)
                {
                    highlight.PlayCompletion();
                    Destroy(markerObject, 1f);
                    return;
                }
            }

            Destroy(markerObject);
        }
    }
}
