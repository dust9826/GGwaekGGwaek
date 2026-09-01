using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    public static class PedestrianIncidentSystem
    {
        private static readonly HashSet<PedestrianContext> Pedestrians = new();

        public static void Register(PedestrianContext pedestrian)
        {
            if (pedestrian != null) Pedestrians.Add(pedestrian);
        }

        public static void Unregister(PedestrianContext pedestrian)
        {
            if (pedestrian != null) Pedestrians.Remove(pedestrian);
        }

        public static void Broadcast(PedestrianContext source, Vector3 position, float radiusM)
        {
            float squaredRadius = radiusM * radiusM;
            foreach (PedestrianContext pedestrian in Pedestrians) {
                if (pedestrian == null || pedestrian == source) continue;
                if ((pedestrian.transform.position - position).sqrMagnitude > squaredRadius) continue;
                pedestrian.ReportIncident(position);
            }
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void Reset()
        {
            Pedestrians.Clear();
        }
    }
}
