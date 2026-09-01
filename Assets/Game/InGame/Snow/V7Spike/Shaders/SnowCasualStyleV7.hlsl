#ifndef SNOW_CASUAL_STYLE_V7_INCLUDED
#define SNOW_CASUAL_STYLE_V7_INCLUDED

// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 -> V7 (and the
// _Cs6 shader-global prefix -> _Cs7). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.

// -----------------------------------------------------------------------------------------------
// v6's CASUAL / TOY shading, shared by all three render modes and by the puffs.
//
// This file is the whole "look" of v6. The simulation underneath is v3's plus the two inherited
// fixes; nothing in here is allowed to read or write simulation state, and nothing in here changes
// where a surface is - it changes only what colour that surface is, plus ONE bounded, opt-in
// displacement (SnowCasualRoundH / SnowCasualLoadLift) whose bound is published to the caller so the
// raymarcher's empty-space bound can be widened by exactly that much.
//
// FOUR IDEAS, IN ORDER OF HOW MUCH THEY DO
// ----------------------------------------
// 1. BANDED DIFFUSE with a COLOURED SHADOW. Two or three quantised light steps, and the dark end of
//    the ramp is a saturated blue-violet rather than black. This one choice does most of the work of
//    reading as "toy": a realistic renderer multiplies one albedo by a smooth light term, a stylised
//    one picks a small palette and steps between its entries.
// 2. NO WET LOOK. There is no Blinn lobe here at all. A high-frequency specular on a noisy normal is
//    exactly what made an earlier version of this spike read as wet plastic, so the sheen is gone and
//    what replaces it is a broad, dim rim term plus a few LARGE, sparse, slowly twinkling sparkles.
//    Both are silhouette / cell scale, not pixel scale, so neither can turn into a glitter field.
// 3. FLAT-ISH ALBEDO. The realistic path tints steep faces toward a dense blue body colour and
//    multiplies by curvature AO. Casual snow keeps a little of both - _Cs7AlbedoInfluence and
//    _Cs7AoInfluence - because zero reads as a sticker, but not the full amount, because the full
//    amount is what makes it read as photography.
// 4. FAT EDGES. SnowCasualRoundH soft-maxes the height field against its own dilated max, which
//    fills a convex shoulder with a rounded fillet and leaves flat ground untouched.
//
// EVERY KNOB RETURNS TO THE V3 LOOK
// ---------------------------------
// _Cs7Casual is the master: 0 returns the caller's own realistic colour bit for bit, because
// SnowCasualApply's last act is lerp(realistic, casual, _Cs7Casual) and every displacement term is
// multiplied by it as well. Each individual knob also has a neutral value (bands 1 + softness 1,
// rim 0, sparkle 0, round 0, exaggeration 1), so the parent agent can A/B one idea at a time instead
// of only all-or-nothing.
//
// METAL
// -----
// Every non-void function below has exactly ONE return. This is the project convention for the whole
// variant and it is followed throughout - but it is worth being precise about why, because the
// convention is BROADER than the failure it guards against, and every other v6 shader states the short
// form and points at this note.
//
// THE FAILURE: Unity's Metal back end rejects an early return from inside an if in a non-void HLSL
// function when it cannot prove every OUT parameter was written on that path. It reports "use of
// potentially uninitialized variable" and fails the WHOLE shader, not just the function. DXC accepts the
// same code silently, so an offline compile proves nothing about this class of mistake and the only real
// check is the editor's shadercompiler log.
//
// THE CONVENTION IS CONSERVATIVE, NOT THE CONSTRAINT. v3's SnowRaymarchV3.shader ships six multi-return
// non-void functions - SurfaceY, MarchSnow and SoftShadow with 2, FindHit with 3, FragForward and
// FragDepthOnly with 2 - and it compiles, runs, and is the shader that measures 4.95 ms. So a clean
// single-return audit is COMPLIANCE with this convention and cheap insurance, but it is not evidence that
// a shader is Metal-safe, and a violation would not be evidence that it is not. What it does buy is that
// the out-parameter cases - the sub-population where the real constraint lives, 12 of v6's 94 non-void
// functions - are single-exit by construction rather than by inspection.
// -----------------------------------------------------------------------------------------------

// ------------------------------------------------------------------ uniforms
// Set as GLOBALS by SnowCasualStyleV7.cs, once per frame, rather than per material. There are four
// consumers (raymarch, instanced grains, screen-space composite, puffs) and they must agree exactly
// or a mode switch would look like a lighting change; four copies of the same twenty setters is the
// way that drift happens.

float  _Cs7Casual;          // MASTER. 0 = the v3 look bit for bit, 1 = full toy treatment.

