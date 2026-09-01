using UnityEngine;

namespace PPack
{
    public sealed class DeliveryHouse : MonoBehaviour
    {
        [SerializeField] private DeliveryRoadNode _roadNode;
        [SerializeField] private Transform _door;
        [SerializeField] private GiftDropZone _zone;

        public DeliveryRoadNode RoadNode => _roadNode;
        public Vector3 DoorPosition => _door != null ? _door.position : transform.position;
        public GiftDropZone Zone => _zone;

        public void Configure(DeliveryRoadNode roadNode, Transform door, GiftDropZone zone)
        {
            _roadNode = roadNode;
            _door = door;
            _zone = zone;
        }
    }
}
