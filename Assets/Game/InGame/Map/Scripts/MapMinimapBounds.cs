using UnityEngine;

namespace PPack
{
    /// <summary>Map이 UI에 공개하는 월드 X/Z 투영 영역.</summary>
    [DisallowMultipleComponent]
    public sealed class MapMinimapBounds : MonoBehaviour
    {
        [SerializeField] private Renderer _surfaceRenderer;
        [SerializeField] private Collider _surfaceCollider;
        [SerializeField] private Vector2 _fallbackSize = new Vector2(60f, 60f);

        public Bounds WorldBounds
        {
            get
            {
                if (_surfaceRenderer != null)
                {
                    return _surfaceRenderer.bounds;
                }
                if (_surfaceCollider != null)
                {
                    return _surfaceCollider.bounds;
                }
                return new Bounds(transform.position, new Vector3(_fallbackSize.x, 0.1f, _fallbackSize.y));
            }
        }

        private void Reset()
        {
            _surfaceRenderer = GetComponent<Renderer>();
            _surfaceCollider = GetComponent<Collider>();
        }
    }
}
