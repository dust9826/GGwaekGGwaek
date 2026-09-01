using System;
using Fusion;
using UnityEngine;

namespace PPack
{
    [DefaultExecutionOrder(-900)]
    [DisallowMultipleComponent]
    public sealed class PedestrianNetworkHub : NetworkBehaviour
    {
        [SerializeField] private NpcAppearanceCatalog _appearanceCatalog;
        [SerializeField] private PedestrianAppearance _appearance;
        [SerializeField] private PedestrianContext _context;

        [Networked] private int NetNpcId { get; set; }
        [Networked] private int NetGenerationSeed { get; set; }
        [Networked] private int NetTemperament { get; set; }
        [Networked] private int NetBodyId { get; set; }
        [Networked] private int NetFaceId { get; set; }
        [Networked] private int NetHairId { get; set; }
        [Networked] private int NetTopId { get; set; }
        [Networked] private int NetCoatId { get; set; }
        [Networked] private int NetPantsId { get; set; }
        [Networked] private int NetShoesId { get; set; }
        [Networked] private int NetHatId { get; set; }
        [Networked] private int NetAction { get; set; }

        private static int _nextNpcId = 1;
        private NpcProfileData _profile;
        private int _registeredNpcId;

        private bool IsNetworked => Object != null && Object.IsValid;
        public bool HasBehaviorAuthority => !IsNetworked || Object.HasStateAuthority;
        public bool HasProfile => _profile.NpcId > 0;
        public NpcProfileData Profile => _profile;
        public int NpcId => _profile.NpcId;

        private void Awake()
        {
            if (_appearance == null) _appearance = GetComponent<PedestrianAppearance>();
            if (_context == null) _context = GetComponent<PedestrianContext>();
        }

        private void Start()
        {
            if (NetworkRunner.Instances.Count == 0) GenerateAndApply(CreateSeed());
        }

        private void OnEnable()
        {
            if (HasProfile && _registeredNpcId == 0) {
                _registeredNpcId = _profile.NpcId;
                NpcProfileRegistry.Register(_profile);
            }
        }

        public override void Spawned()
        {
            if (Object.HasStateAuthority) GenerateAndApply(CreateSeed());
            else ApplyNetworkProfile();
        }

        public override void FixedUpdateNetwork()
        {
            if (Object.HasStateAuthority && _context != null) NetAction = (int)_context.CurrentAction;
        }

        public override void Render()
        {
            if (!Object.HasStateAuthority) {
                ApplyNetworkProfile();
                if (_context != null) _context.ApplyReplicatedAction((EPedestrianAction)NetAction);
            }
        }

        public override void Despawned(NetworkRunner runner, bool hasState)
        {
            UnregisterProfile();
        }

        private void OnDisable()
        {
            UnregisterProfile();
        }

        public bool MatchesNpcId(int npcId)
        {
            return HasProfile && _profile.NpcId == npcId;
        }

        private void GenerateAndApply(int seed)
        {
            if (HasProfile) return;
            int npcId = AllocateNpcId();
            NpcAppearanceData appearance = NpcAppearanceGenerator.Generate(
                seed, _appearanceCatalog, NpcProfileRegistry.ActiveProfiles);
            ENpcTemperament temperament = new System.Random(seed).Next(2) == 0
                ? ENpcTemperament.Timid
                : ENpcTemperament.Aggressive;
            NpcProfileData profile = new NpcProfileData(npcId, seed, temperament, appearance);

            if (IsNetworked) WriteNetworkProfile(profile);
            ApplyProfile(profile);
        }

        private void ApplyNetworkProfile()
        {
            if (NetNpcId <= 0 || NetNpcId == _profile.NpcId) return;
            NpcAppearanceData appearance = new NpcAppearanceData {
                BodyId = NetBodyId,
                FaceId = NetFaceId,
                HairId = NetHairId,
                TopId = NetTopId,
                CoatId = NetCoatId,
                PantsId = NetPantsId,
                ShoesId = NetShoesId,
                HatId = NetHatId,
            };
            ApplyProfile(new NpcProfileData(NetNpcId, NetGenerationSeed,
                (ENpcTemperament)NetTemperament, appearance));
        }

        private void ApplyProfile(NpcProfileData profile)
        {
            UnregisterProfile();
            _profile = profile;
            _registeredNpcId = profile.NpcId;
            NpcProfileRegistry.Register(profile);
            _context?.ApplyProfile(profile);
            _appearance?.Apply(profile.Appearance);
        }

        private void WriteNetworkProfile(NpcProfileData profile)
        {
            NetNpcId = profile.NpcId;
            NetGenerationSeed = profile.GenerationSeed;
            NetTemperament = (int)profile.Temperament;
            NetBodyId = profile.Appearance.BodyId;
            NetFaceId = profile.Appearance.FaceId;
            NetHairId = profile.Appearance.HairId;
            NetTopId = profile.Appearance.TopId;
            NetCoatId = profile.Appearance.CoatId;
            NetPantsId = profile.Appearance.PantsId;
            NetShoesId = profile.Appearance.ShoesId;
            NetHatId = profile.Appearance.HatId;
            NetAction = (int)EPedestrianAction.Normal;
        }

        private void UnregisterProfile()
        {
            if (_registeredNpcId <= 0) return;
            NpcProfileRegistry.Unregister(_registeredNpcId);
            _registeredNpcId = 0;
        }

        private static int AllocateNpcId()
        {
            while (NpcProfileRegistry.TryGet(_nextNpcId, out _)) _nextNpcId++;
            return _nextNpcId++;
        }

        private int CreateSeed()
        {
            return unchecked(Environment.TickCount * 397 ^ _nextNpcId);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetIds()
        {
            _nextNpcId = 1;
        }
    }
}
