using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 충돌 하나에서 부딪힌 쪽의 운동량을 뽑는 작은 공용 유틸리티. <see cref="ImpactDoor"/> 와
    /// <see cref="ImpactBreakable"/> 둘 다 쓴다 — 두 번째 프롭(상자)이 생긴 시점에 뽑았다(루트
    /// <c>AGENTS.md</c> 의 "두 번째 호출부" 규칙). <b>부딪히는 쪽을 참조하지 않는다</b> —
    /// <c>Collision.rigidbody.mass</c> 와 <c>relativeVelocity</c> 뿐이라 눈덩이든 차량이든 같다.
    /// </summary>
    public static class ImpactMomentum
    {
        /// <summary>
        /// 갈라지는 접촉(또는 콜라이더 없는 충돌)이면 false. 그 외엔 접촉면 법선 방향의 운동량
        /// (kg·m/s, 항상 양수)과 첫 접촉점을 돌려준다.
        /// </summary>
        public static bool TryCompute(Collision collision, out float momentumKgMps, out ContactPoint contact)
        {
            momentumKgMps = 0f;
            contact = default;

            if (collision.rigidbody == null || collision.contactCount == 0) return false;

            contact = collision.GetContact(0);
            float closingSpeed = Vector3.Dot(collision.relativeVelocity, contact.normal);
            if (closingSpeed <= 0f) return false;

            momentumKgMps = closingSpeed * collision.rigidbody.mass;
            return true;
        }
    }
}
