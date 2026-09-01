using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace PPack
{
    /// <summary>
    /// Drives the standalone snow-to-warehouse play scene without replacing any gameplay input.
    /// It only seeds the rack and reports observed game state. The player creates the real
    /// snowball with the normal E input so the tutorial never auto-feeds the machine.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftProductionDeliveryFlowDirector : MonoBehaviour
    {
        private enum EFlowStep
        {
            Preparing,
            PushSnow,
            PushGift,
            InTransit,
            PullLowerGift,
            PullDeliveredGift,
            Complete
        }

        [Header("Live gameplay references")]
        [SerializeField] private SnowCpuStage _snowStage;
        [SerializeField] private SnowGiftMachinePresentation _snowGiftMachine;
        [SerializeField] private GiftDeliveryTerminal _senderTerminal;
        [SerializeField] private SnowballWarehouseStorage _warehouseStorage;

        [Header("Scene presentation")]
        [SerializeField] private Text _statusText;
        [SerializeField] private Text _counterText;
        [SerializeField, Range(0, 4)] private int _startingPickupGifts = 4;

        private EFlowStep _step = EFlowStep.Preparing;
        private int _rackCountBeforeDelivery;
        private bool _senderAcceptedGift;

        public string CurrentStepName => _step.ToString();
        public int StoredGiftCount => _warehouseStorage != null ? _warehouseStorage.StoredCount : 0;

        private void Awake()
        {
            if (_senderTerminal != null)
                _senderTerminal.GiftIntakeCompleted += HandleGiftIntakeCompleted;
        }

        private IEnumerator Start()
        {
            // SnowCpuStage and the gift appearance components finish their own Awake first.
            yield return null;

            SeedPickupRack();
            _rackCountBeforeDelivery = _warehouseStorage != null ? _warehouseStorage.StoredCount : 0;
            _step = EFlowStep.PushSnow;
            RefreshGuide();
        }

        private void Update()
        {
            if (_snowGiftMachine == null || _warehouseStorage == null) return;

            switch (_step)
            {
                case EFlowStep.PushSnow:
                    if (_snowGiftMachine.IsProcessing || _snowGiftMachine.LastSpawnedGift != null)
                        SetStep(EFlowStep.PushGift);
                    break;

                case EFlowStep.PushGift:
                    if (_senderAcceptedGift) SetStep(EFlowStep.InTransit);
                    break;

                case EFlowStep.InTransit:
                    if (_warehouseStorage.StoredCount > _rackCountBeforeDelivery)
                        SetStep(EFlowStep.PullLowerGift);
                    break;

                case EFlowStep.PullLowerGift:
                    if (_warehouseStorage.StoredCount <= _rackCountBeforeDelivery)
                        SetStep(EFlowStep.PullDeliveredGift);
                    break;

                case EFlowStep.PullDeliveredGift:
                    if (_warehouseStorage.StoredCount < _rackCountBeforeDelivery)
                        SetStep(EFlowStep.Complete);
                    break;
            }

            RefreshCounter();
        }

        private void OnDestroy()
        {
            if (_senderTerminal != null)
                _senderTerminal.GiftIntakeCompleted -= HandleGiftIntakeCompleted;
        }

        private void HandleGiftIntakeCompleted(GiftDeliveryTerminal terminal)
        {
            if (terminal != _senderTerminal) return;
            _senderAcceptedGift = true;
            SetStep(EFlowStep.InTransit);
        }

        private void SeedPickupRack()
        {
            if (_warehouseStorage == null || _snowGiftMachine == null || _snowGiftMachine.GiftPrefab == null)
                return;

            int count = Mathf.Min(_startingPickupGifts, _warehouseStorage.Capacity / 2);
            for (int index = 0; index < count; index++)
            {
                Gift gift = Instantiate(_snowGiftMachine.GiftPrefab);
                gift.name = $"WarehouseStarterGift_{index + 1:00}";
                gift.gameObject.SetActive(true);
                gift.SetKind(SnowballWarehouseStorage.GiftKindForLane(index));
                gift.SetCarried(false);

                Rigidbody body = gift.GetComponent<Rigidbody>();
                if (body == null) body = gift.gameObject.AddComponent<Rigidbody>();
                body.mass = 2f;
                body.linearDamping = 0.8f;
                body.angularDamping = 0.8f;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;

                if (!_warehouseStorage.TryStoreGift(gift)) Destroy(gift.gameObject);
            }
        }

        private void SetStep(EFlowStep next)
        {
            if (_step == next) return;
            _step = next;
            RefreshGuide();
        }

        private void RefreshGuide()
        {
            if (_statusText == null) return;
            _statusText.text = _step switch
            {
                EFlowStep.Preparing => "배송 라인을 준비하는 중...",
                EFlowStep.PushSnow => "1  E로 발밑 눈을 뭉친 뒤 청록색 기계의 투입구로 미세요",
                EFlowStep.PushGift => "2  나온 선물을 낮은 우편 단말기 안으로 발로 미세요",
                EFlowStep.InTransit => "3  선물이 멀리 있는 창고로 배송되는 중입니다",
                EFlowStep.PullLowerGift => "4  창고 왼쪽 첫 칸의 아래 선물을 밀어 꺼내세요",
                EFlowStep.PullDeliveredGift => "5  위에서 내려온 파란 배송 선물을 다시 꺼내세요",
                EFlowStep.Complete => "완료! 눈 → 선물 → 배송 → 창고 인출 흐름을 모두 확인했습니다",
                _ => string.Empty
            };
            RefreshCounter();
        }

        private void RefreshCounter()
        {
            if (_counterText == null) return;
            int count = _warehouseStorage != null ? _warehouseStorage.StoredCount : 0;
            _counterText.text = $"창고 선물  {count} / {(_warehouseStorage != null ? _warehouseStorage.Capacity : 0)}";
        }
    }
}
