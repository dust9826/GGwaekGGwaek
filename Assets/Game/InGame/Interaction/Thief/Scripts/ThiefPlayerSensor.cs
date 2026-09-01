using UnityEngine;

namespace PPack
{

    public enum EThiefAwarenessStage
    {
        Calm,
        Wary,
        Spotted,
    }

    /// <summary>로컬 카메라나 입력 없이 실제 펭귄 오브젝트를 보는 서버용 시야 센서다.</summary>
    [DisallowMultipleComponent]
    public sealed class ThiefPlayerSensor : MonoBehaviour
    {
        [SerializeField, Min(0.1f)] private float _visualRangeM = 10f;
        [SerializeField, Range(1f, 360f)] private float _fieldOfViewDeg = 120f;
        [SerializeField, Min(0f)] private float _closeThreatRangeM = 5f;
        [SerializeField, Min(0f)] private float _eyeHeightM = 1.55f;
        [SerializeField, Min(0.02f)] private float _scanIntervalSeconds = 0.2f;
        [SerializeField, Min(0f)] private float _lostSightHoldSeconds = 0.6f;
        [SerializeField] private LayerMask _occlusionMask = ~0;
        [SerializeField, Range(0f, 1f)] private float _waryEnterThreshold = 0.3f;
        [SerializeField, Range(0f, 1f)] private float _waryExitThreshold = 0.15f;
        [SerializeField, Range(0f, 1f)] private float _spottedExitThreshold = 0.6f;
        [SerializeField, Min(0f)] private float _awarenessDecayPerSecond = 2f;

        private readonly RaycastHit[] _hits = new RaycastHit[16];
        private Transform _visiblePlayer;
        private float _visibleDistanceM;
        private float _nextScanTime;
        private float _lastSeenTime = float.NegativeInfinity;
        private float _awareness01;
        private EThiefAwarenessStage _awarenessStage = EThiefAwarenessStage.Calm;
        private float _lastAwarenessUpdateTime;
        private bool _awarenessTimeInitialized;

        public Transform VisiblePlayer => HasVisiblePlayer ? _visiblePlayer : null;
        public bool HasVisiblePlayer => _visiblePlayer != null &&
            Time.time <= _lastSeenTime + _lostSightHoldSeconds;
        public bool IsCloseThreat => HasVisiblePlayer && _visibleDistanceM <= _closeThreatRangeM;
        public float Awareness01 => _awareness01;
        public EThiefAwarenessStage AwarenessStage => _awarenessStage;

        public void Refresh()
        {
            if (Time.time >= _nextScanTime)
            {
                _nextScanTime = Time.time + _scanIntervalSeconds;
                ScanForPlayer();
            }
            UpdateAwareness();
        }

        private void ScanForPlayer()
        {
            PenguinLocomotion[] players = FindObjectsByType<PenguinLocomotion>(FindObjectsSortMode.None);
            Transform best = null;
            float bestDistance = float.MaxValue;
            for (int index = 0; index < players.Length; index++)
            {
                PenguinLocomotion player = players[index];
                if (player == null || !player.isActiveAndEnabled ||
                    player.gameObject.scene != gameObject.scene) continue;
                float distance = Vector3.Distance(transform.position, player.transform.position);
                if (distance >= bestDistance || !CanSee(player.transform, distance)) continue;
                best = player.transform;
                bestDistance = distance;
            }

            if (best != null)
            {
                _visiblePlayer = best;
                _visibleDistanceM = bestDistance;
                _lastSeenTime = Time.time;
                return;
            }

            if (!HasVisiblePlayer)
            {
                _visiblePlayer = null;
                _visibleDistanceM = 0f;
            }
        }

        private void UpdateAwareness()
        {
            float now = Time.time;
            float deltaSeconds = _awarenessTimeInitialized ? Mathf.Max(0f, now - _lastAwarenessUpdateTime) : 0f;
            _awarenessTimeInitialized = true;
            _lastAwarenessUpdateTime = now;

            _awareness01 = NextAwareness01(_awareness01, HasVisiblePlayer, IsCloseThreat, deltaSeconds,
                _waryEnterThreshold, _awarenessDecayPerSecond);
            _awarenessStage = NextAwarenessStage(_awarenessStage, _awareness01,
                _waryEnterThreshold, _waryExitThreshold, _spottedExitThreshold);
        }

        /// <summary>즉시 상승(시야 확보 시 최소 Wary, 근접 시 즉시 만점), 소실 시 완만한 감쇠.</summary>
        public static float NextAwareness01(float current01, bool hasVisiblePlayer, bool isCloseThreat,
            float deltaSeconds, float waryFloor01, float decayPerSecond)
        {
            if (isCloseThreat) return 1f;
            if (hasVisiblePlayer)
                return current01 < waryFloor01
                    ? waryFloor01 : Mathf.Max(waryFloor01, current01 - decayPerSecond * deltaSeconds);
            return Mathf.Clamp01(current01 - decayPerSecond * deltaSeconds);
        }

        /// <summary>히스테리시스가 있는 3단계 분류 — 진입 문턱과 이탈 문턱이 달라 경계선 깜빡임을 없앤다.</summary>
        public static EThiefAwarenessStage NextAwarenessStage(EThiefAwarenessStage current, float value01,
            float waryEnterThreshold, float waryExitThreshold, float spottedExitThreshold)
        {
            if (current == EThiefAwarenessStage.Spotted)
                return value01 > spottedExitThreshold ? EThiefAwarenessStage.Spotted : EThiefAwarenessStage.Wary;
            if (value01 >= 1f) return EThiefAwarenessStage.Spotted;
            if (current == EThiefAwarenessStage.Wary)
                return value01 <= waryExitThreshold ? EThiefAwarenessStage.Calm : EThiefAwarenessStage.Wary;
            return value01 >= waryEnterThreshold ? EThiefAwarenessStage.Wary : EThiefAwarenessStage.Calm;
        }

        public static bool IsInsideView(Vector3 forward, Vector3 toTarget,
            float rangeM, float fieldOfViewDeg)
        {
            Vector3 flatForward = Vector3.ProjectOnPlane(forward, Vector3.up);
            Vector3 flatTarget = Vector3.ProjectOnPlane(toTarget, Vector3.up);
            if (flatTarget.sqrMagnitude > rangeM * rangeM || flatTarget.sqrMagnitude <= 0.0001f)
                return false;
            if (flatForward.sqrMagnitude <= 0.0001f) return true;
            return Vector3.Angle(flatForward, flatTarget) <= fieldOfViewDeg * 0.5f;
        }

        private bool CanSee(Transform target, float distance)
        {
            Vector3 origin = transform.position + Vector3.up * _eyeHeightM;
            Vector3 targetPoint = target.position + Vector3.up * 0.7f;
            Vector3 delta = targetPoint - origin;
            if (!IsInsideView(transform.forward, delta, _visualRangeM, _fieldOfViewDeg)) return false;
            if (distance <= 0.001f) return true;

            int count = Physics.RaycastNonAlloc(origin, delta.normalized, _hits, delta.magnitude,
                _occlusionMask, QueryTriggerInteraction.Ignore);
            float nearestBlockingDistance = float.MaxValue;
            for (int index = 0; index < count; index++)
            {
                Collider hit = _hits[index].collider;
                if (hit == null || hit.transform.IsChildOf(transform)) continue;
                if (hit.transform == target || hit.transform.IsChildOf(target)) return true;
                nearestBlockingDistance = Mathf.Min(nearestBlockingDistance, _hits[index].distance);
            }
            return nearestBlockingDistance == float.MaxValue;
        }
    }
}
