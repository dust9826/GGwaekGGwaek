using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace PPack
{
    /// <summary>
    /// Unity 6000.6.0b7에서 Domain Reload 없이 SnowDelivery PlayMode 테스트를 반복하면
    /// GPUResidentDrawer/ObjectDispatcher 네이티브 크래시가 발생한다. 프로젝트 설정이 다른
    /// 브랜치나 수동 변경으로 되돌아가더라도 에디터 시작과 Play Mode 진입 전에 안전하게 복구한다.
    /// 또한 이 버전에서 이전 씬이 로드한 Opsive BehaviorTree 자산이 에디터 캐시에 남으면
    /// Domain Reload 직렬화가 실패하므로 SinglePlay 진입 직전에 해당 캐시를 비운다.
    /// </summary>
    [InitializeOnLoad]
    internal static class SnowDeliveryPlayModeStabilityGuard
    {
        private const string SinglePlayScenePath =
            "Assets/Game/InGame/Cleanliness/Scenes/SinglePlay.unity";

        static SnowDeliveryPlayModeStabilityGuard()
        {
            EditorApplication.delayCall += EnforceDomainReload;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
        }

        private static void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state != PlayModeStateChange.ExitingEditMode) return;

            EnforceDomainReload();
            ReleaseCachedSinglePlayAssets();
        }

        private static void ReleaseCachedSinglePlayAssets()
        {
            if (SceneManager.GetActiveScene().path != SinglePlayScenePath) return;

            Selection.objects = System.Array.Empty<Object>();
            EditorUtility.UnloadUnusedAssetsImmediate(false);
        }

        private static void EnforceDomainReload()
        {
            if (!EditorSettings.enterPlayModeOptionsEnabled) return;

            EnterPlayModeOptions options = EditorSettings.enterPlayModeOptions;
            if ((options & EnterPlayModeOptions.DisableDomainReload) == 0) return;

            EditorSettings.enterPlayModeOptions =
                options & ~EnterPlayModeOptions.DisableDomainReload;
            Debug.LogWarning(
                "SnowDelivery 안정성 보호: Unity 6000.6.0b7 네이티브 크래시를 막기 위해 " +
                "Enter Play Mode Options의 Disable Domain Reload를 해제했습니다.");
        }
    }
}
