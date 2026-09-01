using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 멀티 게임플레이 씬이 <see cref="SessionLauncher"/> 에게 <b>자기 이름을 알려 주는</b> 곳.
    ///
    /// <para><b>왜 씬이 알려 주는가.</b> 전에는 <c>SessionLauncher</c> 가 게임플레이 씬 경로를
    /// <c>const</c> 로 박고 아바타 기본값을 <c>"PF_MultiplayPlow"</c> 로 두었다. 그것은 <c>Core</c> 가
    /// <c>InGame</c> 의 파일 경로와 프리팹 이름을 아는 것이고, 어셈블리로 막아 둔 경계를 문자열로
    /// 우회하는 것이다. 게다가 씬이 아무것도 넣지 않으면 조용히 제설차가 스폰되어 원인이 씬에서
    /// 멀리 보였다(2026-08-24 에 지웠다).</para>
    ///
    /// <para><b>씬 경로를 여기 문자열로 두는 것은 중복이 아니다.</b> 이 오브젝트는 그 씬 안에만
    /// 있으므로, 경로가 틀리면 그 씬을 열자마자 <c>StartMatch</c> 가 "빌드 세팅에 없다" 로 죽는다.
    /// 반대로 <c>Core</c> 에 두면 씬을 지워도 상수가 남아 아무도 모른다 — 실제로 그렇게 됐었다.</para>
    ///
    /// <para><b>로비에서 오는 경로에서도 이 값이 필요하다.</b> 서버는 게임플레이 씬을 <b>올리기 전에</b>
    /// 경로를 알아야 하는데 그때 이 오브젝트는 아직 없다. 그래서 이 컴포넌트는 게임플레이 씬뿐
    /// 아니라 <b>세션이 시작되는 씬(메인 메뉴)에도</b> 놓아야 한다. 값이 같으므로 두 번 넣어도
    /// 무해하고, 어느 쪽에서 시작하든 경로가 채워진다.</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class MultiPlayBootstrap : MonoBehaviour
    {
        [Tooltip("이 씬의 경로. SessionLauncher 가 매치 시작에서 이것을 올린다.")]
        [SerializeField]
        private string _gameplayScenePath = "Assets/Game/InGame/Cleanliness/Scenes/MultiPlay.unity";

        [Tooltip("스폰할 아바타 프리팹 이름. Resources 아래에 있어야 한다.")]
        [SerializeField] private string _avatarResource = "PF_PenguinNet";

        [Tooltip("아바타가 설 자리. 비어 있으면 런처가 원점 둘레에 세운다 — 마을 씬에서는 지하다.")]
        [SerializeField] private Transform[] _spawnPoints;

        private void Awake()
        {
            if (!string.IsNullOrEmpty(_gameplayScenePath))
                SessionLauncher.GameplayScenePath = _gameplayScenePath;

            if (!string.IsNullOrEmpty(_avatarResource))
                SessionLauncher.SceneAvatarResource = _avatarResource;

            PublishSpawnPoses();
        }

        /// <summary>메인 메뉴에도 이 컴포넌트가 있으므로, 자리가 없는 쪽이 게임플레이 씬이 넣은 값을
        /// 지우지 않게 채워져 있을 때만 넣는다.</summary>
        private void PublishSpawnPoses()
        {
            if (_spawnPoints == null || _spawnPoints.Length == 0) return;

            var poses = new List<Pose>(_spawnPoints.Length);
            foreach (Transform point in _spawnPoints)
                if (point != null) poses.Add(new Pose(point.position, point.rotation));

            if (poses.Count > 0) SessionLauncher.SceneSpawnPoses = poses.ToArray();
        }
    }
}
