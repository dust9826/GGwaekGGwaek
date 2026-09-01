using UnityEngine;

namespace PPack
{
    public sealed class DeliveryYieldPoint : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadSegment _segment;
        [SerializeField, Min(0f)] private float _segmentDistance;
        [SerializeField] private float _lateralOffset;

        public DeliveryRoadSegment Segment => _segment;
        public float SegmentDistance => _segment == null ? 0f : Mathf.Clamp(_segmentDistance, 0f, _segment.Length);
        public float LateralOffset => _lateralOffset;

        public void Configure(DeliveryRoadSegment segment, float segmentDistance, float lateralOffset)
        {
            _segment = segment;
            _segmentDistance = segmentDistance;
            _lateralOffset = lateralOffset;
        }

        private void OnDrawGizmosSelected()
        {
            if (_segment == null) return;
            DeliveryRoadPose pose = _segment.Evaluate(SegmentDistance);
            Vector3 position = pose.Position + pose.Right * _lateralOffset;
            Gizmos.color = Color.green;
            Gizmos.DrawWireCube(position, new Vector3(2.5f, 0.5f, 5f));
        }
    }
}

