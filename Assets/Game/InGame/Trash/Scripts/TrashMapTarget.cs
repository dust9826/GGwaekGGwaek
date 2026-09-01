using System;
using UnityEngine;

namespace PPack
{
    /// <summary>
    /// Trash 프롭이 맵 UI에 공개하는 최소 상태. UI는 Transform과 분류만 읽고 수거 판정은
    /// 소유하지 않는다. 런타임 생성/제거도 정적 이벤트로 알리므로 주기적인 전체 검색이 없다.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class TrashMapTarget : MonoBehaviour
    {
        public enum TrashSize
        {
            Small,
            Medium,
            Large
        }

        [SerializeField] private TrashSize _size = TrashSize.Small;

        public static event Action<TrashMapTarget> Registered;
        public static event Action<TrashMapTarget> Unregistered;

        public TrashSize Size => _size;
        public Vector3 WorldPosition => transform.position;

        private void OnEnable() => Registered?.Invoke(this);
        private void OnDisable() => Unregistered?.Invoke(this);
    }
}
