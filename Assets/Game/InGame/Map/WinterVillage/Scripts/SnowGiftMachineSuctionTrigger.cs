using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Starts one complete snow-to-gift cycle when a snowball overlaps the intake volume.
    /// </summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(BoxCollider))]
    public sealed class SnowGiftMachineSuctionTrigger : MonoBehaviour
    {
        [SerializeField] private SnowGiftMachinePresentation _presentation;
        [SerializeField, Min(0.1f)] private float _openingRadius = 1.18f;
        [SerializeField, Min(0.1f)] private float _frontDepth = 1.25f;
        [SerializeField, Min(0f)] private float _rearTolerance = 0.06f;
        [SerializeField, Min(0f)] private float _surfaceTolerance;

        private readonly Collider[] _overlaps = new Collider[16];
        private BoxCollider _triggerVolume;

        public float OpeningRadius => _openingRadius;
        public float FrontDepth => _frontDepth;
        public float RearTolerance => _rearTolerance;
        public float SurfaceTolerance => _surfaceTolerance;

        private void Awake()
        {
            _triggerVolume = GetComponent<BoxCollider>();
        }

        public void Configure(
            SnowGiftMachinePresentation presentation,
            float openingRadius,
            float frontDepth,
            float rearTolerance,
            float surfaceTolerance = 0f)
        {
            _presentation = presentation;
            _openingRadius = Mathf.Max(0.1f, openingRadius);
            _frontDepth = Mathf.Max(0.1f, frontDepth);
            _rearTolerance = Mathf.Max(0f, rearTolerance);
            _surfaceTolerance = Mathf.Max(0f, surfaceTolerance);
        }

        public bool IsWithinFrontIntake(Vector3 worldPosition)
        {
            if (_presentation == null || _presentation.IntakeAnchor == null) return false;

            Vector3 intakeLocal = _presentation.IntakeAnchor.InverseTransformPoint(worldPosition);
            if (intakeLocal.z < -_frontDepth || intakeLocal.z > _rearTolerance) return false;

            float radialSqr = intakeLocal.x * intakeLocal.x + intakeLocal.y * intakeLocal.y;
            return radialSqr <= _openingRadius * _openingRadius;
        }

        public bool IsColliderWithinFrontIntake(Collider target)
        {
            if (target == null || _presentation == null || _presentation.IntakeAnchor == null) return false;
            if (IsWithinFrontIntake(target.transform.position)) return true;

            Transform intake = _presentation.IntakeAnchor;
            Vector3 centerLocal = intake.InverseTransformPoint(target.bounds.center);
            if (centerLocal.z > _rearTolerance + _surfaceTolerance) return false;

            Vector3 surfaceLocal = intake.InverseTransformPoint(target.bounds.ClosestPoint(intake.position));
            if (surfaceLocal.z < -_frontDepth - _surfaceTolerance ||
                surfaceLocal.z > _rearTolerance + _surfaceTolerance)
                return false;

            float radius = _openingRadius + _surfaceTolerance;
            float radialSqr = surfaceLocal.x * surfaceLocal.x + surfaceLocal.y * surfaceLocal.y;
            return radialSqr <= radius * radius;
        }

        private void OnTriggerEnter(Collider other)
        {
            TryConsume(other);
        }

        private void OnTriggerStay(Collider other)
        {
            TryConsume(other);
        }

        private void FixedUpdate()
        {
            if (_presentation == null || _presentation.IsProcessing) return;
            if (_triggerVolume == null) _triggerVolume = GetComponent<BoxCollider>();
            if (_triggerVolume == null || !_triggerVolume.enabled) return;

            Vector3 scale = _triggerVolume.transform.lossyScale;
            var absoluteScale = new Vector3(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
            Vector3 halfExtents = Vector3.Scale(_triggerVolume.size * 0.5f, absoluteScale);
            Vector3 center = _triggerVolume.transform.TransformPoint(_triggerVolume.center);
            int count = Physics.OverlapBoxNonAlloc(
                center,
                halfExtents,
                _overlaps,
                _triggerVolume.transform.rotation,
                Physics.AllLayers,
                QueryTriggerInteraction.Collide);

            for (int index = 0; index < count && !_presentation.IsProcessing; index++)
                TryConsume(_overlaps[index]);
        }

        private void TryConsume(Collider other)
        {
            if (other == null || _presentation == null || _presentation.IsProcessing) return;

            SnowBallCarrier snowball = other.GetComponentInParent<SnowBallCarrier>();
            if (snowball == null) return;
            if (!IsColliderWithinFrontIntake(other)) return;
            _presentation.TryConsume(snowball);
        }
    }
}