// -- banded diffuse ------------------------------------------------------------------------------
float  _Cs7Bands;           // quantised light steps. 1 + softness 1 is the smooth A/B.
float  _Cs7BandSoftness;    // width of each step's transition, in band units. 1 = no plateau.
float  _Cs7Wrap;            // diffuse wrap before quantisation, so the terminator is not a knife.

// -- the palette. Lit near white with a slight warm tint, shadow a SATURATED BLUE-VIOLET, never
//    black; mid is the step between them and is what makes three bands read as three and not two.
float4 _Cs7LitColor;
float4 _Cs7MidColor;
float4 _Cs7ShadowColor;

float  _Cs7AlbedoInfluence; // how much of the caller's albedo survives. 0 = flat palette only.
float  _Cs7AoInfluence;     // how much of the caller's AO survives.
float  _Cs7Exposure;        // overall gain on the casual colour, so the palette can be authored
                            // in the 0..1 range it is easiest to reason about.

// -- rim and sparkle (what replaces the specular) -------------------------------------------------
float  _Cs7RimStrength;     // 0 disables. Broad and dim on purpose.
float  _Cs7RimPower;        // LOW = broad. High would be a specular by another name.
float4 _Cs7RimColor;

float  _Cs7SparkleAmount;   // 0 disables.
float  _Cs7SparkleScaleM;   // metres per sparkle cell. LARGE, so sparkles are sparse and readable.
float  _Cs7SparkleRadius;   // sparkle size in cell units, so it scales with the cell.
float  _Cs7SparkleThresh;   // 0..1 fraction of cells that never sparkle at all.
float  _Cs7SparkleSpeed;    // twinkles per second. Slow.
float4 _Cs7SparkleColor;
float  _Cs7Time;            // seconds, pushed from C# rather than read from _Time, because half of
                            // this chain is drawn from a hand-submitted CommandBuffer.

// -- shape exaggeration. Presentation only; the simulation never sees either of these. ------------
float  _Cs7RoundM;          // fillet amplitude in metres for the raymarch path. 0 disables.
float  _Cs7RoundK;          // headroom in metres at which the fillet is HALF applied. Not a blend width.

// Half-width in metres of the finite difference the BAND term's normal is taken over. 0 means "use the
// caller's shading normal", which is v6's original behaviour and the cause of contour terracing: see
// SnowCasualApply.
float  _Cs7BandNormalWideM;
float  _Cs7LoadExaggeration;// NEUTRAL DEFAULT 1.0. >1 makes piled snow look deeper than it is.
float  _Cs7LoadLiftMaxM;    // hard bound on that lift, so the raymarcher's coarse bound stays valid.
float  _Cs7VirginDepthM;    // the depth exaggeration measures "piled" against.

float  _Cs7LumpSquash;      // per-lump vertical squash for the instanced path. 0 = true spheres.

// ------------------------------------------------------------------ helpers

uint SnowCslHashU(uint x)
{
    x ^= x >> 16;
    x *= 0x7feb352du;
    x ^= x >> 15;
    x *= 0x846ca68bu;
    x ^= x >> 16;
    return x;
}

float SnowCslHash01(uint x)
{
    return (float)(SnowCslHashU(x) & 0x00ffffffu) * (1.0 / 16777216.0);
}

// Integer lattice hash on a 2D cell. Same construction as the raymarcher's height noise, so the
// sparkle lattice and the surface wear the same grain and a capture compares the look, not the noise.
float SnowCslCellHash(float2 cell)
{
    uint2 u = (uint2)(int2(cell) + 4096);
    return (float)(SnowCslHashU(u.x * 1597334677u ^ u.y * 3812015801u) & 0x00ffffffu)
         * (1.0 / 16777216.0);
}

// NOTE there is deliberately no smooth-max helper here any more.
//
// The fillet used to be a polynomial soft-max of the height against its own dilated max. That was
// wrong in a way that only shows up on flat ground: the polynomial form adds k * hh * (1 - hh), and on
// a locally flat field the two arguments are equal, so hh is exactly 0.5 and it adds k/4
// UNCONDITIONALLY - a 2.5 cm lift over the entire snow field at the shipped k of 0.10, which is
// precisely the thing the fillet is documented not to do. It also meant the raw result could exceed
// the lift bound the raymarcher widens its empty-space bound by, so it needed a hard clamp on top,
// and "a bound plus a clamp to make the bound true" is a bound nobody can reason about.
//
// SnowCasualRoundH below uses a saturating form instead: exactly zero at zero headroom, exactly
// bounded by the amplitude, monotone in between, and no clamp required.

