// ---------------------------------------------------------------------------
// AnyTest 스파이크 v7 에서 이식. 원본: Assets/SnowGrainFakeV7/Shaders/SnowMarchCoreV7.hlsl
//
// 옮긴 것은 **렌더러뿐**이다. v7 의 GPU 시뮬(SnowPileFieldV7.compute 의 Push/Settle/Deposit/
// Heap*/Relax)은 가져오지 않는다 — 권위는 CPU 의 SnowField 에 있고, 데디 서버에는 GPU 가 없다
// (루트 AGENTS.md). 이 파일이 읽는 높이 텍스처는 그 CPU 격자의 업로드 결과다.
//
// 버전 접미사(V7)와 _Cs7 전역 접두사는 이 저장소 규칙에 맞춰 제거했다(AGENTS.md 네이밍).
// 그 외의 수식·상수·주석은 원본 그대로다 — 검증된 코드를 다시 유도하지 않는다.
// ---------------------------------------------------------------------------
#ifndef SNOW_MARCH_CORE_V7_INCLUDED
#define SNOW_MARCH_CORE_V7_INCLUDED

// CARRIED OVER FROM v6 UNCHANGED IN SUBSTANCE. Only identifiers were renamed V6 ->  (and the
// _Cs6 shader-global prefix -> _Snow). Prose below that says "v6", "v5", "v4" or "v3" is the
// LINEAGE talking, not a claim about v7: v7 is v6 with the ball replaced by an accumulating pile
// written into the height field, and this file is one of the parts that did not have to change.

// -----------------------------------------------------------------------------------------------
// THE MARCH, AS ONE SHARED DEFINITION.
//
// This file exists because of a specific failure. v6's first steps-per-pixel instrument drew an extra
// pass of the raymarch shader into an offscreen target with a command buffer and reduced that target
// to a histogram. It compiled, it ran, it never produced a single measurement: the probe target came
// back empty every frame, so the console printed `probe=waiting` from t=0 to the end of the course and
// the one number that was supposed to close the ray-chord risk at 120 m was never produced.
//
// Rather than keep guessing at why a DrawRenderer into a manually bound target produced no fragments,
// the probe was moved to a COMPUTE kernel - the same machinery the twenty simulation kernels already
// use successfully in this variant - and the march was moved HERE so that the fragment shader and the
// probe cannot possibly be measuring different code. Not "a faithful port of the march": the same
// text, included twice.
//
// WHAT IS IN HERE: everything that decides where the snow surface is and how a ray finds it. Field
// sampling, the procedural detail, the casual fillet's input, the surface function, the slab test and
// MarchSnow itself.
//
// WHAT IS DELIBERATELY NOT: anything that needs a rendering context. SetupRay (view/projection
// matrices, the near plane), DepthFromWorld, the normal/AO/self-shadow taps and ShadeSnow all stay in
// SnowRaymarch.shader, because a compute shader has no UNITY_MATRIX_VP and no _MainLightPosition.
// The probe supplies its own ray origin and direction from camera vectors passed in from C#, which
// reproduces SetupRay's perspective branch exactly without needing a matrix inverse.
//
// CONSEQUENTLY THIS FILE MAY NOT USE URP. No Core.hlsl, no TEXTURE2D/SAMPLER macros, no `real`. Plain
// HLSL and Unity's inline sampler states only. SnowCasualStyle.hlsl is included because the surface
// function needs the fillet and the load lift; it is plain HLSL for the same reason.
//
// Every non-void function here has exactly ONE return: the project convention for this variant. See
// the METAL note in SnowCasualStyle.hlsl for the real constraint and why the convention is stricter.
// -----------------------------------------------------------------------------------------------

#include "SnowCasualStyle.hlsl"

