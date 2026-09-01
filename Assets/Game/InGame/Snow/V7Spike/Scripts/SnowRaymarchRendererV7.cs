// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 -> V7 (and the
// _Cs6 shader-global prefix -> _Cs7). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// v6's DEFAULT renderer: ONE proxy box, drawn inside out, with the height field ray marched in the
    /// fragment shader and the hit's true depth written to SV_Depth.
    ///
    /// Nothing per-lump exists here at all - no instance count, no lattice, no per-lump storage. The
    /// entire surface is a function evaluated along a ray, which is why the silhouette is the surface's
    /// own silhouette, why the blade sinking into the snow resolves per pixel through the depth test, and
    /// why v6's casual "fat edges" treatment is two bounded terms added to one function rather than a
    /// different mesh.
    ///
    /// WHY THIS IS THE CASUAL DEFAULT
    /// -----------------------------
    /// It is the cheapest of the three modes in the parent variant - 4.95 ms against 5.41 for the
    /// screen-space chain and 4.7 ms for 400k instanced grains that do not read as a mass at all - and
    /// v6's changes to it are close to cost neutral: the fillet reuses a coarse texture tap the march
    /// already had, and the casual detail defaults REDUCE the noise octave count from 4 to 2, which
    /// removes eight hash evaluations per surface sample. Casual snow does not want fine height detail;
    /// it wants few big soft shapes and a banded ramp.
    ///
    /// This component owns a real <see cref="MeshRenderer"/> rather than Graphics.RenderMeshPrimitives,
    /// because URP has to be able to schedule the ShadowCaster and DepthOnly passes for it, and because
    /// there is exactly one draw so batching buys nothing.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowRaymarchRendererV7 : MonoBehaviour
    {
        public const string ShaderName = "SnowSpike/PileV7/SnowRaymarch";

        // ------------------------------------------------------------------ volume
        [Header("Proxy volume")]
        [Tooltip("Height of the proxy box above the ground, in metres. Has to cover the tallest berm the " +
                 "sim can build or the crest is drawn as a flat plateau. v6's pile is CAPPED at 0.65 m, so " +
                 "the only tall thing left is a fresh berm root, and 1.5 m still has ample headroom. " +
                 "Raising it is nearly free now that the march skips empty air - measured on the parent, " +
                 "going from a 0.45 m march volume to the full 1.5 m box cost 19.8 steps per ray against " +
                 "12.9.")]
        [Range(0.3f, 4f)]
        [SerializeField] private float _maxSnowHeightM = 1.5f;

        [Tooltip("Headroom above the MEASURED field crest that the march actually starts from. This is the " +
                 "safety margin against read-back latency: the crest is read back every few frames, so " +
                 "during the initial pile fill the true crest can be ahead of the number this component " +
                 "sees. Too small and the crest is briefly drawn flat topped; too large and the march just " +
                 "starts a little higher.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _marchTopMarginM = 0.25f;

        [Tooltip("Seconds of crest growth to extrapolate on top of the margin. Measured rate of rise " +
                 "during the fill is up to ~3 m/s of crest, so a few frames of latency is real; this term " +
                 "removes it instead of paying for it with a permanently oversized margin.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _marchTopLeadSeconds = 0.12f;

        [Tooltip("How fast the march top plane is allowed to come back DOWN, in metres per second. Slow, " +
                 "because coming down is only an optimisation and a plane that chases a noisy crest " +
                 "downward would occasionally clip it.")]
        [Range(0.05f, 5f)]
        [SerializeField] private float _marchTopFallRate = 0.6f;

        [Tooltip("Snow thinner than this counts as bare ground: the march finds no surface there at all, " +
                 "discards, and the ground plane shows through. This is also what keeps the swept lane " +
                 "from z-fighting the ground plane, because every accepted hit is at least this far above " +
                 "it - and it is what the casual fillet and the load exaggeration are BOTH faded out " +
                 "against, so neither can visually refill a lane the simulation says is clear.")]
        [Range(0.001f, 0.05f)]
        [SerializeField] private float _minSnowHeightM = 0.005f;

        // ------------------------------------------------------------------ march
        [Header("March (the perf knobs)")]
        [Tooltip("Hard bound on march iterations. A ray that hits it MISSES, so this is a correctness " +
                 "knob before it is a cost knob: an exhausted ray discards and the ground plane shows " +
                 "through where snow should be.\n\n" +
                 "v4 shipped 96 and measured 21.5 mean / 51 p95 / 0.34% exhausted over an 8 m box. v6's " +
                 "box is 120 x 110 m, so a near-horizon ray traverses up to fifteen times the chord " +
                 "before it can descend to the surface, and 96 is no longer a bound with headroom - it " +
                 "is a bound that bites. 256 is that same knob re-fitted to the new geometry, NOT a new " +
                 "mitigation: the coarse-max skip and the LOD step growth are exactly v4's.\n\n" +
                 "READ THE INSTRUMENT BEFORE TOUCHING IT. The [V7] line prints mean steps, p95 steps and " +
                 "the percentage of rays that exhausted the budget. If p95 is pinned at this value and " +
                 "the exhausted percentage is not a rounding error, the answer is not a bigger number " +
                 "here - it is a coarse-max MIP PYRAMID, which turns the skip cost from proportional to " +
                 "distance into logarithmic in it. Bracket it by trying 96 and 512.")]
        [Range(8, 512)]
        [SerializeField] private int _maxSteps = 256;

        [Tooltip("Fine march step in metres, used once the ray is inside air the coarse bound says could " +
                 "be occupied. THIS IS THE TUNNELLING SCALE, and it belongs in TEXELS rather than in " +
                 "absolute metres.\n\n" +
                 "v4 shipped 0.02 m on a 1.56 cm texel, which is 1.28 texels. v6's texel is 12.5 cm - the " +
                 "real project's cell - so 0.02 here would be 0.16 texels: eight times finer than v4 " +
                 "relative to the data the field can actually represent, paid for on every step of every " +
                 "ray. 0.06 is 0.48 texels, still 2.7x finer than v4 in the unit that matters, and it " +
                 "cannot skip a texel - in the fine phase a 6 cm step covers under half a texel " +
                 "horizontally even on the steepest ray in frame.\n\n" +
                 "MODELLED over the shipped camera and a flat 30 cm field, 763 rows of a 1080p frame: " +
                 "0.02 gives mean 47.5 steps and p95 202; 0.06 gives mean 18.3 and p95 73, which is at or " +
                 "below v4's MEASURED 21.5 / 51 despite a fifteen times longer box. Put it back to 0.02 " +
                 "for the A/B that shows what the field size costs at matched ABSOLUTE precision.\n\n" +
                 "Crispness is unaffected: with 4 bisection refinements a 6 cm step locates the hit to " +
                 "under 4 mm along the ray, which is 3% of a texel.")]
        [Range(0.002f, 0.4f)]
        [SerializeField] private float _stepM = 0.06f;

        [Tooltip("Bisection refinements after the crossing is bracketed. Each halves the uncertainty, so 4 " +
                 "turns the 6 cm step into a 3.8 mm hit - 3% of a texel - much more cheaply than " +
                 "getting the same crispness from a smaller step.")]
        [Range(0, 10)]
        [SerializeField] private int _refineSteps = 4;

        [Tooltip("Distance past which the fine step grows in proportion to range, in metres. Far pixels " +
                 "subtend less than a texel, so paying full precision for them is waste - and on a 120 m " +
                 "field the far field is most of the frame, so this is the second-biggest march knob " +
                 "after Max Steps. v4 shipped 12 over an 8 m patch, where it barely engaged; 18 here " +
                 "keeps the near field at full precision (the car is 4.5 m away) and lets the horizon " +
                 "band coarsen. Lower it to buy steps back, at the cost of the far surface stepping.")]
        [Range(1f, 200f)]
        [SerializeField] private float _lodDistanceM = 18f;

        [Tooltip("Run the ShadowCaster pass: a second full march of the volume from the sun's point of " +
                 "view. OFF by default. It roughly doubles the raymarch cost, it needs the sun to have " +
                 "shadows enabled to do anything, and the snow does not sample the shadow map (it has its " +
                 "own, better, self-shadow), so turning it on makes the snow shadow the GROUND and the " +
                 "BLADE but not itself. Correct as far as it goes, but asymmetric - hence opt-in.")]
        [SerializeField] private bool _shadowMarchEnabled = false;

        [Tooltip("Metric depth bias for the shadow pass: the caster is recorded this much nearer the light " +
                 "so a receiver on the same surface does not shadow itself.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _shadowDepthBiasM = 0.02f;

        // ------------------------------------------------------------------ detail
        [Header("Surface detail (added DURING the march, so it is in the silhouette)")]
        [Tooltip("Peak amplitude of the procedural detail in metres, added to the sampled height INSIDE " +
                 "the march so it shows up in the silhouette and in the depth - that is the difference " +
                 "between this and bump mapping.\n\n" +
                 "v6 DEFAULT 0.030, DOWN from v3's 0.045. Casual snow is few large soft shapes; fine " +
                 "height detail is what makes a surface read as photographed material. Put it back to " +
                 "0.045 with 4 octaves at 6 cycles/m for the v3 surface.")]
        [Range(0f, 0.1f)]
        [SerializeField] private float _detailAmpM = 0.030f;

        [Tooltip("Cycles per metre of the first octave. v6 DEFAULT 2.6, DOWN from v3's 6 - a ~38 cm " +
                 "wavelength instead of ~17 cm, i.e. lump scale rather than clump scale. This is half of " +
                 "what makes the surface read chunky; the other half is the fillet below.")]
        [Range(0.5f, 40f)]
        [SerializeField] private float _detailFreq = 2.6f;

        [Tooltip("Octaves of value noise, 1..4, normalised so the amplitude above stays a true amplitude " +
                 "whatever this is. The main detail cost knob: the noise is evaluated at every march step, " +
                 "so each octave is roughly 4 hashes x ~20 steps x every covered pixel.\n\n" +
                 "v6 DEFAULT 2, DOWN from v3's 4. Dropping two octaves removes eight hashes per surface " +
                 "sample, which is most of why v6's extra casual terms come out close to cost neutral.")]
        [Range(1, 4)]
        [SerializeField] private int _detailOctaves = 2;

        [Tooltip("Depth over which the detail fades in as the snow thickens, in metres. Clamped at runtime " +
                 "to be at least the detail amplitude, which guarantees the noise can never pull the " +
                 "surface below the bare-ground threshold.")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float _detailThinM = 0.06f;

        [Tooltip("Ray distance at which the detail starts fading out, in metres.")]
        [Range(1f, 60f)]
        [SerializeField] private float _detailFadeStartM = 8f;

        [Tooltip("Range over which the detail finishes fading out, in metres.")]
        [Range(1f, 60f)]
        [SerializeField] private float _detailFadeRangeM = 8f;

        [Tooltip("Extra detail amplitude, as a multiple, on snow the blade has piled. The virgin slab " +
                 "stays smooth and the pile and berms get lumpy, which is the difference between worked " +
                 "and unworked material - and it is one of the few realistic terms that also helps the " +
                 "casual read, because a toy pile SHOULD be lumpier than flat ground.")]
        [Range(0f, 4f)]
        [SerializeField] private float _clumpBoost = 1.2f;

        [Tooltip("Snow depth at which the clump boost starts ramping in, in metres. Just above the virgin " +
                 "0.30 m slab, so only piled material is affected.")]
        [Range(0.05f, 1.5f)]
        [SerializeField] private float _clumpStartM = 0.34f;

        [Tooltip("Depth over which the clump boost ramps to full, in metres.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _clumpRampM = 0.20f;

        [Tooltip("Amplitude in metres of a SECOND, sharper detail term applied ONLY to the shading normal. " +
                 "In v3 this is the crispness that stops a correct silhouette from reading as styrofoam.\n\n" +
                 "v6 DEFAULT 0 - that crispness is precisely what reads as REALISTIC packed snow, and it " +
                 "is also what a specular lobe turns into wet plastic. It is a knob rather than a deletion: " +
                 "put v3's 0.008 back to see exactly what casual gave up.")]
        [Range(0f, 0.04f)]
        [SerializeField] private float _normalDetailAmpM = 0f;

        [Tooltip("Cycles per metre of the normal-only detail. 22 is a ~4.5 cm wavelength, which the " +
                 "0.012 m normal epsilon still resolves; much finer than about 40 and the finite " +
                 "difference aliases into a shimmer instead of a texture.")]
        [Range(2f, 40f)]
        [SerializeField] private float _normalDetailFreq = 22f;

        [Range(1, 3)]
        [SerializeField] private int _normalDetailOctaves = 2;

        // ------------------------------------------------------------------ the lump lattice
        [Header("Lump lattice (v4's ScreenSpaceLumps look, as a height term)")]
        [Tooltip("Sphere radius in metres, and simultaneously the EXACT amount the march's empty-space " +
                 "bound has to be widened by - the lift is sqrt(r^2 - d^2), so it can never exceed r. " +
                 "0 SWITCHES THE WHOLE TERM OFF, and off is a real zero-cost path: the shader's compare " +
                 "is against a uniform, so the lattice code is not entered at all.\n\n" +
                 "0.30 m. v4's screen-space lumps were r = 16 cm inflated to an effective ~27 cm on an " +
                 "8 x 8 m patch with a 1.56 cm field; this stage is 120 x 110 m on a 12.5 cm cell, so the " +
                 "absolute size has to be larger to subtend the same angle from the same camera. 0.30 m is " +
                 "2.4 texels, which the field can actually resolve, and it is a little over v4's effective " +
                 "size rather than a lot, because unlike v4's these lobes sit ON the collidable surface " +
                 "instead of 27 cm above it.")]
        [Range(0f, 1.2f)]
        [SerializeField] private float _lumpRadiusM = 0.30f;

        [Tooltip("Lattice spacing in metres. SMALLER THAN THE RADIUS ON PURPOSE: neighbouring caps then " +
                 "overlap and the max-combine reads as merged lobes rather than as isolated bumps sitting " +
                 "on a plane. 0.35 against a 0.30 m radius means every point is inside at least one cap " +
                 "over most of the field, with shallow seams where three caps meet - which is exactly the " +
                 "faceted-into-lobed change this is for.\n\n" +
                 "It also bounds the radius: a 3x3 neighbourhood is only sufficient while " +
                 "radius <= spacing * (1.5 - 0.5 * jitter), and the radius is clamped to that at push " +
                 "time rather than the neighbourhood being widened.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float _lumpSpacingM = 0.35f;

        [Tooltip("How far a lump centre may wander from its own cell centre, as a fraction of a cell. 0 is " +
                 "a perfectly regular lattice, which reads as a waffle: the seams line up into two sets of " +
                 "straight lines. 0.35 breaks that up without letting a centre leave its cell.")]
        [Range(0f, 1f)]
        [SerializeField] private float _lumpJitter = 0.35f;

        [Tooltip("Per-lump radius variation. Each lump's radius is r * (1 - this * hash), so it only ever " +
                 "REDUCES the radius and the r bound the march relies on is untouched. Some size variety " +
                 "is what stops the lobes reading as a moulded pattern; too much and the small ones stop " +
                 "reaching their neighbours and the surface goes pimply.")]
        [Range(0f, 0.8f)]
        [SerializeField] private float _lumpRadiusVary = 0.25f;

        [Tooltip("Snow depth ABOVE the bare-ground threshold over which the lobes fade in, in metres. " +
                 "THIS IS A CORRECTNESS KNOB, not polish: the gate is exactly 0 at the threshold, so a " +
                 "lobe can never float over ground the simulation has carved bare - and in this variant " +
                 "the field is routinely carved to bare ground. 0.10 m means the lane edge grows its " +
                 "lobes over the first 10 cm of depth instead of stepping into them.")]
        [Range(0.01f, 0.6f)]
        [SerializeField] private float _lumpGateDepthM = 0.10f;

        [Tooltip("Local RELIEF, in metres, that fully applies the lobes. Relief is the fillet dilation " +
                 "minus the field at the same point - zero on flat virgin snow, large on a berm flank or " +
                 "a dumped mound - and it comes out of a texture tap the march already had, so it is " +
                 "free. 0.10 m over the 25 cm fillet radius is a ~22 degree slope, well under the 55 " +
                 "degree repose angle, so berms and mounds saturate it and only genuinely flat snow does " +
                 "not.")]
        [Range(0.01f, 1f)]
        [SerializeField] private float _lumpReliefM = 0.10f;

        [Tooltip("How much of the lobe amplitude the relief term controls. 0 puts lobes everywhere the " +
                 "snow is deep enough, including the untouched virgin slab; 1 puts them ONLY where there " +
                 "is relief. 0.75 keeps flat virgin snow essentially smooth - a quarter amplitude - while " +
                 "berms and mounds get the full lobes, which is the worked/unworked distinction the " +
                 "existing clump boost already makes for the detail noise.")]
        [Range(0f, 1f)]
        [SerializeField] private float _lumpSlopeStrength = 0.75f;

        [Tooltip("Ray distance at which the lobes start fading out, in metres. Past the end of the fade " +
                 "the surface function does not enter the lattice branch at all, so the far field - which " +
                 "on a 120 m stage is most of the frame - costs nothing extra. Further out than the " +
                 "detail fade's 8 m because a 30 cm lobe is still a silhouette feature at 20 m where a " +
                 "6 cm noise is not.")]
        [Range(1f, 120f)]
        [SerializeField] private float _lumpFadeStartM = 14f;

        [Tooltip("Range over which the lobes finish fading out, in metres.")]
        [Range(1f, 120f)]
        [SerializeField] private float _lumpFadeRangeM = 12f;

        // ------------------------------------------------------------------ shading
        [Header("Normals and occlusion")]
        [Tooltip("Half-width of the normal's finite differences, in metres. Around one texel: much smaller " +
                 "and the normal is all detail noise, much larger and the structure is averaged away.")]
        [Range(0.002f, 0.1f)]
        [SerializeField] private float _normalEpsM = 0.012f;

        [Tooltip("Bound on how far a normal tap may differ from the centre tap, in metres. A tap can land " +
                 "on bare ground, where the surface function reports 'no surface' a kilometre down; " +
                 "unclamped, one such tap flips the normal and the lane edge gets a black fringe.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float _normalClampM = 0.5f;

        [Tooltip("Strength of the curvature ambient occlusion. In v6 this is deliberately LOWER than v3's " +
                 "0.6: the casual palette does its darkening with a coloured shadow band, and stacking a " +
                 "grey curvature multiply on top of that is what makes stylised shading look dirty rather " +
                 "than graphic. The style function also only takes a fraction of it, via " +
                 "Casual Ao Influence on the bootstrap.")]
        [Range(0f, 1f)]
        [SerializeField] private float _aoStrength = 0.40f;

        [Tooltip("Fine curvature, in metres, that fully saturates the crease darkening.")]
        [Range(0.001f, 0.1f)]
        [SerializeField] private float _aoScaleM = 0.008f;

        [Tooltip("Half-width of the coarse curvature taps, in metres. This is the term that darkens the " +
                 "trough between two berms and the root of the front pile.")]
        [Range(0.02f, 0.6f)]
        [SerializeField] private float _aoWideEpsM = 0.12f;

        [Tooltip("Coarse curvature, in metres, that fully saturates the coarse darkening.")]
        [Range(0.005f, 0.5f)]
        [SerializeField] private float _aoWideScaleM = 0.06f;

        [Header("Self shadow (a short march toward the sun)")]
        [Tooltip("Steps in the shadow march. 0 disables it. Per shaded pixel, not per march step, so it is " +
                 "far cheaper than it sounds - and it is the single biggest thing that makes the front pile " +
                 "read as a mass instead of a painted blob. It matters MORE in v6 than in v3, because the " +
                 "casual ramp quantises (wrap * shadow): with no shadow term the bands would follow only " +
                 "the normal and the pile would read as a flat cutout.")]
        [Range(0, 32)]
        [SerializeField] private int _softShadowSteps = 8;

        [Tooltip("Step length of the shadow march in metres. steps * this is how far the shadow can reach, " +
                 "so 8 x 0.06 = 0.48 m - contact and crease scale, not whole-pile scale.")]
        [Range(0.01f, 0.5f)]
        [SerializeField] private float _softShadowStepM = 0.06f;

        [Tooltip("Where the shadow march starts, in metres from the surface. Keeps the first sample off the " +
                 "surface it started on.")]
        [Range(0.002f, 0.2f)]
        [SerializeField] private float _softShadowStartM = 0.02f;

        [Tooltip("Penumbra hardness. The shadow term is clearance over distance, so this scales how quickly " +
                 "a near miss becomes full light. Physically the sun would be ~200; 10 gives the soft, wide " +
                 "contact darkening that reads as snow, and a soft input is what the band edges need in " +
                 "order to land on form rather than on noise.")]
        [Range(1f, 64f)]
        [SerializeField] private float _softShadowHardness = 10f;

        [Range(0f, 1f)]
        [SerializeField] private float _softShadowStrength = 0.85f;

        [Tooltip("How far along the normal the shadow march starts, in metres. Self-intersection bias.")]
        [Range(0.001f, 0.1f)]
        [SerializeField] private float _shadowNormalBiasM = 0.01f;

        [Header("Realistic palette and response - what _Cs7Casual = 0 returns to")]
        [Tooltip("These are v3's values, unchanged. The CASUAL palette is separate, lives on the bootstrap, " +
                 "and is pushed as shader globals because four materials share it.")]
        [SerializeField] private Color _baseColor = new Color(0.94f, 0.96f, 1.00f, 1f);
        [SerializeField] private Color _deepColor = new Color(0.55f, 0.63f, 0.80f, 1f);
        [SerializeField] private Color _ambientColor = new Color(0.34f, 0.40f, 0.54f, 1f);

        [Tooltip("Diffuse wrap for the REALISTIC path. Snow forward-scatters hard enough that a plain N.L " +
                 "terminator reads as plaster. The casual path has its own wrap (Casual Wrap on the " +
                 "bootstrap), applied before quantisation.")]
        [Range(0f, 1f)]
        [SerializeField] private float _wrap = 0.45f;

        [Tooltip("Floor on the main light term, so a fully shadowed facet is dim rather than black. The " +
                 "casual path does not need this - its dark end is a saturated blue-violet, not black.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _fill = 0.10f;

        [Tooltip("Specular sheen strength. v6 DEFAULT 0, DOWN from v3's 0.25: THIS IS THE WET LOOK. A Blinn " +
                 "lobe on a normal carrying high-frequency noise is a field of glints, which is exactly how " +
                 "an earlier build of this spike came to read as wet plastic. What replaces it is a broad " +
                 "dim rim plus large sparse slow sparkles, both in the casual style block. Kept as a knob " +
                 "at 0 rather than deleted, so v3 is reachable.")]
        [Range(0f, 2f)]
        [SerializeField] private float _sheen = 0f;

        [Range(4f, 256f)]
        [SerializeField] private float _sheenPower = 42f;

        [Tooltip("How far a vertical face tints toward the deep colour. In the realistic path this is what " +
                 "makes the cut wall of the lane read as dense body. In the casual path the same albedo is " +
                 "passed to the style function but only Casual Albedo Influence of it survives, because a " +
                 "toy palette is mostly flat colour with the light doing the work.")]
        [Range(0f, 1f)]
        [SerializeField] private float _wallTint = 0.55f;

        [Tooltip("How far off vertical a normal has to be before the wall tint is fully applied, as 1 - n.y.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _wallTintRange = 0.6f;

        // ------------------------------------------------------------------ shader ids
        private static readonly int kHeightTex        = Shader.PropertyToID("_HeightTex");
        private static readonly int kCoarseMaxTex     = Shader.PropertyToID("_CoarseMaxTex");
        private static readonly int kHeightDilateTex  = Shader.PropertyToID("_HeightDilateTex");
        private static readonly int kLumpBakeTex      = Shader.PropertyToID("_LumpBakeTex");
        private static readonly int kBoxMin           = Shader.PropertyToID("_BoxMin");
        private static readonly int kBoxMax           = Shader.PropertyToID("_BoxMax");
        private static readonly int kPatchMin         = Shader.PropertyToID("_PatchMin");
        private static readonly int kInvPatchSize     = Shader.PropertyToID("_InvPatchSize");
        private static readonly int kGroundY          = Shader.PropertyToID("_GroundY");
        private static readonly int kMarchTopY        = Shader.PropertyToID("_MarchTopY");
        private static readonly int kMarchFloorY      = Shader.PropertyToID("_MarchFloorY");
        private static readonly int kMinSnowHeight    = Shader.PropertyToID("_MinSnowHeight");
        private static readonly int kCoarseSafeRadius = Shader.PropertyToID("_CoarseSafeRadiusM");
        private static readonly int kCoarseMaxBias    = Shader.PropertyToID("_CoarseMaxBiasM");
        private static readonly int kMaxSteps         = Shader.PropertyToID("_MaxSteps");
        private static readonly int kStepM            = Shader.PropertyToID("_StepM");
        private static readonly int kRefineSteps      = Shader.PropertyToID("_RefineSteps");
        private static readonly int kLodDistanceInv   = Shader.PropertyToID("_LodDistanceInv");
        private static readonly int kDetailAmpM       = Shader.PropertyToID("_DetailAmpM");
        private static readonly int kDetailFreq       = Shader.PropertyToID("_DetailFreq");
        private static readonly int kDetailOctaves    = Shader.PropertyToID("_DetailOctaves");
        private static readonly int kDetailThinInv    = Shader.PropertyToID("_DetailThinInv");
        private static readonly int kDetailFadeStart  = Shader.PropertyToID("_DetailFadeStartM");
        private static readonly int kDetailFadeInv    = Shader.PropertyToID("_DetailFadeInv");
        private static readonly int kClumpStartM      = Shader.PropertyToID("_ClumpStartM");
        private static readonly int kClumpRampInv     = Shader.PropertyToID("_ClumpRampInv");
        private static readonly int kClumpBoost       = Shader.PropertyToID("_ClumpBoost");
        private static readonly int kLumpRadiusM      = Shader.PropertyToID("_LumpRadiusM");
        // The seven lattice-SHAPE knobs are NOT pushed here any more. They are uniforms of the BAKE, on
        // the field's compute shader, and are forwarded by PushLumpBakeParams below. What the raymarcher
        // still needs is the radius - which is the decode scale for the baked 0..1 texel and the scalar
        // off switch - and the distance fade, which is per ray and so cannot be baked.
        private static readonly int kLumpFadeStartM   = Shader.PropertyToID("_LumpFadeStartM");
        private static readonly int kLumpFadeInv      = Shader.PropertyToID("_LumpFadeInv");
        private static readonly int kNrmDetailAmp     = Shader.PropertyToID("_NormalDetailAmpM");
        private static readonly int kNrmDetailFreq    = Shader.PropertyToID("_NormalDetailFreq");
        private static readonly int kNrmDetailOctaves = Shader.PropertyToID("_NormalDetailOctaves");
        private static readonly int kNormalEpsM       = Shader.PropertyToID("_NormalEpsM");
        private static readonly int kNormalClampM     = Shader.PropertyToID("_NormalClampM");
        private static readonly int kAoStrength       = Shader.PropertyToID("_AoStrength");
        private static readonly int kAoScaleInv       = Shader.PropertyToID("_AoScaleInv");
        private static readonly int kAoWideEpsM       = Shader.PropertyToID("_AoWideEpsM");
        private static readonly int kAoWideScaleInv   = Shader.PropertyToID("_AoWideScaleInv");
        private static readonly int kSoftShadowSteps  = Shader.PropertyToID("_SoftShadowSteps");
        private static readonly int kSoftShadowStepM  = Shader.PropertyToID("_SoftShadowStepM");
        private static readonly int kSoftShadowStartM = Shader.PropertyToID("_SoftShadowStartM");
        private static readonly int kSoftShadowHard   = Shader.PropertyToID("_SoftShadowHardness");
        private static readonly int kSoftShadowStr    = Shader.PropertyToID("_SoftShadowStrength");
        private static readonly int kShadowNormalBias = Shader.PropertyToID("_ShadowNormalBiasM");
        private static readonly int kShadowDepthBias  = Shader.PropertyToID("_ShadowDepthBiasM");
        private static readonly int kWrap             = Shader.PropertyToID("_Wrap");
        private static readonly int kFill             = Shader.PropertyToID("_Fill");
        private static readonly int kSheen            = Shader.PropertyToID("_Sheen");
        private static readonly int kSheenPower       = Shader.PropertyToID("_SheenPower");
        private static readonly int kWallTint         = Shader.PropertyToID("_WallTint");
        private static readonly int kWallTintInv      = Shader.PropertyToID("_WallTintInv");
        private static readonly int kBaseColor        = Shader.PropertyToID("_BaseColor");
        private static readonly int kDeepColor        = Shader.PropertyToID("_DeepColor");
        private static readonly int kAmbientColor     = Shader.PropertyToID("_AmbientColor");

        // ------------------------------------------------------------------ runtime
        private SnowPileFieldV7 _field;
        private Transform _box;
        private MeshRenderer _renderer;
        private Mesh _mesh;
        private Material _material;
        private bool _ready;

        private float _marchTopY;
        private float _lastFieldMax = -1f;
        private float _fieldMaxRate;
        private float _sinceFieldMaxChange;

        // The bound the casual shape terms are allowed to lift the surface by, mirrored from the style
        // settings so the march bound can be widened by exactly that much. See ApplyCasualShapeBounds.
        private float _casualLiftBoundM;

        // Derived values, cached at the moment they are pushed to the material so the steps probe's
        // compute kernel can be handed the SAME numbers rather than recomputing them. Recomputing is
        // how a probe ends up measuring a surface the renderer is not drawing.
        private Vector4 _boxMin, _boxMax, _invPatchSize;
        private float _coarseMaxBias, _detailThin, _clumpStartEff;

        // The lump lattice's EFFECTIVE radius after the 3x3-sufficiency clamp, and the HARD BOUND on the
        // lift. They are the same number - the lift is sqrt(r^2 - d^2) and every gate is <= 1, so r IS the
        // bound - and they are two fields only so the telemetry can report the bound without re-deriving
        // it. Since the bake, that bound is spent on the MARCH TOP PLANE and no longer on the
        // empty-space skip's bias: see DeriveLumpValues.
        private float _lumpRadiusEffM, _lumpLiftBoundM;
        private bool _uniformsValid;

        public bool Ready => _ready;

        /// <summary>The proxy box's renderer, so the steps probe can draw its own pass of it.</summary>
        public Renderer VolumeRenderer => _renderer;

        /// <summary>The raymarch material, so the steps probe can address its probe pass.</summary>
        public Material VolumeMaterial => _material;

        public float MarchTopY => _marchTopY;
        public int MaxSteps => _maxSteps;
        public float StepM => _stepM;
        public float LodDistanceM => _lodDistanceM;
        public int RefineSteps => _refineSteps;
        public bool ShadowMarchEnabled => _shadowMarchEnabled;
        public float MaxSnowHeightM => _maxSnowHeightM;
        public float DetailAmpM => _detailAmpM;
        public float DetailFreq => _detailFreq;
        public int DetailOctaves => _detailOctaves;
        public float NormalDetailAmpM => _normalDetailAmpM;
        public int NormalDetailOctaves => _normalDetailOctaves;
        public float AoStrength => _aoStrength;
        public float Sheen => _sheen;
        public float LumpRadiusM => _lumpRadiusEffM;
        public float LumpSpacingM => _lumpSpacingM;
        public bool LumpEnabled => _lumpRadiusEffM > 1e-5f;
        /// <summary>
        /// The hard bound on the lump lift, in metres, which is what the march TOP PLANE has to clear.
        /// It is no longer added to _CoarseMaxBiasM - the coarse texture carries the baked lift's real
        /// maximum per cell instead - so this is a shape figure now, not a skip cost.
        /// </summary>
        public float LumpBoundHeadroomM => _lumpLiftBoundM;

        /// <summary>
        /// Lets the bootstrap forward scene-authored values in before the first frame, because this
        /// component is created at runtime and so has no inspector while the editor is stopped, and the
        /// Unity CLI refuses component edits during play. Any argument below its legal minimum leaves this
        /// component's own value alone; -1 works for all of them.
        /// </summary>
        public void ApplyOverrides(int maxSteps, float stepM, int refineSteps, float lodDistanceM,
                                   int shadowMarchEnabled, float maxSnowHeightM,
                                   float detailAmpM, float detailFreq, int detailOctaves,
                                   float aoStrength, int softShadowSteps, float clumpBoost,
                                   float normalDetailAmpM, float normalDetailFreq,
                                   int normalDetailOctaves, float sheen)
        {
            if (maxSteps >= 8) _maxSteps = Mathf.Clamp(maxSteps, 8, 512);
            if (stepM > 0f) _stepM = Mathf.Clamp(stepM, 0.002f, 0.4f);
            if (refineSteps >= 0) _refineSteps = Mathf.Clamp(refineSteps, 0, 10);
            if (lodDistanceM > 0f) _lodDistanceM = Mathf.Clamp(lodDistanceM, 1f, 200f);
            if (shadowMarchEnabled >= 0) _shadowMarchEnabled = shadowMarchEnabled != 0;
            if (maxSnowHeightM > 0f) _maxSnowHeightM = Mathf.Clamp(maxSnowHeightM, 0.3f, 4f);

            // 0 is meaningful for these five - it is how the detail, the AO, the self shadow, the
            // normal-only detail and the sheen are switched off for a comparison, and v6 SHIPS two of them
            // at 0 - so their sentinel has to be strictly negative.
            if (detailAmpM >= 0f) _detailAmpM = Mathf.Clamp(detailAmpM, 0f, 0.1f);
            if (detailFreq > 0f) _detailFreq = Mathf.Clamp(detailFreq, 0.5f, 40f);
            if (detailOctaves > 0) _detailOctaves = Mathf.Clamp(detailOctaves, 1, 4);
            if (aoStrength >= 0f) _aoStrength = Mathf.Clamp01(aoStrength);
            if (softShadowSteps >= 0) _softShadowSteps = Mathf.Clamp(softShadowSteps, 0, 32);
            if (clumpBoost >= 0f) _clumpBoost = Mathf.Clamp(clumpBoost, 0f, 4f);

            if (normalDetailAmpM >= 0f) _normalDetailAmpM = Mathf.Clamp(normalDetailAmpM, 0f, 0.04f);
            if (normalDetailFreq > 0f) _normalDetailFreq = Mathf.Clamp(normalDetailFreq, 2f, 40f);
            if (normalDetailOctaves > 0) _normalDetailOctaves = Mathf.Clamp(normalDetailOctaves, 1, 3);

            if (sheen >= 0f) _sheen = Mathf.Clamp(sheen, 0f, 2f);
        }

        /// <summary>
        /// Tells the raymarcher how far the CASUAL shape terms are allowed to lift the surface above the
        /// height field, in metres.
        ///
        /// This is not cosmetic bookkeeping. The coarse max texture is a hard upper bound on the FIELD, and
        /// the march's empty-space skip is only provably safe while the surface stays under
        /// (coarse + _CoarseMaxBiasM). The fillet and the load exaggeration both raise the surface, both
        /// publish a hard bound on how much, and this is where those bounds are added to the bias. Get it
        /// wrong low and the march steps through a rounded shoulder - which shows up as holes exactly along
        /// the edges the fillet was added to soften.
        /// </summary>
        public void ApplyCasualShapeBounds(float roundM, float loadLiftMaxM, float loadExaggeration,
                                          float casualAmount)
        {
            float amount = Mathf.Clamp01(casualAmount);

            // The load lift is only counted when the exaggeration is actually ENABLED. It is neutral at
            // 1.0, where its lift is identically zero, so including its maximum unconditionally inflated
            // the march's skip bound by 12 cm for nothing - the ray then descended 12 cm further than it
            // needed to before it started fine stepping, on every ray, forever. Conservative rather than
            // wrong, but it is march steps paid for a term that is switched off.
            float loadTerm = (loadExaggeration > 1.0001f) ? Mathf.Max(0f, loadLiftMaxM) : 0f;

            _casualLiftBoundM = amount * (Mathf.Max(0f, roundM) + loadTerm);
        }

        /// <summary>
        /// Forwards the four shading knobs an open question actually needs. Negative means "leave alone";
        /// 0 is meaningful for the shadow strength, the wrap and the fill, so all three take a strictly
        /// negative sentinel.
        ///
        /// Why these four out of the twenty-eight this component does not forward:
        ///  * minSnowHeightM is the SECOND dial on the perforation problem. The render-only height dilation
        ///    is the first; lowering the threshold at which snow stops existing is the other, and having
        ///    only one of the two reachable would have left the parent agent stuck if the dilation is not
        ///    enough on its own.
        ///  * softShadowStrength is what the casual band ramp quantises, jointly with the wrap - the bands
        ///    land on (wrap * shadow) - so it is the direct control on whether the bands follow the pile's
        ///    form or only its normal.
        ///  * wrap and fill are the realistic base the casual look is blended against, so they are what a
        ///    _Cs7Casual = 0 A/B is actually comparing to.
        /// The rest are once-measured epsilons, fade distances and the realistic palette.
        /// </summary>
        public void ApplyShadingOverrides(float minSnowHeightM, float softShadowStrength,
                                         float wrap, float fill)
        {
            if (minSnowHeightM > 0f) _minSnowHeightM = Mathf.Clamp(minSnowHeightM, 0.001f, 0.05f);
            if (softShadowStrength >= 0f) _softShadowStrength = Mathf.Clamp01(softShadowStrength);
            if (wrap >= 0f) _wrap = Mathf.Clamp01(wrap);
            if (fill >= 0f) _fill = Mathf.Clamp(fill, 0f, 0.5f);
        }

        /// <summary>
        /// Forwards every lump-lattice knob. Negative means "leave alone" for all nine, because 0 is
        /// MEANINGFUL for six of them: radius 0 is how the whole term is switched off for the A/B against
        /// the measured 4.2 ms / 7.4 mean-step baseline, jitter 0 is the regular lattice, radiusVary 0 is
        /// uniform lumps and slopeStrength 0 is "lobes everywhere the snow is deep".
        /// </summary>
        public void ApplyLumpOverrides(float lumpRadiusM, float lumpSpacingM, float lumpJitter,
                                       float lumpRadiusVary, float lumpGateDepthM, float lumpReliefM,
                                       float lumpSlopeStrength, float lumpFadeStartM,
                                       float lumpFadeRangeM)
        {
            if (lumpRadiusM >= 0f) _lumpRadiusM = Mathf.Clamp(lumpRadiusM, 0f, 1.2f);
            if (lumpSpacingM > 0f) _lumpSpacingM = Mathf.Clamp(lumpSpacingM, 0.05f, 2f);
            if (lumpJitter >= 0f) _lumpJitter = Mathf.Clamp01(lumpJitter);
            if (lumpRadiusVary >= 0f) _lumpRadiusVary = Mathf.Clamp(lumpRadiusVary, 0f, 0.8f);
            if (lumpGateDepthM > 0f) _lumpGateDepthM = Mathf.Clamp(lumpGateDepthM, 0.01f, 0.6f);
            if (lumpReliefM > 0f) _lumpReliefM = Mathf.Clamp(lumpReliefM, 0.01f, 1f);
            if (lumpSlopeStrength >= 0f) _lumpSlopeStrength = Mathf.Clamp01(lumpSlopeStrength);
            if (lumpFadeStartM > 0f) _lumpFadeStartM = Mathf.Clamp(lumpFadeStartM, 1f, 120f);
            if (lumpFadeRangeM > 0f) _lumpFadeRangeM = Mathf.Clamp(lumpFadeRangeM, 1f, 120f);
        }

        /// <summary>
        /// Hands the field the lattice parameters its BAKE kernel needs, and must be called BEFORE
        /// SnowPileFieldV7.Step, because the bake runs inside the step.
        ///
        /// THIS COMPONENT IS THE SINGLE SOURCE and the direction of the push is why. The bake ENCODES
        /// the lift as lift / radius and the marcher DECODES it by multiplying the same radius back, and
        /// the radius in question is the EFFECTIVE one after the 3x3-sufficiency clamp in
        /// DeriveLumpValues - not the authored one. Two components deriving that clamp separately would
        /// eventually disagree, and the symptom would be lobes at the wrong height with nothing in the
        /// toolchain to report it. So it is derived here, once, and mirrored outward.
        ///
        /// DeriveLumpValues is called again from UpdateUniforms in the same frame. It is a pure function
        /// of the serialized knobs, so calling it twice is free and cannot disagree with itself.
        /// </summary>
        public void PushLumpBakeParams(SnowPileFieldV7 field)
        {
            if (field == null) return;

            DeriveLumpValues();

            field.ApplyLumpBakeParams(_lumpRadiusEffM, _lumpSpacingM, _lumpJitter, _lumpRadiusVary,
                                      _lumpGateDepthM, _lumpReliefM, _lumpSlopeStrength,
                                      _minSnowHeightM);
        }

        public void Initialize(SnowPileFieldV7 field)
        {
            _field = field;

            Shader shader = Shader.Find(ShaderName);
            if (shader == null)
            {
                Debug.LogError("[SnowSpike.PileV7] Shader not found: " + ShaderName);
                enabled = false;
                return;
            }

            _material = new Material(shader) { name = "SnowFakeV7Raymarch" };

            // RECTANGULAR, matching the real stage. The shader's slab test is in world space and reads
            // _BoxMin / _BoxMax rather than the mesh, but the mesh still has to COVER the volume or the
            // rasteriser never produces the fragments the march runs in.
            _mesh = SnowFakeV7Meshes.CreateProxyBox(
                new Vector3(field.PatchSizeX, _maxSnowHeightM, field.PatchSizeZ));

            var go = new GameObject("SnowFakeV7_SnowVolume");
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(
                field.PatchMin.x + field.PatchSizeX * 0.5f,
                field.GroundY + _maxSnowHeightM * 0.5f,
                field.PatchMin.y + field.PatchSizeZ * 0.5f);

            // The shader's slab test is in WORLD space, so the box must not be rotated or scaled: object
            // space and world space have to differ by a translation only. Set the WORLD rotation, not the
            // local one, so a rotated owner cannot tilt the volume.
            go.transform.rotation = Quaternion.identity;
            go.transform.localScale = Vector3.one;

            go.AddComponent<MeshFilter>().sharedMesh = _mesh;

            _renderer = go.AddComponent<MeshRenderer>();
            _renderer.sharedMaterial = _material;
            _renderer.receiveShadows = false;
            _renderer.lightProbeUsage = LightProbeUsage.Off;
            _renderer.reflectionProbeUsage = ReflectionProbeUsage.Off;
            _renderer.motionVectorGenerationMode = MotionVectorGenerationMode.ForceNoMotion;
            _renderer.shadowCastingMode = _shadowMarchEnabled ? ShadowCastingMode.On
                                                              : ShadowCastingMode.Off;

            _box = go.transform;
            _marchTopY = field.GroundY + _maxSnowHeightM;
            _ready = true;
        }

        private void OnDestroy()
        {
            if (_material != null) Destroy(_material);
            if (_mesh != null) Destroy(_mesh);
        }

        /// <summary>Shows or hides the volume, so the bootstrap can switch render modes.</summary>
        public void SetVisible(bool visible)
        {
            if (_renderer != null) _renderer.enabled = visible;
        }

        /// <summary>
        /// Pushes every uniform. Must run before the camera renders, i.e. from Update, because the height
        /// texture ping-pongs every step and the march top plane moves every step.
        /// </summary>
        public void UpdateUniforms(float dt)
        {
            if (!_ready || _field == null || !_field.Ready) return;

            _renderer.shadowCastingMode = _shadowMarchEnabled ? ShadowCastingMode.On
                                                              : ShadowCastingMode.Off;

            // BEFORE TrackMarchTop, because the top of the marched volume has to clear the lump lift for
            // the same reason the coarse bias does: the crest reduction measures the FIELD, and a lobe
            // sits up to a full radius above it. Without this the tallest mound would be flat topped.
            DeriveLumpValues();

            TrackMarchTop(dt);

            Vector3 c = _box.position;
            float halfX = _field.PatchSizeX * 0.5f;
            float halfZ = _field.PatchSizeZ * 0.5f;
            var boxMin = new Vector4(c.x - halfX, _field.GroundY, c.z - halfZ, 0f);
            var boxMax = new Vector4(c.x + halfX, _field.GroundY + _maxSnowHeightM, c.z + halfZ, 0f);

            // Both ping-pong / are rebuilt every step, so they have to be rebound every frame.
            _material.SetTexture(kHeightTex, _field.CurrentHeightTexture);
            _material.SetTexture(kCoarseMaxTex, _field.CoarseMaxTexture);

            // Rebuilt every step like the others. The shader tests surface EXISTENCE against this and reads
            // surface HEIGHT from the field above; see _HeightDilateRadius on the field component for why.
            _material.SetTexture(kHeightDilateTex, _field.DilatedHeightTexture);

            // The baked lump lift. NOT ping-ponged, but rewritten over the dirty window every step, and
            // bound every frame for the same reason as the others: the field can reallocate it (a cell
            // size or patch size change destroys and recreates every texture) and a material still
            // holding the destroyed one draws black lobes, not an error.
            _material.SetTexture(kLumpBakeTex, _field.LumpBakeTexture);

            _boxMin = boxMin;
            _boxMax = boxMax;

            _material.SetVector(kBoxMin, boxMin);
            _material.SetVector(kBoxMax, boxMax);
            _material.SetVector(kPatchMin, new Vector4(_field.PatchMin.x, _field.PatchMin.y, 0f, 0f));
            // TWO reciprocals. One would stretch the 120 x 110 m field along Z by 9%, which reads as
            // the snow sliding under the car rather than as anything obviously broken.
            _invPatchSize = new Vector4(1f / Mathf.Max(1e-4f, _field.PatchSizeX),
                                        1f / Mathf.Max(1e-4f, _field.PatchSizeZ), 0f, 0f);
            _material.SetVector(kInvPatchSize, _invPatchSize);
            _material.SetFloat(kGroundY, _field.GroundY);
            _material.SetFloat(kMarchTopY, _marchTopY);

            // A hair above the ground plane, never on it. Every real crossing is at least _minSnowHeightM
            // up, so nothing is lost, and a ray that exits still exits above the ground plane instead of
            // producing hits coplanar with it along the lane edge.
            _material.SetFloat(kMarchFloorY, _field.GroundY + _minSnowHeightM * 0.4f);
            _material.SetFloat(kMinSnowHeight, _minSnowHeightM);

            _material.SetFloat(kCoarseSafeRadius, _field.CoarseSafeRadiusM);

            // The coarse texture bounds the FIELD. It knows nothing about the procedural detail, nor about
            // v6's casual fillet and load exaggeration, so the bound has to be lifted by the largest each
            // of them can ever be or the march could step through a crest. fbm is normalised to +-1 and
            // every other factor is <= 1, so the detail term's worst case is amp * (1 + clumpBoost); the
            // casual terms publish their own hard bounds through ApplyCasualShapeBounds.
            //
            // THE LUMP LATTICE IS NO LONGER IN THIS SUM, and removing it is half the point of baking it.
            // It used to add the full effective radius - 30 cm - as blanket headroom over the whole
            // 120 x 110 m field, because a lobe could be anywhere; flat virgin snow has both lump gates at
            // 0 and therefore no lobe at all, so that was 30 cm of extra descent on every ray for a term
            // that was not there. The bound now comes out of the coarse TEXTURE instead: CoarseMaxBlock
            // maxes the baked lift over each coarse cell, so it is tight where the lobes are and exactly
            // zero where they are not. See CoarseMaxYFrom in SnowMarchCoreV7.hlsl for the full argument.
            _coarseMaxBias = _detailAmpM * (1f + _clumpBoost) + _casualLiftBoundM;
            _material.SetFloat(kCoarseMaxBias, _coarseMaxBias);

            _material.SetFloat(kMaxSteps, _maxSteps);
            _material.SetFloat(kStepM, _stepM);
            _material.SetFloat(kRefineSteps, _refineSteps);
            _material.SetFloat(kLodDistanceInv, 1f / Mathf.Max(0.01f, _lodDistanceM));

            // Clamped so the detail can never pull the surface below the bare-ground threshold: with
            // fade >= amplitude the function h - amp * saturate((h - min) / fade) is non-decreasing in h,
            // so its minimum over the fade band is exactly min.
            float thin = Mathf.Max(_detailThinM, _detailAmpM);
            _detailThin = thin;

            _material.SetFloat(kDetailAmpM, _detailAmpM);
            _material.SetFloat(kDetailFreq, _detailFreq);
            _material.SetFloat(kDetailOctaves, _detailOctaves);
            _material.SetFloat(kDetailThinInv, 1f / Mathf.Max(1e-4f, thin));
            _material.SetFloat(kDetailFadeStart, _detailFadeStartM);
            _material.SetFloat(kDetailFadeInv, 1f / Mathf.Max(0.01f, _detailFadeRangeM));

            // Same argument one level up: the clump boost must not start until the snow is deeper than the
            // boosted amplitude plus the threshold, or the boosted noise could dip under.
            float clumpStart = Mathf.Max(_clumpStartM,
                                         _minSnowHeightM + _detailAmpM * (1f + _clumpBoost) + thin);
            _clumpStartEff = clumpStart;
            _uniformsValid = true;

            _material.SetFloat(kClumpStartM, clumpStart);
            _material.SetFloat(kClumpRampInv, 1f / Mathf.Max(0.01f, _clumpRampM));
            _material.SetFloat(kClumpBoost, _clumpBoost);

            // The lump lattice, in TWO uniforms rather than ten. _LumpRadiusM is the decode scale for the
            // baked 0..1 texel AND the on/off switch: the shader compares it against zero and skips the
            // tap entirely, so 0 is a real zero-cost path. The seven lattice-SHAPE knobs went to the
            // field's bake kernel through PushLumpBakeParams; the FADE stays here because it is a function
            // of the ray's entry range and a world-space texture cannot hold it.
            _material.SetFloat(kLumpRadiusM, _lumpRadiusEffM);
            _material.SetFloat(kLumpFadeStartM, _lumpFadeStartM);
            _material.SetFloat(kLumpFadeInv, 1f / Mathf.Max(0.01f, _lumpFadeRangeM));

            // Normal-only detail. Deliberately NOT folded into the coarse bias: it never enters the surface
            // function, so the bound it would otherwise have to be widened for still holds.
            _material.SetFloat(kNrmDetailAmp, _normalDetailAmpM);
            _material.SetFloat(kNrmDetailFreq, _normalDetailFreq);
            _material.SetFloat(kNrmDetailOctaves, _normalDetailOctaves);

            _material.SetFloat(kNormalEpsM, _normalEpsM);
            _material.SetFloat(kNormalClampM, _normalClampM);

            _material.SetFloat(kAoStrength, _aoStrength);
            _material.SetFloat(kAoScaleInv, 1f / Mathf.Max(1e-4f, _aoScaleM));
            _material.SetFloat(kAoWideEpsM, _aoWideEpsM);
            _material.SetFloat(kAoWideScaleInv, 1f / Mathf.Max(1e-4f, _aoWideScaleM));

            _material.SetFloat(kSoftShadowSteps, _softShadowSteps);
            _material.SetFloat(kSoftShadowStepM, _softShadowStepM);
            _material.SetFloat(kSoftShadowStartM, _softShadowStartM);
            _material.SetFloat(kSoftShadowHard, _softShadowHardness);
            _material.SetFloat(kSoftShadowStr, _softShadowStrength);
            _material.SetFloat(kShadowNormalBias, _shadowNormalBiasM);
            _material.SetFloat(kShadowDepthBias, _shadowDepthBiasM);

            _material.SetFloat(kWrap, _wrap);
            _material.SetFloat(kFill, _fill);
            _material.SetFloat(kSheen, _sheen);
            _material.SetFloat(kSheenPower, _sheenPower);
            _material.SetFloat(kWallTint, _wallTint);
            _material.SetFloat(kWallTintInv, 1f / Mathf.Max(0.01f, _wallTintRange));

            _material.SetColor(kBaseColor, _baseColor);
            _material.SetColor(kDeepColor, _deepColor);
            _material.SetColor(kAmbientColor, _ambientColor);
        }

        /// <summary>
        /// Hands every uniform the SHARED MARCH CORE reads to a compute kernel.
        ///
        /// This is the other half of what makes the steps probe an instrument rather than a guess.
        /// SnowMarchCoreV7.hlsl is included by this shader AND by the probe kernel, so the code is
        /// identical by construction; this makes the INPUTS identical too, and from the same cached
        /// values that were pushed to the material microseconds earlier rather than from a second
        /// evaluation of the same expressions.
        ///
        /// Textures are forwarded explicitly because a compute kernel does not inherit global texture
        /// bindings, and the three of them ping-pong or are rebuilt every step.
        ///
        /// Returns false when the renderer has not pushed a frame's uniforms yet, so the probe can say
        /// so instead of measuring zeros.
        /// </summary>
        public bool PushMarchUniforms(ComputeShader cs, int kernel)
        {
            if (!_ready || cs == null || _field == null || !_field.Ready || !_uniformsValid) return false;

            cs.SetTexture(kernel, kHeightTex, _field.CurrentHeightTexture);
            cs.SetTexture(kernel, kCoarseMaxTex, _field.CoarseMaxTexture);
            cs.SetTexture(kernel, kHeightDilateTex, _field.DilatedHeightTexture);

            // The baked lump lift. THE PROBE WOULD SILENTLY MEASURE THE TERM SWITCHED OFF WITHOUT THIS:
            // an unbound Texture2D reads as zero, the tap would return no lift, and the probe would report
            // the mean step count of a field with no lobes on it while the screen showed lobes. That is
            // exactly the class of failure the shared march core exists to prevent, and moving the lattice
            // into a texture reintroduces it as a BINDING that has to be made rather than as code that
            // could differ - which is the version of the problem that a missing line makes obvious.
            cs.SetTexture(kernel, kLumpBakeTex, _field.LumpBakeTexture);

            cs.SetVector(kBoxMin, _boxMin);
            cs.SetVector(kBoxMax, _boxMax);
            cs.SetVector(kPatchMin, new Vector4(_field.PatchMin.x, _field.PatchMin.y, 0f, 0f));
            cs.SetVector(kInvPatchSize, _invPatchSize);

            cs.SetFloat(kGroundY, _field.GroundY);
            cs.SetFloat(kMarchTopY, _marchTopY);
            cs.SetFloat(kMarchFloorY, _field.GroundY + _minSnowHeightM * 0.4f);
            cs.SetFloat(kMinSnowHeight, _minSnowHeightM);

            cs.SetFloat(kCoarseSafeRadius, _field.CoarseSafeRadiusM);
            cs.SetFloat(kCoarseMaxBias, _coarseMaxBias);

            cs.SetFloat(kMaxSteps, _maxSteps);
            cs.SetFloat(kStepM, _stepM);
            cs.SetFloat(kRefineSteps, _refineSteps);
            cs.SetFloat(kLodDistanceInv, 1f / Mathf.Max(0.01f, _lodDistanceM));

            cs.SetFloat(kDetailAmpM, _detailAmpM);
            cs.SetFloat(kDetailFreq, _detailFreq);
            cs.SetFloat(kDetailOctaves, _detailOctaves);
            cs.SetFloat(kDetailThinInv, 1f / Mathf.Max(1e-4f, _detailThin));
            cs.SetFloat(kDetailFadeStart, _detailFadeStartM);
            cs.SetFloat(kDetailFadeInv, 1f / Mathf.Max(0.01f, _detailFadeRangeM));

            cs.SetFloat(kClumpStartM, _clumpStartEff);
            cs.SetFloat(kClumpRampInv, 1f / Mathf.Max(0.01f, _clumpRampM));
            cs.SetFloat(kClumpBoost, _clumpBoost);

            // The lump lattice, in TWO uniforms plus the texture above. The seven lattice-SHAPE knobs are
            // no longer uniforms of the march core at all - they belong to the bake - so there is nothing
            // left here that could differ between the probe and the draw except the radius, which comes
            // from the same cached effective value the material got.
            cs.SetFloat(kLumpRadiusM, _lumpRadiusEffM);
            cs.SetFloat(kLumpFadeStartM, _lumpFadeStartM);
            cs.SetFloat(kLumpFadeInv, 1f / Mathf.Max(0.01f, _lumpFadeRangeM));

            return true;
        }

        /// <summary>
        /// Resolves the lump lattice's EFFECTIVE radius and the hard bound on its lift.
        ///
        /// THE CLAMP IS A CORRECTNESS CLAMP. The bake only searches the 3x3 lattice neighbourhood of the
        /// query point. A centre sits at most 0.5 * (1 + jitter) cells from its own cell centre, so a lump
        /// two cells away can only reach the query point once
        ///     radius > spacing * (1.5 - 0.5 * jitter)
        /// and past that a lobe would pop in and out as the query point crossed a cell boundary - visible
        /// as a seam in the baked texture that would not even be consistent between one bake window and
        /// the next. Clamping the radius is cheaper than a 5x5 neighbourhood, which would be 25 hash
        /// evaluations per baked texel instead of 9.
        ///
        /// The hard bound on the lift is then exactly that radius, because the lift is sqrt(r^2 - d^2),
        /// the per-lump radius variation only ever reduces r, and every gate and fade factor is in 0..1.
        /// That is what makes lift / radius a full-range 0..1 encoding for the 8-bit bake.
        ///
        /// WHERE THAT BOUND IS STILL SPENT, since the bake took it out of the empty-space skip's bias:
        /// the MARCH TOP PLANE, in TrackMarchTop. The crest reduction measures the height FIELD and a
        /// lobe sits up to a full radius above it, so the top of the marched volume still has to clear
        /// it or the tallest mound would be drawn flat topped. That is one plane for the whole draw, not
        /// headroom every ray descends through, which is why it costs almost nothing where the bias cost
        /// 30 cm of descent per ray.
        /// </summary>
        private void DeriveLumpValues()
        {
            float rMax = _lumpSpacingM * (1.5f - 0.5f * Mathf.Clamp01(_lumpJitter));

            _lumpRadiusEffM = Mathf.Min(Mathf.Max(0f, _lumpRadiusM), rMax);
            _lumpLiftBoundM = _lumpRadiusEffM;
        }

        /// <summary>
        /// Moves the top of the volume the march actually traverses.
        ///
        /// Up instantly, down slowly. The crest arrives over AsyncGPUReadback and is therefore a few frames
        /// stale, so a rate term extrapolates over that latency and the margin covers the remainder. Getting
        /// this wrong low draws the crest as a flat plateau for a few frames; getting it wrong high only
        /// spends march steps, and with the coarse-max skip in place those steps are cheap.
        /// </summary>
        private void TrackMarchTop(float dt)
        {
            float observed = _field.FieldMaxHeight;

            _sinceFieldMaxChange += dt;
            if (!Mathf.Approximately(observed, _lastFieldMax))
            {
                if (_lastFieldMax >= 0f)
                    _fieldMaxRate = (observed - _lastFieldMax) / Mathf.Max(1e-3f, _sinceFieldMaxChange);
                _lastFieldMax = observed;
                _sinceFieldMaxChange = 0f;
            }

            float lead = Mathf.Max(0f, _fieldMaxRate) * _marchTopLeadSeconds;

            // The casual lift bound AND the lump lift bound are added here as well as to the coarse bias:
            // the crest reduction measures the FIELD, and the fillet, the exaggeration and a lobe can each
            // put the drawn surface that much above it. Without this the very top of the tallest mound
            // would be flat topped whenever the margin happened to be tight.
            float wanted = _field.GroundY
                         + Mathf.Min(_maxSnowHeightM,
                                     observed + lead + _marchTopMarginM
                                     + _casualLiftBoundM + _lumpLiftBoundM);

            // Never collapse the volume: an almost empty field still needs a marchable slab.
            wanted = Mathf.Max(wanted, _field.GroundY + Mathf.Min(_maxSnowHeightM, 0.05f));

            _marchTopY = (wanted > _marchTopY)
                ? wanted
                : Mathf.Max(wanted, _marchTopY - _marchTopFallRate * dt);
        }
    }
}
