using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Opens the warehouse doors for approaching rigidbody actors and snaps dropped gifts
    /// into two-level magazine lanes. Pulling a lower gift lets the reserve gift settle into
    /// the pickup slot, like a convenience-store drink rack.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowballWarehouseStorage : MonoBehaviour
    {
        public const int ColorLaneCount = 4;

        [Header("Doors")]
        [SerializeField] private Transform _leftDoorPivot;
        [SerializeField] private Transform _rightDoorPivot;
        [SerializeField] private Rigidbody _leftDoorBody;
        [SerializeField] private Rigidbody _rightDoorBody;
        [SerializeField, Min(0f)] private float _openAngle = 104f;
        [SerializeField, Min(1f)] private float _doorSpeed = 145f;

        [Header("Gift storage")]
        [SerializeField] private Transform[] _storageSlots;
        [SerializeField, Min(0.1f)] private float _releaseDistance = 1.15f;
        [SerializeField, Min(0.05f)] private float _refillDelay = 0.12f;
        [SerializeField, Min(0.05f)] private float _refillDuration = 0.36f;

        private readonly Dictionary<int, int> _approachingActors = new();
        private Quaternion _leftClosedLocalRotation;
        private Quaternion _rightClosedLocalRotation;
        private Gift[] _storedGifts;
        private bool[] _refillInProgress;
        private bool _initialized;
        private bool _forceOpen;

        public int Capacity => _storageSlots?.Length ?? 0;
        public int StoredCount
        {
            get
            {
                PruneStorage();
                int count = 0;
                if (_storedGifts == null) return 0;
                for (int i = 0; i < _storedGifts.Length; i++)
                    if (_storedGifts[i] != null) count++;
                return count;
            }
        }

        public IReadOnlyList<Transform> StorageSlots => _storageSlots;
        public bool DoorsRequestedOpen => _forceOpen || _approachingActors.Count > 0;

        public bool ContainsGift(Gift gift)
        {
            if (gift == null) return false;
            EnsureInitialized();
            PruneStorage();
            for (int i = 0; i < _storedGifts.Length; i++)
                if (_storedGifts[i] == gift) return true;
            return false;
        }

        /// <summary>왼쪽부터 파랑(1단계), 초록(2단계), 노랑(3단계), 빨강(4단계) 레인이다.</summary>
        public static int LaneIndexForKind(EGiftBoxKind kind)
        {
            return kind switch
            {
                EGiftBoxKind.Blue => 0,
                EGiftBoxKind.Green => 1,
                EGiftBoxKind.Yellow => 2,
                EGiftBoxKind.Red => 3,
                _ => 0
            };
        }

        public static EGiftBoxKind GiftKindForLane(int laneIndex)
        {
            return laneIndex switch
            {
                0 => EGiftBoxKind.Blue,
                1 => EGiftBoxKind.Green,
                2 => EGiftBoxKind.Yellow,
                3 => EGiftBoxKind.Red,
                _ => EGiftBoxKind.Blue
            };
        }

        public void Configure(
            Transform leftDoorPivot,
            Rigidbody leftDoorBody,
            Transform rightDoorPivot,
            Rigidbody rightDoorBody,
            Transform[] storageSlots)
        {
            _leftDoorPivot = leftDoorPivot;
            _leftDoorBody = leftDoorBody;
            _rightDoorPivot = rightDoorPivot;
            _rightDoorBody = rightDoorBody;
            _storageSlots = storageSlots;
            _initialized = false;
            EnsureInitialized();
        }

        public void NotifyTrigger(EWarehouseTriggerKind kind, Collider other, bool entered)
        {
            if (other == null) return;

            if (kind == EWarehouseTriggerKind.GiftStorage)
            {
                if (entered)
                {
                    Gift gift = other.GetComponentInParent<Gift>();
                    if (gift != null) TryStoreGift(gift);
                }
                return;
            }

            Gift ignoredGift = other.GetComponentInParent<Gift>();
            if (ignoredGift != null) return;

            Rigidbody body = other.attachedRigidbody;
            if (body == null) return;
            int actorId = body.transform.root.gameObject.GetEntityId().GetHashCode();
            if (entered)
            {
                _approachingActors.TryGetValue(actorId, out int count);
                _approachingActors[actorId] = count + 1;
            }
            else if (_approachingActors.TryGetValue(actorId, out int count))
            {
                if (count <= 1) _approachingActors.Remove(actorId);
                else _approachingActors[actorId] = count - 1;
            }
        }

        public bool TryStoreGift(Gift gift)
        {
            if (gift == null || !gift.isActiveAndEnabled) return false;
            if (gift.IsCarried) return false;

            EnsureInitialized();
            PruneStorage();
            for (int i = 0; i < _storedGifts.Length; i++)
            {
                if (_storedGifts[i] == gift) return true;
            }

            int pickupSlotCount = PickupSlotCount;
            if (pickupSlotCount == ColorLaneCount)
            {
                int pickupIndex = LaneIndexForKind(gift.Kind);
                int reserveIndex = pickupIndex + pickupSlotCount;
                if (TryPlaceGiftInSlot(gift, pickupIndex)) return true;
                if (TryPlaceGiftInSlot(gift, reserveIndex)) return true;
                return false;
            }

            // Keep small test/custom racks useful even when they do not use the production 4-lane layout.
            for (int i = 0; i < _storedGifts.Length; i++)
                if (TryPlaceGiftInSlot(gift, i)) return true;

            return false;
        }

        public void SetDoorOpenImmediate(bool open)
        {
            EnsureInitialized();
            _forceOpen = open;
            Quaternion left = _leftClosedLocalRotation * Quaternion.Euler(0f, open ? _openAngle : 0f, 0f);
            Quaternion right = _rightClosedLocalRotation * Quaternion.Euler(0f, open ? -_openAngle : 0f, 0f);
            SetDoorRotation(_leftDoorPivot, _leftDoorBody, left);
            SetDoorRotation(_rightDoorPivot, _rightDoorBody, right);
        }

        public void ReleaseDoorPreviewOverride()
        {
            _forceOpen = false;
        }

        private void Awake()
        {
            EnsureInitialized();
        }

        private void Update()
        {
            PruneStorage();
            TryBeginReserveRefills();
        }

        private void FixedUpdate()
        {
            EnsureInitialized();
            bool open = DoorsRequestedOpen;
            RotateDoor(_leftDoorPivot, _leftDoorBody, _leftClosedLocalRotation, open ? _openAngle : 0f);
            RotateDoor(_rightDoorPivot, _rightDoorBody, _rightClosedLocalRotation, open ? -_openAngle : 0f);
        }

        private void EnsureInitialized()
        {
            if (_initialized) return;
            _leftClosedLocalRotation = _leftDoorPivot != null ? _leftDoorPivot.localRotation : Quaternion.identity;
            _rightClosedLocalRotation = _rightDoorPivot != null ? _rightDoorPivot.localRotation : Quaternion.identity;
            _storedGifts = new Gift[Capacity];
            _refillInProgress = new bool[PickupSlotCount];
            _initialized = true;
        }

        private int PickupSlotCount => Capacity >= 2 && Capacity % 2 == 0 ? Capacity / 2 : 0;

        private bool IsReserveSlot(int index)
        {
            int pickupSlotCount = PickupSlotCount;
            return pickupSlotCount > 0 && index >= pickupSlotCount;
        }

        private void RotateDoor(Transform pivot, Rigidbody body, Quaternion closedLocal, float angle)
        {
            if (pivot == null) return;
            Quaternion targetLocal = closedLocal * Quaternion.Euler(0f, angle, 0f);
            Quaternion targetWorld = pivot.parent != null ? pivot.parent.rotation * targetLocal : targetLocal;
            Quaternion current = body != null ? body.rotation : pivot.rotation;
            Quaternion next = Quaternion.RotateTowards(current, targetWorld, _doorSpeed * Time.fixedDeltaTime);
            if (body != null) body.MoveRotation(next);
            else pivot.rotation = next;
        }

        private static void SetDoorRotation(Transform pivot, Rigidbody body, Quaternion localRotation)
        {
            if (pivot == null) return;
            pivot.localRotation = localRotation;
            if (body != null) body.rotation = pivot.rotation;
        }

        private void PruneStorage()
        {
            if (_storedGifts == null || _storageSlots == null) return;
            float maxDistanceSqr = _releaseDistance * _releaseDistance;
            for (int i = 0; i < _storedGifts.Length; i++)
            {
                if (i < PickupSlotCount && _refillInProgress[i]) continue;
                Gift gift = _storedGifts[i];
                if (gift == null) continue;
                Transform slot = i < _storageSlots.Length ? _storageSlots[i] : null;
                if (gift.IsCarried || slot == null ||
                    (gift.transform.position - slot.position).sqrMagnitude > maxDistanceSqr)
                    _storedGifts[i] = null;
            }
        }

        private void TryBeginReserveRefills()
        {
            int pickupSlotCount = PickupSlotCount;
            if (pickupSlotCount == 0 || _refillInProgress == null) return;

            for (int pickupIndex = 0; pickupIndex < pickupSlotCount; pickupIndex++)
            {
                if (_refillInProgress[pickupIndex] || _storedGifts[pickupIndex] != null) continue;

                int reserveIndex = pickupIndex + pickupSlotCount;
                Gift reserveGift = _storedGifts[reserveIndex];
                if (reserveGift == null || reserveGift.IsCarried || _storageSlots[pickupIndex] == null) continue;

                _storedGifts[reserveIndex] = null;
                _storedGifts[pickupIndex] = reserveGift;
                _refillInProgress[pickupIndex] = true;
                StartCoroutine(RefillPickupSlot(reserveGift, pickupIndex));
            }
        }

        private System.Collections.IEnumerator RefillPickupSlot(Gift gift, int pickupIndex)
        {
            yield return new WaitForSeconds(_refillDelay);
            if (gift == null || gift.IsCarried)
            {
                FinishRefill(pickupIndex, gift, false);
                yield break;
            }

            Transform targetSlot = _storageSlots[pickupIndex];
            Transform giftTransform = gift.transform;
            Vector3 startPosition = giftTransform.position;
            Quaternion startRotation = giftTransform.rotation;
            CalculateSlotPose(gift, targetSlot, out Vector3 targetPosition, out Quaternion targetRotation);

            Rigidbody body = gift.GetComponent<Rigidbody>();
            if (body != null)
            {
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.isKinematic = true;
            }

            float elapsed = 0f;
            while (gift != null && elapsed < _refillDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _refillDuration);
                float eased = t * t * (3f - 2f * t);
                giftTransform.position = Vector3.Lerp(startPosition, targetPosition, eased);
                giftTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, eased);
                yield return null;
            }

            if (gift == null)
            {
                FinishRefill(pickupIndex, null, false);
                yield break;
            }

            giftTransform.SetPositionAndRotation(targetPosition, targetRotation);
            if (body != null)
            {
                body.isKinematic = false;
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.Sleep();
            }

            FinishRefill(pickupIndex, gift, true);
        }

        private void FinishRefill(int pickupIndex, Gift gift, bool completed)
        {
            if (!completed && _storedGifts[pickupIndex] == gift) _storedGifts[pickupIndex] = null;
            _refillInProgress[pickupIndex] = false;
        }

        private void PlaceGiftAtSlot(Gift gift, Transform slot)
        {
            CalculateSlotPose(gift, slot, out Vector3 position, out Quaternion rotation);
            gift.transform.SetPositionAndRotation(position, rotation);

            if (gift.TryGetComponent(out Rigidbody body))
            {
                int slotIndex = System.Array.IndexOf(_storageSlots, slot);
                bool shouldBeKinematic = IsReserveSlot(slotIndex);
                if (!body.isKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }

                body.isKinematic = shouldBeKinematic;
                if (!shouldBeKinematic)
                {
                    body.linearVelocity = Vector3.zero;
                    body.angularVelocity = Vector3.zero;
                }
                body.Sleep();
            }
        }

        private bool TryPlaceGiftInSlot(Gift gift, int slotIndex)
        {
            if (slotIndex < 0 || slotIndex >= _storedGifts.Length ||
                _storedGifts[slotIndex] != null || _storageSlots[slotIndex] == null)
                return false;

            _storedGifts[slotIndex] = gift;
            PlaceGiftAtSlot(gift, _storageSlots[slotIndex]);
            return true;
        }

        private static void CalculateSlotPose(
            Gift gift,
            Transform slot,
            out Vector3 position,
            out Quaternion rotation)
        {
            Transform giftTransform = gift.transform;
            Vector3 originalPosition = giftTransform.position;
            Quaternion originalRotation = giftTransform.rotation;

            rotation = slot.rotation;
            giftTransform.SetPositionAndRotation(slot.position, rotation);
            position = slot.position;
            Renderer[] renderers = gift.GetComponentsInChildren<Renderer>(true);
            if (renderers.Length > 0)
            {
                Bounds bounds = renderers[0].bounds;
                for (int i = 1; i < renderers.Length; i++) bounds.Encapsulate(renderers[i].bounds);
                position += Vector3.up * (slot.position.y - bounds.min.y + 0.015f);
            }

            giftTransform.SetPositionAndRotation(originalPosition, originalRotation);
        }
    }
}
