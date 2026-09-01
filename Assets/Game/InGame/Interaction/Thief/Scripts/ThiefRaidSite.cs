using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.AI;

namespace PPack
{
    /// <summary>
    /// 보관소 구현 대신 물리 공간만 설명하는 도둑 소유 어댑터다. 보관소 프리팹, 슬롯, 문을 참조하지 않는다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ThiefRaidSite : MonoBehaviour
    {
        [SerializeField] private Collider _lootVolume;
        [SerializeField] private Transform[] _approachPoints = Array.Empty<Transform>();
        [SerializeField] private Vector2 _spawnRadiusMRange = new Vector2(12f, 22f);
        [SerializeField] private AnimationCurve _spawnRadiusDistribution01 =
            new AnimationCurve(new Keyframe(0f, 0f), new Keyframe(1f, 1f));
        [SerializeField, Min(1)] private int _navMeshSampleAttempts = 16;
        [SerializeField, Min(0.1f)] private float _navMeshSampleRadiusM = 2f;

        private NavMeshPath _path;

        public Collider LootVolume => _lootVolume;

        private void Awake()
        {
            _path = new NavMeshPath();
        }

        public void Configure(Collider lootVolume, IReadOnlyList<Transform> approachPoints = null)
        {
            _lootVolume = lootVolume;
            if (approachPoints == null)
            {
                _approachPoints = Array.Empty<Transform>();
                return;
            }

            _approachPoints = new Transform[approachPoints.Count];
            for (int index = 0; index < approachPoints.Count; index++)
                _approachPoints[index] = approachPoints[index];
        }

        public bool Contains(Vector3 position)
        {
            if (_lootVolume == null || !_lootVolume.enabled) return false;
            Vector3 closest = _lootVolume.ClosestPoint(position);
            return (closest - position).sqrMagnitude <= 0.0001f;
        }

        public Vector3 ClosestApproachPoint(Vector3 from)
        {
            Transform best = null;
            float bestDistanceSq = float.MaxValue;
            for (int index = 0; index < _approachPoints.Length; index++)
            {
                Transform candidate = _approachPoints[index];
                if (candidate == null) continue;
                float distanceSq = (candidate.position - from).sqrMagnitude;
                if (distanceSq >= bestDistanceSq) continue;
                best = candidate;
                bestDistanceSq = distanceSq;
            }

            return best != null ? best.position
                : _lootVolume != null ? _lootVolume.bounds.center : transform.position;
        }

        public bool TryFindGift(Vector3 from, EGiftBoxKind preferredKind, out Gift gift)
        {
            gift = FindBestGift(from, preferredKind);
            return gift != null;
        }

        public bool TrySampleSpawnPoint(System.Random random, out Vector3 position)
        {
            float min = Mathf.Max(0f, Mathf.Min(_spawnRadiusMRange.x, _spawnRadiusMRange.y));
            float max = Mathf.Max(min, Mathf.Max(_spawnRadiusMRange.x, _spawnRadiusMRange.y));
            for (int attempt = 0; attempt < Mathf.Max(1, _navMeshSampleAttempts); attempt++)
            {
                double angle = random.NextDouble() * Math.PI * 2.0;
                float uniform = (float)random.NextDouble();
                float ratio = _spawnRadiusDistribution01 != null && _spawnRadiusDistribution01.length > 0
                    ? Mathf.Clamp01(_spawnRadiusDistribution01.Evaluate(uniform))
                    : Mathf.Sqrt(uniform);
                float radius = Mathf.Lerp(min, max, ratio);
                Vector3 candidate = transform.position + new Vector3(
                    (float)Math.Cos(angle) * radius, 0f, (float)Math.Sin(angle) * radius);
                if (!NavMesh.SamplePosition(candidate, out NavMeshHit hit,
                        _navMeshSampleRadiusM, NavMesh.AllAreas)) continue;
                position = hit.position;
                return true;
            }

            position = default;
            return false;
        }

        private Gift FindBestGift(Vector3 from, EGiftBoxKind preferredKind)
        {
            Gift best = null;
            int bestUpgradeSteps = int.MaxValue;
            float bestDistanceSq = float.MaxValue;
            int preferredRank = (int)preferredKind;
            IReadOnlyList<Gift> all = Gift.All;
            for (int index = 0; index < all.Count; index++)
            {
                Gift candidate = all[index];
                if (candidate == null || !candidate.isActiveAndEnabled || candidate.IsCarried) continue;
                if (candidate.gameObject.scene != gameObject.scene || !Contains(candidate.transform.position)) continue;
                int candidateRank = (int)candidate.Kind;
                if (candidateRank > preferredRank) continue;
                int upgradeSteps = preferredRank - candidateRank;
                if (upgradeSteps > bestUpgradeSteps) continue;
                if (!HasCompletePath(from, candidate.transform.position)) continue;
                float distanceSq = (candidate.transform.position - from).sqrMagnitude;
                if (upgradeSteps == bestUpgradeSteps && distanceSq >= bestDistanceSq) continue;
                best = candidate;
                bestUpgradeSteps = upgradeSteps;
                bestDistanceSq = distanceSq;
            }
            return best;
        }

        private bool HasCompletePath(Vector3 from, Vector3 to)
        {
            _path ??= new NavMeshPath();
            if (!NavMesh.SamplePosition(from, out NavMeshHit start, 2f, NavMesh.AllAreas)) return false;
            if (!NavMesh.SamplePosition(to, out NavMeshHit end, 2f, NavMesh.AllAreas)) return false;
            return NavMesh.CalculatePath(start.position, end.position, NavMesh.AllAreas, _path)
                && _path.status == NavMeshPathStatus.PathComplete;
        }

        private void OnDrawGizmosSelected()
        {
            Gizmos.color = new Color(0.8f, 0.2f, 0.9f, 0.8f);
            float min = Mathf.Max(0f, Mathf.Min(_spawnRadiusMRange.x, _spawnRadiusMRange.y));
            float max = Mathf.Max(min, Mathf.Max(_spawnRadiusMRange.x, _spawnRadiusMRange.y));
            Gizmos.DrawWireSphere(transform.position, min);
            Gizmos.DrawWireSphere(transform.position, max);
        }
    }
}
