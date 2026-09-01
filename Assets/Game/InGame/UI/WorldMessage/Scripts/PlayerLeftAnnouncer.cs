using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 세션에서 누가 빠지면 그것을 화면 알림으로 바꾼다. <b>이 클래스가 경계다</b> —
    /// <c>Core</c> 는 "빠졌다" 는 사실만 알리고(<see cref="SessionLauncher.PlayerLeft"/>),
    /// 그것을 토스트로 볼지는 여기서 정한다.
    ///
    /// <para><b>복제하지 않는다.</b> Fusion 의 <c>OnPlayerLeft</c> 는 전 피어에서 불리므로 각자
    /// 자기 화면에 띄우면 된다. 서버가 알려 줄 필요가 없어서 <c>[Networked]</c> 도
    /// <c>NetworkObject</c> 도 쓰지 않는다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlayerLeftAnnouncer : MonoBehaviour
    {
        private void OnEnable()
        {
            SessionLauncher.PlayerLeft -= OnPlayerLeft;
            SessionLauncher.PlayerLeft += OnPlayerLeft;
        }

        // 정적 이벤트라 해지를 빠뜨리면 씬을 넘어 살아남는다. PlayMode 배치는 DisableSceneReload 라
        // 그 구독이 다음 판까지 따라가 죽은 오브젝트를 부른다.
        private void OnDisable() => SessionLauncher.PlayerLeft -= OnPlayerLeft;

        private void OnPlayerLeft(PlayerRef player) => WorldMessagePresenter.Post(Describe(player));

        /// <summary>
        /// 나간 사람을 어떻게 부를지. 이름을 모르면 <c>#2 LEFT</c> 가 된다.
        ///
        /// <para><b>로비를 읽지 않는다</b> — 그것은 게임플레이 씬이 올라오면 사라진다(2026-09-01 실측).
        /// <see cref="SessionLauncher"/> 가 로비가 살아 있는 동안 베껴 둔 것을 읽는다. 그리고 나가는
        /// 순간에는 이미 늦을 수 있으므로, <b>미리 적어 둔 것</b>을 읽는 것이 요점이다.</para>
        /// </summary>
        internal static string Describe(PlayerRef player) =>
            $"{SessionLobby.Format(SessionLauncher.NameOf(player.PlayerId), player.PlayerId)} LEFT";
    }
}
