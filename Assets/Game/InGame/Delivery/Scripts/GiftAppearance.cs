using System;
using UnityEngine;

namespace PPack
{
    [DisallowMultipleComponent]
    public sealed class GiftAppearance : MonoBehaviour
    {
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");

        [Header("Random Size (width / height / depth)")]
        [SerializeField] private Vector3 _minimumSize = new Vector3(0.52f, 0.38f, 0.48f);
        [SerializeField] private Vector3 _maximumSize = new Vector3(0.92f, 0.78f, 0.84f);

        [Header("Santa Village Palette")]
        [SerializeField] private Color[] _boxPalette =
        {
            new Color(0.72f, 0.09f, 0.08f),
            new Color(0.07f, 0.34f, 0.20f),
            new Color(0.08f, 0.22f, 0.43f),
            new Color(0.38f, 0.09f, 0.25f),
            new Color(0.04f, 0.38f, 0.40f),
            new Color(0.76f, 0.61f, 0.23f)
        };

        [SerializeField] private Color[] _ribbonPalette =
        {
            new Color(1f, 0.68f, 0.16f),
            new Color(0.96f, 0.92f, 0.78f),
            new Color(0.43f, 0.77f, 0.90f),
            new Color(0.86f, 0.12f, 0.10f),
            new Color(0.12f, 0.55f, 0.30f)
        };

        [Header("Visual Parts")]
        [SerializeField] private Transform _body;
        [SerializeField] private Transform _lid;
        [SerializeField] private Transform _ribbonAcrossWidth;
        [SerializeField] private Transform _ribbonAcrossDepth;
        [SerializeField] private Transform _bowLeft;
        [SerializeField] private Transform _bowRight;
        [SerializeField] private Transform _bowKnot;
        [SerializeField] private Renderer[] _boxRenderers = Array.Empty<Renderer>();
        [SerializeField] private Renderer[] _ribbonRenderers = Array.Empty<Renderer>();
        [SerializeField] private BoxCollider _collider;
        [SerializeField] private bool _randomizeOnAwake = true;

        private MaterialPropertyBlock _propertyBlock;

        public int Seed { get; private set; }
        public Vector3 Size { get; private set; }
        public Color BoxColor { get; private set; }
        public Color RibbonColor { get; private set; }
        public Vector3 MinimumSize => _minimumSize;
        public Vector3 MaximumSize => _maximumSize;

        private void Awake()
        {
            if (_randomizeOnAwake) Randomize();
        }

        [ContextMenu("Randomize Appearance")]
        public void Randomize()
        {
            Randomize(UnityEngine.Random.Range(1, int.MaxValue));
        }

        public void Randomize(int seed)
        {
            Seed = seed == 0 ? 1 : seed;
            var random = new System.Random(Seed);

            Vector3 size = new Vector3(
                Range(random, _minimumSize.x, _maximumSize.x),
                Range(random, _minimumSize.y, _maximumSize.y),
                Range(random, _minimumSize.z, _maximumSize.z));

            Color boxColor = Pick(_boxPalette, random, Color.red);
            Color ribbonColor = PickContrastingRibbon(boxColor, random);
            ApplyAppearance(size, boxColor, ribbonColor);
        }

        public void ApplyAppearance(Vector3 size, Color boxColor, Color ribbonColor)
        {
            Size = new Vector3(
                Mathf.Clamp(size.x, _minimumSize.x, _maximumSize.x),
                Mathf.Clamp(size.y, _minimumSize.y, _maximumSize.y),
                Mathf.Clamp(size.z, _minimumSize.z, _maximumSize.z));
            BoxColor = boxColor;
            RibbonColor = ribbonColor;

            float lidHeight = Mathf.Clamp(Size.y * 0.14f, 0.065f, 0.11f);
            float ribbonWidth = Mathf.Clamp(Mathf.Min(Size.x, Size.z) * 0.19f, 0.085f, 0.15f);
            float top = Size.y + lidHeight;
            float wrappedHeight = top + 0.012f;
            float bowWidth = Mathf.Clamp(Mathf.Min(Size.x, Size.z) * 0.34f, 0.16f, 0.29f);
            float bowHeight = Mathf.Clamp(Size.y * 0.16f, 0.065f, 0.12f);

            SetPart(_body, new Vector3(0f, Size.y * 0.5f, 0f), Size);
            SetPart(_lid, new Vector3(0f, Size.y + lidHeight * 0.5f, 0f),
                new Vector3(Size.x * 1.055f, lidHeight, Size.z * 1.055f));
            SetPart(_ribbonAcrossWidth, new Vector3(0f, wrappedHeight * 0.5f, 0f),
                new Vector3(Size.x * 1.075f, wrappedHeight, ribbonWidth));
            SetPart(_ribbonAcrossDepth, new Vector3(0f, wrappedHeight * 0.5f, 0f),
                new Vector3(ribbonWidth, wrappedHeight, Size.z * 1.075f));

            SetPart(_bowLeft, new Vector3(-bowWidth * 0.38f, top + bowHeight * 0.58f, 0f),
                new Vector3(bowWidth * 0.78f, bowHeight * 0.72f, bowWidth * 0.48f));
            SetPart(_bowRight, new Vector3(bowWidth * 0.38f, top + bowHeight * 0.58f, 0f),
                new Vector3(bowWidth * 0.78f, bowHeight * 0.72f, bowWidth * 0.48f));
            SetPart(_bowKnot, new Vector3(0f, top + bowHeight * 0.60f, 0f),
                Vector3.one * Mathf.Clamp(ribbonWidth * 0.78f, 0.07f, 0.115f));

            if (_bowLeft != null) _bowLeft.localRotation = Quaternion.Euler(0f, -22f, 0f);
            if (_bowRight != null) _bowRight.localRotation = Quaternion.Euler(0f, 22f, 0f);

            SetRendererColor(_boxRenderers, BoxColor);
            SetRendererColor(_ribbonRenderers, RibbonColor);

            if (_collider != null)
            {
                float totalHeight = top + bowHeight * 1.25f;
                _collider.center = new Vector3(0f, totalHeight * 0.5f, 0f);
                _collider.size = new Vector3(Size.x * 1.06f, totalHeight, Size.z * 1.06f);
            }
        }

