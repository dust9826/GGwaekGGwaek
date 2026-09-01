using UnityEngine;

namespace SnowSpike.PileV7
{
    /// <summary>
    /// One frame of driver intent, from the keyboard or from the autopilot - INCLUDING THE VERBS.
    ///
    /// THERE IS NO DUMP FIELD. Reversing with the blade down IS the dump, so the third verb is expressed
    /// by the sign of <see cref="Throttle"/> and nothing else. That is not a saving of one bool: a dump
    /// key and a reverse-deposit would be two ways to ask for the same thing, and one of them would
    /// inevitably be the one the player learned instead of the grammar.
    /// </summary>
    public struct SnowCarInputV7
    {
        /// <summary>-1 brake / reverse, +1 accelerate. WITH THE BLADE DOWN, REVERSING IS THE DEPOSIT.</summary>
        public float Throttle;

        /// <summary>-1 left, +1 right.</summary>
        public float Steer;

        /// <summary>Space: the HELD drift key.</summary>
        public bool Drift;

        /// <summary>Shift.</summary>
        public bool Boost;

        /// <summary>VERB 1. True = blade UP: no cut, no accumulation, no plough resistance.</summary>
        public bool BladeUp;

        /// <summary>VERB 2. The DISCHARGE SIDE: -1 LEFT, 0 STRAIGHT, +1 RIGHT.</summary>
        public int BladeAngle;
    }

    /// <summary>
    /// Every handling knob, in one block, filled from the bootstrap's serialized fields each frame.
    ///
    /// A struct passed in per frame rather than fields on a runtime-created component ON PURPOSE: a
    /// component the bootstrap adds at runtime has no inspector while the editor is stopped and the Unity
    /// CLI refuses component edits during play, so every knob on it would need a forwarding call with a
    /// sentinel. Keeping the state here and the KNOBS on the one component that is actually in the scene
    /// means there is nothing to forward and nothing that can be unreachable.
    /// </summary>
    public struct SnowCarSettingsV7
    {
        // ---- longitudinal ----
        public float AccelMps2;
        public float TopSpeedMps;
        public float BrakeMps2;
        public float ReverseTopSpeedMps;
        public float ReverseAccelMps2;
        public float CoastDecelMps2;
        public float BoostMultiplier;

        // ---- steering ----
        public float SteerRateDegPerSec;
        public float SteerRateAtTopSpeed;
        public float SteerFadeSpeedMps;
        public float SteerMinSpeedMps;
        public float TurnInPerSec;

        // ---- grip ----
        public float LateralGrip;
        public float GripAtTopSpeed;
        public float DriftGripMultiplier;
        public float DriftSteerMultiplier;

        // ---- THE PILE'S MASS, and what it does to all of the above ----
        public float PileMassKg;
        public float MassRefKg;
        public float SpeedFactorAtRef;
        public float AccelFactorAtRef;
        public float BrakeFactorAtRef;
        public float CoastFactorAtRef;
        public float TurnInFactorAtRef;
        public float GripFactorAtRef;
        public float TurnRadiusGrowAtRef;

        // ---- the snow's own resistance, separate from the load ----
        public float SnowDragBaseMps2;
        public float SnowDragPerSpeed;
        public float SnowBiteDepthM;
        public float SnowSampleAheadM;
        public float SnowRideFactor;

        // ---- WHAT I AM DRIVING THROUGH, which is a different question from what I am carrying ----
        //
        // The mass factors above answer "how heavy is my load". THESE answer "how deep is the snow under
        // my wheels", and the two are MULTIPLICATIVE on top speed because they are independent: an empty
        // blade in virgin snow is slow for one reason, a full blade on a scraped lane is slow for another.
        // This pair is the reward that makes blade-up transit worth anything - a cleared lane is a road.
        public float DepthSpeedSatDepthM;
        public float DepthSpeedFloor;

        // ---- THE VERBS' OWN HANDLING KNOBS ----
        //
        // The forward speed at and above which the blade's face is taken to be SUPPORTING the heap.
        // Below half of it the blade lets go, and the heap is deposited where it stands. Hysteresis 2:1,
        // so a vehicle hovering at the threshold does not chatter between carrying and depositing.
        public float BladeAttachSpeedMps;

        // THE ANGLED BLADE'S REACTION, per m3/s of cast rate. Casting to one side shoves the vehicle the
        // OTHER way and yaws it the other way, both proportional to the cast rate - so the cue grows in
        // deep snow and vanishes on a clear lane, and the player can learn it.
        public float CastPushMps2PerM3s;
        public float CastYawDegPerM3s;

        // The extra deceleration of BREAKING THE PILE LOOSE, at a full blade and a face re-steepening at
        // its full rate. Brief by construction: the face only climbs while it is behind the shove angle.
        public float FaceBreakLooseMps2;

        // ---- what the FIELD measured last step, for the two reactions above ----
        //
        // Read one step late, deliberately. The vehicle integrates BEFORE the field steps - it has to, the
        // field needs the blade's exact start and end pose for the frame - so these are last step's
        // numbers. On a 0.4 s face collapse and a cast rate that changes over metres, one frame of lag is
        // not observable, and the alternative is a second, CPU-side estimate of both, i.e. a second source
        // of truth for something the GPU already measured.
        public float CastRateM3PerSec;
        public float FaceSteepen01;

        // ---- body reaction (all neutral at 0) ----
        public float PitchAccelDeg;
        public float PitchLoadDeg;
        public float RollLateralDeg;
        public float BodyResponsePerSec;

        // ---- world ----
        public float RideHeightM;
        public float BoundsMarginM;
        public float WallSlideKeep;
    }