// THE LUMP LATTICE'S FORMULA AND ITS 8-BIT ENCODING. Included here rather than reproduced, because
// the bake kernel in SnowPileField.compute includes the same file: the thing that writes the baked
// texture and the thing that decodes it therefore cannot disagree about what a texel means. This is
// where _LumpRadiusM (radius, decode scale, and the OFF switch, all one uniform) comes from, along
// with the lattice knobs the BAKE uses. The marcher itself only calls SnowLumpDecodeM; the lattice
// evaluation compiles away here because nothing in this file reaches it.
#include "SnowLumpLattice.hlsl"

// PLAIN HLSL, not the URP TEXTURE2D/SAMPLER macros, and Unity's INLINE sampler states rather than
// per-texture ones. Two reasons, both load bearing for this file's purpose:
//
//   * a .compute has no URP shader library, and this header has to compile identically into both the
//     fragment shader and the steps probe or the probe is measuring something else;
//   * sampler_point_clamp is an explicit guarantee. The coarse max and the dilation are hard BOUNDS,
//     and a bilinear tap interpolates a maximum downward - it would quietly destroy the guarantee the
//     empty-space skip rests on. Relying on the RenderTexture's own filterMode left that correctness
//     property one inspector click away from being wrong.
Texture2D _HeightTex;

// Dilated coarse max of the field: each texel holds the largest snow height anywhere within a wide
// neighbourhood, so a point sample of it is a hard upper bound on the surface over a known box
// around the sampling point. Point filtered - a bilinear tap would interpolate the bound DOWNWARD
// and destroy the guarantee it exists to provide.
//
// v6 NO LONGER uses this for the casual fillet. v4 could, because its coarse cell was 12.5 cm over
// an 8 m patch; here the coarse cell has to be metres so the skip can cross a 120 m box, and a
// fillet with a 2 m halo is a different look, not v4's look.
Texture2D _CoarseMaxTex;

// The height field max-filtered at TWO radii, in one texture so the march takes one tap:
//   R = the surface EXISTENCE test (radius _HeightDilateRadius, 1 texel = 12.5 cm)
//   G = the casual FILLET's source  (radius _FilletDilateRadius, 2 texels = 25 cm, v4's width)
// Point filtered for the same reason the coarse max is: a bilinear tap would interpolate the
// dilated maximum back downward and reintroduce the sub-threshold dip it exists to remove.
Texture2D _HeightDilateTex;

// THE BAKED LUMP LIFT, at exactly TWICE the field resolution (1920 x 1760 over the 120 x 110 m
// patch, so 6.25 cm per texel) and single-channel 8-bit: 3.4 MB. A 30 cm lump is 4.8 texels across,
// which with the fillet's rounding is enough for a lobe; baking at the field's own 12.5 cm would
// make it 2.4 texels and the lobes would read as blocks.
//
// BILINEAR, and unlike every other texture in this file that is not a hazard. The coarse max and the
// dilation are BOUNDS, and interpolating a maximum moves it downward; this texture is a VALUE, the
// surface height contribution itself, and it wants to be smooth. Its own bound is published to the
// coarse-max build separately - see CoarseMaxBlock in SnowPileField.compute, which maxes the baked
// texels over each coarse cell so the skip's upper bound is the ACTUAL lift rather than a blanket +r.
Texture2D _LumpBakeTex;

SamplerState sampler_linear_clamp;
SamplerState sampler_point_clamp;

// Plain uniforms rather than a UnityPerMaterial cbuffer. Exactly one renderer in the scene uses
// this shader, so SRP batching buys nothing, and keeping them out of a cbuffer removes a whole
// class of cbuffer-layout mismatch failure. Every count is a float and is cast to int at the loop
// head, so nothing depends on how int uniforms happen to pack.
float4 _BaseColor;
float4 _DeepColor;
float4 _AmbientColor;

float4 _BoxMin;          // .xyz world min corner of the proxy box
float4 _BoxMax;          // .xyz world max corner of the proxy box (mesh extent, not march)
float4 _PatchMin;        // .xy world (x,z) of the patch corner, matching the height texture
float4 _InvPatchSize;    // .xy = 1 / patch extent per axis. TWO components, because v6's stage
                         // is 120 x 110 m: one reciprocal would stretch the field along Z.
