using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// The blade's motion over one step AND ITS THREE VERBS. The field turns the motion into a UNION OF
    /// SWEPT ORIENTED BOXES and the verbs into which kernels run and what the emit does with the ledger.
    ///
    /// v6 handed the field a pair of ball poses and it swept a CAPSULE union, because the swept volume of
    /// a translating disc is exactly a capsule. A blade is a line segment with a little thickness, so its
    /// swept volume is an oriented BOX - one box is exact for a translation at ANY blade angle, and the
    /// tiling survives only for the case where it is still needed: a TURNING blade's centre traces an ARC
    /// and the straight chord between the two end poses cuts the corner.
    ///
    /// THE VERBS TRAVEL WITH THE SWEEP RATHER THAN AS COMPONENT STATE, deliberately. They change what the
    /// cut and the emit MEAN for exactly this one step, and the field must never be able to run a step
    /// whose geometry came from one frame and whose verbs came from another - which is precisely the class
    /// of bug that a receipt-based erase turns into a leak.
    /// </summary>
    public struct SnowPileSweepV7
    {
        /// <summary>World XZ of the blade line's centre at the START of the step.</summary>
        public Vector2 StartCenter;

        /// <summary>Unit world XZ direction of travel at the START of the step.</summary>
        public Vector2 StartForward;

        /// <summary>World XZ of the blade line's centre at the END of the step.</summary>
        public Vector2 EndCenter;

        /// <summary>Unit world XZ direction of travel at the END of the step.</summary>
        public Vector2 EndForward;

        /// <summary>Signed speed along the end forward, in m/s. Negative while reversing.</summary>
        public float SignedSpeed;

        /// <summary>How many swept boxes the step's arc is tiled into, 1..8. 1 is the chord.</summary>
        public int Segments;

        /// <summary>
        /// VERB 1. True = ploughing: cut, accumulate, resist. False = transit: the field does not dispatch
        /// Push at all, so nothing is cut and nothing reaches the ledger.
        /// </summary>
        public bool BladeDown;

        /// <summary>
        /// VERB 2. The DISCHARGE SIDE, and the only three legal values are -1 LEFT, 0 STRAIGHT, +1 RIGHT.
        /// The blade is yawed so that the discharge end TRAILS, which is what casts snow off that side.
        /// </summary>
        public int AngleState;

        /// <summary>
        /// VERB 3, resolved. True while the blade's face is supporting the heap, i.e. while the vehicle is
        /// driving INTO it: blade down AND forward faster than the attach speed. False the moment it is
        /// not - raised, stopped, or reversing - and then the heap is DEPOSITED where it stands.
        ///
        /// DIRECTIONAL IN GENERAL, not a reverse edge. The vehicle owner resolves it with hysteresis from
        /// the signed forward speed, so a blade that merely stops has already let go.
        /// </summary>
        public bool BladeAttached;

        /// <summary>
        /// How hard the vehicle is shoving, 0..1: the forward throttle while attached, 0 otherwise. Drives
        /// the LEADING FACE ANGLE - shoved at 1, collapsing toward the repose angle at 0.
        /// </summary>
        public float Push01;
    }

    /// <summary>
    /// Variant B <b>v7</b>: v6's height field, activity-bounded dispatch, repose relax, CPU mirror,
    /// booked-deletion mass invariant, baked lump-lobe layer and raymarcher, with <b>THE BALL DELETED</b>
    /// and replaced by <b>A HEAP THAT ACCUMULATES IN FRONT OF A BLADE AND IS SHOVED FORWARD</b>.
    ///
    /// THE ONE IDEA
    /// ------------
    /// v6's load was a separate SPHERE MESH sized r = (3V/4pi)^(1/3) from a ledger, drawn beside the
    /// field with its own shader, its own spin and its own contact AO. It read as a ball ROLLING, which
    /// is what it was. v7 puts the load INTO THE HEIGHT FIELD:
    ///
    ///     THE PILE IS FIELD HEIGHT. There is no second surface.
    ///
    /// Consequences, in the order they matter:
    ///   * the existing raymarcher, the existing BAKED lump-lobe layer and the existing casual treatment
    ///     render the pile for free - no new draw, no new shader, no new material;
    ///   * the drawn silhouette IS the collidable surface, because there is only one surface;
    ///   * the pile can be pushed, spilled, driven over and re-eaten with the same kernels the rest of
    ///     the snow already uses;
    ///   * and the thing in front of the vehicle reads as a PILE BEING SHOVED rather than a ball being
    ///     rolled, which is the whole point of the variant.
    ///
    /// HOW THE PILE MOVES WITHOUT LEAKING
    /// ----------------------------------
    /// Per-texel advection - read behind, write ahead - diffuses a crest into mush within a second and
    /// loses mass to the interpolation. So v7 does what v6's DUMP already did conservatively, only
    /// continuously, every frame:
    /// <code>
    ///     HeapErase   subtract the heap's PREVIOUS footprint, credit it to the ledger
    ///     Push        cut ahead of the blade at FULL DEPTH, credit it to the ledger
    ///     Settle      take the deliberate loss out of the ledger, split it berm / bin
    ///     Deposit     lay the berm share beside the cleared lane
    ///     HeapEmit    re-emit the WHOLE ledger at the blade's NEW pose
    ///     Relax       repose, with the heap's own footprint held at the SHOVE angle
    /// </code>
    /// and the erase reads a RECEIPT rather than re-deriving the old footprint - see
    /// <see cref="_heapRt"/> and the compute shader's HeapErase.
    ///
    /// THE SHAPE, AND THE SPLIT THAT IS THE ACCUMULATING FEEL
    /// -----------------------------------------------------
    /// A flat-topped RIDGE lying across the blade, hipped at each end: leading face at
    /// <see cref="_heapFrontAngleDeg"/> (STEEPER than repose, because it is being shoved), back face at
    /// <see cref="_heapBackAngleDeg"/> (steeper still, because the blade is holding it), flanks at
    /// exactly the repose angle, crest <see cref="_heapCrestAheadM"/> ahead of the blade line.
    ///
    /// THE SPLIT KNOB IS <see cref="_heapWidthPerHeight"/>, and it is the accumulating feel:
    /// <code>
    ///     crest half-length  Lc(H) = bladeHalfWidth + widthPerHeight * H      (clamped)
    ///     volume             V(H)  = c H^2 (2 Lc(H) + 2H / (3 tanRepose))
    ///                        c     = (1/tanFront + 1/tanBack) / 2
    /// </code>
    /// At widthPerHeight 0 every extra cubic metre goes into HEIGHT and the heap grows into a tower
    /// inside the blade's width; at 2 it goes mostly into crest LENGTH and the heap fans out sideways
    /// past the blade. 1.10 is the shipped value and it holds the heap about four times wider than it is
    /// tall at every size, which is what a blade full of snow looks like.
    ///
    /// V IS MONOTONE IN H, so the height is solved by BISECTION rather than by a cubic formula - and the
    /// solve does not have to be accurate: HeapEmit normalises by the scanned weight sum, so an error in
    /// H changes the heap's HEIGHT, never its mass. The solve sets the aspect ratio; the normalisation
    /// sets the volume.
    ///
    /// CAPACITY AND OVERFLOW, AS A RATE
    /// -------------------------------
    /// Vcap = V(<see cref="_heapMaxHeightM"/>) at the LIVE face angle. The release is not a threshold: it
    /// ramps from <see cref="_releaseStartFill"/> to full at capacity at <see cref="_releaseRatePerSec"/>,
    /// and above capacity a hard term releases exactly the excess so the heap is pinned however the rate
    /// knob is set. Released material is NOT recorded, so it is debited from the ledger and becomes
    /// permanent field mass. A STRAIGHT blade puts it in two cones, one off each end; an ANGLED blade puts
    /// all of it - plus the cast - in ONE, at the discharge end, which is what makes a windrow rather than
    /// two walls.
    ///
    /// THE THREE VERBS
    /// ---------------
    /// <code>
    ///     blade DOWN / UP                       plough / transit
    ///     blade angle LEFT / STRAIGHT / RIGHT   where the snow is thrown
    ///     FORWARD / REVERSE                     accumulate / deposit
    /// </code>
    /// They arrive on <see cref="SnowPileSweepV7"/> and cost no kernel between them:
    ///
    ///   * UP does not dispatch Push, so nothing is cut and nothing reaches the ledger;
    ///   * ANGLE rotates the swept box's own frame - so the cut footprint is a rotated rectangle - and
    ///     moves a share of the ledger from the heap channel to the release channel, LEDGER TRANSPORT
    ///     rather than a per-texel lateral push, because per-texel transport is what diffuses and leaks;
    ///   * REVERSE (and stopping, and raising) makes HeapErase RETIRE the receipt rather than reclaim it:
    ///     the heap stays exactly where it stands as permanent field mass, the carried volume goes to
    ///     zero, and the invariant does not move by one millilitre because the field was never touched.
    ///
    /// RAISING A LOADED BLADE DROPS THE HEAP, and that is a decision rather than a fallout. The blade's
    /// face is the only thing holding 1.8 tonnes of snow, so a raised blade cannot be carrying it; keeping
    /// it would also make blade-up transit strictly worse than ploughing, since the carried-mass couplings
    /// would still be paying for a load that is doing no work. Dropping it is what makes UP a verb worth
    /// pressing - and it is the escape from the boundary stall, because an empty vehicle can reverse out of
    /// a wall it cannot plough through.
    ///
    /// THE PACING, DERIVED (this component cannot run the editor, so nothing here is measured)
    /// --------------------------------------------------------------------------------------
    /// Full-depth pickup over a 2.3 m blade in a 0.30 m slab at 8 m/s cuts 0.30 * 2.3 * 8 = 5.52 m3/s of
    /// terrain, of which <see cref="_conservedFraction"/> 0.40 reaches the ledger: 2.21 m3/s. Capacity at
    /// the shipped shape knobs (peak 1.60 m, widthPerHeight 1.10, front 65 deg, back 75 deg, repose
    /// 55 deg, blade half 1.15 m) is
    /// <code>
    ///     c   = (1/tan65 + 1/tan75)/2 = (0.4663 + 0.2679)/2 = 0.3671
    ///     Lc  = 1.15 + 1.10 * 1.60    = 2.91 m      (crest 5.82 m across)
    ///     V   = 0.3671 * 1.60^2 * (2*2.91 + 2*1.60/(3*tan55)) = 0.9396 * 6.567 = 6.17 m3
    /// </code>
    /// so the heap should fill in about 2.8 s and 22 m of ploughing, then hold at 6.2 m3 / 1,850 kg while
    /// everything after that goes to the walls. The mass coupling slows the vehicle as it fills, which
    /// stretches the fill time, and re-driving your own walls returns material for free - both push the
    /// real number up. THE PARENT AGENT MEASURES; these are the numbers to measure against.
    ///
    /// WHERE `carried` SITS IN THE INVARIANT
    /// ------------------------------------
    /// The pile is field mass, so it cannot also be ledger mass without being double counted. After
    /// HeapEmit the ledger holds fixed-point dust and nothing else, and the invariant
    /// <c>field + carried + deleted - initial</c> is therefore mostly a statement about the field - which
    /// is exactly as strong as before, because every transfer is an exact integer pair and any unbooked
    /// creation still shows at full size. The PILE'S OWN VOLUME is published separately from
    /// <c>_Carry[3]</c>, the sum of what HeapEmit actually wrote - a GPU measurement, not a CPU
    /// prediction. See <see cref="PileVolumeM3"/> and <see cref="MassInvariantErrorL"/>.
    ///
    /// EVERY TRANSFER IS A MEASURED INTEGER, AND ONE OF THEM USED NOT TO BE
    /// -------------------------------------------------------------------
    /// The berm was the exception, and it was a real monotone leak. <c>Settle</c> debited the ledger for a
    /// PREDICTED berm volume and left <c>Deposit</c> to place it; Deposit placed
    /// <c>over * (true cut / _Stats[0])</c>, and <c>_Stats[0]</c> is a round-to-nearest sum that
    /// over-reports the cut by a mean +0.05 fixed units per cut texel, always in the same direction -
    /// because <c>rem</c> is already snapped to the ledger's ten-times-finer grid, so one texel in ten
    /// lands on the half-integer <c>(int)(x + 0.5)</c> rounds UP. The shortfall was destroyed and never
    /// booked, at roughly 1.4 mL per full-depth step, i.e. the 46 mL/s monotone negative drift the meter
    /// caught after three minutes. Deposit now debits the ledger with THE SAME INTEGER it adds to the
    /// field, Settle books only what it genuinely destroys, and no prediction is left in the mass path.
    /// <see cref="UnplacedLitres"/> is the standing guard against the next one.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class SnowPileFieldV7 : MonoBehaviour
    {
        // ------------------------------------------------------------------ field geometry
        [Header("Patch - THE REAL STAGE")]
        [Tooltip("World X of the patch corner. The real project's SnowStage origin is -60.")]
        [SerializeField] private float _patchOriginX = -60f;

        [Tooltip("World Z of the patch corner. The real project's SnowStage origin is -55.")]
        [SerializeField] private float _patchOriginZ = -55f;

        [Tooltip("Patch extent along X in metres. The real project's SnowStage is 120.")]
        [SerializeField] private float _patchSizeX = 120f;

        [Tooltip("Patch extent along Z in metres. The real project's SnowStage is 110.")]
        [SerializeField] private float _patchSizeZ = 110f;

        [Tooltip("Initial snow depth in metres. The real project's SnowStage max depth is 0.30.")]
        [SerializeField] private float _startDepth = 0.30f;

        [Tooltip("Ground plane height. The field stores snow depth above this.")]
        [SerializeField] private float _groundY = 0f;

        [Header("Resolution")]
        [Tooltip("Metres per texel, both axes. 0.125 over 120 x 110 m gives 960 x 880 = 845k texels, " +
                 "which is the real project's SnowStage cell exactly, so every measurement transfers.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _cellSizeM = 0.125f;

        [Tooltip("Block size of the CPU mirror downsample. Both axis resolutions are rounded up to a " +
                 "multiple of 8 * this, so the mirror stays an EXACT box average of the full field - " +
                 "which is the only reason the CPU volume is trustworthy enough to be a conservation " +
                 "check.")]
        [Range(1, 16)]
        [SerializeField] private int _mirrorDownsample = 2;

        [Tooltip("Frames between AsyncGPUReadback requests for the CPU mirror.")]
        [Range(1, 30)]
        [SerializeField] private int _readbackInterval = 4;

        // ------------------------------------------------------------------ THE BLADE
        [Header("THE BLADE")]
        [Tooltip("Depth of TERRAIN the blade shaves per PASS, in metres. 0.30 IS THE WHOLE SLAB, ON " +
                 "PURPOSE, and it is v6's measured lesson: v6 shaved a 4 cm SKIN so its ball would grow " +
                 "slowly, and the consequence was that the world barely recorded the pass - the lane " +
                 "looked driven over rather than cleared. A plough clears.\n\n" +
                 "The budget machinery is unchanged, so the skin is one knob away: at 0.04 you get v6's " +
                 "pacing with v7's shape.")]
        [Range(0.005f, 0.60f)]
        [SerializeField] private float _pickupDepthM = 0.30f;

        [Tooltip("Per-PASS cap on how fast the blade reclaims material it ALREADY OWNS - its own windrows, " +
                 "and piles it reversed out from under earlier. Re-pickup is FREE (it is not charged " +
                 "against the pass budget and it pays no deliberate-loss toll), which is what makes " +
                 "gather -> shove -> deposit a LOOP rather than a one-way ratchet.\n\n" +
                 "It needs a cap, and the deposit verb is exactly why: reversing out of a full blade leaves " +
                 "a 1.6 m pile standing, and uncapped, driving back into it would put the whole thing on " +
                 "the ledger in ONE FRAME - a visual pop and a two-tonne step change in the handling. " +
                 "0.60 m per pass is twice the slab depth, so a windrow goes in one pass and a deposited " +
                 "pile takes three or four.")]
        [Range(0.02f, 3f)]
        [SerializeField] private float _pileGrabPerPassM = 0.60f;

        [Tooltip("The blade's cutting width in metres. 2.30 is v5's blade, which is the real project's.\n\n" +
                 "CONSTANT, unlike v6's clearing width. v6's stripe was 2r and r came from the ledger, " +
                 "so growing the ball widened the plough. A blade does not widen - what grows in v7 is " +
                 "the HEAP, and the heap's crest is allowed to grow PAST this width, which is exactly " +
                 "what makes the overflow escape sideways instead of the stripe silently widening to " +
                 "swallow it.")]
        [Range(0.3f, 8f)]
        [SerializeField] private float _bladeWidthM = 2.3f;

        [Tooltip("The blade's thickness along travel, in metres. It sets the swept footprint of a " +
                 "STATIONARY blade, and it is the residence length the per-pass cut budget is divided " +
                 "over: a texel is under the blade for this/speed seconds, so the per-step budget is " +
                 "pickupDepth * sweepDistance / this. A blade parked on snow therefore cuts NOTHING, " +
                 "which is what keeps the cut timestep independent.")]
        [Range(0.05f, 2f)]
        [SerializeField] private float _bladeDepthM = 0.35f;

        [Tooltip("Density of packed snow in kg/m3. 300 is wind-packed / machine-packed snow; fresh " +
                 "powder is 50-100 and glacial ice is 900. This is the ONLY thing that turns the pile's " +
                 "volume into the mass the handling is driven from, so it is the master gain on 'how " +
                 "heavy does this feel'. At 300, the 6.17 m3 capacity heap is 1,850 kg.")]
        [Range(50f, 900f)]
        [SerializeField] private float _snowDensityKgPerM3 = 300f;

        [Tooltip("Swept boxes the footprint is tiled into, 1..8.\n\n" +
                 "ONE box is exactly the swept volume of a translating blade, so this knob is a no-op " +
                 "for a straight step. It tiles the ARC: a turning blade's centre traces a curve, the " +
                 "straight chord between the two end poses cuts the corner, AND the blade's own heading " +
                 "rotates through the step - each sub-box takes its orientation from its own direction " +
                 "of travel, so the tiling covers both.\n\n" +
                 "MEASURE BEFORE RAISING IT. At the shipped top speed and full lock the chord error is " +
                 "a few millimetres; the knob exists because a hitched frame (dt clamped at 0.05 s) " +
                 "sweeps 0.7 m through several degrees and that is where it would start to matter.")]
        [Range(1, 8)]
        [SerializeField] private int _bladeSegments = 3;

        // ------------------------------------------------------------------ THE HEAP
        [Header("THE HEAP - shape, and the split that IS the accumulating feel")]
        [Tooltip("MAXIMUM heap peak height in metres, above whatever the field already holds. This is " +
                 "one half of the CAPACITY: past the volume this height and the crest length can " +
                 "contain, the excess escapes sideways as spill instead of the heap growing forever.\n\n" +
                 "1.60 m with the shipped shape knobs is a 6.17 m3 / 1,850 kg capacity, about 2.8 s and " +
                 "22 m of ploughing at 8 m/s. Raise it for a longer accumulation phase and a heavier " +
                 "endgame; lower it to start building walls almost immediately.\n\n" +
                 "Keep it under the raymarcher's Max Snow Height M minus the slab depth, or the top of " +
                 "a full heap is drawn as a flat plateau.")]
        [Range(0.1f, 3f)]
        [SerializeField] private float _heapMaxHeightM = 1.60f;

        [Tooltip("THE SPLIT KNOB, and the one that decides whether this reads as accumulating. Metres " +
                 "of extra crest HALF-LENGTH per metre of heap HEIGHT:\n\n" +
                 "    crest half-length  Lc(H) = bladeHalfWidth + this * H\n\n" +
                 "0 sends every extra cubic metre into HEIGHT: the heap grows into a tower inside the " +
                 "blade's own width, which reads as a wall being carried rather than snow piling up. " +
                 "2 sends it mostly into crest LENGTH: the heap fans out sideways past the blade and " +
                 "stays low, which reads as a windrow forming.\n\n" +
                 "1.10 is the shipped value: with the two hips it holds the heap about four times wider " +
                 "than it is tall at EVERY size, so the growth is legible as growth (both dimensions " +
                 "move) without the silhouette ever becoming a tower.")]
        [Range(0f, 3f)]
        [SerializeField] private float _heapWidthPerHeight = 1.10f;

        [Tooltip("Hard cap on the crest HALF-length in metres, i.e. the other half of the capacity. Past " +
                 "this the heap can only grow taller, and once the height cap binds too, everything " +
                 "spills. 3.2 m is a 6.4 m crest against a 2.3 m blade - a heap already bulging well " +
                 "past the blade's ends, which is where a real plough starts shedding.")]
        [Range(0.3f, 8f)]
        [SerializeField] private float _heapMaxHalfWidthM = 3.2f;

        [Tooltip("How far AHEAD of the blade line the heap's crest sits, in metres. 'Centred slightly " +
                 "ahead of the blade', and the number is LOAD BEARING rather than aesthetic.\n\n" +
                 "THE CONSTRAINT: the back face runs peak/tan(backAngle) behind the crest and the swept " +
                 "cut reaches half the blade thickness ahead of the blade line, so\n\n" +
                 "    crestAhead  >  peak/tan(back) + bladeDepth/2\n\n" +
                 "or the heap's back toe hangs over the trench the blade just cut. It would not LEAK if " +
                 "it did - the erase runs before the cut, so nothing is double credited - but the toe " +
                 "would then stand on bare ground as a 30 cm cliff whose outer pairs are NOT both inside " +
                 "the heap, so they get the repose limit and relax drags heap material back into the " +
                 "cleared lane, where the erase's min() debits it for good. The pile would bleed backwards.\n\n" +
                 "At the shipped 1.60 m peak and 75 degree back face the run is 0.429 m against a 0.175 m " +
                 "half-thickness, so the constraint is crestAhead > 0.604; 0.75 leaves 0.146 m, i.e. 1.2 " +
                 "texels of clearance at the 12.5 cm cell. RAISE THIS IF YOU RAISE THE PEAK OR FLATTEN " +
                 "THE BACK FACE.")]
        [Range(0.05f, 3f)]
        [SerializeField] private float _heapCrestAheadM = 0.75f;

        [Tooltip("Angle of the LEADING face in degrees - the face being shoved into the snow. STEEPER " +
                 "THAN THE REPOSE ANGLE ON PURPOSE: it is under active compression from the blade, so " +
                 "it stands steeper than a settled pile would. 65 against a 55 degree repose is a " +
                 "visibly bulldozed face; set it to the repose angle for the A/B that shows what the " +
                 "steepness buys.\n\n" +
                 "Relax would flatten this back to repose in a few frames, which is what the relax " +
                 "GUARD below exists to prevent.")]
        [Range(20f, 85f)]
        [SerializeField] private float _heapFrontAngleDeg = 65f;

        [Tooltip("Angle of the BACK face in degrees - the blade side. Steeper than the leading face, " +
                 "because the blade is physically holding it. Steeper also means the back toe lands " +
                 "closer to the crest, which is what lets Heap Crest Ahead M stay small and keeps the " +
                 "heap off the trench.")]
        [Range(20f, 88f)]
        [SerializeField] private float _heapBackAngleDeg = 75f;

        [Tooltip("Slope limit in degrees that RELAX is allowed to use INSIDE the heap's own footprint. " +
                 "It has to be at least the steeper of the two faces or relax eats them; 80 leaves 5 " +
                 "degrees of margin over the shipped 75 degree back face.\n\n" +
                 "The guard is exactly mass conserving: the test is 'are BOTH texels of this neighbour " +
                 "pair inside the heap', which is symmetric in the pair, so the relax stays exactly " +
                 "antisymmetric. See the Relax kernel.")]
        [Range(10f, 89f)]
        [SerializeField] private float _heapGuardAngleDeg = 80f;

        [Tooltip("Enable the relax guard on the heap's footprint. OFF IS THE A/B AND IT IS WORTH " +
                 "RUNNING ONCE: with the guard off, relax flattens the leading face toward repose " +
                 "within a few frames, the erase's min() debits the difference to the field, and the " +
                 "pile visibly refuses to accumulate past a slab. That is the failure this switch " +
                 "exists to make visible.")]
        [SerializeField] private bool _heapRelaxGuard = true;

        // ------------------------------------------------------------------ THE RELEASE: overflow + cast
        [Header("RELEASE - the windrow you leave behind, as a RATE and not a threshold")]
        [Tooltip("How far BEHIND the blade line each release cone's centre sits, in metres. Behind, so a " +
                 "windrow is left in the vehicle's wake rather than in its path.")]
        [Range(0f, 4f)]
        [SerializeField] private float _spillBackM = 0.45f;

        [Tooltip("How far OUTSIDE the blade's half-width each release cone's centre sits, in metres. It " +
                 "must be at least the release radius, or the cone's inner half lands inside the blade's " +
                 "swept width and is re-cut on the next frame - not a leak (every path is booked) but a " +
                 "recirculation that buys nothing. The code takes max(this, radius) for exactly that " +
                 "reason; 0.95 against a 0.85 radius leaves 10 cm of clearance without the clamp biting.")]
        [Range(0f, 4f)]
        [SerializeField] private float _spillOutM = 0.95f;

        [Tooltip("Radius of each release cone in metres. Small, because the release is a continuous " +
                 "dribble rather than an event: it is re-emitted every frame, so the windrow is built out " +
                 "of hundreds of overlapping cones and its shape comes from the vehicle's path plus relax, " +
                 "not from any one cone.\n\n" +
                 "It is also what bounds _Carry[5]'s int32 headroom: two cones at the knob's 4 m top end " +
                 "sum to 1.29e8 fixed weight units, 16.6x under saturation.")]
        [Range(0.15f, 4f)]
        [SerializeField] private float _spillRadiusM = 0.85f;

        [Tooltip("Fill fraction at which snow STARTS curling off the blade ends, 0..1.\n\n" +
                 "THE RELEASE IS A RATE, NOT A THRESHOLD, and that is the correction over v7's first " +
                 "cut: a real plough sheds continuously, so the release rises smoothly from zero here to " +
                 "full at capacity and only then hands over to the hard cap. 0.35 means a blade a third " +
                 "full is already laying a thin windrow, which is what makes the windrow read as a " +
                 "consequence of ploughing rather than as an overflow event.\n\n" +
                 "0 is MEANINGFUL (shed from empty), so this takes a strictly negative sentinel.")]
        [Range(0f, 1f)]
        [SerializeField] private float _releaseStartFill = 0.35f;

        [Tooltip("Fraction of the LEDGER released per SECOND at capacity, ramped down to zero at Release " +
                 "Start Fill. A fraction rather than an absolute rate, because a fuller blade really does " +
                 "shed more, and because the emit's currency is fractions of the ledger.\n\n" +
                 "THE KNOB SETS THE EQUILIBRIUM FILL, and that is how to read it: the blade settles where " +
                 "the shed equals the intake. DERIVED against the shipped couplings (nothing here is " +
                 "measured), with the cruising speed in virgin snow solved from drive-vs-drag at each load:\n" +
                 "    rate/s   fill    pile     mass    v       windrow\n" +
                 "    0.00     100%    6.18 m3  1853 kg 1.70    0.47 m3/s   (v7's threshold behaviour)\n" +
                 "    0.15      79%    4.85 m3  1456 kg 1.97    0.55 m3/s   SHIPPED\n" +
                 "    0.30      67%    4.12 m3  1237 kg 2.17    0.60 m3/s\n" +
                 "    1.50      51%    3.13 m3   939 kg 2.50    0.69 m3/s\n" +
                 "0.15 keeps the accumulation story - the blade visibly fills to about four fifths and the " +
                 "mass coupling still reaches 79% of the range it had - while the windrow starts as a thin " +
                 "ribbon around half full and grows continuously from there. Raise it for a plough that " +
                 "runs light and windrows hard.\n\n" +
                 "The HARD cap above capacity is not this knob and cannot be turned off: past capacity the " +
                 "emit releases exactly the excess, so the heap is pinned however low this is set. 0 " +
                 "therefore restores v7's original threshold behaviour exactly and is the A/B, which is why " +
                 "this takes a strictly negative sentinel.")]
        [Range(0f, 3f)]
        [SerializeField] private float _releaseRatePerSec = 0.15f;

        // ------------------------------------------------------------------ THE VERBS
        [Header("THE VERBS - blade down/up, blade angle, forward/reverse")]
        [Tooltip("How far the blade is yawed in its LEFT and RIGHT states, in degrees. The cut footprint " +
                 "becomes a rectangle rotated by this, and the CAST rate is proportional to its sine.\n\n" +
                 "30 degrees is a working plough angle: sin 30 is 0.5, so half the travel speed goes into " +
                 "working snow along the blade toward the discharge end. Past about 45 the blade stops " +
                 "clearing its own width (the projected width is 2*halfWidth*cos(angle) + " +
                 "2*halfDepth*sin(angle), i.e. 2.17 m at 30 degrees against the 2.30 m blade, but only " +
                 "1.80 m at 45) and the lane narrows visibly.")]
        [Range(5f, 60f)]
        [SerializeField] private float _bladeAngleDeg = 30f;

        [Tooltip("THE CAST'S GAIN, dimensionless, and it is a RESIDENCE TIME argument rather than a " +
                 "fudge. Snow entering at the leading end of an angled blade is worked along the face at " +
                 "roughly speed * sin(angle) and leaves at the trailing end, so it lives on the blade for " +
                 "bladeWidth / (speed * sin(angle)) seconds and the blade discharges\n\n" +
                 "    castRate = pile * speed * sin(angle) / bladeWidth * this      [m3/s]\n\n" +
                 "THE EQUILIBRIUM SOLVES ITSELF, and it is the whole trade. Solving intake == discharge with " +
                 "the cruising speed taken from drive-vs-drag at each load (nothing here is measured):\n" +
                 "    eff    fill   pile      mass     v        windrow\n" +
                 "    0.0     79%   4.85 m3   1456 kg  1.97     0.55 m3/s   (straight, the A/B)\n" +
                 "    0.5     41%   2.51 m3    754 kg  2.77     0.76 m3/s\n" +
                 "    1.0     21%   1.27 m3    381 kg  3.53     0.97 m3/s   SHIPPED\n" +
                 "    2.0     10%   0.63 m3    190 kg  4.10     1.13 m3/s\n" +
                 "An angled blade at 1.0 therefore holds a FIFTH of what a straight one holds, windrows " +
                 "nearly twice as fast, and - because it is carrying so much less - drives 80% faster. " +
                 "THAT IS THE TRADE THE VERB EXISTS TO MAKE, and all three halves of it are legible at once. " +
                 "Lower this for a blade that still fills while casting; 0 disables the cast and leaves only " +
                 "the rotated cut, which is the A/B, so this takes a strictly negative sentinel.")]
        [Range(0f, 3f)]
        [SerializeField] private float _bladeCastEfficiency = 1f;

        [Tooltip("Degrees per second at which the LEADING FACE angle chases what the shove is asking for.\n\n" +
                 "THE FRONT FACE IS NOT A CONSTANT ANY MORE. It stands at Heap Front Angle Deg while the " +
                 "vehicle is actually shoving and collapses toward the REPOSE angle when the shove stops - " +
                 "which is what a pile of snow does when the thing holding it steps back. 25 deg/s covers " +
                 "the shipped 65 -> 55 degree span in 0.4 s, so lifting off has a visible settle and " +
                 "re-engaging has a visible re-steepen.\n\n" +
                 "CONSERVATION IS UNAFFECTED however fast this moves: the emit normalises by the weight " +
                 "sum it just scanned with these same tangents, so changing the face angle changes WHERE " +
                 "the ledger is placed and never HOW MUCH. 0 freezes the face at the shoved angle and is " +
                 "the A/B, so this takes a strictly negative sentinel.")]
        [Range(0f, 180f)]
        [SerializeField] private float _faceRelaxDegPerSec = 25f;

        // ------------------------------------------------------------------ push / berms
        [Header("Push and the berms beside the lane")]
        [Tooltip("Height the blade scrapes the snow down to, in metres above the ground.")]
        [Range(0f, 0.3f)]
        [SerializeField] private float _scrapeHeight = 0f;

        [Tooltip("Maximum distance one texel of snow travels in a single step, in metres. Sets the " +
                 "deposit gather radius, so it is the main cost knob for the push pass. v6 measured " +
                 "that this is NOT the berm bottleneck - the berm SHARE is - so raise Berm Share Of " +
                 "Loss before reaching for this.")]
        [Range(0.02f, 1.5f)]
        [SerializeField] private float _maxPushDist = 0.40f;

        [Tooltip("Extra distance past the footprint so deposited snow lands clear of it.")]
        [Range(0f, 0.2f)]
        [SerializeField] private float _pushMargin = 0.06f;

        [Tooltip("0 = every source texel lands right at its escape face, which piles the whole " +
                 "footprint into a one-texel wall that relax cannot chew through. 1 = the landing " +
                 "distance is smeared out to Max Push Dist by a hash of the source texel.")]
        [Range(0f, 1f)]
        [SerializeField] private float _pushSpread = 1f;

        [Tooltip("Fraction of the footprint half-width where sideways spill starts. 0 means every texel " +
                 "off the centre line escapes outward, which is what builds the berms flanking the lane.")]
        [Range(0f, 1f)]
        [SerializeField] private float _sideSpillStart = 0f;

        [Range(0f, 1f)]
        [SerializeField] private float _sideSpillStrength = 1f;

        [Tooltip("Fraction of the footprint half-width at which the berm's escape is FULLY sideways.\n\n" +
                 "For v7 this is not the measured correction it was for v6's disc - it is what a plough " +
                 "does. Anything thrown FORWARD would land inside the heap's own footprint, where it is " +
                 "re-cut next frame as free pile: booked, but a recirculation that buys nothing. 0.15 " +
                 "sends everything outside a narrow core sideways; 1.0 restores v5's blade blend and is " +
                 "the A/B.")]
        [Range(0.02f, 1f)]
        [SerializeField] private float _sideSpillFullAt = 0.15f;

        // ------------------------------------------------------------------ deliberate loss
        [Header("Deliberate loss - this snow is NOT mass conserving")]
        [Tooltip("Fraction of the volume the blade cuts OUT OF THE TERRAIN each step that survives onto " +
                 "the pile. The rest is DELETED and BOOKED.\n\n" +
                 "Charged on the TERRAIN cut and not on the total removal, and in v7 that is far more " +
                 "than a nicety: the pile is lifted off the field and put back sixty times a second, so " +
                 "charging the loss on the ledger or on the total removal would destroy 60% of the pile " +
                 "sixty times a second and it could never accumulate at all. The pile is not RE-CUT " +
                 "every frame, it is RE-PLACED, and only genuinely new terrain pays the toll.")]
        [Range(0.05f, 1f)]
        [SerializeField] private float _conservedFraction = 0.40f;

        [Tooltip("Of the volume the deliberate loss DESTROYS, the fraction squeezed out sideways as " +
                 "berms instead. 0 destroys all of it and the lane has clean edges; 1 puts all of it on " +
                 "the ground and the lane is flanked by windrows.\n\n" +
                 "Taken out of the LOSS rather than out of the ledger, deliberately, so the berms cost " +
                 "the pile's growth NOTHING. At the shipped 0.40 conserved and 0.35 here: of each cubic " +
                 "metre cut, 0.40 reaches the pile, 0.21 lands as berm and 0.39 is booked deleted.\n\n" +
                 "NOTE THE SIGN, because v6 measured it and it is counter-intuitive: the berm share is " +
                 "paid out of the LOSS pool, so RAISING the conserved fraction SHRINKS the berms.")]
        [Range(0f, 1f)]
        [SerializeField] private float _bermShareOfLoss = 0.35f;

        [Tooltip("Seconds for the pile to shed 1/e of itself with no inflow. 0 DISABLES it and 0 is the " +
                 "SHIPPED value.\n\n" +
                 "This is v5's leaky bucket, kept only as an A/B. v7 already has a physical drain - the " +
                 "capacity overflow - so an exponential one would be a second, invisible answer to the " +
                 "same question. The leaked volume is booked deleted, so the invariant is untouched " +
                 "either way.")]
        [Range(0f, 60f)]
        [SerializeField] private float _pileShedTauSeconds = 0f;

        [Tooltip("ABSOLUTE floor on the LEAK threshold, in litres. See Mass Tolerance Ppm: the " +
                 "effective tolerance is the LARGER of the two, and this one is what governs a small " +
                 "field where a relative bound would be tighter than the instrument's own noise.")]
        [Range(0.05f, 50f)]
        [SerializeField] private float _massToleranceL = 3f;

        [Tooltip("RELATIVE part of the LEAK threshold, in parts per million of the INITIAL volume, and " +
                 "it fixes a known v6 defect.\n\n" +
                 "v6 compared the invariant against an absolute 3 L on a 3,960,000 L field - 0.76 ppm - " +
                 "while the instrument's own quantisation floor scales WITH the total: the mirror is a " +
                 "float32 whose ULP at 3,960 m3 is 0.25 L, there are 211k mirror cells, and the real " +
                 "measured errors ran around 1 ppm. So v6's meter cried wolf on its own arithmetic. " +
                 "2 ppm of 3,960 m3 is 7.9 L, comfortably above the float noise and still one part in " +
                 "500,000 of the field - a leak that mattered would be orders of magnitude larger. Set " +
                 "this to 0 to force v6's absolute-only behaviour exactly.")]
        [Range(0f, 50f)]
        [SerializeField] private float _massTolerancePpm = 2f;

        [Header("Cut noise")]
        [Tooltip("How far the per-step cut budget is modulated by world-space noise, as a fraction. The " +
                 "budget shaves a mathematically flat floor, which reads as machined terrain.\n\n" +
                 "LOWER THAN V6'S 0.40, on purpose. v6 shaved a 4 cm skin, so 40% of it was 1.6 cm of " +
                 "wobble. v7 takes the whole 30 cm column, so 40% would leave up to 12 cm of snow " +
                 "standing in patches inside the nominal cleared width - which is precisely the " +
                 "stripeResidual defect v6 shipped with. 0.15 leaves at most 4.5 cm, which reads as a " +
                 "rough scraped floor rather than as missed snow.")]
        [Range(0f, 1f)]
        [SerializeField] private float _cutNoiseAmp = 0.15f;

        [Range(0.05f, 3f)]
        [SerializeField] private float _cutNoiseScaleM = 0.45f;

        // ------------------------------------------------------------------ relax
        [Header("Relax (angle of repose)")]
        [Range(0, 8)]
        [SerializeField] private int _relaxIterations = 4;

        [Tooltip("Angle of repose in degrees. The untouched slab is flat, so it has zero slope and this " +
                 "kernel cannot slump it at any angle. What it controls is how the lane walls, the " +
                 "berms, the SPILL WALLS and a DUMPED MOUND hold - and it is also the heap's FLANK " +
                 "angle, so it is what decides how far the heap fans out sideways at a given height.")]
        [Range(10f, 80f)]
        [SerializeField] private float _reposeAngle = 55f;

        [Tooltip("Fraction of the excess slope moved per neighbour pair per iteration, at 60 fps. " +
                 "8 neighbours * 0.5 * rate < 1 keeps the scheme stable, so this is hard capped at 0.24.")]
        [Range(0.01f, 0.24f)]
        [SerializeField] private float _relaxRate = 0.22f;

        [Header("Relax dispatch bounding")]
        [Tooltip("Dispatch the relax over the region that has actually changed instead of over the whole " +
                 "960 x 880 field. OFF reproduces the whole-field relax exactly and is the A/B. The " +
                 "window is a NO-FLUX WALL, so it is exactly mass conserving at any size.")]
        [SerializeField] private bool _relaxWindowEnabled = true;

        [Tooltip("How long a touched footprint keeps being relaxed after the vehicle has left it, in " +
                 "seconds. A DEPOSITED PILE is the tallest thing in the variant and takes the longest to " +
                 "settle - it is left standing with its leading face at the shove angle and no receipt, so " +
                 "the relax guard no longer protects it and it has 10 degrees of excess slope to shed. If " +
                 "the window expires first it freezes mid-slump, which reads as a bug in the deposit.")]
        [Range(0.05f, 8f)]
        [SerializeField] private float _relaxTrailSeconds = 2.5f;

        [Range(0, 64)]
        [SerializeField] private int _relaxWindowPadTexels = 6;

        [Header("Stepping")]
        [Range(0.005f, 0.2f)]
        [SerializeField] private float _maxStepSeconds = 0.05f;

        // ------------------------------------------------------------------ raymarch acceleration
        [Header("Raymarch acceleration (the empty-space skip)")]
        [Range(0.125f, 8f)]
        [SerializeField] private float _coarseCellM = 1f;

        [Range(1, 8)]
        [SerializeField] private int _coarseDilate = 2;

        [Header("Render-only dilation (perforation fix + the casual fillet source)")]
        [Range(0, 6)]
        [SerializeField] private int _heightDilateRadius = 1;

        [Range(0, 8)]
        [SerializeField] private int _filletDilateRadius = 2;

        // ------------------------------------------------------------------ shader property ids
        private static readonly int kSrcTex        = Shader.PropertyToID("_SrcTex");
        private static readonly int kDstTex        = Shader.PropertyToID("_DstTex");
        private static readonly int kRemovedSrc    = Shader.PropertyToID("_RemovedSrc");
        private static readonly int kRemovedDst    = Shader.PropertyToID("_RemovedDst");
        private static readonly int kMirrorTex     = Shader.PropertyToID("_MirrorTex");
        private static readonly int kFloorTex      = Shader.PropertyToID("_FloorTex");
        private static readonly int kHeapTex       = Shader.PropertyToID("_HeapTex");
        private static readonly int kHeapSrc       = Shader.PropertyToID("_HeapSrc");
        private static readonly int kDilateDst     = Shader.PropertyToID("_DilateDst");
        private static readonly int kCoarseSrc     = Shader.PropertyToID("_CoarseSrc");
        private static readonly int kCoarseDst     = Shader.PropertyToID("_CoarseDst");
        private static readonly int kStats         = Shader.PropertyToID("_Stats");
        private static readonly int kCarry         = Shader.PropertyToID("_Carry");

        private static readonly int kResX          = Shader.PropertyToID("_ResX");
        private static readonly int kResZ          = Shader.PropertyToID("_ResZ");
        private static readonly int kTexelSize     = Shader.PropertyToID("_TexelSize");
        private static readonly int kInvTexelSize  = Shader.PropertyToID("_InvTexelSize");
        private static readonly int kPatchMin      = Shader.PropertyToID("_PatchMin");
        private static readonly int kStartDepth    = Shader.PropertyToID("_StartDepth");

        private static readonly int kBladeSegCount = Shader.PropertyToID("_BladeSegCount");
        private static readonly int kBladeSeg      = Shader.PropertyToID("_BladeSeg");
        private static readonly int kBladeHalfWide = Shader.PropertyToID("_BladeHalfWidth");
        private static readonly int kBladeHalfDeep = Shader.PropertyToID("_BladeHalfDepth");
        private static readonly int kBladeAngleCos = Shader.PropertyToID("_BladeAngleCos");
        private static readonly int kBladeAngleSin = Shader.PropertyToID("_BladeAngleSin");
        private static readonly int kBladeCenter   = Shader.PropertyToID("_BladeCenter");
        private static readonly int kBladeFwd      = Shader.PropertyToID("_BladeFwd");
        private static readonly int kBladeRight    = Shader.PropertyToID("_BladeRight");
        private static readonly int kBladeHalf     = Shader.PropertyToID("_BladeHalf");
        private static readonly int kHeapCenter    = Shader.PropertyToID("_HeapCenter");
        private static readonly int kHeapFwd       = Shader.PropertyToID("_HeapFwd");
        private static readonly int kHeapRight     = Shader.PropertyToID("_HeapRight");
        private static readonly int kMaxCutStep    = Shader.PropertyToID("_MaxCutStep");
        private static readonly int kMaxPileStep   = Shader.PropertyToID("_MaxPileStep");
        private static readonly int kScrapeHeight  = Shader.PropertyToID("_ScrapeHeight");

        private static readonly int kHeapPeakM     = Shader.PropertyToID("_HeapPeakM");
        private static readonly int kHeapHalfCrest = Shader.PropertyToID("_HeapHalfCrestM");
        private static readonly int kHeapCrestAhd  = Shader.PropertyToID("_HeapCrestAheadM");
        private static readonly int kHeapTanFront  = Shader.PropertyToID("_HeapTanFront");
        private static readonly int kHeapTanBack   = Shader.PropertyToID("_HeapTanBack");
        private static readonly int kHeapTanFlank  = Shader.PropertyToID("_HeapTanFlank");
        private static readonly int kHeapFraction  = Shader.PropertyToID("_HeapFraction");
        private static readonly int kReleaseFrac   = Shader.PropertyToID("_ReleaseFraction");
        private static readonly int kReleaseCount  = Shader.PropertyToID("_ReleaseCount");
        private static readonly int kReleaseCenter = Shader.PropertyToID("_ReleaseCenter");
        private static readonly int kReleaseRadius = Shader.PropertyToID("_ReleaseRadius");
        private static readonly int kHeapReclaim   = Shader.PropertyToID("_HeapReclaim");
        private static readonly int kHeapWindow    = Shader.PropertyToID("_HeapWindow");
        private static readonly int kHeapEraseWin  = Shader.PropertyToID("_HeapEraseWindow");

        private static readonly int kMaxPushDist   = Shader.PropertyToID("_MaxPushDist");
        private static readonly int kPushMargin    = Shader.PropertyToID("_PushMargin");
        private static readonly int kPushSpread    = Shader.PropertyToID("_PushSpread");
        private static readonly int kSpillStart    = Shader.PropertyToID("_SideSpillStart");
        private static readonly int kSpillFullAt   = Shader.PropertyToID("_SideSpillFullAt");
        private static readonly int kSpillStrength = Shader.PropertyToID("_SideSpillStrength");
        private static readonly int kDepositRadius = Shader.PropertyToID("_DepositRadius");
        private static readonly int kWindow        = Shader.PropertyToID("_Window");

        private static readonly int kConservedFrac = Shader.PropertyToID("_ConservedFraction");
        private static readonly int kBermShare     = Shader.PropertyToID("_BermShareOfLoss");
        private static readonly int kPileShedTau   = Shader.PropertyToID("_PileShedTau");
        private static readonly int kStepDt        = Shader.PropertyToID("_StepDt");
        private static readonly int kCutNoiseAmp   = Shader.PropertyToID("_CutNoiseAmp");
        private static readonly int kCutNoiseScale = Shader.PropertyToID("_CutNoiseScale");

        private static readonly int kMaxDelta      = Shader.PropertyToID("_MaxDelta");
        private static readonly int kMaxDeltaDiag  = Shader.PropertyToID("_MaxDeltaDiag");
        private static readonly int kMaxDeltaHeap  = Shader.PropertyToID("_MaxDeltaHeap");
        private static readonly int kMaxDeltaHeapD = Shader.PropertyToID("_MaxDeltaHeapDiag");
        private static readonly int kHeapGuardOn   = Shader.PropertyToID("_HeapGuardOn");
        private static readonly int kRelaxRate     = Shader.PropertyToID("_RelaxRate");
        private static readonly int kRelaxWindow   = Shader.PropertyToID("_RelaxWindow");

        private static readonly int kMirrorBlock   = Shader.PropertyToID("_MirrorBlock");
        private static readonly int kMirrorResX    = Shader.PropertyToID("_MirrorResX");
        private static readonly int kMirrorResZ    = Shader.PropertyToID("_MirrorResZ");
        private static readonly int kCoarseResX    = Shader.PropertyToID("_CoarseResX");
        private static readonly int kCoarseResZ    = Shader.PropertyToID("_CoarseResZ");
        private static readonly int kCoarseBlock   = Shader.PropertyToID("_CoarseBlock");
        private static readonly int kCoarseDilateId = Shader.PropertyToID("_CoarseDilate");
        private static readonly int kHeightDilateR = Shader.PropertyToID("_HeightDilateRadius");
        private static readonly int kFilletDilateR = Shader.PropertyToID("_FilletDilateRadius");

        // ---- the lump bake ----------------------------------------------------------------------
        // The lattice knobs live on the RENDERER, because that is where the look is authored, and are
        // forwarded here once a frame by SnowRaymarchRendererV7.PushLumpBakeParams. They are pushed to
        // the compute shader from ONE place (BuildLumpBake) so a knob cannot reach the bake without
        // reaching the marcher's decode scale in the same frame.
        private static readonly int kLumpBakeDst   = Shader.PropertyToID("_LumpBakeDst");
        private static readonly int kLumpBakeSrc   = Shader.PropertyToID("_LumpBakeSrc");
        private static readonly int kDilateSrc     = Shader.PropertyToID("_DilateSrc");
        private static readonly int kLumpBakeResX  = Shader.PropertyToID("_LumpBakeResX");
        private static readonly int kLumpBakeResZ  = Shader.PropertyToID("_LumpBakeResZ");
        private static readonly int kLumpBakeWindow = Shader.PropertyToID("_LumpBakeWindow");
        private static readonly int kLumpMinSnowH  = Shader.PropertyToID("_LumpMinSnowHeight");
        private static readonly int kLumpRadiusM   = Shader.PropertyToID("_LumpRadiusM");
        private static readonly int kLumpSpacingM  = Shader.PropertyToID("_LumpSpacingM");
        private static readonly int kLumpSpacingInv = Shader.PropertyToID("_LumpSpacingInv");
        private static readonly int kLumpJitter    = Shader.PropertyToID("_LumpJitter");
        private static readonly int kLumpRadiusVary = Shader.PropertyToID("_LumpRadiusVary");
        private static readonly int kLumpGateInv   = Shader.PropertyToID("_LumpGateInv");
        private static readonly int kLumpReliefInv = Shader.PropertyToID("_LumpReliefInv");
        private static readonly int kLumpSlopeStr  = Shader.PropertyToID("_LumpSlopeStrength");

        private const int kThreadGroup = 8;
        // Matches kVolScale in the compute shader, which is COARSER than v6's 1e9 because v7's stats slots
        // sum THE WHOLE HEAP twice per step rather than one step's cut - see the note beside kVolScale.
        private const double kVolScaleInv = 1e-6;
        private const float kCarryScaleInv = 1e-7f;
        private const float kDeletedScaleInv = 1e-6f;
        private const int kStatsSlots = 16;
        private const int kCarrySlots = 12;
        private const int kMaxDepositRadius = 24;

        /// <summary>Maximum swept boxes, matching SNOW_MAX_BLADE_SEG in the compute shader.</summary>
        public const int MaxBladeSegments = 8;

        /// <summary>Maximum release cones, matching SNOW_MAX_RELEASE in the compute shader.</summary>
        public const int MaxReleaseCones = 3;

        // ------------------------------------------------------------------ THE BINDING AUDIT
        //
        // An RWStructuredBuffer or RWTexture that a kernel reads but that was never bound to THAT kernel
        // does not fail the dispatch: Unity logs at most a warning and the access lands wherever the slot
        // happens to resolve. On the parent variant that exact omission put a _Stats write into the carry
        // buffer, grew the field by 36% and printed a 17.9 kL LEAK, and nothing about it is visible in a
        // compile or in a reading of the kernel - because the kernel is correct.
        //
        // So the binding is not written beside the audit, it is GENERATED FROM IT. kKernelSpec is a
        // transcription of the KERNEL -> RESOURCE table in the compute shader's header, BindStatic binds
        // every non-ping-pong resource in that table once at resource-creation time, and the two
        // ping-pong textures are the only thing any dispatch site is allowed to pass. There is no other
        // SetTexture or SetBuffer in this file, so a kernel cannot be given a resource the table does not
        // list, and cannot be denied one it does.
        private const int R_SRC     = 1 << 0;
        private const int R_DST     = 1 << 1;
        private const int R_REMSRC  = 1 << 2;
        private const int R_REMDST  = 1 << 3;
        private const int R_FLOOR   = 1 << 4;
        private const int R_MIRROR  = 1 << 5;
        private const int R_DILATE  = 1 << 6;
        private const int R_CSRC    = 1 << 7;
        private const int R_CDST    = 1 << 8;
        private const int R_STATS   = 1 << 9;
        private const int R_CARRY   = 1 << 10;
        private const int R_DILSRC  = 1 << 11;   // the dilate texture as a READ-ONLY view, for the bake
        private const int R_LUMPSRC = 1 << 12;   // the baked lump lift, read by the coarse-max build
        private const int R_LUMPDST = 1 << 13;   // the baked lump lift, written by LumpBake
        private const int R_HEAPDST = 1 << 14;   // THE HEAP RECEIPT, read/written by erase and emit
        private const int R_HEAPSRC = 1 << 15;   // the same texture, READ-ONLY, for the relax guard

        private struct KernelSpec
        {
            public string Name;
            public int Res;
            public KernelSpec(string name, int res) { Name = name; Res = res; }
        }

        private static readonly KernelSpec[] kKernelSpec =
        {
            new KernelSpec("Init",            R_DST | R_REMDST | R_FLOOR | R_HEAPDST),
            new KernelSpec("ClearStats",      R_STATS),
            new KernelSpec("HeapErase",       R_DST | R_HEAPDST | R_STATS | R_CARRY),
            new KernelSpec("Push",            R_SRC | R_DST | R_REMDST | R_FLOOR | R_STATS | R_CARRY),
            new KernelSpec("Settle",          R_STATS | R_CARRY),
            new KernelSpec("Deposit",         R_SRC | R_DST | R_REMSRC | R_STATS | R_CARRY),
            new KernelSpec("HeapBegin",       R_CARRY),
            new KernelSpec("HeapScan",        R_CARRY),
            new KernelSpec("HeapEmit",        R_DST | R_HEAPDST | R_STATS | R_CARRY),
            new KernelSpec("HeapFinish",      R_CARRY),
            new KernelSpec("Relax",           R_SRC | R_DST | R_HEAPSRC),
            new KernelSpec("CopyRect",        R_SRC | R_DST),
            new KernelSpec("Downsample",      R_SRC | R_MIRROR),
            new KernelSpec("HeightMax",       R_SRC | R_STATS),
            new KernelSpec("HeightDilate",    R_SRC | R_DILATE),
            new KernelSpec("CoarseMaxBlock",  R_SRC | R_CDST | R_LUMPSRC),
            new KernelSpec("CoarseMaxDilate", R_CSRC | R_CDST),
            new KernelSpec("LumpBake",        R_SRC | R_DILSRC | R_LUMPDST),
        };

        // Slot indices into kKernelSpec / _kernel, named so a dispatch site reads like the table.
        private const int K_INIT = 0, K_CLEAR_STATS = 1, K_HEAP_ERASE = 2, K_PUSH = 3, K_SETTLE = 4,
                          K_DEPOSIT = 5, K_HEAP_BEGIN = 6, K_HEAP_SCAN = 7, K_HEAP_EMIT = 8,
                          K_HEAP_FINISH = 9, K_RELAX = 10, K_COPY_RECT = 11, K_DOWNSAMPLE = 12,
                          K_HEIGHT_MAX = 13, K_HEIGHT_DILATE = 14, K_COARSE_BLOCK = 15,
                          K_COARSE_DILATE = 16, K_LUMP_BAKE = 17;

        // Sized FROM the table rather than to a literal, so appending a kernel cannot leave this one
        // short - which would be an IndexOutOfRange at startup rather than a silent misbinding, but
        // there is no reason to have the failure mode at all.
        private readonly int[] _kernel = new int[kKernelSpec.Length];
        private string _bindAuditText = "unbuilt";

        /// <summary>The kernel -> resource table as one line, printed on the first [V7] console line.</summary>
        public string BindAuditText => _bindAuditText;

        // ------------------------------------------------------------------ runtime state
        private ComputeShader _cs;

        private RenderTexture _heightA;
        private RenderTexture _heightB;
        private RenderTexture _removed;
        private RenderTexture _mirrorRt;
        private RenderTexture _floorRt;
        private RenderTexture _coarseBlockRt;
        private RenderTexture _coarseMaxRt;
        private RenderTexture _dilatedRt;

        /// <summary>
        /// THE HEAP'S RECEIPT: per texel, the fixed-point integer count HeapEmit added there as heap.
        ///
        /// RFloat holding an INTEGER, which is exact under 2^24 fixed units - 1.678 m3 per texel, i.e.
        /// 107 m of height at the shipped 12.5 cm cell. <see cref="HeapHeightExactLimitM"/> is that bound
        /// expressed as a height, and the heap's maximum is clamped against it with a warning, because at
        /// a 1 m cell the same bound is only 1.68 m.
        ///
        /// POINT FILTERED and never sampled by anything but the three kernels that own it: it carries a
        /// count HeapErase has to read back bit for bit, and a bilinear tap of a receipt is a receipt for
        /// a different amount.
        /// </summary>
        private RenderTexture _heapRt;

        // THE BAKED LUMP LIFT, at exactly 2x the field resolution and single channel 8-bit: 1920 x 1760
        // at 6.25 cm over the 120 x 110 m patch, 3.4 MB. Twice the field and not the field's own
        // resolution because a 30 cm lump is 4.8 texels across at 6.25 cm and only 2.4 at 12.5 cm, and
        // 2.4 texels of a rounded cap reads as a block, not a lobe.
        private RenderTexture _lumpBakeRt;

        private GraphicsBuffer _statsBuffer;
        private GraphicsBuffer _carryBuffer;

        private int _resX, _resZ;
        private int _mirrorResX, _mirrorResZ;
        private int _mirrorBlock;
        private float _texelSize;
        private float _extentX, _extentZ;
        private Vector2 _patchMin;
        private int _coarseResX, _coarseResZ;
        private int _coarseBlock;

        private float[] _mirror;
        private int[] _statsCpu;
        private int[] _carryCpu;
        private bool _mirrorRequested;
        private bool _statsRequested;
        private bool _carryRequested;
        private bool _mirrorValid;
        private int _frame;

        private readonly Vector4[] _segments = new Vector4[MaxBladeSegments];
        private int _segCount = 1;

        private readonly Vector4[] _releaseCones = new Vector4[MaxReleaseCones];
        private int _releaseConeCount;

        private float _mirrorVolume;
        private float _initialVolume;
        private double _removedLast;
        private double _depositedLast;
        private double _terrainCutLast;
        private double _bermLast;
        private double _releaseDepositedLast;
        private double _heapReclaimedLast;
        private double _heapEmittedLast;
        private float _fieldMaxLast;

        private static readonly int[] kZeroCarry = new int[kCarrySlots];

        // THE LEDGER, as read back. This is the number the INVARIANT uses, and after HeapEmit it is
        // fixed-point dust: the pile is FIELD MASS. See the class header.
        private float _ledgerL;
        private float _deletedL;
        private float _deletedStepL;

        // THE STANDING GUARD, in litres. _Stats[3]: fixed-point mass a placement kernel resolved for a
        // texel it could not write. Zero by construction - HeapScan's normaliser and HeapEmit's writable
        // set are the SAME FieldWritable test - and published beside the invariant so that a future emit
        // channel which normalises over the full footprint and writes only the in-field part reports
        // itself immediately and at full size, instead of as a slow invariant drift minutes later.
        private float _unplacedL;
        private float _unplacedPeakL;
        private float _releaseTotalM3;
        private float _lastDt = 1f / 60f;

        // ---- the pile ------------------------------------------------------------------------
        //
        // _pileRawM3 is _Carry[10] as read back: the ledger as it stood the moment Settle finished, i.e.
        // AFTER the erase credited the old heap and the cut credited new terrain and the deliberate loss
        // was taken, and BEFORE the emit spent any of it. That is the pile's volume, measured on the GPU.
        //
        // IT CANNOT BE _Carry[3] (what the emit placed), and the reason is a deadlock rather than a
        // preference: on the first frames of a run nothing has been placed, so the CPU would solve a zero
        // height, ComputeEmitWindow would find a degenerate footprint, the emit would be skipped, and
        // nothing would ever be placed. See the Settle kernel.
        //
        // _heapPlacedM3 is _Carry[3] and is kept as INDEPENDENT EVIDENCE: it is what the emit actually
        // wrote as recorded heap, so heapPlaced / pile should equal HeapFractionLastStep, and the console
        // prints both so the parent agent can check that identity rather than take it on trust.
        private float _pileRawM3;
        private float _heapPlacedM3;
        private float _pileHeightM;
        private float _pileHalfCrestM;
        private float _pileCapacityM3;
        private float _heapFractionLast = 1f;

        // ---- THE VERBS, as resolved for the last step -----------------------------------------
        private bool _bladeDownLast = true;
        private int _angleStateLast;
        private bool _attachedLast;
        private float _push01Last;

        // THE LEADING FACE'S LIVE ANGLE, which is state rather than a knob: it chases the shove and
        // collapses toward repose when the shove stops. Seeded at the shoved angle so the first frame of a
        // run is not a settle.
        private float _faceAngleDeg = 65f;
        private float _faceSteepen01;

        // ---- the CAST and the DEPOSIT ---------------------------------------------------------
        private float _castFracLast;
        private double _castRateLast;
        private float _releaseFracLast;
        private double _releaseRateCommandedLast;

        private bool _depositRanThisStep;
        private int _depositCount;
        private float _lastDepositedM3;

        // DEPOSIT LATENCY COMPENSATION, v6's dump compensation in shape and for exactly the same reason.
        // The pile volume arrives over AsyncGPUReadback several frames late, so without this the vehicle
        // would keep its old mass for ~67 ms after the heap detached, which on a step change of nearly two
        // tonnes reads as the input having been dropped.
        //
        // GROWTH does NOT need this and deliberately does not get it: growth is smooth, so a few frames of
        // lag is invisible, and a predictor on a smooth signal is just a second source of truth. Only the
        // deposit is a discontinuity, so only the deposit is compensated.
        //
        // The correction is "volume the CPU believes has left the pile SINCE the read-back in hand was
        // issued", a difference of two running totals rather than a flag - which is what makes it correct
        // when several deposits overlap one read-back, and self-healing: the moment no deposit falls
        // between a request and its completion the two totals are equal and the correction is exactly
        // zero, so an inaccurate estimate cannot accumulate.
        private double _depositTotalM3;
        private double _depositTotalAtRequest;
        private double _depositTotalSeenByRead;

        // ---- the heap's recorded window ------------------------------------------------------
        //
        // THE RECEIPT'S EXTENT, and it is STORED rather than recomputed. HeapEmit writes _heapRt only
        // inside the window it was dispatched over, so erasing exactly that window - clamped to the
        // field, because the emit bounds-checks - is provably sufficient. Recomputing the heap's shape at
        // the previous pose instead would need last frame's pose, last frame's peak and last frame's
        // crest length all kept in step, and the first one to fall out of step would leave a receipt
        // outstanding: the field would keep that mass while the ledger also still counted it, which is
        // the one way this design could print LEAK.
        private RectInt _heapEraseRect;
        private RectInt _heapWindowRect;
        private int _heapWindowTexels;

        // ---- relax activity trail ----------------------------------------------------------------
        private const int kTrailCapacity = 1024;
        private readonly RectInt[] _trailRect = new RectInt[kTrailCapacity];
        private readonly float[] _trailTime = new float[kTrailCapacity];
        private int _trailHead;
        private int _trailCount;
        private float _trailClock;

        private RectInt _relaxRect;
        private int _relaxTexels;
        private int _relaxDispatchesLast;

        // ---- the lump bake ------------------------------------------------------------------------
        // The lattice parameters as forwarded by the renderer, plus the window the last bake covered.
        // _lumpBakeAllDirty forces the next bake to cover the WHOLE texture: set on allocation, on a
        // field reset, and whenever any lattice parameter changes, because a windowed bake would
        // otherwise leave most of the field holding lobes built from the previous knob values.
        private float _lumpBakeRadiusM;
        private float _lumpBakeSpacingM = 0.35f;
        private float _lumpBakeJitter = 0.35f;
        private float _lumpBakeRadiusVary = 0.25f;
        private float _lumpBakeGateDepthM = 0.10f;
        private float _lumpBakeReliefM = 0.10f;
        private float _lumpBakeSlopeStrength = 0.75f;
        private float _lumpBakeMinSnowHeightM = 0.005f;
        private bool _lumpBakeAllDirty = true;

        private RectInt _lumpBakeRect;
        private int _lumpBakeTexels;
        private int _lumpBakeDispatchesLast;

        private readonly System.Diagnostics.Stopwatch _watch = new System.Diagnostics.Stopwatch();
        private double _stepMs;

        // ------------------------------------------------------------------ public surface
        public bool Ready => _cs != null && _heightA != null;
        public RenderTexture CurrentHeightTexture => _heightA;
        public RenderTexture CoarseMaxTexture => _coarseMaxRt;
        public RenderTexture DilatedHeightTexture => _dilatedRt;
        public RenderTexture LumpBakeTexture => _lumpBakeRt;

        public float CoarseSafeRadiusM => Mathf.Clamp(_coarseDilate, 1, 8) * _coarseBlock * _texelSize;
        public int CoarseResolutionX => _coarseResX;
        public int CoarseResolutionZ => _coarseResZ;
        public float CoarseCellM => _coarseBlock * _texelSize;

        public int ResolutionX => _resX;
        public int ResolutionZ => _resZ;
        public int MirrorResolutionX => _mirrorResX;
        public int MirrorResolutionZ => _mirrorResZ;
        public float TexelSize => _texelSize;
        public float PatchSizeX => _extentX;
        public float PatchSizeZ => _extentZ;
        public float GroundY => _groundY;
        public Vector2 PatchMin => _patchMin;
        public Vector2 PatchCenter => _patchMin + new Vector2(_extentX * 0.5f, _extentZ * 0.5f);
        public float StartDepth => _startDepth;
        public float ReposeAngle => _reposeAngle;
        public float FieldMaxHeight => _fieldMaxLast;
        public int FieldTexels => _resX * _resZ;
        public int RelaxWindowTexels => _relaxTexels;
        public RectInt RelaxWindowRect => _relaxRect;
        public bool RelaxWindowEnabled => _relaxWindowEnabled;
        public int RelaxDispatchesLastStep => _relaxDispatchesLast;
        public long RelaxTapsLastStep => (long)_relaxTexels * 8L * Mathf.Clamp(_relaxIterations, 0, 8);
        public long RelaxTapsWholeField => (long)FieldTexels * 8L * Mathf.Clamp(_relaxIterations, 0, 8);

        // ---- the lump bake, for the [V7] telemetry line ------------------------------------------
        public int LumpBakeResolutionX => _resX * 2;
        public int LumpBakeResolutionZ => _resZ * 2;
        public int LumpBakeTexels => _resX * _resZ * 4;
        public RectInt LumpBakeWindowRect => _lumpBakeRect;
        public int LumpBakeWindowTexels => _lumpBakeTexels;
        public int LumpBakeDispatchesLastStep => _lumpBakeDispatchesLast;
        public int BladeSegmentsLastStep => _segCount;
        public double LastStepMilliseconds => _stepMs;
        public float MirrorVolume => _mirrorVolume;
        public float InitialVolume => _initialVolume;
        public bool MirrorValid => _mirrorValid;
        public float MaxStepSeconds => _maxStepSeconds;
        public float ConservedFraction => _conservedFraction;
        public float BermShareOfLoss => _bermShareOfLoss;
        public float PileShedTauSeconds => _pileShedTauSeconds;
        public float CutNoiseAmp => _cutNoiseAmp;
        public float PickupDepthM => _pickupDepthM;
        public float SnowDensityKgPerM3 => _snowDensityKgPerM3;
        public float BladeWidthM => _bladeWidthM;
        public float BladeDepthM => _bladeDepthM;
        public float SideSpillFullAt => _sideSpillFullAt;

        /// <summary>Swept-box tiling count, so the owner can build the sweep from the field's knob.</summary>
        public int BladeSegments => _bladeSegments;

        // ---- THE HEAP, for the telemetry line and for the handling --------------------------------

        /// <summary>
        /// THE PILE'S VOLUME in cubic metres, as the CAR, the SHAPE SOLVE and the telemetry see it: the
        /// ledger at the one moment it is both complete and unspent - the end of Settle - minus a DEPOSIT
        /// that has been issued but that no read-back has caught up with yet.
        ///
        /// A GPU MEASUREMENT, not a CPU integration. The pile is field mass, so there is no ledger to
        /// read it out of - and that is a feature: this number cannot drift away from what is on screen,
        /// because it IS the sum of what was put on screen.
        ///
        /// Deliberately NOT what <see cref="MassInvariantErrorL"/> uses. The invariant has to compare
        /// like with like - a ledger and a field mirror that lag together - and subtracting a predicted
        /// deposit from one side of it would print a spurious LEAK for the few frames between the deposit
        /// and the read-back that observes it.
        /// </summary>
        public float PileVolumeM3
        {
            get
            {
                double v = _pileRawM3 - (_depositTotalM3 - _depositTotalSeenByRead);
                return (float)System.Math.Max(0.0, v);
            }
        }

        /// <summary>Pile mass in kilogrammes. This is what the vehicle's handling is actually driven from.</summary>
        public float PileMassKg => PileVolumeM3 * Mathf.Max(1f, _snowDensityKgPerM3);

        /// <summary>
        /// Volume the emit actually wrote as RECORDED HEAP, in cubic metres - independent evidence that the
        /// capacity split did what it was told. heapPlaced / pile should equal
        /// <see cref="HeapFractionLastStep"/>; both are printed so that identity can be checked rather than
        /// assumed.
        /// </summary>
        public float HeapPlacedM3 => _heapPlacedM3;

        /// <summary>The heap's authored peak height this step, in metres above the field it sits on.</summary>
        public float PileHeightM => _pileHeightM;

        /// <summary>The heap's flat crest length across the blade, in metres.</summary>
        public float PileCrestWidthM => _pileHalfCrestM * 2f;

        /// <summary>
        /// The heap's FULL footprint width including both hips, in metres: the crest plus
        /// peak/tan(repose) on each side. This is the number to compare against the height when asking
        /// "is it wider than it is tall", and against the blade when asking "is it bulging past it".
        /// </summary>
        public float PileFootprintWidthM =>
            _pileHalfCrestM * 2f + 2f * _pileHeightM / Mathf.Max(0.05f, TanRepose);

        /// <summary>What the heap's height and crest caps can contain, in cubic metres.</summary>
        public float PileCapacityM3 => _pileCapacityM3;

        /// <summary>Pile volume as a fraction of capacity. Above 1 the excess is spilling sideways.</summary>
        public float PileFill01 => PileVolumeM3 / Mathf.Max(1e-4f, _pileCapacityM3);

        /// <summary>Fraction of the ledger that went into the RECORDED heap. Below 1 means overflowing.</summary>
        public float HeapFractionLastStep => _heapFractionLast;

        /// <summary>
        /// True while a blade that IS CARRYING is nonetheless sending part of the ledger to the release
        /// channel - because the fill is high enough to shed, or because it is angled and casting.
        ///
        /// The attachment test is what keeps this honest. An unattached blade has heapFrac 0 by definition,
        /// so without it the [V7] line would read OVERFLOWING every time the vehicle stopped or raised the
        /// blade, which is exactly when it is carrying nothing at all.
        /// </summary>
        public bool Overflowing => _attachedLast && _heapFractionLast < 0.999f;

        /// <summary>Volume HeapErase lifted back off the field on the last read-back step, in m3.</summary>
        public double HeapReclaimedLastStep => _heapReclaimedLast;

        /// <summary>Volume HeapEmit wrote as heap on the last read-back step, in m3.</summary>
        public double HeapEmittedLastStep => _heapEmittedLast;

        /// <summary>Volume released (overflow + cast) on the last read-back step, in m3.</summary>
        public double ReleaseDepositedLastStep => _releaseDepositedLast;

        /// <summary>Release rate in m3/s as MEASURED by the emit: how fast the windrow is being built.</summary>
        public double ReleaseRateM3PerSec => _releaseDepositedLast / Mathf.Max(1e-5f, _lastDt);

        /// <summary>Cumulative volume released to the field since the last reset, in m3.</summary>
        public float ReleaseTotalM3 => _releaseTotalM3;

        public RectInt HeapWindowRect => _heapWindowRect;
        public int HeapWindowTexels => _heapWindowTexels;
        public bool HeapRelaxGuard => _heapRelaxGuard;
        public float HeapGuardAngleDeg => _heapGuardAngleDeg;
        public float HeapFrontAngleDeg => _heapFrontAngleDeg;
        public float HeapBackAngleDeg => _heapBackAngleDeg;
        public float HeapWidthPerHeight => _heapWidthPerHeight;
        public float HeapMaxHeightM => _heapMaxHeightM;
        public float HeapCrestAheadM => _heapCrestAheadM;

        /// <summary>Raw read-back ledger in litres. For the invariant only; see the class header.</summary>
        public float LedgerLitres => _ledgerL;

        // ---- THE VERBS, published ------------------------------------------------------------------

        /// <summary>Verb 1 as it stood on the last step: true = ploughing, false = transit.</summary>
        public bool BladeDownLastStep => _bladeDownLast;

        /// <summary>Verb 2 as it stood on the last step: -1 LEFT, 0 STRAIGHT, +1 RIGHT.</summary>
        public int BladeAngleStateLastStep => _angleStateLast;

        /// <summary>The blade angle knob in degrees. 0 while the blade is STRAIGHT.</summary>
        public float BladeAngleAppliedDeg => _angleStateLast == 0 ? 0f : _bladeAngleDeg;

        /// <summary>Verb 3 as it stood on the last step: was the blade's face supporting the heap.</summary>
        public bool BladeAttachedLastStep => _attachedLast;

        /// <summary>
        /// THE LEADING FACE'S LIVE ANGLE in degrees - shoved at <see cref="HeapFrontAngleDeg"/> while the
        /// vehicle is pushing, collapsing toward <see cref="ReposeAngle"/> when it stops or reverses.
        /// </summary>
        public float FaceAngleDeg => _faceAngleDeg;

        /// <summary>
        /// How hard the face is RE-STEEPENING right now, 0..1, scaled by how full the blade is. This is
        /// what the vehicle turns into the brief extra resistance of breaking the pile loose, and it is
        /// nonzero only while the face is actually being pushed back up toward the shove angle.
        /// </summary>
        public float FaceSteepen01 => _faceSteepen01;

        /// <summary>
        /// THE CAST RATE in m3/s: volume the angled blade is working along its face and discharging at the
        /// trailing end. Zero with the blade straight, zero on bare ground, and proportional to speed and
        /// to what is on the blade - which is why the vehicle's cast reaction grows in deep snow and
        /// vanishes on a clear lane.
        /// </summary>
        public double CastRateM3PerSec => _castRateLast;

        /// <summary>Fraction of the ledger the cast took this step, before the overflow term.</summary>
        public float CastFractionLastStep => _castFracLast;

        /// <summary>Fraction of the ledger the release channel was COMMANDED to take this step.</summary>
        public float ReleaseFractionLastStep => _releaseFracLast;

        /// <summary>The COMMANDED release rate in m3/s, i.e. cast plus overflow before the emit runs.</summary>
        public double ReleaseRateCommandedM3PerSec => _releaseRateCommandedLast;

        /// <summary>How many times the heap has been DEPOSITED - left standing - since the last reset.</summary>
        public int DepositCount => _depositCount;

        /// <summary>Volume left standing by the last deposit, in m3.</summary>
        public float LastDepositedM3 => _lastDepositedM3;

        /// <summary>True on a step where the heap was deposited, i.e. the receipt was retired.</summary>
        public bool DepositRanLastStep => _depositRanThisStep;

        /// <summary>Volume the blade cut out of the TERRAIN per second, before the deliberate loss.</summary>
        public double TerrainCutRateM3PerSec => _terrainCutLast / Mathf.Max(1e-5f, _lastDt);

        /// <summary>
        /// Volume reaching the PILE per second, in m3/s.
        ///
        /// NOTE WHAT IS AND IS NOT IN IT. _removedLast is Push's total, and Push never sees the heap -
        /// HeapErase lifted it off the field first, and credited it straight to _Carry[0] without
        /// touching _Stats[0]. So this is the genuine new cut minus the deliberate loss charged on its
        /// terrain share, and it does NOT include the heap being re-placed.
        /// </summary>
        public double PickupRateM3PerSec =>
            (_removedLast - (1.0 - Mathf.Clamp01(_conservedFraction)) * _terrainCutLast)
            / Mathf.Max(1e-5f, _lastDt);

        public double CutRateM3PerSec => _removedLast / Mathf.Max(1e-5f, _lastDt);
        public double BermRateM3PerSec => _bermLast / Mathf.Max(1e-5f, _lastDt);
        public double RemovedVolumeLastStep => _removedLast;
        public double DepositedVolumeLastStep => _depositedLast;

        /// <summary>Cumulative volume deliberately destroyed since the last reset, in litres.</summary>
        public float DeletedLitres => _deletedL;
        public float DeletedLastStepL => _deletedStepL;

        /// <summary>
        /// THE EFFECTIVE LEAK THRESHOLD in litres: the larger of the absolute floor and the relative
        /// bound. See <see cref="_massTolerancePpm"/> for why v6's absolute-only 3 L was a defect.
        /// </summary>
        public float MassToleranceL =>
            Mathf.Max(_massToleranceL, _massTolerancePpm * 1e-6f * _initialVolume * 1000f);

        /// <summary>The absolute floor, reported separately so the console can say which bound is live.</summary>
        public float MassToleranceAbsoluteL => _massToleranceL;
        public float MassTolerancePpm => _massTolerancePpm;

        /// <summary>
        /// THE CONSERVATION INVARIANT, in litres:
        /// <code>
        ///     invariant = (field + carried + deleted) - initial
        /// </code>
        /// v7's hardest test of it, and the reason it is the first acceptance criterion: the pile is
        /// lifted off the field and put back down SIXTY TIMES A SECOND. Both halves of that transfer are
        /// in this expression, so an erase that credited a millilitre more than it subtracted - or an
        /// emit that placed a millilitre more than it debited - shows up here at full size and prints
        /// LEAK within seconds rather than accumulating invisibly.
        ///
        /// `carried` reads near zero and that is correct, not slack: the pile IS field mass, so it is
        /// counted in the first term. Any unbooked creation or destruction anywhere still lands in
        /// `field` at full size.
        /// </summary>
        public float MassInvariantErrorL =>
            (_mirrorVolume * 1000f + _ledgerL + _deletedL) - (_initialVolume * 1000f);

        public bool MassLeaking => Mathf.Abs(MassInvariantErrorL) > MassToleranceL;

        /// <summary>
        /// UNPLACED, in litres: mass a placement kernel resolved for a destination it could not write,
        /// last step. THE STANDING GUARD, and it reads 0.0000 forever when the emit is right.
        ///
        /// WHY IT EXISTS AND WHY IT IS NOT THE INVARIANT AGAIN. The invariant catches an unbooked
        /// transfer, but only once enough of them have accumulated to clear a 7.9 L proportional
        /// tolerance - which for the berm leak this replaced took three minutes at 46 mL/s. This reads the
        /// FAULT rather than its integral: HeapScan sums a texel's weight into the normaliser if and only
        /// if HeapEmit can write that texel (one <c>FieldWritable</c> test, used by both), so a share
        /// resolved for an unwritable texel is exactly zero. An emit channel added later that normalises
        /// over the full footprint and writes only the in-field part resolves a nonzero share off-patch
        /// and lands here, in litres, on the first frame its footprint touches the patch edge.
        ///
        /// Deposit deliberately has no term here: it debits the ledger with the same integer it adds to
        /// the field, so anything its gather declines stays CARRIED and the invariant counts it. There is
        /// no prediction left anywhere in the mass path to go wrong.
        /// </summary>
        public float UnplacedLitres => _unplacedL;

        /// <summary>Worst <see cref="UnplacedLitres"/> since the last reset, so a one-frame spike cannot
        /// scroll past unseen between two console lines.</summary>
        public float UnplacedPeakLitres => _unplacedPeakL;

        /// <summary>
        /// The heap height, in metres, above which the receipt texture's float32 integer exactness would
        /// break: 2^24 fixed units is 1.678 m3 per texel, so this is 1.678 / texelArea. At the shipped
        /// 12.5 cm cell it is 107 m and non-binding; at a 1 m cell it is 1.68 m and binds.
        /// </summary>
        public float HeapHeightExactLimitM => 1.678f / Mathf.Max(1e-6f, _texelSize * _texelSize);

        private float TanRepose => Mathf.Tan(Mathf.Clamp(_reposeAngle, 10f, 80f) * Mathf.Deg2Rad);

        public int RelaxIterations
        {
            get => _relaxIterations;
            set => _relaxIterations = Mathf.Clamp(value, 0, 8);
        }

        /// <summary>
        /// Snow height in metres above the ground, from the CPU-side mirror. The only sampler gameplay
        /// code is allowed to use: it never touches a RenderTexture, matching the real project's rule.
        /// </summary>
        public float HeightAt(Vector3 world)
        {
            if (!_mirrorValid || _mirror == null) return 0f;

            float cell = _texelSize * _mirrorBlock;
            float u = (world.x - _patchMin.x) / cell - 0.5f;
            float v = (world.z - _patchMin.y) / cell - 0.5f;

            int x0 = Mathf.Clamp(Mathf.FloorToInt(u), 0, _mirrorResX - 1);
            int y0 = Mathf.Clamp(Mathf.FloorToInt(v), 0, _mirrorResZ - 1);
            int x1 = Mathf.Min(x0 + 1, _mirrorResX - 1);
            int y1 = Mathf.Min(y0 + 1, _mirrorResZ - 1);

            float fx = Mathf.Clamp01(u - x0);
            float fy = Mathf.Clamp01(v - y0);

            float h00 = _mirror[y0 * _mirrorResX + x0];
            float h10 = _mirror[y0 * _mirrorResX + x1];
            float h01 = _mirror[y1 * _mirrorResX + x0];
            float h11 = _mirror[y1 * _mirrorResX + x1];

            return Mathf.Lerp(Mathf.Lerp(h00, h10, fx), Mathf.Lerp(h01, h11, fx), fy);
        }

        /// <summary>Clamps a world XZ point to the patch, inset by <paramref name="marginM"/>.</summary>
        public Vector2 ClampToPatch(Vector2 world, float marginM)
        {
            float m = Mathf.Max(0f, marginM);
            return new Vector2(
                Mathf.Clamp(world.x, _patchMin.x + m, _patchMin.x + _extentX - m),
                Mathf.Clamp(world.y, _patchMin.y + m, _patchMin.y + _extentZ - m));
        }

        // ------------------------------------------------------------------ overrides
        //
        // This component is added at RUNTIME by the bootstrap, so it has no inspector while the editor is
        // stopped and the Unity CLI refuses component edits during play. A knob that lives only here is a
        // knob nobody can reach.
        //
        // Any argument below its legal minimum means "leave this component's own serialized value alone".
        // Where 0 is a MEANINGFUL value the sentinel is strictly negative, and that is called out at each
        // site.

        public void ApplyGeometryOverrides(float originX, float originZ,
                                           float sizeX, float sizeZ, float cellSizeM,
                                           int mirrorDownsample)
        {
            // The origin is legitimately NEGATIVE, so it cannot use a negative sentinel. NaN is the
            // "leave alone" value here.
            if (!float.IsNaN(originX)) _patchOriginX = originX;
            if (!float.IsNaN(originZ)) _patchOriginZ = originZ;

            if (sizeX > 0f) _patchSizeX = Mathf.Clamp(sizeX, 1f, 4000f);
            if (sizeZ > 0f) _patchSizeZ = Mathf.Clamp(sizeZ, 1f, 4000f);
            if (cellSizeM > 0f) _cellSizeM = Mathf.Clamp(cellSizeM, 0.02f, 1f);
            if (mirrorDownsample > 0) _mirrorDownsample = Mathf.Clamp(mirrorDownsample, 1, 16);
        }

        /// <summary>THE BLADE. Negative means "leave alone"; none of these has a meaningful 0.</summary>
        public void ApplyBladeOverrides(float pickupDepthM, float pileGrabPerPassM,
                                        float bladeWidthM, float bladeDepthM,
                                        float snowDensityKgPerM3, int bladeSegments)
        {
            if (pickupDepthM > 0f) _pickupDepthM = Mathf.Clamp(pickupDepthM, 0.005f, 0.60f);
            if (pileGrabPerPassM > 0f) _pileGrabPerPassM = Mathf.Clamp(pileGrabPerPassM, 0.02f, 3f);
            if (bladeWidthM > 0f) _bladeWidthM = Mathf.Clamp(bladeWidthM, 0.3f, 8f);
            if (bladeDepthM > 0f) _bladeDepthM = Mathf.Clamp(bladeDepthM, 0.05f, 2f);
            if (snowDensityKgPerM3 > 0f)
                _snowDensityKgPerM3 = Mathf.Clamp(snowDensityKgPerM3, 50f, 900f);
            if (bladeSegments >= 1) _bladeSegments = Mathf.Clamp(bladeSegments, 1, MaxBladeSegments);
        }

        /// <summary>
        /// THE HEAP'S SHAPE, ITS CAPACITY AND THE RELAX GUARD. Negative means "leave alone"; 0 is
        /// MEANINGFUL for the width split (0 = every cubic metre into height) so that one takes a
        /// strictly negative sentinel, and the guard takes -1 leave / 0 off / 1 on because 0 is its A/B.
        /// </summary>
        public void ApplyHeapOverrides(float heapMaxHeightM, float heapWidthPerHeight,
                                       float heapMaxHalfWidthM, float heapCrestAheadM,
                                       float heapFrontAngleDeg, float heapBackAngleDeg,
                                       float heapGuardAngleDeg, int heapRelaxGuard)
        {
            if (heapMaxHeightM > 0f) _heapMaxHeightM = Mathf.Clamp(heapMaxHeightM, 0.1f, 3f);
            if (heapWidthPerHeight >= 0f)
                _heapWidthPerHeight = Mathf.Clamp(heapWidthPerHeight, 0f, 3f);
            if (heapMaxHalfWidthM > 0f) _heapMaxHalfWidthM = Mathf.Clamp(heapMaxHalfWidthM, 0.3f, 8f);
            if (heapCrestAheadM > 0f) _heapCrestAheadM = Mathf.Clamp(heapCrestAheadM, 0.05f, 3f);
            if (heapFrontAngleDeg > 0f)
                _heapFrontAngleDeg = Mathf.Clamp(heapFrontAngleDeg, 20f, 85f);
            if (heapBackAngleDeg > 0f) _heapBackAngleDeg = Mathf.Clamp(heapBackAngleDeg, 20f, 88f);
            if (heapGuardAngleDeg > 0f)
                _heapGuardAngleDeg = Mathf.Clamp(heapGuardAngleDeg, 10f, 89f);
            if (heapRelaxGuard >= 0) _heapRelaxGuard = heapRelaxGuard != 0;
        }

        /// <summary>
        /// THE RELEASE - where the windrow is laid and how fast it is shed. Negative means "leave alone";
        /// 0 is MEANINGFUL for the back and out offsets, for the start fill (shed from empty) and for the
        /// rate (which restores the pure threshold behaviour), so all four take a strictly negative
        /// sentinel.
        /// </summary>
        public void ApplyReleaseOverrides(float spillBackM, float spillOutM, float spillRadiusM,
                                          float releaseStartFill, float releaseRatePerSec)
        {
            if (spillBackM >= 0f) _spillBackM = Mathf.Clamp(spillBackM, 0f, 4f);
            if (spillOutM >= 0f) _spillOutM = Mathf.Clamp(spillOutM, 0f, 4f);
            if (spillRadiusM > 0f) _spillRadiusM = Mathf.Clamp(spillRadiusM, 0.15f, 4f);
            if (releaseStartFill >= 0f) _releaseStartFill = Mathf.Clamp01(releaseStartFill);
            if (releaseRatePerSec >= 0f)
                _releaseRatePerSec = Mathf.Clamp(releaseRatePerSec, 0f, 3f);
        }

        /// <summary>
        /// THE VERBS' OWN KNOBS. Negative means "leave alone"; 0 is MEANINGFUL for the cast efficiency
        /// (cut rotation without the cast) and for the face relax rate (a frozen face), so those two take
        /// a strictly negative sentinel. The blade angle has no meaningful 0 - a 0 degree LEFT state is
        /// just STRAIGHT - so it takes the ordinary one.
        /// </summary>
        public void ApplyVerbOverrides(float bladeAngleDeg, float bladeCastEfficiency,
                                       float faceRelaxDegPerSec)
        {
            if (bladeAngleDeg > 0f) _bladeAngleDeg = Mathf.Clamp(bladeAngleDeg, 5f, 60f);
            if (bladeCastEfficiency >= 0f)
                _bladeCastEfficiency = Mathf.Clamp(bladeCastEfficiency, 0f, 3f);
            if (faceRelaxDegPerSec >= 0f)
                _faceRelaxDegPerSec = Mathf.Clamp(faceRelaxDegPerSec, 0f, 180f);
        }

        /// <summary>
        /// The deliberate loss. Negative means "leave alone"; 0 is MEANINGFUL for the berm share, the
        /// shed tau and the cut noise, so all three take a strictly negative sentinel.
        /// </summary>
        public void ApplyLossOverrides(float conservedFraction, float bermShareOfLoss,
                                       float pileShedTauSeconds, float cutNoiseAmp, float cutNoiseScaleM)
        {
            if (conservedFraction > 0f) _conservedFraction = Mathf.Clamp(conservedFraction, 0.05f, 1f);
            if (bermShareOfLoss >= 0f) _bermShareOfLoss = Mathf.Clamp01(bermShareOfLoss);
            if (pileShedTauSeconds >= 0f)
                _pileShedTauSeconds = Mathf.Clamp(pileShedTauSeconds, 0f, 60f);
            if (cutNoiseAmp >= 0f) _cutNoiseAmp = Mathf.Clamp01(cutNoiseAmp);
            if (cutNoiseScaleM > 0f) _cutNoiseScaleM = Mathf.Clamp(cutNoiseScaleM, 0.05f, 3f);
        }

        /// <summary>Relax and repose. Negative means "leave alone"; 0 is a meaningful iteration count.</summary>
        public void ApplyRelaxOverrides(int relaxIterations, float reposeAngleDeg)
        {
            if (relaxIterations >= 0) _relaxIterations = Mathf.Clamp(relaxIterations, 0, 8);
            if (reposeAngleDeg > 0f) _reposeAngle = Mathf.Clamp(reposeAngleDeg, 10f, 80f);
        }

        /// <summary>
        /// SCENARIO and INSTRUMENT knobs. Negative means "leave alone"; 0 is meaningful for the scrape
        /// height and for the ppm tolerance (0 forces v6's absolute-only behaviour), so those two take a
        /// strictly negative sentinel.
        /// </summary>
        public void ApplyScenarioOverrides(float startDepth, float scrapeHeight,
                                           float massToleranceL, float massTolerancePpm,
                                           int coarseDilate, float coarseCellM,
                                           float maxPushDistM, float pushMarginM,
                                           float sideSpillFullAt)
        {
            if (startDepth > 0f) _startDepth = Mathf.Max(0f, startDepth);
            if (scrapeHeight >= 0f) _scrapeHeight = Mathf.Clamp(scrapeHeight, 0f, 0.3f);
            if (massToleranceL > 0f) _massToleranceL = Mathf.Clamp(massToleranceL, 0.05f, 50f);
            if (massTolerancePpm >= 0f) _massTolerancePpm = Mathf.Clamp(massTolerancePpm, 0f, 50f);
            if (coarseDilate >= 1) _coarseDilate = Mathf.Clamp(coarseDilate, 1, 8);
            if (coarseCellM > 0f) _coarseCellM = Mathf.Clamp(coarseCellM, 0.125f, 8f);
            if (maxPushDistM > 0f) _maxPushDist = Mathf.Clamp(maxPushDistM, 0.02f, 1.5f);
            if (pushMarginM >= 0f) _pushMargin = Mathf.Clamp(pushMarginM, 0f, 0.2f);
            if (sideSpillFullAt > 0f) _sideSpillFullAt = Mathf.Clamp(sideSpillFullAt, 0.02f, 1f);
        }

        /// <summary>
        /// Relax bounding. relaxWindowEnabled takes -1 for "leave alone", 0 for the whole-field A/B and 1
        /// for the bounded dispatch; the pad takes a strictly negative sentinel because 0 is a meaningful
        /// pad.
        /// </summary>
        public void ApplyRelaxWindowOverrides(int relaxWindowEnabled, float relaxTrailSeconds,
                                              int relaxWindowPadTexels)
        {
            if (relaxWindowEnabled >= 0) _relaxWindowEnabled = relaxWindowEnabled != 0;
            if (relaxTrailSeconds > 0f)
                _relaxTrailSeconds = Mathf.Clamp(relaxTrailSeconds, 0.05f, 8f);
            if (relaxWindowPadTexels >= 0)
                _relaxWindowPadTexels = Mathf.Clamp(relaxWindowPadTexels, 0, 64);
        }

        /// <summary>Render-only dilation radii. 0 is meaningful (it is the A/B) for both.</summary>
        public void ApplyRenderDilationOverrides(int heightDilateRadius, int filletDilateRadius)
        {
            if (heightDilateRadius >= 0) _heightDilateRadius = Mathf.Clamp(heightDilateRadius, 0, 6);
            if (filletDilateRadius >= 0) _filletDilateRadius = Mathf.Clamp(filletDilateRadius, 0, 8);
        }

        /// <summary>
        /// The lump lattice's parameters, for the BAKE. Called once a frame by
        /// SnowRaymarchRendererV7.PushLumpBakeParams, BEFORE <see cref="Step"/>, and that direction is
        /// deliberate: the renderer owns the look, owns the 3x3-sufficiency clamp on the radius and owns
        /// the decode scale the marcher multiplies the baked texel by, so it has to be the single source
        /// of these numbers. If the bake encoded against one radius and the marcher decoded against
        /// another, the lobes would silently change height and nothing would report it.
        ///
        /// NO leave-alone sentinels here, unlike the other Apply* methods on this component: this is not
        /// scene authoring being forwarded once, it is a mirror of another component's live state, and a
        /// half-applied mirror is exactly the drift the paragraph above is about.
        ///
        /// Any change forces the NEXT bake to cover the whole texture. A windowed bake after a knob
        /// change would leave the rest of the field holding lobes built from the old knobs - a seam along
        /// the window edge that would look like a bug in the lattice rather than like a stale bake.
        /// </summary>
        public void ApplyLumpBakeParams(float radiusM, float spacingM, float jitter, float radiusVary,
                                        float gateDepthM, float reliefM, float slopeStrength,
                                        float minSnowHeightM)
        {
            bool changed = radiusM        != _lumpBakeRadiusM
                        || spacingM       != _lumpBakeSpacingM
                        || jitter         != _lumpBakeJitter
                        || radiusVary     != _lumpBakeRadiusVary
                        || gateDepthM     != _lumpBakeGateDepthM
                        || reliefM        != _lumpBakeReliefM
                        || slopeStrength  != _lumpBakeSlopeStrength
                        || minSnowHeightM != _lumpBakeMinSnowHeightM;

            _lumpBakeRadiusM = radiusM;
            _lumpBakeSpacingM = spacingM;
            _lumpBakeJitter = jitter;
            _lumpBakeRadiusVary = radiusVary;
            _lumpBakeGateDepthM = gateDepthM;
            _lumpBakeReliefM = reliefM;
            _lumpBakeSlopeStrength = slopeStrength;
            _lumpBakeMinSnowHeightM = minSnowHeightM;

            if (changed) _lumpBakeAllDirty = true;
        }

        // ------------------------------------------------------------------ THERE IS NO DUMP REQUEST
        //
        // v7's first cut had RequestDump(worldXZ): the whole pile placed as one solved cone, on a key.
        // IT IS DELETED, along with its cone height, its radius cap and its minimum volume, because the
        // verb grammar already contains the same action and contains it better:
        //
        //     REVERSING WITH THE BLADE DOWN IS THE DUMP.
        //
        // and it is a strictly better dump, on three counts. It leaves the pile in the SHAPE the pile
        // actually had rather than re-solving it into a cone. It costs NO dispatches at all - the deposit
        // is a retired receipt, not an emit. And it is directional in general rather than an edge, so
        // stopping, reversing and raising the blade are all the same physical statement: the blade's face
        // is not holding this any more.
        //
        // The one thing the old dump had that this does not is placement at arm's length - you cannot
        // deposit a pile somewhere you are not. That is the correct trade for a plough: you push snow to
        // where you want it and then you back out of it.

        // ------------------------------------------------------------------ lifecycle
        private void OnDestroy() => ReleaseResources();

        public void EnsureResources()
        {
            if (_cs == null)
            {
                var shared = Resources.Load<ComputeShader>("SnowPileFieldV7");
                if (shared == null)
                {
                    Debug.LogError("[V7] Resources/SnowPileFieldV7.compute not found.");
                    enabled = false;
                    return;
                }

                // 에셋을 그대로 쓰지 않고 **인스턴스를 복제**한다. ComputeShader 의 SetTexture/SetInt 는
                // 에셋 전역 상태라, 한 프로세스에 필드가 둘 이상이면(멀티피어에서는 피어마다 하나다)
                // 마지막에 쓴 쪽이 이깁니다 — 한 필드의 디스패치가 다른 필드의 텍스처를 향하고, 진 쪽
                // 필드는 통째로 0 이 된다. 실측: 서버 1 + 클라 2 로 띄우면 매번 한 클라이언트의 필드만
                // 정상이고 나머지는 전부 0 이었다(어느 쪽이 죽는지는 실행마다 바뀐다).
                _cs = Instantiate(shared);
                _cs.name = shared.name + " (" + name + ")";

                for (int i = 0; i < kKernelSpec.Length; ++i)
                    _kernel[i] = _cs.FindKernel(kKernelSpec[i].Name);
            }

            _mirrorBlock = Mathf.Clamp(_mirrorDownsample, 1, 16);

            // The TEXEL SIZE is held fixed at the requested cell and the PATCH EXTENT is what snaps, so
            // the cell is always exactly what the inspector says and no area constant ever drifts. At
            // 120 / 110 / 0.125 nothing snaps: 960 and 880 are both multiples of 16.
            float cell = Mathf.Clamp(_cellSizeM, 0.02f, 1f);
            int step = kThreadGroup * _mirrorBlock;

            int rx = RoundUpTo(Mathf.Max(step, Mathf.CeilToInt(_patchSizeX / cell)), step);
            int rz = RoundUpTo(Mathf.Max(step, Mathf.CeilToInt(_patchSizeZ / cell)), step);

            if (rx != _resX || rz != _resZ || _heightA == null)
            {
                ReleaseResources();
                _resX = rx;
                _resZ = rz;
                _mirrorResX = rx / _mirrorBlock;
                _mirrorResZ = rz / _mirrorBlock;

                _heightA = CreateField("SnowPileV7HeightA", rx, rz, RenderTextureFormat.RFloat, FilterMode.Bilinear);
                _heightB = CreateField("SnowPileV7HeightB", rx, rz, RenderTextureFormat.RFloat, FilterMode.Bilinear);
                _removed = CreateField("SnowPileV7Removed", rx, rz, RenderTextureFormat.RFloat, FilterMode.Point);
                _mirrorRt = CreateField("SnowPileV7Mirror", _mirrorResX, _mirrorResZ, RenderTextureFormat.RFloat, FilterMode.Point);
                _floorRt  = CreateField("SnowPileV7Floor", rx, rz, RenderTextureFormat.RFloat, FilterMode.Point);

                // THE HEAP RECEIPT. Point filtered, never interpolated: it carries a fixed-point count
                // HeapErase has to read back bit for bit, and a bilinear tap of a receipt is a receipt
                // for a different amount.
                _heapRt = CreateField("SnowPileV7Heap", rx, rz, RenderTextureFormat.RFloat, FilterMode.Point);

                _coarseBlock = Mathf.Max(1, Mathf.RoundToInt(_coarseCellM / cell));
                while (_coarseBlock > 1 && ((rx % _coarseBlock) != 0 || (rz % _coarseBlock) != 0))
                    _coarseBlock--;

                _coarseResX = rx / _coarseBlock;
                _coarseResZ = rz / _coarseBlock;

                _coarseBlockRt = CreateField("SnowPileV7CoarseBlock", _coarseResX, _coarseResZ, RenderTextureFormat.RFloat, FilterMode.Point);
                _coarseMaxRt   = CreateField("SnowPileV7CoarseMax", _coarseResX, _coarseResZ, RenderTextureFormat.RFloat, FilterMode.Point);
                _dilatedRt     = CreateField("SnowPileV7Dilated", rx, rz, RenderTextureFormat.RGFloat, FilterMode.Point);

                // THE BAKE TARGET. BILINEAR, unlike every other render-only texture here, because it
                // carries a VALUE (the surface's own lift in metres, encoded 0..1) rather than a BOUND,
                // and the marcher wants it smooth. Its bound is published separately, by maxing it into
                // the coarse cell - see CoarseMaxBlock.
                //
                // R8 is a UNORM format and a legal UAV store target, but it is CHECKED rather than
                // assumed: an unsupported store format would come back as a black texture, i.e. as "the
                // lobes silently vanished", which is exactly the failure this variant's binding audit
                // exists to make impossible to ship.
                RenderTextureFormat bakeFmt =
                    SystemInfo.SupportsRandomWriteOnRenderTextureFormat(RenderTextureFormat.R8)
                        ? RenderTextureFormat.R8 : RenderTextureFormat.RHalf;

                if (bakeFmt != RenderTextureFormat.R8)
                    Debug.LogWarning("[V7] R8 random write unsupported; lump bake fell back to RHalf " +
                                     "(same 0..1 encoding, 2x the memory).");

                _lumpBakeRt = CreateField("SnowPileV7LumpBake", rx * 2, rz * 2, bakeFmt,
                                          FilterMode.Bilinear);
                _lumpBakeAllDirty = true;

                _statsBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, kStatsSlots, sizeof(int));
                _carryBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, kCarrySlots, sizeof(int));

                _mirror = new float[_mirrorResX * _mirrorResZ];
                _statsCpu = new int[kStatsSlots];
                _carryCpu = new int[kCarrySlots];
            }
            else
            {
                _coarseBlock = Mathf.Max(1, Mathf.RoundToInt(_coarseCellM / cell));
                while (_coarseBlock > 1 && ((rx % _coarseBlock) != 0 || (rz % _coarseBlock) != 0))
                    _coarseBlock--;
            }

            _texelSize = cell;
            _extentX = rx * cell;
            _extentZ = rz * cell;
            _patchMin = new Vector2(_patchOriginX, _patchOriginZ);

            // THE RECEIPT'S EXACTNESS BOUND, ENFORCED rather than documented. _heapRt stores a
            // fixed-point count in a float32, exact up to 2^24 units; past that the erase would read back
            // a value one quantum off what the emit wrote, and the difference would be silently debited
            // to the field on every single frame. Non-binding at any sane cell size, live at 1 m.
            float limit = HeapHeightExactLimitM;
            if (_heapMaxHeightM > limit)
            {
                Debug.LogWarning("[V7] Heap Max Height M " + _heapMaxHeightM.ToString("F2") +
                                 " m exceeds the receipt texture's float32 exactness bound of " +
                                 limit.ToString("F2") + " m at a " + (cell * 100f).ToString("F1") +
                                 " cm cell; clamped. Use a finer cell for a taller heap.");
                _heapMaxHeightM = limit;
            }

            BindStatic();
            ResetField();
        }

        /// <summary>
        /// Binds every resource in <see cref="kKernelSpec"/> except the two ping-pong height textures,
        /// once per allocation. See THE BINDING AUDIT above for why this is generated from the table
        /// rather than written beside it.
        /// </summary>
        private void BindStatic()
        {
            var audit = new System.Text.StringBuilder(640);

            for (int i = 0; i < kKernelSpec.Length; ++i)
            {
                int k = _kernel[i];
                int res = kKernelSpec[i].Res;

                if ((res & R_REMSRC) != 0) _cs.SetTexture(k, kRemovedSrc, _removed);
                if ((res & R_REMDST) != 0) _cs.SetTexture(k, kRemovedDst, _removed);
                if ((res & R_FLOOR) != 0) _cs.SetTexture(k, kFloorTex, _floorRt);
                if ((res & R_MIRROR) != 0) _cs.SetTexture(k, kMirrorTex, _mirrorRt);
                if ((res & R_DILATE) != 0) _cs.SetTexture(k, kDilateDst, _dilatedRt);
                if ((res & R_CSRC) != 0) _cs.SetTexture(k, kCoarseSrc, _coarseBlockRt);
                if ((res & R_DILSRC) != 0) _cs.SetTexture(k, kDilateSrc, _dilatedRt);
                if ((res & R_LUMPSRC) != 0) _cs.SetTexture(k, kLumpBakeSrc, _lumpBakeRt);
                if ((res & R_LUMPDST) != 0) _cs.SetTexture(k, kLumpBakeDst, _lumpBakeRt);
                if ((res & R_HEAPDST) != 0) _cs.SetTexture(k, kHeapTex, _heapRt);
                if ((res & R_HEAPSRC) != 0) _cs.SetTexture(k, kHeapSrc, _heapRt);

                // The only per-kernel resolution in the table: the two-stage coarse reduction writes to
                // different textures, and SetTexture is per kernel, so both live here rather than at a
                // dispatch site.
                if ((res & R_CDST) != 0)
                {
                    _cs.SetTexture(k, kCoarseDst,
                        kKernelSpec[i].Name == "CoarseMaxBlock" ? _coarseBlockRt : _coarseMaxRt);
                }

                if ((res & R_STATS) != 0) _cs.SetBuffer(k, kStats, _statsBuffer);
                if ((res & R_CARRY) != 0) _cs.SetBuffer(k, kCarry, _carryBuffer);

                audit.Append(kKernelSpec[i].Name).Append('=').Append(ResText(res)).Append(' ');
            }

            _bindAuditText = audit.ToString().TrimEnd();
        }

        private static string ResText(int res)
        {
            var s = new System.Text.StringBuilder(64);
            if ((res & R_SRC) != 0) s.Append("src,");
            if ((res & R_DST) != 0) s.Append("dst,");
            if ((res & R_REMSRC) != 0) s.Append("remSrc,");
            if ((res & R_REMDST) != 0) s.Append("remDst,");
            if ((res & R_FLOOR) != 0) s.Append("floor,");
            if ((res & R_MIRROR) != 0) s.Append("mirror,");
            if ((res & R_DILATE) != 0) s.Append("dilate,");
            if ((res & R_CSRC) != 0) s.Append("cSrc,");
            if ((res & R_CDST) != 0) s.Append("cDst,");
            if ((res & R_STATS) != 0) s.Append("stats,");
            if ((res & R_CARRY) != 0) s.Append("carry,");
            if ((res & R_DILSRC) != 0) s.Append("dilSrc,");
            if ((res & R_LUMPSRC) != 0) s.Append("lumpSrc,");
            if ((res & R_LUMPDST) != 0) s.Append("lumpDst,");
            if ((res & R_HEAPDST) != 0) s.Append("heapDst,");
            if ((res & R_HEAPSRC) != 0) s.Append("heapSrc,");
            if (s.Length > 0) s.Length -= 1;
            return s.ToString();
        }

        /// <summary>
        /// Binds the two ping-pong textures for one dispatch and runs it. The ONLY place in this file that
        /// binds a texture per dispatch, and it refuses to run a kernel whose table entry wants a height
        /// texture it was not given - which turns "I forgot to bind the source" from a silent scribble
        /// into a console error naming the kernel.
        /// </summary>
        private void Dispatch(int slot, int w, int h, RenderTexture src, RenderTexture dst)
        {
            int res = kKernelSpec[slot].Res;

            if (((res & R_SRC) != 0) != (src != null) || ((res & R_DST) != 0) != (dst != null))
            {
                Debug.LogError("[V7] bind mismatch on kernel " + kKernelSpec[slot].Name +
                               ": table wants " + ResText(res) +
                               ", got src=" + (src != null) + " dst=" + (dst != null));
                return;
            }

            int k = _kernel[slot];
            if (src != null) _cs.SetTexture(k, kSrcTex, src);
            if (dst != null) _cs.SetTexture(k, kDstTex, dst);

            int gx = (w + kThreadGroup - 1) / kThreadGroup;
            int gy = (h + kThreadGroup - 1) / kThreadGroup;
            if (gx <= 0 || gy <= 0) return;
            _cs.Dispatch(k, gx, gy, 1);
        }

        /// <summary>Single-thread kernels: one group, no ping-pong texture, nothing to get wrong.</summary>
        private void DispatchSingle(int slot)
        {
            _cs.Dispatch(_kernel[slot], 1, 1, 1);
        }

        private static int RoundUpTo(int value, int multiple)
        {
            return ((value + multiple - 1) / multiple) * multiple;
        }

        private static RenderTexture CreateField(string label, int w, int h,
                                                 RenderTextureFormat format, FilterMode filter)
        {
            var rt = new RenderTexture(w, h, 0, format, RenderTextureReadWrite.Linear)
            {
                name = label,
                enableRandomWrite = true,
                filterMode = filter,
                wrapMode = TextureWrapMode.Clamp,
                useMipMap = false,
                autoGenerateMips = false,
            };
            rt.Create();
            return rt;
        }

        private void ReleaseResources()
        {
            if (_heightA != null) { _heightA.Release(); Destroy(_heightA); _heightA = null; }
            if (_heightB != null) { _heightB.Release(); Destroy(_heightB); _heightB = null; }
            if (_removed != null) { _removed.Release(); Destroy(_removed); _removed = null; }
            if (_mirrorRt != null) { _mirrorRt.Release(); Destroy(_mirrorRt); _mirrorRt = null; }
            if (_floorRt != null) { _floorRt.Release(); Destroy(_floorRt); _floorRt = null; }
            if (_heapRt != null) { _heapRt.Release(); Destroy(_heapRt); _heapRt = null; }
            if (_coarseBlockRt != null) { _coarseBlockRt.Release(); Destroy(_coarseBlockRt); _coarseBlockRt = null; }
            if (_coarseMaxRt != null) { _coarseMaxRt.Release(); Destroy(_coarseMaxRt); _coarseMaxRt = null; }
            if (_dilatedRt != null) { _dilatedRt.Release(); Destroy(_dilatedRt); _dilatedRt = null; }
            if (_lumpBakeRt != null) { _lumpBakeRt.Release(); Destroy(_lumpBakeRt); _lumpBakeRt = null; }

            _statsBuffer?.Dispose();
            _statsBuffer = null;
            _carryBuffer?.Dispose();
            _carryBuffer = null;

            _mirror = null;
            _statsCpu = null;
            _carryCpu = null;

            _mirrorValid = false;
            _mirrorRequested = false;
            _statsRequested = false;
            _carryRequested = false;
            _resX = 0;
            _resZ = 0;
        }

        /// <summary>Refills the field with the start depth and resets the conservation baseline.</summary>
        public void ResetField()
        {
            if (!Ready) return;

            PushStaticUniforms();

            Dispatch(K_INIT, _resX, _resZ, null, _heightA);
            Dispatch(K_INIT, _resX, _resZ, null, _heightB);

            // The ledger is persistent, so a field reset has to empty it too or its contents would be
            // counted as still carried while the snow it represented has just been recreated. And with
            // the receipt texture just zeroed by Init, THE ERASE WINDOW MUST BE EMPTIED IN THE SAME
            // BREATH: the pair is what keeps "the receipt's support is inside the recorded rect" an
            // invariant rather than a coincidence.
            _carryBuffer.SetData(kZeroCarry);
            _heapEraseRect = new RectInt(0, 0, 0, 0);
            _heapWindowRect = new RectInt(0, 0, 0, 0);
            _heapWindowTexels = 0;

            _ledgerL = 0f;
            _deletedL = 0f;
            _deletedStepL = 0f;
            _unplacedL = 0f;
            _unplacedPeakL = 0f;
            _releaseTotalM3 = 0f;
            _removedLast = 0.0;
            _depositedLast = 0.0;
            _terrainCutLast = 0.0;
            _bermLast = 0.0;
            _releaseDepositedLast = 0.0;
            _heapReclaimedLast = 0.0;
            _heapEmittedLast = 0.0;

            _pileRawM3 = 0f;
            _heapPlacedM3 = 0f;
            _pileHeightM = 0f;
            _pileHalfCrestM = _bladeWidthM * 0.5f;
            _heapFractionLast = 1f;

            _depositRanThisStep = false;
            _depositCount = 0;
            _lastDepositedM3 = 0f;
            _depositTotalM3 = 0.0;
            _depositTotalAtRequest = 0.0;
            _depositTotalSeenByRead = 0.0;

            _bladeDownLast = true;
            _angleStateLast = 0;
            _attachedLast = false;
            _push01Last = 0f;
            _castFracLast = 0f;
            _castRateLast = 0.0;
            _releaseFracLast = 0f;
            _releaseRateCommandedLast = 0.0;

            // Seeded at the SHOVED angle, not at repose: a reset puts a fresh blade against fresh snow and
            // the first shove should not have to break a settled face loose to get going.
            _faceAngleDeg = Mathf.Clamp(_heapFrontAngleDeg, 20f, 85f);
            _faceSteepen01 = 0f;

            _initialVolume = _startDepth * _extentX * _extentZ;
            _mirrorVolume = _initialVolume;

            if (_mirror != null)
            {
                for (int i = 0; i < _mirror.Length; ++i) _mirror[i] = _startDepth;
                _mirrorValid = true;
            }

            // A flat slab's crest is its depth. Seeding this rather than leaving it at zero matters: the
            // raymarcher would otherwise put its top plane below the snow for the handful of frames
            // before the first stats read-back lands.
            _fieldMaxLast = _startDepth;

            _pileCapacityM3 = HeapVolumeForHeight(_heapMaxHeightM, TanFace);

            _trailHead = 0;
            _trailCount = 0;
            _relaxRect = new RectInt(0, 0, _resX, _resZ);
            _relaxTexels = _resX * _resZ;

            // A RE-INITIALISED FIELD IS ENTIRELY DIRTY, so the next bake covers the whole texture
            // regardless of what the activity trail says - which it has to, because the trail was just
            // emptied and the old bake describes a field that no longer exists.
            _lumpBakeAllDirty = true;

            // ORDER IS A DEPENDENCY CHAIN, NOT A STYLE: the bake's relief gate reads the fillet dilation,
            // and the coarse max reads the bake. Dilate, then bake, then bound.
            BuildDilatedHeight();
            BuildLumpBake();
            BuildCoarseMax();
        }

        // ------------------------------------------------------------------ THE HEAP'S GEOMETRY
        //
        // The shape's volume, and the height that produces a wanted volume. Both on the CPU, both cheap,
        // and neither of them load bearing for CONSERVATION - HeapEmit normalises by the scanned weight
        // sum, so an error here changes the heap's HEIGHT and never its mass. What they ARE load bearing
        // for is the ASPECT RATIO, which is the whole visual read.

        /// <summary>The crest half-length at a given peak height, clamped by the width cap.</summary>
        private float HeapHalfCrestFor(float heightM)
        {
            float lc = _bladeWidthM * 0.5f + Mathf.Max(0f, _heapWidthPerHeight) * Mathf.Max(0f, heightM);
            return Mathf.Min(lc, _heapMaxHalfWidthM);
        }

        /// <summary>
        /// Volume in cubic metres of the heap profile at a given peak height, for a given LEADING FACE
        /// tangent.
        ///
        /// THE FACE TANGENT IS A PARAMETER AND NOT A FIELD READ, because the leading face angle is now
        /// STATE: it collapses toward repose when the shove stops and re-steepens when it resumes, so the
        /// capacity and the height solve have to be evaluated against the angle in force THIS step or the
        /// solved height and the emitted profile would disagree about the same pile.
        ///
        /// THE PROFILE IS max(0, H - frontOrBackDrop - flankDrop), i.e. a flat-topped ridge with a hipped
        /// end at each side. Its volume is EXACT rather than approximated:
        /// <code>
        ///     cross-section along travel   A(H) = c H^2,   c = (1/tanFront + 1/tanBack) / 2
        ///     the prism over the crest          = A(H) * 2 Lc
        ///     each hip: at lateral distance x past the crest the peak is H - x tanFlank and the two
        ///               slopes are unchanged, so the cross-section is A(H) * (1 - x tanFlank / H)^2.
        ///               Integrating x from 0 to H/tanFlank gives A(H) * H / (3 tanFlank).
        /// </code>
        /// so V(H) = A(H) * (2 Lc(H) + 2 H / (3 tanFlank)), which is monotone increasing in H even with
        /// the width clamp active - and that monotonicity is what makes the bisection below correct
        /// without a case split.
        ///
        /// A FLATTER FACE HOLDS MORE AT THE SAME HEIGHT, so the CAPACITY breathes a little as the face
        /// collapses and re-steepens: at the shipped knobs c is 0.367 at 65 degrees and 0.490 at the
        /// 55 degree repose, i.e. 33% more volume in the same silhouette. That is physical - a slumped
        /// blade-load really does hold more than a bulldozed one of the same height - and it cannot touch
        /// conservation, because HeapEmit normalises by the weight sum it scanned with these same tangents.
        /// </summary>
        private float HeapVolumeForHeight(float heightM, float tanFront)
        {
            float h = Mathf.Max(0f, heightM);
            if (h <= 1e-5f) return 0f;

            float tanF = Mathf.Max(0.05f, tanFront);
            float tanB = Mathf.Tan(Mathf.Clamp(_heapBackAngleDeg, 20f, 88f) * Mathf.Deg2Rad);
            float tanR = TanRepose;

            float c = 0.5f * (1f / tanF + 1f / tanB);
            float a = c * h * h;

            return a * (2f * HeapHalfCrestFor(h) + 2f * h / (3f * tanR));
        }

        /// <summary>
        /// Peak height whose profile holds <paramref name="volumeM3"/> at the given leading face tangent,
        /// by BISECTION on <see cref="HeapVolumeForHeight"/> over [0, hiM].
        ///
        /// Bisection rather than the closed-form cubic root, deliberately. V is a cubic in H only while
        /// the crest half-length is unclamped; past the width cap it is a DIFFERENT cubic, and a
        /// closed-form solve would need a case split that gets the boundary wrong the first time someone
        /// changes the cap. Bisection needs only monotonicity, which the function has by construction, and
        /// 40 halvings of a 3 m bracket resolve the height to about three nanometres for eighty multiplies
        /// once a frame.
        /// </summary>
        private float SolveHeapHeight(float volumeM3, float hiM, float tanFront)
        {
            if (volumeM3 <= 1e-9f) return 0f;

            float lo = 0f;
            float hi = Mathf.Max(1e-4f, hiM);

            for (int i = 0; i < 40; ++i)
            {
                float mid = 0.5f * (lo + hi);
                if (HeapVolumeForHeight(mid, tanFront) < volumeM3) lo = mid; else hi = mid;
            }

            return 0.5f * (lo + hi);
        }

        /// <summary>
        /// Tangent of the LEADING FACE'S LIVE ANGLE. One place, so the height solve, the capacity, the
        /// emit window and the shader uniform cannot be computed from different numbers in the same frame.
        /// </summary>
        private float TanFace => Mathf.Tan(Mathf.Clamp(_faceAngleDeg, 20f, 85f) * Mathf.Deg2Rad);

        // ------------------------------------------------------------------ the step
        /// <summary>
        /// Runs one simulation step. Call once per frame from the owner's Update, after the vehicle has
        /// been integrated, and pass the blade's start and end pose for the frame ALONG WITH ITS VERBS.
        ///
        /// THE ORDER OF THE SIX PHASES IS THE DESIGN, and two of the orderings are load bearing rather
        /// than tidy:
        ///
        ///   * HeapErase BEFORE Push. This is the first of the two guards against the pile being counted
        ///     twice: by the time the cut reads the field, the heap is not in it, so there is nothing
        ///     under the blade for the cut to re-credit. Swapping these two would credit the heap to the
        ///     ledger from the erase AND from the cut, and the invariant would print LEAK immediately -
        ///     which is the good failure mode, but it should never be reachable. (The second guard is
        ///     _FloorTex, which the emit deliberately does not touch.)
        ///   * HeapEmit AFTER Deposit and BEFORE Relax. After, because both read-modify-write the same
        ///     texture and the heap has to land on top of this step's berms rather than under them.
        ///     Before, because relax has to see the heap in order to settle the windrow around it -
        ///     and because the GUARD reads the receipt the relax is being run against.
        ///
        /// WHAT THE THREE VERBS DO TO THAT ORDER, and it is subtraction rather than addition:
        ///
        ///   blade UP        PHASE 2 and PHASE 4 are not dispatched. Nothing is cut, so nothing reaches
        ///                   the ledger and no berm is laid. The other four phases run unchanged.
        ///   blade ANGLE     PHASE 1's footprint and PHASE 5's heap frame are rotated by the angle, and
        ///                   part of PHASE 5's ledger is diverted from the heap channel to the release
        ///                   channel and emitted at the discharge end.
        ///   REVERSE / stop  PHASE 1 RETIRES instead of reclaiming: the heap is left standing and the
        ///                   carried volume goes to zero. PHASE 5 records nothing.
        /// </summary>
        public void Step(float dt, in SnowPileSweepV7 sweep)
        {
            if (!Ready) return;

            _watch.Restart();
            _lastDt = Mathf.Max(1e-5f, dt);
            _trailClock += _lastDt;
            _depositRanThisStep = false;

            PushStaticUniforms();

            // ---- THE VERBS, resolved once and used everywhere below -----------------------------------
            _bladeDownLast = sweep.BladeDown;
            _angleStateLast = (sweep.AngleState > 0) ? 1 : ((sweep.AngleState < 0) ? -1 : 0);
            _attachedLast = sweep.BladeAttached && sweep.BladeDown;
            _push01Last = _attachedLast ? Mathf.Clamp01(sweep.Push01) : 0f;

            // THE LEADING FACE. It chases what the shove is asking for at a bounded rate, so lifting off
            // settles the face toward repose and re-engaging pushes it back up - and _faceSteepen01 is how
            // hard it is climbing right now, scaled by how full the blade is, which is what the vehicle
            // turns into the brief resistance of breaking the pile loose.
            {
                float shovedDeg = Mathf.Clamp(_heapFrontAngleDeg, 20f, 85f);
                float reposeDeg = Mathf.Clamp(_reposeAngle, 10f, 80f);
                float wantDeg = Mathf.Lerp(Mathf.Min(reposeDeg, shovedDeg), shovedDeg, _push01Last);

                // `faceRate`, not `rate`: PHASE 6's relax rate is called that further down the same method.
                float faceRate = Mathf.Max(0f, _faceRelaxDegPerSec);

                if (faceRate <= 0f)
                {
                    // THE FROZEN-FACE A/B. The face is pinned at the shove angle and, since it never
                    // climbs, there is no re-steepen and therefore no break-loose cost either. Setting the
                    // cost to zero EXPLICITLY rather than letting the ratio below decide it matters: on the
                    // one frame this knob is turned to 0 the face can jump a long way, and a divide by the
                    // now-zero full-rate would report that jump as a full-strength break-loose.
                    _faceAngleDeg = shovedDeg;
                    _faceSteepen01 = 0f;
                }
                else
                {
                    float before = _faceAngleDeg;
                    _faceAngleDeg = Mathf.MoveTowards(_faceAngleDeg, wantDeg, faceRate * _lastDt);

                    // 1 while the face is climbing at its full rate, 0 in steady state, scaled by how full
                    // the blade is - an empty blade has no pile to break loose.
                    float climbDeg = Mathf.Max(0f, _faceAngleDeg - before);
                    _faceSteepen01 = Mathf.Clamp01(climbDeg / (faceRate * _lastDt))
                                   * Mathf.Clamp01(PileFill01);
                }
            }

            // ---- the swept blade, as a box union plus two poses --------------------------------------
            float halfWidth = Mathf.Max(0.05f, _bladeWidthM * 0.5f);
            float halfDepth = Mathf.Max(0.02f, _bladeDepthM * 0.5f);

            // THE BLADE'S YAW. Positive rotates the face normal toward +right, which drags the RIGHT end
            // of the blade line backwards - so a positive angle discharges to the RIGHT, and the state's
            // sign IS the discharge side.
            float angleRad = _angleStateLast * Mathf.Clamp(_bladeAngleDeg, 5f, 60f) * Mathf.Deg2Rad;
            float angleCos = Mathf.Cos(angleRad);
            float angleSin = Mathf.Sin(angleRad);

            BuildSegments(sweep, halfWidth, halfDepth, angleCos, angleSin,
                          out Vector2 pCenter, out Vector2 pFwd, out Vector2 pRight,
                          out Vector2 pHalf, out float sweepDist);

            Vector2 endFwd = SafeDir(sweep.EndForward);
            Vector2 endRight = new Vector2(endFwd.y, -endFwd.x);

            // THE HEAP'S FRAME IS THE BLADE'S FRAME, NOT THE TRAVEL FRAME, and with an angled blade those
            // are different. The ridge lies along the blade line and its crest sits ahead along the face
            // normal, because that is where a blade full of snow actually is; pointing the heap down the
            // travel direction instead would hang its back toe over the trench on one side and leave it
            // standing on uncut snow on the other.
            Vector2 faceFwd = new Vector2(endFwd.x * angleCos + endFwd.y * angleSin,
                                          endFwd.y * angleCos - endFwd.x * angleSin);
            Vector2 faceRight = new Vector2(faceFwd.y, -faceFwd.x);

            _cs.SetInt(kBladeSegCount, _segCount);
            _cs.SetVectorArray(kBladeSeg, _segments);
            _cs.SetFloat(kBladeHalfWide, halfWidth);
            _cs.SetFloat(kBladeHalfDeep, halfDepth);
            _cs.SetFloat(kBladeAngleCos, angleCos);
            _cs.SetFloat(kBladeAngleSin, angleSin);
            _cs.SetVector(kBladeCenter, new Vector4(pCenter.x, pCenter.y, 0f, 0f));
            _cs.SetVector(kBladeFwd, new Vector4(pFwd.x, pFwd.y, 0f, 0f));
            _cs.SetVector(kBladeRight, new Vector4(pRight.x, pRight.y, 0f, 0f));
            _cs.SetVector(kBladeHalf, new Vector4(pHalf.x, pHalf.y, 0f, 0f));
            _cs.SetVector(kHeapCenter, new Vector4(sweep.EndCenter.x, sweep.EndCenter.y, 0f, 0f));
            _cs.SetVector(kHeapFwd, new Vector4(faceFwd.x, faceFwd.y, 0f, 0f));
            _cs.SetVector(kHeapRight, new Vector4(faceRight.x, faceRight.y, 0f, 0f));
            _cs.SetFloat(kScrapeHeight, _scrapeHeight);

            // PER-PASS -> PER-STEP. A texel is under the blade for bladeDepth/sweepDist steps, so this
            // fraction makes the total over one traverse equal _pickupDepthM exactly. A frame with no
            // displacement gets a zero budget, which is what keeps the cut timestep independent even when
            // the vehicle is stationary - and is also why a parked blade does not slowly eat a hole under
            // itself. NO UPPER CLAMP: a frame that sweeps further than the blade is thick covers the texel
            // exactly once, so it must be allowed to take the whole pass depth at once.
            float stepFrac = sweepDist / Mathf.Max(1e-4f, _bladeDepthM);

            _cs.SetFloat(kMaxCutStep, _pickupDepthM * stepFrac);
            _cs.SetFloat(kMaxPileStep, _pileGrabPerPassM * stepFrac);

            // The gather radius must cover the maximum travel distance, or mass sent past the radius is
            // never gathered and silently disappears. Derive the radius first, then clamp the distance
            // actually handed to the shader to what that radius can reach.
            int depositRadius = Mathf.Clamp(Mathf.CeilToInt(_maxPushDist / _texelSize) + 1,
                                            1, kMaxDepositRadius);
            float reach = (depositRadius - 1) * _texelSize;

            _cs.SetFloat(kMaxPushDist, Mathf.Min(_maxPushDist, reach));
            _cs.SetFloat(kPushMargin, _pushMargin);
            _cs.SetFloat(kPushSpread, _pushSpread);
            _cs.SetFloat(kSpillStart, _sideSpillStart);
            _cs.SetFloat(kSpillFullAt, _sideSpillFullAt);
            _cs.SetFloat(kSpillStrength, _sideSpillStrength);
            _cs.SetInt(kDepositRadius, depositRadius);

            _cs.SetFloat(kConservedFrac, Mathf.Clamp(_conservedFraction, 0.05f, 1f));
            _cs.SetFloat(kBermShare, Mathf.Clamp01(_bermShareOfLoss));
            _cs.SetFloat(kPileShedTau, Mathf.Max(0f, _pileShedTauSeconds));
            _cs.SetFloat(kStepDt, _lastDt);
            _cs.SetFloat(kCutNoiseAmp, Mathf.Clamp01(_cutNoiseAmp));
            _cs.SetFloat(kCutNoiseScale, Mathf.Clamp(_cutNoiseScaleM, 0.05f, 3f));

            // Stats are per-step, so clear them first - and that has to be BEFORE HeapErase, which is now
            // the first kernel in the step that writes one.
            _cs.Dispatch(_kernel[K_CLEAR_STATS], 1, 1, 1);

            // ---- PHASE 1: lift the heap back off the field, OR LET GO OF IT ---------------------------
            //
            // Over the window recorded when it was emitted, UNCONDITIONALLY, whether or not anything else
            // runs this step. Skipping it on a quiet frame would leave the receipt outstanding while the
            // field still held the mass, and the pile would silently become part of the terrain WITHOUT the
            // ledger ever letting go of it - which is the one way this design could print LEAK.
            //
            // THE MODE IS THE THIRD VERB. Attached (blade down and driving into the face) reclaims, so the
            // heap can be re-emitted at the new pose. Not attached - raised, stopped, or reversing - RETIRES:
            // the receipt is cleared, the field is not touched, and the pile is left standing exactly where
            // it was. That is the deposit, it costs nothing, and it cannot move the invariant.
            RectInt eraseRect = _heapEraseRect;
            bool retiring = false;
            if (eraseRect.width > 0 && eraseRect.height > 0)
            {
                retiring = !_attachedLast;

                _cs.SetFloat(kHeapReclaim, _attachedLast ? 1f : 0f);
                _cs.SetVector(kHeapEraseWin, new Vector4(eraseRect.xMin, eraseRect.yMin,
                                                         eraseRect.width, eraseRect.height));
                Dispatch(K_HEAP_ERASE, eraseRect.width, eraseRect.height, null, _heightA);
            }

            if (retiring)
            {
                // LATENCY COMPENSATION AND THE COUNT, on the one frame the pile stops being ours. The
                // read-back that observes the real number supersedes this a few frames later; see
                // _depositTotalM3. Nothing is placed and nothing is booked, so this is instrumentation
                // only - the simulation has already done the whole deposit by clearing a receipt.
                float leftStanding = PileVolumeM3;
                _depositTotalM3 += leftStanding;
                _depositRanThisStep = true;
                _depositCount++;
                _lastDepositedM3 = leftStanding;

                // THE RECEIPT IS GONE, so the window it lived in is gone with it. Not clearing this would
                // make the next step erase a window with no receipts in it - harmless today, because the
                // kernel early-outs on recFixed <= 0, but it is the kind of stale bookkeeping that stops
                // being harmless the moment anything else writes _HeapTex.
                //
                // The LOCAL `eraseRect` is deliberately NOT cleared with it: it still goes into the
                // activity trail below, which is what keeps the relax window over the abandoned pile so its
                // deliberately-steep faces - no longer receipted, so no longer guarded - actually settle to
                // repose instead of freezing at the shove angle.
                _heapEraseRect = new RectInt(0, 0, 0, 0);
            }

            // ---- PHASE 2: cut ------------------------------------------------------------------------
            //
            // NOT DISPATCHED WITH THE BLADE UP, which is the whole of "up does not cut and does not
            // accumulate": no Push means no removal, no _RemovedDst, nothing credited to the ledger and -
            // because Settle zeroes `over` when _Stats[0] is zero - no berm either. There is no
            // "is the blade up?" test inside any kernel.
            // `window` is declared and seeded separately rather than as an `out` inside the &&, because a
            // short-circuited `out` leaves it not-definitely-assigned for the compiler even on the branch
            // where the call did run.
            Vector4 window = Vector4.zero;
            bool pushed = _bladeDownLast
                       && ComputeDepositWindow(halfWidth, halfDepth, depositRadius, out window);
            if (pushed)
            {
                Dispatch(K_PUSH, _resX, _resZ, _heightA, _heightB);
            }

            // ---- PHASE 3: the deliberate loss --------------------------------------------------------
            //
            // Between Push and Deposit, ALWAYS: Deposit's berm fraction is what Settle writes, and Settle
            // has to run even on a step with no push or the fraction would be stale.
            DispatchSingle(K_SETTLE);

            // ---- PHASE 4: the berms ------------------------------------------------------------------
            if (pushed)
            {
                _cs.SetVector(kWindow, window);
                Dispatch(K_DEPOSIT, (int)window.z, (int)window.w, _heightB, _heightA);
            }

            // ---- PHASE 5: re-emit the pile at the new pose -------------------------------------------
            Vector4? emitWindow = EmitHeap(sweep.EndCenter, endFwd, endRight,
                                           faceFwd, faceRight, halfWidth, sweep.SignedSpeed);

            // ---- PHASE 6: relax ----------------------------------------------------------------------
            float tan = TanRepose;
            _cs.SetFloat(kMaxDelta, tan * _texelSize);
            _cs.SetFloat(kMaxDeltaDiag, tan * _texelSize * 1.41421356f);

            // THE GUARD's own limit, never BELOW the repose limit - a guard that relaxed the constraint
            // would let the heap's interior collapse faster than the field around it, which is the
            // opposite of what it is for.
            float tanGuard = Mathf.Max(tan, Mathf.Tan(Mathf.Clamp(_heapGuardAngleDeg, 10f, 89f)
                                                      * Mathf.Deg2Rad));
            _cs.SetFloat(kMaxDeltaHeap, tanGuard * _texelSize);
            _cs.SetFloat(kMaxDeltaHeapD, tanGuard * _texelSize * 1.41421356f);
            _cs.SetFloat(kHeapGuardOn, _heapRelaxGuard ? 1f : 0f);

            float rate = Mathf.Clamp(_relaxRate * dt * 60f, 0.001f, 0.24f);
            _cs.SetFloat(kRelaxRate, rate);

            NoteActivity(pushed ? window : (Vector4?)null, emitWindow, eraseRect);
            DispatchRelax();

            // Coarse mirror for the CPU.
            _cs.SetInt(kMirrorBlock, _mirrorBlock);
            _cs.SetInt(kMirrorResX, _mirrorResX);
            _cs.SetInt(kMirrorResZ, _mirrorResZ);
            Dispatch(K_DOWNSAMPLE, _mirrorResX, _mirrorResZ, _heightA, null);

            // Crest of the whole field, for the raymarcher's top plane. Deliberately last: it has to
            // describe the texture that is about to be drawn.
            Dispatch(K_HEIGHT_MAX, _resX, _resZ, _heightA, null);

            // ORDER IS A DEPENDENCY CHAIN, NOT A STYLE. The bake's relief gate reads the fillet dilation,
            // and the coarse-max bound reads the bake, so the dilate has to be built before the bake and
            // the bake before the bound. It also has to be exactly this way round for the march's bound
            // to be safe: the coarse max is rebuilt over the WHOLE field from whatever the bake texture
            // currently holds, so it bounds precisely the texels the marcher will read this frame - even
            // if the bake window missed something, or the bake was skipped entirely because the radius is
            // 0. See CoarseMaxYFrom in SnowMarchCoreV7.hlsl.
            BuildDilatedHeight();
            BuildLumpBake();
            BuildCoarseMax();

            _watch.Stop();
            _stepMs = _watch.Elapsed.TotalMilliseconds;

            _frame++;
            RequestReadbacks();
        }

        /// <summary>
        /// PHASE 5. Decides the heap's shape from the pile's volume, decides how much of the ledger stays
        /// on the blade against how much curls off it, points the release cones at the blade ends or at the
        /// discharge end, and runs the three emit kernels. Returns the window it dispatched over, for the
        /// activity trail, or null when nothing was emitted.
        ///
        /// THE FRACTIONS SUM TO 1 IN NORMAL OPERATION, which is what makes the pile the field's problem
        /// rather than the ledger's: everything the ledger holds is placed every frame, so the drawn
        /// surface and the simulated mass are the same object with no lag between them. The two ways the
        /// sum can fall short are both conserving and both self-correcting on the next frame - the weight
        /// sums are rounded UP, and a footprint hanging off the patch edge has its off-field share counted
        /// in the sum but never written, so that share simply stays on the ledger.
        ///
        /// THREE THINGS TAKE FROM THE RELEASE CHANNEL, and they are added and then clamped rather than
        /// treated as cases:
        ///
        ///   OVERFLOW    a smooth rate that rises from Release Start Fill to full at capacity, plus a HARD
        ///               term above capacity that pins the heap exactly. Snow comes off the ends before the
        ///               blade is full, which is what a plough does.
        ///   THE CAST    an angled blade works snow along its face and discharges it at the trailing end.
        ///               LEDGER TRANSPORT: a fraction of the ledger changes channel. Nothing is pushed
        ///               texel to texel, so there is nothing to diffuse and nothing to lose.
        ///   DETACHMENT  a blade that is not driving into its own face holds nothing, so the whole ledger
        ///               goes to release. This is what drains the fresh cut of a blade being dragged
        ///               backwards instead of letting it pile up on a ledger nobody is carrying.
        /// </summary>
        private Vector4? EmitHeap(Vector2 endCenter, Vector2 endFwd, Vector2 endRight,
                                  Vector2 faceFwd, Vector2 faceRight, float halfWidth, float signedSpeed)
        {
            // ---- what the pile currently is, and what shape holds it ---------------------------------
            //
            // From the READ-BACK, which is a few frames stale, and that is fine for v6's reason: growth is
            // smooth, so a few frames of lag is invisible, and a predictor on a smooth signal is just a
            // second source of truth. The one discontinuity - a DEPOSIT - IS compensated, in PileVolumeM3.
            float pile = PileVolumeM3;

            float tanF = TanFace;
            float tanB = Mathf.Tan(Mathf.Clamp(_heapBackAngleDeg, 20f, 88f) * Mathf.Deg2Rad);
            float tanR = TanRepose;

            _pileCapacityM3 = HeapVolumeForHeight(_heapMaxHeightM, tanF);

            // ---- THE CAST: the angled blade's ledger transport ---------------------------------------
            //
            // Snow entering at the leading end is worked along the face at about speed * sin(angle) and
            // leaves at the trailing end, so it lives on the blade for bladeWidth / (speed * sin(angle))
            // seconds. The fraction of the ledger that leaves in one step is therefore
            //
            //     castFrac = |speed| * sin(angle) * efficiency * dt / bladeWidth
            //
            // which is INDEPENDENT of how much is on the blade - so the cast RATE in m3/s is proportional
            // to the load, which is proportional to the intake, which is proportional to the depth. That is
            // exactly why the vehicle's cast reaction grows in deep snow and vanishes on a clear lane
            // without anything having to test the depth.
            //
            // Zero when the blade is straight (sin 0), zero with the blade up or detached (the whole ledger
            // is going to release anyway), and zero at a standstill.
            float castFrac = 0f;
            if (_angleStateLast != 0 && _attachedLast && _bladeCastEfficiency > 0f)
            {
                float sinA = Mathf.Abs(Mathf.Sin(Mathf.Clamp(_bladeAngleDeg, 5f, 60f) * Mathf.Deg2Rad));
                float along = Mathf.Abs(signedSpeed) * sinA * _bladeCastEfficiency;
                castFrac = Mathf.Clamp01(along * _lastDt / Mathf.Max(0.1f, _bladeWidthM));
            }

            // ---- what stays on the blade -------------------------------------------------------------
            float heapFrac;
            float releaseFrac;

            if (!_attachedLast)
            {
                // NOT ATTACHED. Nothing is recorded, so no receipt is written and the next erase finds
                // nothing: whatever the blade cut this step is laid down beside it and the pile the vehicle
                // WAS carrying is already standing where the retire left it.
                heapFrac = 0f;
                releaseFrac = 1f;
                _pileHeightM = 0f;
                _pileHalfCrestM = halfWidth;
            }
            else if (pile <= 1e-6f)
            {
                heapFrac = 1f;
                releaseFrac = 0f;
                _pileHeightM = 0f;
                _pileHalfCrestM = halfWidth;
            }
            else
            {
                // THE SHAPE. Solved from the whole pile below capacity and pinned at the maximum above it.
                if (pile <= _pileCapacityM3)
                {
                    _pileHeightM = SolveHeapHeight(pile, _heapMaxHeightM, tanF);
                    _pileHalfCrestM = HeapHalfCrestFor(_pileHeightM);
                }
                else
                {
                    _pileHeightM = _heapMaxHeightM;
                    _pileHalfCrestM = HeapHalfCrestFor(_heapMaxHeightM);
                }

                // THE OVERFLOW, AS A RATE PLUS A CAP.
                //
                // The SOFT term is a per-step fraction of the ledger, ramped by a smoothstep from the start
                // fill to capacity, so the shed grows continuously and is already meaningful at 80% full.
                // The HARD term is the old exact rule and is what actually pins the heap: above capacity it
                // releases precisely the excess, so the heap cannot grow past its maximum however the rate
                // knob is set - and at rate 0 the soft term vanishes and this is v7's original threshold
                // behaviour, exactly.
                float fill = pile / Mathf.Max(1e-4f, _pileCapacityM3);
                float start = Mathf.Clamp01(_releaseStartFill);
                float ramp = Mathf.SmoothStep(0f, 1f,
                                              Mathf.Clamp01((fill - start) / Mathf.Max(1e-4f, 1f - start)));

                float softFrac = Mathf.Clamp01(Mathf.Max(0f, _releaseRatePerSec) * _lastDt) * ramp;
                float hardFrac = (pile > _pileCapacityM3)
                               ? Mathf.Clamp01(1f - _pileCapacityM3 / pile)
                               : 0f;

                // ADDED, not maxed, for the cast: casting and shedding are different holes in the same
                // blade and a blade doing both loses more than a blade doing either. The overflow's own two
                // terms ARE maxed, because they are two descriptions of the same hole.
                releaseFrac = Mathf.Clamp01(Mathf.Max(softFrac, hardFrac) + castFrac);
                heapFrac = 1f - releaseFrac;
            }

            _heapFractionLast = heapFrac;
            _castFracLast = castFrac;
            _releaseFracLast = releaseFrac;
            _castRateLast = castFrac * pile / Mathf.Max(1e-5f, _lastDt);
            _releaseRateCommandedLast = releaseFrac * pile / Mathf.Max(1e-5f, _lastDt);

            // ---- the release cones ------------------------------------------------------------------
            //
            // The cone list is what makes the windrow one-sided when the blade is angled, and it is the
            // ONLY place the discharge side is expressed geometrically.
            _releaseConeCount = 0;
            float releaseRadius = Mathf.Max(0.15f, _spillRadiusM);

            // GATED ON THE BLADE BEING DOWN, and that is what makes "up carries nothing and leaves nothing"
            // literally true rather than nearly true. With the blade up nothing was cut, so the ledger holds
            // only fixed-point dust; without this gate the emit would still run and dribble that dust onto
            // the ground under a raised blade. With it, the heap window is empty too, ComputeEmitWindow
            // returns false, and the transit verb costs exactly zero dispatches and places exactly zero
            // snow. The dust stays on the ledger, where the invariant counts it as `carried`.
            //
            // With the blade DOWN and unattached the cones ARE placed even when the fraction is tiny: the
            // window has to exist for the emit to run, and while the blade is being dragged backwards the
            // emit is the only thing draining the fresh cut off a ledger nobody is carrying.
            bool wantCones = _bladeDownLast && (releaseFrac > 1e-6f || !_attachedLast);

            if (wantCones)
            {
                // BEHIND the blade line, so the windrow is built in the vehicle's wake rather than in its
                // path, and OUTSIDE the swept width so the next frame's cut cannot reach it.
                // max(out, radius) is what guarantees the second property whatever the two knobs say.
                float outM = Mathf.Max(_spillOutM, _spillRadiusM);

                if (_angleStateLast != 0)
                {
                    // ANGLED: ONE cone, at the DISCHARGE end. The discharge end is the trailing end of the
                    // blade line, which is already pulled back by halfWidth*sin(angle) - so the extra back
                    // offset is taken from the blade's own geometry rather than guessed, and the cone lands
                    // clear of the swept box on the side the snow is actually leaving.
                    Vector2 endOfBlade = endCenter + faceRight * (_angleStateLast * halfWidth);
                    Vector2 outward = endRight * (_angleStateLast * outM);
                    _releaseCones[0] = ConeAt(endOfBlade + outward - endFwd * _spillBackM);
                    _releaseConeCount = 1;
                }
                else
                {
                    // STRAIGHT: two cones, one off each end, exactly as before.
                    Vector2 back = endCenter - endFwd * _spillBackM;
                    _releaseCones[0] = ConeAt(back + endRight * (halfWidth + outM));
                    _releaseCones[1] = ConeAt(back - endRight * (halfWidth + outM));
                    _releaseConeCount = 2;
                }
            }

            // Unused slots still have to hold something finite: the shader loop is bounded by
            // _ReleaseCount, but stale centres would make a later step with a higher count read an old
            // pose for its extra slots.
            for (int i = _releaseConeCount; i < MaxReleaseCones; ++i)
            {
                _releaseCones[i] = (_releaseConeCount > 0) ? _releaseCones[0] : ConeAt(endCenter);
            }

            // ---- the profile uniforms ---------------------------------------------------------------
            //
            // tanF is the LIVE face angle, not the authored one, and the height solve above used the same
            // number. That identity is what keeps conservation exact while the face reshapes: HeapScan sums
            // this profile and HeapEmit divides by that sum, so the emitted volume is the ledger whatever
            // the tangents are - but a solve and an emit disagreeing about the tangent would put the crest
            // at the wrong height and make the pile look like it was breathing.
            _cs.SetFloat(kHeapPeakM, _pileHeightM);
            _cs.SetFloat(kHeapHalfCrest, _pileHalfCrestM);
            _cs.SetFloat(kHeapCrestAhd, _heapCrestAheadM);
            _cs.SetFloat(kHeapTanFront, tanF);
            _cs.SetFloat(kHeapTanBack, tanB);
            _cs.SetFloat(kHeapTanFlank, tanR);
            _cs.SetFloat(kHeapFraction, heapFrac);
            _cs.SetFloat(kReleaseFrac, releaseFrac);
            _cs.SetInt(kReleaseCount, _releaseConeCount);
            _cs.SetVectorArray(kReleaseCenter, _releaseCones);
            _cs.SetFloat(kReleaseRadius, releaseRadius);

            // ---- the window -------------------------------------------------------------------------
            //
            // In the HEAP'S OWN FRAME, which with an angled blade is the rotated one - the four corners of
            // the profile's exact support are transformed by faceFwd/faceRight, so the window covers a
            // rotated ridge without being padded for it.
            if (!ComputeEmitWindow(endCenter, faceFwd, faceRight, heapFrac, releaseFrac, releaseRadius,
                                   tanF, tanB, tanR, out Vector4 win, out RectInt clamped))
            {
                // Nothing to place anywhere on the patch. The receipt window is emptied, which is
                // correct: HeapEmit did not run, so nothing was recorded, so there is nothing to erase.
                _heapEraseRect = new RectInt(0, 0, 0, 0);
                _heapWindowRect = _heapEraseRect;
                _heapWindowTexels = 0;
                return null;
            }

            _cs.SetVector(kHeapWindow, win);

            DispatchSingle(K_HEAP_BEGIN);
            Dispatch(K_HEAP_SCAN, (int)win.z, (int)win.w, null, null);
            Dispatch(K_HEAP_EMIT, (int)win.z, (int)win.w, null, _heightA);
            DispatchSingle(K_HEAP_FINISH);

            // THE RECEIPT'S EXTENT, RECORDED. The CLAMPED rect, because HeapEmit bounds-checks before it
            // touches a texture, so the receipt's support is exactly this and nothing outside it. Stored
            // rather than recomputed next frame: see _heapEraseRect's declaration for why that
            // distinction is the whole design.
            _heapEraseRect = (heapFrac > 1e-6f) ? clamped : new RectInt(0, 0, 0, 0);
            _heapWindowRect = clamped;
            _heapWindowTexels = clamped.width * clamped.height;

            return win;
        }

        private static Vector4 ConeAt(Vector2 p) => new Vector4(p.x, p.y, 0f, 0f);

        /// <summary>
        /// Tiles the step's motion into <see cref="_segCount"/> swept boxes and derives the PRIMARY pose
        /// that PushOffset's escape geometry is expressed in.
        ///
        /// For a translation ONE box from the start centre to the end centre is the swept volume EXACTLY,
        /// at any blade angle - see InsideBlade, which intersects the two per-axis t intervals rather than
        /// testing a tiling of poses. For a TURN the centre traces an arc and that chord cuts the corner, so
        /// the sub-centres are placed on a quadratic Bezier whose control point is where the two tangent
        /// lines meet - and each sub-box takes its own orientation from its own direction of travel, which
        /// is what covers the blade's own rotation through the step. The control point is clamped along the
        /// start tangent to twice the chord length, because two nearly-antiparallel tangents - which is what
        /// a reversing vehicle produces - put the intersection arbitrarily far away and would fling the
        /// boxes off the map.
        ///
        /// THE PRIMARY HALF EXTENTS ARE THE ROTATED BOX'S AABB, not the un-rotated one's. A blade yawed by
        /// alpha spans halfDepth*|cos| + halfWidth*|sin| along travel and halfDepth*|sin| + halfWidth*|cos|
        /// across it, and PushOffset drives the berm escape distance from exactly these numbers - so
        /// handing it the un-rotated extents would throw the berms of an angled pass out to where the blade
        /// is not. At alpha 0 this is the old expression term for term.
        /// </summary>
        private void BuildSegments(in SnowPileSweepV7 sweep, float halfWidth, float halfDepth,
                                   float angleCos, float angleSin,
                                   out Vector2 primaryCenter, out Vector2 primaryFwd,
                                   out Vector2 primaryRight, out Vector2 primaryHalf,
                                   out float sweepDist)
        {
            Vector2 a = sweep.StartCenter;
            Vector2 b = sweep.EndCenter;
            Vector2 fA = SafeDir(sweep.StartForward);
            Vector2 fB = SafeDir(sweep.EndForward);

            Vector2 d = b - a;
            sweepDist = d.magnitude;

            int k = Mathf.Clamp(sweep.Segments <= 0 ? 1 : sweep.Segments, 1, MaxBladeSegments);
            _segCount = k;

            // Control point: solve a + s*fA = b - u*fB for s. cross(fA, fB) is the turn's sine.
            Vector2 ctrl = (a + b) * 0.5f;
            float cross = fA.x * fB.y - fA.y * fB.x;
            if (Mathf.Abs(cross) > 1e-4f && sweepDist > 1e-6f)
            {
                float s = (d.x * fB.y - d.y * fB.x) / cross;
                s = Mathf.Clamp(s, 0f, sweepDist * 2f);
                ctrl = a + fA * s;
            }

            for (int i = 0; i < k; ++i)
            {
                float t0 = (float)i / k;
                float t1 = (float)(i + 1) / k;
                Vector2 p0 = Bezier(a, ctrl, b, t0);
                Vector2 p1 = Bezier(a, ctrl, b, t1);
                _segments[i] = new Vector4(p0.x, p0.y, p1.x, p1.y);
            }

            // Unused slots still have to hold something finite: the shader loop is bounded by
            // _BladeSegCount, but stale poses in the array would make a later step with a higher count
            // read last step's geometry for its extra slots.
            for (int i = k; i < MaxBladeSegments; ++i) _segments[i] = _segments[k - 1];

            // The primary pose: the swept rectangle's bounding box at the END heading, which is where the
            // blade actually is now rather than where it was halfway through the step.
            primaryFwd = fB;
            primaryRight = new Vector2(fB.y, -fB.x);
            primaryCenter = (a + b) * 0.5f;

            float ac = Mathf.Abs(angleCos);
            float asn = Mathf.Abs(angleSin);
            float boxF = halfDepth * ac + halfWidth * asn;
            float boxR = halfDepth * asn + halfWidth * ac;

            float alongF = Mathf.Abs(Vector2.Dot(d, primaryFwd));
            float alongR = Mathf.Abs(Vector2.Dot(d, primaryRight));
            primaryHalf = new Vector2(boxF + alongF * 0.5f, boxR + alongR * 0.5f);
        }

        private static Vector2 Bezier(Vector2 a, Vector2 c, Vector2 b, float t)
        {
            float u = 1f - t;
            return u * u * a + 2f * u * t * c + t * t * b;
        }

        private static Vector2 SafeDir(Vector2 v)
        {
            float m = v.magnitude;
            return (m > 1e-6f) ? v / m : Vector2.up;
        }

        // ------------------------------------------------------------------ windows
        /// <summary>
        /// Texel-space AABB of the SWEPT BOX UNION, expanded by the maximum deposit travel. Returns false
        /// when the blade cannot touch the patch at all.
        ///
        /// Sized from the SEGMENTS, not from the primary pose's box, and that is a conservation requirement
        /// rather than a nicety: Push cuts wherever any box covers, and a texel Push cut outside the gather
        /// window would have its mass removed and never re-deposited - an unbooked loss, which is precisely
        /// the class of defect the invariant exists to catch.
        /// </summary>
        private bool ComputeDepositWindow(float halfWidth, float halfDepth, int depositRadius,
                                          out Vector4 window)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;

            for (int i = 0; i < _segCount; ++i)
            {
                AccumulatePoint(new Vector2(_segments[i].x, _segments[i].y),
                                ref minX, ref maxX, ref minZ, ref maxZ);
                AccumulatePoint(new Vector2(_segments[i].z, _segments[i].w),
                                ref minX, ref maxX, ref minZ, ref maxZ);
            }

            // The box's own HALF-DIAGONAL covers any orientation the sub-boxes take, so this pad is valid
            // whatever the heading is and whatever the arc did. Using halfWidth alone would be short by up
            // to halfDepth on a diagonal heading, and a short window is an unbooked loss.
            float boxReach = Mathf.Sqrt(halfWidth * halfWidth + halfDepth * halfDepth);
            float pad = boxReach + (depositRadius + 2) * _texelSize;

            int i0 = Mathf.FloorToInt((minX - pad - _patchMin.x) / _texelSize);
            int i1 = Mathf.CeilToInt((maxX + pad - _patchMin.x) / _texelSize);
            int j0 = Mathf.FloorToInt((minZ - pad - _patchMin.y) / _texelSize);
            int j1 = Mathf.CeilToInt((maxZ + pad - _patchMin.y) / _texelSize);

            i0 = Mathf.Clamp(i0, 0, _resX - 1);
            i1 = Mathf.Clamp(i1, 0, _resX - 1);
            j0 = Mathf.Clamp(j0, 0, _resZ - 1);
            j1 = Mathf.Clamp(j1, 0, _resZ - 1);

            bool overlaps = maxX + pad >= _patchMin.x
                            && minX - pad <= _patchMin.x + _extentX
                            && maxZ + pad >= _patchMin.y
                            && minZ - pad <= _patchMin.y + _extentZ;

            window = new Vector4(i0, j0, i1 - i0 + 1, j1 - j0 + 1);
            return overlaps && window.z > 0f && window.w > 0f;
        }

        /// <summary>
        /// Texel-space AABB covering BOTH the heap profile's support and the release cones.
        ///
        /// The returned <paramref name="window"/> is deliberately NOT clamped to the field, matching v6's
        /// dump window: HeapScan has to see any texels that fall off the patch in order to measure the FULL
        /// weight, and every kernel bounds-checks the texel before touching a texture, so those threads are
        /// simply idle. <paramref name="clamped"/> is the in-field part, and that is what gets recorded as
        /// the erase window - because it is exactly where HeapEmit can write a receipt.
        ///
        /// THE PROFILE'S SUPPORT IS EXACT, NOT ESTIMATED. HeapProfileM is positive precisely where
        /// peak - drop > 0, so in heap-local coordinates it lives in
        /// <code>
        ///     u in [crestAhead - peak/tanBack, crestAhead + peak/tanFront]
        ///     v in [-(halfCrest + peak/tanFlank), +(halfCrest + peak/tanFlank)]
        /// </code>
        /// and the four corners of that rectangle are rotated into world space here. A two-texel pad on top
        /// absorbs the texel-centre offset. Getting this too SMALL would leave receipts outside the recorded
        /// window - the one way this design could leak - so it is derived from the same three tangents the
        /// shader uses rather than from a guessed radius.
        /// </summary>
        private bool ComputeEmitWindow(Vector2 endCenter, Vector2 endFwd, Vector2 endRight,
                                       float heapFrac, float releaseFrac, float releaseRadius,
                                       float tanF, float tanB, float tanR,
                                       out Vector4 window, out RectInt clamped)
        {
            float minX = float.MaxValue, maxX = float.MinValue;
            float minZ = float.MaxValue, maxZ = float.MinValue;
            bool any = false;

            if (heapFrac > 1e-6f && _pileHeightM > 1e-5f)
            {
                float uBack  = _heapCrestAheadM - _pileHeightM / Mathf.Max(0.05f, tanB);
                float uFront = _heapCrestAheadM + _pileHeightM / Mathf.Max(0.05f, tanF);
                float vHalf  = _pileHalfCrestM + _pileHeightM / Mathf.Max(0.05f, tanR);

                for (int su = 0; su < 2; ++su)
                {
                    for (int sv = 0; sv < 2; ++sv)
                    {
                        float u = (su == 0) ? uBack : uFront;
                        float v = (sv == 0) ? -vHalf : vHalf;
                        AccumulatePoint(endCenter + endFwd * u + endRight * v,
                                        ref minX, ref maxX, ref minZ, ref maxZ);
                        any = true;
                    }
                }
            }

            if (releaseFrac > 1e-6f && _releaseConeCount > 0)
            {
                for (int i = 0; i < _releaseConeCount; ++i)
                {
                    var c = new Vector2(_releaseCones[i].x, _releaseCones[i].y);
                    AccumulatePoint(c + new Vector2(-releaseRadius, -releaseRadius),
                                    ref minX, ref maxX, ref minZ, ref maxZ);
                    AccumulatePoint(c + new Vector2(releaseRadius, releaseRadius),
                                    ref minX, ref maxX, ref minZ, ref maxZ);
                    any = true;
                }
            }

            window = Vector4.zero;
            clamped = new RectInt(0, 0, 0, 0);
            if (!any) return false;

            float pad = 2f * _texelSize;

            int i0 = Mathf.FloorToInt((minX - pad - _patchMin.x) / _texelSize);
            int i1 = Mathf.CeilToInt((maxX + pad - _patchMin.x) / _texelSize);
            int j0 = Mathf.FloorToInt((minZ - pad - _patchMin.y) / _texelSize);
            int j1 = Mathf.CeilToInt((maxZ + pad - _patchMin.y) / _texelSize);

            bool overlaps = maxX + pad >= _patchMin.x
                            && minX - pad <= _patchMin.x + _extentX
                            && maxZ + pad >= _patchMin.y
                            && minZ - pad <= _patchMin.y + _extentZ;

            window = new Vector4(i0, j0, i1 - i0 + 1, j1 - j0 + 1);
            if (!overlaps || window.z <= 0f || window.w <= 0f) return false;

            int cx0 = Mathf.Clamp(i0, 0, _resX - 1);
            int cy0 = Mathf.Clamp(j0, 0, _resZ - 1);
            int cx1 = Mathf.Clamp(i1, 0, _resX - 1);
            int cy1 = Mathf.Clamp(j1, 0, _resZ - 1);
            clamped = new RectInt(cx0, cy0, cx1 - cx0 + 1, cy1 - cy0 + 1);

            return true;
        }

        private static void AccumulatePoint(Vector2 p, ref float minX, ref float maxX,
                                            ref float minZ, ref float maxZ)
        {
            if (p.x < minX) minX = p.x;
            if (p.x > maxX) maxX = p.x;
            if (p.y < minZ) minZ = p.y;
            if (p.y > maxZ) maxZ = p.y;
        }

        // ------------------------------------------------------------------ relax dispatch
        private void NoteActivity(Vector4? depositWindow, Vector4? emitWindow, RectInt eraseRect)
        {
            RectInt r = new RectInt(0, 0, 0, 0);
            bool any = false;

            if (depositWindow.HasValue)
            {
                r = ToRect(depositWindow.Value);
                any = true;
            }

            if (emitWindow.HasValue)
            {
                RectInt er = ToRect(emitWindow.Value);
                r = any ? Union(r, er) : er;
                any = true;
            }

            // THE ERASE'S OWN FOOTPRINT MATTERS TOO, and forgetting it would be a real defect rather than
            // a missed optimisation: on the frame a dump empties the pile, the erase is the ONLY thing
            // that touched the heap's old ground, and if that ground fell outside the relax window the
            // crater the erase left would never settle.
            if (eraseRect.width > 0 && eraseRect.height > 0)
            {
                r = any ? Union(r, eraseRect) : eraseRect;
                any = true;
            }

            if (!any) return;

            _trailRect[_trailHead] = r;
            _trailTime[_trailHead] = _trailClock;
            _trailHead = (_trailHead + 1) % kTrailCapacity;
            if (_trailCount < kTrailCapacity) _trailCount++;
        }

        private RectInt ToRect(Vector4 window)
        {
            int x0 = Mathf.Clamp((int)window.x, 0, Mathf.Max(0, _resX - 1));
            int y0 = Mathf.Clamp((int)window.y, 0, Mathf.Max(0, _resZ - 1));
            int x1 = Mathf.Clamp((int)window.x + (int)window.z - 1, 0, Mathf.Max(0, _resX - 1));
            int y1 = Mathf.Clamp((int)window.y + (int)window.w - 1, 0, Mathf.Max(0, _resZ - 1));
            return new RectInt(x0, y0, Mathf.Max(1, x1 - x0 + 1), Mathf.Max(1, y1 - y0 + 1));
        }

        private static RectInt Union(RectInt a, RectInt b)
        {
            int x0 = Mathf.Min(a.xMin, b.xMin);
            int y0 = Mathf.Min(a.yMin, b.yMin);
            int x1 = Mathf.Max(a.xMax, b.xMax);
            int y1 = Mathf.Max(a.yMax, b.yMax);
            return new RectInt(x0, y0, x1 - x0, y1 - y0);
        }

        /// <summary>
        /// THE PING-PONG IS DIFFERENT IN THE TWO PATHS AND IT HAS TO BE. Whole field: A -> B and swap,
        /// because B is entirely rewritten. Windowed: A -> B over the window and then COPY the window back
        /// to A, with NO swap, because outside the window B holds a copy of the field from two iterations
        /// ago and promoting it would silently revert everything the rest of the step did.
        /// </summary>
        private void DispatchRelax()
        {
            int iterations = Mathf.Clamp(_relaxIterations, 0, 8);
            _relaxDispatchesLast = 0;

            if (!_relaxWindowEnabled)
            {
                _relaxRect = new RectInt(0, 0, _resX, _resZ);
                _relaxTexels = _resX * _resZ;
                _cs.SetVector(kRelaxWindow, new Vector4(0f, 0f, _resX, _resZ));

                for (int i = 0; i < iterations; ++i)
                {
                    Dispatch(K_RELAX, _resX, _resZ, _heightA, _heightB);
                    _relaxDispatchesLast++;
                    Swap();
                }
                return;
            }

            if (!ComputeRelaxWindow(iterations, out RectInt rect))
            {
                _relaxRect = new RectInt(0, 0, 0, 0);
                _relaxTexels = 0;
                return;
            }

            _relaxRect = rect;
            _relaxTexels = rect.width * rect.height;

            if (iterations <= 0) return;

            _cs.SetVector(kRelaxWindow, new Vector4(rect.xMin, rect.yMin, rect.width, rect.height));

            for (int i = 0; i < iterations; ++i)
            {
                Dispatch(K_RELAX, rect.width, rect.height, _heightA, _heightB);
                Dispatch(K_COPY_RECT, rect.width, rect.height, _heightB, _heightA);
                _relaxDispatchesLast += 2;
            }
        }

        private bool ComputeRelaxWindow(int iterations, out RectInt rect)
        {
            rect = new RectInt(0, 0, 0, 0);

            float cutoff = _trailClock - Mathf.Max(0.01f, _relaxTrailSeconds);
            bool any = false;

            for (int i = 0; i < _trailCount; ++i)
            {
                int idx = (_trailHead - 1 - i + kTrailCapacity * 2) % kTrailCapacity;
                if (_trailTime[idx] < cutoff) break;      // the ring is in time order walking backwards

                rect = any ? Union(rect, _trailRect[idx]) : _trailRect[idx];
                any = true;
            }

            if (!any) return false;

            int pad = Mathf.Clamp(iterations, 0, 8) + Mathf.Clamp(_relaxWindowPadTexels, 0, 64);

            int x0 = rect.xMin - pad;
            int y0 = rect.yMin - pad;
            int x1 = rect.xMax + pad;
            int y1 = rect.yMax + pad;

            // Snap outward to the thread group so the dispatch has no partially filled group. Always safe:
            // a bigger no-flux box is a weaker constraint, never a wrong one.
            x0 = Mathf.Max(0, (x0 / kThreadGroup) * kThreadGroup);
            y0 = Mathf.Max(0, (y0 / kThreadGroup) * kThreadGroup);
            x1 = Mathf.Min(_resX, RoundUpTo(Mathf.Max(x1, x0 + kThreadGroup), kThreadGroup));
            y1 = Mathf.Min(_resZ, RoundUpTo(Mathf.Max(y1, y0 + kThreadGroup), kThreadGroup));

            rect = new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
            return rect.width > 0 && rect.height > 0;
        }

        private void BuildCoarseMax()
        {
            _cs.SetInt(kCoarseResX, _coarseResX);
            _cs.SetInt(kCoarseResZ, _coarseResZ);
            _cs.SetInt(kCoarseBlock, _coarseBlock);
            _cs.SetInt(kCoarseDilateId, Mathf.Clamp(_coarseDilate, 1, 8));

            Dispatch(K_COARSE_BLOCK, _coarseResX, _coarseResZ, _heightA, null);
            Dispatch(K_COARSE_DILATE, _coarseResX, _coarseResZ, null, null);
        }

        private void BuildDilatedHeight()
        {
            _cs.SetInt(kHeightDilateR, Mathf.Clamp(_heightDilateRadius, 0, 6));
            _cs.SetInt(kFilletDilateR, Mathf.Clamp(_filletDilateRadius, 0, 8));
            Dispatch(K_HEIGHT_DILATE, _resX, _resZ, _heightA, null);
        }

        /// <summary>
        /// Bakes the lump lift over the dirty window, and pushes the lattice uniforms whether or not it
        /// dispatches - because <see cref="BuildCoarseMax"/> runs immediately after this and CoarseMaxBlock
        /// reads _LumpRadiusM as its own scalar on/off switch and _LumpBakeResX/Z to clamp its ring.
        ///
        /// AT RADIUS 0 NOTHING IS DISPATCHED AND NOTHING IS READ. That is not an optimisation, it is the
        /// A/B: the recorded lobes-off baseline (gpu 6.13 ms, meanSteps 23.70) has to stay reproducible
        /// with the bake in the build, so the off path must cost exactly what it cost before - no bake
        /// dispatch, no bake read in the coarse build, and no lump headroom in the march bias.
        /// </summary>
        private void BuildLumpBake()
        {
            _cs.SetInt(kLumpBakeResX, _resX * 2);
            _cs.SetInt(kLumpBakeResZ, _resZ * 2);
            _cs.SetFloat(kLumpRadiusM, _lumpBakeRadiusM);
            _cs.SetFloat(kLumpSpacingM, _lumpBakeSpacingM);
            _cs.SetFloat(kLumpSpacingInv, 1f / Mathf.Max(1e-4f, _lumpBakeSpacingM));
            _cs.SetFloat(kLumpJitter, _lumpBakeJitter);
            _cs.SetFloat(kLumpRadiusVary, _lumpBakeRadiusVary);
            _cs.SetFloat(kLumpGateInv, 1f / Mathf.Max(1e-4f, _lumpBakeGateDepthM));
            _cs.SetFloat(kLumpReliefInv, 1f / Mathf.Max(1e-4f, _lumpBakeReliefM));
            _cs.SetFloat(kLumpSlopeStr, _lumpBakeSlopeStrength);
            _cs.SetFloat(kLumpMinSnowH, _lumpBakeMinSnowHeightM);

            _lumpBakeDispatchesLast = 0;
            _lumpBakeRect = new RectInt(0, 0, 0, 0);
            _lumpBakeTexels = 0;

            if (_lumpBakeRadiusM <= 1e-5f) return;
            if (!ComputeLumpBakeWindow(out RectInt rect)) return;

            _lumpBakeRect = rect;
            _lumpBakeTexels = rect.width * rect.height;

            _cs.SetVector(kLumpBakeWindow, new Vector4(rect.xMin, rect.yMin, rect.width, rect.height));
            Dispatch(K_LUMP_BAKE, rect.width, rect.height, _heightA, null);

            _lumpBakeDispatchesLast = 1;
            _lumpBakeAllDirty = false;
        }

        /// <summary>
        /// The bake window, in BAKE texels: the relax window - the same activity-trail rect
        /// <see cref="DispatchRelax"/> uses, already padded there by the iteration count and
        /// <see cref="_relaxWindowPadTexels"/> - expanded by the lump reach and doubled.
        ///
        /// THREE cases, all of them real:
        ///  * everything dirty (first allocation, a field reset, or any lattice knob changed) -> the whole
        ///    texture, once. A windowed bake after a knob change would leave the rest of the field holding
        ///    lobes built from the old spacing.
        ///  * an empty relax window (nothing has moved for _relaxTrailSeconds) -> nothing to do, no
        ///    dispatch, and the telemetry reports 0 texels rather than pretending it baked.
        ///  * otherwise the padded, doubled, group-snapped window.
        /// </summary>
        private bool ComputeLumpBakeWindow(out RectInt rect)
        {
            int bakeResX = _resX * 2;
            int bakeResZ = _resZ * 2;

            if (_lumpBakeAllDirty)
            {
                rect = new RectInt(0, 0, bakeResX, bakeResZ);
                return true;
            }

            RectInt r = _relaxRect;
            rect = new RectInt(0, 0, 0, 0);
            if (r.width <= 0 || r.height <= 0) return false;

            int pad = LumpBakePadTexels();

            int x0 = (r.xMin - pad) * 2;
            int y0 = (r.yMin - pad) * 2;
            int x1 = (r.xMax + pad) * 2;
            int y1 = (r.yMax + pad) * 2;

            // Snap outward to the thread group so the dispatch has no partially filled group, exactly as
            // the relax window does. Always safe: baking a texel that did not need it writes the same
            // value it already held, because the lift is a pure function of position and field.
            x0 = Mathf.Max(0, (x0 / kThreadGroup) * kThreadGroup);
            y0 = Mathf.Max(0, (y0 / kThreadGroup) * kThreadGroup);
            x1 = Mathf.Min(bakeResX, RoundUpTo(Mathf.Max(x1, x0 + kThreadGroup), kThreadGroup));
            y1 = Mathf.Min(bakeResZ, RoundUpTo(Mathf.Max(y1, y0 + kThreadGroup), kThreadGroup));

            rect = new RectInt(x0, y0, Mathf.Max(0, x1 - x0), Mathf.Max(0, y1 - y0));
            return rect.width > 0 && rect.height > 0;
        }

        /// <summary>
        /// How far, in FIELD texels, a one-texel field edit can move the baked lift.
        ///
        /// The STRICT dependency is the first term: the lift's gates are all this reads of the field. The
        /// depth gate takes the field BILINEARLY, so +-1 texel, and the relief term takes the fillet
        /// dilation, which is itself a max over +-_filletDilateRadius. The lattice geometry does not read
        /// the field at all.
        ///
        /// The second term is the LUMP REACH and it is deliberate headroom: a lobe centre sits within
        /// 0.5 * (1 + jitter) * spacing of its own cell centre and its cap spans r, so this is how far one
        /// lobe extends from the cell that owns it. Including it means a lobe overlapping the dirty region
        /// is re-baked whole instead of half updated at the window edge, and it comfortably covers the one
        /// extra bake texel the marcher's bilinear tap and the coarse-max ring reach for.
        /// </summary>
        private int LumpBakePadTexels()
        {
            int gatePad = Mathf.Clamp(_filletDilateRadius, 0, 8) + 1;

            float reachM = _lumpBakeRadiusM
                         + 0.5f * (1f + Mathf.Clamp01(_lumpBakeJitter)) * Mathf.Max(0f, _lumpBakeSpacingM);

            return gatePad + Mathf.CeilToInt(reachM / Mathf.Max(1e-4f, _texelSize));
        }

        private void PushStaticUniforms()
        {
            _cs.SetInt(kResX, _resX);
            _cs.SetInt(kResZ, _resZ);
            _cs.SetFloat(kTexelSize, _texelSize);
            _cs.SetFloat(kInvTexelSize, 1f / _texelSize);
            _cs.SetVector(kPatchMin, new Vector4(_patchMin.x, _patchMin.y, 0f, 0f));
            _cs.SetFloat(kStartDepth, _startDepth);
        }

        private void Swap()
        {
            RenderTexture t = _heightA;
            _heightA = _heightB;
            _heightB = t;
        }

        // ------------------------------------------------------------------ CPU mirror readback
        //
        // Plain Request, not RequestIntoNativeArray: the latter captures the managed array's
        // AtomicSafetyHandle and hands it to native code, which bumps the handle's version node when the
        // request retires and permanently invalidates the caller's NativeArray. The data is copied out of
        // request.GetData<T>() inside the callback instead, where the request's own handle is still live.
        private void RequestReadbacks()
        {
            if (_frame % Mathf.Max(1, _readbackInterval) != 0) return;

            if (!_mirrorRequested && _mirror != null)
            {
                _mirrorRequested = true;
                AsyncGPUReadback.Request(_mirrorRt, 0, OnMirrorRead);
            }

            if (!_statsRequested && _statsCpu != null)
            {
                _statsRequested = true;
                AsyncGPUReadback.Request(_statsBuffer, OnStatsRead);
            }

            if (!_carryRequested && _carryCpu != null)
            {
                _carryRequested = true;

                // Snapshot the cumulative DEPOSITED volume AT THE MOMENT OF THE REQUEST. The read-back
                // this issues will observe a pile that already reflects every deposit up to here, so when
                // it lands this snapshot becomes the new baseline and only deposits issued after it still
                // need correcting. See DEPOSIT LATENCY COMPENSATION.
                _depositTotalAtRequest = _depositTotalM3;
                AsyncGPUReadback.Request(_carryBuffer, OnCarryRead);
            }
        }

        private void OnMirrorRead(AsyncGPUReadbackRequest request)
        {
            _mirrorRequested = false;
            if (request.hasError || _mirror == null) return;

            NativeArray<float> data = request.GetData<float>();
            int count = Mathf.Min(data.Length, _mirror.Length);
            if (count <= 0) return;

            NativeArray<float>.Copy(data, 0, _mirror, 0, count);
            _mirrorValid = true;

            // Accumulated in DOUBLE, which at this size is not fussiness: the mirror is 211k cells and the
            // total is ~3,960 m3, so a float32 accumulator would lose the low bits of every late addition
            // and the invariant would read a drift of litres the simulation never had.
            double sum = 0.0;
            for (int i = 0; i < count; ++i) sum += _mirror[i];

            _mirrorVolume = (float)(sum / count * _extentX * _extentZ);
        }

        private void OnStatsRead(AsyncGPUReadbackRequest request)
        {
            _statsRequested = false;
            if (request.hasError || _statsCpu == null) return;

            NativeArray<int> data = request.GetData<int>();
            int count = Mathf.Min(data.Length, _statsCpu.Length);
            if (count < 15) return;

            NativeArray<int>.Copy(data, 0, _statsCpu, 0, count);

            _removedLast   = _statsCpu[0] * kVolScaleInv;
            _depositedLast = _statsCpu[1] * kVolScaleInv;
            _fieldMaxLast  = _statsCpu[9] * 1e-5f;
            _bermLast      = _statsCpu[10] * kVolScaleInv;
            _terrainCutLast = _statsCpu[11] * kVolScaleInv;
            _releaseDepositedLast = _statsCpu[12] * kVolScaleInv;
            _heapReclaimedLast = _statsCpu[13] * kVolScaleInv;
            _heapEmittedLast = _statsCpu[14] * kVolScaleInv;

            // THE STANDING GUARD. _Stats[3] is at the LEDGER'S scale, not kVolScale, because what it sums
            // is a ledger integer - the share a placement kernel resolved for a texel it could not write.
            _unplacedL = _statsCpu[3] * kCarryScaleInv * 1000f;
            if (_unplacedL > _unplacedPeakL) _unplacedPeakL = _unplacedL;
        }

        private void OnCarryRead(AsyncGPUReadbackRequest request)
        {
            _carryRequested = false;
            if (request.hasError || _carryCpu == null) return;

            NativeArray<int> data = request.GetData<int>();
            int count = Mathf.Min(data.Length, _carryCpu.Length);
            if (count < kCarrySlots) return;

            NativeArray<int>.Copy(data, 0, _carryCpu, 0, count);

            _ledgerL = _carryCpu[0] * kCarryScaleInv * 1000f;

            // THE PILE, as the GPU measured it: the ledger at the end of Settle, before the emit spent it.
            // See the Settle kernel for why it cannot be slot 3.
            _pileRawM3 = _carryCpu[10] * kCarryScaleInv;

            // What the emit actually wrote as recorded heap, kept as independent evidence of the split. On
            // the frame after a deposit this is genuinely 0, because nothing was recorded.
            _heapPlacedM3 = _carryCpu[3] * kCarryScaleInv;

            // Booked deletion. CUMULATIVE in [6] because it is read back every few frames and a per-step
            // value would simply be missed; [7] is the last step's figure, for the rate.
            //
            // PLUS THE REMAINDER IN [11], and that term is what makes the published figure agree with the
            // ledger EXACTLY rather than to within the coarser book's quantum. [6] counts in kDeletedScale
            // units, i.e. ten ledger units each; Settle keeps the leftover ledger units in [11] rather than
            // truncating them away, so [6]*10 + [11] is precisely the sum of every debit it has taken.
            //
            // TWO TERMS, NOT (a*10 + b) SCALED ONCE: [6]*10 reaches 1.4e9 on a long run and a float32 that
            // large has a 128-unit ULP, i.e. 13 mL of readout noise. Scaling each slot at its own
            // resolution keeps both inside float32's exact integer range.
            _deletedL = _carryCpu[6] * kDeletedScaleInv * 1000f
                      + _carryCpu[11] * kCarryScaleInv * 1000f;
            _deletedStepL = _carryCpu[7] * kDeletedScaleInv * 1000f;

            // The walls, cumulative, so "how much have I windrowed" is a memory of what was driven. At
            // kDeletedScale rather than kCarryScale - HeapFinish rescales it on the way in, because a
            // running total at the ledger's own resolution would wrap int32 inside one lap.
            _releaseTotalM3 = _carryCpu[9] * kDeletedScaleInv;

            // This read-back has seen every deposit up to its own request, so that snapshot is the new
            // baseline; anything deposited since then is still ahead of it and stays corrected for.
            _depositTotalSeenByRead = _depositTotalAtRequest;
        }
    }
}
