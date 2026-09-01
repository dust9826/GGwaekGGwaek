using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 날짜 시스템과 눈폭풍 사이의 접착부. 날짜 시스템이 도입되면 권위 피어가
    /// <see cref="NotifyDateStarted"/> 를 한 번 호출한다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ScheduledBlizzardDirector : MonoBehaviour
    {
        [SerializeField] private BlizzardEvent _event;
        [SerializeField] private int[] _blizzardDayIndices = { 1 };

        private int _lastTriggeredDayIndex = int.MinValue;
        private BlizzardNetHub _netHub;

        public int LastTriggeredDayIndex => _lastTriggeredDayIndex;

        private void Awake()
        {
            if (_event == null) _event = GetComponent<BlizzardEvent>();
        }

        public bool NotifyDateStarted(int dayIndex)
        {
            if (_event == null || _lastTriggeredDayIndex == dayIndex || !ContainsScheduledDay(dayIndex))
                return false;

            // <b>멀티에서는 서버만 정한다.</b> 전에는 모든 피어가 각자 이 경로를 탔는데, 클라이언트는
            // BlizzardEvent.Trigger() 의 권위 검사에 걸려 경고만 남기고 아무 일도 못 했다. 이제
            // 클라이언트는 여기서 조용히 빠지고, 서버가 복제한 티켓을 BlizzardNetHub 가 받아 재생한다.
            // 허브가 없으면(싱글플레이) 그대로 진행한다.
            BlizzardNetHub hub = _netHub != null
                ? _netHub
                : _netHub = FindAnyObjectByType<BlizzardNetHub>(FindObjectsInactive.Include);
            bool multiplayerFollower = hub != null && hub.Object != null && hub.Object.IsValid
                                       && !hub.Object.HasStateAuthority;
            if (multiplayerFollower) return false;

            if (!_event.TryPlanRoute(out BlizzardRoutePlan route)) return false;
            if (!_event.Trigger(route)) return false;

            // 경로를 먼저 쓰고 티켓을 올린다 - 순서는 허브가 지킨다.
            if (hub != null) hub.RaiseBlizzard(route);

            _lastTriggeredDayIndex = dayIndex;
            return true;
        }

        [ContextMenu("Trigger First Scheduled Day For Testing")]
        private void TriggerFirstScheduledDayForTesting()
        {
            if (!Application.isPlaying)
            {
                Debug.LogWarning($"{nameof(ScheduledBlizzardDirector)} 테스트 트리거는 Play Mode에서만 실행한다.", this);
                return;
            }

            if (_blizzardDayIndices == null || _blizzardDayIndices.Length == 0)
            {
                Debug.LogWarning($"{nameof(ScheduledBlizzardDirector)}에 예약 날짜가 없다.", this);
                return;
            }

            NotifyDateStarted(_blizzardDayIndices[0]);
        }

        private bool ContainsScheduledDay(int dayIndex)
        {
            if (_blizzardDayIndices == null) return false;
            for (int index = 0; index < _blizzardDayIndices.Length; index++)
            {
                if (_blizzardDayIndices[index] == dayIndex) return true;
            }

            return false;
        }
    }
}