float  _GroundY;
float  _MarchTopY;       // world Y the march starts from: field crest + margin, <= _BoxMax.y
float  _MarchFloorY;     // world Y the march stops at, a hair above the ground plane
float  _MinSnowHeight;   // below this the field is bare ground and there is no surface at all
float  _CoarseSafeRadiusM; // box half-width, per axis, that _CoarseMaxTex is valid over
float  _CoarseMaxBiasM;    // headroom added to the coarse bound: detail noise PLUS v6's fillet
                           // and load lift, which is why those two publish hard bounds

float  _MaxSteps;
float  _StepM;
float  _RefineSteps;
float  _LodDistanceInv;  // 1 / _lodDistanceM; past that distance the step grows with range

float  _DetailAmpM;      // peak detail amplitude in metres
float  _DetailFreq;      // cycles per metre of the first octave
float  _DetailOctaves;
float  _DetailThinInv;   // 1 / the depth over which detail fades in as the snow thickens
float  _DetailFadeStartM;// ray distance at which detail starts fading out
float  _DetailFadeInv;   // 1 / the range over which it finishes fading
float  _ClumpStartM;     // snow depth at which the clump boost starts ramping in
float  _ClumpRampInv;
float  _ClumpBoost;      // extra detail amplitude, as a multiple, on worked/piled snow

// THE LUMP LATTICE, NOW BAKED. v4's ScreenSpaceLumps look - tall rounded overlapping lobes instead of
// angular faceted slabs - WITHOUT v4's separate screen-space depth/thickness/blur chain, whose
// measured cost had a ~6 ms FIXED full-screen floor that does not shrink with lump count.
//
// It is NOT a 3D SDF union of spheres. It is a hash-jittered lattice of spheres CONVERTED INTO A
// HEIGHT CONTRIBUTION: for each lattice cell near (x,z) the sphere's own cap height above its centre
// plane, sqrt(r^2 - d^2), maxed over the 3x3 neighbourhood and added to the base height. The surface
// stays a height function h(x,z), so the vertical `ray.y <= h(x,z)` hit test, the coarse-max skip and
// the three passes' SV_Depth writes are all untouched.
//
// WHAT CHANGED, AND WHY. That neighbourhood used to be evaluated HERE, inside the surface function,
// which meant nine hashes and a sqrt at every march step, every normal tap and every shadow tap -
// about 36 times per pixel. It is now evaluated ONCE PER TEXEL PER SIMULATION STEP by the LumpBake
// kernel, over the same dirty window relax uses, into a 6.25 cm single-channel 8-bit texture, and
// this file takes ONE bilinear tap of it. The formula, the gates and the encoding all live in
// SnowLumpLattice.hlsl, included above, so there is exactly one definition of the surface.
//
// The knobs that shape the lattice (spacing, jitter, radius variation, the depth gate and the relief
// term) are therefore NOT uniforms of this shader any more: they are uniforms of the BAKE, pushed
// onto the field's compute shader by SnowRaymarchRenderer.PushLumpBakeParams. What is left here is
// _LumpRadiusM - which the include declares, and which is still the decode scale AND the off switch -
// plus the distance fade, which stays per pixel because it is per RAY and cannot be baked.
float  _LumpFadeStartM;   // ray distance at which the lump term starts fading out
float  _LumpFadeInv;      // 1 / the range over which it finishes fading

// A SECOND, sharper detail term that lands ONLY in the shading normal. In v3 this is the crispness
// that stops a correct silhouette from being wrapped in styrofoam. In v6 it DEFAULTS TO ZERO,
// because crisp packed-snow grain is exactly the thing that reads as photographic - but it is a
// knob, not a deletion, so the parent agent can put v3's 8 mm back and see what casual gave up.
float  _NormalDetailAmpM;
float  _NormalDetailFreq;
float  _NormalDetailOctaves;

