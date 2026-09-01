using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    [RequireComponent(typeof(Rigidbody), typeof(Collider))]
    [DisallowMultipleComponent]
    public sealed class BreakableHydrant : MonoBehaviour
    {
        [Header("Impact")]
        [SerializeField, Min(1f)] private float _breakMomentumKgMps = 90f;
        [SerializeField, Min(0f)] private float _brokenTopImpulse = 4.5f;

        [Header("Presentation")]
        [SerializeField] private GameObject _intactVisual;
        [SerializeField] private GameObject _brokenBase;
        [SerializeField] private Rigidbody _brokenTop;
        [SerializeField] private ParticleSystem _waterJet;
        [SerializeField] private Transform _iceGrowth;
        [SerializeField, Min(0f)] private float _freezeDelaySeconds = 0.75f;
        [SerializeField, Min(0.1f)] private float _freezeDurationSeconds = 2.2f;
        [SerializeField, Min(0.1f)] private float _waterDurationSeconds = 4.6f;

        private Collider _collider;
        private bool _isBroken;
        private float _brokenElapsed;
        [SerializeField] private Vector3 _iceFullScale = Vector3.one;

        public bool IsBroken => _isBroken;
        public float FreezeAmount => Mathf.Clamp01(
            (_brokenElapsed - _freezeDelaySeconds) / _freezeDurationSeconds);

        public void Configure(
            GameObject intactVisual,
            GameObject brokenBase,
            Rigidbody brokenTop,
            ParticleSystem waterJet,
            Transform iceGrowth)
        {
            _intactVisual = intactVisual;
            _brokenBase = brokenBase;
            _brokenTop = brokenTop;
            _waterJet = waterJet;
            _iceGrowth = iceGrowth;
            _iceFullScale = iceGrowth != null ? iceGrowth.localScale : Vector3.one;
            CacheInitialState();
        }

        private void Awake()
        {
            _collider = GetComponent<Collider>();
            GetComponent<Rigidbody>().isKinematic = true;
            CacheInitialState();
        }

        private void CacheInitialState()
        {
            if (_iceGrowth != null)
            {
                _iceGrowth.localScale = new Vector3(
                    _iceFullScale.x * 0.08f,
                    _iceFullScale.y * 0.02f,
                    _iceFullScale.z * 0.08f);
                _iceGrowth.gameObject.SetActive(false);
            }

            if (!_isBroken)
            {
                if (_intactVisual != null) _intactVisual.SetActive(true);
                if (_brokenBase != null) _brokenBase.SetActive(false);
                if (_brokenTop != null)
                {
                    _brokenTop.isKinematic = true;
                    _brokenTop.gameObject.SetActive(false);
                }
            }
        }

        private void Update()
        {
            if (!_isBroken) return;

            _brokenElapsed += Time.deltaTime;
            float freeze = FreezeAmount;
            if (_iceGrowth != null && freeze > 0f)
            {
                _iceGrowth.gameObject.SetActive(true);
                float eased = freeze * freeze * (3f - 2f * freeze);
                _iceGrowth.localScale = Vector3.Scale(
                    _iceFullScale,
                    new Vector3(Mathf.Lerp(0.08f, 1f, eased), Mathf.Lerp(0.02f, 1f, eased), Mathf.Lerp(0.08f, 1f, eased)));
            }

            if (_waterJet != null && _brokenElapsed >= _waterDurationSeconds && _waterJet.isEmitting)
                _waterJet.Stop(true, ParticleSystemStopBehavior.StopEmitting);
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_isBroken) return;
            if (!ImpactMomentum.TryCompute(collision, out float momentum, out ContactPoint contact)) return;
            if (momentum < _breakMomentumKgMps) return;

            Vector3 impulse = -contact.normal * _brokenTopImpulse + Vector3.up * (_brokenTopImpulse * 0.65f);
            BreakNow(impulse);
        }

        public void BreakNow(Vector3 impulse)
        {
            if (_isBroken) return;
            _isBroken = true;
            _brokenElapsed = 0f;

            if (_collider == null) _collider = GetComponent<Collider>();
            if (_collider != null) _collider.enabled = false;
            if (_intactVisual != null) _intactVisual.SetActive(false);
            if (_brokenBase != null) _brokenBase.SetActive(true);

            if (_brokenTop != null)
            {
                _brokenTop.gameObject.SetActive(true);
                _brokenTop.transform.SetParent(null, true);
                _brokenTop.isKinematic = false;
                _brokenTop.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
                _brokenTop.AddForce(impulse, ForceMode.Impulse);
                _brokenTop.AddTorque(Random.onUnitSphere * _brokenTopImpulse, ForceMode.Impulse);
            }

            if (_waterJet != null && SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                _waterJet.gameObject.SetActive(true);
                _waterJet.Play(true);
            }
        }
    }
}
