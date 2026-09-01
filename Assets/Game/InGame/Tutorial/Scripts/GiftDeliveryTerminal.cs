using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// A low, kick-fed parcel terminal. Terminals sharing a channel send gifts to one another.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftDeliveryTerminal : MonoBehaviour
    {
        private static readonly List<GiftDeliveryTerminal> ActiveTerminals = new();
        private static readonly Dictionary<Gift, float> OutputProtectionUntil = new();

        [Header("Delivery Network")]
        [SerializeField] private string _channelId = "TutorialParcelLine";
        [SerializeField, Min(0f)] private float _travelDelay = 0.8f;

        [Header("Anchors")]
        [SerializeField] private Transform _intakeAnchor;
        [SerializeField] private Transform _tunnelAnchor;
        [SerializeField] private Transform _outputAnchor;
        [SerializeField] private Gift _giftPrefab;

        [Header("Output Feel")]
        [SerializeField] private ParticleSystem[] _outputBurstVfx;
        [SerializeField] private AudioSource _outputAudio;
        [SerializeField] private AudioClip _outputClip;
        [SerializeField, Min(0.05f)] private float _intakeDuration = 0.48f;
        [SerializeField, Min(0.05f)] private float _popDuration = 0.34f;
        [SerializeField] private float _launchSpeed = 2.1f;
        [SerializeField] private float _launchUpSpeed = 0.75f;

        private bool _isIntaking;

        public string ChannelId => _channelId;
        public Transform EntryAnchor => _intakeAnchor;
        public Transform TunnelAnchor => _tunnelAnchor;
        public event System.Action<GiftDeliveryTerminal> GiftIntakeCompleted;
        public event System.Action<GiftDeliveryTerminal, Gift> GiftOutputCompleted;

        private void OnEnable()
        {
            if (!ActiveTerminals.Contains(this)) ActiveTerminals.Add(this);
        }

        private void OnDisable()
        {
            ActiveTerminals.Remove(this);
        }

        public bool TryAccept(Gift gift)
        {
            if (_isIntaking || gift == null || gift.IsCarried || !_giftPrefab || !_tunnelAnchor || !_outputAnchor)
                return false;

            if (OutputProtectionUntil.TryGetValue(gift, out float protectedUntil))
            {
                if (Time.time < protectedUntil) return false;
                OutputProtectionUntil.Remove(gift);
            }

            StartCoroutine(TransferRoutine(gift));
            return true;
        }

        private IEnumerator TransferRoutine(Gift gift)
        {
            _isIntaking = true;
            GiftDeliveryTerminal receiver = FindReceiver();
            EGiftBoxKind kind = gift.Kind;
            int value = gift.Value;

            Rigidbody body = gift.GetComponent<Rigidbody>();
            if (body != null)
            {
                body.linearVelocity = Vector3.zero;
                body.angularVelocity = Vector3.zero;
                body.isKinematic = true;
            }

            Collider[] colliders = gift.GetComponentsInChildren<Collider>(true);
            foreach (Collider target in colliders) target.enabled = false;

            Transform giftTransform = gift.transform;
            Vector3 startPosition = giftTransform.position;
            Quaternion startRotation = giftTransform.rotation;
            Vector3 startScale = giftTransform.localScale;
            Vector3 targetPosition = _tunnelAnchor.position;
            Quaternion targetRotation = _tunnelAnchor.rotation;

            float elapsed = 0f;
            while (gift != null && elapsed < _intakeDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _intakeDuration);
                float eased = t * t * (3f - 2f * t);
                giftTransform.position = Vector3.Lerp(startPosition, targetPosition, eased);
                giftTransform.rotation = Quaternion.Slerp(startRotation, targetRotation, eased);
                giftTransform.localScale = Vector3.Lerp(startScale, startScale * 0.06f, eased);
                yield return null;
            }

            if (gift != null) Destroy(gift.gameObject);
            GiftIntakeCompleted?.Invoke(this);
            yield return new WaitForSeconds(_travelDelay);

            if (receiver == null || !receiver.isActiveAndEnabled) receiver = this;
            receiver.StartCoroutine(receiver.OutputRoutine(kind, value));
            _isIntaking = false;
        }

        private GiftDeliveryTerminal FindReceiver()
        {
            foreach (GiftDeliveryTerminal terminal in ActiveTerminals)
            {
                if (terminal != null && terminal != this && terminal.isActiveAndEnabled &&
                    terminal._channelId == _channelId)
                    return terminal;
            }

            return this;
        }

        private IEnumerator OutputRoutine(EGiftBoxKind kind, int value)
        {
            PlayOutputFeel();

            Gift spawned = Instantiate(_giftPrefab, _outputAnchor.position, _outputAnchor.rotation);
            spawned.name = _giftPrefab.name + "_Delivered";
            spawned.SetKind(kind);
            spawned.SetValue(value);
            spawned.SetCarried(false);
            OutputProtectionUntil[spawned] = Time.time + 1.5f;

            Rigidbody body = spawned.GetComponent<Rigidbody>();
            if (body == null) body = spawned.gameObject.AddComponent<Rigidbody>();
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.isKinematic = true;

            Collider[] colliders = spawned.GetComponentsInChildren<Collider>(true);
            foreach (Collider target in colliders) target.enabled = false;

            Transform giftTransform = spawned.transform;
            Vector3 finalScale = giftTransform.localScale;
            Vector3 startPosition = _outputAnchor.position + transform.up * 0.08f;
            Vector3 endPosition = startPosition + OutputDirection() * 0.72f + transform.up * 0.12f;
            giftTransform.position = startPosition;
            giftTransform.localScale = finalScale * 0.04f;

            float elapsed = 0f;
            while (spawned != null && elapsed < _popDuration)
            {
                elapsed += Time.deltaTime;
                float t = Mathf.Clamp01(elapsed / _popDuration);
                float moveT = 1f - Mathf.Pow(1f - t, 3f);
                float scaleT = t < 0.72f
                    ? Mathf.Lerp(0.04f, 1.12f, Mathf.SmoothStep(0f, 1f, t / 0.72f))
                    : Mathf.Lerp(1.12f, 1f, (t - 0.72f) / 0.28f);
                giftTransform.position = Vector3.Lerp(startPosition, endPosition, moveT);
                giftTransform.localScale = finalScale * scaleT;
                yield return null;
            }

            if (spawned == null) yield break;
            giftTransform.localScale = finalScale;
            foreach (Collider target in colliders)
                if (target != null) target.enabled = true;

            body.isKinematic = false;
            body.mass = 2f;
            body.linearDamping = 0.8f;
            body.angularDamping = 0.8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = OutputDirection() * _launchSpeed + transform.up * _launchUpSpeed;
            body.angularVelocity = Random.insideUnitSphere * 1.6f;
            GiftOutputCompleted?.Invoke(this, spawned);
        }

        private Vector3 OutputDirection()
        {
            Vector3 direction = -transform.forward;
            direction.y = 0f;
            return direction.sqrMagnitude > 0.001f ? direction.normalized : Vector3.forward;
        }

        private void PlayOutputFeel()
        {
            if (_outputBurstVfx != null)
            {
                foreach (ParticleSystem particles in _outputBurstVfx)
                {
                    if (particles == null) continue;
                    particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    particles.Play(true);
                }
            }

            if (_outputAudio == null || _outputClip == null) return;
            _outputAudio.pitch = Random.Range(0.96f, 1.04f);
            _outputAudio.PlayOneShot(_outputClip, 0.9f);
        }

#if UNITY_EDITOR
        public void EditorConfigure(
            Transform intakeAnchor,
            Transform tunnelAnchor,
            Transform outputAnchor,
            Gift giftPrefab,
            ParticleSystem[] outputBurstVfx,
            AudioSource outputAudio,
            AudioClip outputClip)
        {
            _intakeAnchor = intakeAnchor;
            _tunnelAnchor = tunnelAnchor;
            _outputAnchor = outputAnchor;
            _giftPrefab = giftPrefab;
            _outputBurstVfx = outputBurstVfx;
            _outputAudio = outputAudio;
            _outputClip = outputClip;
            UnityEditor.EditorUtility.SetDirty(this);
        }
#endif
    }
}