float  _NormalEpsM;
float  _NormalClampM;    // bound on how far a normal tap may differ from its centre

float  _AoStrength;
float  _AoScaleInv;      // 1 / the fine curvature that saturates the crease term
float  _AoWideEpsM;
float  _AoWideScaleInv;

float  _SoftShadowSteps;
float  _SoftShadowStepM;
float  _SoftShadowStartM;
float  _SoftShadowHardness;
float  _SoftShadowStrength;
float  _ShadowNormalBiasM;
float  _ShadowDepthBiasM;

float  _Wrap;            // diffuse wrap, for snow's forward scatter
float  _Fill;            // floor on the main light term so nothing goes black
float  _Sheen;           // v6 defaults this to 0: no wet look. Kept so v3 is reachable.
float  _SheenPower;
float  _WallTint;        // how far toward _DeepColor a vertical face goes
float  _WallTintInv;

// ------------------------------------------------------------------ field and detail

float SampleFieldH(float2 xz)
{
    float2 uv = (xz - _PatchMin.xy) * _InvPatchSize.xy;
    return _HeightTex.SampleLevel(sampler_linear_clamp, uv, 0).r;
}

// The dilated coarse max as a DEPTH above the ground, without the march bias. The bias belongs
// to the march's skip bound and nothing else may borrow it.
float SampleCoarseH(float2 xz)
{
    float2 uv = (xz - _PatchMin.xy) * _InvPatchSize.xy;
    return _CoarseMaxTex.SampleLevel(sampler_point_clamp, uv, 0).r;
}

// ONE tap, TWO radii. .x is the surface EXISTENCE test - never used for the surface height, so
// it cannot invent snow, only stop a one-texel bare patch surrounded by snow from being
// discarded as a hole. .y is the casual fillet's source at its own, finer radius.
float2 SampleDilated(float2 xz)
{
    float2 uv = (xz - _PatchMin.xy) * _InvPatchSize.xy;
    return _HeightDilateTex.SampleLevel(sampler_point_clamp, uv, 0).rg;
}

// THE WHOLE LUMP LATTICE, IN ONE BILINEAR TAP, decoded back to metres. This replaces nine hashes, a
// sqrt, a field-relief read and two saturates per evaluation - and there were ~36 evaluations per
// pixel, once per march step plus the normal and shadow taps.
//
// The UV is the same expression the other three taps use, because the bake covers exactly the same
// world patch at exactly twice the resolution, so nothing about the addressing has to know that.
float SampleLumpLift(float2 xz)
{
    float2 uv = (xz - _PatchMin.xy) * _InvPatchSize.xy;
    return SnowLumpDecodeM(_LumpBakeTex.SampleLevel(sampler_linear_clamp, uv, 0).r);
}

// Normal of the SMOOTH FIELD ONLY - no procedural detail, no fillet - taken over a wide finite
// difference. This is what the casual band ramp is quantised against.
//
// Quantising the shading normal instead is what produced CONTOUR TERRACING: that normal carries the
// ~38 cm procedural detail, so every bump got its own closed band contour and a smooth snow field
// came out looking like a topographic map. A cel ramp has to be quantised against the macro shape,
// and this is the macro shape.
float3 FieldMacroNormal(float2 xz, float e)
{
    float hx0 = SampleFieldH(xz - float2(e, 0.0));
    float hx1 = SampleFieldH(xz + float2(e, 0.0));
    float hz0 = SampleFieldH(xz - float2(0.0, e));
    float hz1 = SampleFieldH(xz + float2(0.0, e));
    return normalize(float3(hx0 - hx1, 2.0 * e, hz0 - hz1));
}

