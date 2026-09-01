using UnityEngine;

namespace PPack
{
    /// <summary>추첨 후보 전부. 프리셋을 나누고 싶으면 에셋을 하나 더 만든다 —
    /// <c>.asset</c> 은 YAML 이라 Plastic 이 병합하지 못한다.</summary>
    [CreateAssetMenu(menuName = "PPack/Augment/Augment Pool")]
    public sealed class AugmentPool : ScriptableObject
    {
        public AugmentDefinition[] Entries = new AugmentDefinition[0];
    }
}
