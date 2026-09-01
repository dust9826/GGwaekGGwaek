using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PedestrianContext))]
    public sealed class PedestrianImpactReceiver : MonoBehaviour
    {
        private PedestrianContext _context;
        private PedestrianNetworkHub _networkHub;

        private void Awake()
        {
            _context = GetComponent<PedestrianContext>();
            _networkHub = GetComponent<PedestrianNetworkHub>();
        }

        private void OnCollisionEnter(Collision collision)
        {
            if (_networkHub != null && !_networkHub.HasBehaviorAuthority) return;
            if (!ImpactMomentum.TryCompute(collision, out float momentum, out ContactPoint contact)) return;
            _context.ReportImpact(momentum, contact.point);
        }
    }
}
