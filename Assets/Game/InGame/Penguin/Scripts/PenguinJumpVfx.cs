using UnityEngine;
using UnityEngine.Rendering;

namespace PPack
{
    /// <summary>실제로 발동한 점프마다 발밑에 단발 파티클을 재생한다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PenguinLocomotion))]
    [RequireComponent(typeof(CapsuleCollider))]
    public sealed class PenguinJumpVfx : MonoBehaviour
    {
        [SerializeField] private GameObject _effectPrefab;
        [SerializeField, Min(0f)] private float _groundOffsetM = 0.01f;

        private PenguinLocomotion _locomotion;
        private CapsuleCollider _capsule;

        private void Awake()
        {
            _locomotion = GetComponent<PenguinLocomotion>();
            _capsule = GetComponent<CapsuleCollider>();
        }

        private void OnEnable()
        {
            if (_locomotion != null) _locomotion.Jumped += Play;
        }

        private void OnDisable()
        {
            if (_locomotion != null) _locomotion.Jumped -= Play;
        }

        private void Play()
        {
            if (_effectPrefab == null || SystemInfo.graphicsDeviceType == GraphicsDeviceType.Null)
                return;

            Bounds bounds = _capsule.bounds;
            Vector3 position = new(bounds.center.x, bounds.min.y + _groundOffsetM, bounds.center.z);
            Vector3 footForward = Vector3.ProjectOnPlane(transform.forward, Vector3.up);
            if (footForward.sqrMagnitude <= 0.0001f) footForward = Vector3.forward;

            Quaternion rotation = Quaternion.LookRotation(Vector3.up, footForward.normalized);
            GameObject effect = Instantiate(_effectPrefab, position, rotation);
            effect.name = "FX_PenguinJump";

            float lifetime = 0.5f;
            foreach (ParticleSystem particle in effect.GetComponentsInChildren<ParticleSystem>(true))
            {
                ParticleSystem.MainModule main = particle.main;
                lifetime = Mathf.Max(lifetime,
                    main.startDelay.constantMax + main.duration + main.startLifetime.constantMax);
            }

            Destroy(effect, lifetime);
        }
    }
}
