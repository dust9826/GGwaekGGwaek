using UnityEngine;

namespace PPack
{
    /// <summary>스테이지 진행 권위와 맵의 날짜 연출, 예약 눈폭풍을 잇는 접착부.
    /// Map과 weather가 Cleanliness를 직접 참조하지 않도록 이 기능이 오케스트레이션을 맡는다.</summary>
    [DisallowMultipleComponent]
    public sealed class StageDateCoordinator : MonoBehaviour
    {
        [SerializeField] private GameManager _gameManager;
        [SerializeField] private TimeOfDayDirector _timeOfDay;
        [SerializeField] private ScheduledBlizzardDirector _blizzard;

        public void Configure(GameManager gameManager, TimeOfDayDirector timeOfDay,
            ScheduledBlizzardDirector blizzard)
        {
            Unsubscribe();
            _gameManager = gameManager;
            _timeOfDay = timeOfDay;
            _blizzard = blizzard;
            Subscribe();
        }

        private void OnEnable() => Subscribe();

        private void Start()
        {
            if (_gameManager != null && _gameManager.Phase == EGamePhase.Playing)
                OnGameStarted();
        }

        private void OnDisable()
        {
            if (_timeOfDay != null) _timeOfDay.Pause();
            Unsubscribe();
        }

        private void Subscribe()
        {
            if (_gameManager != null)
            {
                _gameManager.GameStarted -= OnGameStarted;
                _gameManager.GameStarted += OnGameStarted;
                _gameManager.GameEnded -= OnGameEnded;
                _gameManager.GameEnded += OnGameEnded;
            }

            if (_timeOfDay != null)
            {
                _timeOfDay.DayAdvanced -= OnDayAdvanced;
                _timeOfDay.DayAdvanced += OnDayAdvanced;
            }
        }

        private void Unsubscribe()
        {
            if (_gameManager != null)
            {
                _gameManager.GameStarted -= OnGameStarted;
                _gameManager.GameEnded -= OnGameEnded;
            }

            if (_timeOfDay != null) _timeOfDay.DayAdvanced -= OnDayAdvanced;
        }

        private void OnGameStarted()
        {
            if (_timeOfDay == null) return;
            _timeOfDay.Begin();
            NotifyDateStarted(_timeOfDay.DayIndex);
        }

        private void OnGameEnded()
        {
            if (_timeOfDay != null) _timeOfDay.Pause();
        }

        private void OnDayAdvanced(int dayIndex) => NotifyDateStarted(dayIndex);

        private void NotifyDateStarted(int dayIndex)
        {
            if (_blizzard != null) _blizzard.NotifyDateStarted(dayIndex);
        }
    }
}
