using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 눈덩이(또는 그에 준하는 투사체)에 맞았다는 사실 하나. 던지는 쪽(`InGame/Interaction/`)과
    /// 맞는 쪽(`InGame/Penguin/`)이 첫날부터 서로 다른 두 소비자라 `Core`에 둔다 — 게임플레이
    /// 효과(넉백·감속·무효)는 아직 정하지 않았으므로 여기 담지 않는다.
    /// </summary>
    public readonly struct SnowballHit
    {
        public readonly Vector3 Point;
        public readonly Vector3 Normal;
        public readonly Vector3 Direction;
        public readonly float MomentumKgMps;
        public readonly GameObject Source;

        public SnowballHit(Vector3 point, Vector3 normal, Vector3 direction, float momentumKgMps, GameObject source)
        {
            Point = point;
            Normal = normal;
            Direction = direction;
            MomentumKgMps = momentumKgMps;
            Source = source;
        }
    }
}
