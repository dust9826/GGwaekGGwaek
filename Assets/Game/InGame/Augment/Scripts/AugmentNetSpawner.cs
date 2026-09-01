using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 게임플레이 씬이 <see cref="AugmentNetHub"/> 를 스폰하는 자리.
    /// <see cref="MissionNetSpawner"/> 와 같은 모양이고 같은 이유다 — <c>Core</c> 의 런처가 이
    /// 프리팹을 알면 <c>Core</c> 가 <c>InGame</c> 의 증강을 아는 것이 된다. 씬이 참조를 들면
    /// 씬을 지웠을 때 참조도 함께 사라진다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class AugmentNetSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject _hubPrefab;

        private bool _requested;

        private void Update()
        {
            if (_requested) return;

            // ⚠ <b>매치가 시작된 뒤에만 스폰한다</b>(2026-08-26 실측, MissionNetSpawner 와 같은 함정).
            // 러너는 방을 연 순간부터 서버이므로 그 조건만 보면 로비 단계에서 허브를 스폰하고,
            // 곧이어 StartMatch 의 씬 로드가 그것을 삼킨다 — 스폰은 "한 번 했다" 로 남고 허브는
            // 어디에도 없는 상태가 되며 콘솔에는 아무 오류도 남지 않는다.
            if (SessionLauncher.Phase != ESessionPhase.Playing) return;

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;

            if (_hubPrefab == null)
            {
                _requested = true;
                Debug.LogError($"{nameof(AugmentNetSpawner)}: 허브 프리팹이 비어 있다. 증강이 안 뜬다.");
                return;
            }

            // 스폰이 큐를 거치면 반환값이 null 이므로(SessionLauncher.SpawnAvatar 의 같은 함정),
            // 반환값이 아니라 요청 자체로 한 번을 보장한다.
            _requested = true;
            runner.Spawn(_hubPrefab);
        }
    }
}
