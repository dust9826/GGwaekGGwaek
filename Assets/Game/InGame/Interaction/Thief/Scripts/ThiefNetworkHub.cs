using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>도둑의 관찰 가능한 원인만 한 곳에서 복제한다.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefNetworkHub : NetworkBehaviour
    {
        [SerializeField] private ThiefActor _actor;

        [Networked] private int NetAction { get; set; }
        [Networked] private int NetGait { get; set; }
        [Networked] private NetworkBool NetHasCargo { get; set; }
        [Networked] private int NetLiftPhase { get; set; }
        [Networked] private float NetLiftPhaseProgress { get; set; }
        [Networked] private float NetExitCountdownRemaining { get; set; }
        [Networked] private int NetImpactPhase { get; set; }
        [Networked] private float NetImpactPhaseProgress { get; set; }

        public EThiefAction PresentedAction => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? (EThiefAction)NetAction : _actor != null ? _actor.CurrentAction : EThiefAction.Waiting;
        public EThiefGait PresentedGait => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? (EThiefGait)NetGait : _actor != null ? _actor.CurrentGait : EThiefGait.Idle;
        public bool PresentedHasCargo => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? NetHasCargo : _actor != null && _actor.HasCargo;
        public EThiefLiftPhase PresentedLiftPhase => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? (EThiefLiftPhase)NetLiftPhase : _actor != null ? _actor.LiftPhase : EThiefLiftPhase.None;
        public float PresentedLiftPhaseProgress => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? NetLiftPhaseProgress : _actor != null ? _actor.LiftPhaseProgress01 : 0f;
        public float PresentedExitCountdownRemaining => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? NetExitCountdownRemaining : _actor != null ? _actor.ExitCountdownRemaining : 0f;
        public EThiefImpactPhase PresentedImpactPhase => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? (EThiefImpactPhase)NetImpactPhase : _actor != null ? _actor.ImpactPhase : EThiefImpactPhase.None;
        public float PresentedImpactPhaseProgress => Object != null && Object.IsValid && !Object.HasStateAuthority
            ? NetImpactPhaseProgress : _actor != null ? _actor.ImpactPhaseProgress01 : 0f;

        private void Awake()
        {
            if (_actor == null) _actor = GetComponent<ThiefActor>();
        }

        public override void FixedUpdateNetwork()
        {
            if (!Object.HasStateAuthority || _actor == null) return;
            NetAction = (int)_actor.CurrentAction;
            NetGait = (int)_actor.CurrentGait;
            NetHasCargo = _actor.HasCargo;
            NetLiftPhase = (int)_actor.LiftPhase;
            NetLiftPhaseProgress = _actor.LiftPhaseProgress01;
            NetExitCountdownRemaining = _actor.ExitCountdownRemaining;
            NetImpactPhase = (int)_actor.ImpactPhase;
            NetImpactPhaseProgress = _actor.ImpactPhaseProgress01;
        }

        public override void Render()
        {
            if (Object.HasStateAuthority || _actor == null) return;
            _actor.ApplyReplicatedState((EThiefAction)NetAction, (EThiefGait)NetGait, NetHasCargo,
                (EThiefLiftPhase)NetLiftPhase, NetLiftPhaseProgress, NetExitCountdownRemaining,
                (EThiefImpactPhase)NetImpactPhase, NetImpactPhaseProgress);
        }
    }
}
