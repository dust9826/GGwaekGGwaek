using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 펭귄이 눈덩이 투사체에 맞았다는 사실을 복제하는 수신기. <b>게임플레이 효과는 0</b> —
    /// 넉백·감속·무효 중 무엇을 붙일지 아직 정하지 않았으므로, 카운터만 늘리고 실제 반응은 이
    /// 컴포넌트를 구독하는 쪽(추후)에 맡긴다.
    ///
    /// <para><see cref="ImpactDoor"/>·<see cref="ImpactBreakable"/> 과 같은 패턴이다 — 서버가
    /// 히트 카운터만 올리고, 각 피어가 그 카운터가 바뀐 것을 <see cref="Update"/> 폴링으로 보고
    /// 자기 쪽에서 <see cref="HitReceived"/> 를 발생시킨다(원인만 복제, 결과는 로컬).</para>
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PenguinSnowballHit : NetworkBehaviour, ISnowballHittable
    {
        [Networked] private int NetHitCount { get; set; }
        [Networked] private Vector3 NetLastHitPoint { get; set; }
        [Networked] private Vector3 NetLastHitDirection { get; set; }
        [Networked] private float NetLastHitMomentumKgMps { get; set; }

        private int _localHitCount;
        private SnowballHit _localLastHit;
        private int _lastSeenHitCount = -1;

        private bool IsNetworked => Object != null && Object.IsValid;
        private bool IsAuthority => !IsNetworked || Object.HasStateAuthority;

        /// <summary>가장 최근에 받은 히트. 아직 하나도 없으면 기본값.</summary>
        public SnowballHit LastHit { get; private set; }

        /// <summary>새 히트를 받을 때마다(모든 피어에서) 한 번씩 발생.</summary>
        public event System.Action<SnowballHit> HitReceived;

        public void OnSnowballHit(in SnowballHit hit)
        {
            if (!IsAuthority) return;

            if (IsNetworked)
            {
                NetHitCount++;
                NetLastHitPoint = hit.Point;
                NetLastHitDirection = hit.Direction;
                NetLastHitMomentumKgMps = hit.MomentumKgMps;
            }
            else
            {
                _localHitCount++;
                _localLastHit = hit;
            }
        }

        private void Update()
        {
            int hitCount = IsNetworked ? NetHitCount : _localHitCount;
            if (hitCount == _lastSeenHitCount) return;
            _lastSeenHitCount = hitCount;

            SnowballHit hit = IsNetworked
                ? new SnowballHit(NetLastHitPoint, Vector3.up, NetLastHitDirection, NetLastHitMomentumKgMps, null)
                : _localLastHit;

            LastHit = hit;
            HitReceived?.Invoke(hit);
        }
    }
}
