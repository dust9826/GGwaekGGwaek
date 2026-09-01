// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 -> V7 (and the
// _Cs6 shader-global prefix -> _Cs7). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.
using UnityEngine;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// The CPU half of v6's casual look: one struct carrying every style knob, and one method that
    /// pushes them as SHADER GLOBALS.
    ///
    /// Globals, not material properties, on purpose. Four different materials consume this palette -
    /// the raymarcher, the instanced lumps, the screen-space composite and the puffs - and they have
    /// to agree exactly, or cycling the render mode would read as a lighting change rather than as a
    /// technique change. Four copies of the same twenty setters is precisely how that drift happens.
    /// Nothing outside this variant uses the <c>_Cs7</c> prefix, so a global cannot collide.
    ///
    /// Every default here is the CASUAL value. The v3 look is <see cref="Casual"/> = 0, which makes
    /// every shader return the realistic colour it computed anyway; individual ideas can also be
    /// switched off one at a time (bands 1 + softness 1, rim 0, sparkle 0, round 0, exaggeration 1),
    /// which is what the parent agent needs in order to see what each one bought.
    /// </summary>
    public struct SnowCasualStyleSettingsV7
    {
        public float Casual;

        public float Bands;
        public float BandSoftness;
        public float Wrap;

        public Color LitColor;
        public Color MidColor;
        public Color ShadowColor;

        public float AlbedoInfluence;
        public float AoInfluence;
        public float Exposure;

        public float RimStrength;
        public float RimPower;
        public Color RimColor;

        public float SparkleAmount;
        public float SparkleScaleM;
        public float SparkleRadius;
        public float SparkleThreshold;
        public float SparkleSpeed;
        public Color SparkleColor;

        public float RoundM;
        public float RoundK;
        public float BandNormalWideM;
        public float LoadExaggeration;
        public float LoadLiftMaxM;
        public float VirginDepthM;
        public float LumpSquash;
    }

    /// <summary>Pushes <see cref="SnowCasualStyleSettingsV7"/> into the shader globals.</summary>
    public static class SnowCasualStyleV7
    {
        private static readonly int kCasual        = Shader.PropertyToID("_Cs7Casual");
        private static readonly int kBands         = Shader.PropertyToID("_Cs7Bands");
        private static readonly int kBandSoftness  = Shader.PropertyToID("_Cs7BandSoftness");
        private static readonly int kWrap          = Shader.PropertyToID("_Cs7Wrap");
        private static readonly int kLitColor      = Shader.PropertyToID("_Cs7LitColor");
        private static readonly int kMidColor      = Shader.PropertyToID("_Cs7MidColor");
        private static readonly int kShadowColor   = Shader.PropertyToID("_Cs7ShadowColor");
        private static readonly int kAlbedoInfl    = Shader.PropertyToID("_Cs7AlbedoInfluence");
        private static readonly int kAoInfl        = Shader.PropertyToID("_Cs7AoInfluence");
        private static readonly int kExposure      = Shader.PropertyToID("_Cs7Exposure");
        private static readonly int kRimStrength   = Shader.PropertyToID("_Cs7RimStrength");
        private static readonly int kRimPower      = Shader.PropertyToID("_Cs7RimPower");
        private static readonly int kRimColor      = Shader.PropertyToID("_Cs7RimColor");
        private static readonly int kSparkleAmount = Shader.PropertyToID("_Cs7SparkleAmount");
        private static readonly int kSparkleScale  = Shader.PropertyToID("_Cs7SparkleScaleM");
        private static readonly int kSparkleRadius = Shader.PropertyToID("_Cs7SparkleRadius");
        private static readonly int kSparkleThresh = Shader.PropertyToID("_Cs7SparkleThresh");
        private static readonly int kSparkleSpeed  = Shader.PropertyToID("_Cs7SparkleSpeed");
        private static readonly int kSparkleColor  = Shader.PropertyToID("_Cs7SparkleColor");
        private static readonly int kTime          = Shader.PropertyToID("_Cs7Time");
        private static readonly int kRoundM        = Shader.PropertyToID("_Cs7RoundM");
        private static readonly int kRoundK        = Shader.PropertyToID("_Cs7RoundK");
        private static readonly int kBandNormalW   = Shader.PropertyToID("_Cs7BandNormalWideM");
        private static readonly int kLoadExag      = Shader.PropertyToID("_Cs7LoadExaggeration");
        private static readonly int kLoadLiftMax   = Shader.PropertyToID("_Cs7LoadLiftMaxM");
        private static readonly int kVirginDepth   = Shader.PropertyToID("_Cs7VirginDepthM");
        private static readonly int kLumpSquash    = Shader.PropertyToID("_Cs7LumpSquash");

        public static void Apply(in SnowCasualStyleSettingsV7 s, float timeSeconds)
        {
            Shader.SetGlobalFloat(kCasual, Mathf.Clamp01(s.Casual));

            Shader.SetGlobalFloat(kBands, Mathf.Clamp(s.Bands, 1f, 8f));
            Shader.SetGlobalFloat(kBandSoftness, Mathf.Clamp01(s.BandSoftness));
            Shader.SetGlobalFloat(kWrap, Mathf.Clamp01(s.Wrap));

            Shader.SetGlobalColor(kLitColor, s.LitColor);
            Shader.SetGlobalColor(kMidColor, s.MidColor);
            Shader.SetGlobalColor(kShadowColor, s.ShadowColor);

            Shader.SetGlobalFloat(kAlbedoInfl, Mathf.Clamp01(s.AlbedoInfluence));
            Shader.SetGlobalFloat(kAoInfl, Mathf.Clamp01(s.AoInfluence));
            Shader.SetGlobalFloat(kExposure, Mathf.Clamp(s.Exposure, 0.1f, 3f));

            Shader.SetGlobalFloat(kRimStrength, Mathf.Clamp(s.RimStrength, 0f, 2f));
            Shader.SetGlobalFloat(kRimPower, Mathf.Clamp(s.RimPower, 0.5f, 16f));
            Shader.SetGlobalColor(kRimColor, s.RimColor);

            Shader.SetGlobalFloat(kSparkleAmount, Mathf.Clamp(s.SparkleAmount, 0f, 4f));
            Shader.SetGlobalFloat(kSparkleScale, Mathf.Clamp(s.SparkleScaleM, 0.02f, 2f));
            Shader.SetGlobalFloat(kSparkleRadius, Mathf.Clamp(s.SparkleRadius, 0.02f, 0.5f));
            Shader.SetGlobalFloat(kSparkleThresh, Mathf.Clamp01(s.SparkleThreshold));
            Shader.SetGlobalFloat(kSparkleSpeed, Mathf.Clamp(s.SparkleSpeed, 0f, 8f));
            Shader.SetGlobalColor(kSparkleColor, s.SparkleColor);

            Shader.SetGlobalFloat(kTime, timeSeconds);

            Shader.SetGlobalFloat(kRoundM, Mathf.Clamp(s.RoundM, 0f, 0.3f));
            Shader.SetGlobalFloat(kRoundK, Mathf.Clamp(s.RoundK, 0.005f, 0.5f));
            Shader.SetGlobalFloat(kBandNormalW, Mathf.Clamp(s.BandNormalWideM, 0f, 0.5f));
            Shader.SetGlobalFloat(kLoadExag, Mathf.Clamp(s.LoadExaggeration, 1f, 3f));
            Shader.SetGlobalFloat(kLoadLiftMax, Mathf.Clamp(s.LoadLiftMaxM, 0f, 0.5f));
            Shader.SetGlobalFloat(kVirginDepth, Mathf.Max(0f, s.VirginDepthM));
            Shader.SetGlobalFloat(kLumpSquash, Mathf.Clamp01(s.LumpSquash));

            // Cached so the steps probe's COMPUTE kernel can be given the same numbers explicitly.
            _lastShape = new Vector4(Mathf.Clamp01(s.Casual), Mathf.Clamp(s.RoundM, 0f, 0.3f),
                                     Mathf.Clamp(s.RoundK, 0.005f, 0.5f), Mathf.Max(0f, s.VirginDepthM));
            _lastLoad = new Vector2(Mathf.Clamp(s.LoadExaggeration, 1f, 3f),
                                    Mathf.Clamp(s.LoadLiftMaxM, 0f, 0.5f));
            _hasShape = true;
        }

        private static Vector4 _lastShape;
        private static Vector2 _lastLoad;
        private static bool _hasShape;

        /// <summary>
        /// Forwards the six values the SURFACE FUNCTION reads - the fillet and the load lift - onto a
        /// compute shader.
        ///
        /// EXPLICIT, not left to the globals. Everything else in this class is a shader global and the
        /// four rendering consumers pick it up for free, but the steps probe marches the surface in a
        /// COMPUTE kernel, and "compute shaders probably see Shader.SetGlobalFloat" is exactly the kind
        /// of probably that produced a probe which silently reported nothing for a whole session. If
        /// these six were quietly zero in the probe, the fillet would vanish from the probed surface,
        /// the fine phase of the march would be a few centimetres shorter, and the instrument would
        /// under-report the step count with no way to tell.
        ///
        /// Called every frame the probe runs, straight after Apply, so the two cannot diverge.
        /// </summary>
        public static void ApplyToCompute(ComputeShader cs)
        {
            if (cs == null || !_hasShape) return;

            cs.SetFloat(kCasual, _lastShape.x);
            cs.SetFloat(kRoundM, _lastShape.y);
            cs.SetFloat(kRoundK, _lastShape.z);
            cs.SetFloat(kVirginDepth, _lastShape.w);
            cs.SetFloat(kLoadExag, _lastLoad.x);
            cs.SetFloat(kLoadLiftMax, _lastLoad.y);
        }
    }
}