    /// <summary>
    /// The arcade vehicle, with the PILE's mass wired into it.
    ///
    /// THE ONE EQUATION THAT MATTERS is v5's, which is the reference project's shape:
    /// <code>
    ///     heading += yawRate * dt;                 // the BODY turns
    ///     vF = dot(v, fwd);  vR = dot(v, right);   // decompose against the NEW axes
    ///     vR *= exp(-grip * dt);                   // the lateral part is DAMPED, not cancelled
    ///     v  = fwd * vF + right * vR;
    /// </code>
    ///
    /// EVERY COUPLING IS V6'S, UNCHANGED IN SHAPE:
    /// <code>
    ///     f(M) = 1 / (1 + k*M),   k solved so f(MassRefKg) == the "at reference" knob
    /// </code>
    /// 1 at zero mass, exactly the stated value at the reference mass, and it keeps falling for ever
    /// without reaching zero. It is also the right shape rather than a convenient one: acceleration is
    /// F/m, so a constant-force drive with a load already IS a reciprocal. The ordering of the seven -
    /// acceleration and braking falling harder than top speed, because weight is felt in the derivative
    /// rather than in the value - is v6's measured result and is kept verbatim.
    ///
    /// WHAT V7 RETUNES, AND WHY IT HAD TO
    /// ----------------------------------
    /// ONLY THE REFERENCE MASS. v6's load was an unbounded running total: a ball could reach 30 m3 /
    /// 9 tonnes if you drove long enough, so 2,400 kg (8 m3) sat in the middle of a wide range and the
    /// couplings had somewhere to go above it.
    ///
    /// v7's load is BOUNDED BY THE HEAP'S CAPACITY. At the shipped shape knobs that is 6.17 m3, i.e.
    /// 1,850 kg at 300 kg/m3, and the pile spends most of a run pinned near it because the overflow spills
    /// rather than accumulating. Leaving the reference at 2,400 would mean the couplings never reached
    /// their stated values at all - a full heap would only be 0.77 of the way to "at reference" and the
    /// whole mass read would be a third of what the numbers say.
    ///
    /// 1,200 kg is 4 m3, i.e. 65% of capacity - reached in under two seconds of ploughing - so the stated
    /// factors are what a HALF-FULL blade feels like and a full one is meaningfully heavier than that.
    /// DERIVED, since this file cannot run the editor: at 1,850 kg the factors come out
    /// <code>
    ///     accel  1/(1 + (1/0.22-1)/1200 * 1850) = 0.155      top speed 0.347  -> 4.9 m/s from 14
    ///     brake  0.194     coast 0.072     turn-in 0.198     grip 0.394
    ///     turn radius x5.6 wider at the same speed
    /// </code>
    /// which is a vehicle that is unmistakably loaded and still drivable, and which reversing out from
    /// under the pile instantly restores. THE PARENT AGENT MEASURES; these are the numbers to measure
    /// against.
    ///
    /// WHAT THE VERB GRAMMAR ADDS TO THE HANDLING
    /// ------------------------------------------
    ///   * A DEPTH-DRIVEN TOP SPEED, multiplicative with and separate from the carried-mass factor. The
    ///     two answer different questions - what I am carrying against what I am driving through - and
    ///     keeping them separate is what makes a scraped lane read as a ROAD.
    ///   * PLOUGH RESISTANCE ONLY WITH THE BLADE DOWN. Raising it stops the snow drag entirely; the depth
    ///     term is what is left, so blade-up transit through virgin snow is still not fast, and blade-up
    ///     transit over a cleared lane is.
    ///   * THE ATTACHMENT, directional and hysteretic: the blade's face only supports the heap while the
    ///     vehicle is driving into it. Stopping, reversing or raising lets go.
    ///   * THE ANGLED BLADE'S REACTION: a lateral shove and a yaw moment away from the discharge side,
    ///     both proportional to the cast rate.
    ///   * BREAKING THE PILE LOOSE: a brief extra deceleration while the leading face is re-steepening.
    ///
    /// Integrated in Update, not FixedUpdate, for v5's reason: the snow step needs the blade's EXACT start
    /// and end pose for the frame it is about to draw, because the cut is the swept volume between them.
    /// </summary>
    public sealed class SnowPileCarV7
    {
        // ---- pose and motion ----
        private Vector2 _pos;
        private float _headingDeg;
        private Vector2 _vel;

        private float _steerApplied;
        private float _yawRateDegPerSec;
        private float _slipAngleDeg;
        private float _prevForwardSpeed;
        private float _longAccel;
        private float _latAccel;

        private float _bodyPitchDeg;
        private float _bodyRollDeg;
        private float _rideY;

        private bool _driftActive;
        private float _snowDepthAhead;
        private float _snowDepthUnder;
        private float _snowDragLast;

        // ---- THE VERBS as this frame resolved them ----
        private bool _bladeDown = true;
        private int _bladeAngleState;
        private bool _bladeAttached;
        private float _fDepth = 1f;
        private float _castPushMps2;
        private float _castYawDegPerSec;
        private float _breakLooseMps2;

        // ---- the mass factors actually applied this frame, published for the [V7] line ----
        private float _fSpeed = 1f;
        private float _fAccel = 1f;
        private float _fBrake = 1f;
        private float _fCoast = 1f;
        private float _fTurnIn = 1f;
        private float _fGrip = 1f;
        private float _gTurnRadius = 1f;

        // ---- the frame's BLADE sweep, captured before and after the integration ----
        private Vector2 _bladeStartXZ;
        private Vector2 _bladeStartFwd;
        private Vector2 _bladeEndXZ;
        private Vector2 _bladeEndFwd;

        public Vector2 PositionXZ => _pos;
        public float HeadingDeg => _headingDeg;
        public Vector2 VelocityXZ => _vel;

        /// <summary>Signed speed along the vehicle's facing, in m/s. Negative while reversing.</summary>
        public float ForwardSpeed => Vector2.Dot(_vel, ForwardXZ);
        public float Speed => _vel.magnitude;

        public float YawRateDegPerSec => _yawRateDegPerSec;
        public float SlipAngleDeg => _slipAngleDeg;
        public bool DriftActive => _driftActive;
        public float SteerApplied => _steerApplied;
        public float LongitudinalAccel => _longAccel;
        public float LateralAccel => _latAccel;

        public float SnowDepthAheadM => _snowDepthAhead;
        public float SnowDepthUnderM => _snowDepthUnder;
        public float SnowDragMps2 => _snowDragLast;

        // ---- THE VERBS, published for the [V7] line and for the field's next step ----

        /// <summary>Verb 1: true = ploughing, false = transit.</summary>
        public bool BladeDown => _bladeDown;

        /// <summary>Verb 2: the discharge side, -1 LEFT, 0 STRAIGHT, +1 RIGHT.</summary>
        public int BladeAngleState => _bladeAngleState;

        /// <summary>
        /// Verb 3, resolved: is the blade's face supporting the heap. Blade down AND driving forward into
        /// it, with 2:1 hysteresis on the attach speed so it cannot chatter.
        /// </summary>
        public bool BladeAttached => _bladeAttached;

        /// <summary>
        /// THE DEPTH-DRIVEN TOP SPEED FACTOR, 0..1. 1 on scraped ground, the floor fraction in snow at or
        /// past the saturation depth. MULTIPLICATIVE with <see cref="MassSpeedFactor"/> and separate from
        /// it by design - see the class header.
        /// </summary>
        public float DepthSpeedFactor => _fDepth;

        /// <summary>Lateral acceleration the angled blade's cast applied this frame, m/s2, signed.</summary>
        public float CastPushMps2 => _castPushMps2;

