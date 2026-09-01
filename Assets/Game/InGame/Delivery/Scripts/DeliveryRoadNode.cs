using UnityEngine;

namespace PPack
{
    public sealed class DeliveryRoadNode : MonoBehaviour
    {
        [SerializeField] private string _id;

        public string Id => string.IsNullOrWhiteSpace(_id) ? name : _id;

        public void Configure(string id) => _id = id;

        private void OnDrawGizmos()
        {
            Gizmos.color = Color.yellow;
            Gizmos.DrawSphere(transform.position, 0.25f);
        }
    }
}

