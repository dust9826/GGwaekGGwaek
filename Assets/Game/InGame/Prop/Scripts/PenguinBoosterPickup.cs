using System.Collections;
using UnityEngine;
using UnityEngine.Events;

namespace PPack
{
    /// <summary>펭귄이 닿으면 Fire Bullet 연출과 속도 부스트를 주는 재생성형 픽업.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SphereCollider))]
    public sealed class PenguinBoosterPickup : MonoBehaviour
    {
        [Header("효과")]
        [SerializeField] private GameObject _fireBulletVfx;
        [SerializeField, Range(1f, 3f)] private float _speedMultiplier = 1.6f;
        [SerializeField, Min(0.1f)] private float _durationSeconds = 5f;
        [SerializeField, Min(0f)] private float _respawnSeconds = 8f;

        [Header("표현")]
        [SerializeField] private Transform _visualRoot;
        [SerializeField] private float _rotationDegreesPerSecond = 75f;
        [SerializeField, Min(0f)] private float _bobAmplitude = 0.13f;
        [SerializeField, Min(0.01f)] private float _bobCyclesPerSecond = 0.75f;
        [SerializeField] private UnityEvent _collected = new UnityEvent();

        private Collider _trigger;
        private Vector3 _visualBaseLocalPosition;
        private bool _isAvailable = true;
        private Coroutine _respawnRoutine;

        public bool IsAvailable => _isAvailable;

        private void Awake()
        {
            _trigger = GetComponent<Collider>();
            _trigger.isTrigger = true;
            if (_visualRoot != null)
                _visualBaseLocalPosition = _visualRoot.localPosition;
        }

        private void Update()
        {
            if (!_isAvailable || _visualRoot == null)
                return;

            _visualRoot.Rotate(Vector3.up,
                _rotationDegreesPerSecond * Time.deltaTime, Space.Self);
            float bob = Mathf.Sin(Time.time * Mathf.PI * 2f * _bobCyclesPerSecond) *
                        _bobAmplitude;
            Vector3 localPosition = _visualBaseLocalPosition;
            localPosition.y += bob;
            _visualRoot.localPosition = localPosition;
        }

        private void OnTriggerEnter(Collider other)
        {
            PenguinLocomotion locomotion = other.GetComponentInParent<PenguinLocomotion>();
            if (locomotion != null)
                TryCollect(locomotion);
        }

        /// <summary>물리 트리거와 테스트가 공유하는 수집 진입점.</summary>
        public bool TryCollect(PenguinLocomotion locomotion)
        {
            if (!_isAvailable || locomotion == null)
                return false;

            PenguinBoostReceiver receiver = locomotion.GetComponent<PenguinBoostReceiver>();
            if (receiver == null)
                receiver = locomotion.gameObject.AddComponent<PenguinBoostReceiver>();

            receiver.Activate(_fireBulletVfx, _speedMultiplier, _durationSeconds);
            _collected.Invoke();
            SetAvailable(false);

            if (_respawnRoutine != null)
                StopCoroutine(_respawnRoutine);
            if (_respawnSeconds > 0f)
                _respawnRoutine = StartCoroutine(RespawnAfterDelay());

            return true;
        }

        private IEnumerator RespawnAfterDelay()
        {
            yield return new WaitForSeconds(_respawnSeconds);
            _respawnRoutine = null;
            SetAvailable(true);
        }

        private void SetAvailable(bool available)
        {
            _isAvailable = available;
            if (_trigger != null)
                _trigger.enabled = available;
            if (_visualRoot != null)
            {
                _visualRoot.gameObject.SetActive(available);
                if (available)
                    _visualRoot.localPosition = _visualBaseLocalPosition;
            }
        }

        private void OnDisable()
        {
            if (_respawnRoutine != null)
            {
                StopCoroutine(_respawnRoutine);
                _respawnRoutine = null;
            }
        }

        private void Reset()
        {
            Collider pickupCollider = GetComponent<Collider>();
            if (pickupCollider != null)
                pickupCollider.isTrigger = true;
        }
    }
}