// Hard upper bound on the world Y of the surface anywhere within _CoarseSafeRadiusM of xz on both
// axes, given a coarse depth the caller has already sampled. Taking the sample as an argument lets
// the march loop use ONE tap for both the surface evaluation and the skip bound, which is why the
// fillet costs the march nothing.
//
// WHY THIS IS AN UPPER BOUND WITH THE BAKED LUMP LIFT IN. The surface is
//     y = _GroundY + hRound(h) + loadLift + detail + lumpLift
// and the bound is split in two: _CoarseMaxBiasM is a blanket headroom for the terms that are still
// evaluated per pixel, while the lump term is carried by the coarse TEXTURE itself.
//
//   detail   <= _DetailAmpM * (1 + _ClumpBoost)   (fbm is normalised to +-1, every gate is <= 1)
//   casual   <= the fillet's published bound plus the load lift's clamp
//   lumpLift <= already inside coarseH
//
// The lump term is NO LONGER in the bias, and that is the point of the bake. CoarseMaxBlock reduces
// each coarse cell to max(field height over the cell) + max(baked lift over the cell), so coarseH
// already carries the lift that is actually there instead of the blanket + _LumpRadiusM this used to
// add over the whole 120 x 110 m field. On flat virgin snow, where both gates are 0, the added term
// is now exactly 0 rather than 30 cm of headroom every ray had to descend through.
//
// The three properties that make it a true bound, in the order they can break:
//   1. Per cell the bake is reduced with a PLAIN max over the cell's own 2b x 2b bake texels plus a
//      one-texel ring, and the marcher's tap is BILINEAR - a convex combination of four texels whose
//      centres are within one bake texel of the sample point, hence all four inside that padded
//      block. A convex combination never exceeds the max of its inputs.
//   2. The marcher scales the decoded tap by lodFade.y, which is in 0..1, so the fade can only ever
//      LOWER the surface below the bound. It is never widened for the fade.
//   3. ORDER WITHIN THE FRAME CANNOT BREAK IT, which is what makes this safe rather than lucky. The
//      coarse max is rebuilt over the WHOLE field, from the current contents of both textures, at the
//      end of every Step and after every field reset - and always AFTER the bake. So it bounds
//      exactly the bytes the marcher will read this frame. If a field edit lands and the bake window
//      misses part of it, or the bake is a frame stale, or the bake is skipped entirely because the
//      radius is 0, the marcher and the coarse build still read the SAME baked texels, so the bound
//      holds; a stale bake is a lobe one frame late, never a ray that skips a surface.
float CoarseMaxYFrom(float coarseH)
{
    return _GroundY + coarseH + _CoarseMaxBiasM;
}

// Integer lattice hash. No sin(): the sin trick loses precision unevenly and this has to be
// bit-stable, because the march, the bisection and the normal taps must all agree on one surface.
float LatticeHash(float2 pi)
{
    uint2 u = (uint2)(int2(pi) + 4096);
    uint  h = u.x * 1597334677u ^ u.y * 3812015801u;
    h ^= h >> 15;
    h *= 2246822519u;
    h ^= h >> 13;
    return (float)(h & 0x00ffffffu) * (1.0 / 16777216.0);
}

float VNoise(float2 p)
{
    float2 i = floor(p);
    float2 f = p - i;
    f = f * f * (3.0 - 2.0 * f);

    float a = LatticeHash(i);
    float b = LatticeHash(i + float2(1.0, 0.0));
    float c = LatticeHash(i + float2(0.0, 1.0));
    float d = LatticeHash(i + float2(1.0, 1.0));

    return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y) * 2.0 - 1.0;
}

// Normalised to -1..1 whatever the octave count is, so _DetailAmpM stays a true amplitude in
// metres and changing the octave count does not silently change the surface height. The octave
// branches are uniform across the whole draw: a scalar compare, not divergence.
float DetailFbm(float2 p)
{
    float n   = VNoise(p);
    float tot = 1.0;

    if (_DetailOctaves > 1.5) { n += VNoise(p * 2.13 + 17.31) * 0.50; tot += 0.50; }
    if (_DetailOctaves > 2.5) { n += VNoise(p * 4.41 + 41.77) * 0.25; tot += 0.25; }
    if (_DetailOctaves > 3.5) { n += VNoise(p * 8.77 + 93.13) * 0.125; tot += 0.125; }

    return n / tot;
}

