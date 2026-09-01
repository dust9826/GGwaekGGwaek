using Fusion;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// 선물 상자의 <b>네트워크 신원</b>. 종류 하나만 복제한다.
    ///
    /// <para>자세는 <c>NetworkRigidbody</c> 가, 완료 판정은 서버의 <see cref="RequestDirector"/> 가
    /// 소유한다. 색·리본·라벨은 종류에서 유도되는 <b>결과</b>이므로 보내지 않는다 — 루트 규약대로
    /// 원인만 보내고 각 피어가 다시 그린다.</para>
    ///
    /// <para>종류를 보내야 하는 이유는 의뢰가 종류로 판정되기 때문이다. 클라이언트가 상자 색을
    /// 다르게 그리면 "빨강을 가져갔는데 파랑이 배달됐다" 가 된다.</para>
    /// </summary>
    [RequireComponent(typeof(Gift))]
    [DisallowMultipleComponent]
    public sealed class GiftNetState : NetworkBehaviour
    {
        [Networked] private byte NetKind { get; set; }

        private Gift _gift;
        private int _appliedKind = -1;

        public override void Spawned()
        {
            _gift = GetComponent<Gift>();
            if (Object.HasStateAuthority) NetKind = (byte)_gift.Kind;
            ApplyKind();
        }

        public override void Render() => ApplyKind();

        /// <summary>서버가 스폰 직전에 종류를 정한다. 프리팹 기본값 그대로 두면 전부 같은 색이 된다.</summary>
        public void ServerSetKind(EGiftBoxKind kind)
        {
            NetKind = (byte)kind;
            _gift = GetComponent<Gift>();
            _gift.SetKind(kind);
            _appliedKind = (int)kind;
        }

        private void ApplyKind()
        {
            if (_gift == null) _gift = GetComponent<Gift>();
            if (_gift == null || _appliedKind == NetKind) return;
            _appliedKind = NetKind;
            _gift.SetKind((EGiftBoxKind)NetKind);
        }
    }
}
