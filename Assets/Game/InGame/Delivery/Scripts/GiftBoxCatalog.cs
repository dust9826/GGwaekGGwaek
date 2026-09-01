using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>선물 상자의 종류. **4색**이다 — 빨강·노랑·초록·파랑.
    /// 난이도는 <b>빨 &gt; 노 &gt; 초 &gt; 파</b> 순으로 빨강이 가장 어렵고 파랑이 가장 쉽다.
    /// 수치 자체는 코드가 아니라 <see cref="GiftBoxCatalog"/> 에셋의
    /// <see cref="GiftBoxKindEntry.DifficultyWeight"/>가 정한다.
    ///
    /// <para>예전엔 무지개 7색(주황·남색·보라 포함)이었다. <b>값을 연속으로 다시 매겼으므로
    /// 순서를 바꾸면 안 된다</b> — 정수로 직렬화되므로 중간에 끼우면 기존 에셋의 값이
    /// 조용히 다른 색으로 읽힌다. 덩불어 열거형 값이 그대로 난이도 순위다(0이 최고난도).</para></summary>
    public enum EGiftBoxKind
    {
        Red,
        Yellow,
        Green,
        Blue,
    }

    [Serializable]
    public struct GiftBoxKindEntry
    {
        public EGiftBoxKind Kind;
        public string DisplayName;
        public Color Color;

        /// <summary>클수록 어려운 종류. 난이도 = 거리항 × 이 값 × 지터 × 전역스칼라.</summary>
        [Min(0f)] public float DifficultyWeight;
    }

    /// <summary>상자 종류의 단일 출처. 의뢰(원하는 종류)와 실제 선물(<see cref="Gift.Kind"/>)이
    /// 같은 카탈로그를 참조해야 매칭이 어긋나지 않는다. <see cref="VacuumToolModeCatalog"/>와
    /// 같은 "에셋이 데이터, 코드는 읽기만" 패턴이다.
    ///
    /// <para>색별 난이도 가중치·색상은 잠정값이다. <see cref="Reset"/>가 4색을
    /// 빨강 최고 → 파랑 최저로 시드하지만, 확정 수치는 에셋에서 조절한다.</para></summary>
    [CreateAssetMenu(menuName = "PPack/Delivery/Gift Box Catalog")]
    public sealed class GiftBoxCatalog : ScriptableObject
    {
        [SerializeField] private List<GiftBoxKindEntry> _kinds = new List<GiftBoxKindEntry>();

        public IReadOnlyList<GiftBoxKindEntry> Kinds => _kinds;
        public int Count => _kinds.Count;

        public bool TryGet(EGiftBoxKind kind, out GiftBoxKindEntry entry)
        {
            for (int index = 0; index < _kinds.Count; index++)
            {
                if (_kinds[index].Kind != kind) continue;
                entry = _kinds[index];
                return true;
            }

            entry = default;
            return false;
        }

        /// <summary>등록되지 않은 종류는 가중치 1로 본다 — 카탈로그가 비어도 난이도 계산이 죽지 않게.</summary>
        public float DifficultyWeight(EGiftBoxKind kind)
            => TryGet(kind, out GiftBoxKindEntry entry) ? Mathf.Max(entry.DifficultyWeight, 0f) : 1f;

        public Color ColorOf(EGiftBoxKind kind)
            => TryGet(kind, out GiftBoxKindEntry entry) ? entry.Color : Color.white;

        public EGiftBoxKind KindAt(int index) => _kinds[index].Kind;

        private void Reset()
        {
            _kinds = new List<GiftBoxKindEntry>(SeedRainbow());
        }

        /// <summary>잠정 시드. 난이도는 빨강(1.9) → 파랑(1.0)로 내려간다. 확정 수치는 사용자가 준다.
        ///
        /// <para>7색에서 4색으로 줄이면서 간격을 다시 벌렸다. 예전 간격(0.15)을 그대로 두면
        /// 1.90~1.45에 몰려 색깔 난이도 차이가 거의 안 느껴진다.</para></summary>
        public static IEnumerable<GiftBoxKindEntry> SeedRainbow()
        {
            yield return Entry(EGiftBoxKind.Red, "빨강", new Color(0.90f, 0.13f, 0.13f), 1.90f);
            yield return Entry(EGiftBoxKind.Yellow, "노랑", new Color(0.97f, 0.85f, 0.20f), 1.60f);
            yield return Entry(EGiftBoxKind.Green, "초록", new Color(0.25f, 0.72f, 0.30f), 1.30f);
            yield return Entry(EGiftBoxKind.Blue, "파랑", new Color(0.18f, 0.45f, 0.88f), 1.00f);
        }

        private static GiftBoxKindEntry Entry(EGiftBoxKind kind, string name, Color color, float weight)
            => new GiftBoxKindEntry { Kind = kind, DisplayName = name, Color = color, DifficultyWeight = weight };
    }
}
