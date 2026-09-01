using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 활성 배달 퀘스트의 색을 목표 집 지붕에 표시하고 굴뚝 연기를 같은 색으로 물들인다.
    /// 퀘스트 판정은 바꾸지 않는 표현 컴포넌트다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryHouseRoofDisplay : MonoBehaviour
    {
        [SerializeField] private StageHouseSignals _signals;

        private readonly Dictionary<int, HouseRoofIdentity> _roofsByHouseIndex =
            new Dictionary<int, HouseRoofIdentity>();
        private readonly Dictionary<int, GiftDeliveryHouseQuestSmoke> _smokeByHouseIndex =
            new Dictionary<int, GiftDeliveryHouseQuestSmoke>();

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
            if (_signals != null)
            {
                _signals.HouseOpened -= HandleHouseOpened;
                _signals.HouseClosed -= HandleHouseClosed;
            }

            ClearAllRoofs();
        }

        private void ClearAllRoofs()
        {
            foreach (HouseRoofIdentity roof in _roofsByHouseIndex.Values)
                if (roof != null) roof.SetQuestSender(false);
            _roofsByHouseIndex.Clear();

            foreach (GiftDeliveryHouseQuestSmoke smoke in _smokeByHouseIndex.Values)
                RemoveSmokeEffect(smoke);
            _smokeByHouseIndex.Clear();
        }

        private void HandleHouseOpened(StageHouseSignal signal)
        {
            DeliveryHouse house = _signals.HouseAt(signal.HouseIndex);
            if (house == null) return;

            HouseRoofIdentity roof = FindNearestRoof(house.transform.position);
            if (roof == null) return;

            roof.SetQuestSender(true, signal.Color);
            _roofsByHouseIndex[signal.HouseIndex] = roof;

            ParticleSystem chimneySmoke = FindOrCreateChimneySmoke(roof, out bool ownsSmoke);
            if (chimneySmoke == null) return;

            // <b>이미 붙어 있으면 그것을 쓴다.</b> <see cref="GiftDeliveryHouseQuestSmoke"/> 는
            // [DisallowMultipleComponent] 라, 이미 있는 오브젝트에 AddComponent 하면 예외가 아니라
            // <b>null 을 돌려준다</b> — 그래서 바로 다음 줄에서 NRE 가 났다.
            //
            // 굴뚝은 실제로 공유된다. <see cref="FindOrCreateChimneySmoke"/> 는 지붕 이름에서 뽑은
            // ChimneySmoke_XX 를 찾는데, <see cref="FindNearestRoof"/> 가 서로 다른 집에 같은 지붕을
            // 돌려주면 두 집이 같은 굴뚝에 온다.
            //
            // 그리고 이 경로는 <c>RequestHouseSignalPresenter.Update</c> 에서 오므로 한 번으로 끝나지
            // 않는다 — 2026-08-29 2인 실행에서 몇 분 만에 <b>15,415회</b> 났고 전부 같은
            // ChimneySmoke_07 이었다. 예외마다 스택 트레이스를 뜨므로 그 자체로 프레임이 무너진다.
            if (!chimneySmoke.TryGetComponent(out GiftDeliveryHouseQuestSmoke smoke))
                smoke = chimneySmoke.gameObject.AddComponent<GiftDeliveryHouseQuestSmoke>();
            smoke.Configure(chimneySmoke, roof.DisplayColor, ownsSmoke);
            _smokeByHouseIndex[signal.HouseIndex] = smoke;
        }

        // 지붕색은 완료든 만료든 똑같이 꺼진다. 완료 연출은 바닥 표식이 낸다.
        private void HandleHouseClosed(int houseIndex, bool completed) => ClearRoof(houseIndex);

        private void ClearRoof(int houseIndex)
        {
            if (_roofsByHouseIndex.TryGetValue(houseIndex, out HouseRoofIdentity roof))
            {
                _roofsByHouseIndex.Remove(houseIndex);
                if (roof != null) roof.SetQuestSender(false);
            }

            if (_smokeByHouseIndex.TryGetValue(houseIndex, out GiftDeliveryHouseQuestSmoke smoke))
            {
                _smokeByHouseIndex.Remove(houseIndex);
                RemoveSmokeEffect(smoke);
            }
        }

        private static ParticleSystem FindOrCreateChimneySmoke(
            HouseRoofIdentity roof,
            out bool ownsSmoke)
        {
            ownsSmoke = false;
            // HouseId는 원본 프리팹 이름(PF_WinterHouse_Lit_D 등)이고, 씬 인스턴스 이름의
            // VillageHouse_XX가 ChimneySmoke_XX와 실제로 대응한다.
            string houseName = roof.name;
            if (string.IsNullOrWhiteSpace(houseName)) houseName = roof.HouseId;
            int separator = houseName.LastIndexOf('_');
            if (separator < 0 || separator >= houseName.Length - 1) return null;

            string expectedName = "ChimneySmoke_" + houseName.Substring(separator + 1);
            ParticleSystem[] smokeSystems =
                FindObjectsByType<ParticleSystem>(FindObjectsInactive.Include);
            ParticleSystem template = null;
            for (int index = 0; index < smokeSystems.Length; index++)
            {
                ParticleSystem smoke = smokeSystems[index];
                if (smoke == null) continue;
                if (string.Equals(smoke.name, expectedName, StringComparison.Ordinal)) return smoke;
                if (template == null && smoke.name.StartsWith("ChimneySmoke_", StringComparison.Ordinal))
                    template = smoke;
            }

            if (template == null || !roof.TryGetChimneyTopCenter(out Vector3 chimneyTop)) return null;

            GameObject smokeObject = Instantiate(template.gameObject, template.transform.parent);
            smokeObject.name = expectedName;
            smokeObject.transform.SetPositionAndRotation(chimneyTop, Quaternion.identity);
            smokeObject.transform.localScale = template.transform.localScale;

            ParticleSystem createdSmoke = smokeObject.GetComponent<ParticleSystem>();
            if (createdSmoke == null)
            {
                Destroy(smokeObject);
                return null;
            }

            createdSmoke.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            createdSmoke.Play(true);
            ownsSmoke = true;
            return createdSmoke;
        }

        private static void RemoveSmokeEffect(GiftDeliveryHouseQuestSmoke smoke)
        {
            if (smoke == null) return;
            bool ownsSmoke = smoke.OwnsSmoke;
            GameObject smokeObject = smoke.gameObject;
            smoke.Restore();
            if (ownsSmoke) Destroy(smokeObject);
            else Destroy(smoke);
        }

        private static HouseRoofIdentity FindNearestRoof(Vector3 position)
        {
            HouseRoofIdentity[] roofs = FindObjectsByType<HouseRoofIdentity>(FindObjectsInactive.Include);
            HouseRoofIdentity nearest = null;
            float nearestDistance = float.PositiveInfinity;
            for (int index = 0; index < roofs.Length; index++)
            {
                float distance = (roofs[index].transform.position - position).sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = roofs[index];
                nearestDistance = distance;
            }
            return nearest;
        }
    }
}
