using System;
using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>이 판이 얻은 증강 전부와 스탯별 합산값.
    ///
    /// <para><b>static 이 아니다.</b> 지금은 판에 하나(팀 공유)지만, 소유가 per-player 로
    /// 뒤집혀도 붙이는 위치만 바뀌고 코드는 그대로다(스펙 §2·§5).</para>
    ///
    /// <para>소비처는 <see cref="GetMultiplier"/> 또는 <see cref="GetValue"/> 를 필요할 때
    /// 읽는다. 참조가 비어 있으면 효과가 없고 <b>기존 동작 그대로</b>다.</para></summary>
    [DisallowMultipleComponent]
    public sealed class AugmentLoadout : MonoBehaviour
    {
        private static readonly int StatCount = Enum.GetValues(typeof(EAugmentStat)).Length;

        private readonly List<AugmentDefinition> _owned = new();
        private readonly float[] _values = new float[StatCount];

        public IReadOnlyList<AugmentDefinition> Owned => _owned;

        /// <summary>획득 목록이 바뀌었다.</summary>
        public event Action Changed;

        /// <summary>가산 합계. 확률 축은 이 값을 그대로 쓴다.</summary>
        public float GetValue(EAugmentStat stat) => _values[(int)stat];

        /// <summary>배율 축이 쓰는 값. <b>0 아래로 내려가지 않는다</b> — 패널티가 겹쳐도
        /// 보상이 음수가 되지는 않는다.</summary>
        public float GetMultiplier(EAugmentStat stat) => Mathf.Max(0f, 1f + _values[(int)stat]);

        public bool Has(AugmentDefinition definition) => definition != null && _owned.Contains(definition);

        public void Add(AugmentDefinition definition)
        {
            if (definition == null) return;

            _owned.Add(definition);
            Accumulate(definition.Benefits);
            Accumulate(definition.Penalties);
            Changed?.Invoke();
        }

        /// <summary>판이 다시 시작할 때 비운다.</summary>
        public void Clear()
        {
            if (_owned.Count == 0) return;

            _owned.Clear();
            Array.Clear(_values, 0, _values.Length);
            Changed?.Invoke();
        }

        private void Accumulate(AugmentEffect[] effects)
        {
            if (effects is null) return;

            for (int index = 0; index < effects.Length; index++)
                _values[(int)effects[index].Stat] += effects[index].Value;
        }
    }
}