float NormalDetailFbm(float2 xz)
{
    float2 p   = xz * _NormalDetailFreq;
    float  n   = VNoise(p);
    float  tot = 1.0;

    if (_NormalDetailOctaves > 1.5) { n += VNoise(p * 2.17 + 61.71) * 0.50; tot += 0.50; }
    if (_NormalDetailOctaves > 2.5) { n += VNoise(p * 4.63 + 131.3) * 0.25; tot += 0.25; }

    return n / tot;
}

// THE surface. Everything that needs to know where the snow is calls this or the wrapper below,
// and only those.
//
// The caller supplies the DILATE tap, which carries both the existence test (.x) and the casual
// fillet's source (.y). That is deliberately the tap the march loop already had to take, which
// is why giving the fillet its own radius on a 120 m field costs the march nothing.
//
// lodFade is a float2 and is constant for a whole fragment (it is derived from the ray's entry
// distance, not from the current step), which is what keeps this single valued during one pixel's
// march. A fade that varied along the ray would make the surface move as the march approached it,
// and the bisection would converge on a point that is not on any surface.
//   .x = the procedural DETAIL fade
//   .y = the LUMP LATTICE fade
// Two components rather than two arguments so every one of the ~20 call sites that only threads the
// value through did not have to grow a parameter, and so the two can never be passed in swapped.
//
// Single exit: the "no surface" sentinel is selected with a ternary at the end rather than
// returned early from inside the if.
float SurfaceYFromDil(float2 xz, float2 lodFade, float2 dil)
{
    float h = SampleFieldH(xz);

    float thin  = saturate((h - _MinSnowHeight) * _DetailThinInv);
    float clump = 1.0 + _ClumpBoost * saturate((h - _ClumpStartM) * _ClumpRampInv);

    // CASUAL, and bounded. The fillet soft-maxes the field against the FILLET dilation (dil.y),
    // not against the coarse march bound the way v4 did - see the texture's comment. Its lift
    // comes back so it is obvious at the call site that the amount is known; the C# side adds
    // the same bound to _CoarseMaxBiasM.
    float liftBound;
    float hRound = SnowCasualRoundH(h, dil.y, thin, liftBound);

    // Presentation exaggeration of the LOAD, neutral at 1.0, hard clamped, and gated on the same
    // thin fade so the cleared lane is never raised.
    float hDisp = hRound + SnowCasualLoadLift(h) * thin;

    // THE LUMP LATTICE, ONE TAP. Everything that used to be computed here - the nine hashes, the 3x3
    // cap max, the depth gate that stops a lobe floating over bare ground and the relief term that
    // keeps flat virgin snow smooth - is BAKED into _LumpBakeTex by the LumpBake kernel at 6.25 cm,
    // over the same dirty window relax uses. See SnowLumpLattice.hlsl for the formula and the gates.
    //
    // WHAT IS STILL PER PIXEL, DELIBERATELY: the distance fade. It is a function of the RAY's entry
    // range, not of position, so it cannot be baked into a world-space texture at all - two rays
    // crossing the same texel at different ranges want different amounts. Keeping it as a multiply on
    // the decoded tap costs one instruction, keeps _lumpFadeStartM / _lumpFadeRangeM live knobs, and
    // preserves the old behaviour exactly: fade only ever REDUCES the lift, so the march's bound is
    // untouched by it.
    //
    // The whole term is skipped when the radius is 0 (a uniform, so the compare is scalar across the
    // draw and "off" really is a zero-cost path - no tap issued, not a tap multiplied by zero) or when
    // the lump fade has run out, which is per pixel but constant along that pixel's march, so the far
    // field still costs nothing extra.
    float lumpLift = 0.0;
    if (_LumpRadiusM > 1e-5 && lodFade.y > 1e-4)
    {
        lumpLift = SampleLumpLift(xz) * lodFade.y;
    }

    float y = _GroundY + hDisp
            + DetailFbm(xz * _DetailFreq) * (_DetailAmpM * thin * clump * lodFade.x)
            + lumpLift;

    // Bare ground is the ABSENCE of a surface, not a surface at height zero. A value far below the
    // box floor makes the march miss and the fragment discard, so the ground plane is what gets
    // seen - and no accepted hit is ever within _MinSnowHeight of the ground plane, which is what
    // removes the z-fight in the swept lane.
    //
    // THE EXISTENCE TEST USES THE DILATED FIELD; THE HEIGHT ABOVE USES THE REAL ONE. That split is
    // the fix for perforation. The cut noise strands scattered individual texels in the 0..5 mm
    // band, and a one-texel patch of legitimately bare ground surrounded on every side by snow does
    // not read as bare ground - it reads as a hole punched through the snow with hard texel-aligned
    // edges. Testing the small max filter covers those, because it has snow to pull from; the open
    // swept lane still reads as open, because out there the dilation has nothing to pull from and
    // stays under the threshold. Nothing is invented either way: where the dilated test passes but
    // h is tiny, the surface sits at the true near-zero height and is simply not discarded.
    //
    // _HeightDilateRadius 0 makes the dilated red channel equal to the field and restores the
    // perforated behaviour exactly, which is the A/B.
    return (dil.x < _MinSnowHeight) ? (_GroundY - 1000.0) : y;
}