// Quantises a 0..1 light term into _Cs7Bands soft steps.
//
// The transition occupies the first `soft` of each band and the rest is a plateau, so softness 0 is
// a hard cel edge and softness 1 removes the plateau entirely. That is deliberately NOT centred on
// the band edge: a centred transition puts the plateau across the edge, which means the brightest
// and darkest bands are half width and a 3-band ramp reads as 4.
float SnowCslBand(float x, float bands, float soft)
{
    float b = max(1.0, bands);
    float s = saturate(x) * b;
    float f = floor(s);
    float e = smoothstep(0.0, max(0.001, saturate(soft)), s - f);
    return saturate((f + e) / b);
}

// The palette ramp. Three stops, so a 3-band light term lands one band on each.
float3 SnowCslTone(float t)
{
    float3 lo = _Cs7ShadowColor.rgb;
    float3 md = _Cs7MidColor.rgb;
    float3 hi = _Cs7LitColor.rgb;
    return (t < 0.5) ? lerp(lo, md, saturate(t * 2.0))
                     : lerp(md, hi, saturate((t - 0.5) * 2.0));
}

// A few LARGE, sparse, slowly twinkling sparkles. Deliberately not a specular:
//   * the cell is ~20 cm, so a sparkle is a readable blob rather than a sub-pixel glint;
//   * _Cs7SparkleThresh silences most cells outright, so the surface is not uniformly glittery;
//   * the twinkle is a slow per-cell sine, so it reads as hand-animated rather than as aliasing;
//   * the only view dependence is a BROAD half-vector term, which decides where sparkles appear at
//     all but never sharpens them.
// The domain is sheared with height so a vertical cut wall gets sparkles too instead of vertical
// stripes, which is the same trick the realistic path uses for its detail domain.
float3 SnowCslSparkle(float3 p, float3 n, float3 L, float3 V)
{
    float2 q  = float2(p.x + p.y * 0.6, p.z - p.y * 0.6) / max(0.02, _Cs7SparkleScaleM);
    float2 ci = floor(q);
    float2 cf = q - ci;

    float h0 = SnowCslCellHash(ci);
    float h1 = SnowCslCellHash(ci + float2(37.0, 91.0));
    float h2 = SnowCslCellHash(ci + float2(-53.0, 17.0));

    // One sparkle per cell, at a hashed position well inside it so it cannot straddle a cell seam.
    float2 centre = float2(0.25 + 0.5 * h0, 0.25 + 0.5 * h1);
    float  d      = length(cf - centre);
    float  blob   = saturate(1.0 - d / max(0.02, _Cs7SparkleRadius));

    float twinkle = 0.5 + 0.5 * sin((_Cs7Time * max(0.0, _Cs7SparkleSpeed) + h2) * 6.2831853);
    float alive   = step(saturate(_Cs7SparkleThresh), h2);

    float facing = saturate(dot(normalize(L + V), n));

    return _Cs7SparkleColor.rgb
         * (max(0.0, _Cs7SparkleAmount) * blob * blob * alive * twinkle * facing * facing);
}

// ------------------------------------------------------------------ the shading entry point

