using UnityEngine;

namespace PPack
{
    public enum ENpcTemperament
    {
        Timid,
        Aggressive,
    }

    public enum EPedestrianAction
    {
        Normal,
        HitReaction,
        Flee,
        Attack,
    }

    [DisallowMultipleComponent]
    public sealed class PedestrianContext : MonoBehaviour
    {
        [SerializeField] private ENpcTemperament _temperament;
        [SerializeField, Min(0f)] private float _impactThresholdKgMps = 10f;
        [SerializeField, Min(0f)] private float _witnessRadiusM = 12f;

        private bool _hasPendingStrongImpact;
        private bool _hasPendingIncident;
        private bool _isHitReactionComplete;
        private bool _isReactionComplete;

        public ENpcTemperament Temperament => _temperament;
        public EPedestrianAction CurrentAction { get; private set; } = EPedestrianAction.Normal;
        public bool HasPendingStrongImpact => _hasPendingStrongImpact;
        public bool HasPendingIncident => _hasPendingIncident;
        public bool IsHitReacting => CurrentAction == EPedestrianAction.HitReaction;
        public bool IsHitReactionComplete => _isHitReactionComplete;
        public bool IsReactionComplete => _isReactionComplete;
        public Vector3 IncidentPosition { get; private set; }

        private void OnEnable()
        {
            PedestrianIncidentSystem.Register(this);
        }

        private void OnDisable()
        {
            PedestrianIncidentSystem.Unregister(this);
        }

        public bool ReportImpact(float momentumKgMps)
        {
            return ReportImpact(momentumKgMps, transform.position);
        }

        public bool ReportImpact(float momentumKgMps, Vector3 incidentPosition)
        {
            if (momentumKgMps < _impactThresholdKgMps) return false;
            _hasPendingStrongImpact = true;
            IncidentPosition = incidentPosition;
            PedestrianIncidentSystem.Broadcast(this, incidentPosition, _witnessRadiusM);
            return true;
        }

        public void ReportIncident()
        {
            ReportIncident(transform.position);
        }

        public void ReportIncident(Vector3 incidentPosition)
        {
            _hasPendingIncident = true;
            IncidentPosition = incidentPosition;
        }

        public void ApplyProfile(NpcProfileData profile)
        {
            _temperament = profile.Temperament;
        }

        public void ApplyReplicatedAction(EPedestrianAction action)
        {
            CurrentAction = action;
        }

        public bool ConsumeImpactAndBeginHitReaction()
        {
            if (!_hasPendingStrongImpact || CurrentAction != EPedestrianAction.Normal) return false;
            _hasPendingStrongImpact = false;
            _hasPendingIncident = true;
            _isHitReactionComplete = false;
            _isReactionComplete = false;
            CurrentAction = EPedestrianAction.HitReaction;
            return true;
        }

        public void NotifyHitReactionComplete()
        {
            if (IsHitReacting) _isHitReactionComplete = true;
        }

        public bool FinishHitReaction()
        {
            if (!IsHitReacting || !_isHitReactionComplete) return false;
            _isHitReactionComplete = false;
            CurrentAction = EPedestrianAction.Normal;
            return true;
        }

        public bool BeginReaction(EPedestrianAction reaction)
        {
            if (!_hasPendingIncident || CurrentAction != EPedestrianAction.Normal) return false;
            if (reaction != EPedestrianAction.Flee && reaction != EPedestrianAction.Attack) return false;

            _isReactionComplete = false;
            CurrentAction = reaction;
            return true;
        }

        public void NotifyReactionComplete()
        {
            if (CurrentAction == EPedestrianAction.Flee || CurrentAction == EPedestrianAction.Attack) {
                _isReactionComplete = true;
            }
        }

        public bool FinishReaction()
        {
            if (!_isReactionComplete) return false;
            if (CurrentAction != EPedestrianAction.Flee && CurrentAction != EPedestrianAction.Attack) return false;

            _isReactionComplete = false;
            _hasPendingIncident = false;
            CurrentAction = EPedestrianAction.Normal;
            return true;
        }
    }
}
