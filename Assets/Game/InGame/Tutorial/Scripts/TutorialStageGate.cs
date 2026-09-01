using System.Collections;
using UnityEngine;

namespace PPack
{
    /// <summary>해당 튜토리얼 단계를 끝낼 때까지 다음 훈련실 통로를 막는다.</summary>
    [DisallowMultipleComponent]
    public sealed class TutorialStageGate : MonoBehaviour
    {
        private static readonly Color LockedColor = new Color(0.64f, 0.90f, 1f, 0.22f);
        private static readonly Color UnlockedColor = new Color(1f, 0.88f, 0.52f, 0.34f);

        [SerializeField] private int _unlockAfterStep;
        [SerializeField] private BoxCollider _barrier;
        [SerializeField] private Transform _fieldPulseRoot;
        [SerializeField] private Renderer[] _fieldRenderers;
        [SerializeField] private ParticleSystem _lockParticles;
        [SerializeField] private ParticleSystem _unlockParticles;

        private MaterialPropertyBlock _propertyBlock;
        private Coroutine _unlockRoutine;

        public int UnlockAfterStep => _unlockAfterStep;
        public bool IsLocked => _barrier != null && _barrier.enabled;

        private void Awake()
        {
            _propertyBlock = new MaterialPropertyBlock();
            SetCompletedStep(-1);
        }

        private void Update()
        {
            if (!IsLocked) return;

            float pulse01 = Mathf.Sin(Time.unscaledTime * 1.35f) * 0.5f + 0.5f;
            if (_fieldPulseRoot != null)
                _fieldPulseRoot.localScale = new Vector3(1f, Mathf.Lerp(0.997f, 1.006f, pulse01), 1f);
            SetFieldProperties(LockedColor, Mathf.Lerp(0.15f, 0.22f, pulse01));
        }

        public void SetCompletedStep(int completedStep)
        {
            bool shouldLock = completedStep < _unlockAfterStep;
            bool wasLocked = IsLocked;
            if (_barrier != null) _barrier.enabled = shouldLock;

            if (shouldLock)
            {
                if (_unlockRoutine != null)
                {
                    StopCoroutine(_unlockRoutine);
                    _unlockRoutine = null;
                }
                SetFieldVisible(true);
                SetFieldProperties(LockedColor, 0.18f);
                if (_lockParticles != null)
                {
                    ParticleSystem.MainModule main = _lockParticles.main;
                    main.startColor = new ParticleSystem.MinMaxGradient(
                        LockedColor, Color.Lerp(LockedColor, Color.white, 0.45f));
                    if (!_lockParticles.isPlaying) _lockParticles.Play(true);
                }
                return;
            }

            if (!wasLocked)
            {
                if (_lockParticles != null)
                    _lockParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                SetFieldVisible(false);
                return;
            }

            if (_lockParticles != null)
                _lockParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            if (_unlockParticles != null)
            {
                ParticleSystem.MainModule unlockMain = _unlockParticles.main;
                unlockMain.startColor = new ParticleSystem.MinMaxGradient(
                    UnlockedColor, Color.Lerp(UnlockedColor, Color.white, 0.55f));
                _unlockParticles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                _unlockParticles.Play(true);
            }
            if (_unlockRoutine != null) StopCoroutine(_unlockRoutine);
            _unlockRoutine = StartCoroutine(AnimateUnlock());
        }

        private IEnumerator AnimateUnlock()
        {
            const float duration = 0.42f;
            float elapsed = 0f;
            SetFieldVisible(true);
            while (elapsed < duration)
            {
                float t = Mathf.Clamp01(elapsed / duration);
                float eased = 1f - Mathf.Pow(1f - t, 3f);
                SetFieldProperties(UnlockedColor, Mathf.Lerp(0.34f, 0f, eased));
                if (_fieldPulseRoot != null)
                    _fieldPulseRoot.localScale = new Vector3(
                        1f + eased * 0.08f,
                        1f - eased * 0.10f,
                        1f + eased * 0.08f);
                elapsed += Time.unscaledDeltaTime;
                yield return null;
            }

            SetFieldVisible(false);
            if (_fieldPulseRoot != null) _fieldPulseRoot.localScale = Vector3.one;
            _unlockRoutine = null;
        }

        private void SetFieldVisible(bool visible)
        {
            if (_fieldRenderers == null) return;
            foreach (Renderer fieldRenderer in _fieldRenderers)
                if (fieldRenderer != null) fieldRenderer.enabled = visible;
        }

        private void SetFieldProperties(Color color, float alpha)
        {
            if (_fieldRenderers == null) return;
            if (_propertyBlock == null) _propertyBlock = new MaterialPropertyBlock();
            color.a = alpha;
            Color glow = Color.Lerp(color, Color.white, 0.18f) * 1.15f;
            glow.a = 1f;
            foreach (Renderer fieldRenderer in _fieldRenderers)
            {
                if (fieldRenderer == null) continue;
                fieldRenderer.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor("_Color", color);
                _propertyBlock.SetColor("_ShapeColor", color);
                _propertyBlock.SetColor("_BaseColor", color);
                _propertyBlock.SetColor("_GlowColor", glow);
                _propertyBlock.SetFloat("_Alpha", alpha);
                fieldRenderer.SetPropertyBlock(_propertyBlock);
            }
        }
    }
}
