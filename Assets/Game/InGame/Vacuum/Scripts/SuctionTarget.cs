using System.Collections.Generic;
using UnityEngine;

namespace PPack
{
    /// <summary>
    ///     Marks an object as catchable by suction. Objects without this component are ignored by
    ///     capture searches entirely.
    /// </summary>
    [RequireComponent(typeof(Collider))]
    public class SuctionTarget : MonoBehaviour
    {
        [Tooltip("Optional mesh-only child stretched along its local Z axis while suction is under tension. Leave empty to disable deformation without scaling the collider.")]
        [SerializeField]
        private Transform _visualRoot;
        [SerializeField] private float _maxStretch = 0.45f;
        [SerializeField] private float _stretchResponse = 12f;

        [Header("부채꼴 진입 하이라이트")]
        [Tooltip("HDR 이미션 색. 알파는 안 쓴다. R을 B보다 낮게 뒀다 — 마젠타/핑크가 아니라 자수정 " +
                 "같은 진보라·인디고로 읽히려면 R이 B에 못 미쳐야 한다. G를 거의 0으로 둔 것은 " +
                 "의도 — 물체 자체 albedo·조명 기여가 이미 밝아 이미션을 더하면 채널이 1.0을 넘어 " +
                 "흰색으로 클리핑될 수 있는데, G만 낮게 유지해야 클리핑되더라도 흰색이 아니라 " +
                 "보라로 읽힌다.")]
        [SerializeField, ColorUsage(false, true)] private Color _glowColor = new(0.35f, 0.03f, 0.85f);
        [Tooltip("맥동의 최솟값·최댓값. Bloom 문턱(0.9)을 일부러 안 넘긴다 — 넘기면 채널이 " +
                 "1.0 위로 클리핑되면서 옅은 라일락/흰색으로 번져 '어둡고 진한' 느낌이 깨진다. " +
                 "채도 있는 진보라를 그대로 유지하는 낮은 범위에서만 맥동시킨다.")]
        [SerializeField, Min(0f)] private float _glowIntensityMin = 0.5f;
        [SerializeField, Min(0f)] private float _glowIntensityMax = 1.2f;
        [Tooltip("맥동 속도(rad/s).")]
        [SerializeField, Min(0f)] private float _pulseSpeed = 6f;
        [Tooltip("하이라이트 중 표면 기본색에 곱하는 배율(0~1). 이미션만 더하면 물체 자신의 " +
                 "albedo·조명 기여가 이미 밝아 옅은 라일락으로만 보인다 — 표면을 어둡게 죽여야" +
                 "보라 이미션이 진하고 어둡게 도드라진다.")]
        [SerializeField, Range(0f, 1f)] private float _baseColorDarken = 0.12f;

        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        private Vector3 _restingVisualScale;
        private bool _hasVisualState;

        private Renderer[] _glowRenderers;
        private Color[] _restingBaseColors;
        private bool _isHighlighted;

        /// <summary>부채꼴 사거리 안에 들어온 동안 보라색 맥동 이미션을 켜고 끈다. 포획 로직은
        /// 소유하지 않는다 — 마커/프레젠테이션만 담당한다.</summary>
        public void SetHighlighted(bool highlighted)
        {
            _isHighlighted = highlighted;
            if (highlighted)
            {
                EnsureGlowRenderers();
                SetBaseColorDarkened(true);
            }
            else
            {
                SetEmission(Color.black);
                SetBaseColorDarkened(false);
            }
        }

        private void Update()
        {
            if (!_isHighlighted) return;

            float wave = (Mathf.Sin(Time.time * _pulseSpeed) + 1f) * 0.5f;
            SetEmission(_glowColor * Mathf.Lerp(_glowIntensityMin, _glowIntensityMax, wave));
        }

        /// <summary>렌더러당 한 번만 `.material`을 읽어 인스턴스를 만든다 — 프리팹 원본이나
        /// (테스트 큐브가 쓰는) URP 기본 Lit.mat 같은 공용 에셋은 전혀 건드리지 않는다.
        /// `__TrashOutline` 자식은 제외 — 코랄 실루엣 외곽선은 그대로 두고 표면만 빛나게 한다.</summary>
        private void EnsureGlowRenderers()
        {
            // 길이 0도 재시도 대상이다 — 렌더러가 아직 준비되기 전(씬 시작 직후 한 프레임)에
            // 불려 빈 배열이 캐시되면, null 만 걸러서는 다시는 못 채운다.
            if (_glowRenderers is { Length: > 0 }) return;

            var renderers = GetComponentsInChildren<Renderer>(true);
            var rendererList = new List<Renderer>(renderers.Length);
            var baseColorList = new List<Color>(renderers.Length);
            foreach (Renderer renderer in renderers)
            {
                if (renderer.transform.name == "__TrashOutline") continue;
                Material material = renderer.material;
                material.EnableKeyword("_EMISSION");
                rendererList.Add(renderer);
                baseColorList.Add(material.HasProperty(BaseColorId) ? material.GetColor(BaseColorId) : Color.white);
            }

            _glowRenderers = rendererList.ToArray();
            _restingBaseColors = baseColorList.ToArray();
        }

        private void SetEmission(Color color)
        {
            if (_glowRenderers == null) return;
            foreach (Renderer renderer in _glowRenderers)
            {
                if (renderer != null) renderer.material.SetColor(EmissionColorId, color);
            }
        }

        private void SetBaseColorDarkened(bool darkened)
        {
            if (_glowRenderers == null) return;
            for (int i = 0; i < _glowRenderers.Length; i++)
            {
                Renderer renderer = _glowRenderers[i];
                if (renderer == null || !renderer.material.HasProperty(BaseColorId)) continue;

                Color resting = _restingBaseColors[i];
                renderer.material.SetColor(BaseColorId, darkened ? resting * _baseColorDarken : resting);
            }
        }

        public void BeginSuction()
        {
            if (_visualRoot == null)
            {
                return;
            }

            _restingVisualScale = _visualRoot.localScale;
            _hasVisualState = true;
        }

        public void SetSuctionTension(float normalizedTension)
        {
            if (!_hasVisualState)
            {
                return;
            }

            float stretch = 1f + Mathf.Clamp01(normalizedTension) * _maxStretch;
            float squeeze = 1f / Mathf.Sqrt(stretch);
            Vector3 stretchedScale = Vector3.Scale(_restingVisualScale, new Vector3(squeeze, squeeze, stretch));
            float blend = 1f - Mathf.Exp(-_stretchResponse * Time.deltaTime);

            _visualRoot.localScale = Vector3.Lerp(_visualRoot.localScale, stretchedScale, blend);
        }

        public void EndSuction()
        {
            if (!_hasVisualState)
            {
                return;
            }

            _visualRoot.localScale = _restingVisualScale;
            _hasVisualState = false;
        }
    }
}
