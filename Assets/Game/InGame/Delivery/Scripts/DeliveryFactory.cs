using UnityEngine;

namespace PPack
{
    public sealed class DeliveryFactory : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNode _roadNode;
        [SerializeField] private Transform _truckStop;

        public DeliveryRoadNode RoadNode => _roadNode;
        public Vector3 StopPosition => _truckStop != null ? _truckStop.position : transform.position;

        public void Configure(DeliveryRoadNode roadNode, Transform truckStop = null)
        {
            _roadNode = roadNode;
            _truckStop = truckStop;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(1f, 0.55f, 0.1f, 0.9f);
            Gizmos.DrawWireCube(StopPosition, new Vector3(1.5f, 0.5f, 3f));
        }
    }
}

