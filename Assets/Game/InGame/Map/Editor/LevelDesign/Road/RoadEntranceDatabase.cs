using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace PPack
{
    [Serializable]
    internal sealed class RoadEntranceProfile
    {
        [SerializeField] private GameObject _prefab;
        [SerializeField] private Vector3 _localPosition;
        [SerializeField] private Vector3 _localForward = Vector3.forward;
        [SerializeField, Min(0.5f)] private float _doorWidth = 1.5f;
        [SerializeField] private bool _autoEstimated;

        public GameObject Prefab => _prefab;
        public Vector3 LocalPosition => _localPosition;
        public Vector3 LocalForward => _localForward.sqrMagnitude > 0.001f
            ? _localForward.normalized
            : Vector3.forward;
        public float DoorWidth => Mathf.Max(0.5f, _doorWidth);
        public bool AutoEstimated => _autoEstimated;

        public RoadEntranceProfile(
            GameObject prefab,
            Vector3 localPosition,
            Vector3 localForward,
            float doorWidth,
            bool autoEstimated)
        {
            _prefab = prefab;
            _localPosition = localPosition;
            _localForward = localForward;
            _doorWidth = doorWidth;
            _autoEstimated = autoEstimated;
        }
    }

    internal readonly struct RoadEntranceWorldData
    {
        public readonly GameObject HouseRoot;
        public readonly RoadEntranceProfile Profile;
        public readonly Vector3 Position;
        public readonly Vector3 Forward;
        public readonly float DoorWidth;

        public RoadEntranceWorldData(GameObject houseRoot, RoadEntranceProfile profile)
        {
            HouseRoot = houseRoot;
            Profile = profile;
            Position = houseRoot.transform.TransformPoint(profile.LocalPosition);

            Vector3 forward = houseRoot.transform.TransformDirection(profile.LocalForward);
            forward.y = 0f;
            Forward = forward.sqrMagnitude > 0.001f ? forward.normalized : houseRoot.transform.forward;

            Vector3 scale = houseRoot.transform.lossyScale;
            float horizontalScale = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.z));
            DoorWidth = profile.DoorWidth * Mathf.Max(0.01f, horizontalScale);
        }
    }

    internal sealed class RoadEntranceDatabase : ScriptableObject
    {
        [SerializeField] private List<RoadEntranceProfile> _entries = new();

        public IReadOnlyList<RoadEntranceProfile> Entries => _entries;

        public bool TryResolve(GameObject pickedObject, out RoadEntranceWorldData entrance)
        {
            entrance = default;
            if (pickedObject == null) return false;

            GameObject instanceRoot = PrefabUtility.GetNearestPrefabInstanceRoot(pickedObject);
            if (instanceRoot == null) return false;

            GameObject source = PrefabUtility.GetCorrespondingObjectFromSource(instanceRoot);
            if (source == null) return false;

            for (int i = 0; i < _entries.Count; i++)
            {
                RoadEntranceProfile profile = _entries[i];
                if (profile?.Prefab != source) continue;
                entrance = new RoadEntranceWorldData(instanceRoot, profile);
                return true;
            }

            return false;
        }

        public bool ContainsPrefab(GameObject prefab)
        {
            return TryGetProfile(prefab, out _);
        }

        public bool TryGetProfile(GameObject prefab, out RoadEntranceProfile profile)
        {
            profile = null;
            if (prefab == null) return false;

            for (int i = 0; i < _entries.Count; i++)
            {
                if (_entries[i]?.Prefab != prefab) continue;
                profile = _entries[i];
                return true;
            }
            return false;
        }

        public void Add(RoadEntranceProfile profile)
        {
            if (profile?.Prefab == null || ContainsPrefab(profile.Prefab)) return;
            _entries.Add(profile);
        }

        internal void ReplaceEntriesForTests(IEnumerable<RoadEntranceProfile> entries)
        {
            _entries = entries == null ? new List<RoadEntranceProfile>() : new List<RoadEntranceProfile>(entries);
        }
    }
}