// Turns the caller's realistic ingredients into the toy look, and blends back to the caller's own
// realistic colour by _Cs7Casual so a strict A/B is one uniform away.
//
//   realistic  the colour the caller already computed the v3 way. Returned untouched at _Cs7Casual 0.
//   albedo     the caller's albedo, INCLUDING whatever wall tint it applies.
//   n, L, V    unit shading normal, unit direction to the light, unit direction to the eye.
//   nBand      unit MACRO normal: the one the quantised band term is driven from. See below.
//   shadow     0..1 the caller's own shadow term.
//   ao         0..1 the caller's own occlusion term.
//   posWS      world position, for the sparkle lattice.
//   lightCol   the main light's colour, so a coloured sun still tints the sparkles.
//   ambient    the caller's ambient term, used only to keep the shadow end from going flat.
//
// WHY THE BAND TERM NEEDS ITS OWN NORMAL - this is the fix for CONTOUR TERRACING.
//
// Quantising a light term computed from the SHADING normal puts a band edge around every bump that
// normal carries. The shading normal here carries the procedural detail, which is a ~38 cm wavelength
// at a few centimetres of amplitude, so a smooth snow field came out covered in concentric closed
// contours like a topographic map - the bands were following the noise instead of the form.
//
// A cel ramp has to be quantised against the MACRO shape and nothing finer. nBand is that shape: the
// raymarcher passes a wide-epsilon normal of the smooth field with no detail and no fillet in it, the
// screen-space mode passes its bilaterally smoothed normal (which is already exactly this), and the
// instanced mode passes the height-field normal rather than the lump's own. The detailed normal is
// still used for the rim and the sparkles, where high frequency is wanted.
//
// Passing n as nBand reproduces the terraced behaviour exactly, which is the A/B.
float3 SnowCasualApply(float3 realistic, float3 albedo, float3 n, float3 nBand, float3 L, float3 V,
                       float shadow, float ao, float3 posWS, float3 lightCol, float3 ambient)
{
    // Wrap first, quantise second. Quantising a raw N.L puts a band edge right on the terminator, where
    // the geometry is most nearly tangent to the light and the edge therefore wobbles most.
    float wrap = saturate((dot(nBand, L) + _Cs7Wrap) / (1.0 + _Cs7Wrap));
    float lit  = SnowCslBand(saturate(wrap * shadow), _Cs7Bands, _Cs7BandSoftness);

    float3 tone = SnowCslTone(lit);

    // Flat-ish: a little of the caller's albedo and a little of its AO, not all of either.
    float3 col = tone * lerp(1.0, albedo, saturate(_Cs7AlbedoInfluence));
    col *= lerp(1.0, saturate(ao), saturate(_Cs7AoInfluence));

    // The shadow end keeps a share of the real ambient so an unlit facet still belongs to the scene
    // rather than being a flat swatch of violet.
    col += ambient * _Cs7ShadowColor.rgb * (1.0 - lit) * 0.25;

    // Broad, dim rim. LOW power on purpose: this is a silhouette read, not a highlight.
    float rim = pow(saturate(1.0 - saturate(dot(n, V))), max(0.5, _Cs7RimPower));
    col += _Cs7RimColor.rgb * (max(0.0, _Cs7RimStrength) * rim);

    col += SnowCslSparkle(posWS, n, L, V) * lightCol;

    col *= max(0.0, _Cs7Exposure);

    return lerp(realistic, col, saturate(_Cs7Casual));
}

// ------------------------------------------------------------------ shape, presentation only

// FAT EDGES for the raymarch path.
//
// h is the field depth at xz; coarseH is a point sample of the DILATED coarse max, i.e. an upper bound
// on the field depth anywhere within the coarse safe radius. `head = coarseH - h` is therefore a
// measure of how much taller the field gets somewhere nearby: exactly zero on locally flat ground, and
// large next to a wall. The fillet is a saturating function of that headroom, so it fills a convex
// shoulder and leaves flat ground completely alone. That is what makes a wall top read fat rather than
// crisp.
//
// THREE PROPERTIES, ALL EXACT, because the raymarcher's empty-space skip depends on them:
//   lift(head = 0) == 0        flat ground is not moved at all
//   lift            <  amp     the returned bound is a true bound, with no clamp needed
//   d lift / d head >= 0       monotone, so the fillet cannot fold the surface back on itself
//
// The previous polynomial soft-max satisfied neither of the first two: it added k * hh * (1 - hh), and
// on locally flat ground the two arguments are equal so hh is exactly 0.5 and it added k/4
// UNCONDITIONALLY - a 2.5 cm lift over the whole snow field at the shipped k of 0.10, which is
// precisely what the fillet is documented not to do - and its raw result could exceed the bound it
// published, so it needed a hard clamp on top. A bound that requires a clamp to be true is not a bound.
//
// _Cs7RoundK is now the HEADROOM AT WHICH THE FILLET IS HALF APPLIED, in metres, NOT a blend width.
// Small k means even a shallow step gets most of the fillet; large k means only a pronounced wall does.
//
// thin fades the whole thing out as the snow thins, which is load bearing rather than cosmetic: without
// it, bare ground inside the swept lane would be lifted and the lane would refill visually while the
// simulation still says it is clear.
float SnowCasualRoundH(float h, float coarseH, float thin, out float liftBoundM)
{
    float amp = max(0.0, _Cs7RoundM) * saturate(thin) * saturate(_Cs7Casual);
    liftBoundM = amp;

    float head = max(0.0, coarseH - h);
    float k    = max(1e-4, _Cs7RoundK);

    return h + amp * (head / (head + k));
}

// "The load may look slightly larger than the field says." NEUTRAL AT 1.0, so the parent agent can
// see exactly what it bought. Only material above the virgin slab depth is affected - the swept lane
// and the untouched slab are never moved - and the lift is hard bounded so the march bound stays
// valid.
float SnowCasualLoadLift(float h)
{
    float k    = max(0.0, _Cs7LoadExaggeration - 1.0) * saturate(_Cs7Casual);
    float over = max(0.0, h - _Cs7VirginDepthM);
    return min(over * k, max(0.0, _Cs7LoadLiftMaxM));
}

#endif // SNOW_CASUAL_STYLE_V7_INCLUDED
