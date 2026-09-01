using System.Collections.Generic;
using MoreMountains.Feedbacks;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 선택된 집 위에 HELP 말풍선을 띄우고 해당 주문이 끝날 때까지 유지하는 표현 전용 컴포넌트다.
    /// 어느 주문에서 왔는지는 모르고 <see cref="StageHouseSignals"/>만 구독한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryHouseHelpDisplay : MonoBehaviour
    {
        [SerializeField] private StageHouseSignals _signals;
        [SerializeField] private Font _font;
        [SerializeField] private MMF_Player _entranceFeedback;
        [SerializeField] private MMF_Player _idleFeedback;
        [SerializeField, Min(0f)] private float _height = 8.2f;
        [SerializeField, Min(0f)] private float _roofClearance = 0.65f;
        [SerializeField, Min(0.1f)] private float _scale = 1f;

        private readonly Dictionary<int, GiftDeliveryHouseHelpEffect> _effectsByHouseIndex =
            new Dictionary<int, GiftDeliveryHouseHelpEffect>();

        public void Configure(
            StageHouseSignals signals,
            Font font,
            MMF_Player entranceFeedback,
            MMF_Player idleFeedback)
        {
            _signals = signals;
            _font = font;
            _entranceFeedback = entranceFeedback;
            _idleFeedback = idleFeedback;
        }

        private void OnEnable()
        {
            if (_signals == null) return;
            _signals.HouseOpened += HandleHouseOpened;
            _signals.HouseClosed += HandleHouseClosed;

            // 컴포넌트가 주문 시작 뒤 다시 활성화돼도 목표 집의 HELP를 즉시 복원한다.
            IReadOnlyList<StageHouseSignal> active = _signals.Active;
            for (int index = 0; index < active.Count; index++) HandleHouseOpened(active[index]);
        }

        private void OnDisable()
        {
            if (_signals != null)
            {
                _signals.HouseOpened -= HandleHouseOpened;
                _signals.HouseClosed -= HandleHouseClosed;
            }

            ClearAll();
        }

        private void HandleHouseOpened(StageHouseSignal signal)
        {
            if (_effectsByHouseIndex.ContainsKey(signal.HouseIndex)) return;

            DeliveryHouse house = _signals.HouseAt(signal.HouseIndex);
            if (house == null) return;

            var effectObject = new GameObject($"GiftHouseHelp_{house.name}");
            effectObject.transform.SetParent(transform);
            effectObject.SetActive(false);

            GiftDeliveryHouseHelpEffect effect = effectObject.AddComponent<GiftDeliveryHouseHelpEffect>();
            Transform anchor = house.transform;
            Vector3 worldAnchorPosition = house.transform.position + Vector3.up * _height;

            HouseRoofIdentity roof = FindNearestRoof(house.transform.position);
            if (roof != null && roof.TryGetRoofTopCenter(out Vector3 roofTopCenter))
            {
                anchor = roof.transform;
                worldAnchorPosition = roofTopCenter + Vector3.up * _roofClearance;
            }

            effect.Configure(
                anchor,
                signal.Color,
                _font,
                anchor.InverseTransformPoint(worldAnchorPosition),
                _scale,
                _entranceFeedback,
                _idleFeedback);

            effectObject.SetActive(true);
            _effectsByHouseIndex[signal.HouseIndex] = effect;
        }

        private static HouseRoofIdentity FindNearestRoof(Vector3 position)
        {
            HouseRoofIdentity[] roofs = FindObjectsByType<HouseRoofIdentity>(FindObjectsInactive.Include);
            HouseRoofIdentity nearest = null;
            float nearestDistance = float.PositiveInfinity;

            for (int index = 0; index < roofs.Length; index++)
            {
                HouseRoofIdentity roof = roofs[index];
                if (roof == null) continue;

                float distance = (roof.transform.position - position).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = roof;
                nearestDistance = distance;
            }

            return nearest;
        }

        private void HandleHouseClosed(int houseIndex, bool completed) => Remove(houseIndex);

        private void Remove(int houseIndex)
        {
            if (!_effectsByHouseIndex.TryGetValue(houseIndex, out GiftDeliveryHouseHelpEffect effect)) return;
            _effectsByHouseIndex.Remove(houseIndex);
            if (effect != null) Destroy(effect.gameObject);
        }

        private void ClearAll()
        {
            foreach (GiftDeliveryHouseHelpEffect effect in _effectsByHouseIndex.Values)
                if (effect != null) Destroy(effect.gameObject);
            _effectsByHouseIndex.Clear();
        }
    }
}
