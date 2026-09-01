using UnityEngine;

namespace PPack
{
    /// <summary>증강 한 장.
    ///
    /// <para><b><see cref="Id"/> 는 안정 키다.</b> 이름·설명은 튜닝 대상이지만 이것은 아니다 —
    /// 나중 로컬라이제이션 테이블이 이 값을 건다(스펙 §9).</para>
    ///
    /// <para>이득과 패널티를 배열 둘로 나눈 이유는 UI 다. 부호로 좋고 나쁨을 추론하려면
    /// "이 스탯은 높을수록 좋다" 테이블이 따로 필요한데, 데이터 작성자가 명시하면 그것이
    /// 통째로 사라진다.</para>
    ///
    /// <para>필드가 public 인 것은 <see cref="StageBalanceConfig"/> 선례를 따른 것이다 —
    /// 인스펙터 튜닝이 목적이고 런타임 변경 지점이 없다.</para></summary>
    [CreateAssetMenu(menuName = "PPack/Augment/Augment Definition")]
    public sealed class AugmentDefinition : ScriptableObject
    {
        [Tooltip("안정 키. 문구가 바뀌어도 바꾸지 않는다.")]
        public string Id;

        [Tooltip("카드에 뜨는 이름. 영어로 쓴다(스펙 §9).")]
        public string DisplayName;

        [TextArea, Tooltip("카드 한 줄 설명. 영어로 쓴다.")]
        public string Description;

        public AugmentEffect[] Benefits = new AugmentEffect[0];
        public AugmentEffect[] Penalties = new AugmentEffect[0];

        [Tooltip("추첨 가중치. 0이면 안 나온다.")]
        [Min(0f)] public float Weight = 1f;
    }
}