        /// <summary>Yaw rate the angled blade's cast added this frame, deg/s, signed.</summary>
        public float CastYawDegPerSec => _castYawDegPerSec;

        /// <summary>Extra deceleration from breaking the pile loose this frame, m/s2.</summary>
        public float BreakLooseMps2 => _breakLooseMps2;

        public float BodyPitchDeg => _bodyPitchDeg;
        public float BodyRollDeg => _bodyRollDeg;
        public float RideY => _rideY;

        public float MassSpeedFactor => _fSpeed;
        public float MassAccelFactor => _fAccel;
        public float MassBrakeFactor => _fBrake;
        public float MassCoastFactor => _fCoast;
        public float MassTurnInFactor => _fTurnIn;
        public float MassGripFactor => _fGrip;

        /// <summary>How much wider the same steering input scribes its arc, as a multiple. 1 = empty.</summary>
        public float MassTurnRadiusFactor => _gTurnRadius;

        /// <summary>
        /// The turn radius the vehicle is CURRENTLY scribing, in metres, or 0 when it is not turning. R is
        /// v / omega, so this is the number the turn-radius coupling is supposed to move and it is reported
        /// directly rather than inferred from the yaw rate.
        /// </summary>
        public float TurnRadiusM
        {
            get
            {
                float w = Mathf.Abs(_yawRateDegPerSec) * Mathf.Deg2Rad;
                return (w > 1e-3f) ? Mathf.Abs(ForwardSpeed) / w : 0f;
            }
        }

        public Vector2 ForwardXZ
        {
            get
            {
                float r = _headingDeg * Mathf.Deg2Rad;
                return new Vector2(Mathf.Sin(r), Mathf.Cos(r));
            }
        }

        /// <summary>
        /// The right-hand XZ pair matching Unity's left-handed world axes: for forward = (0,1), i.e. +Z,
        /// this gives right = (1,0), i.e. +X.
        /// </summary>
        public Vector2 RightXZ
        {
            get { Vector2 f = ForwardXZ; return new Vector2(f.y, -f.x); }
        }

        public Vector2 BladeStartXZ => _bladeStartXZ;
        public Vector2 BladeEndXZ => _bladeEndXZ;
        public Vector2 BladeStartForward => _bladeStartFwd;
        public Vector2 BladeEndForward => _bladeEndFwd;

        /// <summary>
        /// f(M) = 1 / (1 + k*M) with k solved so f(refKg) == atRef.
        ///
        /// 1 at zero mass, exactly atRef at the reference mass, monotone, and never zero - so there is no
        /// mass at which the vehicle is bricked, only masses at which it is miserable. A wall would be a
        /// bug report; a hyperbola is a decision the player made.
        /// </summary>
        public static float MassFactor(float massKg, float refKg, float atRef)
        {
            float a = Mathf.Clamp(atRef, 0.02f, 1f);
            float k = (1f / a - 1f) / Mathf.Max(1f, refKg);
            return 1f / (1f + k * Mathf.Max(0f, massKg));
        }

        /// <summary>The same curve the other way up, for quantities that GROW with mass.</summary>
        public static float MassGrowth(float massKg, float refKg, float atRef)
        {
            float a = Mathf.Max(1f, atRef);
            float k = (a - 1f) / Mathf.Max(1f, refKg);
            return 1f + k * Mathf.Max(0f, massKg);
        }

        /// <summary>Teleports the vehicle and kills every derivative. Used at course start and on wrap.</summary>
        public void Reset(Vector2 posXZ, float headingDeg, float bladeOffsetM)
        {
            _pos = posXZ;
            _headingDeg = headingDeg;
            _vel = Vector2.zero;
            _steerApplied = 0f;
            _yawRateDegPerSec = 0f;
            _slipAngleDeg = 0f;
            _prevForwardSpeed = 0f;
            _longAccel = 0f;
            _latAccel = 0f;
            _bodyPitchDeg = 0f;
            _bodyRollDeg = 0f;
            _driftActive = false;

            // THE BLADE LETS GO ON A TELEPORT, which is the only defensible answer: the vehicle is
            // somewhere else now, and a receipt for a heap it is no longer in front of would be a claim on
            // mass it cannot reach. The field's own erase retires that receipt on the next step, so the
            // heap is left standing where the vehicle used to be - conserving, and visible.
            _bladeAttached = false;
            _fDepth = 1f;
            _castPushMps2 = 0f;
            _castYawDegPerSec = 0f;
            _breakLooseMps2 = 0f;

            // Both ends of the sweep collapsed onto the new pose, so the teleport frame's swept volume is
            // the blade's own footprint rather than a box dragged across the whole stage.
            Vector2 b = _pos + ForwardXZ * bladeOffsetM;
            _bladeStartXZ = b;
            _bladeEndXZ = b;
            _bladeStartFwd = ForwardXZ;
            _bladeEndFwd = ForwardXZ;
        }

