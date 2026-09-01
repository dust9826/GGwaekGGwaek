using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 맵에 놓는 NPC 한 명의 스폰 지점. 서버 또는 NetworkRunner가 없는 단독 모드에서만 스폰한다.
    /// 여러 NPC를 같은 <see cref="NpcGroupContext"/>에 연결해도 각 NPC 오브젝트는 독립적이다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NpcSpawnPoint : NetworkBehaviour
    {
        [SerializeField] private GameObject _npcPrefab;
        [SerializeField] private NpcGroupContext _group;

        private bool _spawned;

        private bool IsNetworked => Object != null && Object.IsValid;
        private bool IsAuthority => !IsNetworked || Object.HasStateAuthority;

        public override void Spawned()
        {
            if (IsAuthority) SpawnNpc();
        }

        private void Update()
        {
            if (_spawned || IsNetworked) return;
            if (NetworkRunner.Instances.Count > 0) return;
            SpawnNpc();
        }

        private void SpawnNpc()
        {
            if (_spawned || _npcPrefab == null) return;
            _spawned = true;

            GameObject instance;
            if (Runner == null)
            {
                instance = Instantiate(_npcPrefab, transform.position, transform.rotation);
            }
            else
            {
                NetworkObject networkPrefab = _npcPrefab.GetComponent<NetworkObject>();
                if (networkPrefab == null)
                {
                    Debug.LogError($"{name}: 멀티플레이 NPC 프리팹에는 NetworkObject가 필요합니다.", this);
                    return;
                }

                NetworkObject networkInstance = Runner.Spawn(networkPrefab, transform.position, transform.rotation);
                if (networkInstance == null) return;
                instance = networkInstance.gameObject;
            }

            NpcGroupMember member = instance.GetComponent<NpcGroupMember>();
            if (member == null)
            {
                Debug.LogError($"{name}: NPC 프리팹에 NpcGroupMember가 필요합니다.", instance);
                return;
            }

            member.SetGroup(_group);
        }

        private void OnDrawGizmos()
        {
            Gizmos.color = new Color(0.3f, 1f, 0.55f, 0.8f);
            Gizmos.DrawWireSphere(transform.position, 0.25f);
            Gizmos.DrawLine(transform.position, transform.position + transform.forward);
        }
    }
}

