using UnityEngine;

namespace PPack
{
    /// <summary>개발용 의뢰 플로우가 완료 시점을 판단하는 교체점.</summary>
    public abstract class RequestCompletionCondition : MonoBehaviour
    {
        public abstract bool IsSatisfied(Transform player, RequestDirector director, GiftRequest request);
    }
}