        /// <summary>Integrates one frame.</summary>
        /// <param name="dt">Clamped frame time.</param>
        /// <param name="s">The handling knobs, straight off the bootstrap, including the pile's mass.</param>
        /// <param name="input">Driver intent for this frame.</param>
        /// <param name="field">The height field, sampled through its CPU mirror only.</param>
        /// <param name="bladeOffsetM">Blade line distance ahead of the vehicle's centre. CONSTANT in v7 -
        /// the blade does not move as the pile grows, unlike v6's ball mount.</param>
        public void Step(float dt, in SnowCarSettingsV7 s, in SnowCarInputV7 input,
                         SnowPileFieldV7 field, float bladeOffsetM)
        {
            dt = Mathf.Max(1e-5f, dt);

            _bladeStartXZ = _pos + ForwardXZ * bladeOffsetM;
            _bladeStartFwd = ForwardXZ;

            _driftActive = input.Drift;

            // ---- THE VERBS, resolved before anything reads them ---------------------------------------
            _bladeDown = !input.BladeUp;
            _bladeAngleState = (input.BladeAngle > 0) ? 1 : ((input.BladeAngle < 0) ? -1 : 0);

            // THE ATTACHMENT IS DIRECTIONAL AND HYSTERETIC, and it is directional IN GENERAL rather than
            // "did the stick just go backwards": a blade's face can only support a heap while the vehicle
            // is driving into it, so stopping lets go just as reversing does. That is what makes the
            // boundary stall escapable without a special case - wedged against a wall the vehicle IS
            // stopped, so the heap detaches, the mass couplings release, and reverse is available.
            //
            // 2:1 hysteresis, because a single threshold on a speed that hovers near it would flicker
            // between carrying and depositing sixty times a second, and each flicker would retire a receipt
            // and re-cut the pile.
            float attachOn = Mathf.Max(0.01f, s.BladeAttachSpeedMps);
            float attachOff = attachOn * 0.5f;
            float fwdNow = Vector2.Dot(_vel, ForwardXZ);
            _bladeAttached = _bladeDown
                          && (_bladeAttached ? (fwdNow > attachOff) : (fwdNow > attachOn));

            // ---- what the PILE'S MASS does to the handling -------------------------------------------
            float m = Mathf.Max(0f, s.PileMassKg);
            float refM = Mathf.Max(1f, s.MassRefKg);

            _fSpeed  = MassFactor(m, refM, s.SpeedFactorAtRef);
            _fAccel  = MassFactor(m, refM, s.AccelFactorAtRef);
            _fBrake  = MassFactor(m, refM, s.BrakeFactorAtRef);
            _fCoast  = MassFactor(m, refM, s.CoastFactorAtRef);
            _fTurnIn = MassFactor(m, refM, s.TurnInFactorAtRef);
            _fGrip   = MassFactor(m, refM, s.GripFactorAtRef);
            _gTurnRadius = MassGrowth(m, refM, s.TurnRadiusGrowAtRef);

            // 0..1 for the purely cosmetic body lean, which does want to saturate.
            float load01 = Mathf.Clamp01(m / refM);

            // ---- turn-in: the steer INPUT is filtered, the steer RATE is not -------------------------
            //
            // Filtering the applied steering angle rather than the yaw rate is what makes a stab of the key
            // feel like a steering wheel with mass instead of like a rotation being switched on.
            float turnIn = Mathf.Max(0.01f, s.TurnInPerSec * _fTurnIn);
            _steerApplied = Mathf.MoveTowards(_steerApplied, Mathf.Clamp(input.Steer, -1f, 1f),
                                              turnIn * dt);

            float fwdSpeedBefore = Vector2.Dot(_vel, ForwardXZ);
            float speedT = Mathf.Clamp01(Mathf.Abs(fwdSpeedBefore) /
                                         Mathf.Max(0.1f, s.SteerFadeSpeedMps));

            // The unloaded yaw rate at THIS speed, then divided by the turn-radius growth. Dividing omega
            // here rather than scaling a "steer factor" is what makes the coupling mean what it says:
            // R = v/omega, so R at this speed is multiplied by exactly _gTurnRadius, whatever the speed
            // curve happens to be doing.
            float steerRate = Mathf.Lerp(s.SteerRateDegPerSec,
                                         s.SteerRateDegPerSec * s.SteerRateAtTopSpeed, speedT)
                            * (_driftActive ? Mathf.Max(0.05f, s.DriftSteerMultiplier) : 1f)
                            / Mathf.Max(1f, _gTurnRadius);

            // No yaw at a standstill, and reversing inverts the steering exactly as a real vehicle does.
            float yawGate = Mathf.Clamp01(Mathf.Abs(fwdSpeedBefore) / Mathf.Max(0.05f, s.SteerMinSpeedMps));
            _yawRateDegPerSec = steerRate * _steerApplied
                              * Mathf.Sign(fwdSpeedBefore == 0f ? 1f : fwdSpeedBefore)
                              * yawGate;

            // ---- THE ANGLED BLADE'S YAW MOMENT ------------------------------------------------------
            //
            // The cast throws snow toward the discharge side, so the snow throws the BLADE toward the other
            // side; the blade is ahead of the centre of mass, so that force yaws the nose the same way.
            // Angled LEFT therefore yaws RIGHT, which is the cue the player learns.
            //
            // ADDED TO THE YAW RATE rather than to the steering input, deliberately: a steering-input
            // disturbance would be filtered by the turn-in rate and would fade as the load grew, so a full
            // blade casting hard would pull LESS than an empty one. This is a moment, and it acts like one.
            //
            // The magnitude is proportional to the CAST RATE, so it grows in deep snow and vanishes on a
            // clear lane without anything testing the depth. NOTE THE KNOWN RISK, which is why the gain is
            // a knob: at the default 4.0 deg/s per m3/s, angling a blade at its straight equilibrium
            // (4.85 m3 at 1.97 m/s) casts 2.08 m3/s, which is 8.3 deg/s against the 25.7 deg/s of steering
            // authority a 1,456 kg load has left - 32% of it, decaying over the 2.3 s the blade takes to
            // drain into the windrow. Higher and it fights the player's intended line in a narrow corridor.
            _castYawDegPerSec = 0f;
            if (_bladeAngleState != 0 && _bladeAttached)
            {
                _castYawDegPerSec = -_bladeAngleState * s.CastYawDegPerM3s
                                  * Mathf.Max(0f, s.CastRateM3PerSec);
                _yawRateDegPerSec += _castYawDegPerSec * yawGate;
            }

            _headingDeg += _yawRateDegPerSec * dt;
            if (_headingDeg > 360f) _headingDeg -= 360f;
            if (_headingDeg < -360f) _headingDeg += 360f;

            // ---- decompose against the NEW axes -----------------------------------------------------
            Vector2 fwd = ForwardXZ;
            Vector2 right = RightXZ;

            float vF = Vector2.Dot(_vel, fwd);
            float vR = Vector2.Dot(_vel, right);

            // ---- WHAT IS UNDER THE WHEELS, sampled BEFORE the longitudinal so the top speed can use it -
            //
            // Through the CPU mirror only, which is the real project's rule for gameplay code.
            _snowDepthUnder = 0f;
            _snowDepthAhead = 0f;
            _snowDragLast = 0f;

            bool haveField = field != null && field.MirrorValid;
            if (haveField)
            {
                Vector2 aheadXZ = _pos + fwd * Mathf.Max(0f, s.SnowSampleAheadM);
                _snowDepthUnder = field.HeightAt(new Vector3(_pos.x, 0f, _pos.y));
                _snowDepthAhead = field.HeightAt(new Vector3(aheadXZ.x, 0f, aheadXZ.y));
            }

            // ---- THE DEPTH-DRIVEN TOP SPEED, AND IT IS WHY A CLEARED LANE IS A ROAD -------------------
            //
            // Linear in the depth UNDER THE VEHICLE, from 1 on scraped ground to the floor fraction at the
            // saturation depth. Not the depth AHEAD, and not the max of the two: this is what the wheels
            // are in right now, and the whole point is that driving onto ground the blade already cleared
            // releases immediately.
            //
            // MULTIPLICATIVE WITH THE CARRIED-MASS FACTOR AND KEPT SEPARATE FROM IT. f(M) answers "how
            // heavy is my load" and this answers "how deep is what I am driving through"; they are
            // independent, so they multiply, and they are reported separately so a slow vehicle can be
            // diagnosed rather than guessed at. At the shipped knobs an empty blade does 14 m/s on a scrape
            // and 4.9 in virgin 30 cm; a full blade does 4.9 on a scrape and 1.7 in virgin snow.
            float satDepth = Mathf.Max(0.01f, s.DepthSpeedSatDepthM);
            float floor01 = Mathf.Clamp(s.DepthSpeedFloor, 0.02f, 1f);
            _fDepth = Mathf.Lerp(1f, floor01, Mathf.Clamp01(_snowDepthUnder / satDepth));

            // ---- longitudinal -----------------------------------------------------------------------
            float topFwd = Mathf.Max(0.1f, s.TopSpeedMps * _fSpeed * _fDepth *
                                     (input.Boost ? Mathf.Max(1f, s.BoostMultiplier) : 1f));
            float topRev = Mathf.Max(0.1f, s.ReverseTopSpeedMps * _fSpeed * _fDepth);

            float throttle = Mathf.Clamp(input.Throttle, -1f, 1f);

            if (throttle > 0.001f)
            {
                vF += s.AccelMps2 * _fAccel * throttle * dt;
            }
            else if (throttle < -0.001f)
            {
                // Brake while still rolling forward, reverse once essentially stopped. The 0.15 m/s
                // threshold is what stops it oscillating between the two at a crawl.
                if (vF > 0.15f) vF -= s.BrakeMps2 * _fBrake * (-throttle) * dt;
                else vF -= s.ReverseAccelMps2 * _fAccel * (-throttle) * dt;
            }
            else
            {
                // MOMENTUM. A heavy pile coasts much further, which is most of what makes the mass read as
                // mass rather than as a speed cap.
                vF = Mathf.MoveTowards(vF, 0f, s.CoastDecelMps2 * _fCoast * dt);
            }

            vF = Mathf.Clamp(vF, -topRev, topFwd);

            // ---- the snow RESISTS, and ONLY WITH THE BLADE DOWN --------------------------------------
            //
            // The load makes the vehicle slow everywhere. THIS makes it slow HERE: driving into 30 cm of
            // virgin slab bites, and crossing onto a lane the blade has already cleared releases.
            //
            // GATED ON THE BLADE, which is the third of the three things "up" has to stop doing. It is
            // sampled at the BLADE's own position (Snow Sample Ahead M is the blade offset), so this term
            // IS the plough resistance and a raised blade cannot be paying it. What survives with the blade
            // up is the depth term above - so transit through virgin snow is capped but not fought, and
            // transit over a cleared lane is neither.
            //
            // DIVIDED BY THE MASS. Snow drag is a FORCE and deceleration is F/m, so a heavy pile has to
            // plough through the same snow with far more inertia behind it. Left undivided, a two-tonne
            // load would be stopped by fresh snow faster than an empty blade.
            if (haveField && _bladeDown)
            {
                // The deeper of the two, so entering deep snow bites BEFORE the vehicle is in it and
                // leaving releases only once it has actually left.
                float depth = Mathf.Max(_snowDepthUnder, _snowDepthAhead);
                float bite = Mathf.Clamp01(depth / Mathf.Max(0.01f, s.SnowBiteDepthM));

                _snowDragLast = (s.SnowDragBaseMps2 + s.SnowDragPerSpeed * Mathf.Abs(vF))
                              * bite * _fCoast;
                vF = Mathf.MoveTowards(vF, 0f, _snowDragLast * dt);
            }

            // ---- BREAKING THE PILE LOOSE ------------------------------------------------------------
            //
            // The leading face collapses toward repose when the shove stops, and re-engaging has to push it
            // back up. That re-steepening is work: it is the moment of breaking a settled pile loose, and it
            // costs a brief extra deceleration proportional to how hard the face is climbing and to how
            // full the blade is - both of which the field measured last step.
            //
            // BRIEF BY CONSTRUCTION rather than by a timer: the face only climbs while it is behind the
            // shove angle, so the cost lasts exactly as long as the re-steepen does (0.4 s at the shipped
            // 10 degree span and 25 deg/s) and is exactly zero in steady ploughing.
            _breakLooseMps2 = 0f;
            if (_bladeDown && s.FaceSteepen01 > 1e-4f && s.FaceBreakLooseMps2 > 0f)
            {
                _breakLooseMps2 = s.FaceBreakLooseMps2 * Mathf.Clamp01(s.FaceSteepen01) * _fCoast;
                vF = Mathf.MoveTowards(vF, 0f, _breakLooseMps2 * dt);
            }

            // ---- THE ANGLED BLADE'S LATERAL SHOVE ---------------------------------------------------
            //
            // The reaction to casting snow toward the discharge side, applied as a lateral acceleration
            // AWAY from it - so angled left shoves the vehicle right. It enters the LATERAL velocity, which
            // means the grip damping below decides how much of it survives: vR settles at a/grip, so the
            // default 0.6 gives 0.32 m/s while a blade at its straight equilibrium drains (about 76 cm of
            // drift over 2.3 s, in a 2.3 m lane) and 9.5 cm/s in the steady angled state. Small in absolute
            // terms and completely legible, because it is exactly correlated with a verb the player pressed.
            _castPushMps2 = 0f;
            if (_bladeAngleState != 0 && _bladeAttached)
            {
                _castPushMps2 = -_bladeAngleState * s.CastPushMps2PerM3s
                              * Mathf.Max(0f, s.CastRateM3PerSec);
                vR += _castPushMps2 * dt;
            }

            // ---- lateral grip: DAMPED, never cancelled ----------------------------------------------
            float gripT = Mathf.Clamp01(Mathf.Abs(vF) / Mathf.Max(0.1f, s.SteerFadeSpeedMps));
            float grip = Mathf.Lerp(s.LateralGrip, s.LateralGrip * s.GripAtTopSpeed, gripT)
                       * _fGrip
                       * (_driftActive ? Mathf.Max(0.01f, s.DriftGripMultiplier) : 1f);

            float vRBefore = vR;

            // exp() rather than (1 - grip*dt): the linear form goes NEGATIVE above grip*dt = 1, which at a
            // high grip and a hitched frame flips the slide to the other side of the vehicle.
            vR *= Mathf.Exp(-Mathf.Max(0f, grip) * dt);

            _vel = fwd * vF + right * vR;

            // ---- integrate, then slide along the stage boundary --------------------------------------
            Vector2 next = _pos + _vel * dt;
            Vector2 clamped = (field != null)
                ? field.ClampToPatch(next, Mathf.Max(0f, s.BoundsMarginM))
                : next;

            if ((clamped - next).sqrMagnitude > 1e-10f)
            {
                // Same decomposition, against the wall instead of against the vehicle: the component into
                // the wall is removed and the component along it is kept, scaled by WallSlideKeep.
                Vector2 n = (clamped - next).normalized;
                Vector2 along = _vel - n * Vector2.Dot(_vel, n);
                _vel = along * Mathf.Clamp01(s.WallSlideKeep);
                next = clamped;
            }

            _pos = next;

            // ---- derived readouts --------------------------------------------------------------------
            float vFNow = Vector2.Dot(_vel, ForwardXZ);
            float vRNow = Vector2.Dot(_vel, RightXZ);

            _longAccel = (vFNow - _prevForwardSpeed) / dt;
            _prevForwardSpeed = vFNow;

            // Centripetal plus the lateral velocity the grip just removed: the first is the load in a
            // steady turn, the second is the snap when the vehicle catches.
            _latAccel = (_yawRateDegPerSec * Mathf.Deg2Rad) * vFNow + (vR - vRBefore) / dt;

            _slipAngleDeg = (_vel.sqrMagnitude > 1e-4f)
                ? Mathf.Atan2(vRNow, Mathf.Abs(vFNow)) * Mathf.Rad2Deg
                : 0f;

            // ---- body reaction, every term neutral at 0 -----------------------------------------------
            float pitchTarget = -(_longAccel / 9.81f) * s.PitchAccelDeg + load01 * s.PitchLoadDeg;
            float rollTarget = (_latAccel / 9.81f) * s.RollLateralDeg;

            float k = 1f - Mathf.Exp(-Mathf.Max(0.01f, s.BodyResponsePerSec) * dt);
            _bodyPitchDeg = Mathf.Lerp(_bodyPitchDeg, Mathf.Clamp(pitchTarget, -25f, 25f), k);
            _bodyRollDeg = Mathf.Lerp(_bodyRollDeg, Mathf.Clamp(rollTarget, -25f, 25f), k);

            // Riding over the snow rather than through it, by a settable fraction of the depth.
            float rideLift = _snowDepthUnder * Mathf.Clamp01(s.SnowRideFactor);
            _rideY = Mathf.Lerp(_rideY, s.RideHeightM + rideLift, k);

            _bladeEndXZ = _pos + ForwardXZ * bladeOffsetM;
            _bladeEndFwd = ForwardXZ;
        }
    }