// One-tap wrapper, for the callers that do not already have the dilate sample: the bisection, the
// normal taps and the self-shadow march.
float SurfaceY(float2 xz, float2 lodFade)
{
    return SurfaceYFromDil(xz, lodFade, SampleDilated(xz));
}

// ------------------------------------------------------------------ ray setup

// Slab test. The reciprocal is built with an explicit sign so a zero direction component becomes a
// huge finite number instead of an inf that could meet a zero numerator and produce a NaN - which
// on a ray exactly grazing a face is not a hypothetical.
bool IntersectBox(float3 ro, float3 rd, float3 bmin, float3 bmax, out float t0, out float t1)
{
    // Written out as full vectors rather than relying on scalar promotion inside a
    // vector-conditional, which not every HLSL front end accepts.
    float3 s   = (rd >= 0.0) ? float3(1.0, 1.0, 1.0) : float3(-1.0, -1.0, -1.0);
    float3 inv = s / max(abs(rd), 1e-9);

    float3 a  = (bmin - ro) * inv;
    float3 b  = (bmax - ro) * inv;
    float3 lo = min(a, b);
    float3 hi = max(a, b);

    t0 = max(max(lo.x, lo.y), lo.z);
    t1 = min(min(hi.x, hi.y), hi.z);
    return t1 > t0;
}


// ------------------------------------------------------------------ the march

