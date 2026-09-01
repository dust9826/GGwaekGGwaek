using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 펭귄 한 마리의 임시 부스터 상태를 소유한다. 재획득하면 VFX를 중복 생성하지 않고
    /// 남은 시간만 새로 채운다.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PenguinLocomotion))]
    public sealed class PenguinBoostReceiver : MonoBehaviour
    {
        private const string PreferredAnchorName = "BodyPivot";

        [Header("부스터 VFX 부착")]
        [SerializeField] private Transform _vfxAnchor;
        [SerializeField] private Vector3 _vfxLocalPosition = new Vector3(0f, 0.55f, -0.5f);
        [SerializeField] private Vector3 _vfxLocalEuler = Vector3.zero;
        [SerializeField, Min(0.01f)] private float _vfxLocalScale = 0.42f;

        private PenguinLocomotion _locomotion;
        private GameObject _activeVfx;
        private float _expiresAt;

        public bool IsBoosted { get; private set; }
        public float RemainingSeconds => IsBoosted ? Mathf.Max(0f, _expiresAt - Time.time) : 0f;

        private void Awake()
        {
            _locomotion = GetComponent<PenguinLocomotion>();
            if (_vfxAnchor == null)
                _vfxAnchor = FindDescendant(transform, PreferredAnchorName) ?? transform;
        }

        private void Update()
        {
            if (IsBoosted && Time.time >= _expiresAt)
                StopBoost();
        }

        /// <summary>부스트를 시작하거나, 이미 활성화된 부스트의 시간을 갱신한다.</summary>
        public void Activate(GameObject fireBulletVfxPrefab, float speedMultiplier,
            float durationSeconds)
        {
            if (_locomotion == null)
                _locomotion = GetComponent<PenguinLocomotion>();

            float safeMultiplier = Mathf.Clamp(speedMultiplier, 1f, 3f);
            float safeDuration = Mathf.Max(0.1f, durationSeconds);
            _locomotion.SetSpeedBoostMultiplier(safeMultiplier);
            _expiresAt = Time.time + safeDuration;
            IsBoosted = true;

            if (_activeVfx == null && fireBulletVfxPrefab != null)
                CreateVfx(fireBulletVfxPrefab);
            else if (_activeVfx != null)
                RestartVfx(_activeVfx);
        }

        public void StopBoost()
        {
            if (_locomotion != null)
                _locomotion.SetSpeedBoostMultiplier(1f);

            IsBoosted = false;
            _expiresAt = 0f;

            if (_activeVfx != null)
            {
                Destroy(_activeVfx);
                _activeVfx = null;
            }
        }

        private void OnDisable()
        {
            StopBoost();
        }

        private void CreateVfx(GameObject prefab)
        {
            Transform anchor = _vfxAnchor != null ? _vfxAnchor : transform;
            _activeVfx = Instantiate(prefab, anchor, false);
            _activeVfx.name = "VFX_Booster_FireBullet";
            Transform vfxTransform = _activeVfx.transform;
            vfxTransform.localPosition = _vfxLocalPosition;
            vfxTransform.localRotation = Quaternion.Euler(_vfxLocalEuler);
            vfxTransform.localScale = Vector3.one * _vfxLocalScale;
            RestartVfx(_activeVfx);
        }

        private static void RestartVfx(GameObject root)
        {
            Animator[] animators = root.GetComponentsInChildren<Animator>(true);
            foreach (Animator animator in animators)
            {
                animator.Rebind();
                animator.Update(0f);
            }

            TrailRenderer[] trails = root.GetComponentsInChildren<TrailRenderer>(true);
            foreach (TrailRenderer trail in trails)
                trail.Clear();

            ParticleSystem[] particles = root.GetComponentsInChildren<ParticleSystem>(true);
            foreach (ParticleSystem particle in particles)
            {
                particle.Clear(true);
                particle.Play(true);
            }
        }

        private static Transform FindDescendant(Transform root, string targetName)
        {
            foreach (Transform child in root)
            {
                if (child.name == targetName)
                    return child;

                Transform nested = FindDescendant(child, targetName);
                if (nested != null)
                    return nested;
            }

            return null;
        }
    }
}