    /// <summary>
    /// A fixed measurement course, because the editor is driven over the CLI without OS focus and the
    /// keyboard never reaches the app.
    ///
    /// IT HAS TO DEMONSTRATE EVERY VERB, or the verbs cannot be evaluated from a capture at all - which is
    /// why five of its segments exist only for the grammar:
    ///  * `castLeft` / `castRight` plough a lane with the blade ANGLED, long enough for the windrow to
    ///    grow on one side and for the lateral pull to be visible in the yaw trace;
    ///  * `backOut` REVERSES with the blade down, which is the deposit: the pile is left standing and the
    ///    vehicle drives out from under it;
    ///  * `transit` then raises the blade and drives FORWARD along the lane it just reversed down - the
    ///    direct A/B for the depth-driven top speed, on ground the blade cleared minutes earlier;
    ///  * `wallStall` deliberately steers INTO the patch boundary with a full blade, with the bounds guard
    ///    suppressed, which reproduces the wedged-at-the-wall stall exactly;
    ///  * `wallEscape` raises the blade and reverses out of it, carrying nothing and leaving nothing.
    ///
    /// THE DUMP SEGMENT IS GONE, with the dump verb. `backOut` replaces it and is strictly more
    /// informative: it puts the pile down in the shape the pile had, and it exercises the receipt retire
    /// rather than a second emitter.
    ///
    /// Manual input overrides it while held, and the moment the key is released the course resumes from
    /// wherever it had got to.
    /// </summary>
    public sealed class SnowPileCourseV7
    {
        /// <summary>What the bounds guard is allowed to do during a segment.</summary>
        public enum BoundsMode
        {
            /// <summary>Turn away from the edge and refuse to brake into it. The default.</summary>
            Guard = 0,

