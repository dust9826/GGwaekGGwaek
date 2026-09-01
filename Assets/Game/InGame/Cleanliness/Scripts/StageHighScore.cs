using UnityEngine;

namespace PPack
{
    /// <summary>스테이지 최고 점수의 로컬 저장소. 기록은 <b>기기마다 자기 것</b>이다 — 멀티에서도
    /// 피어끼리 공유하지 않는다. 공유 기록판은 서버가 있어야 하고 지금은 없다.
    /// 키 접두어는 <c>PPack.SelectedStage</c> 등 OutGame 이 이미 쓰는 규칙을 따른다.</summary>
    public static class StageHighScore
    {
        private const string KeyPrefix = "PPack.HighScore.";

        public static int Read(string stageId) => PlayerPrefs.GetInt(KeyFor(stageId), 0);

        /// <summary>기록을 갱신했으면 true — 결과 화면이 NEW RECORD 를 띄우는 근거다.
        /// 동점은 갱신이 아니다.</summary>
        public static bool Submit(string stageId, int score)
        {
            if (score <= Read(stageId)) return false;

            PlayerPrefs.SetInt(KeyFor(stageId), score);
            PlayerPrefs.Save();
            return true;
        }

        public static void Clear(string stageId)
        {
            PlayerPrefs.DeleteKey(KeyFor(stageId));
            PlayerPrefs.Save();
        }

        private static string KeyFor(string stageId) =>
            KeyPrefix + (string.IsNullOrWhiteSpace(stageId) ? "unknown" : stageId);
    }
}
