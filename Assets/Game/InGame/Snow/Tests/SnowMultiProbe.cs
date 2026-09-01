using System.Collections;
using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// <b>진짜 2프로세스</b> 멀티에서 눈이 도는지 재는 일회성 프로브.
    ///
    /// <para>자동 테스트(<c>SnowCpuSyncTests</c>)는 <c>PeerMode = Multiple</c> 로 한 프로세스에 피어를
    /// 여럿 띄운다. 그것은 편하지만 <b>프로덕션 토폴로지가 아니다</b> - 실제로는 프로세스마다 피어
    /// 하나이고, 그 경로에는 포톤 릴레이·직렬화·왕복이 전부 들어 있다. 이 프로브는 그 경로를 지난다.</para>
    ///
    /// <para>씬에 저장하지 않는다. 에디터가 이 컴포넌트를 런타임에 붙이고, 결과를 콘솔 한 줄로 남긴다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowMultiProbe : MonoBehaviour
    {
        public const string RoomCode = "XPEN";

        [SerializeField] private bool _isServer;

        private void Start()
        {
            // <b>씬 교체를 살아남아야 한다.</b> 서버가 매치를 시작하면 LoadSceneMode.Single 로 게임플레이
            // 씬을 올리면서 현재 씬을 내리고, 그때 이 오브젝트가 파괴되면 코루틴이 조용히 멈춘다 -
            // 실측으로 그래서 결과 로그가 하나도 안 남았다(연결과 씬 동기화는 정상이었다).
            DontDestroyOnLoad(gameObject);
            StartCoroutine(Run());
        }

        private IEnumerator Run()
        {
            string who = _isServer ? "서버" : "클라";
            Debug.Log($"[MultiProbe] {who} 시작");

            if (_isServer)
            {
                var host = SessionLauncher.HostServerOnly(RoomCode);
                while (!host.IsCompleted) yield return null;
                Debug.Log($"[MultiProbe] 서버 방 만들기 = {host.Result}");
                if (!host.Result) yield break;
            }
            else
            {
                var join = SessionLauncher.JoinRoom(RoomCode);
                while (!join.IsCompleted) yield return null;
                Debug.Log($"[MultiProbe] 클라 참가 = {join.Result}");
                if (!join.Result) yield break;
            }

            // 서버는 사람이 둘 다 들어온 뒤 매치를 시작한다. 클라가 붙을 시간을 준다.
            if (_isServer)
            {
                float until = Time.realtimeSinceStartup + 40f;
                while (Time.realtimeSinceStartup < until)
                {
                    if (SessionLauncher.LocalServer?.Runner != null
                        && SessionLauncher.LocalServer.Runner.ActivePlayers.GetEnumerator().MoveNext())
                    {
                        break;
                    }

                    yield return null;
                }

                var match = SessionLauncher.StartMatch();
                while (!match.IsCompleted) yield return null;
                Debug.Log($"[MultiProbe] 매치 시작 = {match.Result}");
            }

            // 두 피어 모두 스테이지가 뜨기를 기다린다.
            SnowCpuStage stage = null;
            float deadline = Time.realtimeSinceStartup + 60f;
            while (stage == null && Time.realtimeSinceStartup < deadline)
            {
                foreach (SnowCpuStage s in FindObjectsByType<SnowCpuStage>(FindObjectsSortMode.None))
                {
                    if (s.Runner == null || !s.Runner.IsRunning || s.Field == null) continue;
                    stage = s;
                }

                if (stage == null) yield return null;
            }

            if (stage == null) { Debug.LogError("[MultiProbe] 스테이지가 안 떴다"); yield break; }

            Debug.Log($"[MultiProbe] {who} 스테이지 준비 - 총량 {stage.TotalHeightMm:N0}");

            // 클라이언트가 사람처럼 조작한다: E로 뭉친 뒤 좌클릭을 누른 채 전진한다.
            if (!_isServer)
            {
                bool action = false;
                bool create = false;
                Vector2 move = Vector2.zero;
                SessionLauncher.TestInputSource = _ =>
                {
                    var data = new NetworkInputData { Move = move };
                    data.Buttons.Set((int)EInputButton.Action, action);
                    data.Buttons.Set((int)EInputButton.CreateSnowball, create);
                    return data;
                };

                yield return new WaitForSeconds(2f);
                create = true;
                yield return new WaitForSeconds(1f);
                create = false;
                action = true;
                move = new Vector2(0f, 1f);
                yield return new WaitForSeconds(6f);
                move = Vector2.zero;
                action = false;
            }

            // 전파 속도를 잰다. 1.5 초마다 <b>고정 좌표</b>의 깊이와 서버 대기열을 남긴다 -
            // 대기열이 쌓이는 속도와 빠지는 속도의 차이가 곧 "느리게 느껴지는" 것의 정체다.
            int prevSent = SnowCpuStage.DebugChunksSent;
            for (int round = 0; round < 24; round++)
            {
                yield return new WaitForSeconds(1.5f);

                int pending = 0;
                if (_isServer && stage.Runner != null)
                {
                    foreach (PlayerRef pr in stage.Runner.ActivePlayers)
                    {
                        int q = stage.PendingStaleFor(pr.PlayerId);
                        if (q > pending) pending = q;
                    }
                }

                int sent = SnowCpuStage.DebugChunksSent;
                int sentDelta = sent - prevSent;
                prevSent = sent;

                long held = 0;
                foreach (SnowBallCarrier b in FindObjectsByType<SnowBallCarrier>(FindObjectsSortMode.None))
                    held = b.MassMm;

                Debug.Log($"[MultiProbe:속도] {who} t{round * 1.5f:F1}s " +
                          $"z4={stage.HeightAtM(3f, 4f) * 100f:F1} " +
                          $"z10={stage.HeightAtM(3f, 10f) * 100f:F1} " +
                          $"z16={stage.HeightAtM(3f, 16f) * 100f:F1} " +
                          $"공={held:N0} 대기열={pending} 보낸청크/1.5s={sentDelta}");
            }
        }
    }
}