            /// <summary>Leave the steering alone. For a segment that is deliberately at the wall.</summary>
            Free = 1,

            /// <summary>Steer TOWARD the nearest edge, so the stall is reproducible rather than lucky.</summary>
            Seek = 2,
        }

        public struct Segment
        {
            public string Name;
            public float Seconds;
            public float Throttle;
            public float Steer;
            public bool Drift;

            /// <summary>VERB 1. False is the default and means the blade is DOWN, ploughing.</summary>
            public bool BladeUp;

            /// <summary>VERB 2. The discharge side: -1 LEFT, 0 STRAIGHT, +1 RIGHT. 0 is the default.</summary>
            public int Angle;

            /// <summary>What the bounds guard may do. Guard is the default.</summary>
            public BoundsMode Bounds;
        }

        /// <summary>
        /// THE COURSE. Durations are at the shipped top speed; Course Time Scale on the bootstrap scales
        /// all of them together. 77 s at scale 1, so a two-minute capture sees a whole lap and the start of
        /// the next.
        ///
        ///  launch     standing start with an empty blade - the accel curve at its lightest
        ///  gather     straight through virgin slab: THE ACCUMULATION, heap empty -> capacity
        ///  overflow   MORE straight, well past capacity: THE WINDROW. Release as a rate, both sides.
        ///  castLeft   ANGLED LEFT down a virgin lane: the heap fills far slower, ONE windrow grows on the
        ///             left, and the vehicle is pulled RIGHT. The headline shot for verb 2.
        ///  castRight  the mirror, which is what catches a sign error in the cast, the cone placement and
        ///             the rotated cut all at once
        ///  hardLeft   full lock at speed under a load: turn radius, turn-in, the swept ARC
        ///  ease       a beat of straight, so the recovery is separable from the turn
        ///  hardRight  the mirror, which is what catches a sign error in the swept box union
        ///  circle     sustained full lock: the heap is shoved through its own windrow, which is the
        ///             hardest case for the swept union AND where free re-pickup is visible
        ///  coast      THROTTLE OFF at speed. The only segment that measures MOMENTUM - and the first
        ///             place the LEADING FACE collapses toward repose, because the shove has stopped.
        ///  brake      full brake: the load's effect on stopping, and the face settles further
        ///  backOut    REVERSE with the blade DOWN. THE DEPOSIT: the pile is left standing where it is,
        ///             the carried volume goes to zero, and the vehicle drives out from under it.
        ///  transit    BLADE UP, forward, back along the lane it just reversed down. The depth-driven top
        ///             speed with nothing to plough and nothing to carry: the fastest the vehicle ever is.
        ///  regain     blade back DOWN on cleared ground, then into virgin snow: re-engagement, which is
        ///             where the face re-steepens and the break-loose resistance shows
        ///  haul       more straight-line ploughing, so the stall arrives with a full blade
        ///  wallStall  STEER INTO THE BOUNDARY with the guard off. This is the measured failure state:
        ///             a full blade wedged against the wall at 0.01 m/s and 100% fill.
        ///  wallEscape BLADE UP and REVERSE, guard still off. Carries nothing, leaves nothing, and gets
        ///             out - which is the whole argument for the up verb being a verb.
        ///  crossWall  drive across your own windrow: free re-pickup, and _MaxPileStep under load
        ///  drift      0.85 lock with the drift key HELD
        ///  settle     coast to a stop, so the last frames of a capture are quiet and readable
        /// </summary>
        private static readonly Segment[] kCourse =
        {
            new Segment { Name = "launch",     Seconds =  2.5f, Throttle =  1f, Steer =  0f },
            new Segment { Name = "gather",     Seconds =  4.0f, Throttle =  1f, Steer =  0f },
            new Segment { Name = "overflow",   Seconds =  8.0f, Throttle =  1f, Steer =  0f },
            new Segment { Name = "castLeft",   Seconds =  6.0f, Throttle =  1f, Steer =  0f, Angle = -1 },
            new Segment { Name = "castRight",  Seconds =  4.0f, Throttle =  1f, Steer =  0f, Angle =  1 },
            new Segment { Name = "hardLeft",   Seconds =  2.6f, Throttle =  1f, Steer = -1f },
            new Segment { Name = "ease",       Seconds =  1.4f, Throttle =  1f, Steer =  0f },
            new Segment { Name = "hardRight",  Seconds =  2.6f, Throttle =  1f, Steer =  1f },
            new Segment { Name = "circle",     Seconds =  6.0f, Throttle =  1f, Steer =  1f },
            new Segment { Name = "coast",      Seconds =  3.0f, Throttle =  0f, Steer =  0f },
            new Segment { Name = "brake",      Seconds =  1.6f, Throttle = -1f, Steer =  0f },
            new Segment { Name = "backOut",    Seconds =  3.0f, Throttle = -1f, Steer =  0f },
            new Segment { Name = "transit",    Seconds =  5.0f, Throttle =  1f, Steer =  0f, BladeUp = true },
            new Segment { Name = "regain",     Seconds =  2.5f, Throttle =  1f, Steer =  0f },
            new Segment { Name = "haul",       Seconds =  5.0f, Throttle =  1f, Steer =  0f },
            new Segment { Name = "wallStall",  Seconds =  6.0f, Throttle =  1f, Steer =  0f,
                          Bounds = BoundsMode.Seek },
            new Segment { Name = "wallEscape", Seconds =  3.0f, Throttle = -1f, Steer =  0f,
                          BladeUp = true, Bounds = BoundsMode.Free },
            new Segment { Name = "crossWall",  Seconds =  5.0f, Throttle =  1f, Steer = -0.35f },
            new Segment { Name = "drift",      Seconds =  4.0f, Throttle =  1f, Steer = -0.85f, Drift = true },
            new Segment { Name = "settle",     Seconds =  2.0f, Throttle =  0f, Steer =  0f },
        };