// Returns false when the ray leaves the volume without ever getting below the surface, which is
// the only way this shader produces a pixel: a miss discards, so the proxy box is never visible.
//
// v6 ADDS A STEP COUNTER. `steps` comes back as the number of march iterations actually executed
// and `exhausted` as 1 when the loop ran out of budget rather than hitting or leaving the volume.
// At 8 m the skip radius was 25 cm and the box was 8 m across; here the box is 120 m, so the
// steps-per-pixel figure is the number that decides whether the flat coarse-max skip is still
// adequate or whether it has to become a MIP pyramid. Measuring it is cheaper than guessing.
//
// BOTH out parameters are written on every path, before the single return. That is not style: it
// is the exact condition Metal checks before it will accept a non-void function, and adding an
// out parameter to a function that already had one is precisely where that gets broken.
bool MarchSnow(float3 ro, float3 rd, float tStart, float tEnd, float2 lodFade,
               out float tHit, out int steps, out float exhausted)
{
    int   maxSteps = max(1, (int)_MaxSteps);

    float invDescent = 1.0 / max(-rd.y, 1e-3);

    float t     = tStart;
    float tPrev = tStart;
    bool  hit   = false;
    int   used  = 0;
    bool  ranOut = true;

    [loop]
    for (int i = 0; i < maxSteps; ++i)
    {
        used = i + 1;

        float3 p = ro + rd * t;

        // TWO taps, and both were already here in v4: the DILATE tap carries the existence test
        // and the casual fillet's source, the COARSE tap is the skip bound. v4 took the same two;
        // all that changed is which of them the fillet reads.
        float2 dil     = SampleDilated(p.xz);
        float  coarseH = SampleCoarseH(p.xz);

        if (p.y <= SurfaceYFromDil(p.xz, lodFade, dil)) { hit = true; ranOut = false; break; }
        if (t >= tEnd) { ranOut = false; break; }

        tPrev = t;

        float fine = _StepM * max(1.0, t * _LodDistanceInv);
        float safe = min(_CoarseSafeRadiusM, (p.y - CoarseMaxYFrom(coarseH)) * invDescent);

        t = min(tEnd, t + max(fine, safe));
    }

    // Bisect between the last known-outside sample and the first known-inside one. This is what
    // turns a stepped terrace into a crisp surface: at a 2 cm step and 4 refinements the hit is
    // located to 1.25 mm, a twelfth of a texel, far more cheaply than a smaller step would.
    //
    // Guarded by `hit` and folded into a single exit rather than returning early on the miss path,
    // which is this variant's convention.
    //
    // Being accurate about why, since v3's MarchSnow returns early here and compiles: that is not
    // luck, it is that the constraint does not bind there. Metal rejects an early return from a
    // non-void function only when it cannot prove every OUT parameter was written on that path, and
    // v3 assigns tHit before each of its returns. The single-exit form is followed anyway because it
    // makes the property structural instead of something a future edit has to re-establish - move
    // one assignment below a return and the proof disappears, silently on DXC. See the METAL note
    // in SnowCasualStyle.hlsl.
    float lo = tPrev;
    float hi = t;

    if (hit)
    {
        int refine = max(0, (int)_RefineSteps);
        [loop]
        for (int r = 0; r < refine; ++r)
        {
            float  mid = 0.5 * (lo + hi);
            float3 pm  = ro + rd * mid;
            if (pm.y <= SurfaceY(pm.xz, lodFade)) hi = mid; else lo = mid;
        }
    }

    // On a hit: the INSIDE end, deliberately, because landing a hair inside the surface means a
    // silhouette never shows a sliver of whatever is behind it. On a miss: the box exit, which is
    // what the caller uses to place its (discarded) fragment.
    tHit      = hit ? hi : tEnd;
    steps     = used;
    exhausted = (ranOut && !hit) ? 1.0 : 0.0;
    return hit;
}

// BOTH distance fades, from one call, at the ray's ENTRY distance.
//   .x = the procedural detail fade      (_DetailFadeStartM / _DetailFadeInv)
//   .y = the lump lattice fade           (_LumpFadeStartM  / _LumpFadeInv)
// The lump fade is what makes the far field cost nothing extra: past the end of its range the
// surface function's lump branch is not taken at all. It reuses the marcher's existing LOD distance
// CONCEPT - a per-ray function of entry range, exactly like the detail fade and like the step growth
// driven by _LodDistanceInv - rather than introducing a second notion of distance.
float2 MarchLodFades(float t)
{
    return saturate(float2(1.0 - (t - _DetailFadeStartM) * _DetailFadeInv,
                           1.0 - (t - _LumpFadeStartM)  * _LumpFadeInv));
}



#endif // SNOW_MARCH_CORE_V7_INCLUDED
