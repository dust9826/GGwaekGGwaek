using System.Text;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// Variant B <b>v7</b> entry point: <b>THE PILE, AND THE THREE VERBS THAT DRIVE IT</b>. Drop this on one
    /// empty GameObject in an empty scene and press Play. It creates the ground, the light, a chase camera,
    /// the vehicle with its BLADE, the materials and the mesh, wires up the height field and the raymarcher,
    /// drives the fixed measurement course - which demonstrates every verb - and prints the [V7] line.
    ///
    /// WHAT V7 IS
    /// ----------
    /// v6 with the BALL taken off and a PILE put in its place. The vehicle pushes a heap of snow that
    /// ACCUMULATES as it ploughs; the heap is written into the HEIGHT FIELD rather than drawn as a separate
    /// mesh; its mass governs the driving; snow curls off the blade ends into a windrow as it fills; and
    /// reversing out from under it leaves it standing.
    ///
    /// THE ONE SENTENCE: <b>THE PILE IS FIELD HEIGHT.</b> Everything else follows from that -
    ///   * the existing raymarcher, the existing baked lump lobes and the existing casual treatment draw
    ///     the pile for free, so there is no ball shader, no ball mesh, no spin and no contact AO;
    ///   * the drawn silhouette IS the collidable surface, because there is only one surface;
    ///   * and the thing in front of the vehicle reads as being SHOVED rather than ROLLED, which is the
    ///     whole brief.
    ///
    /// THE VERB GRAMMAR, WHICH IS WHAT MAKES IT A VEHICLE RATHER THAN A DEMO
    /// -------------------------------------------------------------------
    /// <code>
    ///     blade DOWN / UP                       plough / transit          E
    ///     blade angle LEFT / STRAIGHT / RIGHT   where the snow is thrown  1 / 2 / 3
    ///     FORWARD / REVERSE                     accumulate / deposit      W / S
    /// </code>
    /// THREE VERBS, NO FOURTH, AND IN PARTICULAR NO DUMP BUTTON: reversing with the blade down IS the dump,
    /// and it is a better one - it leaves the pile in the shape the pile actually had, at the place the
    /// player put it, and it costs no dispatches because a deposit is a retired receipt rather than an emit.
    ///
    /// The set is self-consistent in two ways that are the reason it was chosen. Reversing with the blade UP
    /// carries nothing and leaves nothing, so a vehicle wedged against the stage boundary with a full blade
    /// can always get out. And an angled blade produces a real lateral force, so "angled left pushes me
    /// right" is a cue the player can learn rather than noise.
    ///
    /// WHAT CHANGED UNDERNEATH, IN ORDER OF HOW MUCH
    /// ---------------------------------------------
    ///   1. THE LOAD IS FIELD HEIGHT. Erase the heap's recorded footprint, cut, re-emit the whole ledger
    ///      at the new pose - every frame, conservatively, using v6's release machinery run continuously.
    ///      SnowPileFieldV7's header states the invariant's algebra line by line.
    ///   2. THE FOOTPRINT IS A SWEPT ORIENTED-BOX UNION, exact per sub-step rather than tiled-and-hoped,
    ///      and ROTATED by the blade angle. v6 swept a capsule because a ball is a disc.
    ///   3. THE BITE IS THE WHOLE COLUMN. 30 cm per pass, not v6's 4 cm skin, because v6 measured that a
    ///      skin makes the world barely record the pass.
    ///   4. THE RELEASE IS A RATE. Snow curls off the blade ends continuously as the fill rises, not at a
    ///      threshold - and with the blade angled it goes preferentially to the discharge end, which is
    ///      what makes a windrow instead of two symmetric walls.
    ///   5. A RELAX GUARD on the heap's footprint, so the deliberately-steep leading face is not eaten by
    ///      the repose relax the instant it is emitted - and the face itself now COLLAPSES toward repose
    ///      when the shove stops, and costs a brief extra resistance to break loose again.
    ///   6. THE MASS REFERENCE IS RETUNED, from 2,400 kg to 1,200, because v7's load is BOUNDED by
    ///      capacity where v6's was an unbounded running total.
    ///   7. THE TOP SPEED FALLS WITH THE SNOW DEPTH UNDER THE WHEELS, multiplicative with and separate from
    ///      the carried-mass factor. That is the reward that makes blade-up transit worth anything: a lane
    ///      the blade has cleared is a ROAD.
    ///
    /// WHAT V7 DROPPED FROM V6, AND WHY IT IS NOT A REGRESSION
    /// ------------------------------------------------------
    /// SnowBallRendererV6, SnowBallV6.shader, the icosphere mesh, the roll integration, the radius-fraction
    /// lump displacement and the contact AO - about 550 lines whose entire job was to make a second surface
    /// look like it belonged to the first. v7 has one surface. THE DUMP VERB went too, with its key, its
    /// course segment, its three cone knobs, its emit path and its `| dump` telemetry section; the verb
    /// grammar contains it. The raymarcher, the baked lump layer, the casual treatment, the steps probe, the
    /// activity-trail relax window and the mass invariant all came across unchanged.
    ///
    /// NOTHING IN THIS FILE IS MEASURED. This variant was authored without running the editor; every
    /// number in a tooltip that is not inherited from a v6 measurement is DERIVED and says so.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowFakeV7Bootstrap : MonoBehaviour
    {
        // -------------------------------------------------------------------------------------------
        // Sweep forwarding.
        //
        // The workers are created at runtime, so their own inspectors do not exist while the editor is
        // stopped and the Unity CLI refuses component edits during play. Every knob below is therefore
        // forwarded to the component that owns it, and the contract throughout is "an argument below its
        // legal minimum leaves the component's own value alone", with a strictly negative sentinel wherever
        // 0 is a meaningful value.
        //
        // SCENE-SERIALISED VALUES BEAT NEW C# DEFAULTS. Anything already present on a saved scene object
        // wins over a default changed here; this project has been bitten by that repeatedly. v7 ships as a
        // NEW component on a NEW GameObject, so on a fresh scene these defaults are what run.
        // -------------------------------------------------------------------------------------------

        [Header("CASUAL LOOK - master")]
        [Tooltip("THE A/B. 0 = the realistic look, bit for bit. 1 = full casual. Every shader computes the " +
                 "realistic colour in full and then lerps to the casual one by this value, so 0 is an " +
                 "identity rather than an approximation. C toggles it at runtime.")]
        [Range(0f, 1f)]
        [SerializeField] private float _casualAmount = 1f;

        [Header("CASUAL LOOK - banded diffuse and the coloured shadow")]
        [Tooltip("Quantised light steps. 4 is the LOCKED value inherited from v5/v6. B steps it at runtime.")]
        [Range(1f, 8f)]
        [SerializeField] private float _casualBands = 4f;

        [Tooltip("Width of each band's transition, in band units. 0.45 is the LOCKED value. 0 is a hard " +
                 "cel edge; 1 removes the plateau entirely.")]
        [Range(0f, 1f)]
        [SerializeField] private float _casualBandSoftness = 0.45f;

        [Tooltip("Diffuse wrap applied BEFORE quantisation, so the band edges land on the body of the form " +
                 "rather than on the terminator where the surface is nearly tangent to the light.")]
        [Range(0f, 1f)]
        [SerializeField] private float _casualWrap = 0.25f;

        [SerializeField] private Color _casualLitColor = new Color(1.00f, 0.985f, 0.945f, 1f);
        [SerializeField] private Color _casualMidColor = new Color(0.80f, 0.845f, 0.98f, 1f);

        [Tooltip("The SHADOW end: a saturated BLUE-VIOLET, never black. This single choice does most of " +
                 "the work of reading as 'toy'. Setting it to something dark and grey is the fastest way " +
                 "to undo the whole look.")]
        [SerializeField] private Color _casualShadowColor = new Color(0.40f, 0.40f, 0.78f, 1f);

        [Tooltip("How much of the renderer's own albedo survives into the casual colour. CASUAL IS MADE BY " +
                 "WITHHOLDING INFORMATION: 0 is a flat three-colour palette that reads as a sticker, 1 is " +
                 "cel-shaded photography. 0.35 is the locked value.")]
        [Range(0f, 1f)]
        [SerializeField] private float _casualAlbedoInfluence = 0.35f;

        [Range(0f, 1f)]
        [SerializeField] private float _casualAoInfluence = 0.45f;

        [Range(0.1f, 3f)]
        [SerializeField] private float _casualExposure = 1.05f;

        [Header("CASUAL LOOK - no wet glint: rim and sparkles instead")]
        [Range(0f, 2f)]
        [SerializeField] private float _casualRimStrength = 0.22f;

        [Range(0.5f, 16f)]
        [SerializeField] private float _casualRimPower = 2.2f;

        [SerializeField] private Color _casualRimColor = new Color(0.86f, 0.92f, 1.00f, 1f);

        [Range(0f, 4f)]
        [SerializeField] private float _casualSparkleAmount = 0.55f;

        [Range(0.02f, 2f)]
        [SerializeField] private float _casualSparkleScaleM = 0.24f;

        [Range(0.02f, 0.5f)]
        [SerializeField] private float _casualSparkleRadius = 0.16f;

        [Range(0f, 1f)]
        [SerializeField] private float _casualSparkleThreshold = 0.86f;

        [Range(0f, 8f)]
        [SerializeField] private float _casualSparkleSpeed = 0.35f;

        [SerializeField] private Color _casualSparkleColor = new Color(1f, 1f, 1f, 1f);

        [Header("CASUAL LOOK - fat edges (raymarch)")]
        [Tooltip("Fillet amplitude in metres. 0.09 is the LOCKED value. 0 disables it and the raymarched " +
                 "surface is the realistic one. IT MATTERS MORE IN V7 THAN IN V6: the heap's leading face " +
                 "and its two hips are the biggest convex shoulders the variant has ever built, and this " +
                 "is what stops them reading as folded card.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float _casualRoundM = 0.09f;

        [Tooltip("The HEADROOM at which the fillet is HALF applied, in metres. 0.14 is the LOCKED value. " +
                 "NOT a blend width.")]
        [Range(0.005f, 0.5f)]
        [SerializeField] private float _casualRoundK = 0.14f;

        [Tooltip("Half-width in metres of the finite difference the BAND term's normal is taken over. 0.03 " +
                 "is the LOCKED value. 0 drives the bands from the shading normal instead, which " +
                 "reproduces contour terracing and is the A/B.")]
        [Range(0f, 0.5f)]
        [SerializeField] private float _casualBandNormalWideM = 0.03f;

        [Header("CASUAL LOOK - exaggeration (NEUTRAL AT 1.0)")]
        [Tooltip("Makes snow above the virgin slab depth look deeper than it is. NEUTRAL AT 1.0.\n\n" +
                 "IT IS WORTH A LOOK IN V7 WHERE IT WAS NOT IN V6: v6's exaggeration could only act on " +
                 "berms a few centimetres tall, but v7's HEAP is entirely above the slab depth, so this " +
                 "term now has the variant's headline object to work on. Bounded by Casual Load Lift Max M " +
                 "and reported to the raymarcher, so the march's empty-space bound stays valid.")]
        [Range(1f, 3f)]
        [SerializeField] private float _casualLoadExaggeration = 1f;

        [Range(0f, 0.5f)]
        [SerializeField] private float _casualLoadLiftMaxM = 0.12f;

        // -------------------------------------------------------------------------------------------
        [Header("Raymarch - volume")]
        [Tooltip("Height of the proxy box above the ground, in metres. RAISED from v6's 2.5, and it has " +
                 "to be: v6's tallest feature was a 1.2 m dumped mound on a 0.30 m slab, where v7's is a " +
                 "1.6 m HEAP standing on that same slab plus the fillet - and a dumped mound on top of a " +
                 "wall can beat that. 3.0 leaves headroom. It is nearly free because the march's top " +
                 "plane tracks the MEASURED crest and only uses the box as a ceiling.")]
        [Range(-1f, 6f)]
        [SerializeField] private float _maxSnowHeightM = 3.0f;

        [Header("Raymarch - perf knobs")]
        [Tooltip("Hard bound on march iterations. A ray that hits this MISSES, so it is a correctness knob " +
                 "before it is a cost knob. Read the probe's p95 and exhausted% before touching it.")]
        [Range(-1, 512)]
        [SerializeField] private int _maxSteps = 256;

        [Tooltip("Fine march step in metres. 0.04 is the LOCKED value: 0.32 texels at the 12.5 cm cell.")]
        [Range(-1f, 0.4f)]
        [SerializeField] private float _stepM = 0.04f;

        [Range(-1, 10)]
        [SerializeField] private int _refineSteps = 4;

        [Range(-1f, 200f)]
        [SerializeField] private float _lodDistanceM = 18f;

        [Tooltip("-1 leave alone, 0 off, 1 on. Roughly doubles the raymarch cost and the snow does not " +
                 "sample the shadow map itself, so it makes the snow shadow the ground and the VEHICLE but " +
                 "not itself.")]
        [Range(-1, 1)]
        [SerializeField] private int _shadowMarchEnabled = 0;

        [Header("Raymarch - surface detail (the LOCKED casual values)")]
        [Tooltip("Peak procedural detail amplitude in metres. 0.06 with a 0.42 cycles/m frequency and ONE " +
                 "octave is the locked casual surface: a single 2.4 m roll, which is macro FORM for " +
                 "banding rather than texture.")]
        [Range(-1f, 0.1f)]
        [SerializeField] private float _detailAmpM = 0.06f;

        [Tooltip("Cycles per metre of the first detail octave. 0.42 is a 2.4 m wavelength: ONE BIG ROLL.")]
        [Range(-1f, 40f)]
        [SerializeField] private float _detailFreq = 0.42f;

        [Tooltip("Detail octaves. 1 is the locked value. Also the main detail cost knob, since the noise " +
                 "runs at every march step.")]
        [Range(-1, 4)]
        [SerializeField] private int _detailOctaves = 1;

        [Tooltip("Extra detail amplitude, as a multiple, on snow the blade has piled. THE HEAP IS THE " +
                 "BIGGEST CONSUMER OF THIS IN THE VARIANT'S HISTORY: it stands 1.6 m above the slab, so " +
                 "the clump ramp is fully saturated over the whole heap and only the heap, which is " +
                 "exactly the worked/unworked distinction it exists to draw.")]
        [Range(-1f, 4f)]
        [SerializeField] private float _clumpBoost = 1.2f;

        [Tooltip("Curvature ambient occlusion strength. 0.40 is the locked value: the coloured shadow band " +
                 "already does the darkening, and stacking a grey curvature multiply on top is what makes " +
                 "stylised shading look dirty rather than graphic.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _aoStrength = 0.40f;

        [Range(-1, 32)]
        [SerializeField] private int _softShadowSteps = 8;

        [Tooltip("Amplitude in metres of the SECOND, sharper detail term applied only to the shading " +
                 "normal. 0 IS THE LOCKED VALUE - NO GRAIN.")]
        [Range(-1f, 0.04f)]
        [SerializeField] private float _normalDetailAmpM = 0f;

        [Range(-1f, 40f)]
        [SerializeField] private float _normalDetailFreq = 18f;

        [Range(-1, 3)]
        [SerializeField] private int _normalDetailOctaves = 3;

        [Tooltip("Specular sheen for the REALISTIC path. 0 IS THE LOCKED VALUE - this is the wet look.")]
        [Range(-1f, 2f)]
        [SerializeField] private float _sheen = 0f;

        // -------------------------------------------------------------------------------------------
        [Header("Raymarch - THE LUMP LATTICE, BAKED (measured on v6: gpu 7.83 / meanSteps 34.61)")]
        [Tooltip("Lump sphere radius in metres. 0 SWITCHES THE WHOLE TERM OFF and is the A/B against v6's " +
                 "measured lobes-off baseline (gpu 6.13 +-0.14, frame 3.76, meanSteps 23.70); lobes on and " +
                 "BAKED measured gpu 7.83 +-0.52, frame 4.56, meanSteps 34.61 over 14 samples. -1 leaves " +
                 "the renderer's value alone.\n\n" +
                 "DO NOT REVERT TO THE PER-STEP NINE-HASH VERSION: the same look cost gpu 15.44 that way. " +
                 "The bake is 1920 x 1760 R8 at 2x field resolution, written by a windowed kernel, read " +
                 "with ONE bilinear tap.")]
        [Range(-1f, 1.2f)]
        [SerializeField] private float _lumpRadiusM = 0.30f;

        [Tooltip("Lattice spacing in metres. SMALLER THAN THE RADIUS ON PURPOSE - 0.35 against 0.30 - so " +
                 "neighbouring caps overlap and the max-combine reads as MERGED LOBES rather than as " +
                 "isolated bumps on a plane. It also bounds the radius: the bake searches a 3x3 " +
                 "neighbourhood, which is only sufficient while radius <= spacing * (1.5 - 0.5 * jitter), " +
                 "and the renderer clamps the radius to that rather than widening the search.")]
        [Range(-1f, 2f)]
        [SerializeField] private float _lumpSpacingM = 0.35f;

        [Tooltip("How far a lump centre may wander inside its own cell, as a fraction of a cell. 0 is a " +
                 "perfectly regular lattice, which reads as a waffle. 0 is meaningful, so -1 is the " +
                 "leave-alone sentinel.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _lumpJitter = 0.35f;

        [Tooltip("Per-lump radius variation. Each lump gets r * (1 - this * hash), so it only ever REDUCES " +
                 "the radius and the bound the march relies on is untouched.")]
        [Range(-1f, 0.8f)]
        [SerializeField] private float _lumpRadiusVary = 0.25f;

        [Tooltip("Snow depth ABOVE the bare-ground threshold over which the lobes fade in, in metres. " +
                 "CORRECTNESS, not polish: the gate is exactly 0 at the threshold so a lobe can never " +
                 "float over ground the simulation has carved bare, and v7 carves to bare ground on every " +
                 "pass.")]
        [Range(-1f, 0.6f)]
        [SerializeField] private float _lumpGateDepthM = 0.10f;

        [Tooltip("Local RELIEF in metres that fully applies the lobes, where relief is the fillet dilation " +
                 "minus the field at the same point - zero on flat virgin snow, large on a heap flank, a " +
                 "spill wall or a dumped mound, and free because the march already took that tap.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _lumpReliefM = 0.10f;

        [Tooltip("How much of the lobe amplitude the relief term controls. 0 = lobes everywhere the snow " +
                 "is deep enough; 1 = lobes ONLY where there is relief. 0.75 leaves flat virgin snow at a " +
                 "quarter amplitude and gives THE HEAP the full lobes, which is what makes the pile read " +
                 "as packed snow rather than as a smooth wedge. 0 is meaningful, so -1 is the sentinel.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _lumpSlopeStrength = 0.75f;

        [Tooltip("Ray distance at which the lobes start fading out, in metres. Past the end of the fade " +
                 "the surface function never enters the lattice tap, so the far field costs nothing extra.")]
        [Range(-1f, 120f)]
        [SerializeField] private float _lumpFadeStartM = 14f;

        [Tooltip("Range over which the lobes finish fading out, in metres.")]
        [Range(-1f, 120f)]
        [SerializeField] private float _lumpFadeRangeM = 12f;

        // -------------------------------------------------------------------------------------------
        [Header("Field - THE REAL STAGE SIZE")]
        [Tooltip("World X of the snow patch corner. -60 is the real project's SnowStage origin. NaN is not " +
                 "a legal value here and the origin is legitimately negative, so unlike every other knob " +
                 "on this component this one has NO leave-alone sentinel: it is always forwarded.")]
        [SerializeField] private float _patchOriginX = -60f;

        [SerializeField] private float _patchOriginZ = -55f;

        [Range(4f, 400f)]
        [SerializeField] private float _patchSizeX = 120f;

        [Range(4f, 400f)]
        [SerializeField] private float _patchSizeZ = 110f;

        [Tooltip("Metres per texel. 0.125 over 120 x 110 m is 960 x 880 = 845k cells, the real project's " +
                 "SnowStage cell exactly, so every number measured here transfers.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _cellSizeM = 0.125f;

        [Range(1, 16)]
        [SerializeField] private int _mirrorDownsample = 2;

        [Range(-1, 8)]
        [SerializeField] private int _relaxIterations = 4;

        [Tooltip("Angle of repose in degrees. In v7 it has THREE jobs, not one: it decides whether a spill " +
                 "wall and a dumped mound stand or slump, AND it is the heap's own FLANK angle, so it also " +
                 "decides how far the heap fans out sideways at a given height and therefore how much " +
                 "volume the capacity holds. Changing it moves the capacity - see the field's own tooltip.")]
        [Range(-1f, 80f)]
        [SerializeField] private float _reposeAngleDeg = 55f;

        [Range(-1, 6)]
        [SerializeField] private int _heightDilateRadius = 1;

        [Range(-1, 8)]
        [SerializeField] private int _filletDilateRadius = 2;

        // -------------------------------------------------------------------------------------------
        [Header("THE BLADE")]
        [Tooltip("Depth of TERRAIN the blade shaves per PASS, in metres. -1 leaves the field's own value " +
                 "alone. 0.30 IS THE WHOLE SLAB, and it is v6's measured lesson: v6's 4 cm skin made the " +
                 "world barely record the pass. A plough clears. At 0.04 you get v6's pacing with v7's " +
                 "shape, which is the A/B.")]
        [Range(-1f, 0.60f)]
        [SerializeField] private float _pickupDepthM = 0.30f;

        [Tooltip("Per-PASS cap on how fast the blade reclaims material it ALREADY OWNS - its own spill " +
                 "walls and its own dumped mounds. That re-pickup is FREE, which is what makes gather -> " +
                 "shove -> dump a loop, but uncapped a two-metre mound would enter the ledger in ONE FRAME.")]
        [Range(-1f, 3f)]
        [SerializeField] private float _pileGrabPerPassM = 0.60f;

        [Tooltip("The blade's cutting width in metres. 2.30 is v5's blade, which is the real project's. " +
                 "CONSTANT - v6's clearing width grew with the ball, and not having that is what makes the " +
                 "overflow escape sideways instead of the stripe widening to swallow it.")]
        [Range(-1f, 8f)]
        [SerializeField] private float _bladeWidthM = 2.3f;

        [Tooltip("The blade's thickness along travel, in metres. It is the residence length the per-pass " +
                 "cut budget is divided over, so it is what keeps the cut timestep independent: a parked " +
                 "blade cuts nothing.")]
        [Range(-1f, 2f)]
        [SerializeField] private float _bladeDepthM = 0.35f;

        [Tooltip("Density of packed snow in kg/m3. THE MASTER GAIN ON HOW HEAVY THIS FEELS, because it is " +
                 "the only thing that turns the pile's volume into the mass the handling reads. 300 is " +
                 "wind-packed snow; the 6.17 m3 capacity heap is then 1,850 kg.")]
        [Range(-1f, 900f)]
        [SerializeField] private float _snowDensityKgPerM3 = 300f;

        [Tooltip("Swept boxes the footprint is tiled into. ONE is exact for a translation; the tiling is " +
                 "only for a turn's arc and for the blade's own rotation through the step. 1 is the chord " +
                 "A/B.")]
        [Range(-1, 8)]
        [SerializeField] private int _bladeSegments = 3;

        [Tooltip("Gap in metres between the vehicle's nose and the blade line. The blade's mount distance " +
                 "is carLength/2 + this, and unlike v6's ball mount it is CONSTANT - the blade does not " +
                 "move as the pile grows. What moves ahead of the blade is the heap's crest, by Heap Crest " +
                 "Ahead M on the field.")]
        [Range(0f, 2f)]
        [SerializeField] private float _bladeGapM = 0.10f;

        // -------------------------------------------------------------------------------------------
        [Header("THE HEAP - shape, and the split that IS the accumulating feel")]
        [Tooltip("MAXIMUM heap peak height in metres, above whatever the field already holds. One half of " +
                 "the CAPACITY. -1 leaves the field's own value alone.\n\n" +
                 "1.60 m with the shipped shape knobs is a DERIVED 6.17 m3 / 1,850 kg capacity, about " +
                 "2.8 s and 22 m of ploughing at 8 m/s. Raise it for a longer accumulation phase and a " +
                 "heavier endgame; lower it to start building walls almost immediately. Keep it under Max " +
                 "Snow Height M minus the slab depth or the heap's top is drawn as a flat plateau.")]
        [Range(-1f, 3f)]
        [SerializeField] private float _heapMaxHeightM = 1.60f;

        [Tooltip("THE SPLIT KNOB. Metres of extra crest HALF-LENGTH per metre of heap HEIGHT, i.e. how the " +
                 "volume is divided between growing TALLER and growing WIDER. 0 is MEANINGFUL (all volume " +
                 "into height), so -1 is the leave-alone sentinel.\n\n" +
                 "0 = a tower inside the blade's own width, which reads as a wall being carried. 2 = a low " +
                 "wide fan, which reads as a windrow forming. 1.10 holds the heap about four times wider " +
                 "than it is tall at every size, so the growth is legible in BOTH dimensions.")]
        [Range(-1f, 3f)]
        [SerializeField] private float _heapWidthPerHeight = 1.10f;

        [Tooltip("Hard cap on the crest HALF-length in metres: the other half of the capacity. 3.2 is a " +
                 "6.4 m crest against a 2.3 m blade - a heap already bulging well past the blade's ends, " +
                 "which is where a real plough starts shedding.")]
        [Range(-1f, 8f)]
        [SerializeField] private float _heapMaxHalfWidthM = 3.2f;

        [Tooltip("How far AHEAD of the blade line the heap's crest sits, in metres. LOAD BEARING: it must " +
                 "exceed peak/tan(backAngle) + bladeDepth/2, or the heap's back toe hangs over the trench " +
                 "the blade just cut, stands on bare ground as a 30 cm cliff, and relax - which only " +
                 "guards pairs with BOTH texels inside the heap - drags heap material back into the lane " +
                 "where the erase debits it for good. The pile would bleed backwards.\n\n" +
                 "At 1.60 m peak and 75 deg the constraint is > 0.604; 0.75 leaves 0.146 m of clearance. " +
                 "RAISE IT IF YOU RAISE THE PEAK OR FLATTEN THE BACK FACE.")]
        [Range(-1f, 3f)]
        [SerializeField] private float _heapCrestAheadM = 0.75f;

        [Tooltip("Angle of the LEADING face in degrees. STEEPER THAN REPOSE ON PURPOSE - it is under " +
                 "active compression from the blade. Set it equal to the repose angle for the A/B that " +
                 "shows what the steepness buys.")]
        [Range(-1f, 85f)]
        [SerializeField] private float _heapFrontAngleDeg = 65f;

        [Tooltip("Angle of the BACK face in degrees - the blade side, steeper still because the blade is " +
                 "holding it. Steeper also keeps the back toe close to the crest, which is what lets Heap " +
                 "Crest Ahead M stay small.")]
        [Range(-1f, 88f)]
        [SerializeField] private float _heapBackAngleDeg = 75f;

        [Tooltip("Slope limit in degrees that RELAX may use INSIDE the heap's footprint. Must be at least " +
                 "the steeper of the two faces or relax eats them; 80 leaves 5 degrees over the 75 degree " +
                 "back face.")]
        [Range(-1f, 89f)]
        [SerializeField] private float _heapGuardAngleDeg = 80f;

        [Tooltip("Relax guard on the heap's footprint: -1 leave alone, 0 OFF, 1 on.\n\n" +
                 "0 IS THE A/B AND IT IS THE MOST INFORMATIVE ONE IN THE VARIANT. With the guard off, " +
                 "relax flattens the deliberately-steep leading face toward the repose angle within a few " +
                 "frames, the erase's min() debits the difference to the field, and the pile visibly " +
                 "refuses to accumulate past a slab while the walls grow far too early. That is the " +
                 "failure the guard exists to prevent, and it is worth seeing once.")]
        [Range(-1, 1)]
        [SerializeField] private int _heapRelaxGuard = 1;

        // -------------------------------------------------------------------------------------------
        [Header("RELEASE - the windrow you leave behind, as a RATE and not a threshold")]
        [Tooltip("How far BEHIND the blade line each release cone sits, in metres, so the windrow is left " +
                 "in the wake rather than in the path. 0 is meaningful, so -1 is the sentinel.")]
        [Range(-1f, 4f)]
        [SerializeField] private float _spillBackM = 0.45f;

        [Tooltip("How far OUTSIDE the blade's half-width each release cone sits, in metres. The field takes " +
                 "max(this, radius) so the cone can never overlap the swept width, where it would be " +
                 "re-cut next frame. 0 is meaningful, so -1 is the sentinel.")]
        [Range(-1f, 4f)]
        [SerializeField] private float _spillOutM = 0.95f;

        [Tooltip("Radius of each release cone in metres. Small, because the release is a continuous dribble " +
                 "re-emitted every frame - the windrow's shape comes from the path plus relax, not from any " +
                 "one cone.")]
        [Range(-1f, 4f)]
        [SerializeField] private float _spillRadiusM = 0.85f;

        [Tooltip("Fill fraction at which snow STARTS curling off the blade ends. THE RELEASE IS A RATE, " +
                 "NOT A THRESHOLD: a real plough sheds continuously, so it rises smoothly from here to " +
                 "full at capacity and only then hands over to the hard cap. 0.35 means a blade a third " +
                 "full is already laying a thin windrow. 0 is MEANINGFUL (shed from empty), so -1 is the " +
                 "sentinel.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _releaseStartFill = 0.35f;

        [Tooltip("Fraction of the LEDGER released per SECOND at capacity, ramped to zero at Release Start " +
                 "Fill. THIS KNOB SETS THE EQUILIBRIUM FILL: the blade settles where the shed equals the " +
                 "intake. DERIVED (nothing here is measured), with the cruising speed in virgin snow solved " +
                 "from drive-vs-drag at each load:\n" +
                 "    rate/s   fill    mass      windrow\n" +
                 "    0.00     100%    1853 kg   0.47 m3/s   (v7's threshold behaviour, the A/B)\n" +
                 "    0.15      79%    1456 kg   0.55 m3/s   SHIPPED\n" +
                 "    0.30      67%    1237 kg   0.60 m3/s\n" +
                 "    1.50      51%     939 kg   0.69 m3/s\n" +
                 "0.15 keeps the accumulation story - the blade fills to about four fifths and the mass " +
                 "coupling still reaches 79% of its old range - while the windrow starts as a thin ribbon " +
                 "around half full and grows from there.\n\n" +
                 "The HARD cap above capacity is not this knob and cannot be switched off, so 0 restores " +
                 "v7's original threshold behaviour exactly - which is why 0 is meaningful and -1 is the " +
                 "sentinel.")]
        [Range(-1f, 3f)]
        [SerializeField] private float _releaseRatePerSec = 0.15f;

        // -------------------------------------------------------------------------------------------
        [Header("THE VERBS - blade down/up, blade angle, forward/reverse. THERE IS NO DUMP.")]
        [Tooltip("How far the blade is yawed in its LEFT and RIGHT states, in degrees. The cut footprint " +
                 "becomes a rectangle rotated by this and the cast rate is proportional to its sine.\n\n" +
                 "30 degrees is a working plough angle: half the travel speed goes into working snow along " +
                 "the face. Past about 45 the blade stops clearing its own width - the projected width is " +
                 "2.17 m at 30 degrees against the 2.30 m blade but only 1.80 m at 45 - and the lane " +
                 "narrows visibly. -1 leaves the field's own value alone.")]
        [Range(-1f, 60f)]
        [SerializeField] private float _bladeAngleDeg = 30f;

        [Tooltip("THE CAST'S GAIN. A residence-time argument, not a fudge: snow entering at the leading " +
                 "end of an angled blade is worked along the face at speed*sin(angle) and leaves at the " +
                 "trailing end, so castRate = pile * speed * sin(angle) / bladeWidth * this.\n\n" +
                 "THE EQUILIBRIUM SOLVES ITSELF and it IS the trade. Solving intake == discharge with the " +
                 "cruising speed taken from drive-vs-drag at each load (nothing here is measured):\n" +
                 "    eff    fill   mass      v      windrow\n" +
                 "    0.0     79%   1456 kg   1.97   0.55 m3/s   (straight, the A/B)\n" +
                 "    0.5     41%    754 kg   2.77   0.76 m3/s\n" +
                 "    1.0     21%    381 kg   3.53   0.97 m3/s   SHIPPED\n" +
                 "    2.0     10%    190 kg   4.10   1.13 m3/s\n" +
                 "An angled blade at 1.0 holds a FIFTH of what a straight one holds, windrows nearly twice " +
                 "as fast, and - carrying so much less - drives 80% faster. All three halves of the trade " +
                 "are legible at once. 0 disables the cast and leaves only the rotated cut, which is the " +
                 "A/B, so 0 is meaningful and -1 is the sentinel.")]
        [Range(-1f, 3f)]
        [SerializeField] private float _bladeCastEfficiency = 1f;

        [Tooltip("LATERAL SHOVE from the angled blade, in m/s2 per m3/s of cast rate. Casting to one side " +
                 "pushes the vehicle the OTHER way, so 'angled left pushes me right' is learnable.\n\n" +
                 "DERIVED AT 0.6, at the two cast rates the shipped couplings actually produce (nothing " +
                 "here is measured; the loads are the solved equilibria, not guesses):\n" +
                 "  * angling a blade at its straight equilibrium (4.85 m3 at 1.97 m/s) casts 2.08 m3/s, " +
                 "i.e. 1.25 m/s2 against a loaded grip of 3.4/s, i.e. 0.32 m/s of side-slip - 9.3 degrees, " +
                 "and about 76 cm of displacement over the 2.3 s the blade takes to drain into the windrow. " +
                 "Felt, clearly caused, and correctable in a 2.3 m lane.\n" +
                 "  * the angled equilibrium (1.27 m3 at 3.53 m/s) casts 0.98 m3/s, i.e. 0.59 m/s2 and " +
                 "9.5 cm/s, which is 1.5 degrees of slip - subtle, which is right: at steady state the " +
                 "blade is only holding a fifth of capacity and is not throwing much. The YAW knob below is " +
                 "what carries the cue there.\n\n" +
                 "1.0 puts 1.3 m of transient drift into a 2.3 m lane; 1.5 makes an angled pass a slide. " +
                 "0 is the A/B, so -1 is the sentinel.")]
        [Range(-1f, 10f)]
        [SerializeField] private float _castPushMps2PerM3s = 0.6f;

        [Tooltip("YAW MOMENT from the angled blade, in deg/s per m3/s of cast rate. THIS IS THE KNOB WITH " +
                 "THE KNOWN RISK: the reaction acts at the blade, ahead of the centre of mass, so it turns " +
                 "the vehicle as well as pushing it, and too much of it FIGHTS THE PLAYER'S INTENDED LINE " +
                 "in a narrow corridor. It is also the LOUDER of the two reactions, so it is what actually " +
                 "carries the cue.\n\n" +
                 "4.0 IS THE DOCUMENTED DEFAULT, derived against the loaded steering authority it has to " +
                 "share (nothing here is measured):\n" +
                 "  * angling a blade at its straight equilibrium casts 2.08 m3/s -> 8.3 deg/s against the " +
                 "25.7 deg/s of authority a 1,456 kg load has left, i.e. 32% of the available yaw, decaying " +
                 "over the 2.3 s drain. A deliberate pull that a held counter-steer beats.\n" +
                 "  * the angled equilibrium casts 0.98 m3/s -> 3.9 deg/s against 56.8 deg/s, i.e. 7% - a " +
                 "constant lean on the wheel rather than a fight.\n\n" +
                 "8 puts the transient at 65% of authority and 12 overpowers it outright, which in a narrow " +
                 "corridor is exactly the failure this note is about. 0 removes the yaw and leaves only the " +
                 "lateral shove, which is the A/B, so -1 is the sentinel.")]
        [Range(-1f, 20f)]
        [SerializeField] private float _castYawDegPerM3s = 4f;

        [Tooltip("Forward speed in m/s at and above which the blade's face is taken to be SUPPORTING the " +
                 "heap. Below HALF of it the blade lets go and the heap is deposited where it stands " +
                 "(2:1 hysteresis, so it cannot chatter).\n\n" +
                 "THE ATTACHMENT IS WHAT MAKES THE DEPOSIT DIRECTIONAL IN GENERAL rather than a reverse " +
                 "edge: a blade that merely stops has already let go, which is what makes the wedged-at-the-" +
                 "boundary stall escapable with no special case at all. 0.5 m/s is a crawl - well under the " +
                 "1.97 m/s a blade at its straight equilibrium cruises at in virgin snow, and under the " +
                 "3.53 m/s an angled one does - so ordinary ploughing never crosses it.")]
        [Range(-1f, 4f)]
        [SerializeField] private float _bladeAttachSpeedMps = 0.5f;

        [Tooltip("Degrees per second at which the LEADING FACE chases what the shove is asking for. The " +
                 "face stands at Heap Front Angle Deg while the vehicle is shoving and collapses toward the " +
                 "REPOSE angle when it stops or reverses. 25 deg/s covers the 65 -> 55 degree span in 0.4 s. " +
                 "0 freezes the face at the shoved angle and is the A/B, so -1 is the sentinel.")]
        [Range(-1f, 180f)]
        [SerializeField] private float _faceRelaxDegPerSec = 25f;

        [Tooltip("THE RE-ENGAGE COST in m/s2: the extra deceleration of BREAKING THE PILE LOOSE, at a full " +
                 "blade with the face re-steepening at its full rate. Brief by construction rather than by " +
                 "a timer - the face only climbs while it is behind the shove angle, so it lasts exactly as " +
                 "long as the re-steepen (0.4 s at the shipped rate) and is exactly zero in steady " +
                 "ploughing.\n\n" +
                 "4.0 m/s2 against the 9 m/s2 drive is a clear hesitation without being a stall. 0 removes " +
                 "it and is the A/B, so this takes a strictly negative sentinel.")]
        [Range(0f, 20f)]
        [SerializeField] private float _faceBreakLooseMps2 = 4f;

        // -------------------------------------------------------------------------------------------
        [Header("VEHICLE - THE DEPTH-DRIVEN SPEED. A cleared lane is a road.")]
        [Tooltip("Snow depth in metres at which the depth speed penalty SATURATES. 0.30 is the virgin " +
                 "slab, so a lane the blade has not touched is the worst case by definition and anything " +
                 "shallower is proportionally faster.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _depthSpeedSatDepthM = 0.30f;

        [Tooltip("Top speed as a FRACTION of the unloaded top speed at and past the saturation depth. 0.35 " +
                 "means virgin 30 cm snow holds an empty blade to 4.9 m/s of its 14, and a scraped lane of " +
                 "3 cm gives back 93% of it.\n\n" +
                 "MULTIPLICATIVE WITH AND SEPARATE FROM the carried-mass speed factor, deliberately: f(M) " +
                 "answers 'how heavy is my load' and this answers 'how deep is what I am driving through'. " +
                 "They are independent questions, so they multiply, and they are reported separately in the " +
                 "[V7] line so a slow vehicle can be diagnosed instead of guessed at. THIS IS THE REWARD " +
                 "THAT MAKES BLADE-UP TRANSIT WORTH ANYTHING - without it a cleared lane is just a picture.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _depthSpeedFloor = 0.35f;

        // -------------------------------------------------------------------------------------------
        [Header("Deliberate loss - this snow is NOT mass conserving")]
        [Tooltip("Fraction of what the blade cuts OUT OF THE TERRAIN that survives onto the pile; the rest " +
                 "is deleted and BOOKED. Charged on the terrain cut and not on the total removal - which " +
                 "in v7 is structural rather than a nicety, because the pile is lifted off the field and " +
                 "put back sixty times a second and charging the loss on the ledger would destroy it.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _conservedFraction = 0.40f;

        [Tooltip("Of the volume the deliberate loss destroys, the fraction squeezed out sideways as berms " +
                 "instead. Taken out of the LOSS rather than out of the pile, so the berms cost the growth " +
                 "rate nothing. 0 is meaningful.\n\n" +
                 "NOTE THE SIGN, because v6 measured it and it is counter-intuitive: berm = bermShare * " +
                 "(1 - conserved), so RAISING Conserved Fraction SHRINKS the berms. v6 also measured that " +
                 "Max Push Dist M is NOT the berm bottleneck - this is.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _bermShareOfLoss = 0.35f;

        [Tooltip("Seconds for the pile to shed 1/e of itself with no inflow. 0 DISABLES it and 0 IS THE " +
                 "SHIPPED VALUE. v7 already has a physical drain - the capacity overflow - so an " +
                 "exponential one would be a second, invisible answer to the same question. v5's leaky " +
                 "bucket, kept purely as an A/B.")]
        [Range(-1f, 60f)]
        [SerializeField] private float _pileShedTauSeconds = 0f;

        [Tooltip("How far the per-step cut budget is modulated by world-space noise, as a fraction. LOWER " +
                 "THAN V6'S 0.40 on purpose: v6 modulated a 4 cm skin, v7 modulates the whole 30 cm " +
                 "column, so 0.40 would leave up to 12 cm standing in patches inside the cleared width - " +
                 "which is exactly the stripeResidual defect v6 shipped with. 0.15 leaves at most 4.5 cm.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _cutNoiseAmp = 0.15f;

        [Range(-1f, 3f)]
        [SerializeField] private float _cutNoiseScaleM = 0.45f;

        // -------------------------------------------------------------------------------------------
        [Header("VEHICLE - body")]
        [Range(1f, 10f)]
        [SerializeField] private float _carLength = 4.0f;

        [Range(0.5f, 4f)]
        [SerializeField] private float _carWidth = 1.9f;

        [Range(0.3f, 3f)]
        [SerializeField] private float _carHeight = 1.0f;

        [Range(0f, 1f)]
        [SerializeField] private float _carRideHeightM = 0.12f;

        [SerializeField] private Color _carColor = new Color(0.92f, 0.86f, 0.30f, 1f);

        [Tooltip("Draw a visible plate at the blade line, and it is an INSTRUMENT rather than art: the " +
                 "heap's shape, its crest offset and the swept cut are all expressed relative to this " +
                 "line, so having it on screen is what lets a capture say whether the heap is sitting " +
                 "where the numbers say it is. 0 hides it.")]
        [Range(0f, 2f)]
        [SerializeField] private float _bladePlateHeightM = 0.9f;

        [SerializeField] private Color _bladeColor = new Color(0.82f, 0.24f, 0.20f, 1f);

        [Header("VEHICLE - longitudinal (BEFORE the mass factors)")]
        [Range(1f, 40f)]
        [SerializeField] private float _accelMps2 = 9f;

        [Range(2f, 40f)]
        [SerializeField] private float _topSpeedMps = 14f;

        [Range(1f, 60f)]
        [SerializeField] private float _brakeMps2 = 16f;

        [Range(0.5f, 20f)]
        [SerializeField] private float _reverseTopSpeedMps = 5f;

        [Range(0.5f, 30f)]
        [SerializeField] private float _reverseAccelMps2 = 6f;

        [Tooltip("Deceleration with no throttle and no brake, in m/s^2, BEFORE the mass coast factor. The " +
                 "single knob that decides whether releasing the key feels like lifting off or like " +
                 "hitting something.")]
        [Range(0f, 20f)]
        [SerializeField] private float _coastDecelMps2 = 3.5f;

        [Range(1f, 3f)]
        [SerializeField] private float _boostMultiplier = 1.6f;

        [Header("VEHICLE - steering")]
        [Range(10f, 400f)]
        [SerializeField] private float _steerRateDegPerSec = 130f;

        [Range(0.05f, 1f)]
        [SerializeField] private float _steerRateAtTopSpeed = 0.42f;

        [Range(1f, 40f)]
        [SerializeField] private float _steerFadeSpeedMps = 14f;

        [Range(0.05f, 5f)]
        [SerializeField] private float _steerMinSpeedMps = 1.2f;

        [Range(0.5f, 100f)]
        [SerializeField] private float _turnInPerSec = 4.5f;

        [Header("VEHICLE - grip and drift")]
        [Range(0.2f, 40f)]
        [SerializeField] private float _lateralGrip = 9f;

        [Range(0.05f, 1f)]
        [SerializeField] private float _gripAtTopSpeed = 0.62f;

        [Range(0.01f, 1f)]
        [SerializeField] private float _driftGripMultiplier = 0.30f;

        [Range(0.2f, 3f)]
        [SerializeField] private float _driftSteerMultiplier = 1.35f;

        // -------------------------------------------------------------------------------------------
        [Header("VEHICLE - WHAT THE PILE'S MASS DOES. This is the point of the variant.")]
        [Tooltip("THE REFERENCE MASS, in kilogrammes: the mass at which every factor below equals exactly " +
                 "what it says.\n\n" +
                 "RETUNED FROM V6'S 2400, AND THIS IS THE ONE HANDLING NUMBER V7 CHANGES. v6's load was an " +
                 "unbounded running total - a ball could reach 30 m3 / 9 tonnes - so 2,400 kg (8 m3) sat in " +
                 "the middle of a wide range. v7's load is BOUNDED BY THE HEAP'S CAPACITY: 6.17 m3, i.e. " +
                 "1,850 kg at 300 kg/m3, and the pile spends most of a run pinned near it because the " +
                 "overflow spills rather than accumulating. Leaving the reference at 2,400 would mean a " +
                 "FULL blade only reached 0.77 of the way to 'at reference' and the whole mass read would " +
                 "be a third of what the numbers say.\n\n" +
                 "1200 kg is 4 m3, 65% of capacity, reached in under two seconds - so the stated factors " +
                 "are what a HALF-FULL blade feels like. DERIVED at full capacity: accel 0.155, top speed " +
                 "0.347 (4.9 m/s from 14), brake 0.194, coast 0.072, turn-in 0.198, grip 0.394, turn radius " +
                 "x5.6. If the pile's mass range moves - a different density, a different capacity - THIS " +
                 "is the knob to move with it, and the rule of thumb is two thirds of capacity mass.")]
        [Range(100f, 40000f)]
        [SerializeField] private float _massRefKg = 1200f;

        [Tooltip("TOP SPEED at the reference mass, as a fraction. Listed FIRST but it is the LEAST " +
                 "important of the seven: on its own, slower reads as a bug.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _massSpeedFactor = 0.45f;

        [Tooltip("ACCELERATION at the reference mass, as a fraction. 0.22, i.e. it falls TWICE AS HARD as " +
                 "top speed, and that asymmetry is the whole trick: a vehicle whose top speed drops reads " +
                 "as slowed down, one that takes twice as long to REACH that speed reads as heavy. Weight " +
                 "is felt in the derivative, not in the value.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _massAccelFactor = 0.22f;

        [Tooltip("BRAKING at the reference mass. Falls with acceleration, for the same reason and to the " +
                 "same end: a heavy thing is hard to stop as well as hard to start.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _massBrakeFactor = 0.30f;

        [Tooltip("COASTING deceleration at the reference mass: MOMENTUM. It also divides the SNOW DRAG, " +
                 "which is not an extra: drag is a force and deceleration is F/m, so left undivided a " +
                 "loaded blade would be stopped by fresh snow FASTER than an empty one.\n\n" +
                 "0.12 IS INHERITED AS SOLVED, NOT PICKED. Coasting distance is v^2/(2a) with " +
                 "v = topSpeed * speedFactor and a = coastDecel * this, so the distance ratio against an " +
                 "empty vehicle is speedFactor^2 / this: the distance only GROWS while this is below " +
                 "0.45^2 = 0.2025, and v6 measured that an intuitive-looking 0.30 made the loaded vehicle " +
                 "coast 0.68x as FAR - the exact opposite of the brief. The coast TIME grows monotonically " +
                 "either way and that is what a player reads as momentum.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _massCoastFactor = 0.12f;

        [Tooltip("TURN-IN response at the reference mass: the wheel itself getting heavy. v5 measured this " +
                 "as the single most legible of its three couplings - speed and yaw rate are numbers a " +
                 "player infers, a wheel that takes half a second to answer is felt immediately.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _massTurnInFactor = 0.28f;

        [Tooltip("LATERAL GRIP at the reference mass: inertia. The nose comes round before the vehicle " +
                 "does and the turn washes wide.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _massGripFactor = 0.50f;

        [Tooltip("TURN RADIUS at the reference mass, as a MULTIPLE: at the SAME SPEED the same steering " +
                 "input scribes an arc this much wider.\n\n" +
                 "EXPRESSED AS A RADIUS AND NOT AS A STEER RATE, deliberately: the steering rate already " +
                 "RISES as speed falls, so a loaded vehicle - which is slower - collects a rate bonus that " +
                 "can more than cancel a naive steer factor, and v5 shipped a loaded plough that turned " +
                 "TIGHTER than an empty one because of it. R = v/omega, so dividing omega by this " +
                 "multiplies R by exactly this at whatever speed the vehicle is actually doing.\n\n" +
                 "4.0 is v6's measured value: at 2.4 the absolute arc at the vehicle's OWN top speed " +
                 "SHRANK as the load grew (14.7 m empty -> 9.0 m loaded) because the loaded vehicle is also " +
                 "much slower; 4.0 held it flat at 14.7 -> 15.0 while the 90-degree turn TIME went 1.65 -> " +
                 "3.75 s. 5.9 makes the absolute arc genuinely grow, at the cost of yaw authority.")]
        [Range(1f, 8f)]
        [SerializeField] private float _massTurnRadiusGrow = 4.0f;

        // -------------------------------------------------------------------------------------------
        [Header("VEHICLE - what the SNOW does to the handling")]
        [Range(0f, 30f)]
        [SerializeField] private float _snowDragBaseMps2 = 3.0f;

        [Range(0f, 4f)]
        [SerializeField] private float _snowDragPerSpeed = 0.45f;

        [Range(0.02f, 1f)]
        [SerializeField] private float _snowBiteDepthM = 0.30f;

        [Tooltip("How far AHEAD of the vehicle centre the resistance samples the snow. Roughly the blade's " +
                 "position, so entering deep snow bites as the BLADE reaches it rather than as the driver's " +
                 "seat does. LOWER THAN V6'S 2.6 because v7's blade mount is constant and closer: " +
                 "carLength/2 + gap = 2.1 m.")]
        [Range(0f, 8f)]
        [SerializeField] private float _snowSampleAheadM = 2.1f;

        [Range(0f, 1f)]
        [SerializeField] private float _snowRideFactor = 0.35f;

        [Header("VEHICLE - body reaction (every term neutral at 0)")]
        [Range(0f, 20f)]
        [SerializeField] private float _pitchAccelDeg = 3.5f;

        [Tooltip("Degrees the body pitches back at the reference mass, so the weight on the blade has a " +
                 "visible reaction as well as a felt one.")]
        [Range(0f, 30f)]
        [SerializeField] private float _massPitchDeg = 6f;

        [Range(0f, 20f)]
        [SerializeField] private float _rollLateralDeg = 5f;

        [Range(0.5f, 40f)]
        [SerializeField] private float _bodyResponsePerSec = 9f;

        [Range(0f, 1f)]
        [SerializeField] private float _wallSlideKeep = 0.85f;

        [Range(0f, 20f)]
        [SerializeField] private float _carBoundsMarginM = 3f;

        // -------------------------------------------------------------------------------------------
        [Header("AUTO DRIVE - the measurement course")]
        [Tooltip("Run the fixed course. DEFAULT ON, because the editor is driven over the CLI without OS " +
                 "focus and the keyboard never reaches the app: with this off a capture is a picture of a " +
                 "parked vehicle. A DRIVING key overrides it WHILE HELD and the course resumes the moment " +
                 "the key is released; a VERB key latches until the course laps.\n\n" +
                 "v7's course is ~77 s and FIVE of its twenty segments exist only to demonstrate the verb " +
                 "grammar, because a verb the autopilot never uses cannot be evaluated from a capture: " +
                 "CASTLEFT and CASTRIGHT plough with the blade angled and grow a windrow on one side; " +
                 "BACKOUT reverses with the blade down, which leaves the pile standing; TRANSIT raises the " +
                 "blade and runs back down that cleared lane at full speed; and WALLSTALL / WALLESCAPE " +
                 "deliberately wedge the vehicle against the stage boundary with a full blade and then get " +
                 "it out with blade-up reverse.")]
        [SerializeField] private bool _autoDrive = true;

        [Range(0.1f, 8f)]
        [SerializeField] private float _courseTimeScale = 1f;

        [SerializeField] private float _courseStartX = 10f;
        [SerializeField] private float _courseStartZ = -38f;

        [Range(-180f, 180f)]
        [SerializeField] private float _courseStartHeadingDeg = 0f;

        [Range(2f, 40f)]
        [SerializeField] private float _courseBoundsMarginM = 12f;

        [Tooltip("Refill the whole snow field every time the course laps. OFF by default, so lap 2 " +
                 "re-ploughs a stage that already has a lane, berms, two WALLS and a MOUND in it - which " +
                 "is the interesting case for free re-pickup and for the swept box union.")]
        [SerializeField] private bool _courseResetFieldOnLap = false;

        // -------------------------------------------------------------------------------------------
        [Header("Reachability - knobs an OPEN QUESTION needs from a CLI")]
        [Tooltip("Initial snow depth in metres. The full-depth pickup and the capacity arithmetic were " +
                 "derived against 0.30.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _startDepth = 0.30f;

        [Range(-1f, 0.3f)]
        [SerializeField] private float _scrapeHeight = 0f;

        [Tooltip("ABSOLUTE floor on the LEAK threshold, in litres. The effective tolerance is the LARGER " +
                 "of this and the ppm bound below.")]
        [Range(-1f, 50f)]
        [SerializeField] private float _massToleranceL = 3f;

        [Tooltip("RELATIVE part of the LEAK threshold, in ppm of the initial volume, and it FIXES A KNOWN " +
                 "V6 DEFECT: v6 compared the invariant against an absolute 3 L on a 3,960,000 L field " +
                 "while its own measured errors ran around 1 ppm, so the meter cried wolf on its own " +
                 "float32 arithmetic. 2 ppm is 7.9 L here. 0 is MEANINGFUL - it forces v6's absolute-only " +
                 "behaviour exactly - so this takes a strictly negative sentinel.")]
        [Range(-1f, 50f)]
        [SerializeField] private float _massTolerancePpm = 2f;

        [Range(-1, 8)]
        [SerializeField] private int _coarseDilate = 2;

        [Range(-1f, 8f)]
        [SerializeField] private float _coarseCellM = 1f;

        [Tooltip("Maximum distance one texel of BERM travels in a single step, in metres. v6 MEASURED that " +
                 "this is NOT the berm bottleneck - the berm SHARE is - so raise Berm Share Of Loss before " +
                 "reaching for this.")]
        [Range(-1f, 1.5f)]
        [SerializeField] private float _maxPushDistM = 0.40f;

        [Range(-1f, 0.2f)]
        [SerializeField] private float _pushMarginM = 0.06f;

        [Tooltip("Fraction of the footprint half-width at which the berm's escape is FULLY sideways. -1 " +
                 "leaves the field's own value alone. For v7 this is simply what a plough does: anything " +
                 "thrown FORWARD lands inside the heap's own footprint and is re-cut next frame, which is " +
                 "booked but buys nothing. 1.0 restores v5's blade blend and is the A/B.")]
        [Range(-1f, 1f)]
        [SerializeField] private float _sideSpillFullAt = 0.15f;

        [Header("Relax dispatch bounding")]
        [Range(-1, 1)]
        [SerializeField] private int _relaxWindowEnabled = 1;

        [Tooltip("How long a touched footprint keeps being relaxed after the vehicle has left it. A spill " +
                 "wall and a dumped mound are the tallest things in the variant and take the longest to " +
                 "reach repose; if the window expires first they freeze mid-slump.")]
        [Range(-1f, 8f)]
        [SerializeField] private float _relaxTrailSeconds = 2.5f;

        [Range(-1, 64)]
        [SerializeField] private int _relaxWindowPadTexels = 6;

        [Header("Raymarch steps-per-pixel probe (THE field-size instrument)")]
        [Range(-1, 1)]
        [SerializeField] private int _stepsProbeEnabled = 1;

        [Range(-1, 720)]
        [SerializeField] private int _stepsProbeHeight = 216;

        [Range(-1, 120)]
        [SerializeField] private int _stepsProbeInterval = 6;

        [Tooltip("Draw the FORWARD pass as a march-cost heat map instead of as snow. H toggles it. The " +
                 "mean and the p95 say HOW MUCH the march costs; only this says WHERE - and in v7 the " +
                 "question is specifically what the 1.6 m heap does to the rays that graze it.")]
        [Range(-1, 1)]
        [SerializeField] private int _stepsHeatView = 0;

        [Header("Reachability - more knobs an OPEN QUESTION needs from a CLI")]
        [Range(-1f, 0.05f)]
        [SerializeField] private float _rayMinSnowHeightM = 0.005f;

        [Range(-1f, 1f)]
        [SerializeField] private float _raySoftShadowStrength = 0.85f;

        [Range(-1f, 1f)]
        [SerializeField] private float _rayWrap = 0.45f;

        [Range(-1f, 0.5f)]
        [SerializeField] private float _rayFill = 0.10f;

        // -------------------------------------------------------------------------------------------
        [Header("Scene (a bright TOY palette)")]
        [Range(0f, 200f)]
        [SerializeField] private float _groundMarginM = 30f;

        [SerializeField] private Color _groundColor = new Color(0.42f, 0.36f, 0.40f, 1f);
        [SerializeField] private Color _skyColor = new Color(0.55f, 0.74f, 0.92f, 1f);
        [SerializeField] private Color _ambientLight = new Color(0.44f, 0.48f, 0.62f, 1f);

        [SerializeField] private bool _uncapFramerate = true;

        // -------------------------------------------------------------------------------------------
        [Header("Camera (chase, and it has to see PAST a 1.6 m heap)")]
        [Tooltip("RAISED from v6's 9. A 1.6 m heap 6 m wide sitting 0.65 m in front of a 1 m vehicle " +
                 "occludes the road ahead from a low chase camera - a problem v6's ball never had, because " +
                 "the ball was narrow and the camera looked over it. Pull back and up.")]
        [Range(1f, 40f)]
        [SerializeField] private float _cameraBack = 11f;

        [Tooltip("RAISED from v6's 4, for the occlusion reason above: at 6 m up over 11 m back the sight " +
                 "line clears a 1.9 m crest about 4 m ahead of the blade.")]
        [Range(0.3f, 40f)]
        [SerializeField] private float _cameraUp = 6f;

        [Range(0f, 30f)]
        [SerializeField] private float _cameraLookAhead = 6f;

        [Range(-2f, 5f)]
        [SerializeField] private float _cameraLookUp = 0.35f;

        [Tooltip("BIAS THE AIM POINT FROM THE VEHICLE TOWARD THE HEAP'S CREST, 0..1. 0 reproduces v6's " +
                 "neutral framing exactly and is the A/B.\n\n" +
                 "DEFAULT 0.30 RATHER THAN V6'S 0, which is a judgement rather than a measurement: a " +
                 "growing object that never changes its place in frame does not read as growing, and the " +
                 "brief is specifically about reading accumulation. 0.30 puts the heap near the centre of " +
                 "the lower frame while keeping the vehicle in shot.")]
        [Range(0f, 1f)]
        [SerializeField] private float _cameraPileBias = 0.30f;

        [Tooltip("Extra camera DISTANCE in metres per metre of HEAP HEIGHT. 0 is the neutral A/B.\n\n" +
                 "v6's equivalent tracked the BALL RADIUS, an unbounded quantity, so it needed a big gain. " +
                 "v7's heap height is bounded by the capacity (1.6 m), so 1.5 here only ever adds 2.4 m of " +
                 "distance - enough to keep the heap at roughly a constant fraction of frame height without " +
                 "the shot drifting. Costs march steps, so read the probe after changing it.")]
        [Range(0f, 8f)]
        [SerializeField] private float _cameraDistancePerHeight = 1.5f;

        [Tooltip("How much of the distance-per-height also goes into HEIGHT, as a fraction. 0.45 keeps the " +
                 "camera's pitch roughly constant as it pulls back, which is what 'the same shot, further " +
                 "away' means.")]
        [Range(0f, 1f)]
        [SerializeField] private float _cameraHeightShareOfDistance = 0.45f;

        [Range(20f, 90f)]
        [SerializeField] private float _cameraFov = 58f;

        [Range(0f, 40f)]
        [SerializeField] private float _cameraFovSpeedGain = 12f;

        [Range(0f, 1f)]
        [SerializeField] private float _cameraSmoothing = 0.08f;

        [Range(0f, 1f)]
        [SerializeField] private float _cameraAimSmoothing = 0.12f;

        [Range(0f, 1.5f)]
        [SerializeField] private float _cameraVelocityLeadSeconds = 0.25f;

        [Header("Console instrument")]
        [Tooltip("Seconds between [V7] Debug.Log lines. OnGUI is not captured by CLI screenshots or by " +
                 "camera-only game-view captures, so this console line is the readable instrument.")]
        [Range(0.1f, 10f)]
        [SerializeField] private float _logInterval = 1f;

        [SerializeField] private bool _logEnabled = true;

        [Range(1, 64)]
        [SerializeField] private int _residualSamples = 24;

        [Range(0.5f, 30f)]
        [SerializeField] private float _residualTrailSeconds = 6f;

        [Range(0f, 5f)]
        [SerializeField] private float _residualSkipSeconds = 0.8f;

        [Header("HUD")]
        [Range(10, 40)]
        [SerializeField] private int _hudFontSize = 18;

        [Range(0.01f, 1f)]
        [SerializeField] private float _hudSmoothing = 0.1f;

        [Range(1f, 60f)]
        [SerializeField] private float _hudRefreshHz = 10f;

        // ------------------------------------------------------------------ runtime
        public const string VariantName = "SnowGrainFakeV7 (THE PILE)";

        private SnowPileFieldV7 _field;
        private SnowRaymarchRendererV7 _raymarch;
        private SnowStepsProbeV7 _stepsProbe;

        private Transform _carRoot;      // yaw only. The blade's mount is derived from this and nothing else.
        private Transform _carBody;      // pitch and roll. Cosmetic, and provably so.
        private Transform _cameraRig;
        private Camera _camera;
        private Light _sun;

        private readonly SnowPileCarV7 _car = new SnowPileCarV7();
        private readonly SnowPileCourseV7 _course = new SnowPileCourseV7();
        private readonly SnowPileChaseCameraV7 _chase = new SnowPileChaseCameraV7();

        private bool _paused;
        private bool _manualOverride;
        private SnowCarInputV7 _lastInput;
        private SnowPileSweepV7 _lastSweep;
        private float _bladeOffsetM;

        // THE BLADE PLATE, kept so the marker can be YAWED with the blade angle. A marker that lied about
        // where the cut is would be worse than no marker: the whole reason the plate is drawn is so a
        // capture can say whether the heap is sitting where the arithmetic claims.
        private Transform _bladePlate;

        // ---- THE VERBS, and who owns them ---------------------------------------------------------
        //
        // The COURSE owns them until a human presses a verb key, at which point the manual state wins and
        // keeps winning until the course laps or R resets. That is deliberately different from the driving
        // keys, which win only WHILE HELD: a verb is a position, not a push, so "wins while held" would
        // mean the blade snapped back down the instant the key was released, and pressing 1 with the
        // autopilot driving would do nothing observable at all.
        private bool _manualVerbs;
        private bool _bladeUpManual;
        private int _bladeAngleManual;

        // What actually went to the vehicle and the field this frame, for the telemetry line.
        private bool _bladeUpApplied;
        private int _bladeAngleApplied;

        // The relax-guard A/B, so G can put it back.
        private int _heapRelaxGuardSaved = -1;

        private const int kPathCapacity = 2048;
        private readonly Vector2[] _pathPos = new Vector2[kPathCapacity];
        private readonly float[] _pathTime = new float[kPathCapacity];
        private int _pathHead;
        private int _pathCount;
        private float _pathClock;

        private float _frameMs;
        private float _simMs;
        private float _hudTimer;
        private float _logTimer;
        private bool _auditLogged;

        private int _originalVSync;
        private int _originalTargetFrameRate;
        private bool _framerateOverridden;

        private GpuTimeRecorder _gpuRecorder;

        private readonly StringBuilder _sb = new StringBuilder(3000);
        private readonly StringBuilder _log = new StringBuilder(2600);
        private string _hudText = "starting...";
        private GUIStyle _hudStyle;
        private Texture2D _hudBackdrop;

        // ------------------------------------------------------------------ setup

        // ------------------------------------------------------------------ 입력 (이식 시 갈아끼운 부분)
        //
        // 원본은 구 UnityEngine.Input 을 썼는데 이 프로젝트는 **Input System 전용**이라(Active Input
        // Handling = Input System Package) 그 API 를 읽는 순간 InvalidOperationException 이 나고,
        // 부트스트랩이 세팅 중간에 죽어 하늘만 남는다(실측). 그래서 키 조회만 새 시스템으로 옮겼다.
        private static bool KeyDown(UnityEngine.InputSystem.Key key)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[key].wasPressedThisFrame;
        }

        private static bool KeyHeld(UnityEngine.InputSystem.Key key)
        {
            var kb = UnityEngine.InputSystem.Keyboard.current;
            return kb != null && kb[key].isPressed;
        }

        private void Awake()
        {
            if (_uncapFramerate)
            {
                _originalVSync = QualitySettings.vSyncCount;
                _originalTargetFrameRate = Application.targetFrameRate;
                QualitySettings.vSyncCount = 0;
                Application.targetFrameRate = -1;
                _framerateOverridden = true;
            }

            RenderSettings.ambientMode = AmbientMode.Flat;
            RenderSettings.ambientLight = _ambientLight;

            _field = GetComponent<SnowPileFieldV7>() ?? gameObject.AddComponent<SnowPileFieldV7>();
            _raymarch = GetComponent<SnowRaymarchRendererV7>()
                     ?? gameObject.AddComponent<SnowRaymarchRendererV7>();
            _stepsProbe = GetComponent<SnowStepsProbeV7>() ?? gameObject.AddComponent<SnowStepsProbeV7>();

            // Before EnsureResources / Initialize, so the forwarded geometry actually sizes the render
            // textures and the forwarded march knobs size the proxy box on the first draw.
            //
            // The origin is legitimately negative, so it gets no leave-alone sentinel and is always
            // forwarded; every other argument keeps the usual "below its legal minimum means leave the
            // component's own value alone" contract.
            _field.ApplyGeometryOverrides(_patchOriginX, _patchOriginZ, _patchSizeX, _patchSizeZ,
                                          _cellSizeM, _mirrorDownsample);
            _field.ApplyRelaxOverrides(_relaxIterations, _reposeAngleDeg);
            _field.ApplyBladeOverrides(_pickupDepthM, _pileGrabPerPassM, _bladeWidthM, _bladeDepthM,
                                       _snowDensityKgPerM3, _bladeSegments);
            _field.ApplyHeapOverrides(_heapMaxHeightM, _heapWidthPerHeight, _heapMaxHalfWidthM,
                                      _heapCrestAheadM, _heapFrontAngleDeg, _heapBackAngleDeg,
                                      _heapGuardAngleDeg, _heapRelaxGuard);
            _field.ApplyReleaseOverrides(_spillBackM, _spillOutM, _spillRadiusM,
                                         _releaseStartFill, _releaseRatePerSec);
            _field.ApplyVerbOverrides(_bladeAngleDeg, _bladeCastEfficiency, _faceRelaxDegPerSec);
            _field.ApplyLossOverrides(_conservedFraction, _bermShareOfLoss, _pileShedTauSeconds,
                                      _cutNoiseAmp, _cutNoiseScaleM);
            _field.ApplyRenderDilationOverrides(_heightDilateRadius, _filletDilateRadius);
            _field.ApplyRelaxWindowOverrides(_relaxWindowEnabled, _relaxTrailSeconds,
                                             _relaxWindowPadTexels);
            _field.ApplyScenarioOverrides(_startDepth, _scrapeHeight, _massToleranceL, _massTolerancePpm,
                                          _coarseDilate, _coarseCellM, _maxPushDistM, _pushMarginM,
                                          _sideSpillFullAt);

            _raymarch.ApplyShadingOverrides(_rayMinSnowHeightM, _raySoftShadowStrength,
                                            _rayWrap, _rayFill);
            _raymarch.ApplyOverrides(_maxSteps, _stepM, _refineSteps, _lodDistanceM,
                                     _shadowMarchEnabled, _maxSnowHeightM,
                                     _detailAmpM, _detailFreq, _detailOctaves,
                                     _aoStrength, _softShadowSteps, _clumpBoost,
                                     _normalDetailAmpM, _normalDetailFreq, _normalDetailOctaves,
                                     _sheen);

            _raymarch.ApplyLumpOverrides(_lumpRadiusM, _lumpSpacingM, _lumpJitter, _lumpRadiusVary,
                                         _lumpGateDepthM, _lumpReliefM, _lumpSlopeStrength,
                                         _lumpFadeStartM, _lumpFadeRangeM);

            // BEFORE EnsureResources, which resets the field and bakes the lump lift once over the whole
            // texture. Without this the very first bake would run at radius 0 - no lobes at all until the
            // first simulation step, and a full re-bake on that step for nothing.
            _raymarch.PushLumpBakeParams(_field);

            _stepsProbe.ApplyOverrides(_stepsProbeEnabled, _stepsProbeHeight, _stepsProbeInterval,
                                       _stepsHeatView);

            _field.EnsureResources();
            if (!_field.Ready)
            {
                enabled = false;
                return;
            }

            BuildGround();
            BuildLight();
            BuildCar();
            BuildCamera();

            _bladeOffsetM = BladeOffset();
            _car.Reset(new Vector2(_courseStartX, _courseStartZ), _courseStartHeadingDeg, _bladeOffsetM);
            _course.Restart();
            ApplyCarTransforms();

            _lastSweep = BuildSweep();

            // Before the first UpdateUniforms, because the raymarcher folds the casual shape bounds into
            // the march's empty-space bias and a bias that is too small holes the surface exactly along the
            // edges the fillet was added to soften.
            _raymarch.ApplyCasualShapeBounds(_casualRoundM, _casualLoadLiftMaxM,
                                             _casualLoadExaggeration, _casualAmount);
            PushCasualStyle();

            _raymarch.Initialize(_field);

            // After the raymarcher, because the probe forwards that component's cached march uniforms onto
            // its own compute kernel. The kernel #includes the same SnowMarchCoreV7.hlsl the fragment
            // shader does, so it is the same march, not a replica of it.
            _stepsProbe.Initialize(_camera, _raymarch);

            _gpuRecorder = GpuTimeRecorder.Create();
        }

        /// <summary>
        /// The blade line's distance ahead of the vehicle centre. CONSTANT, unlike v6's ball mount, which
        /// had to grow or a growing ball would eat its own car. v7's blade is a fixed piece of the vehicle;
        /// what moves ahead of it as the pile grows is the HEAP'S CREST, and that offset lives on the field
        /// as Heap Crest Ahead M.
        /// </summary>
        private float BladeOffset()
        {
            return _carLength * 0.5f + Mathf.Max(0f, _bladeGapM);
        }

        /// <summary>
        /// The heap's crest position in world space, for the camera's aim point and for the console's
        /// height probes. Derived from the vehicle's yaw-only pose and the field's own crest offset, so it
        /// is the same point the emit kernel builds the profile around - not an approximation of it.
        /// </summary>
        private Vector3 HeapCrestWorld()
        {
            Vector2 f = _car.ForwardXZ;
            Vector2 p = _car.PositionXZ + f * (_bladeOffsetM + _field.HeapCrestAheadM);
            return new Vector3(p.x, _field.GroundY, p.y);
        }

        /// <summary>
        /// Pushes the whole casual palette as SHADER GLOBALS, once per frame. Globals rather than material
        /// properties because TWO consumers need them to agree exactly - the raymarcher and the probe's
        /// compute kernel - and v6 had a third (its ball shader) which is exactly the drift this guards
        /// against.
        /// </summary>
        private void PushCasualStyle()
        {
            var s = new SnowCasualStyleSettingsV7
            {
                Casual = _casualAmount,

                Bands = _casualBands,
                BandSoftness = _casualBandSoftness,
                Wrap = _casualWrap,

                LitColor = _casualLitColor,
                MidColor = _casualMidColor,
                ShadowColor = _casualShadowColor,

                AlbedoInfluence = _casualAlbedoInfluence,
                AoInfluence = _casualAoInfluence,
                Exposure = _casualExposure,

                RimStrength = _casualRimStrength,
                RimPower = _casualRimPower,
                RimColor = _casualRimColor,

                SparkleAmount = _casualSparkleAmount,
                SparkleScaleM = _casualSparkleScaleM,
                SparkleRadius = _casualSparkleRadius,
                SparkleThreshold = _casualSparkleThreshold,
                SparkleSpeed = _casualSparkleSpeed,
                SparkleColor = _casualSparkleColor,

                RoundM = _casualRoundM,
                RoundK = _casualRoundK,
                BandNormalWideM = _casualBandNormalWideM,
                LoadExaggeration = _casualLoadExaggeration,
                LoadLiftMaxM = _casualLoadLiftMaxM,

                // Measured against the field's own start depth rather than a hand-typed 0.30, so the
                // exaggeration keeps meaning "material the blade piled" if the start depth is changed.
                VirginDepthM = _field.StartDepth,

                LumpSquash = 0f,
            };

            SnowCasualStyleV7.Apply(s, Time.time);
        }

        private void OnDestroy()
        {
            _gpuRecorder.Dispose();
            if (_hudBackdrop != null) Destroy(_hudBackdrop);

            if (_framerateOverridden)
            {
                QualitySettings.vSyncCount = _originalVSync;
                Application.targetFrameRate = _originalTargetFrameRate;
                _framerateOverridden = false;
            }
        }

        private void BuildGround()
        {
            var go = GameObject.CreatePrimitive(PrimitiveType.Plane);
            go.name = "SnowFakeV7_Ground";
            go.transform.SetParent(transform, false);
            go.transform.localPosition = new Vector3(_field.PatchMin.x + _field.PatchSizeX * 0.5f,
                                                     _field.GroundY,
                                                     _field.PatchMin.y + _field.PatchSizeZ * 0.5f);

            // Unity's plane primitive is 10 x 10 m, scaled per axis because the stage is 120 x 110: a
            // uniform scale would either leave bare gaps along one axis or overdraw the other, and the
            // cleared LANE - which is the whole progression readout - is read against this surface.
            go.transform.localScale = new Vector3((_field.PatchSizeX + _groundMarginM * 2f) * 0.1f,
                                                  1f,
                                                  (_field.PatchSizeZ + _groundMarginM * 2f) * 0.1f);

            Destroy(go.GetComponent<Collider>());

            var mr = go.GetComponent<MeshRenderer>();
            mr.sharedMaterial = MakeLitMaterial("SnowFakeV7Ground", _groundColor, 0.9f);
            mr.shadowCastingMode = ShadowCastingMode.Off;
        }

        private void BuildLight()
        {
            var go = new GameObject("SnowFakeV7_Sun");
            go.transform.SetParent(transform, false);
            go.transform.rotation = Quaternion.Euler(48f, -34f, 0f);

            _sun = go.AddComponent<Light>();
            _sun.color = new Color(1f, 0.965f, 0.90f, 1f);
            _sun.type = LightType.Directional;
            _sun.intensity = 1.35f;

            // Off unless the shadow march is on. Without shadows on the light URP never schedules a
            // ShadowCaster pass, so the toggle would silently do nothing.
            _sun.shadows = _raymarch != null && _raymarch.ShadowMarchEnabled
                ? LightShadows.Soft : LightShadows.None;
        }

        /// <summary>
        /// Builds the vehicle as TWO nested transforms plus a BLADE PLATE.
        ///
        ///   root  position and YAW. The blade's mount is derived from this and from nothing else.
        ///   body  pitch and roll. Purely cosmetic - it carries the chassis and the plate, and nothing the
        ///         simulation reads.
        ///
        /// THE PLATE IS PARENTED TO THE BODY, NOT TO THE ROOT, AND THAT IS DELIBERATE: it is a marker, not
        /// the blade. The real blade is the swept box union the field cuts with, which is expressed in the
        /// root's yaw-only frame and is completely immune to the body's cosmetic pitch. Parenting the
        /// marker to the body means the marker LEANS with the vehicle, which is honest about it being
        /// decoration; if it were the cut, a pitching body would make the cut wander.
        /// </summary>
        private void BuildCar()
        {
            var root = new GameObject("SnowFakeV7_Car");
            root.transform.SetParent(transform, false);
            _carRoot = root.transform;

            var body = new GameObject("SnowFakeV7_CarBody");
            body.transform.SetParent(_carRoot, false);
            _carBody = body.transform;

            var chassis = GameObject.CreatePrimitive(PrimitiveType.Cube);
            chassis.name = "SnowFakeV7_CarChassis";
            chassis.transform.SetParent(_carBody, false);
            chassis.transform.localScale = new Vector3(_carWidth, _carHeight, _carLength);
            chassis.transform.localPosition = new Vector3(0f, _carHeight * 0.5f, 0f);
            Destroy(chassis.GetComponent<Collider>());

            var chassisMr = chassis.GetComponent<MeshRenderer>();
            chassisMr.sharedMaterial = MakeLitMaterial("SnowFakeV7Car", _carColor, 0.5f);
            chassisMr.shadowCastingMode = ShadowCastingMode.Off;

            // A small cab, so the vehicle has a readable front at a glance and the heading is never
            // ambiguous in a still capture.
            var cab = GameObject.CreatePrimitive(PrimitiveType.Cube);
            cab.name = "SnowFakeV7_CarCab";
            cab.transform.SetParent(_carBody, false);
            cab.transform.localScale = new Vector3(_carWidth * 0.82f, _carHeight * 0.62f, _carLength * 0.38f);
            cab.transform.localPosition = new Vector3(0f, _carHeight * 1.3f, -_carLength * 0.12f);
            Destroy(cab.GetComponent<Collider>());

            var cabMr = cab.GetComponent<MeshRenderer>();
            cabMr.sharedMaterial = MakeLitMaterial("SnowFakeV7CarCab", _carColor * 0.72f, 0.5f);
            cabMr.shadowCastingMode = ShadowCastingMode.Off;

            if (_bladePlateHeightM > 1e-4f)
            {
                var plate = GameObject.CreatePrimitive(PrimitiveType.Cube);
                plate.name = "SnowFakeV7_BladePlate";
                plate.transform.SetParent(_carBody, false);
                plate.transform.localScale = new Vector3(_bladeWidthM, _bladePlateHeightM,
                                                         Mathf.Max(0.05f, _bladeDepthM));
                plate.transform.localPosition = new Vector3(0f, _bladePlateHeightM * 0.35f,
                                                            BladeOffset());
                Destroy(plate.GetComponent<Collider>());

                // KEPT, so ApplyCarTransforms can yaw it with the blade angle and lift it with the blade.
                _bladePlate = plate.transform;

                var plateMr = plate.GetComponent<MeshRenderer>();
                plateMr.sharedMaterial = MakeLitMaterial("SnowFakeV7Blade", _bladeColor, 0.4f);
                plateMr.shadowCastingMode = ShadowCastingMode.Off;
            }
        }

        private void BuildCamera()
        {
            var go = new GameObject("SnowFakeV7_Camera");
            go.transform.SetParent(transform, false);

            _camera = go.AddComponent<Camera>();
            _camera.clearFlags = CameraClearFlags.SolidColor;
            _camera.backgroundColor = _skyColor;
            _camera.fieldOfView = _cameraFov;
            _camera.nearClipPlane = 0.05f;
            _camera.allowHDR = false;
            _camera.allowMSAA = false;
            _camera.farClipPlane = Mathf.Max(400f, (_field.PatchSizeX + _field.PatchSizeZ) * 2f);

            _cameraRig = go.transform;

            Vector3 carPos = CarWorldPos();
            Vector3 carFwd = CarWorldForward();
            _chase.Snap(carPos, carFwd, carPos, _cameraUp, _cameraBack, _cameraLookAhead, _cameraFov);
        }

        /// <summary>
        /// The vehicle's world position at GROUND level, not at chassis level. The ride height and the lift
        /// over snow live on the BODY transform, so the chase camera - which targets this - stays put
        /// instead of bobbing every time the vehicle crosses from slab onto a cleared lane.
        /// </summary>
        private Vector3 CarWorldPos()
        {
            Vector2 p = _car.PositionXZ;
            return new Vector3(p.x, _field.GroundY, p.y);
        }

        private Vector3 CarWorldForward()
        {
            Vector2 f = _car.ForwardXZ;
            return new Vector3(f.x, 0f, f.y);
        }

        private static Material MakeLitMaterial(string label, Color color, float roughness)
        {
            Shader shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");

            var mat = new Material(shader) { name = label };
            mat.SetColor("_BaseColor", color);
            mat.SetColor("_Color", color);
            mat.SetFloat("_Smoothness", 1f - Mathf.Clamp01(roughness));
            mat.SetFloat("_Metallic", 0f);
            return mat;
        }

        // ------------------------------------------------------------------ per frame
        private void Update()
        {
            if (_field == null || !_field.Ready) return;

            float dt = Mathf.Min(Time.deltaTime, _field.MaxStepSeconds);

            ReadToggleKeys();
            DriveCar(dt);

            // BEFORE the field step. Every frame, because this component is the ONLY one in the test scene,
            // so these fields are the only lump knobs reachable from outside while the editor is playing and
            // forwarding them once at startup would make every lump A/B a restart.
            //
            // The step is what runs the bake, and the bake has to encode against the SAME effective radius
            // the marcher will decode with this frame. Pushed after the step instead, a knob change would
            // land as one frame of lobes at the wrong height.
            _raymarch.ApplyLumpOverrides(_lumpRadiusM, _lumpSpacingM, _lumpJitter, _lumpRadiusVary,
                                         _lumpGateDepthM, _lumpReliefM, _lumpSlopeStrength,
                                         _lumpFadeStartM, _lumpFadeRangeM);
            _raymarch.PushLumpBakeParams(_field);

            // THE HEAP'S AND THE VERBS' OWN KNOBS, forwarded EVERY FRAME rather than once at startup, and
            // for a reason v6's ball knobs did not have: the heap's shape, its capacity, the relax guard,
            // the blade angle, the cast gain, the release rate and the face relax rate are the things the
            // parent agent will bracket, and every one of them is a live A/B that has to be reachable with
            // set_component_properties on THIS component while the editor is playing.
            _field.ApplyHeapOverrides(_heapMaxHeightM, _heapWidthPerHeight, _heapMaxHalfWidthM,
                                      _heapCrestAheadM, _heapFrontAngleDeg, _heapBackAngleDeg,
                                      _heapGuardAngleDeg, _heapRelaxGuard);
            _field.ApplyReleaseOverrides(_spillBackM, _spillOutM, _spillRadiusM,
                                         _releaseStartFill, _releaseRatePerSec);
            _field.ApplyVerbOverrides(_bladeAngleDeg, _bladeCastEfficiency, _faceRelaxDegPerSec);

            if (!_paused)
            {
                _lastSweep = BuildSweep();
                _field.Step(dt, _lastSweep);
            }

            // Every frame, because the sparkle phase advances with time and because the shape bounds have to
            // reach the raymarcher before it computes its march bias.
            _raymarch.ApplyCasualShapeBounds(_casualRoundM, _casualLoadLiftMaxM,
                                             _casualLoadExaggeration, _casualAmount);
            PushCasualStyle();

            // Uniforms have to be pushed before the camera renders, which is why this is in Update and not
            // in a callback: the height texture ping-pongs every step and the march's top plane moves every
            // step.
            _raymarch.UpdateUniforms(dt);

            StepCamera(dt);

            // Strictly after StepCamera AND after _raymarch.UpdateUniforms: the probe uses the camera's
            // current pose and the renderer's cached uniforms from THIS frame. A probe taken with last
            // frame's camera measures last frame's geometry against this frame's field.
            _stepsProbe.PushHeatMode();
            _stepsProbe.Tick();

            // Console instrument first: it is the primary readout when the editor is driven over the CLI, so
            // it must never be starved by anything in the on-screen HUD path.
            EmitConsoleLine(dt);
            UpdateHud(dt);
        }

        private void ReadToggleKeys()
        {
            // C is the look A/B: it snaps the master between full casual and the realistic image.
            if (KeyDown(UnityEngine.InputSystem.Key.C)) _casualAmount = (_casualAmount > 0.5f) ? 0f : 1f;

            if (KeyDown(UnityEngine.InputSystem.Key.B))
            {
                _casualBands = Mathf.Round(_casualBands) + 1f;
                if (_casualBands > 4.5f) _casualBands = 2f;
            }

            // G is THE RELAX GUARD A/B, and it replaces v6's L (which toggled the ball's lump form - a
            // shader v7 does not have). It is the most informative switch in the variant: with the guard off
            // the heap's deliberately-steep leading face slumps toward the repose angle within a few frames
            // and the pile stops accumulating, which is the failure the guard exists to prevent.
            if (KeyDown(UnityEngine.InputSystem.Key.G))
            {
                if (_heapRelaxGuardSaved < 0)
                {
                    _heapRelaxGuardSaved = _heapRelaxGuard;
                    _heapRelaxGuard = 0;
                }
                else
                {
                    _heapRelaxGuard = _heapRelaxGuardSaved;
                    _heapRelaxGuardSaved = -1;
                }
            }

            // ---- THE VERB KEYS -----------------------------------------------------------------------
            //
            // E toggles the BLADE between down and up. 1 / 2 / 3 select the three ANGLE states. The third
            // verb needs no key at all: it is the throttle's sign, because reversing with the blade down IS
            // the deposit.
            //
            // A verb key LATCHES manual verb control until the course laps or R resets, unlike the driving
            // keys which win only while held. A verb is a position rather than a push, so "wins while held"
            // would snap the blade back down the instant the key came up, and pressing 1 while the autopilot
            // drives would do nothing observable at all.
            if (KeyDown(UnityEngine.InputSystem.Key.E))
            {
                if (!_manualVerbs)
                {
                    // Adopt whatever is on screen right now, so the first press toggles from what the
                    // player can see rather than from a stale manual value.
                    _bladeUpManual = _bladeUpApplied;
                    _bladeAngleManual = _bladeAngleApplied;
                }
                _manualVerbs = true;
                _bladeUpManual = !_bladeUpManual;
            }

            if (KeyDown(UnityEngine.InputSystem.Key.Digit1) || KeyDown(UnityEngine.InputSystem.Key.Digit2)
                                                 || KeyDown(UnityEngine.InputSystem.Key.Digit3))
            {
                if (!_manualVerbs) _bladeUpManual = _bladeUpApplied;
                _manualVerbs = true;
                _bladeAngleManual = KeyDown(UnityEngine.InputSystem.Key.Digit1) ? -1
                                  : (KeyDown(UnityEngine.InputSystem.Key.Digit3) ? 1 : 0);
            }

            if (KeyDown(UnityEngine.InputSystem.Key.Comma)) _field.RelaxIterations = _field.RelaxIterations - 1;
            if (KeyDown(UnityEngine.InputSystem.Key.Period)) _field.RelaxIterations = _field.RelaxIterations + 1;

            if (KeyDown(UnityEngine.InputSystem.Key.R))
            {
                _field.ResetField();
                _bladeOffsetM = BladeOffset();
                _car.Reset(new Vector2(_courseStartX, _courseStartZ), _courseStartHeadingDeg,
                           _bladeOffsetM);
                _course.Restart();
                _pathHead = 0;
                _pathCount = 0;

                // R hands the verbs back to the course, because R means "measure this again from the top"
                // and a latched manual blade would silently make that a different measurement.
                _manualVerbs = false;
                _bladeUpManual = false;
                _bladeAngleManual = 0;
            }

            if (KeyDown(UnityEngine.InputSystem.Key.P)) _paused = !_paused;

            if (KeyDown(UnityEngine.InputSystem.Key.H))
            {
                _stepsHeatView = (_stepsHeatView > 0) ? 0 : 1;
                _stepsProbe.ApplyOverrides(-1, -1, -1, _stepsHeatView);
            }
        }

        /// <summary>
        /// Reads the driver, runs the vehicle, and puts the result on the transforms.
        ///
        /// THE DRIVING KEYS WIN WHILE HELD and the course resumes the instant they are released, rather than
        /// latching manual mode: the autopilot is the measurement, and a human at the keyboard is an
        /// interruption to it, not a mode change.
        ///
        /// THE VERB KEYS LATCH, and that difference is deliberate - see ReadToggleKeys. A verb is a
        /// position, not a push, so it has to survive the key coming up; and once a human has chosen a blade
        /// position, having the course silently take it back on the next segment boundary would be worse than
        /// either rule. The latch ends when the course laps or R resets.
        /// </summary>
        private void DriveCar(float dt)
        {
            // Legacy input, because the project's activeInputHandler is 2 (Both) and the new system is not
            // set up in this scene.
            float throttle = 0f;
            if (KeyHeld(UnityEngine.InputSystem.Key.W) || KeyHeld(UnityEngine.InputSystem.Key.UpArrow)) throttle += 1f;
            if (KeyHeld(UnityEngine.InputSystem.Key.S) || KeyHeld(UnityEngine.InputSystem.Key.DownArrow)) throttle -= 1f;

            float steer = 0f;
            if (KeyHeld(UnityEngine.InputSystem.Key.D) || KeyHeld(UnityEngine.InputSystem.Key.RightArrow)) steer += 1f;
            if (KeyHeld(UnityEngine.InputSystem.Key.A) || KeyHeld(UnityEngine.InputSystem.Key.LeftArrow)) steer -= 1f;

            bool drift = KeyHeld(UnityEngine.InputSystem.Key.Space);
            bool boost = KeyHeld(UnityEngine.InputSystem.Key.LeftShift) || KeyHeld(UnityEngine.InputSystem.Key.RightShift);

            _manualOverride = throttle != 0f || steer != 0f || drift || boost;

            SnowCarInputV7 input;
            if (_manualOverride || !_autoDrive)
            {
                input = new SnowCarInputV7
                {
                    Throttle = throttle,
                    Steer = steer,
                    Drift = drift,
                    Boost = boost,
                };
            }
            else if (_paused)
            {
                // Frozen, not advanced. Pausing to read the console line must not silently move the course
                // on underneath the numbers being read.
                input = new SnowCarInputV7();
            }
            else
            {
                input = _course.Step(dt, _courseTimeScale, _car, _field, _courseBoundsMarginM,
                                     out bool lapped);

                if (lapped)
                {
                    _bladeOffsetM = BladeOffset();
                    _car.Reset(new Vector2(_courseStartX, _courseStartZ), _courseStartHeadingDeg,
                               _bladeOffsetM);
                    if (_courseResetFieldOnLap) _field.ResetField();

                    // A lap boundary hands the verbs back to the course for the same reason R does: the next
                    // lap is meant to be the same measurement as this one.
                    _manualVerbs = false;
                }
            }

            // ---- THE VERBS, resolved from whoever owns them --------------------------------------------
            //
            // The course's segment always publishes its verbs, even on a frame where the driving keys have
            // taken the throttle over, so a human steering by hand still gets the segment's blade position
            // unless they have chosen one themselves. That is the least surprising of the three possible
            // rules: the blade only ever changes because the course said so or because you said so.
            if (_manualVerbs)
            {
                input.BladeUp = _bladeUpManual;
                input.BladeAngle = _bladeAngleManual;
            }
            else if (_autoDrive && !_paused)
            {
                input.BladeUp = _course.SegmentBladeUp;
                input.BladeAngle = _course.SegmentAngle;
            }

            _bladeUpApplied = input.BladeUp;
            _bladeAngleApplied = (input.BladeAngle > 0) ? 1 : ((input.BladeAngle < 0) ? -1 : 0);

            _lastInput = input;

            if (!_paused)
            {
                _bladeOffsetM = BladeOffset();
                _car.Step(dt, BuildCarSettings(), input, _field, _bladeOffsetM);
                NotePath(dt);
            }

            ApplyCarTransforms();
        }

        /// <summary>Gathers this frame's handling knobs. Nothing is cached, so every knob is live.</summary>
        private SnowCarSettingsV7 BuildCarSettings()
        {
            return new SnowCarSettingsV7
            {
                AccelMps2 = _accelMps2,
                TopSpeedMps = _topSpeedMps,
                BrakeMps2 = _brakeMps2,
                ReverseTopSpeedMps = _reverseTopSpeedMps,
                ReverseAccelMps2 = _reverseAccelMps2,
                CoastDecelMps2 = _coastDecelMps2,
                BoostMultiplier = _boostMultiplier,

                SteerRateDegPerSec = _steerRateDegPerSec,
                SteerRateAtTopSpeed = _steerRateAtTopSpeed,
                SteerFadeSpeedMps = _steerFadeSpeedMps,
                SteerMinSpeedMps = _steerMinSpeedMps,
                TurnInPerSec = _turnInPerSec,

                LateralGrip = _lateralGrip,
                GripAtTopSpeed = _gripAtTopSpeed,
                DriftGripMultiplier = _driftGripMultiplier,
                DriftSteerMultiplier = _driftSteerMultiplier,

                PileMassKg = _field.PileMassKg,
                MassRefKg = _massRefKg,
                SpeedFactorAtRef = _massSpeedFactor,
                AccelFactorAtRef = _massAccelFactor,
                BrakeFactorAtRef = _massBrakeFactor,
                CoastFactorAtRef = _massCoastFactor,
                TurnInFactorAtRef = _massTurnInFactor,
                GripFactorAtRef = _massGripFactor,
                TurnRadiusGrowAtRef = _massTurnRadiusGrow,

                SnowDragBaseMps2 = _snowDragBaseMps2,
                SnowDragPerSpeed = _snowDragPerSpeed,
                SnowBiteDepthM = _snowBiteDepthM,
                SnowSampleAheadM = _snowSampleAheadM,
                SnowRideFactor = _snowRideFactor,

                DepthSpeedSatDepthM = _depthSpeedSatDepthM,
                DepthSpeedFloor = _depthSpeedFloor,

                BladeAttachSpeedMps = Mathf.Max(0.01f, _bladeAttachSpeedMps),
                CastPushMps2PerM3s = Mathf.Max(0f, _castPushMps2PerM3s),
                CastYawDegPerM3s = Mathf.Max(0f, _castYawDegPerM3s),
                FaceBreakLooseMps2 = Mathf.Max(0f, _faceBreakLooseMps2),

                // LAST STEP'S MEASUREMENTS, from the field, for the two reactions the vehicle applies. One
                // frame late by construction - the vehicle has to integrate before the field can be told
                // where the blade went - and see SnowCarSettingsV7 for why that is the right trade rather
                // than a second CPU-side estimate of both.
                CastRateM3PerSec = (float)_field.CastRateM3PerSec,
                FaceSteepen01 = _field.FaceSteepen01,

                PitchAccelDeg = _pitchAccelDeg,
                PitchLoadDeg = _massPitchDeg,
                RollLateralDeg = _rollLateralDeg,
                BodyResponsePerSec = _bodyResponsePerSec,

                RideHeightM = _carRideHeightM,
                BoundsMarginM = _carBoundsMarginM,
                WallSlideKeep = _wallSlideKeep,
            };
        }

        private void ApplyCarTransforms()
        {
            _carRoot.position = CarWorldPos();
            _carRoot.rotation = Quaternion.Euler(0f, _car.HeadingDeg, 0f);

            // The chassis rides; the root does not. See CarWorldPos.
            _carBody.localPosition = new Vector3(0f, _car.RideY, 0f);
            _carBody.localRotation = Quaternion.Euler(_car.BodyPitchDeg, 0f, _car.BodyRollDeg);

            // THE PLATE YAWS WITH THE BLADE ANGLE AND LIFTS WITH THE BLADE, because it is an INSTRUMENT: a
            // marker that stayed square while the cut rotated, or stayed down while the blade was up, would
            // make every capture of the two new verbs a lie about what the simulation was doing.
            if (_bladePlate != null)
            {
                _bladePlate.localRotation =
                    Quaternion.Euler(0f, _bladeAngleApplied * Mathf.Max(0f, _bladeAngleDeg), 0f);

                Vector3 p = _bladePlate.localPosition;
                p.y = _bladePlateHeightM * 0.35f
                    + (_bladeUpApplied ? _bladePlateHeightM * 0.75f : 0f);
                _bladePlate.localPosition = p;
            }
        }

        private void NotePath(float dt)
        {
            _pathClock += dt;
            _pathPos[_pathHead] = _car.PositionXZ;
            _pathTime[_pathHead] = _pathClock;
            _pathHead = (_pathHead + 1) % kPathCapacity;
            if (_pathCount < kPathCapacity) _pathCount++;
        }

        /// <summary>
        /// The blade's motion over this frame, as a START and an END pose. The field turns it into a swept
        /// box union; see SnowPileSweepV7.
        ///
        /// There is no clamp on the displacement and v5 had one. It is not needed: the swept volume is a box
        /// whose cross-section is fixed, so a long step is simply a long box, which is exactly what a long
        /// step swept. The field's own Max Step Seconds already bounds it to 0.7 m at top speed.
        /// </summary>
        private SnowPileSweepV7 BuildSweep()
        {
            return new SnowPileSweepV7
            {
                StartCenter = _car.BladeStartXZ,
                StartForward = _car.BladeStartForward,
                EndCenter = _car.BladeEndXZ,
                EndForward = _car.BladeEndForward,
                SignedSpeed = _car.ForwardSpeed,
                Segments = Mathf.Clamp(_bladeSegments < 1 ? _field.BladeSegments : _bladeSegments,
                                       1, SnowPileFieldV7.MaxBladeSegments),

                // THE VERBS, straight off the vehicle that resolved them. The attachment in particular has
                // to come from there and not from the input: it is hysteretic on the vehicle's own signed
                // speed, so it is state the vehicle owns, and reconstructing it here would be a second
                // opinion about whether the blade is carrying anything.
                BladeDown = _car.BladeDown,
                AngleState = _car.BladeAngleState,
                BladeAttached = _car.BladeAttached,
                Push01 = Mathf.Clamp01(_lastInput.Throttle),
            };
        }

        private void StepCamera(float dt)
        {
            Vector3 carPos = CarWorldPos();
            Vector3 carFwd = CarWorldForward();
            Vector3 carVel = new Vector3(_car.VelocityXZ.x, 0f, _car.VelocityXZ.y);

            // THE HEAP IS THE SUBJECT, and unlike v6 the thing tracked is a HEIGHT rather than a radius:
            // the pile is bounded by its capacity, so the pull-back has a bounded range and the shot cannot
            // drift off into the distance however long the run is.
            float h = _field.PileHeightM;

            Vector3 crest = HeapCrestWorld();
            Vector3 aimBase = Vector3.Lerp(carPos, crest, Mathf.Clamp01(_cameraPileBias));

            float extra = h * Mathf.Max(0f, _cameraDistancePerHeight);
            float back = _cameraBack + extra;
            float up = _cameraUp + extra * Mathf.Clamp01(_cameraHeightShareOfDistance);

            float speed01 = Mathf.Clamp01(_car.Speed / Mathf.Max(0.1f, _topSpeedMps));

            _chase.Step(_cameraRig, _camera, dt, carPos, carFwd, carVel, aimBase,
                        up, back, _cameraLookAhead, _cameraLookUp,
                        _cameraFov, _cameraFovSpeedGain,
                        _cameraSmoothing, _cameraAimSmoothing, _cameraVelocityLeadSeconds, speed01);
        }

        /// <summary>
        /// Mean snow depth along the vehicle's OWN RECENT PATH, plus the worst of the samples. A MEAN, not a
        /// probe: the cut noise cell is 45 cm against a 25 cm mirror cell, so one sample reads anywhere from
        /// bare ground to a few centimetres.
        ///
        /// IN V7 THIS IS THE FULL-DEPTH ACCEPTANCE READOUT. v6 shipped with worst = 30 cm - untouched
        /// patches inside the nominal cleared width, an artefact of a disc footprint plus a 40% cut-noise
        /// modulation of a 4 cm skin. v7's footprint is a rectangle that covers uniformly and its noise
        /// modulates 15% of the whole column, so the DERIVED expectation is mean near 0 and worst at or
        /// under 4.5 cm. If worst comes back near 30 again, the footprint is not covering.
        /// </summary>
        private float PathResidual(out float worstM)
        {
            worstM = 0f;
            if (_pathCount <= 0) return 0f;

            float newest = _pathClock - Mathf.Max(0f, _residualSkipSeconds);
            float oldest = newest - Mathf.Max(0.1f, _residualTrailSeconds);

            int n = Mathf.Clamp(_residualSamples, 1, 64);

            float sum = 0f;
            int taken = 0;

            for (int i = 0; i < n; ++i)
            {
                float t = (n == 1) ? 0.5f : (float)i / (n - 1);
                float want = Mathf.Lerp(oldest, newest, t);

                if (!SamplePathAt(want, out Vector2 p)) continue;

                float hh = _field.HeightAt(new Vector3(p.x, 0f, p.y));
                sum += hh;
                taken++;
                if (hh > worstM) worstM = hh;
            }

            return (taken > 0) ? sum / taken : 0f;
        }

        private bool SamplePathAt(float time, out Vector2 pos)
        {
            pos = Vector2.zero;

            for (int i = 0; i < _pathCount; ++i)
            {
                int idx = (_pathHead - 1 - i + kPathCapacity * 2) % kPathCapacity;
                if (_pathTime[idx] <= time)
                {
                    pos = _pathPos[idx];
                    return true;
                }
            }
            return false;
        }

        // ------------------------------------------------------------------ instrument
        private void UpdateHud(float dt)
        {
            float a = Mathf.Clamp01(_hudSmoothing);
            _frameMs = Mathf.Lerp(_frameMs, Time.unscaledDeltaTime * 1000f, a);
            _simMs = Mathf.Lerp(_simMs, (float)_field.LastStepMilliseconds, a);

            _hudTimer -= dt;
            if (_hudTimer > 0f) return;
            _hudTimer = 1f / Mathf.Max(1f, _hudRefreshHz);

            float residual = PathResidual(out float worst);

            _sb.Clear();
            _sb.Append("variant   ").Append(VariantName).Append('\n');

            _sb.Append("PILE      V ").Append(_field.PileVolumeM3.ToString("F3")).Append(" m3")
               .Append("   h ").Append(_field.PileHeightM.ToString("F2")).Append(" m")
               .Append("   crest ").Append(_field.PileCrestWidthM.ToString("F2")).Append(" m")
               .Append("   footprint ").Append(_field.PileFootprintWidthM.ToString("F2")).Append(" m")
               .Append("   mass ").Append(_field.PileMassKg.ToString("F0")).Append(" kg")
               .Append("   fill ").Append((_field.PileFill01 * 100f).ToString("F0")).Append('%')
               .Append(_field.Overflowing ? "  OVERFLOWING"
                       : (_field.PickupRateM3PerSec > 1e-4 ? "  ACCUMULATING" : "  idle")).Append('\n');

            _sb.Append("handling  speed x").Append(_car.MassSpeedFactor.ToString("F2"))
               .Append("   accel x").Append(_car.MassAccelFactor.ToString("F2"))
               .Append("   brake x").Append(_car.MassBrakeFactor.ToString("F2"))
               .Append("   coast x").Append(_car.MassCoastFactor.ToString("F2"))
               .Append("   turnIn x").Append(_car.MassTurnInFactor.ToString("F2"))
               .Append("   grip x").Append(_car.MassGripFactor.ToString("F2"))
               .Append("   turnR x").Append(_car.MassTurnRadiusFactor.ToString("F2"))
               .Append(" (now ").Append(_car.TurnRadiusM.ToString("F1")).Append(" m)").Append('\n');

            _sb.Append("heap      cap ").Append(_field.PileCapacityM3.ToString("F2")).Append(" m3")
               .Append("   erase ").Append(_field.HeapReclaimedLastStep.ToString("F4")).Append(" m3")
               .Append("   emit ").Append(_field.HeapEmittedLastStep.ToString("F4")).Append(" m3")
               .Append("   placed ").Append(_field.HeapPlacedM3.ToString("F3")).Append(" m3")
               .Append("   spill ").Append(_field.ReleaseRateM3PerSec.ToString("F3")).Append(" m3/s")
               .Append("   walls ").Append(_field.ReleaseTotalM3.ToString("F2")).Append(" m3")
               .Append("   guard ").Append(_field.HeapRelaxGuard ? "on" : "OFF").Append('\n');

            _sb.Append("VERBS     blade ").Append(_field.BladeDownLastStep ? "DOWN" : "UP")
               .Append("   angle ").Append(AngleWord(_field.BladeAngleStateLastStep))
               .Append(" (").Append(_field.BladeAngleAppliedDeg.ToString("F0")).Append(" deg)")
               .Append("   attached ").Append(_field.BladeAttachedLastStep ? "yes" : "NO")
               .Append("   face ").Append(_field.FaceAngleDeg.ToString("F1")).Append(" deg")
               .Append("   cast ").Append(_field.CastRateM3PerSec.ToString("F3")).Append(" m3/s")
               .Append("   release ").Append(_field.ReleaseRateCommandedM3PerSec.ToString("F3")).Append(" m3/s")
               .Append("   depthSpeed x").Append(_car.DepthSpeedFactor.ToString("F2"))
               .Append(_field.DepositRanLastStep ? "   DEPOSITING" : "")
               .Append("   deposits ").Append(_field.DepositCount).Append('\n');

            _sb.Append("drive     ").Append(_manualOverride ? "MANUAL" : (_autoDrive ? "AUTO" : "idle"))
               .Append("  seg ").Append(_course.SegmentName)
               .Append(" (").Append(_course.SegmentIndex + 1).Append('/').Append(_course.SegmentCount)
               .Append(", ").Append(_course.SegmentRemaining.ToString("F1")).Append("s left)")
               .Append("  lap ").Append(_course.Lap)
               .Append(_paused ? "   [SIM PAUSED]" : "").Append('\n');

            _sb.Append("vehicle   v ").Append(_car.ForwardSpeed.ToString("F2")).Append(" m/s")
               .Append(" (").Append((_car.ForwardSpeed * 3.6f).ToString("F0")).Append(" km/h)")
               .Append("   steer ").Append(_lastInput.Steer.ToString("F2"))
               .Append("->").Append(_car.SteerApplied.ToString("F2"))
               .Append("   yaw ").Append(_car.YawRateDegPerSec.ToString("F0")).Append(" deg/s")
               .Append("   slip ").Append(_car.SlipAngleDeg.ToString("F1")).Append(" deg")
               .Append(_car.DriftActive ? "   DRIFT" : "").Append('\n');

            _sb.Append("snow feel depth ahead ").Append((_car.SnowDepthAheadM * 100f).ToString("F1")).Append(" cm")
               .Append(" under ").Append((_car.SnowDepthUnderM * 100f).ToString("F1")).Append(" cm")
               .Append("   drag ").Append(_car.SnowDragMps2.ToString("F1")).Append(" m/s2")
               .Append("   pitch ").Append(_car.BodyPitchDeg.ToString("F1"))
               .Append(" roll ").Append(_car.BodyRollDeg.ToString("F1")).Append(" deg").Append('\n');

            _sb.Append("casual    amount ").Append(_casualAmount.ToString("F2"))
               .Append(_casualAmount < 0.001f ? " (REALISTIC)" : "")
               .Append("   bands ").Append(_casualBands.ToString("F0"))
               .Append("   round ").Append((_casualRoundM * 100f).ToString("F1")).Append("cm")
               .Append("   lump ").Append(_raymarch.LumpEnabled ? "on" : "off")
               .Append(" r ").Append(_raymarch.LumpRadiusM.ToString("F2")).Append("m").Append('\n');

            _sb.Append("march     ").Append(_stepsProbe.StatusText).Append("   ");
            if (_stepsProbe.HasMeasurement)
            {
                _sb.Append("mean ").Append(_stepsProbe.MeanSteps.ToString("F1"))
                   .Append("  p95 ").Append(_stepsProbe.P95Steps)
                   .Append("  max ").Append(_stepsProbe.MaxSteps)
                   .Append("  exhausted ").Append(_stepsProbe.ExhaustedPercent.ToString("F2")).Append('%')
                   .Append("  of ").Append(_raymarch.MaxSteps).Append(" budget");
            }
            _sb.Append(_stepsProbe.HeatView ? "   [HEAT VIEW]" : "").Append('\n');

            _sb.Append("frame ms  ").Append(_frameMs.ToString("F2"))
               .Append("   ").Append((_frameMs > 0.001f ? 1000f / _frameMs : 0f).ToString("F0")).Append(" fps");
            if (_gpuRecorder.Valid)
                _sb.Append("   gpu ms ").Append(_gpuRecorder.Milliseconds.ToString("F2"));
            _sb.Append("   sim ms ").Append(_simMs.ToString("F3")).Append('\n');

            _sb.Append("field     ").Append(_field.ResolutionX).Append(" x ").Append(_field.ResolutionZ)
               .Append("  ").Append((_field.TexelSize * 100f).ToString("F2")).Append(" cm/texel")
               .Append("   relax ").Append(_field.RelaxIterations)
               .Append(" over ").Append(_field.RelaxWindowRect.width).Append('x')
               .Append(_field.RelaxWindowRect.height)
               .Append(" = ").Append((100.0 * _field.RelaxWindowTexels /
                                      Mathf.Max(1, _field.FieldTexels)).ToString("F2")).Append("% of field")
               .Append("   crest ").Append((_field.FieldMaxHeight * 100f).ToString("F1")).Append(" cm")
               .Append('\n');

            _sb.Append("lane      path mean ").Append((residual * 100f).ToString("F1")).Append(" cm")
               .Append(" worst ").Append((worst * 100f).ToString("F1")).Append(" cm")
               .Append("   cut ").Append(_field.CutRateM3PerSec.ToString("F3")).Append(" m3/s")
               .Append("   berm ").Append(_field.BermRateM3PerSec.ToString("F3")).Append(" m3/s")
               .Append("   blade ").Append(_field.BladeWidthM.ToString("F2")).Append(" m")
               .Append('\n');

            _sb.Append("mass      ").Append(_field.MirrorVolume.ToString("F3")).Append(" m3")
               .Append(" + carried ").Append(_field.LedgerLitres.ToString("F1")).Append(" L")
               .Append(" + deleted ").Append(_field.DeletedLitres.ToString("F1")).Append(" L")
               .Append("   invariant ").Append(_field.MassInvariantErrorL.ToString("F3")).Append(" L")
               .Append(" / tol ").Append(_field.MassToleranceL.ToString("F1")).Append(" L")
               .Append(_field.MassLeaking ? "   *** LEAK ***" : "   ok")
               .Append("   unplaced ").Append(_field.UnplacedPeakLitres.ToString("F4")).Append(" L")
               .Append(_field.UnplacedPeakLitres > 1e-5f ? " *** UNPLACED ***" : "").Append('\n');

            _sb.Append('\n');
            _sb.Append("VERB KEYS E blade DOWN/UP   1 angle LEFT   2 STRAIGHT   3 angle RIGHT\n");
            _sb.Append("          REVERSE WITH THE BLADE DOWN IS THE DUMP - S leaves the pile standing\n");
            _sb.Append("          a verb key latches manual verbs until the course laps or R resets\n");
            _sb.Append("keys      W/S drive   A/D steer   SPACE drift (hold)   SHIFT boost\n");
            _sb.Append("          driving keys override the auto course WHILE HELD; release and it resumes\n");
            _sb.Append("          C casual A-B   B bands   G RELAX GUARD A-B   H march heat view\n");
            _sb.Append("          , . relax iterations   R reset   P pause\n");
            _sb.Append("          console: one [V7] line every ")
               .Append(_logInterval.ToString("F1")).Append(" s carries all of the above");

            _hudText = _sb.ToString();
        }

        /// <summary>
        /// One [V7] line per _logInterval seconds carrying every number that matters.
        ///
        /// This is a deliverable, not a debug aid: the editor is driven over the Unity CLI without OS focus,
        /// where `screenshot --view Game` does not capture the IMGUI overlay and camera-only game-view
        /// captures never run OnGUI at all. The console line is the only instrument.
        /// </summary>
        private void EmitConsoleLine(float dt)
        {
            if (!_logEnabled) return;

            // The binding audit, once, on the first line. It is the kernel -> resource table the field
            // actually bound from, so it is evidence rather than a claim.
            if (!_auditLogged)
            {
                _auditLogged = true;
                Debug.Log("[V7] bindAudit " + _field.BindAuditText);
            }

            _logTimer -= dt;
            if (_logTimer > 0f) return;
            _logTimer = Mathf.Max(0.1f, _logInterval);

            float pathMean = PathResidual(out float pathWorst);

            Vector3 crest = HeapCrestWorld();
            Vector3 fwd3 = CarWorldForward();
            Vector3 bladePt = CarWorldPos() + fwd3 * _bladeOffsetM;
            Vector3 ahead = crest + fwd3 * (_field.PileHeightM / 1.5f + 0.5f);
            Vector3 behind = CarWorldPos() - fwd3 * (_carLength * 0.5f + 0.5f);

            _log.Clear();
            _log.Append("[V7]");

            // ---- THE PILE. Everything this variant exists to show. -----------------------------------
            _log.Append(" pile V=").Append(_field.PileVolumeM3.ToString("F3")).Append("m3")
                .Append(" h=").Append(_field.PileHeightM.ToString("F3")).Append('m')
                .Append(" crest=").Append(_field.PileCrestWidthM.ToString("F2")).Append('m')
                .Append(" footprint=").Append(_field.PileFootprintWidthM.ToString("F2")).Append('m')
                .Append(" mass=").Append(_field.PileMassKg.ToString("F0")).Append("kg")
                .Append(" cap=").Append(_field.PileCapacityM3.ToString("F3")).Append("m3")
                .Append(" fill=").Append((_field.PileFill01 * 100f).ToString("F1")).Append('%')
                .Append(" heapFrac=").Append(_field.HeapFractionLastStep.ToString("F4"))
                .Append(" pickup=").Append(_field.PickupRateM3PerSec.ToString("F4")).Append("m3/s")
                .Append(' ').Append(_field.Overflowing ? "OVERFLOWING"
                        : (_field.PickupRateM3PerSec > 1e-4 ? "ACCUMULATING" : "idle"));

            _log.Append(" | handling speedF=").Append(_car.MassSpeedFactor.ToString("F3"))
                .Append(" accelF=").Append(_car.MassAccelFactor.ToString("F3"))
                .Append(" brakeF=").Append(_car.MassBrakeFactor.ToString("F3"))
                .Append(" coastF=").Append(_car.MassCoastFactor.ToString("F3"))
                .Append(" turnInF=").Append(_car.MassTurnInFactor.ToString("F3"))
                .Append(" gripF=").Append(_car.MassGripFactor.ToString("F3"))
                .Append(" turnRadiusF=").Append(_car.MassTurnRadiusFactor.ToString("F3"))
                .Append(" turnR=").Append(_car.TurnRadiusM.ToString("F1")).Append('m')
                .Append(" refKg=").Append(_massRefKg.ToString("F0"));

            // ---- THE ERASE-AND-RE-EMIT, which is the variant's highest-risk path ----------------------
            _log.Append(" | heap erase=").Append(_field.HeapReclaimedLastStep.ToString("F5")).Append("m3")
                .Append(" emit=").Append(_field.HeapEmittedLastStep.ToString("F5")).Append("m3")
                // INDEPENDENT EVIDENCE, not a duplicate of pile V: this is what the emit reported writing
                // as RECORDED heap, so heapPlaced/V should equal heapFrac. Printing both is what lets the
                // capacity split be CHECKED rather than believed.
                .Append(" heapPlaced=").Append(_field.HeapPlacedM3.ToString("F4")).Append("m3")
                .Append(" release=").Append(_field.ReleaseDepositedLastStep.ToString("F5")).Append("m3")
                .Append(" spillRate=").Append(_field.ReleaseRateM3PerSec.ToString("F4")).Append("m3/s")
                .Append(" wallTotal=").Append(_field.ReleaseTotalM3.ToString("F3")).Append("m3")
                .Append(" win=").Append(_field.HeapWindowRect.width).Append('x')
                .Append(_field.HeapWindowRect.height)
                .Append('=').Append(_field.HeapWindowTexels).Append("tx")
                .Append(" guard=").Append(_field.HeapRelaxGuard ? "on" : "OFF")
                .Append(" guardDeg=").Append(_field.HeapGuardAngleDeg.ToString("F0"))
                .Append(" frontDeg=").Append(_field.HeapFrontAngleDeg.ToString("F0"))
                .Append(" backDeg=").Append(_field.HeapBackAngleDeg.ToString("F0"))
                .Append(" crestAhead=").Append(_field.HeapCrestAheadM.ToString("F2")).Append('m')
                .Append(" split=").Append(_field.HeapWidthPerHeight.ToString("F2"))
                .Append(" maxH=").Append(_field.HeapMaxHeightM.ToString("F2")).Append('m');

            // ---- THE VERB GRAMMAR --------------------------------------------------------------------
            //
            // A NEW SECTION rather than fields interleaved into the existing ones, deliberately: every
            // token above this line is unchanged in name, order and format, because the parent agent greps
            // them. The only field that LEFT is the `| dump` section, which had to go with the dump verb.
            //
            // NOTE `faceDeg` AGAINST `frontDeg` ABOVE. frontDeg is the AUTHORED shove angle and has not
            // moved; faceDeg is the LIVE leading-face angle, which collapses toward the repose angle when
            // the shove stops. Two different numbers, two different names, and the pair is the readout for
            // the face-collapse verb.
            _log.Append(" | blade bladeState=").Append(_field.BladeDownLastStep ? "down" : "up")
                .Append(" angle=").Append(AngleWord(_field.BladeAngleStateLastStep))
                .Append(" angleDeg=").Append(_field.BladeAngleAppliedDeg.ToString("F1"))
                .Append(" attach=").Append(_field.BladeAttachedLastStep ? 1 : 0)
                .Append(" depthSpeedF=").Append(_car.DepthSpeedFactor.ToString("F3"))
                .Append(" depthSpeedCm=").Append((_car.SnowDepthUnderM * 100f).ToString("F1"))
                .Append(" faceDeg=").Append(_field.FaceAngleDeg.ToString("F2"))
                .Append(" faceSteepen=").Append(_field.FaceSteepen01.ToString("F3"))
                .Append(" breakLoose=").Append(_car.BreakLooseMps2.ToString("F2")).Append("m/s2")
                .Append(" castRate=").Append(_field.CastRateM3PerSec.ToString("F4")).Append("m3/s")
                .Append(" castFrac=").Append(_field.CastFractionLastStep.ToString("F4"))
                .Append(" releaseRate=").Append(_field.ReleaseRateCommandedM3PerSec.ToString("F4")).Append("m3/s")
                .Append(" releaseFrac=").Append(_field.ReleaseFractionLastStep.ToString("F4"))
                .Append(" castPush=").Append(_car.CastPushMps2.ToString("F2")).Append("m/s2")
                .Append(" castYaw=").Append(_car.CastYawDegPerSec.ToString("F2")).Append("deg/s")
                .Append(" verbSrc=").Append(_manualVerbs ? "manual" : "course")
                .Append(" deposits=").Append(_field.DepositCount)
                .Append(" lastDeposit=").Append(_field.LastDepositedM3.ToString("F3")).Append("m3")
                .Append(_field.DepositRanLastStep ? " DEPOSITING" : "");

            // ---- THE VEHICLE ------------------------------------------------------------------------
            _log.Append(" | drive=").Append(_manualOverride ? "manual" : (_autoDrive ? "auto" : "idle"))
                .Append(" seg=").Append(_course.SegmentName)
                .Append('(').Append(_course.SegmentIndex + 1).Append('/').Append(_course.SegmentCount)
                .Append(' ').Append(_course.SegmentRemaining.ToString("F1")).Append("s)")
                .Append(" lap=").Append(_course.Lap)
                .Append(" tScale=").Append(_courseTimeScale.ToString("F2"));

            _log.Append(" | car v=").Append(_car.ForwardSpeed.ToString("F2")).Append("m/s")
                .Append(" steerIn=").Append(_lastInput.Steer.ToString("F2"))
                .Append(" steerApplied=").Append(_car.SteerApplied.ToString("F2"))
                .Append(" yaw=").Append(_car.YawRateDegPerSec.ToString("F1")).Append("deg/s")
                .Append(" slip=").Append(_car.SlipAngleDeg.ToString("F1")).Append("deg")
                .Append(" drift=").Append(_car.DriftActive ? "on" : "off")
                .Append(" accel=").Append(_car.LongitudinalAccel.ToString("F2")).Append("m/s2")
                .Append(" lat=").Append(_car.LateralAccel.ToString("F1")).Append("m/s2")
                .Append(" pitch=").Append(_car.BodyPitchDeg.ToString("F1"))
                .Append(" roll=").Append(_car.BodyRollDeg.ToString("F1"))
                .Append(" pos=").Append(_car.PositionXZ.x.ToString("F1"))
                .Append(',').Append(_car.PositionXZ.y.ToString("F1"))
                .Append(" hdg=").Append(_car.HeadingDeg.ToString("F0"))
                .Append(" bladeOffset=").Append(_bladeOffsetM.ToString("F2")).Append('m');

            _log.Append(" | snow depthAhead=").Append((_car.SnowDepthAheadM * 100f).ToString("F1")).Append("cm")
                .Append(" depthUnder=").Append((_car.SnowDepthUnderM * 100f).ToString("F1")).Append("cm")
                .Append(" drag=").Append(_car.SnowDragMps2.ToString("F2")).Append("m/s2");

            // ---- THE FIELD -------------------------------------------------------------------------
            _log.Append(" | field=").Append(_field.ResolutionX).Append('x').Append(_field.ResolutionZ)
                .Append(' ').Append((_field.TexelSize * 100f).ToString("F2")).Append("cm/texel")
                .Append(' ').Append(_field.PatchSizeX.ToString("F1")).Append('x')
                .Append(_field.PatchSizeZ.ToString("F1")).Append('m')
                .Append(" origin=").Append(_field.PatchMin.x.ToString("F1"))
                .Append(',').Append(_field.PatchMin.y.ToString("F1"))
                .Append(" mirror=").Append(_field.MirrorResolutionX).Append('x')
                .Append(_field.MirrorResolutionZ)
                .Append(" coarse=").Append(_field.CoarseResolutionX).Append('x')
                .Append(_field.CoarseResolutionZ)
                .Append('@').Append((_field.CoarseCellM * 100f).ToString("F0")).Append("cm")
                .Append(" safeR=").Append(_field.CoarseSafeRadiusM.ToString("F2")).Append('m');

            _log.Append(" | relax=").Append(_field.RelaxIterations)
                .Append(" repose=").Append(_field.ReposeAngle.ToString("F0")).Append("deg")
                .Append(" window=").Append(_field.RelaxWindowEnabled ? "on" : "OFF")
                .Append(' ').Append(_field.RelaxWindowRect.width).Append('x')
                .Append(_field.RelaxWindowRect.height)
                .Append('=').Append(_field.RelaxWindowTexels).Append("tx")
                .Append(" of ").Append(_field.FieldTexels).Append("tx")
                .Append(" (").Append((100.0 * _field.RelaxWindowTexels /
                                      Mathf.Max(1, _field.FieldTexels)).ToString("F2")).Append("%)")
                .Append(" taps=").Append(_field.RelaxTapsLastStep)
                .Append(" vsWhole=").Append(_field.RelaxTapsWholeField)
                .Append(" dispatches=").Append(_field.RelaxDispatchesLastStep);

            // ---- THE MARCH INSTRUMENT ---------------------------------------------------------------
            _log.Append(" | march probe=").Append(_stepsProbe.StatusText);
            if (!_stepsProbe.HasMeasurement)
            {
                _log.Append(" dispatches=").Append(_stepsProbe.Dispatches)
                    .Append(" readbacks=").Append(_stepsProbe.Readbacks)
                    .Append(" rbErrors=").Append(_stepsProbe.ReadbackErrors)
                    .Append(" testedRays=").Append(_stepsProbe.TestedRays)
                    .Append(" offBox=").Append(_stepsProbe.OffBoxRays);
            }
            else
            {
                _log.Append(" meanSteps=").Append(_stepsProbe.MeanSteps.ToString("F1"))
                    .Append(" p95Steps=").Append(_stepsProbe.P95Steps)
                    .Append(" maxSteps=").Append(_stepsProbe.MaxSteps)
                    .Append(" exhausted=").Append(_stepsProbe.ExhaustedPercent.ToString("F2")).Append('%')
                    .Append(" budget=").Append(_raymarch.MaxSteps)
                    .Append(" rays=").Append(_stepsProbe.MarchedRays);
            }
            _log.Append(" stepM=").Append(_raymarch.StepM.ToString("F3"))
                .Append(" refine=").Append(_raymarch.RefineSteps)
                .Append(" lodM=").Append(_raymarch.LodDistanceM.ToString("F0"))
                .Append(" marchTop=").Append(_raymarch.MarchTopY.ToString("F2")).Append('m')
                .Append(" lump=").Append(_raymarch.LumpEnabled ? "on" : "off")
                .Append(" lumpR=").Append(_raymarch.LumpRadiusM.ToString("F2")).Append('m')
                .Append(" lumpS=").Append(_raymarch.LumpSpacingM.ToString("F2")).Append('m')
                .Append(" lumpBound=+").Append(_raymarch.LumpBoundHeadroomM.ToString("F2")).Append('m')
                .Append(" lumpBake=").Append(_field.LumpBakeResolutionX).Append('x')
                .Append(_field.LumpBakeResolutionZ)
                .Append(" lumpBakeWin=").Append(_field.LumpBakeWindowRect.width).Append('x')
                .Append(_field.LumpBakeWindowRect.height)
                .Append('=').Append(_field.LumpBakeWindowTexels).Append("tx")
                .Append(" of ").Append(_field.LumpBakeTexels).Append("tx")
                .Append(" (").Append((100.0 * _field.LumpBakeWindowTexels /
                                      Mathf.Max(1, _field.LumpBakeTexels)).ToString("F2")).Append("%)")
                .Append(" lumpBakeDispatches=").Append(_field.LumpBakeDispatchesLastStep)
                .Append(" camUp=").Append((_cameraUp + _field.PileHeightM * _cameraDistancePerHeight *
                                           _cameraHeightShareOfDistance).ToString("F2"))
                .Append(" camBack=").Append((_cameraBack + _field.PileHeightM *
                                             _cameraDistancePerHeight).ToString("F2"))
                .Append(" camBias=").Append(_cameraPileBias.ToString("F2"))
                .Append(" fov=").Append(_camera.fieldOfView.ToString("F1"))
                .Append(_stepsProbe.HeatView ? " HEAT" : "");

            // ---- cost ------------------------------------------------------------------------------
            _log.Append(" | sim=").Append(_simMs.ToString("F3")).Append("ms")
                .Append(" frame=").Append(_frameMs.ToString("F2")).Append("ms")
                .Append(' ').Append((_frameMs > 0.001f ? 1000f / _frameMs : 0f).ToString("F0")).Append("fps");
            if (_gpuRecorder.Valid)
                _log.Append(" gpu=").Append(_gpuRecorder.Milliseconds.ToString("F2")).Append("ms");

            // ---- the casual knobs actually in force -------------------------------------------------
            _log.Append(" | casual=").Append(_casualAmount.ToString("F2"))
                .Append(_casualAmount < 0.001f ? "(REALISTIC)" : "")
                .Append(" bands=").Append(_casualBands.ToString("F0"))
                .Append(" soft=").Append(_casualBandSoftness.ToString("F2"))
                .Append(" wrap=").Append(_casualWrap.ToString("F2"))
                .Append(" albedoInf=").Append(_casualAlbedoInfluence.ToString("F2"))
                .Append(" aoInf=").Append(_casualAoInfluence.ToString("F2"))
                .Append(" rim=").Append(_casualRimStrength.ToString("F2"))
                .Append(" sparkle=").Append(_casualSparkleAmount.ToString("F2"))
                .Append(" round=").Append((_casualRoundM * 100f).ToString("F1")).Append("cm")
                .Append(" detail=").Append(_raymarch.DetailAmpM.ToString("F3")).Append('/')
                .Append(_raymarch.DetailOctaves).Append("oct")
                .Append(" nrmDetail=").Append(_raymarch.NormalDetailAmpM.ToString("F3"))
                .Append(" exaggeration=").Append(_casualLoadExaggeration.ToString("F2"));

            _log.Append(" | hmax=").Append((_field.FieldMaxHeight * 100f).ToString("F1")).Append("cm");

            // FOUR height probes rather than v6's three, and the extra one is the point: `crest` is where
            // the heap's peak is supposed to be, so crest minus blade is the direct read on whether the
            // emitted profile is where the arithmetic says.
            _log.Append(" | cpuH blade=").Append((_field.HeightAt(bladePt) * 100f).ToString("F1")).Append("cm")
                .Append(" crest=").Append((_field.HeightAt(crest) * 100f).ToString("F1")).Append("cm")
                .Append(" ahead=").Append((_field.HeightAt(ahead) * 100f).ToString("F1")).Append("cm")
                .Append(" behind=").Append((_field.HeightAt(behind) * 100f).ToString("F1")).Append("cm");

            _log.Append(" | stripeResidual mean=").Append((pathMean * 100f).ToString("F1")).Append("cm")
                .Append(" worst=").Append((pathWorst * 100f).ToString("F1")).Append("cm")
                .Append(" n=").Append(Mathf.Clamp(_residualSamples, 1, 64))
                .Append(" over=").Append(_residualTrailSeconds.ToString("F1")).Append('s');

            // ---- THE INVARIANT. field + carried + DELETED == initial, to within the tolerance. --------
            //
            // v7's hardest test of it: the pile is lifted off the field and put back down sixty times a
            // second, and both halves of that transfer are in this expression. `carried` reads near zero
            // because the pile IS field mass - see SnowPileFieldV7's header.
            //
            // AND `unplaced=`, WHICH IS THE FAULT RATHER THAN ITS INTEGRAL. The invariant only speaks once
            // an unbooked transfer has accumulated past a 7.9 L proportional tolerance - three minutes, for
            // the 46 mL/s berm leak this variant shipped with. `unplaced` is the mass a placement kernel
            // resolved for a texel it could not write, per step, and it is ZERO BY CONSTRUCTION: HeapScan's
            // normaliser and HeapEmit's writable set are one FieldWritable test. A fifth emit channel that
            // normalises over the full footprint and writes only the in-field part reads nonzero here on
            // the first frame its footprint touches the patch edge. `peak` is the worst since the last
            // reset, so a one-frame spike cannot fall between two console lines.
            _log.Append(" | mass=").Append(_field.MirrorVolume.ToString("F3")).Append("m3")
                .Append(" +carried=").Append((_field.LedgerLitres * 0.001f).ToString("F4")).Append("m3")
                .Append(" +deleted=").Append((_field.DeletedLitres * 0.001f).ToString("F4")).Append("m3")
                .Append(" initial=").Append(_field.InitialVolume.ToString("F1")).Append("m3")
                .Append(" invariant=").Append(_field.MassInvariantErrorL.ToString("F3")).Append('L')
                .Append("/tol").Append(_field.MassToleranceL.ToString("F2"))
                .Append("(abs").Append(_field.MassToleranceAbsoluteL.ToString("F1"))
                .Append(",ppm").Append(_field.MassTolerancePpm.ToString("F1")).Append(')')
                .Append(_field.MassLeaking ? " LEAK" : " ok")
                .Append(" unplaced=").Append(_field.UnplacedLitres.ToString("F4")).Append('L')
                .Append("/peak").Append(_field.UnplacedPeakLitres.ToString("F4")).Append('L')
                .Append(_field.UnplacedPeakLitres > 1e-5f ? " UNPLACED" : "");

            _log.Append(" | loss conserved=").Append(_field.ConservedFraction.ToString("F2"))
                .Append(" bermShare=").Append(_field.BermShareOfLoss.ToString("F2"))
                .Append(" shedTau=").Append(_field.PileShedTauSeconds.ToString("F1")).Append('s')
                .Append(_field.PileShedTauSeconds <= 1e-4f ? "(RATCHET)" : "")
                .Append(" deleted=").Append(_field.DeletedLitres.ToString("F1")).Append('L')
                .Append(" cutRate=").Append(_field.CutRateM3PerSec.ToString("F4")).Append("m3/s")
                .Append(" terrainRate=").Append(_field.TerrainCutRateM3PerSec.ToString("F4")).Append("m3/s")
                .Append(" bermRate=").Append(_field.BermRateM3PerSec.ToString("F4")).Append("m3/s")
                .Append(" pickupDepth=").Append(_field.PickupDepthM.ToString("F3")).Append('m')
                .Append(" density=").Append(_field.SnowDensityKgPerM3.ToString("F0")).Append("kg/m3")
                .Append(" blade=").Append(_field.BladeWidthM.ToString("F2")).Append('x')
                .Append(_field.BladeDepthM.ToString("F2")).Append('m')
                .Append(" segs=").Append(_field.BladeSegmentsLastStep);

            if (_paused) _log.Append(" | PAUSED");

            Debug.Log(_log.ToString());
        }

        private void OnGUI()
        {
            if (_hudStyle == null)
            {
                _hudBackdrop = new Texture2D(1, 1) { name = "SnowFakeV7HudBackdrop" };
                _hudBackdrop.SetPixel(0, 0, new Color(0f, 0f, 0f, 0.68f));
                _hudBackdrop.Apply();

                _hudStyle = new GUIStyle(GUI.skin.label)
                {
                    alignment = TextAnchor.UpperLeft,
                    wordWrap = false,
                    richText = false,
                };
                _hudStyle.normal.textColor = Color.white;
            }

            _hudStyle.fontSize = _hudFontSize;

            const float pad = 12f;

            // 23 lines: 14 data rows (variant, PILE, handling, heap, VERBS, drive, vehicle, snow feel,
            // casual, march, frame ms, field, lane, mass), a blank, and 8 key rows. COUNTED against
            // UpdateHud rather than guessed, because a backdrop shorter than the text leaves the key hints
            // on a transparent background where they are unreadable against the snow, which is the one part
            // of this HUD a human actually reads - and the verb keys are now the part a human most needs.
            float height = _hudFontSize * 1.42f * 23f;

            GUI.DrawTexture(new Rect(0f, 0f, Screen.width, height + pad * 2f), _hudBackdrop);
            GUI.Label(new Rect(pad, pad, Screen.width - pad * 2f, height), _hudText, _hudStyle);
        }

        /// <summary>
        /// The blade angle state as a word, for both instruments. ONE function, so the HUD and the console
        /// line can never disagree about which way the blade is pointing - which, for a verb whose entire
        /// acceptance test is "does the windrow appear on the side the telemetry says", would be the single
        /// most misleading bug available here.
        /// </summary>
        private static string AngleWord(int state)
        {
            return (state > 0) ? "right" : ((state < 0) ? "left" : "straight");
        }

        /// <summary>
        /// Thin wrapper so a missing "GPU Frame Time" counter (it is not available on every Metal driver)
        /// degrades to "not shown" instead of throwing.
        /// </summary>
        private struct GpuTimeRecorder
        {
            private Unity.Profiling.ProfilerRecorder _recorder;
            private bool _created;

            public static GpuTimeRecorder Create()
            {
                var wrapper = new GpuTimeRecorder();
                try
                {
                    wrapper._recorder = Unity.Profiling.ProfilerRecorder.StartNew(
                        Unity.Profiling.ProfilerCategory.Render, "GPU Frame Time");
                    wrapper._created = true;
                }
                catch (System.Exception)
                {
                    wrapper._created = false;
                }
                return wrapper;
            }

            public bool Valid => _created && _recorder.Valid && _recorder.Count > 0;

            public float Milliseconds => _recorder.LastValue * 1e-6f;

            public void Dispose()
            {
                if (_created) _recorder.Dispose();
                _created = false;
            }
        }
    }
}