        private int _index;
        private float _elapsed;
        private int _lap = 1;
        private bool _boundsOverride;

        public int Lap => _lap;
        public int SegmentIndex => _index;
        public int SegmentCount => kCourse.Length;
        public bool BoundsOverride => _boundsOverride;

        /// <summary>Verb 1 as the course wants it this frame. The bootstrap may override it.</summary>
        public bool SegmentBladeUp => kCourse[_index].BladeUp;

        /// <summary>Verb 2 as the course wants it this frame. The bootstrap may override it.</summary>
        public int SegmentAngle => kCourse[_index].Angle;

        public string SegmentName =>
            _boundsOverride ? (kCourse[_index].Name + "+bounds") : kCourse[_index].Name;

        public float SegmentRemaining => Mathf.Max(0f, kCourse[_index].Seconds - _elapsed);

        public static float TotalSeconds
        {
            get
            {
                float t = 0f;
                for (int i = 0; i < kCourse.Length; ++i) t += kCourse[i].Seconds;
                return t;
            }
        }

        public void Restart()
        {
            _index = 0;
            _elapsed = 0f;
            _boundsOverride = false;
        }

        /// <summary>Advances the course and returns this frame's input.</summary>
        /// <param name="lapped">True on the frame the course wraps back to segment 0.</param>
        public SnowCarInputV7 Step(float dt, float timeScale, SnowPileCarV7 car,
                                   SnowPileFieldV7 field, float boundsMarginM,
                                   out bool lapped)
        {
            lapped = false;

            float scale = Mathf.Max(0.05f, timeScale);
            _elapsed += dt / scale;

            if (_elapsed >= kCourse[_index].Seconds)
            {
                _elapsed = 0f;
                _index++;
                if (_index >= kCourse.Length)
                {
                    _index = 0;
                    _lap++;
                    lapped = true;
                }
            }

            Segment seg = kCourse[_index];

            var input = new SnowCarInputV7
            {
                Throttle = seg.Throttle,
                Steer = seg.Steer,
                Drift = seg.Drift,
                Boost = false,

                // THE VERBS ARE STATE, NOT EDGES, and that is the whole difference from the dump they
                // replace. A held blade position and a held blade angle can be read off a still frame; an
                // edge cannot, and an edge fired once per segment entry is exactly the kind of thing that
                // silently stops firing when the segment list is reordered.
                BladeUp = seg.BladeUp,
                BladeAngle = seg.Angle,
            };

            _boundsOverride = false;

            // ---- keep it on the stage, or deliberately drive off it -------------------------------
            //
            // A ~77 s course at up to 14 m/s covers several hundred metres, which does not fit inside
            // 120 x 110 m however the segments are arranged. Rather than shrink the course until it happens
            // to fit - which would make the segments describe the field rather than the handling - the
            // guard turns the vehicle back toward the middle near an edge, and SAYS SO in the segment name
            // so a capture is never silently something else.
            //
            // TWO SEGMENTS OPT OUT, and they have to. `wallStall` SEEKS the nearest edge, because the
            // failure it reproduces - a full blade wedged against the boundary at 0.01 m/s - is not
            // something that can be waited for; and `wallEscape` runs FREE, because the guard's own
            // "never brake into a wall" clause would force the throttle positive and fight the reverse that
            // is the escape being demonstrated.
            if (field != null && seg.Bounds != BoundsMode.Free)
            {
                Vector2 p = car.PositionXZ;

                if (seg.Bounds == BoundsMode.Seek)
                {
                    // Aim at the NEAREST edge, by pushing the position out past it and steering at that
                    // point. Same arithmetic as the guard with the target on the other side, so there is one
                    // steering law here and not two.
                    Vector2 c = field.PatchCenter;
                    Vector2 half = new Vector2(field.PatchSizeX * 0.5f, field.PatchSizeZ * 0.5f);
                    Vector2 d = p - c;

                    // Whichever axis is proportionally closer to its own wall is the one to run at.
                    Vector2 target = (Mathf.Abs(d.x) / Mathf.Max(1e-3f, half.x)
                                      > Mathf.Abs(d.y) / Mathf.Max(1e-3f, half.y))
                        ? new Vector2(c.x + Mathf.Sign(d.x == 0f ? 1f : d.x) * half.x * 1.5f, p.y)
                        : new Vector2(p.x, c.y + Mathf.Sign(d.y == 0f ? 1f : d.y) * half.y * 1.5f);

                    Vector2 toWall = target - p;
                    if (toWall.sqrMagnitude > 1e-6f)
                    {
                        float wantDeg = Mathf.Atan2(toWall.x, toWall.y) * Mathf.Rad2Deg;
                        float delta = Mathf.DeltaAngle(car.HeadingDeg, wantDeg);
                        input.Steer = Mathf.Clamp(delta / 45f, -1f, 1f);
                    }
                }
                else
                {
                    Vector2 inset = field.ClampToPatch(p, Mathf.Max(1f, boundsMarginM));

                    if ((inset - p).sqrMagnitude > 1e-6f)
                    {
                        _boundsOverride = true;

                        Vector2 toCentre = field.PatchCenter - p;
                        if (toCentre.sqrMagnitude > 1e-6f)
                        {
                            float wantDeg = Mathf.Atan2(toCentre.x, toCentre.y) * Mathf.Rad2Deg;
                            float delta = Mathf.DeltaAngle(car.HeadingDeg, wantDeg);
                            input.Steer = Mathf.Clamp(delta / 45f, -1f, 1f);
                        }

                        // Never brake into a wall while trying to turn away from it: a stopped vehicle
                        // cannot yaw, so the guard would deadlock exactly where it is needed.
                        input.Throttle = Mathf.Max(input.Throttle, 0.5f);
                        input.Drift = false;
                    }
                }
            }

            return input;
        }
    }

