using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 멀티에서 네트워크 선물 생성을 소유한다. 주기적인 무작위 공급은 <see cref="GiftSpawner"/> 의
    /// 멀티판인 임시 리그이고, 눈덩이 교환기는 위치를 지정해 같은 서버 스폰 경로를 호출한다.
    ///
    /// <para><b>서버만 스폰한다.</b> 각 피어가 자기 난수로 만들면 화면마다 다른 상자가 놓이고, 서버의
    /// 완료 판정은 그중 자기 것만 본다.</para>
    ///
    /// <para><b>매치가 시작된 뒤에만 스폰한다</b> — 로비 단계에서 스폰하면 곧이어 오는 씬 로드가
    /// 삼킨다(<see cref="MissionNetSpawner"/> 에 같은 함정을 적어 두었다).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class GiftNetSpawner : MonoBehaviour
    {
        [SerializeField] private NetworkObject _giftPrefab;
        [SerializeField] private GiftBoxCatalog _catalog;
        [SerializeField, Min(0.5f)] private float _radiusM = 6f;
        [SerializeField, Min(0.2f)] private float _spawnHeightM = 0.5f;
        [SerializeField, Min(1)] private int _maxAlive = 8;
        [SerializeField, Min(0.1f)] private float _intervalSeconds = 4f;
        [SerializeField] private int _randomSeed;

        private System.Random _random;
        private float _timer;

        /// <summary>서버 권위로 지정 위치에 선물을 만든다. 눈덩이 교환기도 이 공급 경로를 쓴다.</summary>
        public bool ServerSpawnGift(
            EGiftBoxKind kind,
            Vector3 position,
            Quaternion rotation,
            Vector3 linearVelocity,
            Vector3 angularVelocity)
        {
            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null || !runner.IsRunning || !runner.IsServer || _giftPrefab == null)
                return false;

            runner.Spawn(_giftPrefab, position, rotation, null,
                (_, spawned) =>
                {
                    if (spawned.TryGetComponent(out GiftNetState state)) state.ServerSetKind(kind);
                    if (!spawned.TryGetComponent(out Rigidbody body)) return;
                    body.linearVelocity = linearVelocity;
                    body.angularVelocity = angularVelocity;
                });
            return true;
        }

        private void Update()
        {
            if (SessionLauncher.Phase != ESessionPhase.Playing) return;

            NetworkRunner runner = NetworkRunner.GetRunnerForScene(gameObject.scene);
            if (runner == null || !runner.IsRunning || !runner.IsServer || _giftPrefab == null) return;

            _timer -= Time.deltaTime;
            if (_timer > 0f) return;
            _timer = _intervalSeconds;

            // 살아 있는 수는 Gift.All 로 센다. Runner.Spawn 은 큐를 거치면 null 을 돌려주므로
            // 반환값을 모아 세면 그 판이 과하게 스폰된다(SessionLauncher.SpawnAvatar 의 같은 함정).
            if (Gift.All.Count >= _maxAlive) return;

            _random ??= _randomSeed != 0 ? new System.Random(_randomSeed) : new System.Random();

            double angle = _random.NextDouble() * System.Math.PI * 2.0;
            double distance = System.Math.Sqrt(_random.NextDouble()) * _radiusM;
            var offset = new Vector3((float)(System.Math.Cos(angle) * distance), _spawnHeightM,
                                     (float)(System.Math.Sin(angle) * distance));
            EGiftBoxKind kind = RollKind();

            ServerSpawnGift(kind, transform.position + offset, Quaternion.identity,
                Vector3.zero, Vector3.zero);
        }

        private EGiftBoxKind RollKind()
        {
            if (_catalog != null && _catalog.Count > 0) return _catalog.KindAt(_random.Next(_catalog.Count));
            return (EGiftBoxKind)_random.Next(7);
        }
    }
}
