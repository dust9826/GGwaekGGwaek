using System.Collections;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 세션 경로를 <b>한 번에</b> 확인하는 검증 프로브.
    ///
    /// <para>왜 컴포넌트인가: 검증 중에 <c>eval</c>(에디터 CLI 의 Roslyn 실행)을 여러 번 부르면 컴파일
    /// 스톨이 생기고, 그 스톨이 Photon 연결을 끊어 세션이 조용히 내려간다(런너 0, 에러 0). "차가 안
    /// 움직인다"·"START 가 안 먹는다"로 보였던 증상이 실제로는 이것이었다. 그래서 시작·대기·판독을
    /// 코루틴 하나가 하고 결과만 콘솔에 남긴다 — 콘솔 읽기는 CLI 명령이라 스톨이 없다.</para>
    ///
    /// <para>빈 오브젝트에 붙이면 Play 시 서버 단독 경로(방 열기 → 매치 시작 → 씬 전환)를 밟고 그 결과를
    /// 로그로 남긴다. 클라이언트가 필요한 검증은 MPPM 인스턴스로 한다.</para>
    /// </summary>
    public sealed class SessionOneShotProbe : MonoBehaviour
    {
        [SerializeField] private string _roomCode = "PROBE1";

        private IEnumerator Start()
        {
            Debug.Log("[Probe] 방을 연다");
            var host = SessionLauncher.HostServerOnly(_roomCode);
            while (!host.IsCompleted) yield return null;
            Debug.Log($"[Probe] host ok={host.Result} phase={SessionLauncher.Phase} " +
                      $"lobby={(SessionLobby.Instance != null)}");

            yield return new WaitForSeconds(2f);

            Debug.Log("[Probe] 서버에서 매치를 시작한다");
            var match = SessionLauncher.StartMatch();
            while (!match.IsCompleted) yield return null;
            Debug.Log($"[Probe] startMatch ok={match.Result} phase={SessionLauncher.Phase}");

            yield return new WaitForSeconds(3f);
            Debug.Log($"[Probe] 최종 phase={SessionLauncher.Phase} " +
                      $"scene={UnityEngine.SceneManagement.SceneManager.GetActiveScene().name}");
        }
    }
}