    /// <summary>
    /// Chase camera: a follow spring, look-ahead, and a field of view that opens with speed - plus the two
    /// framing terms v6 introduced, kept because v7 needs them for the same reason and MORE.
    ///
    /// A GROWING OBJECT THAT NEVER CHANGES ITS PLACE IN FRAME DOES NOT READ AS GROWING. The camera takes an
    /// AIM POINT rather than deriving one from the vehicle, so the bootstrap can bias it from the vehicle
    /// toward the heap, and its distance is a parameter the bootstrap grows with the heap's HEIGHT so the
    /// pile holds roughly the same apparent size.
    ///
    /// WHAT IS DIFFERENT FROM V6 IS WHAT THE DISTANCE TRACKS. v6 pulled back per metre of BALL RADIUS -
    /// an unbounded quantity. v7's heap is bounded by its capacity, so the same knob applied to the heap's
    /// height moves the camera over a much smaller range; more importantly, a v7 heap 1.6 m tall and 6 m
    /// wide sitting 0.65 m in front of a 1 m tall vehicle will OCCLUDE the road ahead from a low camera,
    /// which is a gameplay problem the ball never had. Both terms are neutral by default so this
    /// reproduces v6's framing exactly until someone turns them up, and the parent agent is the one who can
    /// see whether the occlusion needs a higher camera.
    /// </summary>
    public sealed class SnowPileChaseCameraV7
    {
        private Vector3 _pos;
        private Vector3 _lookAt;
        private float _fov;
        private bool _seeded;

        public Vector3 Position => _pos;
        public float Fov => _fov;

        public void Snap(Vector3 carPos, Vector3 carFwd, Vector3 aimBase,
                         float up, float back, float lookAhead, float fov)
        {
            _pos = carPos - carFwd * back + Vector3.up * up;
            _lookAt = aimBase + carFwd * lookAhead;
            _fov = fov;
            _seeded = true;
        }

        /// <summary>Moves the rig.</summary>
        /// <param name="aimBase">Where the camera looks BEFORE the look-ahead and the velocity lead: the
        /// vehicle's position biased toward the heap. This is the parameter that makes the pile the subject
        /// of the shot rather than a thing in front of the subject.</param>
        /// <param name="speed01">The vehicle's speed as a fraction of its top speed.</param>
        public void Step(Transform rig, Camera camera, float dt,
                         Vector3 carPos, Vector3 carFwd, Vector3 carVel, Vector3 aimBase,
                         float up, float back, float lookAhead, float lookUp,
                         float fovBase, float fovSpeedGain, float positionSmoothing,
                         float aimSmoothing, float velocityLeadSeconds, float speed01)
        {
            if (!_seeded)
            {
                Snap(carPos, carFwd, aimBase, up, back, lookAhead, fovBase);
            }

            // The rig trails the vehicle's PAST, so a hard turn swings the camera wide instead of snapping
            // to the new heading. Leading the look-at by the velocity rather than by the facing is what
            // keeps a drifting vehicle in frame.
            Vector3 lead = carVel * Mathf.Max(0f, velocityLeadSeconds);

            Vector3 wantPos = carPos - carFwd * back + Vector3.up * up;
            Vector3 wantLook = aimBase + carFwd * lookAhead + lead + Vector3.up * lookUp;

            float kp = (positionSmoothing <= 0f) ? 1f
                     : 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, positionSmoothing));
            float ka = (aimSmoothing <= 0f) ? 1f
                     : 1f - Mathf.Exp(-dt / Mathf.Max(1e-4f, aimSmoothing));

            _pos = Vector3.Lerp(_pos, wantPos, kp);
            _lookAt = Vector3.Lerp(_lookAt, wantLook, ka);

            rig.position = _pos;

            Vector3 toLook = _lookAt - _pos;
            if (toLook.sqrMagnitude > 1e-6f)
                rig.rotation = Quaternion.LookRotation(toLook.normalized, Vector3.up);

            // FOV opens with speed, smoothed on the aim constant because a field of view that tracks a
            // noisy speed reads as the image breathing.
            float wantFov = fovBase + fovSpeedGain * Mathf.Clamp01(speed01);
            _fov = Mathf.Lerp(_fov, wantFov, ka);

            if (camera != null) camera.fieldOfView = _fov;
        }
    }
}
