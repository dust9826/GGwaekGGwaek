using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Serialization;

namespace PPack
{
    public sealed class Gift : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int ColorId = Shader.PropertyToID("_Color");
        private static readonly List<Gift> _all = new List<Gift>();
        private static int _nextId;

        [SerializeField] private int _value = 1;
        [FormerlySerializedAs("_type")]
        // 기본값은 가장 쉬운 종류다. 예전엔 보라였고, 4색으로 줄이면서 그 자리를 파랑이 받는다.
        [SerializeField] private EGiftBoxKind _kind = EGiftBoxKind.Blue;

        private int _id;
        private Object _claimOwner;
        private bool _legacyCarried;

        public int Value => _value;
        public int Id => _id;
        public EGiftBoxKind Kind => _kind;
        public bool IsCarried => _legacyCarried || _claimOwner != null;
        public Object ClaimOwner => _claimOwner;

        public void SetValue(int value) => _value = value;
        /// <summary>기존 테스트와 임시 배치 도구용 호환 API. 새 운반자는 소유자 기반 선점을 사용한다.</summary>
        public void SetCarried(bool carried) => _legacyCarried = carried;

        public bool TryClaim(Object owner)
        {
            if (owner == null || _legacyCarried) return false;
            if (_claimOwner == null)
            {
                _claimOwner = owner;
                return true;
            }
            return _claimOwner == owner;
        }

        public bool IsClaimedBy(Object owner)
        {
            return owner != null && _claimOwner == owner;
        }

        public bool ReleaseClaim(Object owner)
        {
            if (owner == null || _claimOwner != owner) return false;
            _claimOwner = null;
            return true;
        }

        public void SetKind(EGiftBoxKind kind)
        {
            _kind = kind;
            if (TryGetComponent(out GiftAppearance appearance))
            {
                appearance.ApplyGiftKind(kind);
                return;
            }

            Color color = ColorForKind(kind);
            var block = new MaterialPropertyBlock();
            foreach (Renderer target in GetComponentsInChildren<Renderer>(true))
            {
                target.GetPropertyBlock(block);
                block.SetColor(BaseColorId, color);
                block.SetColor(ColorId, color);
                target.SetPropertyBlock(block);
                block.Clear();
            }
        }

        public static Color ColorForKind(EGiftBoxKind kind)
        {
            return kind switch
            {
                EGiftBoxKind.Red => new Color(0.90f, 0.13f, 0.13f),
                EGiftBoxKind.Yellow => new Color(0.97f, 0.85f, 0.20f),
                EGiftBoxKind.Green => new Color(0.25f, 0.72f, 0.30f),
                _ => new Color(0.18f, 0.45f, 0.88f)
            };
        }

        public static IReadOnlyList<Gift> All => _all;

        private void Awake()
        {
            _id = _nextId++;
        }

        private void OnEnable() => _all.Add(this);
        private void OnDisable()
        {
            _all.Remove(this);
            _claimOwner = null;
            _legacyCarried = false;
        }
    }
}
