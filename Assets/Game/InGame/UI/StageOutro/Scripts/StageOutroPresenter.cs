using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>SinglePlayDirector.StageEnded와 StageOutroController를 잇는 접착부. UI가
    /// Cleanliness를 구독하는 방향으로만 의존한다 — SinglePlayDirector는 이 스크립트의 존재를
    /// 모른다(Cleanliness/AGENTS.md 경계). RETRY/CONTINUE 버튼의 씬 전환도 여기서 맡는다.</summary>
    public sealed class StageOutroPresenter : MonoBehaviour
    {
        [SerializeField] private SinglePlayDirector _director;
        [SerializeField] private StageOutroController _outro;
        [SerializeField] private string _singlePlayScenePath = "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";
        [SerializeField] private string _mainMenuScenePath = "Assets/Game/OutGame/UI/MainMenu/Scenes/MainMenu.unity";

        [Tooltip("최고 점수를 저장할 스테이지 키. OutGame 의 PPack.SelectedStage 와 같은 id 를 쓴다.")]
        [SerializeField] private string _stageId = "winter-village";

        private void OnEnable()
        {
            if (_director != null) _director.StageEnded += HandleStageEnded;
        }

        private void OnDisable()
        {
            if (_director != null) _director.StageEnded -= HandleStageEnded;
        }

        /// <summary>결과 카드는 점수와 최고 점수를 그린다.</summary>
        private void HandleStageEnded()
        {
            if (_outro == null || _director == null) return;

            StageMetrics metrics = _director.LastMetrics;
            int clearPercent = Mathf.RoundToInt(metrics.SnowClearedPercent01 * 100f);
            string timeText = FormatTime(_director.ElapsedSeconds);
            bool isNewRecord = StageHighScore.Submit(_stageId, metrics.TotalPoints);

            _outro.gameObject.SetActive(true);
            _outro.SetResult(metrics.TotalPoints, StageHighScore.Read(_stageId), isNewRecord,
                clearPercent, timeText);
        }

        public void OnRetryRequested()
        {
            SceneManager.LoadScene(_singlePlayScenePath, LoadSceneMode.Single);
        }

        public void OnContinueRequested()
        {
            SceneManager.LoadScene(_mainMenuScenePath, LoadSceneMode.Single);
        }

        private static string FormatTime(float seconds)
        {
            int total = Mathf.Max(0, Mathf.RoundToInt(seconds));
            return $"{total / 60:00}:{total % 60:00}";
        }
    }
}