        public void ApplyGiftKind(EGiftBoxKind kind)
        {
            Vector3 size = Size.sqrMagnitude > 0.001f
                ? Size
                : Vector3.Lerp(_minimumSize, _maximumSize, 0.5f);
            switch (kind)
            {
                case EGiftBoxKind.Yellow:
                    ApplyAppearance(size, Gift.ColorForKind(kind), new Color(0.98f, 0.90f, 0.67f));
                    break;
                case EGiftBoxKind.Blue:
                    ApplyAppearance(size, Gift.ColorForKind(kind), new Color(0.96f, 0.84f, 0.20f));
                    break;
                default:
                    ApplyAppearance(size, Gift.ColorForKind(kind), new Color(0.12f, 0.55f, 0.30f));
                    break;
            }
        }

        public void Configure(
            Transform body,
            Transform lid,
            Transform ribbonAcrossWidth,
            Transform ribbonAcrossDepth,
            Transform bowLeft,
            Transform bowRight,
            Transform bowKnot,
            Renderer[] boxRenderers,
            Renderer[] ribbonRenderers,
            BoxCollider boxCollider,
            bool randomizeOnAwake)
        {
            _body = body;
            _lid = lid;
            _ribbonAcrossWidth = ribbonAcrossWidth;
            _ribbonAcrossDepth = ribbonAcrossDepth;
            _bowLeft = bowLeft;
            _bowRight = bowRight;
            _bowKnot = bowKnot;
            _boxRenderers = boxRenderers ?? Array.Empty<Renderer>();
            _ribbonRenderers = ribbonRenderers ?? Array.Empty<Renderer>();
            _collider = boxCollider;
            _randomizeOnAwake = randomizeOnAwake;
        }

        private Color PickContrastingRibbon(Color boxColor, System.Random random)
        {
            if (_ribbonPalette == null || _ribbonPalette.Length == 0) return Color.white;

            int start = random.Next(_ribbonPalette.Length);
            for (int offset = 0; offset < _ribbonPalette.Length; offset++)
            {
                Color candidate = _ribbonPalette[(start + offset) % _ribbonPalette.Length];
                if (ColorDistanceSquared(boxColor, candidate) >= 0.16f) return candidate;
            }

            return _ribbonPalette[start];
        }

        private void SetRendererColor(Renderer[] renderers, Color color)
        {
            if (renderers == null) return;
            _propertyBlock ??= new MaterialPropertyBlock();

            for (int index = 0; index < renderers.Length; index++)
            {
                Renderer target = renderers[index];
                if (target == null) continue;
                target.GetPropertyBlock(_propertyBlock);
                _propertyBlock.SetColor(BaseColorId, color);
                target.SetPropertyBlock(_propertyBlock);
                _propertyBlock.Clear();
            }
        }

        private static void SetPart(Transform part, Vector3 localPosition, Vector3 localScale)
        {
            if (part == null) return;
            part.localPosition = localPosition;
            part.localScale = localScale;
        }

        private static float Range(System.Random random, float minimum, float maximum)
        {
            if (maximum <= minimum) return minimum;
            return Mathf.Lerp(minimum, maximum, (float)random.NextDouble());
        }

        private static Color Pick(Color[] palette, System.Random random, Color fallback)
        {
            return palette == null || palette.Length == 0 ? fallback : palette[random.Next(palette.Length)];
        }

        private static float ColorDistanceSquared(Color a, Color b)
        {
            float red = a.r - b.r;
            float green = a.g - b.g;
            float blue = a.b - b.b;
            return red * red + green * green + blue * blue;
        }
    }
}
