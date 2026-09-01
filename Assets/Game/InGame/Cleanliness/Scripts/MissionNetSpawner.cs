using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 게임플레이 씬이 <see cref="MissionNetHub"/>를 스폰하는 자리.
    ///
    /// <para><b>왜 씬이 들고 있나.</b> 아바타 프리팹과 같은 이유다 — <c>Core</c>의 런처가 이 프리팹을
    /// 알면 <c>Core</c>가 <c>InGame</c>의 미션을 아는 것이 된다. 씬이 참조를 들면 씬을 지웠을 때
    /// 참조도 함께 사라진다.</para>
    ///
    /// <para><b>⚠ 씬에 놓인 <see cref="SimulationBehaviour"/>는 <c>Runner</c>를 자동으로 받지 않는다</b>
    /// (2026-08-26 실측: 같은 씬의 <see cref="SnowCpuStage"/>는 <c>Runner</c>가 있는데 이 컴포넌트는
    /// 없었고, <c>FixedUpdateNetwork</c>가 한 번도 불리지 않아 허브가 스폰되지 않았다). 그쪽은
    /// <c>runner.AddGlobal(this)</c>로 <b>스스로 등록</b>해서 틱을 받는다. 이 컴포넌트는 틱이 필요
    /// 없고 스폰 한 번이면 되므로, 등록하지 않고 씬의 런너를 직접 찾는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MissionNetSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject _hubPrefab;

        private bool _requested;

        private void Update()
        {
            if (_requested) return;

            // ⚠ <b>매치가 시작된 뒤에만 스폰한다</b> (2026-08-26 실측). 런너는 방을 연 순간부터
            // 서버이고 돌고 있으므로, 그 조건만 보면 <b>로비 단계에서</b> 허브를 스폰하게 된다.
            // 그러면 곧이어 <c>StartMatch</c> 의 씬 로드가 그것을 삼켜, 스폰은 "한 번 했다" 로 남고
            // 허브는 어디에도 없는 상태가 된다 - 콘솔에는 아무 오류도 남지 않는다.
            if (SessionLauncher.Phase != ESessionPhase.Playing) return;

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null || !runner.IsRunning || !runner.IsServer) return;

            if (_hubPrefab == null)
            {
                _requested = true;
                Debug.LogError($"{nameof(MissionNetSpawner)}: 허브 프리팹이 비어 있다. 미션이 시작되지 않는다.");
                return;
            }

            // 스폰이 큐를 거치면 반환값이 null 이므로(SessionLauncher.SpawnAvatar 의 같은 함정),
            // 반환값이 아니라 요청 자체로 한 번을 보장한다.
            _requested = true;
            runner.Spawn(_hubPrefab);
        }
    }
}
