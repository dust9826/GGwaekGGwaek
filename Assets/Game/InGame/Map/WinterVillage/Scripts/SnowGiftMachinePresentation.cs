using System.Collections;
using Fusion;
using MoreMountains.Feedbacks;
using UnityEngine;
using UnityEngine.VFX;

namespace PPack
{
    /// <summary>
    /// 눈덩이가 빨려 들어가고, 기계가 꿀꺽 반응한 뒤, 선물이 출력되는 한 사이클의 표현을 소유한다.
    /// 눈덩이 루트는 질량과 네트워크 표현이 스케일을 소유하므로 Feel은 별도 복제 비주얼만 움직인다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowGiftMachinePresentation : MonoBehaviour
    {
        [Header("Anchors and VFX")]
        [SerializeField] private Transform _intakeAnchor;
        [SerializeField] private Transform _giftOutputAnchor;
        [SerializeField] private VisualEffect _suctionVfx;

        [Header("Feel")]
        [SerializeField] private Transform _machineMotionRoot;
        [SerializeField] private Transform _intakeVisual;
        [SerializeField] private MeshFilter _intakeVisualMeshFilter;
        [SerializeField] private MeshRenderer _intakeVisualRenderer;
        [SerializeField] private MMF_Player _intakeFeedback;
        [SerializeField] private MMF_Player _digestFeedback;
        [SerializeField] private Transform _giftPopDriver;
        [SerializeField] private MMF_Player _giftPopFeedback;
        [SerializeField] private ParticleSystem[] _giftBurstVfx;

        [Header("Suction State Feedback")]
        [SerializeField] private ParticleSystem[] _airflowVfx;
        [SerializeField] private ParticleSystem _powerOnVfx;
        [SerializeField] private ParticleSystem _powerOffVfx;

        [Header("Audio")]
        [SerializeField] private AudioSource _suctionAudioSource;
        [SerializeField] private AudioSource _giftOutputAudioSource;
        [SerializeField] private AudioClip _suctionClip;
        [SerializeField] private AudioClip _giftOutputClip;
        [SerializeField, Range(0f, 1f)] private float _suctionVolume = 0.62f;
        [SerializeField, Range(0f, 1f)] private float _giftOutputVolume = 0.72f;

        [Header("Gift")]
        [SerializeField] private Gift _giftPrefab;

        [Tooltip("판이 얻은 증강. 비어 있으면 효과가 없고 기존 동작 그대로다.")]
        [SerializeField] private AugmentLoadout _augments;
        [SerializeField, Min(0.05f)] private float _intakeDuration = 0.72f;
        [SerializeField, Min(0.05f)] private float _cycleDuration = 1.68f;
        [SerializeField] private Vector3 _giftLaunchVelocity = new Vector3(0f, 2.8f, 1.7f);
        [SerializeField, Min(0f)] private float _giftSpin = 4.5f;
        [SerializeField] private Transform _giftLandingAnchor;
        [SerializeField, Min(0.1f)] private float _giftLandingFlightSeconds = 1.8f;

        [Header("Snow Delivery Conversion")]
        [SerializeField] private SnowCpuStage _conversionStage;
        [SerializeField] private bool _useGrowthStageGiftKind;
        [SerializeField] private GiftNetSpawner _networkGiftSpawner;

        private Renderer[] _hiddenSnowRenderers;
        private Collider[] _disabledSnowColliders;
        private Rigidbody _snowBody;
        private bool _snowBodyWasKinematic;
        private bool _isSuctionActive;
        private bool _isProcessing;
        private bool _hasPendingGiftKind;
        private EGiftBoxKind _pendingGiftKind;
        private Coroutine _processRoutine;
        private Gift _lastSpawnedGift;
        private bool _isNetworkConversion;

        public Transform IntakeAnchor => _intakeAnchor;
        public Transform GiftOutputAnchor => _giftOutputAnchor;
        public VisualEffect SuctionVfx => _suctionVfx;
        public Transform MachineMotionRoot => _machineMotionRoot;
        public Transform IntakeVisual => _intakeVisual;
        public MMF_Player IntakeFeedback => _intakeFeedback;
        public MMF_Player DigestFeedback => _digestFeedback;
        public Transform GiftPopDriver => _giftPopDriver;
        public MMF_Player GiftPopFeedback => _giftPopFeedback;
        public ParticleSystem[] GiftBurstVfx => _giftBurstVfx;
        public ParticleSystem[] AirflowVfx => _airflowVfx;
        public ParticleSystem PowerOnVfx => _powerOnVfx;
        public ParticleSystem PowerOffVfx => _powerOffVfx;
        public AudioSource SuctionAudioSource => _suctionAudioSource;
        public AudioSource GiftOutputAudioSource => _giftOutputAudioSource;
        public AudioClip SuctionClip => _suctionClip;
        public AudioClip GiftOutputClip => _giftOutputClip;
        public Gift GiftPrefab => _giftPrefab;
        public Gift LastSpawnedGift => _lastSpawnedGift;
        public bool IsSuctionActive => _isSuctionActive;
        public bool IsProcessing => _isProcessing;

        public static EGiftBoxKind GiftKindForGrowthStage(ESnowBallGrowthStage stage)
        {
            return stage switch
            {
                ESnowBallGrowthStage.Seed => EGiftBoxKind.Blue,
                ESnowBallGrowthStage.Stage1 => EGiftBoxKind.Blue,
                ESnowBallGrowthStage.Stage2 => EGiftBoxKind.Green,
                ESnowBallGrowthStage.Stage3 => EGiftBoxKind.Yellow,
                ESnowBallGrowthStage.Stage4 => EGiftBoxKind.Red,
                _ => EGiftBoxKind.Blue,
            };
        }

        public void Configure(Transform intakeAnchor, Transform giftOutputAnchor, VisualEffect suctionVfx)
        {
            _intakeAnchor = intakeAnchor;
            _giftOutputAnchor = giftOutputAnchor;
            _suctionVfx = suctionVfx;
            StopSuctionFeedback(true);
        }

        public void ConfigureFeel(
            Transform machineMotionRoot,
            Transform intakeVisual,
            MeshFilter intakeVisualMeshFilter,
            MeshRenderer intakeVisualRenderer,
            MMF_Player intakeFeedback,
            MMF_Player digestFeedback,
            Transform giftPopDriver,
            MMF_Player giftPopFeedback,
            Gift giftPrefab,
            ParticleSystem[] giftBurstVfx)
        {
            _machineMotionRoot = machineMotionRoot;
            _intakeVisual = intakeVisual;
            _intakeVisualMeshFilter = intakeVisualMeshFilter;
            _intakeVisualRenderer = intakeVisualRenderer;
            _intakeFeedback = intakeFeedback;
            _digestFeedback = digestFeedback;
            _giftPopDriver = giftPopDriver;
            _giftPopFeedback = giftPopFeedback;
            _giftPrefab = giftPrefab;
            _giftBurstVfx = giftBurstVfx;
            ResetDrivers();
        }

        public void ConfigureSuctionFeedback(
            ParticleSystem[] airflowVfx,
            ParticleSystem powerOnVfx,
            ParticleSystem powerOffVfx)
        {
            _airflowVfx = airflowVfx;
            _powerOnVfx = powerOnVfx;
            _powerOffVfx = powerOffVfx;
            StopSuctionFeedback(true);
        }

        public void ConfigureAudio(
            AudioSource suctionAudioSource,
            AudioClip suctionClip,
            AudioSource giftOutputAudioSource,
            AudioClip giftOutputClip)
        {
            _suctionAudioSource = suctionAudioSource;
            _suctionClip = suctionClip;
            _giftOutputAudioSource = giftOutputAudioSource;
            _giftOutputClip = giftOutputClip;

            if (_suctionAudioSource != null)
            {
                _suctionAudioSource.playOnAwake = false;
                _suctionAudioSource.loop = false;
                _suctionAudioSource.clip = _suctionClip;
            }

            if (_giftOutputAudioSource != null)
            {
                _giftOutputAudioSource.playOnAwake = false;
                _giftOutputAudioSource.loop = false;
                _giftOutputAudioSource.clip = null;
            }
        }

        public void ConfigureSnowDeliveryConversion(
            SnowCpuStage conversionStage, Transform giftLandingAnchor = null)
        {
            _conversionStage = conversionStage;
            _useGrowthStageGiftKind = conversionStage != null;
            _giftLandingAnchor = giftLandingAnchor;
        }

        /// <summary>눈덩이를 한 번만 받아 전체 Feel 사이클을 시작한다. 멀티에서는 서버만 승인한다.</summary>
        public bool TryConsume(SnowBallCarrier snowball)
        {
            if (_isProcessing || snowball == null) return false;
            _isNetworkConversion = snowball.Object != null && snowball.Object.IsValid;
            if (_isNetworkConversion)
            {
                NetworkRunner runner = snowball.Runner;
                if (runner == null || !runner.IsRunning || !runner.IsServer ||
                    !snowball.Object.HasStateAuthority)
                    return false;
            }

            bool headlessServer = _isNetworkConversion &&
                SystemInfo.graphicsDeviceType == UnityEngine.Rendering.GraphicsDeviceType.Null;
            if (!headlessServer && !PrepareIntakeVisual(snowball)) return false;

            _hasPendingGiftKind = _useGrowthStageGiftKind;
            if (_hasPendingGiftKind)
                _pendingGiftKind = GiftKindForGrowthStage(snowball.GrowthStage);

            _lastSpawnedGift = null;
            _isProcessing = true;
            _processRoutine = StartCoroutine(Process(snowball));
            return true;
        }

        public void BeginSuction()
        {
            if (_isSuctionActive) return;

            _isSuctionActive = true;
            PlaySuctionAudio();
            PlayOneShot(_powerOnVfx);
            if (_airflowVfx != null)
            {
                foreach (ParticleSystem airflow in _airflowVfx)
                {
                    if (airflow == null) continue;
                    airflow.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                    airflow.Play(true);
                }
            }

            if (_suctionVfx != null)
            {
                _suctionVfx.gameObject.SetActive(true);
                _suctionVfx.enabled = true;
                _suctionVfx.Reinit();
                _suctionVfx.Play();
            }
        }

        public void EndSuction()
        {
            if (!_isSuctionActive)
            {
                StopSuctionFeedback(true);
                return;
            }

            _isSuctionActive = false;
            StopSuctionAudio();
            if (_airflowVfx != null)
            {
                foreach (ParticleSystem airflow in _airflowVfx)
                    if (airflow != null)
                        airflow.Stop(true, ParticleSystemStopBehavior.StopEmitting);
            }
            PlayOneShot(_powerOffVfx);

            if (_suctionVfx != null)
            {
                _suctionVfx.Stop();
                _suctionVfx.gameObject.SetActive(false);
            }
        }

        private static void PlayOneShot(ParticleSystem particles)
        {
            if (particles == null) return;
            particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            particles.Play(true);
        }

        private void StopSuctionFeedback(bool clear)
        {
            _isSuctionActive = false;
            StopSuctionAudio();
            ParticleSystemStopBehavior behavior = clear
                ? ParticleSystemStopBehavior.StopEmittingAndClear
                : ParticleSystemStopBehavior.StopEmitting;
            if (_airflowVfx != null)
            {
                foreach (ParticleSystem airflow in _airflowVfx)
                    if (airflow != null) airflow.Stop(true, behavior);
            }
            if (_powerOnVfx != null) _powerOnVfx.Stop(true, behavior);
            if (_powerOffVfx != null) _powerOffVfx.Stop(true, behavior);
            if (_suctionVfx == null) return;
            _suctionVfx.Stop();
            _suctionVfx.gameObject.SetActive(false);
        }

        private void PlaySuctionAudio()
        {
            if (_suctionAudioSource == null || _suctionClip == null) return;
            _suctionAudioSource.Stop();
            _suctionAudioSource.clip = _suctionClip;
            _suctionAudioSource.volume = _suctionVolume;
            _suctionAudioSource.pitch = Random.Range(0.97f, 1.02f);
            _suctionAudioSource.Play();
        }

        private void StopSuctionAudio()
        {
            if (_suctionAudioSource != null && _suctionAudioSource.isPlaying)
                _suctionAudioSource.Stop();
        }

        private IEnumerator Process(SnowBallCarrier snowball)
        {
            BeginSuction();
            PrepareGiftDriver();
            _intakeFeedback?.PlayFeedbacks(transform.position);
            _digestFeedback?.PlayFeedbacks(transform.position);
            _giftPopFeedback?.PlayFeedbacks(transform.position);

            yield return new WaitForSeconds(_intakeDuration);

            EndSuction();
            if (_intakeVisualRenderer != null) _intakeVisualRenderer.enabled = false;
            if (!CommitSnowballConsumption(snowball))
            {
                RestoreSnowballIfInterrupted();
                ResetDrivers();
                _hasPendingGiftKind = false;
                _isProcessing = false;
                _processRoutine = null;
                yield break;
            }

            float remaining = Mathf.Max(0f, _cycleDuration - _intakeDuration);
            if (remaining > 0f) yield return new WaitForSeconds(remaining);

            SpawnGift();

            // 증강은 SpawnGift 를 한 번 더 부를 뿐, 그 안의 _isNetworkConversion 분기는 건드리지
            // 않는다. 두 경로를 합치는 것은 파이프라인 스펙 §8 이 범위 밖으로 미룬 일이고,
            // 호출 횟수만 늘리면 그 정리가 나중에 와도 이 코드는 따라올 필요가 없다.
            if (_augments != null && UnityEngine.Random.value < _augments.GetValue(EAugmentStat.ExtraGiftChance))
                SpawnGift();

            ResetDrivers();
            _isProcessing = false;
            _processRoutine = null;
        }

        private bool PrepareIntakeVisual(SnowBallCarrier snowball)
        {
            if (_intakeVisual == null || _intakeVisualMeshFilter == null || _intakeVisualRenderer == null)
                return false;

            MeshFilter sourceFilter = snowball.GetComponentInChildren<MeshFilter>();
            MeshRenderer sourceRenderer = snowball.GetComponentInChildren<MeshRenderer>();
            if (sourceFilter == null || sourceRenderer == null || sourceFilter.sharedMesh == null) return false;

            _intakeVisualMeshFilter.sharedMesh = sourceFilter.sharedMesh;
            _intakeVisualRenderer.sharedMaterials = sourceRenderer.sharedMaterials;
            var block = new MaterialPropertyBlock();
            sourceRenderer.GetPropertyBlock(block);
            _intakeVisualRenderer.SetPropertyBlock(block);

            _intakeVisual.SetPositionAndRotation(sourceRenderer.transform.position, sourceRenderer.transform.rotation);
            _intakeVisual.localScale = WorldScaleToLocal(_intakeVisual.parent, sourceRenderer.transform.lossyScale);
            _intakeVisualRenderer.enabled = true;

            _hiddenSnowRenderers = snowball.GetComponentsInChildren<Renderer>(true);
            foreach (Renderer renderer in _hiddenSnowRenderers) renderer.enabled = false;

            _disabledSnowColliders = snowball.GetComponentsInChildren<Collider>(true);
            foreach (Collider target in _disabledSnowColliders) target.enabled = false;

            _snowBody = snowball.GetComponent<Rigidbody>();
            if (_snowBody != null)
            {
                _snowBodyWasKinematic = _snowBody.isKinematic;
                _snowBody.linearVelocity = Vector3.zero;
                _snowBody.angularVelocity = Vector3.zero;
                _snowBody.isKinematic = true;
            }

            return true;
        }

        private bool CommitSnowballConsumption(SnowBallCarrier snowball)
        {
            if (_isNetworkConversion)
            {
                if (_conversionStage == null ||
                    !_conversionStage.TryConsumeBallForNetworkConversion(snowball, out _))
                    return false;
            }
            else if (_useGrowthStageGiftKind)
            {
                if (_conversionStage == null ||
                    !_conversionStage.TryConsumeBallForLocalConversion(snowball, out _))
                    return false;
            }
            else if (snowball != null)
            {
                Destroy(snowball.gameObject);
            }

            _hiddenSnowRenderers = null;
            _disabledSnowColliders = null;
            _snowBody = null;
            return true;
        }

        private void RestoreSnowballIfInterrupted()
        {
            if (_hiddenSnowRenderers != null)
            {
                foreach (Renderer renderer in _hiddenSnowRenderers)
                    if (renderer != null) renderer.enabled = true;
            }

            if (_disabledSnowColliders != null)
            {
                foreach (Collider target in _disabledSnowColliders)
                    if (target != null) target.enabled = true;
            }

            if (_snowBody != null) _snowBody.isKinematic = _snowBodyWasKinematic;
            _hiddenSnowRenderers = null;
            _disabledSnowColliders = null;
            _snowBody = null;
        }

        private void PrepareGiftDriver()
        {
            if (_giftPopDriver == null) return;
            _giftPopFeedback?.StopFeedbacks();
            _giftPopDriver.localPosition = Vector3.zero;
            _giftPopDriver.localRotation = Quaternion.identity;
            _giftPopDriver.localScale = Vector3.one * 0.01f;
            _giftPopDriver.gameObject.SetActive(true);
        }

        private void SpawnGift()
        {
            if (_giftPopDriver == null) return;

            PlayGiftBurst();
            PlayGiftOutputAudio();

            EGiftBoxKind kind = _hasPendingGiftKind
                ? _pendingGiftKind
                : (EGiftBoxKind)Random.Range(0, 7);
            _hasPendingGiftKind = false;

            if (_isNetworkConversion)
            {
                if (_networkGiftSpawner == null)
                    _networkGiftSpawner = FindFirstObjectByType<GiftNetSpawner>();

                Vector3 position = _giftPopDriver.position;
                Vector3 angularVelocity = Random.onUnitSphere * _giftSpin;
                if (_networkGiftSpawner == null ||
                    !_networkGiftSpawner.ServerSpawnGift(kind, position, _giftPopDriver.rotation,
                        GiftLaunchVelocity(position), angularVelocity))
                    Debug.LogError("눈덩이 교환기가 네트워크 선물을 생성하지 못했다.", this);

                _isNetworkConversion = false;
                return;
            }

            if (_giftPrefab == null) return;

            Gift gift = Instantiate(_giftPrefab, _giftPopDriver.position, _giftPopDriver.rotation);
            gift.gameObject.SetActive(true);
            gift.SetKind(kind);

            Rigidbody body = gift.GetComponent<Rigidbody>();
            if (body == null) body = gift.gameObject.AddComponent<Rigidbody>();
            body.mass = 2f;
            const float settledLinearDamping = 0.8f;
            body.linearDamping = _giftLandingAnchor == null ? settledLinearDamping : 0f;
            body.angularDamping = 0.8f;
            body.interpolation = RigidbodyInterpolation.Interpolate;
            body.collisionDetectionMode = CollisionDetectionMode.ContinuousDynamic;
            body.linearVelocity = GiftLaunchVelocity(gift.transform.position);
            body.angularVelocity = Random.onUnitSphere * _giftSpin;
            _lastSpawnedGift = gift;
            if (_giftLandingAnchor != null)
                StartCoroutine(FinishGiftLanding(body, _giftLandingAnchor, settledLinearDamping));
        }

        private Vector3 GiftLaunchVelocity(Vector3 startPosition)
        {
            if (_giftLandingAnchor == null) return transform.TransformDirection(_giftLaunchVelocity);

            float flightSeconds = Mathf.Max(0.1f, _giftLandingFlightSeconds);
            Vector3 displacement = _giftLandingAnchor.position - startPosition;
            return displacement / flightSeconds - Physics.gravity * (flightSeconds * 0.5f);
        }

        private IEnumerator FinishGiftLanding(
            Rigidbody body, Transform landingAnchor, float linearDamping)
        {
            yield return new WaitForSeconds(_giftLandingFlightSeconds);
            if (body == null || landingAnchor == null) yield break;

            body.position = landingAnchor.position;
            body.rotation = landingAnchor.rotation;
            body.linearVelocity = Vector3.zero;
            body.angularVelocity = Vector3.zero;
            body.linearDamping = linearDamping;
        }

        private void PlayGiftBurst()
        {
            if (_giftBurstVfx == null) return;

            foreach (ParticleSystem particles in _giftBurstVfx)
            {
                if (particles == null) continue;
                particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
                particles.Play(true);
            }
        }

        private void PlayGiftOutputAudio()
        {
            if (_giftOutputAudioSource == null || _giftOutputClip == null) return;
            _giftOutputAudioSource.pitch = Random.Range(0.98f, 1.04f);
            _giftOutputAudioSource.PlayOneShot(_giftOutputClip, _giftOutputVolume);
        }

        private void ResetDrivers()
        {
            if (_intakeVisualRenderer != null) _intakeVisualRenderer.enabled = false;
            if (_giftPopDriver != null)
            {
                _giftPopDriver.localPosition = Vector3.zero;
                _giftPopDriver.localRotation = Quaternion.identity;
                _giftPopDriver.localScale = Vector3.one * 0.01f;
            }
            if (_machineMotionRoot != null)
            {
                _machineMotionRoot.localPosition = Vector3.zero;
                _machineMotionRoot.localRotation = Quaternion.identity;
                _machineMotionRoot.localScale = Vector3.one;
            }
        }

        private static Vector3 WorldScaleToLocal(Transform parent, Vector3 worldScale)
        {
            if (parent == null) return worldScale;
            Vector3 parentScale = parent.lossyScale;
            return new Vector3(
                SafeDivide(worldScale.x, parentScale.x),
                SafeDivide(worldScale.y, parentScale.y),
                SafeDivide(worldScale.z, parentScale.z));
        }

        private static float SafeDivide(float value, float divisor) =>
            Mathf.Abs(divisor) > 0.0001f ? value / divisor : value;

        private void OnDisable()
        {
            if (_processRoutine != null) StopCoroutine(_processRoutine);
            _processRoutine = null;
            RestoreSnowballIfInterrupted();
            _hasPendingGiftKind = false;
            _isProcessing = false;
            StopSuctionFeedback(true);
            if (_giftBurstVfx != null)
            {
                foreach (ParticleSystem particles in _giftBurstVfx)
                    if (particles != null)
                        particles.Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            }
            ResetDrivers();
        }
    }
}
