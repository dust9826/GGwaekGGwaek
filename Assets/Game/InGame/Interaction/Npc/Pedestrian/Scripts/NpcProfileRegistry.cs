using System.Collections.Generic;

namespace PPack
{
    public static class NpcProfileRegistry
    {
        private static readonly Dictionary<int, NpcProfileData> Profiles = new();
        private static readonly List<NpcProfileData> Snapshot = new();

        public static IReadOnlyList<NpcProfileData> ActiveProfiles
        {
            get
            {
                Snapshot.Clear();
                foreach (NpcProfileData profile in Profiles.Values) Snapshot.Add(profile);
                return Snapshot;
            }
        }

        public static void Register(NpcProfileData profile)
        {
            if (profile.NpcId > 0) Profiles[profile.NpcId] = profile;
        }

        public static void Unregister(int npcId)
        {
            Profiles.Remove(npcId);
        }

        public static bool TryGet(int npcId, out NpcProfileData profile)
        {
            return Profiles.TryGetValue(npcId, out profile);
        }

        [UnityEngine.RuntimeInitializeOnLoadMethod(
            UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Profiles.Clear();
            Snapshot.Clear();
        }
    }
}
